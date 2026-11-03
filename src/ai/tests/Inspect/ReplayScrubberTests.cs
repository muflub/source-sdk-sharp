using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Trees;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Inspect;

/// <summary>
/// The record/replay scrubber (plan_hl2mp_ai §7.1, Phase 6 step 3): a
/// recording's brains stood at any tick, the bot's inspector record there with
/// its recorded and replayed intents. The frame is a golden JSON
/// (<c>AI_GOLDEN_WRITE=1</c> rewrites it); scrubbing back and forth gives the
/// same frame at the same tick.
/// </summary>
public sealed class ReplayScrubberTests
{
    private static string Golden => RepoLayout.Repo("src", "ai", "tests", "goldens", "ai-replay-frame.json");

    private sealed class NoNav : IHl2mpNavigation
    {
        public LoadedNav? Current => null;

        public string? Refusal => "none";

        public NavObstacleState? Obstacles => null;
        public NavObstacleState? ObstaclesFor(SourceSharp.Abi.EntityHandle player) => Obstacles;

        public NavSight? Sight => null;


        public ChokeSlot? Chokes => null;
    }

    private sealed class Log : SourceSharp.Abi.ILog
    {
        public void Msg(string message) { }

        public void Warning(string message) { }
    }

    private static readonly Lazy<(byte[] Recording, MemoryWorld World)> Sample = new(ThreadDeterminismTests.Make);

    private static AiReplayCursor Cursor()
        => new(Sample.Value.Recording, _ => (Sample.Value.World.Graph, null), setup => new Hl2mpDecider(setup));

    private static string Frame(AiReplayCursor c, int tick, int slot = 1)
    {
        c.SeekTo(new Tick(tick));
        return new AiReplayRoutes(new NoNav(), new Log()).Record(c, slot);
    }

    [Fact]
    public void TheFrameAtATickIsTheGolden()
    {
        string json = Frame(Cursor(), 61);
        Assert.Contains("\"same\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"tick\":61", json, StringComparison.Ordinal);
        if (Environment.GetEnvironmentVariable("AI_GOLDEN_WRITE") == "1")
            File.WriteAllText(Golden, json);
        Assert.True(File.Exists(Golden), $"no golden at {Golden}: AI_GOLDEN_WRITE=1 writes it");
        Assert.Equal(File.ReadAllText(Golden), json);
    }

    [Fact]
    public void ScrubbingBackAndForthGivesTheSameFrame()
    {
        AiReplayCursor c = Cursor();
        string first = Frame(c, 61);
        string later = Frame(c, 200);
        string again = Frame(c, 61);
        Assert.NotEqual(first, later);
        Assert.Equal(first, again);
    }

    [Fact]
    public void EveryDecideOfTheSampleIsTheRecordingsIntent()
    {
        AiReplayCursor c = Cursor();
        c.SeekTo(c.Ticks[^1]);
        int compared = 0;
        foreach (Tick t in c.Ticks)
        {
            if (c.TryRecorded(1, t, out IntentCommand recorded))
            {
                Assert.True(c.TryReplayed(1, t, out IntentCommand replayed), $"no replayed intent at {t}");
                Assert.Equal(recorded, replayed);
                compared++;
            }
        }

        Assert.True(compared > 40, $"the control: {compared} decides compared");
    }
}
