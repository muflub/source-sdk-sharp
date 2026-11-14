using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using SourceSharp.Shared.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The per-entity arm of the trace rule (tf2 F04): a registered
/// <see cref="IEntityCollideRule"/> chain asked between the pass checks and
/// the group matrix, for game entities only, every registered rule in
/// agreement; with no rule the filter answers as
/// <see cref="TraceFilterRulesTests"/> pins it, unchanged.
/// </summary>
public class TraceFilterCollideRuleTests
{
    // bspflags.h, as TraceFilterRulesTests has them: a mask that asks for
    // entities, so a studio prop passes the shape clause.
    private const int MaskSolid = 0x1 | 0x2 | 0x8 | 0x4000 | TraceFilterRules.ContentsMonster;

    private static readonly EntityHandle Crate = Handle(40, 3);
    private static readonly EntityHandle Rocket = Handle(41, 1);

    private static EntityHandle Handle(int entry, int serial) => new((uint)(entry | (serial << 12)));

    private sealed class Chained(params IEntityCollideRule[] rules) : IHooks<IEntityCollideRule>
    {
        public IReadOnlyList<IEntityCollideRule> All => rules;

        public void Each(string what, Action<IEntityCollideRule> call)
        {
            foreach (IEntityCollideRule rule in rules)
                call(rule);
        }
    }

    private sealed class Refusing(EntityHandle target) : IEntityCollideRule
    {
        public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask)
            => entity != target;
    }

    private sealed class Agreeing : IEntityCollideRule
    {
        public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask) => true;
    }

    private sealed class Recorder : IEntityCollideRule
    {
        public int Asks;
        public EntityHandle Mover = Rocket;
        public EntityHandle Entity;
        public CollisionGroup Group;
        public int Mask;

        public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask)
        {
            Asks++;
            Mover = mover;
            Entity = entity;
            Group = group;
            Mask = contentsMask;
            return true;
        }
    }

    private static TraceCandidate Prop(int group = 0) => new(group, SolidType.VPhysics, 12, EntityHandle.None);

    private static (TraceFilterRules Rules, Candidates Blocks) Rig(IHooks<IEntityCollideRule> rules, int candidateGroup = 0)
    {
        var blocks = new Candidates();
        blocks.Blocks[Crate] = Prop(candidateGroup);
        blocks.Blocks[Rocket] = Prop(candidateGroup);
        return (TraceFilterRules.Over(blocks, CollisionRules.BuildMatrix(), rules), blocks);
    }

    private static bool Hits(TraceFilterRules rules, EntityHandle candidate, int group = 0, EntityHandle pass = default)
    {
        TraceFilterQuery query = rules.Resolve(pass, group);
        return rules.ShouldHitEntity(query, candidate.Value, MaskSolid);
    }

    private sealed class Candidates : ITraceCandidates
    {
        public readonly Dictionary<EntityHandle, TraceCandidate> Blocks = [];

        public bool TryRead(EntityHandle entity, out TraceCandidate candidate)
            => Blocks.TryGetValue(entity, out candidate);

        public bool IsWorld(EntityHandle entity) => false;

        public bool IsBrushModel(int modelIndex) => false;
    }

    [Fact]
    public void NoCollideRuleKeepsStockCollision()
    {
        // A chain registered with no rule: the stock answer, the group matrix
        // alone, both its hit and its refusal.
        var (empty, _) = Rig(new Chained());
        var (vehicle, _) = Rig(new Chained(), (int)EngineCollisionGroup.InVehicle);

        Assert.True(Hits(empty, Crate));
        Assert.False(Hits(vehicle, Crate));
    }

    [Fact]
    public void ARefusingRuleStopsItsEntityAndNothingElse()
    {
        var (rules, _) = Rig(new Chained(new Refusing(Crate)));

        Assert.False(Hits(rules, Crate));
        Assert.True(Hits(rules, Rocket));
    }

    [Fact]
    public void AnAgreeingRuleChangesNoStockAnswer()
    {
        var (agreeing, _) = Rig(new Chained(new Agreeing()));

        // The hit the matrix alone gives ...
        Assert.True(Hits(agreeing, Crate));

        // ... and the stock refusals stay refusals through the chain: an
        // entity in the vehicle group is not hit by a plain trace.
        var (vehicleAgreeing, _) = Rig(new Chained(new Agreeing()), (int)EngineCollisionGroup.InVehicle);
        Assert.False(Hits(vehicleAgreeing, Crate));
    }

    [Fact]
    public void EveryRegisteredRuleMustAgree()
    {
        var (bothAgree, _) = Rig(new Chained(new Agreeing(), new Agreeing()));
        var (oneRefuses, _) = Rig(new Chained(new Agreeing(), new Refusing(Crate)));

        Assert.True(Hits(bothAgree, Crate));
        Assert.False(Hits(oneRefuses, Crate));
    }

    [Fact]
    public void TheRuleHearsTheCandidateTheTracesGroupAndTheMask()
    {
        // The trace's group, never the candidate's: stock hands the entity
        // m_collisionGroup (util_shared.cpp:305), and TF2's ShouldCollide tests
        // it for COLLISION_GROUP_PLAYER_MOVEMENT. The two groups differ here so
        // the fact can tell them apart.
        var recorder = new Recorder();
        var (rules, _) = Rig(new Chained(recorder), (int)EngineCollisionGroup.Debris);

        Hits(rules, Crate, (int)EngineCollisionGroup.PlayerMovement);

        Assert.Equal(1, recorder.Asks);
        Assert.Equal(Crate, recorder.Entity);
        Assert.Equal((int)EngineCollisionGroup.PlayerMovement, (int)recorder.Group);
        Assert.Equal(MaskSolid, recorder.Mask);
    }

    /// <summary>
    /// The mover is the trace's pass entity: a movement trace names the player
    /// it moves, and a rule that compares two players (a team mode's teammate
    /// pass, which the mask's two team bits cannot carry) reads it here.
    /// </summary>
    [Fact]
    public void TheRuleHearsTheTracesPassEntityAsTheMover()
    {
        var recorder = new Recorder();
        var (rules, _) = Rig(new Chained(recorder));

        Hits(rules, Crate, pass: Rocket);

        Assert.Equal(1, recorder.Asks);
        Assert.Equal(Rocket, recorder.Mover);
    }

    /// <summary>A trace that names no pass entity hands the rule no mover.</summary>
    [Fact]
    public void ATraceWithNoPassEntityHandsNoMover()
    {
        var recorder = new Recorder();
        var (rules, _) = Rig(new Chained(recorder));

        Hits(rules, Crate);

        Assert.Equal(1, recorder.Asks);
        Assert.Equal(EntityHandle.None, recorder.Mover);
    }
}
