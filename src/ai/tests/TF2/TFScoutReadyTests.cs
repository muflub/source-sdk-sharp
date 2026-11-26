using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>The Scout is ready (C.4): the composition fields it by the class files' own ready flags.</summary>
public sealed class TFScoutReadyTests
{
    [Fact]
    public void TheScoutsFileSaysItIsReady()
        => Assert.True(TFClassSpecs.Ready(TFClass.Scout));

    /// <summary>A team of nine Soldiers gets a Scout for its tenth by the ready flags alone (the Scout furthest below its share).</summary>
    [Fact]
    public void ATeamOfNineSoldiersGetsAScout()
    {
        var counts = new int[(int)TFClass.CountAll];
        counts[(int)TFClass.Soldier] = 9;
        Assert.Equal(TFClass.Scout, TFClassComposition.Pick(counts, TFTeamStance.Symmetric, c => TFBotClassPick.ReadyBy(c, "")));
    }
}
