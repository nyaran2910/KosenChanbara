using System;
using System.Collections.Generic;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class DuelPresentation : MonoBehaviour
{
    private const float FlashSeconds = 0.15f;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColor = Shader.PropertyToID("_Color");

    private sealed class FlashRenderer
    {
        public readonly Renderer Renderer;
        public readonly MaterialPropertyBlock Original = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock flash = new MaterialPropertyBlock();

        public FlashRenderer(Renderer renderer)
        {
            Renderer = renderer;
            renderer.GetPropertyBlock(Original);
        }

        public void Show(Color color)
        {
            Renderer.GetPropertyBlock(flash);
            flash.SetColor(BaseColor, color);
            flash.SetColor(LegacyColor, color);
            Renderer.SetPropertyBlock(flash);
        }

        public void Restore()
        {
            if (Renderer != null)
                Renderer.SetPropertyBlock(Original);
        }
    }

    private FlashRenderer[][] bodies;
    private FlashRenderer[][] blades;
    private readonly List<FlashRenderer> flashing = new List<FlashRenderer>();
    private double flashUntil;
    private AudioSource audioSource;
    private AudioClip[] sounds;
    private GameObject winnerCanvas;
    private Text winnerLabel;
    private Text winnerScoreLabel;
    private Button[] duelResultButtons, singleResultButtons;
    private int resultSelection;
    private static Font japaneseFont;
    private static readonly Color Ink = new Color(.16f, .29f, .38f);
    private static readonly Color Sky = new Color(.22f, .65f, .86f);
    public bool ResultVisible => !paused && ((winnerCanvas != null && winnerCanvas.activeSelf)
        || (singleGameOverCanvas != null && singleGameOverCanvas.activeSelf));
    public int ResultSelection => resultSelection;
    private DuelAvatar[] avatars;
    private DuelView[] views;
    private bool menuPresentation;
    private DuelEffects effects;
    private DuelArena duelArena;
    private Transform[] players, swords;
    private AudioSource movementAudio;
    private AudioClip swingSound, stepSound;
    private bool paused;
    private double winnerAt = double.PositiveInfinity;
    private MatchWinner pendingWinner;
    private GameObject singleGameOverCanvas;
    private bool singleGameOverVisible;

    public void Initialize(GameObject firstPlayer, GameObject secondPlayer,
        GameObject firstSword, GameObject secondSword, SpriteRenderer arena, Action rematch, Action resume, MasterController controls)
    {
        bodies = new[] { Collect(firstPlayer, firstSword), Collect(secondPlayer, secondSword) };
        blades = new[] { Collect(firstSword), Collect(secondSword) };
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0.65f;
        sounds = new[]
        {
            CreateSound("Hit", 0.14f, 125f, 0.3f, false),
            CreateSound("Guard", 0.15f, 1500f, 0.08f, true),
            CreateSound("Guard break", 0.13f, 310f, 0.75f, false),
            CreateSound("Clash", 0.12f, 900f, 0.25f, true)
        };
        players = new[] { firstPlayer.transform, secondPlayer.transform };
        swords = new[] { firstSword.transform, secondSword.transform };
        avatars = new[] { new DuelAvatar(players[0], swords[0]), new DuelAvatar(players[1], swords[1]) };
        views = new DuelView[2];
        for (int i = 0; i < 2; i++)
            views[i] = CreateView(i);
        effects = gameObject.AddComponent<DuelEffects>(); effects.Initialize(swords[0], swords[1]);
        duelArena = gameObject.AddComponent<DuelArena>(); duelArena.Initialize(arena);
        movementAudio = gameObject.AddComponent<AudioSource>(); movementAudio.playOnAwake = false; movementAudio.volume = 0.22f;
        swingSound = CreateSwingSound();
        stepSound = CreateSound("Footstep", 0.09f, 75f, 0.12f, false);
        CreateWinnerUI(() => { rematch(); resume(); }, controls);
        CreateSingleGameOverUI(() => { rematch(); resume(); }, controls);
        CreateMatchHud();
    }

    private DuelView CreateView(int index)
    {
        var renderers = new Renderer[bodies[index].Length];
        for (int j = 0; j < renderers.Length; j++) renderers[j] = bodies[index][j].Renderer;
        DuelView view = new DuelView(players[index], players[1 - index], renderers);
        view.SetOwnBodyFade(!menuPresentation);
        return view;
    }

    public void SetMenuPresentation(bool menu)
    {
        menuPresentation=menu;
        if (menu) SetShowdownFade(0f);
        foreach(DuelView view in views)view.SetOwnBodyFade(!menu);
    }

    public void SetLayout(int mode, int viewedPlayer)
    {
        for (int i = 0; i < 2; i++)
            views[i].SetLayout(mode == 1 || i == viewedPlayer,
                mode == 1 ? new Rect(i * 0.5f, 0f, 0.5f, 1f) : new Rect(0f, 0f, 1f, 1f),
                i == (mode == 1 ? 0 : viewedPlayer));
    }

    public void SetFocusTarget(int playerIndex, Transform target)
    {
        if (views != null && playerIndex >= 0 && playerIndex < views.Length)
            views[playerIndex].SetFocusTarget(target);
    }

    public void SetCharacter(int index, GameObject bodyPrefab, GameObject bladePrefab, double now)
    {
        ClearFlash();
        DuelAvatar previousAvatar = avatars[index];
        DuelView previousView = views[index];
        previousView.Dispose();
        // Keep the player and sword roots: cameras, input components and phone
        // assignments continue to use their existing objects.
        foreach (Transform child in players[index])
            if (child != swords[index] && child.name.StartsWith("Chambara"))
            { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        foreach (Transform child in swords[index])
            if (child.name.StartsWith("Chambara"))
            {
                child.gameObject.SetActive(false);
                // Destroy is deferred. Retire the name immediately so the new
                // avatar can attach its glove during the same color change.
                child.name = "Retired " + child.name;
                Destroy(child.gameObject);
            }
        GameObject body = Instantiate(bodyPrefab, players[index], false);
        body.name = bodyPrefab.name; body.transform.localPosition = Vector3.down;
        GameObject blade = Instantiate(bladePrefab, swords[index], false); blade.name = bladePrefab.name;
        bodies[index] = Collect(players[index].gameObject, swords[index].gameObject);
        blades[index] = Collect(swords[index].gameObject);
        avatars[index] = new DuelAvatar(players[index], swords[index]);
        avatars[index].CopyMotionFrom(previousAvatar);
        effects.ReplaceAvatar(previousAvatar, avatars[index]);
        views[index] = CreateView(index);
        views[index].CopyReactionFrom(previousView, now);
        foreach (Renderer renderer in blade.GetComponentsInChildren<Renderer>())
            foreach (Material material in renderer.sharedMaterials)
                if (material.name.StartsWith("Padded_sword"))
                { effects.SetSwordColor(index, material.color); return; }
        Renderer bladeRenderer = blade.GetComponentInChildren<Renderer>();
        if (bladeRenderer != null && bladeRenderer.sharedMaterial != null)
            effects.SetSwordColor(index, bladeRenderer.sharedMaterial.color);
    }

    public void SetStage(GameObject prefab, bool ringOutEnabled = true, bool baseballField = false, bool replacePlatform = false,
        GameObject backgroundPrefab = null)
        => duelArena.SetStage(prefab, ringOutEnabled, baseballField, replacePlatform, backgroundPrefab);

    private static FlashRenderer[] Collect(GameObject root, GameObject exclude = null)
    {
        var renderers = new List<FlashRenderer>();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (renderer.enabled && (exclude == null || !renderer.transform.IsChildOf(exclude.transform)))
                renderers.Add(new FlashRenderer(renderer));
        }
        return renderers.ToArray();
    }

    public void Play(CombatResult result, double now, Vector3 displacement)
        => Play(result, now, displacement, displacement);

    public void Play(CombatResult result, double now, Vector3 displacement, Vector3 worldCutDirection)
        => PlayAt(result, now, displacement, worldCutDirection, ContactPoint(result));

    public void PlayAt(CombatResult result, double now, Vector3 displacement, Vector3 worldCutDirection, Vector3 contactPoint)
    {
        ClearFlash();
        int sound;
        switch (result.Outcome)
        {
            case CombatOutcome.Hit:
                Flash(bodies[result.PushedPlayer], Color.white);
                sound = 0;
                break;
            case CombatOutcome.GuardSuccess:
                sound = 1;
                break;
            case CombatOutcome.GuardBreak:
                Flash(blades[result.PushedPlayer], new Color(1f, 0.4f, 0.04f));
                sound = 2;
                break;
            case CombatOutcome.Clash:
                Flash(blades[0], Color.white);
                Flash(blades[1], Color.white);
                sound = 3;
                break;
            default:
                return;
        }
        flashUntil = now + FlashSeconds;
        audioSource.PlayOneShot(sounds[sound]);
        Color color = sound == 1 ? Color.cyan : sound == 2 ? new Color(1f, 0.45f, 0.08f) : Color.white;
        effects.Emit(contactPoint, color, now, false, sound == 2 ? 1.8f : 1.4f);
        effects.Emit(contactPoint, Color.white, now, false, .65f);
        impactAt = now; impactTint = color;
        for (int i = 0; i < 2; i++)
        {
            if (i == result.PushedPlayer && CombatReaction.IsHit(result.Outcome))
                StaggerCharacter(i, worldCutDirection, now, CombatReaction.RecoverySeconds, result.HitStopSeconds);
            else if (i == result.PushedPlayer && result.Outcome == CombatOutcome.GuardSuccess)
                GuardRecoilCharacter(i, displacement, now, result.HitStopSeconds);
            else if (i == result.Attacker && CombatReaction.IsHit(result.Outcome))
                AttackImpactCharacter(i, result, worldCutDirection, now);
            else avatars[i].React(result, i, now, displacement);
            views[i].Kick(now, sound == 0 || sound == 2 ? 1f : 0.65f);
        }
    }

    private void Flash(FlashRenderer[] renderers, Color color)
    {
        foreach (FlashRenderer renderer in renderers)
        {
            renderer.Show(color);
            flashing.Add(renderer);
        }
    }

    public Vector3 ChestOffset(int index) => avatars[index].ChestOffset;

    public SwordPose PoseSword(int index, SwordPose pose, double now, bool guard = false)
        => avatars[index].PoseSword(pose, now, guard);

    public void AttackImpactCharacter(int index, CombatResult result, Vector3 worldCutDirection, double now)
    {
        avatars[index].React(result, index, now, worldCutDirection);
        views[index].AttackImpact(now, worldCutDirection, result.HitStopSeconds);
    }

    public void StaggerCharacter(int index, Vector3 worldCutDirection, double now, float recoverySeconds, float hitStopSeconds = 0f)
    {
        avatars[index].Stagger(worldCutDirection, now, recoverySeconds, hitStopSeconds);
        ShowStagger(avatars[index], now, recoverySeconds, hitStopSeconds);
    }

    public void ShowStagger(DuelAvatar avatar, double now, float recoverySeconds, float hitStopSeconds = DuelMatch.HitStopSeconds)
        => effects.ShowStagger(avatar, now, recoverySeconds + Mathf.Max(0f, hitStopSeconds));

    public void GuardRecoilCharacter(int index, Vector3 worldPushDirection, double now, float hitStopSeconds = 0f)
    {
        avatars[index].GuardRecoil(worldPushDirection, now, hitStopSeconds);
        ShowStagger(avatars[index], now, ReactionSwordPose.GuardDurationSeconds, hitStopSeconds);
    }

    public void PoseCharacterPreview(int index, double now, float dt, Quaternion rotation, bool guard, float speed)
    {
        avatars[index].Tick(now, dt, rotation, guard, speed);
        MovementSound(index, speed, now);
        effects.Tick(now, false);
    }

    public void PoseCharacters(double now, float dt, Quaternion first, Quaternion second, bool firstGuard, bool secondGuard, float firstSpeed, float secondSpeed, bool frozen)
    {
        if (frozen) return;
        avatars[0].Tick(now, dt, first, firstGuard, firstSpeed);
        avatars[1].Tick(now, dt, second, secondGuard, secondSpeed);
        MovementSound(0, firstSpeed, now);
        MovementSound(1, secondSpeed, now);
    }
    private void MovementSound(int index, float speed, double now)
    {
        if (avatars[index].Swung)
            movementAudio.PlayOneShot(swingSound, Mathf.Clamp01(speed / 350f));
        if (avatars[index].Stepped)
        {
            movementAudio.PlayOneShot(stepSound, 0.35f);
            effects.Emit(avatars[index].StepPosition, new Color(0.7f, 0.65f, 0.55f, 0.4f), now, true);
        }
    }
    public void Tick(double now, bool frozen)
    {
        if (flashing.Count > 0 && now >= flashUntil) ClearFlash();
        for (int i = 0; i < views.Length; i++)
        {
            views[i].SetBodyRoll(avatars[i].ReactionRoll(now));
            views[i].SetBodyPitch(avatars[i].ReactionPitch(now));
            views[i].Tick(now);
        }
        effects.Tick(now, frozen);
        TickMatchPresentation(now);
        if (pendingWinner != MatchWinner.None && now >= winnerAt && !paused) winnerCanvas.SetActive(true);
    }
    public void ClearMotionHistory()
    {
        effects.ClearTrails();
        foreach (DuelAvatar avatar in avatars) avatar.ClearMotionHistory();
        movementAudio.Stop();
    }
    public void SetPaused(bool value)
    {
        SetSuspended(value);
    }

    public void ShowSingleGameOver(bool visible)
    {
        if (singleGameOverCanvas == null) return;
        if (visible && !singleGameOverVisible) { resultSelection = 0; UpdateResultFocus(); }
        singleGameOverVisible = visible;
        singleGameOverCanvas.SetActive(visible && !paused);
    }

    public void BeginSingleDefeat(double now)
    {
        ShowSingleGameOver(false);
        avatars[0].FallOnCourse(now);
        views[0].SetFocusTarget(players[0]);
        views[0].SetOwnBodyFade(false);
        views[0].SetWalkingView(true);
        foreach (DuelView view in views) view.SetCinematic(0f);
        SetShowdownFade(0f);
        SetBanner("", "", 0f);
        singleHearts.text = new string('♡', ShowdownProgress.MaxHearts);
    }
    private Vector3 ContactPoint(CombatResult result)
    {
        CombatPose Pose(int index)
        {
            Vector3 grip = swords[index].position, blade = swords[index].forward;
            Vector3 body = players[index].TransformPoint(avatars[index].ChestCenter);
            return new CombatPose(grip, blade, grip, blade, body, players[index].forward,
                Vector3.zero, grip, blade);
        }
        return CombatContact.ContactPoint(result, Pose(0), Pose(1));
    }

    private void ClearFlash()
    {
        foreach (FlashRenderer renderer in flashing)
            renderer.Restore();
        flashing.Clear();
    }

    public void ShowWinner(MatchWinner winner, double now)
    {
        winnerLabel.text = winner == MatchWinner.Player1 ? "P1 の勝ち！"
            : winner == MatchWinner.Player2 ? "P2 の勝ち！" : "引き分け！";
        winnerLabel.color = winner == MatchWinner.Player1 ? new Color(1f, 0.35f, 0.3f)
            : winner == MatchWinner.Player2 ? Sky : Ink;
        resultSelection = 0; UpdateResultFocus();
        pendingWinner = winner; winnerAt = now + 0.6;
        // Round fall/celebration already started before the match result panel.
    }

    public void ResetEffects()
    {
        ClearFlash();
        ResetMatchPresentation();
        flashUntil = 0;
        audioSource.Stop();
        winnerCanvas.SetActive(false); pendingWinner = MatchWinner.None; winnerAt = double.PositiveInfinity;
        resultSelection = 0; UpdateResultFocus();
        effects.ResetEffects();
        foreach (DuelAvatar avatar in avatars) avatar.Reset();
        foreach (DuelView view in views) view.Reset();
        ClearMotionHistory();
    }

    private static Button CreateSettingButton(string name, Transform parent, Font font, string label,
        Vector2 size, Vector2 position, Action clicked)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size; rect.anchoredPosition = position;
        Button button = go.GetComponent<Button>(); button.onClick.AddListener(() => clicked());
        CreateText(name + " label", rect, font, 22, size - new Vector2(8, 4), Vector2.zero).text = label;
        return button;
    }

    public static Font CreateJapaneseFont()
    {
        if (japaneseFont != null) return japaneseFont;
        string[] preferred = { "Hiragino Maru Gothic ProN", "Hiragino Kaku Gothic ProN", "Hiragino Sans", "Meiryo", "Yu Gothic", "Noto Sans CJK JP", "Noto Sans JP", "Arial Unicode MS" };
        string[] installed = Font.GetOSInstalledFontNames();
        foreach (string candidate in preferred)
            foreach (string available in installed)
                if (string.Equals(candidate, available, StringComparison.OrdinalIgnoreCase))
                    return japaneseFont = Font.CreateDynamicFontFromOSFont(available, 32);
        return japaneseFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    public void NavigateResult(Vector2Int direction)
    {
        if (!ResultVisible || direction == Vector2Int.zero) return;
        int step = direction.y != 0 ? -direction.y : direction.x;
        resultSelection = (resultSelection + (step > 0 ? 1 : 2)) % 3;
        UpdateResultFocus();
    }

    public void ConfirmResult()
    {
        if (!ResultVisible) return;
        Button[] buttons = singleGameOverVisible ? singleResultButtons : duelResultButtons;
        buttons[resultSelection].onClick.Invoke();
    }

    private void UpdateResultFocus()
    {
        foreach (Button[] buttons in new[] { duelResultButtons, singleResultButtons })
        {
            if (buttons == null) continue;
            for (int i = 0; i < buttons.Length; i++)
            {
                bool selected = i == resultSelection;
                buttons[i].GetComponent<Image>().color = selected ? Sky : new Color(.9f, .96f, 1f);
                buttons[i].GetComponentInChildren<Text>().color = selected ? Color.white : Ink;
            }
        }
    }

    private GameObject CreateResultCanvas(string name, int order, out RectTransform panel)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
        var card = new GameObject("Result panel", typeof(RectTransform)); card.transform.SetParent(root.transform, false);
        panel = card.GetComponent<RectTransform>(); panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(920f, 550f);
        root.SetActive(false);
        return root;
    }

    private Button[] CreateResultButtons(Transform panel, Action replay, MasterController controls)
    {
        Action[] actions = { replay,
            () => { GameFlowController flow = controls.GetComponent<GameFlowController>(); if (flow != null) flow.ReturnToModes(); },
            () => { GameFlowController flow = controls.GetComponent<GameFlowController>(); if (flow != null) flow.ReturnToCharacters(); } };
        string[] labels = { "もう一度あそぶ", "モードを選ぶ", "キャラクターを選ぶ" };
        string[] names = { "Rematch", "Mode select", "Character select" };
        var buttons = new Button[3];
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i] = CreateSettingButton(names[i], panel, CreateJapaneseFont(), labels[i], new Vector2(700, 76), new Vector2(0, 12 - i * 94), actions[i]);
            buttons[i].navigation = new Navigation { mode = Navigation.Mode.None };
        }
        return buttons;
    }

    private void CreateSingleGameOverUI(Action retry, MasterController controls)
    {
        singleGameOverCanvas = CreateResultCanvas("Single game over", 1050, out RectTransform panel);
        Text title = CreateText("Single game over title", panel, CreateJapaneseFont(), 64, new Vector2(840, 96), new Vector2(0, 174));
        title.text = "組み手終了"; title.color = Sky;
        Text guidance = CreateText("Single game over guidance", panel, CreateJapaneseFont(), 28, new Vector2(840, 48), new Vector2(0, 103));
        guidance.color = Ink;
        singleResultButtons = CreateResultButtons(panel, retry, controls); UpdateResultFocus();
    }

    private void CreateWinnerUI(Action rematch, MasterController controls)
    {
        winnerCanvas = CreateResultCanvas("Match result", 1100, out RectTransform panel);
        winnerLabel = CreateText("Winner", panel, CreateJapaneseFont(), 64, new Vector2(840, 96), new Vector2(0, 174));
        winnerScoreLabel = CreateText("Match score", panel, CreateJapaneseFont(), 28, new Vector2(840, 48), new Vector2(0, 103));
        winnerScoreLabel.color = Ink;
        duelResultButtons = CreateResultButtons(panel, rematch, controls); UpdateResultFocus();
    }

    private static Text CreateText(string objectName, Transform parent, Font font, int size,
        Vector2 dimensions, Vector2 position)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = dimensions;
        rect.anchoredPosition = position;
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static AudioClip CreateSwingSound()
    {
        const int sampleRate = 22050;
        const float duration = 0.16f;
        var samples = new float[Mathf.CeilToInt(duration * sampleRate)];
        var random = new System.Random(23);
        float filtered = 0f, low = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)(samples.Length - 1);
            // Soft band-limited air, with zero-valued ends instead of a white-noise click.
            filtered += 0.22f * ((float)random.NextDouble() * 2f - 1f - filtered);
            low += 0.025f * (filtered - low);
            float envelope = Mathf.Sin(Mathf.PI * t);
            samples[i] = (filtered - low) * envelope * envelope * 0.6f;
        }
        AudioClip clip = AudioClip.Create("Sword air", samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip CreateSound(string clipName, float duration, float frequency, float noiseMix, bool metallic)
    {
        const int sampleRate = 22050;
        var samples = new float[Mathf.CeilToInt(duration * sampleRate)];
        var random = new System.Random(17);
        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)sampleRate;
            float progress = time / duration;
            float envelope = Mathf.Min(1f, time / 0.003f) * Mathf.Exp(-7f * progress) * (1f - progress);
            float tone = Mathf.Sin(2f * Mathf.PI * frequency * time * (metallic ? 1f : 1f - 0.3f * progress));
            if (metallic)
                tone = (tone + 0.5f * Mathf.Sin(2f * Mathf.PI * frequency * 2.73f * time)) / 1.5f;
            float noise = (float)random.NextDouble() * 2f - 1f;
            samples[i] = ((1f - noiseMix) * tone + noiseMix * noise) * envelope * 0.8f;
        }
        AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        DisposeMatchPresentation();
        ClearFlash();
        if (views != null) foreach (DuelView view in views) view.Dispose();
        if (swingSound != null) Destroy(swingSound);
        if (stepSound != null) Destroy(stepSound);
        if (sounds == null)
            return;
        foreach (AudioClip clip in sounds)
            if (clip != null)
                Destroy(clip);
    }
}
