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

    [Serializable]
    private sealed class ConnectionConfig
    {
        public string signalingUrl;
        public string hostKey;
    }

    private sealed class SlotState
    {
        public readonly string Id;
        public RTCPeerConnection Peer;
        public RTCDataChannel Channel;
        public readonly List<RTCIceCandidateInit> PendingCandidates = new List<RTCIceCandidateInit>();
        public bool RemoteDescriptionSet;
        public bool CloseRequested;
        public double ConnectionDeadline;
        public double DisconnectDeadline;
        public RTCIceConnectionState IceConnectionState;
        public MotionSample LatestSample;
        public bool HasSample;
        public uint LatestSequence;
        public bool HasSequence;
        public int RecenterVersion;
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
        public Text GuardText;
        public Text StatusText;
    }

    private static PhoneControllerHub instance;
    private readonly SlotState[] slots = { new SlotState("p1"), new SlotState("p2") };
    private readonly SlotView[] views = { new SlotView(), new SlotView() };
    private readonly ConcurrentQueue<string> receivedSignals = new ConcurrentQueue<string>();
    private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

    private CancellationTokenSource cancellation;
    private ClientWebSocket signalingSocket;
    private ConnectionConfig config;
    private GameObject overlay;

    public bool OverlayVisible => overlay != null && overlay.activeSelf;

    public void SetOverlayVisible(bool visible)
    {
        if (overlay != null)
            overlay.SetActive(visible);
        if (!visible && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
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
        config = LoadConfiguration();
        CreateOverlay();
    }

    private void Start()
    {
        StartCoroutine(WebRTC.Update());

        if (config == null || string.IsNullOrWhiteSpace(config.signalingUrl) || string.IsNullOrWhiteSpace(config.hostKey))
        {
            Debug.LogError("Create StreamingAssets/controller-connection.json", this);
            return;
        }

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

    public void Recenter(ControllerSlot controllerSlot)
    {
        GetSlot(controllerSlot).RecenterVersion++;
    }

    public void RotateSlot(ControllerSlot controllerSlot)
    {
        SlotState slot = GetSlot(controllerSlot);
        _ = SendSignalAsync(new { type = "slot.rotate", slot = slot.Id }, cancellation.Token);
        slot.Status = "Rotating QR...";
    }

    private ConnectionConfig LoadConfiguration()
    {
        ConnectionConfig loaded = null;
        string path = Path.Combine(Application.streamingAssetsPath, "controller-connection.json");

        try
        {
            if (File.Exists(path))
            {
                loaded = JsonUtility.FromJson<ConnectionConfig>(File.ReadAllText(path));
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not read phone controller config: {exception.Message}");
        }

        if (loaded == null)
        {
            loaded = new ConnectionConfig();
        }

        string urlOverride = Environment.GetEnvironmentVariable("KOSEN_CONTROLLER_SIGNALING_URL");
        string keyOverride = Environment.GetEnvironmentVariable("KOSEN_CONTROLLER_HOST_KEY");
        if (!string.IsNullOrWhiteSpace(urlOverride))
        {
            loaded.signalingUrl = urlOverride;
        }
        if (!string.IsNullOrWhiteSpace(keyOverride))
        {
            loaded.hostKey = keyOverride;
        }

        return loaded;
    }

    private async Task RunSignalingLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ClientWebSocket socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri(config.signalingUrl), token);
                signalingSocket = socket;
                await SendSignalAsync(new { type = "host.create", hostKey = config.hostKey }, token);
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

    private async Task ReceiveSignalingAsync(ClientWebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
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
                if (slot != null)
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
                    ClosePeer(slot, "Waiting for new QR scan");
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
        channel.OnOpen = () =>
        {
            if (slot.Peer != peer || slot.Channel != channel)
            {
                return;
            }

            slot.Status = "Connected (direct LAN)";
            slot.ConnectionDeadline = 0;
            slot.DisconnectDeadline = 0;
        };
        channel.OnClose = () =>
        {
            if (slot.Peer != peer || slot.Channel != channel)
            {
                return;
            }

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
        if (!MotionPacketCodec.TryDecodeMotion(bytes, now, out MotionSample sample))
        {
            return;
        }
        if (slot.HasSequence && !MotionPacketCodec.IsNewerSequence(sample.Sequence, slot.LatestSequence))
        {
            return;
        }

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
            slot.RecenterVersion++;
            slot.Status = "Recentered from phone";
        }
    }

    private void ClosePeer(SlotState slot, string status)
    {
        slot.Status = status;
        slot.CloseRequested = false;
        slot.ConnectionDeadline = 0;
        slot.DisconnectDeadline = 0;
        slot.IceConnectionState = RTCIceConnectionState.New;
        slot.HasSample = false;
        slot.HasSequence = false;
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
        return slot.Channel != null && slot.Channel.ReadyState == RTCDataChannelState.Open;
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
        canvas.sortingOrder = 1000;
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
        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = anchor;
        cardRect.anchorMax = anchor;
        cardRect.pivot = pivot;
        cardRect.anchoredPosition = position;
        cardRect.sizeDelta = new Vector2(370, 490);

        Text title = CreateText(card.transform, slots[index].Id.ToUpperInvariant(), 32, TextAnchor.MiddleCenter);
        title.rectTransform.anchoredPosition = new Vector2(0, -16);
        title.rectTransform.sizeDelta = new Vector2(340, 44);

        GameObject qrObject = new GameObject("QR", typeof(RawImage));
        qrObject.transform.SetParent(card.transform, false);
        RawImage qr = qrObject.GetComponent<RawImage>();
        qr.color = Color.white;
        qr.rectTransform.anchorMin = new Vector2(0.5f, 1);
        qr.rectTransform.anchorMax = new Vector2(0.5f, 1);
        qr.rectTransform.pivot = new Vector2(0.5f, 1);
        qr.rectTransform.anchoredPosition = new Vector2(0, -60);
        qr.rectTransform.sizeDelta = new Vector2(250, 250);
        views[index].QrImage = qr;

        Text guardText = CreateText(card.transform, "GUARD --", 24, TextAnchor.MiddleCenter);
        guardText.rectTransform.anchoredPosition = new Vector2(0, -325);
        guardText.rectTransform.sizeDelta = new Vector2(340, 44);
        views[index].GuardText = guardText;

        Text status = CreateText(card.transform, "Waiting for QR", 18, TextAnchor.MiddleCenter);
        status.rectTransform.anchoredPosition = new Vector2(0, -370);
        status.rectTransform.sizeDelta = new Vector2(350, 42);
        views[index].StatusText = status;

        Button recenter = CreateButton(card.transform, "RECENTER", new Vector2(-88, -445));
        ControllerSlot capturedSlot = (ControllerSlot)index;
        recenter.onClick.AddListener(() => Recenter(capturedSlot));
        Button rotate = CreateButton(card.transform, "NEW QR", new Vector2(88, -445));
        rotate.onClick.AddListener(() => RotateSlot(capturedSlot));
    }

    private static Text CreateText(Transform parent, string value, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(value, typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.rectTransform.anchorMin = new Vector2(0.5f, 1);
        text.rectTransform.anchorMax = new Vector2(0.5f, 1);
        text.rectTransform.pivot = new Vector2(0.5f, 1);
        return text;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 position)
    {
        GameObject buttonObject = new GameObject(label, typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        Image image = buttonObject.GetComponent<Image>();
        image.color = Color.white;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1);
        rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(158, 54);

        Text text = CreateText(buttonObject.transform, label, 18, TextAnchor.MiddleCenter);
        text.color = Color.black;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        text.rectTransform.anchoredPosition = Vector2.zero;
        text.rectTransform.sizeDelta = Vector2.zero;
        return buttonObject.GetComponent<Button>();
    }

    private void UpdateOverlay()
    {
        for (int index = 0; index < slots.Length; index++)
        {
            SlotState slot = slots[index];
            SlotView view = views[index];
            if (view.StatusText != null)
                view.StatusText.text = slot.Status;
            if (view.QrImage != null && view.QrImage.texture != slot.QrTexture)
            {
                view.QrImage.texture = slot.QrTexture;
            }
            if (view.GuardText != null)
            {
                bool available = IsChannelOpen(slot) && !IsStale((ControllerSlot)index);
                bool guardHeld = available && GuardHeld((ControllerSlot)index);
                view.GuardText.text = !available ? "GUARD --" : guardHeld ? "GUARD ON" : "GUARD OFF";
            }
        }
    }

    private void OnDestroy()
    {
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
