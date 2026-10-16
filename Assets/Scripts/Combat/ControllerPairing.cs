using System;
using System.Collections.Generic;

namespace SchoolFestival.Combat
{
    // Shared wire layout: kojo_HW/include/controller_pairing.h.
    public static class ControllerPairingWire
    {
        public const int ControlLength = 48;
        public enum Kind : byte { Request = 1, Offer, Commit, Confirm, Discover, Found }
        public struct Frame
        {
            public Kind Type;
            public ulong Device, Host, Request, Session;
            public byte Slot, Group, Red, Green, Blue;
        }
        public static ulong NewId()
        {
            ulong value = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            return value == 0 ? 1UL : value;
        }
        public static uint Crc(byte[] data, int length)
        {
            uint crc = uint.MaxValue;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            }
            return crc ^ uint.MaxValue;
        }
        public static ulong Read(byte[] data, int offset, int length)
        {
            ulong value = 0;
            for (int i = 0; i < length; i++) value |= (ulong)data[offset + i] << (i * 8);
            return value;
        }
        public static void Write(byte[] data, int offset, ulong value, int length)
        { for (int i = 0; i < length; i++) data[offset + i] = (byte)(value >> (i * 8)); }
        public static byte[] Encode(Frame frame)
        {
            var data = new byte[ControlLength];
            data[0] = 0x4b; data[1] = 0x50; data[2] = 1; data[3] = (byte)frame.Type;
            Write(data, 4, frame.Device, 8); Write(data, 12, frame.Host, 8);
            Write(data, 20, frame.Request, 8); Write(data, 28, frame.Session, 8);
            data[36] = frame.Slot; data[37] = frame.Group;
            data[38] = frame.Red; data[39] = frame.Green; data[40] = frame.Blue;
            Write(data, 44, Crc(data, 44), 4);
            return data;
        }
        public static bool TryDecode(byte[] data, out Frame frame)
        {
            frame = default;
            if (data == null || data.Length != ControlLength || data[0] != 0x4b || data[1] != 0x50 || data[2] != 1
                || data[3] < 1 || data[3] > 6 || data[36] > 1 || data[37] > 2
                || data[41] != 0 || data[42] != 0 || data[43] != 0 || Read(data, 44, 4) != Crc(data, 44)) return false;
            frame = new Frame { Type = (Kind)data[3], Device = Read(data, 4, 8), Host = Read(data, 12, 8),
                Request = Read(data, 20, 8), Session = Read(data, 28, 8), Slot = data[36], Group = data[37],
                Red = data[38], Green = data[39], Blue = data[40] };
            return frame.Device != 0 && frame.Request != 0;
        }
        // KD wraps either a KJ sample (88 bytes total) or KA feedback (40).
        public static byte[] Wrap(byte[] payload, ulong device, ulong session)
        {
            var data = new byte[payload.Length + 24];
            data[0] = 0x4b; data[1] = 0x44; data[2] = 1; data[3] = (byte)data.Length;
            Write(data, 4, device, 8); Write(data, 12, session, 8);
            Array.Copy(payload, 0, data, 20, payload.Length);
            Write(data, data.Length - 4, Crc(data, data.Length - 4), 4);
            return data;
        }
        public static bool Unwrap(byte[] data, out ulong device, out ulong session, out byte[] payload)
        {
            device = session = 0; payload = null;
            if (data == null || (data.Length != 88 && data.Length != 40) || data[0] != 0x4b || data[1] != 0x44
                || data[2] != 1 || data[3] != data.Length || Read(data, data.Length - 4, 4) != Crc(data, data.Length - 4)) return false;
            device = Read(data, 4, 8); session = Read(data, 12, 8);
            if (device == 0 || session == 0) return false;
            payload = new byte[data.Length - 24]; Array.Copy(data, 20, payload, 0, payload.Length);
            return true;
        }
    }

    // Transport-independent state machine. The hub serializes access under its buffer lock.
    public sealed class ControllerPairingRegistry
    {
        public struct Binding { public ulong Device, Session; }
        public readonly ulong Host;
        public readonly byte Group;
        public readonly Binding[] Bindings = new Binding[2];
        public int WaitingSlot { get; private set; } = -1;
        public int Revision { get; private set; }
        public int CompletedSlot { get; private set; } = -1;
        private ControllerPairingWire.Frame candidate, lastCommit;
        private string candidateEndpoint;
        private bool committed;
        private double expires, lastCommitUntil;
        public ControllerPairingRegistry(ulong host, byte group) { Host = host; Group = group; }
        public void Begin(int slot)
        {
            if (slot < 0 || slot > 1) throw new ArgumentOutOfRangeException(nameof(slot));
            Cancel(); WaitingSlot = slot; CompletedSlot = -1;
        }
        public void Cancel() { WaitingSlot = -1; candidate = default; committed = false; }
        public void Load(int slot, ulong device, ulong session)
        {
            if (device != 0 && session != 0) Bindings[slot] = new Binding { Device = device, Session = session };
        }
        public bool Handle(ControllerPairingWire.Frame message, string endpoint, double now, out ControllerPairingWire.Frame response)
        {
            response = default;
            if (message.Type == ControllerPairingWire.Kind.Request && WaitingSlot >= 0 && message.Host == 0 && message.Session == 0)
            {
                if (candidate.Device != 0 && now > expires) { candidate = default; committed = false; }
                if (candidate.Device == 0)
                {
                    candidate = new ControllerPairingWire.Frame { Type = ControllerPairingWire.Kind.Offer, Device = message.Device,
                        Host = Host, Request = message.Request, Session = ControllerPairingWire.NewId(), Slot = (byte)WaitingSlot, Group = Group,
                        Red = (byte)(32 + (Host & 63)), Green = (byte)(32 + ((Host >> 8) & 63)), Blue = (byte)(32 + ((Host >> 16) & 63)) };
                    candidateEndpoint = endpoint; expires = now + 12;
                }
                if (message.Device != candidate.Device || message.Request != candidate.Request || endpoint != candidateEndpoint) return false;
                response = candidate; return true;
            }
            if (message.Type == ControllerPairingWire.Kind.Commit)
            {
                if (WaitingSlot >= 0 && now <= expires && Matches(message, candidate) && endpoint == candidateEndpoint)
                {
                    committed = true; response = candidate; response.Type = ControllerPairingWire.Kind.Confirm;
                    return true;
                }
                if (now <= lastCommitUntil && Matches(message, lastCommit))
                { response = lastCommit; response.Type = ControllerPairingWire.Kind.Confirm; return true; }
            }
            if (message.Type == ControllerPairingWire.Kind.Discover && message.Host == Host && message.Group == Group
                && MatchesBinding(message.Device, message.Session, message.Slot))
            { response = message; response.Type = ControllerPairingWire.Kind.Found; return true; }
            return false;
        }
        private static bool Matches(ControllerPairingWire.Frame a, ControllerPairingWire.Frame b) =>
            a.Device == b.Device && a.Host == b.Host && a.Request == b.Request && a.Session == b.Session && a.Slot == b.Slot && a.Group == b.Group;
        public bool MatchesBinding(ulong device, ulong session, int slot) => slot >= 0 && slot < 2
            && device != 0 && session != 0 && Bindings[slot].Device == device && Bindings[slot].Session == session;
        public bool Allows(ulong device, ulong session, int slot, double now) => MatchesBinding(device, session, slot)
            || (committed && WaitingSlot == slot && now <= expires && candidate.Device == device && candidate.Session == session);
        // Only call after the inner KJ frame passes CRC, ordering and clock checks.
        public bool Accept(ulong device, ulong session, int slot, double now)
        {
            if (!Allows(device, session, slot, now)) return false;
            if (committed && candidate.Device == device && candidate.Session == session && WaitingSlot == slot)
            {
                for (int i = 0; i < 2; i++) if (Bindings[i].Device == device) Bindings[i] = default;
                Bindings[slot] = new Binding { Device = device, Session = session };
                lastCommit = candidate; lastCommitUntil = now + 15;
                CompletedSlot = slot; Revision++; Cancel();
            }
            return true;
        }
    }

    // Serial reads can split frames or contain diagnostics; never inspect a rejected envelope's inner KJ as another device.
    public sealed class PairedControllerStream
    {
        private readonly List<byte> buffer = new List<byte>(2048);
        public void Feed(byte[] data, int count, Action<byte[]> receive)
        {
            if (data == null || count < 0 || count > data.Length) throw new ArgumentOutOfRangeException(nameof(count));
            for (int i = 0; i < count; i++) buffer.Add(data[i]);
            int used = 0;
            while (buffer.Count - used >= 4)
            {
                if (buffer[used] != 0x4b || buffer[used + 1] != 0x44 || buffer[used + 2] != 1 || buffer[used + 3] != 88) { used++; continue; }
                if (buffer.Count - used < 88) break;
                var frame = buffer.GetRange(used, 88).ToArray(); used += 88;
                receive(frame);
            }
            if (used > 0) buffer.RemoveRange(0, used);
        }
    }
}
