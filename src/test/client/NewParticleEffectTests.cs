using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="NewParticleEffect"/>'s arms stock's recording never reached
/// (README-particlemgr-new-stock.md): the sleep skip, the first box, the draw's
/// gates and skip-render tests, the debug box, retirement. Each is derived
/// from particlemgr.cpp / particles_new.cpp.
/// </summary>
public class NewParticleEffectTests
{
    private static NewParticleEffect Stepped()
    {
        var e = new NewParticleEffect();
        e.Step(0.1f); // past its first frame
        return e;
    }

    [Fact]
    public void AnEffectAtItsSleepTimeWithParticlesIsNotListed()
    {
        Assert.Equal((false, false), Stepped().SimList(now: 5f, nextSleepTime: 5f, activeParticles: 3, hasMoved: false));
    }

    [Fact]
    public void AnEffectBeforeItsSleepTimeIsListed()
    {
        Assert.Equal((true, false), Stepped().SimList(now: 4.9f, nextSleepTime: 5f, activeParticles: 3, hasMoved: false));
    }

    [Fact]
    public void AnEffectWithNoParticlesIsListedPastItsSleepTime()
    {
        Assert.Equal((true, false), Stepped().SimList(now: 6f, nextSleepTime: 5f, activeParticles: 0, hasMoved: false));
    }

    [Fact]
    public void ASleepingEffectThatMovedIsListedForItsBoundsOnly()
    {
        Assert.Equal((true, true), Stepped().SimList(now: 6f, nextSleepTime: 5f, activeParticles: 3, hasMoved: true));
    }

    [Fact]
    public void AnEffectFlaggedForRemovalIsNotListedUnlessItMoved()
    {
        NewParticleEffect e = Stepped();
        e.Set(NewParticleFlags.Remove, true);
        Assert.Equal((false, false), e.SimList(now: 1f, nextSleepTime: 5f, activeParticles: 0, hasMoved: false));
        Assert.Equal((true, true), e.SimList(now: 1f, nextSleepTime: 5f, activeParticles: 0, hasMoved: true));
    }

    [Fact]
    public void TheFirstStepSimulatesZeroSecondsAndClearsTheFirstFrame()
    {
        var e = new NewParticleEffect();
        Assert.Equal(0f, e.Step(0.1f));
        Assert.False(e.Has(NewParticleFlags.FirstFrame));
        Assert.Equal(0.1f, e.Step(0.1f));
    }

    [Fact]
    public void AnEffectThatDoesNotSimulateStepsNothingAfterItsFirstFrame()
    {
        NewParticleEffect e = Stepped();
        e.Set(NewParticleFlags.Simulate, false);
        Assert.Equal(-1f, e.Step(0.1f));
    }

    [Fact]
    public void TheStepClearsTheDrawnFlag()
    {
        var e = new NewParticleEffect();
        e.Set(NewParticleFlags.Drawn, true);
        e.Step(0.1f);
        Assert.False(e.Has(NewParticleFlags.Drawn));
    }

    [Fact]
    public void AFirstBoxIsRecalculatedOnlyWhenTheEffectUpdatesItsOwn()
    {
        var e = new NewParticleEffect();
        e.Set(NewParticleFlags.NeedsBBoxUpdate, true);
        Assert.False(e.FirstBoxRecalculates);
        e.Set(NewParticleFlags.AutoUpdateBBox, true);
        Assert.True(e.FirstBoxRecalculates);
    }

    [Fact]
    public void AFirstBoxTheEffectSetsItselfIsTakenAsSet()
    {
        var e = new NewParticleEffect();
        e.Set(NewParticleFlags.NeedsBBoxUpdate, true);
        e.FirstBox(recalculated: false);
        Assert.False(e.Has(NewParticleFlags.NeedsBBoxUpdate));
    }

    [Fact]
    public void AFailedRecalculationKeepsAskingForAFirstBox()
    {
        var e = new NewParticleEffect();
        e.Set(NewParticleFlags.NeedsBBoxUpdate | NewParticleFlags.AutoUpdateBBox, true);
        e.FirstBox(recalculated: false);
        Assert.True(e.Has(NewParticleFlags.NeedsBBoxUpdate));
        e.FirstBox(recalculated: true);
        Assert.False(e.Has(NewParticleFlags.NeedsBBoxUpdate));
    }

