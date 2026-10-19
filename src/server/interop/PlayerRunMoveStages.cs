using System.Runtime.CompilerServices;

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
    public PlayerRunMoveStages(MovementRunner movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        Api = movement.Api;

        // Built once, so the per-frame command loop allocates no delegate.
        EngineSide api = Api;
        RunMove = (player, index, stage) => RunMoveVia(api, player, index, stage);
    }

    /// <summary>The side these stages run on: the runner's.</summary>
    internal EngineSide Api { get; }

    /// <summary><c>PlayerRunMove</c> on this side, as the command loop calls it.</summary>
    internal Func<EntityHandle, int, int, int> RunMove { get; }

    /// <summary>
    /// <paramref name="side"/>'s <c>PlayerRunMove</c> slot, guarded on the slot
    /// being wired. An unwired slot answers 0, which the command loop reads as
    /// the refusal it is.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RunMoveVia(EngineSide side, EntityHandle player, int index, int stage)
        => side.ServerAttached ? side.ServerShims.MovementServer.PlayerRunMove(player.Value, index, stage) : 0;
}
