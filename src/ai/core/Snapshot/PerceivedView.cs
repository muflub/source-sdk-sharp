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
/// shows of the frags it carries. <see cref="Team"/> is the team its own HUD shows it on: the binding's
/// number, 0 when the mode plays no teams (plan_bots_teams_tf2 B.2). <see cref="Class"/>, <see cref="Conditions"/>
/// and <see cref="Metal"/> are what a class-based game's HUD shows of the bot (plan_bots_teams_tf2 C.2): its
/// class (the binding's number, 0 for a game without classes), the conditions it is in, and its metal.
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
    int Grenades = 0,
    int Team = 0,
    int Class = 0,
    ActorConditions Conditions = ActorConditions.None,
    int Metal = 0)
{
    public bool Alive => (Flags & SelfFlags.Alive) != 0;
}

/// <summary>
/// The conditions a client renders on a player (plan_bots_teams_tf2 C.2), its
/// own or another's: on fire, stunned, invulnerable, disguised, cloaked, taunting.
/// A game without them leaves <see cref="None"/>.
/// </summary>
[Flags]
public enum ActorConditions : byte
{
    None = 0,
    Burning = 1 << 0,
    Stunned = 1 << 1,
    Invulnerable = 1 << 2,
    Disguised = 1 << 3,
    Cloaked = 1 << 4,
    Taunting = 1 << 5,
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
/// list). <see cref="Team"/> is the team a client at the bot's eyes would render it on (plan_bots_teams_tf2
/// B.2: in HL2DM the model's team, in TF2 a disguise's), 0 when the mode plays no teams. <see cref="Class"/>
/// is the class a client renders it as (a disguise's; 0 for a game without classes), <see cref="Conditions"/>
/// the conditions drawn on it, and <see cref="NameSlot"/> the slot of the player whose name its target ID
/// shows when that is not its own (a disguise's borrowed name, 0 for its own: plan_bots_teams_tf2 C.3).
/// </summary>
public readonly record struct ActorFacts(
    ActorId Id,
    AiVector Origin,
    AiVector Velocity,
    int WeaponSeen,
    ActorActivity Activity,
    float VisibleFraction,
    int Team = 0,
    int Class = 0,
    ActorConditions Conditions = ActorConditions.None,
    int NameSlot = 0);

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

    /// <summary>The self's team and each sighted actor's, read after the view (format 6's tail); a missing one stays 0.</summary>
    internal void SetTeams(int self, ReadOnlySpan<int> sighted)
    {
        Self = Self with { Team = self };
        for (int i = 0; i < SightedCount && i < sighted.Length; i++)
            _sighted[i] = _sighted[i] with { Team = sighted[i] };
    }

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
