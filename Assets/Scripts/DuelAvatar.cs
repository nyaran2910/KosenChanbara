using System.Collections.Generic;
using SchoolFestival.Combat;
using UnityEngine;

// Pose only the imported visual skeleton. The player's combat origin never moves here.
public sealed class DuelAvatar
{
    private readonly Transform player, sword, visual, chest, hips, head;
    private readonly Transform[] bones;
    private readonly Vector3[] positions;
    private readonly Quaternion[] rotations;
    private readonly Vector3 initialVisualPosition, initialVisualScale, restChest;
    private readonly Quaternion initialVisualRotation;
    private readonly Leg[] legs;
    private readonly Arm[] arms;
    private double reactionStart = -100, finishStart = -100;
    private float recoil;
    private bool falling, celebrating, defeated;
    private bool defeatDisappears = true;
    private Vector3 defeatSwordGrip;
    private Quaternion defeatSwordRotation;
    private Vector3[] defeatVertices;
    private float defeatFloor;
    private static Mesh gloveMesh;
    private float stepOffset, stepFrom, stepTo, stepElapsed, stepLift;
    private bool stepping, stepArmed, hasMotionBaseline, suppressStepEffects;
    private readonly AttackStroke feedbackStroke = new AttackStroke();
    private Vector3 previousSwingDirection = Vector3.up;
    private Vector3 recoilDirection = Vector3.back, recoveryDirection;
    private bool recoveringFeet, suppressRecoveryEffects;
    private int recoveryLandings, landingLeg;
    private Vector3 renderedOrigin, previousOrigin, gaitVelocity;
    private int movingLeg = -1, nextLeg;
    private float previousStepOffset;
    private bool groundInitialized;
    private double staggerStart = -100, thrustStart = -100, guardRecoilStart = -100;
    private float staggerSeconds, thrustSeconds;
    private bool thrustSoundPending;
    private Vector3 staggerDirection, thrustDirection, headTopOffset;
    private SwordPose reactionSword;
    private Vector3 reactionSwordChest;
    private bool reactionGuard;
    private float reactionGuardFrom;
    private double reactionGuardChanged = -100;
    public Vector3 ChestOffset => chest == null ? Vector3.zero : player.InverseTransformPoint(chest.position) - restChest;
    public Vector3 HeadPosition => head != null ? head.TransformPoint(headTopOffset) : player.position + player.up;
    public Vector3 ChestCenter => restChest + ChestOffset;
    public bool IsStanding => player != null && visual != null && player.gameObject.activeInHierarchy && !falling && !defeated;
    public bool Stepped { get; private set; }
    public bool Swung { get; private set; }
    public Vector3 StepPosition { get; private set; }

    public float ReactionRoll(double now) => ReactionAngles(now).z;
    public float ReactionPitch(double now) => ReactionAngles(now).x;

    private Vector3 ReactionAngles(double now)
    {
        if (!IsStanding) return Vector3.zero;
        if (now >= staggerStart && now < staggerStart + staggerSeconds)
            return ReactionSwordPose.Sample(player.InverseTransformDirection(staggerDirection),
                (float)(now - staggerStart), staggerSeconds, false).ChestAngles;
        if (now >= guardRecoilStart && now < guardRecoilStart + ReactionSwordPose.GuardDurationSeconds)
            return ReactionSwordPose.Sample(recoilDirection, (float)(now - guardRecoilStart),
                ReactionSwordPose.GuardDurationSeconds, true).ChestAngles;
        return Vector3.zero;
    }

    private sealed class Leg
    {
        public Transform Upper, Lower, Ankle;
        public Vector3 Plant, KneePole;
        // Plant is the rest stance; contact anchors never inherit player movement.
        public Vector3 WorldPlant, StepStart, StepTarget;
        public Quaternion WorldRotation, StepRotation;
        public float Elapsed, Duration;
        public bool Moving;
        public Quaternion FootRotation;
        public float UpperLength, LowerLength;
    }

    private sealed class Arm
    {
        public Transform Upper, Lower, Wrist;
        public float Side, UpperLength, LowerLength;
    }

