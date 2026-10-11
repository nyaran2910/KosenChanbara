using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class CombatContactTests
{
    private static CombatPose Thrust()
        => new CombatPose(new Vector3(0f, .15f, .55f), Vector3.forward,
            new Vector3(0f, .15f, .75f), Vector3.forward, Vector3.zero, Vector3.forward,
            Vector3.forward, new Vector3(0f, .15f, 1.1f), Vector3.forward);

    private static CombatPose Guard(Vector3 direction)
    {
        Vector3 body = Vector3.forward * 2.4f;
        Quaternion facing = Quaternion.LookRotation(Vector3.back);
        Vector3 blade = facing * direction.normalized;
        // Aim the actual blade's middle across this thrust, rather than
        // assuming a guard toggle shields the whole chest automatically.
        Vector3 grip = body + Vector3.back * .55f + Vector3.up * .15f - blade * .9f;
        return new CombatPose(grip, blade, grip, blade, body, Vector3.back, Vector3.zero, grip, blade);
    }

    [TestCase(.2f, false)]
    [TestCase(.29f, false)]
    [TestCase(.31f, true)]
    [TestCase(1f, true)]
    public void GuardPaddingDoesNotExtendIntoTheHand(float height, bool blocks)
    {
        Vector3 grip = new Vector3(0f, height, -1f);
        Assert.That(CombatContact.TrySweptBladeTouchesBlade(grip, Vector3.forward,
            grip, Vector3.forward, Vector3.zero, Vector3.up, out _), Is.EqualTo(blocks));
        Assert.That(CombatContact.GuardInterceptsCommittedCut(grip, Vector3.forward,
            grip, Vector3.forward, Vector3.zero, Vector3.up), Is.EqualTo(blocks));
    }

    [Test]
    public void ARealHiltStrikeCannotBecomeAGuardThroughThePredictedContinuation()
    {
        Vector3 grip = Vector3.forward * .5f, guardGrip = Vector3.forward * 2f;
        var attack = new CombatPose(grip, new Vector3(0f, .1f, 1f).normalized, grip, Vector3.forward,
            Vector3.zero, Vector3.forward, Vector3.down, grip + Vector3.up * 1.1f, Vector3.forward);
        var defence = new CombatPose(guardGrip, Vector3.up, guardGrip, Vector3.up,
            Vector3.forward * 2.3f, Vector3.back, Vector3.zero, guardGrip, Vector3.up);
        Assert.That(CombatContact.TrySweptBladeTouchesBlade(attack.PreviousGrip, attack.PreviousBlade,
            attack.Grip, attack.Blade, defence.Grip, defence.Blade, out _), Is.False);
        Assert.That(CombatContact.GuardInterceptsCommittedCut(attack.Grip, attack.Blade, attack.EndGrip,
            attack.EndBlade, defence.Grip, defence.Blade), Is.True, "The virtual endpoint used to falsely rescue this hilt strike.");
        Assert.That(CombatContact.Resolve(AttackKind.Cut, attack, defence, true).Outcome, Is.EqualTo(CombatOutcome.GuardBreak));
    }

    [Test]
    public void SwordToleranceIsSmallerButGlancingBodyHitsKeepTheirExistingTolerance()
    {
        Vector3 grip = Vector3.zero;
        Assert.That(CombatContact.TrySweptBladeTouchesBlade(grip, Vector3.forward, grip, Vector3.forward,
            new Vector3(.30f, -1f, 1f), Vector3.up, out _), Is.True);
        Assert.That(CombatContact.TrySweptBladeTouchesBlade(grip, Vector3.forward, grip, Vector3.forward,
            new Vector3(.38f, -1f, 1f), Vector3.up, out _), Is.False);
        Assert.That(CombatContact.TrySweptBladeTouchesBody(grip, Vector3.forward, grip, Vector3.forward,
            new Vector3(.55f, .15f, 1.4f), out _), Is.True);
        Assert.That(CombatContact.BladePadding, Is.EqualTo(.23f));
        Assert.That(CombatContact.ThrustExtraPadding, Is.EqualTo(.1f));
    }

    [TestCase(.2f, CombatOutcome.GuardBreak)]
    [TestCase(1f, CombatOutcome.GuardSuccess)]
    public void ThrustAtTheHandCannotBeSavedByTheBroadFrontGuardRegion(float height, CombatOutcome expected)
    {
        var attack = new CombatPose(new Vector3(0f, height, -2f), Vector3.forward,
            new Vector3(0f, height, -1f), Vector3.forward, Vector3.back * 2f,
            Vector3.forward, Vector3.forward, Vector3.zero, Vector3.forward);
        var defence = new CombatPose(Vector3.zero, Vector3.up, Vector3.zero, Vector3.up,
            Vector3.up * height, Vector3.back, Vector3.zero, Vector3.zero, Vector3.up);
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, attack, defence, true).Outcome, Is.EqualTo(expected));
    }

    [TestCase(0f, 1f)] [TestCase(0f, -1f)] [TestCase(1f, 0f)] [TestCase(-1f, 0f)]
    [TestCase(1f, 1f)] [TestCase(-1f, 1f)] [TestCase(1f, -1f)] [TestCase(-1f, -1f)]
    public void ThrustRequiresBladeContactAndUsesTheSameGuardRuleInSharedResolverAndDuel(float x, float y)
    {
        CombatPose attack = Thrust(), guard = Guard(new Vector3(x, y, 0f).normalized);
        bool touchesBlade = true;
        CombatOutcome expected = touchesBlade ? CombatOutcome.GuardSuccess : CombatOutcome.GuardBreak;
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, attack, guard, true).Outcome, Is.EqualTo(expected));
        var match = new DuelMatch();
        var held = new CombatInput(90f, 0f, true);
        match.Step(new CombatInput(0f, 0f), held, attack, guard, 0d);
        CombatResult result = match.Step(new CombatInput(0f, 0f, thrustRequested: true), held, attack, guard, .01d);
        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.Kind, Is.EqualTo(AttackKind.Thrust));
        Assert.That(result.SignedPush, Is.EqualTo(touchesBlade ? -DuelMatch.GuardPush : DuelMatch.GuardBreakPush));
    }

    [Test]
    public void AnUnguardedThrustHasFortyPercentMorePushAndRequiresRealFiniteContact()
    {
        CombatPose attack = Thrust(), defender = Guard(Vector3.up);
        var match = new DuelMatch();
        var input = new CombatInput(0f, 0f);
        match.Step(input, input, attack, defender, 0d);
        CombatResult result = match.Step(new CombatInput(0f, 0f, thrustRequested: true), input, attack, defender, .01d);
        Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(result.Kind, Is.EqualTo(AttackKind.Thrust));
        Assert.That(result.SignedPush, Is.EqualTo(DuelMatch.HitPush));
        CombatPose distant = new CombatPose(defender.Grip + Vector3.forward * 5f, defender.Blade,
            defender.Grip + Vector3.forward * 5f, defender.Blade, defender.BodyCenter + Vector3.forward * 5f,
            defender.Forward, Vector3.zero, defender.EndGrip + Vector3.forward * 5f, defender.EndBlade);
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, attack, distant, false).Outcome, Is.EqualTo(CombatOutcome.None));
        CombatPose remoteGuard = new CombatPose(defender.Grip + Vector3.right * 4f, defender.Blade,
            defender.Grip + Vector3.right * 4f, defender.Blade, defender.BodyCenter, defender.Forward,
            Vector3.zero, defender.EndGrip + Vector3.right * 4f, defender.EndBlade);
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, attack, remoteGuard, true).Outcome, Is.EqualTo(CombatOutcome.GuardBreak));
    }

    [Test]
    public void WiderBladeFindsBodyGlancingContactsWithAdditionalThrustTolerance()
    {
        CombatPose attack = Thrust();
        Vector3 target = new Vector3(.55f, .15f, 2.4f);
        Assert.That(CombatContact.TrySweptBladeTouchesSphere(attack.PreviousGrip, attack.PreviousBlade,
            attack.Grip, attack.Blade, target, CombatContact.BodyRadius, out _), Is.False);
        Assert.That(CombatContact.TrySweptBladeTouchesBody(attack.PreviousGrip, attack.PreviousBlade,
            attack.Grip, attack.Blade, target, out _), Is.True);
        target.x = .7f;
        Assert.That(CombatContact.TrySweptBladeTouchesBody(attack.PreviousGrip, attack.PreviousBlade,
            attack.Grip, attack.Blade, target, out _), Is.False);
        var defender = new CombatPose(target, Vector3.up, target, Vector3.up, target,
            Vector3.back, Vector3.zero, target, Vector3.up);
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, attack, defender, false).Outcome, Is.EqualTo(CombatOutcome.Hit));
        target.x = .8f;
        defender = new CombatPose(target, Vector3.up, target, Vector3.up, target,
            Vector3.back, Vector3.zero, target, Vector3.up);
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, attack, defender, false).Outcome, Is.EqualTo(CombatOutcome.None));
    }

    [Test]
    public void WiderGuardAcceptsAGrazingSwordContactButStillRequiresNearbyBlades()
    {
        Vector3 grip = Vector3.zero;
        Assert.That(CombatContact.TrySweptBladeTouchesBlade(grip, Vector3.forward, grip,
            Vector3.forward, new Vector3(.3f, -1f, 1f), Vector3.up, out _), Is.True);
        Assert.That(CombatContact.TrySweptBladeTouchesBlade(grip, Vector3.forward, grip,
            Vector3.forward, new Vector3(.5f, -1f, 1f), Vector3.up, out _), Is.False);
    }

    [Test]
    public void ThrustRetractionCannotHitAfterTheForwardPartMissed()
    {
        CombatPose forward = Thrust(), target = Guard(Vector3.up);
        CombatPose retracting = new CombatPose(forward.Grip + Vector3.forward * .2f, forward.Blade,
            forward.Grip, forward.Blade, forward.BodyCenter, forward.Forward, forward.CutDirection, forward.EndGrip, forward.EndBlade);
        Assert.That(CombatContact.TrySweptBladeTouchesBody(retracting.PreviousGrip, retracting.PreviousBlade,
            retracting.Grip, retracting.Blade, target.BodyCenter, out _), Is.True, "The returning blade physically passes the target.");
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, retracting, target, false).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, forward, target, false).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void StaticUprightRemoteAndRearBladesNeverCauseAngleOnlyHits()
    {
        CombatPose attack = Thrust(), target = Guard(Vector3.up);
        CombatPose still = new CombatPose(attack.Grip, attack.Blade, attack.Grip, attack.Blade,
            attack.BodyCenter, attack.Forward, attack.CutDirection, attack.EndGrip, attack.EndBlade);
        Assert.That(CombatContact.Resolve(AttackKind.Cut, still, target, false).Outcome, Is.EqualTo(CombatOutcome.None));
        CombatPose upright = new CombatPose(attack.PreviousGrip, Vector3.up, attack.Grip, Vector3.up,
            attack.BodyCenter, attack.Forward, Vector3.forward, attack.EndGrip, Vector3.up);
        Assert.That(CombatContact.Resolve(AttackKind.Cut, upright, target, false).Outcome, Is.EqualTo(CombatOutcome.None));
        CombatPose rear = new CombatPose(attack.PreviousGrip, Vector3.back, attack.Grip, Vector3.back,
            attack.BodyCenter, attack.Forward, Vector3.forward, attack.EndGrip, Vector3.back);
        Assert.That(CombatContact.Resolve(AttackKind.Cut, rear, target, false).Outcome, Is.EqualTo(CombatOutcome.None));
        var match = new DuelMatch();
        match.Step(new CombatInput(90f, 0f), new CombatInput(90f, 0f), still, target, 0d);
        Assert.That(match.Step(new CombatInput(40f, 100f), new CombatInput(90f, 0f), still, target, .01d).Outcome,
            Is.EqualTo(CombatOutcome.None), "An accepted input without moving finite blade contact cannot hit in duel.");
    }

    [Test]
    public void GripBreathingCannotTurnAStationaryCutBladeIntoAnAttack()
    {
        CombatPose attack = Thrust(), target = Guard(Vector3.up);
        Assert.That(attack.Moving, Is.True, "The hand translates through the target.");
        Assert.That(CombatContact.TrySweptBladeTouchesBody(attack.PreviousGrip, attack.PreviousBlade,
            attack.Grip, attack.Blade, target.BodyCenter, out _), Is.True);
        Assert.That(CombatContact.Resolve(AttackKind.Cut, attack, target, false).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, attack, target, false).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void SimultaneousBladesClashAtTheirSharedIntermediateTime()
    {
        Vector3 a = new Vector3(0f, 0f, .55f), b = new Vector3(0f, 0f, 1.1f);
        var first = new CombatPose(a, Vector3.up, a, Vector3.forward, Vector3.zero,
            Vector3.forward, Vector3.down, a, Vector3.forward);
        var second = new CombatPose(b, Vector3.up, b, Vector3.back, Vector3.forward * 2f,
            Vector3.back, Vector3.down, b, Vector3.back);
        Assert.That(SwordContact.DistanceSquared(a + Vector3.up * CombatContact.BladeStart,
            a + Vector3.up * CombatContact.BladeLength, b + Vector3.up * CombatContact.BladeStart,
            b + Vector3.up * CombatContact.BladeLength), Is.GreaterThan(CombatContact.GuardPadding * CombatContact.GuardPadding),
            "Initial blade positions alone miss this clash.");
        Assert.That(SwordContact.DistanceSquared(a + Vector3.forward * CombatContact.BladeStart,
            a + Vector3.forward * CombatContact.BladeLength, b + Vector3.back * CombatContact.BladeStart,
            b + Vector3.back * CombatContact.BladeLength), Is.GreaterThan(CombatContact.GuardPadding * CombatContact.GuardPadding),
            "Final blade positions alone miss this clash.");
        Assert.That(CombatContact.TryBladesClash(first, second, out float fraction), Is.True);
        Assert.That(fraction, Is.GreaterThan(0f).And.LessThan(1f));
    }

    [Test]
    public void TheSharedStrokeKeepsDecelerationButCancelsGuardReturnExpiryAndInvalidSources()
    {
        var stroke = new AttackStroke();
        Assert.That(stroke.Update(new CombatInput(90f, 0f), 0d), Is.False);
        Assert.That(stroke.Update(new CombatInput(45f, 80f), .01d), Is.True);
        Assert.That(stroke.Kind, Is.EqualTo(AttackKind.Cut));
        Assert.That(stroke.Update(new CombatInput(20f, 5f), .1d), Is.True);
        Assert.That(stroke.Update(new CombatInput(21f, 5f), .11d), Is.False);
        stroke.Update(new CombatInput(90f, 0f), .2d);
        stroke.Update(new CombatInput(45f, 80f), .21d);
        Assert.That(stroke.Update(new CombatInput(20f, 5f), .21d + AttackStroke.CutSeconds + .01d), Is.False);
        stroke.Update(new CombatInput(90f, 0f), 1.2d);
        stroke.Update(new CombatInput(45f, 80f), 1.21d);
        Assert.That(stroke.Update(new CombatInput(20f, 5f, true), 1.22d), Is.False);
        Assert.That(stroke.Update(new CombatInput(10f, 5f), 1.23d), Is.False);
        Assert.That(stroke.Update(new CombatInput(0f, 0f, valid: false, thrustRequested: true), 1.3d), Is.False);
        Assert.That(stroke.Update(new CombatInput(0f, 0f, poseGeneration: 2, thrustRequested: true), 1.4d), Is.True);
        Assert.That(stroke.Kind, Is.EqualTo(AttackKind.Thrust), "A new controller's explicit fresh impulse is usable without reusing its old cut.");
        stroke.Reset();
        Assert.That(stroke.Update(new CombatInput(25.01f, 0f, thrustRequested: true), 2d), Is.False);
    }
}