    [Fact]
    public void AnInsideOutBoxCollapsesToTheSortOrigin()
    {
        Vector min = new(1e6f, 1e6f, 1e6f), max = new(-1e6f, -1e6f, -1e6f), sort = new(1, 2, 3);
        Assert.False(NewParticleEffect.RecalculateBoundingBox(ref min, ref max, sort));
        Assert.Equal(sort, min);
        Assert.Equal(sort, max);
    }

    [Fact]
    public void ARealBoxIsKept()
    {
        Vector min = new(0, 0, 0), max = new(1, 1, 1);
        Assert.True(NewParticleEffect.RecalculateBoundingBox(ref min, ref max, new Vector(5, 5, 5)));
        Assert.Equal(new Vector(1, 1, 1), max);
    }

    [Fact]
    public void ReleaseRemovesOnlyAnAllocatedFinishedEffectAtZeroReferences()
    {
        var held = new NewParticleEffect();
        held.Release(refCountAfter: 1, allocated: true, finished: true);
        Assert.False(held.Has(NewParticleFlags.Remove));
        var burning = new NewParticleEffect();
        burning.Release(0, allocated: true, finished: false);
        Assert.False(burning.Has(NewParticleFlags.Remove));
        var stat = new NewParticleEffect();
        stat.Release(0, allocated: false, finished: true);
        Assert.False(stat.Has(NewParticleFlags.Remove));
        var done = new NewParticleEffect();
        done.Release(0, allocated: true, finished: true);
        Assert.True(done.Has(NewParticleFlags.Remove));
    }

    [Fact]
    public void AnEffectWithNoRenderHandleReportsNoChange()
    {
        var e = new NewParticleEffect();
        Assert.False(e.DetectChanges(new Vector(0, 0, 0), new Vector(1, 1, 1), 2f, hasRenderHandle: false));
        Assert.Equal(new Vector(1e6f, 1e6f, 1e6f), e.LastMin);
    }

    private static NewParticleDrawIn Draw() => new()
    {
        Flags = 1 | NewParticleEffect.StudioTransparency,
        DrawParticles = true,
        ClientModeDraws = true,
        RenderSystems = true,
        Camera = new Vector(0, 0, 0),
        Min = new Vector(10, -1, -1),
        Max = new Vector(12, 1, 1),
        MaxDrawDistance = 100000f,
        NoDrawTimeToGoToSleep = 0.1f,
        LastSimulationTime = 5f,
        NextSleepTime = 4f,
        SkipRenderControlPoint = -1,
        CameraEntity = 1,
        SkipEntity = -1,
        LocalPlayer = 1,
        ObserverTarget = -1,
    };

    [Fact]
    public void RDrawParticlesZeroDrawsNothing()
    {
        NewParticleDrawIn d = Draw();
        d.DrawParticles = false;
        Assert.Equal(new NewParticleDraw(NewParticleDrawAction.None, NewParticleDrawReason.DrawParticles, 4f), NewParticleEffect.Draw(d));
    }

    [Fact]
    public void AClientModeOrManagerThatDrawsNoParticlesDrawsNothing()
    {
        NewParticleDrawIn d = Draw();
        d.ClientModeDraws = false;
        Assert.Equal(NewParticleDrawReason.ClientMode, NewParticleEffect.Draw(d).Reason);
        d = Draw();
        d.RenderSystems = false;
        Assert.Equal(NewParticleDrawReason.ClientMode, NewParticleEffect.Draw(d).Reason);
    }

    [Fact]
    public void ADepthPassDrawsNothing()
    {
        NewParticleDrawIn d = Draw();
        d.Flags = 0x40000000;
        Assert.Equal(NewParticleDrawReason.ShadowDepth, NewParticleEffect.Draw(d).Reason);
        d.Flags = 0x08000000;
        Assert.Equal(NewParticleDrawReason.ShadowDepth, NewParticleEffect.Draw(d).Reason);
    }

    [Fact]
    public void ABoxPastTheMaxDrawDistanceIsCulledAndKeptAwakeForItsNoDrawTime()
    {
        NewParticleDrawIn d = Draw();
        d.MaxDrawDistance = 9.9f; // the box is 10 away
        Assert.Equal(new NewParticleDraw(NewParticleDrawAction.None, NewParticleDrawReason.Distance, 5.1f), NewParticleEffect.Draw(d));
    }

    [Fact]
    public void TheDistanceCullNeverPullsTheSleepTimeIn()
    {
        NewParticleDrawIn d = Draw();
        d.MaxDrawDistance = 1f;
        d.NextSleepTime = 9f;
        Assert.Equal(9f, NewParticleEffect.Draw(d).NextSleepTime);
    }

