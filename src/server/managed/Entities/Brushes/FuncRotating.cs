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
/// CFuncRotating (bmodels.cpp): a brush that spins. Start, Stop, Toggle and
/// Use set the speed it heads for; with SF_BRUSH_ACCDCC it spins up and down
/// by its fan friction a tenth of a second at a time (reversing through
/// zero), otherwise it changes at once. StopAtStartPos brings it to rest at
/// the angles it spawned at. SF_BRUSH_HURT makes its touch hurt and throw by
/// its speed, and what blocks it takes its dmg. Its running noise
/// (<c>message</c>) starts quiet and ramps its pitch and volume with the speed.
/// </summary>
[ScriptedEntity(EntityNames.FuncRotating, ScriptedEntityKind.Brush)]
public sealed class FuncRotating : ScriptedEntity
{
    public FuncRotating(IEnginePrecache precache, IEngineSound sound, IEngineDamage damage, IEngineEnvironment environment)
    {
        Precache = precache;
        Sound = sound;
        Damage = damage;
        Environment = environment;
    }

    private readonly IEnginePrecache Precache;
    private readonly IEngineSound Sound;
    private readonly IEngineDamage Damage;
    private readonly IEngineEnvironment Environment;

    private float Tick => Environment.Globals is { IsAvailable: true } g ? g.IntervalPerTick : ServerGameDll.DefaultTickInterval;

    /// <summary>Stock's Spawn makes it SOLID_VPHYSICS.</summary>
    protected override SolidType BrushSolidType => SolidType.VPhysics;

    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="FuncRotatingVars"/>.</summary>
    [ScriptVars] public FuncRotatingVars Vars = new();

