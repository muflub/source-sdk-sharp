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

    public static AiEvent SeenFire(ActorId bot, Tick tick, ActorId shooter, int weaponKind)
        => new(AiEventKind.SeenFire, bot, tick, shooter, default, default, 0f, 0, weaponKind);

    public static AiEvent SeenReload(ActorId bot, Tick tick, ActorId actor, ReloadPhase phase)
        => new(AiEventKind.SeenReload, bot, tick, actor, default, default, 0f, (int)phase, 0);

    public static AiEvent Damaged(ActorId bot, Tick tick, ActorId attacker, float amount, AiVector dirFromBot, int damageType)
        => new(AiEventKind.Damaged, bot, tick, attacker, dirFromBot, default, amount, damageType, 0);

    public static AiEvent Killed(ActorId bot, Tick tick, ActorId attacker)
        => new(AiEventKind.Killed, bot, tick, attacker, default, default, 0f, 0, 0);

    public static AiEvent Spawned(ActorId bot, Tick tick, AiVector pos)
        => new(AiEventKind.Spawned, bot, tick, ActorId.None, pos, default, 0f, 0, 0);

    public static AiEvent CommandRejected(ActorId bot, Tick tick, int seq, RejectReason why)
        => new(AiEventKind.CommandRejected, bot, tick, ActorId.None, default, default, 0f, (int)why, seq);

    /// <summary>One scoreboard row as the bot's HUD shows it: an actor's frags and deaths.</summary>
    public static AiEvent ScoreboardRow(ActorId bot, Tick tick, ActorId actor, int frags, int deaths)
        => new(AiEventKind.ScoreboardRow, bot, tick, actor, default, default, 0f, frags, deaths);
}
