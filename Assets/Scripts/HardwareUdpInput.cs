using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

/// <summary>Validates one complete KJ packet per datagram, with independent sender clocks.</summary>
public sealed class HardwareUdpInput
{
    private sealed class SenderState
    {
        public readonly MotionSample[] Previous = new MotionSample[2];
        public readonly bool[] Valid = new bool[2];
        public readonly uint[] Millis = new uint[2];
        public double LastReceived;
    }

    private readonly byte group;
    private readonly Dictionary<IPEndPoint, SenderState> senders = new Dictionary<IPEndPoint, SenderState>();
    private readonly IPEndPoint[] owners = new IPEndPoint[2];
    private readonly double[] ownerReceived = new double[2];

    public HardwareUdpInput(byte group = 0) { this.group = group; }

    public bool TryAccept(byte[] datagram, IPEndPoint sender, double realtime,
        out ControllerSlot slot, out MotionSample sample, out bool restarted)
    {
        slot = ControllerSlot.P1;
        sample = default;
        restarted = false;
        if (sender == null || sender.AddressFamily != AddressFamily.InterNetwork ||
            !HardwareMotionPacketCodec.TryDecode(datagram, realtime, out byte packetGroup, out slot, out sample) || packetGroup != group)
            return false;
        int index = (int)slot;
        bool ownerChanged = owners[index] == null || !owners[index].Equals(sender);
        if (ownerChanged && owners[index] != null && realtime - ownerReceived[index] <= HardwareControllerHub.StaleAfterSeconds)
            return false;

        if (!senders.TryGetValue(sender, out SenderState state))
        {
            // Keep malformed/flooding peers from creating an unbounded history table.
            if (senders.Count >= 128)
            {
                List<IPEndPoint> expired = new List<IPEndPoint>();
                foreach (KeyValuePair<IPEndPoint, SenderState> item in senders)
                    if (realtime - item.Value.LastReceived > HardwareMotionStream.RestartAfterSeconds) expired.Add(item.Key);
                foreach (IPEndPoint endpoint in expired) senders.Remove(endpoint);
                if (senders.Count >= 128) return false;
            }
            state = new SenderState();
            senders.Add(new IPEndPoint(sender.Address, sender.Port), state);
        }
        restarted = !state.Valid[index] || realtime - state.Previous[index].ReceivedRealtime > HardwareMotionStream.RestartAfterSeconds;
        uint millis = (uint)sample.BrowserTimestampMilliseconds;
        double extendedMillis = millis;
        if (!restarted)
        {
            if (!MotionPacketCodec.IsNewerSequence(sample.Sequence, state.Previous[index].Sequence)) return false;
            uint elapsed = unchecked(millis - state.Millis[index]);
            if (elapsed > 10000) return false;
            extendedMillis = state.Previous[index].BrowserTimestampMilliseconds + elapsed;
        }
        sample = new MotionSample(sample.Rotation, sample.AngularVelocityDegrees, sample.LinearAcceleration,
            sample.GuardHeld, sample.OrientationValid, sample.GyroscopeValid, sample.AccelerationValid,
            sample.Sequence, sample.RecenterSequence, extendedMillis, realtime, sample.RecenterHeld);
        state.Previous[index] = sample;
        state.Millis[index] = millis;
        state.Valid[index] = true;
        state.LastReceived = realtime;
        owners[index] = new IPEndPoint(sender.Address, sender.Port);
        ownerReceived[index] = realtime;
        restarted |= ownerChanged;
        return true;
    }
}

/// <summary>Called under the hub lock. Fresh UDP wins without USB/UDP duplicate flapping.</summary>
public sealed class HardwareTransportSamples
{
    private sealed class Source
    {
        public readonly MotionSample[] Samples = new MotionSample[2];
        public readonly bool[] Valid = new bool[2];
        public readonly int[] Versions = new int[2];
        public readonly string[] Ports = new string[2];
    }
    private readonly Source usb = new Source();
    private readonly Source udp = new Source();
    private readonly Source[] selected = new Source[2];
    private readonly int[] selectedVersion = new int[2];

    public void SubmitUsb(ControllerSlot slot, MotionSample sample, bool restarted, string port) => Submit(usb, slot, sample, restarted, port);
    public void SubmitUdp(ControllerSlot slot, MotionSample sample, bool restarted, string port) => Submit(udp, slot, sample, restarted, port);
    private static void Submit(Source source, ControllerSlot slot, MotionSample sample, bool restarted, string port)
    {
        int index = (int)slot;
        source.Samples[index] = sample;
        source.Valid[index] = true;
        source.Ports[index] = port;
        if (restarted) source.Versions[index]++;
    }
    public void ClearUsb() { Array.Clear(usb.Valid, 0, usb.Valid.Length); }
    public void Clear()
    {
        ClearUsb();
        Array.Clear(udp.Valid, 0, udp.Valid.Length);
        Array.Clear(selected, 0, selected.Length);
    }
    public bool TrySelect(ControllerSlot slot, double realtime, out MotionSample sample, out bool reset, out string port)
    {
        int index = (int)slot;
        Source source = Fresh(udp, index, realtime) ? udp : Fresh(usb, index, realtime) ? usb : null;
        sample = default;
        reset = false;
        port = string.Empty;
        if (source == null) return false;
        reset = selected[index] != source || selectedVersion[index] != source.Versions[index];
        selected[index] = source;
        selectedVersion[index] = source.Versions[index];
        sample = source.Samples[index];
        port = source.Ports[index];
        return true;
    }
    private static bool Fresh(Source source, int index, double realtime) => source.Valid[index] &&
        realtime - source.Samples[index].ReceivedRealtime <= HardwareControllerHub.StaleAfterSeconds;
}
