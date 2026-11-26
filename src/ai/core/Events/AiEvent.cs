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

    /// <summary>One team score the HUD shows (plan_bots_teams_tf2 B.2): Code the team, Code2 its score. A mode may play two to four teams.</summary>
    MatchState = 41,

    /// <summary>A death notice on the HUD's kill feed: who died, and who killed them.</summary>
    KillFeed = 42,

    /// <summary>The bot looked at a static item spawn: Code the spawn's index, Code2 1 if the item was there.</summary>
    ItemSeen = 43,

    /// <summary>The weapons the bot holds, as its HUD's weapon selection shows them: a mask of catalog kinds in Code.</summary>
    Holdings = 44,

    /// <summary>The level's choke points are worked out: from here the bot may use them (§5.5). They are map knowledge; the event only fixes the tick, so a replay decides alike.</summary>
    ChokesKnown = 45,

    /// <summary>The answer to a bot's look along a line (§5.4's prefire): Position the point, Code the ask's number, Amount 1 if nothing solid is in the way. World and props only: no player stops it.</summary>
    LineAnswer = 46,

    /// <summary>The scoreboard says an actor went to watch (Code 1) or came back to play (Code 0): to a bot, gone or new (§4.1, ruling 2026-11-02).</summary>
    ActorSpectating = 47,

    /// <summary>The server's <c>ai_bot_streaks</c> as told to a bot (§5.5): Code 0 off, 1 on; a console's asking for one now, 2 hot, 3 cold.</summary>
    StreaksSet = 48,

    /// <summary>
    /// The <c>player_team</c> event and the scoreboard's team column, as every client has them
    /// (plan_bots_teams_tf2 B.2, B.9): Other is now on team Code (0 when the mode plays no teams),
    /// coming from Code2. The bot itself is told of its own change too.
    /// </summary>
    ActorTeam = 49,

    /// <summary>The server's <c>mp_friendlyfire</c>, which every client is sent (a replicated setting): Code 1 on, 0 off (plan_bots_teams_tf2 B.5).</summary>
    FriendlyFireSet = 54,

    /// <summary>
    /// A teammate's callout (plan_bots_teams_tf2 B.4), re-published by the session to the
    /// speaker's teammates' lanes after its delay: Other the speaker, Position the place,
    /// Amount the confidence, Code the <see cref="CalloutKind"/> and the count (count &lt;&lt; 8),
    /// Code2 the subject's slot plus one (0 none).
    /// </summary>
    Callout = 55,

    /// <summary>A teammate's order read in team chat (B.5, Q5): Other the one who said it, Code the <see cref="OrderKind"/>.</summary>
    Order = 56,

    /// <summary>
    /// The bot's HUD marks Other as its nemesis (Code 1), or no longer (Code 0): a game's own
    /// standing mark on someone who keeps killing the bot unanswered (plan_bots_teams_tf2_part_g
    /// G.2.5: TF2's domination, which its scoreboard and the icon over the dominator show the
    /// dominated player).
    /// </summary>
    Nemesis = 57,

    /// <summary>The bot reached the place its intent last moved it to (its applier's feedback): Position the place.</summary>
    Arrived = 58,

    /// <summary>
    /// A teammate was hurt where the bot could know of it (its pack's own channel, a game's team
    /// callout): Other the ally, Position where it stands, Amount the damage, the attacker in Code
    /// and Code2 (none for the world).
    /// </summary>
    AllyDamaged = 59,

    /// <summary>A teammate died: Other the ally, Position where it fell, the killer in Code and Code2 (none for the world).</summary>
    AllyDied = 60,

    /// <summary>
    /// A hero (a player the bot fights) went down and can be revived (Code 1), or got up again
    /// (Code 0): Other the hero, Position where. A downed hero is no threat while it lies there.
    /// </summary>
    HeroDowned = 61,

    /// <summary>
    /// The bot's body ran into Other, a solid player (plan_bots_teams_tf2 C.3, stock's <c>CTFBot::Touch</c>, tf_bot.cpp:1445):
    /// teammates pass through each other, so a solid one the bot's client draws as a teammate is an enemy in a disguise.
    /// </summary>
    Bumped = 62,

    // recorded only, as Observed records (§3.4.4's hit rate): no brain is told
    /// <summary>The actor fired: Code the weapon's kind, Amount the rounds the fire took. A trigger pull, however many pellets it throws.</summary>
    ShotFired = 50,

    /// <summary>The actor pressed its second trigger and it went: Code the weapon's kind.</summary>
    AltFired = 52,

    /// <summary>The actor's fire hurt Other: Amount the damage, Code the weapon in the actor's hands (-1 none), Code2 the range in units (-1 unknown).</summary>
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

    /// <summary>The killer of a <see cref="AiEventKind.KillFeed"/> or <see cref="AiEventKind.AllyDied"/> event, the attacker of an <see cref="AiEventKind.AllyDamaged"/> one.</summary>
    public ActorId Killer => Kind is AiEventKind.KillFeed or AiEventKind.AllyDied or AiEventKind.AllyDamaged ? new ActorId(Code, Code2) : ActorId.None;

    /// <summary>The bot reached <paramref name="at"/>, where its intent last moved it.</summary>
    public static AiEvent Arrived(ActorId bot, Tick tick, AiVector at)
        => new(AiEventKind.Arrived, bot, tick, ActorId.None, at, default, 0f, 0, 0);

    /// <summary><paramref name="ally"/>, standing at <paramref name="at"/>, took <paramref name="amount"/> damage from <paramref name="attacker"/> (none for the world).</summary>
    public static AiEvent AllyDamaged(ActorId bot, Tick tick, ActorId ally, AiVector at, float amount, ActorId attacker)
        => new(AiEventKind.AllyDamaged, bot, tick, ally, at, default, amount, attacker.Slot, attacker.Serial);

    /// <summary><paramref name="ally"/> died at <paramref name="at"/>, killed by <paramref name="killer"/> (none for the world).</summary>
    public static AiEvent AllyDied(ActorId bot, Tick tick, ActorId ally, AiVector at, ActorId killer)
        => new(AiEventKind.AllyDied, bot, tick, ally, at, default, 0f, killer.Slot, killer.Serial);

    /// <summary><paramref name="hero"/> went down at <paramref name="at"/> (<paramref name="down"/>), or got up again.</summary>
    /// <summary>The bot's body touched <paramref name="other"/>, a player solid to it (<see cref="AiEventKind.Bumped"/>).</summary>
    public static AiEvent Bumped(ActorId bot, Tick tick, ActorId other, AiVector at)
        => new(AiEventKind.Bumped, bot, tick, other, at, default, 0f, 0, 0);

    public static AiEvent HeroDowned(ActorId bot, Tick tick, ActorId hero, AiVector at, bool down)
        => new(AiEventKind.HeroDowned, bot, tick, hero, at, default, 0f, down ? 1 : 0, 0);

    public static AiEvent Holdings(ActorId bot, Tick tick, int heldKinds)
        => new(AiEventKind.Holdings, bot, tick, ActorId.None, default, default, 0f, heldKinds, 0);

    public static AiEvent ChokesKnown(ActorId bot, Tick tick) => new(AiEventKind.ChokesKnown, bot, tick, ActorId.None, default, default, 0f, 0, 0);

    /// <summary>What a <see cref="AiEventKind.StreaksSet"/> says.</summary>
    public const int StreaksOff = 0, StreaksOn = 1, StreakHot = 2, StreakCold = 3;

    public static AiEvent StreaksSet(ActorId bot, Tick tick, int code)
        => new(AiEventKind.StreaksSet, bot, tick, ActorId.None, default, default, 0f, code, 0);

    /// <summary>The <c>player_team</c> event as a client is told it: <paramref name="actor"/> went from <paramref name="oldTeam"/> to <paramref name="team"/>.</summary>
    public static AiEvent ActorTeam(ActorId bot, Tick tick, ActorId actor, int team, int oldTeam)
        => new(AiEventKind.ActorTeam, bot, tick, actor, default, default, 0f, team, oldTeam);

    /// <summary>What every callout is worth to the one who hears it (B.4): heard, not seen.</summary>
    public const float CalloutConfidence = 0.5f;

    public static AiEvent Callout(ActorId bot, Tick tick, ActorId speaker, in SayIntent say)
        => new(AiEventKind.Callout, bot, tick, speaker, say.Place, default, CalloutConfidence, (byte)say.Kind | (say.Count << 8), say.Subject);

    /// <summary>A <see cref="AiEventKind.Callout"/>'s kind.</summary>
    public CalloutKind CalloutKind => Kind == AiEventKind.Callout ? (CalloutKind)(Code & 0xFF) : CalloutKind.None;

    /// <summary>A <see cref="AiEventKind.Callout"/>'s count.</summary>
    public int CalloutCount => Kind == AiEventKind.Callout ? Code >> 8 : 0;

    public static AiEvent Order(ActorId bot, Tick tick, ActorId from, OrderKind order)
        => new(AiEventKind.Order, bot, tick, from, default, default, 0f, (int)order, 0);

    /// <summary>The bot's HUD began (<paramref name="marked"/>) or stopped marking <paramref name="actor"/> as its nemesis.</summary>
    public static AiEvent Nemesis(ActorId bot, Tick tick, ActorId actor, bool marked)
        => new(AiEventKind.Nemesis, bot, tick, actor, default, default, 0f, marked ? 1 : 0, 0);

    public static AiEvent FriendlyFireSet(ActorId bot, Tick tick, bool on)
        => new(AiEventKind.FriendlyFireSet, bot, tick, ActorId.None, default, default, 0f, on ? 1 : 0, 0);

    /// <summary>A team score the bot's HUD shows: <paramref name="team"/>'s.</summary>
    public static AiEvent MatchState(ActorId bot, Tick tick, int team, int score)
        => new(AiEventKind.MatchState, bot, tick, ActorId.None, default, default, 0f, team, score);

    public static AiEvent ActorSpectating(ActorId bot, Tick tick, ActorId actor, bool spectating)
        => new(AiEventKind.ActorSpectating, bot, tick, actor, default, default, 0f, spectating ? 1 : 0, 0);

    public static AiEvent LineAnswer(ActorId bot, Tick tick, AiVector point, int ask, bool clear)
        => new(AiEventKind.LineAnswer, bot, tick, ActorId.None, point, default, clear ? 1f : 0f, ask, 0);

    public static AiEvent ItemSeen(ActorId bot, Tick tick, int item, bool present)
        => new(AiEventKind.ItemSeen, bot, tick, ActorId.None, default, default, 0f, item, present ? 1 : 0);

    /// <summary>Written for a sighting already held when a recording starts (Code 1): a shot's target, but no stimulus for a reaction.</summary>
    public static AiEvent SightHeld(ActorId bot, Tick tick, ActorId target, AiVector pos, AiVector vel, float visibleFraction)
        => new(AiEventKind.SightGained, bot, tick, target, pos, vel, visibleFraction, HeldSight, 0);

    /// <summary>A <see cref="AiEventKind.SightGained"/>'s Code when the sighting began before the recording did.</summary>
    public const int HeldSight = 1;

    /// <summary><see cref="AiEventKind.ShotFired"/>: for the recording, never a brain.</summary>
    public static AiEvent ShotFired(ActorId actor, Tick tick, int weaponKind, int rounds)
        => new(AiEventKind.ShotFired, actor, tick, ActorId.None, default, default, rounds, weaponKind, 0);

    /// <summary><see cref="AiEventKind.AltFired"/>: for the recording, never a brain.</summary>
    public static AiEvent AltFired(ActorId actor, Tick tick, int weaponKind)
        => new(AiEventKind.AltFired, actor, tick, ActorId.None, default, default, 1f, weaponKind, 0);

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

    /// <summary>
    /// One scoreboard row as the bot's HUD shows it: an actor's frags, deaths and rank (1 the leader, in
    /// <see cref="Amount"/>), and the team and class the row shows (plan_bots_teams_tf2 C.2; 0 for none),
    /// carried in <see cref="Position"/>'s X and Y so the recording's layout stays.
    /// </summary>
    public static AiEvent ScoreboardRow(ActorId bot, Tick tick, ActorId actor, int frags, int deaths, int rank = 0, int team = 0, int playerClass = 0)
        => new(AiEventKind.ScoreboardRow, bot, tick, actor, new AiVector(team, playerClass, 0f), default, rank, frags, deaths);
}
