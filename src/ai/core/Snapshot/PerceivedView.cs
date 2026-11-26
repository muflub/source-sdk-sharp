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

    /// <summary>
    /// The game holds the body still: keys move it nowhere. A player knows it from the HUD (TF2's
    /// pre-round countdown) and from walking into nothing; a bot that is held is not stuck.
    /// </summary>
    Frozen = 1 << 7,
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
/// own or another's: on fire, stunned, invulnerable, disguised, cloaked, taunting;
/// and (round 2's S4, recording format 12) soaked in Jarate, in milk, wet, marked
/// for death, in the air from a blast, scoped. A game without them leaves <see cref="None"/>.
/// The low byte is format 10's; the bits from <see cref="Jarated"/> up are format 12's
/// tail (<see cref="PerceivedView.LowConditions"/>).
/// </summary>
[Flags]
public enum ActorConditions : ushort
{
    None = 0,
    Burning = 1 << 0,
    Stunned = 1 << 1,
    Invulnerable = 1 << 2,
    Disguised = 1 << 3,
    Cloaked = 1 << 4,
    Taunting = 1 << 5,

    /// <summary>Soaked in Jarate: the drips every client draws on him.</summary>
    Jarated = 1 << 8,

    /// <summary>Soaked in Mad Milk: its drips.</summary>
    Milked = 1 << 9,

    /// <summary>Wet as a game's water rule reads it: in the water, or soaked in something a client draws.</summary>
    Wet = 1 << 10,

    /// <summary>The mark for death drawn over his head.</summary>
    MarkedForDeath = 1 << 11,

    /// <summary>In the air from a blast: off the ground, out of the water, after an explosion threw him.</summary>
    BlastAirborne = 1 << 12,

    /// <summary>Looking down a scope: a Sniper zoomed in, slowed, his rifle's dot on what he aims at.</summary>
    Zoomed = 1 << 13,
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
/// shows: its own slot, or a disguise's borrowed one (plan_bots_teams_tf2 C.3), so the field reads alike for everyone;
/// 0 only in a view no builder drew (a recording before format 10). <see cref="Facing"/>
/// is where it looks, as every client sees a player's head and aim (plan_bots_teams_tf2 P7: a backstab, a dodge across a
/// line of fire); not recorded, so a replay reads it as default (pitch and yaw 0).
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
    int NameSlot = 0,
    AiAngles Facing = default);

/// <summary>What a sighted thing that is not a player is (plan_bots_teams_tf2 C.3).</summary>
public enum ThingKind : byte
{
    None = 0,

    /// <summary>Something fired, in flight or lying where it landed: a rocket, a grenade, a sticky, an arrow, a flare.</summary>
    Projectile,

    /// <summary>Something built and standing: a sentry, a dispenser, a teleporter.</summary>
    Building,
}

