//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Input;

//-----------------------------------------------------------------------------
// THE INPUT CONVARS (plan_client_novalve §2 cluster 13): every convar the
// stock input TUs declared -- in_main, in_mouse, in_joystick,
// in_steamcontroller, in_camera, cam_thirdperson and c_rumble -- with stock's
// name, default, help, flags and bounds (generated from the C0 contract
// snapshot, docs/novalve-snapshot/client-c0/client-convars-contract.txt, so
// the console lists exactly what it listed before).
//
// The input code does not read these classes' parsed values. It reads
// InputCvars, which keeps the ENGINE's view of each (ConVar::GetFloat is the
// atof of the text, GetInt its truncation), because the oracle replays stock's
// arithmetic bit for bit and the managed typed layer parses "2.5" as an int 0.
//-----------------------------------------------------------------------------

/// <summary>
/// One input convar's declaration. It only relays changes to
/// <see cref="InputCvars"/>, which owns the engine-semantics reading.
/// </summary>
public abstract class InputConVarDeclaration : StringConVar
{
    /// <summary>Told the engine's text after every change, whatever its source.</summary>
    public event Action<string>? Changed;

    protected override void OnChanged(string oldValue, string newValue) => Changed?.Invoke(newValue);

    /// <summary>Pushes a value the input code computed to the engine.</summary>
    public void Push(string text) => SetValue(text);
}

[ConVar("cl_anglespeedkey", "0.67")]
public sealed class ClAnglespeedkeyConVar : InputConVarDeclaration { }

[ConVar("cl_yawspeed", "210",
        Help = "Client yaw speed.",
        Min = -100000f,
        Max = 100000f)]
public sealed class ClYawspeedConVar : InputConVarDeclaration { }

[ConVar("cl_pitchspeed", "225",
        Help = "Client pitch speed.",
        Min = -100000f,
        Max = 100000f)]
public sealed class ClPitchspeedConVar : InputConVarDeclaration { }

[ConVar("cl_pitchdown", "89",
        Flags = ConVarFlags.Cheat)]
public sealed class ClPitchdownConVar : InputConVarDeclaration { }

[ConVar("cl_pitchup", "89",
        Flags = ConVarFlags.Cheat)]
public sealed class ClPitchupConVar : InputConVarDeclaration { }

[ConVar("cl_sidespeed", "450",
        Flags = ConVarFlags.Cheat | ConVarFlags.Replicated)]
public sealed class ClSidespeedConVar : InputConVarDeclaration { }

[ConVar("cl_upspeed", "320",
        Flags = ConVarFlags.Cheat | ConVarFlags.Replicated)]
public sealed class ClUpspeedConVar : InputConVarDeclaration { }

[ConVar("cl_forwardspeed", "450",
        Flags = ConVarFlags.Cheat | ConVarFlags.Replicated)]
public sealed class ClForwardspeedConVar : InputConVarDeclaration { }

[ConVar("cl_backspeed", "450",
        Flags = ConVarFlags.Cheat | ConVarFlags.Replicated)]
public sealed class ClBackspeedConVar : InputConVarDeclaration { }

[ConVar("lookspring", "0",
        Flags = ConVarFlags.Archive)]
public sealed class LookspringConVar : InputConVarDeclaration { }

[ConVar("lookstrafe", "0",
        Flags = ConVarFlags.Archive)]
public sealed class LookstrafeConVar : InputConVarDeclaration { }

[ConVar("joystick", "0",
        Flags = ConVarFlags.Archive)]
public sealed class JoystickConVar : InputConVarDeclaration { }

[ConVar("thirdperson_platformer", "0",
        Help = "Player will aim in the direction they are moving.")]
public sealed class ThirdpersonPlatformerConVar : InputConVarDeclaration { }

[ConVar("thirdperson_screenspace", "0",
        Help = "Movement will be relative to the camera, eg: left means screen-left")]
public sealed class ThirdpersonScreenspaceConVar : InputConVarDeclaration { }

[ConVar("sv_noclipduringpause", "0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Replicated,
        Help = "If cheats are enabled, then you can noclip with the game paused (for doing screenshots, etc.).")]
public sealed class SvNoclipduringpauseConVar : InputConVarDeclaration { }

[ConVar("in_usekeyboardsampletime", "1",
        Help = "Use keyboard sample time smoothing.")]
public sealed class InUsekeyboardsampletimeConVar : InputConVarDeclaration { }

