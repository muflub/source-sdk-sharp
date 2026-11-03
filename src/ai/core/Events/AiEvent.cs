namespace SourceSharp.Ai;

/// <summary>What happened to a bot (plan_hl2mp_ai §2.2). One family per row of its table.</summary>
public enum AiEventKind : byte
{
    None = 0,

    // sound
    SoundHeard = 1,

    // sight
    SightGained = 10,
    SightLost = 11,
    SeenFire = 12,
    SeenReload = 13,

    // hurt
    Damaged = 20,
    Killed = 21,
    AttackerSeenKill = 22,

    /// <summary>The bot's own hit hurt another player (its hit marker, §5.1's health estimate).</summary>
    DealtDamage = 23,

    // self
    Spawned = 30,
    Removed = 31,
    Respawnable = 32,
    Landed = 33,
    Stuck = 34,
    ClipEmpty = 35,
    WeaponChanged = 36,
    PickedUp = 37,
    CommandRejected = 38,

    // gameplay (the HUD)
    ScoreboardRow = 40,
    MatchState = 41,

    /// <summary>A death notice on the HUD's kill feed: who died, and who killed them.</summary>
    KillFeed = 42,

    /// <summary>The bot looked at a static item spawn: Code the spawn's index, Code2 1 if the item was there.</summary>
    ItemSeen = 43,

    /// <summary>The weapons the bot holds, as its HUD's weapon selection shows them: a mask of catalog kinds in Code.</summary>
    Holdings = 44,

    // recorded only, as Observed records (§3.4.4's hit rate): no brain is told
    /// <summary>The actor fired: Code the weapon's kind, Amount the rounds the fire took. A trigger pull, however many pellets it throws.</summary>
    ShotFired = 50,

    /// <summary>The actor's fire hurt Other: Amount the damage, Code the weapon it last fired (-1 none), Code2 the range in units (-1 unknown).</summary>
    HitDealt = 51,
}

/// <summary>What a heard sound was. The binding classifies by sound name.</summary>
public enum SoundKind : byte
{
    Unknown = 0,
    Gunfire,
    Reload,
    Explosion,
    Footstep,
    Pain,
    Death,
    Pickup,
    Door,
    Physics,

    /// <summary>A weapon's dry click: the shooter pulled the trigger on an empty clip.</summary>
    DryFire,
}

/// <summary>Which part of a reload a bot saw (the animation a client renders).</summary>
public enum ReloadPhase : byte
{
    Start = 0,
    Loop,
    End,
}

/// <summary>Why the applier refused an intent (§2.4).</summary>
public enum RejectReason : byte
{
    None = 0,
    StaleActor,
    OldSequence,
    Expired,
    Dead,
    Observer,
}

