using SourceSharp.Abi;
using Debug.Api;
using SourceSharp.Server;
using SourceSharp.Server.Interface;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// WHAT <c>plan_entity.md</c> STEP 4c DECIDED ABOUT OVERLAYS, AND THE ONE TRAP
/// THAT MAKES AN OVERLAY DIFFERENT FROM EVERY OTHER DRAW.
///
/// <para>
/// The step's gate is "overlays draw for a handle", and a handle is exactly
/// where this goes wrong quietly. <c>EntityHandle.None</c> is
/// <c>INVALID_EHANDLE_INDEX</c>; <c>default(EntityHandle)</c> is entry 0 serial
/// 0, which is a REAL SLOT — worldspawn. A caller that means "nothing" and
/// writes <c>default</c> therefore draws an overlay on the world and SEES
/// SOMETHING, which is indistinguishable from working. Two lanes have already
/// been caught by the same shape (see the memory note
/// <c>default-handle-is-entry-zero</c>), so the distinction is pinned here
/// rather than left to the reader.
/// </para>
///
/// <para>
/// The rest are the refusal entries. Step 4c deleted two behaviours — the AI
/// node editor's overlays and <c>NDebugOverlay::Text</c>'s obstruction check —
/// and §7's rule is that a deleted behaviour is a
/// <see cref="ContractBaseline.Refusals"/> entry with a reason and never a
/// silent no-op. These facts are what stop those entries rotting into
/// decoration; they are deliberately NOT a test of the overlay code, which is
/// pinned by <c>sourcesharp/test/overlay_library_test.cpp</c> on the native leg
/// because it is arithmetic with no managed side to it.
/// </para>
/// </summary>
public class OverlayContractTests
{
    //-------------------------------------------------------------------------
    // THE HANDLE
    //-------------------------------------------------------------------------

    [Fact]
    public void TheEmptyHandleIsTheInvalidSentinelAndNotZero()
    {
        Assert.Equal(uint.MaxValue, EntityHandle.None.Value);
    }

    [Fact]
    public void TheRawZeroHandleIsNotTheEmptyHandle()
    {
        // Not a curiosity: entry 0 serial 0 can be worldspawn, so it names
        // something that exists. This is why DrawEntityOverlay refuses None and
        // deliberately does NOT refuse entry zero. (default(EntityHandle) IS
        // None since 2026-10-05: EntityHandleInversionTests.)
        Assert.NotEqual(EntityHandle.None, new EntityHandle(0u));
    }

    //-------------------------------------------------------------------------
    // THE DRAW, AGAINST THE RECORDING ENGINE
    //-------------------------------------------------------------------------

    [Fact]
    public void DrawingAnOverlayForAHandleReachesTheEngine()
    {
        var fake = new RecordingEngine().Returns("Hud.DrawEntityOverlay", true);
        var hud = (IEngineHud)fake;

        Assert.True(hud.DrawEntityOverlay(new EntityHandle(0x0001_0001u), 0, "crate", 1.0f));
        Assert.True(fake.Called("Hud.DrawEntityOverlay"));
    }

    [Fact]
    public void TheDrawCarriesTheHandleItWasGiven()
    {
        var fake = new RecordingEngine().Returns("Hud.DrawEntityOverlay", true);
        var hud = (IEngineHud)fake;
        var handle = new EntityHandle(0x0002_0003u);

        hud.DrawEntityOverlay(handle, 2, "crate", 1.0f);

        Assert.True(fake.Last("Hud.DrawEntityOverlay") is { } call
                 && call.ArgAs<EntityHandle>(0) == handle);
    }

    // A null text is legal and means "name yourself" -- the argument still has
    // to reach the boundary rather than being dropped on this side.
    [Fact]
    public void ANullTextIsPassedThroughAsEmpty()
    {
        var fake = new RecordingEngine().Returns("Hud.DrawEntityOverlay", true);
        var hud = (IEngineHud)fake;

        hud.DrawEntityOverlay(new EntityHandle(0x0001_0001u), 0, null, 1.0f);

        Assert.True(fake.Last("Hud.DrawEntityOverlay") is { } call
                 && call.ArgAs<string>(2) == string.Empty);
    }

    //-------------------------------------------------------------------------
    // THE READOUT
    //-------------------------------------------------------------------------

    [Fact]
    public void TheReadoutSeparatesNothingAskedFromNothingDrawn()
    {
        var fake = new RecordingEngine();
        var hud = (IEngineHud)fake;

        // Nobody looking: no viewer, and nothing counted anywhere.
        fake.Returns("Hud.OverlayReadout", (0u, 0u, 0u, 0u, false));
        var quiet = hud.OverlayReadout();

        // The regression: draws were attempted and every one of them found no
        // viewer. Same zero in `drawn`, and a completely different fact.
        fake.Returns("Hud.OverlayReadout", (0u, 0u, 17u, 0u, false));
        var broken = hud.OverlayReadout();

        Assert.Equal(quiet.Drawn, broken.Drawn);
        Assert.NotEqual(quiet.NoViewer, broken.NoViewer);
    }

    [Fact]
    public void TheReadoutReportsTheViewerFlag()
    {
        var fake = new RecordingEngine().Returns("Hud.OverlayReadout", (5u, 1u, 0u, 0u, true));

        Assert.True(((IEngineHud)fake).OverlayReadout().ViewerSet);
    }

    [Fact]
    public void ResettingTheCountersIsItsOwnCall()
    {
        var fake = new RecordingEngine();

        ((IEngineHud)fake).OverlayResetCounts();

        Assert.True(fake.Called("Hud.OverlayResetCounts"));
    }

    //-------------------------------------------------------------------------
    // THE TWO REFUSALS
    //-------------------------------------------------------------------------

