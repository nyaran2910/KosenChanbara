using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class DuelPresentation
{
    private GameObject matchHud;
    private Text scoreLeft, scoreRight, clockLabel, phaseLabel, phaseDetail, singleProgress, singleHearts;
    private Image impactOverlay, progressFill, showdownFade;
    private bool singleMode, freeMode;
    private RoundPhase lastPhase = (RoundPhase)(-1);
    private double showdownIntroStarted = double.NaN;
    private int showdownIntroSection;
    private const float RoundIntroSeconds = 1.3f, ReadyIntroSeconds = 1.2f, StartBannerSeconds = .7f;
    private double flowPulse = -100, impactAt = -100, splashAt = double.PositiveInfinity;
    private MatchWinner fallingWinner;
    private AudioClip readyCue, goCue, pointCue, splashCue;
    private Color impactTint;

    private void CreateMatchHud()
    {
        matchHud = new GameObject("Match presentation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        matchHud.transform.SetParent(transform, false);
        Canvas canvas = matchHud.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 950;
        CanvasScaler scaler = matchHud.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        Font font = CreateJapaneseFont();
        scoreLeft = HudText("P1 score", font, 36, new Vector2(560, 70), new Vector2(-570, 455));
        scoreRight = HudText("P2 score", font, 36, new Vector2(560, 70), new Vector2(570, 455));
        scoreLeft.color = new Color(1, .38f, .29f); scoreRight.color = new Color(.3f, .72f, 1);
        clockLabel = HudText("Round clock", font, 36, new Vector2(350, 65), new Vector2(0, 450));
        clockLabel.color = Ink;
        singleProgress = HudText("Showdown progress", font, 34, new Vector2(700, 70), new Vector2(0, 460));
        singleHearts = HudText("Showdown life", font, 34, new Vector2(600, 60), new Vector2(-610, 460));
        singleHearts.color = new Color(1, .35f, .3f);
        progressFill = HudImage("Showdown progress fill", new Vector2(740, 8), new Vector2(0, 411), Sky);
        progressFill.type = Image.Type.Simple;
        phaseLabel = HudText("Phase", font, 104, new Vector2(1700, 125), new Vector2(0, 75));
        phaseLabel.fontStyle = FontStyle.Bold;
        phaseLabel.color = Sky;
        phaseDetail = HudText("Phase detail", font, 28, new Vector2(1500, 55), new Vector2(0, -15));
        phaseDetail.color = Ink; phaseDetail.gameObject.SetActive(false); singleProgress.color = Ink;
        impactOverlay = HudImage("Impact flash", new Vector2(2400, 1800), Vector2.zero, Color.clear);
        showdownFade = HudImage("Showdown transition fade", Vector2.zero, Vector2.zero, Color.clear);
        showdownFade.rectTransform.anchorMin = Vector2.zero; showdownFade.rectTransform.anchorMax = Vector2.one;
        showdownFade.rectTransform.offsetMin = showdownFade.rectTransform.offsetMax = Vector2.zero;
        showdownFade.gameObject.SetActive(false);
        readyCue = CreateSound("Ready bell", .34f, 660, .02f, true);
        goCue = CreateSound("Start bell", .6f, 990, .02f, true);
        pointCue = CreateSound("Point bell", .6f, 520, .02f, true);
        splashCue = CreateSound("Water splash", .9f, 85, .92f, false);
        SetMatchMode(false);
    }
    private Text HudText(string name, Font font, int size, Vector2 dimensions, Vector2 position)
    {
        Text text = CreateText(name, matchHud.transform, font, size, dimensions, position);
        return text;
    }
    private Image HudImage(string name, Vector2 size, Vector2 position, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(matchHud.transform, false);
        RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = size; rect.anchoredPosition = position;
        Image image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
    }
    public void SetMatchMode(bool single)
    {
        singleMode = single; lastPhase = (RoundPhase)(-1);
        showdownIntroStarted = double.NaN; showdownIntroSection = 0;
        scoreLeft.gameObject.SetActive(!single); scoreRight.gameObject.SetActive(!single); clockLabel.gameObject.SetActive(true);
        clockLabel.rectTransform.anchoredPosition = single ? new Vector2(650, 460) : new Vector2(0, 450);
        singleProgress.gameObject.SetActive(single); singleHearts.gameObject.SetActive(single);
        progressFill.gameObject.SetActive(single);
        phaseLabel.text = phaseDetail.text = "";
        foreach (DuelView view in views) view.SetCinematic(0);
        SetShowdownFade(0f);
    }
    public void SetShowdownFade(float opacity)
    {
        if (showdownFade == null) return;
        opacity = Mathf.Clamp01(opacity);
        showdownFade.color = new Color(0f, 0f, 0f, opacity);
        showdownFade.gameObject.SetActive(opacity > 0f);
    }
    public void ResetShowdownView()
    {
        foreach (DuelAvatar avatar in avatars) avatar.Reset();
        foreach (DuelView view in views) view.Reset();
        ClearMotionHistory();
    }
    public void SetFreePresentation(bool free)
    {
        freeMode = free;
        matchHud.SetActive(!paused && !free);
        if (free) SetBanner("", "", 0f);
    }
    private static string Points(int count) => new string('●', Mathf.Min(count, DuelSeries.WinsRequired)) + new string('○', Mathf.Max(0, DuelSeries.WinsRequired - count));
    public void UpdateDuelFlow(DuelSeries series, double now)
    {
        if (singleMode) return;
        SetPresentationPhase(series.Phase, now);
        scoreLeft.text = "P1   " + Points(series.FirstScore); scoreRight.text = Points(series.SecondScore) + "   P2";
        winnerScoreLabel.text = "P1　" + series.FirstScore + "　—　" + series.SecondScore + "　P2";
        clockLabel.text = series.IsFinalRound ? "時間無制限" : series.Phase == RoundPhase.Fight ? Mathf.CeilToInt((float)series.Remaining(now)).ToString("00") : "3本先取";
        float age = (float)(now - series.PhaseStarted);
        string title = "";
        switch (series.Phase)
        {
            case RoundPhase.Versus: title = "P1     VS     P2"; break;
            case RoundPhase.Round:
                title = series.IsFinalRound ? "最終決戦" : series.RoundNumber + "本目";
                break;
            case RoundPhase.Ready: title = "かまえて！"; break;
            case RoundPhase.Fight: if (age < StartBannerSeconds) title = "はじめ！"; break;
            case RoundPhase.Result:
                title = age < 1.4f ? (fallingWinner == MatchWinner.None ? "時間切れ！" : "場外！")
                    : series.RoundWinner == MatchWinner.Draw ? (series.IsFinalRound ? "最終決戦" : "引き分け！") : series.RoundWinner == MatchWinner.Player1 ? "P1 の一本！" : "P2 の一本！";
                break;
        }
        SetBanner(title, "", age);
        SetIntroCinematic(series.Phase, age);
    }
    private void SetPresentationPhase(RoundPhase phase, double now)
    {
        if (lastPhase == phase) return;
        lastPhase = phase; flowPulse = now;
        if (phase == RoundPhase.Ready) audioSource.PlayOneShot(readyCue);
        if (phase == RoundPhase.Fight) audioSource.PlayOneShot(goCue);
    }
    private void SetIntroCinematic(RoundPhase phase, float age, float roundSeconds = RoundIntroSeconds)
    {
        float cinematic = phase == RoundPhase.Versus ? 1 : phase == RoundPhase.Round
            ? 1 - Mathf.Clamp01(age / roundSeconds) : 0;
        foreach (DuelView view in views) view.SetCinematic(cinematic);
    }
    private void SetBanner(string title, string detail, float age)
    {
        phaseLabel.text = title; phaseDetail.text = detail;
        float scale = 1 + .22f * Mathf.Exp(-age * 12);
        phaseLabel.rectTransform.localScale = Vector3.one * scale;
    }
    public void BeginRoundResult(MatchWinner winner, double now, bool ringOut)
    {
        fallingWinner = ringOut ? winner : MatchWinner.None;
        if (ringOut)
        {
            avatars[0].Finish(winner != MatchWinner.Player1, now);
            avatars[1].Finish(winner != MatchWinner.Player2, now);
            splashAt = now + .8;
        }
        audioSource.PlayOneShot(pointCue);
    }
    public void UpdateShowdown(ShowdownProgress progress, double now, double started,
        int section = 1, bool advancing = false, float advanceProgress = 0f)
    {
        singleProgress.text = "組み手　" + progress.Defeated.ToString("000") + " / 100人";
        singleHearts.text = new string('♥', progress.Hearts) + new string('♡', ShowdownProgress.MaxHearts - progress.Hearts);
        clockLabel.gameObject.SetActive(true);
        clockLabel.text = "区間 " + section.ToString("00") + " / 10";
        progressFill.rectTransform.sizeDelta = new Vector2(740f * progress.Defeated / ShowdownProgress.Target, 8);
        foreach (DuelView view in views)
        {
            view.SetOwnBodyFade(!advancing && !menuPresentation);
            view.SetWalkingView(advancing);
        }
        if (advancing)
        {
            SetBanner(advanceProgress < .7f ? "区間クリア！" : "次の区間へ", "", advanceProgress);
            SetIntroCinematic(RoundPhase.Fight, 0);
            return;
        }
        if (showdownIntroStarted != started || showdownIntroSection != section)
        {
            showdownIntroStarted = started; showdownIntroSection = section;
            lastPhase = (RoundPhase)(-1);
        }
        float age = Mathf.Max(0f, (float)(now - started));
        float introScale = 1f;
        float roundSeconds = RoundIntroSeconds * introScale;
        float fightAt = (RoundIntroSeconds + ReadyIntroSeconds) * introScale;
        RoundPhase phase = age < roundSeconds ? RoundPhase.Round
            : age < fightAt ? RoundPhase.Ready : RoundPhase.Fight;
        float phaseAge = phase == RoundPhase.Round ? age
            : age - (phase == RoundPhase.Ready ? roundSeconds : fightAt);
        SetPresentationPhase(phase, now);
        string title = phase == RoundPhase.Round ? (section == 1 ? "100人組み手" : section + "区間目")
            : phase == RoundPhase.Ready ? "かまえて！"
            : phaseAge < StartBannerSeconds ? "はじめ！" : "";
        SetBanner(title, "", phaseAge);
        SetIntroCinematic(phase, phaseAge, roundSeconds);
    }
    public void ShowSingleResult(ShowdownProgress progress)
    {
        foreach (Text text in singleGameOverCanvas.GetComponentsInChildren<Text>(true))
        {
            if (text.name == "Single game over title") text.text = progress.Cleared ? "100人達成！" : "組み手終了";
            if (text.name == "Single game over guidance") text.text = progress.Defeated + " / 100人撃破　残りハート " + progress.Hearts;
        }
        SetBanner("", "", 0);
        ShowSingleGameOver(true);
    }
    public void PlaySingleContact(Vector3 point, bool guard, bool playerHurt, double now)
    {
        Color color = guard ? new Color(.2f, .9f, 1) : playerHurt ? new Color(1, .15f, .08f) : new Color(1, .83f, .22f);
        effects.Emit(point, color, now, false, guard ? 1.1f : 1.6f);
        effects.Emit(point, Color.white, now, false, .65f);
        audioSource.PlayOneShot(sounds[guard ? 1 : 0]);
        views[0].Kick(now, playerHurt ? 1 : .55f);
        if (playerHurt) { ClearFlash(); Flash(bodies[0], color); flashUntil = now + .18; }
        impactAt = now; impactTint = color;
    }
    private void TickMatchPresentation(double now)
    {
        float opacity = Mathf.Max(0, 1 - (float)(now - impactAt) / .15f) * .12f;
        impactOverlay.color = new Color(impactTint.r, impactTint.g, impactTint.b, opacity);
        if (now < splashAt) return;
        splashAt = double.PositiveInfinity;
        for (int i = 0; i < 2; i++)
        {
            if ((i == 0 && fallingWinner == MatchWinner.Player1) || (i == 1 && fallingWinner == MatchWinner.Player2)) continue;
            Vector3 position = players[i].position + Vector3.down * 2.5f;
            effects.Emit(position, new Color(.35f, .8f, 1), now, false, 3.5f);
            effects.Emit(position + Vector3.up * .35f, Color.white, now, true, 3);
            views[i].Kick(now, .9f);
        }
        audioSource.PlayOneShot(splashCue);
    }
    public void SetSuspended(bool value)
    {
        paused = value;
        matchHud.SetActive(!value && !freeMode);
        singleGameOverCanvas.SetActive(!value && singleGameOverVisible);
        if (value) { audioSource.Pause(); movementAudio.Pause(); winnerCanvas.SetActive(false); }
        else { audioSource.UnPause(); movementAudio.UnPause(); }
    }
    private void ResetMatchPresentation()
    {
        splashAt = double.PositiveInfinity; fallingWinner = MatchWinner.None;
        impactAt = -100; impactOverlay.color = Color.clear;
        SetShowdownFade(0f);
    }
    private void DisposeMatchPresentation()
    {
        if (readyCue != null) Destroy(readyCue); if (goCue != null) Destroy(goCue);
        if (pointCue != null) Destroy(pointCue); if (splashCue != null) Destroy(splashCue);
    }
}