    [Fact]
    public void ABoxAtExactlyTheMaxDrawDistanceIsDrawn()
    {
        NewParticleDrawIn d = Draw();
        d.MaxDrawDistance = 10f;
        Assert.Equal(NewParticleDrawReason.Drawn, NewParticleEffect.Draw(d).Reason);
    }

    [Fact]
    public void AnOpaquePassGoesToTheRenderCache()
    {
        NewParticleDrawIn d = Draw();
        d.Flags = 1;
        Assert.Equal(NewParticleDrawAction.RenderCache, NewParticleEffect.Draw(d).Action);
    }

    [Fact]
    public void ATranslucentPassRendersNow()
    {
        Assert.Equal(NewParticleDrawAction.Render, NewParticleEffect.Draw(Draw()).Action);
    }

    private static NewParticleDrawIn Skipping()
    {
        NewParticleDrawIn d = Draw();
        d.SkipRenderControlPoint = 1;
        d.HighestControlPoint = 1;
        d.SkipEntity = 1; // the camera's own entity
        return d;
    }

    [Fact]
    public void ASystemOnTheCamerasOwnEntityIsSkippedInFirstPerson()
    {
        Assert.Equal(NewParticleDrawReason.SkipSelf, NewParticleEffect.Draw(Skipping()).Reason);
    }

    [Fact]
    public void ASystemOnTheCamerasOwnEntityIsDrawnInThirdPerson()
    {
        NewParticleDrawIn d = Skipping();
        d.ThirdPerson = true;
        Assert.Equal(NewParticleDrawReason.Drawn, NewParticleEffect.Draw(d).Reason);
    }

    [Fact]
    public void TheSkipTestsNeedTheControlPointToHaveBeenSet()
    {
        NewParticleDrawIn d = Skipping();
        d.HighestControlPoint = 0;
        Assert.Equal(NewParticleDrawReason.Drawn, NewParticleEffect.Draw(d).Reason);
    }

    [Fact]
    public void TheSkipTestsNeedAnEntityAtTheCamera()
    {
        NewParticleDrawIn d = Skipping();
        d.CameraEntity = -1;
        Assert.Equal(NewParticleDrawReason.Drawn, NewParticleEffect.Draw(d).Reason);
    }

    [Fact]
    public void AnOpaquePassSkipsNothing()
    {
        NewParticleDrawIn d = Skipping();
        d.Flags = 1;
        Assert.Equal(NewParticleDrawAction.RenderCache, NewParticleEffect.Draw(d).Action);
    }

    [Fact]
    public void ASystemOnTheEntityTheLocalPlayerSpectatesInEyeIsSkipped()
    {
        NewParticleDrawIn d = Skipping();
        d.SkipEntity = 7;
        d.ObserverMode = NewParticleEffect.ObserverInEye;
        d.ObserverTarget = 7;
        Assert.Equal(NewParticleDrawReason.SkipInEye, NewParticleEffect.Draw(d).Reason);
        d.ObserverMode = 5; // chase
        Assert.Equal(NewParticleDrawReason.Drawn, NewParticleEffect.Draw(d).Reason);
    }

    [Fact]
    public void TheInEyeTestNeedsTheLocalPlayerToBeTheCamera()
    {
        NewParticleDrawIn d = Skipping();
        d.SkipEntity = 7;
        d.ObserverMode = NewParticleEffect.ObserverInEye;
        d.ObserverTarget = 7;
        d.LocalPlayer = 2;
        Assert.Equal(NewParticleDrawReason.Drawn, NewParticleEffect.Draw(d).Reason);
    }

    [Fact]
    public void TheDebugBoxIsGreyWhenCulled()
    {
        Assert.Equal((true, 64, 64, 64), NewParticleEffect.DebugBox(culled: true, showBBoxCost: 0, activeParticles: 10, autoUpdateBBox: true));
    }

    [Fact]
    public void TheDebugBoxIsRedForAnEffectThatUpdatesItsOwnBoxAndGreenOtherwise()
    {
        Assert.Equal((true, 255, 0, 0), NewParticleEffect.DebugBox(false, 0, 10, autoUpdateBBox: true));
        Assert.Equal((true, 0, 255, 0), NewParticleEffect.DebugBox(false, 0, 10, autoUpdateBBox: false));
    }

