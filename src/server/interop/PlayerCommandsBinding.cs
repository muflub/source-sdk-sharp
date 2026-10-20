using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's player-command path, managed-built (plan_engineapi_vtables
/// E17): ProcessMovement, the batched usercmd decode and run, and the surface
/// notice. These are the Entry.cs thunks' server bodies, moved. Movement and
/// the run fail to 0 and the surface notice is void, so the generated catch
/// suits them. A decode that faults answers -1, so it keeps its own catch.
/// </summary>
/// <remarks>
/// <paramref name="movement"/> is the composition's own movement runner
/// (plan_abi_hotreload D8): the movement crossing, the per-command stages and
/// the player shadow all run on the side it was built over.
/// </remarks>
public sealed unsafe class PlayerCommandsBinding(Crossings crossings, ILog log, ISideModule module, IServerModule game,
                                                  MovementRunner movement, MovementRules rules,
                                                  PlayerRunMoveStages stages, PlayerCommandQueue commands,
                                                  Func<bool> shadowEnabled) : IManagedPlayerCommands
{
    // The module's three movement decisions, built once (statics to zero):
    // handed to the loop for each run and taken back after, so no closure
    // over this module is left in a loop that outlives it.
    private readonly MovementRules _rules = rules;

    // The command loop's PlayerRunMove stages, over the same side as the
    // runner (they were MovementRunner's, which both sides build).
    private readonly PlayerRunMoveStages _stages = stages;

    /// <summary>
    /// The binding over <paramref name="game"/>'s movement decisions and
    /// <paramref name="movement"/>'s stages, which are built here once (phase
    /// 11 review: a constructor only keeps what it is handed).
    /// </summary>
    public static PlayerCommandsBinding For(Crossings crossings, ILog log, ISideModule module, IServerModule game,
                                            MovementRunner movement, PlayerCommandQueue commands, Func<bool> shadowEnabled)
        => new(crossings, log, module, game, movement,
               new MovementRules(
                   r => game.GetJumpScale(in r),
                   (i, f, sp, d, m) => game.GetFrictionScale(i, f, sp, d, m),
                   (i, ms, d, m) => game.GetMaxSpeedScale(i, ms, d, m)),
               PlayerRunMoveStages.For(movement), commands, shadowEnabled);

    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    /// <summary>
    /// The WHOLE of ProcessMovement, in C#. Unlike the thunk this replaces,
    /// the three movement decisions are pointed at this module only for the
    /// call: the runner hands them to its loop and puts the previous rules
    /// back after, so no closure over this module is left in a loop that
    /// outlives it, and a nested crossing's rules stay intact.
    /// </summary>
    public int MoveProcessMovement(void* a0)
    {
        Enter(CrossingOp.ServerMoveProcessMovement);
        if (a0 == null) return 0;

        return movement.ProcessMovement((MoveStateBlob*)a0, _rules);
    }

    /// <summary>The frame's queue, run: one crossing per player per frame.</summary>
    public int PlayerRunCommands(uint a0, int a1)
    {
        Enter(CrossingOp.ServerPlayerRunCommands);
        return commands.Run(new EntityHandle(a0), a1, _stages, module, game, shadowEnabled());
    }

    /// <summary>One packet, whole: decoded, queued, and answered in run order.</summary>
    public int PlayerDecodeCommands(uint a0, void* a1, int a2, int a3, int a4, int a5, int a6, int a7,
                                    void* a8, int a9, void* a10)
    {
        Enter(CrossingOp.ServerPlayerDecodeCommands);
        try
        {
            if (a1 == null || a8 == null || a10 == null || a3 < 0 || a9 < 0)
                return -1;

            int n = commands.DecodeAndQueue(
                new EntityHandle(a0), new ReadOnlySpan<byte>(a1, (a3 + 7) / 8),
                a2, a3, a4, a5, a6, a7 != 0,
                new Span<DecodedUserCmd>(a8, a9), out int bitsRead);
            *(int*)a10 = bitsRead;
            return n;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedPlayerCommands.PlayerDecodeCommands", e);
            return -1;
        }
    }

    public void OnPlayerSurfaceChanged(uint a0, int a1)
    {
        Enter(CrossingOp.ServerOnPlayerSurfaceChanged);
        game.OnPlayerSurfaceChanged(new EntityHandle(a0), a1);
    }
}
