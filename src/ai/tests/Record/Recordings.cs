namespace SourceSharp.Ai.Tests.Record;

/// <summary>Makes small recordings in memory by driving a live session through a recorder.</summary>
internal static class Recordings
{
    internal static readonly AiRecordingHeader Header = new(7, "ss_test", 0x1111, 0x2222, 8, 4);

    internal static byte[] Make(Action<AiSession> drive)
    {
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, Header);
        var session = new AiSession(new InlineTransport(), recorder, Header.Slots, Header.SightedCapacity);
        drive(session);
        return bytes.ToArray();
    }

    internal static void Publish(AiSession s, int tick, params ActorId[] bots)
    {
        AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(tick));
        foreach (ActorId bot in bots)
        {
            PerceivedView v = snap.Add(bot, new SelfState(new AiVector(bot.Slot, tick, 0), default, default, 100, 0, 1, 18, 150, SelfFlags.Alive));
            v.AddSighted(new ActorFacts(new ActorId(7, 3), new AiVector(1, 2, 3), new AiVector(4, 5, 6), 2, ActorActivity.Reloading, 0.5f));
        }

        s.Publish(snap);
    }

    internal static AiReplayResult Replay(byte[] recording)
        => AiReplayer.Replay(new MemoryStream(recording), new InlineTransport());

    /// <summary>The records of a recording, as (type, payload) pairs, after the file header.</summary>
    internal static List<(AiRecordType Type, byte[] Payload)> Records(byte[] recording)
    {
        var s = new MemoryStream(recording);
        AiRecordFormat.ReadFileHeader(s);
        var list = new List<(AiRecordType, byte[])>();
        while (AiRecordFormat.TryReadRecord(s, out AiRecordType t, out byte[] p))
            list.Add((t, p));
        return list;
    }

    /// <summary>A recording rebuilt from records, so a fact can change one.</summary>
    internal static byte[] Build(IEnumerable<(AiRecordType Type, byte[] Payload)> records)
    {
        var s = new MemoryStream();
        AiRecordFormat.WriteFileHeader(s);
        foreach ((AiRecordType t, byte[] p) in records)
            AiRecordFormat.WriteRecord(s, t, p);
        return s.ToArray();
    }
}
