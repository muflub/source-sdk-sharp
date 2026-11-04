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
/// move mid-round.
/// </para>
/// </summary>
public sealed class ServerMoveConstants
{
    private readonly MoveConVars _convars;
    private readonly Func<MovementParams> _params;
    private readonly IEngineEnvironment _environment;
    private readonly Func<float> _engineTime;

    public ServerMoveConstants(MoveConVars convars, Func<MovementParams> parameters,
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
            Friction = _convars.Friction.Value,
            Accelerate = _convars.Accelerate.Value,
            AirAccelerate = _convars.AirAccelerate.Value,
            StopSpeed = _convars.StopSpeed.Value,
            MaxVelocity = _convars.MaxVelocity.Value,
            MaxSpeed = _convars.MaxSpeed.Value,
            Bounce = _convars.Bounce.Value,
            RollAngle = _convars.RollAngle.Value,
            RollSpeed = _convars.RollSpeed.Value,
            NoClipSpeed = _convars.NoClipSpeed.Value,
            NoClipAccelerate = _convars.NoClipAccelerate.Value,
            SpecSpeed = _convars.SpecSpeed.Value,
            SpecAccelerate = _convars.SpecAccelerate.Value,
            AirSpeedCap = p.AirSpeedCap,
            DuckSpeedScale = p.DuckSpeedScale,
            Optimizations = Optimizations,
            SpecNoClip = _convars.SpecNoClip.Value,
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
                                      ModelScale: 1f, CurrentGravity: _convars.Gravity.Value);
    }
}

/// <summary>The convars a command's constants read, built by the container.</summary>
public sealed class MoveConVars(SvFrictionConVar friction, SvAccelerateConVar accelerate,
                                SvAirAccelerateConVar airAccelerate, SvStopSpeedConVar stopSpeed,
                                SvMaxVelocityConVar maxVelocity, SvMaxSpeedConVar maxSpeed, SvBounceConVar bounce,
                                SvRollAngleConVar rollAngle, SvRollSpeedConVar rollSpeed,
                                SvNoclipSpeedConVar noClipSpeed, SvNoclipAccelerateConVar noClipAccelerate,
                                SvSpecSpeedConVar specSpeed, SvSpecAccelerateConVar specAccelerate,
                                SvSpecNoclipConVar specNoClip, SvGravityConVar gravity)
{
    public readonly SvFrictionConVar Friction = friction;
    public readonly SvAccelerateConVar Accelerate = accelerate;
    public readonly SvAirAccelerateConVar AirAccelerate = airAccelerate;
    public readonly SvStopSpeedConVar StopSpeed = stopSpeed;
    public readonly SvMaxVelocityConVar MaxVelocity = maxVelocity;
    public readonly SvMaxSpeedConVar MaxSpeed = maxSpeed;
    public readonly SvBounceConVar Bounce = bounce;
    public readonly SvRollAngleConVar RollAngle = rollAngle;
    public readonly SvRollSpeedConVar RollSpeed = rollSpeed;
    public readonly SvNoclipSpeedConVar NoClipSpeed = noClipSpeed;
    public readonly SvNoclipAccelerateConVar NoClipAccelerate = noClipAccelerate;
    public readonly SvSpecSpeedConVar SpecSpeed = specSpeed;
    public readonly SvSpecAccelerateConVar SpecAccelerate = specAccelerate;
    public readonly SvSpecNoclipConVar SpecNoClip = specNoClip;
    public readonly SvGravityConVar Gravity = gravity;
}
