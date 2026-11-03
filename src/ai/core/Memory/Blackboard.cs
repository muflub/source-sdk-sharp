namespace SourceSharp.Ai;

/// <summary>How a bot remembers (plan_hl2mp_ai §5.1, §10): how fast confidence fades, how fast habits are forgotten.</summary>
public readonly record struct MemoryParams(float MemoryHalfLifeSeconds, float HabitHalfLifeSeconds)
{
    /// <summary>From skills: Alertness lengthens the memory (2 s at 0 to 12 s at 10); habits halve every two minutes.</summary>
    public static MemoryParams For(SkillProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int i = profile.IndexOf("alertness");
        int alertness = i < 0 ? 0 : Math.Clamp(profile.Levels[i], 0, StandardSkills.Cap);
        return new MemoryParams(2f + alertness, HabitHalfLife);
    }

    public const float HabitHalfLife = 120f;
}

/// <summary>
/// One opponent as the bot remembers it (§5.1): where it was last seen and
/// heard and when, how sure the bot still is (decaying), its health as the bot
/// has seen it dealt, the weapon seen in its hands, the scoreboard's rank, and
/// the threat it posed.
/// </summary>
public sealed class KnownActor
{
    public ActorId Id { get; internal set; }

    public bool Known => Id.IsValid;

    public AiVector LastSeenPos { get; internal set; }

    public AiVector LastSeenVel { get; internal set; }

    public Tick LastSeenTick { get; internal set; }

    public bool EverSeen { get; internal set; }

    /// <summary>Held in sight now (between SightGained and SightLost).</summary>
    public bool InSight { get; internal set; }

    public AiVector LastHeardPos { get; internal set; }

    public Tick LastHeardTick { get; internal set; }

    public SoundKind LastHeardKind { get; internal set; }

    public bool EverHeard { get; internal set; }

    /// <summary>How sure the bot is of where it is, 0..1: 1 at a perception, halving every memory half-life.</summary>
    public float Confidence { get; internal set; }

    /// <summary>Health as far as the bot knows: full at its spawn, less the hits the bot dealt it (its hit marker said the health left).</summary>
    public int HealthEstimate { get; internal set; } = 100;

    public int WeaponSeen { get; internal set; } = -1;

    public CellId LastKnownCell { get; internal set; }

    public int TimesEngaged { get; internal set; }

    public float DamageTakenFrom { get; internal set; }

    public int Frags { get; internal set; }

    public int Deaths { get; internal set; }

    /// <summary>Its scoreboard rank, 1 the leader; 0 before the bot's HUD showed it.</summary>
    public int Rank { get; internal set; }

    public bool Dead { get; internal set; }

    public AmmoEstimate Ammo { get; } = new();

    /// <summary>Where it is now while unseen (§5.3.1), or null with no nav graph.</summary>
    public PositionPredictor? Where { get; internal set; }

    /// <summary>The last perception of any kind.</summary>
    public Tick LastPerceivedTick => LastSeenTick > LastHeardTick ? LastSeenTick : LastHeardTick;

    internal void Clear()
    {
        Id = ActorId.None;
        LastSeenPos = LastSeenVel = LastHeardPos = default;
        LastSeenTick = LastHeardTick = default;
        EverSeen = EverHeard = InSight = Dead = false;
        LastHeardKind = SoundKind.Unknown;
        Confidence = 0f;
        HealthEstimate = 100;
        WeaponSeen = -1;
        LastKnownCell = CellId.None;
        TimesEngaged = 0;
        DamageTakenFrom = 0f;
        Frags = Deaths = Rank = 0;
        Ammo.Reset();
        Where?.Clear();
    }
}

/// <summary>Something heard and not explained by a sighted actor (§5.1): ranked by loudness and recency.</summary>
public readonly record struct Investigation(AiVector Position, float Loudness, Tick Tick, SoundKind Kind, ActorId Source);

/// <summary>What the bot believes of one static item spawn (§5.1).</summary>
public struct ItemBelief
{
    /// <summary>Seen there (true), seen gone (false), or never looked at (null).</summary>
    public bool? Present;

    public Tick SeenTick;

    /// <summary>When the bot expects it back, after seeing it gone.</summary>
    public Tick ExpectedBack;

    /// <summary>The respawn interval: the game's rule, refined by reappearances seen.</summary>
    public float RespawnSeconds;
}

/// <summary>
/// One bot's memory (§5.1): known actors by slot (with their ammo
/// estimates), the investigations, the items, and the bot's own state.
/// Fixed size: everything is allocated when the bot is made. Habits live in
/// <see cref="OpponentHabits"/>, which outlive a life and a level (§5.2).
/// </summary>
public sealed class Blackboard
{
    /// <summary>The investigations kept (§5.1: 8).</summary>
    public const int InvestigationCapacity = 8;

    private readonly KnownActor[] _actors;
    private readonly Investigation[] _investigations = new Investigation[InvestigationCapacity];
    private ItemBelief[] _items = [];

