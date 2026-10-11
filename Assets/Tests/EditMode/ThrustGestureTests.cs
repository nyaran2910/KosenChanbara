using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class ThrustGestureTests
{
    private ThrustGesture gesture;
    private double time;

    [SetUp]
    public void SetUp()
    {
        gesture = new ThrustGesture();
        time = 0;
        for (int i = 0; i < 8; i++) Tick(Vector3.forward, Vector3.zero);
    }

    private bool Tick(Vector3 blade, Vector3 acceleration, bool valid = true, bool guard = false, int generation = 0)
    {
        time += .02;
        return gesture.Update(blade, acceleration, valid, guard, time, generation);
    }

    [Test]
    public void SustainedForwardImpulseTriggersOnceAndTheBackwardReturnRearms()
    {
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);
        for (int i = 0; i < 40; i++)
            Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
        // Pulling back never attacks, but releases the preceding push.
        for (int i = 0; i < 30; i++)
            Assert.That(Tick(Vector3.forward, Vector3.back * 5), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);
    }

    [TestCase(.02)]
    [TestCase(1.0 / 60.0)]
    [TestCase(.01)]
    public void RepeatedPushAndPullThrustsNeedNoStationaryPause(double samplePeriod)
    {
        for (int thrust = 0; thrust < 12; thrust++)
        {
            int triggers = 0;
            for (int i = 0; i < Mathf.CeilToInt(.08f / (float)samplePeriod); i++)
            {
                time += samplePeriod;
                if (gesture.Update(Vector3.forward, Vector3.forward * 6f, true, false, time, 0)) triggers++;
            }
            Assert.That(triggers, Is.EqualTo(1), "thrust " + thrust);
            for (int i = 0; i < Mathf.CeilToInt(.36f / (float)samplePeriod); i++)
            {
                time += samplePeriod;
                Assert.That(gesture.Update(Vector3.forward, new Vector3(2f, 0f, -5f), true, false, time, 0), Is.False);
            }
        }
    }

    [Test]
    public void RepeatedThrustsRearmWhileTheHandStillMovesAcrossTheBlade()
    {
        for (int thrust = 0; thrust < 12; thrust++)
        {
            Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
            Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True, "thrust " + thrust);
            for (int i = 0; i < 26; i++)
                Assert.That(Tick(Vector3.forward, new Vector3(2.5f, -2f, .8f)), Is.False);
        }
    }

    [Test]
    public void RecoveryDuringCooldownIsRememberedAfterTheHandMovesAgain()
    {
        Tick(Vector3.forward, Vector3.forward * 5);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);
        for (int i = 0; i < 8; i++) Tick(Vector3.forward, Vector3.zero);
        // The rest ended before cooldown; lateral motion continues beyond it.
        for (int i = 0; i < 18; i++)
            Assert.That(Tick(Vector3.forward, new Vector3(6f, 0f, 2f)), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);
    }

    [Test]
    public void BriefForwardReleaseDuringCooldownRearmsBeforeLateralMovementResumes()
    {
        Tick(Vector3.forward, Vector3.forward * 5);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);
        // A normal short pause need not last a tenth of a second. The hand
        // then keeps moving sideways through the remainder of cooldown.
        for (int i = 0; i < 5; i++)
            Assert.That(Tick(Vector3.forward, new Vector3(2f, 0f, .4f)), Is.False);
        for (int i = 0; i < 12; i++)
            Assert.That(Tick(Vector3.forward, new Vector3(6f, 0f, 2f)), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);
    }

    [Test]
    public void FreshThrustCanFollowTheCompletedAnimationWithoutAnExtraLongWait()
    {
        Tick(Vector3.forward, Vector3.forward * 5);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);
        for (int i = 0; i < 16; i++)
            Assert.That(Tick(Vector3.forward, Vector3.zero), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);
    }

    [TestCase(.02, 2)]
    [TestCase(1.0 / 60.0, 2)]
    [TestCase(.01, 3)]
    public void ShortForwardImpulseRespondsAtCommonSensorSamplingRates(double samplePeriod, int samples)
    {
        for (int i = 0; i < samples; i++)
        {
            time += samplePeriod;
            bool triggered = gesture.Update(Vector3.forward, Vector3.forward * 6f, true, false, time, 0);
            Assert.That(triggered, Is.EqualTo(i == samples - 1), "sample " + (i + 1));
        }
    }

    [Test]
    public void SofterForwardTailAndSmallLateralWobbleCompleteTheOriginalImpulse()
    {
        Assert.That(Tick(Vector3.forward, Vector3.forward * 6f), Is.False);
        Assert.That(Tick(Vector3.forward, new Vector3(2.5f, 0f, 2.2f)), Is.True);
    }

    [Test]
    public void SoftOrSidewaysMovementCannotStartAnImpulseUsingTheContinuationThreshold()
    {
        for (int i = 0; i < 10; i++)
            Assert.That(Tick(Vector3.forward, new Vector3(2.5f, 0f, 2.2f)), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 6f), Is.False);
        for (int i = 0; i < 10; i++)
            Assert.That(Tick(Vector3.forward, new Vector3(6f, 0f, 2f)), Is.False);
    }

    [Test]
    public void PointingOutsideTheSharedThrustConeDiscardsAnUnfinishedImpulse()
    {
        Assert.That(Tick(Vector3.forward, Vector3.forward * 6f), Is.False);
        Vector3 outsideCone = Quaternion.AngleAxis(26f, Vector3.up) * Vector3.forward;
        Assert.That(Tick(outsideCone, Vector3.forward * 6f), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 6f), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 6f), Is.True);
    }

    [Test]
    public void EarlyRecoveryDoesNotBypassCooldown()
    {
        Tick(Vector3.forward, Vector3.forward * 5);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);
        for (int i = 0; i < 8; i++) Tick(Vector3.forward, Vector3.zero);
        for (int i = 0; i < 5; i++)
            Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
    }

    [Test]
    public void ClockRestartDiscardsTheOldImpulseAndAllowsFreshThrusts()
    {
        gesture.Reset();
        time = 60;
        for (int i = 0; i < 8; i++) Tick(Vector3.forward, Vector3.zero);
        Tick(Vector3.forward, Vector3.forward * 5);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);

        time = 0;
        for (int i = 0; i < 5; i++)
            Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
        for (int i = 0; i < 8; i++) Tick(Vector3.forward, Vector3.zero);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.True);
    }

    [TestCase(1, 0, 0)]
    [TestCase(0, 1, 0)]
    [TestCase(0, 0, -1)]
    [TestCase(0, 0, 0)]
    [TestCase(1, 0, 1)]
    public void SidewaysBackwardQuietAndMostlySidewaysAccelerationDoNotAttack(float x, float y, float z)
    {
        for (int i = 0; i < 15; i++)
            Assert.That(Tick(Vector3.forward, new Vector3(x, y, z) * 5), Is.False);
    }

    [TestCase(0, 1, 0)]
    [TestCase(1, 0, 0)]
    [TestCase(0, 0, -1)]
    public void SwordMustPointForward(float x, float y, float z)
    {
        for (int i = 0; i < 10; i++)
            Assert.That(Tick(new Vector3(x, y, z), Vector3.forward * 5), Is.False);
    }

    [Test]
    public void SingleAccelerationSpikeIsInsufficient()
    {
        Assert.That(Tick(Vector3.forward, Vector3.forward * 15), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.zero), Is.False);
    }

    [TestCase(.04)]
    [TestCase(.1)]
    public void OneAccelerationSpikeCannotAttackEvenWhenASlowFrameSpansTheMinimumDuration(double samplePeriod)
    {
        time += samplePeriod;
        Assert.That(gesture.Update(Vector3.forward, Vector3.forward * 15f, true, false, time, 0), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.zero), Is.False);
    }

    [TestCase(true, true, 0)]
    [TestCase(false, false, 0)]
    [TestCase(true, false, 1)]
    public void GuardInvalidPoseAndRecenterDiscardPreviousImpulse(bool valid, bool guard, int generation)
    {
        Tick(Vector3.forward, Vector3.forward * 5);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5, valid, guard, generation), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5, true, false, generation), Is.False);
    }

    [Test]
    public void DuplicateTimestampAndDelayedSampleCannotFinishAnImpulse()
    {
        Tick(Vector3.forward, Vector3.forward * 5);
        for (int i = 0; i < 10; i++)
            Assert.That(gesture.Update(Vector3.forward, Vector3.forward * 5, true, false, time, 0), Is.False);
        time += .2;
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
        Assert.That(Tick(Vector3.forward, Vector3.forward * 5), Is.False);
    }
}
