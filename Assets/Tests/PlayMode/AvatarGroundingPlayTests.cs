using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// Uses the actual imported skeleton: a test rig would miss scale and bind-pose errors.
public sealed class AvatarGroundingPlayTests
{
    private MonoBehaviour master;
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
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
        master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c => c.GetType().Name == "MasterController");
        master.enabled = false;
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "GameFlowController") component.enabled = false;
    }
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub") Object.Destroy(component.gameObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator WalkingAndAttackerAdvanceVisiblyAlternateFeetAcrossTheWholeRoster()
    {
        object presentation = Field<object>(master, "presentation");
        int count = (int)master.GetType().GetProperty("CharacterCount").GetValue(master);
        for (int character = 0; character < count; character++)
        {
            Invoke(master, "SetCharacter", 0, character);
            object avatar = Field<Array>(presentation, "avatars").GetValue(0);
            Transform player = Field<Transform>(avatar, "player");
            Vector3 origin = player.position;
            Assert.That(Field<Transform>(avatar, "sword").Find("ChambaraGripGlove"), Is.Null);
            foreach (int fps in new[] { 30, 60, 144 })
                foreach (bool fast in new[] { false, true })
                {
                    player.position = origin; Invoke(avatar, "Reset");
                    Array legs = Field<Array>(avatar, "legs");
                    float ground = Field<Transform>(legs.GetValue(0), "Ankle").position.y;
                    float[] lift = new float[2]; bool[] moved = new bool[2];
                    int landings = 0;
                    // The attacker has no stagger: movement alone must drive
                    // a readable gait, including the fast shared combat push.
                    for (int frame = 0; frame < fps * 2; frame++)
                    {
                        float now = frame / (float)fps;
                        float distance = fast ? 3f * CombatAdvance.Blend(now, CombatAdvance.Duration(3f))
                            : Mathf.Min(now, 1f) * 2f;
                        player.position = origin + player.forward * distance;
                        Invoke(avatar, "Tick", (double)now, 1f / fps, Quaternion.Euler(-90f, 0f, 0f), false, 0f);
                        int inAir = 0;
                        for (int i = 0; i < 2; i++)
                        {
                            object leg = legs.GetValue(i);
                            if (Field<bool>(leg, "Moving")) { moved[i] = true; inAir++; }
                            lift[i] = Mathf.Max(lift[i], Field<Transform>(leg, "Ankle").position.y - ground);
                            Assert.That(Vector3.Distance(Field<Transform>(leg, "Ankle").position, Field<Vector3>(leg, "WorldPlant")),
                                Is.LessThan(.006f), $"Character {character}, {fps}fps, fast={fast}, frame {frame}: the actual foot must reach its animated contact without a stretched/floating leg. hip={Field<Transform>(leg, "Upper").position}, target={Field<Vector3>(leg, "WorldPlant")}, lengths={Field<float>(leg, "UpperLength")}/{Field<float>(leg, "LowerLength")}, moving={Field<bool>(leg, "Moving")}");
                        }
                        Assert.That(inAir, Is.LessThanOrEqualTo(1));
                        Assert.That(Vector3.Distance(Field<Vector3>(avatar, "renderedOrigin"), player.position), Is.LessThan(.001f));
                        if ((bool)avatar.GetType().GetProperty("Stepped").GetValue(avatar)) landings++;
                        if (character == 0 && fps == 60 && fast && (frame == 5 || frame == 13 || frame == 20 || frame == 28)
                            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR")))
                        {
                            yield return null;
                            CaptureReaction(avatar, "attacker-step-" + frame);
                        }
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        Assert.That(moved[i], Is.True, $"Character {character}, {fps}fps, fast={fast}: both feet must participate.");
                        Assert.That(lift[i], Is.GreaterThan(.05f), "The foot lift must be readable instead of sliding with the attacker.");
                    }
                    Assert.That(landings, Is.GreaterThanOrEqualTo(2));
                    Assert.That(Field<int>(avatar, "movingLeg"), Is.EqualTo(-1));
                }
            player.position = origin;
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator RepeatedFastPushesKeepTheVisibleRootWithTheCombatRoot()
    {
        Array avatars = Field<Array>(Field<object>(master, "presentation"), "avatars");
        object avatar = avatars.GetValue(0);
        Transform player = Field<Transform>(avatar, "player"), visual = Field<Transform>(avatar, "visual");
        Vector3 initial = player.position;
        Vector3 visualOffset = Field<Vector3>(avatar, "initialVisualPosition");
        Invoke(avatar, "Reset");
        for (int hit = 0; hit < 4; hit++)
        {
            Vector3 start = player.position;
            for (int frame = 1; frame <= 8; frame++)
            {
                player.position = start + Vector3.back * (3f * Mathf.SmoothStep(0f, 1f, frame / 8f));
                Invoke(avatar, "Tick", hit * .14 + frame / 60d, 1f / 60f,
                    Quaternion.Euler(-90f, 0f, 0f), false, 0f);
                Vector3 expected = player.TransformPoint(visualOffset)
                    + Field<Vector3>(avatar, "renderedOrigin") - player.position;
                Assert.That(Vector3.Distance(Field<Vector3>(avatar, "renderedOrigin"), player.position), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(visual.position, expected), Is.LessThanOrEqualTo(.1201f),
                    "The visible fighter must remain beside its combat root during repeated hits.");
            }
        }
        Assert.That(Vector3.Distance(player.position, initial), Is.GreaterThan(11.9f));
        yield return null;
    }

    [UnityTest]
    public IEnumerator KnockbackAndTurningKeepWorldContactsAndAlternateRecoverySteps()
    {
        Array avatars = Field<Array>(Field<object>(master, "presentation"), "avatars");
        for (int playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            object avatar = avatars.GetValue(playerIndex);
            Transform player = Field<Transform>(avatar, "player");
            Vector3 origin = player.position; Quaternion rotation = player.rotation;
            foreach (int fps in new[] { 30, 60, 144 })
                foreach (Vector3 direction in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
                {
                    player.SetPositionAndRotation(origin, rotation); Invoke(avatar, "Reset");
                    Array legs = Field<Array>(avatar, "legs");
                    Assert.That(legs.Length, Is.EqualTo(2));
                    float ground = Field<Transform>(legs.GetValue(0), "Ankle").position.y;
                    var result = new CombatResult(CombatOutcome.Hit, 1 - playerIndex, playerIndex, 3f);
                    Invoke(avatar, "React", result, playerIndex, 0.0, direction * 3f);
                    int contacts = 0, plantedFrames = 0, liftedFrames = 0;
                    Vector3 previousPlayerPosition = origin;
                    for (int frame = 0; frame < fps * 2; frame++)
                    {
                        float time = frame / (float)fps;
                        float push = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - result.HitStopSeconds) / DuelMatch.PushSeconds));
                        player.position = origin + direction * (3f * push);
                        Vector3 rootDelta = player.position - previousPlayerPosition;
                        float rootSpeed = rootDelta.magnitude * fps;
                        previousPlayerPosition = player.position;
                        player.rotation = rotation * Quaternion.Euler(0f, 20f * push, 0f);
                        Vector3[] before = new Vector3[2]; bool[] wasMoving = new bool[2];
                        for (int i = 0; i < 2; i++)
                        { before[i] = Field<Vector3>(legs.GetValue(i), "WorldPlant"); wasMoving[i] = Field<bool>(legs.GetValue(i), "Moving"); }
                        Invoke(avatar, "Tick", (double)time, 1f / fps, Quaternion.Euler(-90f, 0f, 0f), false, 0f);
                        Transform visual = Field<Transform>(avatar, "visual");
                        Vector3 visualBase = player.TransformPoint(Field<Vector3>(avatar, "initialVisualPosition"))
                            + Field<Vector3>(avatar, "renderedOrigin") - player.position;
                        Assert.That(Vector3.Distance(visual.position, visualBase), Is.LessThanOrEqualTo(.1201f),
                            "Pelvis correction must stay close to the hitbox through a fast push at " + fps + " FPS");
                        int moving = 0;
                        for (int i = 0; i < 2; i++)
                        {
                            object leg = legs.GetValue(i);
                            bool inAir = Field<bool>(leg, "Moving");
                            if (inAir) { moving++; liftedFrames++; }
                            else if (!wasMoving[i])
                            {
                                plantedFrames++;
                                Vector3 expectedPlant = before[i] + (rootSpeed > 12f ? rootDelta : Vector3.zero);
                                Assert.That(Vector3.Distance(Field<Vector3>(leg, "WorldPlant"), expectedPlant), Is.LessThan(0.00001f),
                                    "Only a fast recoil may carry the contact anchor with the combat root");
                                if (rootSpeed <= 12f)
                                    Assert.That(Vector3.Distance(Field<Transform>(leg, "Ankle").position, before[i]), Is.LessThan(0.003f), "Ordinary support slid outside its world contact at " + fps + " FPS");
                            }
                            Assert.That(Field<Transform>(leg, "Ankle").position.y, Is.GreaterThanOrEqualTo(ground - 0.003f), "Foot penetrated the floor");
                            Vector3 hip = Field<Transform>(leg, "Upper").position;
                            Vector3 knee = Field<Transform>(leg, "Lower").position;
                            Vector3 ankle = Field<Transform>(leg, "Ankle").position;
                            Vector3 bend = Vector3.ProjectOnPlane(knee - hip, ankle - hip);
                            Assert.That(Vector3.Dot(bend, player.forward), Is.GreaterThanOrEqualTo(-0.001f), "Knee bent backwards");
                        }
                        Assert.That(moving, Is.LessThanOrEqualTo(1), "Both feet must not step together");
                        if ((bool)avatar.GetType().GetProperty("Stepped").GetValue(avatar)) contacts++;
                    }
                    Assert.That(contacts, Is.GreaterThanOrEqualTo(2));
                    Assert.That(plantedFrames, Is.GreaterThan(fps));
                    Assert.That(liftedFrames, Is.GreaterThan(0));
                    Assert.That(Field<int>(avatar, "movingLeg"), Is.EqualTo(-1), "Recovery must settle");
                    Assert.That(Vector3.Distance(Field<Vector3>(avatar, "renderedOrigin"), player.position), Is.LessThan(0.001f));
                    // Reset/retry must move contacts to the new spawn instead of leaving them at the old hit location.
                    player.SetPositionAndRotation(origin, rotation); Invoke(avatar, "Reset");
                    foreach (object leg in legs)
                        Assert.That(Vector3.Distance(Field<Vector3>(leg, "WorldPlant"), Field<Transform>(leg, "Ankle").position), Is.LessThan(0.001f));
                }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator DirectionalStaggerKeepsOneWorldSupportAndRecoversWithoutMovingCombatOrigin()
    {
        Array avatars = Field<Array>(Field<object>(master, "presentation"), "avatars");
        foreach (object avatar in avatars)
            foreach (int fps in new[] { 30, 60, 144 })
                foreach (Vector3 cut in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
                    Vector3.up, Vector3.down, (Vector3.right + Vector3.down).normalized, (Vector3.left + Vector3.up).normalized })
                {
                    Invoke(avatar, "Reset");
                    Transform player = Field<Transform>(avatar, "player"), hip = Field<Transform>(avatar, "hips"), head = Field<Transform>(avatar, "head");
                    Array legs = Field<Array>(avatar, "legs");
                    Vector3 origin = player.position, upright = (head.position - hip.position).normalized;
                    float floor = Field<Transform>(legs.GetValue(0), "Ankle").position.y;
                    Vector3 stumble = Vector3.ProjectOnPlane(cut, player.up);
                    if (stumble.sqrMagnitude < .01f) stumble = player.forward * (Vector3.Dot(cut, player.up) < 0f ? 1f : -1f);
                    Invoke(avatar, "Tick", 0.0, 1f / fps, Quaternion.identity, false, 0f);
                    Transform chest = Field<Transform>(avatar, "chest");
                    Quaternion neutralChest = chest.localRotation;
                    SwordPose neutralSword = PoseReactionSword(avatar, Quaternion.identity, 0.0);
                    Invoke(avatar, "Stagger", cut, 0.0, CombatReaction.RecoverySeconds, DuelMatch.HitStopSeconds);
                    bool directionalLean = false, recoveryStep = false;
                    float greatestChestLean = 0f, greatestBodyTravel = 0f, greatestSwordLean = 0f;
                    for (int frame = 0; frame < fps * 2; frame++)
                    {
                        Vector3[] before = legs.Cast<object>().Select(leg => Field<Vector3>(leg, "WorldPlant")).ToArray();
                        bool[] wasMoving = legs.Cast<object>().Select(leg => Field<bool>(leg, "Moving")).ToArray();
                        double time = frame / (double)fps;
                        SwordPose swordPose = PoseReactionSword(avatar, Quaternion.identity, time);
                        if (time < DuelMatch.HitStopSeconds)
                        {
                            Assert.That(Vector3.Distance(swordPose.Position, neutralSword.Position), Is.LessThan(.000001f), "The struck grip must freeze throughout hit stop, allowing only transform round-trip precision.");
                            Assert.That(Quaternion.Angle(swordPose.Rotation, neutralSword.Rotation), Is.LessThan(.001f));
                        }
                        Invoke(avatar, "Tick", time, 1f / fps, Quaternion.identity, false, 0f);
                        Assert.That(player.position, Is.EqualTo(origin), "Stagger must not move the combat origin");
                        if (frame == 0 || frame == Mathf.RoundToInt((DuelMatch.HitStopSeconds + CombatReaction.RecoverySeconds * .16f) * fps)
                            || frame == fps)
                        {
                            float pitch = (float)avatar.GetType().GetMethod("ReactionPitch").Invoke(avatar, new object[] { time });
                            float expectedPitch = ReactionSwordPose.Sample(player.InverseTransformDirection(cut),
                                (float)time - DuelMatch.HitStopSeconds, CombatReaction.RecoverySeconds, false).ChestAngles.x;
                            Assert.That(pitch, Is.EqualTo(expectedPitch).Within(.001f), "Vertical impacts must expose the same timed chest pitch to the camera.");
                        }
                        int moving = 0;
                        for (int i = 0; i < legs.Length; i++)
                        {
                            object leg = legs.GetValue(i); bool inAir = Field<bool>(leg, "Moving");
                            if (inAir) { moving++; recoveryStep = true; }
                            else if (!wasMoving[i])
                                Assert.That(Vector3.Distance(Field<Transform>(leg, "Ankle").position, before[i]), Is.LessThan(.003f), "Supporting sole slid during a directional stagger");
                            Assert.That(Field<Transform>(leg, "Ankle").position.y, Is.GreaterThanOrEqualTo(floor - .003f));
                        }
                        Assert.That(moving, Is.LessThanOrEqualTo(1));
                        Vector3 lean = Vector3.ProjectOnPlane((head.position - hip.position).normalized - upright, player.up);
                        directionalLean |= Vector3.Dot(lean, stumble.normalized) > .05f;
                        greatestChestLean = Mathf.Max(greatestChestLean, Quaternion.Angle(neutralChest, chest.localRotation));
                        greatestSwordLean = Mathf.Max(greatestSwordLean, Quaternion.Angle(neutralSword.Rotation, swordPose.Rotation));
                        greatestBodyTravel = Mathf.Max(greatestBodyTravel, Vector3.Distance(Field<Vector3>(avatar, "renderedOrigin"), origin));
                        float age = (float)time - DuelMatch.HitStopSeconds;
                        if (age > .1f && age < .2f) AssertRecoveryGrip(avatar);
                        if (avatar == avatars.GetValue(0) && fps == 60 && frame == 12)
                        {
                            string direction = cut == Vector3.right ? "right" : cut == Vector3.left ? "left"
                                : cut == Vector3.up ? "up" : cut == Vector3.down ? "down" : null;
                            if (direction != null && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR")))
                            {
                                // Let Unity refresh the skin between poses captured in this same test.
                                yield return null;
                                CaptureReaction(avatar, "stagger-" + direction, Field<Transform>(avatar, "sword"));
                            }
                        }
                    }
                    Assert.That(directionalLean, Is.True, "Body did not lean in the cut direction: " + cut);
                    Assert.That(greatestChestLean, Is.GreaterThan(25f), "The upper body should visibly fold away from the cut");
                    Assert.That(greatestSwordLean, Is.GreaterThan(15f), "The sword must stumble with the body instead of remaining forward.");
                    Assert.That(greatestBodyTravel, Is.InRange(.25f, .271f), "A stronger hit pose must keep the existing body travel");
                    Assert.That(recoveryStep, Is.True, "Stagger should produce a visible recovery step");
                    Assert.That(Field<int>(avatar, "movingLeg"), Is.EqualTo(-1));
                    Assert.That(Vector3.Distance(Field<Vector3>(avatar, "renderedOrigin"), origin), Is.LessThan(.001f));
                }
        yield return null;
    }

    [UnityTest]
    public IEnumerator ABlockedAttackerLosesBalanceWithTheExistingPushAndPlantedRecoveryFeet()
    {
        object presentation = Field<object>(master, "presentation"), effects = Field<object>(presentation, "effects");
        Array avatars = Field<Array>(presentation, "avatars");
        MethodInfo play = presentation.GetType().GetMethod("Play", new[] { typeof(CombatResult), typeof(double), typeof(Vector3), typeof(Vector3) });
        for (int playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            object avatar = avatars.GetValue(playerIndex);
            Transform player = Field<Transform>(avatar, "player"), chest = Field<Transform>(avatar, "chest");
            Vector3 origin = player.position;
            foreach (int fps in new[] { 30, 60, 144 })
            {
                player.position = origin;
                foreach (object character in avatars) Invoke(character, "Reset");
                Invoke(effects, "ResetEffects");
                Invoke(avatar, "Tick", -.01, 1f / fps, Quaternion.identity, false, 0f);
                Quaternion neutralChest = chest.localRotation;
                SwordPose neutralSword = PoseReactionSword(avatar, Quaternion.identity, -.01);
                Array legs = Field<Array>(avatar, "legs"), arms = Field<Array>(avatar, "arms");
                Quaternion[] neutralArms = arms.Cast<object>().Select(arm => Field<Transform>(arm, "Upper").localRotation).ToArray();
                float floor = Field<Transform>(legs.GetValue(0), "Ankle").position.y;
                Vector3 pushDirection = -player.forward;
                var result = new CombatResult(CombatOutcome.GuardSuccess, playerIndex, playerIndex, -DuelMatch.GuardPush);
                play.Invoke(presentation, new object[] { result, 0.0, pushDirection * DuelMatch.GuardPush, player.right });
                Assert.That(Field<double>(avatars.GetValue(1 - playerIndex), "guardRecoilStart"), Is.LessThan(0), "The defender kept their balance");
                float greatestChestLean = 0f, greatestArmReach = 0f, greatestSwordLean = 0f;
                bool recoveryStep = false;
                Vector3 previousPlayerPosition = origin;
                for (int frame = 0; frame < fps * 2; frame++)
                {
                    double time = frame / (double)fps;
                    float push = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(((float)time - result.HitStopSeconds) / DuelMatch.PushSeconds));
                    Vector3 suppliedOrigin = origin + pushDirection * (DuelMatch.GuardPush * push);
                    player.position = suppliedOrigin;
                    Vector3 rootDelta = suppliedOrigin - previousPlayerPosition;
                    float rootSpeed = rootDelta.magnitude * fps;
                    previousPlayerPosition = suppliedOrigin;
                    Vector3[] supports = legs.Cast<object>().Select(leg => Field<Vector3>(leg, "WorldPlant")).ToArray();
                    bool[] wasMoving = legs.Cast<object>().Select(leg => Field<bool>(leg, "Moving")).ToArray();
                    SwordPose swordPose = PoseReactionSword(avatar, Quaternion.identity, time);
                    if (time < result.HitStopSeconds)
                    {
                        Assert.That(Vector3.Distance(swordPose.Position, neutralSword.Position), Is.LessThan(.000001f), "The blocked grip must freeze throughout hit stop, allowing only transform round-trip precision.");
                        Assert.That(Quaternion.Angle(swordPose.Rotation, neutralSword.Rotation), Is.LessThan(.001f));
                    }
                    Invoke(avatar, "Tick", time, 1f / fps, Quaternion.identity, false, 0f);
                    Invoke(effects, "Tick", time, false);
                    Assert.That(player.position, Is.EqualTo(suppliedOrigin), "Guard recoil added combat push");
                    Assert.That(Vector3.Dot(Field<Vector3>(avatar, "renderedOrigin") - origin, pushDirection),
                        Is.InRange(-.001f, DuelMatch.GuardPush + .001f), "Guard recoil added visual travel beyond the existing push");
                    greatestChestLean = Mathf.Max(greatestChestLean, Quaternion.Angle(neutralChest, chest.localRotation));
                    greatestSwordLean = Mathf.Max(greatestSwordLean, Quaternion.Angle(neutralSword.Rotation, swordPose.Rotation));
                    float age = (float)time - result.HitStopSeconds;
                    if (age > ReactionSwordPose.GuardDurationSeconds * .4f && age < ReactionSwordPose.GuardDurationSeconds * .6f) AssertRecoveryGrip(avatar);
                    for (int armIndex = 0; armIndex < arms.Length; armIndex++)
                        greatestArmReach = Mathf.Max(greatestArmReach,
                            Quaternion.Angle(neutralArms[armIndex], Field<Transform>(arms.GetValue(armIndex), "Upper").localRotation));
                    int moving = 0;
                    for (int i = 0; i < legs.Length; i++)
                    {
                        object leg = legs.GetValue(i);
                        if (Field<bool>(leg, "Moving")) { moving++; recoveryStep = true; }
                        else if (!wasMoving[i])
                        {
                            Vector3 expectedSupport = supports[i] + (rootSpeed > 12f ? rootDelta : Vector3.zero);
                            Assert.That(Vector3.Distance(Field<Vector3>(leg, "WorldPlant"), expectedSupport), Is.LessThan(.00001f),
                                "Only fast recoil may carry a planted contact");
                            Assert.That(Vector3.Distance(Field<Transform>(leg, "Ankle").position, expectedSupport), Is.LessThan(.003f),
                                "Guard recoil foot must stay at its coherent contact");
                        }
                        Assert.That(Field<Transform>(leg, "Ankle").position.y, Is.GreaterThanOrEqualTo(floor - .003f));
                    }
                    Assert.That(moving, Is.LessThanOrEqualTo(1));
                    int marks = (int)effects.GetType().GetProperty("ActiveStaggerCount").GetValue(effects);
                    Assert.That(marks, Is.EqualTo(time < result.HitStopSeconds + ReactionSwordPose.GuardDurationSeconds ? 1 : 0), "The blocked attacker's ! must end with the guard reaction");
                    if (playerIndex == 0 && fps == 60 && (frame == 12 || frame == 24)
                        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR")))
                    {
                        yield return null;
                        CaptureReaction(avatar, frame == 12 ? "guard-recoil-peak" : "guard-recoil-return", Field<Transform>(avatar, "sword"));
                    }
                }
                Assert.That(greatestChestLean, Is.GreaterThan(30f), "A blocked attacker should visibly lose their upper-body balance");
                Assert.That(greatestSwordLean, Is.GreaterThan(40f), "The blocked sword must rebound with the upper body.");
                Assert.That(greatestArmReach, Is.GreaterThan(25f), "The arms should follow the rebounding sword to recover balance");
                Assert.That(recoveryStep, Is.True);
                Assert.That(Field<int>(avatar, "movingLeg"), Is.EqualTo(-1));
                Assert.That(Vector3.Distance(Field<Vector3>(avatar, "renderedOrigin"), player.position), Is.LessThan(.001f));
                for (int armIndex = 0; armIndex < arms.Length; armIndex++)
                    Assert.That(Quaternion.Angle(neutralArms[armIndex], Field<Transform>(arms.GetValue(armIndex), "Upper").localRotation), Is.LessThan(.1f), "The arm pose must settle without accumulating rotations");
            }
            player.position = origin; Invoke(avatar, "Reset");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator SurpriseMarkFollowsTheActualHeadFacesEachViewAndEndsWithRecovery()
    {
        object presentation = Field<object>(master, "presentation");
        object avatar = Field<Array>(presentation, "avatars").GetValue(0), effects = Field<object>(presentation, "effects");
        Transform player = Field<Transform>(avatar, "player");
        Invoke(avatar, "Reset"); Invoke(effects, "ResetEffects");
        Invoke(presentation, "StaggerCharacter", 0, player.right, 0.0, CombatReaction.RecoverySeconds, DuelMatch.HitStopSeconds);
        Assert.That((int)effects.GetType().GetProperty("ActiveStaggerCount").GetValue(effects), Is.EqualTo(1));
        for (int frame = 0; frame < 15; frame++)
            Invoke(avatar, "Tick", frame / 60.0, 1f / 60f, Quaternion.identity, false, 0f);
        Invoke(effects, "Tick", .24, false);
        object mark = Field<Array>(effects, "staggerMarks").Cast<object>().Single(item => Field<bool>(item, "Active"));
        Mesh mesh = Field<Mesh>(mark, "Mesh");
        Vector3 head = (Vector3)avatar.GetType().GetProperty("HeadPosition").GetValue(avatar);
        Assert.That(mesh.bounds.center.y - head.y, Is.InRange(.25f, .45f));
        Assert.That(Vector3.ProjectOnPlane(mesh.bounds.center - head, Vector3.up).magnitude, Is.LessThan(.01f));
        Camera camera = new GameObject("Surprise mark view test").AddComponent<Camera>(); camera.enabled = false;
        try
        {
            foreach (Quaternion rotation in new[] { Quaternion.Euler(12f, 35f, 0f), Quaternion.Euler(-8f, 215f, 0f) })
            {
                camera.transform.rotation = rotation;
                effects.GetType().GetMethod("FaceStaggerMarks", Fields).Invoke(effects, new object[] { default(ScriptableRenderContext), camera });
                Vector3[] vertices = mesh.vertices;
                Vector3 normal = Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]).normalized;
                Assert.That(Mathf.Abs(Vector3.Dot(normal, camera.transform.forward)), Is.GreaterThan(.999f));
            }
        }
        finally { Object.Destroy(camera.gameObject); }
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR")))
        {
            yield return null;
            CaptureReaction(avatar, "stagger-surprise-mark");
        }
        Invoke(effects, "Tick", (double)(DuelMatch.HitStopSeconds + CombatReaction.RecoverySeconds + .01f), false);
        Assert.That((int)effects.GetType().GetProperty("ActiveStaggerCount").GetValue(effects), Is.EqualTo(0));
        Invoke(presentation, "ShowStagger", avatar, 1.0, CombatReaction.RecoverySeconds, DuelMatch.HitStopSeconds);
        Invoke(effects, "ResetEffects");
        Assert.That((int)effects.GetType().GetProperty("ActiveStaggerCount").GetValue(effects), Is.EqualTo(0));
        yield return null;
    }

    [UnityTest]
    public IEnumerator GuardRemainsReadableDuringTheSharedOneSecondRecoveryAndCameraRollEndsWithIt()
    {
        Assert.That(CombatReaction.RecoverySeconds, Is.EqualTo(1f));
        Assert.That(ReactionSwordPose.GuardDurationSeconds, Is.EqualTo(CombatReaction.RecoverySeconds));
        object presentation = Field<object>(master, "presentation");
        foreach (object avatar in Field<Array>(presentation, "avatars"))
            foreach (bool blocked in new[] { false, true })
            {
                Invoke(avatar, "Reset");
                Transform player = Field<Transform>(avatar, "player"), sword = Field<Transform>(avatar, "sword");
                Vector3 origin = player.position;
                Invoke(avatar, "Tick", -.01d, 1f / 60f, Quaternion.identity, false, 0f);
                SwordPose captured = PoseReactionSword(avatar, Quaternion.identity, -.01d);
                float stop = blocked ? DuelMatch.GuardStopSeconds : DuelMatch.HitStopSeconds;
                if (blocked) Invoke(avatar, "GuardRecoil", -player.forward + player.right, 0d, stop);
                else Invoke(avatar, "Stagger", player.right, 0d, CombatReaction.RecoverySeconds, stop);
                Vector3 remembered = Vector3.up;
                var settings = new SwordPoseSettings(Field<Vector3>(avatar, "restChest"));
                float greatestRoll = 0f;
                for (int frame = 0; frame <= 72; frame++)
                {
                    double now = frame / 60d;
                    bool guard = now >= .15d;
                    SwordPose target = SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, Vector3.up), guard, settings, ref remembered);
                    SwordPose visible = (SwordPose)avatar.GetType().GetMethod("PoseSword", new[] { typeof(SwordPose), typeof(double), typeof(bool) })
                        .Invoke(avatar, new object[] { target, now, guard });
                    sword.SetLocalPositionAndRotation(visible.Position, visible.Rotation);
                    Invoke(avatar, "Tick", now, 1f / 60f, Quaternion.identity, guard, 0f);
                    float roll = (float)avatar.GetType().GetMethod("ReactionRoll").Invoke(avatar, new object[] { now });
                    greatestRoll = Mathf.Max(greatestRoll, Mathf.Abs(roll));
                    if (now < stop)
                    {
                        Assert.That(Vector3.Distance(captured.Position, visible.Position), Is.LessThan(.000001f));
                        Assert.That(Quaternion.Angle(captured.Rotation, visible.Rotation), Is.LessThan(.06f));
                        Assert.That(roll, Is.EqualTo(0f));
                    }
                    if (guard && now >= .15d + GuardPoseTransition.DurationSeconds && now < stop + CombatReaction.RecoverySeconds)
                    {
                        Assert.That(Vector3.Angle(target.Direction, visible.Direction), Is.LessThan(15f), "A recovering guard must show its defence direction after the shared smooth transition.");
                    }
                    Vector3 reactionDirection = blocked ? (-Vector3.forward + Vector3.right).normalized : Vector3.right;
                    if (now >= .27d && ReactionSwordPose.Sample(reactionDirection, (float)now - stop,
                        CombatReaction.RecoverySeconds, blocked).Impact > .72f) AssertRecoveryGrip(avatar);
                    if (now >= stop + CombatReaction.RecoverySeconds)
                    {
                        Assert.That(roll, Is.EqualTo(0f), "The horizon stops following the body when recovery ends.");
                        Assert.That(Vector3.Distance(target.Position, visible.Position), Is.LessThan(.00001f));
                        Assert.That(Quaternion.Angle(target.Rotation, visible.Rotation), Is.LessThan(.06f));
                    }
                    Assert.That(player.position, Is.EqualTo(origin), "Guarding during recovery must not add combat displacement.");
                }
                Assert.That(greatestRoll, Is.GreaterThan(15f));
            }
        yield return null;
    }

    [UnityTest]
    public IEnumerator GuardAfterAHitOrBlockedAttackBlendsFromTheVisibleSwordWithoutCancellingBodyRecovery()
    {
        object presentation = Field<object>(master, "presentation");
        foreach (object avatar in Field<Array>(presentation, "avatars"))
            foreach (bool blocked in new[] { false, true })
            {
                Invoke(avatar, "Reset");
                Transform player = Field<Transform>(avatar, "player"), chest = Field<Transform>(avatar, "chest");
                Transform sword = Field<Transform>(avatar, "sword");
                Vector3 origin = player.position;
                Invoke(avatar, "Tick", -.01d, 1f / 60f, Quaternion.identity, false, 0f);
                Quaternion restChest = chest.localRotation;
                SwordPose captured = PoseReactionSword(avatar, Quaternion.identity, -.01d);
                float stop = blocked ? DuelMatch.GuardStopSeconds : DuelMatch.HitStopSeconds;
                if (blocked) Invoke(avatar, "GuardRecoil", -player.forward + player.right, 0d, stop);
                else Invoke(avatar, "Stagger", player.right, 0d, CombatReaction.RecoverySeconds, stop);
                Vector3 remembered = Vector3.up;
                SwordPose defence = SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, Vector3.up),
                    true, new SwordPoseSettings(Field<Vector3>(avatar, "restChest")), ref remembered);
                MethodInfo pose = avatar.GetType().GetMethod("PoseSword", new[] { typeof(SwordPose), typeof(double), typeof(bool) });
                SwordPose immediate = (SwordPose)pose.Invoke(avatar, new object[] { defence, .01d, true });
                Assert.That(Vector3.Distance(immediate.Position, captured.Position), Is.LessThan(.00001f));
                Assert.That(Quaternion.Angle(immediate.Rotation, captured.Rotation), Is.LessThan(.06f),
                    "Pressing guard must start at the struck sword, not teleport to the guard target.");
                Assert.That(chest.localRotation, Is.EqualTo(restChest));
                SwordPose previous = immediate;
                for (int frame = 1; frame <= 9; frame++)
                {
                    double time = .01d + frame / 60d;
                    SwordPose blending = (SwordPose)pose.Invoke(avatar, new object[] { defence, time, true });
                    Assert.That(Vector3.Distance(previous.Position, blending.Position), Is.LessThan(.5f));
                    Assert.That(Quaternion.Angle(previous.Rotation, blending.Rotation), Is.LessThan(35f));
                    previous = blending;
                }
                double peak = stop + CombatReaction.RecoverySeconds * (blocked ? .5d : .16d);
                SwordPose recovering = (SwordPose)pose.Invoke(avatar, new object[] { defence, peak, true });
                sword.SetLocalPositionAndRotation(recovering.Position, recovering.Rotation);
                Invoke(avatar, "Tick", peak, 1f / 60f, Quaternion.identity, true, 0f);
                Assert.That(Vector3.Angle(defence.Direction, recovering.Direction), Is.LessThan(15f));
                Assert.That(Quaternion.Angle(restChest, chest.localRotation), Is.GreaterThan(25f), "Blended defence must retain the body recoil.");
                AssertRecoveryGrip(avatar);
                Assert.That(player.position, Is.EqualTo(origin));
                double recoveredAt = stop + CombatReaction.RecoverySeconds + .001d;
                SwordPose recovered = (SwordPose)pose.Invoke(avatar, new object[] { defence, recoveredAt, true });
                Assert.That(Vector3.Distance(recovered.Position, defence.Position), Is.LessThan(.00001f));
                Assert.That(Quaternion.Angle(recovered.Rotation, defence.Rotation), Is.LessThan(.06f));
            }
        yield return null;
    }

    [UnityTest]
    public IEnumerator RecoveryGuardsKeepTheirFiniteBladesOutsideEveryCharactersActualTorso()
    {
        object presentation = Field<object>(master, "presentation");
        for (int character = 0; character < 4; character++)
        {
            Invoke(master, "SetCharacter", 0, character);
            object avatar = Field<Array>(presentation, "avatars").GetValue(0);
            Transform player = Field<Transform>(avatar, "player"), sword = Field<Transform>(avatar, "sword");
            Transform visual = Field<Transform>(avatar, "visual");
            Vector3 origin = player.position;
            Vector3 anchor = Field<Vector3[]>(master, "characterChestCenters")[character];
            MethodInfo poseSword = avatar.GetType().GetMethod("PoseSword", new[] { typeof(SwordPose), typeof(double), typeof(bool) });
            foreach (int fps in new[] { 30, 60, 144 })
                foreach (bool blocked in new[] { false, true })
                    for (int octant = 0; octant < 8; octant++)
                    {
                        Invoke(avatar, "Reset");
                        Vector3 remembered = Vector3.up;
                        Quaternion input = Quaternion.FromToRotation(Vector3.forward, ShowdownCombat.ReadyDirection(octant));
                        Invoke(avatar, "Tick", -.01d, 1f / fps, input, true, 0f);
                        SwordPose neutral = SwordPoseMath.Calculate(input, true,
                            new SwordPoseSettings(anchor + (Vector3)avatar.GetType().GetProperty("ChestOffset").GetValue(avatar)), ref remembered);
                        sword.SetLocalPositionAndRotation(neutral.Position, neutral.Rotation);
                        float stop = blocked ? DuelMatch.GuardStopSeconds : DuelMatch.HitStopSeconds;
                        if (blocked) Invoke(avatar, "GuardRecoil", -player.forward, 0d, stop);
                        else Invoke(avatar, "Stagger", -player.forward, 0d, CombatReaction.RecoverySeconds, stop);
                        // A downward guard and a backward lean previously sent
                        // the visible finite blade directly through the chest.
                        int inspectFrame = Mathf.RoundToInt((stop + CombatReaction.RecoverySeconds * (blocked ? .5f : .22f)) * fps);
                        for (int frame = 0; frame <= Mathf.CeilToInt((stop + CombatReaction.RecoverySeconds + .05f) * fps); frame++)
                        {
                            double now = frame / (double)fps;
                            Vector3 offset = (Vector3)avatar.GetType().GetProperty("ChestOffset").GetValue(avatar);
                            SwordPose logical = SwordPoseMath.Calculate(input, true, new SwordPoseSettings(anchor + offset), ref remembered);
                            sword.SetLocalPositionAndRotation(logical.Position, logical.Rotation);
                            if (now >= stop) Invoke(avatar, "Tick", now, 1f / fps, input, true, 0f);
                            SwordPose visible = (SwordPose)poseSword.Invoke(avatar, new object[] { logical, now, true });
                            sword.SetLocalPositionAndRotation(visible.Position, visible.Rotation);
                            Vector3 currentChest = anchor + (Vector3)avatar.GetType().GetProperty("ChestOffset").GetValue(avatar);
                            Vector3 start = visible.Position + visible.Direction * CombatContact.BladeStart;
                            Vector3 end = visible.Position + visible.Direction * CombatContact.BladeLength;
                            Assert.That(CombatContact.PointSegmentDistanceSquared(currentChest, start, end),
                                Is.GreaterThan(CombatContact.BodyRadius * CombatContact.BodyRadius),
                                $"Guard blade entered character {character}'s chest at {fps}fps, guard {octant}, blocked={blocked}, time={now:F3}.");
                            if (frame == inspectFrame)
                            {
                                BodySurface[] torso = BakeBodySurfaces(visual, player, true);
                                Vector3 side = Vector3.Cross(visible.Direction, Vector3.up).normalized;
                                foreach (Vector3 padding in new[] { Vector3.zero, Vector3.up * .06f,
                                    Vector3.down * .06f, side * .06f, -side * .06f })
                                    Assert.That(BladeCrossesModel(torso, start + padding, end + padding), Is.False,
                                        $"Guard blade crossed character {character}'s imported torso at {fps}fps, guard {octant}, blocked={blocked}.");
                            }
                            Assert.That(player.position, Is.EqualTo(origin), "Keeping the guard blade outside the body must not add combat push.");
                        }
                    }
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator SidewaysSwordMotionDoesNotTriggerAForwardStep()
    {
        Array avatars = Field<Array>(Field<object>(master, "presentation"), "avatars");
        foreach (object avatar in avatars)
        {
            Invoke(avatar, "Reset");
            Array legs = Field<Array>(avatar, "legs");
            Vector3[] plants = legs.Cast<object>().Select(leg => Field<Transform>(leg, "Ankle").position).ToArray();
            for (int frame = 0; frame < 120; frame++)
            {
                Quaternion sword = Quaternion.AngleAxis(Mathf.Min(90f, frame * 3f), Vector3.forward) * Quaternion.Euler(-90f, 0f, 0f);
                Invoke(avatar, "Tick", frame / 60.0, 1f / 60f, sword, false, 180f);
                Assert.That((bool)avatar.GetType().GetProperty("Stepped").GetValue(avatar), Is.False);
                for (int i = 0; i < legs.Length; i++)
                    Assert.That(Vector3.Distance(Field<Transform>(legs.GetValue(i), "Ankle").position, plants[i]), Is.LessThan(0.001f));
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator AllCharactersFallOntoTheFloorBeforeExitingAndResetTheirScale()
    {
        object presentation = Field<object>(master, "presentation");
        for (int character = 0; character < 4; character++)
        {
            Invoke(master, "SetCharacter", 0, character);
            object avatar = Field<Array>(presentation, "avatars").GetValue(0);
            Transform player = Field<Transform>(avatar, "player"), visual = Field<Transform>(avatar, "visual");
            Vector3 origin = player.position, scale = visual.localScale;
            Assert.That(MeshBounds(visual).size.y, Is.InRange(1.5f, 3f), "Imported model size must be measured in metres");
            Invoke(avatar, "Defeat", 0.0);
            for (int frame = 0; frame <= 72; frame++)
            {
                Invoke(avatar, "Tick", frame / 32.0, 1f / 32f, Quaternion.identity, false, 0f);
                Bounds body = MeshBounds(visual);
                Assert.That(body.min.y, Is.GreaterThanOrEqualTo(-.003f), "Defeated character sank into the course: " + character);
                Assert.That(player.position, Is.EqualTo(origin), "Defeat moved the combat origin");
                if (frame == 22)
                {
                    Assert.That(body.size.y, Is.LessThan(1f), "The opponent should be lying down before exiting");
                    CaptureDefeatedCharacter(player, body, character);
                }
            }
            Assert.That(visual.localScale.magnitude, Is.LessThan(scale.magnitude * .1f));
            Invoke(avatar, "Reset");
            Assert.That(visual.localScale, Is.EqualTo(scale));
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator EveryCharacterHasNoGripGlovesAndRearBladeClearsTheActualHead()
    {
        object presentation = Field<object>(master, "presentation");
        for (int character = 0; character < 4; character++)
            for (int color = 0; color < 6; color++)
            {
                Invoke(master, "SetCharacter", 0, character); Invoke(master, "SetColor", 0, color);
                object avatar = Field<Array>(presentation, "avatars").GetValue(0);
                Invoke(avatar, "Reset");
                Transform player = Field<Transform>(avatar, "player"), visual = Field<Transform>(avatar, "visual");
                Transform sword = Field<Transform[]>(presentation, "swords")[0];
                Assert.That(sword.Find("ChambaraGripGlove"), Is.Null, "Grip gloves are disabled for every character and weapon.");
                float headTop = MeshBounds(visual).max.y - player.position.y;
                Vector3 chest = Field<Vector3[]>(master, "characterChestCenters")[character];
                foreach (float pitch in new[] { -45f, -60f })
                {
                    Vector3 last = Vector3.up;
                    SwordPose pose = SwordPoseMath.Calculate(Quaternion.Euler(pitch, 180f, 0f), false,
                        new SwordPoseSettings(chest), ref last);
                    float acrossHead = -pose.Position.z / pose.Direction.z;
                    Assert.That((pose.Position + acrossHead * pose.Direction).y, Is.GreaterThan(headTop + .08f),
                        "Rear blade crossed the actual head for character " + character);
                }
                yield return null;
            }
    }

    [UnityTest]
    public IEnumerator SideToRearAndOverheadTransitionsDoNotCrossAnyActualCharacterMesh()
    {
        object presentation = Field<object>(master, "presentation");
        for (int character = 0; character < 4; character++)
        {
            Invoke(master, "SetCharacter", 0, character);
            object avatar = Field<Array>(presentation, "avatars").GetValue(0);
            Invoke(avatar, "Reset");
            Transform player = Field<Transform>(avatar, "player"), visual = Field<Transform>(avatar, "visual");
            Vector3 chest = Field<Vector3[]>(master, "characterChestCenters")[character];
            BodySurface[] surfaces = BakeBodySurfaces(visual, player);
            Vector3 last = Vector3.up;
            for (int yaw = 80; yaw <= 280; yaw += 10)
            {
                SwordPose pose = SwordPoseMath.Calculate(Quaternion.Euler(0, yaw, 0), false, new SwordPoseSettings(chest), ref last);
                Assert.That(pose.Position.y, Is.EqualTo(chest.y + .1f).Within(.00001f), "Horizontal rear turn raised character " + character);
                AssertBladeClearOfModel(surfaces, pose, "Horizontal blade crossed character " + character + " at yaw " + yaw);
            }
            for (int pitch = 0; pitch >= -60; pitch -= 5)
            {
                SwordPose pose = SwordPoseMath.Calculate(Quaternion.Euler(pitch, 180, 0), false, new SwordPoseSettings(chest), ref last);
                AssertBladeClearOfModel(surfaces, pose, "Raising the rear blade crossed character " + character + " at pitch " + pitch);
            }
            yield return null;
        }
    }

    private static void AssertBladeClearOfModel(BodySurface[] surfaces, SwordPose pose, string message)
    {
        Vector3 side = Vector3.Cross(pose.Direction, Vector3.up).normalized;
        // Probe the centre and padding edges against the imported triangles,
        // rather than relying on a broad renderer bound.
        foreach (Vector3 offset in new[] { Vector3.zero, Vector3.up * .06f, Vector3.down * .06f, side * .06f, -side * .06f })
        {
            Vector3 start = pose.Position + offset, end = start + pose.Direction * 2.1f;
            Assert.That(BladeCrossesModel(surfaces, start, end), Is.False, message);
        }
    }

    private static bool BladeCrossesModel(BodySurface[] surfaces, Vector3 start, Vector3 end)
    {
        foreach (BodySurface surface in surfaces)
            for (int triangle = 0; triangle < surface.Triangles.Length; triangle += 3)
                if (SegmentCrossesTriangle(start, end,
                    surface.Vertices[surface.Triangles[triangle]], surface.Vertices[surface.Triangles[triangle + 1]], surface.Vertices[surface.Triangles[triangle + 2]])) return true;
        return false;
    }

    private sealed class BodySurface
    {
        public Vector3[] Vertices;
        public int[] Triangles;
    }

    private static BodySurface[] BakeBodySurfaces(Transform visual, Transform player, bool torsoOnly = false)
    {
        var baked = new Mesh();
        BodySurface[] surfaces = visual.GetComponentsInChildren<SkinnedMeshRenderer>()
            .Where(skin => !torsoOnly || skin.name.Contains("_Chest") || skin.name.Contains("_Body")
                || skin.name.Contains("_Waist") || skin.name.Contains("_Back_guard"))
            .Select(skin =>
        {
            skin.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = player.InverseTransformPoint(skin.transform.TransformPoint(vertices[i]));
            return new BodySurface { Vertices = vertices, Triangles = baked.triangles };
        }).ToArray();
        Object.Destroy(baked);
        Assert.That(surfaces.Length, Is.GreaterThan(0));
        return surfaces;
    }

    private static bool SegmentCrossesTriangle(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 direction = end - start, firstEdge = b - a, secondEdge = c - a;
        Vector3 across = Vector3.Cross(direction, secondEdge);
        float determinant = Vector3.Dot(firstEdge, across);
        if (Mathf.Abs(determinant) < .0000001f) return false;
        float inverse = 1f / determinant;
        Vector3 fromVertex = start - a;
        float u = Vector3.Dot(fromVertex, across) * inverse;
        if (u < 0 || u > 1) return false;
        Vector3 otherAcross = Vector3.Cross(fromVertex, firstEdge);
        float v = Vector3.Dot(direction, otherAcross) * inverse;
        if (v < 0 || u + v > 1) return false;
        float along = Vector3.Dot(secondEdge, otherAcross) * inverse;
        return along >= 0 && along <= 1;
    }

    private static Bounds MeshBounds(Transform visual)
    {
        Bounds bounds = default; bool initialized = false;
        var mesh = new Mesh();
        foreach (SkinnedMeshRenderer skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skin.BakeMesh(mesh, true);
            foreach (Vector3 vertex in mesh.vertices)
            {
                Vector3 world = skin.transform.TransformPoint(vertex);
                if (!initialized) { bounds = new Bounds(world, Vector3.zero); initialized = true; }
                else bounds.Encapsulate(world);
            }
        }
        Object.Destroy(mesh);
        Assert.That(initialized, Is.True);
        return bounds;
    }

    private static SwordPose PoseReactionSword(object avatar, Quaternion input, double now)
    {
        Vector3 chest = Field<Vector3>(avatar, "restChest")
            + (Vector3)avatar.GetType().GetProperty("ChestOffset").GetValue(avatar);
        Vector3 remembered = Vector3.up;
        SwordPose logical = SwordPoseMath.Calculate(input, false, new SwordPoseSettings(chest), ref remembered);
        SwordPose rendered = (SwordPose)avatar.GetType().GetMethod("PoseSword", new[] { typeof(SwordPose), typeof(double) }).Invoke(avatar, new object[] { logical, now });
        Field<Transform>(avatar, "sword").SetLocalPositionAndRotation(rendered.Position, rendered.Rotation);
        return rendered;
    }

    private static void AssertRecoveryGrip(object avatar)
    {
        Transform sword = Field<Transform>(avatar, "sword");
        foreach (object arm in Field<Array>(avatar, "arms"))
        {
            Transform upper = Field<Transform>(arm, "Upper"), lower = Field<Transform>(arm, "Lower"), wrist = Field<Transform>(arm, "Wrist");
            Assert.That(wrist, Is.Not.Null, "The imported rig must provide the wrist used to follow the grip.");
            float upperLength = Field<float>(arm, "UpperLength"), lowerLength = Field<float>(arm, "LowerLength");
            Vector3 grip = sword.TransformPoint(new Vector3(0f, 0f, Field<float>(arm, "Side") > 0f ? .125f : -.125f));
            float distance = Vector3.Distance(upper.position, grip);
            // Imported bones inherit nonuniform scale. Rotating them changes
            // their world lengths slightly; the checks below constrain that
            // change independently. Judge reach using the posed chain rather
            // than a rest-length limit that ignores that allowed variation.
            float posedUpperLength = Vector3.Distance(upper.position, lower.position);
            float posedLowerLength = Vector3.Distance(lower.position, wrist.position);
            float reachable = Mathf.Clamp(distance, Mathf.Abs(posedUpperLength - posedLowerLength) + .0001f,
                posedUpperLength + posedLowerLength - .0001f);
            Assert.That(Vector3.Distance(wrist.position, grip), Is.LessThan(Mathf.Abs(distance - reachable) + .004f),
                "Each arm must follow its own sword grip as far as the rig can reach.");
            Assert.That(Vector3.Distance(upper.position, lower.position), Is.EqualTo(upperLength).Within(.003f), "Recovery must not stretch the upper arm.");
            Assert.That(Vector3.Distance(lower.position, wrist.position), Is.EqualTo(lowerLength).Within(.003f), "Recovery must not stretch the forearm.");
        }
    }

    private static void CaptureReaction(object avatar, string name, Transform sword = null)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR"))) return;
        Bounds body = MeshBounds(Field<Transform>(avatar, "visual"));
        body.Encapsulate((Vector3)avatar.GetType().GetProperty("HeadPosition").GetValue(avatar) + Vector3.up * .6f);
        if (sword != null) body.Encapsulate(sword.position + sword.forward * CombatContact.BladeLength);
        CaptureCharacter(Field<Transform>(avatar, "player"), body, name, true);
    }

    private static void CaptureDefeatedCharacter(Transform player, Bounds body, int character)
        => CaptureCharacter(player, body, "defeated-character-" + character, false);

    private static void CaptureCharacter(Transform player, Bounds body, string name, bool standing)
    {
        string folder = Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR");
        if (string.IsNullOrEmpty(folder)) return;
        Camera camera = new GameObject("Character preview").AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 45f; camera.nearClipPlane = .1f;
        camera.transform.position = body.center + player.forward * 4f
            + (standing ? player.right * 2f + Vector3.up * .6f : Vector3.up * 2f);
        camera.transform.LookAt(body.center);
        var target = new RenderTexture(960, 720, 24);
        var picture = new Texture2D(960, 720, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        Renderer[] renderers = player.GetComponentsInChildren<Renderer>(true);
        bool[] hidden = renderers.Select(renderer => renderer.forceRenderingOff).ToArray();
        try
        {
            // The title hides the gameplay renderers before this pose-only test
            // disables its controller. Restore them just for the preview camera.
            foreach (Renderer renderer in renderers) renderer.forceRenderingOff = false;
            target.Create();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); picture.Apply();
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), picture.EncodeToPNG());
        }
        finally
        {
            for (int i = 0; i < renderers.Length; i++) renderers[i].forceRenderingOff = hidden[i];
            RenderTexture.active = previous;
            target.Release(); Object.Destroy(target); Object.Destroy(picture); Object.Destroy(camera.gameObject);
        }
    }
}
