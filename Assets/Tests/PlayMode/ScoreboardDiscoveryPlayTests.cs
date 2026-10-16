using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

public sealed class ScoreboardDiscoveryPlayTests
{
    private ScoreboardPlayTests.TestScoreServer server;
    private DiscoveryResponder responder;
    private object client;
    private static readonly Type ClientType = Type.GetType("ScoreboardClient, Assembly-CSharp", true);
    private static readonly Type ReplyType = Type.GetType("ScoreboardReply, Assembly-CSharp", true);
    private static object Get(object target, string name) => target.GetType().GetField(name).GetValue(target);

    [SetUp]
    public void Setup()
    {
        server = new ScoreboardPlayTests.TestScoreServer();
        responder = new DiscoveryResponder(server.Port, server.ServerId);
        client = Activator.CreateInstance(ClientType, new object[] { responder.Port });
    }

    [TearDown]
    public void Cleanup()
    {
        ((IDisposable)client)?.Dispose(); responder?.Dispose(); server?.Dispose();
    }

    private sealed class Received
    {
        public object Reply;
        public string Error;
        public void Complete(object reply, string error) { Reply = reply; Error = error; }
    }

    private IEnumerator Request(string method, Received result, params object[] values)
    {
        Type callbackType = typeof(Action<,>).MakeGenericType(ReplyType, typeof(string));
        Delegate callback = Delegate.CreateDelegate(callbackType, result, typeof(Received).GetMethod("Complete"));
        return (IEnumerator)ClientType.GetMethod(method).Invoke(client, values.Concat(new object[] { callback }).ToArray());
    }

    private object Submission(string serverId)
    {
        object score = Activator.CreateInstance(Type.GetType("ScoreSubmission, Assembly-CSharp", true));
        foreach (var pair in new[] { ("runId", Guid.NewGuid().ToString()), ("name", "新人"), ("difficulty", "normal"), ("serverId", serverId) })
            score.GetType().GetField(pair.Item1).SetValue(score, pair.Item2);
        score.GetType().GetField("defeated").SetValue(score, 88);
        return score;
    }

    [UnityTest]
    public IEnumerator AutomaticallyDiscoversAndCachesTheServerWithoutAnIpSetting()
    {
        var loaded = new Received();
        yield return Request("Load", loaded, "normal");
        Assert.That(loaded.Error, Is.Null);
        Assert.That(Get(loaded.Reply, "serverId"), Is.EqualTo(server.ServerId));
        Assert.That(responder.Nonces.Count, Is.EqualTo(1));
        responder.Respond = false;
        var qualification = new Received();
        yield return Request("Qualify", qualification, "normal", 88);
        Assert.That(qualification.Error, Is.Null, "A healthy cached endpoint does not need another broadcast.");
        Assert.That(responder.Nonces.Count, Is.EqualTo(1));
        Assert.That(server.Headers.All(header => header.Contains("X-Kosen-Score-Server: " + server.ServerId)), Is.True);
    }

    [UnityTest]
    public IEnumerator BroadcastWorksWithoutLoopbackResponses()
    {
        responder.IgnoreLoopback = true;
        var loaded = new Received();
        yield return Request("Load", loaded, "normal");
        Assert.That(loaded.Error, Is.Null, "The LAN broadcast response must supply a reachable source address.");
        Assert.That(Get(loaded.Reply, "serverId"), Is.EqualTo(server.ServerId));
        Assert.That(responder.LanResponses, Is.GreaterThan(0));
    }

    [UnityTest]
    public IEnumerator FailedDiscoveryCanFindAServerStartedLaterAndRejectsUnrelatedReplies()
    {
        responder.WrongNonce = true;
        var missing = new Received();
        yield return Request("Load", missing, "normal");
        Assert.That(missing.Reply, Is.Null);
        Assert.That(missing.Error, Does.Contain("見つかりません"));
        Assert.That(server.Headers.Count, Is.Zero);
        responder.WrongNonce = false; responder.Version = 2;
        var oldProtocol = new Received();
        yield return Request("Load", oldProtocol, "normal");
        Assert.That(oldProtocol.Reply, Is.Null);
        responder.Version = 1;
        var started = new Received();
        yield return Request("Load", started, "normal");
        Assert.That(started.Error, Is.Null);
    }

