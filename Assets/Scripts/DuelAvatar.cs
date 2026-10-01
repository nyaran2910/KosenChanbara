using System.Collections.Generic;
using SchoolFestival.Combat;
using UnityEngine;

// Pose only the imported visual skeleton. The player's combat origin never moves here.
public sealed class DuelAvatar
{
    private readonly Transform player, visual, chest, hips;
    private readonly Transform[] bones;
    private readonly Vector3[] positions;
    private readonly Quaternion[] rotations;
    private readonly Vector3 initialVisualPosition, restChest;
    private readonly Quaternion initialVisualRotation;
    private readonly Leg[] legs;
    private double reactionStart = -100, finishStart = -100;
    private float recoil;
    private bool falling, celebrating;
    private float stepPhase;
    public Vector3 ChestOffset => chest == null ? Vector3.zero : player.InverseTransformPoint(chest.position) - restChest;
    public bool Stepped { get; private set; }

    private sealed class Leg
    {
        public Transform Upper, Lower, Ankle;
        public Vector3 Plant;
        public Quaternion FootRotation;
        public float UpperLength, LowerLength;
    }

    public DuelAvatar(Transform player, Transform sword)
    {
        this.player = player;
        foreach (Transform child in player)
            if (child.name.StartsWith("Chambara") && child != sword) { visual = child; break; }
        if (visual == null) { bones = new Transform[0]; positions = new Vector3[0]; rotations = new Quaternion[0]; legs = new Leg[0]; return; }
        initialVisualPosition = visual.localPosition;
        initialVisualRotation = visual.localRotation;
        var found = new Dictionary<string, Transform>();
        foreach (Transform t in visual.GetComponentsInChildren<Transform>()) found[t.name] = t;
        found.TryGetValue("chest", out chest);
        if (!found.TryGetValue("hip", out hips) && !found.TryGetValue("hips", out hips)) found.TryGetValue("body", out hips);
        var allBones = new HashSet<Transform>();
        foreach (SkinnedMeshRenderer skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            foreach (Transform t in skin.bones) if (t != null) allBones.Add(t);
            skin.updateWhenOffscreen = true;
            // FBX skins retain the importer's centimetre scale. Expand in metres,
            // otherwise a 1.5-local-unit margin becomes a 150-metre shadow bound.
            Vector3 scale = skin.transform.lossyScale;
            float worldScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z), 0.001f);
            Bounds b = skin.localBounds; b.Expand(1.5f / worldScale); skin.localBounds = b;
        }
        bones = new Transform[allBones.Count]; allBones.CopyTo(bones);
        positions = new Vector3[bones.Length]; rotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++) { positions[i] = bones[i].localPosition; rotations[i] = bones[i].localRotation; }
        restChest = chest == null ? Vector3.zero : player.InverseTransformPoint(chest.position);
        var list = new List<Leg>();
        foreach (string side in new[] { "l", "r" })
        {
            if (!found.TryGetValue("foot_" + side + "1", out Transform upper)
                || !found.TryGetValue("foot_" + side + "2", out Transform lower)
                || !found.TryGetValue("ankle_" + side, out Transform ankle)) continue;
            list.Add(new Leg { Upper = upper, Lower = lower, Ankle = ankle,
                Plant = player.InverseTransformPoint(ankle.position), FootRotation = Quaternion.Inverse(player.rotation) * ankle.rotation,
                UpperLength = Vector3.Distance(upper.position, lower.position), LowerLength = Vector3.Distance(lower.position, ankle.position) });
        }
        legs = list.ToArray();
    }

    public void React(CombatResult result, int index, double now)
    {
        reactionStart = now + result.HitStopSeconds;
        recoil = result.PushedPlayer == index ? (result.Outcome == CombatOutcome.Hit ? 1f : 0.6f) : -0.3f;
        if (result.Outcome == CombatOutcome.Clash) recoil = 0.3f;
    }

    public void Finish(bool lost, double now) { finishStart = now; falling = lost; celebrating = !lost; }

    public void Tick(double now, float dt, Quaternion input, bool guard, float speed)
    {
        Stepped = false;
        if (visual == null) return;
        RestoreBones();
        float sinceFinish = (float)(now - finishStart);
        if (falling)
        {
            float t = Mathf.Clamp01(sinceFinish / 0.6f);
            visual.localPosition += new Vector3(0f, -2.6f * t * t, -0.8f * t);
            visual.localRotation *= Quaternion.Euler(-65f * t, 0f, 18f * t);
            return;
        }
        Vector3 direction = input * Vector3.forward;
        float forward = guard ? 0.12f : Mathf.Clamp01(Vector3.Angle(Vector3.up, direction) / 90f);
        float reaction = recoil * Mathf.Sin(Mathf.PI * Mathf.Clamp01((float)(now - reactionStart) / 0.32f));
        float sway = Mathf.Sin((float)now * 2.3f) * 0.018f;
        visual.localPosition += new Vector3(sway, -0.018f * forward, 0.035f * forward - 0.09f * reaction);
        Rotate(hips, new Vector3(4f * forward - 6f * reaction, 3f * direction.x, -sway * 100f));
        Rotate(chest, new Vector3(9f * forward - 18f * reaction, direction.x * 8f, -direction.x * 5f));
        if (celebrating) Rotate(chest, new Vector3(-8f, Mathf.Sin(sinceFinish * 8f) * 8f, 0f));
        float previous = stepPhase;
        // A fast swing advances one shuffle; quiet input returns to a planted stance.
        stepPhase = Mathf.MoveTowards(stepPhase, Mathf.Clamp01(speed / 160f) * forward, dt * 5f);
        Stepped = previous < 0.25f && stepPhase >= 0.25f;
        for (int i = 0; i < legs.Length; i++)
        {
            Leg leg = legs[i];
            float step = i == 0 ? stepPhase : -0.25f * stepPhase;
            Vector3 target = leg.Plant + new Vector3(0f, Mathf.Sin(Mathf.Abs(step) * Mathf.PI) * 0.07f, step * 0.18f - reaction * 0.06f);
            SolveLeg(leg, player.TransformPoint(target));
        }
    }

    private void Rotate(Transform bone, Vector3 euler)
    {
        if (bone != null) bone.rotation = player.rotation * Quaternion.Euler(euler) * Quaternion.Inverse(player.rotation) * bone.rotation;
    }

    private void SolveLeg(Leg leg, Vector3 target)
    {
        Vector3 hip = leg.Upper.position;
        Vector3 direction = target - hip;
        float distance = Mathf.Clamp(direction.magnitude, 0.001f, (leg.UpperLength + leg.LowerLength) * 0.995f);
        direction.Normalize();
        Vector3 pole = Vector3.ProjectOnPlane(player.forward, direction).normalized;
        if (pole.sqrMagnitude < 0.1f) pole = player.right;
        float along = (leg.UpperLength * leg.UpperLength - leg.LowerLength * leg.LowerLength + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, leg.UpperLength * leg.UpperLength - along * along));
        Vector3 knee = hip + direction * along + pole * height;
        leg.Upper.rotation = Quaternion.FromToRotation(leg.Lower.position - hip, knee - hip) * leg.Upper.rotation;
        Vector3 end = hip + direction * distance;
        leg.Lower.rotation = Quaternion.FromToRotation(leg.Ankle.position - leg.Lower.position, end - leg.Lower.position) * leg.Lower.rotation;
        leg.Ankle.rotation = player.rotation * leg.FootRotation;
    }

    private void RestoreBones()
    {
        visual.SetLocalPositionAndRotation(initialVisualPosition, initialVisualRotation);
        for (int i = 0; i < bones.Length; i++) bones[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
    }

    public void Reset()
    {
        reactionStart = finishStart = -100; falling = celebrating = false; recoil = stepPhase = 0; Stepped = false;
        if (visual != null) RestoreBones();
    }
}
