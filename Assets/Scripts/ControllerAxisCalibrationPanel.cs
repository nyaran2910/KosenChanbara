using System;
using System.Collections.Generic;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Axis presets live separately from the basic pause actions.
public sealed class ControllerAxisCalibrationPanel : MonoBehaviour
{
    public enum Step { Player, Presets }
    public bool IsOpen => canvas != null && canvas.activeSelf;
    public Step CurrentStep { get; private set; }
    public Button EntryButton { get; private set; }
    private MotionController[] sensors;
    private Action closed;
    private GameFlowController flow;
    private GameObject entryCanvas;
    private double entryAfter;
    private bool SetupEntryVisible => flow != null && flow.PauseOpen
        && flow.CurrentScreen != GameFlowController.Screen.Playing
        && flow.CurrentScreen != GameFlowController.Screen.Preview;
    private GameObject canvas, page;
    private Font font;
    private Text instruction, feedback;
    private readonly List<Button> buttons = new List<Button>();
    private readonly List<Button> playerButtons = new List<Button>();
    private readonly bool[] guardWasHeld = new bool[2];
    private readonly MenuResetButton reset = new MenuResetButton();
    private readonly AxisDirectionMonitor preview = new AxisDirectionMonitor();
    private readonly List<Button> presetButtons = new List<Button>();
    public int CurrentPreset => sensors[player].AxisMapping.PresetIndex;
    private readonly MenuGestureNavigation[] navigation = { new MenuGestureNavigation(), new MenuGestureNavigation() };
    private int player, session;
    private ControllerInputSource source;
    private double inputAfter;
    private static readonly Color Ink = new Color(.10f, .24f, .34f);
    private static readonly Color Pale = new Color(.90f, .96f, 1f);

