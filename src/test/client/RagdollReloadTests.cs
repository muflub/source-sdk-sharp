using System;
using System.Collections.Generic;
using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_entity.md Phase 6 step 6e's second gate clause: A RAGDOLL SURVIVES A
/// HOT RELOAD.
///
/// <para>
/// <b>WHAT ACTUALLY BREAKS.</b> The NATIVE body is not unloaded with the
/// managed side — <c>ragdoll_library.cpp</c>'s id table is C++ memory and a
/// <c>cs_cl_reload</c> does not touch it. What a reload destroys is the managed
/// half's KNOWLEDGE of that body: whether it collapsed, what it collapsed from,
/// and how many attempts it took. Rebuilt with all of that at zero, the new
/// <see cref="ClientSsRagdoll"/> re-poses a body the solver already owns and
/// then reports "collapsed after 1 attempt" about a corpse that fell over a
/// minute ago.
/// </para>
///
/// <para>
/// <b>AND A COUNT CANNOT SEE IT.</b> One ragdoll before, one ragdoll after —
/// on a reload that destroyed the body and built a new one, on a reload that
/// carried it, and on a reload that carried nothing and left the entity
/// standing rigidly to attention. That is the same shape of blind spot the
/// strengthened reload gate found elsewhere, where 478 entities were saved and
/// <b>-1</b> restored with every count check green. So every fact here asserts
/// IDENTITY or STATE.
/// </para>
/// </summary>
public class RagdollReloadTests
{
    private const string Model = "models/humans/group03/male_01.mdl";
    private static readonly EntityHandle Body = new(99u);

    /// <summary>The registry a body looks its dead player up in. Empty here.</summary>
    private sealed class NoViewers : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live()
            => Array.Empty<ClientStandaloneEntity>();

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    /// <summary>
    /// A body that has already collapsed, with the solver holding it at
    /// <paramref name="where"/>.
    /// </summary>
    private static ClientSsRagdoll Collapsed(Vector where, out RecordingEngine engine,
                                             int sequence = 4)
    {
        engine = new RecordingEngine();

        engine.Returns("EntityRender.GetModelName", Model);
        engine.Returns("EntityRender.LookupSequenceByActivity", sequence);
        engine.Returns("EntityRender.BecomeRagdoll", true);
        engine.Returns("EntityRender.RagdollState", RagdollState.Present);
        engine.Returns("EntityRender.RagdollState.origin", where);
        engine.Returns("Entities.IsValid", true);

        var body = new ClientSsRagdoll(engine, new NoViewers(), new RecordingRagdolls(engine));

        body.BindEngine(engine, engine);
        body.State.BindLocal();
        body.State.PlayerIndex = -1;
        body.Handle = Body;

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        return body;
    }

    /// <summary>
    /// The reload, as the registry performs it: the OLD instance's state into a
    /// record, then a BRAND NEW instance of the class filled from it.
    ///
    /// <para>
    /// A NEW OBJECT, not the same one with its fields poked. That is the whole
    /// point of a reload — the type belongs to the assembly being unloaded — and
    /// a test that reused the instance would pass for a class that carried
    /// nothing at all.
    /// </para>
    /// </summary>
    private static ClientSsRagdoll Reload(ClientSsRagdoll before, RecordingEngine engine,
                                          RagdollState after, Vector afterOrigin)
    {
        var record = new SavedEntity(Body.Value, before.ClassName, string.Empty);

        before.SaveState(record);

        engine.Returns("EntityRender.RagdollState", after);
        engine.Returns("EntityRender.RagdollState.origin", afterOrigin);

        var rebuilt = new ClientSsRagdoll(engine, new NoViewers(), new RecordingRagdolls(engine));

        rebuilt.BindEngine(engine, engine);
        rebuilt.State.BindLocal();
        rebuilt.Handle = Body;
        rebuilt.LoadState(record);

        return rebuilt;
    }

    //-------------------------------------------------------------------------
    // The marked tier
    //-------------------------------------------------------------------------

