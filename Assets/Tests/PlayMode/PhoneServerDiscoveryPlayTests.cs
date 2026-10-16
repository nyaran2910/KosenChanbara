using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

public sealed class PhoneServerDiscoveryPlayTests
{
    private static readonly Type Discovery = Type.GetType("PhoneServerDiscovery, Assembly-CSharp", true);
    private static readonly Type Endpoint = Type.GetType("PhoneServerEndpoint, Assembly-CSharp", true);
    private sealed class Result
    {
        public object Value; public string Error;
        public void Complete(object value, string error) { Value = value; Error = error; }
    }
    private static IEnumerator Find(object discovery, Result result)
    {
        var callback = Delegate.CreateDelegate(typeof(Action<,>).MakeGenericType(Endpoint, typeof(string)), result, typeof(Result).GetMethod("Complete"));
        return (IEnumerator)Discovery.GetMethod("Find").Invoke(discovery, new object[] { callback });
    }
    private sealed class Responder : IDisposable
    {
        private readonly UdpClient socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        private readonly Thread thread;
        public string Id = Guid.NewGuid().ToString();
        public volatile bool WrongNonce, InvalidPort, IgnoreLoopback;
        public int Port => ((IPEndPoint)socket.Client.LocalEndPoint).Port;
        public Responder()
        {
            thread = new Thread(() => {
                while (true) try {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    string query = Encoding.UTF8.GetString(socket.Receive(ref remote));
                    if (IgnoreLoopback && IPAddress.IsLoopback(remote.Address)) continue;
                    var match = System.Text.RegularExpressions.Regex.Match(query, "\"nonce\":\"([a-f0-9]{32})\"");
                    if (!match.Success) continue;
                    string nonce = WrongNonce ? new string('0', 32) : match.Groups[1].Value;
                    string reply = "{\"type\":\"kosen.phone.offer\",\"version\":1,\"nonce\":\"" + nonce +
                        "\",\"serverId\":\"" + Id + "\",\"port\":" + (InvalidPort ? 0 : 8080) +
                        ",\"hostKey\":\"a-long-dynamic-key-provided-by-the-parent\"}";
                    byte[] bytes = Encoding.UTF8.GetBytes(reply); socket.Send(bytes, bytes.Length, remote);
                } catch (SocketException) { break; } catch (ObjectDisposedException) { break; }
            }) { IsBackground = true };
            thread.Start();
        }
        public void Dispose() { socket.Dispose(); thread.Join(1000); }
    }
    [UnityTest]
    public IEnumerator FindsParentAndRetrievesKeyWithoutConfigurationThenFindsRestartedParent()
    {
        using (var responder = new Responder())
        using (var discovery = (IDisposable)Activator.CreateInstance(Discovery, new object[] { responder.Port }))
        {
            var result = new Result(); yield return Find(discovery, result);
            Assert.That(result.Error, Is.Null);
            Assert.That(Endpoint.GetField("Url").GetValue(result.Value), Is.EqualTo("ws://127.0.0.1:8080/signal"));
            Assert.That(Endpoint.GetField("HostKey").GetValue(result.Value), Is.EqualTo("a-long-dynamic-key-provided-by-the-parent"));
            responder.Id = Guid.NewGuid().ToString();
            result = new Result(); yield return Find(discovery, result);
            Assert.That(Endpoint.GetField("ServerId").GetValue(result.Value), Is.EqualTo(responder.Id));
        }
    }
    [UnityTest]
    public IEnumerator RejectsWrongNonceAndInvalidPortsAndRetriesWithoutRestartingGame()
    {
        using (var responder = new Responder())
        using (var discovery = (IDisposable)Activator.CreateInstance(Discovery, new object[] { responder.Port }))
        {
            responder.WrongNonce = true;
            var result = new Result(); yield return Find(discovery, result); Assert.That(result.Value, Is.Null);
            responder.WrongNonce = false; responder.InvalidPort = true;
            result = new Result(); yield return Find(discovery, result); Assert.That(result.Value, Is.Null);
            responder.InvalidPort = false; responder.IgnoreLoopback = true;
            result = new Result(); yield return Find(discovery, result); Assert.That(result.Error, Is.Null);
        }
    }
}
