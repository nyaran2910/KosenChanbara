using SchoolFestival.Combat;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

public sealed class HardwareUdpInputTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static readonly IPEndPoint First = new IPEndPoint(IPAddress.Parse("192.168.1.21"), 42042);
    private static readonly IPEndPoint Second = new IPEndPoint(IPAddress.Parse("192.168.1.22"), 42042);

    [Test]
    public void DatagramFragmentsOversizedPacketsAndConcatenatedPacketsAreNeverJoined()
    {
        object input = Input();
        byte[] packet = Packet(1, 10);
        Assert.That(Accept(input, packet.Take(30).ToArray(), First, 1), Is.False);
        Assert.That(Accept(input, packet.Skip(30).ToArray(), First, 1.01), Is.False);
        Assert.That(Accept(input, packet.Concat(packet).ToArray(), First, 1.02), Is.False);
        Assert.That(Accept(input, packet.Concat(new byte[] { 0 }).ToArray(), First, 1.03), Is.False);
        Assert.That(Accept(input, packet, First, 1.04), Is.True);
    }

    [Test]
    public void InvalidCrcGroupFlagsAndSlotCannotClaimOwnership()
    {
        object input = Input();
        foreach (int offset in new[] { 0, 2, 3, 4, 5, 6, 7, 18, 60 })
        {
            byte[] broken = Packet(1, 10);
            broken[offset] ^= 0x80;
            if (offset != 60) Seal(broken);
            Assert.That(Accept(input, broken, First, 1), Is.False, "offset " + offset);
        }
        Assert.That(Accept(input, Packet(1, 10, group: 1), First, 1), Is.False);
        Assert.That(Accept(input, Packet(1, 10), Second, 1.01), Is.True);
        object groupOne = Input(1);
        Assert.That(Accept(groupOne, Packet(1, 10, group: 1), First, 1), Is.True);
    }

    [Test]
    public void FreshOwnerBlocksOtherIpOrPortButSlotsRemainIndependent()
    {
        object input = Input();
        Assert.That(Accept(input, Packet(100, 1000), First, 1), Is.True);
        Assert.That(Accept(input, Packet(1, 10), Second, 1.20), Is.False);
        IPEndPoint otherPort = new IPEndPoint(First.Address, First.Port + 1);
        Assert.That(Accept(input, Packet(1, 10), otherPort, 1.20), Is.False);
        Assert.That(Accept(input, Packet(1, 10, slot: 1), Second, 1.20), Is.True);
        Assert.That(Accept(input, Packet(1, 10), Second, 1.251, out object sample, out bool restarted), Is.True);
        Assert.That(restarted, Is.True, "A new owner needs a new orientation baseline.");
        Assert.That(Get<uint>(sample, "Sequence"), Is.EqualTo(1));
        Assert.That(Accept(input, Packet(101, 1010), First, 1.30), Is.False);
    }

    [Test]
    public void DuplicatesReorderingAndInvalidClockDoNotRefreshOwnership()
    {
        object input = Input();
        Assert.That(Accept(input, Packet(10, 100), First, 1), Is.True);
        Assert.That(Accept(input, Packet(10, 100), First, 1.1), Is.False);
        Assert.That(Accept(input, Packet(9, 90), First, 1.2), Is.False);
        Assert.That(Accept(input, Packet(11, 99), First, 1.21), Is.False);
        Assert.That(Accept(input, Packet(11, 10101), First, 1.22), Is.False);
        Assert.That(Accept(input, Packet(1, 10), Second, 1.251), Is.True);
    }

    [Test]
    public void SequenceAndClockWrapResumeAcrossSilenceAndRebootOnRollback()
    {
        object input = Input();
        Assert.That(Accept(input, Packet(uint.MaxValue, uint.MaxValue - 9), First, 1, out object first, out bool firstReset), Is.True);
        Assert.That(firstReset, Is.True);
        Assert.That(Accept(input, Packet(0, 10), First, 1.02, out object wrapped, out bool reset), Is.True);
        Assert.That(reset, Is.False);
        Assert.That(Get<double>(wrapped, "BrowserTimestampMilliseconds") - Get<double>(first, "BrowserTimestampMilliseconds"), Is.EqualTo(20));
        Assert.That(Accept(input, Packet(0, 1), First, 1.30), Is.False);
        Assert.That(Accept(input, Packet(1, 1000), First, 1.521, out object resumed, out reset), Is.True);
        Assert.That(reset, Is.False, "A forward sequence and clock resume the existing epoch after silence.");
        Assert.That(Get<uint>(resumed, "Sequence"), Is.EqualTo(1));
        Assert.That(Accept(input, Packet(0, 1), First, 2.10, out object rebooted, out reset), Is.True);
        Assert.That(reset, Is.True);
        Assert.That(Get<double>(rebooted, "BrowserTimestampMilliseconds"), Is.EqualTo(1));
        Assert.That(Accept(input, Packet(1, 0), First, 2.8, out object clockReset, out reset), Is.True);
        Assert.That(reset, Is.True, "A forward sequence cannot hide a rolled-back clock after silence.");
        Assert.That(Get<double>(clockReset, "BrowserTimestampMilliseconds"), Is.EqualTo(0));
    }

    [Test]
    public void SensorlessHeartbeatStillOwnsSlotAndPreservesButtons()
    {
        object input = Input();
        Assert.That(Accept(input, Packet(1, 10, flags: 0), First, 1, out object sample, out _), Is.True);
        Assert.That(Get<bool>(sample, "OrientationValid"), Is.False);
        Assert.That(Accept(input, Packet(1, 10), Second, 1.1), Is.False);
        Assert.That(Accept(input, Packet(2, 20, flags: 17), First, 1.1, out sample, out _), Is.True);
        Assert.That(Get<bool>(sample, "GuardHeld"), Is.True);
        Assert.That(Get<bool>(sample, "RecenterHeld"), Is.True);
        Assert.That(Get<bool>(sample, "OrientationValid"), Is.False);
    }

    [Test]
    public void FreshUdpWinsOverDuplicatedUsbAndUsbDisconnectDoesNotClearWireless()
    {
        object buffer = Activator.CreateInstance(Runtime("HardwareTransportSamples"));
        Submit(buffer, "SubmitUsb", Packet(1, 10), 1, true, "COM3");
        Assert.That(Select(buffer, 1, out _, out bool reset, out string port), Is.True);
        Assert.That(reset, Is.True); Assert.That(port, Is.EqualTo("COM3"));
        Submit(buffer, "SubmitUdp", Packet(1, 10), 1.01, true, "UDP first");
        Assert.That(Select(buffer, 1.01, out _, out reset, out port), Is.True);
        Assert.That(reset, Is.False, "The same controller sequence and recenter epoch survives a transport handoff.");
        Assert.That(port, Is.EqualTo("UDP first"));
        for (uint seq = 2; seq <= 5; seq++)
        {
            double time = 1 + seq * .01;
            Submit(buffer, "SubmitUsb", Packet(seq, seq * 10), time, false, "COM3");
            Submit(buffer, "SubmitUdp", Packet(seq, seq * 10), time, false, "UDP first");
            Assert.That(Select(buffer, time, out _, out reset, out port), Is.True);
            Assert.That(reset, Is.False); Assert.That(port, Is.EqualTo("UDP first"));
        }
        buffer.GetType().GetMethod("ClearUsb").Invoke(buffer, null);
        Assert.That(Select(buffer, 1.1, out _, out reset, out port), Is.True);
        Assert.That(reset, Is.False); Assert.That(port, Is.EqualTo("UDP first"));
        Assert.That(Select(buffer, 1.301, out _, out _, out _), Is.False);
    }

    [Test]
    public void StaleUdpFallsBackToUsbOnceAndWirelessRecoveryOrRebootResetsOnce()
    {
        object buffer = Activator.CreateInstance(Runtime("HardwareTransportSamples"));
        Submit(buffer, "SubmitUdp", Packet(10, 100), 1, true, "UDP first");
        Assert.That(Select(buffer, 1, out _, out _, out _), Is.True);
        Submit(buffer, "SubmitUsb", Packet(11, 110), 1.26, true, "COM3");
        Assert.That(Select(buffer, 1.26, out _, out bool reset, out string port), Is.True);
        Assert.That(reset, Is.False, "A newer same-epoch USB frame keeps the current pose reference.");
        Assert.That(port, Is.EqualTo("COM3"));
        Assert.That(Select(buffer, 1.27, out _, out reset, out _), Is.True);
        Assert.That(reset, Is.False);
        Submit(buffer, "SubmitUdp", Packet(12, 120), 1.28, false, "UDP first");
        Assert.That(Select(buffer, 1.28, out _, out reset, out port), Is.True);
        Assert.That(reset, Is.False); Assert.That(port, Is.EqualTo("UDP first"));
        Submit(buffer, "SubmitUdp", Packet(0, 1), 2, true, "UDP first");
        Assert.That(Select(buffer, 2, out object sample, out reset, out _), Is.True);
        Assert.That(reset, Is.True); Assert.That(Get<uint>(sample, "Sequence"), Is.Zero);
        Assert.That(Select(buffer, 2.01, out _, out reset, out _), Is.True);
        Assert.That(reset, Is.False);
        buffer.GetType().GetMethod("Clear").Invoke(buffer, null);
        Assert.That(Select(buffer, 2.02, out _, out _, out _), Is.False);
    }

    [Test]
    public void FreshestTransportWinsAndOlderCrossTransportSequenceCannotReplaceIt()
    {
        object buffer = Activator.CreateInstance(Runtime("HardwareTransportSamples"));
        Submit(buffer, "SubmitUsb", Packet(20, 200), 1, true, "COM3");
        Assert.That(Select(buffer, 1, out _, out _, out _), Is.True);
        Submit(buffer, "SubmitUdp", Packet(19, 190), 1.01, true, "UDP old");
        Assert.That(Select(buffer, 1.01, out object selected, out bool reset, out string port), Is.True);
        Assert.That(port, Is.EqualTo("COM3"));
        Assert.That(Get<uint>(selected, "Sequence"), Is.EqualTo(20));
        Assert.That(reset, Is.False);
        Submit(buffer, "SubmitUdp", Packet(21, 210), 1.02, false, "UDP new");
        Assert.That(Select(buffer, 1.02, out selected, out reset, out port), Is.True);
        Assert.That(port, Is.EqualTo("UDP new"));
        Assert.That(Get<uint>(selected, "Sequence"), Is.EqualTo(21));
        Assert.That(reset, Is.False);
        Submit(buffer, "SubmitUdp", Packet(22, 1400), 2.2, false, "UDP new");
        Assert.That(Select(buffer, 2.2, out selected, out reset, out _), Is.True);
        Assert.That(Get<uint>(selected, "Sequence"), Is.EqualTo(22));
        Assert.That(reset, Is.False, "A forward same-source sample after a long gap keeps the pose reference.");
    }

    [Test]
    public void QueuedHeartbeatCannotOverwritePendingHaptic()
    {
        GameObject host = new GameObject("Feedback coalescing test");
        host.SetActive(false);
        try
        {
            Type hubType = Runtime("HardwareControllerHub");
            MonoBehaviour hub = (MonoBehaviour)host.AddComponent(hubType);
            var pairing = new ControllerPairingRegistry(33, 0); pairing.Load(0, 11, 44);
            hubType.GetField("pairing", Private).SetValue(hub, pairing);
            object slot = Enum.ToObject(Runtime("ControllerSlot"), 0);
            System.Reflection.MethodInfo encode = Runtime("HardwareGameAcknowledgement").GetMethod("Encode");
            byte[] haptic = (byte[])encode.Invoke(null, new object[] { (byte)0, slot, (uint)10, (ushort)250 });
            byte[] newerHaptic = (byte[])encode.Invoke(null, new object[] { (byte)0, slot, (uint)11, (ushort)400 });
            byte[] heartbeat = (byte[])encode.Invoke(null, new object[] { (byte)0, slot, (uint)12, (ushort)0 });
            System.Reflection.MethodInfo queue = hubType.GetMethod("QueueFeedback", Private);
            queue.Invoke(hub, new object[] { slot, haptic, null, null, null });
            queue.Invoke(hub, new object[] { slot, heartbeat, null, null, null });
            object[] pending = (object[])hubType.GetField("pendingFeedback", Private).GetValue(hub);
            byte[] queued = (byte[])pending[0].GetType().GetField("Bytes").GetValue(pending[0]);
            Assert.That(ControllerPairingWire.Unwrap(queued, out _, out _, out byte[] first), Is.True);
            Assert.That((first[6] | (first[7] << 8)), Is.EqualTo(250));

            queue.Invoke(hub, new object[] { slot, newerHaptic, null, null, null });
            pending = (object[])hubType.GetField("pendingFeedback", Private).GetValue(hub);
            queued = (byte[])pending[0].GetType().GetField("Bytes").GetValue(pending[0]);
            Assert.That(ControllerPairingWire.Unwrap(queued, out _, out _, out byte[] second), Is.True);
            Assert.That((second[6] | (second[7] << 8)), Is.EqualTo(400));
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    [Test]
    public void LoopbackDatagramReachesHubAndReconnectAndDestroyReleaseSocketAndThread()
    {
        GameObject host = null;
        Thread thread = null;
        try
        {
            MonoBehaviour hub = StartUdpHub(0, out host, out thread);
            Assert.That(WaitFor(() => Field<Socket>(hub, "udpSocket") != null), Is.True, "UDP socket must bind.");
            Socket original = Field<Socket>(hub, "udpSocket");
            Assert.That(((IPEndPoint)original.LocalEndPoint).Address, Is.EqualTo(IPAddress.Any));
            int port = ((IPEndPoint)original.LocalEndPoint).Port;
            using (UdpClient sender = new UdpClient(AddressFamily.InterNetwork))
            {
                byte[] packet = ControllerPairingWire.Wrap(Packet(1, 10, flags: 0), 11, 44);
                byte[] wrongIdentity = ControllerPairingWire.Wrap(Packet(1, 10, flags: 0), 12, 44);
                sender.Send(wrongIdentity, wrongIdentity.Length, new IPEndPoint(IPAddress.Loopback, port));
                Thread.Sleep(50); Assert.That(HubHasSample(hub), Is.False);
                sender.Send(packet, 30, new IPEndPoint(IPAddress.Loopback, port));
                byte[] tail = packet.Skip(30).ToArray();
                sender.Send(tail, tail.Length, new IPEndPoint(IPAddress.Loopback, port));
                sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));
                Assert.That(WaitFor(() => HubHasSample(hub)), Is.True, "A complete datagram must reach the main-thread API.");
                Tick(hub);
                Assert.That((string)hub.GetType().GetProperty("Status").GetValue(hub), Does.StartWith("無線リモコンがつながりました"));
                Assert.That((string)hub.GetType().GetProperty("PortName").GetValue(hub), Does.StartWith("UDP 127.0.0.1:"));
                Assert.That(Field<Thread>(hub, "receiver"), Is.Null, "The isolated test must not open USB.");

                hub.GetType().GetMethod("Reconnect").Invoke(hub, null);
                Assert.That(WaitFor(() => Field<Socket>(hub, "udpSocket") != null && Field<Socket>(hub, "udpSocket") != original), Is.True);
                Assert.Throws<ObjectDisposedException>(() => { int ignored = original.Available; });
                Tick(hub);
                Assert.That(Field<bool[]>(hub, "valid")[0], Is.False, "Reconnect clears previous controller values.");
                Socket replacement = Field<Socket>(hub, "udpSocket");
                StopUdpHub(host); host = null;
                Assert.That(thread.IsAlive, Is.False);
                Assert.Throws<ObjectDisposedException>(() => { int ignored = replacement.Available; });
            }
        }
        finally { if (host != null) StopUdpHub(host); }
    }

    [Test]
    public void RegistrationHandshakeAndDiscoveryWorkOverRealUdpAndSaveOnlyConfirmedIdentity()
    {
        GameObject host = null;
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sf-pairing-" + Guid.NewGuid().ToString("N"));
        try
        {
            MonoBehaviour hub = StartUdpHub(0, out host, out _);
            hub.GetType().GetField("pairingPath", Private).SetValue(hub, System.IO.Path.Combine(folder, "pairing.json"));
            var registry = Field<ControllerPairingRegistry>(hub, "pairing"); registry.Begin(1);
            Assert.That(WaitFor(() => Field<Socket>(hub, "udpSocket") != null), Is.True);
            var destination = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)Field<Socket>(hub, "udpSocket").LocalEndPoint).Port);
            using (var sender = new UdpClient(AddressFamily.InterNetwork))
            {
                sender.Client.ReceiveTimeout = 2000;
                IPEndPoint source = new IPEndPoint(IPAddress.Any, 0);
                var request = new ControllerPairingWire.Frame { Type = ControllerPairingWire.Kind.Request, Device = 55, Request = 66 };
                byte[] data = ControllerPairingWire.Encode(request); sender.Send(data, data.Length, destination);
                Assert.That(ControllerPairingWire.TryDecode(sender.Receive(ref source), out var offer), Is.True);
                Assert.That(offer.Type, Is.EqualTo(ControllerPairingWire.Kind.Offer)); Assert.That(offer.Slot, Is.EqualTo(1));
                offer.Type = ControllerPairingWire.Kind.Commit; data = ControllerPairingWire.Encode(offer);
                sender.Send(data, data.Length, destination);
                Assert.That(ControllerPairingWire.TryDecode(sender.Receive(ref source), out var confirm), Is.True);
                Assert.That(confirm.Type, Is.EqualTo(ControllerPairingWire.Kind.Confirm));
                Assert.That(registry.Revision, Is.Zero, "Commit alone does not finish the game's registration.");
                data = ControllerPairingWire.Wrap(Packet(1, 10, slot: 1), 55, confirm.Session); sender.Send(data, data.Length, destination);
                Assert.That(WaitFor(() => { Tick(hub); return Field<bool[]>(hub, "valid")[1]; }), Is.True);
                Assert.That(registry.MatchesBinding(55, confirm.Session, 1), Is.True);
                Assert.That(registry.MatchesBinding(11, 44, 0), Is.True, "The other player's registration survives.");
                Assert.That(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "pairing.json")), Does.Contain("0000000000000037"));
                confirm.Type = ControllerPairingWire.Kind.Discover; confirm.Request = 77; data = ControllerPairingWire.Encode(confirm);
                sender.Send(data, data.Length, destination);
                Assert.That(ControllerPairingWire.TryDecode(sender.Receive(ref source), out var found), Is.True);
                Assert.That(found.Type, Is.EqualTo(ControllerPairingWire.Kind.Found)); Assert.That(found.Request, Is.EqualTo(77));
            }
        }
        finally
        {
            if (host != null) StopUdpHub(host);
            if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
        }
    }

    [Test]
    public void ExplicitReenrollmentCanReplaceAFreshUdpOwnerImmediately()
    {
        GameObject host = null;
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sf-repair-" + Guid.NewGuid().ToString("N"));
        try
        {
            MonoBehaviour hub = StartUdpHub(0, out host, out _);
            hub.GetType().GetField("pairingPath", Private).SetValue(hub, System.IO.Path.Combine(folder, "pairing.json"));
            Assert.That(WaitFor(() => Field<Socket>(hub, "udpSocket") != null), Is.True);
            var destination = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)Field<Socket>(hub, "udpSocket").LocalEndPoint).Port);
            using (var oldSender = new UdpClient(AddressFamily.InterNetwork))
            using (var newSender = new UdpClient(AddressFamily.InterNetwork))
            {
                newSender.Client.ReceiveTimeout = 2000;
                byte[] original = ControllerPairingWire.Wrap(Packet(100, 1000), 11, 44); oldSender.Send(original, original.Length, destination);
                Assert.That(WaitFor(() => HubHasSample(hub)), Is.True);
                Field<ControllerPairingRegistry>(hub, "pairing").Begin(0);
                var request = new ControllerPairingWire.Frame { Type = ControllerPairingWire.Kind.Request, Device = 55, Request = 66 };
                byte[] data = ControllerPairingWire.Encode(request); newSender.Send(data, data.Length, destination);
                IPEndPoint source = new IPEndPoint(IPAddress.Any, 0);
                Assert.That(ControllerPairingWire.TryDecode(newSender.Receive(ref source), out var offer), Is.True);
                offer.Type = ControllerPairingWire.Kind.Commit; data = ControllerPairingWire.Encode(offer); newSender.Send(data, data.Length, destination);
                Assert.That(ControllerPairingWire.TryDecode(newSender.Receive(ref source), out var confirm), Is.True);
                data = ControllerPairingWire.Wrap(Packet(1, 10), 55, confirm.Session); newSender.Send(data, data.Length, destination);
                Assert.That(WaitFor(() => { Tick(hub); return Field<ControllerPairingRegistry>(hub, "pairing").MatchesBinding(55, confirm.Session, 0); }), Is.True);
                original = ControllerPairingWire.Wrap(Packet(101, 1010), 11, 44); oldSender.Send(original, original.Length, destination);
                Thread.Sleep(30); Tick(hub);
                Assert.That(Get<uint>(Field<Array>(hub, "latest").GetValue(0), "Sequence"), Is.EqualTo(1), "The replaced device cannot reclaim the slot.");
            }
        }
        finally
        {
            if (host != null) StopUdpHub(host);
            if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
        }
    }

    [Test]
    public void OccupiedUdpPortLeavesUsbInputUsableAndReceiverStopsDuringRetry()
    {
        GameObject host = null;
        Thread thread = null;
        using (Socket occupied = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            occupied.Bind(new IPEndPoint(IPAddress.Any, 0));
            try
            {
                MonoBehaviour hub = StartUdpHub(((IPEndPoint)occupied.LocalEndPoint).Port, out host, out thread);
                Assert.That(WaitFor(() => Field<string>(hub, "udpError").Length > 0), Is.True);
                Submit(Field<object>(hub, "transports"), "SubmitUsb", Packet(1, 10), Time.realtimeSinceStartupAsDouble, true, "USB test");
                Tick(hub);
                Assert.That(Field<bool[]>(hub, "valid")[0], Is.True);
                Assert.That((string)hub.GetType().GetProperty("Status").GetValue(hub), Is.EqualTo("USBリモコンがつながりました"));
                Assert.That((string)hub.GetType().GetProperty("PortName").GetValue(hub), Is.EqualTo("USB test"));
            }
            finally { if (host != null) StopUdpHub(host); }
            Assert.That(thread.IsAlive, Is.False);
        }
    }

    private static MonoBehaviour StartUdpHub(int port, out GameObject host, out Thread thread)
    {
        host = new GameObject("Isolated UDP hub test");
        host.SetActive(false); // Skip Awake and the native serial receiver.
        MonoBehaviour hub = (MonoBehaviour)host.AddComponent(Runtime("HardwareControllerHub"));
        var pairing = new ControllerPairingRegistry(33, 0); pairing.Load(0, 11, 44);
        hub.GetType().GetField("pairing", Private).SetValue(hub, pairing);
        hub.GetType().GetField("savedPairingRevision", Private).SetValue(hub, 0);
        hub.GetType().GetField("udpPort", Private).SetValue(hub, port);
        hub.GetType().GetField("realtimeOrigin", Private).SetValue(hub, Time.realtimeSinceStartupAsDouble);
        hub.GetType().GetField("stopwatch", Private).SetValue(hub, Stopwatch.StartNew());
        thread = new Thread(() => hub.GetType().GetMethod("ReceiveUdpLoop", Private).Invoke(hub, null)) { IsBackground = true };
        hub.GetType().GetField("udpReceiver", Private).SetValue(hub, thread);
        thread.Start();
        return hub;
    }
    private static bool HubHasSample(MonoBehaviour hub) { Tick(hub); return Field<bool[]>(hub, "valid")[0]; }
    private static void StopUdpHub(GameObject host)
    {
        // Unity does not guarantee OnDestroy for a host that was never active.
        Component hub = host.GetComponent(Runtime("HardwareControllerHub"));
        hub.GetType().GetMethod("OnDestroy", Private).Invoke(hub, null);
        UnityEngine.Object.DestroyImmediate(host);
    }
    private static void Tick(MonoBehaviour hub) => hub.GetType().GetMethod("Update", Private).Invoke(hub, null);
    private static T Field<T>(MonoBehaviour hub, string name)
    {
        lock (hub.GetType().GetField("bufferLock", Private).GetValue(hub))
            return (T)hub.GetType().GetField(name, Private).GetValue(hub);
    }
    private static bool WaitFor(Func<bool> condition)
    {
        Stopwatch deadline = Stopwatch.StartNew();
        while (deadline.Elapsed.TotalSeconds < 2)
        {
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return condition();
    }

    private static object Input(byte group = 0) => Activator.CreateInstance(Runtime("HardwareUdpInput"), new object[] { group });
    private static bool Accept(object input, byte[] packet, IPEndPoint sender, double realtime) => Accept(input, packet, sender, realtime, out _, out _);
    private static bool Accept(object input, byte[] packet, IPEndPoint sender, double realtime, out object sample, out bool restarted)
    {
        object[] args = { packet, sender, realtime, null, null, false };
        bool accepted = (bool)input.GetType().GetMethod("TryAccept").Invoke(input, args);
        sample = args[4]; restarted = (bool)args[5];
        return accepted;
    }
    private static void Submit(object buffer, string method, byte[] packet, double realtime, bool restarted, string port)
    {
        object[] args = { packet, realtime, (byte)0, null, null };
        Assert.That(Runtime("HardwareMotionPacketCodec").GetMethod("TryDecode").Invoke(null, args), Is.True);
        buffer.GetType().GetMethod(method).Invoke(buffer, new[] { args[3], args[4], restarted, port });
    }
    private static bool Select(object buffer, double realtime, out object sample, out bool reset, out string port)
    {
        object[] args = { Enum.ToObject(Runtime("ControllerSlot"), 0), realtime, null, false, string.Empty };
        bool selected = (bool)buffer.GetType().GetMethod("TrySelect").Invoke(buffer, args);
        sample = args[2]; reset = (bool)args[3]; port = (string)args[4];
        return selected;
    }
    private static T Get<T>(object sample, string property) => (T)sample.GetType().GetProperty(property).GetValue(sample);
    private static byte[] Packet(uint sequence, uint millis, byte slot = 0, byte group = 0, byte flags = 15)
    {
        byte[] bytes = new byte[64];
        bytes[0] = 0x4b; bytes[1] = 0x4a; bytes[2] = 1; bytes[3] = 64;
        bytes[4] = group; bytes[5] = slot; bytes[6] = flags;
        Write(bytes, 8, BitConverter.GetBytes(sequence)); Write(bytes, 12, BitConverter.GetBytes(millis));
        Write(bytes, 32, BitConverter.GetBytes(1f)); Seal(bytes);
        return bytes;
    }
    private static void Seal(byte[] bytes) => Write(bytes, 60, BitConverter.GetBytes((uint)Runtime("HardwareMotionPacketCodec").GetMethod("Crc32").Invoke(null, new object[] { bytes, 60 })));
    private static void Write(byte[] bytes, int offset, byte[] value)
    {
        if (!BitConverter.IsLittleEndian) Array.Reverse(value);
        Array.Copy(value, 0, bytes, offset, value.Length);
    }
}
