using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using System.IO;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class HardwareReceiverLifecyclePlayTests
{
    private GameObject root;
    private MonoBehaviour hub;
    private string previousPort;
    private int port;
    private string pairingPath;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name)).First(t => t != null);
    private bool Connected => (bool)hub.GetType().GetMethod("IsConnected").Invoke(hub,
        new[] { Enum.ToObject(RuntimeType("ControllerSlot"), 0) });
    private bool Listening => (bool)hub.GetType().GetField("udpListening", Fields).GetValue(hub);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        foreach (MonoBehaviour existing in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (existing.GetType().Name == "HardwareControllerHub") UnityEngine.Object.Destroy(existing.gameObject);
        yield return null;
        using (var reserve = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            port = ((IPEndPoint)reserve.Client.LocalEndPoint).Port;
        previousPort = Environment.GetEnvironmentVariable("KOSEN_CONTROLLER_UDP_PORT");
        Environment.SetEnvironmentVariable("KOSEN_CONTROLLER_UDP_PORT", port.ToString());
        root = new GameObject("Controller receiver lifecycle test");
        root.SetActive(false);
        hub = (MonoBehaviour)root.AddComponent(RuntimeType("HardwareControllerHub"));
        pairingPath = Path.Combine(Path.GetTempPath(), "sf-pairing-" + Guid.NewGuid() + ".json");
        hub.GetType().GetField("pairingPath", Fields).SetValue(hub, pairingPath);
        root.SetActive(true);
        ((ControllerPairingRegistry)hub.GetType().GetField("pairing", Fields).GetValue(hub)).Load(0, 1, 2);
        yield return WaitUntil(() => Listening);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        Environment.SetEnvironmentVariable("KOSEN_CONTROLLER_UDP_PORT", previousPort);
        yield return null;
        if (pairingPath != null && File.Exists(pairingPath)) File.Delete(pairingPath);
    }

    [UnityTest]
    public IEnumerator DisableClearsStaleInputAndEnableResumesRealUdpReceiving()
    {
        using (var sender = new UdpClient())
        {
            yield return SendUntilConnected(sender, 1);
            hub.enabled = false;
            Assert.That(Connected, Is.False, "Stopped transport must not keep a held input active.");
            Assert.That(Listening, Is.False);
            hub.enabled = true;
            yield return WaitUntil(() => Listening);
            Assert.That(Connected, Is.False, "Re-enabling must wait for a new packet.");
            yield return SendUntilConnected(sender, 100);
            Assert.That(hub.GetType().GetProperty("PortName").GetValue(hub).ToString(), Does.StartWith("UDP "));
        }
    }

    [UnityTest]
    public IEnumerator OnlyAcceptedControllerFramesReceiveGameConnectionFeedback()
    {
        using (var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            byte[] corrupt = Packet(1);
            corrupt[20] ^= 1;
            sender.Send(corrupt, corrupt.Length, new IPEndPoint(IPAddress.Loopback, port));
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(sender.Available, Is.Zero, "Invalid CRC must not light a controller's connected LED.");
            yield return SendUntilConnected(sender, 2);
            yield return WaitUntil(() => sender.Available > 0);
            IPEndPoint endpoint = null;
            byte[] envelope = sender.Receive(ref endpoint);
            Assert.That(ControllerPairingWire.Unwrap(envelope, out ulong device, out ulong session, out byte[] ack), Is.True);
            Assert.That(device, Is.EqualTo(1UL)); Assert.That(session, Is.EqualTo(2UL));
            Assert.That(ack.Length, Is.EqualTo(16));
            Assert.That(ack.Take(8), Is.EqualTo(new byte[] { 0x4b, 0x41, 1, 16, 0, 0, 0, 0 }));
            Assert.That(BitConverter.ToUInt32(ack, 8), Is.GreaterThanOrEqualTo(2u));
            uint crc = (uint)RuntimeType("HardwareMotionPacketCodec").GetMethod("Crc32")
                .Invoke(null, new object[] { ack, 12 });
            Assert.That(BitConverter.ToUInt32(ack, 12), Is.EqualTo(crc));
            hub.enabled = false;
            while (sender.Available > 0) sender.Receive(ref endpoint);
            byte[] next = Packet(100);
            sender.Send(next, next.Length, new IPEndPoint(IPAddress.Loopback, port));
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(sender.Available, Is.Zero, "Feedback stops with the game receiver.");
        }
    }

    private IEnumerator SendUntilConnected(UdpClient sender, uint sequence)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 2;
        do
        {
            byte[] bytes = Packet(sequence++);
            sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, port));
            yield return null;
            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Receiver did not accept a fresh UDP packet.");
        } while (!Connected);
    }

    private static byte[] Packet(uint sequence)
    {
        byte[] bytes = new byte[64];
        bytes[0] = 0x4b; bytes[1] = 0x4a; bytes[2] = 1; bytes[3] = 64;
        Array.Copy(BitConverter.GetBytes(sequence), 0, bytes, 8, 4);
        Array.Copy(BitConverter.GetBytes(sequence * 10), 0, bytes, 12, 4);
        Array.Copy(BitConverter.GetBytes(1f), 0, bytes, 32, 4);
        uint crc = (uint)RuntimeType("HardwareMotionPacketCodec").GetMethod("Crc32")
            .Invoke(null, new object[] { bytes, 60 });
        Array.Copy(BitConverter.GetBytes(crc), 0, bytes, 60, 4);
        return ControllerPairingWire.Wrap(bytes, 1, 2);
    }

    private static IEnumerator WaitUntil(Func<bool> condition)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 2;
        while (!condition())
        {
            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Receiver did not bind its UDP socket.");
            yield return null;
        }
    }
}