    public void Initialize(MotionController[] controllers, Font japaneseFont, Action onClosed)
    {
        sensors = controllers; font = japaneseFont; closed = onClosed;
        canvas = new GameObject("Controller axis calibration", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.transform.SetParent(transform, false);
        Canvas c = canvas.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 1220;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        canvas.SetActive(false);
        flow = GetComponent<GameFlowController>();
        BuildEntry();
    }

    // Calibration belongs to setup before play. The 2026-10-10 gameplay pause
    // explicitly has only three actions plus display choices; retain this
    // Esc setup entry on the title/selection screens without adding a fourth
    // in-game action.
    private void BuildEntry()
    {
        entryCanvas = new GameObject("Axis calibration settings entry", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        entryCanvas.transform.SetParent(transform, false);
        Canvas c = entryCanvas.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 1211;
        CanvasScaler scaler = entryCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        GameObject go = new GameObject("軸合わせ", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(entryCanvas.transform, false);
        RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(60, 300); rect.sizeDelta = new Vector2(220, 55);
        Image image = go.GetComponent<Image>(); image.color = Pale;
        EntryButton = go.GetComponent<Button>(); EntryButton.targetGraphic = image;
        EntryButton.navigation = new Navigation { mode = Navigation.Mode.None };
        EntryButton.onClick.AddListener(() =>
        {
            if (!SetupEntryVisible || Time.realtimeSinceStartupAsDouble < entryAfter) return;
            transform.Find("Connection pause").gameObject.SetActive(false);
            PhoneControllerHub.EnsureInstance().SetOverlayVisible(false);
            Open();
        });
        GameObject label = new GameObject("Label", typeof(RectTransform), typeof(Text)); label.transform.SetParent(go.transform, false);
        Text text = label.GetComponent<Text>(); text.font = font; text.text = "軸合わせ"; text.fontSize = 24;
        text.color = Ink; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        RectTransform labelRect = text.rectTransform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        entryCanvas.SetActive(false);
    }

    public void Open()
    {
        canvas.SetActive(true);
        entryCanvas.SetActive(false);
        for (int i = 0; i < 2; i++)
        { guardWasHeld[i] = sensors[i] != null && sensors[i].GuardHeld; navigation[i].Reset(); }
        ShowPlayers();
    }

    public void Close()
    {
        if (!IsOpen) return;
        canvas.SetActive(false);
        entryCanvas.SetActive(SetupEntryVisible);
        entryAfter = Time.realtimeSinceStartupAsDouble + .2;
        closed?.Invoke();
    }

    private void Update()
    {
        if (!IsOpen) { entryCanvas.SetActive(SetupEntryVisible); return; }
        Keyboard keys = Keyboard.current;
        if (keys != null && keys.escapeKey.wasPressedThisFrame) { Close(); return; }
        if (CurrentStep != Step.Player && !SameInput())
        { ShowPlayers("接続が変わりました。もう一度P1 / P2を選んでください。"); return; }

        bool confirm = false;
        for (int i = 0; i < 2; i++)
        {
            MotionController sensor = sensors[i];
            bool guard = sensor != null && sensor.GuardHeld;
            if (guard && !guardWasHeld[i] && (CurrentStep == Step.Player || i == player)) confirm = true;
            guardWasHeld[i] = guard;
        }
        if (CurrentStep == Step.Player)
        {
            for (int i = 0; i < playerButtons.Count; i++)
            {
                MotionController sensor = sensors[i];
                playerButtons[i].interactable = Fresh(sensor);
                playerButtons[i].GetComponentInChildren<Text>().text = "P" + (i + 1) + "（" + SourceName(sensor) + "）";
            }
        }
        else
        {
            MotionController sensor = sensors[player];
            MenuResetAction action = reset.Read(sensor.IsConnected && !sensor.IsStale,
                sensor.RecenterHeld, sensor.ResetCounter, Time.unscaledTime);
            if (action == MenuResetAction.LongPress) { Close(); return; }
            if (action == MenuResetAction.ShortPress)
            {
                sensor.Recenter();
                preview.Reset();
            }
            TickPreview();
        }

        if (Time.realtimeSinceStartupAsDouble >= inputAfter)
        {
            Vector2Int move = Vector2Int.zero;
            if (keys != null)
            {
                if (keys.upArrowKey.wasPressedThisFrame || keys.wKey.wasPressedThisFrame) move = Vector2Int.up;
                if (keys.downArrowKey.wasPressedThisFrame || keys.sKey.wasPressedThisFrame) move = Vector2Int.down;
                if (keys.leftArrowKey.wasPressedThisFrame || keys.aKey.wasPressedThisFrame) move = Vector2Int.left;
                if (keys.rightArrowKey.wasPressedThisFrame || keys.dKey.wasPressedThisFrame) move = Vector2Int.right;
                confirm |= keys.enterKey.wasPressedThisFrame;
            }
            // Direction feedback stays live while navigation takes one step
            // per swing. Moving focus does not apply a different preset.
            for (int i = 0; i < 2; i++)
            {
                if (CurrentStep == Step.Presets && i != player) continue;
                MotionController sensor = sensors[i];
                bool phone = false; Quaternion reference = Quaternion.identity; MotionSample sample = default;
                bool valid = sensor != null && sensor.IsPoseValid
                    && sensor.TryGetMenuSample(out sample, out phone, out reference);
                Vector2Int flick = navigation[i].Read(sample.Rotation, sample.AngularVelocityDegrees,
                    sample.GyroscopeValid, valid, sample.GuardHeld, valid ? sensor.MenuPoseGeneration : -1,
                    valid ? sample.BrowserTimestampMilliseconds / 1000d : double.NaN, phone, reference);
                if (flick != Vector2Int.zero) move = flick;
            }
            if (move != Vector2Int.zero) Navigate(move);
            if (confirm && EventSystem.current != null)
            {
                GameObject selected = EventSystem.current.currentSelectedGameObject;
                Button button = selected != null ? selected.GetComponent<Button>() : null;
                if (button != null && buttons.Contains(button) && button.IsInteractable()) button.onClick.Invoke();
            }
        }
        Focus();
    }

    private void ShowPlayers(string message = "軸を合わせるプレイヤーを選んでください。")
    {
        CurrentStep = Step.Player;
        Build("軸合わせ", message);
        playerButtons.Clear();
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            Button button = AddButton("P" + (i + 1), new Vector2(i == 0 ? -230 : 230, -30), () => SelectPlayer(index));
            button.interactable = Fresh(sensors[i]); playerButtons.Add(button);
        }
        AddButton("もどる", new Vector2(0, -310), Close);
        SelectFirst();
    }

    public void SelectPlayer(int index)
    {
        if (index < 0 || index >= sensors.Length || !Fresh(sensors[index])) return;
        player = index; source = sensors[player].ActiveSource; session = sensors[player].InputSessionGeneration;
        ShowPresets();
    }

    private void ShowPresets()
    {
        CurrentStep = Step.Presets;
        reset.Read(false, false, 0, 0);
        MotionController sensor = sensors[player];
        reset.Read(true, sensor.RecenterHeld, sensor.ResetCounter, Time.unscaledTime);
        preview.Reset();
        foreach (MenuGestureNavigation gesture in navigation) gesture.Reset();
        Build(Heading(), "6つのメニュー軸を選んで、上下左右に振って試してください。\nゲーム中の剣の動きは変わりません。");
        // Keep the live result above every preset on the same page.
        Place(instruction.rectTransform, new Vector2(0, 195), new Vector2(1500, 110));
        feedback = Label("振った方向：待機", new Vector2(0, 300), new Vector2(1400, 80), 38);
        presetButtons.Clear();
        for (int i = 0; i < ControllerAxisMapping.PresetCount; i++)
        {
            int index = i;
            presetButtons.Add(AddButton((i + 1) + "　" + ControllerAxisMapping.PresetName(i),
                new Vector2(i % 2 == 0 ? -250 : 250, 40 - i / 2 * 110), () => SelectPreset(index)));
        }
        AddButton("P1 / P2を選び直す", new Vector2(-250, -350), () => ShowPlayers());
        AddButton("もどる", new Vector2(250, -350), Close);
        RefreshPresets();
        presetButtons[CurrentPreset].Select();
    }

    public void SelectPreset(int index)
    {
        if (CurrentStep != Step.Presets || !SameInput() || !Fresh(sensors[player])
            || index < 0 || index >= ControllerAxisMapping.PresetCount) return;
        if (CurrentPreset == index) return;
        MotionController sensor = sensors[player];
        sensor.SetMenuAxisMapping(ControllerAxisMapping.FromPreset(index));
        preview.Reset();
        feedback.text = "振った方向：待機";
        RefreshPresets();
    }

    private void RefreshPresets()
    {
        for (int i = 0; i < presetButtons.Count; i++)
        {
            bool selected = i == CurrentPreset;
            presetButtons[i].image.color = selected ? new Color(.65f, .85f, 1f) : Pale;
            presetButtons[i].GetComponentInChildren<Text>().text = (i + 1) + "　"
                + ControllerAxisMapping.PresetName(i) + (selected ? "　✓" : "");
        }
    }

    private void TickPreview()
    {
        MotionController sensor = sensors[player];
        MotionSample sample = default; bool phone = false; Quaternion reference = Quaternion.identity;
        bool valid = Fresh(sensor) && sensor.IsPoseValid
            && sensor.TryGetMenuSample(out sample, out phone, out reference);
        Quaternion rotation = phone ? MenuGestureNavigation.PhoneWorldToMenuWorld * sample.Rotation
            : Quaternion.Inverse(reference) * sample.Rotation;
        Vector2Int direction = preview.Read(rotation, sample.AngularVelocityDegrees,
            sample.GyroscopeValid, valid, valid ? sample.BrowserTimestampMilliseconds / 1000d : double.NaN);
        if (direction != Vector2Int.zero)
            feedback.text = "振った方向：" + (direction == Vector2Int.up ? "↑ 上" : direction == Vector2Int.down ? "↓ 下"
                : direction == Vector2Int.left ? "← 左" : "→ 右");
        else if (!valid) feedback.text = "振った方向：入力を待っています…";
    }

    private bool SameInput() => sensors[player] != null && sensors[player].ActiveSource == source
        && source != ControllerInputSource.None && sensors[player].InputSessionGeneration == session;
    private static bool Fresh(MotionController sensor) => sensor != null && !sensor.IsStale
        && sensor.TryGetLatestSample(out MotionSample sample) && sample.OrientationValid;
    private static string SourceName(MotionController sensor) => sensor == null ? "未接続"
        : sensor.ActiveSource == ControllerInputSource.Phone ? "スマホ"
        : sensor.ActiveSource == ControllerInputSource.Hardware ? "コントローラ" : "未接続";
    private string Heading() => "P" + (player + 1) + "　" + SourceName(sensors[player]) + "の軸合わせ";

    private void Build(string heading, string message)
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (page != null) { page.SetActive(false); Destroy(page); }
        buttons.Clear(); inputAfter = Time.realtimeSinceStartupAsDouble + .2;
        page = new GameObject("Axis calibration page", typeof(RectTransform), typeof(Image));
        page.transform.SetParent(canvas.transform, false);
        RectTransform rect = page.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        page.GetComponent<Image>().color = new Color(.97f, .99f, 1f);
        Label(heading, new Vector2(0, 390), new Vector2(1300, 80), 38);
        instruction = Label(message, new Vector2(0, 180), new Vector2(1500, 300), 30);
        Label("マウス / ↑↓・Enter / ガードで選択　　Escでもどる", new Vector2(0, -470), new Vector2(1500, 45), 20);
    }

    private Text Label(string value, Vector2 position, Vector2 size, int fontSize)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(page.transform, false);
        Text text = go.GetComponent<Text>(); text.font = font; text.text = value; text.fontSize = fontSize;
        text.color = Ink; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        Place(text.rectTransform, position, size); return text;
    }

