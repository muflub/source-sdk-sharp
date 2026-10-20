using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's player-command path, managed-built (plan_engineapi_vtables
/// E17): the batched usercmd decode and run. The run fails to 0, so the
/// generated catch suits it. A decode that faults answers -1, so it keeps its own catch. The
/// movement crossing went with plan_server_native_v2 T4g: the commands'
/// stages and the loop are managed (<see cref="ServerPlayerCommands"/>).
/// </summary>
/// <remarks>
/// <paramref name="engine"/> is the composition's own (plan_abi_hotreload
/// D8): the per-command stages and the player shadow run on its side.
/// </remarks>
public sealed unsafe class PlayerCommandsBinding(Crossings crossings, ILog log, ISideModule module, IServerModule game,
                                                  MovementRules rules, PlayerCommandQueue commands,
                                                  Func<bool> shadowEnabled, ServerPlayerCommands? serverCommands,
                                                  ModuleEngine? engine) : IManagedPlayerCommands
{
    // The module's three movement decisions, built once by For (statics to
    // zero) and handed to the server's command stages, the pack its loop runs.
    private readonly MovementRules _rules = rules;

    // The server's command stages (plan_server_native_v2 T4g).
    private readonly ServerPlayerCommands? _serverCommands = serverCommands;

    private static int Refuse(EntityHandle player, int index, int stage, in UserCmd cmd) => 0;

    /// <summary>
    /// The binding over <paramref name="game"/>'s movement decisions, which
    /// are built here once and handed to <paramref name="serverCommands"/>
    /// (phase 11 review: a constructor only keeps what it is handed).
    /// </summary>
    public static PlayerCommandsBinding For(Crossings crossings, ILog log, ISideModule module, IServerModule game,
                                            PlayerCommandQueue commands, Func<bool> shadowEnabled,
                                            ServerPlayerCommands? serverCommands = null, ModuleEngine? engine = null)
    {
        var rules = new MovementRules(
            r => game.GetJumpScale(in r),
            (i, f, sp, d, m) => game.GetFrictionScale(i, f, sp, d, m),
            (i, ms, d, m) => game.GetMaxSpeedScale(i, ms, d, m));

        if (serverCommands is not null)
            serverCommands.Rules = rules;

        return new(crossings, log, module, game, rules, commands, shadowEnabled, serverCommands, engine);
    }

    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    /// <summary>
    /// The frame's queue, run: one crossing per player per frame. The tick
    /// base is placed for the batch first, as RunQueuedCommands placed it
    /// (AdjustPlayerTimeBase), then each command runs its managed stages.
    /// </summary>
    public int PlayerRunCommands(uint a0, int a1)
    {
        Enter(CrossingOp.ServerPlayerRunCommands);
        var player = new EntityHandle(a0);
        _serverCommands?.AdjustTickBase(player, a1);

        // No command stages (a binding built without them) refuses each
        // command at its prepare, as native refused a player it had not got.
        return commands.Run(player, a1, _serverCommands?.Stage ?? new PlayerCommandStage(Refuse), engine?.Api, module, game, shadowEnabled());
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

}
