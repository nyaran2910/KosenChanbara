using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>kojo_HW docs/PROTOCOL.md: little-endian, 64 bytes, CRC32/IEEE.</summary>
public static class HardwareMotionPacketCodec
{
    public const int PacketLength = 64;

    public static bool TryDecode(byte[] bytes, double receivedRealtime, out byte group, out ControllerSlot slot, out MotionSample sample)
    {
        group = 0;
        slot = ControllerSlot.P1;
        sample = default;
        if (bytes == null || bytes.Length != PacketLength || bytes[0] != 0x4b || bytes[1] != 0x4a ||
            bytes[2] != 1 || bytes[3] != PacketLength || bytes[4] > 2 || bytes[5] > 1 ||
            (bytes[6] & 0xe0) != 0 || bytes[7] != 0 || bytes[18] != 0 || bytes[19] != 0 ||
            ReadUInt32(bytes, 60) != Crc32(bytes, 60))
            return false;

        group = bytes[4];
        slot = (ControllerSlot)bytes[5];
        // Convert through the same canonical right-handed sensor mapping as phones.
        byte[] phonePacket = new byte[MotionPacketCodec.MotionPacketLength];
        phonePacket[0] = MotionPacketCodec.ProtocolVersion;
        phonePacket[1] = bytes[6];
        Array.Copy(bytes, 16, phonePacket, 2, 2);
        Array.Copy(bytes, 8, phonePacket, 4, 4);
        byte[] timestamp = BitConverter.GetBytes((double)ReadUInt32(bytes, 12));
        if (!BitConverter.IsLittleEndian) Array.Reverse(timestamp);
        Array.Copy(timestamp, 0, phonePacket, 8, 8);
        Array.Copy(bytes, 20, phonePacket, 16, 40);
        return MotionPacketCodec.TryDecodeMotion(phonePacket, receivedRealtime, out sample);
    }

    public static uint Crc32(byte[] bytes, int count)
    {
        uint crc = uint.MaxValue;
        for (int i = 0; i < count; i++)
        {
            crc ^= bytes[i];
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        return crc ^ uint.MaxValue;
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
}

/// <summary>Reassembles arbitrary USB reads; state and validation run on the receive thread.</summary>
public sealed class HardwareMotionStream
{
    public const double RestartAfterSeconds = 0.5;
    private readonly byte group;
    private readonly List<byte> buffer = new List<byte>(2048);
    private readonly MotionSample[] previous = new MotionSample[2];
    private readonly bool[] hasPrevious = new bool[2];
    private readonly uint[] lastMillis = new uint[2];
    private readonly double[] extendedMillis = new double[2];

    public HardwareMotionStream(byte group = 0) { this.group = group; }

    public void Feed(byte[] bytes, int count, double realtime, Action<ControllerSlot, MotionSample, bool> receive)
    {
        if (bytes == null || count < 0 || count > bytes.Length) throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = 0; i < count; i++) buffer.Add(bytes[i]);
        int consumed = 0;
        while (buffer.Count - consumed >= HardwareMotionPacketCodec.PacketLength)
        {
            if (buffer[consumed] != 0x4b || buffer[consumed + 1] != 0x4a)
            {
                consumed++;
                continue;
            }
            byte[] packet = buffer.GetRange(consumed, HardwareMotionPacketCodec.PacketLength).ToArray();
            if (!HardwareMotionPacketCodec.TryDecode(packet, realtime, out byte packetGroup, out ControllerSlot slot, out MotionSample sample))
            {
                consumed++;
                continue;
            }
            consumed += HardwareMotionPacketCodec.PacketLength;
            if (packetGroup != group) continue;
            int index = (int)slot;
            bool restarted = !hasPrevious[index] || realtime - previous[index].ReceivedRealtime > RestartAfterSeconds;
            uint millis = (uint)sample.BrowserTimestampMilliseconds;
            if (!restarted)
            {
                if (!MotionPacketCodec.IsNewerSequence(sample.Sequence, previous[index].Sequence)) continue;
                uint elapsed = unchecked(millis - lastMillis[index]);
                // millis() may wrap; backward time or impossible jumps within a live stream are invalid.
                if (elapsed > 10000) continue;
                extendedMillis[index] += elapsed;
            }
            else extendedMillis[index] = millis;
            lastMillis[index] = millis;
            sample = new MotionSample(sample.Rotation, sample.AngularVelocityDegrees, sample.LinearAcceleration,
                sample.GuardHeld, sample.OrientationValid, sample.GyroscopeValid, sample.AccelerationValid,
                sample.Sequence, sample.RecenterSequence, extendedMillis[index], realtime, sample.RecenterHeld);
            previous[index] = sample;
            hasPrevious[index] = true;
            receive(slot, sample, restarted);
        }
        if (consumed > 0) buffer.RemoveRange(0, consumed);
    }
}
