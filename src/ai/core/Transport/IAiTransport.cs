namespace SourceSharp.Ai;

/// <summary>A piece of work a transport runs: a bot's queued brain steps, or a flow field to build.</summary>
public interface IAiWork
{
    void Run();
}

/// <summary>
/// The hand-off between the main thread and wherever brains run (§2.6):
/// <see cref="InlineTransport"/> runs work on the caller, the
/// <see cref="PooledTransport"/> on a worker of an <see cref="AiWorkerPool"/>.
/// A transport never changes a step's inputs, only when and where it runs.
/// </summary>
public interface IAiTransport
{
    /// <summary>Whether work runs on the caller, done when <see cref="Dispatch"/> returns.</summary>
    bool Inline { get; }

    /// <summary>Hands work over; false when nothing will run it (a stopped pool), and the caller keeps it.</summary>
    bool Dispatch(IAiWork work);
}

/// <summary>Runs work at once, on the caller (the main thread's <c>OnPostThink</c> under <c>ai_sync 1</c>, the facts, the replay tool).</summary>
public sealed class InlineTransport : IAiTransport
{
    public bool Inline => true;

    public bool Dispatch(IAiWork work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Run();
        return true;
    }
}

/// <summary>Runs work on the workers of <paramref name="pool"/> (§2.7): never on the caller.</summary>
public sealed class PooledTransport(AiWorkerPool pool) : IAiTransport
{
    public bool Inline => false;

    public AiWorkerPool Pool => pool;

    public bool Dispatch(IAiWork work) => pool.Post(work);
}
