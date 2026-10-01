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
    public void GuardGripOpposesBladeAcrossTheBodyFrontPlane(float x, float y, float z)
    {
        Vector3 direction = new Vector3(x, y, z).normalized;
        Vector3 planar = new Vector3(x, y, 0f).normalized;
        Vector3 last = Vector3.up;
        SwordPose pose = SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, direction), true,
            Chest, Radius, Plane, ref last);
        Vector3 center = Chest + Vector3.forward * Plane;
        AssertVector(pose.Position, center - Radius * planar);
        AssertVector(pose.Direction, planar);
        AssertVector(last, planar);
        Assert.That(pose.Position.z, Is.EqualTo(center.z).Within(0.00001f));
        Assert.That((pose.Position + 2.1f * pose.Direction).z, Is.EqualTo(center.z).Within(0.00001f));
        Assert.That(Vector3.Dot(center - pose.Position, pose.Direction), Is.EqualTo(Radius).Within(0.00001f));
    }

    [TestCase(1)]
    [TestCase(-1)]
    public void NearlyAxialGuardKeepsLastDirectionAndStartsUpright(int forwardSign)
    {
        Vector3 last = Vector3.zero;
        Quaternion axial = Quaternion.FromToRotation(Vector3.forward, new Vector3(0.02f, 0.02f, forwardSign).normalized);
        SwordPose pose = SwordPoseMath.Calculate(axial, true, Chest, Radius, Plane, ref last);
        AssertVector(pose.Direction, Vector3.up);
        Vector3 diagonal = new Vector3(-1f, 1f, 0f).normalized;
        SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, diagonal), true, Chest, Radius, Plane, ref last);
        pose = SwordPoseMath.Calculate(axial, true, Chest, Radius, Plane, ref last);
        AssertVector(pose.Direction, diagonal);
        AssertVector(pose.Position, Chest + Vector3.forward * Plane - Radius * diagonal);
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
    public void GameplayGripStaysInFrontOfTheTorso(float x, float y, float z)
    {
        Quaternion input = Quaternion.FromToRotation(Vector3.forward, new Vector3(x, y, z).normalized);
        Vector3 last = Vector3.up;
        var settings = new SwordPoseSettings(Chest);
        foreach (bool guard in new[] { false, true })
        {
            SwordPose pose = SwordPoseMath.Calculate(input, guard, settings, ref last);
            Assert.That(pose.Position.z, Is.GreaterThanOrEqualTo(0.45f));
            if (guard)
            {
                Vector3 center = Chest + new Vector3(0, 0.55f, 0.55f);
                AssertVector(pose.Position + pose.Direction * 0.6f, center);
                Assert.That(pose.Position.y, Is.GreaterThanOrEqualTo(Chest.y - 0.05f - 0.00001f));
            }
            else
            {
                Assert.That(Quaternion.Angle(pose.Rotation, input), Is.LessThan(0.01f));
                if (z >= 0)
                    AssertVector(pose.Position, Chest + new Vector3(0, 0.1f, 0.55f) + pose.Direction * 0.55f);
            }
        }
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

    private static void AssertVector(Vector3 actual, Vector3 expected)
        => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.00001f));
}
