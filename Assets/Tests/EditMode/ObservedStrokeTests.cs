using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class ObservedStrokeTests
{
    private static Vector3 Blade(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    private static CombatPose Pose(float from, float to)
        => new CombatPose(Vector3.forward * .6f, Blade(from), Vector3.forward * .6f, Blade(to),
            Vector3.zero, Vector3.forward, Vector3.zero, Vector3.zero, Vector3.forward);
    private static CombatInput Input(float yaw, float speed = 500f, bool guard = false, int generation = 0)
        => new CombatInput(Mathf.Abs(yaw), speed, guard, true, generation);

    [TestCase(30)] [TestCase(60)] [TestCase(144)]
    public void AShortCutNeedsARealTakebackBeforeAReverseCutCanAttack(int fps)
    {
        var stroke = new AttackStroke(); double dt = 1d / fps;
        Assert.That(stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), 0d), Is.False);
        Assert.That(stroke.Update(Input(-10f, 1500f), Pose(-35f, -10f), dt), Is.True);
        Assert.That(stroke.StartedKind, Is.EqualTo(AttackKind.Cut));
        stroke.Consume();
        Assert.That(stroke.Update(Input(0f, 1500f), Pose(-10f, 0f), dt * 2d), Is.False, "One continuing stroke never scores twice.");
        Assert.That(stroke.Update(Input(-25f, 1500f), Pose(0f, -25f), dt * 3d), Is.False, "The pullback is not an attack.");
        Assert.That(stroke.Update(Input(10f, 1500f), Pose(-25f, 10f), dt * 4d), Is.True);
        Assert.That(stroke.Kind, Is.EqualTo(AttackKind.Cut));
    }

    [TestCase(40f)] [TestCase(90f)] [TestCase(149f)]
    public void SlowFullSweepsCannotStartAnAttackEvenWithEnoughTravel(float speed)
    {
        var stroke = new AttackStroke();
        stroke.Update(Input(70f, 0f), Pose(70f, 70f), 0d);
        for (int i = 1; i <= 14; i++)
        {
            float from = 70f - (i - 1) * 10f, to = 70f - i * 10f;
            Assert.That(stroke.Update(Input(to, speed), Pose(from, to), i * 10d / speed), Is.False);
        }
    }

    [TestCase(30)] [TestCase(60)] [TestCase(144)]
    public void AnOrdinaryKeyboardRateCutStaysActiveUntilItReachesTheOpponent(int fps)
    {
        var stroke = new AttackStroke(); stroke.Update(Input(90f, 0f), Pose(90f, 90f), 0d);
        bool active = false;
        for (int frame = 1; frame <= fps; frame++)
        {
            float previous = 90f - (frame - 1) * 90f / fps, current = 90f - frame * 90f / fps;
            active = stroke.Update(Input(current, 180f), Pose(previous, current), (double)frame / (fps * 2));
        }
        Assert.That(active, Is.True);
        Assert.That(stroke.Update(Input(0f, 0f), Pose(0f, 0f), .5d + 1d / fps), Is.False);
    }

    [Test]
    public void ACoarseSweepThroughTheOpponentWorksEvenWhenBothEndpointsAreOutsideTheCone()
    {
        var stroke = new AttackStroke();
        stroke.Update(Input(-75f, 0f), Pose(-75f, -75f), 0d);
        Assert.That(stroke.Update(Input(75f, 1500f), Pose(-75f, 75f), .1d), Is.True);
    }

    [TestCase(false, -1)] [TestCase(false, 1)]
    [TestCase(true, -1)] [TestCase(true, 1)]
    public void ACompletedCutCanImmediatelyReverseAcrossTheFrontWithoutRest(bool vertical, int side)
    {
        Vector3 axis = vertical ? Vector3.right : Vector3.up;
        CombatPose Motion(float from, float to)
        {
            Vector3 a = Quaternion.AngleAxis(from, axis) * Vector3.forward;
            Vector3 b = Quaternion.AngleAxis(to, axis) * Vector3.forward;
            return new CombatPose(Vector3.forward * .6f, a, Vector3.forward * .6f, b,
                Vector3.zero, Vector3.forward, Vector3.zero, Vector3.zero, Vector3.forward);
        }
        var stroke = new AttackStroke();
        float ready = side * 45f, center = side * 10f, far = -side * 45f;
        Assert.That(stroke.Update(Input(ready, 0f), Motion(ready, ready), 0d), Is.False);
        Assert.That(stroke.Update(Input(center, 1000f), Motion(ready, center), .05d), Is.True);
        stroke.Consume();
        Assert.That(stroke.Update(Input(far, 1000f), Motion(center, far), .1d, blocked: true), Is.False);
        Assert.That(stroke.Update(Input(center, 1000f), Motion(far, center), .25d), Is.True,
            "The reverse is a new observed stroke after reaching the opposite preparation side.");
        Assert.That(stroke.StartedKind, Is.EqualTo(AttackKind.Cut));
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
        Assert.That(stroke.Update(Input(-40f), Pose(-10f, -40f), .18d), Is.False);
        Assert.That(stroke.Update(Input(-10f), Pose(-40f, -10f), .23d), Is.True);
    }

    [Test]
    public void AConsumedCutCanReverseFromTheFarSideAfterTheSharedHitStop()
    {
        var stroke = new AttackStroke(); stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), 0d);
        stroke.Update(Input(-10f), Pose(-35f, -10f), .05d); stroke.Consume();
        Assert.That(stroke.Update(Input(0f), Pose(-10f, 0f), .06d, true), Is.False);
        Assert.That(stroke.Update(Input(20f), Pose(0f, 20f), .07d, true), Is.False);
        Assert.That(stroke.Update(Input(40f), Pose(20f, 40f), .25d), Is.False);
        Assert.That(stroke.Update(Input(10f), Pose(40f, 10f), .3d), Is.True);
        stroke.Consume();
        Assert.That(stroke.Update(Input(-35f), Pose(10f, -35f), .35d), Is.False);
        Assert.That(stroke.Update(Input(-5f), Pose(-35f, -5f), .4d), Is.True);
    }

    [Test]
    public void TappingGuardDuringHitStopCannotScoreUntilARealReversal()
    {
        var stroke = new AttackStroke(); stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), 0d);
        stroke.Update(Input(-10f), Pose(-35f, -10f), .05d); stroke.Consume();
        Assert.That(stroke.Update(Input(0f, guard: true), Pose(-10f, 0f), .06d, true), Is.False);
        Assert.That(stroke.Update(Input(10f), Pose(0f, 10f), .07d, true), Is.False);
        Assert.That(stroke.Update(Input(35f), Pose(10f, 35f), .25d), Is.False);
        Assert.That(stroke.Update(Input(5f), Pose(35f, 5f), .3d), Is.True);
        stroke.Consume();
        Assert.That(stroke.Update(Input(-35f), Pose(5f, -35f), .35d), Is.False);
        Assert.That(stroke.Update(Input(-5f), Pose(-35f, -5f), .4d), Is.True);
    }

    [Test]
    public void GuardStaggerAndSourceChangesDiscardOldMotionBeforeAcceptingFreshMovement()
    {
        var stroke = new AttackStroke(); stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), 0d);
        stroke.Update(Input(-10f), Pose(-35f, -10f), .05d);
        Assert.That(stroke.Update(Input(0f, guard: true), Pose(-10f, 0f), .06d), Is.False);
        Assert.That(stroke.Update(Input(-35f), Pose(0f, -35f), .07d), Is.False);
        Assert.That(stroke.Update(Input(-10f), Pose(-35f, -10f), .12d), Is.False);
        Assert.That(stroke.Update(Input(-35f), Pose(-10f, -35f), .13d), Is.False);
        Assert.That(stroke.Update(Input(0f), Pose(-35f, 0f), .14d, true), Is.False);
        Assert.That(stroke.Update(Input(0f), Pose(0f, 0f), .2d), Is.False);
        Assert.That(stroke.Update(Input(-10f, generation: 1), Pose(-35f, -10f), .21d), Is.False);
        Assert.That(stroke.Update(Input(15f, generation: 1), Pose(-10f, 15f), .26d), Is.False);
        Assert.That(stroke.Update(Input(45f, generation: 1), Pose(15f, 45f), .31d), Is.False);
        Assert.That(stroke.Update(Input(10f, generation: 1), Pose(45f, 10f), .36d), Is.True);
    }
    [TestCase(30)] [TestCase(60)] [TestCase(144)]
    public void SlowLargeMotionsAndFastTinyMotionsAreNotAttacks(int fps)
    {
        foreach(bool slow in new[]{true,false})
        {
            var stroke=new AttackStroke();float previous=45f;
            stroke.Update(Input(previous,0f),Pose(previous,previous),0d);
            int frames=slow?fps:Mathf.CeilToInt(fps*.1f);
            for(int frame=1;frame<=frames;frame++)
            {
                float current=45f-(slow?60f*frame/fps:20f*frame/frames);
                Assert.That(stroke.Update(Input(current,slow?60f:2000f),Pose(previous,current),(double)frame/fps),Is.False);
                previous=current;
            }
        }
    }

    [TestCase(30)] [TestCase(60)] [TestCase(144)]
    public void AConsumedSwingCannotDamageOnItsTakebackButCanAttackOnTheNextForwardCut(int fps)
    {
        var stroke=new AttackStroke();double dt=1d/fps;
        stroke.Update(Input(80f,0f),Pose(80f,80f),0d);
        Assert.That(stroke.Update(Input(45f,2000f),Pose(80f,45f),dt),Is.True);
        stroke.Consume();
        Assert.That(stroke.Update(Input(80f,2000f),Pose(45f,80f),dt*2),Is.False);
        Assert.That(stroke.Kind,Is.EqualTo(AttackKind.None));
        Assert.That(stroke.Update(Input(45f,2000f),Pose(80f,45f),dt*3),Is.True);
    }

    [TestCase(30)] [TestCase(60)] [TestCase(144)]
    public void AFullFollowThroughAndItsWholeKeyboardRateReversalScoreOneNewStroke(int fps)
    {
        var stroke=new AttackStroke();
        stroke.Update(Input(90f,0f),Pose(90f,90f),0d);
        Assert.That(stroke.Update(Input(0f,2000f),Pose(90f,0f),.05d),Is.True);
        stroke.Consume();
        Assert.That(stroke.Update(Input(-90f,2000f),Pose(0f,-90f),.1d),Is.False);
        float previous=-90f;
        int freshCounters = 0;
        for(int frame=1;frame<=fps*2;frame++)
        {
            float current=-90f+frame*90f/fps;
            stroke.Update(Input(current,180f),Pose(previous,current),.1d+(double)frame/(fps*2));
            if (stroke.StartedKind == AttackKind.Cut) { freshCounters++; stroke.Consume(); }
            previous=current;
        }
        Assert.That(freshCounters, Is.EqualTo(1), "The full return is one new cut, not a repeat on every sample.");
    }

    [Test]
    public void ACoarseReverseCanHitInTheSameFrameItCrossesTheFront()
    {
        var stroke=new AttackStroke();stroke.Update(Input(70f,0f),Pose(70f,70f),0d);
        Assert.That(stroke.Update(Input(-30f,2000f),Pose(70f,-30f),.1d),Is.True);
        stroke.Consume();
        Assert.That(stroke.Update(Input(30f,2000f),Pose(-30f,30f),.2d),Is.True);
        Assert.That(stroke.StartedKind,Is.EqualTo(AttackKind.Cut));
    }

    [Test]
    public void AnOppositeSideCounterNeedsRealMotionAndMinimumCutSpeed()
    {
        var stroke=new AttackStroke();stroke.Update(Input(90f,0f),Pose(90f,90f),0d);
        stroke.Update(Input(0f,2000f),Pose(90f,0f),.05d);stroke.Consume();
        stroke.Update(Input(-90f,2000f),Pose(0f,-90f),.1d);
        Assert.That(stroke.Update(Input(-90f,0f),Pose(-90f,-90f),.12d),Is.False);
        Assert.That(stroke.Update(Input(-90f,0f),Pose(-90f,-90f),.25d),Is.False);
        Assert.That(stroke.Update(Input(-80f,149f),Pose(-90f,-80f),.3d),Is.False);
        Assert.That(stroke.Update(Input(-50f,500f),Pose(-80f,-50f),.34d),Is.True,
            "The far side is the new ready position; the return cuts as soon as it travels enough.");
        stroke.Consume();
        Assert.That(stroke.Update(Input(-30f,500f),Pose(-50f,-30f),.4d),Is.False,
            "A consumed counter cannot hit again during the same continuous swing.");
    }

    [TestCase(30)] [TestCase(60)] [TestCase(144)]
    public void ADeliberatelyPreparedFastShortCounterWorksAtCoarseAndFineFrameRates(int fps)
    {
        var stroke = new AttackStroke();
        stroke.Update(Input(90f, 0f), Pose(90f, 90f), 0d);
        Assert.That(stroke.Update(Input(0f, 2000f), Pose(90f, 0f), .05d), Is.True);
        stroke.Consume();
        Assert.That(stroke.Update(Input(-35f, 2000f), Pose(0f, -35f), .1d), Is.False);
        stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), .12d);
        stroke.Update(Input(-35f, 0f), Pose(-35f, -35f), .25d);
        float previous = -35f;
        bool cut = false;
        for (int frame = 1; frame <= Mathf.CeilToInt(35f / 900f * fps); frame++)
        {
            float current = Mathf.Min(0f, -35f + frame * 900f / fps);
            cut |= stroke.Update(Input(current, 900f), Pose(previous, current), .25d + (double)frame / fps);
            previous = current;
        }
        Assert.That(cut, Is.True, "Rest plus a fast 35-degree counter must not depend on how much of it fits in the first frame.");
    }

    [Test]
    public void ABladeRotatingAroundAStationaryMiddleHasInsufficientAttackMovement()
    {
        var stroke=new AttackStroke();CombatPose before=Pose(45f,45f);
        stroke.Update(Input(45f,0f),before,0d);
        const float middle=(CombatContact.BladeStart+CombatContact.BladeLength)*.5f;
        Vector3 fixedMiddle=before.Grip+before.Blade*middle;
        var movement=new CombatPose(before.Grip,before.Blade,fixedMiddle-Blade(20f)*middle,Blade(20f),
            Vector3.zero,Vector3.forward,Vector3.zero,Vector3.zero,Vector3.forward);
        Assert.That(stroke.Update(Input(20f,1000f),movement,.05d),Is.False);
    }

}
