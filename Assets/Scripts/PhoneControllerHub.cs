using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class PhoneControllerHub : MonoBehaviour
{
    private const double StaleAfterSeconds = 0.2;
    private const double ConnectionTimeoutSeconds = 8.0;
    private const double DisconnectGraceSeconds = 2.0;

    private sealed class SlotState
    {
        public readonly string Id;
        public RTCPeerConnection Peer;
        public RTCDataChannel Channel;
        // Track transport callbacks independently of the native channel object.
        public bool ChannelOpen;
        public string PhoneId = "", AcceptedPhoneId = "";
        public bool RegistrationCandidate;
        public int PreserveRotations;
        public int AxisOrder = 1, AxisSigns, AxisRevision, PendingAxisRevision;
        public double AxisSendAt;
        public string AxisAction = "axis.configure";
        public int PendingOrder, PendingSigns;
        public readonly List<RTCIceCandidateInit> PendingCandidates = new List<RTCIceCandidateInit>();
        public bool RemoteDescriptionSet;
        public bool CloseRequested;
        public double ConnectionDeadline;
        public double DisconnectDeadline;
        public RTCIceConnectionState IceConnectionState;
        public MotionSample LatestSample;
        public bool HasSample;
        public uint LatestSequence;
        public long Packets;
        public bool HasSequence;
        public bool HasAxisSequenceFloor;
        public uint AxisSequenceFloor;
        public int RecenterVersion;
        public int ConnectionGeneration;
        public ushort LatestRecenterSequence;
        public bool HasRecenterSequence;
        public string Status = "Waiting for QR";
        public string JoinUrl = string.Empty;
        public Texture2D QrTexture;

        public SlotState(string id)
        {
            Id = id;
        }
    }

    private sealed class SlotView
    {
        public RawImage QrImage;
    }

    private static PhoneControllerHub instance;
    private readonly SlotState[] slots = { new SlotState("p1"), new SlotState("p2") };
    private readonly SlotView[] views = { new SlotView(), new SlotView() };
    private readonly GameObject[] slotCards = new GameObject[2];
    private readonly ConcurrentQueue<string> receivedSignals = new ConcurrentQueue<string>();
    private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

    private CancellationTokenSource cancellation;
    private ClientWebSocket signalingSocket;
    private readonly PhoneServerDiscovery discovery = new PhoneServerDiscovery();
    private GameObject overlay;
    private double nextTrafficSnapshot;
    private static readonly string RuntimeDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kosen-chanbara");

    private int registrationSlot = -1, registrationGeneration;
    public bool RegistrationReady(ControllerSlot slot) => registrationSlot == (int)slot && GetSlot(slot).RegistrationCandidate && IsConnected(slot);
    public Texture2D GetQrTexture(ControllerSlot slot) => GetSlot(slot).QrTexture;
    public string GetRegistrationStatus(ControllerSlot slot) => GetSlot(slot).Status;
    public int GetAxisOrder(ControllerSlot slot) => GetSlot(slot).AxisOrder;
    public int GetAxisSigns(ControllerSlot slot) => GetSlot(slot).AxisSigns;
    public bool AxisApplied(ControllerSlot slot) => GetSlot(slot).PendingAxisRevision == 0;
    public void BeginRegistration(ControllerSlot controllerSlot)
    {
        EndRegistration(false);
        registrationSlot = (int)controllerSlot;
        SlotState slot = GetSlot(controllerSlot);
        registrationGeneration = slot.ConnectionGeneration;
        slot.RegistrationCandidate = false;
        RotateRegistrationQr(slot);
    }
    public void EndRegistration(bool commit)
    {
        if (registrationSlot < 0) return;
        SlotState slot = slots[registrationSlot];
        if (commit && slot.RegistrationCandidate && slot.PhoneId.Length > 0)
        {
            slot.AcceptedPhoneId = slot.PhoneId;
            PlayerPrefs.SetString("PhoneController." + slot.Id, slot.PhoneId); PlayerPrefs.Save();
        }
        else if (slot.RegistrationCandidate && slot.PhoneId != slot.AcceptedPhoneId) ClosePeer(slot, "登録をキャンセルしました");
        slot.RegistrationCandidate = false;
        registrationSlot = -1;
        RotateRegistrationQr(slot);
    }
    private void RotateRegistrationQr(SlotState slot)
    {
        slot.PreserveRotations++;
        if (cancellation != null) _ = SendSignalAsync(new { type = "slot.rotate", slot = slot.Id }, cancellation.Token);
    }
    public void ConfigureAxes(ControllerSlot controllerSlot, int order, int signs, string action = "axis.configure")
    {
        SlotState slot = GetSlot(controllerSlot);
        if (order < 0 || order > 5 || signs < 0 || signs > 7) return;
        slot.PendingOrder = order; slot.PendingSigns = signs; slot.AxisAction = action;
        slot.PendingAxisRevision = Math.Max(slot.PendingAxisRevision, slot.AxisRevision) + 1;
        slot.AxisSendAt = 0;
    }
    private void SendControl(SlotState slot, object command)
    {
        if (slot.Channel == null) return;
        try { slot.Channel.Send(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(command))); }
        catch (Exception e) { Debug.LogWarning("スマホ設定の送信に失敗: " + e.Message); }
    }

    public bool OverlayVisible => overlay != null && overlay.activeSelf;

    public void SetOverlayVisible(bool visible)
    {
        if (overlay != null)
            overlay.SetActive(visible);
        if (!visible && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void SetSinglePlayerMode(bool singlePlayer)
    {
        // Both slots remain available: in single play they share one fighter.
        if (slotCards[1] != null)
            slotCards[1].SetActive(true);
        if (slotCards[0] != null)
            slotCards[0].GetComponent<RectTransform>().anchoredPosition = new Vector2(-225, 220);
    }

    public static PhoneControllerHub EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindAnyObjectByType<PhoneControllerHub>();
        if (instance != null)
        {
            return instance;
        }

        GameObject gameObject = new GameObject("Phone Controller Hub");
        instance = gameObject.AddComponent<PhoneControllerHub>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        cancellation = new CancellationTokenSource();
        foreach (SlotState slot in slots) slot.AcceptedPhoneId = PlayerPrefs.GetString("PhoneController." + slot.Id, "");
        CreateOverlay();
    }

    private void Start()
    {
        StartCoroutine(WebRTC.Update());

        _ = RunSignalingLoopAsync(cancellation.Token);
    }

    private void Update()
    {
        while (receivedSignals.TryDequeue(out string json))
        {
            HandleSignalingMessage(json);
        }

        double now = Time.realtimeSinceStartupAsDouble;
        for (int index = 0; index < slots.Length; index++)
        {
            SlotState slot = slots[index];
            if (slot.ChannelOpen && slot.PendingAxisRevision > 0 && now >= slot.AxisSendAt)
            {
                slot.AxisSendAt = now + .3;
                SendControl(slot, new { type = slot.AxisAction, order = slot.PendingOrder, signs = slot.PendingSigns, request = slot.PendingAxisRevision });
            }

            if (slot.CloseRequested)
            {
                slot.CloseRequested = false;
                ClosePeer(slot, slot.Status);
            }

            if (slot.Peer != null && !IsChannelOpen(slot) && slot.ConnectionDeadline > 0 && now >= slot.ConnectionDeadline)
            {
                ClosePeer(slot, "Direct LAN connection timed out");
            }

            if (slot.DisconnectDeadline > 0 && now >= slot.DisconnectDeadline &&
                slot.IceConnectionState == RTCIceConnectionState.Disconnected)
            {
                ClosePeer(slot, "Phone disconnected");
            }
        }

        if (now >= nextTrafficSnapshot)
        {
            nextTrafficSnapshot = now + 1.0;
            try
            {
                Directory.CreateDirectory(RuntimeDirectory);
                var snapshot = new JObject { ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 };
                foreach (SlotState slot in slots)
                    snapshot[slot.Id] = new JObject { ["packets"] = slot.Packets,
                        ["age"] = slot.HasSample ? now - slot.LatestSample.ReceivedRealtime : -1,
                        ["connected"] = IsChannelOpen(slot) };
                File.WriteAllText(Path.Combine(RuntimeDirectory, "phone-traffic.json"), snapshot.ToString(Formatting.None));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        UpdateOverlay();
    }

    public bool IsConnected(ControllerSlot controllerSlot)
    {
        return IsChannelOpen(GetSlot(controllerSlot));
    }

    public bool IsStale(ControllerSlot controllerSlot)
    {
        SlotState slot = GetSlot(controllerSlot);
        return !IsChannelOpen(slot) || !slot.HasSample ||
               Time.realtimeSinceStartupAsDouble - slot.LatestSample.ReceivedRealtime > StaleAfterSeconds;
    }

    public bool GuardHeld(ControllerSlot controllerSlot)
    {
        SlotState slot = GetSlot(controllerSlot);
        return !IsStale(controllerSlot) && slot.LatestSample.GuardHeld;
    }

    public bool TryGetLatestSample(ControllerSlot controllerSlot, out MotionSample sample)
    {
        SlotState slot = GetSlot(controllerSlot);
        sample = slot.LatestSample;
        return slot.HasSample;
    }

    public int GetRecenterVersion(ControllerSlot controllerSlot)
    {
        return GetSlot(controllerSlot).RecenterVersion;
    }

    public int GetConnectionGeneration(ControllerSlot controllerSlot)
        => GetSlot(controllerSlot).ConnectionGeneration;

    public void Recenter(ControllerSlot controllerSlot)
    {
        GetSlot(controllerSlot).RecenterVersion++;
    }

    public void SendFeedback(ControllerSlot controllerSlot, float seconds)
    {
        SlotState slot = GetSlot(controllerSlot);
        if (!IsChannelOpen(slot))
            return;

        int durationMs = Mathf.Clamp(Mathf.RoundToInt(seconds * 1000f), 1, 1000);
        byte[] message = Encoding.UTF8.GetBytes($"{{\"type\":\"vibrate\",\"durationMs\":{durationMs}}}");
        try
        {
            slot.Channel.Send(message);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Phone vibration send failed: {exception.Message}", this);
        }
    }

    public void RotateSlot(ControllerSlot controllerSlot)
    {
        SlotState slot = GetSlot(controllerSlot);
        _ = SendSignalAsync(new { type = "slot.rotate", slot = slot.Id }, cancellation.Token);
        slot.Status = "Rotating QR...";
    }

    private async Task RunSignalingLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var completion = new TaskCompletionSource<PhoneServerEndpoint>();
            using (token.Register(() => completion.TrySetCanceled()))
            {
                StartCoroutine(discovery.Find((endpoint, error) => {
                    if (error != null) Debug.LogWarning(error, this);
                    completion.TrySetResult(endpoint);
                }));
                PhoneServerEndpoint target;
                try { target = await completion.Task; }
                catch (OperationCanceledException) { break; }
                if (target == null)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(2), token); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                ClientWebSocket socket = new ClientWebSocket();
                try
                {
                    using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        connectTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                        await socket.ConnectAsync(new Uri(target.Url), connectTimeout.Token);
                    }
                    signalingSocket = socket;
                    await SendSignalAsync(new { type = "host.create", hostKey = target.HostKey }, token);
                    await ReceiveSignalingAsync(socket, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Phone controller signaling reconnect: {exception.Message}");
                }
                finally
                {
                    if (signalingSocket == socket)
                    {
                        signalingSocket = null;
                    }
                    socket.Dispose();
                }

                receivedSignals.Enqueue("{\"type\":\"host.disconnected\"}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ReceiveSignalingAsync(ClientWebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            using (MemoryStream stream = new MemoryStream())
            using (var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                // Parent heartbeat detects a silent LAN disconnect and triggers discovery.
                receiveTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), receiveTimeout.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }
                    stream.Write(buffer, 0, result.Count);
                    if (stream.Length > 262144)
                    {
                        throw new InvalidDataException("Signaling message is too large");
                    }
                } while (!result.EndOfMessage);

                receivedSignals.Enqueue(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
    }

    private async Task SendSignalAsync(object message, CancellationToken token)
    {
        ClientWebSocket socket = signalingSocket;
        if (socket == null || socket.State != WebSocketState.Open)
        {
            return;
        }

        bool lockTaken = false;
        try
        {
            await sendLock.WaitAsync(token);
            lockTaken = true;
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Phone controller signaling send failed: {exception.Message}");
        }
        finally
        {
            if (lockTaken)
            {
                sendLock.Release();
            }
        }
    }

    private void HandleSignalingMessage(string json)
    {
        JObject message;
        try
        {
            message = JObject.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        string type = message.Value<string>("type");
        switch (type)
        {
            case "session.created":
                HandleSessionCreated(message);
                break;
            case "peer.joined":
            {
                SlotState slot = GetSlot(message.Value<string>("slot"));
                if (slot != null && (registrationSlot == Array.IndexOf(slots, slot) || slot.AcceptedPhoneId.Length > 0))
                {
                    ClosePeer(slot, "Phone joined");
                    slot.Status = "Negotiating direct LAN link...";
                    slot.ConnectionDeadline = Time.realtimeSinceStartupAsDouble + ConnectionTimeoutSeconds;
                    StartCoroutine(CreateOffer(slot));
                }
                break;
            }
            case "rtc.answer":
            {
                SlotState slot = GetSlot(message.Value<string>("slot"));
                string sdp = message.Value<string>("sdp");
                if (slot?.Peer != null && !string.IsNullOrEmpty(sdp))
                {
                    StartCoroutine(ApplyAnswer(slot, sdp));
                }
                break;
            }
            case "rtc.candidate":
                HandleRemoteCandidate(message);
                break;
            case "peer.left":
            {
                SlotState slot = GetSlot(message.Value<string>("slot"));
                if (slot != null)
                {
                    // The phone's signaling socket can disappear while its direct
                    // LAN DataChannel is still healthy. The DataChannel close/error
                    // callback remains the authority once WebRTC is established.
                    if (IsChannelOpen(slot))
                    {
                        slot.Status = "Connected directly (signaling unavailable)";
                    }
                    else
                    {
                        ClosePeer(slot, "Waiting for phone");
                    }
                }
                break;
            }
            case "slot.rotated":
            {
                SlotState slot = GetSlot(message.Value<string>("slot"));
                if (slot != null)
                {
                    if (slot.PreserveRotations == 0) ClosePeer(slot, "Waiting for new QR scan");
                    else slot.PreserveRotations--;
                    ApplySlotPresentation(slot, message);
                }
                break;
            }
            case "host.disconnected":
                // Signaling only bootstraps WebRTC. An established LAN data channel
                // must keep working if the VPS or internet path briefly disappears.
                break;
            case "error":
                Debug.LogWarning(message.Value<string>("message") ?? "Phone controller signaling error");
                break;
        }
    }

    private void HandleSessionCreated(JObject message)
    {
        for (int index = 0; index < slots.Length; index++)
        {
            if (!IsChannelOpen(slots[index]))
            {
                ClosePeer(slots[index], "Waiting for QR scan");
            }
        }

        JObject slotObjects = message["slots"] as JObject;
        if (slotObjects == null)
        {
            return;
        }

        ApplySlotPresentation(slots[0], slotObjects["p1"] as JObject);
        ApplySlotPresentation(slots[1], slotObjects["p2"] as JObject);
    }

    private void ApplySlotPresentation(SlotState slot, JObject data)
    {
        if (data == null)
        {
            return;
        }

        slot.JoinUrl = data.Value<string>("joinUrl") ?? string.Empty;
        string base64 = data.Value<string>("qrPngBase64");
        if (string.IsNullOrWhiteSpace(base64))
        {
            return;
        }

        int comma = base64.IndexOf(',');
        if (comma >= 0)
        {
            base64 = base64.Substring(comma + 1);
        }

        try
        {
            byte[] png = Convert.FromBase64String(base64);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            if (!texture.LoadImage(png, false))
            {
                Destroy(texture);
                return;
            }

            if (slot.QrTexture != null)
            {
                Destroy(slot.QrTexture);
            }
            slot.QrTexture = texture;
        }
        catch (FormatException)
        {
            Debug.LogWarning($"Invalid QR image received for {slot.Id}");
        }
    }

    private IEnumerator CreateOffer(SlotState slot)
    {
        RTCConfiguration rtcConfiguration = default;
        rtcConfiguration.iceServers = new[]
        {
            new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } }
        };
        RTCPeerConnection peer = new RTCPeerConnection(ref rtcConfiguration);
        slot.Peer = peer;
        slot.RemoteDescriptionSet = false;
        slot.PendingCandidates.Clear();

        peer.OnIceCandidate = candidate =>
        {
            if (slot.Peer != peer || candidate == null)
            {
                return;
            }

            _ = SendSignalAsync(new
            {
                type = "rtc.candidate",
                slot = slot.Id,
                candidate = new
                {
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMLineIndex
                }
            }, cancellation.Token);
        };

        peer.OnIceConnectionChange = state =>
        {
            if (slot.Peer != peer)
            {
                return;
            }

            slot.IceConnectionState = state;
            if (state == RTCIceConnectionState.Disconnected)
            {
                slot.Status = "LAN link interrupted";
                slot.DisconnectDeadline = Time.realtimeSinceStartupAsDouble + DisconnectGraceSeconds;
            }
            else if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
            {
                slot.DisconnectDeadline = 0;
            }
            else if (state == RTCIceConnectionState.Failed || state == RTCIceConnectionState.Closed)
            {
                slot.Status = $"LAN link {state}";
                slot.CloseRequested = true;
            }
        };

        RTCDataChannelInit channelOptions = new RTCDataChannelInit
        {
            ordered = false,
            maxRetransmits = 0,
            protocol = "motion-v1"
        };
        RTCDataChannel channel = peer.CreateDataChannel("motion-v1", channelOptions);
        slot.Channel = channel;
        slot.ChannelOpen = false;
        channel.OnOpen = () =>
        {
            if (slot.Peer != peer || slot.Channel != channel)
            {
                return;
            }

            // Identity arrives over the actual data channel, not the signaling socket.
            slot.Status = "スマホを確認しています";
            slot.ConnectionDeadline = 0;
            slot.DisconnectDeadline = 0;
        };
        channel.OnClose = () =>
        {
            if (slot.Peer != peer || slot.Channel != channel)
            {
                return;
            }

            slot.ChannelOpen = false;
            slot.Status = "Phone disconnected";
            slot.CloseRequested = true;
        };
        channel.OnMessage = bytes =>
        {
            if (slot.Peer == peer && slot.Channel == channel)
            {
                HandleDataChannelMessage(slot, bytes);
            }
        };

        RTCSessionDescriptionAsyncOperation offerOperation = peer.CreateOffer();
        yield return offerOperation;
        if (slot.Peer != peer)
        {
            yield break;
        }
        if (offerOperation.IsError)
        {
            slot.Status = $"Offer failed: {offerOperation.Error.message}";
            slot.CloseRequested = true;
            yield break;
        }

        RTCSessionDescription offer = offerOperation.Desc;
        RTCSetSessionDescriptionAsyncOperation localOperation = peer.SetLocalDescription(ref offer);
        yield return localOperation;
        if (slot.Peer != peer)
        {
            yield break;
        }
        if (localOperation.IsError)
        {
            slot.Status = $"Local SDP failed: {localOperation.Error.message}";
            slot.CloseRequested = true;
            yield break;
        }

        _ = SendSignalAsync(new { type = "rtc.offer", slot = slot.Id, sdp = offer.sdp }, cancellation.Token);
    }

    private IEnumerator ApplyAnswer(SlotState slot, string sdp)
    {
        RTCPeerConnection peer = slot.Peer;
        if (peer == null)
        {
            yield break;
        }

        RTCSessionDescription answer = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = sdp
        };
        RTCSetSessionDescriptionAsyncOperation operation = peer.SetRemoteDescription(ref answer);
        yield return operation;
        if (slot.Peer != peer)
        {
            yield break;
        }
        if (operation.IsError)
        {
            slot.Status = $"Remote SDP failed: {operation.Error.message}";
            slot.CloseRequested = true;
            yield break;
        }

        slot.RemoteDescriptionSet = true;
        foreach (RTCIceCandidateInit pending in slot.PendingCandidates)
        {
            AddRemoteCandidate(peer, pending);
        }
        slot.PendingCandidates.Clear();
    }

    private void HandleRemoteCandidate(JObject message)
    {
        SlotState slot = GetSlot(message.Value<string>("slot"));
        JObject candidateObject = message["candidate"] as JObject;
        string candidateText = candidateObject?.Value<string>("candidate");
        if (slot?.Peer == null || string.IsNullOrEmpty(candidateText))
        {
            return;
        }

        RTCIceCandidateInit candidate = new RTCIceCandidateInit
        {
            candidate = candidateText,
            sdpMid = candidateObject.Value<string>("sdpMid"),
            sdpMLineIndex = candidateObject["sdpMLineIndex"]?.Value<int?>()
        };

        if (slot.RemoteDescriptionSet)
        {
            AddRemoteCandidate(slot.Peer, candidate);
        }
        else
        {
            slot.PendingCandidates.Add(candidate);
        }
    }

    private static void AddRemoteCandidate(RTCPeerConnection peer, RTCIceCandidateInit candidateInit)
    {
        using (RTCIceCandidate candidate = new RTCIceCandidate(candidateInit))
        {
            peer.AddIceCandidate(candidate);
        }
    }

    private void HandleDataChannelMessage(SlotState slot, byte[] bytes)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (bytes != null && bytes.Length > 0 && bytes.Length <= 1024 && bytes[0] == (byte)'{')
        {
            try
            {
                JObject message = JObject.Parse(Encoding.UTF8.GetString(bytes));
                string type = message.Value<string>("type");
                if (type == "controller.hello")
                {
                    string id = message.Value<string>("deviceId") ?? "";
                    if (id.Length < 8 || id.Length > 80) return;
                    bool enrolling = registrationSlot == Array.IndexOf(slots, slot) && slot.ConnectionGeneration != registrationGeneration;
                    if (!enrolling && id != slot.AcceptedPhoneId) { ClosePeer(slot, "登録待ちの画面から接続してください"); return; }
                    slot.PhoneId = id; slot.ChannelOpen = true; slot.RegistrationCandidate = enrolling;
                    slot.ConnectionDeadline = 0; slot.Status = "スマホがつながりました";
                    SendControl(slot, new { type = "controller.ready" });
                }
                if ((type == "controller.hello" || type == "axis.state") && slot.ChannelOpen)
                {
                    int order = message.Value<int?>("order") ?? -1, signs = message.Value<int?>("signs") ?? -1;
                    if (order >= 0 && order < 6 && signs >= 0 && signs < 8)
                    {
                        slot.AxisOrder = order; slot.AxisSigns = signs;
                        int request = message.Value<int?>("request") ?? 0;
                        if (request > 0 && request == slot.PendingAxisRevision && order == slot.PendingOrder && signs == slot.PendingSigns)
                        {
                            slot.AxisRevision = request; slot.PendingAxisRevision = 0; slot.RecenterVersion++; slot.HasSample = false;
                            uint? next = message.Value<uint?>("nextSequence");
                            slot.HasAxisSequenceFloor = next.HasValue; slot.AxisSequenceFloor = next ?? 0;
                        }
                    }
                }
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is ArgumentException) { }
            return;
        }
        if (!slot.ChannelOpen || !MotionPacketCodec.TryDecodeMotion(bytes, now, out MotionSample sample))
        {
            return;
        }
        if (slot.HasAxisSequenceFloor)
        {
            if (sample.Sequence != slot.AxisSequenceFloor && !MotionPacketCodec.IsNewerSequence(sample.Sequence, slot.AxisSequenceFloor)) return;
            slot.HasAxisSequenceFloor = false;
        }
        if (slot.HasSequence && !MotionPacketCodec.IsNewerSequence(sample.Sequence, slot.LatestSequence))
        {
            return;
        }

        slot.Packets++;
        slot.LatestSample = sample;
        slot.LatestSequence = sample.Sequence;
        slot.HasSample = true;
        slot.HasSequence = true;

        if (!slot.HasRecenterSequence)
        {
            slot.LatestRecenterSequence = sample.RecenterSequence;
            slot.HasRecenterSequence = true;
        }
        else if (MotionPacketCodec.IsNewerRecenterSequence(sample.RecenterSequence, slot.LatestRecenterSequence))
        {
            slot.LatestRecenterSequence = sample.RecenterSequence;
            // GameFlow recenters on a short press, including in menus.
        }
    }

    private void ClosePeer(SlotState slot, string status)
    {
        slot.ConnectionGeneration++;
        slot.Status = status;
        slot.ChannelOpen = false;
        slot.PhoneId = ""; slot.RegistrationCandidate = false;
        slot.PendingAxisRevision = 0;
        slot.CloseRequested = false;
        slot.ConnectionDeadline = 0;
        slot.DisconnectDeadline = 0;
        slot.IceConnectionState = RTCIceConnectionState.New;
        slot.HasSample = false;
        slot.HasSequence = false;
        slot.HasAxisSequenceFloor = false;
        slot.LatestRecenterSequence = 0;
        slot.HasRecenterSequence = false;
        slot.RecenterVersion++;
        slot.RemoteDescriptionSet = false;
        slot.PendingCandidates.Clear();

        if (slot.Channel != null)
        {
            slot.Channel.OnOpen = null;
            slot.Channel.OnClose = null;
            slot.Channel.OnMessage = null;
            slot.Channel.Dispose();
            slot.Channel = null;
        }
        if (slot.Peer != null)
        {
            slot.Peer.OnIceCandidate = null;
            slot.Peer.OnIceConnectionChange = null;
            slot.Peer.Dispose();
            slot.Peer = null;
        }
    }

    private static bool IsChannelOpen(SlotState slot)
    {
        return slot.ChannelOpen;
    }

    private SlotState GetSlot(ControllerSlot slot)
    {
        return slots[(int)slot];
    }

    private SlotState GetSlot(string slotId)
    {
        if (string.Equals(slotId, "p1", StringComparison.OrdinalIgnoreCase))
        {
            return slots[0];
        }
        if (string.Equals(slotId, "p2", StringComparison.OrdinalIgnoreCase))
        {
            return slots[1];
        }
        return null;
    }

    private void CreateOverlay()
    {
        GameObject canvasObject = new GameObject("Phone Controller Overlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        overlay = canvasObject;
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1220;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindAnyObjectByType<EventSystem>() == null)
        {
            new GameObject("Phone Controller EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule))
                .transform.SetParent(transform, false);
        }

        CreateSlotCard(canvasObject.transform, 0, new Vector2(-225, 220), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1));
        CreateSlotCard(canvasObject.transform, 1, new Vector2(225, 220), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1));
    }

    private void CreateSlotCard(Transform parent, int index, Vector2 position, Vector2 anchor, Vector2 pivot)
    {
        GameObject card = new GameObject($"{slots[index].Id.ToUpperInvariant()} Controller", typeof(RectTransform));
        card.transform.SetParent(parent, false);
        slotCards[index] = card;
        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = anchor;
        cardRect.anchorMax = anchor;
        cardRect.pivot = pivot;
        cardRect.anchoredPosition = position;
        cardRect.sizeDelta = new Vector2(370, 330);

        GameObject qrObject = new GameObject("QR", typeof(RawImage));
        qrObject.transform.SetParent(card.transform, false);
        RawImage qr = qrObject.GetComponent<RawImage>();
        qr.color = Color.white;
        qr.raycastTarget = false;
        qr.rectTransform.anchorMin = new Vector2(0.5f, 1);
        qr.rectTransform.anchorMax = new Vector2(0.5f, 1);
        qr.rectTransform.pivot = new Vector2(0.5f, 1);
        qr.rectTransform.anchoredPosition = new Vector2(0, -60);
        qr.rectTransform.sizeDelta = new Vector2(250, 250);
        views[index].QrImage = qr;
    }

    private void UpdateOverlay()
    {
        for (int index = 0; index < slots.Length; index++)
        {
            SlotState slot = slots[index];
            SlotView view = views[index];
            if (view.QrImage != null && view.QrImage.texture != slot.QrTexture)
            {
                view.QrImage.texture = slot.QrTexture;
            }
        }
    }

    private void OnDestroy()
    {
        discovery.Dispose();
        if (instance == this)
        {
            instance = null;
        }

        cancellation?.Cancel();
        ClosePeer(slots[0], "Stopped");
        ClosePeer(slots[1], "Stopped");
        signalingSocket?.Dispose();

        foreach (SlotState slot in slots)
        {
            if (slot.QrTexture != null)
            {
                Destroy(slot.QrTexture);
            }
        }
    }
}
