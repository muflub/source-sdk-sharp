using System.Collections.Concurrent;
using System.Diagnostics;

namespace SourceSharp.Ai;

/// <summary>Where a line is asked for: the endpoint and the model.</summary>
public readonly record struct ChatTarget(string Url, string Model);

/// <summary>How a writer's answer ended.</summary>
public enum ChatFailure
{
    None,

    /// <summary>The time ran out (the worker's cancel), or the stop did.</summary>
    Cancelled,

    /// <summary>Nothing listens there, or it is not an endpoint the writer may use.</summary>
    ServerDown,

    /// <summary>The server is up and has no such model.</summary>
    ModelMissing,

    /// <summary>Anything else: a bad status, a body it could not read.</summary>
    Other,
}

/// <summary>A writer's answer: the model's raw text, or why there is none (with a line for the console).</summary>
public readonly record struct ChatWriterReply(string? Text, ChatFailure Failure = ChatFailure.None, string Detail = "");

/// <summary>
/// The seam to the model (plan_hl2mp_ai §3.6): runs on the chat worker, never
/// on the main thread. It may block until <paramref name="cancel"/> fires and
/// must stop soon after. The HTTP one is the binding's; tests hold a fake.
/// </summary>
public interface IBotChatWriter
{
    ChatWriterReply Write(ChatPrompt prompt, ChatTarget target, CancellationToken cancel);

    /// <summary>Asks the server to load the model, so the first line is not lost to a cold start.</summary>
    ChatWriterReply Warm(ChatTarget target, CancellationToken cancel);
}

/// <summary>A line wanted, as the main thread hands it to the worker.</summary>
public sealed record ChatJob(long Id, int Slot, ChatPrompt Prompt, ChatTarget Target, float TimeoutSeconds, string SelfName);

/// <summary>What became of a job, or of the warm-up (<see cref="Id"/> 0).</summary>
public readonly record struct ChatResult(long Id, int Slot, string? Line, ChatRefusal Refusal, ChatFailure Failure, string Detail, bool TimedOut, float Seconds);

/// <summary>The chat worker's counts (§3.6, <c>/ai/counters</c>): written by both threads, read by the route.</summary>
public sealed class ChatCounters
{
    private long _requests, _answered, _latencyTotalUs, _latencyMaxUs, _timedOut, _serverDown, _refused, _rateLimited, _stale, _said, _replies, _mainTotalNs, _mainTicks, _mainMaxNs, _leaked, _finished;
    private readonly long[] _refusedBy = new long[Enum.GetValues<ChatRefusal>().Length];

    public long Requests => Interlocked.Read(ref _requests);

    public long Answered => Interlocked.Read(ref _answered);

    public long TimedOut => Interlocked.Read(ref _timedOut);

    public long ServerDown => Interlocked.Read(ref _serverDown);

    public long Refused => Interlocked.Read(ref _refused);

    public long RateLimited => Interlocked.Read(ref _rateLimited);

    public long Stale => Interlocked.Read(ref _stale);

    public long Said => Interlocked.Read(ref _said);

    public long Replies => Interlocked.Read(ref _replies);

    public long Leaked => Interlocked.Read(ref _leaked);

    /// <summary>Requests the worker is done with, their results in its queue.</summary>
    public long Finished => Interlocked.Read(ref _finished);

    public double LatencyMeanMs => Answered == 0 ? 0.0 : Interlocked.Read(ref _latencyTotalUs) / 1000.0 / Answered;

    public double LatencyMaxMs => Interlocked.Read(ref _latencyMaxUs) / 1000.0;

    /// <summary>The main thread's bookkeeping per tick, in microseconds, since the level started.</summary>
    public double MainMeanUs => _mainTicks == 0 ? 0.0 : _mainTotalNs / 1000.0 / _mainTicks;

    public double MainMaxUs => _mainMaxNs / 1000.0;

    public long MainTicks => _mainTicks;

    public long RefusedBy(ChatRefusal why) => Interlocked.Read(ref _refusedBy[(int)why]);

    public void Request() => Interlocked.Increment(ref _requests);

    public void Answer(float seconds)
    {
        long us = (long)(seconds * 1e6f);
        Interlocked.Increment(ref _answered);
        Interlocked.Add(ref _latencyTotalUs, us);
        long max;
        while (us > (max = Interlocked.Read(ref _latencyMaxUs)) && Interlocked.CompareExchange(ref _latencyMaxUs, us, max) != max)
        {
        }
    }

    public void Timeout() => Interlocked.Increment(ref _timedOut);

    public void Down() => Interlocked.Increment(ref _serverDown);

    public void Refuse(ChatRefusal why)
    {
        Interlocked.Increment(ref _refused);
        Interlocked.Increment(ref _refusedBy[(int)why]);
    }

    public void Limited() => Interlocked.Increment(ref _rateLimited);

    public void Drop() => Interlocked.Increment(ref _stale);

    public void Say(bool reply)
    {
        Interlocked.Increment(ref _said);
        if (reply)
            Interlocked.Increment(ref _replies);
    }

    public void Leak() => Interlocked.Increment(ref _leaked);

    public void Finish() => Interlocked.Increment(ref _finished);

