using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

// Golden cases come from player-authored cuts that already guard successfully.
// Keep success explicit: equating two shared outcomes would hide two failures.
public sealed class NaturalGuardParityTests
{
    [TestCase(0, 1, 30)] [TestCase(0, 1, 60)] [TestCase(0, 1, 144)]
    [TestCase(1, 0, 30)] [TestCase(1, 0, 60)] [TestCase(1, 0, 144)]
    [TestCase(2, 3, 30)] [TestCase(2, 3, 60)] [TestCase(2, 3, 144)]
    public void AuthoredNpcCutsRespectTheSameSuccessfulGuardAsNaturalPlayerCuts(int attackIndex, int guardOctant, int fps)
    {
        Assert.That(FirstContact(attackIndex, guardOctant, false, fps), Is.EqualTo(CombatOutcome.GuardSuccess),
            "Preserve the established player-to-enemy successful guard.");
        Assert.That(FirstContact(attackIndex, guardOctant, true, fps), Is.EqualTo(CombatOutcome.GuardSuccess),
            "An authored NPC cut must not break the equivalent player's guard.");
    }

    private static CombatOutcome FirstContact(int attackIndex, int guardOctant, bool npc, int fps)
    {
        float dt = 1f / fps; const float distance = 2.35f;
        var settings = new SwordPoseSettings(Vector3.zero);
        Vector3 ready = ShowdownCombat.AttackReadyDirection(attackIndex), remembered = Vector3.up;
        Quaternion facing = Quaternion.Euler(0f, 180f, 0f);
        SwordPose guard = SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward,
            ShowdownCombat.ReadyDirection(guardOctant)), true, settings, ref remembered);
        Vector3 body = Vector3.forward * distance;
        Vector3 grip = body + facing * guard.Position, blade = facing * guard.Direction;
        var defence = new CombatPose(grip, blade, grip, blade, body, Vector3.back, Vector3.zero, grip, blade);
        SwordPose neutral = SwordPoseMath.Calculate(Quaternion.identity, false, settings, ref remembered);
        Vector3 aim = ShowdownCombat.CutAim(body, settings), velocity = Vector3.zero;
        SwordPose previous = default;
        bool active = false;
        for (int frame = 0; frame <= Mathf.CeilToInt(ShowdownDifficulty.InitialSwingSeconds / dt); frame++)
        {
            float progress = Mathf.Clamp01(frame * dt / ShowdownDifficulty.InitialSwingSeconds);
            Vector3 direction = progress <= .5f ? Vector3.Slerp(ready, Vector3.forward, progress * 2f)
                : Vector3.Slerp(Vector3.forward, -ready, (progress - .5f) * 2f);
            SwordPose pose = npc ? ShowdownCombat.CutPose(aim, ready, progress, settings)
                : SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, direction), false, settings, ref remembered);
            if (frame == 0) { previous = pose; continue; }
            velocity = CombatContact.CutVelocity(velocity, previous.Direction, pose.Direction, dt);
            if (!active && Vector3.Angle(pose.Direction, Vector3.forward) > DuelMatch.AttackAngle)
            { previous = pose; continue; }
            active = true;
            var attack = new CombatPose(previous.Position, previous.Direction, pose.Position, pose.Direction,
                Vector3.zero, Vector3.forward, velocity, neutral.Position, Vector3.forward);
            CombatOutcome outcome = CombatContact.Resolve(AttackKind.Cut, attack, defence, true).Outcome;
            Assert.That(CombatExchange.Resolve(AttackKind.Cut, AttackKind.None, attack, defence, false, true).Contact.Outcome, Is.EqualTo(outcome));
            Assert.That(CombatExchange.Resolve(AttackKind.None, AttackKind.Cut, defence, attack, true, false).Contact.Outcome, Is.EqualTo(outcome),
                "Both player slots and NPC attacks use the same guard interception.");
            if (outcome != CombatOutcome.None) return outcome;
            previous = pose;
        }
        return CombatOutcome.None;
    }
}
