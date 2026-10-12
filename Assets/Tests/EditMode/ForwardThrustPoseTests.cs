using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class ForwardThrustPoseTests
{
    [TestCase(-24f, 0f)] [TestCase(24f, 0f)] [TestCase(0f, 24f)] [TestCase(0f, -24f)]
    public void ANearForwardThrustCentersBeforeItsDriveAndReturnsToTheInput(float yaw, float pitch)
    {
        Vector3 remembered = Vector3.up;
        var settings = new SwordPoseSettings(new Vector3(.04f, .8f, -.02f));
        SwordPose input = SwordPoseMath.Calculate(Quaternion.Euler(pitch, yaw, 37f), false, settings, ref remembered);
        SwordPose forward = SwordPoseMath.Calculate(Quaternion.identity, false, settings, ref remembered);
        SwordPose beginning = ThrustPose.ApplyCentered(input, input, forward, 0f);
        Assert.That(Vector3.Distance(beginning.Position, input.Position), Is.LessThan(.00001f));
        Assert.That(Quaternion.Angle(beginning.Rotation, input.Rotation), Is.LessThan(.01f));
        foreach (float age in new[] { ThrustPose.ChamberSeconds, .04f, .07f, .09f, .11f })
        {
            SwordPose driven = ThrustPose.ApplyCentered(input, input, forward, age);
            Assert.That(Vector3.Angle(driven.Direction, Vector3.forward), Is.LessThan(.01f));
            Assert.That(driven.Position.x, Is.EqualTo(forward.Position.x).Within(.00001f));
            Assert.That(driven.Position.y, Is.EqualTo(forward.Position.y).Within(.00001f));
        }
        SwordPose recovered = ThrustPose.ApplyCentered(input, input, forward, ThrustPose.DurationSeconds);
        Assert.That(recovered.Position, Is.EqualTo(input.Position));
        Assert.That(recovered.Rotation, Is.EqualTo(input.Rotation));
    }

    [Test]
    public void ReleasingASidewaysGuardCanStillChamberSmoothlyIntoAStraightThrust()
    {
        Vector3 remembered = Vector3.up;
        var settings = new SwordPoseSettings(Vector3.up);
        SwordPose guard = SwordPoseMath.Calculate(Quaternion.Euler(0f, 90f, 0f), true, settings, ref remembered);
        SwordPose input = SwordPoseMath.Calculate(Quaternion.Euler(0f, 18f, 0f), false, settings, ref remembered);
        SwordPose forward = SwordPoseMath.Calculate(Quaternion.identity, false, settings, ref remembered);
        Assert.That(ThrustPose.ApplyCentered(input, guard, forward, 0f).Position, Is.EqualTo(guard.Position));
        SwordPose halfway = ThrustPose.ApplyCentered(input, guard, forward, ThrustPose.ChamberSeconds * .5f);
        Assert.That(Vector3.Angle(halfway.Direction, Vector3.forward), Is.InRange(40f, 50f));
        Assert.That(Vector3.Angle(ThrustPose.ApplyCentered(input, guard, forward, .04f).Direction, Vector3.forward), Is.LessThan(.01f));
    }

    [TestCase(3f)] [TestCase(18f)] [TestCase(-24f)]
    public void AThrustCannotDamageAlongAnOffCenterBladeEvenIfItMovesForward(float yaw)
    {
        Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        var attack = new CombatPose(Vector3.forward * .4f, direction, Vector3.forward * .65f, direction,
            Vector3.zero, Vector3.forward, Vector3.forward, Vector3.zero, Vector3.forward);
        Vector3 body = Vector3.forward * 2.4f;
        var defence = new CombatPose(body, Vector3.up, body, Vector3.up,
            body, Vector3.back, Vector3.zero, body, Vector3.up);
        Assert.That(CombatContact.TrySweptBladeTouchesBody(attack.PreviousGrip, direction,
            attack.Grip, direction, body, out _, CombatContact.ThrustExtraPadding), Is.True);
        Assert.That(CombatContact.Resolve(AttackKind.Thrust, attack, defence, false).Outcome, Is.EqualTo(CombatOutcome.None));
    }
}
