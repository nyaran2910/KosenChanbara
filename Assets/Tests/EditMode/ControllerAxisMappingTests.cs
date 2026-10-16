using System;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class ControllerInputDirectionTests
{
    [Test]
    public void ReversedPcbMountFlipsPitchAndYawAndPreservesBladeTwist()
    {
        Vector3 RotateAxis(Vector3 axis) {
            HardwareControllerFrame.Rotation(Quaternion.AngleAxis(30f, axis))
                .ToAngleAxis(out _, out Vector3 mapped);
            return mapped;
        }
        // With the PCB face reversed, sensor roll/pitch still map to the
        // original grip axes, while both swing directions reverse at source.
        Assert.That(Vector3.Angle(RotateAxis(Vector3.right), Vector3.back), Is.LessThan(.01f));
        Assert.That(Vector3.Angle(RotateAxis(Vector3.forward), Vector3.left), Is.LessThan(.01f));
        Assert.That(Vector3.Angle(RotateAxis(Vector3.up), Vector3.down), Is.LessThan(.01f));
        Assert.That(Vector3.Angle(HardwareControllerFrame.Vector(Vector3.up), Vector3.down), Is.LessThan(.01f));
    }

    [TestCase(1, 0)]
    [TestCase(-1, 0)]
    [TestCase(0, 1)]
    [TestCase(0, -1)]
    public void UprightReversedBoardKeepsPhysicalMenuAndCombatDirectionsAfterReset(int horizontal, int vertical)
    {
        Quaternion oldMount = Quaternion.Euler(0f, 90f, 0f);
        // Turn the board over while leaving the top of the grip upright.
        Quaternion boardTurn = Quaternion.AngleAxis(180f, Vector3.right);
        Quaternion basePose = Quaternion.Euler(-90f, 0f, 0f);
        Quaternion gesture = Quaternion.AngleAxis(24f, vertical != 0 ? Vector3.left * vertical : Vector3.up * horizontal);
        foreach (Quaternion reference in new[] { Quaternion.identity, Quaternion.Euler(23f, -70f, 41f) })
        {
            Quaternion sensorBefore = reference * boardTurn;
            Quaternion sensorAfter = reference * Quaternion.Inverse(oldMount) * gesture * oldMount * boardTurn;
            Quaternion decodedBefore = HardwareControllerFrame.Rotation(sensorBefore);
            Quaternion decodedAfter = HardwareControllerFrame.Rotation(sensorAfter);
            Quaternion sword = basePose * Quaternion.Inverse(decodedBefore) * decodedAfter;
            Assert.That(Quaternion.Angle(sword, basePose * gesture), Is.LessThan(.06f),
                "Turning the PCB over must not change the sword's physical movement after a reset.");
            var navigation = new MenuGestureNavigation();
            navigation.Read(decodedBefore, true, false, 0, 0);
            Assert.That(navigation.Read(decodedAfter, true, false, 0, .02), Is.EqualTo(new Vector2Int(horizontal, vertical)));

            Vector3 physicalAcceleration = new Vector3(2f, -3f, 4f);
            Vector3 beforeTurn = Quaternion.Inverse(oldMount) * physicalAcceleration;
            Vector3 afterTurn = Quaternion.Inverse(boardTurn) * beforeTurn;
            Assert.That(Vector3.Distance(HardwareControllerFrame.Vector(afterTurn), physicalAcceleration), Is.LessThan(.0001f));
        }
    }

    [Test]
    public void SwordPoseChangesProduceTheSameDirectionWithoutSourceSpecificCorrection()
    {
        Vector2Int Read() {
            var input = new MenuGestureNavigation();
            input.Read(Quaternion.identity, true, false, 0, 0);
            return input.Read(Quaternion.AngleAxis(24, Vector3.up), true, false, 0, .02);
        }
        Assert.That(Read(), Is.EqualTo(Vector2Int.right));
    }

    [Test]
    public void DirectionDisplayFollowsContinuousMotionWithoutMenuRearming()
    {
        var monitor = new AxisDirectionMonitor();
        Assert.That(monitor.Read(Quaternion.identity, true, 0), Is.EqualTo(Vector2Int.zero));
        Assert.That(monitor.Read(Quaternion.AngleAxis(24, Vector3.up), true, .02), Is.EqualTo(Vector2Int.right));
        Assert.That(monitor.Read(Quaternion.AngleAxis(48, Vector3.up), true, .04), Is.EqualTo(Vector2Int.right));
        Assert.That(monitor.Read(Quaternion.AngleAxis(24, Vector3.up), true, .06), Is.EqualTo(Vector2Int.left));
        Assert.That(monitor.Read(Quaternion.AngleAxis(25, Vector3.up), true, .08), Is.EqualTo(Vector2Int.zero));
    }

    [Test]
    public void OrientationDisplayUsesPacketTimeAndSkipsDuplicatesAndClockGaps()
    {
        var monitor = new AxisDirectionMonitor();
        Quaternion upright = Quaternion.Euler(-90, 0, 0);
        Assert.That(monitor.Read(upright, true, 10), Is.EqualTo(Vector2Int.zero));
        Assert.That(monitor.Read(Quaternion.Euler(-82, 0, 0), true, 10.02), Is.EqualTo(Vector2Int.down));
        Assert.That(monitor.Read(Quaternion.Euler(-82, 50, 0), true, 10.02), Is.EqualTo(Vector2Int.zero));
        Assert.That(monitor.Read(upright, true, 0), Is.EqualTo(Vector2Int.zero));
        Assert.That(monitor.Read(Quaternion.Euler(-82, 0, 0), true, .02), Is.EqualTo(Vector2Int.down));
        Assert.That(monitor.Read(Quaternion.Euler(-82, 50, 0), true, 1), Is.EqualTo(Vector2Int.zero));
        monitor.Read(upright, false, 1.02);
        Assert.That(monitor.Read(Quaternion.Euler(-70, 0, 0), true, 1.04), Is.EqualTo(Vector2Int.zero));
    }
}
