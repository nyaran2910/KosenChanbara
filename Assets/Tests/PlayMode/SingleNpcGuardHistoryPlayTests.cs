using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class SingleNpcGuardHistoryPlayTests
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
    object Spawn()
    {
        Call(master, "SpawnSingleNpc");
        object npc = Field<IList>(master, "singleNpcs")[0];
        Transform player = Field<GameObject>(master, "PlayerObject1").transform, root = Field<GameObject>(npc, "Root").transform;
        root.position = player.position + player.forward * 2.35f; root.LookAt(player.position);
        Set(master, "singleActiveNpc", npc);
        Set(npc, "ContinuousAttack", false); Set(npc, "AttackTimer", .2f);
        Call(master, "UpdateNpcGuard", npc, 0f);
        return npc;
    }
    [UnityTest] public IEnumerator EnteringANpcGuardPublishesTheNewPoseImmediatelyLikeAPlayerGuardInput()
    {
        object npc = Spawn(); Transform root = Field<GameObject>(npc, "Root").transform;
        Vector3 formerGrip = root.position + root.forward * .5f, formerBlade = root.forward;
        Set(npc, "PreviousGrip", formerGrip); Set(npc, "PreviousBlade", formerBlade);
        Set(npc, "GuardOctant", 2); Set(npc, "AttackTimer", -1f); Set(npc, "StunnedUntil", 4d);
        Call(master, "UpdateNpcGuard", npc, 0f);
        CombatPose guard = (CombatPose)Call(master, "NpcCombatPose", npc);
        Assert.That(Vector3.Distance(guard.PreviousGrip, guard.Grip), Is.LessThan(.00001f));
        Assert.That(Vector3.Angle(guard.PreviousBlade, guard.Blade), Is.LessThan(.01f));
        Assert.That(Vector3.Angle(formerBlade, guard.Blade), Is.GreaterThan(45f), "The fixture must actually change from attack to guard.");
        Assert.That(guard.AngularMoving, Is.False, "A fresh guard does not defend with or sweep its former non-guard posture.");
        yield return null;
    }
    [UnityTest] public IEnumerator ContinuingNpcGuardsKeepTheirRealPreviousPoseWhenChangingDirection()
    {
        object npc = Spawn(); Set(npc, "GuardOctant", 2); Set(npc, "AttackTimer", -1f);
        Call(master, "UpdateNpcGuard", npc, 0f);
        CombatPose before = (CombatPose)Call(master, "NpcCombatPose", npc);
        Set(npc, "PreviousGrip", before.Grip); Set(npc, "PreviousBlade", before.Blade);
        Set(npc, "GuardOctant", 3); Call(master, "UpdateNpcGuard", npc, .02f);
        CombatPose after = (CombatPose)Call(master, "NpcCombatPose", npc);
        Assert.That(Vector3.Distance(after.PreviousGrip, before.Grip), Is.LessThan(.00001f));
        Assert.That(Vector3.Angle(after.PreviousBlade, before.Blade), Is.LessThan(.01f));
        Assert.That(after.AngularMoving, Is.True, "A continuing guard's actual movement still participates in the simultaneous sweep.");
        yield return null;
    }
    [UnityTest] public IEnumerator LeavingGuardForAnAuthoredSwingDoesNotResetTheAttackHistory()
    {
        object npc = Spawn(); Set(npc, "AttackTimer", -1f); Call(master, "UpdateNpcGuard", npc, 0f);
        Transform root = Field<GameObject>(npc, "Root").transform;
        Vector3 preparedGrip = root.position + root.forward * .45f, preparedBlade = root.up;
        Set(npc, "PreviousGrip", preparedGrip); Set(npc, "PreviousBlade", preparedBlade);
        Set(npc, "AttackTimer", .1f); Call(master, "UpdateNpcGuard", npc, 0f);
        Set(npc, "VisualBasePose", new SwordPose(new Vector3(0f, .55f, .45f), Quaternion.identity));
        CombatPose attack = (CombatPose)Call(master, "NpcCombatPose", npc);
        Assert.That(Vector3.Distance(attack.PreviousGrip, preparedGrip), Is.LessThan(.00001f));
        Assert.That(Vector3.Angle(attack.PreviousBlade, preparedBlade), Is.LessThan(.01f));
        Assert.That(attack.AngularMoving, Is.True, "New preparation and swing endpoints remain intact.");
        yield return null;
    }

    [UnityTest] public IEnumerator ACoarseAuthoredNpcCutStopsAtItsObservedImpactAndKeepsTheComboWait()
    {
        object npc = Spawn(), first = Field<object>(master, "first");
        Transform root = Field<GameObject>(npc, "Root").transform, sword = Field<Transform>(npc, "Sword");
        object avatar = Field<object>(npc, "Avatar");
        Vector3 chestOffset = (Vector3)avatar.GetType().GetProperty("ChestOffset").GetValue(avatar);
        var settings = new SwordPoseSettings(Field<Vector3>(npc, "ChestCenter") + chestOffset,
            Field<float>(master, "gripRadius"), Field<float>(master, "guardRadius"),
            Field<float>(master, "guardPlaneOffset"), Field<float>(master, "guardHeight"));
        CombatPose player = (CombatPose)Call(master, "PlayerDefencePose", first);
        Vector3 aim = ShowdownCombat.CutAim(root.InverseTransformPoint(player.BodyCenter + Vector3.up * .55f), settings);
        SwordPose ready = ShowdownCombat.CutPose(aim, Vector3.up, 0f, settings);
        // A coarse frame crosses almost the whole authored swing. Keep a tiny
        // forward component so the two observations define its real front arc.
        SwordPose late = ShowdownCombat.CutPose(aim, Vector3.up, .9f, settings);
        Set(npc, "PreviousGrip", root.TransformPoint(ready.Position));
        Set(npc, "PreviousBlade", root.TransformDirection(ready.Direction));
        Set(npc, "VisualBasePose", late);
        Set(npc, "AttackDirection", -root.up);
        sword.SetLocalPositionAndRotation(late.Position, late.Rotation);
        CombatPose enemy = (CombatPose)Call(master, "NpcCombatPose", npc);
        CombatResult result = CombatExchange.Resolve(AttackKind.None, AttackKind.Cut,
            player, enemy, false, false).Contact;
        Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(result.Attacker, Is.EqualTo(1));
        Assert.That(result.ContactFraction, Is.GreaterThan(0f).And.LessThan(1f),
            "The real swept blade must meet the player before this coarse frame ends.");
        Vector3 impactGrip = Vector3.Lerp(enemy.PreviousGrip, enemy.Grip, result.ContactFraction);
        Vector3 impactBlade = Vector3.Slerp(enemy.PreviousBlade, enemy.Blade, result.ContactFraction).normalized;
        Call(master, "ApplySingleContact", npc, result, player, enemy, false);
        SwordPose stopped = Field<SwordPose>(npc, "VisualBasePose");
        Assert.That(Vector3.Distance(root.TransformPoint(stopped.Position), impactGrip), Is.LessThan(.0001f));
        Assert.That(Vector3.Angle(root.TransformDirection(stopped.Direction), impactBlade), Is.LessThan(.01f));
        Assert.That(Vector3.Angle(stopped.Direction, late.Direction), Is.GreaterThan(20f),
            "Stopping must retain the contact posture, not the later follow-through.");
        Assert.That(Vector3.Distance(sword.position, impactGrip), Is.LessThan(.0001f));
        Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
        Assert.That(Field<bool>(npc, "ContactResolved"), Is.True);
        Assert.That(Field<bool>(npc, "ContinuousAttack"), Is.True);
        Assert.That(Field<double>(npc, "NextAttack"), Is.EqualTo(3d + ShowdownCombat.HitFollowupDelaySeconds).Within(.000001d));
        Assert.That(Field<double>(npc, "AttackNotBefore"), Is.EqualTo(Field<double>(npc, "NextAttack")));
        yield return null;
    }
}
