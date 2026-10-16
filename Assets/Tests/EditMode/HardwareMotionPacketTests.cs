using SchoolFestival.Combat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class HardwareMotionPacketTests
{
    private static Type Codec => Type.GetType("HardwareMotionPacketCodec, Assembly-CSharp", true);
    private static Type Stream => Type.GetType("HardwareMotionStream, Assembly-CSharp", true);
    private readonly List<object> received = new List<object>();
    private readonly List<bool> restarted = new List<bool>();

    [SetUp]
    public void ClearReceivedSamples() { received.Clear(); restarted.Clear(); }

    [Test]
    public void HardwarePacketChecksCrcIdentityLengthGroupSlotAndFiniteSensorValues()
    {
        byte[] packet = Packet(10, 100);
        Assert.That(Decode(packet), Is.True);
        foreach (int offset in new[] { 0, 2, 3, 4, 5, 6, 7, 18, 20, 60 })
        {
            byte[] broken = (byte[])packet.Clone();
            broken[offset] ^= 0x80;
            if (offset < 20) Seal(broken);
            Assert.That(Decode(broken), Is.False, "offset " + offset);
        }
        byte[] nan = Packet(10, 100);
        Write(nan, 36, BitConverter.GetBytes(float.NaN));
        Seal(nan);
        Assert.That(Decode(nan), Is.False);
        byte[] zeroRotation = Packet(10, 100);
        Write(zeroRotation, 32, BitConverter.GetBytes(0f));
        Seal(zeroRotation);
        Assert.That(Decode(zeroRotation), Is.False);
    }

    [Test]
    public void ResetHoldSurvivesPacketDecodingAndUsbStreamWithoutARecenterCommand()
    {
        byte[] bytes = Packet(1, 10);
        bytes[6] = 16; // Buttons remain usable even before the IMU is ready.
        Seal(bytes);
        Assert.That(Get<bool>(DecodeSample(bytes), "RecenterHeld"), Is.True);
        object stream = Activator.CreateInstance(Stream, new object[] { (byte)0 });
        Feed(stream, bytes, 1);
        Assert.That(Get<bool>(received.Single(), "RecenterHeld"), Is.True);
        Assert.That(Get<ushort>(received.Single(), "RecenterSequence"), Is.Zero);
        bytes = Packet(2, 20); bytes[6] = 0; Seal(bytes);
        Feed(stream, bytes, 1.01);
        Assert.That(Get<bool>(received.Last(), "RecenterHeld"), Is.False);
    }

    [Test]
    public void MissingSensorHeartbeatRemainsConnectedWithoutAValidPose()
    {
        byte[] bytes = Packet(1, 1);
        bytes[6] = 0;
        Seal(bytes);
        object sample = DecodeSample(bytes);
        Assert.That(Get<bool>(sample, "OrientationValid"), Is.False);
        Assert.That(Get<bool>(sample, "GuardHeld"), Is.False);
        Assert.That(Get<Quaternion>(sample, "Rotation"), Is.EqualTo(Quaternion.identity));
    }

    [Test]
    public void HardwareMapsTheSameAxesAndUnitsAsPhonePackets()
    {
        byte[] bytes = Packet(12, 300);
        Write(bytes, 36, BitConverter.GetBytes(40f));
        Write(bytes, 40, BitConverter.GetBytes(20f));
        Write(bytes, 44, BitConverter.GetBytes(10f));
        Seal(bytes);
        object sample = DecodeSample(bytes);
        Assert.That(Get<Vector3>(sample, "AngularVelocityDegrees"), Is.EqualTo(
            HardwareControllerFrame.Vector(new Vector3(-40, -20, 10))));
        Assert.That(Get<Quaternion>(sample, "Rotation"), Is.EqualTo(Quaternion.identity));
        Assert.That(Get<bool>(sample, "GuardHeld"), Is.True);
        Assert.That(Get<uint>(sample, "Sequence"), Is.EqualTo(12u));
    }

    [Test]
    public void ArbitraryUsbChunksNoiseBadCrcAndOtherGroupsResynchronize()
    {
        object stream = Activator.CreateInstance(Stream, new object[] { (byte)0 });
        byte[] damaged = Packet(1, 10); damaged[25] ^= 1;
        byte[] wrongGroup = Packet(2, 20); wrongGroup[4] = 1; Seal(wrongGroup);
        byte[] all = new byte[] { 1, 2, 0x4b, 0x4a, 99 }.Concat(damaged).Concat(wrongGroup)
            .Concat(Packet(3, 30)).Concat(Packet(4, 40)).ToArray();
        int offset = 0;
        foreach (int size in new[] { 1, 2, 67, 3, 20, 5, 80, 2, 100 })
        {
            int count = Math.Min(size, all.Length - offset);
            Feed(stream, all.Skip(offset).Take(count).ToArray(), 1.0);
            offset += count;
        }
        if (offset < all.Length) Feed(stream, all.Skip(offset).ToArray(), 1.0);
        Assert.That(received.Select(s => Get<uint>(s, "Sequence")), Is.EqualTo(new uint[] { 3, 4 }));
    }

    [Test]
    public void SequenceAndMillisWrapAndOrdinaryGapResumeWithoutRestart()
    {
        object stream = Activator.CreateInstance(Stream, new object[] { (byte)0 });
        Feed(stream, Packet(uint.MaxValue, uint.MaxValue - 9), 1);
        Feed(stream, Packet(0, 10), 1.02);
        Assert.That(received.Count, Is.EqualTo(2));
        Assert.That(Get<double>(received[1], "BrowserTimestampMilliseconds") - Get<double>(received[0], "BrowserTimestampMilliseconds"), Is.EqualTo(20));
        Feed(stream, Packet(0, 10), 1.03); // duplicate
        Feed(stream, Packet(1, 5), 1.04); // backwards clock
        Assert.That(received.Count, Is.EqualTo(2));
        Feed(stream, Packet(1, 20), 1.05);
        Assert.That(received.Count, Is.EqualTo(3));
        Feed(stream, Packet(2, 1040), 2); // a long packet gap with forward counters is not a reboot
        Assert.That(received.Count, Is.EqualTo(4));
        Feed(stream, Packet(0, 1), 3); // counter and clock rollback after silence is a reboot
        Assert.That(received.Count, Is.EqualTo(5));
        Feed(stream, Packet(1, 0), 4); // even with a forward sequence, a rolled-back clock is a reboot
        Assert.That(received.Count, Is.EqualTo(6));
        Assert.That(restarted, Is.EqualTo(new[] { true, false, false, false, true, true }));
    }

    private void Collect(object sample, bool reset) { received.Add(sample); restarted.Add(reset); }
    private void Feed(object stream, byte[] bytes, double time)
    {
        MethodInfo method = Stream.GetMethod("Feed");
        Type actionType = method.GetParameters()[3].ParameterType;
        Type[] args = actionType.GenericTypeArguments;
        ParameterExpression slot = Expression.Parameter(args[0]);
        ParameterExpression sample = Expression.Parameter(args[1]);
        ParameterExpression reset = Expression.Parameter(typeof(bool));
        Delegate callback = Expression.Lambda(actionType,
            Expression.Call(Expression.Constant(this), GetType().GetMethod("Collect", BindingFlags.Instance | BindingFlags.NonPublic),
                Expression.Convert(sample, typeof(object)), reset), slot, sample, reset).Compile();
        method.Invoke(stream, new object[] { bytes, bytes.Length, time, callback });
    }
    private static bool Decode(byte[] bytes)
    {
        object[] args = { bytes, 1d, (byte)0, Activator.CreateInstance(Type.GetType("ControllerSlot, Assembly-CSharp", true)), null };
        return (bool)Codec.GetMethod("TryDecode").Invoke(null, args);
    }
    private static object DecodeSample(byte[] bytes)
    {
        object[] args = { bytes, 1d, (byte)0, Activator.CreateInstance(Type.GetType("ControllerSlot, Assembly-CSharp", true)), null };
        Assert.That(Codec.GetMethod("TryDecode").Invoke(null, args), Is.True);
        return args[4];
    }
    private static T Get<T>(object sample, string property) => (T)sample.GetType().GetProperty(property).GetValue(sample);
    private static byte[] Packet(uint sequence, uint millis)
    {
        byte[] bytes = new byte[64];
        bytes[0] = 0x4b; bytes[1] = 0x4a; bytes[2] = 1; bytes[3] = 64; bytes[6] = 15;
        Write(bytes, 8, BitConverter.GetBytes(sequence)); Write(bytes, 12, BitConverter.GetBytes(millis));
        Write(bytes, 32, BitConverter.GetBytes(1f));
        Seal(bytes);
        return bytes;
    }
    private static void Seal(byte[] bytes) => Write(bytes, 60, BitConverter.GetBytes((uint)Codec.GetMethod("Crc32").Invoke(null, new object[] { bytes, 60 })));
    private static void Write(byte[] target, int offset, byte[] value)
    {
        if (!BitConverter.IsLittleEndian) Array.Reverse(value);
        Array.Copy(value, 0, target, offset, value.Length);
    }
}