[ConVar("m_pitch", "0.022",
        Flags = ConVarFlags.Archive,
        Help = "Mouse pitch factor.")]
public sealed class MPitchConVar : InputConVarDeclaration { }

[ConVar("m_filter", "0",
        Flags = ConVarFlags.Archive,
        Help = "Mouse filtering (set this to 1 to average the mouse over 2 frames).")]
public sealed class MFilterConVar : InputConVarDeclaration { }

[ConVar("sensitivity", "3",
        Flags = ConVarFlags.Archive,
        Help = "Mouse sensitivity.",
        Min = 0.0001f,
        Max = 10000000f)]
public sealed class SensitivityConVar : InputConVarDeclaration { }

[ConVar("m_side", "0.8",
        Flags = ConVarFlags.Archive,
        Help = "Mouse side factor.")]
public sealed class MSideConVar : InputConVarDeclaration { }

[ConVar("m_yaw", "0.022",
        Flags = ConVarFlags.Archive,
        Help = "Mouse yaw factor.")]
public sealed class MYawConVar : InputConVarDeclaration { }

[ConVar("m_forward", "1",
        Flags = ConVarFlags.Archive,
        Help = "Mouse forward factor.")]
public sealed class MForwardConVar : InputConVarDeclaration { }

[ConVar("m_customaccel", "0",
        Flags = ConVarFlags.Archive,
        Help = "Custom mouse acceleration:\n0: custom accelaration disabled\n1: mouse_acceleration = min(m_customaccel_max, pow(raw_mouse_delta, m_customaccel_exponent) * m_customaccel_scale + sensitivity)\n2: Same as 1, with but x and y sensitivity are scaled by m_pitch and m_yaw respectively.\n3: mouse_acceleration = pow(raw_mouse_delta, m_customaccel_exponent - 1) * sensitivity")]
public sealed class MCustomaccelConVar : InputConVarDeclaration { }

[ConVar("m_customaccel_scale", "0.04",
        Flags = ConVarFlags.Archive,
        Help = "Custom mouse acceleration value.",
        Min = 0f)]
public sealed class MCustomaccelScaleConVar : InputConVarDeclaration { }

[ConVar("m_customaccel_max", "0",
        Flags = ConVarFlags.Archive,
        Help = "Max mouse move scale factor, 0 for no limit")]
public sealed class MCustomaccelMaxConVar : InputConVarDeclaration { }

[ConVar("m_customaccel_exponent", "1",
        Flags = ConVarFlags.Archive,
        Help = "Mouse move is raised to this power before being scaled by scale factor.",
        Min = 1f)]
public sealed class MCustomaccelExponentConVar : InputConVarDeclaration { }

[ConVar("m_mousespeed", "1",
        Flags = ConVarFlags.Archive,
        Help = "Windows mouse acceleration (0 to disable, 1 to enable [Windows 2000: enable initial threshold], 2 to enable secondary threshold [Windows 2000 only]).",
        Min = 0f,
        Max = 2f)]
public sealed class MMousespeedConVar : InputConVarDeclaration { }

[ConVar("m_mouseaccel1", "0",
        Flags = ConVarFlags.Archive,
        Help = "Windows mouse acceleration initial threshold (2x movement).",
        Min = 0f)]
public sealed class MMouseaccel1ConVar : InputConVarDeclaration { }

[ConVar("m_mouseaccel2", "0",
        Flags = ConVarFlags.Archive,
        Help = "Windows mouse acceleration secondary threshold (4x movement).",
        Min = 0f)]
public sealed class MMouseaccel2ConVar : InputConVarDeclaration { }

[ConVar("m_rawinput", "1",
        Flags = ConVarFlags.Archive,
        Help = "Use Raw Input for mouse input.")]
public sealed class MRawinputConVar : InputConVarDeclaration { }

[ConVar("m_rawinput_onetime_reset", "0",
        Flags = ConVarFlags.Archive | ConVarFlags.Hidden)]
public sealed class MRawinputOnetimeResetConVar : InputConVarDeclaration { }

[ConVar("m_limitedcapture_workaround", "0",
        Help = "Workaround limitations on mouse capture in some environments")]
public sealed class MLimitedcaptureWorkaroundConVar : InputConVarDeclaration { }

// Stock's own two spellings, by build (in_mouse.cpp's #if DEBUG).
#if DEBUG
[ConVar("cl_mouselook", "1",
        Flags = ConVarFlags.Archive,
        Help = "Set to 1 to use mouse for look, 0 for keyboard look.")]
