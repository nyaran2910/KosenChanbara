using System;
using UnityEngine;

public enum ControllerInputSource
{
    None,
    Phone,
    Hardware
}

public enum ControllerSlot
{
    P1 = 0,
    P2 = 1
}

[Flags]
public enum MotionDataFlags : byte
{
    None = 0,
    Guard = 1 << 0,
    OrientationValid = 1 << 1,
    GyroscopeValid = 1 << 2,
    AccelerationValid = 1 << 3,
    RecenterHeld = 1 << 4
}

public readonly struct MotionSample
{
    public MotionSample(Quaternion rotation, Vector3 angularVelocityDegrees, Vector3 linearAcceleration,
        bool guardHeld, bool orientationValid, bool gyroscopeValid, bool accelerationValid,
        uint sequence, ushort recenterSequence, double browserTimestampMilliseconds, double receivedRealtime)
        : this(rotation, angularVelocityDegrees, linearAcceleration, guardHeld, orientationValid,
            gyroscopeValid, accelerationValid, sequence, recenterSequence, browserTimestampMilliseconds, receivedRealtime, false) { }

    public MotionSample(
        Quaternion rotation,
        Vector3 angularVelocityDegrees,
        Vector3 linearAcceleration,
        bool guardHeld,
        bool orientationValid,
        bool gyroscopeValid,
        bool accelerationValid,
        uint sequence,
        ushort recenterSequence,
        double browserTimestampMilliseconds,
        double receivedRealtime,
        bool recenterHeld)
    {
        Rotation = rotation;
        AngularVelocityDegrees = angularVelocityDegrees;
        LinearAcceleration = linearAcceleration;
        GuardHeld = guardHeld;
        OrientationValid = orientationValid;
        GyroscopeValid = gyroscopeValid;
        AccelerationValid = accelerationValid;
        Sequence = sequence;
        RecenterSequence = recenterSequence;
        BrowserTimestampMilliseconds = browserTimestampMilliseconds;
        ReceivedRealtime = receivedRealtime;
        RecenterHeld = recenterHeld;
    }

    public Quaternion Rotation { get; }
    public Vector3 AngularVelocityDegrees { get; }
    public Vector3 LinearAcceleration { get; }
    public bool GuardHeld { get; }
    public bool RecenterHeld { get; }
    public bool OrientationValid { get; }
    public bool GyroscopeValid { get; }
    public bool AccelerationValid { get; }
    public uint Sequence { get; }
    public ushort RecenterSequence { get; }
    public double BrowserTimestampMilliseconds { get; }
    public double ReceivedRealtime { get; }
}

public static class MotionPacketCodec
{
    public const byte ProtocolVersion = 1;
    public const int MotionPacketLength = 56;
    public static bool TryDecodeMotion(byte[] bytes, double receivedRealtime, out MotionSample sample)
    {
        sample = default;
        if (bytes == null || bytes.Length != MotionPacketLength || bytes[0] != ProtocolVersion)
        {
            return false;
        }

        MotionDataFlags flags = (MotionDataFlags)bytes[1];
        ushort recenterSequence = ReadUInt16(bytes, 2);
        uint sequence = ReadUInt32(bytes, 4);
        double browserTimestamp = ReadDouble(bytes, 8);
        Quaternion browserRotation = new Quaternion(
            ReadSingle(bytes, 16), ReadSingle(bytes, 20), ReadSingle(bytes, 24), ReadSingle(bytes, 28));
        Vector3 browserGyroscope = new Vector3(
            ReadSingle(bytes, 32), ReadSingle(bytes, 36), ReadSingle(bytes, 40));
        Vector3 browserAcceleration = new Vector3(
            ReadSingle(bytes, 44), ReadSingle(bytes, 48), ReadSingle(bytes, 52));

        if (!IsFinite(browserTimestamp) || !IsFinite(browserRotation) ||
            !IsFinite(browserGyroscope) || !IsFinite(browserAcceleration))
        {
            return false;
        }

        bool orientationValid = (flags & MotionDataFlags.OrientationValid) != 0;
        bool gyroscopeValid = (flags & MotionDataFlags.GyroscopeValid) != 0;
        bool accelerationValid = (flags & MotionDataFlags.AccelerationValid) != 0;
        double rotationMagnitudeSquared =
            (double)browserRotation.x * browserRotation.x + (double)browserRotation.y * browserRotation.y +
            (double)browserRotation.z * browserRotation.z + (double)browserRotation.w * browserRotation.w;
        if (browserTimestamp < 0 ||
            (orientationValid && (rotationMagnitudeSquared < 0.25 || rotationMagnitudeSquared > 4.0)) ||
            (gyroscopeValid && !IsWithinAbsoluteLimit(browserGyroscope, 10000f)) ||
            (accelerationValid && !IsWithinAbsoluteLimit(browserAcceleration, 1000f)))
        {
            return false;
        }

        // Browser sensors are right-handed. Reflecting Z maps them to Unity's
        // left-handed coordinates while keeping the same physical phone motion.
        Quaternion unityRotation = new Quaternion(
            -browserRotation.x, -browserRotation.y, browserRotation.z, browserRotation.w).normalized;
        // Angular velocity is an axial vector. Unlike acceleration, a handedness
        // reflection also contributes the determinant sign.
        Vector3 unityGyroscope = new Vector3(
            -browserGyroscope.x, -browserGyroscope.y, browserGyroscope.z);
        Vector3 unityAcceleration = new Vector3(
            browserAcceleration.x, browserAcceleration.y, -browserAcceleration.z);

        sample = new MotionSample(
            unityRotation,
            unityGyroscope,
            unityAcceleration,
            (flags & MotionDataFlags.Guard) != 0,
            orientationValid,
            gyroscopeValid,
            accelerationValid,
            sequence,
            recenterSequence,
            browserTimestamp,
            receivedRealtime,
            (flags & MotionDataFlags.RecenterHeld) != 0);
        return true;
    }

    public static bool IsNewerSequence(uint candidate, uint current)
    {
        return unchecked((int)(candidate - current)) > 0;
    }

    public static bool IsNewerRecenterSequence(ushort candidate, ushort current)
    {
        return unchecked((short)(candidate - current)) > 0;
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        if (BitConverter.IsLittleEndian)
        {
            return BitConverter.ToUInt16(bytes, offset);
        }

        return (ushort)(bytes[offset] | bytes[offset + 1] << 8);
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        if (BitConverter.IsLittleEndian)
        {
            return BitConverter.ToUInt32(bytes, offset);
        }

        return (uint)(bytes[offset] | bytes[offset + 1] << 8 |
                      bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
    }

    private static float ReadSingle(byte[] bytes, int offset)
    {
        if (BitConverter.IsLittleEndian)
        {
            return BitConverter.ToSingle(bytes, offset);
        }

        byte[] value = new byte[4];
        Array.Copy(bytes, offset, value, 0, 4);
        Array.Reverse(value);
        return BitConverter.ToSingle(value, 0);
    }

    private static double ReadDouble(byte[] bytes, int offset)
    {
        if (BitConverter.IsLittleEndian)
        {
            return BitConverter.ToDouble(bytes, offset);
        }

        byte[] value = new byte[8];
        Array.Copy(bytes, offset, value, 0, 8);
        Array.Reverse(value);
        return BitConverter.ToDouble(value, 0);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(Quaternion value) =>
        IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    private static bool IsWithinAbsoluteLimit(Vector3 value, float limit) =>
        Mathf.Abs(value.x) <= limit && Mathf.Abs(value.y) <= limit && Mathf.Abs(value.z) <= limit;
}
