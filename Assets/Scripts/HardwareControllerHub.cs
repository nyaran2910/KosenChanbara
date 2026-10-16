using SchoolFestival.Combat;
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
    private readonly IPEndPoint[] udpFeedbackTargets = new IPEndPoint[2];
    private readonly uint[] udpFeedbackSequences = new uint[2];
    private readonly double[] udpFeedbackReceived = new double[2];
    private readonly long[] udpPackets = new long[2];
    private readonly long[] usbPackets = new long[2];
    private readonly string[] selectedPorts = new string[2];
    private readonly HardwareSerialPort[] usbFeedbackPorts = new HardwareSerialPort[2];
    private readonly string[] usbFeedbackPortNames = new string[2];
    private readonly uint[] usbFeedbackSequences = new uint[2];
    private readonly double[] usbFeedbackReceived = new double[2];
    private readonly double[] usbLastAcknowledged = { double.NegativeInfinity, double.NegativeInfinity };
    private readonly object usbReceiversLock = new object();
    private readonly object backgroundWorkLock = new object();
    private readonly AutoResetEvent backgroundWorkReady = new AutoResetEvent(false);
    private readonly FeedbackRequest[] pendingFeedback = new FeedbackRequest[2];
    private readonly bool[] pendingFeedbackValid = new bool[2];
    private string pendingTrafficSnapshot;
    private Thread backgroundWorker;
    private readonly Dictionary<string, Thread> usbReceivers = new Dictionary<string, Thread>(StringComparer.Ordinal);
    private double nextTrafficWrite;
    private static readonly string RuntimeDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kosen-chanbara");
    private static readonly string FirmwareUpdateFlag = Path.Combine(RuntimeDirectory, "firmware-update.flag");
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

    private ControllerPairingRegistry pairing;
    private int savedPairingRevision = -1;
    private string pairingPath;
    public int PairingRevision { get { lock (bufferLock) return pairing?.Revision ?? 0; } }
    public int CompletedPairingSlot { get { lock (bufferLock) return pairing?.CompletedSlot ?? -1; } }
    [Serializable] private sealed class SavedPairing
    {
        public string host, p1Device, p1Session, p2Device, p2Session;
    }
    private static ulong Hex(string value) => ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture, out ulong id) ? id : 0;
    private void LoadPairing()
    {
        if (pairing != null) return;
        SavedPairing saved = null;
        try
        {
            string path = pairingPath ?? Path.Combine(RuntimeDirectory, "controller-pairing.json");
            if (File.Exists(path)) saved = JsonUtility.FromJson<SavedPairing>(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
        { UnityEngine.Debug.LogWarning("コントローラ登録を読み込めません: " + e.Message); }
        ulong host = Hex(saved?.host);
        pairing = new ControllerPairingRegistry(host == 0 ? ControllerPairingWire.NewId() : host, group);
        if (saved != null)
        {
            pairing.Load(0, Hex(saved.p1Device), Hex(saved.p1Session));
            pairing.Load(1, Hex(saved.p2Device), Hex(saved.p2Session));
        }
    }
    private void SavePairing()
    {
        SavedPairing data; int revision;
        lock (bufferLock)
        {
            if (pairing == null || pairing.Revision == savedPairingRevision) return;
            revision = pairing.Revision;
            data = new SavedPairing { host = pairing.Host.ToString("X16"),
                p1Device = pairing.Bindings[0].Device.ToString("X16"), p1Session = pairing.Bindings[0].Session.ToString("X16"),
                p2Device = pairing.Bindings[1].Device.ToString("X16"), p2Session = pairing.Bindings[1].Session.ToString("X16") };
        }
        try
        {
            string path = pairingPath ?? Path.Combine(RuntimeDirectory, "controller-pairing.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + ".tmp", JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
            else File.Move(path + ".tmp", path);
            savedPairingRevision = revision;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        { UnityEngine.Debug.LogWarning("コントローラ登録を保存できません: " + e.Message); }
    }
    private bool AcceptPairing(ulong device, ulong session, int slot, double realtime)
    {
        if (pairing.MatchesBinding(device, session, slot)) return true;
        var old = (ControllerPairingRegistry.Binding[])pairing.Bindings.Clone();
        if (!pairing.Accept(device, session, slot, realtime)) return false;
        for (int i = 0; i < 2; i++)
        {
            if (old[i].Device == pairing.Bindings[i].Device && old[i].Session == pairing.Bindings[i].Session) continue;
            transports.ClearSlot((ControllerSlot)i); valid[i] = false; pendingGenerations[i]++;
            udpFeedbackTargets[i] = null; usbFeedbackPorts[i] = null; usbFeedbackPortNames[i] = null;
            lock (backgroundWorkLock) { pendingFeedbackValid[i] = false; pendingFeedback[i] = null; }
        }
        return true;
    }
    public void BeginPairing(ControllerSlot slot)
    {
        LoadPairing();
        lock (bufferLock) pairing.Begin((int)slot);
    }
    public void CancelPairing() { lock (bufferLock) pairing?.Cancel(); }

    private sealed class FeedbackRequest
    {
        public byte[] Bytes;
        public IPEndPoint UdpTarget;
        public Socket UdpSocket;
        public HardwareSerialPort SerialPort;
    }

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
        backgroundWorker = new Thread(BackgroundWorkLoop) { IsBackground = true, Name = "kojo controller feedback and traffic" };
        backgroundWorker.Start();
        lock (bufferLock)
        {
            transports.Clear();
            Array.Clear(pendingValid, 0, pendingValid.Length);
            Array.Clear(valid, 0, valid.Length);
            Array.Clear(udpFeedbackTargets, 0, udpFeedbackTargets.Length);
            Array.Clear(usbFeedbackPorts, 0, usbFeedbackPorts.Length);
            Array.Clear(usbFeedbackPortNames, 0, usbFeedbackPortNames.Length);
            Array.Clear(selectedPorts, 0, selectedPorts.Length);
            Array.Clear(udpPackets, 0, udpPackets.Length);
            Array.Clear(usbPackets, 0, usbPackets.Length);
            udpListening = false;
            udpError = string.Empty;
        }
        portOverride = Environment.GetEnvironmentVariable("KOSEN_CONTROLLER_SERIAL_PORT");
        if (int.TryParse(Environment.GetEnvironmentVariable("KOSEN_CONTROLLER_UDP_PORT"), out int configuredPort) && configuredPort > 0 && configuredPort <= 65535)
            udpPort = configuredPort;
        if (byte.TryParse(Environment.GetEnvironmentVariable("KOSEN_CONTROLLER_GROUP"), out byte configuredGroup) && configuredGroup <= 2)
            group = configuredGroup;
        LoadPairing();
        SavePairing();
        realtimeOrigin = Time.realtimeSinceStartupAsDouble;
        stopwatch = Stopwatch.StartNew();
        receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "kojo USB controller discovery" };
        udpReceiver = new Thread(ReceiveUdpLoop) { IsBackground = true, Name = "kojo UDP controller" };
        receiver.Start();
        udpReceiver.Start();
    }

    private void Update()
    {
        SavePairing();
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
                selectedPorts[i] = pendingValid[i] ? port : string.Empty;
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
        if (realtime >= nextTrafficWrite)
        {
            nextTrafficWrite = realtime + 1;
            QueueTrafficSnapshot(realtime);
        }
    }

    private void QueueTrafficSnapshot(double realtime)
    {
        try
        {
            string snapshot;
            lock (bufferLock)
            {
                snapshot = "{\"time\":" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() +
                    ",\"p1\":" + SlotTraffic(0, realtime) +
                    ",\"p2\":" + SlotTraffic(1, realtime) + "}";
            }
            lock (backgroundWorkLock) pendingTrafficSnapshot = snapshot;
            backgroundWorkReady.Set();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void BackgroundWorkLoop()
    {
        while (!stopping)
        {
            backgroundWorkReady.WaitOne(250);
            FeedbackRequest[] feedback = new FeedbackRequest[2];
            bool[] hasFeedback = new bool[2];
            string snapshot;
            lock (backgroundWorkLock)
            {
                snapshot = pendingTrafficSnapshot;
                pendingTrafficSnapshot = null;
                for (int i = 0; i < 2; i++)
                {
                    if (!pendingFeedbackValid[i]) continue;
                    feedback[i] = pendingFeedback[i];
                    pendingFeedback[i] = null;
                    pendingFeedbackValid[i] = false;
                    hasFeedback[i] = true;
                }
            }
            for (int i = 0; i < 2; i++) if (hasFeedback[i]) WriteFeedback(feedback[i]);
            if (snapshot == null) continue;
            try
            {
                Directory.CreateDirectory(RuntimeDirectory);
                File.WriteAllText(Path.Combine(RuntimeDirectory, "controller-traffic.json"), snapshot);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void WriteFeedback(FeedbackRequest request)
    {
        if (request == null) return;
        try
        {
            if (request.UdpSocket != null && request.UdpTarget != null) request.UdpSocket.SendTo(request.Bytes, request.UdpTarget);
            else request.SerialPort?.Write(request.Bytes);
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    // Feedback is deliberately coalesced per controller. A congested or removed
    // transport must never make the Unity thread wait behind an older write.
    private void QueueFeedback(ControllerSlot slot, byte[] bytes, Socket socket, IPEndPoint target, HardwareSerialPort serial)
    {
        int index = (int)slot;
        lock (bufferLock)
        {
            if (pairing == null || pairing.Bindings[index].Device == 0) return;
            var binding = pairing.Bindings[index];
            bytes = ControllerPairingWire.Wrap(bytes, binding.Device, binding.Session);
        }
        lock (backgroundWorkLock)
        {
            if (pendingFeedbackValid[index] && IsHaptic(pendingFeedback[index].Bytes) && !IsHaptic(bytes))
                return;
            pendingFeedback[index] = new FeedbackRequest
            {
                Bytes = bytes,
                UdpSocket = socket,
                UdpTarget = target,
                SerialPort = serial
            };
            pendingFeedbackValid[index] = true;
        }
        backgroundWorkReady.Set();
    }

    private static bool IsHaptic(byte[] bytes) => bytes != null && bytes.Length == 40 &&
        (bytes[26] != 0 || bytes[27] != 0);

    private string SlotTraffic(int index, double realtime)
    {
        string peer = selectedPorts[index] ?? string.Empty;
        double received = valid[index] ? latest[index].ReceivedRealtime : double.NegativeInfinity;
        if (peer.StartsWith("UDP ", StringComparison.Ordinal) && udpFeedbackTargets[index] != null)
            received = udpFeedbackReceived[index];
        else if (usbFeedbackPortNames[index] != null && usbFeedbackPortNames[index].Length > 0)
        {
            received = usbFeedbackReceived[index];
            if (peer.Length == 0) peer = usbFeedbackPortNames[index];
        }
        double age = double.IsNegativeInfinity(received) ? -1 : Math.Max(0, realtime - received);
        return "{\"udp\":" + udpPackets[index] + ",\"usb\":" + usbPackets[index] +
            ",\"age\":" + age.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
            ",\"peer\":\"" + peer + "\"}";
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
    public void SendFeedback(ControllerSlot slot, float seconds)
    {
        int index = (int)slot;
        if (index < 0 || index >= udpFeedbackTargets.Length || seconds <= 0f) return;
        ushort duration = (ushort)Mathf.Clamp(Mathf.RoundToInt(seconds * 1000f), 1, 1000);
        byte[] bytes;
        Socket socket = null;
        IPEndPoint target = null;
        HardwareSerialPort serial = null;
        uint sequence = 0;
        lock (bufferLock)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            string selectedPort = selectedPorts[index] ?? string.Empty;
            if (selectedPort.StartsWith("UDP ", StringComparison.Ordinal) &&
                udpSocket != null && udpFeedbackTargets[index] != null &&
                now - udpFeedbackReceived[index] <= StaleAfterSeconds)
            {
                socket = udpSocket;
                target = udpFeedbackTargets[index];
                sequence = udpFeedbackSequences[index];
            }
            else if (usbFeedbackPorts[index] != null && selectedPort == usbFeedbackPortNames[index] &&
                now - usbFeedbackReceived[index] <= StaleAfterSeconds)
            {
                serial = usbFeedbackPorts[index];
                sequence = usbFeedbackSequences[index];
            }
            else return;
        }
        bytes = HardwareGameAcknowledgement.Encode(group, slot, sequence, duration);
        QueueFeedback(slot, bytes, socket, target, serial);
    }
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
            Array.Clear(udpFeedbackTargets, 0, udpFeedbackTargets.Length);
            Array.Clear(usbFeedbackPorts, 0, usbFeedbackPorts.Length);
            Array.Clear(usbFeedbackPortNames, 0, usbFeedbackPortNames.Length);
            Array.Clear(selectedPorts, 0, selectedPorts.Length);
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
                    byte[] datagram = new byte[count];
                    Array.Copy(bytes, datagram, count);
                    double realtime = realtimeOrigin + stopwatch.Elapsed.TotalSeconds;
                    if (ControllerPairingWire.TryDecode(datagram, out var control))
                    {
                        byte[] response = null;
                        lock (bufferLock)
                            if (pairing.Handle(control, sender.ToString(), realtime, out var reply)) response = ControllerPairingWire.Encode(reply);
                        if (response != null) socket.SendTo(response, sender);
                        continue;
                    }
                    if (!ControllerPairingWire.Unwrap(datagram, out ulong device, out ulong session, out byte[] packet)
                        || packet.Length != HardwareMotionPacketCodec.PacketLength) continue;
                    int packetSlot = packet[5];
                    lock (bufferLock)
                    {
                        if (!pairing.Allows(device, session, packetSlot, realtime)) continue;
                        if (!pairing.MatchesBinding(device, session, packetSlot)) input.ResetSlot((ControllerSlot)packetSlot);
                    }
                    if (!input.TryAccept(packet, (IPEndPoint)sender, realtime, out ControllerSlot slot, out MotionSample sample, out bool restarted)) continue;
                    lock (bufferLock)
                    {
                        if (!stopping && version == Volatile.Read(ref reconnectVersion))
                        {
                            if (!AcceptPairing(device, session, (int)slot, realtime)) continue;
                            transports.SubmitUdp(slot, sample, restarted, "UDP " + sender);
                            udpFeedbackTargets[(int)slot] = (IPEndPoint)sender;
                            udpFeedbackSequences[(int)slot] = sample.Sequence;
                            udpFeedbackReceived[(int)slot] = realtime;
                            udpPackets[(int)slot]++;
                        }
                    }
                    if (!stopping && realtime - lastAcknowledged[(int)slot] >= .1)
                    {
                        byte[] acknowledgement = HardwareGameAcknowledgement.Encode(group, slot, sample.Sequence);
                        QueueFeedback(slot, acknowledgement, socket, (IPEndPoint)sender, null);
                        lastAcknowledged[(int)slot] = realtime;
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
        while (!stopping)
        {
            int version = Volatile.Read(ref reconnectVersion);
            if (File.Exists(FirmwareUpdateFlag))
            {
                // Stop and close every serial handle before esptool takes the ports.
                while (!stopping && File.Exists(FirmwareUpdateFlag))
                {
                    PruneUsbReceivers();
                    if (!HasLiveUsbReceivers()) break;
                    Thread.Sleep(10);
                }
                Pause(100, version);
                continue;
            }

            string[] ports;
            try { ports = FindPorts(); }
            catch (Exception exception)
            {
                SetStatus("USBの確認に失敗しました: " + exception.Message, string.Empty);
                Pause(1000, version);
                continue;
            }

            bool hasPort = false;
            lock (usbReceiversLock)
            {
                PruneUsbReceiversLocked();
                foreach (string port in ports)
                {
                    hasPort = true;
                    if (stopping || version != Volatile.Read(ref reconnectVersion) || usbReceivers.ContainsKey(port)) continue;
                    string capturedPort = port;
                    int capturedVersion = version;
                    Thread worker = new Thread(() => ReceivePortLoop(capturedPort, capturedVersion))
                    {
                        IsBackground = true,
                        Name = "kojo USB " + capturedPort
                    };
                    usbReceivers.Add(port, worker);
                    worker.Start();
                }
            }
            if (!hasPort) SetStatus("USBリモコンを待っています", string.Empty);
            Pause(250, version);
        }
    }

    private void ReceivePortLoop(string port, int version)
    {
        byte[] bytes = new byte[1024];
        try
        {
            if (File.Exists(FirmwareUpdateFlag)) return;
            using (HardwareSerialPort serial = new HardwareSerialPort(port))
            {
                HardwareMotionStream stream = new HardwareMotionStream(group);
                PairedControllerStream envelopes = new PairedControllerStream();
                ulong serialSession = 0;
                SetStatus("USB接続済み・リモコンを待っています", port);
                double opened = stopwatch.Elapsed.TotalSeconds;
                double lastValid = opened;
                bool hasPacket = false;
                while (!stopping && version == Volatile.Read(ref reconnectVersion) && !File.Exists(FirmwareUpdateFlag))
                {
                    int count = serial.Read(bytes);
                    if (count > 0)
                    {
                        envelopes.Feed(bytes, count, envelope =>
                        {
                            if (!ControllerPairingWire.Unwrap(envelope, out ulong device, out ulong session, out byte[] packet)) return;
                            double realtime = realtimeOrigin + stopwatch.Elapsed.TotalSeconds;
                            lock (bufferLock) if (!pairing.Allows(device, session, packet[5], realtime)) return;
                            if (serialSession != session) { stream = new HardwareMotionStream(group); serialSession = session; }
                            stream.Feed(packet, packet.Length, realtime, (slot, sample, restarted) =>
                            {
                            int index = (int)slot;
                            lock (bufferLock)
                            {
                                if (!stopping && version == Volatile.Read(ref reconnectVersion))
                                {
                                    if (!AcceptPairing(device, session, index, realtime)) return;
                                    transports.SubmitUsb(slot, sample, restarted, port);
                                    usbPackets[index]++;
                                    usbFeedbackPorts[index] = serial;
                                    usbFeedbackPortNames[index] = port;
                                    usbFeedbackSequences[index] = sample.Sequence;
                                    usbFeedbackReceived[index] = sample.ReceivedRealtime;
                                    if (sample.ReceivedRealtime - usbLastAcknowledged[index] >= .1)
                                    {
                                        usbLastAcknowledged[index] = sample.ReceivedRealtime;
                                        QueueFeedback(slot, HardwareGameAcknowledgement.Encode(group, slot, sample.Sequence), null, null, serial);
                                    }
                                }
                            }
                            hasPacket = true;
                            lastValid = stopwatch.Elapsed.TotalSeconds;
                            });
                        });
                    }
                    else Thread.Sleep(5);
                    // Release unrelated ports and reopen unplugged or silent receivers.
                    if (!hasPacket && stopwatch.Elapsed.TotalSeconds - opened > 3) break;
                    if (hasPacket && stopwatch.Elapsed.TotalSeconds - lastValid > 3) break;
                }
            }
        }
        catch (Exception exception) { SetStatus("USB: " + exception.Message, port); }
        finally
        {
            lock (bufferLock)
            {
                for (int index = 0; index < usbFeedbackPorts.Length; index++)
                {
                    if (usbFeedbackPortNames[index] != port) continue;
                    usbFeedbackPorts[index] = null;
                    usbFeedbackPortNames[index] = string.Empty;
                }
            }
        }
    }

    private void PruneUsbReceivers()
    {
        lock (usbReceiversLock) PruneUsbReceiversLocked();
    }

    private void PruneUsbReceiversLocked()
    {
        List<string> finished = new List<string>();
        foreach (KeyValuePair<string, Thread> item in usbReceivers)
            if (!item.Value.IsAlive) finished.Add(item.Key);
        foreach (string port in finished) usbReceivers.Remove(port);
    }

    private bool HasLiveUsbReceivers()
    {
        lock (usbReceiversLock)
        {
            foreach (Thread worker in usbReceivers.Values)
                if (worker.IsAlive) return true;
            return false;
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
        return HardwareSerialPort.FindWindowsPorts();
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
            Array.Clear(udpFeedbackTargets, 0, udpFeedbackTargets.Length);
            Array.Clear(usbFeedbackPorts, 0, usbFeedbackPorts.Length);
            Array.Clear(usbFeedbackPortNames, 0, usbFeedbackPortNames.Length);
            Array.Clear(selectedPorts, 0, selectedPorts.Length);
            transports.Clear();
            Array.Clear(pendingValid, 0, pendingValid.Length);
            Array.Clear(valid, 0, valid.Length);
            udpListening = false;
        }
        udpReceiver?.Join(1500);
        receiver?.Join(1500);
        List<Thread> usbThreads;
        lock (usbReceiversLock)
        {
            usbThreads = new List<Thread>(usbReceivers.Values);
            usbReceivers.Clear();
        }
        foreach (Thread thread in usbThreads) thread.Join(500);
        udpReceiver = receiver = null;
        udpSocket = null;
    }

    private void OnDestroy()
    {
        StopReceiving();
        if (instance == this) instance = null;
    }
}
