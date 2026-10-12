using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class DuelMatchTests
{
    private DuelMatch match;
    private CombatPose[] previousPoses;
    private bool[] poseBaseline, previousGuard;
    private static CombatInput Upright => new CombatInput(90f, 0f);
    private static CombatInput Strike => new CombatInput(40f, 120f);

    // Gate tests supply explicit finite blade/body contact instead of an
    // angle-only hit shortcut. Geometry-specific misses are tested separately.
    private CombatResult Step(CombatInput first, CombatInput second, float angle, double now)
    {
        CombatPose Pose(int index, CombatInput input)
        {
            Vector3 body = Vector3.forward * (index * 2f);
            Vector3 forward = index == 0 ? Vector3.forward : Vector3.back;
            Quaternion facing = Quaternion.LookRotation(forward);
            float guardedAngle = angle * Mathf.Deg2Rad;
            Vector3 blade = facing * (input.GuardHeld ? new Vector3(Mathf.Sin(guardedAngle), Mathf.Cos(guardedAngle), 0f)
                : Quaternion.AngleAxis(-input.ForwardAngle, Vector3.right) * Vector3.forward);
            // State tests deliberately route each blade through its target;
            // the observed directions still follow the supplied input history.
            Vector3 grip = input.GuardHeld ? body + forward * .55f + Vector3.up * .55f - blade * .6f
                : Vector3.forward * ((1 - index) * 2f) - blade * .7f;
            Vector3 oldGrip = poseBaseline[index] && (!input.GuardHeld || previousGuard[index]) ? previousPoses[index].Grip : grip;
            Vector3 oldBlade = poseBaseline[index] && (!input.GuardHeld || previousGuard[index]) ? previousPoses[index].Blade : blade;
            var result = new CombatPose(oldGrip, oldBlade, grip, blade, body, forward, blade - oldBlade, grip, forward);
            previousPoses[index] = result; poseBaseline[index] = true; previousGuard[index] = input.GuardHeld;
            return result;
        }
        return match.Step(first, second, Pose(0, first), Pose(1, second), now);
    }

    [SetUp]
    public void SetUp()
    {
        match = new DuelMatch();
        previousPoses = new CombatPose[2]; poseBaseline = new bool[2]; previousGuard = new bool[2];
        Step(Upright, Upright, 0f, 0);
    }

    [TestCase(0, 3f, 1)]
    [TestCase(1, -3f, 0)]
    public void NormalHitIsSymmetric(int attacker, float push, int pushedPlayer)
    {
        CombatResult result = Step(attacker == 0 ? Strike : Upright,
            attacker == 1 ? Strike : Upright, 0f, 0.01);
        Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(result.Attacker, Is.EqualTo(attacker));
        Assert.That(result.PushedPlayer, Is.EqualTo(pushedPlayer));
        Assert.That(result.SignedPush, Is.EqualTo(push));
        Assert.That(result.HitStopSeconds, Is.EqualTo(0.06f));
        Assert.That(match.IsStunned(pushedPlayer, 0.2), Is.True);
        Assert.That(match.IsStunned(pushedPlayer, 0.27), Is.True);
        Assert.That(match.IsRecovering(pushedPlayer, 0.55), Is.True);
        Assert.That(match.IsStunned(pushedPlayer, 1.08), Is.False);
    }

    [TestCase(0, 60f, CombatOutcome.GuardSuccess, -2f, 0)]
    [TestCase(0, 60.01f, CombatOutcome.GuardSuccess, -2f, 0)]
    [TestCase(1, 60f, CombatOutcome.GuardSuccess, 2f, 1)]
    [TestCase(1, 90f, CombatOutcome.GuardSuccess, 2f, 1)]
    public void GuardKeepsRealBladeInterceptionsAtAndBeyondTheOldAngleBoundary(int attacker, float swordAngle,
        CombatOutcome outcome, float push, int pushedPlayer)
    {
        var guard = new CombatInput(90f, 0f, true);
        CombatResult result = Step(attacker == 0 ? Strike : guard,
            attacker == 1 ? Strike : guard, swordAngle, 0.01);
        Assert.That(result.Outcome, Is.EqualTo(outcome));
        Assert.That(result.SignedPush, Is.EqualTo(push));
        Assert.That(result.PushedPlayer, Is.EqualTo(pushedPlayer));
        Assert.That(result.HitStopSeconds, Is.EqualTo(outcome == CombatOutcome.GuardBreak ? 0.06f : 0.04f));
    }

    [TestCase(0f)] [TestCase(59.9f)] [TestCase(60f)]
    [TestCase(60.1f)] [TestCase(90f)] [TestCase(120.1f)]
    public void RealBladeInterceptionsDoNotDependOnTheCutVelocityAngle(float angle)
    {
        Vector3 grip = Vector3.forward * .55f;
        Vector3 body = Vector3.forward * 2f;
        Vector3 guardGrip = body + Vector3.back * .55f - Vector3.right * .9f;
        var attack = new CombatPose(grip, Vector3.up, grip, Vector3.forward, Vector3.zero,
            Vector3.forward, Quaternion.AngleAxis(angle, Vector3.up) * Vector3.right, grip, Vector3.forward);
        var defence = new CombatPose(guardGrip, Vector3.right, guardGrip, Vector3.right,
            body, Vector3.back, Vector3.zero, guardGrip, Vector3.right);
        Assert.That(CombatExchange.Resolve(AttackKind.Cut, AttackKind.None, attack, defence, false, true).Contact.Outcome,
            Is.EqualTo(CombatOutcome.GuardSuccess));
        Assert.That(CombatExchange.Resolve(AttackKind.None, AttackKind.Cut, defence, attack, true, false).Contact.Outcome,
            Is.EqualTo(CombatOutcome.GuardSuccess));
    }

    [Test]
    public void AMissingGuardBladeCannotBlock()
    {
        Vector3 grip = Vector3.forward * .55f, body = Vector3.forward * 2f;
        var attack = new CombatPose(grip, Vector3.up, grip, Vector3.forward, Vector3.zero,
            Vector3.forward, Vector3.down, grip, Vector3.forward);
        var defence = new CombatPose(body, Vector3.zero, body, Vector3.zero,
            body, Vector3.back, Vector3.zero, body, Vector3.zero);
        Assert.That(CombatContact.Resolve(AttackKind.Cut, attack, defence, true).Outcome, Is.EqualTo(CombatOutcome.GuardBreak));
    }

    [Test]
    public void SimultaneousSwingsClashAndConsumeBothAttacks()
    {
        CombatResult result = Step(Strike, Strike, 85f, 0.01);
        Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Clash));
        Assert.That(result.SignedPush, Is.Zero);
        Assert.That(result.PushedPlayer, Is.EqualTo(-1));
        Assert.That(result.HitStopSeconds, Is.EqualTo(0.04f));
        Step(new CombatInput(35f, 100f), new CombatInput(35f, 100f), 0f, 0.3);
        Assert.That(Step(new CombatInput(30f, 100f), new CombatInput(30f, 100f), 0f, 0.31).Outcome,
            Is.EqualTo(CombatOutcome.None));
        Step(new CombatInput(30f, 0f), new CombatInput(30f, 0f), 0f, .4);
        Step(new CombatInput(30f, 0f), new CombatInput(30f, 0f), 0f, .47);
        Assert.That(Step(new CombatInput(0f, 150f), new CombatInput(0f, 150f), 0f, .48).Outcome, Is.EqualTo(CombatOutcome.Clash));
    }

    [TestCase(65f, 75f, CombatOutcome.Hit)]
    [TestCase(65.01f, 75f, CombatOutcome.None)]
    [TestCase(65f, 74.9f, CombatOutcome.None)]
    public void AttackThresholdsAreInclusive(float angle, float speed, CombatOutcome outcome)
    {
        Assert.That(Step(new CombatInput(angle, speed), Upright, 0f, 0.01).Outcome, Is.EqualTo(outcome));
    }

    [Test]
    public void SmallForwardStepsCanAttackAtHighFrameRates()
    {
        Step(new CombatInput(35f, 0f), Upright, 0f, 1d);
        bool hit = false;
        for (int frame = 1; frame <= 150; frame++)
            if (Step(new CombatInput(35f - frame * .2f, 120f), Upright, 0f, 1d + frame * .001d).Outcome == CombatOutcome.Hit)
            { hit = true; break; }
        Assert.That(hit, Is.True, "Small real increments accumulate into a fresh stroke.");
    }

    [Test]
    public void MovingBackOrHoldingStillDoesNotAttack()
    {
        Step(new CombatInput(40f, 0f), Upright, 0f, 0.01);
        Assert.That(Step(new CombatInput(44f, 150f), Upright, 0f, 0.02).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(Step(new CombatInput(44f, 150f), Upright, 0f, 0.03).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(Step(new CombatInput(43f, 150f), Upright, 0f, 0.04).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(Step(new CombatInput(18f, 150f), Upright, 0f, 0.05).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void GuardTogglingCannotRearmASpentSwing()
    {
        Step(Strike, Upright, 0f, 0.01);
        Step(new CombatInput(35f, 100f), Upright, 0f, 1.3);
        Step(new CombatInput(30f, 100f, true), Upright, 0f, 1.31);
        Assert.That(Step(new CombatInput(25f, 100f), Upright, 0f, 1.32).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(Step(new CombatInput(25f, 100f), Upright, 0f, 1.33).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(Step(new CombatInput(5f, 100f), Upright, 0f, 1.35).Outcome, Is.EqualTo(CombatOutcome.None),
            "The same consumed movement stays consumed after a quick guard tap.");
        Step(new CombatInput(5f, 0f), Upright, 0f, 1.4);
        Step(new CombatInput(5f, 0f), Upright, 0f, 1.47);
        Assert.That(Step(new CombatInput(45f, 100f), Upright, 0f, 1.48).Outcome, Is.EqualTo(CombatOutcome.None), "Preparing the new stance is harmless.");
        Assert.That(Step(new CombatInput(10f, 100f), Upright, 0f, 1.49).Outcome, Is.EqualTo(CombatOutcome.Hit),
            "After a real takeback, a short forward cut does not require an eighty-degree stance.");
    }

    [Test]
    public void GuardCanBeHeldWhileReturningToStance()
    {
        Step(Strike, Upright, 0f, 0.01);
        Step(new CombatInput(90f, 100f, true), Upright, 0f, 1.3);
        Step(new CombatInput(90f, 0f, true), Upright, 0f, 1.31);
        Step(new CombatInput(90f, 0f, true), Upright, 0f, 1.38);
        Step(Upright, Upright, 0f, 1.39); // Release guard while still in the stance.
        Assert.That(Step(Strike, Upright, 0f, 1.4).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void ResolutionDoesNotQueueASwing()
    {
        Step(Strike, Upright, 0f, 0.01);
        Assert.That(Step(Upright, Strike, 0f, 0.04).Outcome, Is.EqualTo(CombatOutcome.None));
        Step(Upright, new CombatInput(35f, 100f), 0f, 1.3);
        Assert.That(Step(Upright, new CombatInput(30f, 100f), 0f, 1.31).Outcome, Is.EqualTo(CombatOutcome.None));
        Step(Upright, Upright, 0f, 1.4);
        Assert.That(Step(Upright, Strike, 0f, 1.41).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StaggerAllowsOnlyGuardAndDoesNotMakeTheTargetInvulnerable(bool guard)
    {
        Step(Strike, Upright, 0f, .01);
        Assert.That(match.IsStunned(1, .5), Is.True);
        Assert.That(match.IsStunned(1, 1.08), Is.False);
        Step(Upright, Upright, 0f, .2);
        Assert.That(Step(Upright, Strike, 0f, .205).Outcome, Is.EqualTo(CombatOutcome.None),
            "A staggered player cannot counterattack or queue a swing.");
        Step(Upright, new CombatInput(90f, 0f, guard), 90f, .21);
        Assert.That(Step(Strike, new CombatInput(90f, 0f, guard), 90f, .22).Outcome,
            Is.EqualTo(guard ? CombatOutcome.GuardSuccess : CombatOutcome.Hit),
            "The opponent can attack, and recovery still permits a real guard.");
    }

    [Test]
    public void BlockedAttackerCanGuardTheCounterattackDuringHalfSecondStagger()
    {
        Assert.That(Step(Strike, new CombatInput(90f, 0f, true), 90f, .01).Outcome,
            Is.EqualTo(CombatOutcome.GuardSuccess));
        Assert.That(match.IsStunned(0, .5), Is.True);
        Assert.That(match.IsStunned(0, 1.06), Is.False);
        Step(new CombatInput(90f, 0f, true), Upright, 90f, .2);
        Assert.That(Step(new CombatInput(90f, 0f, true), Strike, 90f, .21).Outcome,
            Is.EqualTo(CombatOutcome.GuardSuccess));
    }

    [Test]
    public void InvalidPoseDisablesGuard()
    {
        Assert.That(Step(Strike, new CombatInput(90f, 0f, true, false), 90f, 0.01).Outcome,
            Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void StalePoseMustReturnToStanceBeforeAttacking()
    {
        Step(new CombatInput(90f, 0f, valid: false), Upright, 0f, 0.01);
        Assert.That(Step(Strike, Upright, 0f, 0.02).Outcome, Is.EqualTo(CombatOutcome.None));
        Assert.That(Step(new CombatInput(30f, 150f), Upright, 0f, 0.03).Outcome, Is.EqualTo(CombatOutcome.None));
        Step(Upright, Upright, 0f, 0.04);
        Assert.That(Step(Strike, Upright, 0f, 0.05).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }

    [Test]
    public void RecenterOrReconnectGenerationCannotProduceAnInstantAttack()
    {
        Assert.That(Step(new CombatInput(40f, 300f, poseGeneration: 1), Upright, 0f, 0.01).Outcome,
            Is.EqualTo(CombatOutcome.None));
        Assert.That(Step(new CombatInput(30f, 300f, poseGeneration: 1), Upright, 0f, 0.02).Outcome,
            Is.EqualTo(CombatOutcome.None));
        Step(new CombatInput(90f, 0f, poseGeneration: 1), Upright, 0f, 0.03);
        Assert.That(Step(new CombatInput(40f, 150f, poseGeneration: 1), Upright, 0f, 0.04).Outcome,
            Is.EqualTo(CombatOutcome.Hit));
    }

    [TestCase(15f, 0f, MatchWinner.Player2)]
    [TestCase(0f, -15f, MatchWinner.Player1)]
    [TestCase(15f, -15f, MatchWinner.Draw)]
    public void ReachingTheBoundaryEndsTheRound(float firstZ, float secondZ, MatchWinner winner)
    {
        Assert.That(match.CheckRingOut(new Vector2(0f, firstZ), new Vector2(0f, secondZ), 15f), Is.EqualTo(winner));
        Assert.That(match.CheckRingOut(Vector2.zero, Vector2.zero, 15f), Is.EqualTo(winner));
        Assert.That(Step(Strike, Strike, 0f, 1).Outcome, Is.EqualTo(CombatOutcome.None));
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
        Step(Strike, Upright, 0f, 0.01);
        match.CheckRingOut(Vector2.zero, new Vector2(0f, 15f), 15f);
        match.Reset();
        Assert.That(match.Winner, Is.EqualTo(MatchWinner.None));
        Assert.That(match.IsResolving(0.02), Is.False);
        Assert.That(match.IsStunned(1, 0.02), Is.False);
        Assert.That(Step(Strike, Strike, 0f, 0.02).Outcome, Is.EqualTo(CombatOutcome.None));
        Step(Upright, Upright, 0f, 0.03);
        Assert.That(Step(Upright, Strike, 0f, 0.04).Outcome, Is.EqualTo(CombatOutcome.Hit));
    }
}
