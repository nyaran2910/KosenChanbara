using System;
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

public sealed class TutorialFlowPlayTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private MonoBehaviour master;
    private MonoBehaviour flow;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        MonoBehaviour[] components = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        master = components.Single(c => c.GetType().Name == "MasterController");
        flow = components.Single(c => c.GetType().Name == "GameFlowController");
    }

    private static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "MotionController"
                || component.GetType().Name == "HardwareControllerHub") component.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "HardwareControllerHub")
                Object.Destroy(component.gameObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator TutorialCountsLessonsThenShowsCompletionForTwoSecondsAndReturnsToTitle()
    {
        Button title = Buttons(Field<GameObject>(flow, "menuCanvas")).Single(b => Label(b) == "チュートリアル");
        title.onClick.Invoke();
        Assert.That(Get<bool>(master, "IsTutorialMode"), Is.True);
        Assert.That(Get<bool>(master, "IsSingleMode"), Is.True);
        Assert.That(Get<int>(master, "StageIndex"), Is.EqualTo(2));
        Assert.That(Get<object>(flow, "CurrentScreen").ToString(), Is.EqualTo("Characters"));
        Call(flow, "Ready", 0);
        Assert.That(Get<object>(flow, "CurrentScreen").ToString(), Is.EqualTo("Playing"));
        yield return null;

        IList enemies = Field<IList>(master, "singleNpcs");
        Assert.That(enemies.Count, Is.EqualTo(1), "The tutorial uses one practice opponent, not the ten-person round.");
        object npc = enemies[0];
        object hero = Field<object>(master, "first");
        CombatPose player = (CombatPose)Call(master, "PlayerDefencePose", hero);
        CombatPose enemy = (CombatPose)Call(master, "NpcCombatPose", npc);
        Assert.That(Get<string>(master, "TutorialInstruction"), Is.EqualTo("コントローラは不安定だ。基本姿勢からリセットをして合わせろ(0/5)"));
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Not.Contain("キーボード"));
        Assert.That(Get<bool>(master, "TutorialComplete"), Is.False);

        master.enabled = flow.enabled = false;
        MonoBehaviour sensor = (MonoBehaviour)Field<Array>(flow, "motion").GetValue(0);
        object phone = Field<object>(sensor, "hub");
        object phoneSlot = Field<Array>(phone, "slots").GetValue(0);
        uint sequence = 0;
        void ResetPacket(bool held, ushort counter)
        {
            object sample = Activator.CreateInstance(Type.GetType("MotionSample, Assembly-CSharp", true), new object[] {
                Quaternion.identity, Vector3.zero, Vector3.zero, false, true, true, true,
                ++sequence, counter, sequence * 10d, Time.realtimeSinceStartupAsDouble, held
            });
            Set(phoneSlot, "LatestSample", sample); Set(phoneSlot, "HasSample", true); Set(phoneSlot, "ChannelOpen", true);
            Call(sensor, "Update");
            Call(flow, "ReadReset", 0, sensor);
        }
        ResetPacket(false, 0);
        // Long holds open the pause, but do not advance the reset lesson.
        ResetPacket(true, 0);
        yield return new WaitForSecondsRealtime(.51f);
        ResetPacket(true, 0);
        Assert.That(Get<bool>(flow, "PauseOpen"), Is.True);
        ResetPacket(false, 0);
        ResetPacket(true, 0); ResetPacket(false, 1);
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("0/5"),
            "A reset while paused must not score, and a long hold must not score.");
        Call(flow, "TogglePause");
        // Reconnecting establishes a baseline even if the device's counter changed.
        Set(phoneSlot, "ChannelOpen", false);
        Call(sensor, "Update"); Call(flow, "ReadReset", 0, sensor);
        ResetPacket(false, 42);
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("0/5"));
        for (ushort i = 1; i <= 5; i++)
        {
            ResetPacket(true, (ushort)(41 + i)); ResetPacket(false, (ushort)(42 + i));
            if (i < 5) Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain($"{i}/5"));
        }
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("ガードボタン"));
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("0/5"));

        var guard = new CombatResult(CombatOutcome.GuardSuccess, 1, 1, DuelMatch.GuardPush);
        for (int i = 0; i < 4; i++) Call(master, "ApplySingleContact", npc, guard, player, enemy, true);
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("4/5"));
        var receivedHit = new CombatResult(CombatOutcome.Hit, 1, 0, -DuelMatch.HitPush);
        Call(master, "ApplySingleContact", npc, receivedHit, player, enemy, false);
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("4/5"),
            "An unguarded mistake during the guard lesson must not count or reset the lesson.");
        Call(master, "ApplySingleContact", npc, guard, player, enemy, true);
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("0/5"));
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("平行"));
        double passiveTick = Get<double>(master, "MatchTime") + .016d;
        Set(master, "matchTime", passiveTick);
        Call(master, "TickTutorialMode", passiveTick, .016f);
        Assert.That((bool)Call(master, "SingleNpcGuarding", npc, passiveTick), Is.True,
            "The cut lesson partner must use the shared AI's passive guard state.");
        Assert.That((float)Field<object>(npc, "AttackTimer"), Is.LessThan(0f),
            "The partner must not carry an attack into a passive lesson.");

        var hit = new CombatResult(CombatOutcome.Hit, 0, 1, DuelMatch.HitPush);
        Set(master, "matchTime", Get<double>(master, "MatchTime") + .21d);
        int maximumHearts = (int)Field<object>(npc, "MaximumHearts");
        for (int i = 0; i < 5; i++)
        {
            if (i > 0) Set(master, "matchTime", Get<double>(master, "MatchTime") + .21d);
            Call(master, "ApplySingleContact", npc, hit, player, enemy, false);
            Assert.That((int)Field<object>(npc, "Hearts"), Is.EqualTo(maximumHearts),
                "The practice partner must survive each scored cut at full health.");
        }
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("直後は攻撃ができない"));
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("0/5"));

        string counterMessage = "ガードされた直後は攻撃ができない！ガードして反撃を防げ！ガードされてから反撃をガードで防ぐ(0/5)";
        Assert.That(Get<string>(master, "TutorialInstruction"), Is.EqualTo(counterMessage));
        Set(master, "matchTime", Get<double>(master, "MatchTime") + 10d);
        Call(flow, "UpdateTutorialOverlay");
        Assert.That(Field<Text>(flow, "tutorialPrompt").text, Is.EqualTo(counterMessage),
            "The complete rule and task must stay visible without an explanatory timeout.");
        var blocked = new CombatResult(CombatOutcome.GuardSuccess, 0, 0, DuelMatch.GuardPush);
        Call(master, "ApplySingleContact", npc, guard, player, enemy, true);
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("0/5"),
            "A standalone guard is not a blocked-attack/counter-defense cycle.");
        Call(master, "ApplySingleContact", npc, blocked, player, enemy, false);
        Set(master, "matchTime", Get<double>(master, "MatchTime") + .3d);
        Call(master, "ApplySingleContact", npc, receivedHit, player, enemy, false);
        Call(master, "ApplySingleContact", npc, guard, player, enemy, true);
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("0/5"), "A missed counter restarts the attempt.");
        void AssertPassiveBetweenCounters()
        {
            // Exercise real AI updates past recovery; contact-only tests used
            // to miss a leaked continuous-attack flag and the next attempt.
            for (int frame = 0; frame < 100; frame++)
            {
                double now = Get<double>(master, "MatchTime") + .02d;
                Set(master, "matchTime", now);
                Call(master, "TickTutorialMode", now, .02f);
                Assert.That(Field<bool>(npc, "ContinuousAttack"), Is.False);
                Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
            }
            Assert.That((bool)Call(master, "SingleNpcGuarding", npc, Get<double>(master, "MatchTime")), Is.True);
            Assert.That(Field<double>(npc, "NextAttack"), Is.EqualTo(double.PositiveInfinity));
        }
        AssertPassiveBetweenCounters(); // A failed counter must also stop chaining.
        for (int i = 0; i < 5; i++)
        {
            Call(master, "ApplySingleContact", npc, blocked, player, enemy, false);
            Assert.That(Field<bool>(npc, "ContinuousAttack"), Is.True);
            Set(master, "matchTime", Get<double>(master, "MatchTime") + .3d);
            Call(master, "ApplySingleContact", npc, guard, player, enemy, true);
            if (i < 4)
            {
                Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain($"{i + 1}/5"));
                Assert.That(Field<bool>(npc, "ContinuousAttack"), Is.False);
                AssertPassiveBetweenCounters();
            }
        }
        string hitMessage = "攻撃に当たった直後も攻撃はできない！ガードをして攻撃を止めろ！攻撃をされた直後にガードをして攻撃を止める(0/5)";
        Assert.That(Get<string>(master, "TutorialInstruction"), Is.EqualTo(hitMessage));
        Set(master, "matchTime", Get<double>(master, "MatchTime") + 10d);
        Call(flow, "UpdateTutorialOverlay");
        Assert.That(Field<Text>(flow, "tutorialPrompt").text, Is.EqualTo(hitMessage));
        Assert.That(Field<Text>(flow, "tutorialPrompt").preferredHeight,
            Is.LessThanOrEqualTo(Field<Text>(flow, "tutorialPrompt").rectTransform.rect.height));
        Call(master, "ApplySingleContact", npc, guard, player, enemy, true);
        Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain("0/5"));
        for (int i = 0; i < 5; i++)
        {
            Set(master, "matchTime", Get<double>(master, "MatchTime") + .3d);
            Call(master, "ApplySingleContact", npc, receivedHit, player, enemy, false);
            Set(master, "matchTime", Get<double>(master, "MatchTime") + .3d);
            Call(master, "ApplySingleContact", npc, guard, player, enemy, true);
            if (i < 4) Assert.That(Get<string>(master, "TutorialInstruction"), Does.Contain($"{i + 1}/5"));
            Assert.That(Get<bool>(master, "IsTutorialMode"), Is.True, "The active contact must finish before the arena changes.");
        }
        double completedAt = Get<double>(master, "MatchTime") + .016d;
        Set(master, "matchTime", completedAt);
        Call(master, "TickTutorialMode", completedAt, .016f);
        Assert.That(Get<bool>(master, "IsTutorialMode"), Is.True);
        Assert.That(Get<bool>(master, "TutorialComplete"), Is.True);
        Call(flow, "UpdateTutorialOverlay");
        Assert.That(Field<Text>(flow, "tutorialPrompt").text, Does.Contain("クリア"));
        Set(master, "matchTime", completedAt + 1.99d);
        Call(master, "TickTutorialMode", completedAt + 1.99d, .016f);
        Assert.That(Get<object>(flow, "CurrentScreen").ToString(), Is.EqualTo("Playing"));
        Set(master, "matchTime", completedAt + 2.01d);
        Call(master, "TickTutorialMode", completedAt + 2.01d, .016f);
        Assert.That(Get<bool>(master, "IsFreeMode"), Is.False);
        Assert.That(Get<object>(flow, "CurrentScreen").ToString(), Is.EqualTo("Title"));
        Assert.That(Get<bool>(master, "IsTutorialMode"), Is.False);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name, Flags).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static object Call(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    private static Button[] Buttons(GameObject root) => root.GetComponentsInChildren<Button>(true);
    private static string Label(Button button) => button.GetComponentInChildren<Text>().text;
}