    /// <summary>
    /// func_rotating's own spawnflags (util.h, bmodels.cpp), and the cautionary
    /// case for the whole set: bits 4 and 8 are THIS entity's Z and X axes, not
    /// <see cref="RotationAxisFlag"/>'s 64 and 128, which belong to the entities
    /// that go through <c>CBaseToggle::AxisDir</c>. Z is checked before X, and
    /// neither leaves yaw.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0,
        StartOn = 1,
        Backwards = 2,
        AxisZ = 4,
        AxisX = 8,
        Accelerates = 16,
        Hurts = 32,
        NotSolid = 64,
        SmallRadius = 128,
        MediumRadius = 256,
        LargeRadius = 512,
    }

    /// <summary>Which of stock's move functions runs at the next move time.</summary>
    private enum Step { Rotate, SpinUp, SpinDown, Reverse }

    /// <summary>m_vecMoveAng: the unit turn, its sign the backwards flag.</summary>
    [SaveField] private Vector _moveAngles;

    /// <summary>m_flSpeed and m_flTargetSpeed.</summary>
    [SaveField] private float _speed;

    [SaveField] private float _targetSpeed;

    [SaveField] private bool _reversed;

    [SaveField] private bool _stopAtStartPos;

    /// <summary>m_angStart: where StopAtStartPos brings it to rest.</summary>
    [SaveField] private Vector _startAngles;

    [SaveField] private Step _step;

    /// <summary>m_flFanFriction: the key's percent, as a fraction.</summary>
    [SaveField] private float _friction;

    [SaveField] private float _volume;

    [SaveField] private string _noise = string.Empty;

    [SaveField] private int _soundLevel;

    private int _patch = -1;

    private const string MoveContext = "RotatingMove", StartContext = "RotatingStart";

    /// <summary>FANPITCHMIN and FANPITCHMAX.</summary>
    private const float PitchMin = 30f, PitchMax = 100f;

    /// <summary>CHAN_STATIC; SND_CHANGE_VOL | SND_CHANGE_PITCH.</summary>
    private const int ChannelStatic = 6, ChangeVolumeAndPitch = 1 | 2;

    public override void OnSpawn()
    {
        // KeyValue's fanfriction and Volume, then Spawn's defaults.
        _friction = Vars.FanFriction / 100f;
        if (_friction == 0f)
            _friction = 1f;

        _volume = Math.Clamp(Vars.Volume / 10f, 0f, 1f);
        if (_volume == 0f)
            _volume = 1f;

        // The radius flags' attenuations, as ATTN_TO_SNDLVL makes them a level.
        float attenuation = Flags.HasFlag(SpawnFlag.SmallRadius) ? 2.0f
                          : Flags.HasFlag(SpawnFlag.MediumRadius) ? 1.25f
                          : 0.8f;
        _soundLevel = (int)(50f + 20f / attenuation);

        _moveAngles = Flags.HasFlag(SpawnFlag.AxisZ) ? new Vector(0f, 0f, 1f)
                    : Flags.HasFlag(SpawnFlag.AxisX) ? new Vector(1f, 0f, 0f)
                    : new Vector(0f, 1f, 0f);
        if (Flags.HasFlag(SpawnFlag.Backwards))
            _moveAngles *= -1f;

        bool notSolid = Flags.HasFlag(SpawnFlag.NotSolid);
        if (notSolid)
            SetSolidity(BrushSolidType, SolidFlags.NotSolid);
        else
            SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        Vars.MaxSpeed = MathF.Abs(Vars.MaxSpeed);
        if (Vars.MaxSpeed == 0f)
            Vars.MaxSpeed = 100f;

        // "leave a magic delay for client to start up"
        if (Flags.HasFlag(SpawnFlag.StartOn))
            SetContextThink(StartContext, 0.2f);

        _speed = 0f;

        // CFuncRotating::Precache.
        _noise = Vars.Message.Length > 0 ? Vars.Message : "DoorSound.Null";
        Precache.PrecacheSound(_noise);

        // CreateVPhysics: a shadow that follows it, which props meet, unless it is not solid.
        if (!notSolid)
            InitShadowPhysics();
        _startAngles = SpawnAngles;

        if (Vars.SolidBsp)
            SetSolidity(SolidType.Bsp, notSolid ? SolidFlags.NotSolid : SolidFlags.None);
    }

    /// <summary>SF_BRUSH_HURT: its touch is every frame, for anything that takes damage.</summary>
    public override bool TouchEveryTick => Flags.HasFlag(SpawnFlag.Hurts);

    public override bool TouchedByAnything => Flags.HasFlag(SpawnFlag.Hurts);

    /// <summary>CFuncRotating::HurtTouch: a crush by a tenth of its turn rate, and a throw as hard away from its middle.</summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (!Flags.HasFlag(SpawnFlag.Hurts) || !(FindByHandle<ScriptedEntity>(toucher)?.TakesDamage ?? true))
            return;

        float damage = (_moveAngles * _speed).Length / 10f;
        Damage.Apply(toucher, Handle, damage, DamageType.Crush);

        Vector away = Entities.GetOrigin(toucher) - Entities.GetWorldSpaceCenter(Handle);
        float length = away.Length;
        Entities.SetVelocity(toucher, length > 0f ? away * (damage / length) : Vector.Zero);
    }

    /// <summary>CFuncRotating::Blocked: dmg's crush to what is in the way.</summary>
    public override void OnBlocked(EntityHandle other) => Damage.Apply(other, Handle, Vars.BlockDamage, DamageType.Crush);

    [ScriptedEvent("Use")] private void OnUsed() => SetTargetSpeed(_speed != 0f ? 0f : Vars.MaxSpeed);

    [ScriptedEvent] private void OnStart() { _stopAtStartPos = false; SetTargetSpeed(Vars.MaxSpeed); }

    [ScriptedEvent] private void OnStop() { _stopAtStartPos = false; SetTargetSpeed(0f); }

    [ScriptedEvent] private void OnToggle() => SetTargetSpeed(_speed > 0f ? 0f : Vars.MaxSpeed);

    [ScriptedEvent] private void OnReverse() { _stopAtStartPos = false; _reversed = !_reversed; SetTargetSpeed(_speed); }

    [ScriptedEvent] private void OnStartForward() { _reversed = false; SetTargetSpeed(Vars.MaxSpeed); }

    [ScriptedEvent] private void OnStartBackward() { _stopAtStartPos = false; _reversed = true; SetTargetSpeed(Vars.MaxSpeed); }

    /// <summary>InputSetSpeed: a fraction of its top speed, its sign the direction.</summary>
    [ScriptedEvent]
    private void OnSetSpeed(float speed)
    {
        _stopAtStartPos = false;
        _reversed = speed < 0f;
        SetTargetSpeed(Math.Clamp(MathF.Abs(speed), 0f, 1f) * Vars.MaxSpeed);
    }

    /// <summary>InputStopAtStartPos: head for rest, and stop at the spawn angles.</summary>
    [ScriptedEvent]
    private void OnStopAtStartPos()
    {
        _stopAtStartPos = true;
        SetTargetSpeed(0f);
        SetContextThink(MoveContext, NextMoveInterval);
    }

    /// <summary>GetNextMoveInterval: a tick while it is stopping at its start, else a tenth.</summary>
    private float NextMoveInterval => _stopAtStartPos ? Tick : 0.1f;

    /// <summary>CFuncRotating::SetTargetSpeed: at once, or by steps with SF_BRUSH_ACCDCC.</summary>
    private void SetTargetSpeed(float speed)
    {
        speed = MathF.Abs(speed);
        if (_reversed)
            speed = -speed;
        _targetSpeed = speed;

        if (!Flags.HasFlag(SpawnFlag.Accelerates))
        {
            UpdateSpeed(_targetSpeed);
            _step = Step.Rotate;
        }
        else if ((_speed > 0f && _targetSpeed < 0f) || (_speed < 0f && _targetSpeed > 0f))
        {
            _step = Step.Reverse;
        }
        else if (MathF.Abs(_speed) < MathF.Abs(_targetSpeed))
        {
            _step = Step.SpinUp;
        }
        else if (MathF.Abs(_speed) > MathF.Abs(_targetSpeed))
        {
            _step = Step.SpinDown;
        }
        else
        {
            _step = Step.Rotate;
        }

        SetContextThink(MoveContext, NextMoveInterval);
    }

    public override void OnThink(string context)
    {
        if (context == StartContext)
        {
            OnUsed();
            return;
        }

        if (context != MoveContext)
            return;

        switch (_step)
        {
            case Step.SpinUp: SpinUpMove(); break;
            case Step.SpinDown: SpinDownMove(); break;
            case Step.Reverse: ReverseMove(); break;
            default: RotateMove(); break;
        }
    }

    private void SpinUpMove()
    {
        bool done = false;
        float newSpeed = MathF.Abs(_speed) + 0.2f * Vars.MaxSpeed * _friction;
        if (MathF.Abs(newSpeed) >= MathF.Abs(_targetSpeed))
        {
            newSpeed = _targetSpeed;
            done = !_stopAtStartPos;
        }
        else if (_targetSpeed < 0f)
        {
            newSpeed = -newSpeed;
        }

        UpdateSpeed(newSpeed);

        if (done)
        {
            _step = Step.Rotate;
            RotateMove();
        }

        SetContextThink(MoveContext, NextMoveInterval);
    }

    private void SpinDownMove()
    {
        if (SpinDown(_targetSpeed))
        {
            _step = Step.Rotate;
            RotateMove();
        }
        else
        {
            SetContextThink(MoveContext, NextMoveInterval);
        }
    }

    private void ReverseMove()
    {
        if (SpinDown(0f))
            SetTargetSpeed(_targetSpeed);
        else
            SetContextThink(MoveContext, NextMoveInterval);
    }

    /// <summary>CFuncRotating::SpinDown: a tenth of its top speed by friction, until it reaches the target.</summary>
    private bool SpinDown(float target)
    {
        bool done = false;
        float newSpeed = MathF.Max(0f, MathF.Abs(_speed) - 0.1f * Vars.MaxSpeed * _friction);
        if (MathF.Abs(newSpeed) <= MathF.Abs(target))
        {
            newSpeed = target;
            done = !_stopAtStartPos;
        }
        else if (_speed < 0f)
        {
            newSpeed = -newSpeed;
        }

        UpdateSpeed(newSpeed);
        return done;
    }

    /// <summary>CFuncRotating::RotateMove: nothing to do unless it is stopping at its start, which it does within a tick of it.</summary>
    private void RotateMove()
    {
        if (!_stopAtStartPos)
            return;

        SetContextThink(MoveContext, NextMoveInterval);

        int axis = CheckAxis;
        float delta = AngleDelta(Component(Entities.GetAngles(Handle), axis) - Component(_startAngles, axis));
        float perTick = Component(_moveAngles * _speed, axis) * Tick;
        if (MathF.Abs(delta) < MathF.Abs(perTick))
        {
            SetTargetSpeed(0f);
            SetAngles(_startAngles);
            _stopAtStartPos = false;
        }
    }

    /// <summary>The axis StopAtStartPos measures: the turn's, in stock's order.</summary>
    private int CheckAxis => _moveAngles.X != 0f ? 0 : _moveAngles.Y != 0f ? 1 : 2;

    private static float Component(Vector v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>anglemod, then folded to (-180, 180].</summary>
    private static float AngleDelta(float degrees)
    {
        float a = degrees % 360f;
        if (a < 0f)
            a += 360f;
        return a > 180f ? a - 360f : a;
    }

    /// <summary>
    /// CFuncRotating::UpdateSpeed: clamped to its top speed; while stopping at
    /// its start, slowed so it arrives there; the noise started, ramped or
    /// stopped; and the turn written.
    /// </summary>
    private void UpdateSpeed(float newSpeed)
    {
        float oldSpeed = _speed;
        _speed = Math.Clamp(newSpeed, -Vars.MaxSpeed, Vars.MaxSpeed);

        if (_stopAtStartPos)
        {
            int axis = CheckAxis;
            float delta = AngleDelta(Component(Entities.GetAngles(Handle), axis) - Component(_startAngles, axis));

            if (newSpeed < 100f)
            {
                if (newSpeed <= 25f && MathF.Abs(delta) < 1f)
                {
                    _targetSpeed = 0f;
                    _stopAtStartPos = false;
                    _speed = 0f;
                    SetAngles(_startAngles);
                }
                else if (MathF.Abs(delta) > 90f)
                {
                    _speed = oldSpeed;
                }
                else
                {
                    float minSpeed = MathF.Max(MathF.Abs(delta), 20f);
                    _speed = oldSpeed > 0f ? minSpeed : -minSpeed;
                }
            }
        }

        if (oldSpeed == 0f && _speed != 0f)
        {
            // Starting to move: the noise, quiet and low, then ramped.
            if (_patch < 0)
                _patch = Sound.SoundPatchCreate(Handle, ChannelStatic, _noise, _soundLevel);
            Sound.SoundPatchEmit(_patch, 0.01f, PitchMin, 0, 0f, emitCaption: true);
            RampPitchVolume();
        }
        else if (oldSpeed != 0f && _speed == 0f)
        {
            if (_patch >= 0)
                Sound.SoundPatchStop(_patch);
        }
        else
        {
            RampPitchVolume();
        }

        Spin(_moveAngles * _speed);
    }

    /// <summary>CFuncRotating::RampPitchVol: volume and pitch by the share of its top speed.</summary>
    private void RampPitchVolume()
    {
        if (_patch < 0)
            return;

        float share = MathF.Abs(_speed) / Vars.MaxSpeed;
        float volume = Math.Clamp(_volume * share, 0f, 1f);
        int pitch = Math.Clamp((int)(PitchMin + (PitchMax - PitchMin) * share), 0, 255);
        if (pitch == 100)
            pitch = 99;

        Sound.SoundPatchEmit(_patch, volume, pitch, ChangeVolumeAndPitch, 0f, emitCaption: false);
    }

    public override void OnRemoved()
    {
        if (_patch >= 0)
            Sound.SoundPatchFree(_patch);
        _patch = -1;
    }
}

/// <summary>
/// The map keys <see cref="FuncRotating"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncRotatingVars
{
    public float MaxSpeed { get; set; } = 100f;

    /// <summary>How fast it spins up and down, in percent (SF_BRUSH_ACCDCC).</summary>
    public float FanFriction { get; set; }

    /// <summary>Its noise's volume, 0 to 10.</summary>
    public float Volume { get; set; }

    /// <summary>The running noise.</summary>
    [VarName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>The crush to what blocks it.</summary>
    [VarName("dmg")]
    public float BlockDamage { get; set; }

    /// <summary>SOLID_BSP rather than SOLID_VPHYSICS.</summary>
    [VarName("solidbsp")]
    public bool SolidBsp { get; set; }
}
