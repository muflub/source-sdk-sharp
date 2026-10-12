//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Spins something up to a speed and holds it there — dm_lockdown's fan is one,
/// driven through a phys_hinge that constrains it to its axis.
///
/// The spin-up is the part that is managed. Working out what torque to apply to
/// reach a speed is a feedback loop that runs every physics tick and stays
/// native; deciding that the fan takes four seconds to get there is a game
/// decision that changes once.
/// </summary>
[ScriptedEntity(EntityNames.PhysMotor)]
public sealed class PhysMotor : PhysicsForceEntity
{
    public PhysMotor(IGameClock clock, IEngineMotionControllers motion)
        : base(motion)
    {
        Clock = clock;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;
    /// <summary>What a map may set on this entity. See <see cref="PhysMotorVars"/>.</summary>
    [ScriptVars] public PhysMotorVars PhysMotorVars = new();

    private const int StartOnFlag = 1;

    /// <summary>
    /// The spin axis, computed once in OnSpawn from the axis helper — which a
    /// hot reload does not run again — so it is <c>[SaveField]</c>.
    /// Uncarried, a running fan came back pushing its motor along (0,0,0):
    /// a silent stop whose only symptom was a fan coasting (stock saves
    /// m_vecAxis; phys_controller.cpp's DATADESC).
    /// </summary>
    [SaveField] private Vector _direction;

    [SaveField] private float _currentSpeed;
    private float _acceleration;

    /// <summary>How fast the ramp climbs, degrees per second per second.</summary>
    internal float Acceleration => _acceleration;

    /// <summary>Sets what the map would have, for a test that has no map.</summary>
    internal void Configure(float targetSpeed, float spinUp)
    {
        PhysMotorVars.TargetSpeed = targetSpeed;
        PhysMotorVars.SpinUp = spinUp;
    }
    private float _lastRampTime;

    /// <summary>Stock's m_fOn: a hot reload does not re-run the spawn flag read.</summary>
    [SaveField] private bool _running;

    protected override MotionControllerKind Kind => MotionControllerKind.Motor;

    public override void OnSpawn()
    {
        Vector line = PhysMotorVars.Axis - Origin;

        // Stock self-destructs on a bad axis rather than spinning about nothing,
        // and says so. A motor that silently did nothing would look like the
        // attached prop being stuck.
        if (line.Length <= 1f)
        {
            Log.Warning($"[SourceSharp] phys_motor '{Name}' has no valid axis helper; removing itself\n");
            Remove();
            return;
        }

        _direction = SnapToAxis(line.Normalized);
        _running = (SpawnFlags & StartOnFlag) != 0;

        base.OnSpawn();
    }

    protected override void OnAttached()
    {
        RecalculateAcceleration();
        _lastRampTime = Clock.Now;

        // Applied even when stopped, so the axis and torque ceiling are known
        // before anything asks the motor to turn.
        Push();

        if (_running)
            SetNextThink(0f);
    }

    public override void OnThink()
    {
        base.OnThink();

        // No "on" gate, as stock's Think has none: SetSpeed schedules this
        // think and the ramp runs whether or not the motor was started on
        // (phys_controller.cpp:825-837; A27). TurnOff stops the thinking.
        if (Controller is null)
            return;

        float now = Clock.Now;
        _currentSpeed = Approach(PhysMotorVars.TargetSpeed, _currentSpeed, _acceleration * (now - _lastRampTime));
        _lastRampTime = now;

        Push();

        // Thinking stops once it is up to speed. The controller keeps holding it
        // there without any further help, which is the whole point of it being
        // native.
        if (_currentSpeed != PhysMotorVars.TargetSpeed)
            SetNextThink(0f);
    }

    [ScriptedEvent("SetSpeed")]
    private void OnSetSpeed(float speed)
    {
        if (PhysMotorVars.TargetSpeed == speed)
            return;

        PhysMotorVars.TargetSpeed = speed;
        RecalculateAcceleration();
        Resume();
    }

    [ScriptedEvent("TurnOn")]
    private void OnTurnOn()
    {
        _running = true;
        Resume();
    }

    /// <summary>
    /// Stops driving. Not the same as a target speed of zero, which would brake:
    /// stock leaves the controller in place with nothing asked of it, so the fan
    /// coasts down against its own friction.
    /// </summary>
    [ScriptedEvent("TurnOff")]
    private void OnTurnOff()
    {
        _running = false;
        _currentSpeed = 0f;

        Push();
        StopThinking();
    }

    private void Resume()
    {
        _lastRampTime = Clock.Now;
        SetNextThink(0f);
    }

    private void Push()
        => Controller?.SetMotor(_direction, _currentSpeed, PhysMotorVars.InertiaFactor,
                                _acceleration + PhysMotorVars.AdditionalAcceleration);

    /// <summary>
    /// Always positive: it is a magnitude, and the controller decides which way
    /// to apply it from the sign of the speed error.
    /// </summary>
    internal void RecalculateAcceleration()
        => _acceleration = PhysMotorVars.SpinUp != 0f
            ? MathF.Abs(PhysMotorVars.TargetSpeed / PhysMotorVars.SpinUp)
            : MathF.Abs(PhysMotorVars.TargetSpeed);

    internal static float Approach(float target, float current, float step)
    {
        float delta = target - current;

        if (delta > step) return current + step;
        if (delta < -step) return current - step;

        return target;
    }

    /// <summary>
    /// The same snap the hinge does, for the same reason: an axis placed by eye
    /// and a thousandth off vertical makes the thing wander as well as turn.
    /// </summary>
    internal static Vector SnapToAxis(Vector direction, float epsilon = 0.002f)
    {
        float projection = 1f - epsilon;

        if (MathF.Abs(direction.X) > projection) return new Vector(MathF.Sign(direction.X), 0f, 0f);
        if (MathF.Abs(direction.Y) > projection) return new Vector(0f, MathF.Sign(direction.Y), 0f);
        if (MathF.Abs(direction.Z) > projection) return new Vector(0f, 0f, MathF.Sign(direction.Z));

        return direction;
    }
}

/// <summary>
/// The map keys <see cref="PhysMotor"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysMotorVars
{
    /// <summary>Target speed, degrees per second.</summary>
    [VarName("speed")]
    public float TargetSpeed { get; set; }

    /// <summary>Seconds to reach that speed. Zero means immediately.</summary>
    public float SpinUp { get; set; }

    /// <summary>Headroom above what the ramp needs, so the motor can fight a load.</summary>
    [VarName("addangaccel")]
    public float AdditionalAcceleration { get; set; }

    /// <summary>
    /// How hard the motor pulls, as a multiple of the object's inertia. Maps use
    /// large numbers here — dm_lockdown's fan asks for 125.
    /// </summary>
    public float InertiaFactor { get; set; } = 1f;

    /// <summary>A world POINT; the axis is the line from this entity to it.</summary>
    public Vector Axis { get; set; }
}
