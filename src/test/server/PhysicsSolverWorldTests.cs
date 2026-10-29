using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What the collision rules are told an entity moves as. Stock's movers are
/// MOVETYPE_PUSH, the "AI movers" whose shadows pass through the world and
/// what is pinned (ShouldCollide, physics.cpp); native reports
/// MOVETYPE_VPHYSICS for any entity holding an object, so a class that moves
/// as a pusher is answered as one. Found live beside stock (2026-10-26): a
/// func_plat's new shadow collided with the floor it sat on and held the plat
/// where it was, and a crate on it never came down.
/// </summary>
public class PhysicsSolverWorldTests
{
    private static readonly EntityHandle Plat = new(0x0004_0351u);

    private static SolverEntity Facts(Func<EntityHandle, bool>? movesAsPusher)
    {
        var engine = new RecordingEngine { Strict = false };
        engine.Returns("Entities.GetEngineBlock", new byte[EngineBlock.SizeBytes])
              .Returns("Networking.EntityGetState.moveType", MoveType.VPhysics);
        var world = new EnginePhysicsSolverWorld(engine, engine, engine, movesAsPusher);

        Assert.True(world.TryEntity(Plat, out SolverEntity facts));
        return facts;
    }

    [Fact]
    public void A_class_that_moves_as_a_pusher_is_a_pusher_to_the_collision_rules()
        => Assert.Equal((int)MoveType.Push, Facts(h => h == Plat).MoveType);

    [Fact]
    public void Anything_else_keeps_the_movetype_native_reports()
        => Assert.Equal((int)MoveType.VPhysics, Facts(_ => false).MoveType);
}
