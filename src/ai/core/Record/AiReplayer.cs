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
    public static AiReplayResult Replay(Stream recording, IAiTransport transport) => Replay(recording, transport, null, null);

    /// <summary>
    /// Replays with the brains remembering over <paramref name="world"/>
    /// (§5.0), and <paramref name="published"/> told after each snapshot's
    /// steps ran: what <c>ai-replay heat</c> draws the beliefs from.
    /// </summary>
    public static AiReplayResult Replay(Stream recording, IAiTransport transport, MemoryWorld? world, Action<AiSession, Tick>? published)
        => Replay(recording, transport, world, null, null, published);

    /// <summary>
    /// Replays with the brains deciding as the game's did: over
    /// <paramref name="world"/>, with the skill <paramref name="curves"/> and the
    /// game's <paramref name="deciders"/> (its trees), so the intents are the
    /// recording's when nothing has changed (§2.6).
    /// </summary>
    public static AiReplayResult Replay(Stream recording, IAiTransport transport, MemoryWorld? world, SkillCurves? curves,
                                        Func<BrainSetup, IDecider?>? deciders, Action<AiSession, Tick>? published)
        => Replay(recording, transport, world, curves, deciders, null, published);

    /// <summary>
    /// Replays a recording that carries its world (format 2's World record):
    /// the brains remember and decide over the recorded map knowledge and
    /// curves, on the graph and sight <paramref name="nav"/> answers for it
    /// (refused when its hash is not the recorded one), with the game's
    /// <paramref name="deciders"/>. A live recording replays this way.
    /// </summary>
    public static AiReplayResult ReplayWithRecordedWorld(Stream recording, IAiTransport transport,
                                                         Func<AiRecordedWorld, (NavGraph? Graph, ICellSight? Sight)> nav,
                                                         Func<BrainSetup, IDecider?> deciders, Action<AiSession, Tick>? published = null)
    {
        ArgumentNullException.ThrowIfNull(nav);
        return Replay(recording, transport, null, null, deciders, nav, published);
    }

    private static AiReplayResult Replay(Stream recording, IAiTransport transport, MemoryWorld? world, SkillCurves? curves,
                                         Func<BrainSetup, IDecider?>? deciders, Func<AiRecordedWorld, (NavGraph? Graph, ICellSight? Sight)>? nav,
                                         Action<AiSession, Tick>? published)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(transport);

        AiRecordFormat.ReadFileHeader(recording);
        if (!AiRecordFormat.TryReadRecord(recording, out AiRecordType first, out byte[] headerBytes)
            || first != AiRecordType.Header)
            throw new InvalidDataException("an .airec file starts with its Header record");

        AiRecordingHeader header = Parse(headerBytes, AiRecordFormat.ReadHeader);
        var replayed = new IntentCapture();
        var session = new AiSession(transport, replayed, header.Slots, header.SightedCapacity) { World = world, Curves = curves, Deciders = deciders };
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
                    AiSnapshot snap = AiRecordFormat.ReadSnapshot(r, session.Snapshots);
                    session.Publish(snap);
                    // A replay outruns any pool: it waits rather than let a lane drop a step (§2.6).
                    if (session.Counters.InFlight >= AiSession.LaneDepth)
                        session.Drain(wait: true);
                    if (published is not null)
                    {
                        // The beliefs are read as of this tick: every step handed over has run.
                        session.Drain(wait: true);
                        published(session, snap.Tick);
                    }
                    break;
                case AiRecordType.Intent:
                    recorded.Intent(AiRecordFormat.ReadIntent(r));
                    break;
                case AiRecordType.UserCmd:
                    AiRecordFormat.ReadUserCmd(r);
                    userCmds++;
                    break;
                case AiRecordType.Frame:
                    AiRecordFormat.ReadFrame(r);
                    break;
                case AiRecordType.Observed:
                    // A person's sight: the metrics', never a brain's.
                    AiRecordFormat.ReadEvent(r);
                    break;
                case AiRecordType.World:
                    AiRecordedWorld known = AiRecordFormat.ReadWorld(r);
                    if (nav is null)
                        break;
                    (NavGraph? graph, ICellSight? sight) = nav(known);
                    if (known.NavGraphHash != 0 && graph?.Hash != known.NavGraphHash)
                        throw new InvalidDataException($"the recording's nav graph is {known.NavGraphHash:x16}, the one at hand {graph?.Hash ?? 0:x16}: rebuild the map's nav or re-record");
                    session.World = new MemoryWorld(known.Map, graph, known.TickSeconds, id => id.ToString(), sight);
                    session.Curves = known.CurvesJson.Length > 0 ? SkillCurves.Parse(known.CurvesJson) : null;
                    break;
            }

            if (r.BaseStream.Position != payload.Length)
                throw new InvalidDataException($"a {type} record holds {payload.Length - r.BaseStream.Position} bytes its type does not");
        }

        session.Drain(wait: true);
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

        public void Frame(in AiActorFrame frame) { }

        public void Observed(in AiEvent e) { }
    }
}
