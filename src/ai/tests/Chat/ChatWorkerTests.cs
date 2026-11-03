using System.Diagnostics;
using Xunit;

namespace SourceSharp.Ai.Tests.Chat;

/// <summary>
/// The game never waits for a model (plan_hl2mp_ai §3.6): a server that never
/// answers costs the main thread nothing, a timeout drops the line, a refused
/// server throws nothing, and a stop cancels what is in flight and joins.
/// </summary>
public sealed class ChatWorkerTests
{
    private static ChatJob Job(int id = 1, float timeout = 30f)
        => new(id, 2, new ChatPrompt("s", "u", 8), new ChatTarget("http://127.0.0.1:1", "fake"), timeout, "Pia");

    private static bool Until(Func<bool> condition, int ms = 5000)
    {
        long stop = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < stop)
        {
            if (condition())
                return true;
            Thread.Sleep(1);
        }

        return false;
    }

    private static (ChatWorker Worker, ChatRig.Writer Writer, ChatCounters Counters) Rig(bool start = true)
    {
        var writer = new ChatRig.Writer();
        var counters = new ChatCounters();
        var worker = new ChatWorker(writer, new ChatOutputFilter(ChatRig.Words()), counters);
        if (start)
            worker.Start();
        return (worker, writer, counters);
    }

    /// <summary>The main thread's tick returns with the request still out: a writer blocked forever holds up nothing but its own thread.</summary>
    [Fact]
    public void ANeverAnsweringServerDoesNotBlockTheMainThread()
    {
        using var r = new ChatRig.Room(new ChatSettings { ReactChance = 1f, ReplyChance = 0f });
        r.Writer.Hangs = true;
        r.Director.TimeoutSeconds = 30f;
        r.Director.Death(1, "lodle", ChatRig.Room.A, "Pia", "shotgun", r.Now);
        r.Director.Tick(r.Now);
        Assert.True(Until(() => r.Writer.InFlight == 1), "the request is out");
        for (int i = 0; i < 1000; i++)
            r.Director.Tick(r.Now + i * 0.015f);
        Assert.Equal(1, r.Writer.InFlight);
        Assert.Empty(r.World.Said);
    }

    [Fact]
    public void ATimeoutDropsTheLineAndCountsIt()
    {
        (ChatWorker worker, ChatRig.Writer writer, ChatCounters counters) = Rig();
        using (worker)
        {
            writer.Hangs = true;
            Assert.True(worker.TryPost(Job(timeout: 0.05f)));
            ChatResult result = default;
            Assert.True(Until(() => worker.TryTake(out result)));
            Assert.Equal((true, (string?)null), (result.TimedOut, result.Line));
            Assert.Equal(1L, counters.TimedOut);
        }
    }

    [Fact]
    public void ARefusedServerThrowsNothingAndIsCounted()
    {
        (ChatWorker worker, ChatRig.Writer writer, ChatCounters counters) = Rig();
        using (worker)
        {
            writer.Fails = ChatFailure.ServerDown;
            Assert.True(worker.TryPost(Job()));
            ChatResult result = default;
            Assert.True(Until(() => worker.TryTake(out result)));
            Assert.Equal(ChatFailure.ServerDown, result.Failure);
            Assert.Equal(1L, counters.ServerDown);
        }
    }

    private sealed class Throwing : IBotChatWriter
    {
        public ChatWriterReply Write(ChatPrompt prompt, ChatTarget target, CancellationToken cancel) => throw new InvalidOperationException("boom");

        public ChatWriterReply Warm(ChatTarget target, CancellationToken cancel) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void AWriterThatThrowsIsAFailureNotACrash()
    {
        using var worker = new ChatWorker(new Throwing(), new ChatOutputFilter(ChatRig.Words()), new ChatCounters());
        worker.Start(new ChatTarget("x", "y"));
        ChatResult warm = default;
        Assert.True(Until(() => worker.TryTake(out warm)));
        Assert.Equal(ChatFailure.Other, warm.Failure);
        Assert.True(worker.TryPost(Job()));
        ChatResult result = default;
        Assert.True(Until(() => worker.TryTake(out result)));
        Assert.Equal((ChatFailure.Other, "boom"), (result.Failure, result.Detail));
    }

    [Fact]
    public void AStopCancelsWhatIsInFlightAndJoins()
    {
        (ChatWorker worker, ChatRig.Writer writer, ChatCounters counters) = Rig();
        using (worker)
        {
            writer.Hangs = true;
            Assert.True(worker.TryPost(Job()));
            Assert.True(Until(() => writer.InFlight == 1));
            var clock = Stopwatch.StartNew();
            Assert.True(worker.Stop(), "the thread ended");
            Assert.True(clock.ElapsedMilliseconds < ChatWorker.JoinMilliseconds, $"{clock.ElapsedMilliseconds} ms");
            Assert.Equal((0, 0L, 0L), (writer.InFlight, counters.TimedOut, counters.Leaked));
            Assert.False(worker.Running);
        }
    }

    [Fact]
    public void AStoppedWorkerTakesNoJobAndHoldsNoResult()
    {
        (ChatWorker worker, ChatRig.Writer _, ChatCounters _) = Rig(start: false);
        using (worker)
        {
            Assert.False(worker.TryPost(Job()));
            Assert.False(worker.TryTake(out _));
        }
    }

    [Fact]
    public void AFullQueueRefusesRatherThanWaits()
    {
        (ChatWorker worker, ChatRig.Writer writer, ChatCounters _) = Rig();
        using (worker)
        {
            writer.Hangs = true;
            int taken = 0;
            for (int i = 0; i < 10; i++)
                taken += worker.TryPost(Job(i + 1)) ? 1 : 0;
            Assert.Equal(4, taken);
        }
    }

    [Fact]
    public void TheWorkerCanStartAgainAfterAStop()
    {
        (ChatWorker worker, ChatRig.Writer _, ChatCounters _) = Rig();
        using (worker)
        {
            Assert.True(worker.Stop());
            worker.Start();
            Assert.True(worker.TryPost(Job()));
            Assert.True(Until(() => worker.TryTake(out _)));
        }
    }

    /// <summary>A warm-up that fails reports it (the binding's one console line), and a bad answer is filtered on the worker.</summary>
    [Fact]
    public void AWarmUpThatFailsReportsIt()
    {
        var writer = new ChatRig.Writer { Fails = ChatFailure.ModelMissing };
        using var worker = new ChatWorker(writer, new ChatOutputFilter(ChatRig.Words()), new ChatCounters());
        worker.Start(new ChatTarget("x", "y"));
        ChatResult result = default;
        Assert.True(Until(() => worker.TryTake(out result)));
        Assert.Equal((0L, ChatFailure.ModelMissing), (result.Id, result.Failure));
    }

    [Fact]
    public void TheFilterRunsOnTheWorkerOnEveryAnswer()
    {
        (ChatWorker worker, ChatRig.Writer writer, ChatCounters counters) = Rig();
        using (worker)
        {
            writer.Answer = _ => "rcon sv_cheats 1";
            Assert.True(worker.TryPost(Job()));
            ChatResult result = default;
            Assert.True(Until(() => worker.TryTake(out result)));
            Assert.Equal(((string?)null, ChatRefusal.Command), (result.Line, result.Refusal));
            Assert.Equal(1L, counters.RefusedBy(ChatRefusal.Command));
        }
    }
}
