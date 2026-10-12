//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;
using System.Reflection;
using SourceSharp.Abi;

namespace SourceSharp.Client.Input;

/// <summary>
/// One convar as the ENGINE's <c>ConVar</c> holds it: the text, the float the
/// engine parsed from it (<c>(float)atof</c>, clamped to the bounds) and the
/// int it truncated that to. Writes follow <c>InternalSetFloatValue</c> and
/// <c>InternalSetIntValue</c>: equal values are no-ops, the float is kept
/// EXACT (stock's in-process readers see the float, not its text), and the
/// text is pushed to the engine.
/// </summary>
public sealed class InputCvar
{
    private readonly float? _min;
    private readonly float? _max;
    private Action<string>? _push;

    public InputCvar(string name, string value, float? min = null, float? max = null)
    {
        Name = name;
        _min = min;
        _max = max;
        String = string.Empty;
        Set(value);
    }

    public string Name { get; }
    public string String { get; private set; }
    public float Float { get; private set; }
    public int Int { get; private set; }
    public bool Bool => Int != 0;

    /// <summary>Where a computed value goes: the engine's convar, in the game. Null in a test.</summary>
    internal void BindPush(Action<string>? push) => _push = push;

    /// <summary>
    /// Told after the text changed, from either direction: stock's change
    /// callbacks (cl_thirdperson's ThirdPersonChange, joy_movement_stick's).
    /// </summary>
    public event Action<InputCvar>? Changed;

    /// <summary>A new text from outside (the console, a config, the engine's change callback).</summary>
    public void Set(string text)
    {
        float value = (float)CRuntime.Atof(text);
        if (Clamp(ref value))
            text = value.ToString("F6", CultureInfo.InvariantCulture);
        bool changed = !string.Equals(text, String, StringComparison.Ordinal);
        String = text;
        Float = value;
        Int = (int)value;
        if (changed)
            Changed?.Invoke(this);
    }

    /// <summary><c>ConVar::SetValue( float )</c>.</summary>
    public void Write(float value)
    {
        if (value == Float)
            return;
        Clamp(ref value);
        Float = value;
        Int = (int)value;
        // Round-trip text, so the engine's own float (which native readers such
        // as the third-person pass see) is this one exactly; stock's "%f" text
        // was lossy only in what it printed, never in the float.
        String = value.ToString("R", CultureInfo.InvariantCulture);
        _push?.Invoke(String);
        Changed?.Invoke(this);
    }

    /// <summary><c>ConVar::SetValue( int )</c>.</summary>
    public void Write(int value)
    {
        if (value == Int)
            return;
        float f = value;
        if (Clamp(ref f))
            value = (int)f;
        Float = f;
        Int = value;
        String = value.ToString(CultureInfo.InvariantCulture);
        _push?.Invoke(String);
        Changed?.Invoke(this);
    }

    private bool Clamp(ref float value)
    {
        if (_min is float min && value < min)
        {
            value = min;
            return true;
        }
        if (_max is float max && value > max)
        {
            value = max;
            return true;
        }
        return false;
    }

    public override string ToString() => $"{Name} = {String}";
}

/// <summary>
/// Every input convar, by name, as <see cref="InputCvar"/>s built from the
/// <see cref="InputConVarDeclaration"/> classes' own attributes (one source
/// for the defaults and bounds). In the game, <see cref="Bind"/> ties each to
/// its registered declaration; in a test, values are <see cref="InputCvar.Set"/>
/// directly.
/// </summary>
public sealed class InputCvars
{
    private readonly Dictionary<string, InputCvar> _byName = new(StringComparer.OrdinalIgnoreCase);

    public InputCvars()
    {
        foreach (Type type in typeof(InputConVarDeclaration).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(InputConVarDeclaration).IsAssignableFrom(type))
                continue;
            if (type.GetCustomAttribute<ConVarAttribute>() is ConVarAttribute attr)
                _byName[attr.Name] = new InputCvar(attr.Name, attr.DefaultValue,
                                                   float.IsNaN(attr.Min) ? null : attr.Min,
                                                   float.IsNaN(attr.Max) ? null : attr.Max);
        }

        // in_main.cpp
        AngleSpeedKey = this["cl_anglespeedkey"];
        YawSpeed = this["cl_yawspeed"];
        PitchSpeed = this["cl_pitchspeed"];
        PitchDown = this["cl_pitchdown"];
        PitchUp = this["cl_pitchup"];
        SideSpeed = this["cl_sidespeed"];
        UpSpeed = this["cl_upspeed"];
        ForwardSpeed = this["cl_forwardspeed"];
        BackSpeed = this["cl_backspeed"];
        LookSpring = this["lookspring"];
        LookStrafe = this["lookstrafe"];
        Joystick = this["joystick"];
        ThirdPersonPlatformer = this["thirdperson_platformer"];
        ThirdPersonScreenSpace = this["thirdperson_screenspace"];
        NoclipDuringPause = this["sv_noclipduringpause"];
        UseKeyboardSampleTime = this["in_usekeyboardsampletime"];

