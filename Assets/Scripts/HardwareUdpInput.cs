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

    public void ResetSlot(ControllerSlot slot)
    {
        int index = (int)slot;
        owners[index] = null;
        foreach (SenderState state in senders.Values) state.Valid[index] = false;
    }

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
        restarted = !state.Valid[index];
        uint millis = (uint)sample.BrowserTimestampMilliseconds;
        double extendedMillis = millis;
        if (!restarted)
        {
            double gap = realtime - state.Previous[index].ReceivedRealtime;
            bool newerSequence = MotionPacketCodec.IsNewerSequence(sample.Sequence, state.Previous[index].Sequence);
            uint elapsed = unchecked(millis - state.Millis[index]);
            bool plausibleClock = HardwareMotionStream.IsPlausibleClockAdvance(elapsed, gap);
            if (!newerSequence || !plausibleClock)
            {
                if (sample.Sequence == state.Previous[index].Sequence || gap <= HardwareMotionStream.RestartAfterSeconds)
                    return false;
                restarted = true;
            }
            else extendedMillis = state.Previous[index].BrowserTimestampMilliseconds + elapsed;
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

/// <summary>Called under the hub lock. Selects the newest fresh sample and preserves the pose epoch across transport handoff.</summary>
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
    private readonly MotionSample[] selectedSample = new MotionSample[2];
    private readonly bool[] selectedValid = new bool[2];
    private readonly int[,] observedVersions = new int[2, 2];
    private readonly bool[,] observedVersionValid = new bool[2, 2];

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
    public void ClearSlot(ControllerSlot slot)
    {
        int index = (int)slot;
        usb.Valid[index] = udp.Valid[index] = selectedValid[index] = false;
        selected[index] = null;
        observedVersionValid[index, 0] = observedVersionValid[index, 1] = false;
    }
    public void ClearUsb() { Array.Clear(usb.Valid, 0, usb.Valid.Length); }
    public void Clear()
    {
        ClearUsb();
        Array.Clear(udp.Valid, 0, udp.Valid.Length);
        Array.Clear(selected, 0, selected.Length);
        Array.Clear(selectedValid, 0, selectedValid.Length);
        Array.Clear(observedVersionValid, 0, observedVersionValid.Length);
    }
    public bool TrySelect(ControllerSlot slot, double realtime, out MotionSample sample, out bool reset, out string port)
    {
        int index = (int)slot;
        bool udpFresh = Fresh(udp, index, realtime);
        bool usbFresh = Fresh(usb, index, realtime);
        Source source = !udpFresh ? (usbFresh ? usb : null) :
            !usbFresh ? udp : udp.Samples[index].ReceivedRealtime >= usb.Samples[index].ReceivedRealtime ? udp : usb;
        sample = default;
        reset = false;
        port = string.Empty;
        if (source == null) return false;
        Source previousSource = selected[index];
        MotionSample previousSample = selectedSample[index];
        if (selectedValid[index] && source != previousSource && Fresh(previousSource, index, realtime))
        {
            MotionSample candidate = source.Samples[index];
            bool olderEpoch = candidate.RecenterSequence != previousSample.RecenterSequence &&
                !MotionPacketCodec.IsNewerRecenterSequence(candidate.RecenterSequence, previousSample.RecenterSequence);
            bool olderSequence = candidate.RecenterSequence == previousSample.RecenterSequence &&
                candidate.Sequence != previousSample.Sequence &&
                !MotionPacketCodec.IsNewerSequence(candidate.Sequence, previousSample.Sequence);
            uint candidateClockDelta = unchecked(HardwareMotionStream.ClockTicks(candidate.BrowserTimestampMilliseconds) -
                HardwareMotionStream.ClockTicks(previousSample.BrowserTimestampMilliseconds));
            bool olderClock = candidate.BrowserTimestampMilliseconds != previousSample.BrowserTimestampMilliseconds &&
                !HardwareMotionStream.IsPlausibleClockAdvance(candidateClockDelta, candidate.ReceivedRealtime - previousSample.ReceivedRealtime);
            if (olderEpoch || olderSequence || olderClock) source = previousSource;
        }

        sample = source.Samples[index];
        int sourceIndex = source == udp ? 1 : 0;
        bool sourceRestarted = source == previousSource && observedVersionValid[index, sourceIndex] &&
            observedVersions[index, sourceIndex] != source.Versions[index];
        bool epochChanged = selectedValid[index] && sample.RecenterSequence != previousSample.RecenterSequence;
        bool sequenceRolledBack = selectedValid[index] && sample.Sequence != previousSample.Sequence &&
            !MotionPacketCodec.IsNewerSequence(sample.Sequence, previousSample.Sequence);
        uint clockDelta = unchecked(HardwareMotionStream.ClockTicks(sample.BrowserTimestampMilliseconds) -
            HardwareMotionStream.ClockTicks(previousSample.BrowserTimestampMilliseconds));
        bool clockRolledBack = selectedValid[index] && sample.BrowserTimestampMilliseconds != previousSample.BrowserTimestampMilliseconds &&
            !HardwareMotionStream.IsPlausibleClockAdvance(clockDelta, sample.ReceivedRealtime - previousSample.ReceivedRealtime);
        reset = !selectedValid[index] || sourceRestarted || epochChanged || sequenceRolledBack || clockRolledBack;
        selected[index] = source;
        observedVersions[index, sourceIndex] = source.Versions[index];
        observedVersionValid[index, sourceIndex] = true;
        selectedSample[index] = sample;
        selectedValid[index] = true;
        port = source.Ports[index];
        return true;
    }
    private static bool Fresh(Source source, int index, double realtime) => source.Valid[index] &&
        realtime - source.Samples[index].ReceivedRealtime <= HardwareControllerHub.StaleAfterSeconds;
}
