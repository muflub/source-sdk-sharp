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
/// worker pool. The intents are the same either way.
/// </summary>
[ConVar("ai_sync", "0", Help = "1 runs the bots' brains inline on the main thread (debugging); 0 on the AI's worker threads")]
public sealed class AiSyncConVar : BoolConVar { }
