namespace SourceSharp.Ai;

/// <summary>
/// Where a living actor stood at a tick (plan_hl2mp_ai §3.4.4): its origin,
/// its eye and its velocity, humans' and bots' alike, so the metrics can say
/// where a target was when a crosshair reached it and where a body moved
/// against where it looked. The session's anonymised actor, never a name.
/// </summary>
public readonly record struct AiActorFrame(ActorId Actor, Tick Tick, AiVector Origin, AiVector Eye, AiVector Velocity);
