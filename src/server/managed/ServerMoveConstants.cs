using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The server's movement constants, read where they are declared
/// (plan_server_native_v2 T4g). The 46 words shell_movement.cpp's
/// <c>FillConstants</c> wrote into every server command's state, each a
/// value this side already owns: the fourteen convars MoveVars.cs declares
/// (read back natively through CManagedConVarRef), the jump height, air cap,
/// duck scale and hulls the movement params registered, sv_gravity
/// (<c>GetCurrentGravity()</c>), the two globals, and the engine's wall clock.
///
/// <para>
/// Read fresh per command, as native did: every convar is one an operator can
/// move mid-round. A convar this build does not declare reads 0 (false), which
/// is what an unresolved CManagedConVarRef answered.
/// </para>
/// </summary>
public sealed class ServerMoveConstants
{
    private readonly ConVarRegistry _convars;
    private readonly Func<MovementParams> _params;
    private readonly IEngineEnvironment _environment;
    private readonly Func<float> _engineTime;

    public ServerMoveConstants(ConVarRegistry convars, Func<MovementParams> parameters,
                               IEngineEnvironment environment, Func<float> engineTime)
    {
        _convars = convars ?? throw new ArgumentNullException(nameof(convars));
        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _engineTime = engineTime ?? throw new ArgumentNullException(nameof(engineTime));
    }

    /// <summary><c>g_bMovementOptimizations</c>: shell_movement.cpp's, a constant true.</summary>
    public const bool Optimizations = true;

    /// <summary>One command's constants, the jump height, the clock and sv_gravity.</summary>
    public MoveFrameConstants Read()
    {
        MovementParams p = _params();
        Globals globals = _environment.Globals;

        var constants = new MoveConstants
        {
            Friction = Float<SvFrictionConVar>(),
            Accelerate = Float<SvAccelerateConVar>(),
            AirAccelerate = Float<SvAirAccelerateConVar>(),
            StopSpeed = Float<SvStopSpeedConVar>(),
            MaxVelocity = Float<SvMaxVelocityConVar>(),
            MaxSpeed = Float<SvMaxSpeedConVar>(),
            Bounce = Float<SvBounceConVar>(),
            RollAngle = Float<SvRollAngleConVar>(),
            RollSpeed = Float<SvRollSpeedConVar>(),
            NoClipSpeed = Float<SvNoclipSpeedConVar>(),
            NoClipAccelerate = Float<SvNoclipAccelerateConVar>(),
            SpecSpeed = Float<SvSpecSpeedConVar>(),
            SpecAccelerate = Float<SvSpecAccelerateConVar>(),
            AirSpeedCap = p.AirSpeedCap,
            DuckSpeedScale = p.DuckSpeedScale,
            Optimizations = Optimizations,
            SpecNoClip = _convars.Get<SvSpecNoclipConVar>()?.Value ?? false,
            MaxClients = globals.IsAvailable ? globals.MaxClients : 0,
            TickInterval = globals.IsAvailable ? globals.IntervalPerTick : 0f,
            View = p.View,
            HullMin = p.HullMin,
            HullMax = p.HullMax,
            DuckHullMin = p.DuckHullMin,
            DuckHullMax = p.DuckHullMax,
            DuckView = p.DuckView,
            ObsHullMin = p.ObsHullMin,
            ObsHullMax = p.ObsHullMax,
            DeadViewHeight = p.DeadViewHeight,
        };

        return new MoveFrameConstants(constants, p.JumpHeight, _engineTime(),
                                      ModelScale: 1f, CurrentGravity: Float<SvGravityConVar>());
    }

    private float Float<T>() where T : FloatConVar => _convars.Get<T>()?.Value ?? 0f;
}
