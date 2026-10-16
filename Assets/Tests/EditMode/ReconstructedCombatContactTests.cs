using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class ReconstructedCombatContactTests
{
    private static CombatPose Turn(CombatPose pose, float yaw)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        return new CombatPose(q * pose.PreviousGrip, q * pose.PreviousBlade, q * pose.Grip, q * pose.Blade,
            q * pose.BodyCenter, q * pose.Forward, q * pose.CutDirection, q * pose.EndGrip, q * pose.EndBlade);
    }
    private static CombatPose Guard(Vector3 direction, Vector3 body)
    {
        Quaternion facing = Quaternion.Euler(0f, 180f, 0f);
        var settings = new SwordPoseSettings(Vector3.zero); Vector3 remembered = Vector3.up;
        SwordPose pose = SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, direction), true, settings, ref remembered);
        Vector3 grip = body + facing * pose.Position, blade = facing * pose.Direction;
        return new CombatPose(grip, blade, grip, blade, body, Vector3.back, Vector3.zero, grip, blade);
    }
    private static CombatPose Cut(Vector3 ready, float progress, float previousProgress, bool npc)
    {
        var settings = new SwordPoseSettings(Vector3.zero); Vector3 remembered = Vector3.up;
        Vector3 aim = npc ? ShowdownCombat.CutAim(Vector3.forward * 2.35f, settings) : Vector3.forward;
        SwordPose Previous(float value)
        {
            if (npc) return ShowdownCombat.CutPose(aim, ready, value, settings);
            Vector3 blade = value <= .5f ? Vector3.Slerp(ready, Vector3.forward, value * 2f)
                : Vector3.Slerp(Vector3.forward, -ready, (value - .5f) * 2f);
            return SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, blade), false, settings, ref remembered);
        }
        SwordPose old = Previous(previousProgress), current = Previous(progress);
        return new CombatPose(old.Position, old.Direction, current.Position, current.Direction, Vector3.zero,
            Vector3.forward, current.Direction - old.Direction, Vector3.one * 40f, Vector3.up);
    }

    [Test]
    public void GuardEffectsStayOnTheBladeAtTheRecordedImpactTime()
    {
        var first = new CombatPose(Vector3.forward, Vector3.right, Vector3.forward, Vector3.forward,
            Vector3.zero, Vector3.forward, Vector3.right, Vector3.zero, Vector3.forward);
        Vector3 grip = Vector3.forward * 2f;
        var second = new CombatPose(grip, Vector3.up, grip + Vector3.right, Vector3.up,
            Vector3.forward * 2.4f, Vector3.back, Vector3.zero, grip, Vector3.up);
        var result = new CombatResult(CombatOutcome.GuardSuccess, 0, 0, -2f, contactFraction: .25f);
        Vector3 point = CombatContact.ContactPoint(result, first, second);
        Vector3 atImpact = Vector3.Lerp(second.PreviousGrip, second.Grip, .25f);
        float bladeDistance = Vector3.Dot(point - atImpact, Vector3.up);
        Assert.That(bladeDistance, Is.InRange(CombatContact.GuardBladeStart, CombatContact.BladeLength));
        Assert.That((point - atImpact - Vector3.up * bladeDistance).sqrMagnitude, Is.LessThan(.000001f));
    }

    [Test]
    public void FrozenSwordPoseUsesTheObservedImpactInsteadOfItsLaterFrameEndpoint()
    {
        Vector3 previousGrip = new Vector3(.3f, .6f, .4f), grip = new Vector3(-.2f, .1f, .7f);
        Quaternion rotation = Quaternion.LookRotation(Vector3.forward) * Quaternion.AngleAxis(31f, Vector3.forward);
        var motion = new CombatPose(previousGrip, Vector3.up, grip, Vector3.forward,
            Vector3.zero, Vector3.forward, Vector3.down, Vector3.one * 100f, Vector3.back);
        SwordPose impact = CombatContact.SampleSwordPose(motion, rotation, .25f);
        Assert.That(Vector3.Distance(impact.Position, Vector3.Lerp(previousGrip, grip, .25f)), Is.LessThan(.00001f));
        Assert.That(Vector3.Angle(impact.Direction, Vector3.Slerp(Vector3.up, Vector3.forward, .25f)), Is.LessThan(.01f));
        Assert.That(Vector3.Angle(impact.Direction, motion.Blade), Is.GreaterThan(60f));
        Assert.That(Quaternion.Angle(CombatContact.SampleSwordPose(motion, rotation, 1f).Rotation, rotation), Is.LessThan(.01f));
    }

    [TestCase(0, 2, true)] [TestCase(0, 6, true)]
    [TestCase(2, 0, true)] [TestCase(2, 4, true)]
    [TestCase(6, 0, true)] [TestCase(6, 4, true)]
    [TestCase(0, 0, false)] [TestCase(0, 4, false)]
    [TestCase(2, 2, false)] [TestCase(2, 6, false)]
    [TestCase(1, 1, true)] [TestCase(7, 7, true)]
    public void NaturalSweepsUseCrossedAndParallelGuardsIdenticallyForNpcPlayersAndBothSeats(int readyOctant, int guardOctant, bool blocks)
    {
        foreach (int fps in new[] { 30, 60, 144 }) foreach (bool npc in new[] { false, true })
        foreach (float yaw in new[] { 0f, 37f, 180f })
        {
            CombatPose defence = Turn(Guard(ShowdownCombat.ReadyDirection(guardOctant), Vector3.forward * 2.35f), yaw);
            CombatOutcome actual = CombatOutcome.None;
            float previous = 0f;
            for (int frame = 1; frame <= Mathf.CeilToInt(ShowdownDifficulty.InitialSwingSeconds * fps); frame++)
            {
                float progress = Mathf.Clamp01(frame / (ShowdownDifficulty.InitialSwingSeconds * fps));
                CombatPose attack = Turn(Cut(ShowdownCombat.ReadyDirection(readyOctant), progress, previous, npc), yaw);
                previous = progress;
                // Preparation is harmless; only the entering front half starts a cut.
                if (progress < .25f) continue;
                actual = CombatContact.Resolve(AttackKind.Cut, attack, defence, true).Outcome;
                Assert.That(CombatExchange.Resolve(AttackKind.Cut, AttackKind.None, attack, defence, false, true).Contact.Outcome, Is.EqualTo(actual));
                Assert.That(CombatExchange.Resolve(AttackKind.None, AttackKind.Cut, defence, attack, true, false).Contact.Outcome, Is.EqualTo(actual));
                if (actual != CombatOutcome.None) break;
            }
            Assert.That(actual, Is.EqualTo(blocks ? CombatOutcome.GuardSuccess : CombatOutcome.GuardBreak),
                "ready=" + readyOctant + " guard=" + guardOctant + " fps=" + fps + " npc=" + npc + " yaw=" + yaw);
        }
    }

    [Test]
    public void FutureNeutralEndpointsNeverConvertAMissedGuardIntoABlock()
    {
        Vector3 grip = Vector3.forward * .5f, guardGrip = new Vector3(.1f, 1.2f, 2f);
        var attack = new CombatPose(grip, new Vector3(0f, .1f, 1f).normalized, grip, Vector3.forward,
            Vector3.zero, Vector3.forward, Vector3.down, new Vector3(.8f, 1.2f, .5f), Vector3.forward);
        var defence = new CombatPose(guardGrip, Vector3.right, guardGrip, Vector3.right,
            Vector3.forward * 2.3f, Vector3.back, Vector3.zero, guardGrip, Vector3.right);
        Assert.That(CombatContact.GuardInterceptsCommittedCut(attack.Grip, attack.Blade, attack.EndGrip, attack.EndBlade,
            defence.Grip, defence.Blade), Is.True, "The unused imaginary continuation would reach the guard.");
        Assert.That(CombatContact.Resolve(AttackKind.Cut, attack, defence, true).Outcome, Is.EqualTo(CombatOutcome.GuardBreak));
        var differentFuture = new CombatPose(attack.PreviousGrip, attack.PreviousBlade, attack.Grip, attack.Blade,
            attack.BodyCenter, attack.Forward, attack.CutDirection, Vector3.one * 100f, Vector3.back);
        Assert.That(CombatContact.Resolve(AttackKind.Cut, differentFuture, defence, true).Outcome, Is.EqualTo(CombatOutcome.GuardBreak));
    }

    [Test]
    public void StoppedAndOutOfRangeBladesDoNotHitThroughDirectionCoverage()
    {
        CombatPose moving = Cut(Vector3.up, .5f, .45f, false), defence = Guard(Vector3.right, Vector3.forward * 20f);
        Assert.That(CombatContact.Resolve(AttackKind.Cut, moving, defence, true).Outcome, Is.EqualTo(CombatOutcome.None));
        defence = Guard(Vector3.right, Vector3.forward * 2.35f);
        var stopped = new CombatPose(moving.Grip, moving.Blade, moving.Grip, moving.Blade,
            moving.BodyCenter, moving.Forward, moving.CutDirection, defence.Grip, defence.Blade);
        Assert.That(CombatContact.Resolve(AttackKind.Cut, stopped, defence, true).Outcome, Is.EqualTo(CombatOutcome.None));
    }

    [Test]
    public void AHighGuardCannotMoveALowRealCutIntoAnImaginaryChestCenteredLine()
    {
        CombatPose defence = Guard(Vector3.up, Vector3.forward * 2.35f);
        Vector3 grip = new Vector3(0f, -1.1f, .55f);
        var attack = new CombatPose(grip, Quaternion.Euler(0f, -15f, 0f) * Vector3.forward,
            grip, Vector3.forward, Vector3.zero, Vector3.forward, Vector3.right,
            Vector3.one * 100f, Vector3.forward);
        Assert.That(CombatContact.TrySweptBladeTouchesBody(attack.PreviousGrip, attack.PreviousBlade,
            attack.Grip, attack.Blade, defence.BodyCenter, out _), Is.True);
        Assert.That(CombatContact.TrySweptBladeTouchesBlade(attack.PreviousGrip, attack.PreviousBlade,
            attack.Grip, attack.Blade, defence.Grip, defence.Blade, out _), Is.False);
        Assert.That(CombatContact.Resolve(AttackKind.Cut, attack, defence, true).Outcome,
            Is.EqualTo(CombatOutcome.GuardBreak), "The crossed guard must cover the actual cut location as well as its direction.");
    }

}
