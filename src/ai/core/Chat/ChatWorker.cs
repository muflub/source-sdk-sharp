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
/// <param name="Check">For an answer: what the model's line is held to.</param>
/// <param name="Fallback">For an answer: the true line that goes out if the model's fails, or fails to come.</param>
public sealed record ChatJob(long Id, int Slot, ChatPrompt Prompt, ChatTarget Target, float TimeoutSeconds, string SelfName, ChatAnswerCheck? Check = null, string? Fallback = null);

/// <summary>What became of a job, or of the warm-up (<see cref="Id"/> 0).</summary>
public readonly record struct ChatResult(long Id, int Slot, string? Line, ChatRefusal Refusal, ChatFailure Failure, string Detail, bool TimedOut, float Seconds);

/// <summary>What stopped a wish from being asked for: the first limit that stood in its way when it was given up.</summary>
public enum ChatLimit
{
    /// <summary>The server's one line per interval.</summary>
    Interval,

    /// <summary>The bot's own cooldown.</summary>
    Cooldown,

    /// <summary>A long line wanted in a fight.</summary>
    Fight,

    /// <summary>The server's one reply per few seconds.</summary>
    Reply,

    /// <summary>The pause while the server is down.</summary>
    Paused,

    /// <summary>The worker's queue was full.</summary>
    Queue,

    /// <summary>The bot had gone.</summary>
    Gone,
}

/// <summary>The chat worker's counts (§3.6, <c>/ai/counters</c>): written by both threads, read by the route.</summary>
public sealed class ChatCounters
{
    private long _requests, _answered, _latencyTotalUs, _latencyMaxUs, _timedOut, _serverDown, _refused, _rateLimited, _stale, _said, _replies, _mainTotalNs, _mainTicks, _mainMaxNs, _leaked, _finished;
    private readonly long[] _refusedBy = new long[Enum.GetValues<ChatRefusal>().Length];
    private readonly long[] _limitedBy = new long[Enum.GetValues<ChatLimit>().Length];
    private long _triggers, _chanceMissed, _questions, _answersModel, _answersTemplate, _answersUnverified, _shrugs, _marks, _answersDropped;

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

    /// <summary>The main thread's whole bookkeeping so far, in microseconds, for a mean over a window.</summary>
    public double MainTotalUs => _mainTotalNs / 1000.0;

    public long RefusedBy(ChatRefusal why) => Interlocked.Read(ref _refusedBy[(int)why]);

    /// <summary>The moments that came up for a bot, whether or not it wanted to speak.</summary>
    public long Triggers => Interlocked.Read(ref _triggers);

    /// <summary>The moments a bot's chance passed over.</summary>
    public long ChanceMissed => Interlocked.Read(ref _chanceMissed);

    /// <summary>The wishes given up for <paramref name="why"/>.</summary>
    public long LimitedBy(ChatLimit why) => Interlocked.Read(ref _limitedBy[(int)why]);

    /// <summary>The questions heard, the ones answered in a model's words and from the plain template, the model's lines refused, the shrugs, the markers sent and the questions nobody got to.</summary>
    public long Questions => Interlocked.Read(ref _questions);

    public long AnswersModel => Interlocked.Read(ref _answersModel);

    public long AnswersTemplate => Interlocked.Read(ref _answersTemplate);

    public long AnswersUnverified => Interlocked.Read(ref _answersUnverified);

    public long Shrugs => Interlocked.Read(ref _shrugs);

    public long Marks => Interlocked.Read(ref _marks);

    public long AnswersDropped => Interlocked.Read(ref _answersDropped);

    public void Question() => Interlocked.Increment(ref _questions);

    public void AnswerModel() => Interlocked.Increment(ref _answersModel);

    public void AnswerTemplate() => Interlocked.Increment(ref _answersTemplate);

    public void AnswerUnverified() => Interlocked.Increment(ref _answersUnverified);

    public void Shrug() => Interlocked.Increment(ref _shrugs);

    public void Mark() => Interlocked.Increment(ref _marks);

    public void AnswerDropped() => Interlocked.Increment(ref _answersDropped);

    public void Trigger(bool wanted)
    {
        Interlocked.Increment(ref _triggers);
        if (!wanted)
            Interlocked.Increment(ref _chanceMissed);
    }

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

    public void Limited(ChatLimit why)
    {
        Interlocked.Increment(ref _rateLimited);
        Interlocked.Increment(ref _limitedBy[(int)why]);
    }

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