/// <summary>
/// One input event. A flat value, not a hierarchy, so a stream of them is a
/// plain array (no allocation per event) and a recording writes each one the
/// same way. The factories below name what each field means per kind;
/// fields a kind does not use stay zero.
/// <list type="bullet">
/// <item><see cref="Other"/>: the other actor (source, target, attacker, victim, shooter).</item>
/// <item><see cref="Position"/>, <see cref="Velocity"/>: where and how fast, when the kind has them.</item>
/// <item><see cref="Amount"/>: loudness, visible fraction, damage.</item>
/// <item><see cref="Code"/>: the kind's enum (sound kind, reload phase, reject reason) or a count.</item>
/// <item><see cref="Code2"/>: a weapon kind (the binding's table index), an item, a sequence, a second count.</item>
/// </list>
/// </summary>
public readonly record struct AiEvent(
    AiEventKind Kind,
    ActorId Bot,
    Tick Tick,
    ActorId Other,
    AiVector Position,
    AiVector Velocity,
    float Amount,
    int Code,
    int Code2)
{
    public static AiEvent SoundHeard(ActorId bot, Tick tick, SoundKind kind, AiVector pos, float loudness, ActorId source, int weaponKind)
        => new(AiEventKind.SoundHeard, bot, tick, source, pos, default, loudness, (int)kind, weaponKind);

    public static AiEvent SightGained(ActorId bot, Tick tick, ActorId target, AiVector pos, AiVector vel, float visibleFraction)
        => new(AiEventKind.SightGained, bot, tick, target, pos, vel, visibleFraction, 0, 0);

    public static AiEvent SightLost(ActorId bot, Tick tick, ActorId target, AiVector lastPos, AiVector lastVel)
        => new(AiEventKind.SightLost, bot, tick, target, lastPos, lastVel, 0f, 0, 0);

    /// <summary>The bot saw <paramref name="shooter"/> fire <paramref name="rounds"/> rounds (the muzzle flash a client draws, §4.5).</summary>
    public static AiEvent SeenFire(ActorId bot, Tick tick, ActorId shooter, int weaponKind, int rounds = 1)
        => new(AiEventKind.SeenFire, bot, tick, shooter, default, default, 0f, rounds, weaponKind);

    /// <summary>The bot saw <paramref name="actor"/>'s reload gesture (§4.5); <paramref name="rounds"/> is the shells a loop put in.</summary>
    public static AiEvent SeenReload(ActorId bot, Tick tick, ActorId actor, ReloadPhase phase, int weaponKind = -1, int rounds = 0)
        => new(AiEventKind.SeenReload, bot, tick, actor, default, default, rounds, (int)phase, weaponKind);

    public static AiEvent Damaged(ActorId bot, Tick tick, ActorId attacker, float amount, AiVector dirFromBot, int damageType)
        => new(AiEventKind.Damaged, bot, tick, attacker, dirFromBot, default, amount, damageType, 0);

    /// <summary>The bot's own hit took <paramref name="amount"/> health from <paramref name="victim"/>.</summary>
    public static AiEvent DealtDamage(ActorId bot, Tick tick, ActorId victim, float amount, int healthLeft)
        => new(AiEventKind.DealtDamage, bot, tick, victim, default, default, amount, healthLeft, 0);

    /// <summary>The kill feed showed <paramref name="victim"/> killed by <paramref name="killer"/> (none for the world or a suicide); the killer rides in Code and Code2.</summary>
    public static AiEvent KillFeed(ActorId bot, Tick tick, ActorId victim, ActorId killer)
        => new(AiEventKind.KillFeed, bot, tick, victim, default, default, 0f, killer.Slot, killer.Serial);

    /// <summary>The killer of a <see cref="AiEventKind.KillFeed"/> event.</summary>
    public ActorId Killer => Kind == AiEventKind.KillFeed ? new ActorId(Code, Code2) : ActorId.None;

    public static AiEvent Holdings(ActorId bot, Tick tick, int heldKinds)
        => new(AiEventKind.Holdings, bot, tick, ActorId.None, default, default, 0f, heldKinds, 0);

    public static AiEvent ItemSeen(ActorId bot, Tick tick, int item, bool present)
        => new(AiEventKind.ItemSeen, bot, tick, ActorId.None, default, default, 0f, item, present ? 1 : 0);

    /// <summary><see cref="AiEventKind.ShotFired"/>: for the recording, never a brain.</summary>
    public static AiEvent ShotFired(ActorId actor, Tick tick, int weaponKind, int rounds)
        => new(AiEventKind.ShotFired, actor, tick, ActorId.None, default, default, rounds, weaponKind, 0);

    /// <summary><see cref="AiEventKind.HitDealt"/>: for the recording, never a brain.</summary>
    public static AiEvent HitDealt(ActorId actor, Tick tick, ActorId victim, float damage, int weaponKind, int range)
        => new(AiEventKind.HitDealt, actor, tick, victim, default, default, damage, weaponKind, range);

    public static AiEvent Killed(ActorId bot, Tick tick, ActorId attacker)
        => new(AiEventKind.Killed, bot, tick, attacker, default, default, 0f, 0, 0);

    public static AiEvent Spawned(ActorId bot, Tick tick, AiVector pos)
        => new(AiEventKind.Spawned, bot, tick, ActorId.None, pos, default, 0f, 0, 0);

    /// <summary>The bot picked something up where it stands: <paramref name="itemKind"/> is the binding's code for what (a weapon's catalog type + 1).</summary>
    public static AiEvent PickedUp(ActorId bot, Tick tick, int itemKind, AiVector pos)
        => new(AiEventKind.PickedUp, bot, tick, ActorId.None, pos, default, 0f, itemKind, 0);

    public static AiEvent CommandRejected(ActorId bot, Tick tick, int seq, RejectReason why)
        => new(AiEventKind.CommandRejected, bot, tick, ActorId.None, default, default, 0f, (int)why, seq);

    /// <summary>One scoreboard row as the bot's HUD shows it: an actor's frags, deaths and rank (1 the leader, in <see cref="Amount"/>).</summary>
    public static AiEvent ScoreboardRow(ActorId bot, Tick tick, ActorId actor, int frags, int deaths, int rank = 0)
        => new(AiEventKind.ScoreboardRow, bot, tick, actor, default, default, rank, frags, deaths);
}