#else
[ConVar("cl_mouselook", "1",
        Flags = ConVarFlags.Archive | ConVarFlags.NotConnected,
        Help = "Set to 1 to use mouse for look, 0 for keyboard look. Cannot be set while connected to a server.")]
#endif
public sealed class ClMouselookConVar : InputConVarDeclaration { }

[ConVar("cl_mouseenable", "1")]
public sealed class ClMouseenableConVar : InputConVarDeclaration { }

[ConVar("joy_name", "joystick",
        Flags = ConVarFlags.Archive)]
public sealed class JoyNameConVar : InputConVarDeclaration { }

[ConVar("joy_advanced", "1",
        Flags = ConVarFlags.Archive)]
public sealed class JoyAdvancedConVar : InputConVarDeclaration { }

[ConVar("joy_advaxisx", "4",
        Flags = ConVarFlags.Archive)]
public sealed class JoyAdvaxisxConVar : InputConVarDeclaration { }

[ConVar("joy_advaxisy", "2",
        Flags = ConVarFlags.Archive)]
public sealed class JoyAdvaxisyConVar : InputConVarDeclaration { }

[ConVar("joy_advaxisz", "0",
        Flags = ConVarFlags.Archive)]
public sealed class JoyAdvaxiszConVar : InputConVarDeclaration { }

[ConVar("joy_advaxisr", "1",
        Flags = ConVarFlags.Archive)]
public sealed class JoyAdvaxisrConVar : InputConVarDeclaration { }

[ConVar("joy_advaxisu", "3",
        Flags = ConVarFlags.Archive)]
public sealed class JoyAdvaxisuConVar : InputConVarDeclaration { }

[ConVar("joy_advaxisv", "0",
        Flags = ConVarFlags.Archive)]
public sealed class JoyAdvaxisvConVar : InputConVarDeclaration { }

[ConVar("joy_forwardthreshold", "0.15",
        Flags = ConVarFlags.Archive)]
public sealed class JoyForwardthresholdConVar : InputConVarDeclaration { }

[ConVar("joy_sidethreshold", "0.15",
        Flags = ConVarFlags.Archive)]
public sealed class JoySidethresholdConVar : InputConVarDeclaration { }

[ConVar("joy_pitchthreshold", "0.15",
        Flags = ConVarFlags.Archive)]
public sealed class JoyPitchthresholdConVar : InputConVarDeclaration { }

[ConVar("joy_yawthreshold", "0.15",
        Flags = ConVarFlags.Archive)]
public sealed class JoyYawthresholdConVar : InputConVarDeclaration { }

[ConVar("joy_forwardsensitivity", "-1",
        Flags = ConVarFlags.Archive)]
public sealed class JoyForwardsensitivityConVar : InputConVarDeclaration { }

[ConVar("joy_sidesensitivity", "1",
        Flags = ConVarFlags.Archive)]
public sealed class JoySidesensitivityConVar : InputConVarDeclaration { }

[ConVar("joy_pitchsensitivity", "1",
        Flags = ConVarFlags.Archive | ConVarFlags.ArchiveXbox)]
public sealed class JoyPitchsensitivityConVar : InputConVarDeclaration { }

[ConVar("joy_yawsensitivity", "-1",
        Flags = ConVarFlags.Archive | ConVarFlags.ArchiveXbox)]
public sealed class JoyYawsensitivityConVar : InputConVarDeclaration { }

[ConVar("joy_response_move", "1",
        Flags = ConVarFlags.Archive,
        Help = "'Movement' stick response mode: 0=Linear, 1=quadratic, 2=cubic, 3=quadratic extreme, 4=power function(i.e., pow(x,1/sensitivity)), 5=two-stage")]
public sealed class JoyResponseMoveConVar : InputConVarDeclaration { }

[ConVar("joy_response_move_vehicle", "6")]
public sealed class JoyResponseMoveVehicleConVar : InputConVarDeclaration { }

[ConVar("joy_response_look", "0",
        Flags = ConVarFlags.Archive,
        Help = "'Look' stick response mode: 0=Default, 1=Acceleration Promotion")]
public sealed class JoyResponseLookConVar : InputConVarDeclaration { }

[ConVar("joy_lowend", "1",
        Flags = ConVarFlags.Archive)]
public sealed class JoyLowendConVar : InputConVarDeclaration { }

[ConVar("joy_lowmap", "1",
        Flags = ConVarFlags.Archive)]