    /// <summary>
    /// What one start's thread works from. A thread a stop gave up on still holds its own, so it can never take a job
    /// posted after the next start, nor answer into it.
    /// </summary>
    private sealed class Lane
    {
        public readonly ConcurrentQueue<ChatJob> Jobs = new();
        public readonly ConcurrentQueue<ChatResult> Results = new();
        public readonly SemaphoreSlim Ready = new(0);
        public int Queued;
    }

    private Lane? _lane;
    private CancellationTokenSource? _stop;
    private Thread? _thread;

    public bool Running => _thread is not null;

    /// <summary>Starts the thread; <paramref name="warm"/> is loaded first.</summary>
    public void Start(ChatTarget? warm = null)
    {
        if (_thread is not null)
            return;
        _stop = new CancellationTokenSource();
        CancellationToken stop = _stop.Token;
        Lane lane = _lane = new Lane();
        _thread = new Thread(() => Run(lane, stop, warm)) { Name = "ai-chat", IsBackground = true };
        _thread.Start();
    }

    /// <summary>Hands <paramref name="job"/> over without waiting; false when the worker is not running or is full (the line is dropped).</summary>
    public bool TryPost(ChatJob job)
    {
        if (_lane is not { } lane || Volatile.Read(ref lane.Queued) >= capacity)
            return false;
        Interlocked.Increment(ref lane.Queued);
        lane.Jobs.Enqueue(job);
        lane.Ready.Release();
        return true;
    }

    public bool TryTake(out ChatResult result)
    {
        if (_lane is { } lane)
            return lane.Results.TryDequeue(out result);
        result = default;
        return false;
    }

    /// <summary>Cancels what is in flight, joins the thread and forgets every job and result. Answers whether the thread ended.</summary>
    public bool Stop(int joinMilliseconds = JoinMilliseconds)
    {
        if (_thread is not { } thread)
            return true;
        _stop!.Cancel();
        bool ended = thread.Join(joinMilliseconds);
        // A thread still to end holds these: they are disposed only once none does.
        if (ended)
        {
            _stop.Dispose();
            _lane!.Ready.Dispose();
        }
        else
        {
            counters.Leak();
        }

        _thread = null;
        _stop = null;
        _lane = null;
        return ended;
    }

    public void Dispose()
    {
        Stop();
    }

    private void Run(Lane lane, CancellationToken stop, ChatTarget? warm)
    {
        if (warm is { } target)
            Warm(lane, target, stop);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                lane.Ready.Wait(stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!lane.Jobs.TryDequeue(out ChatJob? job))
                continue;
            ChatResult result = Ask(job, stop);
            if (!stop.IsCancellationRequested)
                lane.Results.Enqueue(result);
            Interlocked.Decrement(ref lane.Queued);
            counters.Finish();
        }
    }

    private void Warm(Lane lane, ChatTarget target, CancellationToken stop)
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
            lane.Results.Enqueue(new ChatResult(0, 0, null, ChatRefusal.None, reply.Failure, reply.Detail, false, 0f));
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
            return new ChatResult(job.Id, job.Slot, Plain(job), ChatRefusal.None, reply.Failure, reply.Detail, timedOut, seconds);
        }

        counters.Answer(seconds);
        // An answer is meant to say its fact back, so only the instructions count as a prompt to echo.
        ChatVerdict verdict = filter.Check(reply.Text, job.Fallback is null ? job.Prompt.Whole : job.Prompt.System, job.SelfName);
        if (verdict.Refusal != ChatRefusal.None)
            counters.Refuse(verdict.Refusal);
        if (job.Fallback is null)
            return new ChatResult(job.Id, job.Slot, verdict.Line, verdict.Refusal, ChatFailure.None, reply.Text, false, seconds);

        // An answer: the model's words only if they hold to the fact, else the fact.
        if (verdict.Line is { } line && job.Check?.Holds(line) == true)
        {
            counters.AnswerModel();
            return new ChatResult(job.Id, job.Slot, line, ChatRefusal.None, ChatFailure.None, reply.Text, false, seconds);
        }

        if (verdict.Refusal == ChatRefusal.None)
            counters.Refuse(ChatRefusal.Unverified);
        counters.AnswerUnverified();
        return new ChatResult(job.Id, job.Slot, Plain(job), verdict.Refusal == ChatRefusal.None ? ChatRefusal.Unverified : verdict.Refusal, ChatFailure.None, reply.Text, false, seconds);
    }

    /// <summary>An answer's plain line, through the filter like any other; null for a job with none.</summary>
    private string? Plain(ChatJob job)
    {
        if (job.Fallback is not { } plain)
            return null;
        counters.AnswerTemplate();
        return filter.Check(plain, "", job.SelfName).Line;
    }
}