    [Fact]
    public void TheAiNodeEditorOverlayRefusalIsRecorded()
    {
        Assert.Contains(
            ContractBaseline.Refusals,
            r => r.Subject.Contains("UTIL_DrawPositioningOverlay"));
    }

    [Fact]
    public void TheAiNodeEditorRefusalSaysTheBlockDereferencedNull()
    {
        var refusal = Assert.Single(
            ContractBaseline.Refusals,
            r => r.Subject.Contains("UTIL_DrawPositioningOverlay"));

        // The mechanism and not the conclusion. "unused" would excuse nothing;
        // "dereferenced NULL" is the fact that makes deleting it right.
        Assert.Contains("DEREFERENCED NULL", refusal.Reason);
    }

    [Fact]
    public void TheAiNodeEditorRefusalNamesWhatReplacesIt()
    {
        var refusal = Assert.Single(
            ContractBaseline.Refusals,
            r => r.Subject.Contains("UTIL_DrawPositioningOverlay"));

        // A refusal that did not say what you use instead is a dead end rather
        // than a decision.
        Assert.Contains("Engine.DrawEntityOverlay", refusal.Reason);
    }

    [Fact]
    public void TheAiNodeEditorRefusalCitesFamilySix()
    {
        var refusal = Assert.Single(
            ContractBaseline.Refusals,
            r => r.Subject.Contains("UTIL_DrawPositioningOverlay"));

        Assert.Contains("FAMILY 6", refusal.Reason);
    }

    [Fact]
    public void TheTextObstructionRefusalIsRecorded()
    {
        Assert.Contains(
            ContractBaseline.Refusals,
            r => r.Subject.Contains("bViewCheck"));
    }

    [Fact]
    public void TheTextRefusalSaysTheWholeEntryIsAbsentAndWhyThatIsNotASilentNoOp()
    {
        var refusal = Assert.Single(
            ContractBaseline.Refusals,
            r => r.Subject.Contains("bViewCheck"));

        // Since the server gc cull the whole entry point is absent, not just the
        // obstruction arm. The reason must say so, and must say why that is not
        // the silent no-op §7 forbids: nothing calls it.
        Assert.Contains("REFUSED AS A WHOLE", refusal.Reason);
        Assert.Contains("nothing calls it", refusal.Reason);
    }

    [Fact]
    public void BothOverlayRefusalsCiteTheStepThatLandedThem()
    {
        foreach (var refusal in ContractBaseline.Refusals)
        {
            if (!refusal.Subject.Contains("UTIL_DrawPositioningOverlay")
             && !refusal.Subject.Contains("bViewCheck"))
                continue;

            Assert.Contains("step 4c", refusal.Citation);
        }
    }

    //-------------------------------------------------------------------------
    // THE READOUT ON `debug-api status`
    //
    // Where the live gate reads it. Routed directly rather than through a
    // socket, exactly as CrossingsStatusTests drives every other route.
    //-------------------------------------------------------------------------

    [Fact]
    public void StatusCarriesTheOverlayReadout()
    {
        Assert.Contains("\"overlay\":", Status(null));
    }

    [Fact]
    public void StatusCarriesTheOverlayWorkAndAllThreeReasons()
    {
        string body = Status(() => (7u, 3u, 2u, 1u, true));

        Assert.Contains("\"drawn\":7", body);
        Assert.Contains("\"clipped\":3", body);
        Assert.Contains("\"noViewer\":2", body);
        Assert.Contains("\"noOverlay\":1", body);
    }

    [Fact]
    public void StatusCarriesTheViewerFlagAsABoolean()
    {
        Assert.Contains("\"viewerSet\":true", Status(() => (0u, 0u, 0u, 0u, true)));
        Assert.Contains("\"viewerSet\":false", Status(() => (0u, 0u, 0u, 0u, false)));
    }

    /// <summary>
    /// The whole reason the readout is four numbers and not one. A build that
    /// simply stopped drawing reports drawn:0 -- and so does a frame nobody
    /// asked for an overlay on. The two are told apart by noViewer, so both
    /// bodies are built here and compared.
    /// </summary>
    [Fact]
    public void StatusTellsNobodyAskedApartFromNothingDrew()
    {
        string quiet  = Status(() => (0u, 0u, 0u, 0u, false));
        string broken = Status(() => (0u, 0u, 41u, 0u, false));

        Assert.Contains("\"drawn\":0", quiet);
        Assert.Contains("\"drawn\":0", broken);
        Assert.NotEqual(quiet, broken);
        Assert.Contains("\"noViewer\":41", broken);
    }

    /// <summary>
    /// With no report supplied at all the object is still there and reads zero,
    /// so a client can index it unconditionally -- the rule every other report
    /// on this route follows.
    /// </summary>
    [Fact]
    public void StatusCarriesAZeroedOverlayWhenNothingSuppliesOne()
    {
        Assert.Contains("\"overlay\":{\"drawn\":0,\"clipped\":0,\"noViewer\":0,\"noOverlay\":0,\"viewerSet\":false}",
                        Status(null));
    }

    private static string Status(
        Func<(uint Drawn, uint Clipped, uint NoViewer, uint NoOverlay, bool ViewerSet)>? report)
    {
        // G7: the readout reaches the dev API as the server's IDebugStatus counters.
        var server = NeverPumped.Server(TestDebugHooks.Server(counters: () =>
        {
            if (report is null)
                return DebugStatus.ZeroCounters();
            var r = report();
            return DebugStatus.ZeroCounters() with { Overlay = new DebugOverlayCounts(r.Drawn, r.Clipped, r.NoViewer, r.NoOverlay, r.ViewerSet) };
        }));
        HttpSocketServer.TryParse("GET /status HTTP/1.1\r\n\r\n", out var request);

        return server.Route(request).Body;
    }
}
