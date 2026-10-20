using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4f: the trace rule, from stock's
/// <c>CTraceFilterSimple::ShouldHitEntity</c> (util_shared.cpp) with
/// <c>StandardFilterRules</c>, <c>PassServerEntityFilter</c> and
/// <c>CGameRules::ShouldCollide</c>, as the native filter answered them for
/// this hierarchy (trace_filter_library.cpp). One behaviour per fact.
/// </summary>
public class TraceFilterRulesTests
{
    // bspflags.h
    private const int ContentsSolid = 0x1;
    private const int ContentsWindow = 0x2;
    private const int ContentsGrate = 0x8;
    private const int ContentsMoveable = 0x4000;
    private const int ContentsMonster = 0x2000000;
    private const int MaskSolidBrushOnly = ContentsSolid | ContentsMoveable | ContentsWindow | ContentsGrate;
    private const int MaskSolid = MaskSolidBrushOnly | ContentsMonster;

    private const int BrushModel = 7;
    private const int StudioModel = 12;

    private static readonly EntityHandle Crate = Handle(40, 3);
    private static readonly EntityHandle Rocket = Handle(41, 1);
    private static readonly EntityHandle Shooter = Handle(2, 9);
    private static readonly EntityHandle World = Handle(0, 1);
    private static readonly EntityHandle Dead = Handle(55, 4);

    private static EntityHandle Handle(int entry, int serial) => new((uint)(entry | (serial << 12)));

    private sealed class Candidates : ITraceCandidates
    {
        public readonly Dictionary<EntityHandle, TraceCandidate> Blocks = [];
        public int BrushAsks;
        public int ReadAsks;

        public bool TryRead(EntityHandle entity, out TraceCandidate candidate)
        {
            ReadAsks++;
            return Blocks.TryGetValue(entity, out candidate);
        }

        public bool IsWorld(EntityHandle entity) => entity == World;

        public bool IsBrushModel(int modelIndex)
        {
            BrushAsks++;
            return modelIndex == BrushModel;
        }
    }

    private static TraceCandidate Prop(int group = 0, EntityHandle owner = default)
        => new(group, SolidType.VPhysics, StudioModel, owner);

    private static TraceCandidate Brush(SolidType solid)
        => new((int)EngineCollisionGroup.None, solid, BrushModel, EntityHandle.None);

    private static TraceFilterRules Rules(Candidates candidates) => new(candidates, CollisionRules.BuildMatrix());

    private static bool Hits(Candidates candidates, EntityHandle candidate, int mask,
                             EntityHandle pass = default, int group = 0)
    {
        TraceFilterRules rules = Rules(candidates);
        TraceFilterQuery query = rules.Resolve(pass, group);
        return rules.ShouldHitEntity(query, candidate.Value, mask);
    }

    [Fact]
    public void AStaticPropIsNeverAGameEntityAndIsNotHit()
    {
        var c = new Candidates();
        uint staticProp = 17u | 0x40000000u;   // a static prop's handle: index | the engine's static-prop mask

        TraceFilterRules rules = Rules(c);
        Assert.False(rules.ShouldHitEntity(rules.Resolve(default, 0), staticProp, MaskSolid));
        Assert.Equal(0, c.ReadAsks);
    }

    [Fact]
    public void AStaticPropHandleIsRecognisedByItsSerial()
    {
        Assert.True(TraceFilterRules.IsStaticProp(17u | 0x40000000u));
        Assert.False(TraceFilterRules.IsStaticProp(Crate.Value));
        Assert.False(TraceFilterRules.IsStaticProp(EntityHandle.None.Value));
    }

    [Fact]
    public void AHandleWithNoGameEntityBehindItIsNotHit()
    {
        var c = new Candidates();
        Assert.False(Hits(c, Dead, MaskSolid));
    }

    [Fact]
    public void ANonBrushEntityIsSkippedByAMaskThatDoesNotAskForEntities()
    {
        var c = new Candidates();
        c.Blocks[Crate] = Prop();
        Assert.False(Hits(c, Crate, MaskSolidBrushOnly));
    }

    [Fact]
    public void ANonBrushEntityIsHitByAMaskThatAsksForEntities()
    {
        var c = new Candidates();
        c.Blocks[Crate] = Prop();
        Assert.True(Hits(c, Crate, MaskSolid));
    }

    [Fact]
    public void ABrushSolidAsBspStopsAWorldOnlyMask()
    {
        var c = new Candidates();
        c.Blocks[Crate] = Brush(SolidType.Bsp);
        Assert.True(Hits(c, Crate, MaskSolidBrushOnly));
    }

    [Fact]
    public void ABrushSolidAsVPhysicsStopsAWorldOnlyMask()
    {
        var c = new Candidates();
        c.Blocks[Crate] = Brush(SolidType.VPhysics);
        Assert.True(Hits(c, Crate, MaskSolidBrushOnly));
    }

    [Fact]
    public void ABrushThatIsSolidAsABoxIsSkippedByAWorldOnlyMask()
    {
        var c = new Candidates();
        c.Blocks[Crate] = Brush(SolidType.BoundingBox);
        Assert.False(Hits(c, Crate, MaskSolidBrushOnly));
    }

    [Fact]
    public void TheModelTableIsNotAskedWhenTheMaskAsksForEntities()
    {
        var c = new Candidates();
        c.Blocks[Crate] = Brush(SolidType.BoundingBox);
        Assert.True(Hits(c, Crate, MaskSolid));
        Assert.Equal(0, c.BrushAsks);
    }

    [Fact]
    public void ThePassEntityItselfIsNeverHit()
    {
        var c = new Candidates();
        c.Blocks[Shooter] = Prop();
        Assert.False(Hits(c, Shooter, MaskSolid, pass: Shooter));
    }

