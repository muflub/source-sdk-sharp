namespace SourceSharp.Ai;

/// <summary>What a replay produced next to what the recording holds.</summary>
public sealed record AiReplayResult(
    AiRecordingHeader Header,
    byte[] RecordedIntents,
    byte[] ReplayedIntents,
    int RecordedIntentCount,
    int ReplayedIntentCount,
    int UserCmds)
{
    /// <summary>The replay's intent stream is the recording's, byte for byte (§2.6).</summary>
    public bool Identical => RecordedIntents.AsSpan().SequenceEqual(ReplayedIntents);
}

/// <summary>
/// Feeds a recording's inputs to fresh brains through a transport and
/// collects the intent stream they produce, beside the one the recording
/// holds (plan_hl2mp_ai §2.6). Each intent is encoded exactly as the
/// recorder encodes it, so "identical" is a byte comparison.
/// </summary>
public static class AiReplayer
{
    public static AiReplayResult Replay(Stream recording, IAiTransport transport)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(transport);

        AiRecordFormat.ReadFileHeader(recording);
        if (!AiRecordFormat.TryReadRecord(recording, out AiRecordType first, out byte[] headerBytes)
            || first != AiRecordType.Header)
            throw new InvalidDataException("an .airec file starts with its Header record");

        AiRecordingHeader header = Parse(headerBytes, AiRecordFormat.ReadHeader);
        var replayed = new IntentCapture();
        var session = new AiSession(transport, replayed, header.Slots, header.SightedCapacity);
        var recorded = new IntentCapture();
        int userCmds = 0;

        while (AiRecordFormat.TryReadRecord(recording, out AiRecordType type, out byte[] payload))
        {
            using var r = new BinaryReader(new MemoryStream(payload, writable: false));
            switch (type)
            {
                case AiRecordType.Header:
                    throw new InvalidDataException("an .airec file has one Header record");
                case AiRecordType.BotAdded:
                    ActorId bot = AiRecordFormat.ReadActor(r);
                    ulong seed = r.ReadUInt64();
                    byte[] skills = r.ReadBytes(r.ReadUInt16());
                    session.AddBot(bot, seed, skills);
                    break;
                case AiRecordType.BotRemoved:
                    session.RemoveBot(AiRecordFormat.ReadActor(r));
                    break;
                case AiRecordType.Event:
                    session.Push(AiRecordFormat.ReadEvent(r));
                    break;
                case AiRecordType.Snapshot:
                    session.Publish(AiRecordFormat.ReadSnapshot(r, session.Snapshots));
                    break;
                case AiRecordType.Intent:
                    recorded.Intent(AiRecordFormat.ReadIntent(r));
                    break;
                case AiRecordType.UserCmd:
                    AiRecordFormat.ReadUserCmd(r);
                    userCmds++;
                    break;
            }

            if (r.BaseStream.Position != payload.Length)
                throw new InvalidDataException($"a {type} record holds {payload.Length - r.BaseStream.Position} bytes its type does not");
        }

        return new AiReplayResult(header, recorded.Bytes(), replayed.Bytes(), recorded.Count, replayed.Count, userCmds);
    }

    private static T Parse<T>(byte[] payload, Func<BinaryReader, T> read)
    {
        using var r = new BinaryReader(new MemoryStream(payload, writable: false));
        T value = read(r);
        if (r.BaseStream.Position != payload.Length)
            throw new InvalidDataException($"a record holds {payload.Length - r.BaseStream.Position} bytes its type does not");
        return value;
    }

    /// <summary>Encodes each intent it is handed, in order; ignores everything else.</summary>
    private sealed class IntentCapture : IAiRecordSink
    {
        private readonly MemoryStream _bytes = new();
        private readonly BinaryWriter _w;

        public IntentCapture() => _w = new BinaryWriter(_bytes);

        public int Count { get; private set; }

        public byte[] Bytes()
        {
            _w.Flush();
            return _bytes.ToArray();
        }

        public void Intent(in IntentCommand intent)
        {
            AiRecordFormat.Write(_w, intent);
            Count++;
        }

        public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels) { }

        public void BotRemoved(ActorId bot) { }

        public void Event(in AiEvent e) { }

        public void Snapshot(AiSnapshot snapshot) { }

        public void UserCmd(in AiUserCmd cmd) { }
    }
}
