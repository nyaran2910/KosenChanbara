using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public sealed class PhoneServerEndpoint
{
    public string Url, ServerId, HostKey;
}

// A short coroutine polls a nonblocking UDP socket. No socket receive or
// network timeout blocks Unity's main thread.
public sealed class PhoneServerDiscovery : IDisposable
{
    public const int DefaultPort = 42045;
    [Serializable] private sealed class Query
    {
        public string type = "kosen.phone.discover", nonce;
        public int version = 1;
    }
    [Serializable] private sealed class Offer
    {
        public string type, nonce, serverId, hostKey;
        public int version, port;
    }
    private readonly int configuredPort;
    private readonly HashSet<UdpClient> sockets = new HashSet<UdpClient>();
    private bool disposed;
    public PhoneServerDiscovery(int port = 0) { configuredPort = port; }

    public IEnumerator Find(Action<PhoneServerEndpoint, string> completed)
    {
        int port = configuredPort;
        string portSetting = Environment.GetEnvironmentVariable("KOSEN_PHONE_DISCOVERY_PORT");
        if (port == 0 && !string.IsNullOrWhiteSpace(portSetting) && !int.TryParse(portSetting, out port)) port = -1;
        if (port == 0) port = DefaultPort;
        if (port < 1 || port > 65535)
        { completed(null, "スマホサーバーの探索ポートを確認してください"); yield break; }
        UdpClient socket = null;
        try { socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)); socket.EnableBroadcast = true; }
        catch (SocketException) { socket?.Dispose(); socket = null; }
        if (socket == null || disposed)
        { socket?.Dispose(); completed(null, "スマホサーバーを探索できません。接続を確認して再試行してください"); yield break; }
        sockets.Add(socket);
        string nonce = Guid.NewGuid().ToString("N");
        byte[] query = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Query { nonce = nonce }));
        List<IPAddress> targets = BroadcastTargets();
        var found = new Dictionary<string, PhoneServerEndpoint>();
        double started = Time.realtimeSinceStartupAsDouble, nextSend = started;
        try
        {
            while (!disposed && Time.realtimeSinceStartupAsDouble - started < 1.0)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now >= nextSend)
                {
                    foreach (IPAddress target in targets)
                        try { socket.Send(query, query.Length, new IPEndPoint(target, port)); }
                        catch (SocketException) { }
                    nextSend = now + .3;
                }
                for (int i = 0; i < 32; i++)
                {
                    if (!TryReceive(socket, out byte[] packet, out IPEndPoint remote)) break;
                    if (packet.Length > 1024) continue;
                    Offer offer = null;
                    try { offer = JsonUtility.FromJson<Offer>(Encoding.UTF8.GetString(packet)); } catch (ArgumentException) { }
                    if (offer == null || offer.type != "kosen.phone.offer" || offer.version != 1 || offer.nonce != nonce
                        || offer.port < 1 || offer.port > 65535 || !Guid.TryParse(offer.serverId, out Guid id)) continue;
                    string serverId = id.ToString();
                    if (string.IsNullOrEmpty(offer.hostKey) || offer.hostKey.Length < 24 || offer.hostKey.Length > 128) continue;
                    // One host may answer through Wi-Fi, Ethernet and loopback.
                    // Treat those addresses as one server, preferring loopback.
                    if (!found.ContainsKey(serverId) || IPAddress.IsLoopback(remote.Address))
                        found[serverId] = new PhoneServerEndpoint { Url = "ws://" + remote.Address + ":" + offer.port + "/signal", ServerId = serverId, HostKey = offer.hostKey };
                }
                yield return null;
            }
        }
        finally { sockets.Remove(socket); socket.Dispose(); }
        if (disposed) yield break;
        if (found.Count > 1)
        { completed(null, "スマホサーバーが複数見つかりました。1台だけ起動して再試行してください"); yield break; }
        foreach (PhoneServerEndpoint endpoint in found.Values) { completed(endpoint, null); yield break; }
        completed(null, "スマホサーバーが見つかりません。親機とLAN接続を確認してください");
    }

    private static bool TryReceive(UdpClient socket, out byte[] packet, out IPEndPoint remote)
    {
        packet = null; remote = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            if (socket.Available == 0) return false;
            packet = socket.Receive(ref remote); return true;
        }
        catch (SocketException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }

    private static List<IPAddress> BroadcastTargets()
    {
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
        try
        {
            foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation address in network.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.IPv4Mask == null) continue;
                    byte[] ip = address.Address.GetAddressBytes(), mask = address.IPv4Mask.GetAddressBytes();
                    if (Array.TrueForAll(mask, value => value == 0) || Array.TrueForAll(mask, value => value == 255)) continue;
                    for (int i = 0; i < ip.Length; i++) ip[i] = (byte)(ip[i] | ~mask[i]);
                    targets.Add(new IPAddress(ip));
                }
            }
        }
        catch (NetworkInformationException) { }
        catch (SocketException) { }
        return new List<IPAddress>(targets);
    }

    public void Dispose()
    {
        disposed = true;
        foreach (UdpClient socket in sockets) socket.Dispose();
        sockets.Clear();
    }
}