    private Button AddButton(string title, Vector2 position, Action action)
    {
        GameObject go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        go.transform.SetParent(page.transform, false); Place(go.GetComponent<RectTransform>(), position, new Vector2(420, 80));
        Image image = go.GetComponent<Image>(); image.color = Pale;
        Button button = go.GetComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => { if (Time.realtimeSinceStartupAsDouble >= inputAfter) action(); });
        Text text = Label(title, Vector2.zero, new Vector2(400, 70), 27); text.transform.SetParent(go.transform, false);
        Outline outline = go.GetComponent<Outline>(); outline.effectColor = new Color(.08f, .57f, .86f);
        outline.effectDistance = new Vector2(3, -3); outline.enabled = false;
        buttons.Add(button); return button;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
    private void SelectFirst()
    { foreach (Button button in buttons) if (button.IsInteractable()) { button.Select(); break; } }
    private void Navigate(Vector2Int direction)
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        Button current = selected != null ? selected.GetComponent<Button>() : null;
        if (current == null || !buttons.Contains(current)) { SelectFirst(); return; }
        Vector2 origin = current.GetComponent<RectTransform>().anchoredPosition;
        Button next = null; float best = 0;
        foreach (Button button in buttons)
        {
            if (button == current || !button.IsInteractable()) continue;
            Vector2 offset = button.GetComponent<RectTransform>().anchoredPosition - origin;
            float dot = Vector2.Dot(direction, offset);
            if (dot <= 0) continue;
            float score = dot / offset.sqrMagnitude;
            if (score > best) { best = score; next = button; }
        }
        if (next != null) next.Select();
    }
    private void Focus()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        foreach (Button button in buttons) button.GetComponent<Outline>().enabled = button.gameObject == selected;
    }
}
