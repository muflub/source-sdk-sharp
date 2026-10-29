using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_movelinear against stock's CFuncMoveLinear (func_movelinear.cpp): its
/// StartSound and StopSound, BlockDamage's crush, SetSpeed re-aiming a move
/// under way, and the inputs that do nothing when it is already there. Found
/// in the contract diff (2026-10-27): none of the three keys was read.
/// </summary>
public class MoveLinearTests
{
    private static readonly EntityHandle Mover = new(0x0008_0101u);
    private static readonly EntityHandle Blocker = new(0x0008_0102u);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public readonly ThinkScheduler Thinks = new();
        public Vector Origin = Vector.Zero;

        public FuncMoveLinear Entity => (FuncMoveLinear)Registry.Get(Mover)!;

        public void Input(string name, string value = "", float number = 0f)
            => Registry.Input(Mover, name, value, number, EntityHandle.None);

        public string[] Played => Engine.AllOf("Sound.EntityEmitSound").Select(c => c.ArgAs<string>(1)).ToArray();

        public string[] Stopped => Engine.AllOf("Sound.EntityStopSound").Select(c => c.ArgAs<string>(1)).ToArray();

        public (Vector To, float Speed)[] Moves
            => Engine.AllOf("BrushMovement.MoveToPosition").Select(c => (c.ArgAs<Vector>(1), c.ArgAs<float>(2))).ToArray();
    }

    private static Rig Spawned(params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncMoveLinear).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        engine.Returns("Entities.GetSpawnOrigin", Vector.Zero)
              .ReturnsEachCall("Entities.GetOrigin", () => rig.Origin)
              .Returns("Entities.GetBoundsSize", new Vector(66f, 10f, 10f));

        registry.KeyValue(Mover, EntityNames.FuncMovelinear, "origin", "0 0 0");
        foreach (var (key, value) in keys)
            registry.KeyValue(Mover, EntityNames.FuncMovelinear, key, value);
        rig.Entity.BindThinks(rig.Thinks);
        registry.Spawn(Mover, EntityNames.FuncMovelinear);
        return rig;
    }

    [Fact]
    public void It_plays_its_start_sound_as_it_moves_and_its_stop_sound_a_tenth_after()
    {
        var rig = Spawned(("StartSound", "Mover.Start"), ("StopSound", "Mover.Stop"));

        rig.Input("Open");
        Assert.Equal(new[] { "Mover.Start" }, rig.Played);

        rig.Entity.OnMoveDone();
        Assert.True(rig.Thinks.IsScheduled(Mover, "MoveLinearStopSound"));

        rig.Entity.OnThink("MoveLinearStopSound");
        Assert.Equal(new[] { "Mover.Start", "Mover.Stop" }, rig.Played);
        Assert.Equal(new[] { "Mover.Start" }, rig.Stopped);
    }

    [Fact]
    public void Open_does_nothing_at_the_open_end()
    {
        var rig = Spawned();
        rig.Origin = new Vector(64f, 0f, 0f);

        rig.Input("Open");

        Assert.Empty(rig.Moves);
    }

    [Fact]
    public void SetPosition_is_unclamped_and_nothing_when_already_there()
    {
        var rig = Spawned();

        rig.Input("SetPosition", "2", 2f);
        Assert.Equal(new Vector(128f, 0f, 0f), rig.Moves.Single().To);

        rig.Origin = new Vector(128f, 0f, 0f);
        rig.Input("SetPosition", "2", 2f);
        Assert.Single(rig.Moves);
    }

    [Fact]
    public void SetSpeed_re_aims_a_move_under_way()
    {
        var rig = Spawned(("speed", "50"));
        rig.Input("Open");

        rig.Input("SetSpeed", "200", 200f);

        Assert.Equal((new Vector(64f, 0f, 0f), 200f), rig.Moves[^1]);
    }

    [Fact]
    public void BlockDamage_crushes_what_is_in_the_way()
    {
        var rig = Spawned(("BlockDamage", "40"));

        rig.Entity.OnBlocked(Blocker);

        var hurt = rig.Engine.Last("Damage.Apply")!.Value;
        Assert.Equal((Blocker, Mover, 40f, DamageType.Crush),
                     (hurt.ArgAs<EntityHandle>(0), hurt.ArgAs<EntityHandle>(1), hurt.ArgAs<float>(2), hurt.ArgAs<DamageType>(3)));
    }

    [Fact]
    public void Without_BlockDamage_a_block_hurts_nothing()
    {
        var rig = Spawned();

        rig.Entity.OnBlocked(Blocker);

        Assert.False(rig.Engine.Called("Damage.Apply"));
    }
}
