using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed partial class GameFlowController
{
    private readonly SessionPlayTimer sessionTimer = new SessionPlayTimer();
    private GameObject timerCanvas;
    private Text timerRemaining;
    private Image timerBadge;
    private RectTransform timerNotice;
    private CanvasGroup timerNoticeGroup;
    private SessionTimerClock timerNoticeClock;
    private AudioSource timerAudio;
    private AudioClip timerChime;

    private void BuildSessionTimer()
    {
        timerCanvas = Canvas("Session timer", 1500);
        var passthrough = timerCanvas.AddComponent<CanvasGroup>();
        passthrough.interactable = passthrough.blocksRaycasts = false;
        timerBadge = Box(timerCanvas.transform, "Time remaining", Vector2.zero,
            new Vector2(142, 32), new Color(.05f, .14f, .19f, .76f));
        RectTransform badge = timerBadge.rectTransform;
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0f, 1f);
        badge.anchoredPosition = new Vector2(28, -12);
        Clock(badge, new Vector2(-46, 0), 24);
        timerRemaining = Label(badge, "10:00", new Vector2(15, 0), new Vector2(96, 30), 22, Color.white);
        timerRemaining.resizeTextForBestFit = false;

        Image notice = Box(timerCanvas.transform, "Time is up", Vector2.zero,
            new Vector2(650, 132), new Color(.96f, .43f, .13f));
        timerNotice = notice.rectTransform;
        timerNotice.anchorMin = timerNotice.anchorMax = timerNotice.pivot = new Vector2(.5f, 1f);
        timerNoticeGroup = notice.gameObject.AddComponent<CanvasGroup>();
        timerNoticeClock = Clock(timerNotice, new Vector2(-254, 0), 88);
        Label(timerNotice, "時間になりました", new Vector2(44, 20), new Vector2(470, 50), 38, Color.white, TextAnchor.MiddleLeft);
        Label(timerNotice, "10分が経ちました", new Vector2(44, -27), new Vector2(470, 34), 24,
            new Color(1, 1, 1, .9f), TextAnchor.MiddleLeft);
        timerNotice.gameObject.SetActive(false);
        timerAudio = gameObject.AddComponent<AudioSource>();
        timerAudio.playOnAwake = false; timerAudio.spatialBlend = 0f; timerAudio.volume = .3f;
        timerChime = CreateTimerChime();
        timerCanvas.SetActive(false);
    }

    private SessionTimerClock Clock(Transform parent, Vector2 position, float size)
    {
        var root = new GameObject("Clock", typeof(RectTransform), typeof(SessionTimerClock));
        root.transform.SetParent(parent, false);
        var clock = root.GetComponent<SessionTimerClock>();
        clock.color = Color.white; clock.raycastTarget = false;
        Place(clock.rectTransform, position, new Vector2(size, size));
        return clock;
    }

    private void UpdateSessionTimer()
    {
        if (!ScoreDialogOpen && Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            sessionTimer.Toggle(Time.realtimeSinceStartupAsDouble); timerAudio.Stop();
        }
        TickSessionTimer(Time.realtimeSinceStartupAsDouble);
    }

    private void TickSessionTimer(double now)
    {
        bool announce = sessionTimer.Tick(now);
        timerCanvas.SetActive(sessionTimer.Armed);
        if (!sessionTimer.Armed) return;
        int seconds = sessionTimer.RemainingSeconds(now);
        timerRemaining.text = $"{seconds / 60:00}:{seconds % 60:00}";
        timerBadge.color = sessionTimer.Expired ? new Color(.91f, .36f, .1f, .94f)
            : new Color(.05f, .14f, .19f, .76f);
        if (announce) timerAudio.PlayOneShot(timerChime);
        float age = (float)(now - sessionTimer.ExpiredAt);
        bool visible = sessionTimer.Expired && age >= 0f && age < 5f;
        timerNotice.gameObject.SetActive(visible);
        if (!visible) return;
        float enter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / .28f));
        float exit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - 4.55f) / .45f));
        timerNotice.anchoredPosition = new Vector2(0, -24f - (1f - enter) * 160f + exit * 160f);
        timerNoticeGroup.alpha = enter * (1f - exit);
        float ring = Mathf.Sin(age * 25f) * Mathf.Exp(-age * 2.7f) * 14f;
        timerNoticeClock.rectTransform.localRotation = Quaternion.Euler(0, 0, ring);
    }

    private static AudioClip CreateTimerChime()
    {
        const int rate = 22050;
        var samples = new float[(int)(rate * .72f)];
        float[] notes = { 783.99f, 987.77f, 1174.66f };
        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)rate;
            for (int note = 0; note < notes.Length; note++)
            {
                float age = time - note * .14f;
                if (age < 0f) continue;
                float envelope = Mathf.Clamp01(age / .008f) * Mathf.Exp(-age * 10f);
                samples[i] += Mathf.Sin(age * notes[note] * Mathf.PI * 2f) * envelope * .22f;
            }
        }
        AudioClip clip = AudioClip.Create("Session time chime", samples.Length, 1, rate, false);
        clip.SetData(samples, 0); return clip;
    }
}
