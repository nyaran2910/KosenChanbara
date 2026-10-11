using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class ObservedStrokeTests
{
    private static Vector3 Blade(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    private static CombatPose Pose(float from, float to)
        => new CombatPose(Vector3.forward * .6f, Blade(from), Vector3.forward * .6f, Blade(to),
            Vector3.zero, Vector3.forward, Vector3.zero, Vector3.zero, Vector3.forward);
    private static CombatInput Input(float yaw, float speed = 500f, bool guard = false, int generation = 0, bool thrust = false)
        => new CombatInput(Mathf.Abs(yaw), speed, guard, true, generation, thrust);

    [TestCase(30)] [TestCase(60)] [TestCase(144)]
    public void AShortCutAndItsReversalDoNotNeedAnEightyDegreeRearm(int fps)
    {
        var stroke = new AttackStroke(); double dt = 1d / fps;
        Assert.That(stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), 0d), Is.False);
        Assert.That(stroke.Update(Input(-10f, 1500f), Pose(-35f, -10f), dt), Is.True);
        Assert.That(stroke.StartedKind, Is.EqualTo(AttackKind.Cut));
        stroke.Consume();
        Assert.That(stroke.Update(Input(0f, 1500f), Pose(-10f, 0f), dt * 2d), Is.False, "One continuing stroke never scores twice.");
        Assert.That(stroke.Update(Input(-25f, 1500f), Pose(0f, -25f), dt * 3d), Is.True);
        Assert.That(stroke.Kind, Is.EqualTo(AttackKind.Cut));
    }

    [TestCase(30)] [TestCase(60)] [TestCase(144)]
    public void AnOrdinaryKeyboardRateCutStaysActiveUntilItReachesTheOpponent(int fps)
    {
        var stroke = new AttackStroke(); stroke.Update(Input(90f, 0f), Pose(90f, 90f), 0d);
        bool active = false;
        for (int frame = 1; frame <= fps; frame++)
        {
            float previous = 90f - (frame - 1) * 90f / fps, current = 90f - frame * 90f / fps;
            active = stroke.Update(Input(current, 90f), Pose(previous, current), (double)frame / fps);
        }
        Assert.That(active, Is.True);
        Assert.That(stroke.Update(Input(0f, 0f), Pose(0f, 0f), 1d + 1d / fps), Is.False);
    }

    [Test]
    public void ACoarseSweepThroughTheOpponentWorksEvenWhenBothEndpointsAreOutsideTheCone()
    {
        var stroke = new AttackStroke();
        stroke.Update(Input(-75f, 0f), Pose(-75f, -75f), 0d);
        Assert.That(stroke.Update(Input(75f, 1500f), Pose(-75f, 75f), .1d), Is.True);
    }

    [Test]
    public void SmallJitterAndBladeAxisTwistsAreSilentEvenWithHighReportedGyroSpeed()
    {
        var stroke = new AttackStroke(); stroke.Update(Input(0f, 0f), Pose(0f, 0f), 0d);
        float previous = 0f;
        for (int i = 1; i <= 120; i++)
        {
            float current = i % 2 == 0 ? -1f : 1f;
            Assert.That(stroke.Update(Input(current, 1000f), Pose(previous, current), i / 120d), Is.False);
            previous = current;
        }
        Assert.That(stroke.Update(Input(0f, 1000f), Pose(0f, 0f), 1.01d), Is.False);
    }

    [Test]
    public void StoppingDoesNotExecuteTheRestOfTheStrokeAndAQuietIntervalAllowsAnotherShortCut()
    {
        var stroke = new AttackStroke(); stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), 0d);
        Assert.That(stroke.Update(Input(-10f), Pose(-35f, -10f), .05d), Is.True);
        Assert.That(stroke.Update(Input(-10f, 0f), Pose(-10f, -10f), .06d), Is.False);
        Assert.That(stroke.Update(Input(-10f, 0f), Pose(-10f, -10f), .13d), Is.False);
        Assert.That(stroke.Kind, Is.EqualTo(AttackKind.None));
        Assert.That(stroke.Update(Input(15f), Pose(-10f, 15f), .18d), Is.True);
    }

    [Test]
    public void AFreshThrustCanTakeOverADeceleratingCut()
    {
        var stroke = new AttackStroke(); stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), 0d);
        stroke.Update(Input(-10f), Pose(-35f, -10f), .05d);
        Assert.That(stroke.Update(Input(0f, 0f, thrust: true), Pose(-10f, 0f), .06d), Is.True);
        Assert.That(stroke.Kind, Is.EqualTo(AttackKind.Thrust));
    }

    [Test]
    public void SmallBladeWobbleDoesNotCancelARecognizedThrust()
    {
        var stroke = new AttackStroke();
        Assert.That(stroke.Update(Input(0f, thrust: true), Pose(0f, 0f), 0d), Is.True);
        Assert.That(stroke.Update(Input(3f), Pose(0f, 3f), .03d), Is.True);
        Assert.That(stroke.Update(Input(-3f), Pose(3f, -3f), .06d), Is.True);
        Assert.That(stroke.Kind, Is.EqualTo(AttackKind.Thrust));
    }

    [Test]
    public void AConsumedCutCannotHitAgainAfterTheSharedHitStopUnlessItIsActuallyReversedOrRested()
    {
        var stroke = new AttackStroke(); stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), 0d);
        stroke.Update(Input(-10f), Pose(-35f, -10f), .05d); stroke.Consume();
        Assert.That(stroke.Update(Input(0f), Pose(-10f, 0f), .06d, true), Is.False);
        Assert.That(stroke.Update(Input(20f), Pose(0f, 20f), .07d, true), Is.False);
        Assert.That(stroke.Update(Input(40f), Pose(20f, 40f), .25d), Is.False);
        Assert.That(stroke.Update(Input(10f), Pose(40f, 10f), .3d), Is.True);
    }

    [Test]
    public void TappingGuardDuringHitStopDoesNotRearmTheSameConsumedCut()
    {
        var stroke = new AttackStroke(); stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), 0d);
        stroke.Update(Input(-10f), Pose(-35f, -10f), .05d); stroke.Consume();
        Assert.That(stroke.Update(Input(0f, guard: true), Pose(-10f, 0f), .06d, true), Is.False);
        Assert.That(stroke.Update(Input(10f), Pose(0f, 10f), .07d, true), Is.False);
        Assert.That(stroke.Update(Input(35f), Pose(10f, 35f), .25d), Is.False);
        Assert.That(stroke.Update(Input(5f), Pose(35f, 5f), .3d), Is.True);
    }

    [Test]
    public void GuardStaggerAndSourceChangesDiscardOldMotionBeforeAcceptingFreshMovement()
    {
        var stroke = new AttackStroke(); stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), 0d);
        stroke.Update(Input(-10f), Pose(-35f, -10f), .05d);
        Assert.That(stroke.Update(Input(0f, guard: true), Pose(-10f, 0f), .06d), Is.False);
        Assert.That(stroke.Update(Input(-35f), Pose(0f, -35f), .07d), Is.False);
        Assert.That(stroke.Update(Input(-10f), Pose(-35f, -10f), .12d), Is.False);
        Assert.That(stroke.Update(Input(-35f), Pose(-10f, -35f), .13d), Is.True);
        Assert.That(stroke.Update(Input(0f), Pose(-35f, 0f), .14d, true), Is.False);
        Assert.That(stroke.Update(Input(0f), Pose(0f, 0f), .2d), Is.False);
        Assert.That(stroke.Update(Input(-10f, generation: 1), Pose(-35f, -10f), .21d), Is.False);
        Assert.That(stroke.Update(Input(15f, generation: 1), Pose(-10f, 15f), .26d), Is.True);
    }
}
