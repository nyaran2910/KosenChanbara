public static class HardwareGameAcknowledgement
{
    public const int PacketLength = 16;

    // Sent only after the game's UDP receiver accepts this controller's frame.
    public static byte[] Encode(byte group, ControllerSlot slot, uint sequence)
    {
        byte[] bytes = new byte[PacketLength];
        bytes[0] = 0x4b; bytes[1] = 0x41; bytes[2] = 1; bytes[3] = PacketLength;
        bytes[4] = group; bytes[5] = (byte)slot;
        Write(bytes, 8, sequence);
        Write(bytes, 12, HardwareMotionPacketCodec.Crc32(bytes, 12));
        return bytes;
    }

    private static void Write(byte[] bytes, int offset, uint value)
    {
        for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (i * 8));
    }
}
