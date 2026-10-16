using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class CombatExchangeTests
{
    [TestCase(AttackKind.Cut, false, false, CombatOutcome.Hit)]
    [TestCase(AttackKind.Cut, true, true, CombatOutcome.GuardSuccess)]
    [TestCase(AttackKind.Cut, true, false, CombatOutcome.GuardBreak)]
    public void OneMovingBladeHasTheSameHitAndGuardRulesFromEitherSlot(
        AttackKind kind, bool heldGuard, bool effectiveGuard, CombatOutcome outcome)
    {
        CombatPose attack = Attack(kind);
        CombatPose defence = Guard(Vector3.forward * 3.2f,
            effectiveGuard ? Vector3.right : Vector3.up);
        if (heldGuard && !effectiveGuard)
            defence = OffsetBlade(defence, Vector3.right * 4f);

        CombatExchangeResult first = CombatExchange.Resolve(kind, AttackKind.None,
            attack, defence, false, heldGuard);
        CombatExchangeResult second = CombatExchange.Resolve(AttackKind.None, kind,
            defence, attack, heldGuard, false);

        Assert.That(first.Contact.Outcome, Is.EqualTo(outcome));
        Assert.That(first.Contact.Attacker, Is.Zero);
        Assert.That(first.Contact.PushedPlayer, Is.EqualTo(outcome == CombatOutcome.GuardSuccess ? 0 : 1));
        Assert.That(first.Contact.Kind, Is.EqualTo(kind));
        Assert.That(first.Contact.SignedPush, Is.EqualTo(Push(kind, outcome)).Within(.00001f));
        AssertSwapped(first, second);
    }

    [TestCase(AttackKind.Cut, AttackKind.Cut, false)]
    [TestCase(AttackKind.Cut, AttackKind.Cut, true)]
    public void TwoAttackingBladesResolveTheEarlierBodyContactSymmetrically(
        AttackKind firstKind, AttackKind secondKind, bool heldGuard)
    {
        // Two parallel lanes are inside the torso radius but farther apart
        // than blade padding. Both attacks hit, without touching each other.
        CombatPose firstPose = LaneAttack(firstKind, false);
        CombatPose secondPose = LaneAttack(secondKind, true);
        CombatContactResult a = CombatContact.Resolve(firstKind, firstPose, secondPose, heldGuard);
        CombatContactResult b = CombatContact.Resolve(secondKind, secondPose, firstPose, heldGuard);
        CombatOutcome outcome = heldGuard ? CombatOutcome.GuardBreak : CombatOutcome.Hit;
        Assert.That(a.Outcome, Is.EqualTo(outcome));
        Assert.That(b.Outcome, Is.EqualTo(outcome));
        Assert.That(a.Fraction, Is.GreaterThan(0f).And.LessThan(b.Fraction));
        Assert.That(b.Fraction, Is.LessThan(1f));
        Assert.That(CombatContact.TryBladesClash(firstPose, secondPose, out _, firstKind, secondKind), Is.False);

        CombatExchangeResult first = CombatExchange.Resolve(firstKind, secondKind,
            firstPose, secondPose, heldGuard, heldGuard);
        CombatExchangeResult second = CombatExchange.Resolve(secondKind, firstKind,
            secondPose, firstPose, heldGuard, heldGuard);

        Assert.That(first.Contact.Outcome, Is.EqualTo(outcome));
        Assert.That(first.Contact.Attacker, Is.Zero);
        Assert.That(first.Contact.PushedPlayer, Is.EqualTo(1));
        Assert.That(first.Contact.Kind, Is.EqualTo(firstKind));
        Assert.That(first.Contact.SignedPush, Is.EqualTo(Push(firstKind, outcome)).Within(.00001f));
        Assert.That(first.Fraction, Is.EqualTo(a.Fraction).Within(.00001f));
        AssertSwapped(first, second);
    }

    [Test]
    public void AClashBeforeBothBodyContactsStopsBothCuts()
    {
        CrossingCuts(false, out CombatPose firstPose, out CombatPose secondPose);
        CombatContactResult a = CombatContact.Resolve(AttackKind.Cut, firstPose, secondPose, false);
        CombatContactResult b = CombatContact.Resolve(AttackKind.Cut, secondPose, firstPose, false);
        Assert.That(a.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(b.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(CombatContact.TryBladesClash(firstPose, secondPose, out float clash), Is.True);
        Assert.That(clash, Is.GreaterThan(0f).And.LessThan(a.Fraction).And.LessThan(b.Fraction));

        foreach (bool swap in new[] { false, true })
        {
            CombatExchangeResult result = CombatExchange.Resolve(AttackKind.Cut, AttackKind.Cut,
                swap ? secondPose : firstPose, swap ? firstPose : secondPose, false, false);
            Assert.That(result.Contact.Outcome, Is.EqualTo(CombatOutcome.Clash));
            Assert.That(result.Contact.Attacker, Is.EqualTo(-1));
            Assert.That(result.Contact.PushedPlayer, Is.EqualTo(-1));
            Assert.That(result.Contact.SignedPush, Is.Zero);
            Assert.That(result.Fraction, Is.EqualTo(clash).Within(.00001f));
        }
    }

    [Test]
    public void AClashAfterOneBodyContactCannotOverrideThatHitEvenIfItPrecedesTheOtherHit()
    {
        CrossingCuts(true, out CombatPose firstPose, out CombatPose secondPose);
        CombatContactResult a = CombatContact.Resolve(AttackKind.Cut, firstPose, secondPose, false);
        CombatContactResult b = CombatContact.Resolve(AttackKind.Cut, secondPose, firstPose, false);
        Assert.That(a.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(b.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(CombatContact.TryBladesClash(firstPose, secondPose, out float clash), Is.True);
        Assert.That(a.Fraction, Is.LessThan(clash));
        Assert.That(clash, Is.LessThan(b.Fraction));

        CombatExchangeResult first = CombatExchange.Resolve(AttackKind.Cut, AttackKind.Cut,
            firstPose, secondPose, false, false);
        CombatExchangeResult second = CombatExchange.Resolve(AttackKind.Cut, AttackKind.Cut,
            secondPose, firstPose, false, false);
        Assert.That(first.Contact.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(first.Contact.Attacker, Is.Zero);
        Assert.That(first.Contact.PushedPlayer, Is.EqualTo(1));
        Assert.That(first.Fraction, Is.EqualTo(a.Fraction).Within(.00001f));
        AssertSwapped(first, second);
    }

    [TestCase(AttackKind.Cut)]
    public void RecoverySuppressesOnlyItsOwnAttackAndKeepsGuardAvailable(AttackKind kind)
    {
        CombatPose attack = Attack(kind), defence = Guard(Vector3.forward * 3.2f, Vector3.right);
        Assert.That(CombatExchange.Resolve(kind, AttackKind.None, attack, defence, false, false,
            true, false).Contact.Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(CombatExchange.Resolve(kind, AttackKind.None, attack, defence, false, false,
            false, true).Contact.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(CombatExchange.Resolve(kind, AttackKind.None, attack, defence, false, true,
            false, true).Contact.Outcome, Is.EqualTo(CombatOutcome.GuardSuccess));
        Assert.That(CombatExchange.Resolve(AttackKind.None, kind, defence, attack, true, false,
            true, false).Contact.Outcome, Is.EqualTo(CombatOutcome.GuardSuccess));
    }

    private static CombatPose Attack(AttackKind kind)
    {
        Vector3 grip = new Vector3(0f, .15f, .75f);
        return new CombatPose(grip, Vector3.up, grip, Vector3.forward, Vector3.zero,
            Vector3.forward, Vector3.down, grip, Vector3.forward);
    }

    private static CombatPose Guard(Vector3 body, Vector3 direction)
    {
        Quaternion facing = Quaternion.LookRotation(Vector3.back);
        Vector3 blade = facing * direction.normalized;
        Vector3 grip = body + Vector3.back * .55f + Vector3.up * .15f - blade * .9f;
        return new CombatPose(grip, blade, grip, blade, body, Vector3.back, Vector3.zero, grip, blade);
    }

    private static CombatPose OffsetBlade(CombatPose pose, Vector3 offset)
        => new CombatPose(pose.PreviousGrip + offset, pose.PreviousBlade, pose.Grip + offset,
            pose.Blade, pose.BodyCenter, pose.Forward, pose.CutDirection, pose.EndGrip + offset, pose.EndBlade);

    private static CombatPose LaneAttack(AttackKind kind, bool second)
    {
        Vector3 forward = second ? Vector3.back : Vector3.forward;
        Vector3 body = second ? Vector3.forward * 3f : Vector3.zero;
        Vector3 grip = new Vector3(second ? -.3f : .3f, 0f, second ? 2f : 1f);
        Vector3 blade = second ? Vector3.up : Quaternion.AngleAxis(-45f, Vector3.right) * Vector3.forward;
        return new CombatPose(grip, blade, grip, forward, body, forward, Vector3.down, grip, forward);
    }

    private static void CrossingCuts(bool lateClash, out CombatPose first, out CombatPose second)
    {
        Vector3 a = new Vector3(0f, 0f, .55f);
        Vector3 b = lateClash ? new Vector3(0f, .4f, 2.3f) : new Vector3(0f, 0f, 1.4f);
        first = new CombatPose(a, Vector3.up, a, Vector3.forward, Vector3.zero,
            Vector3.forward, Vector3.down, a, Vector3.forward);
        second = new CombatPose(b, lateClash ? Vector3.right : Vector3.up, b, Vector3.back,
            Vector3.forward * 2f, Vector3.back, Vector3.left, b, Vector3.back);
    }

    private static float Push(AttackKind kind, CombatOutcome outcome)
        => outcome == CombatOutcome.GuardSuccess ? -DuelMatch.GuardPush
            : outcome == CombatOutcome.GuardBreak ? DuelMatch.GuardBreakPush
            : DuelMatch.HitPush;

    private static void AssertSwapped(CombatExchangeResult first, CombatExchangeResult second)
    {
        Assert.That(second.Contact.Outcome, Is.EqualTo(first.Contact.Outcome));
        Assert.That(second.Contact.Attacker, Is.EqualTo(1 - first.Contact.Attacker));
        Assert.That(second.Contact.PushedPlayer, Is.EqualTo(1 - first.Contact.PushedPlayer));
        Assert.That(second.Contact.Kind, Is.EqualTo(first.Contact.Kind));
        Assert.That(second.Contact.SignedPush, Is.EqualTo(-first.Contact.SignedPush).Within(.00001f));
        Assert.That(second.Contact.HitStopSeconds, Is.EqualTo(first.Contact.HitStopSeconds));
        Assert.That(second.Fraction, Is.EqualTo(first.Fraction).Within(.00001f));
    }
}
