using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed end of <c>plan_entity.md</c> 5b's stage entry, DRIVEN rather
/// than read.
///
/// <para>
/// <see cref="ClientFrameStageTests"/> audits the wiring from the source
/// because the thunks cannot be entered from managed code. This class audits
/// nothing: <see cref="ClientModule.OnStage"/> is an ordinary method, so it is
/// called and its effect observed. Between them the two cover the whole path —
/// the thunk reaches <c>OnStage</c> (audited) and <c>OnStage</c> does the right
/// thing with what it is given (driven).
/// </para>
///
/// <para>
/// <b>Each fact has a module of its own, and so counters of its own.</b> The
/// current stage and the per-stage counters were statics on
/// <c>ClientModule</c> (so a command could read them without a module
/// reference), which made every fact here share one set and run serially. They
/// are the module's <see cref="IClientFrameStats"/> now.
/// </para>
/// </summary>
public class ClientStageEntryTests
{
    private static ClientModule Fresh() => new(new ClientModuleEngine(TestSides.Create(Side.Client)));

    [Fact]
    public void BeforeAnyStageTheCurrentStageIsUndefined()
    {
        var module = Fresh();

        Assert.Equal(ClientFrameStage.Undefined, module.Stats.CurrentStage);
    }

    [Fact]
    public void OnStageRecordsTheStage()
    {
        var module = Fresh();

        module.OnStage(ClientFrameStage.RenderStart);

        Assert.Equal(ClientFrameStage.RenderStart, module.Stats.CurrentStage);
    }

    [Fact]
    public void TheLastStageWins()
    {
        var module = Fresh();

        module.OnStage(ClientFrameStage.NetUpdateStart);
        module.OnStage(ClientFrameStage.NetUpdateEnd);

        Assert.Equal(ClientFrameStage.NetUpdateEnd, module.Stats.CurrentStage);
    }

    [Fact]
    public void OnStageCountsTheStageItWasGiven()
    {
        var module = Fresh();

        module.OnStage(ClientFrameStage.RenderEnd);
        module.OnStage(ClientFrameStage.RenderEnd);
        module.OnStage(ClientFrameStage.RenderEnd);

        Assert.Equal(3, module.Stats.StageCount(ClientFrameStage.RenderEnd));
    }

    [Fact]
    public void OnStageDoesNotCountAnyOtherStage()
    {
        var module = Fresh();

        module.OnStage(ClientFrameStage.RenderEnd);

        Assert.Equal(0, module.Stats.StageCount(ClientFrameStage.RenderStart));
        Assert.Equal(0, module.Stats.StageCount(ClientFrameStage.Start));
        Assert.Equal(0, module.Stats.StageCount(ClientFrameStage.NetUpdateStart));
    }

    /// <summary>
    /// One pass of a whole engine frame leaves one count on each of the seven
    /// stages — which is the shape "one crossing per stage" produces on this
    /// side, and the shape a stage accidentally counted twice would break.
    /// </summary>
    [Fact]
    public void OneFrameOfStagesCountsEachStageOnce()
    {
        var module = Fresh();

        foreach (ClientFrameStage stage in new[]
                 {
                     ClientFrameStage.Start,
                     ClientFrameStage.NetUpdateStart,
                     ClientFrameStage.NetUpdatePostDataUpdateStart,
                     ClientFrameStage.NetUpdatePostDataUpdateEnd,
                     ClientFrameStage.NetUpdateEnd,
                     ClientFrameStage.RenderStart,
                     ClientFrameStage.RenderEnd,
                 })
        {
            module.OnStage(stage);
        }

        for (int i = 0; i <= (int)ClientFrameStage.RenderEnd; i++)
            Assert.Equal(1, module.Stats.StageCount((ClientFrameStage)i));
    }

    /// <summary>
    /// <see cref="ClientFrameStage.Undefined"/> is -1 and has no counter cell.
    /// It must be survivable rather than an index out of range: the engine does
    /// not raise it as a notification, but the value exists and a shell handing
    /// it through must not take the client down.
    /// </summary>
    [Fact]
    public void UndefinedIsSurvivableAndCountsNothing()
    {
        var module = Fresh();

        module.OnStage(ClientFrameStage.Undefined);

        Assert.Equal(ClientFrameStage.Undefined, module.Stats.CurrentStage);
        Assert.Equal(0, module.Stats.StageCount(ClientFrameStage.Undefined));
    }

    /// <summary>
    /// A stage number the mirror does not know — an engine that grew an eighth
    /// one — is recorded and counted nowhere, rather than throwing. An exception
    /// here would unwind into the engine's C++ frames.
    /// </summary>
    [Fact]
    public void AnUnknownStageIsSurvivable()
    {
        var module = Fresh();

        module.OnStage((ClientFrameStage)99);

        Assert.Equal((ClientFrameStage)99, module.Stats.CurrentStage);
        Assert.Equal(0, module.Stats.StageCount((ClientFrameStage)99));
    }

    [Fact]
    public void ResetStagesZeroesTheCounts()
    {
        var stats = new ClientFrameStats();
        stats.NoteStage(ClientFrameStage.RenderStart);
        Assert.Equal(1, stats.StageCount(ClientFrameStage.RenderStart));

        stats.ResetStages();

        Assert.Equal(0, stats.StageCount(ClientFrameStage.RenderStart));
        Assert.Equal(ClientFrameStage.Undefined, stats.CurrentStage);
    }

    [Fact]
    public void TwoModulesDoNotShareStageCounts()
    {
        // The property the statics lacked: one module's stages are not
        // another's.
        var mine = Fresh();
        var theirs = Fresh();

        theirs.OnStage(ClientFrameStage.RenderStart);

        Assert.Equal(0, mine.Stats.StageCount(ClientFrameStage.RenderStart));
        Assert.Equal(ClientFrameStage.Undefined, mine.Stats.CurrentStage);
        Assert.Equal(1, theirs.Stats.StageCount(ClientFrameStage.RenderStart));
    }

    /// <summary>
    /// <c>ClientModule</c> satisfies <c>IClientModule</c> including the new
    /// member — which is what makes the thunk's <c>s_ClientGame?.OnStage(…)</c>
    /// resolve at runtime rather than finding a null interface reference.
    /// </summary>
    [Fact]
    public void ClientModuleImplementsOnStageThroughTheInterface()
    {
        var concrete = Fresh();
        IClientModule module = concrete;

        module.OnStage(ClientFrameStage.NetUpdateEnd);

        Assert.Equal(ClientFrameStage.NetUpdateEnd, concrete.Stats.CurrentStage);
    }
}