    [Fact]
    public void ABodyThatCollapsedComesBackKnowingItCollapsed()
    {
        var before = Collapsed(new Vector(10f, 20f, 30f), out var engine);

        Assert.True(before.IsRagdoll);

        var after = Reload(before, engine, RagdollState.Present, new Vector(10f, 20f, 30f));

        Assert.True(after.IsRagdoll);
    }

    [Fact]
    public void AndTheSequenceItCollapsedFROM()
    {
        var before = Collapsed(new Vector(10f, 20f, 30f), out var engine, sequence: 11);

        Assert.Equal(11, before.PosedAs);

        var after = Reload(before, engine, RagdollState.Present, new Vector(10f, 20f, 30f));

        Assert.Equal(11, after.PosedAs);
    }

    [Fact]
    public void AndHowManyAttemptsItTook()
    {
        var before = Collapsed(new Vector(10f, 20f, 30f), out var engine);

        Assert.Equal(1, before.Attempts);

        var after = Reload(before, engine, RagdollState.Present, new Vector(10f, 20f, 30f));

        Assert.Equal(1, after.Attempts);
    }

    /// <summary>
    /// THE ONE A RE-POSE WOULD SHOW UP IN. A restored body whose
    /// <c>IsRagdoll</c> came back false takes the create path again on the next
    /// update: it re-poses the model, calls BecomeRagdoll, and reports one
    /// attempt about a body that is seconds old.
    /// </summary>
    [Fact]
    public void ARestoredBodyDoesNotCollapseItselfAgainOnTheNextUpdate()
    {
        var before = Collapsed(new Vector(10f, 20f, 30f), out var engine);
        var after = Reload(before, engine, RagdollState.Present, new Vector(10f, 20f, 30f));

        int posesBefore = engine.CountOf("EntityRender.PlaySequence");
        int createsBefore = engine.CountOf("EntityRender.BecomeRagdoll");

        after.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal(posesBefore, engine.CountOf("EntityRender.PlaySequence"));
        Assert.Equal(createsBefore, engine.CountOf("EntityRender.BecomeRagdoll"));
        Assert.Equal(1, after.Attempts);
    }

    //-------------------------------------------------------------------------
    // The custom tier: the pose, which is the part that says SAME BODY
    //-------------------------------------------------------------------------

    [Fact]
    public void TheSolversPoseIsCarriedAcrossTheReload()
    {
        var before = Collapsed(new Vector(10f, 20f, 30f), out var engine);
        var after = Reload(before, engine, RagdollState.Present, new Vector(10f, 20f, 30f));

        Assert.Equal(10f, after.PoseBeforeReload.X, 3);
        Assert.Equal(20f, after.PoseBeforeReload.Y, 3);
        Assert.Equal(30f, after.PoseBeforeReload.Z, 3);
    }

    [Fact]
    public void ABodyInTheSAMEPlaceReportsNoDrift()
    {
        var before = Collapsed(new Vector(10f, 20f, 30f), out var engine);
        var after = Reload(before, engine, RagdollState.Present, new Vector(10f, 20f, 30f));

        Assert.True(after.SurvivedReload);
        Assert.Equal(0f, after.PoseDriftAcrossReload, 3);
    }

    /// <summary>
    /// A SETTLING BODY MOVES, and the drift is REPORTED rather than judged: how
    /// far a body travels in the time a reload takes is the map's business, not
    /// this class's. What the number is for is telling a body that carried on
    /// falling from one that was rebuilt at the entity's networked origin, which
    /// for a corpse is where it was killed and not where it fell.
    /// </summary>
    [Fact]
    public void ABodyThatMovedReportsTheDistanceAndStillCountsAsSurviving()
    {
        var before = Collapsed(new Vector(0f, 0f, 100f), out var engine);
        var after = Reload(before, engine, RagdollState.Present, new Vector(0f, 0f, 97f));

        Assert.True(after.SurvivedReload);
        Assert.Equal(3f, after.PoseDriftAcrossReload, 3);
    }

