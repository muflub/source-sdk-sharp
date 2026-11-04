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
}

/// <summary>How a <see cref="ActionKind.Fire"/> is held.</summary>
public enum FireStyle : byte
{
    Tap = 0,
    Burst,
    Spray,
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

/// <summary>Why the brain chose this intent: for the inspector only; the applier ignores it.</summary>
public readonly record struct DecisionReason(
    int NodeId,
    float Utility,
    float ScoreboardTerm,
    float AmmoTerm);

/// <summary>
/// A brain's output for one bot from one decide tick, valid until
/// <see cref="ExpiresTick"/> (§2.4: <c>fromTick + 16</c>, about 240 ms).
/// </summary>
public readonly record struct IntentCommand(
    ActorId Bot,
    Tick FromTick,
    int Seq,
    Tick ExpiresTick,
    MoveIntent Move,
    LookIntent Look,
    ActionIntent Action,
    DecisionReason Reason)
{
    /// <summary>How many ticks an intent lives (§2.4).</summary>
    public const int LifetimeTicks = 16;

    /// <summary>Stand still, look along the path, do nothing.</summary>
    public static IntentCommand Hold(ActorId bot, Tick from, int seq)
        => new(bot, from, seq, from + LifetimeTicks, MoveIntent.Hold, LookIntent.AlongPath, ActionIntent.None, default);

    public bool ExpiredAt(Tick tick) => tick >= ExpiresTick;
}
