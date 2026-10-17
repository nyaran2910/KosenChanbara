using System;
using System.Collections.Generic;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class ControllerRegistrationPanel : MonoBehaviour
{
    public enum Step { Player, Waiting, Order, Signs, Saving, Restoring }
    public Step CurrentStep { get; private set; }
    public bool IsOpen => canvas != null && canvas.activeSelf;
    private GameObject canvas, page;
    private Font font;
    private MotionController[] sensors;
    private PhoneControllerHub phone;
    private HardwareControllerHub hardware;
    private Action closed;
    private readonly List<Button> buttons = new List<Button>();
    private readonly bool[] held = new bool[2];
    private readonly MenuGestureNavigation[] gestures = { new MenuGestureNavigation(), new MenuGestureNavigation() };
    private readonly AxisDirectionMonitor direction = new AxisDirectionMonitor();
    private int player, revision, order, signs, originalOrder, originalSigns;
    private double inputAfter;
    private RawImage qr;
    private Text feedback;
    private RectTransform sword;
    private readonly List<Button> choices = new List<Button>();
    private static readonly string[] Orders = { "XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX" };
    private static readonly Color Ink = new Color(.10f, .24f, .34f), Pale = new Color(.90f, .96f, 1f), Blue = new Color(.08f, .57f, .86f);
    private ControllerSlot Slot => (ControllerSlot)player;
    public void Initialize(MotionController[] inputs, Font japaneseFont, Action onClosed)
    {
        sensors = inputs; font = japaneseFont; closed = onClosed;
        phone = PhoneControllerHub.EnsureInstance(); hardware = HardwareControllerHub.EnsureInstance();
        canvas = new GameObject("Controller registration", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.transform.SetParent(transform, false);
        Canvas c = canvas.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 1225;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        canvas.SetActive(false);
    }
    public void Open()
    {
        canvas.SetActive(true); phone.SetOverlayVisible(false);
        for (int i = 0; i < 2; i++) held[i] = sensors[i] != null && sensors[i].GuardHeld;
        ShowPlayers();
    }
    public void Close()
    {
        if (!IsOpen) return;
        if (CurrentStep == Step.Order || CurrentStep == Step.Signs)
            phone.ConfigureAxes(Slot, originalOrder, originalSigns, "axis.cancel");
        hardware.CancelPairing(); phone.EndRegistration(false);
        canvas.SetActive(false); closed?.Invoke();
    }
    private void Finish(bool smartphone)
    {
        hardware.CancelPairing(); phone.EndRegistration(smartphone);
        canvas.SetActive(false); closed?.Invoke();
    }
    public void ShowPlayers()
    {
        hardware.CancelPairing(); phone.EndRegistration(false);
        CurrentStep = Step.Player; Build("コントローラ", "プレイヤーを選んでください");
        Add("P1", new Vector2(-240, 0), () => SelectPlayer(0));
        Add("P2", new Vector2(240, 0), () => SelectPlayer(1));
        Add("もどる", new Vector2(0, -370), Close); SelectFirst();
    }
    public void SelectPlayer(int index)
    {
        if (index < 0 || index > 1) return;
        player = index; revision = hardware.PairingRevision;
        hardware.BeginPairing(Slot); phone.BeginRegistration(Slot);
        CurrentStep = Step.Waiting;
        Build("P" + (player + 1), "QRを読み込む、またはコントローラの\nガードとリセットを1秒間同時押ししてください");
        Place(feedback.rectTransform, new Vector2(0, 260), new Vector2(1500, 120));
        var image = new GameObject("QR", typeof(RectTransform), typeof(RawImage)); image.transform.SetParent(page.transform, false);
        qr = image.GetComponent<RawImage>(); qr.raycastTarget = false; Place(qr.rectTransform, new Vector2(0, -20), new Vector2(320, 320));
        Add("もどる", new Vector2(0, -370), ShowPlayers); SelectFirst();
    }
    private void ShowOrder()
    {
        CurrentStep = Step.Order; Build("P" + (player + 1) + "　軸の組み合わせ", "スマホを動かして、剣の動きが合う組み合わせを選んでください");
        for (int i = 0; i < Orders.Length; i++)
        {
            int option = i;
            choices.Add(Add(Orders[i], new Vector2(-540 + (i % 3) * 350, 110 - (i / 3) * 130), () => ChooseOrder(option), 300));
        }
        Add("次へ", new Vector2(0, -260), ShowSigns);
        Add("もどる", new Vector2(0, -370), CancelAdjustment);
        BuildSword(); Highlight(); SelectFirst();
    }
    public void ChooseOrder(int value)
    {
        if (value < 0 || value > 5) return;
        order = value; phone.ConfigureAxes(Slot, order, signs); direction.Reset(); Highlight();
    }
    private void ShowSigns()
    {
        CurrentStep = Step.Signs; Build("P" + (player + 1) + "　軸の向き", "X・Y・Zそれぞれの正転／反転を選んでください");
        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            choices.Add(Add("", new Vector2(-330, 150 - i * 120), () => ToggleAxis(axis), 540));
        }
        Add("完了", new Vector2(0, -260), SaveAdjustment);
        Add("もどる", new Vector2(0, -370), ShowOrder);
        BuildSword(); Highlight(); SelectFirst();
    }
    public void ToggleAxis(int axis)
    {
        if (axis < 0 || axis > 2) return;
        signs ^= 1 << axis; phone.ConfigureAxes(Slot, order, signs); direction.Reset(); Highlight();
    }
    private void Highlight()
    {
        if (CurrentStep == Step.Order)
            for (int i = 0; i < choices.Count; i++) choices[i].image.color = i == order ? new Color(.6f, .85f, 1f) : Pale;
        if (CurrentStep == Step.Signs)
            for (int i = 0; i < choices.Count; i++) choices[i].GetComponentInChildren<Text>().text = "XYZ"[i] + "　" + ((signs & (1 << i)) == 0 ? "正転" : "反転");
    }
    public void SaveAdjustment()
    {
        phone.ConfigureAxes(Slot, order, signs, "axis.commit");
        CurrentStep = Step.Saving; Build("P" + (player + 1), "設定を保存しています");
    }
    private void CancelAdjustment()
    {
        phone.ConfigureAxes(Slot, originalOrder, originalSigns, "axis.cancel");
        CurrentStep = Step.Restoring; Build("P" + (player + 1), "設定を戻しています");
    }
    private void Back()
    {
        if (CurrentStep == Step.Player) Close();
        else if (CurrentStep == Step.Waiting) ShowPlayers();
        else if (CurrentStep == Step.Signs) ShowOrder();
        else if (CurrentStep == Step.Order) CancelAdjustment();
    }
    private void Update()
    {
        if (!IsOpen) return;
        Keyboard keys = Keyboard.current;
        if (keys != null && keys.escapeKey.wasPressedThisFrame) { Back(); return; }
        if (CurrentStep == Step.Waiting)
        {
            qr.texture = phone.GetQrTexture(Slot); qr.enabled = qr.texture != null;
            if (hardware.PairingRevision != revision && hardware.CompletedPairingSlot == player) { Finish(false); return; }
            if (phone.RegistrationReady(Slot) && phone.TryGetLatestSample(Slot, out MotionSample arrived) && arrived.OrientationValid)
            {
                hardware.CancelPairing(); originalOrder = order = phone.GetAxisOrder(Slot); originalSigns = signs = phone.GetAxisSigns(Slot);
                sensors[player]?.Recenter(); ShowOrder(); return;
            }
        }
        else if (CurrentStep != Step.Player)
        {
            if (!phone.IsConnected(Slot)) { ShowPlayers(); return; }
            if (CurrentStep == Step.Saving && phone.AxisApplied(Slot)) { sensors[player]?.Recenter(); Finish(true); return; }
            if (CurrentStep == Step.Restoring && phone.AxisApplied(Slot)) { ShowPlayers(); return; }
            TickSword();
        }
        bool confirm = false; Vector2Int move = Vector2Int.zero;
        for (int i = 0; i < 2; i++)
        {
            MotionController sensor = sensors[i]; bool guard = sensor != null && sensor.GuardHeld;
            confirm |= guard && !held[i]; held[i] = guard;
            MotionSample sample = default;
            bool valid = sensor != null && sensor.IsPoseValid && sensor.TryGetMenuSample(out sample);
            Vector2Int flick = gestures[i].Read(sample.Rotation, sample.AngularVelocityDegrees, sample.GyroscopeValid, valid, guard, valid ? sensor.PoseGeneration : -1,
                valid ? sample.BrowserTimestampMilliseconds / 1000d : double.NaN, sensor != null && sensor.ActiveSource == ControllerInputSource.Phone);
            if (flick != Vector2Int.zero) move = flick;
        }
        if (keys != null)
        {
            if (keys.upArrowKey.wasPressedThisFrame || keys.wKey.wasPressedThisFrame) move = Vector2Int.up;
            if (keys.downArrowKey.wasPressedThisFrame || keys.sKey.wasPressedThisFrame) move = Vector2Int.down;
            if (keys.leftArrowKey.wasPressedThisFrame || keys.aKey.wasPressedThisFrame) move = Vector2Int.left;
            if (keys.rightArrowKey.wasPressedThisFrame || keys.dKey.wasPressedThisFrame) move = Vector2Int.right;
            confirm |= keys.enterKey.wasPressedThisFrame || keys.leftShiftKey.wasPressedThisFrame || keys.rightShiftKey.wasPressedThisFrame;
        }
        if (Time.realtimeSinceStartupAsDouble >= inputAfter)
        {
            if (move != Vector2Int.zero) Navigate(move);
            if (confirm && CurrentStep != Step.Waiting)
            {
                Button selected = EventSystem.current?.currentSelectedGameObject?.GetComponent<Button>();
                if (selected != null && buttons.Contains(selected)) selected.onClick.Invoke();
            }
        }
        foreach (Button button in buttons) button.GetComponent<Outline>().enabled = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == button.gameObject;
    }
    private void BuildSword()
    {
        var blade = new GameObject("Sword motion", typeof(RectTransform), typeof(Image)); blade.transform.SetParent(page.transform, false);
        blade.GetComponent<Image>().color = Blue; blade.GetComponent<Image>().raycastTarget = false;
        sword = blade.GetComponent<RectTransform>(); Place(sword, new Vector2(650, 70), new Vector2(18, 210)); sword.pivot = new Vector2(.5f, 0);
        feedback.text = "剣先の動きを確認してください"; Place(feedback.rectTransform, new Vector2(600, -130), new Vector2(650, 120));
    }
    private void TickSword()
    {
        if (sword == null || sensors[player] == null) return;
        MotionController sensor = sensors[player];
        // The UI blade extends along +Y; the game blade extends along +Z.
        // Preserve the full calibrated pose, including tilt and twist.
        sword.localRotation = sensor.CurrentLocalRotation * Quaternion.Euler(90f, 0f, 0f);
        MotionSample sample = default; bool fresh = sensor.IsPoseValid && sensor.TryGetMenuSample(out sample);
        // Inspect configured local axes here. The phone world-frame menu
        // conversion cancels axis remapping and hides the adjustment.
        Vector2Int movement = direction.Read(sample.Rotation, sample.AngularVelocityDegrees, sample.GyroscopeValid, fresh, fresh ? sample.BrowserTimestampMilliseconds / 1000d : double.NaN);
        if (movement != Vector2Int.zero) feedback.text = movement == Vector2Int.up ? "↑ 上" : movement == Vector2Int.down ? "↓ 下" : movement == Vector2Int.left ? "← 左" : "→ 右";
    }
    private void Build(string title, string message)
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (page != null) { page.SetActive(false); Destroy(page); }
        buttons.Clear(); choices.Clear(); sword = null; qr = null;
        foreach (MenuGestureNavigation gesture in gestures) gesture.Reset(); direction.Reset();
        inputAfter = Time.realtimeSinceStartupAsDouble + .25;
        page = new GameObject("Registration page", typeof(RectTransform), typeof(Image)); page.transform.SetParent(canvas.transform, false);
        var rect = page.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        page.GetComponent<Image>().color = new Color(.97f, .99f, 1f);
        Label(title, new Vector2(0, 410), new Vector2(1600, 70), 38);
        feedback = Label(message, new Vector2(0, 300), new Vector2(1600, 90), 28);
    }
    private Text Label(string text, Vector2 position, Vector2 size, int sizeFont)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(page.transform, false);
        Text t = go.GetComponent<Text>(); t.font = font; t.fontSize = sizeFont; t.color = Ink; t.text = text; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
        Place(t.rectTransform, position, size); return t;
    }
    private Button Add(string title, Vector2 position, Action action, float width = 420)
    {
        var go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline)); go.transform.SetParent(page.transform, false);
        Place(go.GetComponent<RectTransform>(), position, new Vector2(width, 80)); go.GetComponent<Image>().color = Pale;
        Button b = go.GetComponent<Button>(); b.targetGraphic = go.GetComponent<Image>(); b.navigation = new Navigation { mode = Navigation.Mode.None };
        b.onClick.AddListener(() => { if (Time.realtimeSinceStartupAsDouble >= inputAfter) action(); });
        Text label = Label(title, Vector2.zero, new Vector2(width - 20, 70), 28); label.transform.SetParent(go.transform, false);
        Outline o = go.GetComponent<Outline>(); o.effectColor = Blue; o.effectDistance = new Vector2(3, -3); o.enabled = false;
        buttons.Add(b); return b;
    }
    private static void Place(RectTransform r, Vector2 p, Vector2 size) { r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = p; r.sizeDelta = size; }
    private void SelectFirst() { if (buttons.Count > 0) buttons[0].Select(); }
    private void Navigate(Vector2Int direction)
    {
        Button current = EventSystem.current?.currentSelectedGameObject?.GetComponent<Button>();
        if (current == null || !buttons.Contains(current)) { SelectFirst(); return; }
        Vector2 origin = current.GetComponent<RectTransform>().anchoredPosition; float best = 0; Button next = null;
        foreach (Button b in buttons)
        {
            if (b == current) continue;
            Vector2 offset = b.GetComponent<RectTransform>().anchoredPosition - origin;
            float dot = Vector2.Dot(direction, offset), score = dot / offset.sqrMagnitude;
            if (dot > 0 && score > best) { next = b; best = score; }
        }
        if (next != null) next.Select();
    }
    private void OnDisable() { if (hardware != null) hardware.CancelPairing(); if (phone != null) phone.EndRegistration(false); }
}
