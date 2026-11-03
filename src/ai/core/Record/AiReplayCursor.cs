namespace SourceSharp.Ai;

/// <summary>
/// A recording replayed to any tick on request (plan_hl2mp_ai §7.1's
/// scrubber): the brains are rebuilt as <see cref="AiReplayer"/> builds them
/// and stepped inline up to the last snapshot at or before the tick asked
/// for, so every brain can be read as it was then. Going back replays from
/// the start (the replay is deterministic, §2.6); going on carries on.
/// Holds each bot's recorded and replayed intents by decide tick, for the
/// live-vs-replay diff. Not thread-safe: one caller at a time.
/// </summary>
public sealed class AiReplayCursor
{
    private readonly List<(AiRecordType Type, byte[] Payload)> _records = new();
    private readonly Dictionary<(int Slot, int Tick), IntentCommand> _recorded = new();
    private readonly Dictionary<(int Slot, int Tick), IntentCommand> _replayed = new();
    private readonly Dictionary<int, byte[]> _levels = new();
    private readonly Func<AiRecordedWorld, (NavGraph? Graph, ICellSight? Sight)>? _nav;
    private readonly Func<BrainSetup, IDecider?>? _deciders;
    private AiSession _session = null!;
    private int _next;

    /// <summary>
    /// Reads <paramref name="recording"/> whole. With <paramref name="nav"/>
    /// and a World record, the brains remember and decide as the game's did
    /// (<paramref name="deciders"/>); without, they hold.
    /// </summary>
    public AiReplayCursor(byte[] recording, Func<AiRecordedWorld, (NavGraph? Graph, ICellSight? Sight)>? nav = null,
                          Func<BrainSetup, IDecider?>? deciders = null)
    {
        ArgumentNullException.ThrowIfNull(recording);
        _nav = nav;
        _deciders = deciders;
        var s = new MemoryStream(recording, writable: false);
        AiRecordFormat.ReadFileHeader(s);
        if (!AiRecordFormat.TryReadRecord(s, out AiRecordType first, out byte[] header) || first != AiRecordType.Header)
            throw new InvalidDataException("an .airec file starts with its Header record");
        using (var r = new BinaryReader(new MemoryStream(header)))
            Header = AiRecordFormat.ReadHeader(r);

        var ticks = new List<Tick>();
        while (AiRecordFormat.TryReadRecord(s, out AiRecordType type, out byte[] payload))
        {
            _records.Add((type, payload));
            using var r = new BinaryReader(new MemoryStream(payload));
            switch (type)
            {
                case AiRecordType.Snapshot:
                    ticks.Add(new Tick(r.ReadInt32()));
                    break;
                case AiRecordType.Intent:
                    IntentCommand i = AiRecordFormat.ReadIntent(r);
                    _recorded[(i.Bot.Slot, i.FromTick.Value)] = i;
                    break;
                case AiRecordType.BotAdded:
                    ActorId bot = AiRecordFormat.ReadActor(r);
                    r.ReadUInt64();
                    _levels[bot.Slot] = r.ReadBytes(r.ReadUInt16());
                    break;
                case AiRecordType.World:
                    World = AiRecordFormat.ReadWorld(r);
                    break;
            }
        }

        Ticks = ticks;
        Restart();
    }

    public AiRecordingHeader Header { get; }

    /// <summary>The recorded world, or null for a format 1 recording.</summary>
    public AiRecordedWorld? World { get; }

    /// <summary>Every snapshot's tick, in order.</summary>
    public IReadOnlyList<Tick> Ticks { get; }

    /// <summary>The skill levels each bot slot was added with (the last bot in it).</summary>
    public IReadOnlyDictionary<int, byte[]> Levels => _levels;

    /// <summary>The tick the brains stand at: the last snapshot published, or null before the first.</summary>
    public Tick? At { get; private set; }

    /// <summary>The replay's session, inline: its brains may be read between seeks.</summary>
    public AiSession Session => _session;

