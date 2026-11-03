using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// Runs a player's commands from managed code, for a client the engine never
/// runs (plan_hl2mp_ai §3.2): a bot. The engine calls ProcessUsercmds only for
/// a client with a net channel, so native's pending count stays 0 for a fake
/// client and its RunQueuedCommands does nothing; this is that gate's managed
/// twin over the same loop (<see cref="PlayerCommandQueue"/>) and the same
/// command stages, so a bot's command moves, fires and uses exactly as a
/// human's does.
/// </summary>
public interface IPlayerCommandRunner
{
    /// <summary>Queues <paramref name="cmd"/> for <paramref name="player"/>, as a packet of one command.</summary>
    void Queue(EntityHandle player, in UserCmd cmd);

    /// <summary>
    /// CStandalonePlayer::RunQueuedCommands, in managed: once per server tick
    /// per player, the tick base placed for the batch, every queued command
    /// run through the module's stages, and the frame's <c>curtime</c> and
    /// <c>frametime</c> put back afterwards, as the native batch put them back.
    /// Answers how many commands ran: 0 for an empty queue or a second call in
    /// the same tick.
    /// </summary>
    int RunQueued(EntityHandle player);
}

/// <summary>
/// <see cref="IPlayerCommandRunner"/> over a composition's command queue and
/// stages: PlayerCommandsBinding.PlayerRunCommands' two calls (the tick base,
/// the loop with the module's sink), with native's per-tick gate and clock
/// restore around them.
/// </summary>
public sealed unsafe class PlayerCommandRunner : IPlayerCommandRunner
{
    private readonly PlayerCommandQueue _queue;
    private readonly PlayerCommandStage _stage;
    private readonly Action<EntityHandle, int>? _adjustTickBase;
    private readonly IEngineEnvironment _environment;
    private readonly ISideModule? _module;
    private readonly IServerModule? _game;
    private readonly EngineSide? _side;
    private readonly Func<bool> _shadowEnabled;

    // m_nSimulationTick per player: the tick its queue last ran on.
    private readonly Dictionary<uint, int> _simulationTick = new();

    /// <param name="stage">The command stages (ServerPlayerCommands.Stage in the shipped path).</param>
    /// <param name="adjustTickBase">AdjustPlayerTimeBase for a batch; null leaves the tick base alone.</param>
    /// <param name="side">The side whose player shadow steps after each command; null steps none.</param>
    internal PlayerCommandRunner(PlayerCommandQueue queue, PlayerCommandStage stage, Action<EntityHandle, int>? adjustTickBase,
                               IEngineEnvironment environment, ISideModule? module, IServerModule? game, EngineSide? side,
                               Func<bool> shadowEnabled)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
        _adjustTickBase = adjustTickBase;
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _module = module;
        _game = game;
        _side = side;
        _shadowEnabled = shadowEnabled ?? throw new ArgumentNullException(nameof(shadowEnabled));
    }

    /// <summary>
    /// The runner over a composition's queue and command stages, as its
    /// player-command binding runs a human's, with the fake client's angle
    /// rule (<see cref="ServerPlayerCommands.FakeClientStage"/>).
    /// </summary>
    public static PlayerCommandRunner For(PlayerCommandQueue queue, ServerPlayerCommands commands, IEngineEnvironment environment,
                                          ISideModule module, IServerModule game, ModuleEngine engine, Func<bool> shadowEnabled)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(engine);
        return new PlayerCommandRunner(queue, commands.FakeClientStage, commands.AdjustTickBase, environment, module, game,
                                       engine.Api, shadowEnabled);
    }

    public void Queue(EntityHandle player, in UserCmd cmd)
    {
        UserCmd one = cmd;
        int order;
        _queue.Queue(player, &one, 1, 1, 0, &order, 1);
    }

    public int RunQueued(EntityHandle player)
    {
        Globals g = _environment.Globals;
        if (g.IsAvailable)
        {
            if (_simulationTick.TryGetValue(player.Value, out int last) && last == g.TickCount)
                return 0;
            _simulationTick[player.Value] = g.TickCount;
        }

        int count = _queue.Depth(player);
        if (count == 0)
            return 0;

        // The command stages set each command's clock; the frame's is put
        // back after the batch, as RunQueuedCommands put it back.
        float curTime = g.IsAvailable ? g.CurTime : 0f;
        float frameTime = g.IsAvailable ? g.FrameTime : 0f;
        try
        {
            _adjustTickBase?.Invoke(player, count);
            return _queue.Run(player, count, _stage, _side, _module, _game, _shadowEnabled());
        }
        finally
        {
            if (g.IsAvailable)
                g.SetClock(curTime, frameTime);
        }
    }
}
