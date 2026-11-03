namespace SourceSharp.Ai;

/// <summary>
/// One player's usercmd as the server ran it (plan_hl2mp_ai §3.4.4): bots'
/// and humans', recorded by the same tap so the two can be compared. The
/// core's own copy of the fields that matter; the binding translates the
/// engine's. <see cref="Actor"/> is the session's anonymised actor, never a
/// name or a Steam id.
/// </summary>
public readonly record struct AiUserCmd(
    ActorId Actor,
    Tick Tick,
    int CommandNumber,
    int TickCount,
    AiAngles View,
    float ForwardMove,
    float SideMove,
    float UpMove,
    int Buttons,
    byte Impulse,
    int WeaponSelect,
    int WeaponSubtype,
    int RandomSeed,
    short MouseDx,
    short MouseDy);
