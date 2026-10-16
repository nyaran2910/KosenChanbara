using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed partial class GameFlowController
{
    private ScoreboardClient scoreClient;
    private ScoreboardReply leaderboard;
    private string leaderboardDifficulty, leaderboardMessage;
    private bool leaderboardLoading;
    private float leaderboardRefreshAt;
    private RectTransform leaderboardPanel;
    private GameObject scoreDialog;
    private Text scoreTitle, scoreDetail, scoreFeedback;
    private InputField scoreName;
    private Button scoreSave, scoreSkip;
    private Outline scoreSaveFocus, scoreSkipFocus;
    private int scoreRevision, scoreDefeated, scoreChoice;
    private bool scoreQualified, scoreWorking, scoreSubmitting;
    private string scoreRunId, scoreDifficulty, scoreServerId;
    private ScoreSubmission scoreSubmission;
    private bool ScoreDialogOpen => scoreDialog != null && scoreDialog.activeSelf;
    private string SelectedScoreDifficulty => master.IsEasySingleDifficulty ? "easy" : "normal";

    private void UpdateLeaderboard()
    {
        if (scoreClient == null || CurrentScreen != Screen.Characters || !master.IsSingleMode || master.IsTutorialMode) return;
        if (!leaderboardLoading && Time.unscaledTime >= leaderboardRefreshAt)
        {
            string difficulty = SelectedScoreDifficulty;
            leaderboardLoading = true;
            StartCoroutine(scoreClient.Load(difficulty, (reply, error) => {
                leaderboardLoading = false;
                // A difficulty change can happen while the previous request is in flight.
                if (leaderboardDifficulty != difficulty) { leaderboardRefreshAt = 0; return; }
                leaderboardRefreshAt = Time.unscaledTime + 10f;
                if (error == null) leaderboard = reply;
                leaderboardMessage = error ?? "";
                if (leaderboardPanel != null) RenderLeaderboard();
            }));
        }
    }

    private void BuildLeaderboard()
    {
        if (leaderboardDifficulty != SelectedScoreDifficulty)
        {
            leaderboardDifficulty = SelectedScoreDifficulty;
            leaderboard = null; leaderboardMessage = "読み込み中…"; leaderboardRefreshAt = 0;
        }
        var root = new GameObject("Single leaderboard", typeof(RectTransform));
        root.transform.SetParent(page, false);
        leaderboardPanel = root.GetComponent<RectTransform>();
        Place(leaderboardPanel, new Vector2(470, 25), new Vector2(760, 800));
        RenderLeaderboard();
    }

    private void RenderLeaderboard()
    {
        foreach (Transform child in leaderboardPanel)
        { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        Label(leaderboardPanel, (leaderboardDifficulty == "easy" ? "イージー" : "ノーマル") + "　TOP 10",
            new Vector2(0, 350), new Vector2(700, 60), 34, Ink);
        Label(leaderboardPanel, "順位", new Vector2(-310, 284), new Vector2(90, 40), 21, Muted);
        Label(leaderboardPanel, "名前", new Vector2(-70, 284), new Vector2(410, 40), 21, Muted, TextAnchor.MiddleLeft);
        Label(leaderboardPanel, "倒した人数", new Vector2(280, 284), new Vector2(160, 40), 21, Muted, TextAnchor.MiddleRight);
        for (int i = 0; i < 10; i++)
        {
            ScoreEntry entry = leaderboard != null && i < leaderboard.entries.Length ? leaderboard.entries[i] : null;
            float y = 220 - i * 52;
            Color color = i == 0 && entry != null ? Sky : Ink;
            Label(leaderboardPanel, (i + 1).ToString("00"), new Vector2(-310, y), new Vector2(90, 48), 26, color);
            Text name = Label(leaderboardPanel, entry != null ? entry.name : "—", new Vector2(-70, y),
                new Vector2(410, 48), 27, color, TextAnchor.MiddleLeft);
            name.name = "Leaderboard name " + (i + 1); name.supportRichText = false;
            Text score = Label(leaderboardPanel, entry != null ? entry.defeated + "人" : "—", new Vector2(280, y),
                new Vector2(160, 48), 29, color, TextAnchor.MiddleRight);
            score.name = "Leaderboard score " + (i + 1);
        }
        Label(leaderboardPanel, leaderboardMessage ?? "", new Vector2(0, -325), new Vector2(720, 50), 22, Muted);
    }

    public void OfferSingleScore(int defeated)
    {
        if (scoreClient == null || CurrentScreen != Screen.Playing || !master.IsSingleMode || master.IsTutorialMode || ScoreDialogOpen) return;
        scoreRunId = Guid.NewGuid().ToString(); scoreDifficulty = SelectedScoreDifficulty;
        scoreDefeated = defeated; scoreSubmission = null; scoreQualified = false;
        scoreChoice = 0; ResetMenuGestures();
        scoreDialog = Canvas("Score registration", 1230);
        Image dim = FullScreen(scoreDialog.transform, "Score backdrop").gameObject.AddComponent<Image>();
        dim.color = new Color(0f, .06f, .10f, .66f);
        Image card = Box(scoreDialog.transform, "Score entry panel", Vector2.zero, new Vector2(920, 520), new Color(.97f, .99f, 1f));
        scoreTitle = Label(card.transform, "", new Vector2(0, 175), new Vector2(840, 70), 42, Sky);
        scoreDetail = Label(card.transform, defeated + "人撃破", new Vector2(0, 110), new Vector2(840, 52), 30, Ink);
        scoreDetail.supportRichText = false;
        Image input = Box(card.transform, "Score name", Vector2.zero, new Vector2(720, 80), Color.white);
        Outline(input, Sky);
        scoreName = input.gameObject.AddComponent<InputField>();
        scoreName.targetGraphic = input;
        scoreName.textComponent = Label(input.transform, "", Vector2.zero, new Vector2(670, 68), 30, Ink, TextAnchor.MiddleLeft);
        scoreName.textComponent.supportRichText = false;
        scoreName.placeholder = Label(input.transform, "名前（12文字まで）", Vector2.zero, new Vector2(670, 68), 28, Muted, TextAnchor.MiddleLeft);
        scoreName.characterLimit = 12; scoreName.lineType = InputField.LineType.SingleLine;
        scoreName.navigation = new Navigation { mode = Navigation.Mode.None };
        scoreName.onValidateInput = (text, index, character) => char.IsControl(character) ? '\0' : character;
        scoreName.onValueChanged.AddListener(_ => RefreshScoreActions());
        scoreFeedback = Label(card.transform, "", new Vector2(0, -85), new Vector2(840, 65), 23, Muted);
        scoreSave = Button(card.transform, "登録する", new Vector2(185, -180), new Vector2(310, 70), Pale,
            () => { if (scoreQualified) SubmitSingleScore(); else CheckSingleScore(); });
        scoreSkip = Button(card.transform, "スキップ", new Vector2(-185, -180), new Vector2(310, 70), Pale, CloseScoreDialog);
        scoreSaveFocus = scoreSave.gameObject.AddComponent<Outline>(); scoreSaveFocus.effectDistance = new Vector2(3, -3);
        scoreSkipFocus = scoreSkip.gameObject.AddComponent<Outline>(); scoreSkipFocus.effectDistance = new Vector2(3, -3);
        inputAfter = Time.unscaledTime + .25f;
        CheckSingleScore();
    }

    private void CheckSingleScore()
    {
        if (scoreWorking || !ScoreDialogOpen) return;
        int revision = ++scoreRevision;
        scoreWorking = true; scoreQualified = false;
        scoreTitle.text = "記録を確認中…"; scoreFeedback.text = "";
        scoreName.gameObject.SetActive(false); scoreSave.gameObject.SetActive(false);
        scoreSkip.interactable = true; scoreChoice = 1;
        RefreshScoreActions();
        StartCoroutine(scoreClient.Qualify(scoreDifficulty, scoreDefeated, (reply, error) => {
            if (revision != scoreRevision || !ScoreDialogOpen) return;
            scoreWorking = false;
            if (error != null)
            {
                scoreTitle.text = "ランキングに接続できません";
                scoreFeedback.text = error;
                scoreSave.gameObject.SetActive(true); scoreSave.GetComponentInChildren<Text>().text = "再試行";
                scoreChoice = 0; RefreshScoreActions(); return;
            }
            CacheLeaderboard(reply);
            if (!reply.qualified) { CloseScoreDialog(); return; }
            scoreQualified = true; scoreChoice = 0;
            scoreServerId = reply.serverId;
            scoreTitle.text = reply.rank + "位にランクイン！";
            scoreFeedback.text = "名前を入力してください";
            scoreName.gameObject.SetActive(true); scoreSave.gameObject.SetActive(true);
            scoreSave.GetComponentInChildren<Text>().text = "登録する";
            RefreshScoreActions(); scoreName.Select(); scoreName.ActivateInputField();
        }));
    }

    private void SubmitSingleScore()
    {
        if (scoreWorking || !scoreQualified || !ScoreDialogOpen) return;
        if (scoreSubmission == null)
        {
            string name = scoreName.text.Normalize(NormalizationForm.FormKC).Trim();
            if (!ValidScoreName(name))
            { scoreFeedback.text = "名前を1〜12文字で入力してください"; return; }
            scoreSubmission = new ScoreSubmission { runId = scoreRunId, name = name, difficulty = scoreDifficulty, defeated = scoreDefeated, serverId = scoreServerId };
        }
        // Keep the first payload on retries: a timed-out POST may already have
        // committed. Its stable runId lets the server return that same record.
        int revision = ++scoreRevision;
        scoreWorking = scoreSubmitting = true; scoreFeedback.text = "登録中…";
        RefreshScoreActions();
        StartCoroutine(scoreClient.Submit(scoreSubmission, (reply, error) => {
            if (revision != scoreRevision || !ScoreDialogOpen) return;
            scoreWorking = scoreSubmitting = false;
            if (error != null)
            {
                scoreFeedback.text = error;
                scoreSave.GetComponentInChildren<Text>().text = "再試行";
                RefreshScoreActions(); return;
            }
            CacheLeaderboard(reply);
            scoreQualified = false; scoreName.gameObject.SetActive(false); scoreSave.gameObject.SetActive(false);
            scoreSkip.GetComponentInChildren<Text>().text = "つづける";
            scoreSkip.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -180);
            scoreChoice = 1;
            if (reply.code == "NOT_QUALIFIED")
            {
                scoreTitle.text = "ランキングが更新されました";
                scoreDetail.text = scoreDefeated + "人撃破";
                scoreFeedback.text = "今回の記録は10位圏外になりました";
            }
            else
            {
                scoreTitle.text = "記録しました";
                scoreDetail.text = reply.entry.name + "　" + reply.entry.defeated + "人撃破";
                scoreFeedback.text = reply.rank + "位に登録しました";
            }
            RefreshScoreActions(); ClearSelection();
        }));
    }

    private void CacheLeaderboard(ScoreboardReply reply)
    {
        if (reply.difficulty != leaderboardDifficulty) return;
        leaderboard = reply; leaderboardMessage = ""; leaderboardRefreshAt = Time.unscaledTime + 10f;
    }

    private static bool ValidScoreName(string name)
    {
        if (name.Length == 0 || name.Length > 12) return false;
        for (int i = 0; i < name.Length; i++)
        {
            System.Globalization.UnicodeCategory category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(name, i);
            if (category == System.Globalization.UnicodeCategory.Control || category == System.Globalization.UnicodeCategory.Format
                || category == System.Globalization.UnicodeCategory.Surrogate || category == System.Globalization.UnicodeCategory.PrivateUse
                || category == System.Globalization.UnicodeCategory.OtherNotAssigned) return false;
            if (char.IsHighSurrogate(name[i])) i++;
        }
        return true;
    }

    private void RefreshScoreActions()
    {
        scoreName.interactable = !scoreWorking && scoreSubmission == null;
        scoreSave.interactable = !scoreWorking && (!scoreQualified || !string.IsNullOrWhiteSpace(scoreName.text));
        scoreSkip.interactable = !scoreSubmitting;
        scoreSaveFocus.effectColor = scoreChoice == 0 ? Sky : Pale;
        scoreSkipFocus.effectColor = scoreChoice == 1 ? Sky : Pale;
    }

    private void UpdateScoreDialog()
    {
        Keyboard keyboard = Keyboard.current;
        if (!scoreSubmitting && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        { CloseScoreDialog(); return; }
        for (int i = 0; i < 2; i++)
        {
            MotionController sensor = motion[i];
            ReadReset(i, sensor);
            bool guard = sensor != null && sensor.GuardHeld;
            bool confirm = guard && !guardWasHeld[i]; guardWasHeld[i] = guard;
            // WASD and Shift belong to name entry while this dialog is open.
            Vector2Int move = ReadMove(i, null);
            if (Time.unscaledTime < inputAfter) continue;
            if (move != Vector2Int.zero && scoreSave.gameObject.activeSelf)
            { scoreChoice = move.x < 0 || move.y < 0 ? 1 : 0; RefreshScoreActions(); }
            if (!confirm) continue;
            Button selected = scoreChoice == 0 && scoreSave.gameObject.activeSelf ? scoreSave : scoreSkip;
            if (selected.IsInteractable()) { selected.onClick.Invoke(); return; }
        }
    }

    private void CloseScoreDialog()
    {
        scoreRevision++;
        if (scoreDialog != null) { scoreDialog.SetActive(false); Destroy(scoreDialog); scoreDialog = null; }
        scoreWorking = scoreSubmitting = false;
        ResetMenuGestures(); ClearSelection(); inputAfter = Time.unscaledTime + .25f;
        Keyboard keyboard = Keyboard.current;
        for (int i = 0; i < 2; i++) guardWasHeld[i] = (motion[i] != null && motion[i].GuardHeld)
            || (keyboard != null && (i == 0 ? keyboard.leftShiftKey.isPressed : keyboard.rightShiftKey.isPressed));
    }

    public void ResetSingleScore()
    {
        CloseScoreDialog(); scoreSubmission = null; scoreRunId = null;
    }
}
