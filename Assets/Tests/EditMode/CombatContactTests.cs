using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class CombatContactTests
{
    private static CombatPose TranslatingSword()
        => new CombatPose(new Vector3(0f, .15f, .55f), Vector3.forward,
            new Vector3(0f, .15f, .75f), Vector3.forward, Vector3.zero, Vector3.forward,
            Vector3.forward, new Vector3(0f, .15f, 1.1f), Vector3.forward);

    private static CombatPose Guard(Vector3 direction)
    {
        Vector3 body = Vector3.forward * 2.4f;
        Quaternion facing = Quaternion.LookRotation(Vector3.back);
        Vector3 blade = facing * direction.normalized;
        // Aim the actual blade's middle across the incoming sword, rather than
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
    public void StaticUprightRemoteAndRearBladesNeverCauseAngleOnlyHits()
    {
        CombatPose attack = TranslatingSword(), target = Guard(Vector3.up);
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
        CombatPose attack = TranslatingSword(), target = Guard(Vector3.up);
        Assert.That(attack.Moving, Is.True, "The hand translates through the target.");
        Assert.That(CombatContact.TrySweptBladeTouchesBody(attack.PreviousGrip, attack.PreviousBlade,
            attack.Grip, attack.Blade, target.BodyCenter, out _), Is.True);
        Assert.That(CombatContact.Resolve(AttackKind.Cut, attack, target, false).Outcome, Is.EqualTo(CombatOutcome.None));
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
    public void TheSharedStrokeMakesSlowDecelerationHarmlessAndCancelsGuardReturnExpiryAndInvalidSources()
    {
        var stroke = new AttackStroke();
        Assert.That(stroke.Update(new CombatInput(90f, 0f), 0d), Is.False);
        Assert.That(stroke.Update(new CombatInput(45f, 200f), .01d), Is.True);
        Assert.That(stroke.Kind, Is.EqualTo(AttackKind.Cut));
        Assert.That(stroke.Update(new CombatInput(20f, 5f), .1d), Is.False);
        Assert.That(stroke.Update(new CombatInput(21f, 5f), .11d), Is.False);
        stroke.Update(new CombatInput(90f, 0f), .2d);
        stroke.Update(new CombatInput(45f, 200f), .21d);
        Assert.That(stroke.Update(new CombatInput(20f, 5f), .21d + AttackStroke.CutSeconds + .01d), Is.False);
        stroke.Update(new CombatInput(90f, 0f), 1.2d);
        stroke.Update(new CombatInput(45f, 200f), 1.21d);
        Assert.That(stroke.Update(new CombatInput(20f, 5f, true), 1.22d), Is.False);
        Assert.That(stroke.Update(new CombatInput(10f, 5f), 1.23d), Is.False);
        Assert.That(stroke.Update(new CombatInput(0f, 0f, valid: false), 1.3d), Is.False);
        Assert.That(stroke.Update(new CombatInput(0f, 0f, poseGeneration: 2), 1.4d), Is.False);
        Assert.That(stroke.Kind, Is.EqualTo(AttackKind.None));
        stroke.Reset();
        Assert.That(stroke.Update(new CombatInput(25.01f, 0f), 2d), Is.False);
    }
    [Test]
    public void ARealBodyOverlapOnTheOutwardTakebackIsNotAnAttack()
    {
        Vector3 grip=new Vector3(0f,.15f,.55f),body=Vector3.forward*2.2f;
        Vector3 raised=new Vector3(0f,1f,1f).normalized;
        var returning=new CombatPose(grip,Vector3.forward,grip,raised,Vector3.zero,Vector3.forward,
            Vector3.up,grip,raised);
        var defence=new CombatPose(body,Vector3.up,body,Vector3.up,body,Vector3.back,Vector3.zero,body,Vector3.up);
        Assert.That(CombatContact.TrySweptBladeTouchesBody(returning.PreviousGrip,returning.PreviousBlade,
            returning.Grip,returning.Blade,body,out _),Is.True);
        Assert.That(CombatContact.Resolve(AttackKind.Cut,returning,defence,false).Outcome,Is.EqualTo(CombatOutcome.None));
    }

    [Test]
    public void ACoarseAcrossFrontCutCanOnlyHitDuringItsIncomingHalfAndKeepsOriginalFrameTime()
    {
        Vector3 grip=Vector3.forward*.6f;
        Vector3 previous=Quaternion.Euler(0f,-75f,0f)*Vector3.forward,current=Quaternion.Euler(0f,75f,0f)*Vector3.forward;
        var attack=new CombatPose(grip,previous,grip,current,Vector3.zero,Vector3.forward,Vector3.right,grip,current);
        Vector3 body=Vector3.forward*2.2f;
        var defender=new CombatPose(body,Vector3.up,body,Vector3.up,body,Vector3.back,Vector3.zero,body,Vector3.up);
        CombatContactResult hit=CombatContact.Resolve(AttackKind.Cut,attack,defender,false);
        Assert.That(hit.Outcome,Is.EqualTo(CombatOutcome.Hit));
        Assert.That(hit.Fraction,Is.GreaterThan(0f).And.LessThanOrEqualTo(.5001f));
        body=new Vector3(1.7f,0f,1.6f);
        defender=new CombatPose(body,Vector3.up,body,Vector3.up,body,Vector3.back,Vector3.zero,body,Vector3.up);
        Assert.That(CombatContact.TrySweptBladeTouchesBody(grip,previous,grip,current,body,out _),Is.True);
        Assert.That(CombatContact.Resolve(AttackKind.Cut,attack,defender,false).Outcome,Is.EqualTo(CombatOutcome.None),
            "An observed collision only on the outward half is harmless.");
    }

}
