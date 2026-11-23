using SourceSharp.Ai.Replay;
using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// <c>ai-replay searches</c> (<see cref="SearchStretchTool"/>, lane Gmove) on synthetic streams: a
/// stretch runs from the first intent under the search leaf to the first under another, a death ends
/// one, and the sounds heard within it are its own.
/// </summary>
public sealed class SearchStretchTests
{
    private static readonly ActorId Bot = new(3, 7), Enemy = new(5, 2);

    private static readonly AiDecisionTree Tree = new(["root", "roam", SearchStretchTool.Leaf], [0, 1, 2]);

    private static IntentCommand Intent(int tick, int node)
        => new(Bot, new Tick(tick), tick, new Tick(tick + 16), MoveIntent.Hold, LookIntent.AlongPath, ActionIntent.None, new DecisionReason(node, 0f, 0f, 0f));

    /// <summary>Intents every 8 ticks from 0 to 2400: roam, search from <paramref name="from"/> to <paramref name="to"/>, roam again.</summary>
    private static List<SearchStretch> Run(int from, int to, List<int>? deaths = null, List<AiEvent>? heard = null)
    {
        var intents = new List<IntentCommand>();
        for (int t = 0; t < 2400; t += 8)
            intents.Add(Intent(t, t >= from && t < to ? 2 : 1));
        return SearchStretchTool.From(Tree, new Dictionary<ActorId, List<IntentCommand>> { [Bot] = intents }, new Dictionary<ActorId, SortedList<int, AiActorFrame>>(),
                                      new Dictionary<ActorId, List<AiEvent>> { [Bot] = heard ?? [] }, new Dictionary<ActorId, List<int>> { [Bot] = deaths ?? [] },
                                      new Dictionary<ActorId, int> { [Bot] = 3 });
    }

    [Fact]
    public void AStretchRunsFromItsFirstSearchToTheNextLeaf()
    {
        SearchStretch s = Assert.Single(Run(400, 1800));

        Assert.Equal((400, 1800, 3), (s.From, s.To, s.Team));
        Assert.True(s.Seconds > SearchStretchTool.LongSeconds, $"{s.Seconds} s");
    }

    [Fact]
    public void ADeathEndsAStretchAndTheNextLifeStartsAnother()
    {
        List<SearchStretch> stretches = Run(800, 1800, deaths: [1200]);

        Assert.Equal([(800, 1200), (1200, 1800)], stretches.Select(s => (s.From, s.To)).Order());
    }

    [Fact]
    public void TheSoundsHeardWithinAStretchAreItsOwn()
    {
        var heard = new List<AiEvent>
        {
            AiEvent.SoundHeard(Bot, new Tick(100), SoundKind.Door, default, 1f, Enemy, -1),
            AiEvent.SoundHeard(Bot, new Tick(1000), SoundKind.Door, default, 1f, Enemy, -1),
            AiEvent.SoundHeard(Bot, new Tick(2000), SoundKind.Door, default, 1f, Enemy, -1),
        };

        SearchStretch s = Assert.Single(Run(800, 1800, heard: heard));

        Assert.Equal(1000, Assert.Single(s.Heard).Tick.Value);
    }
}
