using System;
using System.Collections.Generic;
using NUnit.Framework;
using SchoolFestival.Combat;
using static SchoolFestival.Combat.ControllerPairingWire;

public sealed class ControllerPairingTests
{
    private static Frame Request(ulong device = 11, ulong request = 22) => new Frame { Type = Kind.Request, Device = device, Request = request };
    private static Frame Commit(Frame offer) { offer.Type = Kind.Commit; return offer; }
    private static Frame Pair(ControllerPairingRegistry registry, int slot, ulong device, double time)
    {
        registry.Begin(slot);
        Assert.That(registry.Handle(Request(device), "peer", time, out Frame offer), Is.True);
        Assert.That(registry.Handle(Commit(offer), "peer", time + 1, out Frame confirm), Is.True);
        Assert.That(registry.Accept(device, offer.Session, slot, time + 2), Is.True);
        return confirm;
    }
    [Test]
    public void WireRoundTripRejectsCorruptionReservedBytesAndWrongLengths()
    {
        Frame f = new Frame { Type = Kind.Offer, Device = 11, Host = 33, Request = 22, Session = 44, Slot = 1, Red = 55 };
        byte[] encoded = Encode(f);
        Assert.That(encoded.Length, Is.EqualTo(48));
        Assert.That(TryDecode(encoded, out Frame parsed), Is.True);
        Assert.That(parsed.Device, Is.EqualTo(11)); Assert.That(parsed.Session, Is.EqualTo(44)); Assert.That(parsed.Slot, Is.EqualTo(1));
        for (int i = 0; i < encoded.Length; i++)
        {
            var broken = (byte[])encoded.Clone(); broken[i] ^= 1;
            Assert.That(TryDecode(broken, out _), Is.False, "byte " + i);
        }
        encoded[41] = 1; Write(encoded, 44, Crc(encoded, 44), 4);
        Assert.That(TryDecode(encoded, out _), Is.False);
        Assert.That(TryDecode(new byte[49], out _), Is.False);
        // Also checked by the native client test (CRC32/IEEE and little endian).
        Assert.That(Convert.ToBase64String(Encode(f)), Is.EqualTo("S1ABAgsAAAAAAAAAIQAAAAAAAAAWAAAAAAAAACwAAAAAAAAAAQA3AAAAAAB5tH0J"));
    }
    [Test]
    public void EnrollmentRequiresMatchingCommitAndFirstSampleAndRetriesAreIdempotent()
    {
        var registry = new ControllerPairingRegistry(33, 0);
        Assert.That(registry.Handle(Request(), "peer", 0, out _), Is.False);
        registry.Begin(1);
        Assert.That(registry.Handle(Request(), "peer", 0, out Frame offer), Is.True);
        Assert.That(registry.Allows(11, offer.Session, 1, 0), Is.False);
        Assert.That(registry.Handle(Request(99), "peer2", .1, out _), Is.False);
        Assert.That(registry.Handle(Commit(offer), "wrong endpoint", 1, out _), Is.False);
        Assert.That(registry.Handle(Commit(offer), "peer", 1, out _), Is.True);
        Assert.That(registry.Revision, Is.Zero);
        Assert.That(registry.Accept(11, offer.Session + 1, 1, 2), Is.False);
        Assert.That(registry.Accept(11, offer.Session, 0, 2), Is.False);
        Assert.That(registry.Accept(11, offer.Session, 1, 2), Is.True);
        Assert.That(registry.CompletedSlot, Is.EqualTo(1)); Assert.That(registry.WaitingSlot, Is.EqualTo(-1));
        Assert.That(registry.Handle(Commit(offer), "peer", 3, out Frame retry), Is.True);
        Assert.That(retry.Type, Is.EqualTo(Kind.Confirm)); Assert.That(registry.Revision, Is.EqualTo(1));
    }
    [Test]
    public void CancelExpiryAndReassignmentDoNotAcceptOldSessionsOrDuplicateSlots()
    {
        var registry = new ControllerPairingRegistry(33, 0);
        Frame old = Pair(registry, 0, 11, 0);
        registry.Begin(1); registry.Handle(Request(77), "peer", 10, out Frame canceled); registry.Cancel();
        Assert.That(registry.Handle(Commit(canceled), "peer", 11, out _), Is.False);
        Assert.That(registry.MatchesBinding(old.Device, old.Session, 0), Is.True);
        registry.Begin(1); registry.Handle(Request(88), "peer", 20, out Frame expired);
        Assert.That(registry.Handle(Commit(expired), "peer", 33, out _), Is.False);
        Frame replacement = Pair(registry, 1, 11, 40);
        Assert.That(registry.MatchesBinding(old.Device, old.Session, 0), Is.False);
        Assert.That(registry.MatchesBinding(replacement.Device, replacement.Session, 1), Is.True);
    }
    [Test]
    public void PersistedIdentityDiscoversOnlyItsOwnHostAndSession()
    {
        var registry = new ControllerPairingRegistry(33, 0); registry.Load(1, 11, 44);
        Frame discover = new Frame { Type = Kind.Discover, Device = 11, Host = 33, Session = 44, Request = 55, Slot = 1 };
        Assert.That(registry.Handle(discover, "new IP", 100, out Frame found), Is.True);
        Assert.That(found.Type, Is.EqualTo(Kind.Found)); Assert.That(found.Request, Is.EqualTo(55));
        discover.Host++; Assert.That(registry.Handle(discover, "peer", 100, out _), Is.False);
        discover.Host--; discover.Session++; Assert.That(registry.Handle(discover, "peer", 100, out _), Is.False);
    }
    [Test]
    public void UsbEnvelopeHandlesEveryReadBoundaryAndNeverLeaksRejectedInnerPackets()
    {
        byte[] payload = new byte[64]; payload[0] = 0x4b; payload[1] = 0x4a;
        byte[] envelope = Wrap(payload, 11, 44);
        Assert.That(Unwrap(envelope, out ulong device, out ulong session, out byte[] actual), Is.True);
        Assert.That(device, Is.EqualTo(11)); Assert.That(session, Is.EqualTo(44)); Assert.That(actual, Is.EqualTo(payload));
        for (int split = 0; split <= 88; split++)
        {
            var stream = new PairedControllerStream(); var received = new List<byte[]>();
            var first = new byte[split]; var second = new byte[88 - split];
            Array.Copy(envelope, first, split); Array.Copy(envelope, split, second, 0, second.Length);
            stream.Feed(first, first.Length, received.Add); stream.Feed(second, second.Length, received.Add);
            Assert.That(received.Count, Is.EqualTo(1)); Assert.That(received[0], Is.EqualTo(envelope));
        }
        envelope[30] ^= 1;
        Assert.That(Unwrap(envelope, out _, out _, out _), Is.False);
        Assert.That(Unwrap(Wrap(payload, 0, 44), out _, out _, out _), Is.False);
    }
}
