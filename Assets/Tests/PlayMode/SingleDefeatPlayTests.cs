using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class SingleDefeatPlayTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    MonoBehaviour master, flow;
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub", "MotionController" }.Contains(component.GetType().Name)) component.enabled = false;
    }
    [UnitySetUp] public IEnumerator Setup()
    {
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c => c.GetType().Name == "MasterController");
        flow = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c => c.GetType().Name == "GameFlowController");
        master.enabled = flow.enabled = false;
        Call(flow, "ChooseMode", true); Call(flow, "Ready", 0); Call(flow, "BeginMatch");
        Set(master, "matchTime", 3d);
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub" }.Contains(component.GetType().Name)) Object.Destroy(component.gameObject);
        yield return null;
    }
    object FatalHit()
    {
        var progress = Field<ShowdownProgress>(master, "showdown");
        double now = Field<double>(master, "matchTime");
        while (progress.Hearts > 2) Assert.That(progress.Damage(now), Is.True);
        Call(master, "SpawnSingleNpc");
        object npc = Field<IList>(master, "singleNpcs")[0], first = Field<object>(master, "first");
        Transform player = Field<Transform>(first, "Player"), root = Field<GameObject>(npc, "Root").transform;
        root.position = player.position + player.forward * 2f; root.LookAt(player.position);
        CombatPose human = (CombatPose)Call(master, "PlayerDefencePose", first), enemy = (CombatPose)Call(master, "NpcCombatPose", npc);
        Call(master, "ApplySingleContact", npc, new CombatResult(CombatOutcome.Hit, 1, 0, -DuelMatch.HitPush), human, enemy, false);
        Assert.That(progress.Hearts, Is.Zero);
        return npc;
    }
    void TickAt(double now)
    {
        double previous = Field<double>(master, "matchTime");
        Set(master, "matchTime", now);
        Call(master, "TickSingleMode", now, (float)(now - previous));
    }
    [UnityTest] public IEnumerator FatalHitFallsImmediatelyFinishesItsPushAndShowsTheResultAfterTwoGameSeconds()
    {
        object first = Field<object>(master, "first"); Transform player = Field<Transform>(first, "Player");
        Vector3 start = player.position, forward = player.forward;
        object npc = FatalHit(), presentation = Field<object>(master, "presentation");
        object avatar = Field<Array>(presentation, "avatars").GetValue(0), view = Field<Array>(presentation, "views").GetValue(0);
        Transform visual = Field<Transform>(avatar, "visual");
        Quaternion standing = visual.localRotation; Vector3 scale = visual.localScale;
        Assert.That(Field<bool>(avatar, "defeated"), Is.True);
        Assert.That(Field<bool>(presentation, "singleGameOverVisible"), Is.False);
        Assert.That(Field<bool>(view, "ownBodyFade"), Is.False);
        Assert.That(Field<bool>(view, "walkingView"), Is.True);
        Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
        TickAt(3.6d);
        Assert.That(Vector3.Distance(player.position, start - forward * DuelMatch.HitPush), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(standing, visual.localRotation), Is.GreaterThan(60f));
        TickAt(4.999d);
        Assert.That(Field<bool>(presentation, "singleGameOverVisible"), Is.False);
        Assert.That(Vector3.Distance(visual.localScale, scale), Is.LessThan(.00001f), "The player stays on the ground instead of disappearing like defeated enemies.");
        TickAt(5d);
        Assert.That(Field<bool>(presentation, "singleGameOverVisible"), Is.True);
        yield return null;
    }
    [UnityTest] public IEnumerator PauseFreezesTheDefeatClockAndRetryCancelsThePendingResult()
    {
        FatalHit(); TickAt(3.5d);
        object presentation = Field<object>(master, "presentation");
        Call(master, "SetPaused", true);
        for (int frame = 0; frame < 4; frame++) { yield return null; Call(master, "LateUpdate"); }
        Assert.That(Field<double>(master, "matchTime"), Is.EqualTo(3.5d));
        Assert.That(Field<bool>(presentation, "singleGameOverVisible"), Is.False);
        Call(master, "SetPaused", false); TickAt(4.999d);
        Assert.That(Field<bool>(presentation, "singleGameOverVisible"), Is.False);
        Call(master, "ResetMatch"); TickAt(5.1d);
        Assert.That(Field<bool>(master, "singleGameOver"), Is.False);
        Assert.That(double.IsNaN(Field<double>(master, "singleDefeatAt")), Is.True);
        Assert.That(Field<bool>(presentation, "singleGameOverVisible"), Is.False);
        Assert.That(Field<ShowdownProgress>(master, "showdown").Hearts, Is.EqualTo(ShowdownProgress.MaxHearts));
        Assert.That(Field<bool>(Field<Array>(presentation, "avatars").GetValue(0), "defeated"), Is.False);
    }

    [UnityTest] public IEnumerator ReturningToTitleCancelsTheFallAndItsPendingResult()
    {
        FatalHit(); TickAt(3.5d);
        Call(flow, "ReturnToTitle");
        object presentation = Field<object>(master, "presentation");
        Assert.That(Field<bool>(master, "singleGameOver"), Is.False);
        Assert.That(double.IsNaN(Field<double>(master, "singleDefeatAt")), Is.True);
        Assert.That(Field<bool>(presentation, "singleGameOverVisible"), Is.False);
        Assert.That(Field<bool>(Field<Array>(presentation, "avatars").GetValue(0), "defeated"), Is.False);
        double now = Field<double>(master, "matchTime");
        for (int frame = 0; frame < 3; frame++) { yield return null; Call(master, "LateUpdate"); }
        Assert.That(Field<double>(master, "matchTime"), Is.EqualTo(now));
        Assert.That(Field<bool>(presentation, "singleGameOverVisible"), Is.False);
    }
}