    [UnityTest]
    public IEnumerator MultipleDatabasesDoNotCauseAnArbitraryConnection()
    {
        responder.OtherServerId = Guid.NewGuid().ToString();
        var result = new Received();
        yield return Request("Load", result, "normal");
        Assert.That(result.Reply, Is.Null);
        Assert.That(result.Error, Does.Contain("複数"));
        Assert.That(server.Headers.Count, Is.Zero);
    }

    [UnityTest]
    public IEnumerator FailedCachedAddressIsRediscoveredBeforeSavingToTheSameDatabase()
    {
        var loaded = new Received();
        yield return Request("Load", loaded, "normal");
        Assert.That(loaded.Error, Is.Null);
        string identity = server.ServerId;
        server.Dispose();
        server = new ScoreboardPlayTests.TestScoreServer(identity);
        responder.HttpPort = server.Port;
        object score = Submission(identity);
        var saved = new Received();
        yield return Request("Submit", saved, score);
        Assert.That(saved.Error, Is.Null);
        Assert.That(server.Posts, Is.EqualTo(1));
        Assert.That(responder.Nonces.Count, Is.EqualTo(2));
        Assert.That(server.Bodies.Single(), Does.Contain((string)Get(score, "runId")));
    }

    [UnityTest]
    public IEnumerator ARegistrationCannotMoveToADifferentDatabaseDuringRetry()
    {
        var loaded = new Received();
        yield return Request("Load", loaded, "normal");
        Assert.That(loaded.Error, Is.Null);
        string identity = server.ServerId;
        server.Dispose();
        server = new ScoreboardPlayTests.TestScoreServer();
        responder.HttpPort = server.Port; responder.ServerId = server.ServerId;
        var saved = new Received();
        yield return Request("Submit", saved, Submission(identity));
        Assert.That(saved.Reply, Is.Null);
        Assert.That(saved.Error, Does.Contain("元のサーバー"));
        Assert.That(server.Posts, Is.Zero);
    }

    internal sealed class DiscoveryResponder : IDisposable
    {
        private readonly UdpClient socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        private readonly Thread thread;
        private volatile bool stopped;
        public volatile bool Respond = true, WrongNonce, IgnoreLoopback;
        public volatile int HttpPort, Version = 1;
        public volatile string ServerId, OtherServerId;
        public int LanResponses;
        public readonly ConcurrentDictionary<string, bool> Nonces = new ConcurrentDictionary<string, bool>();
        public readonly int Port;
        public DiscoveryResponder(int httpPort, string serverId)
        {
            HttpPort = httpPort; ServerId = serverId;
            Port = ((IPEndPoint)socket.Client.LocalEndPoint).Port;
            thread = new Thread(Serve) { IsBackground = true }; thread.Start();
        }
        private void Serve()
        {
            while (!stopped)
            {
                try
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    string query = Encoding.UTF8.GetString(socket.Receive(ref remote));
                    Match nonce = Regex.Match(query, "\"nonce\":\"([a-f0-9]{32})\"");
                    if (!nonce.Success || !query.Contains("kosen.score.discover")) continue;
                    Nonces.TryAdd(nonce.Groups[1].Value, true);
                    if (!Respond || (IgnoreLoopback && IPAddress.IsLoopback(remote.Address))) continue;
                    if (!IPAddress.IsLoopback(remote.Address)) Interlocked.Increment(ref LanResponses);
                    Send(ServerId); if (OtherServerId != null) Send(OtherServerId);
                    void Send(string serverId)
                    {
                        string body = "{\"type\":\"kosen.score.offer\",\"version\":" + Version + ",\"nonce\":\""
                            + (WrongNonce ? new string('0', 32) : nonce.Groups[1].Value) + "\",\"serverId\":\"" + serverId + "\",\"port\":" + HttpPort + "}";
                        byte[] bytes = Encoding.UTF8.GetBytes(body); socket.Send(bytes, bytes.Length, remote);
                    }
                }
                catch (SocketException) { if (stopped) return; }
                catch (ObjectDisposedException) { return; }
            }
        }
        public void Dispose() { stopped = true; socket.Dispose(); thread.Join(1000); }
    }
}
