using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two ragdoll policies that left <c>game/shared/ragdoll_shared.cpp</c>
/// (lane p4-anim). One behaviour per fact.
/// </summary>
public class RagdollViolencePolicyTests
{
    [Fact]
    public void AllFourConVarsOnIsNotLowViolenceByItself()
        => Assert.False(RagdollViolencePolicy.AnyLegacyConVarOff(true, true, true, true));

    [Fact]
    public void HumanBloodOffIsLowViolence()
        => Assert.True(RagdollViolencePolicy.AnyLegacyConVarOff(false, true, true, true));

    [Fact]
    public void AlienBloodOffIsLowViolence()
        => Assert.True(RagdollViolencePolicy.AnyLegacyConVarOff(true, false, true, true));

    [Fact]
    public void HumanGibsOffIsLowViolence()
        => Assert.True(RagdollViolencePolicy.AnyLegacyConVarOff(true, true, false, true));

    [Fact]
    public void AlienGibsOffIsLowViolence()
        => Assert.True(RagdollViolencePolicy.AnyLegacyConVarOff(true, true, true, false));

    [Fact]
    public void TheEngineAloneCanSayLowViolenceWithEveryConVarOn()
        => Assert.True(RagdollViolencePolicy.IsLowViolence(true, true, true, true, engineLowViolence: true));

    [Fact]
    public void TheEngineSayingNoDoesNotOverrideAConVarSayingYes()
        => Assert.True(RagdollViolencePolicy.IsLowViolence(false, true, true, true, engineLowViolence: false));

    // ---- SetLowViolence, whole -------------------------------------------

    [Fact]
    public void TheServerDoesNotWorryAboutLowViolenceInMultiplayer()
        => Assert.False(RagdollViolencePolicy.LowViolenceForMap(
            baseLowViolence: true, isServer: true, isMultiplayer: true,
            episodic: false, mapName: "dm_lockdown"));

    [Fact]
    public void TheCLIENTStillHonoursLowViolenceInMultiplayer()
        => Assert.True(RagdollViolencePolicy.LowViolenceForMap(
            baseLowViolence: true, isServer: false, isMultiplayer: true,
            episodic: false, mapName: "dm_lockdown"));

    [Fact]
    public void SingleplayerKeepsTheEnginesAnswer()
        => Assert.True(RagdollViolencePolicy.LowViolenceForMap(
            baseLowViolence: true, isServer: true, isMultiplayer: false,
            episodic: false, mapName: "d1_town_01"));

    [Fact]
    public void ACitadelMapForcesViolenceBackOn()
        => Assert.False(RagdollViolencePolicy.LowViolenceForMap(
            baseLowViolence: true, isServer: true, isMultiplayer: false,
            episodic: false, mapName: "d3_citadel_04"));

    [Fact]
    public void TheEpisodicListIsADIFFERENTLIST()
    {
        // ep1_citadel_02 is on the episodic list and NOT on the other one, so
        // asking with the wrong build flag must NOT force violence on. A
        // single merged list would pass every other fact in this file.
        Assert.True(RagdollViolencePolicy.IsFullViolenceMap(episodic: true, "ep1_citadel_02"));
        Assert.False(RagdollViolencePolicy.IsFullViolenceMap(episodic: false, "ep1_citadel_02"));
    }

    [Fact]
    public void TheNonEpisodicListIsNotReachableFromAnEpisodicBuild()
    {
        Assert.True(RagdollViolencePolicy.IsFullViolenceMap(episodic: false, "d3_breen_01"));
        Assert.False(RagdollViolencePolicy.IsFullViolenceMap(episodic: true, "d3_breen_01"));
    }

    [Fact]
    public void TheMapMatchIsCaseInsensitiveBecauseStocksIsQ_stricmp()
        => Assert.True(RagdollViolencePolicy.IsFullViolenceMap(episodic: false, "D3_Citadel_05"));

    [Fact]
    public void TheMapListWinsOverMultiplayerBecauseItIsAppliedLAST()
    {
        // Both clauses would set false here, so this cannot distinguish them.
        // The one that CAN is the map list applied to a NOT-low-violence
        // start: it must not turn violence off, i.e. it only ever clears.
        Assert.False(RagdollViolencePolicy.LowViolenceForMap(
            baseLowViolence: false, isServer: true, isMultiplayer: false,
            episodic: false, mapName: "d3_citadel_03"));
    }

    [Fact]
    public void AnEmptyMapNameMatchesNothing()
        => Assert.False(RagdollViolencePolicy.IsFullViolenceMap(episodic: false, ""));

