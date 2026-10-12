namespace SourceSharp.Abi;

/// <summary>
/// The live path for ONE side: the one crossing in, the trace ABI out.
///
/// <para>
/// An instance per side's composition (plan_abi_hotreload D8). It was a static
/// class keeping both sides' loops in index-by-side arrays and reaching the
/// engine through the static <c>Engine.Current</c>, which followed whichever
/// side was active. Now each module builds its own over its
/// <see cref="ModuleEngine"/> and hands it to the bindings and the replay that
/// drive movement, so two game instances in one process share nothing here.
/// The world it builds holds the side's slot routing as a field, so a call
/// costs what <c>Engine.Current.Slots</c> did, less the side test.
/// </para>
///
/// <para>
/// The one thing it keeps is the <see cref="GameMovement"/>, because that
/// object holds a world and two scratch fields that survive a command.
/// </para>
/// </summary>
public sealed unsafe class MovementRunner
{
    private readonly MovementBridge.EngineMoveWorld _world;
    private readonly bool _isServer;
    private GameMovement? _loop;
    private long _ran;
    private long _declined;

    public MovementRunner(ModuleEngine engine)
        : this(engine, null)
    {
    }

    /// <param name="engine">This side's engine.</param>
    /// <param name="clientWorld">
    /// The client prediction's world over the engine's (<see cref="ClientLoop"/>):
    /// the client puts stock's client move helper there (lane c6-input). Null
    /// runs the loop on the engine's world itself.
    /// </param>
    public MovementRunner(ModuleEngine engine, Func<IMoveWorld, IMoveWorld>? clientWorld)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Api = engine.Api;
        _world = new MovementBridge.EngineMoveWorld(Api);
        _isServer = engine.Side == Side.Server;
        _clientWorld = clientWorld?.Invoke(_world);
    }

    private readonly IMoveWorld? _clientWorld;

    /// <summary>
    /// The side this runner moves players on, for the command loop that runs
    /// beside it: its <c>PlayerRunMove</c> stages (PlayerRunMoveStages, in
    /// Server.Interop, since only the server runs that loop) and the player
    /// shadow.
    /// </summary>
    internal EngineSide Api { get; }

    /// <summary>The world this side's loop moves through, for a fact to ask.</summary>
    internal IMoveWorld World => _world;

    /// <summary>
    /// THE CLIENT'S LOOP, for the managed replay to call directly (step 6f).
    ///
    /// <para>
    /// The same object <see cref="ProcessMovement"/> would have built for this
    /// side, on the same <c>IMoveWorld</c>, so that a client command run from
    /// the replay and a client command run through the native crossing are the
    /// same loop with the same scratch. Two loops per side would carry two
    /// copies of the two fields that survive a command.
    /// </para>
    /// </summary>
    /// <param name="constants">The convars this command runs under, read fresh
    /// per command exactly as the server's crossing reads them.</param>
    /// <param name="jumpHeight">Kept as its own property on the loop, so it is
    /// pushed as its own argument rather than smuggled through the struct.</param>
    /// <param name="engineTime">The wall clock, which CheckStuck's throttle
    /// reads and nothing else does.</param>
    public GameMovement ClientLoop(in MoveConstants constants, float jumpHeight,
                                   float engineTime)
    {
        _world.Time = engineTime;

        GameMovement loop = _loop ??= new GameMovement(_clientWorld ?? _world, default);
        loop.Constants = constants;
        loop.JumpHeight = jumpHeight;
        return loop;
    }

    /// <summary>
    /// Counts one command the CLIENT's replay ran or declined, so
    /// <see cref="CommandsRun"/> means the same thing on both sides after step
    /// 6f moved the client's driver into managed code.
    /// </summary>
    public void NoteClientCommand(bool ran)
    {
        if (ran)
            _ran++;
        else
            _declined++;
    }

    /// <summary>How many commands this side has run in C#. The liveness counter.</summary>
    public long CommandsRun => _ran;

    /// <summary>
    /// The pose of the player an observer at entity index N watches (origin,
    /// angles, velocity), or null: what FullObserverMove follows in in-eye,
    /// chase and POI. Set by the side that knows the observers (the server's
    /// module); unset, the arm is stock's bare return.
    /// </summary>
    public Func<int, (Vector Origin, Vector Angles, Vector Velocity)?>? ObserverTargetPose { get; set; }

    /// <summary>How many it declined — the three refused move types.</summary>
    public long CommandsDeclined => _declined;

    /// <summary>
    /// The entry point the native shim calls, once per command per player.
    /// A state from the other side is declined: this runner is one side's.
    /// </summary>
    /// <returns>1 when the managed loop ran, 0 when it declined.</returns>
    public int ProcessMovement(MoveStateBlob* blob) => ProcessMovement(blob, MovementRules.Stock);

    /// <summary>
    /// The run, with <paramref name="rules"/> as the loop's decisions for this
    /// call only (statics to zero: the binding hands in its module's rules, and
    /// the loop goes back to stock after, so nothing of the module outlives the
    /// call in the loop).
    /// </summary>
    public int ProcessMovement(MoveStateBlob* blob, in MovementRules rules)
    {
        if (blob is null || (blob->IsServer != 0) != _isServer)
            return 0;

        _world.Time = blob->EngineTime;

        GameMovement loop = _loop ??= new GameMovement(_world, default);
        loop.Constants = MovementBridge.ReadConstants(blob);
        loop.JumpHeight = blob->CvJumpHeight;

        MoveState state = MovementBridge.ReadState(blob);

        // FullObserverMove's target, from the side's own observer.
        if (state.ObserverMode is GameMovement.ObsModeInEye or GameMovement.ObsModeChase or GameMovement.ObsModePoi
            && ObserverTargetPose?.Invoke(state.EntIndex) is { } pose)
        {
            state.HasObserverTarget = true;
            state.ObserverTargetOrigin = pose.Origin;
            state.ObserverTargetAngles = pose.Angles;
            state.ObserverTargetVelocity = pose.Velocity;
        }

        MovementRules previous = loop.Rules;
        loop.Rules = rules;
        bool ran;
        try
        {
            ran = loop.ProcessMovement(state);
        }
        finally
        {
            loop.Rules = previous;
        }

        if (!ran)
        {
            _declined++;
            return 0;
        }

        MovementBridge.WriteState(blob, state);
        _ran++;
        return 1;
    }
}