    /// <summary>Steps the brains to the last snapshot at or before <paramref name="tick"/>.</summary>
    public void SeekTo(Tick tick)
    {
        if (At is { } at && tick < at)
            Restart();
        while (_next < _records.Count)
        {
            (AiRecordType type, byte[] payload) = _records[_next];
            if (type == AiRecordType.Snapshot && BitConverter.ToInt32(payload, 0) > tick.Value)
                break;
            Apply(type, payload);
            _next++;
        }
    }

    /// <summary>The intent the recording holds for the bot in <paramref name="slot"/> from <paramref name="tick"/>.</summary>
    public bool TryRecorded(int slot, Tick tick, out IntentCommand intent) => _recorded.TryGetValue((slot, tick.Value), out intent);

    /// <summary>The intent the replay made for that bot from that tick, once stepped past it.</summary>
    public bool TryReplayed(int slot, Tick tick, out IntentCommand intent) => _replayed.TryGetValue((slot, tick.Value), out intent);

    /// <summary>The newest decide tick of the bot in <paramref name="slot"/> at or before <paramref name="tick"/> that the replay has an intent for.</summary>
    public Tick? LastDecide(int slot, Tick tick)
    {
        Tick? best = null;
        foreach ((int s, int t) in _replayed.Keys)
        {
            if (s == slot && t <= tick.Value && (best is null || t > best.Value.Value))
                best = new Tick(t);
        }

        return best;
    }

    private void Restart()
    {
        _replayed.Clear();
        _session = new AiSession(new InlineTransport(), new Capture(_replayed), Header.Slots, Header.SightedCapacity);
        _next = 0;
        At = null;
    }

    private void Apply(AiRecordType type, byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload, writable: false));
        switch (type)
        {
            case AiRecordType.World:
                AiRecordedWorld known = AiRecordFormat.ReadWorld(r);
                if (_nav is null)
                    break;
                (NavGraph? graph, ICellSight? sight) = _nav(known);
                if (known.NavGraphHash != 0 && graph?.Hash != known.NavGraphHash)
                    throw new InvalidDataException($"the recording's nav graph is {known.NavGraphHash:x16}, the one at hand {graph?.Hash ?? 0:x16}");
                _session.World = new MemoryWorld(known.Map, graph, known.TickSeconds, id => id.ToString(), sight);
                _session.Curves = known.CurvesJson.Length > 0 ? SkillCurves.Parse(known.CurvesJson) : null;
                _session.Deciders = _deciders;
                break;
            case AiRecordType.BotAdded:
                ActorId bot = AiRecordFormat.ReadActor(r);
                ulong seed = r.ReadUInt64();
                _session.AddBot(bot, seed, r.ReadBytes(r.ReadUInt16()));
                break;
            case AiRecordType.BotRemoved:
                _session.RemoveBot(AiRecordFormat.ReadActor(r));
                break;
            case AiRecordType.Event:
                _session.Push(AiRecordFormat.ReadEvent(r));
                break;
            case AiRecordType.Snapshot:
                AiSnapshot snap = AiRecordFormat.ReadSnapshot(r, _session.Snapshots);
                _session.Publish(snap);
                At = snap.Tick;
                break;
        }
    }

    private sealed class Capture(Dictionary<(int, int), IntentCommand> into) : IAiRecordSink
    {
        public void Intent(in IntentCommand intent) => into[(intent.Bot.Slot, intent.FromTick.Value)] = intent;

        public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels) { }

        public void BotRemoved(ActorId bot) { }

        public void DecisionTree(IReadOnlyList<string> names, IReadOnlyList<int> branches) { }

        public void Event(in AiEvent e) { }

        public void Snapshot(AiSnapshot snapshot) { }

        public void UserCmd(in AiUserCmd cmd) { }

        public void Frame(in AiActorFrame frame) { }

        public void Observed(in AiEvent e) { }
    }
}
