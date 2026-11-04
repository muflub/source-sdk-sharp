using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// <c>ai_threads</c> (plan_hl2mp_ai §2.7): the AI's worker threads from the
/// next level start; 0 is <c>min(4, processors − 1)</c>. The count changes
/// when a step's intent arrives, never what it is.
/// </summary>
[ConVar("ai_threads", "0", Help = "AI worker threads from the next level start; 0 = min(4, processors - 1)", Min = 0, Max = 16)]
public sealed class AiThreadsConVar : IntConVar { }

/// <summary>
/// <c>ai_sync</c> (§2.6): 1 runs the bots' brains inline on the main thread
/// (debugging, and live scrubbing with <c>ai_step</c>); 0, the default, on the
/// worker pool. The intents are the same either way, but a step lands sooner
/// inline, so it is a cheat as <c>ai_step</c> is.
/// </summary>
[ConVar("ai_sync", "0", Help = "1 runs the bots' brains inline on the main thread (debugging); 0 on the AI's worker threads", Flags = ConVarFlags.Cheat)]
public sealed class AiSyncConVar : BoolConVar { }

/// <summary>
/// <c>ai_budget</c> (user ruling 2026-11-03): 1 caps the bots' main-thread work at
/// a floor plus the frame's unused time, a bot that does not fit waiting for a later
/// frame; 0 runs everything every tick, as <c>ai_sync 1</c> always does. Off by default:
/// the trials in plan_hl2mp_ai §2.7.2 did not show it improving frames, and under pressure
/// bots moved in bursts.
/// </summary>
[ConVar("ai_budget", "0", Help = "1 caps the bots' main-thread time at ai_budget_floor_us plus the frame's unused time; 0 runs every bot every tick")]
public sealed class AiBudgetConVar : BoolConVar { }

/// <summary><c>ai_budget_floor_us</c>: what the bots always get in a frame, however full it is.</summary>
[ConVar("ai_budget_floor_us", "1000", Help = "Microseconds of the main thread the bots always get in a frame", Min = 0, Max = 100000)]
public sealed class AiBudgetFloorConVar : IntConVar { }

/// <summary><c>ai_budget_wait_ticks</c>: how many ticks a bot's turn may wait before it runs regardless, so no bot starves; the cap gives way to it, counted (<c>ai.budget.forced</c>).</summary>
[ConVar("ai_budget_wait_ticks", "3", Help = "Ticks a bot's command or sight may wait for the budget before it runs regardless", Min = 0, Max = 32)]
public sealed class AiBudgetWaitConVar : IntConVar { }
