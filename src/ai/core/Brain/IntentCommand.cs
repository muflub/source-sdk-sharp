namespace SourceSharp.Ai;

/// <summary>How the body moves (plan_hl2mp_ai §2.4).</summary>
public enum MoveKind : byte
{
    Hold = 0,
    Follow,
    Strafe,
    Retreat,
    Jump,
    CrouchJump,
    Crouch,
    Pause,
}

/// <summary>How fast a <see cref="MoveKind.Follow"/> or <see cref="MoveKind.Retreat"/> goes.</summary>
public enum MoveSpeed : byte
{
    Run = 0,
    Walk,
    Sprint,
}

/// <summary>Where the view goes.</summary>
public enum LookKind : byte
{
    Path = 0,
    Point,
    Actor,
    Glance,
    CornerCheck,

    /// <summary>Holding a place, the view swaying across it: one stimulus while <see cref="LookIntent.Ticks"/> names the same place.</summary>
    Watch,
}

/// <summary>Which part of an actor to aim at.</summary>
public enum AimPoint : byte
{
    /// <summary>The chest: what a blast or a swing is aimed at, and a look at a place.</summary>
    Body = 0,
    Head,

    /// <summary>The shoulders, a gun's default (§3.4.1, ruling 2026-11-03): the height a crosshair is kept at.</summary>
    Shoulder,

    /// <summary>The feet: where a splash weapon is aimed, so a miss still lands its blast by the body (plan_bots_teams_tf2 C.3, a Soldier's rocket).</summary>
    Feet,
}

/// <summary>What the hands do.</summary>
public enum ActionKind : byte
{
    None = 0,
    Fire,
    AltFire,
    Reload,
    SelectWeapon,
    Use,

    /// <summary>
    /// A one-shot of the binding's own (plan_bots_teams_tf2 C.3): the
    /// <see cref="ActionIntent.WeaponSlot"/> field carries the row of the
    /// binding's <see cref="AbilityTable"/>, so the core names no game's verb.
    /// </summary>
    Ability,
}

/// <summary>How a <see cref="ActionKind.Fire"/> is held.</summary>
public enum FireStyle : byte
{
    Tap = 0,
    Burst,
    Spray,

    /// <summary>
    /// A second trigger held that is no shot (a minigun's spin, plan_bots_teams_tf2 P7): pressed whatever the view, and kept down
    /// while a <see cref="ActionKind.Fire"/> waits for the eye. Only an <see cref="ActionKind.AltFire"/> takes it; a Spray
    /// AltFire (HL2MP's both barrels) is a shot and waits as before.
    /// </summary>
    Hold,
}

/// <summary>
/// The move part of an intent. Up to four path corners, held inline so an
/// intent is a value with no array behind it.
/// </summary>
public readonly record struct MoveIntent(
    MoveKind Kind,
    MoveSpeed Speed,
    byte CornerCount,
    AiVector Corner0,
    AiVector Corner1,
    AiVector Corner2,
    AiVector Corner3,
    AiVector Direction,
    bool CrouchOn,
    int Ticks)
{
    public const int MaxCorners = 4;

    public static MoveIntent Hold => default;

    /// <summary>Corner <paramref name="i"/> of the <see cref="CornerCount"/> held.</summary>
    public AiVector Corner(int i)
    {
        if ((uint)i >= CornerCount)
            throw new ArgumentOutOfRangeException(nameof(i), i, $"the move holds {CornerCount} corners");
        return i switch { 0 => Corner0, 1 => Corner1, 2 => Corner2, _ => Corner3 };
    }
}

/// <summary>The look part of an intent.</summary>
public readonly record struct LookIntent(
    LookKind Kind,
    AiVector Point,
    ActorId Actor,
    AimPoint Aim,
    float LeadSeconds,
    int Ticks)
{
    public static LookIntent AlongPath => default;
}

/// <summary>
/// The action part of an intent. A non-zero <see cref="OneShotSeq"/> goes on
/// the sequenced one-shot channel and is applied exactly once (§2.4).
/// </summary>
public readonly record struct ActionIntent(
    ActionKind Kind,
    FireStyle Style,
    int WeaponSlot,
    int OneShotSeq)
{
    public static ActionIntent None => default;
}

/// <summary>What a callout says (plan_bots_teams_tf2 B.4): the words a player on a team would use.</summary>
public enum CalloutKind : byte
{
    None = 0,

    /// <summary>"Enemy at the lower hall": an enemy kind, a place, a count.</summary>
    Enemy,

    /// <summary>"Regroup at ...": a rally call on no progress (B.5).</summary>
    Rally,

    /// <summary>"Push!": the rally has gathered, or its time ran out.</summary>
    Push,

    /// <summary>"Going around": the speaker takes the other route to the enemy (Crossfire).</summary>
    GoingAround,

    /// <summary>
    /// "Help!": the speaker wants a teammate by them (plan_bots_teams_tf2_part_g G.2.4: a game's
    /// voice line, heard as its clients hear it). A count of 1 says it is a healer they ask for ("Medic!").
    /// </summary>
    Help,

    /// <summary>"Sentry ahead!": something to keep away from stands near the speaker; the place is the speaker's own.</summary>
    Hazard,
}

/// <summary>
/// The say slot of an intent (plan_bots_teams_tf2 B.4): a callout to the speaker's
/// teammates, re-published by the session to their lanes <see cref="DelayTicks"/>
/// after the intent's tick, in slot order. A place, never a cell or an exact
/// position; <see cref="Subject"/> is the slot the callout is about plus one (0 none);
/// <see cref="Text"/> asks for a <c>say_team</c> line too, for the humans on the team.
/// </summary>
public readonly record struct SayIntent(
    CalloutKind Kind,
    AiVector Place,
    int Count,
    int Subject,
    int DelayTicks,
    int Team,
    bool Text)
{
    public static SayIntent Nothing => default;

    public bool Says => Kind != CalloutKind.None;
}

/// <summary>Why the brain chose this intent: for the inspector only; the applier ignores it.</summary>
public readonly record struct DecisionReason(
    int NodeId,
    float Utility,
    float ScoreboardTerm,
    float AmmoTerm);

/// <summary>
/// A brain's output for one bot from one decide tick, valid until
/// <see cref="ExpiresTick"/> (§2.4: <c>fromTick + 16</c>, about 240 ms).
/// <see cref="HoldsAtFriend"/>: the brain holds Fire at a teammate on purpose (a Medic's beam, which heals only while held),
/// said on every decide, one carrying a one-shot too: after a decide that held it, a decide with a one-shot (a voice line, the
/// pop) that still says so keeps the trigger down; one that does not lets it go (round 2's S9). HL2MP never sets it.
/// </summary>
public readonly record struct IntentCommand(
    ActorId Bot,
    Tick FromTick,
    int Seq,
    Tick ExpiresTick,
    MoveIntent Move,
    LookIntent Look,
    ActionIntent Action,
    DecisionReason Reason,
    SayIntent Say = default,
    bool HoldsAtFriend = false)
{
    /// <summary>How many ticks an intent lives (§2.4).</summary>
    public const int LifetimeTicks = 16;

    /// <summary>Stand still, look along the path, do nothing.</summary>
    public static IntentCommand Hold(ActorId bot, Tick from, int seq)
        => new(bot, from, seq, from + LifetimeTicks, MoveIntent.Hold, LookIntent.AlongPath, ActionIntent.None, default);

    public bool ExpiredAt(Tick tick) => tick >= ExpiresTick;
}
