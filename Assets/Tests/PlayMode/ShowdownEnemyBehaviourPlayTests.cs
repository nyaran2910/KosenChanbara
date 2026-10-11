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

public sealed class ShowdownEnemyBehaviourPlayTests
{
    private MonoBehaviour master, flow;
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static object Call(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, Fields).Invoke(target, args);
    private double Now => Field<double>(master, "matchTime");
    private Transform Player => Field<GameObject>(master, "PlayerObject1").transform;
    private ShowdownProgress Run => Field<ShowdownProgress>(master, "showdown");

    private static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "MotionController"
                || component.GetType().Name == "HardwareControllerHub") component.enabled = false;
    }

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
        master.enabled = flow.enabled = false;
        Call(flow, "ChooseMode", true);
        Call(flow, "Ready", 0);
        Call(flow, "BeginMatch");
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

    private object Spawn(Vector3 offset, int maximumHearts = 1)
    {
        Call(master, "SpawnSingleNpc");
        IList enemies = Field<IList>(master, "singleNpcs");
        object npc = enemies[enemies.Count - 1];
        Transform root = Field<GameObject>(npc, "Root").transform;
        root.position = Player.position + offset;
        root.rotation = Quaternion.LookRotation(Player.position - root.position);
        Set(npc, "WaitingAnchor", root.position);
        Set(npc, "MaximumHearts", maximumHearts);
        Set(npc, "Hearts", maximumHearts);
        Set(npc, "OpeningAttack", false);
        Set(npc, "ApproachAttack", false);
        Set(npc, "NextAttack", Now + 100d);
        Set(npc, "GuardOctant", 0);
        Call(master, "UpdateNpcGuard", npc, 0f);
        return npc;
    }

    private void AimAt(object npc, bool facePlayer = true)
    {
        Transform root = Field<GameObject>(npc, "Root").transform;
        Vector3 toward = root.position - Player.position; toward.y = 0f;
        if (facePlayer) Player.rotation = Quaternion.LookRotation(toward);
        Camera view = Player.GetComponentInChildren<Camera>();
        Vector3 origin = Player.TransformPoint(new Vector3(.35f, 1.6f, -3.4f));
        view.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(root.position + Vector3.up - origin));
    }

    private void MoveAt(double time, float delta = .01f, bool readyView = true)
    {
        Set(master, "matchTime", time);
        Call(master, "AdvanceSingleReaction", time);
        if (readyView)
        {
            object active = Field<object>(master, "singleActiveNpc");
            if (active != null)
            {
                AimAt(active);
                Set(active, "Engaged", true);
            }
        }
        Call(master, "MoveSingleNpcs", delta);
    }

    [UnityTest]
    public IEnumerator NewOpponentWaitsAtStagingDistanceUntilBodyAndCameraHaveSettled()
    {
        object npc = Spawn(Player.forward * 7f);
        Set(master, "singleActiveNpc", npc);
        Set(npc, "ApproachAttack", true);
        Set(npc, "NextAttack", 0d);
        Camera view = Player.GetComponentInChildren<Camera>();
        view.transform.rotation = Quaternion.LookRotation(-Player.forward);
        Transform root = Field<GameObject>(npc, "Root").transform;
        MoveAt(Now + 1d, 1f, false);
        MoveAt(Now + 1d, 1f, false);
        Assert.That(Vector3.Distance(root.position, Player.position), Is.EqualTo(ShowdownCombat.NextOpponentRange).Within(.001f));
        Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f), "Even normal approach attacks wait for the real camera.");
        Player.rotation *= Quaternion.Euler(0f, 90f, 0f);
        AimAt(npc, false);
        MoveAt(Now + 1d, .1f, false);
        Assert.That(Field<bool>(npc, "Engaged"), Is.False, "Camera readiness does not replace player facing.");
        Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
        AimAt(npc);
        double alignedAt = Now;
        MoveAt(alignedAt, 0f, false);
        MoveAt(alignedAt + ShowdownCombat.FocusHoldSeconds - .001d, 0f, false);
        Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f), "The newly visible opponent leaves a brief guard interval.");
        MoveAt(alignedAt + ShowdownCombat.FocusHoldSeconds + .001d, .001f, false);
        Assert.That(Field<bool>(npc, "Engaged"), Is.True);
        Assert.That(Field<float>(npc, "AttackTimer"), Is.GreaterThan(Field<float>(npc, "SwingSeconds") + .49f));
        yield return null;
    }

    [UnityTest]
    public IEnumerator ActualEnemyPreparationAndCutUseEachStagesDifficultyIncludingCounters()
    {
        foreach(bool easy in new[]{false,true})
        for(int section=1;section<=10;section++)
        {
            Call(master,"SetGameplaySuspended",true);Call(master,"SetSingleDifficulty",easy);Call(master,"ResetMatch");
            Field<ShowdownCourse>(master,"singleCourse").GetType().GetProperty("SectionNumber",Fields).SetValue(Field<ShowdownCourse>(master,"singleCourse"),section);
            Call(master,"SetGameplaySuspended",false);
            object npc=Spawn(Player.forward*1.8f);Set(master,"singleActiveNpc",npc);Set(npc,"NextAttack",Now);
            if(!easy)Set(master,"singleStunnedUntil",Now+.5d);
            double start=Now;MoveAt(start,0f);
            var timing=new ShowdownDifficulty(section,easy);
            Assert.That(Field<float>(npc,"WindupSeconds"),Is.EqualTo(timing.WindupSeconds).Within(.000001f),
                "Even a counter keeps the real stage's preparation speed.");
            Assert.That(Field<float>(npc,"SwingSeconds"),Is.EqualTo(timing.SwingSeconds).Within(.000001f));
            Set(npc,"AttackReadyDirection",Vector3.up);
            MoveAt(start+timing.WindupSeconds,timing.WindupSeconds);
            for(int quarter=1;quarter<=3;quarter++)
            {
                MoveAt(start+timing.WindupSeconds+timing.SwingSeconds*quarter/4d,timing.SwingSeconds/4f);
                SwordPose actual=Field<SwordPose>(npc,"VisualBasePose");
                SwordPose expected=ShowdownCombat.CutPose(Field<Vector3>(npc,"AttackAim"),Vector3.up,quarter/4f,
                    new SwordPoseSettings(Field<Vector3>(npc,"ChestCenter")));
                Assert.That(Vector3.Angle(actual.Direction,expected.Direction),Is.LessThan(.05f),
                    "The displayed cut, rather than just the timing field, must follow the stage speed.");
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator LandedHitStartsContinuousCutsWhileEasyWaitsForPlayerRecovery()
    {
        foreach (bool easy in new[] { false, true })
        {
            Call(master, "SetGameplaySuspended", true);
            Call(master, "SetSingleDifficulty", easy);
            Call(master, "ResetMatch");
            Call(master, "SetGameplaySuspended", false);
            object npc = Spawn(Player.forward * 1.8f);
            Set(master, "singleActiveNpc", npc);
            object player = Field<object>(master, "first");
            CombatPose defence = (CombatPose)Call(master, "PlayerDefencePose", player);
            Vector3 grip = defence.BodyCenter + Player.forward * 2f;
            var cut = new CombatPose(grip, Player.right, grip, -Player.forward,
                Field<GameObject>(npc, "Root").transform.position, -Player.forward, -Player.up, grip, -Player.forward);
            CombatResult hit = CombatExchange.Resolve(AttackKind.None, AttackKind.Cut, defence, cut, false, false).Contact;
            Assert.That(hit.Outcome, Is.EqualTo(CombatOutcome.Hit), "The continuous mode follows a real shared-geometry hit.");
            double hitAt = Now;
            Call(master, "ApplySingleContact", npc, hit, defence, cut, false);
            Assert.That(Field<bool>(npc, "ContinuousAttack"), Is.True);
            Assert.That(Field<double>(npc, "NextAttack"), Is.EqualTo(hitAt + .3d));
            Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts - 2));
            Assert.That((bool)Call(master,"SingleNpcGuarding",npc,hitAt+.01d),Is.False,
                "Continuous attackers cannot insert a logical guard while waiting for the next cut.");
            MoveAt(hitAt + .01d, .01f);
            Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
            MoveAt(hitAt + .299d, .01f);
            Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f),
                "A hit must leave 0.3 seconds before the next preparation, even while the player is staggered.");
            MoveAt(hitAt + .3d, 0f);
            if (easy)
            {
                Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
                MoveAt(Field<double>(master, "singleStunnedUntil") - .001d, 0f);
                Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f), "Continuous easy attackers still wait through player stagger.");
                MoveAt(Field<double>(master, "singleStunnedUntil") + .001d, 0f);
            }
            Assert.That(Field<float>(npc, "AttackTimer"), Is.GreaterThan(Field<float>(npc, "SwingSeconds") + .48f));
            Assert.That(Field<AttackStroke>(npc, "Stroke").Active, Is.False, "Every continuous cut still starts with its warning.");
            // Finishing a later miss no longer inserts the ordinary guard wait.
            Set(npc, "AttackTimer", 0f);
            Field<AttackStroke>(npc, "Stroke").Reset();
            Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f));
            Assert.That(Field<double>(npc, "NextAttack"), Is.EqualTo(Now));
            MoveAt(Now, 0f);
            Assert.That(Field<float>(npc, "AttackTimer"), Is.GreaterThan(Field<float>(npc, "SwingSeconds") + .49f));
            yield return null;
        }
    }

    private CombatResult Contact(object npc, bool guard)
    {
        if (guard) Call(master, "UpdateNpcGuard", npc, 0f);
        CombatPose enemy = (CombatPose)Call(master, "NpcCombatPose", npc);
        CombatPose thrust = ThrustThroughDefence(enemy, guard);
        CombatResult result = CombatExchange.Resolve(AttackKind.Thrust, AttackKind.None,
            thrust, enemy, false, guard).Contact;
        Assert.That(result.Outcome, Is.EqualTo(guard ? CombatOutcome.GuardSuccess : CombatOutcome.Hit),
            "The fixture must make a real shared-geometry contact before checking AI policy.");
        Assert.That(result.Attacker, Is.EqualTo(0));
        Call(master, "ApplySingleContact", npc, result, thrust, enemy, false);
        return result;
    }

    private static CombatPose ThrustThroughDefence(CombatPose enemy, bool crossBlade)
    {
        // Guard fixtures pass through the actual blade, beyond its flat base,
        // then continue toward the body. A chest-only thrust can hit the hilt
        // and must no longer be treated as a successful guard.
        Vector3 intercept = crossBlade ? enemy.Grip + enemy.Blade.normalized * .8f : enemy.BodyCenter;
        Vector3 direction = crossBlade ? (enemy.BodyCenter - intercept).normalized : -enemy.Forward.normalized;
        Vector3 previousGrip = intercept - direction * 3f;
        Vector3 grip = enemy.BodyCenter - direction * .8f;
        return new CombatPose(previousGrip, direction, grip, direction,
            previousGrip - direction * .5f, direction, direction, grip, direction);
    }

    [UnityTest]
    public IEnumerator WaitingEnemyCannotGuardOrRetaliateEvenWhenItsBladeCouldDefend()
    {
        object active = Spawn(Player.right * 6f);
        object waiting = Spawn(Player.forward * 2.6f, 3);
        // Give the waiting enemy a real defensive blade, then remove its role.
        // Contact must still be a hit: role policy is separate from appearance.
        Set(master, "singleActiveNpc", waiting);
        Call(master, "UpdateNpcGuard", waiting, 0f);
        Set(master, "singleActiveNpc", active);
        CombatPose enemy = (CombatPose)Call(master, "NpcCombatPose", waiting);
        CombatPose fixture = ThrustThroughDefence(enemy, true);
        object player = Field<object>(master, "first");
        Vector3 grip = fixture.Grip;
        Vector3 previousGrip = fixture.PreviousGrip;
        Set(player, "TargetPose", new SwordPose(Player.InverseTransformPoint(grip),
            Quaternion.FromToRotation(Vector3.forward, Player.InverseTransformDirection(fixture.Blade))));
        Set(player, "CombatBaseline", true);
        Set(player, "CombatGeneration", 44);
        Set(player, "CombatGuard", false);
        Set(player, "CombatFacing", Player.rotation);
        Set(player, "CombatGrip", previousGrip);
        Set(player, "CombatBlade", fixture.Blade);
        CombatPose playerBody = (CombatPose)Call(master, "PlayerDefencePose", player);
        var thrust = new CombatPose(previousGrip, fixture.Blade, grip, fixture.Blade,
            playerBody.BodyCenter, Player.forward, fixture.Blade, grip, fixture.Blade);
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, thrust, enemy, true).Outcome,
            Is.EqualTo(CombatOutcome.GuardSuccess), "The waiting blade could block if guarding were allowed.");
        Call(master, "UpdateSingleAttack", new CombatInput(0f, 0f, false, true, 44, true));
        Assert.That(Field<int>(waiting, "Hearts"), Is.EqualTo(2));
        Assert.That(Field<double>(waiting, "RecoveryUntil"), Is.GreaterThan(Now));
        MoveAt(Field<double>(waiting, "RecoveryUntil") + 2d);
        Assert.That(Field<float>(waiting, "AttackTimer"), Is.LessThan(0f),
            "A struck waiting enemy cannot take the role or begin a retaliation.");
        Assert.That(Field<object>(master, "singleActiveNpc"), Is.SameAs(active));
        yield return null;
    }

    [UnityTest]
    public IEnumerator WaitingEnemyLooksAtPlayerAndWandersWithoutChangingGuardOrApproaching()
    {
        object active = Spawn(Player.forward * 1.8f);
        Spawn(-Player.right * 7f); // The next opponent may approach; this one still wanders.
        object waiting = Spawn(Player.right * 7f);
        Set(master, "singleActiveNpc", active);
        Set(waiting, "NextGuardChange", 0d);
        Transform root = Field<GameObject>(waiting, "Root").transform;
        root.rotation = Quaternion.LookRotation(root.position - Player.position);
        Vector3 anchor = root.position;
        int guard = Field<int>(waiting, "GuardOctant");
        float travelled = 0f;
        for (int frame = 0; frame < 300; frame++)
        {
            Vector3 before = root.position;
            MoveAt(Now + .01d);
            float step = Vector3.Distance(root.position, before);
            Assert.That(step, Is.LessThanOrEqualTo(ShowdownCombat.WaitingWalkSpeed * .01f + .00001f));
            Assert.That(Vector3.Distance(root.position, anchor), Is.LessThanOrEqualTo(ShowdownCombat.WaitingWanderRadius + .00001f));
            travelled += step;
        }
        Assert.That(travelled, Is.GreaterThan(.1f));
        Assert.That(Vector3.Angle(root.forward, Player.position - root.position), Is.LessThan(3f));
        Assert.That(Field<int>(waiting, "GuardOctant"), Is.EqualTo(guard));
        Assert.That(Field<SwordPose>(waiting, "VisualBasePose").Direction.y, Is.LessThan(0f),
            "The waiting blade rests below the horizontal instead of holding a defensive guard.");
        Assert.That(Field<float>(waiting, "AttackTimer"), Is.LessThan(0f));
        yield return null;
    }

    [UnityTest]
    public IEnumerator OpeningAttackerPreparesOnArrivalWhileOrdinaryEnemyWaitsAfterArrival()
    {
        object npc = Spawn(Player.forward * 3.2f);
        Set(master, "singleActiveNpc", npc);
        Set(npc, "OpeningAttack", true);
        Set(npc, "NextAttack", Now);
        MoveAt(Now, 0f);
        Assert.That(Field<float>(npc, "AttackTimer"), Is.EqualTo(SingleTiming().WindupSeconds + SingleTiming().SwingSeconds).Within(.0001f));
        Assert.That(Field<AttackStroke>(npc, "Stroke").Active, Is.False,
            "Immediate arrival skips the guard pause, while preserving the full harmless preparation.");
        Set(npc, "AttackTimer", -1f);
        Set(npc, "OpeningAttack", false);
        Set(npc, "NextAttack", double.PositiveInfinity);
        Transform root = Field<GameObject>(npc, "Root").transform;
        root.position = Player.position + Player.forward * 2.2f;
        MoveAt(Now, 0f);
        double next = Field<double>(npc, "NextAttack");
        Assert.That(next, Is.EqualTo(Now + SingleTiming().AttackWait).Within(.0001d));
        Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
        MoveAt(next + .001d, 0f);
        Assert.That(Field<float>(npc, "AttackTimer"), Is.EqualTo(SingleTiming().WindupSeconds + SingleTiming().SwingSeconds).Within(.0001f));
        yield return null;
    }

    private ShowdownDifficulty SingleTiming() => new ShowdownDifficulty(1);

    [UnityTest]
    public IEnumerator NextOpponentApproachesToWaitingRangeWithoutTakingGuardOrAttackRole()
    {
        object active = Spawn(Player.forward * 1.8f);
        object next = Spawn(Player.right * 7f);
        object waiting = Spawn(-Player.forward * 7f);
        Set(master, "singleActiveNpc", active);
        Set(next, "NextAttack", 0d);
        Transform nextRoot = Field<GameObject>(next, "Root").transform;
        Transform waitingRoot = Field<GameObject>(waiting, "Root").transform;
        Vector3 anchor = waitingRoot.position;
        for (int frame = 0; frame < 400; frame++) MoveAt(Now + .01d);
        Assert.That(Vector3.Distance(nextRoot.position, Player.position), Is.EqualTo(ShowdownCombat.NextOpponentRange).Within(.01f));
        Assert.That(Field<float>(next, "AttackTimer"), Is.LessThan(0f));
        Assert.That(Field<SwordPose>(next, "VisualBasePose").Direction.y, Is.LessThan(0f));
        Assert.That(Vector3.Distance(waitingRoot.position, anchor), Is.LessThanOrEqualTo(ShowdownCombat.WaitingWanderRadius + .0001f));
        Assert.That(Field<object>(master, "singleActiveNpc"), Is.SameAs(active));
        Call(master, "RemoveSingleNpc", active);
        MoveAt(Now + ShowdownCombat.OpponentHandoff(SingleTiming()) + .001d);
        Assert.That(Field<object>(master, "singleActiveNpc"), Is.SameAs(next));
        yield return null;
    }

    [UnityTest]
    public IEnumerator NormalApproachAttackPreparesOutsideCutRangeAndClosesDuringTheWarning()
    {
        foreach (bool easy in new[] { false, true })
        {
            Call(master, "SetGameplaySuspended", true);
            Call(master, "SetSingleDifficulty", easy);
            Call(master, "ResetMatch");
            Call(master, "SetGameplaySuspended", false);
            var difficulty = new ShowdownDifficulty(1, easy);
            float distance = ShowdownCombat.ApproachAttackRange(new ShowdownDifficulty(1)) - .05f;
            object npc = Spawn(Player.forward * distance);
            Set(master, "singleActiveNpc", npc);
            Set(npc, "ApproachAttack", !easy);
            Set(npc, "NextAttack", Now);
            MoveAt(Now, 0f);
            if (easy)
            {
                Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f), "Easy keeps its existing arrival behavior.");
                continue;
            }
            Assert.That(Field<float>(npc, "AttackTimer"), Is.GreaterThan(difficulty.SwingSeconds));
            Assert.That(Field<bool>(npc, "AdvanceDuringWindup"), Is.True);
            Assert.That(Field<AttackStroke>(npc, "Stroke").Active, Is.False);
            MoveAt(Now + difficulty.WindupSeconds + .001d, difficulty.WindupSeconds + .001f);
            Transform root = Field<GameObject>(npc, "Root").transform;
            Assert.That(Vector3.Distance(root.position, Player.position), Is.LessThanOrEqualTo(ShowdownCombat.AttackRange + .001f),
                "Even a frame crossing preparation and cut retains all of its preparation movement.");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator ActivationCueOccursOncePerRoleAndMovesToTheNextLivingEnemy()
    {
        object active = Spawn(Player.forward * 1.8f);
        object waiting = Spawn(Player.right * 7f);
        double selectedAt = Now;
        MoveAt(selectedAt, 0f);
        Call(master, "TickSingleNpcAvatars", Now, 0f);
        Component firstStatus = Field<Component>(active, "Status");
        Component waitingStatus = Field<Component>(waiting, "Status");
        Assert.That(Field<double>(firstStatus, "activatedAt"), Is.EqualTo(selectedAt));
        Assert.That(Field<Canvas>(firstStatus, "activationCue").enabled, Is.True);
        Assert.That(Field<Canvas>(waitingStatus, "activationCue").enabled, Is.False);
        Assert.That(Field<AudioSource>(firstStatus, "activationAudio").volume, Is.GreaterThan(0f));
        MoveAt(selectedAt + .8d, 0f);
        Call(master, "TickSingleNpcAvatars", Now, 0f);
        Assert.That(Field<double>(firstStatus, "activatedAt"), Is.EqualTo(selectedAt),
            "Holding the same role never repeats the activation sound.");
        Assert.That(Field<Canvas>(firstStatus, "activationCue").enabled, Is.True,
            "The arrow continues to mark the selected opponent after its pulse.");
        foreach (Image segment in Field<Image[]>(firstStatus, "activationRing"))
            Assert.That(segment.color.a, Is.EqualTo(0f).Within(.0001f));
        Call(master, "RemoveSingleNpc", active);
        MoveAt(Now + ShowdownCombat.OpponentHandoff(SingleTiming()) + .001d, 0f);
        Call(master, "TickSingleNpcAvatars", Now, 0f);
        Assert.That(Field<object>(master, "singleActiveNpc"), Is.SameAs(waiting));
        Assert.That(Field<double>(waitingStatus, "activatedAt"), Is.EqualTo(Now));
        Assert.That(Field<Canvas>(firstStatus, "activationCue").enabled, Is.False);
        Assert.That(Field<Canvas>(waitingStatus, "activationCue").enabled, Is.True);
        yield return null;
    }

    [UnityTest]
    public IEnumerator NormalCountersDuringPlayerStaggerWhileEasyWaitsThenGivesFullWarning()
    {
        foreach (bool easy in new[] { false, true })
        {
            Call(master, "SetGameplaySuspended", true);
            Call(master, "SetSingleDifficulty", easy);
            Call(master, "ResetMatch");
            Call(master, "SetGameplaySuspended", false);
            object npc = Spawn(Player.forward * 1.8f, 3);
            Set(master, "singleActiveNpc", npc);
            double guardedAt = Now;
            Contact(npc, true);
            var difficulty = new ShowdownDifficulty(1, easy);
            double ordinaryAttackAt = Field<double>(npc, "NextAttack");
            Assert.That(Field<double>(npc, "NextAttack") - guardedAt, Is.LessThan(difficulty.AttackWait * .5f));
            MoveAt(guardedAt + .001d, .001f);
            Assert.That(Now, Is.LessThan(Field<double>(master, "singleStunnedUntil")));
            Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f), "The displacement completes before a new counter targets the moved player.");
            MoveAt(guardedAt + DuelMatch.GuardStopSeconds + DuelMatch.PushSeconds + .001d, .001f);
            if (easy)
            {
                Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
                MoveAt(guardedAt + .4d, .001f);
                Assert.That(Now, Is.GreaterThan(ordinaryAttackAt), "The ordinary counter wait has already expired.");
                Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f), "Even an expired ordinary wait cannot attack a staggered player on easy.");
                MoveAt(Field<double>(master, "singleStunnedUntil") + .001d, .001f);
            }
            Assert.That(Field<float>(npc, "WindupSeconds"), Is.EqualTo(difficulty.WindupSeconds).Within(.0001f));
            Assert.That(Field<float>(npc, "SwingSeconds"), Is.EqualTo(difficulty.SwingSeconds).Within(.0001f));
            Assert.That(Field<float>(npc, "AttackTimer"), Is.GreaterThan(difficulty.SwingSeconds));
            Assert.That(Field<AttackStroke>(npc, "Stroke").Active, Is.False);
            bool sawCue = false;
            for (int frame = 0; frame < 100; frame++)
            {
                MoveAt(Now + .01d);
                Call(master, "TickSingleNpcAvatars", Now, .01f);
                float timer = Field<float>(npc, "AttackTimer");
                if (timer <= difficulty.SwingSeconds) break;
                Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f));
                Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts), "The counter's preparation cannot damage the player.");
                Component status = Field<Component>(npc, "Status");
                Canvas cue = Field<Canvas>(status, "attackCue");
                if (!cue.enabled) continue;
                sawCue = true;
                Assert.That(timer, Is.LessThanOrEqualTo(difficulty.SwingSeconds + ShowdownCombat.AttackCueSeconds));
            }
            Assert.That(sawCue, Is.True, "A fast counter still announces its cut while preparing, including on easy.");
            Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThanOrEqualTo(difficulty.SwingSeconds));
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator EasyCancelsPreparationAndRealPendingCutsThenRestartsWithFreshWarning()
    {
        foreach (bool pendingCut in new[] { false, true })
        {
            Call(master, "SetGameplaySuspended", true);
            Call(master, "SetSingleDifficulty", true);
            Call(master, "ResetMatch");
            Call(master, "SetGameplaySuspended", false);
            object npc = Spawn(Player.forward * 1.8f);
            Set(master, "singleActiveNpc", npc);
            Set(npc, "NextAttack", 0d);
            MoveAt(Now, 0f);
            Set(npc, "AttackReadyDirection", Vector3.up);
            var difficulty = new ShowdownDifficulty(1, true);
            if (pendingCut)
            {
                bool realContact = false;
                for (int frame = 0; frame < 300; frame++)
                {
                    MoveAt(Now + .005d, .005f);
                    if (!Field<AttackStroke>(npc, "Stroke").Active) continue;
                    CombatPose attack = (CombatPose)Call(master, "NpcCombatPose", npc);
                    CombatPose defence = (CombatPose)Call(master, "PlayerDefencePose", Field<object>(master, "first"));
                    if (CombatContact.Resolve(AttackKind.Cut, attack, defence, false).Outcome != CombatOutcome.Hit) continue;
                    realContact = true;
                    break;
                }
                Assert.That(realContact, Is.True, "The cancellation fixture contains a real pending cut through the player.");
            }
            else
            {
                MoveAt(Now + .1d, .1f);
                Assert.That(Field<float>(npc, "AttackTimer"), Is.GreaterThan(difficulty.SwingSeconds));
            }

            double blockedUntil;
            if (pendingCut)
            {
                Assert.That(Run.Damage(Now), Is.True);
                blockedUntil = Now + DuelMatch.HitStopSeconds + CombatReaction.RecoverySeconds;
                Assert.That(Field<double>(master, "singleStunnedUntil"), Is.EqualTo(0d));
            }
            else
            {
                blockedUntil = Now + .5d;
                Set(master, "singleStunnedUntil", blockedUntil);
            }
            int hearts = Run.Hearts;
            double attackDeadline = blockedUntil + .25d;
            Set(npc, "NextAttack", 0d);
            if (pendingCut) Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f));
            else MoveAt(Now, 0f);
            Assert.That(Run.Hearts, Is.EqualTo(hearts), "The interrupted cut cannot damage a staggered player on easy.");
            Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
            Assert.That(Field<AttackStroke>(npc, "Stroke").Active, Is.False);
            Assert.That(Field<bool>(npc, "ContactResolved"), Is.True);
            Assert.That(Field<bool>(npc, "AdvanceDuringWindup"), Is.False);
            Transform root = Field<GameObject>(npc, "Root").transform;
            SwordPose guard = Field<SwordPose>(npc, "VisualBasePose");
            Assert.That(Vector3.Angle(guard.Direction, ShowdownCombat.ReadyDirection(Field<int>(npc, "GuardOctant"))), Is.LessThan(13f));
            Assert.That(Vector3.Distance(Field<Vector3>(npc, "PreviousGrip"), root.TransformPoint(guard.Position)), Is.LessThan(.0001f),
                "The canceled cut cannot survive in collision history.");
            MoveAt(blockedUntil - .001d, 0f);
            Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
            Set(npc, "NextAttack", attackDeadline);
            MoveAt(blockedUntil + .001d, 0f);
            Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f), "An ordinary future wait still survives cancellation and recovery.");
            MoveAt(attackDeadline + .001d, 0f);
            Assert.That(Field<float>(npc, "WindupSeconds"), Is.EqualTo(difficulty.WindupSeconds));
            Assert.That(Field<float>(npc, "AttackTimer"), Is.EqualTo(difficulty.WindupSeconds + difficulty.SwingSeconds).Within(.0001f));
            Assert.That(Field<AttackStroke>(npc, "Stroke").Active, Is.False, "Recovery starts a new harmless preparation, never the canceled sweep.");
            Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f));
            Assert.That(Run.Hearts, Is.EqualTo(hearts));
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator StrongEnemyKeepsItsShorterWaitAfterLosingHearts()
    {
        object weak = Spawn(Player.right * 2f);
        object strong = Spawn(Player.forward * 2f, 3);
        Set(master, "singleActiveNpc", strong);
        Contact(strong, false);
        double strongWait = Field<double>(strong, "NextAttack") - Now;
        Assert.That(Field<int>(strong, "Hearts"), Is.EqualTo(2));
        Assert.That(Field<int>(strong, "MaximumHearts"), Is.EqualTo(3));
        Set(master, "matchTime", Field<double>(strong, "RecoveryUntil") + .01d);
        Set(weak, "AttackTimer", 0f);
        Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f));
        double weakWait = Field<double>(weak, "NextAttack") - Now;
        Assert.That(strongWait, Is.LessThan(weakWait));
        Set(strong, "Hearts", 1);
        Set(strong, "AttackTimer", 0f);
        Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f));
        Assert.That(Field<double>(strong, "NextAttack") - Now, Is.EqualTo(strongWait).Within(.0001d),
            "Strength belongs to starting health and survives damage or a missed swing.");
        Assert.That(Field<double>(strong, "NextGuardChange"), Is.GreaterThan(Now),
            "Finishing a swing leaves time to return before another guard direction is selected.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator SurvivingEnemySwordDefendsImmediatelyAndThroughoutStaggerWithoutAttacking()
    {
        object npc = Spawn(Player.forward * 1.8f, 3);
        Set(master, "singleActiveNpc", npc);
        Transform sword = Field<Transform>(npc, "Sword");
        Vector3 capturedGrip = sword.localPosition;
        Quaternion capturedRotation = sword.localRotation;
        double hitAt = Now;
        CombatResult contact = Contact(npc, false);
        Assert.That(Field<int>(npc, "Hearts"), Is.EqualTo(2));

        MoveAt(hitAt + contact.HitStopSeconds * .5f);
        Call(master, "TickSingleNpcAvatars", Now, .01f);
        SwordPose immediateGuard = Field<SwordPose>(npc, "VisualBasePose");
        Assert.That(Vector3.Distance(sword.localPosition, immediateGuard.Position), Is.LessThan(.0001f));
        Assert.That(Quaternion.Angle(sword.localRotation, immediateGuard.Rotation), Is.LessThan(.01f),
            "A staggered NPC can show its new guard immediately, including during hitstop.");
        Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));

        MoveAt(hitAt + contact.HitStopSeconds + .18d);
        Call(master, "TickSingleNpcAvatars", Now, .01f);
        SwordPose logical = Field<SwordPose>(npc, "VisualBasePose");
        Vector3 reactingGrip = sword.localPosition;
        Quaternion reactingRotation = sword.localRotation;
        Assert.That(Vector3.Angle(logical.Direction, ShowdownCombat.ReadyDirection(Field<int>(npc, "GuardOctant"))), Is.LessThan(13f),
            "Stagger immediately restores the logical chest-front guard.");
        Assert.That(Field<float>(npc, "AttackTimer"), Is.LessThan(0f));
        Transform root = Field<GameObject>(npc, "Root").transform;
        Vector3 logicalGrip = root.TransformPoint(logical.Position);
        Vector3 logicalBlade = root.TransformDirection(logical.Direction);
        CombatPose combat = (CombatPose)Call(master, "NpcCombatPose", npc);
        Assert.That(Vector3.Distance(combat.Grip, logicalGrip), Is.LessThan(.0001f));
        Assert.That(Vector3.Angle(combat.Blade, logicalBlade), Is.LessThan(.01f),
            "The rendered stagger never changes the shared combat pose.");
        Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f));
        Assert.That(Vector3.Distance(Field<Vector3>(npc, "PreviousGrip"), logicalGrip), Is.LessThan(.0001f));
        Assert.That(Vector3.Angle(Field<Vector3>(npc, "PreviousBlade"), logicalBlade), Is.LessThan(.01f));
        Call(master, "TickSingleNpcAvatars", Now, 0f);
        Assert.That(Vector3.Distance(sword.localPosition, reactingGrip), Is.LessThan(.0001f));
        Assert.That(Quaternion.Angle(sword.localRotation, reactingRotation), Is.LessThan(.01f),
            "Repeated rendering never adds the reaction to an already reacted sword.");
        Set(master, "matchTime", Field<double>(npc, "RecoveryUntil") + .01d);
        Call(master, "TickSingleNpcAvatars", Now, .01f);
        Assert.That(Vector3.Distance(sword.localPosition, logical.Position), Is.LessThan(.0001f));
        Assert.That(Quaternion.Angle(sword.localRotation, logical.Rotation), Is.LessThan(.01f));
        yield return null;
    }

    [UnityTest]
    public IEnumerator ActiveGuardChangesSmoothlyAndStaggerHoldsItsGuardWithoutAttacking()
    {
        object active = Spawn(Player.forward * 1.8f);
        object waiting = Spawn(Player.right * 7f);
        Set(master, "singleActiveNpc", active);
        Call(master, "UpdateNpcGuard", active, 0f);
        Transform sword = Field<Transform>(active, "Sword");
        Quaternion beforeRotation = sword.rotation;
        Vector3 beforeGrip = sword.localPosition;
        int beforeOctant = Field<int>(active, "GuardOctant");
        int waitingOctant = Field<int>(waiting, "GuardOctant");
        Set(active, "NextGuardChange", Now);
        Set(waiting, "NextGuardChange", Now);
        MoveAt(Now + .01d);
        int changedOctant = Field<int>(active, "GuardOctant");
        Assert.That(changedOctant, Is.InRange(0, 7).And.Not.EqualTo(beforeOctant));
        Assert.That(Field<int>(waiting, "GuardOctant"), Is.EqualTo(waitingOctant));
        Assert.That(Quaternion.Angle(beforeRotation, sword.rotation), Is.LessThanOrEqualTo(3.01f));
        Vector3 remembered = Vector3.up;
        var settings = new SwordPoseSettings(Field<Vector3>(active, "ChestCenter")
            + (Vector3)Field<object>(active, "Avatar").GetType().GetProperty("ChestOffset").GetValue(Field<object>(active, "Avatar")));
        SwordPose goal = SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward,
            ShowdownCombat.ReadyDirection(changedOctant)), true, settings, ref remembered);
        float wholeMove = Vector3.Distance(beforeGrip, goal.Position);
        Assert.That(Vector3.Distance(beforeGrip, sword.localPosition), Is.GreaterThan(0f)
            .And.LessThanOrEqualTo(wholeMove * (1f - Mathf.Exp(-.01f / .045f)) + .0001f),
            "The grip starts moving smoothly, with a faster response toward the sides and no instant jump.");
        Assert.That(Field<double>(active, "NextGuardChange"), Is.GreaterThan(Now));
        Set(active, "NextGuardChange", Now);
        Set(active, "StunnedUntil", Now + 1d);
        beforeRotation = sword.rotation;
        MoveAt(Now + .01d);
        Assert.That(Field<int>(active, "GuardOctant"), Is.EqualTo(changedOctant));
        Assert.That(Field<float>(active, "AttackTimer"), Is.LessThan(0f));
        Assert.That(Vector3.Angle(sword.localRotation * Vector3.forward, ShowdownCombat.ReadyDirection(changedOctant)), Is.LessThan(13f));
        beforeRotation = sword.rotation;
        MoveAt(Now + .01d);
        Assert.That(Field<int>(active, "GuardOctant"), Is.EqualTo(changedOctant));
        Assert.That(Quaternion.Angle(beforeRotation, sword.rotation), Is.LessThan(.001f));
        Set(active, "StunnedUntil", 0d);
        Set(active, "RecoveryUntil", 0d);
        Set(active, "NextAttack", 0d);
        MoveAt(Now + .01d);
        Assert.That(Field<float>(active, "AttackTimer"), Is.GreaterThan(Field<float>(active, "SwingSeconds")));
        int preparedOctant = Field<int>(active, "GuardOctant");
        Set(active, "NextGuardChange", Now);
        MoveAt(Now + .01d);
        Assert.That(Field<int>(active, "GuardOctant"), Is.EqualTo(preparedOctant),
            "A due guard change cannot retarget a prepared attack.");
        yield return null;
    }
    [UnityTest]
    public IEnumerator PlayerLifeUsesTwoForDirectHitsOneForFailedGuardsAndZeroForBlocks()
    {
        foreach(bool easy in new[]{false,true})
        foreach(var scenario in new[]{
            (outcome:CombatOutcome.Hit,guard:false,damage:2),
            (outcome:CombatOutcome.GuardBreak,guard:true,damage:1),
            (outcome:CombatOutcome.Hit,guard:true,damage:1),
            (outcome:CombatOutcome.GuardSuccess,guard:true,damage:0)})
        {
            Call(master,"SetSingleDifficulty",easy);Call(master,"ResetMatch");
            object npc=Spawn(Player.forward*1.8f);
            object player=Field<object>(master,"first");
            CombatPose defence=(CombatPose)Call(master,"PlayerDefencePose",player);
            CombatPose attack=(CombatPose)Call(master,"NpcCombatPose",npc);
            var result=new CombatResult(scenario.outcome,1,scenario.damage==0?1:0,-1,AttackKind.Cut);
            Call(master,"ApplySingleContact",npc,result,defence,attack,scenario.guard);
            Assert.That(Run.Hearts,Is.EqualTo(10-scenario.damage));
            Assert.That(Field<int>(npc,"Hearts"),Is.EqualTo(1),"The requested damage changes only the player's health.");
        }
        yield return null;
    }

}
