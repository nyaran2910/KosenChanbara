using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// Exercises the scene controller's actual spawn/defeat gates and suspension,
// while driving its clock explicitly rather than waiting for real opponents.
public sealed class ShowdownCoursePlayTests
{
    private MonoBehaviour master, flow;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static object Get(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);
    private static object RuntimeField(object target, string name) => target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
    private static void SetRuntimeField(object target, string name, object value) => target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
    private ShowdownProgress Run => Field<ShowdownProgress>(master, "showdown");
    private Transform Player => Field<GameObject>(master, "PlayerObject1").transform;
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
        Call(flow, "ChooseMode", true); Call(flow, "Ready", 0); Call(flow, "BeginMatch");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "HardwareControllerHub") Object.Destroy(component.gameObject);
        yield return null;
    }

    private void Tick(float delta)
    {
        double now = Field<double>(master, "matchTime") + delta;
        master.GetType().GetField("matchTime", Private).SetValue(master, now);
        Call(master, "TickSingleMode", now, delta);
    }

    // These direct-motion fixtures inspect authored cuts, guard geometry or
    // handoff policy. Publish a settled real view; dedicated AI tests exercise
    // the unsatisfied camera/player gate and its staging interval.
    private void MoveSingleNpcsWithReadyView(float delta)
    {
        Call(master, "AdvanceSingleReaction", Field<double>(master, "matchTime"));
        Call(master, "SelectSingleOpponent");
        object active = RuntimeField(master, "singleActiveNpc");
        if (active != null)
        {
            Transform root = ((GameObject)RuntimeField(active, "Root")).transform;
            Vector3 toward = root.position - Player.position; toward.y = 0f;
            Player.rotation = Quaternion.LookRotation(toward);
            Camera view = Player.GetComponentInChildren<Camera>();
            Vector3 origin = Player.TransformPoint(new Vector3(.35f, 1.6f, -3.4f));
            view.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(root.position + Vector3.up - origin));
            SetRuntimeField(active, "Engaged", true);
        }
        Call(master, "MoveSingleNpcs", delta);
    }

    // Manual stroke fixtures bypass UpdatePose, so publish their chosen pose
    // to the same logical geometry used by single and duel contact.
    private void UpdateSingleAttackWithManualSword(CombatInput input)
    {
        object player = Field<object>(master, "first");
        Transform sword = (Transform)RuntimeField(player, "Sword");
        SetRuntimeField(player, "TargetPose", new SwordPose(sword.localPosition, sword.localRotation));
        Call(master, "UpdateSingleAttack", input);
    }

    private static string DescribeCutGuardContact(CombatPose attack, CombatPose defence)
    {
        float angle = Vector3.Angle(attack.CutDirection, defence.Blade);
        bool blade = CombatContact.TrySweptBladeTouchesBlade(attack.PreviousGrip, attack.PreviousBlade,
            attack.Grip, attack.Blade, defence.Grip, defence.Blade, out float guardFraction);
        bool body = CombatContact.TrySweptBladeTouchesBody(attack.PreviousGrip, attack.PreviousBlade,
            attack.Grip, attack.Blade, defence.BodyCenter, out float bodyFraction);
        return $" Cut/guard angle={Mathf.Min(angle, 180f - angle):F2}, "
            + $"blade contact={blade} ({guardFraction:F3}), "
            + $"body contact={body} ({bodyFraction:F3}), "
            + $"shared guarded outcome={CombatContact.Resolve(AttackKind.Cut, attack, defence, true).Outcome}; "
            + $"attack grip={attack.Grip:F3}, blade={attack.Blade:F3}, cut={attack.CutDirection:F3}, "
            + $"guard grip={defence.Grip:F3}, blade={defence.Blade:F3}.";
    }

    private void ClearSection()
    {
        for (int i = 0; i < 10; i++) Call(master, "SpawnSingleNpc");
        foreach (object npc in Field<IList>(master, "singleNpcs"))
            if (!(bool)npc.GetType().GetField("Defeated").GetValue(npc)) Call(master, "RemoveSingleNpc", npc);
        SetRuntimeField(master, "matchTime", System.Math.Max(Field<double>(master, "matchTime"), Field<double>(master, "singleStarted") + 2.5d));
        Tick(.01f);
    }

    [UnityTest]
    public IEnumerator ScenePlacesNextGroupAtHiddenArrivalAndClearsAtExactlyOneHundred()
    {
        // Skip the opening clock without moving a newly spawned attacker through
        // three seconds of approach and attack in a single simulated frame.
        master.GetType().GetField("matchTime", Private).SetValue(master, Field<double>(master, "matchTime") + 3d);
        Tick(.01f);
        for (int area = 1; area <= 10; area++)
        {
            ClearSection();
            Assert.That(Run.Defeated, Is.EqualTo(area * 10));
            Assert.That(Get(master, "SingleSection"), Is.EqualTo(area));
            Assert.That(Get(master, "SingleAdvancing"), Is.EqualTo(area < 10));
            if (area == 10) break;
            int spawned = Run.Spawned;
            int frames = 0;
            while ((bool)Get(master, "SingleAdvancing") && frames++ < 200)
            {
                Call(master, "SpawnSingleNpc"); // Even a direct spawn request must respect travel.
                Tick(.1f);
                Assert.That(Run.Spawned, Is.EqualTo((bool)Get(master, "SingleAdvancing") ? spawned : spawned + 10));
                Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts));
            }
            Assert.That(frames, Is.LessThan(200));
            Assert.That(Get(master, "SingleSection"), Is.EqualTo(area + 1));
            Assert.That(Run.Spawned, Is.EqualTo((area + 1) * 10), "Arrival places the next full group behind the fade.");
        }
        Assert.That(Run.Spawned, Is.EqualTo(100));
        Assert.That(Run.Cleared, Is.True);
        Assert.That(Field<bool>(master, "singleGameOver"), Is.True);
        Vector3 clearedAt = Player.position;
        Tick(5f); Call(master, "SpawnSingleNpc");
        Assert.That(Player.position, Is.EqualTo(clearedAt));
        Assert.That(Run.Spawned, Is.EqualTo(100));
        yield return null;
    }

    [UnityTest]
    public IEnumerator PauseTitleAndRetryCannotConsumeTheTravelClockOrKeepOldEnemies()
    {
        Tick(3f); ClearSection();
        Assert.That(Get(master, "SingleAdvancing"), Is.True);
        Tick(.2f);
        Vector3 pausedAt = Player.position;
        double pausedTime = (double)Get(master, "MatchTime");
        float progress = (float)Get(master, "SingleAdvanceProgress");
        Call(flow, "TogglePause");
        for (int frame = 0; frame < 100; frame++) Call(master, "LateUpdate");
        Assert.That(Player.position, Is.EqualTo(pausedAt));
        Assert.That(Get(master, "MatchTime"), Is.EqualTo(pausedTime));
        Assert.That(Get(master, "SingleAdvanceProgress"), Is.EqualTo(progress));
        Call(flow, "TogglePause"); Tick(.2f);
        Assert.That(Vector3.Distance(Player.position, pausedAt), Is.GreaterThan(.1f));
        Call(flow, "ReturnToTitle");
        Vector3 titleAt = Player.position;
        for (int frame = 0; frame < 100; frame++) Call(master, "LateUpdate");
        Assert.That(Player.position, Is.EqualTo(titleAt));
        Assert.That(Get(master, "IsPaused"), Is.True);
        Assert.That(Get(master, "SingleSection"), Is.EqualTo(1));
        Assert.That(Get(master, "SingleAdvancing"), Is.False);
        Assert.That(Run.Spawned + Run.Defeated, Is.Zero);
        Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts));
        Assert.That(Field<IList>(master, "singleNpcs").Count, Is.Zero);
        Call(flow, "ChooseMode", true); Call(flow, "Ready", 0); Call(flow, "BeginMatch");
        Call(master, "ResetMatch");
        Assert.That(Player.position, Is.EqualTo(titleAt));
        Assert.That(Get(master, "SingleSection"), Is.EqualTo(1));
        yield return null;
    }

    [UnityTest]
    public IEnumerator BothControllersStayActiveAndOnlyTheSelectedEnemyMayWindUp()
    {
        Assert.That(Field<GameObject>(master, "PlayerObject2").activeInHierarchy, Is.True,
            "P2 motion sampling must continue while both controllers drive the single character.");
        foreach (Renderer renderer in Field<GameObject>(master, "PlayerObject2").GetComponentsInChildren<Renderer>())
            Assert.That(renderer.enabled, Is.False);
        Call(master, "SpawnSingleNpc"); Call(master, "SpawnSingleNpc");
        IList enemies = Field<IList>(master, "singleNpcs");
        Assert.That(enemies.Count, Is.EqualTo(2));
        for (int i = 0; i < 2; i++)
        {
            object enemy = enemies[i];
            Transform root = ((GameObject)enemy.GetType().GetField("Root").GetValue(enemy)).transform;
            root.position = Player.position + new Vector3(i == 0 ? -1.3f : 1.3f, 0f, 2.7f);
            root.rotation = Quaternion.LookRotation(Player.position - root.position);
            enemy.GetType().GetField("NextAttack").SetValue(enemy, 0d);
        }
        MoveSingleNpcsWithReadyView(.01f);
        object selected = RuntimeField(master, "singleActiveNpc");
        Assert.That(selected, Is.SameAs(enemies[0]));
        for (int i = 0; i < enemies.Count; i++)
        {
            object enemy = enemies[i];
            if (i == 0) Assert.That((float)RuntimeField(enemy, "AttackTimer"), Is.GreaterThan(0f));
            else Assert.That((float)RuntimeField(enemy, "AttackTimer"), Is.LessThan(0f));
            Component status = (Component)enemy.GetType().GetField("Status").GetValue(enemy);
            Assert.That(status.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.WorldSpace));
        }
        // Waiting, guard recoil and surviving a hit never hand the role to a
        // nearer enemy. Defeat is the only ordinary handoff boundary.
        SetRuntimeField(selected, "AttackTimer", -1f);
        SetRuntimeField(selected, "StunnedUntil", 2d);
        SetRuntimeField(enemies[1], "NextAttack", -10d);
        MoveSingleNpcsWithReadyView(.01f);
        Assert.That(RuntimeField(master, "singleActiveNpc"), Is.SameAs(selected));
        Assert.That(RuntimeField(enemies[1], "AttackTimer"), Is.LessThan(0f));
        Call(master, "RemoveSingleNpc", selected);
        MoveSingleNpcsWithReadyView(.01f);
        Assert.That(RuntimeField(master, "singleActiveNpc"), Is.Null);
        SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + .2d);
        MoveSingleNpcsWithReadyView(.01f);
        Assert.That(RuntimeField(master, "singleActiveNpc"), Is.SameAs(enemies[1]));
        Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts), "Windup alone cannot damage the player.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator EnemyTimerCompletionCannotDamageWithoutARealBladeSweep()
    {
        Call(master, "SpawnSingleNpc");
        object enemy = Field<IList>(master, "singleNpcs")[0];
        var enemyType = enemy.GetType();
        Transform root = ((GameObject)enemyType.GetField("Root").GetValue(enemy)).transform;
        Transform sword = (Transform)enemyType.GetField("Sword").GetValue(enemy);
        // End a committed swing while its sword is upright: the player is close
        // enough to the challenger, but its blade points away from the body.
        root.position = Player.position + Vector3.forward * 3f;
        root.rotation = Quaternion.LookRotation(Vector3.back);
        sword.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
        enemyType.GetField("VisualBasePose").SetValue(enemy, new SwordPose(sword.localPosition, sword.localRotation));
        enemyType.GetField("PreviousGrip").SetValue(enemy, sword.position);
        enemyType.GetField("PreviousBlade").SetValue(enemy, Vector3.up);
        enemyType.GetField("SwingSeconds").SetValue(enemy, .22f);
        enemyType.GetField("AttackTimer").SetValue(enemy, 0f);
        Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f, false, true, 0));
        Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts));
        Assert.That((float)enemyType.GetField("AttackTimer").GetValue(enemy), Is.LessThan(0f));
        // Point the sword at the player from well beyond the model's reach.
        root.position = Player.position + Vector3.forward * 7f;
        sword.rotation = Quaternion.LookRotation(Vector3.back);
        enemyType.GetField("VisualBasePose").SetValue(enemy, new SwordPose(sword.localPosition, sword.localRotation));
        enemyType.GetField("PreviousGrip").SetValue(enemy, sword.position);
        enemyType.GetField("PreviousBlade").SetValue(enemy, Vector3.back);
        enemyType.GetField("AttackTimer").SetValue(enemy, 0f);
        Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f, false, true, 0));
        Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts), "Completing a distant strike is never a remote hit.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator CrowdedSpawnArcUsesAnAdjacentLaneWithoutSkippingTheTenthChallenger()
    {
        for (int i = 0; i < 9; i++) Call(master, "SpawnSingleNpc");
        IList enemies = Field<IList>(master, "singleNpcs");
        Assert.That(enemies.Count, Is.EqualTo(9));
        for (int i = 0; i < enemies.Count; i++)
        {
            Transform root = ((GameObject)RuntimeField(enemies[i], "Root")).transform;
            root.position = Player.position + Quaternion.Euler(0f, -70f + i * 17.5f, 0f) * Player.forward * 7f;
        }
        // Every random candidate on the original 7m arc is obstructed. The
        // actual scene spawn must choose another clear lane and credit one NPC.
        Call(master, "SpawnSingleNpc");
        Assert.That(enemies.Count, Is.EqualTo(10));
        Assert.That(Run.Spawned, Is.EqualTo(10));
        Vector3 spawned = ((GameObject)RuntimeField(enemies[9], "Root")).transform.position;
        Assert.That(Vector3.Distance(spawned, Player.position), Is.EqualTo(5.75f).Within(.001f));
        for (int i = 0; i < 9; i++)
            Assert.That(Vector3.Distance(spawned, ((GameObject)RuntimeField(enemies[i], "Root")).transform.position),
                Is.GreaterThanOrEqualTo(ShowdownCombat.MinimumSpacing));
        Call(master, "SpawnSingleNpc");
        Assert.That(Run.Spawned, Is.EqualTo(10), "The lane fallback still respects the section spawn gate.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator PlayerStrokeCanContactLaterInItsArcButResolvesOnlyOneFrontEnemy()
    {
        for (int i = 0; i < 3; i++) Call(master, "SpawnSingleNpc");
        IList enemies = Field<IList>(master, "singleNpcs");
        for (int i = 0; i < enemies.Count; i++)
        {
            Transform root = ((GameObject)RuntimeField(enemies[i], "Root")).transform;
            root.position = Player.position + Player.forward * (i < 2 ? 2.5f : -1.5f);
            root.rotation = Quaternion.LookRotation(i < 2 ? -Player.forward : Player.forward);
            SetRuntimeField(enemies[i], "GuardOctant", 0);
            SetRuntimeField(enemies[i], "Hearts", 3);
            SetRuntimeField(enemies[i], "StunnedUntil", 100d);
            SetRuntimeField(enemies[i], "NextAttack", 100d);
            // Stagger allows defence; use a vertical guard parallel to this
            // vertical cut so a legitimate guard does not hide the hit test.
            Call(master, "UpdateNpcGuard", enemies[i], 0f);
            PublishNpcGuardHistory(enemies[i]);
        }
        Transform sword = (Transform)RuntimeField(Field<object>(master, "first"), "Sword");
        void Stroke(float angle, bool guard = false, bool valid = true)
        {
            sword.position = Player.position + Vector3.up * .15f + Player.forward * .55f;
            Vector3 direction = Quaternion.AngleAxis(-angle, Player.right) * Player.forward;
            sword.rotation = Quaternion.LookRotation(direction, Player.up);
            UpdateSingleAttackWithManualSword(new CombatInput(angle, 90f, guard, valid, 44));
        }
        Stroke(80f); Stroke(45f);
        foreach (object enemy in enemies)
            Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(3), "Entering the attack angle is not itself a hit.");
        Stroke(5f);
        Assert.That(RuntimeField(enemies[0], "Hearts"), Is.EqualTo(2), "The blade reaches the body later in this same stroke.");
        Assert.That(RuntimeField(enemies[1], "Hearts"), Is.EqualTo(3), "Overlapping opponents cannot multiply one stroke's damage.");
        Assert.That(RuntimeField(enemies[2], "Hearts"), Is.EqualTo(3), "A forward cut cannot damage an opponent behind the player.");
        Stroke(0f); Stroke(80f, true); Stroke(5f);
        Assert.That(RuntimeField(enemies[0], "Hearts"), Is.EqualTo(2));
        Assert.That(RuntimeField(enemies[1], "Hearts"), Is.EqualTo(3), "Guard release must establish a fresh blade baseline.");
        Stroke(80f); Stroke(45f); Stroke(30f, valid: false); Stroke(5f);
        Assert.That(RuntimeField(enemies[1], "Hearts"), Is.EqualTo(3), "An invalid pose cancels the unfinished stroke.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator FreshCutsCanDamageAStaggeringMissedGuardWithoutRepeatingOneStroke()
    {
        Call(master, "SpawnSingleNpc");
        object enemy = Field<IList>(master, "singleNpcs")[0];
        SetRuntimeField(master, "singleActiveNpc", enemy);
        SetRuntimeField(enemy, "ChestCenter", Vector3.down * .0235f);
        SetRuntimeField(enemy, "Hearts", 3);
        SetRuntimeField(enemy, "GuardOctant", 0);
        object player = Field<object>(master, "first");
        SetRuntimeField(player, "CutVelocity", Vector3.down);
        Transform sword = (Transform)RuntimeField(player, "Sword");
        Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
        void Strike()
        {
            root.position = Player.position + Player.forward * 2.6f;
            root.rotation = Quaternion.LookRotation(-Player.forward);
            SetRuntimeField(enemy, "GuardOctant", 0);
            Call(master, "UpdateNpcGuard", enemy, 0f);
            Transform guardSword = (Transform)RuntimeField(enemy, "Sword");
            guardSword.position += Player.right * 4f;
            SetRuntimeField(enemy, "VisualBasePose", new SwordPose(guardSword.localPosition, guardSword.localRotation));
            foreach (float angle in new[] { 80f, 45f, 20.3f })
            {
                sword.position = Player.position + Vector3.up * .15f + Player.forward * .55f;
                sword.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(-angle, Player.right) * Player.forward, Player.up);
                UpdateSingleAttackWithManualSword(new CombatInput(angle, 90f, false, true, 44));
            }
        }
        Strike();
        Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(2));
        SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + .35d);
        Assert.That(Field<double>(master, "matchTime"), Is.LessThan((double)RuntimeField(enemy, "StunnedUntil")));
        Strike();
        Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(1),
            "A fresh cut can hit past a guard blade that misses the attack while the enemy is still staggered.");
        UpdateSingleAttackWithManualSword(new CombatInput(5f, 90f, false, true, 44));
        Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(1), "Each consumed stroke still damages only once.");
        yield return null;
    }

    private static void PublishNpcGuardHistory(object enemy)
    {
        Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
        SwordPose pose = (SwordPose)RuntimeField(enemy, "VisualBasePose");
        SetRuntimeField(enemy, "PreviousGrip", root.TransformPoint(pose.Position));
        SetRuntimeField(enemy, "PreviousBlade", root.TransformDirection(pose.Direction));
    }

    private void StrikeThroughPhysicalGuard(object enemy, object player, Transform sword, Vector3? fixedIntercept = null)
    {
        CombatPose defence = (CombatPose)Call(master, "NpcCombatPose", enemy);
        Vector3 intercept = fixedIntercept ?? defence.Grip + defence.Blade.normalized * .8f;
        SetRuntimeField(player, "CutVelocity", Vector3.down);
        foreach (float angle in new[] { 80f, 20f, 0f, -5f })
        {
            Vector3 target = angle < 0f ? defence.BodyCenter : intercept;
            sword.position = target - Player.forward * 1.4f;
            sword.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(-angle, Player.right) * Player.forward, Player.up);
            UpdateSingleAttackWithManualSword(new CombatInput(angle, 90f, false, true, 44));
        }
    }

    [UnityTest]
    public IEnumerator AStaggeringWaitingEnemyCanUseItsPhysicalGuard()
    {
        Call(master, "SpawnSingleNpc");
        object enemy = Field<IList>(master, "singleNpcs")[0];
        Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
        root.position = Player.position + Player.forward * 2.6f;
        root.rotation = Quaternion.LookRotation(-Player.forward);
        SetRuntimeField(enemy, "ChestCenter", Vector3.down * .0235f);
        SetRuntimeField(enemy, "Hearts", 3);
        SetRuntimeField(enemy, "GuardOctant", 2);
        SetRuntimeField(enemy, "StunnedUntil", Field<double>(master, "matchTime") + .5d);
        SetRuntimeField(enemy, "RecoveryUntil", Field<double>(master, "matchTime") + .5d);
        Call(master, "UpdateNpcGuard", enemy, 0f);
        PublishNpcGuardHistory(enemy);
        object player = Field<object>(master, "first");
        SetRuntimeField(player, "CutVelocity", Vector3.down);
        Transform sword = (Transform)RuntimeField(player, "Sword");
        StrikeThroughPhysicalGuard(enemy, player, sword);
        Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(3));
        Assert.That(Field<double>(master, "singleStunnedUntil"), Is.GreaterThan(Field<double>(master, "matchTime")),
            "The reachable perpendicular guard recoils the attacker even without an active enemy role.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator EnemyPreparationSwingAndReturnStayContinuousAndCollisionUsesTheVisibleBlade()
    {
        Call(master, "SpawnSingleNpc");
        object enemy = Field<IList>(master, "singleNpcs")[0];
        Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
        Transform sword = (Transform)RuntimeField(enemy, "Sword");
        root.position = Player.position + Player.forward * 3f;
        root.rotation = Quaternion.LookRotation(-Player.forward);
        SetRuntimeField(enemy, "GuardOctant", 0);
        SetRuntimeField(enemy, "NextAttack", 0d);
        SetRuntimeField(master, "singleActiveNpc", enemy);
        Call(master, "UpdateNpcGuard", enemy, 0f);
        bool sawPreparation = false, sawSwing = false, sawReturn = false;
        // Sample the faster cut finely enough to distinguish its raised-hand
        // motion from a discontinuity at the preparation/return boundaries.
        const float dt = .001f;
        for (int frame = 0; frame < 1200; frame++)
        {
            Quaternion previousRotation = sword.rotation;
            Vector3 previousGrip = sword.localPosition;
            SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + dt);
            MoveSingleNpcsWithReadyView(dt);
            float timer = (float)RuntimeField(enemy, "AttackTimer");
            float swing = (float)RuntimeField(enemy, "SwingSeconds");
            bool swinging = timer >= 0f && timer < swing;
            // Each half follows its own arc toward/away from the elevated aim.
            // SmoothStep's maximum derivative is 1.5; blade roll can add up to
            // sqrt(2) to the shortest orientation change in this hemisphere.
            Vector3 aim = root.TransformDirection((Vector3)RuntimeField(enemy, "AttackAim"));
            float arc = 2f * Mathf.Max(Vector3.Angle((Vector3)RuntimeField(enemy, "AttackStart"), aim),
                Vector3.Angle(aim, (Vector3)RuntimeField(enemy, "AttackEnd")));
            float maximumSwingStep = arc * 1.5f * dt / swing + .1f;
            Assert.That(Vector3.Angle(previousRotation * Vector3.forward, sword.forward),
                Is.LessThanOrEqualTo(swinging ? maximumSwingStep : 8f),
                "The blade direction must follow the announced arc without a jump.");
            Assert.That(Quaternion.Angle(previousRotation, sword.rotation), Is.LessThanOrEqualTo(swinging ? maximumSwingStep * Mathf.Sqrt(2f) : 8f),
                "The displayed blade must not snap at preparation, swing, or return boundaries.");
            Assert.That(Vector3.Distance(previousGrip, sword.localPosition), Is.LessThanOrEqualTo(swinging ? .05f : .008f));
            Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f, false, true, 0));
            // This fixture observes one authored swing and its return. The
            // separate counter test covers the new response to player stagger.
            if (Run.Hearts < ShowdownProgress.MaxHearts)
            {
                SetRuntimeField(master, "singleStunnedUntil", 0d);
                SetRuntimeField(enemy, "ContinuousAttack", false);
                SetRuntimeField(enemy, "NextAttack", 100d);
            }
            if (timer >= swing)
            {
                sawPreparation = true;
                Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts), "The preparation sweep is harmless.");
            }
            else if (timer >= 0f)
            {
                sawSwing = true;
                Assert.That(Vector3.Distance((Vector3)RuntimeField(enemy, "PreviousGrip"), sword.position), Is.LessThan(.0001f));
                Assert.That(Vector3.Distance((Vector3)RuntimeField(enemy, "PreviousBlade"), sword.forward), Is.LessThan(.000001f),
                    "Collision history must use the rendered blade pose.");
            }
            else sawReturn = true;
        }
        Assert.That(sawPreparation && sawSwing && sawReturn, Is.True);
        Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts - 2), "A visible close-range sweep damages once, despite many contact frames.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator EntireRoundAppearsScatteredWithOneApproachingSuccessorAndHiddenNextRoundPlacement()
    {
        Tick(.01f);
        Assert.That(Run.Spawned, Is.EqualTo(10), "All ten challengers appear on the first round frame.");
        object[] enemies = Field<IList>(master, "singleNpcs").Cast<object>().ToArray();
        Vector3[] positions = enemies.Select(enemy => ((GameObject)RuntimeField(enemy, "Root")).transform.position).ToArray();
        Assert.That(positions.Max(position => position.x) - positions.Min(position => position.x), Is.GreaterThan(10f));
        Assert.That(positions.Max(position => position.z) - positions.Min(position => position.z), Is.GreaterThan(10f));
        foreach (object enemy in enemies) SetRuntimeField(enemy, "NextAttack", 100d);
        Tick(1f); Tick(1f);
        for (int i = 0; i < enemies.Length; i++)
        {
            Assert.That(((GameObject)RuntimeField(enemies[i], "Root")).transform.position, Is.EqualTo(positions[i]));
            Assert.That(RuntimeField(enemies[i], "AttackTimer"), Is.EqualTo(-1f), "Round/Ready introduction cannot start an attack.");
        }
        Tick(.5f);
        Assert.That(Field<object>(master, "singleActiveNpc"), Is.SameAs(enemies[0]));
        Assert.That(Vector3.Distance(((GameObject)RuntimeField(enemies[0], "Root")).transform.position, positions[0]), Is.GreaterThan(.1f));
        Assert.That(Vector3.Distance(((GameObject)RuntimeField(enemies[1], "Root")).transform.position, positions[1]),
            Is.GreaterThan(ShowdownCombat.WaitingWalkSpeed * .5f), "The next opponent approaches while the current role remains selected.");
        Assert.That(RuntimeField(enemies[1], "AttackTimer"), Is.EqualTo(-1f));
        for (int i = 2; i < enemies.Length; i++)
            Assert.That(Vector3.Distance(((GameObject)RuntimeField(enemies[i], "Root")).transform.position, positions[i]),
                Is.InRange(.001f, ShowdownCombat.WaitingWalkSpeed * .5f + .001f), "Waiting enemies only stroll near their own anchors.");

        Call(master, "RemoveSingleNpc", enemies[0]);
        Tick(.1f);
        Assert.That(Field<object>(master, "singleActiveNpc"), Is.Null, "The next challenger observes the handoff pause.");
        Vector3 beforeActivation = ((GameObject)RuntimeField(enemies[1], "Root")).transform.position;
        Assert.That(Vector3.Distance(beforeActivation, Player.position), Is.LessThan(Vector3.Distance(positions[1], Player.position)));
        Tick(.1f);
        Assert.That(Field<object>(master, "singleActiveNpc"), Is.SameAs(enemies[1]));
        Assert.That(Vector3.Distance(((GameObject)RuntimeField(enemies[1], "Root")).transform.position, beforeActivation), Is.GreaterThan(.1f));
        Assert.That(Run.Spawned, Is.EqualTo(10), "Defeating one opponent does not spawn a replacement.");
        foreach (object enemy in enemies) Call(master, "RemoveSingleNpc", enemy);
        Tick(.01f);
        Assert.That(Get(master, "SingleAdvancing"), Is.True);
        object presentation = Field<object>(master, "presentation");
        UnityEngine.UI.Image fade = (UnityEngine.UI.Image)RuntimeField(presentation, "showdownFade");
        Vector3 departure = Player.position;
        Vector3 destination = Field<ShowdownCourse>(master, "singleCourse").Destination;
        Tick(.99f);
        Assert.That(Get(master, "SingleAdvancing"), Is.True);
        Assert.That(Vector3.Distance(Player.position, departure), Is.EqualTo(ShowdownCourse.WalkingSpeed * .99f).Within(.001f));
        Assert.That(fade.gameObject.activeSelf, Is.False, "The first second shows the player's walk before fading.");
        System.Array views = (System.Array)RuntimeField(presentation, "views");
        Assert.That(RuntimeField(views.GetValue(0), "ownBodyFade"), Is.False, "The walking player is visible at full opacity.");
        Assert.That(RuntimeField(views.GetValue(0), "walkingView"), Is.True, "Walking uses the pulled-back camera that shows the player's feet.");
        Tick(.5f);
        Assert.That(fade.color.a, Is.InRange(.45f, .55f));
        Assert.That(Vector3.Distance(Player.position, departure), Is.EqualTo(ShowdownCourse.WalkingSpeed).Within(.001f),
            "Walking ends after one second; the following fade cannot add another second of movement.");
        Assert.That(Run.Spawned, Is.EqualTo(10));
        Tick(.52f);
        Assert.That(Get(master, "SingleSection"), Is.EqualTo(2));
        Assert.That(Field<double>(master, "singleStarted"), Is.EqualTo(Field<double>(master, "matchTime")), "Arrival restarts the round introduction.");
        Assert.That(Run.Spawned, Is.EqualTo(20), "Arrival places the group while the screen remains opaque.");
        IList newGroup = Field<IList>(master, "singleNpcs");
        Assert.That(newGroup.Count, Is.EqualTo(10), "The blackout removes fallen bodies from the previous group.");
        foreach(object enemy in newGroup)
            Assert.That((bool)RuntimeField(enemy, "Defeated"), Is.False);
        Assert.That(fade.color.a, Is.EqualTo(1f).Within(.001f));
        Assert.That(Player.position, Is.EqualTo(destination), "Opaque transfer skips the long hidden walk.");
        Tick(.5f);
        Assert.That(fade.color.a, Is.InRange(.45f, .55f));
        Tick(.51f);
        Assert.That(fade.gameObject.activeSelf, Is.False);
        Tick(.01f);
        Assert.That(Run.Spawned, Is.EqualTo(20), "The next round's ten opponents appear together.");
        Assert.That(Call(master, "LivingSingleNpcCount"), Is.EqualTo(10));
        foreach (object enemy in Field<IList>(master, "singleNpcs"))
            if (!(bool)RuntimeField(enemy, "Defeated"))
                Assert.That(RuntimeField(enemy, "AttackTimer"), Is.EqualTo(-1f));
        Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts));
        yield return null;
    }

    [UnityTest]
    public IEnumerator RealEnemyPreparationNeverStartsFromBelowButItsGuardStillCan()
    {
        Call(master, "SpawnSingleNpc");
        object enemy = Field<IList>(master, "singleNpcs")[0];
        Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
        root.position = Player.position + Player.forward * 3f;
        root.rotation = Quaternion.LookRotation(-Player.forward);
        for (int preparation = 0; preparation < 32; preparation++)
        {
            SetRuntimeField(enemy, "AttackTimer", -1f);
            SetRuntimeField(enemy, "NextAttack", 0d);
            MoveSingleNpcsWithReadyView(.001f);
            Assert.That((float)RuntimeField(enemy, "AttackTimer"), Is.GreaterThan(0f));
            Assert.That(((Vector3)RuntimeField(enemy, "AttackReadyDirection")).y, Is.GreaterThanOrEqualTo(-.000001f));
        }
        SetRuntimeField(enemy, "AttackTimer", -1f);
        foreach (int octant in new[] { 3, 4, 5 })
        {
            SetRuntimeField(enemy, "GuardOctant", octant);
            Call(master, "UpdateNpcGuard", enemy, 0f);
            Vector3 localGuard = root.InverseTransformDirection((Vector3)RuntimeField(enemy, "GuardDirection"));
            Assert.That(localGuard.y, Is.LessThan(-.7f), "Lower directions remain valid guard orientations.");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator EasyDifficultySlowsTheRealEnemyAndSurvivesSectionTravelAndRetry()
    {
        Call(master, "SetGameplaySuspended", true); Call(master, "SetSingleDifficulty", true);
        Call(master, "ResetMatch"); Call(master, "SetGameplaySuspended", false);
        Assert.That(Get(master, "IsEasySingleDifficulty"), Is.True);
        Call(master, "SpawnSingleNpc");
        object enemy = Field<IList>(master, "singleNpcs")[0];
        Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
        root.position = Player.position + Player.forward * 3f;
        root.rotation = Quaternion.LookRotation(-Player.forward);
        SetRuntimeField(enemy, "NextAttack", 0d);
        MoveSingleNpcsWithReadyView(.01f);
        var easy = new ShowdownDifficulty(1, true);
        Assert.That(RuntimeField(enemy, "WindupSeconds"), Is.EqualTo(easy.WindupSeconds));
        Assert.That(RuntimeField(enemy, "SwingSeconds"), Is.EqualTo(easy.SwingSeconds));
        SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + 3d);
        ClearSection();
        int frames = 0;
        while ((bool)Get(master, "SingleAdvancing") && frames++ < 200) Tick(.1f);
        Assert.That(Get(master, "SingleSection"), Is.EqualTo(2));
        Assert.That(Get(master, "IsEasySingleDifficulty"), Is.True);
        Call(master, "ResetMatch");
        Assert.That(Get(master, "IsEasySingleDifficulty"), Is.True, "Retry keeps the selected difficulty.");
        Assert.That(Get(master, "SingleSection"), Is.EqualTo(1));
        Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts));
        Call(master, "SetSingleDifficulty", false);
        Assert.That(Get(master, "IsEasySingleDifficulty"), Is.True, "Difficulty cannot change in active gameplay.");
        Call(master, "SetGameplaySuspended", true); Call(master, "SetSingleDifficulty", false);
        Assert.That(Get(master, "IsEasySingleDifficulty"), Is.False);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ShortCutsNeedRealPreparationAndDoNotDamageDuringTakeback()
    {
        Call(master, "SpawnSingleNpc");
        object npc = Field<IList>(master, "singleNpcs")[0];
        Transform root = ((GameObject)RuntimeField(npc, "Root")).transform;
        root.SetPositionAndRotation(Player.position + Player.forward * 2.5f, Quaternion.LookRotation(-Player.forward));
        SetRuntimeField(npc, "Hearts", 3);
        SetRuntimeField(npc, "GuardOctant", 0);
        object player = Field<object>(master, "first");
        Transform sword = (Transform)RuntimeField(player, "Sword");
        void Cut(float angle, float speed)
        {
            SetRuntimeField(player, "CutVelocity", angle > 10f ? Vector3.up : Vector3.down);
            sword.position = Player.position + Vector3.up * .15f + Player.forward * .55f;
            sword.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(-angle, Player.right) * Player.forward, Player.up);
            UpdateSingleAttackWithManualSword(new CombatInput(angle, speed, false, true, 700));
        }
        Cut(35f, 0f);
        SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + .1d);
        Cut(10f, 90f);
        Assert.That(RuntimeField(npc, "Hearts"), Is.EqualTo(2), "A short 25-degree real swing must damage without a raised-sword prerequisite.");
        Cut(5f, 90f);
        Assert.That(RuntimeField(npc, "Hearts"), Is.EqualTo(2), "The continuing consumed swing cannot damage twice.");
        SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + DuelMatch.HitStopSeconds + DuelMatch.PushSeconds + .01d);
        Call(master, "AdvanceSingleReaction", Field<double>(master, "matchTime"));
        Cut(5f, 0f);
        SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + .1d);
        Cut(35f, 90f);
        Assert.That(RuntimeField(npc, "Hearts"), Is.EqualTo(2), "Returning to the preparation cannot score another hit.");
        SetRuntimeField(npc, "GuardOctant", 0);
        Call(master, "UpdateNpcGuard", npc, 0f);
        PublishNpcGuardHistory(npc);
        SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + .1d);
        Cut(10f, 90f);
        Assert.That(RuntimeField(npc, "Hearts"), Is.EqualTo(1),
            "A newly prepared short cut is valid; the staggered enemy's parallel vertical guard does not make it immune.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator SlowingBelowTheThresholdStaticBladesAndExpiredCutsCannotHit()
    {
        Call(master, "SpawnSingleNpc");
        object enemy = Field<IList>(master, "singleNpcs")[0];
        Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
        root.position = Player.position + Player.forward * 2.5f;
        root.rotation = Quaternion.LookRotation(-Player.forward);
        SetRuntimeField(enemy, "Hearts", 3); SetRuntimeField(enemy, "StunnedUntil", 100d);
        SetRuntimeField(enemy, "GuardOctant", 0);
        Call(master, "UpdateNpcGuard", enemy, 0f);
        PublishNpcGuardHistory(enemy);
        Transform sword = (Transform)RuntimeField(Field<object>(master, "first"), "Sword");
        void Stroke(float angle, float speed)
        {
            sword.position = Player.position + Vector3.up * .15f + Player.forward * .55f;
            sword.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(-angle, Player.right) * Player.forward, Player.up);
            UpdateSingleAttackWithManualSword(new CombatInput(angle, speed, false, true, 44));
        }
        Stroke(80f, 0f); Stroke(45f, 8f); Stroke(5f, 8f);
        Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(3), "A slow movement cannot begin a strike.");
        Stroke(80f, 120f); Stroke(45f, 120f); Stroke(5f, 8f);
        Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(3), "A recognized cut becomes harmless as it slows below the required speed.");
        Stroke(80f, 120f); Stroke(45f, 120f); Stroke(5f, 120f);
        Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(2), "A fast prepared cut still damages.");
        SetRuntimeField(master, "matchTime", (double)RuntimeField(enemy, "RecoveryUntil") + .01d);
        SetRuntimeField(enemy, "StunnedUntil", 100d);
        // Begin the next real stroke out of reach: raising the sword is a
        // valid upward cut, not an inert eighty-degree rearm any more.
        root.position = Player.position + Player.forward * 10f;
        Stroke(80f, 120f); Stroke(45f, 120f);
        Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(2));
        root.position = Player.position + Player.forward * 1.2f + Vector3.up * .5f;
        Stroke(45f, 0f);
        Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(2), "NPC movement into a stationary blade does not create a strike.");
        root.position = Player.position + Player.forward * 2.5f;
        SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + AttackStroke.CutSeconds + .01d);
        Stroke(5f, 8f);
        Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(2), "The committed contact window expires even if the blade later reaches the body.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator NpcCrossingGuardInterceptsButParallelAndDistantBladesCannot()
    {
        for (int scenario = 0; scenario < 3; scenario++)
        {
            Call(master, "ResetMatch"); Call(master, "SpawnSingleNpc");
            object enemy = Field<IList>(master, "singleNpcs")[0];
            Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
            root.position = Player.position + Player.forward * 2.6f;
            root.rotation = Quaternion.LookRotation(-Player.forward);
            SetRuntimeField(enemy, "ChestCenter", Vector3.down * .0235f);
            SetRuntimeField(enemy, "Hearts", 3);
            SetRuntimeField(enemy, "GuardOctant", scenario == 1 ? 0 : 2);
            SetRuntimeField(enemy, "NextAttack", 100d);
            SetRuntimeField(master, "singleActiveNpc", enemy);
            Call(master, "UpdateNpcGuard", enemy, 0f);
            // Establish the valid physical path independently of the wrong
            // direction or distant-blade variants, so all three receive the
            // same player stroke and finish through the body.
            int selectedOctant = scenario == 1 ? 0 : 2;
            SetRuntimeField(enemy, "GuardOctant", 2);
            Call(master, "UpdateNpcGuard", enemy, 0f);
            Transform guard = (Transform)RuntimeField(enemy, "Sword");
            Vector3 intercept = guard.position + guard.forward * .8f;
            SetRuntimeField(enemy, "GuardOctant", selectedOctant);
            Call(master, "UpdateNpcGuard", enemy, 0f);
            if (scenario == 2) guard.position += Player.right * 4f;
            SetRuntimeField(enemy, "VisualBasePose", new SwordPose(guard.localPosition, guard.localRotation));
            PublishNpcGuardHistory(enemy);
            object player = Field<object>(master, "first");
            SetRuntimeField(player, "CutVelocity", Vector3.down);
            Transform sword = (Transform)RuntimeField(player, "Sword");
            StrikeThroughPhysicalGuard(enemy, player, sword, intercept);
            Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(scenario == 0 ? 3 : 2),
                "The crossing blade blocks, while a parallel or distant blade must not. Scenario " + scenario);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator PlayerStrikeFollowsThePosedNpcChestInsteadOfHittingAnEmptyRootPoint()
    {
        Call(master, "SpawnSingleNpc");
        object enemy = Field<IList>(master, "singleNpcs")[0];
        Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
        root.position = Player.position + Player.forward * 2.5f;
        SetRuntimeField(enemy, "Hearts", 3); SetRuntimeField(enemy, "StunnedUntil", 100d);
        Transform chest = (Transform)RuntimeField(RuntimeField(enemy, "Avatar"), "chest");
        Assert.That(chest, Is.Not.Null);
        chest.position += Player.right * 1.2f;
        Transform sword = (Transform)RuntimeField(Field<object>(master, "first"), "Sword");
        for (int stroke = 0; stroke < 2; stroke++)
        {
            foreach (float angle in new[] { 80f, 45f, 5f })
            {
                sword.position = Player.position + Player.right * (stroke == 0 ? 0f : 1.2f)
                    + Vector3.up * .15f + Player.forward * .55f;
                sword.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(-angle, Player.right) * Player.forward, Player.up);
                UpdateSingleAttackWithManualSword(new CombatInput(angle, 90f, false, true, 44));
            }
            Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(stroke == 0 ? 3 : 2),
                "Only the cut through the displayed chest should contact the enemy.");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator FourBasicCutDirectionsCrossBothPlayerAndNpcGuardsWhileParallelGuardsBreak()
    {
        Call(master, "SpawnSingleNpc");
        object npc = Field<IList>(master, "singleNpcs")[0];
        object player = Field<object>(master, "first");
        Transform npcRoot = ((GameObject)RuntimeField(npc, "Root")).transform;
        Player.rotation = Quaternion.Euler(0f, 37f, 0f);
        // UpdateNpcGuard requires a nonzero direction to the player. Keeping
        // both roots coincident would leave the old waiting sword unchanged.
        npcRoot.SetPositionAndRotation(Player.position + Player.forward * 3f, Player.rotation);
        SetRuntimeField(master, "singleActiveNpc", npc);
        SetRuntimeField(npc, "AttackTimer", -1f);
        SetRuntimeField(npc, "ContinuousAttack", false);
        object avatar = RuntimeField(npc, "Avatar");
        Vector3 chestOffset = (Vector3)avatar.GetType().GetProperty("ChestOffset").GetValue(avatar);
        SetRuntimeField(npc, "ChestCenter", (Vector3)Call(master, "PlayerChestCenter", player) - chestOffset);
        foreach (Vector3 localCut in new[] { Vector3.down, Vector3.up, Vector3.left, Vector3.right })
        foreach (bool crossing in new[] { true, false })
        {
            Vector3 guardDirection = crossing
                ? Mathf.Abs(localCut.y) > .5f ? Vector3.right : Vector3.up
                : localCut;
            int octant = guardDirection == Vector3.up ? 0 : guardDirection == Vector3.right ? 2
                : guardDirection == Vector3.down ? 4 : 6;
            SetRuntimeField(npc, "GuardOctant", octant);
            Call(master, "UpdateNpcGuard", npc, 0f);
            SwordPose npcGuard = (SwordPose)RuntimeField(npc, "VisualBasePose");
            SetRuntimeField(npc, "PreviousGrip", npcRoot.TransformPoint(npcGuard.Position));
            SetRuntimeField(npc, "PreviousBlade", npcRoot.TransformDirection(npcGuard.Direction));
            SetRuntimeField(player, "TargetRotation", Quaternion.FromToRotation(Vector3.forward, guardDirection));
            Call(master, "UpdatePose", player, true);
            SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + GuardPoseTransition.DurationSeconds + .01d);
            Call(master, "UpdatePose", player, true);
            // This fixture holds a settled guard. Establish its source and
            // guard-generation baseline instead of interpolating from the
            // unrelated setup sword pose during the incoming cut.
            SetRuntimeField(player, "CombatBaseline", false);
            Call(master, "PlayerCombatPose", player, new CombatInput(90f, 0f, true, true, 1300 + octant), false);
            CombatPose playerDefence = (CombatPose)Call(master, "PlayerDefencePose", player);
            CombatPose npcDefence = (CombatPose)Call(master, "NpcCombatPose", npc);
            // A guard changes only defence geometry; it does not receive a
            // more forgiving attack path depending on whether its owner is AI.
            foreach (CombatPose defence in new[] { playerDefence, npcDefence })
            {
                Vector3 forward = -defence.Forward;
                Vector3 cut = Player.TransformDirection(localCut);
                Vector3 grip = defence.BodyCenter + defence.Forward * 1.8f + Vector3.up * .55f;
                Vector3 previous = (forward * Mathf.Cos(20f * Mathf.Deg2Rad)
                    - cut * Mathf.Sin(20f * Mathf.Deg2Rad)).normalized;
                var attack = new CombatPose(grip, previous, grip, forward,
                    defence.BodyCenter + defence.Forward * 2.4f, forward, cut,
                    grip + Vector3.up * 20f, Vector3.back);
                CombatOutcome expected = crossing ? CombatOutcome.GuardSuccess : CombatOutcome.GuardBreak;
                CombatResult fromPlayer = CombatExchange.Resolve(AttackKind.Cut, AttackKind.None,
                    attack, defence, false, true).Contact;
                CombatResult fromNpc = CombatExchange.Resolve(AttackKind.None, AttackKind.Cut,
                    defence, attack, true, false).Contact;
                Assert.That(fromPlayer.Outcome, Is.EqualTo(expected),
                    "Player attack must use the tutorial's crossing rule. Cut " + localCut + ", crossing " + crossing);
                Assert.That(fromNpc.Outcome, Is.EqualTo(expected),
                    "NPC and duel's reverse role must obey the same crossing rule. Cut " + localCut + ", crossing " + crossing);
                Assert.That(fromPlayer.Attacker, Is.EqualTo(0));
                Assert.That(fromNpc.Attacker, Is.EqualTo(1));
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator AllEightPlayerGuardsUseSharedFiniteContactAtDifferentFrameRates()
    {
        // NPCs face the player: diagonal ready directions are mirrored across
        // the two characters, while the actual guard blade stays player-local.
        int[] readyOctants = { 2, 1, 0, 7, 2, 1, 0, 7 };
        foreach (float dt in new[] { .005f, .01f, 1f / 60f, 1f / 30f })
        foreach (float distance in new[] { 3f, 3.05f, 3.1f })
        for (int scenario = 0; scenario < 13; scenario++)
        {
            if (scenario >= 8 && distance != 3f) continue;
            Call(master, "ResetMatch");
            bool shouldBlock = false;
            Vector3 guardDirection = ShowdownCombat.ReadyDirection(scenario < 8 ? scenario : scenario == 8 ? 2 : 0);
            bool held = scenario != 9, valid = scenario != 10;
            object player = Field<object>(master, "first");
            SetRuntimeField(player, "TargetRotation", Quaternion.LookRotation(guardDirection,
                Mathf.Abs(guardDirection.y) > .9f ? Vector3.forward : Vector3.up));
            Call(master, "UpdatePose", player, true);
            SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + GuardPoseTransition.DurationSeconds + .01d);
            Call(master, "UpdatePose", player, true);
            Transform guard = (Transform)RuntimeField(player, "Sword");
            SwordPose pose = (SwordPose)RuntimeField(player, "TargetPose");
            guard.SetLocalPositionAndRotation(pose.Position, pose.Rotation);
            if (scenario == 11)
            {
                guard.position += Player.right * 4f;
                SetRuntimeField(player, "TargetPose", new SwordPose(guard.localPosition, guard.localRotation));
            }

            Call(master, "SpawnSingleNpc");
            object enemy = Field<IList>(master, "singleNpcs")[0];
            Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
            root.position = Player.position + Player.forward * distance;
            root.rotation = Quaternion.LookRotation(Player.position - root.position);
            SetRuntimeField(enemy, "NextAttack", 0d);
            MoveSingleNpcsWithReadyView(.01f);
            if (scenario == 12)
            {
                // Start an announced cut, then put it behind the unchanged
                // defender. The focus helper would otherwise turn the player
                // around and accidentally turn this rear-guard test into front guard.
                Assert.That((float)RuntimeField(enemy, "AttackTimer"), Is.GreaterThan(0f));
                root.position = Player.position - Player.forward * distance;
                root.rotation = Quaternion.LookRotation(Player.position - root.position);
                SetRuntimeField(enemy, "AdvanceDuringWindup", false);
            }
            SetRuntimeField(enemy, "AttackReadyDirection", ShowdownCombat.ReadyDirection(scenario < 8 ? readyOctants[scenario] : 2));
            bool resolved = false;
            CombatPose contactAttack = default, contactDefence = default;
            for (int frame = 0; frame < Mathf.CeilToInt(1f / dt); frame++)
            {
                SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + dt);
                if (scenario == 12) Call(master, "MoveSingleNpcs", dt);
                else MoveSingleNpcsWithReadyView(dt);
                contactAttack = (CombatPose)Call(master, "NpcCombatPose", enemy);
                contactDefence = (CombatPose)Call(master, "PlayerDefencePose", player);
                CombatContactResult shared = CombatContact.Resolve(AttackKind.Cut, contactAttack, contactDefence, held && valid);
                shouldBlock = shared.Outcome == CombatOutcome.GuardSuccess;
                Call(master, "ResolveSingleNpcAttacks", new CombatInput(90f, 0f, held, valid, 0));
                if (!(bool)RuntimeField(enemy, "ContactResolved")) continue;
                resolved = true;
                break;
            }
            if (scenario == 12)
                Assert.That(Vector3.Dot(contactAttack.BodyCenter - contactDefence.BodyCenter, contactDefence.Forward),
                    Is.LessThan(0f), "The attacker must still be behind the defender at the actual contact.");
            Assert.That(resolved, Is.True, "The real cut must resolve in scenario " + scenario + " at dt " + dt + " and distance " + distance);
            if (scenario >= 8)
                Assert.That(shouldBlock, Is.False,
                    "Parallel, unheld, invalid, distant and rear guards must not become successful defences. Scenario " + scenario);
            Assert.That(Run.Hearts, Is.EqualTo(shouldBlock ? ShowdownProgress.MaxHearts : ShowdownProgress.MaxHearts - (held ? 1 : 2)),
                "Single uses the same finite blade guard as duel; selecting guard alone must not cover the hands or distant blade. Scenario "
                + scenario + " at dt " + dt + " and distance " + distance + DescribeCutGuardContact(contactAttack, contactDefence));
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator NpcAndPlayerCutsUseIdenticalCommittedGuardEndpoints()
    {
        Call(master, "SpawnSingleNpc");
        object npc = Field<IList>(master, "singleNpcs")[0];
        object player = Field<object>(master, "first");
        Transform root = ((GameObject)RuntimeField(npc, "Root")).transform;
        root.SetPositionAndRotation(Player.position, Player.rotation);
        object avatar = RuntimeField(npc, "Avatar");
        Vector3 offset = (Vector3)avatar.GetType().GetProperty("ChestOffset").GetValue(avatar);
        SetRuntimeField(npc, "ChestCenter", (Vector3)Call(master, "PlayerChestCenter", player) - offset);
        for (int octant = 0; octant < 8; octant++)
        {
            Vector3 direction = ShowdownCombat.ReadyDirection(octant);
            SetRuntimeField(player, "TargetRotation", Quaternion.FromToRotation(Vector3.forward, direction));
            Call(master, "UpdatePose", player, false);
            var target = (SwordPose)RuntimeField(player, "TargetPose");
            SetRuntimeField(npc, "VisualBasePose", target);
            Vector3 grip = root.TransformPoint(target.Position);
            SetRuntimeField(npc, "PreviousGrip", grip);
            SetRuntimeField(npc, "PreviousBlade", root.forward);
            SetRuntimeField(npc, "AttackDirection", root.TransformDirection(Vector3.up));
            CombatPose enemy = (CombatPose)Call(master, "NpcCombatPose", npc);
            CombatPose human = (CombatPose)Call(master, "PlayerCombatPose", player,
                new CombatInput(90f, 0f, false, true, 900 + octant), false);
            Assert.That(Vector3.Distance(enemy.EndGrip, human.EndGrip), Is.LessThan(.00001f),
                "Committed-cut guard interception must not depend on whether the attacker is AI.");
            Assert.That(Vector3.Angle(enemy.EndBlade, human.EndBlade), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(enemy.Grip, human.Grip), Is.LessThan(.00001f));
            Assert.That(Vector3.Angle(enemy.Blade, human.Blade), Is.LessThan(.01f));
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator PlayerLogicalGuardMatchesDuelContactWhileTheVisibleSwordStillLags()
    {
        for (int octant = 0; octant < 8; octant++)
        {
            Call(master, "ResetMatch");
            Player.rotation = Quaternion.Euler(0f, 37f, 0f);
            object player = Field<object>(master, "first");
            Vector3 direction = ShowdownCombat.ReadyDirection(octant);
            SetRuntimeField(player, "TargetRotation", Quaternion.LookRotation(direction,
                Mathf.Abs(direction.y) > .9f ? Vector3.forward : Vector3.up));
            Call(master, "UpdatePose", player, true);
            SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + GuardPoseTransition.DurationSeconds + .01d);
            Call(master, "UpdatePose", player, true);
            Transform sword = (Transform)RuntimeField(player, "Sword");
            CombatPose duel = (CombatPose)Call(master, "PlayerCombatPose", player,
                new CombatInput(90f, 0f, true, true, 600 + octant), false);
            Vector3 forward = duel.Forward.normalized;
            var thrust = new CombatPose(duel.BodyCenter + forward * 1.8f, -forward,
                duel.BodyCenter + forward * .8f, -forward, duel.BodyCenter + forward * 2.8f,
                -forward, -forward, duel.BodyCenter + forward * .8f, -forward);
            CombatContactResult expected = CombatContact.Resolve(AttackKind.Thrust, thrust, duel, true);
            Assert.That(expected.Outcome, Is.EqualTo(CombatOutcome.GuardSuccess).Or.EqualTo(CombatOutcome.GuardBreak), "A real body contact is required for guard octant " + octant);

            for (int lag = 0; lag < 2; lag++)
            {
                sword.position = duel.Grip + (lag == 0 ? Vector3.zero : Player.right * 4f);
                Vector3 visibleDirection = lag == 0 ? forward : duel.Blade;
                sword.rotation = Quaternion.LookRotation(visibleDirection,
                    Mathf.Abs(Vector3.Dot(visibleDirection, Vector3.up)) > .9f
                        ? Player.forward : Vector3.up);
                var visible = new CombatPose(sword.position, sword.forward, sword.position, sword.forward,
                    duel.BodyCenter, forward, Vector3.zero, sword.position, sword.forward);
                if (lag != 0)
                    Assert.That(CombatContact.Resolve(AttackKind.Thrust, thrust, visible, true).Outcome,
                        Is.EqualTo(CombatOutcome.GuardBreak), "The distant displayed sword cannot stop the thrust.");

                CombatPose single = (CombatPose)Call(master, "PlayerDefencePose", player);
                Assert.That(Vector3.Distance(single.Grip, duel.Grip), Is.LessThan(.00001f));
                Assert.That(Vector3.Angle(single.Blade, duel.Blade), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(single.BodyCenter, duel.BodyCenter), Is.LessThan(.00001f));
                Assert.That(Vector3.Angle(single.Forward, duel.Forward), Is.LessThan(.01f));
                CombatContactResult actual = CombatContact.Resolve(AttackKind.Thrust, thrust, single, true);
                Assert.That(actual.Outcome, Is.EqualTo(expected.Outcome), "Visible lag must not change guard octant " + octant);
                Assert.That(actual.Fraction, Is.EqualTo(expected.Fraction).Within(.00001f));
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator OpeningAttackersPrepareWhileApproachingAndOnlyFlashJustBeforeSwinging()
    {
        Call(master, "SpawnSingleNpc"); Call(master, "SpawnSingleNpc");
        IList enemies = Field<IList>(master, "singleNpcs");
        for (int i = 0; i < enemies.Count; i++)
        {
            object enemy = enemies[i];
            Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
            root.position = Player.position + new Vector3(i == 0 ? -1.5f : 1.5f, 0f, 4.2f);
            root.rotation = Quaternion.LookRotation(Player.position - root.position);
            SetRuntimeField(enemy, "NextAttack", 0d);
        }
        MoveSingleNpcsWithReadyView(.01f);
        Assert.That(RuntimeField(enemies[0], "AttackTimer"), Is.GreaterThan(0f), "The opening attacker prepares outside normal range.");
        Assert.That(RuntimeField(enemies[1], "AttackTimer"), Is.LessThan(0f), "An ordinary opponent continues its guarded approach.");
        Transform attacker = ((GameObject)RuntimeField(enemies[0], "Root")).transform;
        Vector3 preparedAt = attacker.position;
        SetRuntimeField(enemies[1], "StunnedUntil", 100d);
        bool sawCue = false, sawSwing = false;
        Component status = (Component)RuntimeField(enemies[0], "Status");
        Assert.That(status.transform.Find("Windup"), Is.Null);
        Assert.That(status.transform.Find("Windup background"), Is.Null);
        for (int frame = 0; frame < 85; frame++)
        {
            SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + .01d);
            MoveSingleNpcsWithReadyView(.01f);
            Call(master, "TickSingleNpcAvatars", Field<double>(master, "matchTime"), .01f);
            float timer = (float)RuntimeField(enemies[0], "AttackTimer");
            float swing = (float)RuntimeField(enemies[0], "SwingSeconds");
            Component cue = (Component)RuntimeField(status, "attackCue");
            bool shown = cue.GetComponent<Canvas>().enabled;
            if (shown)
            {
                sawCue = true;
                Assert.That(timer, Is.GreaterThan(swing));
                Assert.That(timer, Is.LessThanOrEqualTo(swing + ShowdownCombat.AttackCueSeconds));
            }
            if (timer < swing) { sawSwing = true; Assert.That(shown, Is.False); }
        }
        Assert.That(Vector3.Distance(attacker.position, Player.position), Is.LessThan(Vector3.Distance(preparedAt, Player.position) - .5f));
        Assert.That(sawCue && sawSwing, Is.True);
        yield return null;
    }

    [UnityTest]
    public IEnumerator NpcGuardsUseTheSameSwordPoseAsPlayersInAllEightDirections()
    {
        Call(master, "SpawnSingleNpc");
        object enemy = Field<IList>(master, "singleNpcs")[0];
        SetRuntimeField(master, "singleActiveNpc", enemy);
        Transform sword = (Transform)RuntimeField(enemy, "Sword");
        Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
        root.rotation = Quaternion.Euler(0f, 37f, 0f);
        for (int octant = 0; octant < 8; octant++)
        {
            SetRuntimeField(enemy, "GuardOctant", octant);
            Call(master, "UpdateNpcGuard", enemy, 0f);
            Vector3 direction = ShowdownCombat.ReadyDirection(octant);
            Quaternion input = Quaternion.FromToRotation(Vector3.forward, direction);
            Vector3 remembered = Vector3.up;
            Vector3 center = (Vector3)RuntimeField(enemy, "ChestCenter")
                + (Vector3)Get(RuntimeField(enemy, "Avatar"), "ChestOffset");
            SwordPose expected = SwordPoseMath.Calculate(input, true, new SwordPoseSettings(center,
                Field<float>(master, "gripRadius"), Field<float>(master, "guardRadius"),
                Field<float>(master, "guardPlaneOffset"), Field<float>(master, "guardHeight")), ref remembered);
            Assert.That(Vector3.Distance(sword.localPosition, expected.Position), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(sword.localRotation, expected.Rotation), Is.LessThan(.01f));
            Assert.That(Vector3.Angle((Vector3)RuntimeField(enemy, "GuardDirection"), root.TransformDirection(direction)), Is.LessThan(.01f));
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator AllEightEnemyCutsKeepRenderedAndCollisionEndpointsTogetherWhenAFrameSkipsTheSwing()
    {
        for (int octant = 0; octant < 8; octant++)
        {
            Call(master, "ResetMatch"); Call(master, "SpawnSingleNpc");
            object enemy = Field<IList>(master, "singleNpcs")[0];
            Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
            Transform sword = (Transform)RuntimeField(enemy, "Sword");
            root.position = Player.position + Player.forward * 3f;
            root.rotation = Quaternion.LookRotation(-Player.forward);
            SetRuntimeField(enemy, "OpeningAttack", false);
            SetRuntimeField(enemy, "NextAttack", 0d);
            MoveSingleNpcsWithReadyView(0f);
            Assert.That(RuntimeField(enemy, "AttackTimer"), Is.GreaterThan(0f));
            SetRuntimeField(enemy, "AttackReadyDirection", ShowdownCombat.ReadyDirection(octant));
            float swing = (float)RuntimeField(enemy, "SwingSeconds");
            SetRuntimeField(enemy, "AttackTimer", swing + .01f);
            MoveSingleNpcsWithReadyView(.005f);
            var settings = new SwordPoseSettings((Vector3)RuntimeField(enemy, "ChestCenter"),
                Field<float>(master, "gripRadius"), Field<float>(master, "guardRadius"), Field<float>(master, "guardPlaneOffset"), Field<float>(master, "guardHeight"));
            SwordPose start = ShowdownCombat.CutPose((Vector3)RuntimeField(enemy, "AttackAim"),
                ShowdownCombat.ReadyDirection(octant), 0f, settings);
            SwordPose end = ShowdownCombat.CutPose((Vector3)RuntimeField(enemy, "AttackAim"),
                ShowdownCombat.ReadyDirection(octant), 1f, settings);
            MoveSingleNpcsWithReadyView(swing + .02f);
            var pose = (CombatPose)Call(master, "NpcCombatPose", enemy);
            Assert.That(Vector3.Distance(sword.localPosition, end.Position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(sword.localRotation, end.Rotation), Is.LessThan(.01f));
            Assert.That(Vector3.Angle(pose.PreviousBlade, root.TransformDirection(start.Direction)), Is.LessThan(.05f),
                "A coarse frame must preserve the full real ready-to-end sweep, including its observed entry into the forward region.");
            Vector3 remembered = Vector3.up;
            SwordPose committed = SwordPoseMath.Calculate(Quaternion.identity, false, settings, ref remembered);
            Assert.That(Vector3.Distance(pose.EndGrip, root.TransformPoint(committed.Position)), Is.LessThan(.0001f));
            Assert.That(Vector3.Angle(pose.EndBlade, root.forward), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(pose.Grip, sword.position), Is.LessThan(.0001f));
            Assert.That(Vector3.Angle(pose.Blade, sword.forward), Is.LessThan(.01f));
            var defence = (CombatPose)Call(master, "PlayerDefencePose", Field<object>(master, "first"));
            Assert.That(CombatContact.Resolve(AttackKind.Cut, pose, defence, false).Outcome, Is.EqualTo(CombatOutcome.Hit),
                "The skipped visible swing must still contact the player's body from octant " + octant + ".");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator SurvivingEnemySmoothlyChangesItsContinuousGuardAfterEveryReceivedHit()
    {
        Call(master, "SpawnSingleNpc");
        object enemy = Field<IList>(master, "singleNpcs")[0];
        SetRuntimeField(master, "singleActiveNpc", enemy);
        Transform root = ((GameObject)RuntimeField(enemy, "Root")).transform;
        root.position = Player.position + Player.forward * 2f;
        root.rotation = Quaternion.LookRotation(-Player.forward);
        SetRuntimeField(enemy, "Hearts", 3);
        Assert.That(RuntimeField(enemy, "GuardOctant"), Is.EqualTo(-1));
        float initialBias = (float)RuntimeField(enemy, "GuardBiasTarget");
        Transform sword = (Transform)RuntimeField(Field<object>(master, "first"), "Sword");
        for (int hit = 1; hit <= 2; hit++)
        {
            if (hit > 1)
            {
                SetRuntimeField(master, "matchTime", (double)RuntimeField(enemy, "RecoveryUntil") + .01d);
            }
            // Observe direction selection after actual damage without letting
            // the newly selected guard intercept the fixture's next attack.
            SetRuntimeField(enemy, "StunnedUntil", 0d);
            SetRuntimeField(master, "singleActiveNpc", null);
            foreach (float angle in new[] { 80f, 5f })
            {
                sword.position = Player.position + Vector3.up * .15f + Player.forward * .55f;
                sword.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(-angle, Player.right) * Player.forward, Player.up);
                UpdateSingleAttackWithManualSword(new CombatInput(angle, 90f, false, true, 44));
            }
            Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(3 - hit));
            Assert.That(RuntimeField(enemy, "GuardOctant"), Is.EqualTo(-1));
            Assert.That(RuntimeField(enemy, "GuardBiasTarget"), Is.EqualTo(initialBias + hit * 90f));
            SetRuntimeField(master, "singleActiveNpc", enemy);
            float previousBias = (float)RuntimeField(enemy, "GuardBias");
            SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + .01d);
            Call(master, "UpdateNpcGuard", enemy, .01f);
            Assert.That((float)RuntimeField(enemy, "GuardBias") - previousBias, Is.InRange(.29f, .31f),
                "A hit changes the guard's destination gradually, not through an octant snap.");
            Transform blade = (Transform)RuntimeField(enemy, "Sword");
            CombatPose pose = (CombatPose)Call(master, "NpcCombatPose", enemy);
            Assert.That(Vector3.Angle(blade.forward, pose.Blade), Is.LessThan(.01f), "The continuously moving visible guard and contact agree.");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator FreeModeUsesBothPlayersWithoutEnemiesClockWinsOrRoundAdvancement()
    {
        Call(master, "SetGameplaySuspended", true);
        Call(master, "SetGameMode", false); Call(master, "SetFreeMode", true);
        Call(master, "SetGameplaySuspended", false);
        object series = Field<object>(master, "series");
        object phase = Get(series, "Phase");
        SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + 180d);
        Call(master, "LateUpdate");
        Assert.That(Get(master, "IsFreeMode"), Is.True);
        Assert.That(Get(master, "IsSingleMode"), Is.False);
        Assert.That(Get(series, "Phase"), Is.EqualTo(phase));
        Assert.That(Get(series, "FirstScore"), Is.Zero);
        Assert.That(Get(series, "SecondScore"), Is.Zero);
        Assert.That(Get(master, "Winner"), Is.EqualTo(MatchWinner.None));
        Assert.That(Field<IList>(master, "singleNpcs").Count, Is.Zero);
        Assert.That(Field<GameObject>(master, "PlayerObject2").GetComponentsInChildren<Renderer>().Any(r => r.enabled), Is.True);
        object presentation = Field<object>(master, "presentation");
        Assert.That(((GameObject)RuntimeField(presentation, "matchHud")).activeSelf, Is.False);
        Call(master, "SetGameplaySuspended", true); Call(master, "SetFreeMode", false); Call(master, "SetGameMode", true);
        Assert.That(Get(master, "IsFreeMode"), Is.False);
        Assert.That(Get(master, "IsSingleMode"), Is.True);
        yield return null;
    }

    [UnityTest]
    public IEnumerator P2ForwardImpulseSteersTheSingleSwordAndHitsOnlyOnce()
    {
        yield return ForwardImpulseHitsThroughSceneController(true);
    }

    [UnityTest]
    public IEnumerator DuelForwardImpulseUsesTheSharedContactAndThrustPush()
    {
        yield return ForwardImpulseHitsThroughSceneController(false);
    }

    private IEnumerator ForwardImpulseHitsThroughSceneController(bool single)
    {
        if (!single)
        {
            Call(master, "SetGameplaySuspended", true); Call(master, "SetGameMode", false); Call(master, "ResetMatch");
            Call(Field<object>(master, "series"), "Enter", RoundPhase.Fight, Field<double>(master, "matchTime"));
            Call(master, "SetGameplaySuspended", false);
        }
        else SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + 3d);

        object player = Field<object>(master, "first"), other = Field<object>(master, "second");
        var motions = new[] { (MonoBehaviour)RuntimeField(player, "Motion"), (MonoBehaviour)RuntimeField(other, "Motion") };
        object hardware = RuntimeField(motions[0], "hardware"), phone = RuntimeField(motions[0], "hub");
        var latest = (System.Array)RuntimeField(hardware, "latest");
        var valid = (bool[])RuntimeField(hardware, "valid");
        var phoneSlots = (System.Array)RuntimeField(phone, "slots");
        for (int slot = 0; slot < 2; slot++) SetRuntimeField(phoneSlots.GetValue(slot), "ChannelOpen", false);
        System.Type sampleType = System.Type.GetType("MotionSample, Assembly-CSharp", true);
        uint[] sequences = { 0, 0 };
        int pushingSlot = single ? 1 : 0;
        Quaternion forward = Quaternion.Euler(90f, 0f, 0f);
        void Supply(bool calibrate, bool pushing)
        {
            for (int slot = 0; slot < 2; slot++)
            {
                uint sequence = ++sequences[slot];
                Quaternion rotation = !calibrate && slot == pushingSlot ? forward : Quaternion.identity;
                Vector3 acceleration = pushing && slot == pushingSlot ? Vector3.forward * 5f : Vector3.zero;
                // The competing single controller reports angular motion: a
                // straight P2 impulse must still select and retain its own pose.
                Vector3 speed = single && !calibrate && slot != pushingSlot ? Vector3.right * 140f : Vector3.zero;
                latest.SetValue(System.Activator.CreateInstance(sampleType, new object[] {
                    rotation, speed, acceleration, false, true, true, true, sequence, (ushort)0,
                    sequence * 20d, Time.realtimeSinceStartupAsDouble
                }), slot);
                valid[slot] = true;
                Call(motions[slot], "Update");
            }
        }
        void Frame(float dt)
        {
            if (single) Tick(dt);
            else
            {
                SetRuntimeField(master, "matchTime", Field<double>(master, "matchTime") + dt);
                Call(master, "LateUpdate");
            }
        }

        object enemy = null;
        Transform enemyRoot = null;
        if (single)
        {
            Call(master, "SpawnSingleNpc");
            enemy = Field<IList>(master, "singleNpcs")[0];
            enemyRoot = ((GameObject)RuntimeField(enemy, "Root")).transform;
            enemyRoot.position = Player.position + Player.forward * 2.5f;
            enemyRoot.rotation = Quaternion.LookRotation(-Player.forward);
            Call(RuntimeField(enemy, "Avatar"), "Reset");
            SetRuntimeField(enemy, "Hearts", 3);
            SetRuntimeField(enemy, "NextAttack", 0d);
            SetRuntimeField(enemy, "OpeningAttack", false);
            SetRuntimeField(enemy, "ApproachAttack", false);
            MoveSingleNpcsWithReadyView(0f);
            // Keep the body stationary and unguarded during controller samples.
            // Stagger is a valid guard state, so it cannot disable defence here.
            SetRuntimeField(enemy, "WindupSeconds", 100f);
            SetRuntimeField(enemy, "AttackTimer", 100f + (float)RuntimeField(enemy, "SwingSeconds"));
            SetRuntimeField(enemy, "AdvanceDuringWindup", false);
            SetRuntimeField(master, "singleSpawnTimer", 100f);
        }
        Supply(true, false); Frame(.01f);
        for (int i = 0; i < 10; i++) { Supply(false, false); Frame(.01f); }
        Supply(false, true); Frame(.01f);
        Supply(false, true); Frame(.01f);
        Transform sword = (Transform)RuntimeField(player, "Sword");
        Vector3 handAtStart = sword.localPosition;
        Assert.That(Quaternion.Angle((Quaternion)RuntimeField(player, "TargetRotation"),
            (Quaternion)Get(motions[pushingSlot], "CurrentLocalRotation")), Is.LessThan(.05f));
        Assert.That(Field<double>(master, "matchTime") - (double)RuntimeField(player, "ThrustAt"), Is.LessThan(.001d));
        if (single) Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(3), "The impulse begins at zero extension, before moving blade contact.");
        else Assert.That(Field<bool>(master, "reactionActive"), Is.False);
        Vector3 enemyAtStart = single ? enemyRoot.position : Vector3.zero;
        Vector3 playerAtStart = Player.position;

        Supply(false, false); Frame(.065f);
        if (single)
        {
            Assert.That(Field<object>(master, "singleMotion"), Is.SameAs(motions[1]));
            Assert.That(sword.localPosition.z - handAtStart.z, Is.GreaterThan(.1f));
            Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(2));
            Assert.That(Vector3.Distance(enemyRoot.position, enemyAtStart), Is.LessThan(.001f),
                "The shared hitstop precedes single-player displacement as in duel.");
        }
        else
        {
            Assert.That(Field<bool>(master, "reactionActive"), Is.True);
            Assert.That(Vector3.Distance(Field<Vector3>(master, "pushTarget"), Field<Vector3>(master, "pushStart")),
                Is.EqualTo(DuelMatch.ThrustPush).Within(.001f));
        }
        for (int i = 0; i < 7; i++) { Supply(false, false); Frame(.03f); }
        if (single)
        {
            Assert.That(RuntimeField(enemy, "Hearts"), Is.EqualTo(2), "One consumed impulse cannot damage again during return.");
            Assert.That(Vector3.Distance(enemyRoot.position, enemyAtStart), Is.EqualTo(DuelMatch.ThrustPush).Within(.005f));
            Assert.That(Vector3.Distance(Player.position, playerAtStart), Is.EqualTo(DuelMatch.ThrustPush).Within(.005f),
                "The attacker follows the same exchange displacement, preserving a reachable opponent.");
        }
        Assert.That((bool)motions[pushingSlot].GetType().GetMethod("TryConsumeThrust").Invoke(motions[pushingSlot], null), Is.False);
        yield return null;
    }

    [UnityTest]
    public IEnumerator P2MotionSteersSingleCharacterAndEitherGuardWorksWithoutStalePoseLeakage()
    {
        object playerSword = Field<object>(master, "first");
        object otherSword = Field<object>(master, "second");
        var p1 = (MonoBehaviour)RuntimeField(playerSword, "Motion");
        var p2 = (MonoBehaviour)RuntimeField(otherSword, "Motion");
        object hardware = RuntimeField(p1, "hardware"), phone = RuntimeField(p1, "hub");
        Assert.That(RuntimeField(p2, "hardware"), Is.SameAs(hardware));
        Assert.That(RuntimeField(p2, "hub"), Is.SameAs(phone));
        var latest = (System.Array)RuntimeField(hardware, "latest");
        var hardwareValid = (bool[])RuntimeField(hardware, "valid");
        var phoneSlots = (System.Array)RuntimeField(phone, "slots");
        object p2Phone = phoneSlots.GetValue(1);
        for (int slot = 0; slot < 2; slot++) SetRuntimeField(phoneSlots.GetValue(slot), "ChannelOpen", false);
        System.Type sampleType = System.Type.GetType("MotionSample, Assembly-CSharp", true);
        uint sequence = 0;
        object Sample(Quaternion rotation, float speed, bool guard, bool orientationValid, bool stale = false)
            => System.Activator.CreateInstance(sampleType, new object[] {
                rotation, new Vector3(speed, 0f, 0f), Vector3.zero, guard, orientationValid, true, false,
                ++sequence, (ushort)0, sequence * 10d, Time.realtimeSinceStartupAsDouble - (stale ? 1d : 0d)
            });
        void Hardware(int slot, Quaternion rotation, float speed, bool guard, bool stale = false)
        {
            latest.SetValue(Sample(rotation, speed, guard, true, stale), slot);
            hardwareValid[slot] = true;
        }
        void SampleControllers() { Call(p1, "Update"); Call(p2, "Update"); }
        CombatInput Input() => (CombatInput)master.GetType().GetMethod("ReadInput", Private)
            .Invoke(master, new object[] { playerSword, true, false });
        Quaternion Target() => (Quaternion)RuntimeField(playerSword, "TargetRotation");
        Transform visibleSword = (Transform)RuntimeField(playerSword, "Sword");

        // Establish each real MotionController's own first-sample reference.
        Hardware(0, Quaternion.identity, 0f, false);
        Hardware(1, Quaternion.identity, 0f, false);
        SampleControllers(); Tick(.01f);
        Quaternion upright = visibleSword.localRotation;
        Quaternion p2Rotation = Quaternion.Euler(35f, 20f, -15f);
        Hardware(0, Quaternion.identity, 0f, false);
        Hardware(1, p2Rotation, 140f, false);
        SampleControllers(); Tick(.01f);
        Quaternion expected = (Quaternion)Get(p2, "CurrentLocalRotation");
        Assert.That(Quaternion.Angle(Target(), expected), Is.LessThan(.05f));
        Assert.That(Quaternion.Angle(visibleSword.localRotation, expected), Is.LessThan(.05f),
            "The hidden P2 controller must move the visible P1 sword, not merely remain active.");
        Assert.That(Quaternion.Angle(visibleSword.localRotation, upright), Is.GreaterThan(20f));
        Assert.That(Input().AngularSpeed, Is.EqualTo(140f).Within(.01f));

        // P2 keeps steering while either controller independently supplies guard.
        for (int guardingSlot = 0; guardingSlot < 2; guardingSlot++)
        {
            Hardware(0, Quaternion.identity, 0f, guardingSlot == 0);
            Hardware(1, p2Rotation, 140f, guardingSlot == 1);
            SampleControllers(); Tick(.01f);
            Assert.That(Input().Valid && Input().GuardHeld, Is.True, "Guard slot " + guardingSlot);
            Assert.That(Quaternion.Angle(Target(), expected), Is.LessThan(.05f));
            Tick(GuardPoseTransition.DurationSeconds + .01f);
            Vector3 direction = visibleSword.localRotation * Vector3.forward;
            Assert.That(Vector3.Angle(direction, expected * Vector3.forward), Is.LessThan(.01f),
                "Guard from either slot follows the full P2 blade orientation.");
            Vector3 inputDirection = expected * Vector3.forward;
            Assert.That(Vector3.Angle(new Vector3(direction.x, direction.y, 0f),
                new Vector3(inputDirection.x, inputDirection.y, 0f)), Is.LessThan(.01f),
                "The P2 blade retains its chosen guard direction.");
        }

        int generation = Input().PoseGeneration;
        Hardware(0, Quaternion.Euler(-20f, 0f, 10f), 60f, false);
        Hardware(1, Quaternion.Euler(80f, 80f, 80f), 900f, true);
        SetRuntimeField(p2Phone, "LatestSample", Sample(Quaternion.Euler(70f, -70f, 70f), 900f, true, true, true));
        SetRuntimeField(p2Phone, "HasSample", true);
        SetRuntimeField(p2Phone, "ChannelOpen", true);
        SampleControllers(); Tick(.01f);
        CombatInput fallback = Input();
        Assert.That(Get(p2, "ActiveSource").ToString(), Is.EqualTo("Phone"));
        Assert.That(Get(p2, "IsPoseValid"), Is.False, "An open stale phone must not fall through to fresh USB.");
        Assert.That(fallback.Valid, Is.True, "The other fresh controller remains usable in single mode.");
        Assert.That(fallback.GuardHeld, Is.False, "Stale phone and suppressed USB guard cannot leak through.");
        Assert.That(fallback.AngularSpeed, Is.EqualTo(60f).Within(.01f));
        Assert.That(fallback.PoseGeneration, Is.GreaterThan(generation), "A controller handoff discards old swing deltas.");
        Assert.That(Quaternion.Angle(Target(), (Quaternion)Get(p1, "CurrentLocalRotation")), Is.LessThan(.05f));

        // Combat guard requires the same valid pose as duel. A fresh button
        // from a source with invalid orientation cannot guard or steer.
        SetRuntimeField(p2Phone, "LatestSample", Sample(Quaternion.Euler(80f, 80f, 80f), 900f, true, false));
        SampleControllers(); Tick(.01f);
        Assert.That(Get(p2, "IsPoseValid"), Is.False);
        Assert.That(Get(p2, "GuardHeld"), Is.True, "The fresh button reaches the controller.");
        Assert.That(Input().GuardHeld, Is.False, "Combat ignores guard from an invalid pose, as duel does.");
        Assert.That(Input().AngularSpeed, Is.EqualTo(60f).Within(.01f));
        Assert.That(Quaternion.Angle(Target(), (Quaternion)Get(p1, "CurrentLocalRotation")), Is.LessThan(.05f));

        Hardware(0, Quaternion.identity, 900f, true, true);
        SetRuntimeField(p2Phone, "LatestSample", Sample(Quaternion.identity, 900f, true, true, true));
        SampleControllers(); Tick(.01f);
        CombatInput unavailable = Input();
        Assert.That(unavailable.Valid, Is.False);
        Assert.That(unavailable.GuardHeld, Is.False);
        Assert.That(unavailable.AngularSpeed, Is.Zero);
        Assert.That(Run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts));
        yield return null;
    }

}
