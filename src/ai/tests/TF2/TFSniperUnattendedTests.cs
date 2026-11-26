using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

using static SourceSharp.Ai.Tests.TF2.TFSniperFixture;

/// <summary>
/// The Sniper's class branch gives way to a point nobody else is on (the code review's C5).
/// </summary>
public sealed class TFSniperUnattendedTests
{
    /// <summary>
    /// Red's own point being taken with no teammate the bot knows of near it: the Sniper goes onto the point, not to a spot.
    /// The control: the same point not being taken, it goes to a spot.
    /// </summary>
    [Fact]
    public void AnOwnPointBeingTakenWithNobodyOnItTakesTheSniperOntoIt()
    {
        ObjectiveKnowledge taken = Points(Red, 1 << Blue, ObjectiveState.Capturing, Red, 0, ObjectiveState.Locked);
        ObjectiveKnowledge open = Points(Red, 1 << Blue, ObjectiveState.Open, Red, 0, ObjectiveState.Locked);
        TFField control = Field(mates: [3]);
        Walk(control, 3, _ => open, new AiVector(600f, 256f, 0f));
        Assert.Contains(control.Decider.Doing.Verdict, new[] { "to a sniper spot", "holding a sight line" });

        TFField f = Field(mates: [3]);
        Walk(f, 3, _ => taken, new AiVector(600f, 256f, 0f));
        Assert.True(f.Decider.Doing.Goal.DistanceTo(PointA) < 300f, $"{f.Decider.Doing.Verdict} to {f.Decider.Doing.Goal}");
        Assert.True(Counter(f, "sniper.yielded_decides") > 0);
    }
}
