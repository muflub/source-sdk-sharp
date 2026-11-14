using SourceSharp.Abi;
using SourceSharp.Ai;

namespace TF2.Descent.Server.Horde.Session;

/// <summary><c>tf2_descent_ai_sync</c>: 1 runs the horde's brains inline on the game thread (A/B the logic against the hand-off); 0, the default, on the AI workers.</summary>
[ConVar("tf2_descent_ai_sync", "0",
        Help = "1: the horde's robot brains run inline on the game thread (A/B 'the logic is wrong' against 'the hand-off is wrong'); 0: on the AI workers. Read when a level's horde begins.")]
public sealed class HordeAiSyncConVar : BoolConVar { }

/// <summary><c>tf2_descent_ai_threads</c>: the AI workers a level's horde starts; 0 the core's default (min(4, processors - 1)).</summary>
[ConVar("tf2_descent_ai_threads", "2", Min = 0, Max = 16,
        Help = "AI worker threads a level's horde starts (0: min(4, processors - 1)). Read when a level's horde begins.")]
public sealed class HordeAiThreadsConVar : IntConVar { }

/// <summary><c>tf2_descent_ai_record</c>: 1 records each level's horde session to an .airec file.</summary>
[ConVar("tf2_descent_ai_record", "0",
        Help = "1: each level's horde session records its inputs and intents to <game>/ai_recordings/horde-<map>-<n>.airec (AiRecorder). Read when a level's horde begins.")]
public sealed class HordeAiRecordConVar : BoolConVar { }

/// <summary>
/// The horde's AI worker pool (plan_descent_logic 3.6, plan_tf2_rpg B.7): ONE
/// pool object for the server process, its threads started when a level's
/// horde begins and stopped and joined when it ends, so a level's teardown
/// leaves the process's thread count where it was, and twenty levels do not
/// mean twenty pools. Disposed (stopped) with the server's mod services.
/// </summary>
[SideService(Side.Server)]
public sealed class HordeWorkers : IDisposable
{
    private readonly AiWorkerPool _pool = new("descent-ai") { CpuClock = HordeThreadClock.Micros };

    public AiWorkerPool Pool => _pool;

    /// <summary>Times the threads were started.</summary>
    public int Starts { get; private set; }

    /// <summary>Starts <paramref name="threads"/> workers (0 the core's default), stopping any still running first.</summary>
    public void Start(int threads)
    {
        _pool.Start(threads > 0 ? threads : AiWorkerPool.DefaultThreads);
        Starts++;
    }

    /// <summary>Stops the workers and joins them; answers how much queued work was discarded.</summary>
    public int Stop() => _pool.Stop();

    public void Dispose() => _pool.Dispose();
}