        // in_mouse.cpp
        MPitch = this["m_pitch"];
        MFilter = this["m_filter"];
        Sensitivity = this["sensitivity"];
        MSide = this["m_side"];
        MYaw = this["m_yaw"];
        MForward = this["m_forward"];
        CustomAccel = this["m_customaccel"];
        CustomAccelScale = this["m_customaccel_scale"];
        CustomAccelMax = this["m_customaccel_max"];
        CustomAccelExponent = this["m_customaccel_exponent"];
        RawInput = this["m_rawinput"];
        RawInputOnetimeReset = this["m_rawinput_onetime_reset"];
        LimitedCaptureWorkaround = this["m_limitedcapture_workaround"];
        MouseLook = this["cl_mouselook"];
        MouseEnable = this["cl_mouseenable"];

        // in_joystick.cpp
        JoyName = this["joy_name"];
        JoyAdvanced = this["joy_advanced"];
        JoyAdvAxisX = this["joy_advaxisx"];
        JoyAdvAxisY = this["joy_advaxisy"];
        JoyAdvAxisZ = this["joy_advaxisz"];
        JoyAdvAxisR = this["joy_advaxisr"];
        JoyAdvAxisU = this["joy_advaxisu"];
        JoyAdvAxisV = this["joy_advaxisv"];
        JoyForwardThreshold = this["joy_forwardthreshold"];
        JoySideThreshold = this["joy_sidethreshold"];
        JoyPitchThreshold = this["joy_pitchthreshold"];
        JoyYawThreshold = this["joy_yawthreshold"];
        JoyForwardSensitivity = this["joy_forwardsensitivity"];
        JoySideSensitivity = this["joy_sidesensitivity"];
        JoyPitchSensitivity = this["joy_pitchsensitivity"];
        JoyYawSensitivity = this["joy_yawsensitivity"];
        JoyResponseMove = this["joy_response_move"];
        JoyResponseLook = this["joy_response_look"];
        JoyLowEnd = this["joy_lowend"];
        JoyLowMap = this["joy_lowmap"];
        JoyAccelScale = this["joy_accelscale"];
        JoyAccelMax = this["joy_accelmax"];
        JoyAutoAimDampenRange = this["joy_autoaimdampenrange"];
        JoyAutoAimDampen = this["joy_autoaimdampen"];
        JoyVehicleTurnLowEnd = this["joy_vehicle_turn_lowend"];
        JoyVehicleTurnLowMap = this["joy_vehicle_turn_lowmap"];
        JoyDisplayInput = this["joy_display_input"];
        JoyWingmanHack = this["joy_wingmanwarrior_turnhack"];
        JoyAutoSprint = this["joy_autosprint"];
        JoyInvertY = this["joy_inverty"];
        JoyMovementStick = this["joy_movement_stick"];
        JoyXControllerCfgLoaded = this["joy_xcontroller_cfg_loaded"];
        JoyPegged = this["joy_pegged"];
        JoyVirtualPeg = this["joy_virtual_peg"];
        JoyAccelFilter = this["joy_accel_filter"];

        // in_steamcontroller.cpp
        ScYawSensitivity = this["sc_yaw_sensitivity"];
        ScPitchSensitivity = this["sc_pitch_sensitivity"];
        ScLookSensitivityScale = this["sc_look_sensitivity_scale"];

        // in_camera.cpp, cam_thirdperson.cpp
        CamCommand = this["cam_command"];
        CamSnapTo = this["cam_snapto"];
        CamIdealLag = this["cam_ideallag"];
        CamIdealDelta = this["cam_idealdelta"];
        CamIdealYaw = this["cam_idealyaw"];
        CamIdealPitch = this["cam_idealpitch"];
        CamIdealDist = this["cam_idealdist"];
        CamIdealDistRight = this["cam_idealdistright"];
        CamIdealDistUp = this["cam_idealdistup"];
        CamShowAngles = this["cam_showangles"];
        CMaxPitch = this["c_maxpitch"];
        CMinPitch = this["c_minpitch"];
        CMaxYaw = this["c_maxyaw"];
        CMinYaw = this["c_minyaw"];
        CMaxDistance = this["c_maxdistance"];
        CMinDistance = this["c_mindistance"];
        COrthoWidth = this["c_orthowidth"];
        COrthoHeight = this["c_orthoheight"];
        ThirdPerson = this["cl_thirdperson"];

