using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public sealed class HardwareControllerHub : MonoBehaviour
{
    public const double StaleAfterSeconds = 0.25;
    public const int DefaultUdpPort = 42042;
    private static HardwareControllerHub instance;
    private readonly object bufferLock = new object();
    private readonly HardwareTransportSamples transports = new HardwareTransportSamples();
    private readonly MotionSample[] pending = new MotionSample[2];
    private readonly bool[] pendingValid = new bool[2];
    private readonly int[] pendingGenerations = new int[2];
    private readonly MotionSample[] latest = new MotionSample[2];
    private readonly bool[] valid = new bool[2];
    private readonly int[] generations = new int[2];
    private Thread receiver;
    private Thread udpReceiver;
    private Socket udpSocket;
    private volatile bool stopping;
    private int reconnectVersion;
    private string pendingStatus = "USBリモコンを待っています";
    private string pendingPort = string.Empty;
    private bool udpListening;
    private string udpError = string.Empty;
    private int udpPort = DefaultUdpPort;
    private Stopwatch stopwatch;
    private double realtimeOrigin;
    private string portOverride;
    private byte group;

    public string Status { get; private set; } = "無線リモコンを待っています";
    public string PortName { get; private set; } = string.Empty;

    public static HardwareControllerHub EnsureInstance()
    {
        if (instance != null) return instance;
        instance = FindAnyObjectByType<HardwareControllerHub>();
        if (instance != null) return instance;
        return new GameObject("Hardware Controller Hub").AddComponent<HardwareControllerHub>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        if (!Application.isPlaying || (instance != null && instance != this)) return;
        instance = this;
        StartReceiving();
    }

    private void StartReceiving()
    {
        if (udpReceiver != null && udpReceiver.IsAlive) return;
        stopping = false;
        lock (bufferLock)
        {
            transports.Clear();
            Array.Clear(pendingValid, 0, pendingValid.Length);
            Array.Clear(valid, 0, valid.Length);
            udpListening = false;
            udpError = string.Empty;
        }
        portOverride = Environment.GetEnvironmentVariable("KOSEN_CONTROLLER_SERIAL_PORT");
        if (int.TryParse(Environment.GetEnvironmentVariable("KOSEN_CONTROLLER_UDP_PORT"), out int configuredPort) && configuredPort > 0 && configuredPort <= 65535)
            udpPort = configuredPort;
        if (byte.TryParse(Environment.GetEnvironmentVariable("KOSEN_CONTROLLER_GROUP"), out byte configuredGroup) && configuredGroup <= 2)
            group = configuredGroup;
        realtimeOrigin = Time.realtimeSinceStartupAsDouble;
        stopwatch = Stopwatch.StartNew();
        receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "kojo USB controller" };
        udpReceiver = new Thread(ReceiveUdpLoop) { IsBackground = true, Name = "kojo UDP controller" };
        receiver.Start();
        udpReceiver.Start();
    }

    private void Update()
    {
        double realtime = Time.realtimeSinceStartupAsDouble;
        lock (bufferLock)
        {
            bool connected = false;
            bool poseValid = false;
            bool wireless = false;
            string activePorts = string.Empty;
            for (int i = 0; i < latest.Length; i++)
            {
                pendingValid[i] = transports.TrySelect((ControllerSlot)i, realtime, out pending[i], out bool reset, out string port);
                if (reset) pendingGenerations[i]++;
                latest[i] = pending[i];
                valid[i] = pendingValid[i];
                generations[i] = pendingGenerations[i];
                if (!valid[i]) continue;
                connected = true;
                poseValid |= latest[i].OrientationValid;
                wireless |= port.StartsWith("UDP ", StringComparison.Ordinal);
                if (activePorts.Length == 0) activePorts = port;
                else if (!activePorts.Contains(port)) activePorts += " / " + port;
            }
            Status = connected ? (wireless ? "無線リモコンがつながりました" : "USBリモコンがつながりました") +
                (poseValid ? string.Empty : "・センサーを確認しています") :
                udpListening ? "無線リモコンを待っています" :
                udpError.Length > 0 ? "無線を開けませんでした・" + pendingStatus : pendingStatus;
            PortName = connected ? activePorts : udpListening ? "UDP :" + udpPort : pendingPort;
        }
    }

    public bool IsConnected(ControllerSlot slot) => !IsStale(slot);
    public bool IsStale(ControllerSlot slot) => !valid[(int)slot] ||
        Time.realtimeSinceStartupAsDouble - latest[(int)slot].ReceivedRealtime > StaleAfterSeconds;
    public bool TryGetLatestSample(ControllerSlot slot, out MotionSample sample)
    {
        sample = latest[(int)slot];
        return !IsStale(slot);
    }
    public int GetRecenterVersion(ControllerSlot slot) => generations[(int)slot];
    public void Recenter(ControllerSlot slot)
    {
        lock (bufferLock) { pendingGenerations[(int)slot]++; generations[(int)slot] = pendingGenerations[(int)slot]; }
    }
    public void Reconnect()
    {
        lock (bufferLock)
        {
            Interlocked.Increment(ref reconnectVersion);
            transports.Clear();
            Array.Clear(pendingValid, 0, pendingValid.Length);
            Array.Clear(valid, 0, valid.Length);
            udpSocket?.Close();
        }
    }

    private void ReceiveUdpLoop()
    {
        while (!stopping)
        {
            int version = Volatile.Read(ref reconnectVersion);
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Blocking = false;
                socket.Bind(new IPEndPoint(IPAddress.Any, udpPort));
                lock (bufferLock)
                {
                    udpSocket = socket;
                    udpListening = true;
                    udpError = string.Empty;
                }
                HardwareUdpInput input = new HardwareUdpInput(group);
                double[] lastAcknowledged = { double.NegativeInfinity, double.NegativeInfinity };
                // Large enough to detect an oversized datagram instead of truncating it to a valid KJ packet.
                byte[] bytes = new byte[65535];
                while (!stopping && version == Volatile.Read(ref reconnectVersion))
                {
                    // Mono on macOS can leave a blocking ReceiveFrom alive after Close.
                    // Poll in bounded intervals so stopping always releases this thread.
                    if (!socket.Poll(100000, SelectMode.SelectRead)) continue;
                    if (stopping || version != Volatile.Read(ref reconnectVersion)) break;
                    EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                    int count;
                    try { count = socket.ReceiveFrom(bytes, ref sender); }
                    catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut || exception.SocketErrorCode == SocketError.WouldBlock) { continue; }
                    if (count != HardwareMotionPacketCodec.PacketLength) continue;
                    byte[] packet = new byte[count];
                    Array.Copy(bytes, packet, count);
                    double realtime = realtimeOrigin + stopwatch.Elapsed.TotalSeconds;
                    if (!input.TryAccept(packet, (IPEndPoint)sender, realtime, out ControllerSlot slot, out MotionSample sample, out bool restarted)) continue;
                    lock (bufferLock)
                    {
                        if (!stopping && version == Volatile.Read(ref reconnectVersion))
                            transports.SubmitUdp(slot, sample, restarted, "UDP " + sender);
                    }
                    if (!stopping && realtime - lastAcknowledged[(int)slot] >= .1)
                    {
                        byte[] acknowledgement = HardwareGameAcknowledgement.Encode(group, slot, sample.Sequence);
                        try
                        {
                            socket.SendTo(acknowledgement, sender);
                            lastAcknowledged[(int)slot] = realtime;
                        }
                        catch (SocketException) { /* Feedback loss must not interrupt controller input. */ }
                    }
                }
            }
            catch (Exception exception)
            {
                lock (bufferLock) udpError = exception.Message;
            }
            finally
            {
                lock (bufferLock)
                {
                    if (udpSocket == socket) udpSocket = null;
                    udpListening = false;
                }
                socket?.Dispose();
            }
            Pause(1000, version);
        }
    }

    private void ReceiveLoop()
    {
        byte[] bytes = new byte[1024];
        while (!stopping)
        {
            int version = Volatile.Read(ref reconnectVersion);
            string[] ports;
            try { ports = FindPorts(); }
            catch (Exception exception) { SetStatus("USBの確認に失敗しました: " + exception.Message, string.Empty); Pause(1000, version); continue; }
            if (ports.Length == 0) { SetStatus("USBリモコンを待っています", string.Empty); Pause(1000, version); continue; }
            foreach (string port in ports)
            {
                if (stopping || version != Volatile.Read(ref reconnectVersion)) break;
                try
                {
                    using (HardwareSerialPort serial = new HardwareSerialPort(port))
                    {
                        HardwareMotionStream stream = new HardwareMotionStream(group);
                        SetStatus("USB接続済み・リモコンを待っています", port);
                        double opened = stopwatch.Elapsed.TotalSeconds;
                        double lastValid = opened;
                        bool hasPacket = false;
                        while (!stopping && version == Volatile.Read(ref reconnectVersion))
                        {
                            int count = serial.Read(bytes);
                            if (count > 0)
                            {
                                stream.Feed(bytes, count, realtimeOrigin + stopwatch.Elapsed.TotalSeconds, (slot, sample, restarted) =>
                                {
                                    lock (bufferLock)
                                    {
                                        if (!stopping && version == Volatile.Read(ref reconnectVersion))
                                            transports.SubmitUsb(slot, sample, restarted, port);
                                    }
                                    hasPacket = true;
                                    lastValid = stopwatch.Elapsed.TotalSeconds;
                                });
                            }
                            else Thread.Sleep(5);
                            // Do not remain attached to an unrelated serial device forever.
                            if (!hasPacket && stopwatch.Elapsed.TotalSeconds - opened > 3) break;
                            // Reopen on an unplugged receiver or prolonged silence, allowing another port.
                            if (hasPacket && stopwatch.Elapsed.TotalSeconds - lastValid > 3) break;
                        }
                    }
                }
                catch (Exception exception) { SetStatus("USB: " + exception.Message, port); }
                finally
                {
                    lock (bufferLock)
                    {
                        transports.ClearUsb();
                    }
                }
            }
            Pause(1000, version);
        }
    }

    private string[] FindPorts()
    {
        if (!string.IsNullOrWhiteSpace(portOverride)) return new[] { portOverride };
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        List<string> ports = new List<string>();
        ports.AddRange(Directory.GetFiles("/dev", "cu.usbmodem*"));
        ports.AddRange(Directory.GetFiles("/dev", "cu.usbserial*"));
        ports.AddRange(Directory.GetFiles("/dev", "cu.SLAB_USBtoUART*"));
        ports.Sort(StringComparer.Ordinal);
        return ports.ToArray();
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        List<string> ports = new List<string>();
        ports.AddRange(Directory.GetFiles("/dev", "ttyACM*"));
        ports.AddRange(Directory.GetFiles("/dev", "ttyUSB*"));
        ports.Sort(StringComparer.Ordinal);
        return ports.ToArray();
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("HARDWARE\\DEVICEMAP\\SERIALCOMM"))
        {
            if (key == null) return Array.Empty<string>();
            List<string> ports = new List<string>();
            foreach (string name in key.GetValueNames())
                if (key.GetValue(name) is string port) ports.Add(port);
            ports.Sort(StringComparer.Ordinal);
            return ports.ToArray();
        }
#else
        return Array.Empty<string>();
#endif
    }

    private void SetStatus(string status, string port)
    {
        lock (bufferLock) { pendingStatus = status; pendingPort = port; }
    }
    private void Pause(int milliseconds, int version)
    {
        for (int elapsed = 0; elapsed < milliseconds && !stopping && version == Volatile.Read(ref reconnectVersion); elapsed += 25) Thread.Sleep(25);
    }
    private void OnDisable() => StopReceiving();

    private void StopReceiving()
    {
        stopping = true;
        lock (bufferLock)
        {
            udpSocket?.Close();
            transports.Clear();
            Array.Clear(pendingValid, 0, pendingValid.Length);
            Array.Clear(valid, 0, valid.Length);
            udpListening = false;
        }
        udpReceiver?.Join(1500);
        receiver?.Join(2500);
        udpReceiver = receiver = null;
        udpSocket = null;
    }

    private void OnDestroy()
    {
        StopReceiving();
        if (instance == this) instance = null;
    }
}
