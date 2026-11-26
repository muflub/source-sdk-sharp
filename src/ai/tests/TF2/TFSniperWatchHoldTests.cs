using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

using static SourceSharp.Ai.Tests.TF2.TFSniperFixture;

/// <summary>
/// The Sniper's watch held over a flip of the points' goal, and its spots kept per watch (the code review's C6).
/// </summary>
public sealed class TFSniperWatchHoldTests
{
    /// <summary>
    /// An enemy seen on an own point flips the points' goal (to defend it) and back: the watch is held, so no new spot search and
    /// no spot dropped. Red attacks blue's B from its own A; a blue Scout runs past A for two decides.
    /// </summary>
    [Fact]
    public void AnEnemyCrossingAnOwnPointDoesNotDropTheSpot()
    {
        ObjectiveKnowledge level = Points(Red, 1 << Blue, ObjectiveState.Open, Blue, 1 << Red, ObjectiveState.Open);
        TFField f = Field(mates: [3]);
        Walk(f, 3, _ => level, new AiVector(600f, 256f, 0f));
        AiVector before = f.Decider.Doing.Goal;
        long searches = Counter(f, "sniper.spot_searches");
        ActorFacts scout = TFSniperPlayTests.Drawn(A, PointA + new AiVector(100f, 0f, 0f), TFClass.Scout);
        Walk(f, 2, _ => level, new AiVector(600f, 256f, 0f), seen: _ => [scout]);
        Walk(f, 2, _ => level, new AiVector(600f, 256f, 0f));
        Assert.Equal((searches, before), (Counter(f, "sniper.spot_searches"), f.Decider.Doing.Goal));
    }
}
