using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// Format 10 (plan_bots_teams_tf2 P7, review-p7f F1): the Snapshot record's third tail carries what a class-based game
/// draws (the self's class, conditions and metal; each sighted actor's class, conditions, borrowed name and facing) and
/// the game's HUD, written in full when it changed and as one byte when it is the bot's last one. An older file has no
/// such tail and reads as before (FormatTests.AnOlderFormatFileStillReads, through format 9).
/// </summary>
public sealed class RecordFormatTenTests
{
    private sealed class Hud(byte[] bytes) : GameHud
    {
        public override byte[] Recorded() => bytes;
    }

    private static readonly ActorId Bot = new(2, 1), Seen = new(5, 9);

    private static AiSnapshot Snapshot(SnapshotRing ring, int tick, GameHud? hud)
    {
        AiSnapshot s = ring.BeginWrite(new Tick(tick));
        PerceivedView v = s.Add(Bot, new SelfState(default, default, default, 100, 0, 3, 4, 20, SelfFlags.Alive, Team: 2, Class: 3,
                                                   Conditions: ActorConditions.Burning, Metal: 150));
        v.AddSighted(new ActorFacts(Seen, new AiVector(1, 2, 3), default, 7, ActorActivity.None, 0.5f, 3, Class: 8,
                                    Conditions: ActorConditions.Disguised | ActorConditions.Cloaked, NameSlot: 4, Facing: new AiAngles(-10f, 135f, 0f)));
        v.SetGameHud(hud);
        ring.Publish(s);
        return s;
    }

    private static byte[] Encode(AiSnapshot s, AiHudMemo memo)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            AiRecordFormat.Write(w, s, null, memo);
        return ms.ToArray();
    }

    private static PerceivedView Decode(byte[] bytes, AiHudMemo memo)
        => AiRecordFormat.ReadSnapshot(new BinaryReader(new MemoryStream(bytes)), new SnapshotRing(2, 8, 4), memo).ViewFor(Bot.Slot)!;

    /// <summary>The self's class, conditions and metal, and the sighted actor's class, conditions, borrowed name and facing round-trip.</summary>
    [Fact]
    public void WhatTheGameDrawsRoundTrips()
    {
        var ring = new SnapshotRing(2, 8, 4);
        PerceivedView back = Decode(Encode(Snapshot(ring, 1, null), new AiHudMemo()), new AiHudMemo());
        Assert.Equal((3, ActorConditions.Burning, 150), (back.Self.Class, back.Self.Conditions, back.Self.Metal));
        ActorFacts f = back.Sighted[0];
        Assert.Equal((8, ActorConditions.Disguised | ActorConditions.Cloaked, 4, new AiAngles(-10f, 135f, 0f)), (f.Class, f.Conditions, f.NameSlot, f.Facing));
        Assert.Null(back.GameHud);
    }

    /// <summary>The HUD's bytes come back as a recorded HUD; the same HUD next snapshot is one byte, and reads as the bot's last.</summary>
    [Fact]
    public void TheHudIsWrittenWhenItChangesAndNamedAfter()
    {
        var ring = new SnapshotRing(4, 8, 4);
        var hud = new Hud([1, 2, 3, 4, 5]);
        var written = new AiHudMemo();
        byte[] first = Encode(Snapshot(ring, 1, hud), written), second = Encode(Snapshot(ring, 2, hud), written);
        Assert.Equal(first.Length - 9, second.Length);
        var read = new AiHudMemo();
        var a = Assert.IsType<RecordedGameHud>(Decode(first, read).GameHud);
        Assert.Equal([1, 2, 3, 4, 5], a.Bytes);
        Assert.Same(a, Decode(second, read).GameHud);
    }

    /// <summary>A snapshot naming the bot's last HUD to a reader that kept none is refused, not read as no HUD.</summary>
    [Fact]
    public void ALastHudNoReaderKeptIsRefused()
    {
        var ring = new SnapshotRing(4, 8, 4);
        var hud = new Hud([9]);
        var written = new AiHudMemo();
        Encode(Snapshot(ring, 1, hud), written);
        byte[] second = Encode(Snapshot(ring, 2, hud), written);
        Assert.Throws<InvalidDataException>(() => Decode(second, new AiHudMemo()));
    }
}
