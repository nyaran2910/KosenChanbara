using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class BattleGroundPlayTests
{
    private Keyboard keyboard;
    private Keyboard previousKeyboard;
    private Keyboard[] physicalKeyboards;
    private Mouse mouse;
    private MonoBehaviour master;
    private MonoBehaviour hub;
    private MonoBehaviour flow;
    private GameObject firstPlayer;
    private GameObject secondPlayer;
    private GameObject firstSword;
    private GameObject secondSword;
    private InputSettings.BackgroundBehavior previousBackgroundBehavior;
    private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
    private int previousTargetFrameRate;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        previousTargetFrameRate = Application.targetFrameRate;
        // Batch mode can run thousands of frames per second. Exercise keyboard
        // sweeps at the same frame rate as gameplay instead of subpixel steps.
        Application.targetFrameRate = 60;
        previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        // Native editor input must not replace the keyboard driven by this
        // fixture. Restore the user's devices after every test.
        previousKeyboard = Keyboard.current;
        physicalKeyboards = InputSystem.devices.OfType<Keyboard>().Where(device => device.enabled).ToArray();
        foreach (Keyboard device in physicalKeyboards) InputSystem.DisableDevice(device);
        keyboard = InputSystem.AddDevice<Keyboard>();
        SceneManager.sceneLoaded += DisableLiveTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableLiveTransport;
        yield return null;
        master = FindComponent("MasterController");
        firstPlayer = Field<GameObject>(master, "PlayerObject1");
        secondPlayer = Field<GameObject>(master, "PlayerObject2");
        firstSword = Field<GameObject>(master, "Sword1");
        secondSword = Field<GameObject>(master, "Sword2");
        Assert.That(master.enabled, Is.True);
        Assert.That(Paused(), Is.True, "The title must suspend the match clock.");
        Assert.That(HubOverlay(), Is.False);
        flow = FindComponent("GameFlowController");
        Assert.That(flow.GetType().GetProperty("CurrentScreen").GetValue(flow).ToString(), Is.EqualTo("Title"));
        Flow("ChooseMode", false); Flow("Ready", 0); Flow("Ready", 1); Flow("BeginMatch");
        Assert.That(Series().Phase, Is.EqualTo(RoundPhase.Versus));
        yield return AdvanceToFight();
    }

    private void DisableLiveTransport(Scene scene, LoadSceneMode mode)
    {
        // Exercise the real scene and keyboard fallback without opening signaling
        // sessions in an automated test. Awake has already created the overlay.
        hub = FindComponent("PhoneControllerHub");
        hub.enabled = false;
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "MotionController" || component.GetType().Name == "HardwareControllerHub")
                component.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Application.targetFrameRate = previousTargetFrameRate;
        SceneManager.sceneLoaded -= DisableLiveTransport;
        InputSystem.RemoveDevice(keyboard);
        foreach (Keyboard device in physicalKeyboards)
            if (device.added) InputSystem.EnableDevice(device);
        if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        if (mouse != null)
        {
            InputSystem.RemoveDevice(mouse);
            mouse = null;
        }
        InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
        if (hub != null)
            Object.Destroy(hub.gameObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator HitsStopThenPushAndBothPlayersCanWinAndRematch()
    {
        Vector3 initialPosition = master.transform.position;
        int hubId = hub.GetInstanceID();
        for (int attacker = 0; attacker < 2; attacker++)
        {
            float sign = attacker == 0 ? 1f : -1f;
            GameObject sword = attacker == 0 ? firstSword : secondSword;
            GameObject player = attacker == 0 ? firstPlayer : secondPlayer;
            Key attack = attacker == 0 ? Key.W : Key.UpArrow;
            Key returnToStance = attacker == 0 ? Key.S : Key.DownArrow;
            Renderer[] defenders = BodyRenderers(attacker == 0 ? secondPlayer : firstPlayer,
                attacker == 0 ? secondSword : firstSword);
            var block = new MaterialPropertyBlock();
            var arena = Field<SpriteRenderer>(master, "arena");
            int hitsToWin = Mathf.CeilToInt((Mathf.Min(arena.bounds.extents.x, arena.bounds.extents.z) - 1.5f) / 3f);

            for (int hit = 0; hit < hitsToWin; hit++)
            {
                Vector3 beforeHit = master.transform.position;
                yield return HoldUntil(() => Field<bool>(master, "reactionActive"), attack);
                Assert.That(master.transform.position, Is.EqualTo(beforeHit), "Push must wait for hit stop.");
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Quaternion impactRotation = sword.transform.localRotation;
                Vector3 impactPosition = sword.transform.localPosition;
                double start = Field<double>(master, "reactionStart");
                Assert.That(defenders.Length, Is.GreaterThan(1));
                foreach (Renderer defender in defenders)
                {
                    defender.GetPropertyBlock(block);
                    Assert.That(block.GetColor(Shader.PropertyToID("_BaseColor")), Is.EqualTo(Color.white), defender.name);
                }
                // The 140 ms one-shot may finish while the editor renders a
                // slow frame. Validate its audible output configuration instead
                // of requiring an asynchronous audio flag to remain true.
                AudioSource impactAudio = master.GetComponent<AudioSource>();
                Assert.That(impactAudio.isActiveAndEnabled && !impactAudio.mute, Is.True);
                Assert.That(impactAudio.volume, Is.GreaterThan(0f));
                SetKeys(attack, attacker == 0 ? Key.LeftShift : Key.RightShift);
                yield return null;
                if (MatchTime() < start + 0.06)
                {
                    Assert.That(master.transform.position, Is.EqualTo(beforeHit));
                    Assert.That(Quaternion.Angle(sword.transform.localRotation, impactRotation), Is.LessThan(0.001f));
                    Assert.That(sword.transform.localPosition, Is.EqualTo(impactPosition));
                }
                SetKeys(attack);
                yield return new WaitForSecondsRealtime(0.3f);
                foreach (Renderer defender in defenders)
                {
                    defender.GetPropertyBlock(block);
                    Assert.That(block.isEmpty, Is.True, defender.name + " must restore its original properties.");
                }
                Assert.That(master.transform.position.z * sign, Is.GreaterThan(beforeHit.z * sign));
                if (hit < hitsToWin - 1)
                    Assert.That(master.transform.position.z, Is.EqualTo((hit + 1) * 3f * sign).Within(0.001f));

                // Continuing the same swing cannot cause another hit.
                Vector3 afterHit = master.transform.position;
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(master.transform.position, Is.EqualTo(afterHit));
                if (hit < hitsToWin - 1)
                {
                    yield return HoldUntil(() => Vector3.Angle(sword.transform.forward, player.transform.forward) >= 89.9f,
                        returnToStance);
                    // The ordinary keyboard takeback is harmless without
                    // requiring a guard button to suppress an extra hit.
                    SetKeys();
                    yield return null;
                }
            }

            SetKeys();
            MatchWinner expected = attacker == 0 ? MatchWinner.Player1 : MatchWinner.Player2;
            Assert.That(Series().RoundWinner, Is.EqualTo(expected));
            Assert.That(Winner(), Is.EqualTo(MatchWinner.None), "One ring-out awards one point, not the match.");
            Assert.That(DuelSeries.WinsRequired, Is.EqualTo(3), "The duel uses the requested three-point series.");
            for (int point = 1; point < DuelSeries.WinsRequired; point++)
            {
                yield return AdvanceToFight();
                // Ring-out geometry still runs through MasterController.CheckWinner.
                GameObject loser = attacker == 0 ? secondPlayer : firstPlayer;
                loser.transform.position = new Vector3(100, 0, 0);
                master.GetType().GetMethod("CheckWinner", PrivateInstance).Invoke(master, null);
                Assert.That(Series().RoundWinner, Is.EqualTo(expected));
                Assert.That(attacker == 0 ? Series().FirstScore : Series().SecondScore, Is.EqualTo(point + 1));
                Assert.That(Winner(), Is.EqualTo(point + 1 == 3 ? expected : MatchWinner.None));
            }
            Assert.That(Winner(), Is.EqualTo(expected));
            yield return AdvancePhase(3.41);
            GameObject resultCanvas = master.transform.Find("Match result").gameObject;
            // Result presentation uses the gameplay clock; editor rendering
            // stalls must not turn a wall-clock wait into a shorter delay.
            master.GetType().GetField("matchTime", PrivateInstance).SetValue(master, MatchTime() + 0.61);
            yield return null;
            yield return null;
            Assert.That(resultCanvas.activeSelf, Is.True);
            Assert.That(resultCanvas.GetComponentsInChildren<Text>().Any(text => text.text == (attacker == 0 ? "P1 の勝ち！" : "P2 の勝ち！")), Is.True);
            resultCanvas.GetComponentInChildren<Button>().onClick.Invoke();
            Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
            Assert.That(Series().FirstScore + Series().SecondScore, Is.Zero, "Replay must clear both scores.");
            Assert.That(master.transform.position, Is.EqualTo(initialPosition));
            Assert.That(resultCanvas.activeSelf, Is.False);
            Assert.That(hub.GetInstanceID(), Is.EqualTo(hubId), "Rematch must preserve the transport instance.");
            Assert.That(Field<DuelMatch>(master, "match").IsResolving(MatchTime()), Is.False);
            yield return AdvanceToFight();
        }
    }

    [UnityTest]
    public IEnumerator GuardsPreserveBladeColorsAndClashesUseTheirOwnPushAndFlash()
    {
        Vector3 initialPosition = master.transform.position;

        // A held guard whose blade misses the strike still breaks and flashes.
        SetKeys(Key.RightShift);
        StartPhysicalGuardContact(0, Vector3.down, false);
        yield return null;
        Assert.That(Field<float>(master, "hitStopSeconds"), Is.EqualTo(DuelMatch.HitStopSeconds));
        AssertBladeColor(secondSword, new Color(1f, 0.4f, 0.04f));
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(master.transform.position.z, Is.EqualTo(initialPosition.z + 1.5f).Within(0.001f));

        yield return ResetInPlace();
        // Turn P2's upright blade sideways, then hold guard while P1 swings.
        yield return HoldUntil(() => Mathf.Abs(Vector3.Dot(secondSword.transform.forward, Vector3.up)) < 0.01f, Key.RightArrow);
        Field<object>(master, "second").GetType().GetField("Pitch").SetValue(Field<object>(master, "second"), -45f);
        yield return null;
        Material[] guardedMaterials = firstSword.GetComponentsInChildren<Renderer>().Concat(secondSword.GetComponentsInChildren<Renderer>())
            .SelectMany(renderer => renderer.sharedMaterials).Distinct().ToArray();
        Color[] guardedColors = guardedMaterials.Select(material => material.color).ToArray();
        SetKeys(Key.RightShift);
        StartPhysicalGuardContact(0, Vector3.down);
        yield return null;
        Assert.That(Field<float>(master, "hitStopSeconds"), Is.EqualTo(DuelMatch.GuardStopSeconds));
        var guardBlock = new MaterialPropertyBlock();
        foreach (Renderer renderer in firstSword.GetComponentsInChildren<Renderer>().Concat(secondSword.GetComponentsInChildren<Renderer>()))
        {
            renderer.GetPropertyBlock(guardBlock);
            Assert.That(guardBlock.isEmpty, Is.True, renderer.name + " must retain its original blade color on a successful guard.");
        }
        for (int material = 0; material < guardedMaterials.Length; material++)
            Assert.That(guardedMaterials[material].color, Is.EqualTo(guardedColors[material]), "Guard effects must not modify sword materials.");
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(master.transform.position.z, Is.EqualTo(initialPosition.z - DuelMatch.GuardPush).Within(0.001f));

        yield return ResetInPlace();
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W, Key.UpArrow);
        Assert.That(Field<float>(master, "hitStopSeconds"), Is.EqualTo(DuelMatch.GuardStopSeconds));
        AssertBladeColor(firstSword, Color.white);
        AssertBladeColor(secondSword, Color.white);
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(master.transform.position, Is.EqualTo(initialPosition));
        Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in firstSword.GetComponentsInChildren<Renderer>())
        {
            renderer.GetPropertyBlock(block);
            Assert.That(block.isEmpty, Is.True, "The flash must restore the original renderer properties.");
        }
    }

    [UnityTest]
    public IEnumerator ABlockedFighterAcceptsGuardImmediatelyAndBlendsTheSwordDuringHitStop()
    {
        master.enabled = false; flow.enabled = false;
        for (int attacker = 0; attacker < 2; attacker++)
        {
            InvokeMaster("ResetMatch");
            Series().Reset(0);Series().Tick(3.3);Series().Tick(4.7);Series().Tick(6);
            master.GetType().GetField("matchTime", PrivateInstance).SetValue(master,6d);
            object player = Field<object>(master, attacker == 0 ? "first" : "second");
            player.GetType().GetField("Pitch").SetValue(player, 0f);
            InvokeMaster("ReadInput", player, attacker == 0, false);
            DuelMatch match = Field<DuelMatch>(master, "match");
            double now = MatchTime();
            var aGrip = Vector3.forward * .55f;
            var dBody = Vector3.forward * 2f;
            var dGrip = dBody - Vector3.forward * .55f - Vector3.right * .6f + Vector3.up * .55f;
            var attack = new CombatPose(aGrip, Vector3.up, aGrip, Vector3.forward,
                Vector3.zero, Vector3.forward, Vector3.forward, aGrip, Vector3.forward);
            var defence = new CombatPose(dGrip, Vector3.right, dGrip, Vector3.right,
                dBody, Vector3.back, Vector3.zero, dGrip, Vector3.right);
            var upright = new CombatInput(90f, 0f);
            match.Step(upright, upright, attack, defence, now);
            var strike = new CombatInput(40f, 200f);
            var guard = new CombatInput(90f, 0f, true);
            CombatResult result = attacker == 0
                ? match.Step(strike, guard, attack, defence, now)
                : match.Step(guard, strike, defence, attack, now);
            Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.GuardSuccess));
            InvokeMaster("BeginReaction", result, now);
            GameObject sword = attacker == 0 ? firstSword : secondSword;
            SwordPose captured = new SwordPose(sword.transform.localPosition, sword.transform.localRotation);
            SetKeys(attacker == 0 ? Key.LeftShift : Key.RightShift);
            yield return null;
            // Keep the test at the contact time even if the editor's next
            // rendered frame took longer than the short hit stop.
            master.GetType().GetField("matchTime", PrivateInstance).SetValue(master,now-Time.unscaledDeltaTime);
            // Guard is accepted during hit stop while the visible sword starts
            // at its impact pose and moves through the shared transition.
            InvokeMaster("LateUpdate");
            Assert.That(MatchTime(),Is.LessThan(now+result.HitStopSeconds));
            SwordPose target = (SwordPose)player.GetType().GetField("TargetPose").GetValue(player);
            Assert.That((bool)player.GetType().GetField("GuardHeld").GetValue(player), Is.True);
            Assert.That(Vector3.Distance(sword.transform.localPosition, captured.Position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(sword.transform.localRotation, captured.Rotation), Is.LessThan(.05f));
            Assert.That(match.IsStunned(attacker, MatchTime()), Is.True,
                "Defence cannot cancel the one-second offensive restriction.");
            SetKeys(); yield return null;
        }
    }

    [UnityTest]
    public IEnumerator GuardChangesTheWholePoseAndResetUsesHeldGuard()
    {
        object first = Field<object>(master, "first");
        object second = Field<object>(master, "second");
        foreach (object player in new[] { first, second })
        {
            player.GetType().GetField("Pitch").SetValue(player, -60f);
            player.GetType().GetField("Roll").SetValue(player, -45f);
        }
        yield return new WaitForSecondsRealtime(GuardPoseTransition.DurationSeconds + .03f);
        foreach (GameObject sword in new[] { firstSword, secondSword })
            AssertCurrentPose(sword, false);

        Quaternion normalRotation = firstSword.transform.localRotation;
        SetKeys(Key.LeftShift, Key.RightShift);
        yield return null;
        yield return null;
        Assert.That(Quaternion.Angle(firstSword.transform.localRotation, normalRotation), Is.LessThan(10f),
            "Guard starts from the visible sword rather than snapping to its final pose.");
        master.GetType().GetField("matchTime", PrivateInstance).SetValue(master,
            MatchTime() + GuardPoseTransition.DurationSeconds + .01d);
        yield return null;
        Vector3 normalGuardDirection = firstSword.transform.localRotation * Vector3.forward;
        foreach (GameObject sword in new[] { firstSword, secondSword })
        {
            Vector3 direction = sword.transform.localRotation * Vector3.forward;
            object guardedInput = Field<object>(master, sword == firstSword ? "first" : "second");
            Assert.That(Vector3.Angle(direction, ((Quaternion)guardedInput.GetType().GetField("TargetRotation").GetValue(guardedInput)) * Vector3.forward), Is.LessThan(.01f));
            AssertCurrentPose(sword, true);
        }
        Assert.That(Field<bool>(master, "reactionActive"), Is.False, "A guard toggle cannot cause an attack.");
        // Point directly forward: guard follows the new input freely.
        first.GetType().GetField("Pitch").SetValue(first, 0f);
        yield return new WaitForSecondsRealtime(.09f);
        Assert.That(Vector3.Angle(firstSword.transform.forward, firstPlayer.transform.forward), Is.LessThan(.01f));
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);

        Quaternion beforeReset = firstSword.transform.localRotation;
        master.GetType().GetMethod("ResetMatch").Invoke(master, null);
        Assert.That(Quaternion.Angle(beforeReset, firstSword.transform.localRotation), Is.LessThan(.05f),
            "Reset returns the sword smoothly rather than replacing the current pose.");
        yield return new WaitForSecondsRealtime(GuardPoseTransition.DurationSeconds + .03f);
        foreach (GameObject sword in new[] { firstSword, secondSword })
        {
            Assert.That(Vector3.Distance(sword.transform.localRotation * Vector3.forward, Vector3.up), Is.LessThan(0.00001f));
            AssertCurrentPose(sword, true);
        }
        SetKeys();
        yield return null;
        yield return null;
        master.GetType().GetField("matchTime", PrivateInstance).SetValue(master,
            MatchTime() + GuardPoseTransition.DurationSeconds + .01d);
        yield return null;
        AssertCurrentPose(firstSword, false);
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);
    }

    [UnityTest]
    public IEnumerator RCanResetAnOngoingReaction()
    {
        Vector3 initialPosition = master.transform.position;
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W);
        SetKeys(Key.R);
        yield return null;
        yield return null;
        SetKeys();
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);
        Assert.That(master.transform.position, Is.EqualTo(initialPosition));
        Assert.That(Vector3.Angle(firstSword.transform.forward, firstPlayer.transform.forward), Is.EqualTo(90f).Within(0.01f));
        Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
    }


    [UnityTest]
    public IEnumerator PauseStopsTheRoundAndPresentationButKeepsTheControllerObjects()
    {
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W);
        Pause(true);
        double pausedAt = MatchTime();
        Vector3 root = master.transform.position;
        Vector3 sword = firstSword.transform.position;
        Quaternion rotation = firstSword.transform.rotation;
        Transform chest = firstPlayer.GetComponentsInChildren<Transform>().Single(t => t.name == "chest");
        Quaternion body = chest.rotation;
        Camera camera = firstPlayer.GetComponentInChildren<Camera>();
        Vector3 view = camera.transform.localPosition;
        MonoBehaviour fx = FindComponent("DuelEffects");
        int bursts = (int)fx.GetType().GetProperty("ActiveBurstCount").GetValue(fx);
        object[] pausedBursts = Field<Array>(fx, "bursts").Cast<object>()
            .Where(b => (bool)b.GetType().GetField("Active").GetValue(b)).ToArray();
        double[] burstStarts = pausedBursts.Select(b => (double)b.GetType().GetField("Start").GetValue(b)).ToArray();
        Assert.That(bursts, Is.GreaterThan(0));
        Assert.That(HubOverlay(), Is.True);
        Assert.That(hub.gameObject.activeInHierarchy, Is.True);
        Assert.That(firstSword.GetComponents<MonoBehaviour>().Any(c => c.GetType().Name == "MotionController"), Is.True);
        Assert.That(master.transform.Find("Pause UI/Pause hint"), Is.Null);
        SetKeys(Key.W, Key.LeftShift);
        yield return new WaitForSecondsRealtime(0.35f);
        Assert.That(MatchTime(), Is.EqualTo(pausedAt));
        Assert.That(master.transform.position, Is.EqualTo(root));
        Assert.That(firstSword.transform.position, Is.EqualTo(sword));
        Assert.That(firstSword.transform.rotation, Is.EqualTo(rotation));
        Assert.That(chest.rotation, Is.EqualTo(body));
        Assert.That(camera.transform.localPosition, Is.EqualTo(view));
        Assert.That((int)fx.GetType().GetProperty("ActiveBurstCount").GetValue(fx), Is.EqualTo(bursts));
        Assert.That(Field<DuelMatch>(master, "match").IsStunned(1, MatchTime()), Is.True);
        Pause(false);
        Assert.That(HubOverlay(), Is.False);
        Assert.That(master.transform.Find("Pause UI/Pause hint"), Is.Null);
        // The unfinished stop and stun must survive the wall-clock pause.
        Assert.That(firstSword.transform.rotation, Is.EqualTo(rotation));
        yield return new WaitForSecondsRealtime(0.35f);
        Assert.That(master.transform.position.z, Is.GreaterThan(root.z));
        Assert.That(MatchTime() - pausedAt, Is.GreaterThan(0.3), "The gameplay clock must resume.");
        for (int i = 0; i < pausedBursts.Length; i++)
        {
            object burst = pausedBursts[i];
            bool active = (bool)burst.GetType().GetField("Active").GetValue(burst);
            double started = (double)burst.GetType().GetField("Start").GetValue(burst);
            Assert.That(!active || started > burstStarts[i], Is.True,
                "An effect frozen by pause must expire after resume; its pool slot may contain new landing dust.");
        }
        SetKeys();
        // A three-metre recovery takes several steps; their newly emitted dust
        // has its own lifetime. It must finish once the feet have settled.
        double effectsExpireAt = MatchTime() + 2.0;
        while (MatchTime() < effectsExpireAt) yield return null;
        yield return null;
        Assert.That((int)fx.GetType().GetProperty("ActiveBurstCount").GetValue(fx), Is.EqualTo(0));
    }

    [UnityTest]
    public IEnumerator ResumeStartsANewAttackBaselineAndPausedResetStaysPaused()
    {
        yield return null;
        Pause(true);
        object first = Field<object>(master, "first");
        first.GetType().GetField("Pitch").SetValue(first, -30f);
        SetKeys(Key.W, Key.LeftShift);
        yield return null;
        Pause(false);
        yield return null;
        yield return null;
        Assert.That(Field<bool>(master, "reactionActive"), Is.False, "Motion made during pause must not trigger an attack on resume.");
        AssertCurrentPose(firstSword, true);
        Pause(true);
        SetKeys(Key.R);
        yield return null;
        yield return null;
        Assert.That(Paused(), Is.True);
        Assert.That(HubOverlay(), Is.True);
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);
        Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
        SetKeys();
        yield return null;
        SetKeys(Key.Escape);
        yield return null;
        yield return null;
        Assert.That(Paused(), Is.False);
        yield return HoldUntil(() => Vector3.Angle(firstSword.transform.forward, firstPlayer.transform.forward) >= 89.9f, Key.S);
        SetKeys();
        yield return null;
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W);
    }

    [UnityTest]
    public IEnumerator CharacterBonesMoveWithoutMovingCombatCentersAndEffectsReset()
    {
        Vector3 firstCenter = firstPlayer.transform.position, secondCenter = secondPlayer.transform.position;
        Transform chest = firstPlayer.GetComponentsInChildren<Transform>().Single(t => t.name == "chest");
        Transform knee = firstPlayer.GetComponentsInChildren<Transform>().Single(t => t.name == "foot_l2");
        Quaternion body = chest.rotation;
        Vector3 foot = knee.position;
        SetKeys(Key.W);
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(Quaternion.Angle(chest.rotation, body), Is.GreaterThan(0.1f));
        Assert.That(Vector3.Distance(knee.position, foot), Is.GreaterThan(0.001f));
        Assert.That(firstPlayer.transform.position, Is.EqualTo(firstCenter));
        Assert.That(secondPlayer.transform.position, Is.EqualTo(secondCenter));
        MonoBehaviour fx = FindComponent("DuelEffects");
        Assert.That((int)fx.GetType().GetProperty("TrailSampleCount").GetValue(fx), Is.InRange(2, 24));
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W);
        Assert.That((int)fx.GetType().GetProperty("ActiveBurstCount").GetValue(fx), Is.InRange(1, 12));
        Camera camera = firstPlayer.GetComponentInChildren<Camera>();
        Assert.That(camera.fieldOfView, Is.InRange(60, 64.01f));
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.That(camera.fieldOfView, Is.EqualTo(60f).Within(0.001f));
        master.GetType().GetMethod("ResetMatch").Invoke(master, null);
        Assert.That((int)fx.GetType().GetProperty("TrailSampleCount").GetValue(fx), Is.EqualTo(0));
        Assert.That((int)fx.GetType().GetProperty("ActiveBurstCount").GetValue(fx), Is.EqualTo(0));
        foreach (SkinnedMeshRenderer skin in firstPlayer.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh(); skin.BakeMesh(mesh, true);
            float floor = mesh.vertices.Min(v => skin.transform.TransformPoint(v).y);
            Assert.That(floor, Is.GreaterThanOrEqualTo(-0.001f), skin.name);
            Object.Destroy(mesh);
        }
    }

    [UnityTest]
    public IEnumerator FootworkLandsAndKeepsTheSupportingFootPlantedAtDifferentFrameRates()
    {
        master.enabled = false;
        object presentation = Field<object>(master, "presentation");
        Array avatars = Field<Array>(presentation, "avatars");
        for (int playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            GameObject player = playerIndex == 0 ? firstPlayer : secondPlayer;
            object avatar = avatars.GetValue(playerIndex);
            Transform lead = player.GetComponentsInChildren<Transform>().Single(t => t.name == "ankle_l");
            Transform support = player.GetComponentsInChildren<Transform>().Single(t => t.name == "ankle_r");
            Transform knee = player.GetComponentsInChildren<Transform>().Single(t => t.name == "foot_r2");
            foreach (int fps in new[] { 30, 60, 144 })
            {
                avatar.GetType().GetMethod("Reset").Invoke(avatar, null);
                Vector3 leadPlant = lead.position, supportPlant = support.position;
                float highestLift = 0f;
                int landings = 0;
                for (int frame = 0; frame < fps * 2; frame++)
                {
                    float t = frame / (float)fps;
                    float pitch = t < 0.8f ? Mathf.Lerp(-90f, -20f, t / 0.8f) : -20f;
                    TickAvatar(avatar, t, 1f / fps, Quaternion.Euler(pitch, 0f, 0f), false, frame % 2 == 0 ? 120f : 90f);
                    Assert.That(Vector3.Distance(support.position, supportPlant), Is.LessThan(0.001f), "Supporting foot slid at " + fps + " FPS");
                    Assert.That(lead.position.y, Is.GreaterThanOrEqualTo(leadPlant.y - 0.001f));
                    Assert.That(Vector3.Dot(knee.position - support.position, player.transform.forward), Is.GreaterThan(0f), "Knee flipped backwards");
                    highestLift = Mathf.Max(highestLift, lead.position.y - leadPlant.y);
                    if (AvatarFlag(avatar, "Stepped")) landings++;
                    if (fps == 60 && (frame == 0 || frame == 27 || frame == 90)
                        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR")))
                    {
                        yield return null; // Let skinning refresh before the optional rendered preview.
                        CaptureFootwork(player, "P" + (playerIndex + 1) + "-step-" + frame);
                    }
                }
                Assert.That(highestLift, Is.InRange(0.025f, 0.04f));
                Assert.That(landings, Is.EqualTo(1), "A held sword must not keep shuffling");
                Assert.That(lead.position.y, Is.EqualTo(leadPlant.y).Within(0.001f), "Foot stayed suspended");
                Assert.That(Vector3.Dot(lead.position - leadPlant, player.transform.forward), Is.EqualTo(0.12f).Within(0.001f));
                for (int frame = 0; frame < fps; frame++)
                {
                    TickAvatar(avatar, 2f + frame / (float)fps, 1f / fps, Quaternion.Euler(-90f, 0f, 0f), false, 0f);
                    Assert.That(Vector3.Distance(support.position, supportPlant), Is.LessThan(0.001f));
                    if (AvatarFlag(avatar, "Stepped")) landings++;
                    if (fps == 60 && (frame == 7 || frame == 45)
                        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR")))
                    {
                        yield return null;
                        CaptureFootwork(player, "P" + (playerIndex + 1) + "-recover-" + frame);
                    }
                }
                Assert.That(landings, Is.EqualTo(2));
                Assert.That(Vector3.Distance(lead.position, leadPlant), Is.LessThan(0.001f), "Recovery must return to a grounded stance");
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator SensorJitterIsSilentAndRealSwingsOnlyPlayOnce()
    {
        master.enabled = false;
        object presentation = Field<object>(master, "presentation");
        Array avatars = Field<Array>(presentation, "avatars");
        AudioSource movement = Field<AudioSource>(presentation, "movementAudio");
        var pose = presentation.GetType().GetMethod("PoseCharacters");
        Quaternion upright = Quaternion.Euler(-90f, 0f, 0f);
        presentation.GetType().GetMethod("ResetEffects").Invoke(presentation, null);
        // Gyro magnitudes can cross the old threshold while the blade barely moves.
        for (int frame = 0; frame < 240; frame++)
        {
            Quaternion jitter = Quaternion.Euler(-90f + Mathf.Sin(frame) * 1.5f, 0f, 0f);
            pose.Invoke(presentation, new object[] { frame / 60.0, 1f / 60f, jitter, upright, false, false, frame % 2 == 0 ? 130f : 90f, 0f, false });
            Assert.That(AvatarFlag(avatars.GetValue(0), "Swung"), Is.False);
            Assert.That(AvatarFlag(avatars.GetValue(0), "Stepped"), Is.False);
            Assert.That(movement.isPlaying, Is.False, "Idle sensor noise played a movement sound");
        }
        int swings = 0;
        for (int frame = 0; frame < 60; frame++)
        {
            Quaternion rotation = Quaternion.Euler(-90f + Mathf.Min(frame * 3f, 75f), 0f, 0f);
            pose.Invoke(presentation, new object[] { 4.0 + frame / 60.0, 1f / 60f, rotation, upright, false, false, 180f, 0f, false });
            if (AvatarFlag(avatars.GetValue(0), "Swung")) swings++;
        }
        Assert.That(swings, Is.EqualTo(1));
        Assert.That(movement.isPlaying, Is.True, "A real fast swing should still have feedback");
        presentation.GetType().GetMethod("ResetEffects").Invoke(presentation, null);
        presentation.GetType().GetMethod("ClearMotionHistory").Invoke(presentation, null);
        pose.Invoke(presentation, new object[] { 6.0, 1f / 60f, Quaternion.identity, upright, false, false, 500f, 0f, false });
        Assert.That(movement.isPlaying, Is.False, "A recenter or resume pose jump played a sound");
        for (int frame = 0; frame < 60; frame++)
            pose.Invoke(presentation, new object[] { 7.0 + frame / 60.0, 1f / 60f, Quaternion.Euler(0f, 0f, frame * 3f), upright, true, false, 180f, 0f, false });
        Assert.That(movement.isPlaying, Is.False, "Guard motion played a swing sound");
        yield return null;
    }

    [UnityTest]
    public IEnumerator SwingSoundsRequireACutTowardTheOpponent()
    {
        master.enabled = false;
        Array avatars = Field<Array>(Field<object>(master, "presentation"), "avatars");
        Quaternion upright = Quaternion.Euler(-90f, 0f, 0f);
        foreach (int fps in new[] { 30, 60, 144 })
            foreach (object avatar in avatars)
            {
                AssertSwingTrajectory(avatar, fps, t => Quaternion.AngleAxis(90f * t, Vector3.forward) * upright,
                    180f, 0, "Upright to sideways stance");
                AssertSwingTrajectory(avatar, fps, t => Quaternion.Euler(-90f * t, 0f, 0f),
                    180f, 0, "Returning from the opponent to an upright stance");
                AssertSwingTrajectory(avatar, fps, t => Quaternion.AngleAxis(180f * t, Vector3.forward) * Quaternion.Euler(-30f, 0f, 0f),
                    360f, 1, "Short side arc inside the forward cone");
                AssertSwingTrajectory(avatar, fps, t => Quaternion.AngleAxis(180f * t, Vector3.forward),
                    360f, 0, "Twisting around the blade");
                AssertSwingTrajectory(avatar, fps, t => Quaternion.Euler(-90f + 90f * t, 0f, 0f),
                    60f, 0, "Slow movement below the shared attack speed threshold");
                AssertSwingTrajectory(avatar, fps, t => Quaternion.Euler(-90f + 90f * t, 0f, 0f),
                    180f, 1, "Vertical cut");
                AssertSwingTrajectory(avatar, fps, t => Quaternion.Euler(0f, -90f + 180f * t, 0f),
                    360f, 1, "Horizontal cut through the opponent");
                AssertSwingTrajectory(avatar, fps, t => Quaternion.AngleAxis(35f, Vector3.forward) * Quaternion.Euler(-90f + 90f * t, 0f, 0f),
                    180f, 1, "Diagonal cut");
            }
        yield return null;
    }

    private static void AssertSwingTrajectory(object avatar, int fps, Func<float, Quaternion> path,
        float speed, int expectedSounds, string name)
    {
        avatar.GetType().GetMethod("Reset").Invoke(avatar, null);
        int sounds = 0;
        int frames = Mathf.CeilToInt(fps * 0.5f);
        for (int frame = 0; frame <= frames; frame++)
        {
            TickAvatar(avatar, frame / (double)fps, 1f / fps, path(frame / (float)frames), false, speed);
            if (AvatarFlag(avatar, "Swung")) sounds++;
        }
        Assert.That(sounds, Is.EqualTo(expectedSounds), name + " at " + fps + "fps");
    }

    private static void TickAvatar(object avatar, double now, float dt, Quaternion input, bool guard, float speed)
        => avatar.GetType().GetMethod("Tick").Invoke(avatar, new object[] { now, dt, input, guard, speed });
    private static bool AvatarFlag(object avatar, string name) => (bool)avatar.GetType().GetProperty(name).GetValue(avatar);

    [UnityTest]
    public IEnumerator SingleViewKeepsKeyboardAxesAndSwitchesWithoutResettingTheRound()
    {
        Camera[] cameras = { firstPlayer.GetComponentInChildren<Camera>(), secondPlayer.GetComponentInChildren<Camera>() };
        object[] inputs = { Field<object>(master, "first"), Field<object>(master, "second") };
        foreach (object input in inputs)
        {
            input.GetType().GetField("Pitch").SetValue(input, -60f);
            input.GetType().GetField("Roll").SetValue(input, 35f);
        }
        yield return null;
        Quaternion raw = Quaternion.AngleAxis(35f, Vector3.forward) * Quaternion.Euler(-60f, 0f, 0f);
        InvokeMaster("SetDisplayMode", 2);
        Assert.That(cameras.All(camera => camera.enabled), Is.True, "Settings only change while paused");
        Pause(true);
        double clock = MatchTime();
        int hubId = hub.GetInstanceID();
        Vector3 position = master.transform.position;
        InvokeMaster("SetDisplayMode", 2);
        AssertLayout(cameras, 0);
        Assert.That(Quaternion.Angle(firstSword.transform.localRotation, raw), Is.LessThan(0.01f));
        Assert.That(Quaternion.Angle(secondSword.transform.localRotation, raw), Is.LessThan(0.01f));
        Capture(cameras[0], "single-P1");
        InvokeMaster("SetViewedPlayer", 1);
        AssertLayout(cameras, 1);
        Assert.That(Quaternion.Angle(firstSword.transform.localRotation, raw), Is.LessThan(0.01f));
        Assert.That(Quaternion.Angle(secondSword.transform.localRotation, raw), Is.LessThan(0.01f));
        Capture(cameras[1], "single-P2");
        foreach (object input in inputs)
            Assert.That(input.GetType().GetField("Roll").GetValue(input), Is.EqualTo(35f), "Do not overwrite raw controller axes");
        InvokeMaster("SetDisplayMode", 1);
        for (int i = 0; i < 2; i++)
        {
            Assert.That(cameras[i].enabled, Is.True);
            Assert.That(cameras[i].rect, Is.EqualTo(new Rect(i * 0.5f, 0, 0.5f, 1)));
        }
        Assert.That(Quaternion.Angle(secondSword.transform.localRotation, raw), Is.LessThan(0.01f));
        Assert.That(MatchTime(), Is.EqualTo(clock));
        Assert.That(master.transform.position, Is.EqualTo(position));
        Assert.That(hub.GetInstanceID(), Is.EqualTo(hubId));
        // Switching while a blade is already forward must not manufacture a hit.
        foreach (object input in inputs) input.GetType().GetField("Pitch").SetValue(input, -20f);
        InvokeMaster("SetDisplayMode", 2);
        Pause(false);
        SetKeys(Key.W, Key.UpArrow);
        yield return new WaitForSecondsRealtime(0.15f);
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);
        Assert.That(master.transform.position, Is.EqualTo(position));
    }

    [UnityTest]
    public IEnumerator CharacterPreviewPreservesKeyboardAxesInEitherSingleView()
    {
        Pause(true);
        InvokeMaster("SetDisplayMode", 2);
        double clock = MatchTime();
        Quaternion raw = Quaternion.AngleAxis(35f, Vector3.forward) * Quaternion.Euler(-60f, 0f, 0f);
        GameObject[] swords = { firstSword, secondSword };
        for (int viewed = 0; viewed < 2; viewed++)
        {
            InvokeMaster("SetViewedPlayer", viewed);
            for (int preview = 0; preview < 2; preview++)
            {
                object input = Field<object>(master, preview == 0 ? "first" : "second");
                input.GetType().GetField("Pitch").SetValue(input, -60f);
                input.GetType().GetField("Roll").SetValue(input, 35f);
                InvokeMaster("BeginCharacterPreview", preview);
                yield return null;
                Quaternion expected = raw;
                Assert.That(Quaternion.Angle(swords[preview].transform.localRotation, expected), Is.LessThan(.01f));
                Assert.That(input.GetType().GetField("Roll").GetValue(input), Is.EqualTo(35f));
                Assert.That(MatchTime(), Is.EqualTo(clock));
                InvokeMaster("EndCharacterPreview");
            }
        }
    }

    [UnityTest]
    public IEnumerator DuelClosesAnAccumulatedGapAndBothFightersCanHitAgain()
    {
        firstPlayer.transform.position += Vector3.left;
        secondPlayer.transform.position += Vector3.right;
        Vector3 stationary = secondPlayer.transform.position;
        yield return null;
        Assert.That(Vector3.Distance(firstPlayer.transform.position, secondPlayer.transform.position), Is.GreaterThan(3.1f),
            "Closing the gap must be gradual rather than teleporting either fighter.");
        yield return new WaitForSecondsRealtime(.7f);
        Assert.That(Vector3.Distance(firstPlayer.transform.position, secondPlayer.transform.position), Is.EqualTo(3f).Within(.005f));
        Assert.That(secondPlayer.transform.position, Is.EqualTo(stationary), "Keep the recoiling fighter's full displacement.");
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W);
        Assert.That(Field<object>(master, "pushedSword"), Is.SameAs(Field<object>(master, "second")));
        SetKeys();
        yield return new WaitForSecondsRealtime(1.1f);
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.UpArrow);
        Assert.That(Field<object>(master, "pushedSword"), Is.SameAs(Field<object>(master, "first")));
    }

    [UnityTest]
    public IEnumerator PausedAppearanceChangesReplaceBothModelsAndPreserveControllersAndSettings()
    {
        yield return SetConnectionPause(true);
        object presentation = Field<object>(master, "presentation");
        object[] motions = { Field<object>(master, "first").GetType().GetField("Motion").GetValue(Field<object>(master, "first")),
            Field<object>(master, "second").GetType().GetField("Motion").GetValue(Field<object>(master, "second")) };
        int hubId = hub.GetInstanceID();
        SetKeys(Key.LeftShift, Key.RightShift);
        yield return null;
        InvokeMaster("SetDisplayMode", 2);
        InvokeMaster("SetColor", 0, 1);
        Assert.That(Field<Transform>(Field<Array>(presentation, "avatars").GetValue(0), "visual").gameObject.activeSelf, Is.True,
            "Replacement must bind the new active skeleton in the same frame");
        Assert.That(firstPlayer.transform.Find("ChambaraOriginalBlueBody"), Is.Not.Null);
        Assert.That(firstSword.transform.Find("ChambaraBlueSword"), Is.Not.Null);
        InvokeMaster("SetColor", 1, 0);
        yield return null;
        Assert.That(secondPlayer.transform.Find("ChambaraOriginalRedBody"), Is.Not.Null);
        Assert.That(secondSword.transform.Find("ChambaraRedSword"), Is.Not.Null);
        GameObject[] players = { firstPlayer, secondPlayer }, swords = { firstSword, secondSword };
        for (int i = 0; i < 2; i++)
        {
            Assert.That(players[i].transform.Cast<Transform>().Count(t => t.name.StartsWith("Chambara") && t.gameObject.activeSelf), Is.EqualTo(1));
            Assert.That(swords[i].transform.Cast<Transform>().Count(t => t.name.StartsWith("Chambara") && t.name != "ChambaraGripGlove" && t.gameObject.activeSelf), Is.EqualTo(1));
            Assert.That(swords[i].transform.Cast<Transform>().Count(t => t.name == "ChambaraGripGlove" && t.gameObject.activeSelf), Is.Zero);
            Assert.That(BodyRenderers(players[i], swords[i]).All(r => r is SkinnedMeshRenderer), Is.True);
            Assert.That(ReferenceEquals(Field<object>(master, i == 0 ? "first" : "second").GetType().GetField("Motion").GetValue(Field<object>(master, i == 0 ? "first" : "second")), motions[i]), Is.True);
            AssertCurrentPose(swords[i], true);
        }
        Assert.That(hub.GetInstanceID(), Is.EqualTo(hubId));
        // Repeated changes and choosing the same model for both players are valid.
        InvokeMaster("SetColor", 0, 0);
        yield return null;
        Assert.That(firstPlayer.transform.Find("ChambaraOriginalRedBody"), Is.Not.Null);
        InvokeMaster("ResetMatch");
        Assert.That(Paused(), Is.True);
        Assert.That(firstPlayer.transform.Find("ChambaraOriginalRedBody"), Is.Not.Null);
        Assert.That(secondPlayer.transform.Find("ChambaraOriginalRedBody"), Is.Not.Null);
        AssertLayout(new[] { firstPlayer.GetComponentInChildren<Camera>(), secondPlayer.GetComponentInChildren<Camera>() }, 0);
        yield return SetConnectionPause(false);
        yield return null;
        Capture(firstPlayer.GetComponentInChildren<Camera>(), "single-swapped-red");
        InvokeMaster("SetCharacter", 0, 1);
        Assert.That(firstPlayer.transform.Find("ChambaraBaldRedBody"), Is.Null, "Character selection is restricted to pause");
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);
    }

    [UnityTest]
    public IEnumerator DiagonalCutsMoveSidewaysAndSuccessfulGuardsPushBackAlongTheCut()
    {
        for (int attacker = 0; attacker < 2; attacker++)
            foreach (bool guard in new[] { false, true })
            {
                yield return ResetInPlace();
                object input = Field<object>(master, attacker == 0 ? "first" : "second");
                object defense = Field<object>(master, attacker == 0 ? "second" : "first");
                input.GetType().GetField("Roll").SetValue(input, 20f);
                defense.GetType().GetField("Roll").SetValue(defense, guard ? 90f : 0f);
                defense.GetType().GetField("Pitch").SetValue(defense, guard ? -45f : -90f);
                yield return null;
                Key attackKey = attacker == 0 ? Key.W : Key.UpArrow;
                Key guardKey = attacker == 0 ? Key.RightShift : Key.LeftShift;
                if (guard)
                {
                    SetKeys(guardKey);
                    StartPhysicalGuardContact(attacker, new Vector3(.34f, -.94f, 0f));
                    yield return null;
                }
                else yield return HoldUntil(() => Field<bool>(master, "reactionActive"), attackKey);
                Vector3 start = Field<Vector3>(master, "pushStart"), target = Field<Vector3>(master, "pushTarget");
                float attackSign = attacker == 0 ? 1f : -1f;
                int pushed = guard ? attacker : 1 - attacker;
                GameObject[] players = { firstPlayer, secondPlayer };
                Vector3 otherStart = players[1 - pushed].transform.localPosition;
                Vector3 lateralStart = Field<Vector3>(master, "lateralStart"), lateralTarget = Field<Vector3>(master, "lateralTarget");
                Assert.That(target.x - start.x, Is.EqualTo(0f).Within(0.0001f), "The advancing player must not receive sideways recoil");
                Assert.That((lateralTarget.x - lateralStart.x) * attackSign * (guard ? -1f : 1f), Is.InRange(0.03f, guard ? 0.5f : 0.6f));
                Assert.That(target.z - start.z, Is.EqualTo(attackSign * (guard ? -DuelMatch.GuardPush : DuelMatch.HitPush)).Within(0.001f));
                Pause(true);
                Vector3 pausedPosition = master.transform.position;
                Vector3 pausedPlayer = players[pushed].transform.position;
                Quaternion pausedFacing = players[pushed].transform.rotation;
                yield return new WaitForSecondsRealtime(0.08f);
                Assert.That(master.transform.position, Is.EqualTo(pausedPosition));
                Assert.That(players[pushed].transform.position, Is.EqualTo(pausedPlayer));
                Assert.That(players[pushed].transform.rotation, Is.EqualTo(pausedFacing));
                Pause(false); SetKeys();
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(Vector3.Distance(master.transform.position, target), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(players[1 - pushed].transform.localPosition, otherStart), Is.LessThan(.2f),
                    "The unstruck player may close only the small gap left by lateral recoil.");
                Assert.That(Vector3.Distance(players[pushed].transform.localPosition, lateralTarget), Is.LessThan(0.001f));
                yield return new WaitForSecondsRealtime(0.8f);
                for (int p = 0; p < 2; p++)
                {
                    Vector3 direction = players[1 - p].transform.position - players[p].transform.position;
                    Assert.That(Vector3.Angle(players[p].transform.forward, direction), Is.LessThan(0.3f));
                    Camera camera = players[p].GetComponentInChildren<Camera>();
                    Vector3 aim = players[1 - p].transform.position + Vector3.up - camera.transform.position;
                    // Compare directions directly: acos in Vector3.Angle loses
                    // precision near zero and can report 0.02 degrees for aligned vectors.
                    Assert.That(Vector3.Distance(camera.transform.forward, aim.normalized), Is.LessThan(0.0001f));
                    Capture(camera, "diagonal-P" + (p + 1) + (guard ? "-guard" : "-hit"));
                }
            }
    }

    [UnityTest]
    public IEnumerator RepeatedCutsAdvanceAlongTheNewFacingAndRematchRestoresBothPlayers()
    {
        Vector3[] initial = { firstPlayer.transform.localPosition, secondPlayer.transform.localPosition };
        Quaternion[] facing = { firstPlayer.transform.localRotation, secondPlayer.transform.localRotation };
        object input = Field<object>(master, "first");
        input.GetType().GetField("Roll").SetValue(input, 35f);
        yield return null;
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W);
        SetKeys();
        yield return new WaitForSecondsRealtime(0.6f);
        Vector3 forward = firstPlayer.transform.forward;
        Assert.That(Mathf.Abs(forward.x), Is.GreaterThan(0.03f));
        Assert.That(Vector3.Distance(firstPlayer.transform.localPosition, initial[0]), Is.LessThan(.1f),
            "Spacing may restore only the diagonal gap without moving the pushed fighter.");
        yield return HoldUntil(() => Vector3.Angle(firstSword.transform.forward, firstPlayer.transform.forward) >= 89.9f, Key.S);
        SetKeys(); yield return null;
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W);
        Vector3 step = Field<Vector3>(master, "pushTarget") - Field<Vector3>(master, "pushStart");
        Assert.That(Vector3.Distance(step, forward * 3f), Is.LessThan(0.01f));
        Assert.That(Mathf.Abs(step.x), Is.GreaterThan(0.09f), "The next exchange must travel across the stage at its new angle");
        SetKeys(); yield return new WaitForSecondsRealtime(0.6f);
        Vector3 following = firstPlayer.transform.localPosition - initial[0];
        Assert.That(following.magnitude, Is.LessThan(.6f), "Only the lateral spacing excess may make the unstruck player follow between exchanges.");
        Assert.That(Vector3.Dot(following, forward), Is.GreaterThanOrEqualTo(-.001f));
        Capture(firstPlayer.GetComponentInChildren<Camera>(), "second-exchange-P1");
        InvokeMaster("ResetMatch");
        GameObject[] players = { firstPlayer, secondPlayer };
        for (int p = 0; p < 2; p++)
        {
            Assert.That(players[p].transform.localPosition, Is.EqualTo(initial[p]));
            Assert.That(Quaternion.Angle(players[p].transform.localRotation, facing[p]), Is.LessThan(0.001f));
        }
    }

    [UnityTest]
    public IEnumerator LargeDuelAndFreeGapsCloseEvenDuringStaggerAndStayWithinBladeReach()
    {
        master.enabled = false;
        flow.enabled = false;
        foreach (bool free in new[] { false, true })
        {
            Pause(true); InvokeMaster("SetFreeMode", free); Pause(false);
            Vector3 start = firstPlayer.transform.position;
            secondPlayer.transform.position = start + new Vector3(8f, 0f, 5f);
            master.GetType().GetField("reactionActive", PrivateInstance).SetValue(master, false);
            master.GetType().GetField("reactionStart", PrivateInstance).SetValue(master, MatchTime());
            for (int frame = 0; frame < 120; frame++) InvokeMaster("RestoreDuelSpacing", 1f / 60f);
            float gap = Vector3.Distance(firstPlayer.transform.position, secondPlayer.transform.position);
            Assert.That(gap, Is.EqualTo(3f).Within(.002f), "Spacing must restore in duel and free without waiting for a new exchange.");
            for (int player = 0; player < 2; player++)
            {
                Vector3 body = (player == 0 ? firstPlayer : secondPlayer).transform.position;
                Vector3 target = (player == 0 ? secondPlayer : firstPlayer).transform.position;
                Vector3 forward = (target - body).normalized;
                Vector3 grip = body + forward * .55f;
                var attack = new CombatPose(grip, Vector3.up, grip, forward, body, forward,
                    Vector3.down, grip, forward);
                var defence = new CombatPose(target, Vector3.up, target, Vector3.up, target,
                    -forward, Vector3.zero, target, Vector3.up);
                Assert.That(CombatContact.Resolve(AttackKind.Cut, attack, defence, false).Outcome,
                    Is.EqualTo(CombatOutcome.Hit), "Both fighters must actually reach the restored opponent.");
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator KeyboardDirectionsRemainNaturalForBothPlayersInEitherView()
    {
        master.enabled = false; flow.enabled = false;
        object[] players = { Field<object>(master, "first"), Field<object>(master, "second") };
        foreach (int mode in new[] { 1, 2 })
        {
            Pause(true); InvokeMaster("SetDisplayMode", mode); Pause(false);
            for (int player = 0; player < 2; player++)
                foreach (bool left in new[] { true, false })
                {
                    players[player].GetType().GetField("Pitch").SetValue(players[player], -60f);
                    players[player].GetType().GetField("Roll").SetValue(players[player], 0f);
                    SetKeys(player == 0 ? (left ? Key.A : Key.D) : (left ? Key.LeftArrow : Key.RightArrow));
                    yield return null;
                    InvokeMaster("ReadInput", players[player], player == 0, true);
                    int other=1-player;
                    float otherRoll=(float)players[other].GetType().GetField("Roll").GetValue(players[other]);
                    InvokeMaster("ReadInput", players[other], other==0, true);
                    Assert.That(players[other].GetType().GetField("Roll").GetValue(players[other]),Is.EqualTo(otherRoll),
                        "WASD belongs only to P1, and arrows only to P2.");
                    Quaternion rotation = (Quaternion)players[player].GetType().GetField("TargetRotation").GetValue(players[player]);
                    Assert.That((rotation * Vector3.forward).x * (left ? -1f : 1f), Is.GreaterThan(0f));
                }
        }
        foreach (int player in new[] { 0, 1 })
            foreach (bool up in new[] { true, false })
            {
                players[player].GetType().GetField("Pitch").SetValue(players[player], -45f);
                players[player].GetType().GetField("Roll").SetValue(players[player], 0f);
                SetKeys(player == 0 ? (up ? Key.W : Key.S) : (up ? Key.UpArrow : Key.DownArrow));
                yield return null;
                InvokeMaster("ReadInput", players[player], player == 0, true);
                float pitch = (float)players[player].GetType().GetField("Pitch").GetValue(players[player]);
                Assert.That((pitch + 45f) * (up ? 1f : -1f), Is.GreaterThan(0f),
                    "W/up swings forward; S/down restores the original upright stance.");
            }
        SetKeys();
    }

    [UnityTest]
    public IEnumerator CharacterAndColorAreIndependentAndStagesPreserveTheRound()
    {
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W);
        SetKeys(); yield return new WaitForSecondsRealtime(1.1f);
        yield return SetConnectionPause(true);
        double clock = MatchTime();
        Vector3[] positions = { firstPlayer.transform.position, secondPlayer.transform.position };
        int hubId = hub.GetInstanceID();
        InvokeMaster("SetDisplayMode", 2);
        InvokeMaster("SetStage", 1);
        Assert.That((int)master.GetType().GetProperty("StageIndex").GetValue(master), Is.EqualTo(1));
        MonoBehaviour arena = FindComponent("DuelArena");
        GameObject environment = Field<GameObject>(arena, "environment");
        Assert.That(environment, Is.Not.Null);
        Assert.That(environment.transform.position.y, Is.EqualTo(-0.35f).Within(0.01f));
        string[] characters = { "Original", "Bald", "Afro", "Robot" };
        string[] colors = { "Red", "Blue", "Green", "Yellow", "Purple", "Black" };
        for (int character = 0; character < characters.Length; character++)
        {
            InvokeMaster("SetCharacter", 0, character);
            for (int color = 0; color < colors.Length; color++)
            {
                InvokeMaster("SetColor", 0, color);
                Assert.That((int)master.GetType().GetMethod("CharacterIndex").Invoke(master, new object[] { 0 }), Is.EqualTo(character));
                Assert.That((int)master.GetType().GetMethod("ColorIndex").Invoke(master, new object[] { 0 }), Is.EqualTo(color));
                Transform body = firstPlayer.transform.Find("Chambara" + characters[character] + colors[color] + "Body");
                Assert.That(body, Is.Not.Null);
                Assert.That(body.gameObject.activeSelf, Is.True);
                Assert.That(firstSword.transform.Find("Chambara" + colors[color] + "Sword"), Is.Not.Null);
                AssertCurrentPose(firstSword, false);
                yield return null;
                Assert.That(firstPlayer.transform.Cast<Transform>().Count(child => child.name.StartsWith("Chambara")), Is.EqualTo(1));
            }
            CaptureFootwork(firstPlayer, "roster-" + characters[character]);
        }
        InvokeMaster("SetCharacter", 1, 3);
        InvokeMaster("SetColor", 1, 2);
        Assert.That(secondPlayer.transform.Find("ChambaraRobotGreenBody"), Is.Not.Null);
        AssertCurrentPose(secondSword, false);
        Capture(firstPlayer.GetComponentInChildren<Camera>(), "colosseum-P1");
        CapturePause();
        Assert.That(MatchTime(), Is.EqualTo(clock));
        Assert.That(firstPlayer.transform.position, Is.EqualTo(positions[0]));
        Assert.That(secondPlayer.transform.position, Is.EqualTo(positions[1]));
        Assert.That(hub.GetInstanceID(), Is.EqualTo(hubId));
        Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
        InvokeMaster("SetStage", 0);
        Assert.That(environment.activeSelf, Is.False);
        yield return null;
        Assert.That(environment == null, Is.True);
        InvokeMaster("SetStage", 1);
        InvokeMaster("ResetMatch");
        Assert.That((int)master.GetType().GetProperty("StageIndex").GetValue(master), Is.EqualTo(1));
        Assert.That(firstPlayer.transform.Find("ChambaraRobotBlackBody"), Is.Not.Null);
        Assert.That(secondPlayer.transform.Find("ChambaraRobotGreenBody"), Is.Not.Null);
        yield return SetConnectionPause(false); yield return null;
        InvokeMaster("SetStage", 0);
        Assert.That((int)master.GetType().GetProperty("StageIndex").GetValue(master), Is.EqualTo(1), "Stage changes require pause");
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);
    }

    [UnityTest]
    public IEnumerator CircularStadiumUsesTheGrassAndPreservesTheRoundAndController()
    {
        yield return SetConnectionPause(true);
        double clock = MatchTime();
        Vector3[] positions = { firstPlayer.transform.position, secondPlayer.transform.position };
        int hubId = hub.GetInstanceID();
        yield return null;
        InvokeMaster("SetStage", 2);
        Assert.That((int)master.GetType().GetProperty("StageIndex").GetValue(master), Is.EqualTo(2));
        Assert.That((bool)master.GetType().GetProperty("RingOutEnabled").GetValue(master), Is.False);
        MonoBehaviour arena = FindComponent("DuelArena");
        GameObject environment = Field<GameObject>(arena, "environment");
        GameObject platform = Field<GameObject>(arena, "platform");
        Assert.That(platform.activeSelf, Is.False);
        Assert.That(environment.transform.position.y, Is.EqualTo(0f).Within(0.001f));
        Renderer grass = environment.GetComponentsInChildren<Renderer>().Single(r => r.name == "Grass_mowing_stripe_00");
        Assert.That(grass.bounds.max.y, Is.EqualTo(0f).Within(0.001f));
        Assert.That(Field<GameObject>(arena, "background"), Is.Null, "The stadium is the environment itself in field modes.");
        InvokeMaster("SetDisplayMode", 2);
        yield return null;
        Capture(firstPlayer.GetComponentInChildren<Camera>(), "stadium-P1", 1280, 720);
        CapturePause("pause-stadium");
        CaptureFootwork(firstPlayer, "stadium-grounded");
        Assert.That(MatchTime(), Is.EqualTo(clock));
        Assert.That(firstPlayer.transform.position, Is.EqualTo(positions[0]));
        Assert.That(secondPlayer.transform.position, Is.EqualTo(positions[1]));
        Assert.That(hub.GetInstanceID(), Is.EqualTo(hubId));

        // A real keyboard hit can cross the old platform boundary without losing.
        master.transform.position += Vector3.forward * 14f;
        // This fixture teleports the arena participants. Snap the visual bodies
        // too, so the hit targets are at the new position rather than recovering
        // from fourteen metres of animation lag during a single sword stroke.
        FindComponent("DuelPresentation").GetType().GetMethod("ResetEffects").Invoke(FindComponent("DuelPresentation"), null);
        yield return SetConnectionPause(false);
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W);
        SetKeys(); yield return new WaitForSecondsRealtime(0.7f);
        Assert.That(secondPlayer.transform.position.z, Is.GreaterThan(17f));
        Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
        Assert.That(master.transform.Find("Match result").gameObject.activeSelf, Is.False);
        InvokeMaster("ResetMatch");
        Assert.That((int)master.GetType().GetProperty("StageIndex").GetValue(master), Is.EqualTo(2));
        Assert.That(platform.activeSelf, Is.False);
        Assert.That(firstPlayer.transform.position, Is.EqualTo(positions[0]));
        Assert.That(secondPlayer.transform.position, Is.EqualTo(positions[1]));
        yield return SetConnectionPause(true);
        InvokeMaster("SetStage", 1);
        Assert.That(platform.activeSelf, Is.True);
        Assert.That((bool)master.GetType().GetProperty("RingOutEnabled").GetValue(master), Is.True);
        Assert.That(environment.activeSelf, Is.False);
        yield return SetConnectionPause(false); yield return null;
        Assert.That(environment == null, Is.True);
    }

    [UnityTest]
    public IEnumerator StadiumHitsAndGuardsStopBothPlayersAtTheBoundaryInEveryDirection()
    {
        Pause(true); InvokeMaster("SetStage", 2); Pause(false);
        master.enabled = false;
        MethodInfo begin = master.GetType().GetMethod("BeginReaction", PrivateInstance);
        MethodInfo advance = master.GetType().GetMethod("AdvanceReaction", PrivateInstance);
        MethodInfo checkWinner = master.GetType().GetMethod("CheckWinner", PrivateInstance);
        GameObject[] players = { firstPlayer, secondPlayer };
        float[] heights = players.Select(p => p.transform.position.y).ToArray();
        Vector2 center = Field<Vector2>(master, "fieldCenter");
        float radius = Field<float>(master, "fieldRadius");
        Assert.That(radius, Is.GreaterThan(80f), "The limit follows the stadium floor, beyond the old cricket boundary.");
        foreach (int attacker in new[] { 0, 1 })
            foreach (CombatOutcome outcome in new[] { CombatOutcome.Hit, CombatOutcome.GuardSuccess, CombatOutcome.GuardBreak })
                foreach (float angle in new[] { 0f, 45f, 110f, 245f })
                    foreach (float sideways in new[] { -2f, 0f, 2f })
                    {
                        InvokeMaster("ResetMatch");
                        master.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                        bool guard = outcome == CombatOutcome.GuardSuccess;
                        float distance = guard ? DuelMatch.GuardPush : outcome == CombatOutcome.Hit ? DuelMatch.HitPush : DuelMatch.GuardBreakPush;
                        float sign = (attacker == 0 ? 1f : -1f) * (guard ? -1f : 1f);
                        Vector3 direction = firstPlayer.transform.forward * sign;
                        master.transform.position = new Vector3(center.x, 0f, center.y) + direction * (radius - 1.5f - 0.15f);
                        object input = Field<object>(master, attacker == 0 ? "first" : "second");
                        input.GetType().GetField("CutVelocity").SetValue(input, new Vector3(sideways, 0f, -2f));
                        int pushed = guard ? attacker : 1 - attacker;
                        Vector3 otherLocal = players[1 - pushed].transform.localPosition;
                        var result = new CombatResult(outcome, attacker, pushed, distance * sign);
                        begin.Invoke(master, new object[] { result, 0.0 });
                        float movement = Vector3.Distance(Field<Vector3>(master, "pushStart"), Field<Vector3>(master, "pushTarget"));
                        Assert.That(movement, Is.InRange(0.01f, 0.2f), "Knockback should reach the boundary and stop");
                        for (int frame = 0; frame <= 20; frame++)
                        {
                            advance.Invoke(master, new object[] { frame / 60.0 });
                            checkWinner.Invoke(master, null);
                            for (int playerIndex = 0; playerIndex < players.Length; playerIndex++)
                            {
                                Vector3 position = players[playerIndex].transform.position;
                                Assert.That(Vector2.Distance(new Vector2(position.x, position.z), center), Is.LessThan(radius));
                                Assert.That(position.y, Is.EqualTo(heights[playerIndex]).Within(0.001f));
                            }
                            Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
                        }
                        Assert.That(players[1 - pushed].transform.localPosition, Is.EqualTo(otherLocal), "Only the pushed player can move sideways");
                        begin.Invoke(master, new object[] { result, 1.0 });
                        advance.Invoke(master, new object[] { 1.4 });
                        Assert.That(Vector3.Distance(Field<Vector3>(master, "pushStart"), Field<Vector3>(master, "pushTarget")), Is.LessThan(0.001f));
                    }
        InvokeMaster("ResetMatch");
        master.enabled = true;
        yield return null;
    }

    [UnityTest]
    public IEnumerator SuddenDeathSwitchesAtTheNextRoundAndUsesTheSmallerRimForRingOut()
    {
        MonoBehaviour arena = FindComponent("DuelArena");
        float normalRadius = Field<float>(master, "arenaRadius");
        float smallRadius = normalRadius * Field<float>(master, "suddenDeathRadiusRatio");
        for (int round = 0; round < 3; round++)
        {
            Assert.That(Series().Phase, Is.EqualTo(RoundPhase.Fight));
            Assert.That(Series().EndRound(MatchWinner.Draw, MatchTime()), Is.True);
            Assert.That(Field<bool>(master, "suddenDeathStageActive"), Is.False,
                "Keep the completed round on its existing platform until the next round begins.");
            if (round < 2) yield return AdvanceToFight();
        }
        Assert.That(Series().IsFinalRound, Is.True);
        yield return AdvancePhase(3.41);
        Assert.That(Series().Phase, Is.EqualTo(RoundPhase.Round));
        Assert.That(Field<bool>(master, "suddenDeathStageActive"), Is.True);
        GameObject stage = Field<GameObject>(arena, "environment");
        Renderer rim = stage.GetComponentsInChildren<Renderer>().Single(r => r.name == "Pale_red_rim");
        Assert.That(rim.bounds.extents.x, Is.EqualTo(smallRadius).Within(.001f));
        Assert.That(Field<GameObject>(arena, "platform").activeSelf, Is.False);
        AssertStadiumBackground(arena, stage);
        yield return AdvanceToFight();
        Capture(firstPlayer.GetComponentInChildren<Camera>(), "sudden-death-stadium", 1280, 720);
        Vector2 center = Field<Vector2>(master, "arenaCenter");
        firstPlayer.transform.position = new Vector3(center.x + smallRadius + .05f, firstPlayer.transform.position.y, center.y);
        Assert.That(smallRadius + .05f, Is.LessThan(normalRadius));
        master.GetType().GetMethod("CheckWinner", PrivateInstance).Invoke(master, null);
        Assert.That(Winner(), Is.EqualTo(MatchWinner.Player2), "The smaller platform must also have the smaller loss boundary.");
        InvokeMaster("ResetMatch");
        Assert.That(Field<bool>(master, "suddenDeathStageActive"), Is.False);
        stage = Field<GameObject>(arena, "environment");
        Assert.That(stage.GetComponentsInChildren<Renderer>().Single(r => r.name == "Pale_red_rim").bounds.extents.x,
            Is.EqualTo(normalRadius).Within(.001f));
        AssertStadiumBackground(arena, stage);
    }

    private void AssertStadiumBackground(MonoBehaviour arena, GameObject stage)
    {
        GameObject stadium = Field<GameObject>(arena, "background");
        Assert.That(stadium, Is.Not.Null);
        Assert.That(stadium.activeSelf, Is.True);
        Assert.That(stadium.GetComponentsInChildren<Renderer>().Length, Is.EqualTo(134));
        float postBottom = stage.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y);
        float grassHeight = stadium.GetComponentsInChildren<Renderer>().Single(r => r.name == "Grass_mowing_stripe_00").bounds.max.y;
        Assert.That(grassHeight, Is.EqualTo(postBottom).Within(.001f), "The supplied support posts stand on the background stadium grass.");
        Assert.That(stage.GetComponentsInChildren<Renderer>().Single(r => r.name == "Platform_floor").bounds.max.y,
            Is.EqualTo(0f).Within(.001f));
    }

    private void CapturePause(string name = "pause-roster-stages")
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR"))) return;
        Camera camera = firstPlayer.GetComponentInChildren<Camera>();
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(canvas => canvas.isActiveAndEnabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        foreach (Canvas canvas in canvases)
        { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f; }
        Canvas.ForceUpdateCanvases();
        try { Capture(camera, name, 1920, 1080, true); }
        finally
        {
            foreach (Canvas canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
            Canvas.ForceUpdateCanvases();
        }
    }

    [UnityTest]
    public IEnumerator RecoilFollowsTheActualPushAfterHitStopAndKeepsTheSupportFootPlanted()
    {
        master.enabled = false;
        MethodInfo begin = master.GetType().GetMethod("BeginReaction", PrivateInstance);
        MethodInfo advance = master.GetType().GetMethod("AdvanceReaction", PrivateInstance);
        Array avatars = Field<Array>(Field<object>(master, "presentation"), "avatars");
        Quaternion upright = Quaternion.Euler(-90f, 0f, 0f);
        for (int playerIndex = 0; playerIndex < 2; playerIndex++)
            foreach (int fps in new[] { 30, 60, 144 })
            {
                InvokeMaster("ResetMatch");
                GameObject player = playerIndex == 0 ? firstPlayer : secondPlayer;
                object avatar = avatars.GetValue(playerIndex);
                TickAvatar(avatar, 0, 0, upright, false, 0);
                object[] legs = Field<Array>(avatar, "legs").Cast<object>().ToArray();
                Transform[] feet = legs.Select(l => (Transform)l.GetType().GetField("Ankle").GetValue(l)).ToArray();
                Vector3[] plants = feet.Select(t => t.position).ToArray();
                Vector3 root = master.transform.position;
                var result = new CombatResult(CombatOutcome.Hit, 1 - playerIndex, playerIndex, playerIndex == 0 ? -3f : 3f);
                begin.Invoke(master, new object[] { result, 0.0 });
                int landings = 0; float highest = 0f;
                bool[] lifted = new bool[2];
                for (int frame = 0; frame <= fps * 2; frame++)
                {
                    double now = frame / (double)fps;
                    bool[] wasMoving = legs.Select(l => (bool)l.GetType().GetField("Moving").GetValue(l)).ToArray();
                    Vector3[] contacts = legs.Select(l => (Vector3)l.GetType().GetField("WorldPlant").GetValue(l)).ToArray();
                    // Run the production hit-stop and push trajectory, not a
                    // recoil notification with a stationary combat origin.
                    advance.Invoke(master, new object[] { now });
                    TickAvatar(avatar, now, 1f / fps, upright, false, 0);
                    int movingFeet = 0;
                    for (int foot = 0; foot < 2; foot++)
                    {
                        bool moving = (bool)legs[foot].GetType().GetField("Moving").GetValue(legs[foot]);
                        if (moving)
                        {
                            movingFeet++; lifted[foot] = true;
                            Assert.That(Vector3.Distance(feet[foot].position,
                                (Vector3)legs[foot].GetType().GetField("WorldPlant").GetValue(legs[foot])), Is.LessThan(0.003f),
                                "Swinging ankle exceeded its leg reach at " + fps + " FPS");
                        }
                        else if (!wasMoving[foot])
                            Assert.That(Vector3.Distance(feet[foot].position, contacts[foot]), Is.LessThan(0.003f),
                                "Supporting foot slid in world space at " + fps + " FPS");
                        Assert.That(feet[foot].position.y, Is.GreaterThanOrEqualTo(plants[foot].y - 0.003f));
                        highest = Mathf.Max(highest, feet[foot].position.y - plants[foot].y);
                        if (now <= result.HitStopSeconds)
                            Assert.That(Vector3.Distance(feet[foot].position, plants[foot]), Is.LessThan(0.001f), "Feet must wait for hit stop.");
                        if (now >= 1.5) Assert.That(moving, Is.False, "Recovery must settle, not shuffle forever.");
                    }
                    Assert.That(movingFeet, Is.LessThanOrEqualTo(1), "Both feet cannot abandon support together.");
                    if (now <= result.HitStopSeconds)
                    {
                        Assert.That(master.transform.position, Is.EqualTo(root));
                        Assert.That(AvatarFlag(avatar, "Stepped"), Is.False);
                    }
                    if (now >= 1.5) Assert.That(AvatarFlag(avatar, "Stepped"), Is.False);
                    if (AvatarFlag(avatar, "Stepped")) landings++;
                    if (fps == 60 && (frame == 9 || frame == 19) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR")))
                    { yield return null; CaptureFootwork(player, "P" + (playerIndex + 1) + "-recoil-" + frame); }
                }
                Assert.That(Vector3.Distance(master.transform.position, root), Is.EqualTo(3f).Within(0.001f));
                Assert.That(lifted.All(value => value), Is.True, "Both legs must participate in recovery.");
                Assert.That(landings, Is.InRange(2, 30), "A real three-metre push needs alternating contacts without endless chatter.");
                Assert.That(highest, Is.InRange(0.02f, 0.08f), "Recovery needs a visible, short swing-foot arc.");
                Assert.That(Vector3.Distance(Field<Vector3>(avatar, "renderedOrigin"), player.transform.position), Is.LessThan(0.001f));

                // Clearing history during a real airborne step suppresses its
                // delayed landing sound; a new step may still start normally.
                InvokeMaster("ResetMatch");
                begin.Invoke(master, new object[] { result, 0.0 });
                double afterStop = result.HitStopSeconds + 0.05;
                advance.Invoke(master, new object[] { afterStop });
                TickAvatar(avatar, afterStop, 0.05f, upright, false, 0);
                Assert.That(Field<int>(avatar, "movingLeg"), Is.GreaterThanOrEqualTo(0));
                avatar.GetType().GetMethod("ClearMotionHistory").Invoke(avatar, null);
                TickAvatar(avatar, afterStop + 0.3, 0.3f, upright, false, 0);
                Assert.That(AvatarFlag(avatar, "Stepped"), Is.False);
            }
        yield return null;
    }

    private void InvokeMaster(string name, params object[] arguments) => master.GetType().GetMethod(name, BindingFlags.Public | PrivateInstance).Invoke(master, arguments);
    private static void AssertLayout(Camera[] cameras, int viewedPlayer)
    {
        for (int i = 0; i < 2; i++)
        {
            Assert.That(cameras[i].enabled, Is.EqualTo(i == viewedPlayer));
            Assert.That(cameras[i].rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(cameras[i].GetComponent<AudioListener>().enabled, Is.EqualTo(i == viewedPlayer));
        }
    }

    private void CaptureFootwork(GameObject player, string name)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR"))) return;
        Camera camera = new GameObject("Footwork preview").AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 40f; camera.nearClipPlane = 0.1f;
        camera.transform.position = player.transform.TransformPoint(new Vector3(2f, 0.5f, 3.5f));
        camera.transform.LookAt(player.transform.position);
        GameObject other = player == firstPlayer ? secondPlayer : firstPlayer;
        other.SetActive(false);
        try { Capture(camera, name); }
        finally { other.SetActive(true); Object.Destroy(camera.gameObject); }
    }

    [UnityTest]
    public IEnumerator OwnCameraFadesOnlyItsBodyAndRestoresMaterialsAndFlash()
    {
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.W);
        Pause(true); // Keep the hit flash stable while rendering both viewports.
        Renderer[] firstBody = BodyRenderers(firstPlayer, firstSword), secondBody = BodyRenderers(secondPlayer, secondSword);
        Material[][] originals = firstBody.Concat(secondBody).Select(r => r.sharedMaterials).ToArray();
        bool seen = false;
        Camera firstCamera = firstPlayer.GetComponentInChildren<Camera>();
        Camera secondCamera = secondPlayer.GetComponentInChildren<Camera>();
        Action<ScriptableRenderContext, Camera> probe = (context, camera) =>
        {
            if (camera != firstCamera && camera != secondCamera) return;
            seen = true;
            foreach (Renderer renderer in camera == firstCamera ? firstBody : secondBody)
                foreach (Material material in renderer.sharedMaterials)
                    Assert.That(material.color.a, Is.EqualTo(0.3f).Within(0.001f));
            foreach (Renderer renderer in camera == firstCamera ? secondBody : firstBody)
                foreach (Material material in renderer.sharedMaterials)
                    Assert.That(material.color.a, Is.EqualTo(1f));
            foreach (Renderer renderer in firstSword.GetComponentsInChildren<Renderer>())
                Assert.That(renderer.sharedMaterial.color.a, Is.EqualTo(1f));
        };
        RenderPipelineManager.beginCameraRendering += probe;
        try
        {
            Capture(firstCamera, "hit-P1"); Capture(secondCamera, "hit-P2");
            Assert.That(seen, Is.True);
        }
        finally { RenderPipelineManager.beginCameraRendering -= probe; }
        Renderer[] both = firstBody.Concat(secondBody).ToArray();
        for (int i = 0; i < both.Length; i++) Assert.That(both[i].sharedMaterials, Is.EqualTo(originals[i]));
        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in secondBody)
        {
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor(Shader.PropertyToID("_BaseColor")), Is.EqualTo(Color.white));
        }
        master.GetType().GetMethod("ResetMatch").Invoke(master, null);
        Pause(false); SetKeys(); yield return null;
        Capture(firstCamera, "idle-P1"); Capture(secondCamera, "idle-P2");
        SetKeys(Key.LeftShift, Key.RightShift); yield return null; yield return null;
        Capture(firstCamera, "guard-P1"); Capture(secondCamera, "guard-P2");
        // Third camera shows grounded skin geometry without own-view fading.
        Camera front = new GameObject("Rig preview").AddComponent<Camera>();
        front.enabled = false; front.fieldOfView = 45; front.nearClipPlane = 0.1f;
        front.transform.position = firstPlayer.transform.TransformPoint(new Vector3(0, 0.7f, 6));
        front.transform.LookAt(firstPlayer.transform.TransformPoint(new Vector3(0, 0, 0)));
        secondPlayer.SetActive(false);
        Capture(front, "guard-front");
        secondPlayer.SetActive(true); Object.Destroy(front.gameObject);
    }

    private void Capture(Camera camera, string name, int width = 960, int height = 720, bool layoutCanvas = false)
    {
        Rect previous = camera.rect;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        var texture = new RenderTexture(width, height, 24);
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.rect = new Rect(0, 0, 1, 1); texture.Create();
            if (layoutCanvas)
            {
                camera.targetTexture = texture;
                Canvas.ForceUpdateCanvases();
            }
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            string output = Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output); RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            }
        }
        finally
        {
            camera.targetTexture = previousTarget;
            camera.rect = previous; RenderTexture.active = previousActive;
            texture.Release(); Object.Destroy(texture); Object.Destroy(image);
        }
    }

    private bool Paused() => (bool)master.GetType().GetProperty("IsPaused").GetValue(master);
    private double MatchTime() => (double)master.GetType().GetProperty("MatchTime").GetValue(master);
    private bool HubOverlay() => (bool)hub.GetType().GetProperty("OverlayVisible").GetValue(hub);
    private void Pause(bool value)
    {
        if ((bool)flow.GetType().GetProperty("PauseOpen").GetValue(flow) != value) Flow("TogglePause");
    }
    private IEnumerator SetConnectionPause(bool value)
    {
        Pause(value);
        SetKeys();
        yield return null;
        Assert.That(Paused(), Is.EqualTo(value));
        Assert.That((bool)flow.GetType().GetProperty("PauseOpen").GetValue(flow), Is.EqualTo(value));
        Assert.That(Field<GameObject>(flow, "pauseCanvas").activeSelf, Is.EqualTo(value));
        Assert.That(HubOverlay(), Is.False, "The legacy phone overlay is not part of the current Esc menu.");
        Assert.That(master.transform.Find("Pause UI"), Is.Null, "The Y debug menu has been removed.");
    }
    private void Flow(string method, params object[] args) => flow.GetType().GetMethod(method).Invoke(flow, args);
    private DuelSeries Series() => Field<DuelSeries>(master, "series");
    private IEnumerator AdvancePhase(double seconds)
    {
        master.GetType().GetField("matchTime", PrivateInstance).SetValue(master, Series().PhaseStarted + seconds);
        yield return null;
    }
    private IEnumerator AdvanceToFight()
    {
        for (int transition = 0; Series().Phase != RoundPhase.Fight && transition < 5; transition++)
        {
            Assert.That(Series().Phase, Is.Not.EqualTo(RoundPhase.Complete));
            yield return AdvancePhase(Series().Phase == RoundPhase.Versus ? 3.21 : Series().Phase == RoundPhase.Result ? 3.41 : 1.31);
        }
        Assert.That(Series().Phase, Is.EqualTo(RoundPhase.Fight));
        yield return null; // Establish a fresh attack baseline after READY.
    }
    private void AssertCurrentPose(GameObject sword, bool guard)
    {
        int index = sword == firstSword ? 0 : 1;
        object presentation = Field<object>(master, "presentation");
        int character = (int)master.GetType().GetMethod("CharacterIndex").Invoke(master, new object[] { index });
        Vector3 chest = Field<Vector3[]>(master, "characterChestCenters")[character] + (Vector3)presentation.GetType().GetMethod("ChestOffset").Invoke(presentation, new object[] { index });
        var settings = new SwordPoseSettings(chest, Field<float>(master, "gripRadius"), Field<float>(master, "guardRadius"), Field<float>(master, "guardPlaneOffset"), Field<float>(master, "guardHeight"));
        object input = Field<object>(master, index == 0 ? "first" : "second");
        Quaternion inputRotation = (Quaternion)input.GetType().GetField("TargetRotation").GetValue(input);
        Vector3 last = (Vector3)input.GetType().GetField("LastGuardDirection").GetValue(input);
        // Height follows the input pitch; forward tilt remains bounded by the
        // guard limit and the grip's clearance in front of the chest.
        SwordPose expected = SwordPoseMath.Calculate(inputRotation, guard, settings, ref last);
        if (guard) expected = (SwordPose)input.GetType().GetField("TargetPose").GetValue(input);
        Assert.That(Vector3.Distance(sword.transform.localPosition, expected.Position), Is.LessThan(0.0001f));
        Assert.That(Quaternion.Angle(sword.transform.localRotation, expected.Rotation), Is.LessThan(0.01f));
        if (guard)
            Assert.That(Quaternion.Angle(expected.Rotation, inputRotation), Is.LessThan(.01f));
    }

    // Presentation/recoil fixtures establish a real blade interception instead
    // of relying on a guard toggle to cover an unrelated chest-centred cut.
    private void StartPhysicalGuardContact(int attacker, Vector3 cut, bool intercepts = true)
    {
        Vector3 grip = new Vector3(0f, .15f, .55f);
        Vector3 body = Vector3.forward * 2f;
        Vector3 guardBlade = new Vector3(-cut.y, cut.x, 0f).normalized;
        Vector3 guardGrip = body + Vector3.back * .55f + Vector3.up * .15f - guardBlade * .9f;
        if (!intercepts) guardGrip += Vector3.right * 4f;
        var attack = new CombatPose(grip, Vector3.up, grip, Vector3.forward,
            Vector3.zero, Vector3.forward, cut, grip, Vector3.forward);
        var defence = new CombatPose(guardGrip, guardBlade, guardGrip, guardBlade,
            body, Vector3.back, Vector3.zero, guardGrip, guardBlade);
        DuelMatch match = Field<DuelMatch>(master, "match");
        double now = MatchTime();
        var ready = new CombatInput(90f, 0f);
        match.Step(ready, ready, attack, defence, now);
        var strike = new CombatInput(40f, 120f);
        var held = new CombatInput(90f, 0f, true);
        CombatResult result = attacker == 0 ? match.Step(strike, held, attack, defence, now)
            : match.Step(held, strike, defence, attack, now);
        Assert.That(result.Outcome, Is.EqualTo(intercepts ? CombatOutcome.GuardSuccess : CombatOutcome.GuardBreak));
        object input = Field<object>(master, attacker == 0 ? "first" : "second");
        input.GetType().GetField("CutVelocity").SetValue(input, cut);
        InvokeMaster("BeginReaction", result, now);
    }

    private IEnumerator HoldUntil(Func<bool> condition, params Key[] keys)
    {
        SetKeys(keys);
        double deadline = Time.realtimeSinceStartupAsDouble + 4.0;
        do
        {
            yield return null;
            if (Time.realtimeSinceStartupAsDouble >= deadline)
            {
                object player = Field<object>(master, "first");
                Assert.Fail("Keyboard action timed out. Current keyboard=" + Keyboard.current?.deviceId
                    + ", synthetic keyboard=" + keyboard.deviceId + ", up=" + keyboard.upArrowKey.isPressed
                    + ", pitch=" + player.GetType().GetField("Pitch").GetValue(player)
                    + ", sword angle=" + Vector3.Angle(firstSword.transform.forward, firstPlayer.transform.forward)
                    + ", dt=" + Time.unscaledDeltaTime);
            }
        } while (!condition());
    }

    private void SetKeys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    private IEnumerator ResetInPlace()
    {
        SetKeys();
        // Let the queued release reach InputSystem before resetting; otherwise
        // LateUpdate can advance one blade with the previous held attack key.
        yield return null;
        master.GetType().GetMethod("ResetMatch").Invoke(master, null);
        yield return AdvanceToFight();
    }
    private static void AssertBladeColor(GameObject sword, Color expected)
    {
        var block = new MaterialPropertyBlock();
        Transform glove = sword.transform.Find("ChambaraGripGlove");
        foreach (Renderer renderer in sword.GetComponentsInChildren<Renderer>())
        {
            if (glove != null && renderer.transform.IsChildOf(glove)) continue;
            renderer.GetPropertyBlock(block);
            Color actual = block.GetColor(Shader.PropertyToID("_BaseColor"));
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f), renderer.name);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f), renderer.name);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f), renderer.name);
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f), renderer.name);
        }
    }
    private static Renderer[] BodyRenderers(GameObject player, GameObject sword)
        => player.GetComponentsInChildren<Renderer>()
            .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(sword.transform)).ToArray();
    private MatchWinner Winner() => (MatchWinner)master.GetType().GetProperty("Winner").GetValue(master);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
    private static MonoBehaviour FindComponent(string name) => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
        .Single(component => component.GetType().Name == name);
}
