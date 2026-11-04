namespace SourceSharp.Ai;

/// <summary>What the bot's own body and HUD report (plan_hl2mp_ai §4.3).</summary>
[Flags]
public enum SelfFlags : byte
{
    None = 0,
    Alive = 1 << 0,
    Grounded = 1 << 1,
    Ducking = 1 << 2,
    OnLadder = 1 << 3,
    InWater = 1 << 4,
    Reloading = 1 << 5,
    Observer = 1 << 6,
}

/// <summary>
/// The bot itself, as a client knows itself: position, view, health, armour,
/// and the ammo of the weapon in its hands. <see cref="ActiveWeapon"/> is the
/// binding's weapon-kind index. <see cref="AltAmmo"/> is what the HUD shows of the second trigger's
/// pool for that weapon (the SMG's grenades, the AR2's balls), <see cref="Grenades"/> what its weapon list
/// shows of the frags it carries.
/// </summary>
public readonly record struct SelfState(
    AiVector Origin,
    AiVector Velocity,
    AiAngles Eye,
    int Health,
    int Armour,
    int ActiveWeapon,
    int Clip,
    int Reserve,
    SelfFlags Flags,
    int AltAmmo = 0,
    int Grenades = 0)
{
    public bool Alive => (Flags & SelfFlags.Alive) != 0;
}

/// <summary>What a client renders of another actor's animation.</summary>
[Flags]
public enum ActorActivity : byte
{
    None = 0,
    Reloading = 1 << 0,
    Crouched = 1 << 1,
    Firing = 1 << 2,
}

/// <summary>
/// Another actor as a client at the bot's eyes would draw it. There is no
/// health, clip or reserve field, on purpose: a client is never sent them, so
/// the type cannot carry them (§4.3; ActorFactsShapeTests holds the member
/// list).
/// </summary>
public readonly record struct ActorFacts(
    ActorId Id,
    AiVector Origin,
    AiVector Velocity,
    int WeaponSeen,
    ActorActivity Activity,
    float VisibleFraction);

/// <summary>
/// One bot's perception at one tick: its own state, and the actors it holds
/// sight of. It is the only thing a brain's senses read (§4.3). The builder
/// adds only sighted actors, so an actor the bot cannot see is not in here to
/// be read: <see cref="TryActor"/> answers false for it.
///
/// <para>Sized once, refilled each tick by the snapshot's writer; a brain
/// reads it during the step keyed by its tick and keeps nothing of it.</para>
/// </summary>
public sealed class PerceivedView
{
    private readonly ActorFacts[] _sighted;

    public PerceivedView(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _sighted = new ActorFacts[capacity];
    }

    public ActorId Bot { get; private set; }

    public Tick Tick { get; private set; }

    public SelfState Self { get; private set; }

    /// <summary>How many actors the bot holds sight of.</summary>
    public int SightedCount { get; private set; }

    /// <summary>The sighted actors, in the order the builder added them.</summary>
    public ReadOnlySpan<ActorFacts> Sighted => _sighted.AsSpan(0, SightedCount);

    /// <summary>Starts this view over for <paramref name="bot"/> at <paramref name="tick"/>.</summary>
    public void Reset(ActorId bot, Tick tick, in SelfState self)
    {
        Bot = bot;
        Tick = tick;
        Self = self;
        SightedCount = 0;
    }

    /// <summary>The self's second-trigger pool and frags, read after the view (format 5's tail, <see cref="AiRecordFormat"/>).</summary>
    internal void SetAltAmmo(int altAmmo, int grenades) => Self = Self with { AltAmmo = altAmmo, Grenades = grenades };

    /// <summary>Makes this view a copy of <paramref name="other"/> (a step's own copy, so the ring can move on, §2.7).</summary>
    public void CopyFrom(PerceivedView other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.SightedCount > _sighted.Length)
            throw new InvalidOperationException($"the view holds {_sighted.Length} sighted actors at most, the copied one {other.SightedCount}");
        Reset(other.Bot, other.Tick, other.Self);
        other.Sighted.CopyTo(_sighted);
        SightedCount = other.SightedCount;
    }

    /// <summary>Adds an actor the bot has sight of this tick.</summary>
    public void AddSighted(in ActorFacts facts)
    {
        if (!facts.Id.IsValid)
            throw new ArgumentException("a sighted actor needs an id", nameof(facts));
        if (SightedCount == _sighted.Length)
            throw new InvalidOperationException($"the view holds {_sighted.Length} sighted actors at most");
        _sighted[SightedCount++] = facts;
    }

    /// <summary>
    /// The facts of <paramref name="id"/>, if the bot holds sight of it this
    /// tick; false, and nothing, otherwise.
    /// </summary>
    public bool TryActor(ActorId id, out ActorFacts facts)
    {
        foreach (ref readonly ActorFacts f in Sighted)
        {
            if (f.Id == id)
            {
                facts = f;
                return true;
            }
        }

        facts = default;
        return false;
    }
}