    [Fact]
    public void TheFourConVarNamesAreTheFourStockDeclares()
        => Assert.Equal(
            new[] { "violence_hblood", "violence_ablood", "violence_hgibs", "violence_agibs" },
            RagdollViolencePolicy.LegacyViolenceConVars);

    [Fact]
    public void ThereAreThreeEpisodicMapsAndFourOthers()
    {
        Assert.Equal(3, RagdollViolencePolicy.EpisodicFullViolenceMaps.Length);
        Assert.Equal(4, RagdollViolencePolicy.FullViolenceMaps.Length);
    }
}

public class RagdollRetirementPolicyTests
{
    private static RagdollFacts Body(bool effected = false, bool pvs = true,
                                     bool cone = true, bool low = false)
        => new()
        {
            StillEffected = effected,
            InAnyClientPvs = pvs,
            InPlayerViewCone = cone,
            LowViolence = low,
        };

    // ---- the cap ---------------------------------------------------------

    [Fact]
    public void MinusOneMeansTheConVarDecides()
        => Assert.Equal(8, RagdollRetirementPolicy.EffectiveCap(-1, 8, lowViolence: false));

    [Fact]
    public void AManagerThatSpokeOverridesTheConVar()
        => Assert.Equal(3, RagdollRetirementPolicy.EffectiveCap(3, 8, lowViolence: false));

    [Fact]
    public void LowViolenceIsACapOfZeroAndOverridesTheManagerToo()
        => Assert.Equal(0, RagdollRetirementPolicy.EffectiveCap(3, 8, lowViolence: true));

    [Fact]
    public void AManagerCapOfZeroIsNotMistakenForNoManager()
        => Assert.Equal(0, RagdollRetirementPolicy.EffectiveCap(0, 8, lowViolence: false));

    [Fact]
    public void TheConVarDefaultsAreStocks()
    {
        Assert.Equal(8, RagdollRetirementPolicy.DefaultMaxCount);
        Assert.Equal(2, RagdollRetirementPolicy.DefaultImportantMaxCount);
    }

    // ---- the sweep -------------------------------------------------------

    [Fact]
    public void NothingRetiresWhileTheListIsInsideTheCap()
        => Assert.Empty(RagdollRetirementPolicy.Retire(new[] { false, false, false }, cap: 8));

    [Fact]
    public void NothingRetiresAtExactlyTheCap()
        => Assert.Empty(RagdollRetirementPolicy.Retire(new[] { false, false, false }, cap: 3));

    [Fact]
    public void TheOLDESTGoesFirst()
        => Assert.Equal(new[] { 0 }, RagdollRetirementPolicy.Retire(new[] { false, false, false }, cap: 2));

    [Fact]
    public void TheSweepStopsTheMomentTheCountIsInsideTheCap()
        => Assert.Equal(new[] { 0, 1 }, RagdollRetirementPolicy.Retire(
            new[] { false, false, false, false }, cap: 2));

    [Fact]
    public void ABodyMidEffectIsSkippedAndTheNextOneGoesInstead()
        => Assert.Equal(new[] { 1 }, RagdollRetirementPolicy.Retire(
            new[] { true, false, false }, cap: 2));

    [Fact]
    public void ABodyMidEffectDoesNotSpendTheBudget()
    {
        // Four bodies, cap 2, the first two burning. Stock's guard is on the
        // WHOLE list count, which the skip does not lower -- so both survivors
        // go and the answer is two, not one. A version that counted survivors
        // would retire only index 2.
        Assert.Equal(new[] { 2, 3 }, RagdollRetirementPolicy.Retire(
            new[] { true, true, false, false }, cap: 2));
    }

    [Fact]
    public void EveryBodyMidEffectMeansNoneGoesAtAll()
        => Assert.Empty(RagdollRetirementPolicy.Retire(new[] { true, true, true }, cap: 0));

    [Fact]
    public void AnEmptyListRetiresNothingEvenAtACapOfZero()
        => Assert.Empty(RagdollRetirementPolicy.Retire(System.Array.Empty<bool>(), cap: 0));

    [Fact]
    public void ACapOfZeroTakesEveryBodyThatIsNotMidEffect()
        => Assert.Equal(new[] { 0, 1, 2 }, RagdollRetirementPolicy.Retire(
            new[] { false, false, false }, cap: 0));

    // ---- the unseen pass -------------------------------------------------

    [Fact]
    public void LowViolenceRemovesABodyThePlayerIsStaringAt()
        => Assert.True(RagdollRetirementPolicy.ShouldRemove(
            lowViolence: true, inAnyClientPvs: true, inPlayerViewCone: true));