    [Fact]
    public void TheDebugBoxColoursByCostAndHidesCheapSystemsUnlessTheCostIsNegative()
    {
        Assert.Equal((false, 0, 0, 0), NewParticleEffect.DebugBox(false, 100, 40, false));
        Assert.Equal((true, 0, 255, 0), NewParticleEffect.DebugBox(false, -100, 40, false));
        Assert.Equal((true, 0, 255 - 128, 128), NewParticleEffect.DebugBox(false, 100, 75, false));
        Assert.Equal((true, 128, 0, 127), NewParticleEffect.DebugBox(false, 100, 150, false));
        Assert.Equal((true, 255, 0, 0), NewParticleEffect.DebugBox(false, 100, 200, false));
    }

    [Fact]
    public void AProjectivePositionDividesByW()
    {
        var m = new Matrix4x4Raw { M00 = 2, M11 = 2, M22 = 2, M33 = 2, M03 = 4 };
        Assert.Equal(new Vector(3, 2, 3), NewParticleEffect.MultiplyPositionProjective(m, new Vector(1, 2, 3)));
    }

    [Fact]
    public void AZeroWLeavesThePositionUndivided()
    {
        var m = new Matrix4x4Raw { M00 = 1, M11 = 1, M22 = 1 };
        Assert.Equal(new Vector(0, 0, 0), NewParticleEffect.MultiplyPositionProjective(m, new Vector(1, 2, 3)));
    }

    // A view whose world-to-screen maps world x,y straight to pixels (w = 1).
    private static NewParticleRetireView View() => new()
    {
        Width = 100,
        Height = 100,
        Fov = 90f,
        Origin = new Vector(0, 0, -1000),
        WorldToScreen = new Matrix4x4Raw { M00 = 1, M11 = 1, M22 = 1, M33 = 1 },
    };

    private static NewParticleRetireIn Fire(float x, bool first = true, bool replacement = true, ulong def = 1) => new()
    {
        Definition = def,
        CullRadius = 10f,
        CullFillCost = 1f,
        CullPoint = new Vector(x, 50, 0),
        HasReplacement = replacement,
        FirstFrame = first,
    };

    [Fact]
    public void RetirementIsOffAtZeroCostOrWithNoView()
    {
        NewParticleRetireIn[] fires = [Fire(50), Fire(50)];
        var retire = new List<int>();
        Assert.False(NewParticleEffect.EarlyRetire(fires, View(), retireCost: 0f, 1, [], retire));
        NewParticleRetireView none = View();
        none.Width = 0;
        Assert.False(NewParticleEffect.EarlyRetire(fires, none, 1f, 1, [], retire));
        Assert.Empty(retire);
    }

    [Fact]
    public void RetirementSkipsDefinitionsWithNoCullRadiusOrPastTheirFirstFrame()
    {
        NewParticleRetireIn noRadius = Fire(50);
        noRadius.CullRadius = 0f;
        var retire = new List<int>();
        Assert.False(NewParticleEffect.EarlyRetire([noRadius, Fire(50, first: false)], View(), 0.001f, 1, [], retire));
    }

    [Fact]
    public void ADefinitionIsCheckedOncePerFrame()
    {
        var checkedFrames = new Dictionary<ulong, int> { [1] = 7 };
        var retire = new List<int>();
        Assert.False(NewParticleEffect.EarlyRetire([Fire(50), Fire(50)], View(), 0.001f, frameCount: 7, checkedFrames, retire));
        Assert.True(NewParticleEffect.EarlyRetire([Fire(50), Fire(50)], View(), 0.001f, frameCount: 8, checkedFrames, retire));
        Assert.Equal(8, checkedFrames[1]);
    }

    [Fact]
    public void ThePassAnswersTrueEvenWhenNothingIsRetired()
    {
        var retire = new List<int>();
        Assert.True(NewParticleEffect.EarlyRetire([Fire(50), Fire(50)], View(), retireCost: 1000f, 1, [], retire));
        Assert.Empty(retire);
    }

    [Fact]
    public void TheLargestFirstFrameEffectsAreRetiredUntilTheAreaFits()
    {
        // The left fire is clipped by the view's edge, so it covers less.
        NewParticleRetireIn[] fires = [Fire(0), Fire(50), Fire(50, first: false)];
        var retire = new List<int>();
        Assert.True(NewParticleEffect.EarlyRetire(fires, View(), retireCost: 0.001f, 1, [], retire));
        Assert.Equal([1, 0], retire);
    }

    [Fact]
    public void ASingleEffectWithNoReplacementIsKept()
    {
        var retire = new List<int>();
        NewParticleEffect.EarlyRetire([Fire(50, replacement: false)], View(), 0.001f, 1, [], retire);
        Assert.Empty(retire);
    }
}
