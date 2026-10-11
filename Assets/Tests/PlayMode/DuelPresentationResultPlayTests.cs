using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class DuelPresentationResultPlayTests
{
    private MonoBehaviour flow, master, presentation, phone;
    private static MonoBehaviour Find(string name) => Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(x => x.GetType().Name == name);
    private static object Get(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        flow = Find("GameFlowController"); master = Find("MasterController");
        presentation = Find("DuelPresentation"); phone = Find("PhoneControllerHub");
    }

    private void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "MotionController") component.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        if (phone != null) Object.Destroy(phone.gameObject);
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "HardwareControllerHub") Object.Destroy(component.gameObject);
        yield return null;
    }

    private void Begin(bool single)
    {
        Call(flow, "ChooseMode", single); Call(flow, "Ready", 0);
        if (!single) Call(flow, "Ready", 1);
        Assert.That(Get(flow, "IsPlaying"), Is.True);
    }

    private void ShowResult(bool single)
    {
        if (single) Call(presentation, "ShowSingleResult", new ShowdownProgress());
        else
        {
            Call(presentation, "ShowWinner", MatchWinner.Draw, 0d);
            Call(presentation, "Tick", 1d, false);
            Assert.That(presentation.GetComponentsInChildren<Text>().First(t => t.name == "Winner").text, Is.EqualTo("引き分け！"));
        }
        Assert.That(Get(presentation, "ResultVisible"), Is.True);
    }

    [UnityTest]
    public IEnumerator PlayingAndResultKeepStateAndActionsWithoutBackgroundsOrInstructions()
    {
        foreach (bool single in new[] { false, true })
        {
            Begin(single);
            master.enabled = false;
            Call(presentation, "Tick", 0d, false);
            Transform hud = presentation.transform.Find("Match presentation");
            Assert.That(hud.GetComponentsInChildren<Text>(true).Any(t => t.name.Contains("controls")), Is.False);
            Assert.That(hud.GetComponentsInChildren<Image>(true).Any(i => i.name.Contains("plate") || i.name == "Phase ribbon"), Is.False);
            Assert.That(hud.Find(single ? "Showdown progress" : "P1 score").gameObject.activeSelf, Is.True, "Match state remains visible.");
            ShowResult(single);
            Call(presentation, "Tick", 1d, false);
            Transform result = presentation.transform.Find(single ? "Single game over" : "Match result");
            Assert.That(result.GetComponentsInChildren<Text>(true).Any(t => t.name.Contains("controls")), Is.False);
            Assert.That(result.GetComponentsInChildren<Image>(true).All(i => i.GetComponent<Button>() != null), Is.True, "Only clickable actions have UI surfaces.");
            Assert.That(result.GetComponentsInChildren<Button>().Length, Is.EqualTo(3));
            Assert.That(presentation.GetComponentsInChildren<Text>(true).Any(t => t.name == "Keyboard guidance"), Is.False);
            Call(flow, "ReturnToModes");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator LaterSingleSectionsUseTheFullIntroAndTheirFadeClearsOnMenuAndReset()
    {
        Begin(true); master.enabled = false;
        var progress = new ShowdownProgress();
        Call(presentation, "UpdateShowdown", progress, 9d, 0d, 1, true, .4f);
        foreach (object view in Field<System.Array>(presentation, "views"))
            Assert.That(Field<bool>(view, "ownBodyFade"), Is.False, "Walking between sections should show the player's opaque body.");
        Call(presentation, "SetShowdownFade", 1f);
        Image fade = presentation.transform.Find("Match presentation/Showdown transition fade").GetComponent<Image>();
        Assert.That(fade.gameObject.activeInHierarchy, Is.True);
        Assert.That(fade.color, Is.EqualTo(Color.black));
        Assert.That(fade.raycastTarget, Is.False);
        Assert.That(fade.rectTransform.anchorMin, Is.EqualTo(Vector2.zero));
        Assert.That(fade.rectTransform.anchorMax, Is.EqualTo(Vector2.one));
        object firstAvatar = Field<System.Array>(presentation, "avatars").GetValue(0);
        Transform player = Field<Transform>(firstAvatar, "player");
        player.position += Vector3.right * 4f;
        Call(presentation, "ResetShowdownView");
        Assert.That(Field<Vector3>(firstAvatar, "renderedOrigin"), Is.EqualTo(player.position), "The opaque arrival must reset old planted-foot and body travel history.");
        Assert.That(fade.gameObject.activeSelf, Is.True);
        Assert.That(fade.color.a, Is.EqualTo(1f), "Camera and foot reset must preserve the full transition fade.");
        Call(presentation, "SetShowdownFade", .4f);
        Assert.That(fade.color.a, Is.EqualTo(.4f));
        Call(presentation, "SetMenuPresentation", true);
        Assert.That(fade.gameObject.activeSelf, Is.False, "Returning to a menu must clear a partially completed transition.");
        Call(presentation, "SetMenuPresentation", false);
        Intro(10, 10, 2); Assert.That(HudText("Phase"), Is.EqualTo("2区間目")); AssertCinematic(1f);
        foreach (object view in Field<System.Array>(presentation, "views"))
            Assert.That(Field<bool>(view, "ownBodyFade"), Is.True, "Normal play restores the own-view fade.");
        Intro(10.65, 10, 2); AssertCinematic(.5f);
        Intro(11.29, 10, 2); Assert.That(HudText("Phase"), Is.EqualTo("2区間目"));
        Intro(11.31, 10, 2); Assert.That(HudText("Phase"), Is.EqualTo("かまえて！")); AssertCinematic(0f);
        Intro(12.49, 10, 2); Assert.That(HudText("Phase"), Is.EqualTo("かまえて！"));
        Intro(12.51, 10, 2); Assert.That(HudText("Phase"), Is.EqualTo("はじめ！"));
        Assert.That(Field<RoundPhase>(presentation, "lastPhase"), Is.EqualTo(RoundPhase.Fight));
        Call(presentation, "SetShowdownFade", 1f);
        Call(presentation, "ResetEffects");
        Assert.That(fade.gameObject.activeSelf, Is.False, "A retry must not inherit the old section's blackout.");
        yield return null;
        void Intro(double now, double started, int section)
            => Call(presentation, "UpdateShowdown", progress, now, started, section, false, 0f);
    }

    [UnityTest]
    public IEnumerator SingleAndEachNewSectionShowRoundReadyAndFightWithoutRepeatingPhaseCues()
    {
        Begin(true); master.enabled=false;
        var progress=new ShowdownProgress();
        Intro(10,10,1);
        Assert.That(HudText("Phase"),Is.EqualTo("100人組み手"));
        Assert.That(Field<RoundPhase>(presentation,"lastPhase"),Is.EqualTo(RoundPhase.Round));
        AssertCinematic(1f);
        Intro(10.65,10,1);AssertCinematic(.5f);
        Assert.That(Field<double>(presentation,"flowPulse"),Is.EqualTo(10d),"Repeated Round frames keep the same transition pulse.");
        Intro(11.29,10,1);Assert.That(HudText("Phase"),Is.EqualTo("100人組み手"));
        Intro(11.31,10,1);
        Assert.That(HudText("Phase"),Is.EqualTo("かまえて！"));
        Assert.That(Field<RoundPhase>(presentation,"lastPhase"),Is.EqualTo(RoundPhase.Ready));
        AssertCinematic(0f);
        Intro(12.49,10,1);
        Assert.That(Field<double>(presentation,"flowPulse"),Is.EqualTo(11.31d),"Ready is entered once, so its sound is not replayed every frame.");
        Intro(12.51,10,1);
        Assert.That(HudText("Phase"),Is.EqualTo("はじめ！"));
        Assert.That(Field<RoundPhase>(presentation,"lastPhase"),Is.EqualTo(RoundPhase.Fight));
        Intro(13.21,10,1);Assert.That(HudText("Phase"),Is.Empty);
        Assert.That(Field<double>(presentation,"flowPulse"),Is.EqualTo(12.51d));

        Call(presentation,"UpdateShowdown",progress,20d,10d,1,true,.3f);
        Assert.That(HudText("Phase"),Is.EqualTo("区間クリア！"));AssertCinematic(0f);
        Call(presentation,"UpdateShowdown",progress,21d,10d,1,true,.8f);
        Assert.That(HudText("Phase"),Is.EqualTo("次の区間へ"));
        Intro(22,22,2);
        Assert.That(HudText("Phase"),Is.EqualTo("2区間目"));
        Assert.That(HudText("Round clock"),Is.EqualTo("区間 02 / 10"));AssertCinematic(1f);
        Intro(22.65,22,2);AssertCinematic(.5f);
        Intro(23.29,22,2);Assert.That(HudText("Phase"),Is.EqualTo("2区間目"));
        Intro(23.31,22,2);Assert.That(HudText("Phase"),Is.EqualTo("かまえて！"));
        Intro(24.49,22,2);Assert.That(HudText("Phase"),Is.EqualTo("かまえて！"));
        Assert.That(Field<double>(presentation,"flowPulse"),Is.EqualTo(23.31d),"Later sections also enter Ready only once.");
        Intro(24.51,22,2);Assert.That(HudText("Phase"),Is.EqualTo("はじめ！"));
        Assert.That(Field<double>(presentation,"flowPulse"),Is.EqualTo(24.51d));AssertCinematic(0f);
        Call(presentation,"SetMatchMode",true);Intro(30,30,1);
        Assert.That(HudText("Phase"),Is.EqualTo("100人組み手"));AssertCinematic(1f);
        yield return null;

        void Intro(double now,double started,int section)=>Call(presentation,"UpdateShowdown",progress,now,started,section,false,0f);
    }

    [UnityTest]
    public IEnumerator SimultaneousThresholdShowsUnlimitedFinalRoundAndKeepsPlayingAfterAnotherDraw()
    {
        Begin(false);
        master.enabled = false;
        var series = new DuelSeries(); series.Reset(0);
        double now = 0;
        for (int round = 1; round <= 3; round++)
        {
            now = EnterFight(series, now);
            now += DuelSeries.FightSeconds;
            Assert.That(series.Tick(now), Is.True);
        }
        Call(presentation, "UpdateDuelFlow", series, now + 1.5);
        Assert.That(HudText("Phase"), Is.EqualTo("最終決戦"));
        Assert.That(HudText("Round clock"), Is.EqualTo("時間無制限"));
        Assert.That(Get(presentation, "ResultVisible"), Is.False, "The tied third point must not open the match result.");

        now += 3.41; series.Tick(now);
        Call(presentation, "UpdateDuelFlow", series, now);
        Assert.That(HudText("Phase"), Is.EqualTo("最終決戦"));
        now = EnterFight(series, now);
        now += 3600;
        Assert.That(series.Tick(now), Is.False);
        Call(presentation, "UpdateDuelFlow", series, now);
        Assert.That(HudText("Round clock"), Is.EqualTo("時間無制限"));

        series.EndRound(MatchWinner.Draw, now);
        Call(presentation, "UpdateDuelFlow", series, now + 1.5);
        Assert.That(HudText("Phase"), Is.EqualTo("最終決戦"));
        Assert.That(HudText("P1 score"), Is.EqualTo("P1   ●●●"));
        Assert.That(HudText("P2 score"), Is.EqualTo("●●●   P2"));
        Assert.That(Get(presentation, "ResultVisible"), Is.False);
        now = EnterFight(series, now);
        series.EndRound(MatchWinner.Player2, now);
        Call(presentation, "UpdateDuelFlow", series, now + 1.5);
        Assert.That(HudText("Phase"), Is.EqualTo("P2 の一本！"));
        series.Tick(now + 3.41);
        Call(presentation, "ShowWinner", series.Winner, now + 3.41);
        Call(presentation, "Tick", now + 4.41, false);
        Assert.That(Get(presentation, "ResultVisible"), Is.True);
        Assert.That(HudText("Winner"), Is.EqualTo("P2 の勝ち！"));
        yield return null;
    }

    private string HudText(string name) => presentation.GetComponentsInChildren<Text>(true).First(t => t.name == name).text;

    private void AssertCinematic(float expected)
    {
        foreach (object view in Field<System.Array>(presentation, "views"))
            Assert.That(Field<float>(view, "cinematic"), Is.EqualTo(expected).Within(.001f));
    }

    private static double EnterFight(DuelSeries series, double now)
    {
        if (series.Phase == RoundPhase.Result) { now += 3.41; series.Tick(now); }
        if (series.Phase == RoundPhase.Versus) { now += 3.21; series.Tick(now); }
        now += 1.31; series.Tick(now);
        now += 1.21; series.Tick(now);
        Assert.That(series.Phase, Is.EqualTo(RoundPhase.Fight));
        return now;
    }

    [UnityTest]
    public IEnumerator SharedResultSelectionRoutesAllThreeChoicesForBothModes()
    {
        foreach (bool single in new[] { false, true })
        {
            for (int choice = 0; choice < 3; choice++)
            {
                Begin(single); ShowResult(single);
                Assert.That(Get(presentation, "ResultSelection"), Is.EqualTo(0));
                Call(presentation, "NavigateResult", Vector2Int.up);
                Assert.That(Get(presentation, "ResultSelection"), Is.EqualTo(2), "Either controller uses the same wrapped result focus.");
                Call(presentation, "NavigateResult", Vector2Int.down);
                for (int step = 0; step < choice; step++) Call(presentation, "NavigateResult", Vector2Int.down);
                Call(presentation, "ConfirmResult");
                Assert.That(Get(presentation, "ResultVisible"), Is.False, "A result action closes the current result.");
                Assert.That(Get(master, "IsSingleMode"), Is.EqualTo(single), "Returning or replaying preserves the chosen mode.");
                if (choice == 0)
                {
                    Assert.That(Get(flow, "IsPlaying"), Is.True);
                    Assert.That(Get(master, "IsPaused"), Is.False);
                    Call(flow, "ReturnToModes");
                }
                else
                {
                    Assert.That(Get(flow, "CurrentScreen").ToString(), Is.EqualTo(choice == 1 ? "Title" : "Characters"));
                    Assert.That(Get(master, "IsPaused"), Is.True);
                }
            }
        }
        yield return null;
    }
}