    [Fact]
    public void ABodyNoClientHasInPvsGoes()
        => Assert.True(RagdollRetirementPolicy.ShouldRemove(false, inAnyClientPvs: false, inPlayerViewCone: true));

    [Fact]
    public void ABodyBehindThePlayerGoes()
        => Assert.True(RagdollRetirementPolicy.ShouldRemove(false, inAnyClientPvs: true, inPlayerViewCone: false));

    [Fact]
    public void ABodyInFrontOfThePlayerStays()
        => Assert.False(RagdollRetirementPolicy.ShouldRemove(false, inAnyClientPvs: true, inPlayerViewCone: true));

    [Fact]
    public void TheUnseenPassDoesNothingWhileTheListIsInsideTheCap()
        => Assert.Equal(-1, RagdollRetirementPolicy.RetireOneUnseen(
            new[] { Body(pvs: false), Body(pvs: false) }, cap: 2));

    [Fact]
    public void TheUnseenPassTakesTheOLDESTUNSEENBodyAndNotTheOldest()
        => Assert.Equal(1, RagdollRetirementPolicy.RetireOneUnseen(
            new[] { Body(), Body(cone: false), Body(cone: false) }, cap: 1));

    [Fact]
    public void TheUnseenPassTakesATMOSTONE()
        => Assert.Single(RagdollRetirementPolicy.RetireThisFrame(
            new[] { Body(cone: false), Body(cone: false), Body(cone: false) }, cap: 1));

    [Fact]
    public void WhenTheUnseenPassFiresTheSweepDoesNotRunTHATFRAME()
    {
        // Five bodies, cap 1, one of them unseen. Stock RETURNS out of Update
        // after the unseen one, so exactly one goes this frame. A version that
        // fell through to the sweep would take four.
        var bodies = new[] { Body(), Body(), Body(cone: false), Body(), Body() };
        Assert.Equal(new[] { 2 }, RagdollRetirementPolicy.RetireThisFrame(bodies, cap: 1));
    }

    [Fact]
    public void WhenEveryBodyIsVisibleTheSweepRunsInstead()
    {
        var bodies = new[] { Body(), Body(), Body(), Body(), Body() };
        Assert.Equal(new[] { 0, 1, 2, 3 }, RagdollRetirementPolicy.RetireThisFrame(bodies, cap: 1));
    }

    [Fact]
    public void TheUnseenPassIgnoresTheEffectEntityAndTheSweepDoesNot()
    {
        // The same body, burning AND behind the player. The unseen pass takes
        // it (stock's ShouldRemoveThisRagdoll never looks at the effect
        // entity on the server) and the sweep would not.
        var one = new[] { Body(effected: true, cone: false), Body(), Body() };
        Assert.Equal(new[] { 0 }, RagdollRetirementPolicy.RetireThisFrame(one, cap: 2));
        Assert.Equal(new[] { 1 }, RagdollRetirementPolicy.Retire(new[] { true, false, false }, cap: 2));
    }

    // ---- the list order --------------------------------------------------

    [Fact]
    public void MoveToTopAppendsANewBody()
    {
        var lru = new List<int> { 1, 2 };
        RagdollRetirementPolicy.MoveToTop(lru, 3);
        Assert.Equal(new[] { 1, 2, 3 }, lru);
    }

    [Fact]
    public void MoveToTopRemovesTheOldEntryFirstSoNoBodyIsCountedTWICE()
    {
        var lru = new List<int> { 1, 2, 3 };
        RagdollRetirementPolicy.MoveToTop(lru, 1);
        Assert.Equal(new[] { 2, 3, 1 }, lru);
    }

    [Fact]
    public void MoveToTopOfAnEmptyListIsJustAnAdd()
    {
        var lru = new List<int>();
        RagdollRetirementPolicy.MoveToTop(lru, 7);
        Assert.Equal(new[] { 7 }, lru);
    }

    // ---- the important list ----------------------------------------------

    [Fact]
    public void AnImportantListInsideItsCapRetiresNothing()
        => Assert.Equal(-1, RagdollRetirementPolicy.ImportantOverflow(2, 2));

    [Fact]
    public void AnImportantListOverItsCapRetiresItsHEAD()
        => Assert.Equal(0, RagdollRetirementPolicy.ImportantOverflow(3, 2));

    [Fact]
    public void TheImportantOverflowIsONEPerCallAndNotASweep()
    {
        // Ten over a cap of two still names one index. Stock's arm removes
        // m_LRUImportantRagdolls.Head() and returns.
        Assert.Equal(0, RagdollRetirementPolicy.ImportantOverflow(12, 2));
    }
}
