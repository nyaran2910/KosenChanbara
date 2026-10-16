using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class SwordPoseTests
{
    private static readonly Vector3 Chest = new Vector3(0f, 0.025f, 0f);
    private const float Radius = 0.6f;
    private const float Plane = 0.344f;

    [TestCase(0, 1, 0)]
    [TestCase(0, -1, 0)]
    [TestCase(1, 0, 0)]
    [TestCase(-1, 0, 0)]
    [TestCase(1, 1, 1)]
    [TestCase(-1, -1, -1)]
    [TestCase(0, 0, 1)]
    [TestCase(0, 0, -1)]
    public void NormalGripAndTipShareARayFromTheChest(float x, float y, float z)
    {
        Vector3 direction = new Vector3(x, y, z).normalized;
        Quaternion input = Quaternion.FromToRotation(Vector3.forward, direction);
        Vector3 last = Vector3.up;
        SwordPose pose = SwordPoseMath.Calculate(input, false, Chest, Radius, Plane, ref last);
        AssertVector(pose.Position, Chest + Radius * direction);
        AssertVector(pose.Direction, direction);
        Assert.That(Vector3.Distance(pose.Position, Chest), Is.EqualTo(Radius).Within(0.00001f));
        AssertVector(pose.Position + 2.1f * pose.Direction, Chest + 2.7f * direction);
        Assert.That(Quaternion.Angle(pose.Rotation, input), Is.LessThan(0.01f));
    }

    [TestCase(1, 1, 0.5f)]
    [TestCase(-1, 1, -0.5f)]
    [TestCase(1, -1, -0.5f)]
    [TestCase(-1, -1, 0.5f)]
    [TestCase(1, 0, 1)]
    [TestCase(-1, 0, 1)]
    [TestCase(0, 1, 1)]
    [TestCase(0, -1, -1)]
    public void GuardFollowsFullInputOrientationAndCounterbalancesItsGrip(float x, float y, float z)
    {
        Vector3 direction = new Vector3(x,y,z).normalized;
        Quaternion input = Quaternion.FromToRotation(Vector3.forward,direction) * Quaternion.AngleAxis(37f,Vector3.forward);
        Vector3 last = Vector3.up;
        SwordPose normal = SwordPoseMath.Calculate(input,false,Chest,Radius,Plane,ref last);
        SwordPose guarded = SwordPoseMath.Calculate(input,true,Chest,Radius,Plane,ref last);
        Vector3 expectedDirection = z < 0f ? Vector3.ProjectOnPlane(direction, Vector3.forward).normalized : direction;
        Quaternion expectedRotation = Quaternion.FromToRotation(direction, expectedDirection) * input;
        Assert.That(Quaternion.Angle(guarded.Rotation,expectedRotation),Is.LessThan(.01f));
        AssertVector(guarded.Direction,expectedDirection);
        if (Mathf.Abs(direction.x) > .00001f)
            Assert.That(Mathf.Sign(guarded.Position.x-Chest.x),Is.EqualTo(-Mathf.Sign(direction.x)));
        Assert.That(guarded.Position.y-Chest.y,Is.EqualTo(-Radius*SwordPoseMath.GuardGripOutwardScale*expectedDirection.y).Within(.00001f));
        Assert.That(guarded.Position.z-Chest.z,Is.GreaterThanOrEqualTo(Plane));
    }

    [TestCase(1)]
    [TestCase(-1)]
    public void AxialGuardFollowsForwardInputAndUsesTheRememberedSafePlaneBehindTheBody(int forwardSign)
    {
        Vector3 last = Vector3.up;
        Quaternion input = Quaternion.FromToRotation(Vector3.forward,new Vector3(.02f,.02f,forwardSign).normalized);
        SwordPose pose = SwordPoseMath.Calculate(input,true,Chest,Radius,Plane,ref last);
        AssertVector(pose.Direction,forwardSign > 0 ? input * Vector3.forward : Vector3.up);
        last = Vector3.left;
        SwordPose repeat = SwordPoseMath.Calculate(input,true,Chest,Radius,Plane,ref last);
        if(forwardSign > 0) AssertPose(repeat,pose);
        else AssertVector(repeat.Direction,Vector3.left);
    }

    [Test]
    public void GuardAcceptsLargeForwardTiltsAndPreservesRoll()
    {
        var settings = new SwordPoseSettings(Chest); Vector3 last=Vector3.up;
        foreach(float tilt in new[]{3f,8f,25f,70f})
        {
            Vector3 planar=new Vector3(-1,1,0).normalized;
            Vector3 direction=planar*Mathf.Cos(tilt*Mathf.Deg2Rad)+Vector3.forward*Mathf.Sin(tilt*Mathf.Deg2Rad);
            Quaternion input=Quaternion.FromToRotation(Vector3.forward,direction)*Quaternion.AngleAxis(35f,Vector3.forward);
            SwordPose pose=SwordPoseMath.Calculate(input,true,settings,ref last);
            AssertVector(pose.Direction,direction);
            AssertVector(pose.Rotation*Vector3.up,input*Vector3.up);
            Assert.That(pose.Position.z,Is.GreaterThanOrEqualTo(settings.MinimumForward));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SecondPlayersPoseIsTheSameInTheirOwnCoordinateSystem(bool guard)
    {
        Vector3 last = Vector3.up;
        Quaternion input = Quaternion.FromToRotation(Vector3.forward, new Vector3(1f, 1f, 0.5f).normalized);
        SwordPose pose = SwordPoseMath.Calculate(input, guard, Chest, Radius, Plane, ref last);
        Quaternion second = Quaternion.Euler(0f, 180f, 0f);
        Vector3 firstPosition = new Vector3(0f, 1f, -1.5f) + pose.Position;
        Vector3 secondPosition = new Vector3(0f, 1f, 1.5f) + second * pose.Position;
        AssertVector(secondPosition, new Vector3(-firstPosition.x, firstPosition.y, -firstPosition.z));
        AssertVector(second * pose.Direction, new Vector3(-pose.Direction.x, pose.Direction.y, -pose.Direction.z));
    }


    [TestCase(0, 1, 0)]
    [TestCase(0, -1, 0)]
    [TestCase(1, 0, 0)]
    [TestCase(-1, 0, 0)]
    [TestCase(0, 0, 1)]
    [TestCase(0, 0, -1)]
    [TestCase(1, 1, 1)]
    [TestCase(-1, -1, -1)]
    public void GuardAndForwardGameplayGripsStayInFrontOfTheTorso(float x, float y, float z)
    {
        Quaternion input = Quaternion.FromToRotation(Vector3.forward, new Vector3(x, y, z).normalized);
        Vector3 last = Vector3.up;
        var settings = new SwordPoseSettings(Chest);
        foreach (bool guard in new[] { false, true })
        {
            SwordPose pose = SwordPoseMath.Calculate(input, guard, settings, ref last);
            if (guard || z >= 0) Assert.That(pose.Position.z, Is.GreaterThanOrEqualTo(settings.MinimumForward));
            if (guard)
            {
                Assert.That(pose.Direction.z, Is.GreaterThanOrEqualTo(-.00001f));
                if(z >= 0) Assert.That(Quaternion.Angle(pose.Rotation, input), Is.LessThan(.01f));
                Assert.That((pose.Position+pose.Direction*CombatContact.BladeLength).z, Is.GreaterThanOrEqualTo(settings.MinimumForward-.00001f));
            }
            else
            {
                Assert.That(Quaternion.Angle(pose.Rotation, input), Is.LessThan(0.01f));
                if (z >= 0 && y <= 0)
                    AssertVector(pose.Position, Chest + new Vector3(0, 0.1f, 0.55f) + pose.Direction * 0.55f);
            }
        }
    }

    [TestCase(-45f)]
    [TestCase(-60f)]
    public void UpwardRearFacingBladeClearsTheHeadAndKeepsInputOrientation(float upwardPitch)
    {
        Quaternion input = Quaternion.Euler(upwardPitch, 180f, 0f);
        Vector3 last = Vector3.up;
        SwordPose pose = SwordPoseMath.Calculate(input, false, new SwordPoseSettings(Chest), ref last);
        Assert.That(pose.Position.y - Chest.y, Is.InRange(1f, 1.12f + .00001f));
        Assert.That(pose.Position.z, Is.GreaterThanOrEqualTo(.45f));
        Assert.That(Quaternion.Angle(pose.Rotation, input), Is.LessThan(.01f));
        float crossesTorso = -pose.Position.z / pose.Direction.z;
        Assert.That((pose.Position + pose.Direction * crossesTorso).y - Chest.y, Is.GreaterThan(1.1f),
            "The blade must clear the head when crossing its center plane.");
    }

    [TestCase(90f, 0f)]
    [TestCase(-90f, 0f)]
    [TestCase(94f, 90f)]
    [TestCase(-94f, 180f)]
    public void HorizontalSidewaysAndSmallRearwardNoiseKeepTheNormalGrip(float yaw, float roll)
    {
        Quaternion input = Quaternion.Euler(0, yaw, roll);
        Vector3 last = Vector3.up;
        var settings = new SwordPoseSettings(Chest);
        SwordPose pose = SwordPoseMath.Calculate(input, false, settings, ref last);
        Assert.That(pose.Position.y, Is.EqualTo(Chest.y + .1f).Within(.00001f));
        Assert.That(Quaternion.Angle(pose.Rotation, input), Is.LessThan(.01f));
    }

    [TestCase(0, 1, 1)]
    [TestCase(1, 1, 0)]
    [TestCase(0, 1, -1)]
    public void UpperDiagonalDirectionsSelectTheRaisedStance(float x, float y, float z)
    {
        Vector3 direction = new Vector3(x, y, z).normalized;
        Quaternion input = Quaternion.FromToRotation(Vector3.forward, direction);
        Vector3 last = Vector3.up;
        var settings = new SwordPoseSettings(Chest);
        SwordPose pose = SwordPoseMath.Calculate(input, false, settings, ref last);
        float normalHeight = Chest.y + .1f + settings.NormalRadius * direction.y;
        Assert.That(pose.Position.y, Is.GreaterThan(normalHeight + .1f));
        Assert.That(pose.Position.y - Chest.y, Is.LessThanOrEqualTo(1.12f + .00001f));
        Assert.That(Quaternion.Angle(pose.Rotation, input), Is.LessThan(.01f));
    }

    [TestCase(1f)]
    [TestCase(-1f)]
    public void HorizontalSideToRearTurnNeverRaisesTheGripAndKeepsTheFiniteBladeOutsideTheTorso(float turnSign)
    {
        Vector3 last = Vector3.up;
        var settings = new SwordPoseSettings(Chest);
        Vector3 bodyCentre = Chest + Vector3.up * .1f;
        SwordPose previous = SwordPoseMath.Calculate(Quaternion.identity, false, settings, ref last);
        for (int step = 1; step <= 3600; step++)
        {
            Quaternion input = Quaternion.Euler(0, turnSign * step * .1f, 0);
            SwordPose next = SwordPoseMath.Calculate(input, false, settings, ref last);
            Assert.That(next.Position.y, Is.EqualTo(bodyCentre.y).Within(.00001f), "Horizontal rotation must never raise the hand, including behind the body.");
            Assert.That(Vector3.Distance(previous.Position, next.Position), Is.LessThan(.006f));
            Assert.That(next.Rotation, Is.EqualTo(input));
            float distanceAlong = Mathf.Clamp(Vector3.Dot(bodyCentre - next.Position, next.Direction), 0, 2.1f);
            float clearance = Vector3.Distance(bodyCentre, next.Position + next.Direction * distanceAlong);
            Assert.That(clearance, Is.GreaterThan(ShowdownCombat.BodyRadius + .06f), "The finite blade, including its padding, must stay outside the torso.");
            previous = next;
        }
        SwordPose rear = SwordPoseMath.Calculate(Quaternion.Euler(0, 180, 0), false, settings, ref last);
        AssertVector(rear.Position, Chest + new Vector3(0, .1f, -.55f));
    }

    [Test]
    public void RaisingARearBladeStartsAtTheNormalHandAndMovesContinuouslyToTheOverheadStance()
    {
        Vector3 last = Vector3.up;
        var settings = new SwordPoseSettings(Chest);
        SwordPose previous = SwordPoseMath.Calculate(Quaternion.Euler(0, 180, 0), false, settings, ref last);
        for (int step = 1; step <= 450; step++)
        {
            SwordPose next = SwordPoseMath.Calculate(Quaternion.Euler(-step * .1f, 180, 0), false, settings, ref last);
            Assert.That(next.Position.y, Is.GreaterThanOrEqualTo(previous.Position.y - .00001f));
            Assert.That(Vector3.Distance(previous.Position, next.Position), Is.LessThan(.01f), "The back-to-overhead path must have no position jump.");
            previous = next;
        }
        Assert.That(previous.Position.y - Chest.y, Is.EqualTo(1.12f).Within(.00001f));
        Assert.That(previous.Position.z, Is.EqualTo(.45f).Within(.00001f));
    }

    [Test]
    public void GuardGripMovesOppositeTheBladeWithItsRequestedVerticalOffsets()
    {
        Vector3 last=Vector3.up; var settings=new SwordPoseSettings(Chest);
        foreach(float pitch in new[]{55f,25f,0f,-25f,-55f})
        {
            Quaternion input=Quaternion.Euler(pitch,35f,23f);
            SwordPose guarded=SwordPoseMath.Calculate(input,true,settings,ref last);
            Vector3 direction=input*Vector3.forward;
            Assert.That(guarded.Position.x-Chest.x,Is.LessThan(0f),"A right-pointing blade has its grip to the left.");
            Assert.That(guarded.Position.y,Is.EqualTo(Chest.y+settings.GuardHeight-settings.GuardRadius*SwordPoseMath.GuardGripOutwardScale*direction.y+SwordPoseMath.GuardHeightOffset(direction.y)).Within(.00001f));
            Assert.That(guarded.Position.z,Is.GreaterThanOrEqualTo(settings.MinimumForward));
            Assert.That(Quaternion.Angle(guarded.Rotation,input),Is.LessThan(.01f));
        }
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void EnteringAndLeavingGuardHasNoPoseJumpAndFinishesAtTheCurrentTarget(int fps)
    {
        var transition = new GuardPoseTransition();
        Vector3 remembered = Vector3.up;
        var settings = new SwordPoseSettings(Chest);
        Quaternion input = Quaternion.Euler(-30f, 25f, 18f);
        SwordPose normal = SwordPoseMath.Calculate(input, false, settings, ref remembered);
        SwordPose guard = SwordPoseMath.Calculate(input, true, settings, ref remembered);
        AssertPose(transition.Update(normal, false, 0), normal);
        AssertPose(transition.Update(guard, true, 1), normal);
        SwordPose previous = normal;
        int steps = Mathf.CeilToInt(GuardPoseTransition.DurationSeconds * fps);
        for (int frame = 1; frame <= steps; frame++)
        {
            SwordPose pose = transition.Update(guard, true, 1d + (double)frame / fps);
            Assert.That(Vector3.Distance(pose.Position, guard.Position),
                Is.LessThanOrEqualTo(Vector3.Distance(previous.Position, guard.Position) + .00001f));
            Assert.That(Quaternion.Angle(pose.Rotation, guard.Rotation),
                Is.LessThanOrEqualTo(Quaternion.Angle(previous.Rotation, guard.Rotation) + .06f));
            Assert.That(Vector3.Distance(previous.Position, pose.Position),
                Is.LessThan(Vector3.Distance(normal.Position, guard.Position) * .5f));
            previous = pose;
        }
        AssertPose(previous, guard);
        AssertPose(transition.Update(normal, false, 2), guard);
        SwordPose changedInput = SwordPoseMath.Calculate(Quaternion.Euler(20f, -65f, 0f), false, settings, ref remembered);
        SwordPose leaving = transition.Update(changedInput, false, 2.06);
        Assert.That(Vector3.Distance(leaving.Position, guard.Position), Is.GreaterThan(.01f));
        Assert.That(Vector3.Distance(leaving.Position, changedInput.Position), Is.GreaterThan(.01f));
        AssertPose(transition.Update(changedInput, false, 2.13), changedInput);
    }

    [Test]
    public void GuardTransitionCanReverseAndResetWithoutReusingThePreviousInput()
    {
        var transition = new GuardPoseTransition();
        var normal = new SwordPose(new Vector3(.2f, .4f, 1f), Quaternion.Euler(20f, 35f, 0f));
        var guard = new SwordPose(new Vector3(-.4f, .5f, .5f), Quaternion.Euler(-90f, 0f, 15f));
        transition.Update(normal, false, 5);
        transition.Update(guard, true, 5.1);
        SwordPose entering = transition.Update(guard, true, 5.15);
        AssertPose(transition.Update(normal, false, 5.15), entering);
        var latest = new SwordPose(new Vector3(-.3f, .8f, .9f), Quaternion.Euler(-40f, -55f, -25f));
        AssertPose(transition.Update(latest, false, 5.28), latest);
        transition.Update(guard, true, 5.3);
        transition.Reset();
        AssertPose(transition.Update(latest, true, 5.31), latest);
        transition.Update(normal, false, 5.4);
        AssertPose(transition.Update(guard, true, 0), guard);
    }

    [TestCase(30, false)]
    [TestCase(60, false)]
    [TestCase(144, false)]
    [TestCase(30, true)]
    [TestCase(60, true)]
    [TestCase(144, true)]
    public void RecenterStartsAtTheVisiblePoseAndUsesTheGuardTransition(int fps, bool guarding)
    {
        var transition = new GuardPoseTransition();
        Vector3 remembered = Vector3.up;
        var settings = new SwordPoseSettings(Chest);
        SwordPose before = SwordPoseMath.Calculate(Quaternion.Euler(-35f, 65f, 30f), guarding, settings, ref remembered);
        SwordPose centered = SwordPoseMath.Calculate(Quaternion.Euler(-90f, 0f, 0f), guarding, settings, ref remembered);
        transition.BeginFrom(before, guarding, 2);
        AssertPose(transition.Update(centered, guarding, 2), before);
        SwordPose previous = before;
        for (int frame = 1; frame <= Mathf.CeilToInt(GuardPoseTransition.DurationSeconds * fps); frame++)
        {
            SwordPose pose = transition.Update(centered, guarding, 2d + (double)frame / fps);
            Assert.That(Quaternion.Angle(pose.Rotation, centered.Rotation),
                Is.LessThanOrEqualTo(Quaternion.Angle(previous.Rotation, centered.Rotation) + .05f));
            Assert.That(Vector3.Distance(previous.Position, pose.Position),
                Is.LessThan(Vector3.Distance(before.Position, centered.Position) * .5f + .00001f));
            previous = pose;
        }
        AssertPose(previous, centered);
    }

    [TestCase(0, 1, 0, -.41f)]
    [TestCase(0, -1, 0, 1.11f)]
    [TestCase(1, 0, 0, .55f)]
    [TestCase(-1, 0, 0, .55f)]
    public void GuardHandsStayOppositeRightLeftUpAndDownBladeDirections(float x, float y, float z, float expectedHeight)
    {
        Vector3 direction=new Vector3(x,y,z); Vector3 last=Vector3.up;
        var settings=new SwordPoseSettings(Chest);
        Quaternion input=Quaternion.FromToRotation(Vector3.forward,direction);
        SwordPose guarded=SwordPoseMath.Calculate(input,true,settings,ref last);
        Assert.That(guarded.Position.y-Chest.y,Is.EqualTo(expectedHeight).Within(.00001f));
        if(x!=0f) Assert.That(Mathf.Sign(guarded.Position.x-Chest.x),Is.EqualTo(-Mathf.Sign(x)));
        Assert.That(guarded.Position.z,Is.GreaterThanOrEqualTo(settings.MinimumForward));
        AssertVector(guarded.Direction,direction);
    }

    [Test]
    public void HorizontalMirrorPreservesVerticalAndForwardMotionAndBladeRoll()
    {
        foreach (Quaternion input in new[] { Quaternion.Euler(-65f, 25f, 32f), Quaternion.Euler(40f, -135f, -70f), Quaternion.Euler(-90f, 0f, 0f) })
        {
            Quaternion mirrored = SwordPoseMath.MirrorHorizontal(input);
            foreach (Vector3 axis in new[] { Vector3.forward, Vector3.up })
            {
                Vector3 original = input * axis;
                AssertVector(mirrored * axis, new Vector3(-original.x, original.y, original.z));
            }
            Vector3 right = input * Vector3.right;
            AssertVector(mirrored * Vector3.right, new Vector3(right.x, -right.y, -right.z));
            Assert.That(Quaternion.Angle(SwordPoseMath.MirrorHorizontal(mirrored), input), Is.LessThan(0.01f));
        }
    }

    [TestCase(CombatOutcome.Hit, 3f, 0.6f)]
    [TestCase(CombatOutcome.Hit, -3f, 0.6f)]
    [TestCase(CombatOutcome.GuardBreak, 1.5f, 0.3f)]
    [TestCase(CombatOutcome.GuardSuccess, -2.5f, -0.5f)]
    [TestCase(CombatOutcome.Clash, 0f, 0f)]
    public void LateralPushFollowsTheCutAndSuccessfulGuardDeflectsIt(CombatOutcome outcome, float forward, float lateral)
    {
        var result = new CombatResult(outcome, 0, 1, forward);
        AssertVector(SwordPoseMath.PushDisplacement(result, Vector3.forward, Vector3.right), new Vector3(lateral, 0f, forward));
        AssertVector(SwordPoseMath.PushDisplacement(result, Vector3.forward, Vector3.left), new Vector3(-lateral, 0f, forward));
        AssertVector(SwordPoseMath.PushDisplacement(result, Vector3.forward, new Vector3(3f, -4f, 10f)), new Vector3(lateral * 0.6f, 0f, forward));
        AssertVector(SwordPoseMath.PushDisplacement(result, Vector3.forward, Vector3.down), new Vector3(0f, 0f, forward));
        AssertVector(SwordPoseMath.PushDisplacement(result, Vector3.forward, new Vector3(0.001f, -1f, 0f)), new Vector3(0f, 0f, forward));
        Quaternion arena = Quaternion.Euler(0f, 35f, 0f);
        AssertVector(SwordPoseMath.PushDisplacement(result, arena * Vector3.forward, arena * Vector3.right), arena * new Vector3(lateral, 0f, forward));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReactionSwordPreservesTheStruckPoseDuringHitStopAndReturnsToChangedInput(bool blocked)
    {
        Vector3 capturedChest = new Vector3(0f, 1f, 0f);
        Vector3 currentChest = capturedChest + new Vector3(.2f, -.08f, -.15f);
        var captured = new SwordPose(capturedChest + new Vector3(.1f, .15f, .9f), Quaternion.Euler(-15f, 20f, 35f));
        var input = new SwordPose(currentChest + new Vector3(-.3f, .4f, .6f), Quaternion.Euler(-40f, -65f, -20f));
        float duration = blocked ? ReactionSwordPose.GuardDurationSeconds : CombatReaction.RecoverySeconds;
        Vector3 direction = new Vector3(.6f, -.4f, -.7f).normalized;
        SwordPose frozen = ReactionSwordPose.Apply(captured, input, capturedChest, currentChest, direction, -.03f, duration, blocked);
        Assert.That(frozen.Position, Is.EqualTo(captured.Position), "Hit stop must freeze the visible grip even if input and chest change.");
        Assert.That(frozen.Rotation, Is.EqualTo(captured.Rotation));
        SwordPose started = ReactionSwordPose.Apply(captured, input, capturedChest, capturedChest, direction, 0f, duration, blocked);
        AssertVector(started.Position, captured.Position);
        Assert.That(Quaternion.Angle(started.Rotation, captured.Rotation), Is.LessThan(.06f));

        SwordPose beforeEnd = ReactionSwordPose.Apply(captured, input, capturedChest, currentChest, direction, duration - .0001f, duration, blocked);
        SwordPose ended = ReactionSwordPose.Apply(captured, input, capturedChest, currentChest, direction, duration, duration, blocked);
        Assert.That(Vector3.Distance(beforeEnd.Position, input.Position), Is.LessThan(.001f), "Returning control must not jump the grip at recovery's end.");
        Assert.That(Quaternion.Angle(beforeEnd.Rotation, input.Rotation), Is.LessThan(.1f));
        Assert.That(ended.Position, Is.EqualTo(input.Position));
        Assert.That(ended.Rotation, Is.EqualTo(input.Rotation), "Recovery must use the current input, not the captured forward pose.");
    }

    [TestCase(false, 1f, 0f)]
    [TestCase(false, -1f, 0f)]
    [TestCase(false, 0f, 1f)]
    [TestCase(false, 0f, -1f)]
    [TestCase(true, 0f, -1f)]
    public void ReactionGripAndBladeLoseBalanceWithTheBody(bool blocked, float x, float z)
    {
        Vector3 chest = new Vector3(0f, 1f, 0f), direction = new Vector3(x, 0f, z);
        var forward = new SwordPose(chest + new Vector3(0f, .1f, .9f), Quaternion.identity);
        float duration = blocked ? ReactionSwordPose.GuardDurationSeconds : CombatReaction.RecoverySeconds;
        float peakAge = duration * (blocked ? .5f : .16f);
        SwordPose reaction = ReactionSwordPose.Apply(forward, forward, chest, chest, direction, peakAge, duration, blocked);
        Assert.That(Quaternion.Angle(reaction.Rotation, forward.Rotation), Is.GreaterThan(25f), "The sword must visibly lose its forward stance with the body.");
        Assert.That(Vector3.Distance(reaction.Position, forward.Position), Is.GreaterThan(.12f), "The grip must follow the upper body instead of pivoting a fixed sword.");
        if (x != 0f)
            Assert.That(reaction.Position.x * x, Is.LessThan(-.1f), "Opposite lateral impacts must tilt the grip in opposite directions.");
        else
            Assert.That((reaction.Position.y - forward.Position.y) * (blocked ? 1f : -z), Is.GreaterThan(.2f));
    }

    [Test]
    public void ContactPointUsesFiniteSegmentsIncludingParallelAndZeroLengthCases()
    {
        AssertVector(SwordContact.ClosestMidpoint(Vector3.left, Vector3.right, Vector3.down, Vector3.up), Vector3.zero);
        AssertVector(SwordContact.ClosestMidpoint(Vector3.zero, Vector3.right, Vector3.up, Vector3.one), new Vector3(0, 0.5f, 0));
        AssertVector(SwordContact.ClosestMidpoint(Vector3.zero, Vector3.right, new Vector3(2, -1, 0), new Vector3(2, 1, 0)), new Vector3(1.5f, 0, 0));
        AssertVector(SwordContact.ClosestMidpoint(Vector3.zero, Vector3.zero, Vector3.down, Vector3.up), Vector3.zero);
        AssertVector(SwordContact.ClosestMidpoint(Vector3.left, Vector3.left, Vector3.right, Vector3.right), Vector3.zero);
    }

    [Test]
    public void GuardGripMovesLessNearForwardAndAcceleratesTowardBothSides()
    {
        var settings = new SwordPoseSettings(Chest);
        Vector3 last = Vector3.up;
        foreach (float side in new[] { -1f, 1f })
        {
            float previousTravel = 0f, previousIncrement = 0f;
            foreach (float lateral in new[] { .25f, .5f, .75f, 1f })
            {
                Vector3 direction = new Vector3(side * lateral, 0f, Mathf.Sqrt(1f - lateral * lateral));
                SwordPose guard = SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, direction), true, settings, ref last);
                float travel = Mathf.Abs(guard.Position.x - Chest.x);
                float increment = travel - previousTravel;
                Assert.That(Mathf.Sign(guard.Position.x - Chest.x), Is.EqualTo(-side));
                Assert.That(increment, Is.GreaterThan(previousIncrement), "Equal direction increments should move the hand increasingly quickly outside the middle.");
                AssertVector(guard.Direction, direction);
                previousTravel = travel;
                previousIncrement = increment;
            }
        }
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void GuardGripCrossesTheCenterSlowlyThenMovesOutwardQuicklyWithoutDelayingBladeRotation(int fps)
    {
        var settings=new SwordPoseSettings(Chest); Vector3 last=Vector3.up;
        SwordPose left=SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward,Vector3.left),true,settings,ref last);
        SwordPose forward=SwordPoseMath.Calculate(Quaternion.identity,true,settings,ref last);
        SwordPose right=SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward,Vector3.right),true,settings,ref last);
        Assert.That(left.Position.x,Is.GreaterThan(Chest.x));
        Assert.That(right.Position.x,Is.LessThan(Chest.x));
        Assert.That(forward.Position.x,Is.EqualTo(Chest.x).Within(.00001f));
        var inward=new GuardPoseTransition(); var outward=new GuardPoseTransition();
        inward.Update(left,true,0d); outward.Update(forward,true,0d);
        SwordPose slow=left,fast=forward;
        int frames=Mathf.RoundToInt(fps*.2f);
        for(int frame=1;frame<=frames;frame++)
        {
            double now=(double)frame/fps;
            SwordPose previousSlow=slow,previousFast=fast;
            slow=inward.Update(forward,true,now); fast=outward.Update(right,true,now);
            Assert.That(slow.Position.x,Is.InRange(Chest.x,left.Position.x));
            Assert.That(fast.Position.x,Is.InRange(right.Position.x,Chest.x));
            Assert.That(slow.Position.x,Is.LessThanOrEqualTo(previousSlow.Position.x));
            Assert.That(fast.Position.x,Is.LessThanOrEqualTo(previousFast.Position.x));
            Assert.That(Quaternion.Angle(previousSlow.Rotation,slow.Rotation),Is.LessThanOrEqualTo(GuardPoseTransition.GuardTurnDegreesPerSecond/fps+.06f));
            Assert.That(Quaternion.Angle(previousFast.Rotation,fast.Rotation),Is.LessThanOrEqualTo(GuardPoseTransition.GuardTurnDegreesPerSecond/fps+.06f));
        }
        float slowRemaining=(slow.Position.x-Chest.x)/(left.Position.x-Chest.x);
        float fastRemaining=(fast.Position.x-right.Position.x)/(Chest.x-right.Position.x);
        Assert.That(slowRemaining,Is.InRange(.15f,.4f),"Moving toward the middle keeps a visible gentle follow.");
        Assert.That(fastRemaining,Is.LessThan(.04f),"Moving out to the other side catches up promptly.");
        Assert.That(slowRemaining,Is.GreaterThan(fastRemaining*5f));
    }

    [Test]
    public void RearGuardInputsKeepTheEntireBladeInFrontOfTheBodyAndPreserveTransportedRoll()
    {
        var settings=new SwordPoseSettings(Chest);
        foreach(float yaw in new[]{-180f,-135f,-100f,100f,135f,180f})
            foreach(float pitch in new[]{-75f,-40f,0f,40f,75f})
                foreach(float roll in new[]{-60f,0f,80f})
                {
                    Quaternion input=Quaternion.Euler(pitch,yaw,roll);
                    Vector3 direction=input*Vector3.forward,last=Vector3.up;
                    SwordPose pose=SwordPoseMath.Calculate(input,true,settings,ref last);
                    Assert.That(pose.Direction.z,Is.GreaterThanOrEqualTo(-.00001f));
                    Assert.That(pose.Position.z,Is.GreaterThanOrEqualTo(settings.MinimumForward));
                    Assert.That((pose.Position+pose.Direction*CombatContact.BladeStart).z,Is.GreaterThanOrEqualTo(settings.MinimumForward-.00001f));
                    Assert.That((pose.Position+pose.Direction*CombatContact.BladeLength).z,Is.GreaterThanOrEqualTo(settings.MinimumForward-.00001f));
                    Vector3 transportedUp=Quaternion.FromToRotation(direction,pose.Direction)*(input*Vector3.up);
                    AssertVector(pose.Rotation*Vector3.up,transportedUp);
                }
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void EnteringGuardFromARearStanceMovesContinuouslyIntoTheFrontPlane(int fps)
    {
        var settings=new SwordPoseSettings(Chest);Vector3 last=Vector3.up;
        Quaternion rear=Quaternion.Euler(-40f,180f,23f);
        SwordPose normal=SwordPoseMath.Calculate(rear,false,settings,ref last);
        SwordPose guarded=SwordPoseMath.Calculate(rear,true,settings,ref last);
        var transition=new GuardPoseTransition();transition.Update(normal,false,0d);
        int frames=Mathf.CeilToInt(GuardPoseTransition.DurationSeconds*fps)+1;
        for(int frame=0;frame<=frames;frame++)
        {
            SwordPose current=transition.Update(guarded,true,1d+(double)frame/fps);
            if(frame==0) AssertPose(current,normal);
            if((double)frame/fps>=GuardPoseTransition.DurationSeconds)
            {
                Assert.That(current.Direction.z,Is.GreaterThanOrEqualTo(-.00001f));
                Assert.That(current.Position.z,Is.GreaterThanOrEqualTo(settings.MinimumForward));
                AssertPose(current,guarded);
            }
        }
    }

    [Test]
    public void GuardHandsExtendSixtyPercentBeyondTheOriginalRadiusInBothAxes()
    {
        var settings=new SwordPoseSettings(Chest);Vector3 last=Vector3.up;
        foreach(Vector3 direction in new[]{Vector3.left,Vector3.right,Vector3.up,Vector3.down})
        {
            SwordPose pose=SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward,direction),true,settings,ref last);
            float outside=direction.x!=0f?Mathf.Abs(pose.Position.x-Chest.x):
                Mathf.Abs(pose.Position.y-Chest.y-settings.GuardHeight-SwordPoseMath.GuardHeightOffset(direction.y));
            Assert.That(outside,Is.EqualTo(settings.GuardRadius*1.6f).Within(.00001f));
        }
    }

    private static void AssertPose(SwordPose actual, SwordPose expected)
    {
        AssertVector(actual.Position, expected.Position);
        Assert.That(Quaternion.Angle(actual.Rotation, expected.Rotation), Is.LessThan(.06f));
    }

    private static void AssertVector(Vector3 actual, Vector3 expected)
        => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.00001f));
}