    /// <summary>
    /// THE FAILURE THIS EXISTS TO MAKE VISIBLE. The native table is not unloaded
    /// with the managed side, so a body that is gone AFTER a reload was
    /// destroyed rather than mislaid — and every count-based check passes.
    /// </summary>
    [Fact]
    public void ABodyThatDidNotSurviveSaysSoRatherThanReportingZeroDrift()
    {
        var before = Collapsed(new Vector(10f, 20f, 30f), out var engine);
        var after = Reload(before, engine, RagdollState.None, Vector.Zero);

        Assert.False(after.SurvivedReload);

        // NOT ZERO. Zero drift is what a body in exactly the same place reports,
        // so "no body at all" answering zero would be indistinguishable from the
        // best possible outcome.
        Assert.Equal(-1f, after.PoseDriftAcrossReload, 3);
    }

    [Fact]
    public void ABodyThatNeverCollapsedCarriesNoSurvivalClaim()
    {
        var engine = new RecordingEngine();

        engine.Returns("EntityRender.GetModelName", Model);
        engine.Returns("EntityRender.LookupSequenceByActivity", 4);
        engine.Returns("EntityRender.BecomeRagdoll", false);   // no jointed body in the .phy
        engine.Returns("EntityRender.RagdollState", RagdollState.None);
        engine.Returns("EntityRender.RagdollState.origin", Vector.Zero);

        var before = new ClientSsRagdoll(engine, new NoViewers(), new RecordingRagdolls(engine));

        before.BindEngine(engine, engine);
        before.State.BindLocal();
        before.State.PlayerIndex = -1;
        before.Handle = Body;
        before.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.False(before.IsRagdoll);

        var after = Reload(before, engine, RagdollState.None, Vector.Zero);

        Assert.False(after.IsRagdoll);
        Assert.False(after.SurvivedReload);
    }

    /// <summary>
    /// AND IT RETRIES, which is the behaviour the class already had and which
    /// carrying <c>IsRagdoll</c> must not break: a body that has NOT collapsed
    /// asks again on the next update, because a model can arrive after the
    /// entity does.
    /// </summary>
    [Fact]
    public void ABodyThatHasNotCollapsedStillRetriesAfterAReload()
    {
        var engine = new RecordingEngine();

        engine.Returns("EntityRender.GetModelName", Model);
        engine.Returns("EntityRender.LookupSequenceByActivity", 4);
        engine.Returns("EntityRender.BecomeRagdoll", false);
        engine.Returns("EntityRender.RagdollState", RagdollState.None);
        engine.Returns("EntityRender.RagdollState.origin", Vector.Zero);

        var before = new ClientSsRagdoll(engine, new NoViewers(), new RecordingRagdolls(engine));

        before.BindEngine(engine, engine);
        before.State.BindLocal();
        before.State.PlayerIndex = -1;
        before.Handle = Body;
        before.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        var after = Reload(before, engine, RagdollState.None, Vector.Zero);

        int createsBefore = engine.CountOf("EntityRender.BecomeRagdoll");

        after.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal(createsBefore + 1, engine.CountOf("EntityRender.BecomeRagdoll"));
        Assert.Equal(2, after.Attempts);
    }

    //-------------------------------------------------------------------------
    // What the record actually holds — so a fact above cannot pass on a class
    // that carried nothing and happened to recompute the same answer.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheRecordCarriesTheFourMarkedKeysByName()
    {
        var before = Collapsed(new Vector(10f, 20f, 30f), out _);
        var record = new SavedEntity(Body.Value, before.ClassName, string.Empty);

        before.SaveState(record);

        Assert.True(record.Has(nameof(ClientSsRagdoll.IsRagdoll)));
        Assert.True(record.Has(nameof(ClientSsRagdoll.Attempts)));
        Assert.True(record.Has(nameof(ClientSsRagdoll.PosedAs)));
        Assert.True(record.Has(nameof(ClientSsRagdoll.PosedFromThePlayer)));
    }

    [Fact]
    public void AndTheCustomRecordIsCarriedUnderTheSharedPrefix()
    {
        var before = Collapsed(new Vector(10f, 20f, 30f), out _);
        var record = new SavedEntity(Body.Value, before.ClassName, string.Empty);

        before.SaveState(record);

        // THE SAME PREFIX THE SERVER'S WALK USES, because a record written by
        // one walk has to be readable by the other.
        Assert.Contains(record.Local.Keys, k => k.StartsWith(SaveTiers.CustomPrefix, StringComparison.Ordinal));
    }
}
