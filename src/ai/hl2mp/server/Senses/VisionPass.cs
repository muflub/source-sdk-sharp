using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>Where another actor was this tick, as the vision pass read it once for every bot.</summary>
public readonly record struct ActorRead(EntityHandle Player, AiVector Origin, AiVector Eye, AiVector Velocity);

/// <summary>
/// The vision pass (plan_hl2mp_ai §4.1), on the main thread: for each bot and
/// each other living player, range, then cone, then the engine's PVS, then
/// line traces from the bot's eye to the target's head, chest, pelvis and
/// shoulders, served round-robin within <see cref="TracesPerTick"/>, then
/// recognition. Lighting is skipped (ruling 2026-11-01); what blocks a
/// player's view blocks the bot's: world, doors, props, BLOCKLOS brushes and
/// other players' bodies (<see cref="SightMask"/>). One of the two types in
/// the binding that read another actor's position (§4.4; the census holds it).
/// </summary>
[SideService(Side.Server)]
public sealed class VisionPass(IEnginePlayers players, IEngineEntities entities, ITraceQueries traces_)
{
    // bspflags.h: what stock NextBot's sight line stops at
    // (MASK_BLOCKLOS_AND_NPCS | CONTENTS_IGNORE_NODRAW_OPAQUE,
    // NextBotVisionInterface.cpp:704). Windows and grates are not in it.
    private const uint ContentsSolid = 0x1, ContentsBlockLos = 0x40, ContentsIgnoreNodrawOpaque = 0x2000,
                       ContentsMoveable = 0x4000, ContentsMonster = 0x2000000;

    /// <summary>The sight line's mask: <c>MASK_BLOCKLOS_AND_NPCS | CONTENTS_IGNORE_NODRAW_OPAQUE</c>.</summary>
    public const uint SightMask = ContentsSolid | ContentsMoveable | ContentsBlockLos | ContentsMonster | ContentsIgnoreNodrawOpaque;

    /// <summary>A player's sample points: head, chest, pelvis, two shoulders.</summary>
    public const int PlayerSamples = 5;

    /// <summary>Half a player's width, and the shoulders' offset from the chest.</summary>
    public const float HalfWidth = 16f, Shoulder = 12f;

    /// <summary>Player slots a pass covers (the engine's 1..64, slot 0 unused).</summary>
    public const int Slots = 65;

    private readonly Dictionary<int, BotSight> _bots = new();
    private readonly ActorRead?[] _reads = new ActorRead?[Slots];
    private readonly ActorId[] _actors = new ActorId[Slots];
    private readonly TraceBudget _budget = new();
    private readonly List<long> _keys = new();
    private readonly List<int> _costs = new();
    private readonly List<(BotSight Bot, int Target)> _pending = new();
    private bool[] _served = new bool[64];

    /// <summary>The trace budget a tick (<c>ai_vision_traces_per_tick</c>, default 32: §4.1, §12.3).</summary>
    public int TracesPerTick { get; set; } = 32;

    /// <summary>Traces the last pass spent.</summary>
    public int TracesLastTick { get; private set; }

    /// <summary>Line traces in all.</summary>
    public long Traces { get; private set; }

    /// <summary>What the bot in <paramref name="botSlot"/> sees, or null.</summary>
    public SightTracker? SightOf(int botSlot) => _bots.TryGetValue(botSlot, out BotSight? b) ? b.Tracker : null;

    /// <summary>Where the actor in <paramref name="slot"/> was this tick, if living.</summary>
    public ActorRead? ReadOf(int slot) => (uint)slot < Slots ? _reads[slot] : null;

    /// <summary>The actor living in <paramref name="slot"/> this tick, or none.</summary>
    public ActorId ActorIn(int slot) => (uint)slot < Slots ? _actors[slot] : ActorId.None;

    /// <summary>Starts serving <paramref name="bot"/>, forgetting any earlier bot in its slot.</summary>
    public void Add(ActorId bot) => _bots[bot.Slot] = new BotSight(bot, new SightTracker(Slots));

    public void Remove(int slot) => _bots.Remove(slot);