public sealed class JoyLowmapConVar : InputConVarDeclaration { }

[ConVar("joy_accelscale", "0.6",
        Flags = ConVarFlags.Archive)]
public sealed class JoyAccelscaleConVar : InputConVarDeclaration { }

[ConVar("joy_accelmax", "1.0",
        Flags = ConVarFlags.Archive)]
public sealed class JoyAccelmaxConVar : InputConVarDeclaration { }

[ConVar("joy_autoaimdampenrange", "0",
        Flags = ConVarFlags.Archive,
        Help = "The stick range where autoaim dampening is applied. 0 = off")]
public sealed class JoyAutoaimdampenrangeConVar : InputConVarDeclaration { }

[ConVar("joy_autoaimdampen", "0",
        Flags = ConVarFlags.Archive,
        Help = "How much to scale user stick input when the gun is pointing at a valid target.")]
public sealed class JoyAutoaimdampenConVar : InputConVarDeclaration { }

[ConVar("joy_vehicle_turn_lowend", "0.7")]
public sealed class JoyVehicleTurnLowendConVar : InputConVarDeclaration { }

[ConVar("joy_vehicle_turn_lowmap", "0.4")]
public sealed class JoyVehicleTurnLowmapConVar : InputConVarDeclaration { }

[ConVar("joy_diagonalpov", "0",
        Flags = ConVarFlags.Archive,
        Help = "POV manipulator operates on diagonal axes, too.")]
public sealed class JoyDiagonalpovConVar : InputConVarDeclaration { }

[ConVar("joy_display_input", "0",
        Flags = ConVarFlags.Archive)]
public sealed class JoyDisplayInputConVar : InputConVarDeclaration { }

[ConVar("joy_wingmanwarrior_turnhack", "0",
        Flags = ConVarFlags.Archive,
        Help = "Wingman warrior hack related to turn axes.")]
public sealed class JoyWingmanwarriorTurnhackConVar : InputConVarDeclaration { }

[ConVar("joy_autosprint", "0",
        Help = "Automatically sprint when moving with an analog joystick")]
public sealed class JoyAutosprintConVar : InputConVarDeclaration { }

[ConVar("joy_inverty", "0",
        Flags = ConVarFlags.Archive | ConVarFlags.ArchiveXbox,
        Help = "Whether to invert the Y axis of the joystick for looking.")]
public sealed class JoyInvertyConVar : InputConVarDeclaration { }

[ConVar("joy_yawsensitivity_default", "-1.25")]
public sealed class JoyYawsensitivityDefaultConVar : InputConVarDeclaration { }

[ConVar("joy_pitchsensitivity_default", "-1.0")]
public sealed class JoyPitchsensitivityDefaultConVar : InputConVarDeclaration { }

[ConVar("option_duck_method_default", "1.0")]
public sealed class OptionDuckMethodDefaultConVar : InputConVarDeclaration { }

[ConVar("joy_inverty_default", "0",
        Flags = ConVarFlags.ArchiveXbox)]
public sealed class JoyInvertyDefaultConVar : InputConVarDeclaration { }

[ConVar("joy_movement_stick_default", "0",
        Flags = ConVarFlags.ArchiveXbox)]
public sealed class JoyMovementStickDefaultConVar : InputConVarDeclaration { }

[ConVar("sv_stickysprint_default", "0")]
public sealed class SvStickysprintDefaultConVar : InputConVarDeclaration { }

[ConVar("joy_movement_stick", "0",
        Flags = ConVarFlags.Archive | ConVarFlags.ArchiveXbox,
        Help = "Which stick controls movement (0 is left stick)")]
public sealed class JoyMovementStickConVar : InputConVarDeclaration { }

[ConVar("joy_xcontroller_cfg_loaded", "0",
        Flags = ConVarFlags.Archive,
        Help = "If 0, the 360controller.cfg file will be executed on startup & option changes.")]
public sealed class JoyXcontrollerCfgLoadedConVar : InputConVarDeclaration { }

[ConVar("joy_pegged", "0.75")]
public sealed class JoyPeggedConVar : InputConVarDeclaration { }

[ConVar("joy_virtual_peg", "0")]
public sealed class JoyVirtualPegConVar : InputConVarDeclaration { }

[ConVar("joy_accel_filter", "0.2")]
public sealed class JoyAccelFilterConVar : InputConVarDeclaration { }