        // c_rumble.cpp
        RumbleScale = this["cl_rumblescale"];
        DebugRumble = this["cl_debugrumble"];
    }

    /// <summary>A convar by name; throws for a name that is not an input convar (a typo is a bug).</summary>
    public InputCvar this[string name] => _byName.TryGetValue(name, out InputCvar? cvar)
        ? cvar
        : throw new KeyNotFoundException($"'{name}' is not an input convar");

    public bool TryGet(string name, out InputCvar cvar) => _byName.TryGetValue(name, out cvar!);

    public IEnumerable<InputCvar> All => _byName.Values;

    /// <summary>
    /// Ties each to its registered declaration: the declaration's changes come
    /// in, and the input code's writes go out through it.
    /// </summary>
    public int Bind(IEnumerable<ConVarBase> declarations)
    {
        int bound = 0;
        foreach (ConVarBase declaration in declarations)
        {
            if (declaration is not InputConVarDeclaration input || !_byName.TryGetValue(input.Name, out InputCvar? cvar))
                continue;
            cvar.Set(input.RawValue);
            input.Changed += cvar.Set;
            cvar.BindPush(input.Push);
            bound++;
        }
        return bound;
    }

    public InputCvar AngleSpeedKey { get; }
    public InputCvar YawSpeed { get; }
    public InputCvar PitchSpeed { get; }
    public InputCvar PitchDown { get; }
    public InputCvar PitchUp { get; }
    public InputCvar SideSpeed { get; }
    public InputCvar UpSpeed { get; }
    public InputCvar ForwardSpeed { get; }
    public InputCvar BackSpeed { get; }
    public InputCvar LookSpring { get; }
    public InputCvar LookStrafe { get; }
    public InputCvar Joystick { get; }
    public InputCvar ThirdPersonPlatformer { get; }
    public InputCvar ThirdPersonScreenSpace { get; }
    public InputCvar NoclipDuringPause { get; }
    public InputCvar UseKeyboardSampleTime { get; }
    public InputCvar MPitch { get; }
    public InputCvar MFilter { get; }
    public InputCvar Sensitivity { get; }
    public InputCvar MSide { get; }
    public InputCvar MYaw { get; }
    public InputCvar MForward { get; }
    public InputCvar CustomAccel { get; }
    public InputCvar CustomAccelScale { get; }
    public InputCvar CustomAccelMax { get; }
    public InputCvar CustomAccelExponent { get; }
    public InputCvar RawInput { get; }
    public InputCvar RawInputOnetimeReset { get; }
    public InputCvar LimitedCaptureWorkaround { get; }
    public InputCvar MouseLook { get; }
    public InputCvar MouseEnable { get; }
    public InputCvar JoyName { get; }
    public InputCvar JoyAdvanced { get; }
    public InputCvar JoyAdvAxisX { get; }
    public InputCvar JoyAdvAxisY { get; }
    public InputCvar JoyAdvAxisZ { get; }
    public InputCvar JoyAdvAxisR { get; }
    public InputCvar JoyAdvAxisU { get; }
    public InputCvar JoyAdvAxisV { get; }
    public InputCvar JoyForwardThreshold { get; }
    public InputCvar JoySideThreshold { get; }
    public InputCvar JoyPitchThreshold { get; }
    public InputCvar JoyYawThreshold { get; }
    public InputCvar JoyForwardSensitivity { get; }
    public InputCvar JoySideSensitivity { get; }
    public InputCvar JoyPitchSensitivity { get; }
    public InputCvar JoyYawSensitivity { get; }
    public InputCvar JoyResponseMove { get; }
    public InputCvar JoyResponseLook { get; }
    public InputCvar JoyLowEnd { get; }
    public InputCvar JoyLowMap { get; }
    public InputCvar JoyAccelScale { get; }
    public InputCvar JoyAccelMax { get; }
    public InputCvar JoyAutoAimDampenRange { get; }
    public InputCvar JoyAutoAimDampen { get; }
    public InputCvar JoyVehicleTurnLowEnd { get; }
    public InputCvar JoyVehicleTurnLowMap { get; }
    public InputCvar JoyDisplayInput { get; }
    public InputCvar JoyWingmanHack { get; }
    public InputCvar JoyAutoSprint { get; }
    public InputCvar JoyInvertY { get; }
    public InputCvar JoyMovementStick { get; }
    public InputCvar JoyXControllerCfgLoaded { get; }
    public InputCvar JoyPegged { get; }
    public InputCvar JoyVirtualPeg { get; }
    public InputCvar JoyAccelFilter { get; }
    public InputCvar ScYawSensitivity { get; }
    public InputCvar ScPitchSensitivity { get; }
    public InputCvar ScLookSensitivityScale { get; }
    public InputCvar CamCommand { get; }
    public InputCvar CamSnapTo { get; }
    public InputCvar CamIdealLag { get; }
    public InputCvar CamIdealDelta { get; }
    public InputCvar CamIdealYaw { get; }
    public InputCvar CamIdealPitch { get; }
    public InputCvar CamIdealDist { get; }
    public InputCvar CamIdealDistRight { get; }
    public InputCvar CamIdealDistUp { get; }
    public InputCvar CamShowAngles { get; }
    public InputCvar CMaxPitch { get; }
    public InputCvar CMinPitch { get; }
    public InputCvar CMaxYaw { get; }
    public InputCvar CMinYaw { get; }
    public InputCvar CMaxDistance { get; }
    public InputCvar CMinDistance { get; }
    public InputCvar COrthoWidth { get; }
    public InputCvar COrthoHeight { get; }
    public InputCvar ThirdPerson { get; }
    public InputCvar RumbleScale { get; }
    public InputCvar DebugRumble { get; }
}
