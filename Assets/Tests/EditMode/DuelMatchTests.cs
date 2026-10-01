using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class DuelMatchTests
{
    private DuelMatch match;
    private static CombatInput Upright => new CombatInput(90f, 0f);
    private static CombatInput Strike => new CombatInput(40f, 120f);

    [SetUp]
    public void SetUp()
    {
        match = new DuelMatch();
        match.Step(Upright, Upright, 0f, 0);
    }

    [TestCase(0, 3f, 1)]
    [TestCase(1, -3f, 0)]
    public void NormalHitIsSymmetric(int attacker, float push, int pushedPlayer)
    {
        CombatResult result = match.Step(attacker == 0 ? Strike : Upright,
            attacker == 1 ? Strike : Upright, 0f, 0.01);
        Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(result.Attacker, Is.EqualTo(attacker));
        Assert.That(result.PushedPlayer, Is.EqualTo(pushedPlayer));
        Assert.That(result.SignedPush, Is.EqualTo(push));
        Assert.That(result.HitStopSeconds, Is.EqualTo(0.06f));
        Assert.That(match.IsStunned(pushedPlayer, 0.2), Is.True);
        Assert.That(match.IsStunned(pushedPlayer, 0.27), Is.False);
    }

    [TestCase(0, 70f, CombatOutcome.GuardBreak, 1.5f, 1)]
    [TestCase(0, 70.01f, CombatOutcome.GuardSuccess, -1.5f, 0)]
    [TestCase(1, 70f, CombatOutcome.GuardBreak, -1.5f, 0)]
    [TestCase(1, 90f, CombatOutcome.GuardSuccess, 1.5f, 1)]
    public void GuardRequiresAnAngleStrictlyAboveSeventy(int attacker, float swordAngle,
        CombatOutcome outcome, float push, int pushedPlayer)
    {
        var guard = new CombatInput(90f, 0f, true);
        CombatResult result = match.Step(attacker == 0 ? Strike : guard,
            attacker == 1 ? Strike : guard, swordAngle, 0.01);
        Assert.That(result.Outcome, Is.EqualTo(outcome));
        Assert.That(result.SignedPush, Is.EqualTo(push));
        Assert.That(result.PushedPlayer, Is.EqualTo(pushedPlayer));
        Assert.That(result.HitStopSeconds, Is.EqualTo(outcome == CombatOutcome.GuardBreak ? 0.06f : 0.04f));
    }

    [Test]
    public void SimultaneousSwingsClashAndConsumeBothAttacks()
    {
        CombatResult result = match.Step(Strike, Strike, 85f, 0.01);
        Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Clash));
        Assert.That(result.SignedPush, Is.Zero);
        Assert.That(result.PushedPlayer, Is.EqualTo(-1));
        Assert.That(result.HitStopSeconds, Is.EqualTo(0.04f));
        match.Step(new CombatInput(35f, 100f), new CombatInput(35f, 100f), 0f, 0.3);
        Assert.That(match.Step(new CombatInput(30f, 100f), new CombatInput(30f, 100f), 0f, 0.31).Outcome,
            Is.EqualTo(CombatOutcome.None));
        match.Step(Upright, Upright, 0f, 0.4);
        Assert.That(match.Step(Strike, Strike, 0f, 0.41).Outcome, Is.EqualTo(CombatOutcome.Clash));
    }

    [TestCase(45f, 40f, CombatOutcome.Hit)]
    [TestCase(45.01f, 40f, CombatOutcome.None)]
    [TestCase(45f, 39.99f, CombatOutcome.None)]
    public void AttackThresholdsAreInclusive(float angle, float speed, CombatOutcome outcome)
    {
        Assert.That(match.Step(new CombatInput(angle, speed), Upright, 0f, 0.01).Outcome, Is.EqualTo(outcome));
    }

    [Test]
    public void SmallForwardStepsCanAttackAtHighFrameRates()
    {
        match.Step(new CombatInput(45.005f, 45f), Upright, 0f, 1);
        Assert.That(match.Step(new CombatInput(44.999f, 45f), Upright, 0f, 1.0001).Outcome,
            Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void MovingBackOrHoldingStillDoesNotAttack()
    {
        match.Step(new CombatInput(40f, 0f), Upright, 0f, 0.01);
        Assert.That(match.Step(new CombatInput(44f, 150f), Upright, 0f, 0.02).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(match.Step(new CombatInput(44f, 150f), Upright, 0f, 0.03).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(match.Step(new CombatInput(43f, 150f), Upright, 0f, 0.04).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void GuardTogglingCannotRearmASpentSwing()
    {
        match.Step(Strike, Upright, 0f, 0.01);
        match.Step(new CombatInput(35f, 100f), Upright, 0f, 0.3);
        match.Step(new CombatInput(30f, 100f, true), Upright, 0f, 0.31);
        Assert.That(match.Step(new CombatInput(25f, 100f), Upright, 0f, 0.32).Outcome, Is.EqualTo(CombatOutcome.None));
        match.Step(new CombatInput(79.99f, 150f), Upright, 0f, 0.4);
        Assert.That(match.Step(Strike, Upright, 0f, 0.41).Outcome, Is.EqualTo(CombatOutcome.None));
        match.Step(new CombatInput(80f, 150f), Upright, 0f, 0.5);
        Assert.That(match.Step(Strike, Upright, 0f, 0.51).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void GuardCanBeHeldWhileReturningToStance()
    {
        match.Step(Strike, Upright, 0f, 0.01);
        match.Step(new CombatInput(90f, 100f, true), Upright, 0f, 0.3);
        Assert.That(match.Step(Strike, Upright, 0f, 0.31).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void ResolutionDoesNotQueueASwing()
    {
        match.Step(Strike, Upright, 0f, 0.01);
        Assert.That(match.Step(Upright, Strike, 0f, 0.04).Outcome, Is.EqualTo(CombatOutcome.None));
        match.Step(Upright, new CombatInput(35f, 100f), 0f, 0.3);
        Assert.That(match.Step(Upright, new CombatInput(30f, 100f), 0f, 0.31).Outcome, Is.EqualTo(CombatOutcome.None));
        match.Step(Upright, Upright, 0f, 0.4);
        Assert.That(match.Step(Upright, Strike, 0f, 0.41).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void StunPreventsAttackAndGuardAfterThePushEnds()
    {
        match.Step(Strike, Upright, 0f, 0.01);
        match.Step(Upright, Upright, 0f, 0.2);
        Assert.That(match.Step(Upright, Strike, 0f, 0.205).Outcome, Is.EqualTo(CombatOutcome.None));
        CombatResult result = match.Step(Strike, new CombatInput(90f, 0f, true), 90f, 0.21);
        Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void InvalidPoseDisablesGuard()
    {
        Assert.That(match.Step(Strike, new CombatInput(90f, 0f, true, false), 90f, 0.01).Outcome,
            Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void StalePoseMustReturnToStanceBeforeAttacking()
    {
        match.Step(new CombatInput(90f, 0f, valid: false), Upright, 0f, 0.01);
        Assert.That(match.Step(Strike, Upright, 0f, 0.02).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(match.Step(new CombatInput(30f, 150f), Upright, 0f, 0.03).Outcome, Is.EqualTo(CombatOutcome.None));
        match.Step(Upright, Upright, 0f, 0.04);
        Assert.That(match.Step(Strike, Upright, 0f, 0.05).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void RecenterOrReconnectGenerationCannotProduceAnInstantAttack()
    {
        Assert.That(match.Step(new CombatInput(40f, 300f, poseGeneration: 1), Upright, 0f, 0.01).Outcome,
            Is.EqualTo(CombatOutcome.None));
        Assert.That(match.Step(new CombatInput(30f, 300f, poseGeneration: 1), Upright, 0f, 0.02).Outcome,
            Is.EqualTo(CombatOutcome.None));
        match.Step(new CombatInput(90f, 0f, poseGeneration: 1), Upright, 0f, 0.03);
        Assert.That(match.Step(new CombatInput(40f, 150f, poseGeneration: 1), Upright, 0f, 0.04).Outcome,
            Is.EqualTo(CombatOutcome.Hit));
    }

    [TestCase(15f, 0f, MatchWinner.Player2)]
    [TestCase(0f, -15f, MatchWinner.Player1)]
    [TestCase(15f, -15f, MatchWinner.Draw)]
    public void ReachingTheBoundaryEndsTheRound(float firstZ, float secondZ, MatchWinner winner)
    {
        Assert.That(match.CheckRingOut(new Vector2(0f, firstZ), new Vector2(0f, secondZ), 15f), Is.EqualTo(winner));
        Assert.That(match.CheckRingOut(Vector2.zero, Vector2.zero, 15f), Is.EqualTo(winner));
        Assert.That(match.Step(Strike, Strike, 0f, 1).Outcome, Is.EqualTo(CombatOutcome.None));
    }

    [Test]
    public void RingOutUsesRadialDistance()
    {
        Assert.That(match.CheckRingOut(new Vector2(10f, 10f), Vector2.zero, 15f), Is.EqualTo(MatchWinner.None));
        Assert.That(match.CheckRingOut(new Vector2(11f, 11f), Vector2.zero, 15f), Is.EqualTo(MatchWinner.Player2));
    }

    [Test]
    public void RematchClearsWinnerResolutionStunAndSpentSwings()
    {
        match.Step(Strike, Upright, 0f, 0.01);
        match.CheckRingOut(Vector2.zero, new Vector2(0f, 15f), 15f);
        match.Reset();
        Assert.That(match.Winner, Is.EqualTo(MatchWinner.None));
        Assert.That(match.IsResolving(0.02), Is.False);
        Assert.That(match.IsStunned(1, 0.02), Is.False);
        Assert.That(match.Step(Strike, Strike, 0f, 0.02).Outcome, Is.EqualTo(CombatOutcome.None));
        match.Step(Upright, Upright, 0f, 0.03);
        Assert.That(match.Step(Upright, Strike, 0f, 0.04).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }
}