/// <summary>
/// A thing that is not a player, as a client at the bot's eyes would draw it (plan_bots_teams_tf2
/// C.3, P6 step 3): what it is, where, how fast, whose team it is drawn in and whose it is. A
/// building's <see cref="Level"/> is its model's; its <see cref="Health"/> is what that client's
/// target ID shows, -1 when it shows none (stock shows a building's health only to its own team
/// and to a Spy). <see cref="Type"/> is the binding's own number for the kind of projectile or
/// building; <see cref="Id"/> the binding's id for the thing, the same while it lasts.
/// <see cref="Flags"/> are the binding's own bits for what that client draws on the thing (a
/// sapper on a building, a teleporter spun up: round 2's S7, S8), 0 for none.
/// </summary>
public readonly record struct ThingFacts(
    int Id,
    ThingKind Kind,
    int Type,
    AiVector Origin,
    AiVector Velocity,
    int Team,
    int OwnerSlot = 0,
    int Level = 0,
    int Health = -1,
    int Flags = 0);

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
    /// <summary>How many things a view holds unless told otherwise.</summary>
    public const int DefaultThingCapacity = 32;

    private readonly ActorFacts[] _sighted;
    private readonly ThingFacts[] _things;

    public PerceivedView(int capacity, int thingCapacity = DefaultThingCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(thingCapacity);
        _sighted = new ActorFacts[capacity];
        _things = new ThingFacts[thingCapacity];
    }

    public ActorId Bot { get; private set; }

    public Tick Tick { get; private set; }

    public SelfState Self { get; private set; }

    /// <summary>How many actors the bot holds sight of.</summary>
    public int SightedCount { get; private set; }

    /// <summary>The sighted actors, in the order the builder added them.</summary>
    public ReadOnlySpan<ActorFacts> Sighted => _sighted.AsSpan(0, SightedCount);

    /// <summary>How many things that are not players the bot holds sight of (plan_bots_teams_tf2 C.3).</summary>
    public int ThingCount { get; private set; }

    /// <summary>The sighted things, in the order the builder added them.</summary>
    public ReadOnlySpan<ThingFacts> Things => _things.AsSpan(0, ThingCount);

    /// <summary>How many things the view can hold.</summary>
    public int ThingCapacity => _things.Length;

    /// <summary>
    /// The map's objectives as every player's HUD shows them this tick (plan_bots_teams_tf2 C.6):
    /// the binding's published knowledge, the same object for every bot until it changes; null for a
    /// game that publishes none.
    /// </summary>
    public ObjectiveKnowledge? Objectives { get; private set; }

    /// <summary>Starts this view over for <paramref name="bot"/> at <paramref name="tick"/>.</summary>
    public void Reset(ActorId bot, Tick tick, in SelfState self)
    {
        Bot = bot;
        Tick = tick;
        Self = self;
        SightedCount = 0;
        ThingCount = 0;
        Objectives = null;
        GameHud = null;
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

    /// <summary>The conditions format 10's byte holds; the rest ride format 12's tail.</summary>
    public const int LowConditions = 0xFF;

    /// <summary>
    /// Format 12's tail: the conditions past <see cref="LowConditions"/>, the self's and each sighted actor's in
    /// order, over what format 10's tail set.
    /// </summary>
    internal void SetHighConditions(int self, ReadOnlySpan<int> sighted)
    {
        Self = Self with { Conditions = (ActorConditions)(((int)Self.Conditions & LowConditions) | (self << 8)) };
        for (int i = 0; i < SightedCount && i < sighted.Length; i++)
        {
            int low = (int)_sighted[i].Conditions & LowConditions;
            _sighted[i] = _sighted[i] with { Conditions = (ActorConditions)(low | (sighted[i] << 8)) };
        }
    }

    /// <summary>
    /// The self's class, conditions and metal, and each sighted actor's class, conditions, borrowed name and facing, read
    /// after the view (format 10's tail, <see cref="AiRecordFormat"/>); a missing one stays as it was.
    /// </summary>
    internal void SetDrawn(int selfClass, ActorConditions selfConditions, int metal, ReadOnlySpan<(int Class, ActorConditions Conditions, int NameSlot, AiAngles Facing)> sighted)
    {
        Self = Self with { Class = selfClass, Conditions = selfConditions, Metal = metal };
        for (int i = 0; i < SightedCount && i < sighted.Length; i++)
            _sighted[i] = _sighted[i] with { Class = sighted[i].Class, Conditions = sighted[i].Conditions, NameSlot = sighted[i].NameSlot, Facing = sighted[i].Facing };
    }

    /// <summary>Makes this view a copy of <paramref name="other"/> (a step's own copy, so the ring can move on, §2.7).</summary>
    public void CopyFrom(PerceivedView other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.SightedCount > _sighted.Length)
            throw new InvalidOperationException($"the view holds {_sighted.Length} sighted actors at most, the copied one {other.SightedCount}");
        if (other.ThingCount > _things.Length)
            throw new InvalidOperationException($"the view holds {_things.Length} things at most, the copied one {other.ThingCount}");
        Reset(other.Bot, other.Tick, other.Self);
        other.Sighted.CopyTo(_sighted);
        SightedCount = other.SightedCount;
        other.Things.CopyTo(_things);
        ThingCount = other.ThingCount;
        Objectives = other.Objectives;
        GameHud = other.GameHud;
    }

    /// <summary>Adds a thing the bot has sight of this tick; false, and nothing added, when the view is full.</summary>
    public bool AddThing(in ThingFacts facts)
    {
        if (facts.Kind == ThingKind.None)
            throw new ArgumentException("a sighted thing needs a kind", nameof(facts));
        if (ThingCount == _things.Length)
            return false;
        _things[ThingCount++] = facts;
        return true;
    }

    /// <summary>The objectives the HUD shows this tick (<see cref="Objectives"/>).</summary>
    public void SetObjectives(ObjectiveKnowledge? objectives) => Objectives = objectives;

    /// <summary>
    /// What the bot's own HUD shows past <see cref="Self"/> in a game that has more (plan_bots_teams_tf2 P7): its loadout,
    /// its meters, the teammates' health its HUD shows. The game's binding's own type, immutable, the same object while
    /// nothing on it changes; null for a game with none, and in a replay (it is not recorded, as the objectives are not).
    /// </summary>
    public GameHud? GameHud { get; private set; }

    /// <summary>The game's HUD this tick (<see cref="GameHud"/>).</summary>
    public void SetGameHud(GameHud? hud) => GameHud = hud;

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

/// <summary>
/// The base of a game's own HUD read (<see cref="PerceivedView.GameHud"/>, plan_bots_teams_tf2 P7): a game's binding
/// derives its type from it; the core reads nothing of it but the bytes a recording keeps (<see cref="Recorded"/>,
/// format 10), which a replay hands back as a <see cref="RecordedGameHud"/> for the game's decider to read with its own
/// codec. A derived type must be immutable: a view's copy keeps the same object.
/// </summary>
public abstract class GameHud
{
    /// <summary>The bytes a recording keeps of this HUD: all a decider reads of it, so a replay decides as the game did.</summary>
    public abstract byte[] Recorded();
}

/// <summary>A game's HUD as a recording kept it (format 10): its bytes, for the game's decider to read back.</summary>
public sealed class RecordedGameHud(byte[] bytes) : GameHud
{
    /// <summary>The bytes the game's <see cref="GameHud.Recorded"/> wrote.</summary>
    public byte[] Bytes { get; } = bytes;

    public override byte[] Recorded() => Bytes;
}