[ConVar("sc_yaw_sensitivity", "1.0",
        Flags = ConVarFlags.Archive,
        Help = "SteamController yaw factor.")]
public sealed class ScYawSensitivityConVar : InputConVarDeclaration { }

[ConVar("sc_yaw_sensitivity_default", "1.0")]
public sealed class ScYawSensitivityDefaultConVar : InputConVarDeclaration { }

[ConVar("sc_pitch_sensitivity", "0.75",
        Flags = ConVarFlags.Archive,
        Help = "SteamController pitch factor.")]
public sealed class ScPitchSensitivityConVar : InputConVarDeclaration { }

[ConVar("sc_pitch_sensitivity_default", "0.75")]
public sealed class ScPitchSensitivityDefaultConVar : InputConVarDeclaration { }

[ConVar("sc_look_sensitivity_scale", "0.125",
        Help = "Steam Controller look sensitivity global scale factor.")]
public sealed class ScLookSensitivityScaleConVar : InputConVarDeclaration { }

[ConVar("cam_command", "0",
        Flags = ConVarFlags.Cheat)]
public sealed class CamCommandConVar : InputConVarDeclaration { }

[ConVar("cam_snapto", "0",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CamSnaptoConVar : InputConVarDeclaration { }

[ConVar("cam_ideallag", "4.0",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat,
        Help = "Amount of lag used when matching offset to ideal angles in thirdperson view")]
public sealed class CamIdeallagConVar : InputConVarDeclaration { }

[ConVar("cam_idealdelta", "4.0",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat,
        Help = "Controls the speed when matching offset to ideal angles in thirdperson view")]
public sealed class CamIdealdeltaConVar : InputConVarDeclaration { }

[ConVar("cam_idealyaw", "0",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CamIdealyawConVar : InputConVarDeclaration { }

[ConVar("cam_idealpitch", "0",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CamIdealpitchConVar : InputConVarDeclaration { }

[ConVar("cam_idealdist", "150",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CamIdealdistConVar : InputConVarDeclaration { }

[ConVar("cam_idealdistright", "0",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CamIdealdistrightConVar : InputConVarDeclaration { }

[ConVar("cam_idealdistup", "0",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CamIdealdistupConVar : InputConVarDeclaration { }

[ConVar("cam_collision", "1",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat,
        Help = "When in thirdperson and cam_collision is set to 1, an attempt is made to keep the camera from passing though walls.")]
public sealed class CamCollisionConVar : InputConVarDeclaration { }

[ConVar("cam_showangles", "0",
        Flags = ConVarFlags.Cheat,
        Help = "When in thirdperson, print viewangles/idealangles/cameraoffsets to the console.")]
public sealed class CamShowanglesConVar : InputConVarDeclaration { }

[ConVar("c_maxpitch", "90",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CMaxpitchConVar : InputConVarDeclaration { }

[ConVar("c_minpitch", "0",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CMinpitchConVar : InputConVarDeclaration { }

[ConVar("c_maxyaw", "135",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CMaxyawConVar : InputConVarDeclaration { }

[ConVar("c_minyaw", "-135",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CMinyawConVar : InputConVarDeclaration { }

[ConVar("c_maxdistance", "200",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CMaxdistanceConVar : InputConVarDeclaration { }

[ConVar("c_mindistance", "30",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class CMindistanceConVar : InputConVarDeclaration { }

[ConVar("c_orthowidth", "100",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class COrthowidthConVar : InputConVarDeclaration { }

[ConVar("c_orthoheight", "100",
        Flags = ConVarFlags.Archive | ConVarFlags.Cheat)]
public sealed class COrthoheightConVar : InputConVarDeclaration { }

[ConVar("cl_thirdperson", "0",
        Flags = ConVarFlags.Archive | ConVarFlags.NotConnected | ConVarFlags.UserInfo | ConVarFlags.DevelopmentOnly,
        Help = "Enables/Disables third person")]
public sealed class ClThirdpersonConVar : InputConVarDeclaration { }

[ConVar("cl_rumblescale", "1.0",
        Flags = ConVarFlags.Archive | ConVarFlags.ArchiveXbox,
        Help = "Scale sensitivity of rumble effects (0 to 1.0)")]
public sealed class ClRumblescaleConVar : InputConVarDeclaration { }

[ConVar("cl_debugrumble", "0",
        Flags = ConVarFlags.Archive,
        Help = "Turn on rumble debugging spew")]
public sealed class ClDebugrumbleConVar : InputConVarDeclaration { }
