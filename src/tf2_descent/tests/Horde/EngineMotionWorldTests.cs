using SourceSharp.Abi;
using TF2.Descent.Server.Horde.Motion;
using TF2.Shared;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A robot body's sweeps through the engine collide as a player's movement does (stock: a TF
/// player's TracePlayerBBox, COLLISION_GROUP_PLAYER_MOVEMENT, whose filter passes debris: a
/// dropped ammo pack or weapon), and, a robot being of the team against the heroes, its mask
/// carries the heroes' team's contents bit, as an enemy player's does, so a hero still stops it.
/// Live (2026-11-16, level 10 seed 1): a x1.5 pyro sent through a dead robot's tf_ammo_pack stood
/// against it 20 s in each of 5 trials.
/// </summary>
public sealed class EngineMotionWorldTests
{
    private static readonly EntityHandle Body = new(45u | (2u << 12));

    private static readonly Vector Mins = new(-24f, -24f, 0f), Maxs = new(24f, 24f, 82f);

    [Fact]
    public void A_bodys_sweep_is_in_the_player_movement_group()
    {
        var engine = new RecordingEngine().Returns("WorldQuery.TraceBoxInGroup", new SurfaceTraceResult(false, 1f, new Vector(64f, 0f, 0f), Vector.Zero, EntityHandle.None, -1, 0));
        var world = new EngineMotionWorld(engine, engine);

        world.Sweep(Vector.Zero, new Vector(64f, 0f, 0f), Mins, Maxs, Body);

        EngineCall call = engine.Last("WorldQuery.TraceBoxInGroup")!.Value;
        Assert.Equal(EngineMotionWorld.PlayerMovement, call.ArgAs<int>(6));
        Assert.Equal(Body, call.ArgAs<EntityHandle>(5));
    }

    [Fact]
    public void A_bodys_sweep_carries_the_heroes_team_bit_over_the_solid_mask()
    {
        var engine = new RecordingEngine().Returns("WorldQuery.TraceBoxInGroup", new SurfaceTraceResult(false, 1f, new Vector(64f, 0f, 0f), Vector.Zero, EntityHandle.None, -1, 0));
        var world = new EngineMotionWorld(engine, engine);

        world.Sweep(Vector.Zero, new Vector(64f, 0f, 0f), Mins, Maxs, Body);

        uint mask = engine.Last("WorldQuery.TraceBoxInGroup")!.Value.ArgAs<uint>(4);
        Assert.Equal(EngineMotionWorld.MaskSolid | (uint)TFContents.RedTeam, mask);
    }

    /// <summary>The trace filter's two arms a hero meets: the group matrix (player movement hits a player) and TF's team rule (that group hits a RED player only for a mask with RED's bit).</summary>
    [Fact]
    public void A_hero_is_still_struck_by_a_bodys_sweep()
    {
        Assert.True(CollisionRules.ShouldCollide(EngineCollisionGroup.PlayerMovement, EngineCollisionGroup.Player));
        Assert.True(TF2.Shared.Rules.TFTeamCollision.Collides(TF2.Shared.Rules.TFTeamCollision.PlayerMovement, avoidTeammates: true, TFTeam.Red,
                                                              unchecked((int)EngineMotionWorld.BodyMask)));
    }

    [Fact]
    public void Debris_is_passed_by_a_bodys_sweep_and_struck_by_one_in_no_group()
    {
        Assert.False(CollisionRules.ShouldCollide((EngineCollisionGroup)EngineMotionWorld.PlayerMovement, EngineCollisionGroup.Debris));
        Assert.True(CollisionRules.ShouldCollide(EngineCollisionGroup.None, EngineCollisionGroup.Debris));
    }

    [Fact]
    public void A_bodys_place_test_is_in_the_player_movement_group_with_the_same_mask()
    {
        var engine = new RecordingEngine().Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(Vector.Zero, false, false));
        var world = new EngineMotionWorld(engine, engine);

        world.Sweep(Vector.Zero, Vector.Zero, Mins, Maxs, Body);

        EngineCall call = engine.Last("WorldQuery.EntityTraceHull")!.Value;
        Assert.Equal(EngineMotionWorld.MaskSolid | (uint)TFContents.RedTeam, call.ArgAs<uint>(5));
        Assert.Equal(EngineMotionWorld.PlayerMovement, call.ArgAs<int>(6));
    }
}