    public DuelAvatar(Transform player, Transform sword)
    {
        this.player = player;
        this.sword = sword;
        foreach (Transform child in player)
            if (child.gameObject.activeSelf && child.name.StartsWith("Chambara") && child != sword) { visual = child; break; }
        if (visual == null) { bones = new Transform[0]; positions = new Vector3[0]; rotations = new Quaternion[0]; legs = new Leg[0]; arms = new Arm[0]; return; }
        initialVisualPosition = visual.localPosition;
        initialVisualRotation = visual.localRotation;
        initialVisualScale = visual.localScale;
        AttachGripGlove(visual, sword);
        var found = new Dictionary<string, Transform>();
        foreach (Transform t in visual.GetComponentsInChildren<Transform>()) found[t.name] = t;
        found.TryGetValue("chest", out chest);
        found.TryGetValue("head", out head);
        if (!found.TryGetValue("hip", out hips) && !found.TryGetValue("hips", out hips)) found.TryGetValue("body", out hips);
        var allBones = new HashSet<Transform>();
        float headTop = float.NegativeInfinity;
        foreach (SkinnedMeshRenderer skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            headTop = Mathf.Max(headTop, skin.bounds.max.y);
            foreach (Transform t in skin.bones) if (t != null) allBones.Add(t);
            skin.updateWhenOffscreen = true;
            // FBX skins retain the importer's centimetre scale. Expand in metres,
            // otherwise a 1.5-local-unit margin becomes a 150-metre shadow bound.
            Vector3 scale = skin.transform.lossyScale;
            float worldScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z), 0.001f);
            Bounds b = skin.localBounds; b.Expand(1.5f / worldScale); skin.localBounds = b;
        }
        // The arm-less roster skins can omit their arm bones from skin.bones.
        // Still restore every bone that this pose controller rotates each tick.
        foreach (string name in new[] { "chest", "head", "hip", "hips", "body", "arm_l1", "arm_l2", "wrist_l", "arm_r1", "arm_r2", "wrist_r" })
            if (found.TryGetValue(name, out Transform poseBone)) allBones.Add(poseBone);
        if (head != null)
            headTopOffset = head.InverseTransformPoint(head.position + player.up
                * (float.IsNegativeInfinity(headTop) ? .3f : Mathf.Max(0f, headTop - head.position.y)));
        bones = new Transform[allBones.Count]; allBones.CopyTo(bones);
        positions = new Vector3[bones.Length]; rotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++) { positions[i] = bones[i].localPosition; rotations[i] = bones[i].localRotation; }
        restChest = chest == null ? Vector3.zero : player.InverseTransformPoint(chest.position);
        var armList = new List<Arm>();
        foreach (string side in new[] { "l", "r" })
        {
            if (found.TryGetValue("arm_" + side + "1", out Transform upper)
                && found.TryGetValue("arm_" + side + "2", out Transform lower))
            {
                found.TryGetValue("wrist_" + side, out Transform wrist);
                armList.Add(new Arm { Upper = upper, Lower = lower, Wrist = wrist,
                    Side = Vector3.Dot(upper.position - player.position, player.right) >= 0f ? 1f : -1f,
                    UpperLength = Vector3.Distance(upper.position, lower.position),
                    LowerLength = wrist == null ? 0f : Vector3.Distance(lower.position, wrist.position) });
            }
        }
        arms = armList.ToArray();
        var list = new List<Leg>();
        foreach (string side in new[] { "l", "r" })
        {
            if (!found.TryGetValue("foot_" + side + "1", out Transform upper)
                || !found.TryGetValue("foot_" + side + "2", out Transform lower)
                || !found.TryGetValue("ankle_" + side, out Transform ankle)) continue;
            Vector3 axis = ankle.position - upper.position;
            Vector3 pole = Vector3.ProjectOnPlane(lower.position - upper.position, axis);
            // This rig has straight legs in its bind pose. Keep a stable, slightly
            // outward knee plane instead of choosing a new bend direction each frame.
            if (pole.sqrMagnitude < 0.00001f)
                pole = player.forward + player.right * (side == "l" ? 0.12f : -0.12f);
            list.Add(new Leg { Upper = upper, Lower = lower, Ankle = ankle,
                Plant = player.InverseTransformPoint(ankle.position), FootRotation = Quaternion.Inverse(player.rotation) * ankle.rotation,
                KneePole = player.InverseTransformDirection(pole.normalized),
                UpperLength = Vector3.Distance(upper.position, lower.position), LowerLength = Vector3.Distance(lower.position, ankle.position) });
        }
        legs = list.ToArray();
        ResetGrounding();
    }

    public void React(CombatResult result, int index, double now, Vector3 displacement)
    {
        if (result.PushedPlayer == index && (result.Outcome == CombatOutcome.Hit || result.Outcome == CombatOutcome.GuardBreak))
        {
            Stagger(displacement, now, CombatReaction.RecoverySeconds, result.HitStopSeconds);
            return;
        }
        if (result.PushedPlayer == index && result.Outcome == CombatOutcome.GuardSuccess)
        {
            GuardRecoil(displacement, now, result.HitStopSeconds);
            return;
        }
        reactionStart = now + result.HitStopSeconds;
        recoil = result.PushedPlayer == index ? (result.Outcome == CombatOutcome.Hit ? 1f : 0.6f) : -0.3f;
        if (result.Outcome == CombatOutcome.Clash) recoil = 0.3f;
        recoveringFeet = result.PushedPlayer == index && displacement.sqrMagnitude > 0.0001f;
        if (recoveringFeet)
        {
            recoilDirection = player.InverseTransformDirection(displacement.normalized);
            recoveryLandings = 0; suppressRecoveryEffects = false;
            // The support stays at its world contact. The gait below follows the
            // displaced combat origin with actual alternating recovery steps.
            stepping = false; stepLift = 0f;
        }
        else recoilDirection = Vector3.back;
    }

    // Direction is the incoming blade's movement in world space, including
    // vertical cuts. Only the visual body moves; combat origins stay unchanged.
    public void Stagger(Vector3 worldCutDirection, double now, float recoverySeconds, float hitStopSeconds = 0f)
    {
        CaptureReactionSword();
        guardRecoilStart = -100;
        staggerStart = reactionStart = now + Mathf.Max(0f, hitStopSeconds);
        staggerSeconds = Mathf.Max(.1f, recoverySeconds);
        staggerDirection = worldCutDirection.sqrMagnitude > .0001f ? worldCutDirection.normalized : -player.forward;
        recoveryDirection = Vector3.ProjectOnPlane(staggerDirection, player.up);
        if (recoveryDirection.sqrMagnitude < .01f)
            recoveryDirection = player.forward * (Vector3.Dot(staggerDirection, player.up) < 0f ? 1f : -1f);
        recoveryDirection.Normalize();
        recoveringFeet = true; recoveryLandings = 0; suppressRecoveryEffects = false;
        recoil = 0f; thrustStart = -100; thrustSoundPending = false;
        stepping = false; stepLift = 0f;
        ClearMotionHistory();
        suppressRecoveryEffects = false;
    }

    // The blocked attacker loses balance without adding to the existing push
    // or the small legacy recoil translation. This is a pose-only reaction.
    public void GuardRecoil(Vector3 worldPushDirection, double now, float hitStopSeconds = 0f)
    {
        CaptureReactionSword();
        guardRecoilStart = reactionStart = now + Mathf.Max(0f, hitStopSeconds);
        recoil = .6f;
        recoilDirection = worldPushDirection.sqrMagnitude > .0001f
            ? player.InverseTransformDirection(worldPushDirection.normalized) : Vector3.back;
        recoveringFeet = true; recoveryLandings = 0;
        staggerStart = thrustStart = -100; thrustSoundPending = false;
        stepping = false; stepLift = 0f;
        ClearMotionHistory();
        suppressRecoveryEffects = false;
    }

    private void CaptureReactionSword()
    {
        reactionSword = sword == null ? new SwordPose(restChest, Quaternion.identity)
            : new SwordPose(player.InverseTransformPoint(sword.position), Quaternion.Inverse(player.rotation) * sword.rotation);
        reactionSwordChest = restChest + ChestOffset;
        reactionGuard = false; reactionGuardFrom = 0f; reactionGuardChanged = -100;
    }

    // Apply this only when writing the visible sword Transform. TargetPose and
    // combat contact history continue to use the original input pose.
    public SwordPose PoseSword(SwordPose inputPose, double now)
        => PoseSword(inputPose, now, false);

    public SwordPose PoseSword(SwordPose inputPose, double now, bool guard)
    {
        if (falling || defeated) return inputPose;
        Vector3 direction;
        float age, duration;
        bool blocked;
        if (staggerStart > -100 && now < staggerStart + staggerSeconds)
        {
            direction = player.InverseTransformDirection(staggerDirection);
            age = (float)(now - staggerStart); duration = staggerSeconds; blocked = false;
        }
        else if (guardRecoilStart > -100 && now < guardRecoilStart + ReactionSwordPose.GuardDurationSeconds)
        {
            direction = recoilDirection; age = (float)(now - guardRecoilStart);
            duration = ReactionSwordPose.GuardDurationSeconds; blocked = true;
        }
        else return inputPose;
        float blend = ReactionGuardBlend(now);
        if (guard != reactionGuard)
        {
            reactionGuardFrom = guard ? 1f : blend;
            reactionGuardChanged = now; reactionGuard = guard;
        }
        // A recovering player can defend immediately, even during hit stop.
        // Without a guard, the struck sword keeps its original frozen pose.
        if (age < 0f) return guard ? inputPose : reactionSword;
        Vector3 currentChest = restChest + ChestOffset;
        SwordPose reaction = ReactionSwordPose.Apply(reactionSword, inputPose, reactionSwordChest, currentChest,
            direction, age, duration, blocked);
        blend = ReactionGuardBlend(now);
        if (blend <= 0f) return reaction;
        ReactionMotion motion = ReactionSwordPose.Sample(direction, age, duration, blocked);
        // Keep the guarding blade close to its input direction while its grip
        // and both hands follow the displaced chest. Rotate the grip and blade
        // together: rotating only the grip fully can point a down guard through
        // the chest while the body leans backward.
        Quaternion defenceRotation = Quaternion.Slerp(Quaternion.identity, motion.ChestRotation, .25f);
        var defence = new SwordPose(currentChest + defenceRotation * (inputPose.Position - currentChest),
            defenceRotation * inputPose.Rotation);
        SwordPose result = new SwordPose(Vector3.Lerp(reaction.Position, defence.Position, blend),
            Quaternion.Slerp(reaction.Rotation, defence.Rotation, blend));
        return guard ? SwordPoseMath.ConstrainGuardPose(result) : result;
    }

    private float ReactionGuardBlend(double now)
        => reactionGuard ? 1f : Mathf.Lerp(reactionGuardFrom, 0f,
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)(now - reactionGuardChanged) / GuardPoseTransition.DurationSeconds)));

    public void Thrust(Vector3 worldDirection, double now, float durationSeconds)
    {
        thrustStart = now; thrustSeconds = Mathf.Max(.1f, durationSeconds);
        thrustDirection = worldDirection.sqrMagnitude > .0001f ? worldDirection.normalized : player.forward;
        // The gait follows the lunge with one planted support and a real lead
        // foot contact, rather than sliding the whole character toward the tip.
        stepping = false; stepLift = 0f; stepArmed = false;
        thrustSoundPending = true;
    }

    public void CopyMotionFrom(DuelAvatar other)
    {
        reactionStart = other.reactionStart; finishStart = other.finishStart;
        recoil = other.recoil; falling = other.falling; celebrating = other.celebrating;
        defeated = other.defeated;
        defeatDisappears = other.defeatDisappears;
        defeatSwordGrip = other.defeatSwordGrip; defeatSwordRotation = other.defeatSwordRotation;
        if (defeated) CaptureDefeatGeometry();
        stepOffset = other.stepOffset; stepFrom = other.stepFrom; stepTo = other.stepTo;
        stepElapsed = other.stepElapsed; stepLift = other.stepLift; stepping = other.stepping;
        recoilDirection = other.recoilDirection; recoveryDirection = other.recoveryDirection;
        staggerStart = other.staggerStart; staggerSeconds = other.staggerSeconds; staggerDirection = other.staggerDirection;
        guardRecoilStart = other.guardRecoilStart;
        reactionSword = other.reactionSword; reactionSwordChest = other.reactionSwordChest;
        reactionGuard = other.reactionGuard; reactionGuardFrom = other.reactionGuardFrom;
        reactionGuardChanged = other.reactionGuardChanged;
        thrustStart = other.thrustStart; thrustSeconds = other.thrustSeconds; thrustDirection = other.thrustDirection;
        thrustSoundPending = other.thrustSoundPending;
        recoveringFeet = other.recoveringFeet; recoveryLandings = other.recoveryLandings;
        renderedOrigin = other.renderedOrigin; previousOrigin = other.previousOrigin;
        gaitVelocity = other.gaitVelocity; movingLeg = other.movingLeg; nextLeg = other.nextLeg;
        previousStepOffset = other.previousStepOffset;
        for (int i = 0; i < legs.Length && i < other.legs.Length; i++)
        {
            Leg source = other.legs[i], target = legs[i];
            target.WorldPlant = source.WorldPlant; target.WorldRotation = source.WorldRotation;
            target.StepStart = source.StepStart; target.StepTarget = source.StepTarget;
            target.StepRotation = source.StepRotation; target.Elapsed = source.Elapsed;
            target.Duration = source.Duration; target.Moving = source.Moving;
        }
        ClearMotionHistory();
    }

    public void Finish(bool lost, double now) { finishStart = now; falling = lost; celebrating = !lost; defeated = false; }

    // Showdown opponents fall onto the course. Ring-out opponents still fall
    // below the arena through Finish; these are separate visual outcomes.
    public void Defeat(double now)
        => BeginCourseDefeat(now, true);

    public void FallOnCourse(double now)
        => BeginCourseDefeat(now, false);

    private void BeginCourseDefeat(double now, bool disappear)
    {
        finishStart = now; defeated = true; falling = celebrating = false;
        defeatDisappears = disappear;
        if (visual == null) return;
        RestoreBones();
        if (!disappear && sword != null)
        {
            defeatSwordGrip = visual.InverseTransformPoint(sword.position);
            defeatSwordRotation = Quaternion.Inverse(visual.rotation) * sword.rotation;
        }
        CaptureDefeatGeometry();
    }

    private void CaptureDefeatGeometry()
    {
        var points = new List<Vector3>();
        var baked = new Mesh();
        foreach (SkinnedMeshRenderer skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // Imported skins retain their FBX transform scale. Compensate
            // before TransformPoint applies it to the baked vertices.
            skin.BakeMesh(baked, true);
            foreach (Vector3 vertex in baked.vertices)
                points.Add(visual.InverseTransformPoint(skin.transform.TransformPoint(vertex)));
        }
        foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>())
            if (filter.sharedMesh != null)
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    points.Add(visual.InverseTransformPoint(filter.transform.TransformPoint(vertex)));
        Object.Destroy(baked);
        defeatVertices = points.ToArray();
        defeatFloor = visual.position.y;
        if (defeatVertices.Length > 0)
        {
            defeatFloor = float.PositiveInfinity;
            foreach (Vector3 vertex in defeatVertices)
                defeatFloor = Mathf.Min(defeatFloor, visual.TransformPoint(vertex).y);
            defeatFloor = Mathf.Max(defeatFloor, player.TransformPoint(initialVisualPosition).y);
        }
    }

    private void TickDefeat(float elapsed)
    {
        float topple = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .48f));
        visual.localRotation *= Quaternion.Euler(-82f * topple, 0f, 12f * topple);
        visual.localPosition += new Vector3(.06f * topple, 0f, -.25f * topple);
        // Give the landed body a moment to read before shrinking away above
        // the floor. Expanded renderer bounds cannot be used for this contact.
        float exit = defeatDisappears ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - .82f) / .33f)) : 0f;
        visual.localScale = initialVisualScale * (1f - exit);
        float lowest = float.PositiveInfinity;
        foreach (Vector3 vertex in defeatVertices)
            lowest = Mathf.Min(lowest, visual.TransformPoint(vertex).y);
        if (!float.IsPositiveInfinity(lowest)) visual.position += Vector3.up * (defeatFloor - lowest);
        if (!defeatDisappears && sword != null)
            sword.SetPositionAndRotation(visual.TransformPoint(defeatSwordGrip), visual.rotation * defeatSwordRotation);
    }

    public static void AttachGripGlove(Transform body, Transform sword)
    {
        if (body == null || sword == null || sword.Find("ChambaraGripGlove") != null) return;
        Material armor = null;
        foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>())
            foreach (Material material in renderer.sharedMaterials)
                if (material != null && material.name.StartsWith("Armor_") && !material.name.StartsWith("Armor_seams"))
                { armor = material; break; }
        if (armor == null) return;
        var hands = new GameObject("ChambaraGripGlove"); hands.transform.SetParent(sword, false);
        // Two plain spheres share the character's colour and travel with the
        // grip in every pose. They add no combat geometry or separate fingers.
        AddGlovePart(hands.transform, "Right hand", new Vector3(0f, 0f, .125f), Vector3.one * .23f, armor);
        AddGlovePart(hands.transform, "Left hand", new Vector3(0f, 0f, -.125f), Vector3.one * .23f, armor);
    }

    private static void AddGlovePart(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        if (gloveMesh == null)
        {
            const int sides = 16, rings = 10;
            var vertices = new Vector3[(sides + 1) * (rings + 1)];
            var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length];
            var triangles = new int[sides * rings * 6];
            int index = 0;
            for (int ring = 0; ring <= rings; ring++)
                for (int side = 0; side <= sides; side++)
                {
                    float latitude = Mathf.PI * ring / rings, longitude = 2f * Mathf.PI * side / sides;
                    Vector3 normal = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
                    vertices[index] = normal * .5f; normals[index] = normal;
                    uv[index++] = new Vector2(side / (float)sides, ring / (float)rings);
                }
            index = 0;
            for (int ring = 0; ring < rings; ring++)
                for (int side = 0; side < sides; side++)
                {
                    int a = ring * (sides + 1) + side, b = a + sides + 1;
                    triangles[index++] = a; triangles[index++] = a + 1; triangles[index++] = b;
                    triangles[index++] = a + 1; triangles[index++] = b + 1; triangles[index++] = b;
                }
            gloveMesh = new Mesh { name = "Chambara glove", vertices = vertices, normals = normals, uv = uv, triangles = triangles };
            gloveMesh.RecalculateBounds();
        }
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
        part.GetComponent<MeshFilter>().sharedMesh = gloveMesh;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    public void Tick(double now, float dt, Quaternion input, bool guard, float speed)
    {
        Stepped = false; Swung = thrustSoundPending; thrustSoundPending = false;
        if (visual == null) return;
        RestoreBones();
        float sinceFinish = (float)(now - finishStart);
        if (defeated) { TickDefeat(sinceFinish); return; }
        if (falling)
        {
            float t = Mathf.Clamp01(sinceFinish / 0.6f);
            visual.localPosition += new Vector3(0f, -2.6f * t * t, -0.8f * t);
            visual.localRotation *= Quaternion.Euler(-65f * t, 0f, 18f * t);
            return;
        }
        Vector3 direction = input * Vector3.forward;
        float forward = guard ? 0.12f : Mathf.Clamp01(direction.z);
        float reactionDuration = guardRecoilStart > -100 ? ReactionSwordPose.GuardDurationSeconds : .32f;
        float reaction = recoil * Mathf.Sin(Mathf.PI * Mathf.Clamp01((float)(now - reactionStart) / reactionDuration));
        float sway = Mathf.Sin((float)now * 2.3f) * 0.018f;
        // A little knee flex leaves enough reach to keep both soles on the floor.
        visual.localPosition += new Vector3(sway, -0.025f - 0.018f * forward, 0.035f * forward)
            + recoilDirection * (0.09f * reaction);
        Rotate(hips, new Vector3(4f * forward - 6f * reaction, 3f * direction.x, -sway * 100f - recoilDirection.x * reaction * 6f));
        Rotate(chest, new Vector3(9f * forward - 18f * reaction, direction.x * 8f, -direction.x * 5f - recoilDirection.x * reaction * 12f));
        if (celebrating) Rotate(chest, new Vector3(-8f, Mathf.Sin(sinceFinish * 8f) * 8f, 0f));
        Vector3 bodyTravel = Vector3.zero;
        float kneeReaction = 0f;
        bool staggerActive = now >= staggerStart && now < staggerStart + staggerSeconds;
        if (staggerActive)
        {
            Vector3 cut = player.InverseTransformDirection(staggerDirection);
            ReactionMotion motion = ReactionSwordPose.Sample(cut, (float)(now - staggerStart), staggerSeconds, false);
            float impact = motion.Impact, balance = motion.Balance;
            float pitch = cut.z - cut.y * .7f;
            float lean = impact + balance;
            visual.localPosition += Vector3.down * (.055f * impact);
            Rotate(hips, new Vector3(12f * pitch, -5f * cut.x, -14f * cut.x) * lean);
            Rotate(chest, motion.ChestAngles);
            Rotate(head, new Vector3(-8f * pitch, 5f * cut.x, 8f * cut.x) * impact);
            PoseRecoveryArms(cut, impact, balance, false);
            kneeReaction = impact;
            bodyTravel = recoveryDirection * (.27f * impact);
        }
        else if (now >= guardRecoilStart && now < guardRecoilStart + ReactionSwordPose.GuardDurationSeconds)
        {
            ReactionMotion motion = ReactionSwordPose.Sample(recoilDirection, (float)(now - guardRecoilStart), ReactionSwordPose.GuardDurationSeconds, true);
            float impact = motion.Impact, catchBalance = motion.Balance;
            // The sword bounces off the guard; the shoulders open, then the
            // attacker catches their balance on the next grounded step.
            Vector3 push = recoilDirection;
            Rotate(hips, new Vector3(-8f * impact, catchBalance * 8f, -push.x * impact * 8f));
            Rotate(chest, motion.ChestAngles);
            Rotate(head, new Vector3(10f * impact, -catchBalance * 8f, push.x * impact * 8f));
            PoseRecoveryArms(push, impact, catchBalance, true);
            kneeReaction = impact * .8f;
        }
        else if (now >= thrustStart && now < thrustStart + thrustSeconds)
        {
            // Match the sword's brief chamber, fast drive and measured return.
            float swordAge = (float)(now - thrustStart) * ThrustPose.DurationSeconds / thrustSeconds;
            float lunge = ThrustPose.Extension(swordAge) / ThrustPose.Reach;
            Vector3 local = player.InverseTransformDirection(thrustDirection);
            visual.localPosition += Vector3.down * (.025f * Mathf.Max(0f, lunge));
            Rotate(hips, new Vector3(8f * local.z, -4f * local.x, -7f * local.x) * lunge);
            Rotate(chest, new Vector3(15f * local.z, -7f * local.x, -10f * local.x) * lunge);
            bodyTravel = Vector3.ProjectOnPlane(thrustDirection, player.up) * (.22f * lunge);
        }
        bool guardRecoilActive = now >= guardRecoilStart && now < guardRecoilStart + ReactionSwordPose.GuardDurationSeconds;
        if (!staggerActive && !guardRecoilActive)
        {
            TrackSwing(direction, speed, guard, dt, forward, now);
            if (!recoveringFeet && now >= thrustStart + thrustSeconds && movingLeg < 0)
                AdvanceStep(dt, forward, guard, speed);
        }
        if (staggerActive || guardRecoilActive)
        { TrackSuppressedSwing(direction, speed, guard, now); }
        UpdateGrounding(dt, bodyTravel, kneeReaction);
        if (staggerActive || guardRecoilActive)
            // Defence takes effect immediately during recovery. Both hands
            // must follow that new grip fully, rather than lagging behind the
            // blocked attack's deliberately softer balance animation.
            FollowReactionGrip(guard ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(kneeReaction * 1.4f)));
        if (recoveringFeet && Vector3.Distance(renderedOrigin, player.position) < 0.005f
            && movingLeg < 0 && now >= staggerStart + staggerSeconds
            && now >= (guardRecoilStart > -100 ? guardRecoilStart + ReactionSwordPose.GuardDurationSeconds
                : reactionStart + .36f)) recoveringFeet = false;
    }

    private void ResetGrounding()
    {
        renderedOrigin = previousOrigin = player.position; gaitVelocity = Vector3.zero;
        movingLeg = -1; nextLeg = 0; previousStepOffset = stepOffset;
        foreach (Leg leg in legs)
        {
            leg.WorldPlant = player.TransformPoint(leg.Plant);
            leg.WorldRotation = player.rotation * leg.FootRotation;
            leg.Moving = false;
        }
        groundInitialized = true;
    }

    private Vector3 DesiredPlant(int index)
    {
        Vector3 local = legs[index].Plant + (index == 0 ? Vector3.forward * stepOffset : Vector3.zero);
        return player.TransformPoint(local) + renderedOrigin - player.position;
    }

    private void UpdateGrounding(float dt, Vector3 bodyTravel, float kneeReaction)
    {
        if (!groundInitialized) ResetGrounding();
        dt = Mathf.Max(0f, dt);
        // Combat knockback is 3 m in 0.12 s. Retain that rule while the visual
        // body catches its balance at a finite running speed instead of skating.
        Vector3 targetOrigin = player.position + bodyTravel;
        renderedOrigin = Vector3.MoveTowards(renderedOrigin, targetOrigin, 6f * dt);
        renderedOrigin.y = targetOrigin.y;
        gaitVelocity = dt > 0f ? (renderedOrigin - previousOrigin) / dt : Vector3.zero;
        previousOrigin = renderedOrigin;
        visual.position += renderedOrigin - player.position;
        if (gaitVelocity.sqrMagnitude > 0.01f && stepping)
        {
            stepping = false; stepLift = 0f;
        }

        if (movingLeg >= 0)
        {
            Leg moving = legs[movingLeg];
            moving.Elapsed += dt;
            float t = Mathf.Clamp01(moving.Elapsed / moving.Duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            moving.WorldPlant = Vector3.Lerp(moving.StepStart, moving.StepTarget, eased)
                + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.07f);
            moving.WorldRotation = Quaternion.Slerp(moving.WorldRotation, moving.StepRotation, eased);
            if (t >= 1f)
            {
                moving.WorldPlant = moving.StepTarget; moving.WorldRotation = moving.StepRotation;
                moving.Moving = false; landingLeg = movingLeg; movingLeg = -1;
                Stepped = !suppressRecoveryEffects; recoveryLandings++;
                StepPosition = moving.WorldPlant;
            }
        }
        if (movingLeg < 0 && !stepping && legs.Length > 0)
        {
            int candidate = -1;
            for (int offset = 0; offset < legs.Length; offset++)
            {
                int i = (nextLeg + offset) % legs.Length;
                float distance = Vector3.Distance(legs[i].WorldPlant, DesiredPlant(i));
                float turn = Quaternion.Angle(legs[i].WorldRotation, player.rotation * legs[i].FootRotation);
                if (distance > 0.10f || turn > 25f) { candidate = i; break; }
            }
            if (candidate >= 0)
            {
                Leg leg = legs[candidate]; movingLeg = candidate; leg.Moving = true;
                leg.Elapsed = 0f; leg.Duration = Mathf.Lerp(0.22f, 0.06f, Mathf.Clamp01(gaitVelocity.magnitude / 6f));
                leg.StepStart = leg.WorldPlant;
                leg.StepTarget = DesiredPlant(candidate) + Vector3.ClampMagnitude(gaitVelocity * 0.04f, 0.24f);
                leg.StepTarget.y = DesiredPlant(candidate).y;
                // A fast combat push can put the desired stride beyond both
                // legs' combined reach. Keep the next contact within a stance
                // that the pelvis can bridge without stretching either chain.
                foreach (Leg support in legs)
                {
                    if (support == leg) continue;
                    Vector3 hipOffset = leg.Upper.position - support.Upper.position;
                    Vector3 centre = support.WorldPlant + hipOffset;
                    Vector3 stride = leg.StepTarget - centre;
                    stride.y = 0f;
                    float span = (leg.UpperLength + leg.LowerLength
                        + support.UpperLength + support.LowerLength - 0.008f) * 0.65f;
                    Vector3 bounded = centre + Vector3.ClampMagnitude(stride, span);
                    leg.StepTarget.x = bounded.x; leg.StepTarget.z = bounded.z;
                }
                leg.StepRotation = player.rotation * leg.FootRotation;
                nextLeg = (candidate + 1) % legs.Length;
                suppressRecoveryEffects = false;
            }
        }
        // The existing deliberate sword stance moves only the lead foot. Its
        // support remains anchored even if the player turns during the gesture.
        if (legs.Length > 0 && movingLeg < 0)
        {
            legs[0].WorldPlant += player.forward * (stepOffset - previousStepOffset);
            if (Stepped) StepPosition = legs[0].WorldPlant;
        }
        previousStepOffset = stepOffset;

        // Both targets must be reachable: constraining only the support lets
        // the IK clamp the swinging chain and lift its ankle above the arc.
        // The bounded stride leaves an overlap between the two reach regions;
        // project the pelvis into it before solving either leg.
        for (int pass = 0; pass < 12; pass++)
            foreach (Leg leg in legs)
            {
                Vector3 target = leg.WorldPlant + (leg == legs[0] && movingLeg < 0 ? Vector3.up * stepLift : Vector3.zero);
                Vector3 delta = leg.Upper.position - target;
                float reach = leg.UpperLength + leg.LowerLength - 0.004f;
                float horizontal = new Vector2(delta.x, delta.z).magnitude;
                if (horizontal > reach * 0.78f)
                {
                    Vector3 correction = new Vector3(delta.x, 0f, delta.z).normalized * (horizontal - reach * 0.78f);
                    visual.position -= correction;
                    horizontal = reach * 0.78f;
                }
                float maxHeight = Mathf.Sqrt(Mathf.Max(0.001f, reach * reach - horizontal * horizontal));
                if (delta.y > maxHeight) visual.position += Vector3.down * (delta.y - maxHeight);
            }
        for (int i = 0; i < legs.Length; i++)
        {
            Leg leg = legs[i];
            Vector3 target = leg.WorldPlant + (i == 0 && movingLeg < 0 ? Vector3.up * stepLift : Vector3.zero);
            SolveLeg(leg, target, kneeReaction);
        }
    }

    private void TrackSwing(Vector3 direction, float speed, bool guard, float dt, float forward, double now)
    {
        var pose = new CombatPose(Vector3.zero, previousSwingDirection, Vector3.zero, direction,
            Vector3.zero, Vector3.forward, Vector3.zero, Vector3.zero, Vector3.forward);
        feedbackStroke.Update(new CombatInput(Vector3.Angle(Vector3.forward, direction), speed, guard),
            pose, now, celebrating);
        previousSwingDirection = direction;
        Swung |= feedbackStroke.StartedKind == AttackKind.Cut;
        if (!hasMotionBaseline)
        { stepArmed = !guard && !celebrating && forward < .2f; hasMotionBaseline = true; }
    }

    private void TrackSuppressedSwing(Vector3 direction, float speed, bool guard, double now)
    {
        var pose = new CombatPose(Vector3.zero, previousSwingDirection, Vector3.zero, direction,
            Vector3.zero, Vector3.forward, Vector3.zero, Vector3.zero, Vector3.forward);
        feedbackStroke.Update(new CombatInput(Vector3.Angle(Vector3.forward, direction), speed, guard), pose, now, true);
        previousSwingDirection = direction;
    }

    private void AdvanceStep(float dt, float forward, bool guard, float speed)
    {
        if (!guard && !celebrating && forward < 0.2f) stepArmed = true;
        if (!stepping)
        {
            if (stepOffset > 0f && (guard || celebrating || forward < 0.2f))
                StartStep(0f);
            else if (stepArmed && !guard && !celebrating && forward >= 0.32f && speed >= 40f)
            {
                StartStep(0.12f);
                stepArmed = false;
                suppressStepEffects = false;
            }
        }
        if (!stepping) return;
        stepElapsed += Mathf.Max(0f, dt);
        float t = Mathf.Clamp01(stepElapsed / 0.24f);
        stepOffset = Mathf.Lerp(stepFrom, stepTo, Mathf.SmoothStep(0f, 1f, t));
        stepLift = Mathf.Sin(t * Mathf.PI) * 0.035f;
        if (t >= 1f)
        {
            stepLift = 0f;
            stepping = false;
            Stepped = !suppressStepEffects;
            landingLeg = 0;
        }
    }

    private void StartStep(float target)
    {
        stepping = true; stepElapsed = 0f; stepFrom = stepOffset; stepTo = target;
    }

    public void ClearMotionHistory()
    {
        hasMotionBaseline = stepArmed = false;
        feedbackStroke.Reset(); previousSwingDirection = Vector3.up;
        // Finish placing the foot naturally, but do not replay the old gesture's
        // landing sound/dust after resume, recenter or a controller change.
        suppressStepEffects = true;
        suppressRecoveryEffects = true;
        Swung = Stepped = false; thrustSoundPending = false;
    }

    private void Rotate(Transform bone, Vector3 euler)
    {
        if (bone != null) bone.rotation = player.rotation * Quaternion.Euler(euler) * Quaternion.Inverse(player.rotation) * bone.rotation;
    }

    private void PoseRecoveryArms(Vector3 direction, float impact, float balance, bool blocked)
    {
        foreach (Arm arm in arms)
        {
            float open = blocked ? 38f : 30f;
            Rotate(arm.Upper, new Vector3(-20f * impact + balance * 8f,
                arm.Side * impact * 8f, arm.Side * open * impact - direction.x * impact * 10f));
            Rotate(arm.Lower, new Vector3(-32f * impact, arm.Side * 12f * impact, arm.Side * balance * 12f));
        }
    }

    private void FollowReactionGrip(float weight)
    {
        if (sword == null || weight <= 0f) return;
        foreach (Arm arm in arms)
        {
            if (arm.Wrist == null || arm.UpperLength < .001f || arm.LowerLength < .001f) continue;
            Vector3 shoulder = arm.Upper.position;
            Vector3 grip = sword.TransformPoint(new Vector3(0f, 0f, arm.Side > 0f ? .125f : -.125f));
            // Resolve the blend target once. Refinement must not repeatedly
            // apply the weight and pull a partially recovering hand too far.
            Vector3 target = Vector3.Lerp(arm.Wrist.position, grip, weight);
            Vector3 direction = (target - shoulder).normalized;
            Vector3 pole = Vector3.ProjectOnPlane(player.right * arm.Side - player.up * .25f - player.forward * .35f, direction).normalized;
            if (pole.sqrMagnitude < .1f) pole = player.up;
            // Imported rigs have scaled parent transforms. A bone rotation can
            // slightly change the chain's world lengths, so refine the solution
            // with the actual posed lengths instead of leaving a residual gap.
            for (int iteration = 0; iteration < 4; iteration++)
            {
                float upperLength = Vector3.Distance(arm.Upper.position, arm.Lower.position);
                float lowerLength = Vector3.Distance(arm.Lower.position, arm.Wrist.position);
                float distance = Mathf.Clamp(Vector3.Distance(shoulder, target),
                    Mathf.Abs(upperLength - lowerLength) + .0001f, upperLength + lowerLength - .0001f);
                float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
                float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
                Vector3 elbow = shoulder + direction * along + pole * height;
                arm.Upper.rotation = Quaternion.FromToRotation(arm.Lower.position - shoulder, elbow - shoulder) * arm.Upper.rotation;
                Vector3 end = shoulder + direction * distance;
                arm.Lower.rotation = Quaternion.FromToRotation(arm.Wrist.position - arm.Lower.position, end - arm.Lower.position) * arm.Lower.rotation;
            }
        }
    }

    private void SolveLeg(Leg leg, Vector3 target, float kneeReaction)
    {
        Vector3 hip = leg.Upper.position;
        Vector3 direction = target - hip;
        float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(leg.UpperLength - leg.LowerLength) + 0.0001f,
            leg.UpperLength + leg.LowerLength - 0.0001f);
        direction.Normalize();
        Vector3 kneePole = leg.KneePole + Vector3.right * (Mathf.Sign(leg.Plant.x) * .4f * kneeReaction);
        Vector3 pole = Vector3.ProjectOnPlane(player.TransformDirection(kneePole), direction).normalized;
        if (pole.sqrMagnitude < 0.1f) pole = player.right;
        float along = (leg.UpperLength * leg.UpperLength - leg.LowerLength * leg.LowerLength + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, leg.UpperLength * leg.UpperLength - along * along));
        Vector3 knee = hip + direction * along + pole * height;
        leg.Upper.rotation = Quaternion.FromToRotation(leg.Lower.position - hip, knee - hip) * leg.Upper.rotation;
        Vector3 end = hip + direction * distance;
        leg.Lower.rotation = Quaternion.FromToRotation(leg.Ankle.position - leg.Lower.position, end - leg.Lower.position) * leg.Lower.rotation;
        leg.Ankle.rotation = leg.WorldRotation;
    }

    private void RestoreBones()
    {
        visual.SetLocalPositionAndRotation(initialVisualPosition, initialVisualRotation);
        visual.localScale = initialVisualScale;
        for (int i = 0; i < bones.Length; i++) bones[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
    }

    public void Reset()
    {
        reactionStart = finishStart = -100; falling = celebrating = stepping = defeated = false;
        staggerStart = thrustStart = guardRecoilStart = -100; staggerSeconds = thrustSeconds = 0f; thrustSoundPending = false;
        recoil = stepOffset = stepFrom = stepTo = stepElapsed = stepLift = 0f;
        recoveringFeet = false; recoveryLandings = landingLeg = 0;
        recoilDirection = Vector3.back; recoveryDirection = Vector3.zero;
        reactionGuard = false; reactionGuardFrom = 0f; reactionGuardChanged = -100;
        ClearMotionHistory();
        if (visual != null) { RestoreBones(); ResetGrounding(); }
    }
}
