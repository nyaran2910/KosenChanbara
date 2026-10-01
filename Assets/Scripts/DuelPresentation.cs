using System;
using System.Collections.Generic;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.UI;

public sealed class DuelPresentation : MonoBehaviour
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
    private DuelAvatar[] avatars;
    private DuelView[] views;
    private DuelEffects effects;
    private Transform[] players, swords;
    private AudioSource movementAudio;
    private AudioClip swingSound, stepSound;
    private readonly bool[] swinging = new bool[2];
    private readonly double[] lastSwing = { -100, -100 };
    private GameObject pauseCanvas, pausePanel;
    private Text pauseHint;
    private bool paused;
    private double winnerAt = double.PositiveInfinity;
    private MatchWinner pendingWinner;

    public void Initialize(GameObject firstPlayer, GameObject secondPlayer,
        GameObject firstSword, GameObject secondSword, SpriteRenderer arena, Action rematch, Action resume)
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
        {
            var renderers = new Renderer[bodies[i].Length];
            for (int j = 0; j < renderers.Length; j++) renderers[j] = bodies[i][j].Renderer;
            views[i] = new DuelView(players[i], swords[i], renderers);
        }
        effects = gameObject.AddComponent<DuelEffects>(); effects.Initialize(swords[0], swords[1]);
        gameObject.AddComponent<DuelArena>().Initialize(arena);
        movementAudio = gameObject.AddComponent<AudioSource>(); movementAudio.playOnAwake = false; movementAudio.volume = 0.22f;
        swingSound = CreateSound("Sword air", 0.14f, 180f, 0.95f, false);
        stepSound = CreateSound("Footstep", 0.07f, 90f, 0.65f, false);
        CreateWinnerUI(() => { rematch(); resume(); });
        CreatePauseUI(resume);
    }

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

    public void Play(CombatResult result, double now)
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
                Flash(blades[0], Color.cyan);
                Flash(blades[1], Color.cyan);
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
        effects.Emit(ContactPoint(result), color, now, false, sound == 2 ? 1.1f : 0.85f);
        for (int i = 0; i < 2; i++) { avatars[i].React(result, i, now); views[i].Kick(now, sound == 0 || sound == 2 ? 1f : 0.65f); }
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

    public void PoseCharacters(double now, float dt, Quaternion first, Quaternion second, bool firstGuard, bool secondGuard, float firstSpeed, float secondSpeed, bool frozen)
    {
        if (frozen) return;
        avatars[0].Tick(now, dt, first, firstGuard, firstSpeed);
        avatars[1].Tick(now, dt, second, secondGuard, secondSpeed);
        MovementSound(0, firstSpeed, firstGuard, now);
        MovementSound(1, secondSpeed, secondGuard, now);
    }
    private void MovementSound(int index, float speed, bool guard, double now)
    {
        bool fast = speed > 100f && !guard;
        if (fast && !swinging[index] && now - lastSwing[index] > 0.25)
        { movementAudio.PlayOneShot(swingSound, Mathf.Clamp01(speed / 350f)); lastSwing[index] = now; }
        swinging[index] = fast;
        if (avatars[index].Stepped)
        {
            movementAudio.PlayOneShot(stepSound, 0.6f);
            effects.Emit(players[index].TransformPoint(new Vector3(-0.2f, -0.96f, 0.15f)), new Color(0.7f, 0.65f, 0.55f, 0.4f), now, true);
        }
    }
    public void Tick(double now, bool frozen)
    {
        if (flashing.Count > 0 && now >= flashUntil) ClearFlash();
        foreach (DuelView view in views) view.Tick(now);
        effects.Tick(now, frozen);
        if (pendingWinner != MatchWinner.None && now >= winnerAt && !paused) winnerCanvas.SetActive(true);
    }
    public void ClearMotionHistory()
    {
        effects.ClearTrails();
        for (int i = 0; i < 2; i++) { swinging[i] = false; lastSwing[i] = -100; }
        movementAudio.Stop();
    }
    public void SetPaused(bool value)
    {
        paused = value; pausePanel.SetActive(value); pauseHint.gameObject.SetActive(!value);
        if (value) { audioSource.Pause(); movementAudio.Pause(); winnerCanvas.SetActive(false); }
        else { audioSource.UnPause(); movementAudio.UnPause(); }
    }
    private Vector3 ContactPoint(CombatResult result)
    {
        if (result.Outcome == CombatOutcome.Hit)
        {
            int defender = result.PushedPlayer, attacker = 1 - defender;
            Vector3 center = players[defender].position + players[defender].TransformVector(new Vector3(0f, 0.1f, 0f));
            return SwordContact.ClosestMidpoint(swords[attacker].position, swords[attacker].position + swords[attacker].forward * 2.1f,
                center - Vector3.up * 0.4f, center + Vector3.up * 0.65f);
        }
        return SwordContact.ClosestMidpoint(swords[0].position, swords[0].position + swords[0].forward * 2.1f,
            swords[1].position, swords[1].position + swords[1].forward * 2.1f);
    }

    private void ClearFlash()
    {
        foreach (FlashRenderer renderer in flashing)
            renderer.Restore();
        flashing.Clear();
    }

    public void ShowWinner(MatchWinner winner, double now)
    {
        winnerLabel.text = winner == MatchWinner.Player1 ? "P1 WIN"
            : winner == MatchWinner.Player2 ? "P2 WIN" : "DRAW";
        winnerLabel.color = winner == MatchWinner.Player1 ? new Color(1f, 0.35f, 0.3f)
            : winner == MatchWinner.Player2 ? new Color(0.35f, 0.65f, 1f) : Color.white;
        pendingWinner = winner; winnerAt = now + 0.6;
        avatars[0].Finish(winner != MatchWinner.Player1, now);
        avatars[1].Finish(winner != MatchWinner.Player2, now);
    }

    public void ResetEffects()
    {
        ClearFlash();
        flashUntil = 0;
        audioSource.Stop();
        winnerCanvas.SetActive(false); pendingWinner = MatchWinner.None; winnerAt = double.PositiveInfinity;
        effects.ResetEffects();
        foreach (DuelAvatar avatar in avatars) avatar.Reset();
        foreach (DuelView view in views) view.Reset();
        ClearMotionHistory();
    }

    private void CreatePauseUI(Action resume)
    {
        pauseCanvas = new GameObject("Pause UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        pauseCanvas.transform.SetParent(transform, false);
        Canvas canvas = pauseCanvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 990;
        CanvasScaler scaler = pauseCanvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        pausePanel = new GameObject("Pause panel", typeof(RectTransform), typeof(Image)); pausePanel.transform.SetParent(pauseCanvas.transform, false);
        RectTransform panel = pausePanel.GetComponent<RectTransform>(); panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one; panel.sizeDelta = Vector2.zero;
        pausePanel.GetComponent<Image>().color = new Color(0.02f, 0.035f, 0.055f, 0.92f);
        CreateText("Pause title", panel, font, 56, new Vector2(1000, 80), new Vector2(0, 375)).text = "CHAMBARA  /  PAUSED";
        CreateText("Connect guidance", panel, font, 24, new Vector2(1100, 80), new Vector2(0, 285)).text = "Scan your player QR. Hold the phone upright, then RECENTER.";
        var button = new GameObject("Resume", typeof(RectTransform), typeof(Image), typeof(Button)); button.transform.SetParent(panel, false);
        RectTransform rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(400, 64); rect.anchoredPosition = new Vector2(0, -330);
        button.GetComponent<Image>().color = new Color(0.16f, 0.4f, 0.5f); button.GetComponent<Button>().onClick.AddListener(() => resume());
        CreateText("Resume label", rect, font, 28, new Vector2(390, 60), Vector2.zero).text = "PLAY / RESUME  [Esc]";
        CreateText("Keyboard guidance", panel, font, 20, new Vector2(1200, 75), new Vector2(0, -425)).text = "P1: arrows + Right Shift    |    P2: WASD + Left Shift    |    R: rematch";
        pauseHint = CreateText("Pause hint", pauseCanvas.transform, font, 22, new Vector2(240, 40), Vector2.zero);
        pauseHint.rectTransform.anchorMin = pauseHint.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        pauseHint.rectTransform.anchoredPosition = new Vector2(0, -28); pauseHint.text = "[Esc]  PAUSE";
    }

    private void CreateWinnerUI(Action rematch)
    {
        winnerCanvas = new GameObject("Match result", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        winnerCanvas.transform.SetParent(transform, false);
        Canvas canvas = winnerCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1100;
        CanvasScaler scaler = winnerCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var panel = new GameObject("Result panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(winnerCanvas.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(600f, 250f);
        panel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 0.92f);

        winnerLabel = CreateText("Winner", panel.transform, font, 72, new Vector2(540f, 110f), new Vector2(0f, 45f));
        var buttonObject = new GameObject("Rematch", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(panel.transform, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(300f, 64f);
        buttonRect.anchoredPosition = new Vector2(0f, -65f);
        buttonObject.GetComponent<Image>().color = new Color(0.18f, 0.23f, 0.31f);
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(() => rematch());
        CreateText("Rematch label", buttonObject.transform, font, 28,
            new Vector2(290f, 60f), Vector2.zero).text = "REMATCH  [R]";
        winnerCanvas.SetActive(false);
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
