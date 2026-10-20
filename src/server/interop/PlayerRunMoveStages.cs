
namespace SourceSharp.Abi;

/// <summary>
/// The server command loop's way to native: one side's <c>PlayerRunMove</c>
/// stages, as <see cref="PlayerCommandQueue"/> calls them.
///
/// <para>
/// It was the RunMove half of <see cref="MovementRunner"/>, in Shared.Interop,
/// so both sides built a delegate over a server-only slot and only the
/// server's <see cref="PlayerCommandQueue"/> ever called it; the client runs
/// <see cref="MovementRunner.ClientLoop"/>. SideAuditTests' transitive walk
/// named it from the client and Shared.Interop (lane side-audit, 2026-10-02).
/// One instance per server composition, built over its movement runner, so
/// two game instances share nothing here.
/// </para>
/// </summary>
public sealed class PlayerRunMoveStages
{
    internal PlayerRunMoveStages(EngineSide side, IMovementServerArea movement, IWorldServerArea world)
    {
        Api = side ?? throw new ArgumentNullException(nameof(side));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    // That side's movement area, whose PlayerRunMove slot the stages call.
    private readonly IMovementServerArea _movement;

    // Made on the first ask and kept, so the per-frame command loop allocates
    // no delegate.
    private Func<EntityHandle, int, int, int>? _runMove;

    /// <summary>
    /// The stages over <paramref name="movement"/>'s side, with the areas they
    /// call taken from it here once (plan_shim_areas_split phase 11).
    /// </summary>
    public static PlayerRunMoveStages For(MovementRunner movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        EngineSide side = movement.Api;
        return new PlayerRunMoveStages(side, side.Areas.Require<IMovementServerArea>(), side.Areas.Require<IWorldServerArea>());
    }

    /// <summary>The side these stages run on: the runner's.</summary>
    internal EngineSide Api { get; }

    // That side's world area, which the player's shadow traces its climb
    // through (TraceHull).
    private readonly IWorldServerArea _world;

    /// <summary>
    /// UTIL_TraceEntity on this side: the entity's hull swept from start to
    /// end (<see cref="EngineSide.EntityTraceHull"/>), which the player's
    /// shadow traces a climb through before it takes it.
    /// </summary>
    internal EntityHullTrace TraceHull(EntityHandle entity, Vector start, Vector end, Vector mins, Vector maxs,
                                       uint mask, int collisionGroup)
        => Api.EntityTraceHull(_world, entity, start, end, mins, maxs, mask, collisionGroup);

    /// <summary><c>PlayerRunMove</c> on this side, as the command loop calls it.</summary>
    internal Func<EntityHandle, int, int, int> RunMove => _runMove ??= RunMoveStage;

    private int RunMoveStage(EntityHandle player, int index, int stage) => _movement.PlayerRunMove(player.Value, index, stage);
}
