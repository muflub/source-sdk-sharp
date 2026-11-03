namespace SourceSharp.Ai;

/// <summary>
/// One brain step to run: the bot, its decide tick, and the events it has
/// not yet seen (plan_hl2mp_ai §2.6). The inputs are fixed when the job is
/// made, so the transport only decides where and when it runs.
/// </summary>
public sealed class BrainStepJob
{
    private AiEvent[] _events;

    internal BrainStepJob(int capacity) => _events = new AiEvent[capacity];

    public Tick Tick { get; private set; }

    public int Slot { get; private set; }

    public int EventCount { get; private set; }

    public ReadOnlySpan<AiEvent> Events => _events.AsSpan(0, EventCount);

    internal void Set(int slot, Tick tick, ReadOnlySpan<AiEvent> events)
    {
        Slot = slot;
        Tick = tick;
        if (events.Length > _events.Length)
            _events = new AiEvent[Math.Max(events.Length, _events.Length * 2)];
        events.CopyTo(_events);
        EventCount = events.Length;
    }
}

/// <summary>Runs a <see cref="BrainStepJob"/> (the session's side of the hand-off).</summary>
public interface IBrainStepRunner
{
    void Run(BrainStepJob job);
}

/// <summary>
/// The hand-off between the main thread and wherever brains run (§2.6):
/// <see cref="InlineTransport"/> runs a step on the caller, the pooled
/// transport (Phase 6) on a worker. A transport never changes a step's
/// inputs, only when it runs.
/// </summary>
public interface IAiTransport
{
    /// <summary>Hands one step over. The jobs of one tick arrive in bot-slot order.</summary>
    void Dispatch(BrainStepJob job, IBrainStepRunner runner);
}

/// <summary>Runs each step at once, on the caller (the main thread's <c>OnPostThink</c>; <c>ai_sync 1</c>).</summary>
public sealed class InlineTransport : IAiTransport
{
    public void Dispatch(BrainStepJob job, IBrainStepRunner runner)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(runner);
        runner.Run(job);
    }
}
