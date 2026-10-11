using System;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class ControllerAxisMappingTests
{
    [TestCase(0, 1, 2, 3)]
    [TestCase(1, 1, 3, -2)]
    [TestCase(2, 2, 1, -3)]
    [TestCase(3, 2, 3, 1)]
    [TestCase(4, 3, 1, 2)]
    [TestCase(5, 3, 2, -1)]
    public void SixPresetsKeepPoseGyroscopeAndAccelerationInOneFrame(int index, float x, float y, float z)
    {
        var mapping = ControllerAxisMapping.FromPreset(index);
        Assert.That(mapping.PresetIndex, Is.EqualTo(index));
        Vector3 input = new Vector3(1, 2, 3), expected = new Vector3(x, y, z);
        Assert.That(Vector3.Distance(mapping.Gyroscope(input), expected), Is.LessThan(.001f));
        Assert.That(Vector3.Distance(mapping.Acceleration(input), expected), Is.LessThan(.001f));
        foreach (Vector3 axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
        {
            Quaternion observed = Quaternion.AngleAxis(24, axis);
            Quaternion intended = Quaternion.AngleAxis(24, mapping.Gyroscope(axis));
            Assert.That(Quaternion.Angle(mapping.Rotation(observed), intended), Is.LessThan(.05f));
        }
        Quaternion tilt = Quaternion.Euler(24, 40, 13);
        Assert.That(Vector3.Distance(mapping.Rotation(tilt) * mapping.Acceleration(input),
            mapping.Acceleration(tilt * input)), Is.LessThan(.001f));
    }

    [Test]
    public void StandardAndInvalidPresetSelectionAreExplicit()
    {
        Assert.That(ControllerAxisMapping.FromPreset(0).IsCalibrated, Is.False);
        Assert.That(ControllerAxisMapping.FromPreset(0).Rotation(Quaternion.Euler(20, 35, 50)),
            Is.EqualTo(Quaternion.Euler(20, 35, 50)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ControllerAxisMapping.FromPreset(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ControllerAxisMapping.FromPreset(6));
    }

    [Test]
    public void DirectionDisplayFollowsContinuousMotionWithoutMenuRearming()
    {
        var monitor = new AxisDirectionMonitor();
        Assert.That(monitor.Read(Quaternion.identity, Vector3.left * 480, true, true, 0), Is.EqualTo(Vector2Int.up));
        Assert.That(monitor.Read(Quaternion.identity, Vector3.up * 480, true, true, .02), Is.EqualTo(Vector2Int.right));
        Assert.That(monitor.Read(Quaternion.identity, Vector3.right * 480, true, true, .04), Is.EqualTo(Vector2Int.down));
        Assert.That(monitor.Read(Quaternion.identity, Vector3.down * 480, true, true, .06), Is.EqualTo(Vector2Int.left));
        Assert.That(monitor.Read(Quaternion.identity, Vector3.forward * 1000, true, true, .08), Is.EqualTo(Vector2Int.zero));
        Assert.That(monitor.Read(Quaternion.identity, Vector3.left * 360, true, true, .10), Is.EqualTo(Vector2Int.zero));
    }

    [Test]
    public void OrientationDisplayUsesPacketTimeAndSkipsDuplicatesAndClockGaps()
    {
        var monitor = new AxisDirectionMonitor();
        Assert.That(monitor.Read(Quaternion.identity, Vector3.zero, false, true, 10), Is.EqualTo(Vector2Int.zero));
        Assert.That(monitor.Read(Quaternion.Euler(-8,0,0), Vector3.zero, false, true, 10.02), Is.EqualTo(Vector2Int.up));
        Assert.That(monitor.Read(Quaternion.Euler(0,50,0), Vector3.zero, false, true, 10.02), Is.EqualTo(Vector2Int.zero));
        Assert.That(monitor.Read(Quaternion.identity, Vector3.zero, false, true, 0), Is.EqualTo(Vector2Int.zero));
        Assert.That(monitor.Read(Quaternion.Euler(-8,0,0), Vector3.zero, false, true, .02), Is.EqualTo(Vector2Int.up));
        Assert.That(monitor.Read(Quaternion.Euler(0,50,0), Vector3.zero, false, true, 1), Is.EqualTo(Vector2Int.zero));
        monitor.Read(Quaternion.identity, Vector3.zero, false, false, 1.02);
        Assert.That(monitor.Read(Quaternion.Euler(0,70,0), Vector3.zero, false, true, 1.04), Is.EqualTo(Vector2Int.zero));
    }
}