    /// <summary>
    /// One tick: reads every living actor once, runs the stages for every
    /// served bot against every other actor, and appends the sight events
    /// (<c>SightGained</c>, <c>SightLost</c>) to <paramref name="events"/>.
    /// </summary>
    public void Run(Tick tick, float tickSeconds, ReadOnlySpan<SensedBot> bots, ReadOnlySpan<SensedActor> actors, List<AiEvent> events)
    {
        Array.Clear(_reads);
        Array.Clear(_actors);
        foreach (SensedActor a in actors)
        {
            Vector o = entities.GetOrigin(a.Player), e = entities.GetEyePosition(a.Player), v = entities.GetVelocity(a.Player);
            _reads[a.Id.Slot] = new ActorRead(a.Player, new AiVector(o.X, o.Y, o.Z), new AiVector(e.X, e.Y, e.Z), new AiVector(v.X, v.Y, v.Z));
            _actors[a.Id.Slot] = a.Id;
        }

        _keys.Clear();
        _costs.Clear();
        _pending.Clear();
        foreach (SensedBot b in bots)
        {
            if (!_bots.TryGetValue(b.Id.Slot, out BotSight? sight) || sight.Bot != b.Id)
                continue;

            sight.Vision = b.Vision;
            if (_reads[b.Id.Slot] is not { } me)
            {
                // A dead bot sees nothing: everything it held is lost.
                for (int t = 1; t < Slots; t++)
                    Lose(sight, t, tick, events);
                continue;
            }

            Vector ang = entities.GetEyeAngles(b.Player);
            AiVector forward = VisionMath.Forward(new AiAngles(ang.X, ang.Y, ang.Z));
            for (int t = 1; t < Slots; t++)
            {
                if (t == b.Id.Slot || _reads[t] is not { } them)
                {
                    Lose(sight, t, tick, events);
                    continue;
                }

                if (sight.Tracker.TargetOf(t) != _actors[t])
                    Lose(sight, t, tick, events);   // another life in the slot

                AiVector centre = Centre(them);
                SightStage stage = Stage(me.Eye, forward, centre, them, b);
                sight.Stage[t] = stage;
                if (stage == SightStage.Unserved)
                {
                    _keys.Add(((long)b.Id.Slot << 8) | (uint)t);
                    _costs.Add(PlayerSamples);
                    _pending.Add((sight, t));
                }
                else
                    sight.Answered[t] = false;   // a pair that failed range, cone or PVS starts its traces over
            }
        }

        if (_served.Length < _keys.Count)
            _served = new bool[Math.Max(_keys.Count, _served.Length * 2)];
        TracesLastTick = _budget.Select(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_keys),
                                        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_costs), TracesPerTick, _served);
        Traces += TracesLastTick;
        for (int i = 0; i < _pending.Count; i++)
        {
            (BotSight sight, int t) = _pending[i];
            if (_served[i])
            {
                sight.Fraction[t] = Trace(_reads[sight.Bot.Slot]!.Value, _reads[t]!.Value);
                sight.Answered[t] = true;
            }

            // Not served this tick: the pair keeps its last answer (§4.1).
            if (sight.Answered[t])
                sight.Stage[t] = sight.Fraction[t] > 0f ? SightStage.Clear : SightStage.Trace;
        }

        foreach (SensedBot b in bots)
        {
            if (!_bots.TryGetValue(b.Id.Slot, out BotSight? sight) || sight.Bot != b.Id || _reads[b.Id.Slot] is null)
                continue;
            for (int t = 1; t < Slots; t++)
            {
                if (t == b.Id.Slot || _reads[t] is not { } them)
                    continue;
                SightChange c = sight.Tracker.Observe(_actors[t], sight.Stage[t], sight.Stage[t] == SightStage.Clear ? sight.Fraction[t] : 0f,
                                                      sight.Vision, tickSeconds);
                if (c == SightChange.Gained)
                    events.Add(AiEvent.SightGained(b.Id, tick, _actors[t], them.Origin, them.Velocity, sight.Fraction[t]));
                else if (c == SightChange.Lost)
                    events.Add(AiEvent.SightLost(b.Id, tick, _actors[t], them.Origin, them.Velocity));
                if (c != SightChange.None)
                    sight.Last[t] = them;
                else if (sight.Tracker.Sees(t))
                    sight.Last[t] = them;
            }
        }
    }

    private void Lose(BotSight sight, int slot, Tick tick, List<AiEvent> events)
    {
        ActorId held = sight.Tracker.TargetOf(slot);
        sight.Stage[slot] = SightStage.None;
        sight.Answered[slot] = false;
        if (sight.Tracker.Forget(slot) == SightChange.Lost)
        {
            ActorRead last = sight.Last[slot];
            events.Add(AiEvent.SightLost(sight.Bot, tick, held, last.Origin, last.Velocity));
        }
    }

    private SightStage Stage(AiVector eye, AiVector forward, AiVector centre, ActorRead them, SensedBot b)
    {
        if (eye.DistanceTo(centre) > b.Vision.SightRange)
            return SightStage.Range;
        if (!VisionMath.InCone(eye, forward, centre, b.Vision.FovDeg))
            return SightStage.Cone;
        float top = them.Eye.Z - them.Origin.Z + 8f;
        var mins = new Vector(them.Origin.X - HalfWidth, them.Origin.Y - HalfWidth, them.Origin.Z);
        var maxs = new Vector(them.Origin.X + HalfWidth, them.Origin.Y + HalfWidth, them.Origin.Z + top);
        if (!players.BoxInPvs(b.Player, mins, maxs))
            return SightStage.Pvs;
        return SightStage.Unserved;
    }

    /// <summary>The middle of a player's body.</summary>
    public static AiVector Centre(ActorRead a) => a.Origin + (a.Eye - a.Origin) * 0.5f;

    /// <summary>The five points sampled on a player seen from <paramref name="eye"/>: head, chest, pelvis, left and right shoulder.</summary>
    public static void Samples(AiVector eye, ActorRead a, Span<AiVector> into)
    {
        AiVector up = a.Eye - a.Origin;
        AiVector chest = a.Origin + up * 0.75f;
        var dir = new AiVector(a.Origin.X - eye.X, a.Origin.Y - eye.Y, 0f);
        float len = dir.Length2D;
        AiVector right = len > 0f ? new AiVector(dir.Y / len, -dir.X / len, 0f) : new AiVector(0f, 1f, 0f);
        into[0] = a.Eye;
        into[1] = chest;
        into[2] = a.Origin + up * 0.5f;
        into[3] = chest + right * Shoulder;
        into[4] = chest - right * Shoulder;
    }

    /// <summary>The share of the target's sample points whose line from the bot's eye is clear (the target itself does not block).</summary>
    private float Trace(ActorRead me, ActorRead them)
    {
        Span<AiVector> points = stackalloc AiVector[PlayerSamples];
        Samples(me.Eye, them, points);
        var from = new Vector(me.Eye.X, me.Eye.Y, me.Eye.Z);
        int clear = 0;
        foreach (AiVector p in points)
        {
            TraceAnswer t = traces_.Line(from, new Vector(p.X, p.Y, p.Z), SightMask, me.Player);
            if (t.Fraction >= 1f || t.Entity == them.Player)
                clear++;
        }

        return clear / (float)PlayerSamples;
    }

    private sealed class BotSight(ActorId bot, SightTracker tracker)
    {
        public ActorId Bot { get; } = bot;

        public SightTracker Tracker { get; } = tracker;

        public VisionParams Vision { get; set; }

        public SightStage[] Stage { get; } = new SightStage[Slots];

        public float[] Fraction { get; } = new float[Slots];

        public bool[] Answered { get; } = new bool[Slots];

        public ActorRead[] Last { get; } = new ActorRead[Slots];
    }

    /// <summary>
    /// The direction the HUD's damage indicator shows the bot in
    /// <paramref name="botSlot"/> for a hit from the player in
    /// <paramref name="fromSlot"/>: a unit vector from the bot toward it at the
    /// last pass, or zero when either is not living.
    /// </summary>
    public AiVector DirectionFrom(int botSlot, int fromSlot)
        => (uint)botSlot < Slots && (uint)fromSlot < Slots && _reads[botSlot] is { } me && _reads[fromSlot] is { } them
            ? (them.Origin - me.Origin).Normalised()
            : AiVector.Zero;

    /// <summary>How far a bot looks for an item spawn, and how many it checks and traces a tick.</summary>
    public const float ItemRange = 1500f;

    public const int ItemChecksPerTick = 24, ItemTracesPerTick = 4;

    /// <summary>How near a spawn an item must lie to count as there.</summary>
    public const float ItemAt = 32f;

    private int _itemCursor;
    private readonly Dictionary<(int Bot, int Item), (bool Present, int Tick)> _itemReported = new();
    private readonly Dictionary<string, List<AiVector>> _itemPlaces = new(StringComparer.Ordinal);

    /// <summary>
    /// The item spawns the bots look at this tick (§5.1, items observed): a
    /// slice of the (bot, spawn) pairs, each through range, cone, PVS and one
    /// line trace; one that passes is seen present or gone, and an
    /// <c>ItemSeen</c> is pushed when that changes or two seconds after the last.
    /// </summary>
    public void RunItems(Tick tick, ReadOnlySpan<SensedBot> bots, IReadOnlyList<ItemSpawn> items, List<AiEvent> events)
    {
        if (bots.IsEmpty || items.Count == 0)
            return;
        _itemPlaces.Clear();
        int pairs = bots.Length * items.Count, traces = 0;
        for (int k = 0; k < Math.Min(ItemChecksPerTick, pairs) && traces < ItemTracesPerTick; k++)
        {
            int at = _itemCursor++ % pairs;
            SensedBot b = bots[at / items.Count];
            ItemSpawn item = items[at % items.Count];
            if (_reads[b.Id.Slot] is not { } me)
                continue;
            AiVector spot = item.Position + new AiVector(0f, 0f, 8f);
            if (me.Eye.DistanceTo(spot) > ItemRange)
                continue;
            Vector ang = entities.GetEyeAngles(b.Player);
            if (!VisionMath.InCone(me.Eye, VisionMath.Forward(new AiAngles(ang.X, ang.Y, ang.Z)), spot, b.Vision.FovDeg))
                continue;
            if (!players.BoxInPvs(b.Player, new Vector(spot.X - 8f, spot.Y - 8f, spot.Z - 8f), new Vector(spot.X + 8f, spot.Y + 8f, spot.Z + 8f)))
                continue;
            traces++;
            Traces++;
            TraceAnswer t = traces_.Line(new Vector(me.Eye.X, me.Eye.Y, me.Eye.Z), new Vector(spot.X, spot.Y, spot.Z), SightMask, b.Player);
            if (t.Fraction < 0.98f && (t.Entity == EntityHandle.None || entities.GetIndex(t.Entity) == 0))
                continue;   // the world is in the way

            bool present = Places(item.ClassName).Any(p => p.DistanceTo(item.Position) <= ItemAt);
            var key = (b.Id.Slot, item.Index);
            if (_itemReported.TryGetValue(key, out var last) && last.Present == present && tick.Value - last.Tick < 133)
                continue;
            _itemReported[key] = (present, tick.Value);
            events.Add(AiEvent.ItemSeen(b.Id, tick, item.Index, present));
        }
    }

    private List<AiVector> Places(string className)
    {
        if (!_itemPlaces.TryGetValue(className, out List<AiVector>? places))
        {
            places = new List<AiVector>();
            foreach (EntityHandle e in entities.FindAllByClassname(className))
            {
                Vector o = entities.GetOrigin(e);
                places.Add(new AiVector(o.X, o.Y, o.Z));
            }

            _itemPlaces[className] = places;
        }

        return places;
    }

    /// <summary>Forgets what each bot was last told of the items (a new level).</summary>
    public void ForgetItems() => _itemReported.Clear();

    /// <summary>The stage the bot in <paramref name="botSlot"/> reached for <paramref name="target"/> this tick (the inspector's colours, §7.1).</summary>
    public SightStage StageOf(int botSlot, int target)
        => _bots.TryGetValue(botSlot, out BotSight? b) && (uint)target < Slots ? b.Stage[target] : SightStage.None;
}
