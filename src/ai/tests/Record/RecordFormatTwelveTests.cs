using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// Format 12 (round 2's S4): the conditions past format 10's byte (Jarated, milked, wet, marked for death, blast-airborne, scoped)
/// ride a fourth tail on the Snapshot record, written only when a view holds one, so a snapshot with none is format 11's
/// bytes and a format 11 file reads as it did.
/// </summary>
public sealed class RecordFormatTwelveTests
{
    private static readonly ActorId Bot = new(2, 1), Seen = new(5, 9), Other = new(7, 3);

    private const ActorConditions SelfHas = ActorConditions.Burning | ActorConditions.Jarated | ActorConditions.Wet;
    private const ActorConditions SeenHas = ActorConditions.Cloaked | ActorConditions.MarkedForDeath | ActorConditions.BlastAirborne;
    private const ActorConditions OtherHas = ActorConditions.Milked | ActorConditions.Wet | ActorConditions.Taunting | ActorConditions.Zoomed;

    private static AiSnapshot Snapshot(ActorConditions self, ActorConditions seen, ActorConditions other)
    {
        var ring = new SnapshotRing(2, 8, 4);
        AiSnapshot s = ring.BeginWrite(new Tick(4));
        PerceivedView v = s.Add(Bot, new SelfState(default, default, default, 100, 0, 3, 4, 20, SelfFlags.Alive, Team: 2, Class: 2,
                                                   Conditions: self, Metal: 0));
        v.AddSighted(new ActorFacts(Seen, new AiVector(1, 2, 3), default, 7, ActorActivity.None, 0.5f, 3, Class: 8, Conditions: seen, NameSlot: 5));
        v.AddSighted(new ActorFacts(Other, new AiVector(4, 5, 6), default, 7, ActorActivity.None, 1f, 3, Class: 1, Conditions: other, NameSlot: 7));
        ring.Publish(s);
        return s;
    }

    private static byte[] Encode(AiSnapshot s)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            AiRecordFormat.Write(w, s);
        return ms.ToArray();
    }

    private static PerceivedView Decode(byte[] bytes)
        => AiRecordFormat.ReadSnapshot(new BinaryReader(new MemoryStream(bytes)), new SnapshotRing(2, 8, 4)).ViewFor(Bot.Slot)!;

    private static ActorConditions Low(ActorConditions c) => (ActorConditions)((int)c & PerceivedView.LowConditions);

    /// <summary>The self's and every sighted actor's conditions, format 10's byte and format 12's bits alike, come back as written.</summary>
    [Fact]
    public void EveryConditionRoundTrips()
    {
        PerceivedView back = Decode(Encode(Snapshot(SelfHas, SeenHas, OtherHas)));
        Assert.Equal((SelfHas, SeenHas, OtherHas), (back.Self.Conditions, back.Sighted[0].Conditions, back.Sighted[1].Conditions));
    }

    /// <summary>The tail is appended: one byte for the self and one per sighted actor, after format 11's bytes, which stay the same.</summary>
    [Fact]
    public void TheTailFollowsFormatElevensBytesUnchanged()
    {
        byte[] high = Encode(Snapshot(SelfHas, SeenHas, OtherHas));
        byte[] eleven = Encode(Snapshot(Low(SelfHas), Low(SeenHas), Low(OtherHas)));
        Assert.Equal(eleven.Length + 3, high.Length);
        Assert.Equal(eleven, high[..eleven.Length]);
        Assert.Equal([(byte)((int)SelfHas >> 8), (byte)((int)SeenHas >> 8), (byte)((int)OtherHas >> 8)], high[eleven.Length..]);
    }

    /// <summary>
    /// A snapshot whose views hold no condition past format 10's byte writes no tail at all: it is the tail's three bytes
    /// shorter than the same snapshot with one high bit, and as long as one with no conditions.
    /// </summary>
    [Fact]
    public void NoHighConditionWritesNoTail()
    {
        AiSnapshot low = Snapshot(Low(SelfHas), Low(SeenHas), Low(OtherHas));
        AiSnapshot oneHigh = Snapshot(Low(SelfHas), Low(SeenHas), Low(OtherHas) | ActorConditions.MarkedForDeath);
        Assert.False(AiRecordFormat.HasHighConditions(low));
        Assert.True(AiRecordFormat.HasHighConditions(oneHigh));
        Assert.Equal(Encode(oneHigh).Length - 3, Encode(low).Length);
        Assert.Equal(Encode(Snapshot(ActorConditions.None, ActorConditions.None, ActorConditions.None)).Length, Encode(low).Length);
    }

    /// <summary>A format 11 snapshot (format 12's bytes cut before the tail, as a format 11 build wrote them) reads its byte's conditions and none past it.</summary>
    [Fact]
    public void AFormatElevenSnapshotReadsItsByteAndNoMore()
    {
        byte[] high = Encode(Snapshot(SelfHas, SeenHas, OtherHas));
        PerceivedView back = Decode(high[..^3]);
        Assert.Equal((Low(SelfHas), Low(SeenHas), Low(OtherHas)), (back.Self.Conditions, back.Sighted[0].Conditions, back.Sighted[1].Conditions));
    }
}