    public Blackboard(int slots)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slots);
        _actors = new KnownActor[slots];
        for (int i = 0; i < slots; i++)
            _actors[i] = new KnownActor();
    }

    public int Slots => _actors.Length;

    public KnownActor Actor(int slot) => _actors[slot];

    /// <summary>The actor held for <paramref name="id"/>, made (or replaced, for another life) when <paramref name="make"/>.</summary>
    public KnownActor? Of(ActorId id, bool make)
    {
        if (!id.IsValid || (uint)id.Slot >= (uint)_actors.Length)
            return null;
        KnownActor a = _actors[id.Slot];
        if (a.Id == id)
            return a;
        if (!make)
            return null;
        a.Clear();
        a.Id = id;
        return a;
    }

    public int InvestigationCount { get; private set; }

    public ReadOnlySpan<Investigation> Investigations => _investigations.AsSpan(0, InvestigationCount);

    public ReadOnlySpan<ItemBelief> Items => _items;

    /// <summary>The bot's own last damage direction (from its HUD's indicator).</summary>
    public AiVector LastDamageDir { get; internal set; }

    public Tick LastDamageTick { get; internal set; }

    /// <summary>Sizes the items to the map's spawns, each at the game's respawn rule.</summary>
    public void KnowItems(MapKnowledge map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _items = new ItemBelief[map.Items.Length];
        for (int i = 0; i < _items.Length; i++)
            _items[i] = new ItemBelief { RespawnSeconds = map.Items[i].RespawnSeconds };
    }

    /// <summary>The bot looked at an item spawn: present or gone; a gone item is expected back after its interval; one back sooner refines it.</summary>
    public void SeeItem(int index, bool present, Tick tick, float tickSeconds)
    {
        if ((uint)index >= (uint)_items.Length)
            return;
        ref ItemBelief b = ref _items[index];
        if (present && b.Present == false && tick > b.SeenTick)
        {
            // It came back: the interval is at most what this shows, never less than half the rule.
            float back = (tick - b.SeenTick) * tickSeconds;
            if (back < b.RespawnSeconds)
                b.RespawnSeconds = Math.Max(b.RespawnSeconds * 0.5f, back);
        }

        if (!present && b.Present != false)
            b.ExpectedBack = tick + VisionParams.Ticks(b.RespawnSeconds, tickSeconds);
        if (b.Present != present || present)
            b.SeenTick = tick;
        b.Present = present;
    }

    /// <summary>
    /// Adds a heard sound to investigate; the least worth investigating goes
    /// when full (§5.1: loudness × recency).
    /// </summary>
    public void Investigate(in Investigation item, Tick now, float tickSeconds)
    {
        if (InvestigationCount < _investigations.Length)
        {
            _investigations[InvestigationCount++] = item;
            return;
        }

        int worst = 0;
        float worstScore = float.MaxValue;
        for (int i = 0; i < InvestigationCount; i++)
        {
            float s = Worth(_investigations[i], now, tickSeconds);
            if (s < worstScore)
                (worst, worstScore) = (i, s);
        }

        if (Worth(item, now, tickSeconds) > worstScore)
            _investigations[worst] = item;
    }

    /// <summary>Loudness × recency (halving every 5 s).</summary>
    public static float Worth(in Investigation i, Tick now, float tickSeconds)
        => i.Loudness * MathF.Pow(2f, -((now - i.Tick) * tickSeconds) / 5f);

    /// <summary>The investigation most worth going to, if any.</summary>
    public bool TryBestInvestigation(Tick now, float tickSeconds, out Investigation best)
    {
        best = default;
        float top = -1f;
        foreach (Investigation i in Investigations)
        {
            float w = Worth(i, now, tickSeconds);
            if (w > top)
                (top, best) = (w, i);
        }

        return top >= 0f;
    }

    /// <summary>Drops investigations near <paramref name="at"/> (the bot went and looked) or explained by a sighted <paramref name="source"/>.</summary>
    public void Resolve(AiVector at, float radius, ActorId source)
    {
        int n = 0;
        for (int i = 0; i < InvestigationCount; i++)
        {
            Investigation v = _investigations[i];
            bool done = v.Position.DistanceTo(at) <= radius || (source.IsValid && v.Source == source);
            if (!done)
                _investigations[n++] = v;
        }

        InvestigationCount = n;
    }

    /// <summary>Confidence fades (§5.1): <c>c ← c · 2^(−Δt / halfLife)</c> since its last perception.</summary>
    public static float Decayed(float confidence, float seconds, float halfLife)
        => halfLife <= 0f ? 0f : confidence * MathF.Pow(2f, -seconds / halfLife);

    /// <summary>Every known actor's confidence at <paramref name="now"/>, from its last perception.</summary>
    public void Decay(Tick now, float tickSeconds, in MemoryParams p)
    {
        foreach (KnownActor a in _actors)
        {
            if (!a.Known || a.InSight)
                continue;
            // Known only from the scoreboard or the kill feed: no place to be sure of.
            a.Confidence = a.EverSeen || a.EverHeard ? Decayed(1f, (now - a.LastPerceivedTick) * tickSeconds, p.MemoryHalfLifeSeconds) : 0f;
        }
    }

    /// <summary>The bot died (§5.2, the life tier): its investigations and every ammo estimate go; who it knew stays.</summary>
    public void ForgetLife()
    {
        InvestigationCount = 0;
        foreach (KnownActor a in _actors)
        {
            a.Ammo.Reset();
            a.InSight = false;
        }
    }

    /// <summary>A new map (§5.2, the round tier): everything but the habits.</summary>
    public void ForgetMap()
    {
        ForgetLife();
        foreach (KnownActor a in _actors)
            a.Clear();
        _items = [];
    }
}