    [Fact]
    public void WhatThePassEntityOwnsIsNotHit()
    {
        var c = new Candidates();
        c.Blocks[Shooter] = Prop();
        c.Blocks[Rocket] = Prop(owner: Shooter);
        Assert.False(Hits(c, Rocket, MaskSolid, pass: Shooter));
    }

    [Fact]
    public void ThePassEntitysOwnerIsNotHit()
    {
        var c = new Candidates();
        c.Blocks[Shooter] = Prop();
        c.Blocks[Rocket] = Prop(owner: Shooter);
        Assert.False(Hits(c, Shooter, MaskSolid, pass: Rocket));
    }

    [Fact]
    public void AnUnrelatedEntityIsHitPastAPassEntity()
    {
        var c = new Candidates();
        c.Blocks[Shooter] = Prop();
        c.Blocks[Crate] = Prop();
        Assert.True(Hits(c, Crate, MaskSolid, pass: Shooter));
    }

    [Fact]
    public void APassHandleThatNamesNothingSkipsNothing()
    {
        var c = new Candidates();
        c.Blocks[Rocket] = Prop(owner: Dead);
        Assert.True(Hits(c, Rocket, MaskSolid, pass: Dead));
    }

    [Fact]
    public void TheWorldAsThePassEntitySkipsWhatTheWorldOwns()
    {
        var c = new Candidates();
        c.Blocks[Rocket] = Prop(owner: World);
        c.Blocks[Crate] = Prop();
        Assert.False(Hits(c, Rocket, MaskSolid, pass: World));
        Assert.True(Hits(c, Crate, MaskSolid, pass: World));
    }

    [Fact]
    public void ResolvingAPassEntityReadsItsOwnerOnce()
    {
        var c = new Candidates();
        c.Blocks[Rocket] = Prop(owner: Shooter);
        TraceFilterQuery query = Rules(c).Resolve(Rocket, (int)EngineCollisionGroup.Projectile);

        Assert.True(query.PassResolved);
        Assert.Equal(Shooter, query.PassOwner);
        Assert.Equal((int)EngineCollisionGroup.Projectile, query.CollisionGroup);
    }

    [Fact]
    public void NoPassEntityResolvesToNone()
    {
        TraceFilterQuery query = Rules(new Candidates()).Resolve(EntityHandle.None, 0);
        Assert.False(query.PassResolved);
    }

    [Fact]
    public void ADebrisTraceDoesNotHitAPlayer()
    {
        var c = new Candidates();
        c.Blocks[Shooter] = Prop((int)EngineCollisionGroup.Player);
        Assert.False(Hits(c, Shooter, MaskSolid, group: (int)EngineCollisionGroup.Debris));
    }

    [Fact]
    public void AnUngroupedTraceHitsDebris()
    {
        var c = new Candidates();
        c.Blocks[Crate] = Prop((int)EngineCollisionGroup.Debris);
        Assert.True(Hits(c, Crate, MaskSolid, group: (int)EngineCollisionGroup.None));
    }

    [Fact]
    public void NothingHitsAnEntityInAVehicle()
    {
        var c = new Candidates();
        c.Blocks[Shooter] = Prop((int)EngineCollisionGroup.InVehicle);
        Assert.False(Hits(c, Shooter, MaskSolid, group: (int)EngineCollisionGroup.None));
    }

    [Fact]
    public void AGroupOutsideTheTableCollides()
    {
        var c = new Candidates();
        c.Blocks[Crate] = Prop((int)EngineCollisionGroup.InVehicle);
        Assert.True(Hits(c, Crate, MaskSolid, group: 25));
    }

    [Fact]
    public void TheMatrixMustHaveOneRowPerSharedGroup()
        => Assert.Throws<ArgumentException>(() => new TraceFilterRules(new Candidates(), new uint[3]));

    [Fact]
    public void OutsideATraceTheSinkHitsNothing()
    {
        var c = new Candidates();
        c.Blocks[Crate] = Prop();
        var sink = new TraceFilterSink(Rules(c), new Crossings());
        Assert.Equal(0, sink.ShouldHitEntity(Crate.Value, MaskSolid));
    }

    [Fact]
    public void TheSinkAnswersForTheQueryInScopeAndPutsTheOuterOneBack()
    {
        var c = new Candidates();
        c.Blocks[Shooter] = Prop();
        c.Blocks[Crate] = Prop();
        var sink = new TraceFilterSink(Rules(c), new Crossings());

        using (sink.Use(sink.Rules.Resolve(Crate, 0)))
        {
            Assert.Equal(0, sink.ShouldHitEntity(Crate.Value, MaskSolid));

            using (sink.Use(sink.Rules.Resolve(Shooter, 0)))
                Assert.Equal(1, sink.ShouldHitEntity(Crate.Value, MaskSolid));

            Assert.Equal(0, sink.ShouldHitEntity(Crate.Value, MaskSolid));
        }

        Assert.Null(sink.Current);
    }

    [Fact]
    public void TheFilterIsATraceOfEverything()
        => Assert.Equal(0, TraceFilterRules.TraceEverything);   // TRACE_EVERYTHING, IEngineTrace.h

    [Fact]
    public void EachCandidateTheEngineAsksAboutIsCounted()
    {
        var crossings = new Crossings();
        var sink = new TraceFilterSink(Rules(new Candidates()), crossings);

        long before = crossings.Read(CrossingOp.ServerTraceFilterShouldHitEntity);
        sink.ShouldHitEntity(Crate.Value, MaskSolid);
        sink.ShouldHitEntity(Rocket.Value, MaskSolid);

        Assert.Equal(before + 2, crossings.Read(CrossingOp.ServerTraceFilterShouldHitEntity));
    }
}