    /// <summary>Main thread only.</summary>
    public void MainTick(long nanoseconds)
    {
        _mainTotalNs += nanoseconds;
        _mainTicks++;
        if (nanoseconds > _mainMaxNs)
            _mainMaxNs = nanoseconds;
    }
}

/// <summary>
/// The thread bot chat waits on so the game never does (§3.6): one background
/// thread that takes jobs one at a time, asks the writer under a timeout, runs
/// the output filter and leaves the result in a queue the main thread drains.
/// Posting and taking never block. <see cref="Stop"/> cancels what is in flight
/// and joins the thread, so a level's end leaves nothing running and the
/// server's load context can unload.
/// </summary>
public sealed class ChatWorker(IBotChatWriter writer, ChatOutputFilter filter, ChatCounters counters, int capacity = 4) : IDisposable
{
    /// <summary>The longest a stop waits for the writer to notice its cancel.</summary>
    public const int JoinMilliseconds = 2000;

    /// <summary>How long a model may take to load before the warm-up gives up.</summary>
    public const float WarmSeconds = 60f;

    private readonly ConcurrentQueue<ChatJob> _jobs = new();
    private readonly ConcurrentQueue<ChatResult> _results = new();
    private readonly SemaphoreSlim _ready = new(0);
    private CancellationTokenSource? _stop;
    private Thread? _thread;
    private ChatTarget? _warm;
    private int _queued;

    public bool Running => _thread is not null;

    /// <summary>Starts the thread; <paramref name="warm"/> is loaded first.</summary>
    public void Start(ChatTarget? warm = null)
    {
        if (_thread is not null)
            return;
        _warm = warm;
        _stop = new CancellationTokenSource();
        _thread = new Thread(Run) { Name = "ai-chat", IsBackground = true };
        _thread.Start();
    }

    /// <summary>Hands <paramref name="job"/> over without waiting; false when the worker is not running or is full (the line is dropped).</summary>
    public bool TryPost(ChatJob job)
    {
        if (_thread is null || Volatile.Read(ref _queued) >= capacity)
            return false;
        Interlocked.Increment(ref _queued);
        _jobs.Enqueue(job);
        _ready.Release();
        return true;
    }

    public bool TryTake(out ChatResult result) => _results.TryDequeue(out result);

    /// <summary>Cancels what is in flight, joins the thread and forgets every job and result. Answers whether the thread ended.</summary>
    public bool Stop()
    {
        if (_thread is not { } thread)
            return true;
        _stop!.Cancel();
        bool ended = thread.Join(JoinMilliseconds);
        if (!ended)
            counters.Leak();
        _thread = null;
        _stop.Dispose();
        _stop = null;
        _jobs.Clear();
        _results.Clear();
        Volatile.Write(ref _queued, 0);
        while (_ready.Wait(0))
        {
        }

        return ended;
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    private void Run()
    {
        CancellationToken stop = _stop!.Token;
        if (_warm is { } warm)
            Warm(warm, stop);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                _ready.Wait(stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_jobs.TryDequeue(out ChatJob? job))
                continue;
            ChatResult result = Ask(job, stop);
            if (!stop.IsCancellationRequested)
                _results.Enqueue(result);
            Interlocked.Decrement(ref _queued);
            counters.Finish();
        }
    }

    private void Warm(ChatTarget target, CancellationToken stop)
    {
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timed.CancelAfter(TimeSpan.FromSeconds(WarmSeconds));
        ChatWriterReply reply;
        try
        {
            reply = writer.Warm(target, timed.Token);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            reply = new ChatWriterReply(null, e is OperationCanceledException ? ChatFailure.Cancelled : ChatFailure.Other, e.Message);
        }

        if (!stop.IsCancellationRequested)
            _results.Enqueue(new ChatResult(0, 0, null, ChatRefusal.None, reply.Failure, reply.Detail, false, 0f));
    }

    private ChatResult Ask(ChatJob job, CancellationToken stop)
    {
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timed.CancelAfter(TimeSpan.FromSeconds(job.TimeoutSeconds));
        long start = Stopwatch.GetTimestamp();
        ChatWriterReply reply;
        try
        {
            reply = writer.Write(job.Prompt, job.Target, timed.Token);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            reply = new ChatWriterReply(null, e is OperationCanceledException ? ChatFailure.Cancelled : ChatFailure.Other, e.Message);
        }

        float seconds = (float)Stopwatch.GetElapsedTime(start).TotalSeconds;
        if (reply.Failure != ChatFailure.None || reply.Text is null)
        {
            bool timedOut = reply.Failure == ChatFailure.Cancelled && !stop.IsCancellationRequested;
            if (timedOut)
                counters.Timeout();
            else if (reply.Failure is ChatFailure.ServerDown or ChatFailure.ModelMissing)
                counters.Down();
            return new ChatResult(job.Id, job.Slot, null, ChatRefusal.None, reply.Failure, reply.Detail, timedOut, seconds);
        }

        counters.Answer(seconds);
        ChatVerdict verdict = filter.Check(reply.Text, job.Prompt.Whole, job.SelfName);
        if (verdict.Refusal != ChatRefusal.None)
            counters.Refuse(verdict.Refusal);
        return new ChatResult(job.Id, job.Slot, verdict.Line, verdict.Refusal, ChatFailure.None, reply.Text, false, seconds);
    }
}
