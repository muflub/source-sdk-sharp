//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — lane c6-input's oracle probe (debug builds only) ====//
//
// DEBUG BUILDS ONLY, AND OPT-IN, as shell_probes_debug.cpp beside it: compiled
// into client.so only when farm/debug/build is configured with
// -DSS_CLIENT_PROBES=ON. It records the STOCK input code that lane c6-input
// replaces (plan_client_novalve.md §5.1 step 1) into
// $SOURCESHARP_C6_FIXTURE_DIR/input-stock.tsv.
//
//   cs_cl_probe_input N   arm: wrap the input system the stock code reads
//                         (`inputsystem` and tier2's `g_pInputSystem`) in a
//                         recorder that serves SCRIPTED raw mouse deltas,
//                         joystick axes and a fake Steam Controller, and drive
//                         a scripted session for N user commands: key commands,
//                         convar changes and camera commands, in five segments
//                         (keys, mouse, joystick, Steam Controller, third
//                         person). Every read the stock code makes of a device
//                         is logged in order, with every event (key button
//                         transitions, sample times, CreateMove,
//                         ExtraMouseSample, CAM_Think) and what it wrote.
//
// The lines are documented in src/test/client/Fixtures/README-input-stock.md.
//=============================================================================//

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "kbutton.h"
#include "input.h"
#include "inputsystem/iinputsystem.h"
#include "steam/isteamcontroller.h"
#include "tier2/tier2.h"
#include "c6input_probe.h"
#include <stdarg.h>

#include "tier0/memdbgon.h"

extern IInputSystem *inputsystem;

namespace
{

FILE *s_pFixture = NULL;
int s_nCommands = 0;         // commands left to script
int s_nIndex = 0;            // commands scripted so far
int s_nExtra = 0;            // extra samples since arming
unsigned int s_nRandom = 0x2545F491u;

unsigned int NextRandom()
{
	// xorshift32, the usercmd-decode fixture's generator.
	unsigned int x = s_nRandom;
	x ^= x << 13;
	x ^= x >> 17;
	x ^= x << 5;
	s_nRandom = x;
	return x;
}

int RandomRange( int lo, int hi ) { return lo + (int)( NextRandom() % (unsigned int)( hi - lo + 1 ) ); }

struct ButtonName { const kbutton_t *p; const char *name; };
ButtonName s_Names[ 64 ];
int s_nNames = 0;

//-----------------------------------------------------------------------------
// The Steam Controller the stock code finds: one connected pad, every action
// handle its index + 1, and the digital and analog data the script set.
//-----------------------------------------------------------------------------
class CFakeSteamController : public ISteamController
{
public:
	bool m_bDigital[ 64 ] = {};
	bool m_bDigitalActive[ 64 ] = {};
	char m_szNames[ 64 ][ 48 ] = {};
	int m_nNames = 0;
	ControllerAnalogActionData_t m_Move = {};
	ControllerAnalogActionData_t m_Camera = {};
	int m_nConnected = 1;

	int HandleFor( const char *pName )
	{
		for ( int i = 0; i < m_nNames; ++i )
			if ( !Q_strcmp( m_szNames[ i ], pName ) )
				return i + 1;
		if ( m_nNames >= 64 )
			return 0;
		Q_strncpy( m_szNames[ m_nNames ], pName, sizeof( m_szNames[ 0 ] ) );
		return ++m_nNames;
	}

	virtual bool Init() { return true; }
	virtual bool Shutdown() { return true; }
	virtual void RunFrame() {}
	virtual int GetConnectedControllers( ControllerHandle_t *handlesOut )
	{
		if ( m_nConnected > 0 )
			handlesOut[ 0 ] = 77;
		C6P_Log( "SC\t%d", m_nConnected );
		return m_nConnected;
	}
	virtual ControllerActionSetHandle_t GetActionSetHandle( const char * ) { return 0; }
	virtual void ActivateActionSet( ControllerHandle_t, ControllerActionSetHandle_t ) {}
	virtual ControllerActionSetHandle_t GetCurrentActionSet( ControllerHandle_t ) { return 0; }
	virtual void ActivateActionSetLayer( ControllerHandle_t, ControllerActionSetHandle_t ) {}
	virtual void DeactivateActionSetLayer( ControllerHandle_t, ControllerActionSetHandle_t ) {}
	virtual void DeactivateAllActionSetLayers( ControllerHandle_t ) {}
	virtual int GetActiveActionSetLayers( ControllerHandle_t, ControllerActionSetHandle_t * ) { return 0; }
	virtual ControllerDigitalActionHandle_t GetDigitalActionHandle( const char *pszActionName )
	{
		int h = HandleFor( pszActionName );
		C6P_Log( "SDH\t%s\t%d", pszActionName, h );
		return h;
	}
	virtual ControllerDigitalActionData_t GetDigitalActionData( ControllerHandle_t, ControllerDigitalActionHandle_t h )
	{
		ControllerDigitalActionData_t d;
		d.bState = h > 0 && h <= 64 && m_bDigital[ h - 1 ];
		d.bActive = h > 0 && h <= 64 && m_bDigitalActive[ h - 1 ];
		C6P_Log( "SD\t%d\t%d\t%d", (int)h, d.bState ? 1 : 0, d.bActive ? 1 : 0 );
		return d;
	}
	virtual int GetDigitalActionOrigins( ControllerHandle_t, ControllerActionSetHandle_t, ControllerDigitalActionHandle_t, EControllerActionOrigin * ) { return 0; }
	virtual ControllerAnalogActionHandle_t GetAnalogActionHandle( const char *pszActionName )
	{
		int h = HandleFor( pszActionName );
		C6P_Log( "SAH\t%s\t%d", pszActionName, h );
		return h;
	}
	virtual ControllerAnalogActionData_t GetAnalogActionData( ControllerHandle_t, ControllerAnalogActionHandle_t h )
	{
		const char *pName = ( h > 0 && h <= m_nNames ) ? m_szNames[ h - 1 ] : "";
		ControllerAnalogActionData_t d = {};
		if ( !Q_strcmp( pName, "Move" ) )
			d = m_Move;
		else if ( !Q_strcmp( pName, "Camera" ) )
			d = m_Camera;
		C6P_Log( "SAD\t%d\t%d\t%.9g\t%.9g\t%d", (int)h, (int)d.eMode, d.x, d.y, d.bActive ? 1 : 0 );
		return d;
	}
	virtual int GetAnalogActionOrigins( ControllerHandle_t, ControllerActionSetHandle_t, ControllerAnalogActionHandle_t, EControllerActionOrigin * ) { return 0; }
	virtual const char *GetGlyphForActionOrigin( EControllerActionOrigin ) { return ""; }
	virtual const char *GetStringForActionOrigin( EControllerActionOrigin ) { return ""; }
	virtual void StopAnalogActionMomentum( ControllerHandle_t, ControllerAnalogActionHandle_t ) {}
	virtual ControllerMotionData_t GetMotionData( ControllerHandle_t ) { ControllerMotionData_t d = {}; return d; }
	virtual void TriggerHapticPulse( ControllerHandle_t, ESteamControllerPad, unsigned short ) {}
	virtual void TriggerRepeatedHapticPulse( ControllerHandle_t, ESteamControllerPad, unsigned short, unsigned short, unsigned short, unsigned int ) {}
	virtual void TriggerVibration( ControllerHandle_t, unsigned short, unsigned short ) {}
	virtual void SetLEDColor( ControllerHandle_t, uint8, uint8, uint8, unsigned int ) {}
	virtual bool ShowBindingPanel( ControllerHandle_t ) { return false; }
	virtual ESteamInputType GetInputTypeForHandle( ControllerHandle_t ) { return k_ESteamInputType_SteamController; }
	virtual ControllerHandle_t GetControllerForGamepadIndex( int ) { return 0; }
	virtual int GetGamepadIndexForController( ControllerHandle_t ) { return -1; }
	virtual const char *GetStringForXboxOrigin( EXboxOrigin ) { return ""; }
	virtual const char *GetGlyphForXboxOrigin( EXboxOrigin ) { return ""; }
	virtual EControllerActionOrigin GetActionOriginFromXboxOrigin( ControllerHandle_t, EXboxOrigin ) { return k_EControllerActionOrigin_None; }
	virtual EControllerActionOrigin TranslateActionOrigin( ESteamInputType, EControllerActionOrigin e ) { return e; }
	virtual bool GetControllerBindingRevision( ControllerHandle_t, int *, int * ) { return false; }
};

CFakeSteamController s_Controller;

//-----------------------------------------------------------------------------
// The input system, forwarded, with the device reads the stock input code
// makes answered from the script and logged.
//-----------------------------------------------------------------------------
class CRecordingInputSystem : public IInputSystem
{
public:
	IInputSystem *m_pInner = NULL;
	int m_nRawX = 0, m_nRawY = 0;
	bool m_bRawPending = false;
	int m_nAxes[ MAX_JOYSTICK_AXES ] = {};
	int m_nJoysticks = 0;
	bool m_bController = false;

	// IAppSystem
	virtual bool Connect( CreateInterfaceFn factory ) { return m_pInner->Connect( factory ); }
	virtual void Disconnect() { m_pInner->Disconnect(); }
	virtual void *QueryInterface( const char *pInterfaceName ) { return m_pInner->QueryInterface( pInterfaceName ); }
	virtual InitReturnVal_t Init() { return m_pInner->Init(); }
	virtual void Shutdown() { m_pInner->Shutdown(); }

	virtual void AttachToWindow( void *hWnd ) { m_pInner->AttachToWindow( hWnd ); }
	virtual void DetachFromWindow() { m_pInner->DetachFromWindow(); }
	virtual void EnableInput( bool bEnable ) { m_pInner->EnableInput( bEnable ); }
	virtual void EnableMessagePump( bool bEnable ) { m_pInner->EnableMessagePump( bEnable ); }
	virtual void PollInputState() { m_pInner->PollInputState(); }
	virtual int GetPollTick() const { return m_pInner->GetPollTick(); }
	virtual bool IsButtonDown( ButtonCode_t code ) const
	{
		bool b = m_pInner->IsButtonDown( code );
		C6P_Log( "B\t%d\t%d", (int)code, b ? 1 : 0 );
		return b;
	}
	virtual int GetButtonPressedTick( ButtonCode_t code ) const { return m_pInner->GetButtonPressedTick( code ); }
	virtual int GetButtonReleasedTick( ButtonCode_t code ) const { return m_pInner->GetButtonReleasedTick( code ); }
	virtual int GetAnalogValue( AnalogCode_t code ) const
	{
		int v = 0;
		int axis = (int)code - (int)JOYSTICK_FIRST_AXIS;
		if ( axis >= 0 && axis < MAX_JOYSTICK_AXES )
			v = m_nAxes[ axis ];
		else
			v = m_pInner->GetAnalogValue( code );
		C6P_Log( "A\t%d\t%d", (int)code, v );
		return v;
	}
	virtual int GetAnalogDelta( AnalogCode_t code ) const { return m_pInner->GetAnalogDelta( code ); }
	virtual int GetEventCount() const { return m_pInner->GetEventCount(); }
	virtual const InputEvent_t *GetEventData() const { return m_pInner->GetEventData(); }
	virtual void PostUserEvent( const InputEvent_t &event ) { m_pInner->PostUserEvent( event ); }
	virtual int GetJoystickCount() const
	{
		C6P_Log( "N\t%d", m_nJoysticks );
		return m_nJoysticks;
	}
	virtual void EnableJoystickInput( int nJoystick, bool bEnable ) { m_pInner->EnableJoystickInput( nJoystick, bEnable ); }
	virtual void EnableJoystickDiagonalPOV( int nJoystick, bool bEnable ) { m_pInner->EnableJoystickDiagonalPOV( nJoystick, bEnable ); }
	virtual void SampleDevices( void ) { m_pInner->SampleDevices(); }
	virtual void SetRumble( float fLeftMotor, float fRightMotor, int userId ) { C6P_Log( "RU\t%.9g\t%.9g", fLeftMotor, fRightMotor ); m_pInner->SetRumble( fLeftMotor, fRightMotor, userId ); }
	virtual void StopRumble( void ) { C6P_Log( "RS" ); m_pInner->StopRumble(); }
	virtual void ResetInputState() { m_pInner->ResetInputState(); }
	virtual void SetPrimaryUserId( int userId ) { m_pInner->SetPrimaryUserId( userId ); }
	virtual const char *ButtonCodeToString( ButtonCode_t code ) const { return m_pInner->ButtonCodeToString( code ); }
	virtual const char *AnalogCodeToString( AnalogCode_t code ) const { return m_pInner->AnalogCodeToString( code ); }
	virtual ButtonCode_t StringToButtonCode( const char *pString ) const { return m_pInner->StringToButtonCode( pString ); }
	virtual AnalogCode_t StringToAnalogCode( const char *pString ) const { return m_pInner->StringToAnalogCode( pString ); }
	virtual void SleepUntilInput( int nMaxSleepTimeMS ) { m_pInner->SleepUntilInput( nMaxSleepTimeMS ); }
	virtual ButtonCode_t VirtualKeyToButtonCode( int nVirtualKey ) const { return m_pInner->VirtualKeyToButtonCode( nVirtualKey ); }
	virtual int ButtonCodeToVirtualKey( ButtonCode_t code ) const { return m_pInner->ButtonCodeToVirtualKey( code ); }
	virtual ButtonCode_t ScanCodeToButtonCode( int lParam ) const { return m_pInner->ScanCodeToButtonCode( lParam ); }
	virtual int GetPollCount() const { return m_pInner->GetPollCount(); }
	virtual void SetCursorPosition( int x, int y ) { m_pInner->SetCursorPosition( x, y ); }
	virtual void *GetHapticsInterfaceAddress() const { return m_pInner->GetHapticsInterfaceAddress(); }
	virtual void SetNovintPure( bool bPure ) { m_pInner->SetNovintPure( bPure ); }
	virtual bool GetRawMouseAccumulators( int &accumX, int &accumY )
	{
		// The real accumulators are drained either way, so nothing typed into
		// the window leaks into the recording.
		int nx, ny;
		m_pInner->GetRawMouseAccumulators( nx, ny );
		accumX = m_bRawPending ? m_nRawX : 0;
		accumY = m_bRawPending ? m_nRawY : 0;
		m_bRawPending = false;
		C6P_Log( "R\t1\t%d\t%d", accumX, accumY );
		return true;
	}
	virtual void SetConsoleTextMode( bool bConsoleTextMode ) { m_pInner->SetConsoleTextMode( bConsoleTextMode ); }
	virtual ISteamController *SteamControllerInterface()
	{
		C6P_Log( "SP\t%d", m_bController ? 1 : 0 );
		return m_bController ? &s_Controller : NULL;
	}
	virtual uint32 GetNumSteamControllersConnected() { return m_bController ? 1 : 0; }
	virtual bool IsSteamControllerActive()
	{
		C6P_Log( "SI\t%d", m_bController ? 1 : 0 );
		return m_bController;
	}
	virtual bool IsSteamControllerConnected() { return m_bController; }
	virtual int GetSteamControllerIndexForSlot( int nSlot ) { return m_pInner->GetSteamControllerIndexForSlot( nSlot ); }
	virtual bool GetRadialMenuStickValues( int nSlot, float &fX, float &fY ) { return m_pInner->GetRadialMenuStickValues( nSlot, fX, fY ); }
	virtual void ActivateSteamControllerActionSetForSlot( uint64 nSlot, GameActionSet_t eActionSet )
	{
		C6P_Log( "SA\t%d", (int)eActionSet );
	}
	virtual ControllerActionSetHandle_t GetActionSetHandle( GameActionSet_t eActionSet ) { return m_pInner->GetActionSetHandle( eActionSet ); }
	virtual ControllerActionSetHandle_t GetActionSetHandle( const char *szActionSet ) { return m_pInner->GetActionSetHandle( szActionSet ); }
	virtual EControllerActionOrigin GetSteamControllerActionOrigin( const char *action, GameActionSet_t action_set ) { return m_pInner->GetSteamControllerActionOrigin( action, action_set ); }
	virtual EControllerActionOrigin GetSteamControllerActionOrigin( const char *action, ControllerActionSetHandle_t action_set_handle ) { return m_pInner->GetSteamControllerActionOrigin( action, action_set_handle ); }
	virtual const wchar_t *GetSteamControllerFontCharacterForActionOrigin( EControllerActionOrigin origin ) { return m_pInner->GetSteamControllerFontCharacterForActionOrigin( origin ); }
	virtual const wchar_t *GetSteamControllerDescriptionForActionOrigin( EControllerActionOrigin origin ) { return m_pInner->GetSteamControllerDescriptionForActionOrigin( origin ); }
	virtual void SetSkipControllerInitialization( bool bSkip ) { m_pInner->SetSkipControllerInitialization( bSkip ); }
};

CRecordingInputSystem s_Recorder;

// The convars the stock input code reads, dumped at arming so a replay starts
// from the values the recording did.
const char *const s_pszConVars[] =
{
	"sv_cheats", "cl_anglespeedkey", "cl_yawspeed", "cl_pitchspeed", "cl_pitchdown", "cl_pitchup",
	"cl_sidespeed", "cl_upspeed", "cl_forwardspeed", "cl_backspeed", "lookspring", "lookstrafe",
	"joystick", "thirdperson_platformer", "thirdperson_screenspace", "sv_noclipduringpause",
	"in_usekeyboardsampletime", "m_pitch", "m_filter", "sensitivity", "m_side", "m_yaw", "m_forward",
	"m_customaccel", "m_customaccel_scale", "m_customaccel_max", "m_customaccel_exponent",
	"m_rawinput", "cl_mouselook", "cl_mouseenable", "m_limitedcapture_workaround",
	"joy_name", "joy_advanced", "joy_advaxisx", "joy_advaxisy", "joy_advaxisz", "joy_advaxisr",
	"joy_advaxisu", "joy_advaxisv", "joy_forwardthreshold", "joy_sidethreshold", "joy_pitchthreshold",
	"joy_yawthreshold", "joy_forwardsensitivity", "joy_sidesensitivity", "joy_pitchsensitivity",
	"joy_yawsensitivity", "joy_response_move", "joy_response_move_vehicle", "joy_response_look",
	"joy_lowend", "joy_lowmap", "joy_accelscale", "joy_accelmax", "joy_autoaimdampenrange",
	"joy_autoaimdampen", "joy_vehicle_turn_lowend", "joy_vehicle_turn_lowmap", "joy_diagonalpov",
	"joy_display_input", "joy_wingmanwarrior_turnhack", "joy_autosprint", "joy_inverty",
	"joy_movement_stick", "joy_xcontroller_cfg_loaded", "joy_pegged", "joy_virtual_peg",
	"joy_accel_filter", "sc_yaw_sensitivity", "sc_pitch_sensitivity", "sc_look_sensitivity_scale",
	"cam_command", "cam_snapto", "cam_ideallag", "cam_idealdelta", "cam_idealyaw", "cam_idealpitch",
	"cam_idealdist", "cam_idealdistright", "cam_idealdistup", "cam_collision", "cam_showangles",
	"c_maxpitch", "c_minpitch", "c_maxyaw", "c_minyaw", "c_maxdistance", "c_mindistance",
	"c_orthowidth", "c_orthoheight", "cl_thirdperson",
};

void SetConVar( const char *pName, const char *pValue )
{
	ConVar *pVar = g_pCVar->FindVar( pName );
	if ( !pVar )
	{
		C6P_Log( "#\tno convar %s", pName );
		return;
	}
	pVar->SetValue( pValue );
	C6P_Log( "V\t%s\t%s", pName, pVar->GetString() );
}

void Dispatch( const char *pLine )
{
	CCommand args;
	args.Tokenize( pLine );
	ConCommand *pCommand = g_pCVar->FindCommand( args[ 0 ] );
	if ( !pCommand )
	{
		C6P_Log( "#\tno command %s", args[ 0 ] );
		return;
	}
	C6P_Log( "D\t%s", pLine );
	pCommand->Dispatch( args );
}

void Disarm()
{
	if ( inputsystem == &s_Recorder )
		inputsystem = s_Recorder.m_pInner;
	if ( g_pInputSystem == &s_Recorder )
		g_pInputSystem = s_Recorder.m_pInner;
	if ( s_pFixture )
	{
		fprintf( s_pFixture, "#\tend\t%d commands\n", s_nIndex );
		fclose( s_pFixture );
		s_pFixture = NULL;
	}
	Msg( "[c6-input probe] recorded %d commands, %d extra samples\n", s_nIndex, s_nExtra );
}

// --- the script -------------------------------------------------------------

// Segment 0, keys: [0, 60).
void KeySegment( int i )
{
	switch ( i )
	{
	case 0:  Dispatch( "+forward 87" ); break;
	case 3:  Dispatch( "+forward 38" ); break;          // a second key on the same button
	case 4:  Dispatch( "+forward 87" ); break;          // a repeat
	case 6:  Dispatch( "-forward 87" ); break;          // still held by 38
	case 8:  Dispatch( "-forward 38" ); break;
	case 9:  Dispatch( "+moveleft 65" ); Dispatch( "+speed 304" ); break;
	case 11: Dispatch( "-moveleft 65" ); Dispatch( "+moveright 68" ); break;
	case 12: Dispatch( "+back 83" ); Dispatch( "+jump 32" ); break;
	case 13: Dispatch( "-jump 32" ); Dispatch( "+duck 306" ); break;
	case 15: Dispatch( "-speed 304" ); Dispatch( "+walk 307" ); break;
	case 16: Dispatch( "-back 83" ); Dispatch( "-moveright 68" ); Dispatch( "-duck 306" ); break;
	case 17: Dispatch( "+attack 107" ); Dispatch( "+use 101" ); break;
	case 18: Dispatch( "-attack 107" ); Dispatch( "+attack2 108" ); Dispatch( "+reload 114" ); break;
	case 19: Dispatch( "-attack2 108" ); Dispatch( "-reload 114" ); Dispatch( "-use 101" ); Dispatch( "-walk 307" ); break;
	case 20: Dispatch( "impulse 101" ); Dispatch( "+left 130" ); break;
	case 22: Dispatch( "-left 130" ); Dispatch( "+right 131" ); Dispatch( "+speed 304" ); break;
	case 24: Dispatch( "+strafe 308" ); break;          // left/right become side moves
	case 25: Dispatch( "+left 130" ); break;
	case 27: Dispatch( "-left 130" ); Dispatch( "-right 131" ); Dispatch( "-strafe 308" ); Dispatch( "-speed 304" ); break;
	case 28: Dispatch( "+moveup 280" ); Dispatch( "+alt1 281" ); Dispatch( "+zoom 282" ); break;
	case 29: Dispatch( "-moveup 280" ); Dispatch( "+movedown 283" ); Dispatch( "-alt1 281" ); Dispatch( "+grenade1 284" ); break;
	case 30: Dispatch( "-movedown 283" ); Dispatch( "-zoom 282" ); Dispatch( "-grenade1 284" ); Dispatch( "+attack3 285" ); Dispatch( "+score 286" ); break;
	case 31: Dispatch( "-attack3 285" ); Dispatch( "-score 286" ); Dispatch( "+alt2 287" ); Dispatch( "+grenade2 288" ); break;
	case 32: Dispatch( "-alt2 287" ); Dispatch( "-grenade2 288" ); SetConVar( "cl_mouselook", "0" ); break;
	case 33: Dispatch( "+lookup 290" ); break;
	case 36: Dispatch( "-lookup 290" ); Dispatch( "+lookdown 291" ); break;
	case 40: Dispatch( "+klook 292" ); Dispatch( "+forward 87" ); break;   // forward pitches instead
	case 42: Dispatch( "-forward 87" ); Dispatch( "+back 83" ); break;
	case 44: Dispatch( "-back 83" ); Dispatch( "-klook 292" ); Dispatch( "-lookdown 291" ); break;
	case 45: SetConVar( "cl_pitchup", "30" ); Dispatch( "+lookup 290" ); break;  // the pitch clamp
	case 50: Dispatch( "-lookup 290" ); SetConVar( "cl_pitchup", "89" ); SetConVar( "cl_anglespeedkey", "0.4" ); Dispatch( "+speed 304" ); Dispatch( "+lookdown 291" ); break;
	case 53: Dispatch( "-lookdown 291" ); Dispatch( "-speed 304" ); Dispatch( "+attack 107" ); break;
	case 54: Dispatch( "-attack 107" ); Dispatch( "+forward" ); break;      // no key code: a console +forward
	case 56: Dispatch( "-forward" ); SetConVar( "in_usekeyboardsampletime", "0" ); Dispatch( "+right 131" ); break;
	case 58: Dispatch( "-right 131" ); SetConVar( "in_usekeyboardsampletime", "1" ); SetConVar( "cl_mouselook", "1" ); SetConVar( "cl_anglespeedkey", "0.67" ); break;
	default: break;
	}
}

void SetMouse( int dx, int dy )
{
	s_Recorder.m_nRawX = dx;
	s_Recorder.m_nRawY = dy;
	s_Recorder.m_bRawPending = true;
}

// Segment 1, the mouse: [60, 160), twenty commands per acceleration setting.
void MouseSegment( int i )
{
	int k = i - 60;
	switch ( k )
	{
	case 0:  SetConVar( "sensitivity", "3" ); SetConVar( "m_customaccel", "0" ); SetConVar( "m_filter", "0" ); break;
	case 10: SetConVar( "m_filter", "1" ); SetConVar( "sensitivity", "2.5" ); break;
	case 20: SetConVar( "m_filter", "0" ); SetConVar( "m_customaccel", "1" ); SetConVar( "m_customaccel_exponent", "1.3" ); SetConVar( "m_customaccel_scale", "0.04" ); break;
	case 30: SetConVar( "m_customaccel_max", "5" ); break;
	case 40: SetConVar( "m_customaccel", "2" ); SetConVar( "m_customaccel_scale", "3" ); SetConVar( "m_customaccel_max", "0" ); break;
	case 50: SetConVar( "m_customaccel", "3" ); SetConVar( "m_customaccel_exponent", "1.8" ); break;
	case 60: SetConVar( "m_customaccel", "0" ); SetConVar( "m_pitch", "-0.022" ); SetConVar( "m_yaw", "0.03" ); break;
	case 70: SetConVar( "m_pitch", "0.05" ); Dispatch( "+strafe 308" ); break;   // sv_cheats decides what m_pitch reads
	case 78: Dispatch( "-strafe 308" ); SetConVar( "lookstrafe", "1" ); break;
	case 84: SetConVar( "lookstrafe", "0" ); SetConVar( "m_side", "1.2" ); SetConVar( "m_forward", "0.7" ); Dispatch( "+strafe 308" ); break;
	case 90: Dispatch( "-strafe 308" ); SetConVar( "m_pitch", "0.022" ); SetConVar( "m_yaw", "0.022" ); SetConVar( "m_side", "0.8" ); SetConVar( "m_forward", "1" ); break;
	default: break;
	}

	int dx, dy;
	int r = RandomRange( 0, 9 );
	if ( r == 0 ) { dx = 0; dy = 0; }
	else if ( r == 1 ) { dx = RandomRange( -900, 900 ); dy = RandomRange( -900, 900 ); }   // big: the pitch clamps
	else { dx = RandomRange( -40, 40 ); dy = RandomRange( -40, 40 ); }
	SetMouse( dx, dy );
}

void Axes()
{
	for ( int a = 0; a < MAX_JOYSTICK_AXES; ++a )
	{
		int r = RandomRange( 0, 5 );
		if ( r == 0 )
			s_Recorder.m_nAxes[ a ] = 0;
		else if ( r == 1 )
			s_Recorder.m_nAxes[ a ] = RandomRange( -5000, 5000 );        // around the dead zone
		else if ( r == 2 )
			s_Recorder.m_nAxes[ a ] = RandomRange( 0, 1 ) ? 32767 : -32768; // pegged
		else
			s_Recorder.m_nAxes[ a ] = RandomRange( -32768, 32767 );
	}
}

// Segment 2, the joystick: [160, 260).
void JoystickSegment( int i )
{
	int k = i - 160;
	switch ( k )
	{
	case 0:  SetConVar( "joystick", "1" ); s_Recorder.m_nJoysticks = 1; break;
	case 10: SetConVar( "joy_response_move", "0" ); break;
	case 15: SetConVar( "joy_response_move", "2" ); break;
	case 20: SetConVar( "joy_response_move", "3" ); break;
	case 25: SetConVar( "joy_response_move", "4" ); break;
	case 30: SetConVar( "joy_response_move", "5" ); SetConVar( "joy_response_look", "1" ); break;
	case 40: SetConVar( "joy_response_move", "1" ); SetConVar( "joy_lowend", "0.6" ); SetConVar( "joy_lowmap", "0.3" ); SetConVar( "joy_response_look", "0" ); SetConVar( "joy_virtual_peg", "1" ); break;
	case 50: SetConVar( "joy_inverty", "1" ); Dispatch( "+jlook 293" ); SetConVar( "joy_autosprint", "0.5" ); break;
	case 58: SetConVar( "joy_inverty", "0" ); Dispatch( "+strafe 308" ); break;
	case 62: Dispatch( "-strafe 308" ); SetConVar( "lookstrafe", "1" ); break;
	case 66: SetConVar( "lookstrafe", "0" ); Dispatch( "-jlook 293" ); SetConVar( "joy_advanced", "0" ); Dispatch( "joyadvancedupdate" ); break;
	case 72: Dispatch( "+jlook 293" ); break;                 // forward becomes pitch
	case 76: Dispatch( "-jlook 293" ); SetConVar( "joy_advanced", "1" ); SetConVar( "joy_advaxisx", "20" ); SetConVar( "joy_advaxisy", "18" ); Dispatch( "joyadvancedupdate" ); break;  // relative axes
	case 84: Dispatch( "+jlook 293" ); break;
	case 88: Dispatch( "-jlook 293" ); s_Recorder.m_nJoysticks = 0; break;   // unplugged
	case 92: s_Recorder.m_nJoysticks = 1; SetConVar( "joy_advaxisx", "4" ); SetConVar( "joy_advaxisy", "2" ); SetConVar( "joy_wingmanwarrior_turnhack", "1" ); break;
	case 96: SetConVar( "joy_wingmanwarrior_turnhack", "0" ); SetConVar( "joy_lowend", "1" ); SetConVar( "joy_lowmap", "1" ); SetConVar( "joy_virtual_peg", "0" ); SetConVar( "joy_autosprint", "0" ); break;
	case 99: SetConVar( "joystick", "0" ); s_Recorder.m_nJoysticks = 0; break;
	default: break;
	}
	Axes();
}

// Segment 3, the Steam Controller: [260, 330).
void ControllerSegment( int i )
{
	int k = i - 260;
	if ( k == 0 )
	{
		// The pad's action handles are fetched once, at Init_All, from the
		// input system that was there then; fetch them again from the fake (a
		// public member of CInput, which our shell's input derives from).
		s_Recorder.m_bController = true;
		C6P_Log( "SINIT" );
		static_cast< CInput * >( ::input )->InitializeSteamControllerGameActionSets();
	}
	if ( k == 66 )
		s_Controller.m_nConnected = 0;

	// Toggle a few actions, each by its own rhythm; "active" drops now and then.
	static const char *const s_pszActions[] = { "jump", "attack", "duck", "invnext", "showscores", "reload" };
	for ( int a = 0; a < (int)ARRAYSIZE( s_pszActions ); ++a )
	{
		int h = s_Controller.HandleFor( s_pszActions[ a ] );
		s_Controller.m_bDigital[ h - 1 ] = ( ( k / ( a + 2 ) ) % 2 ) == 1;
		s_Controller.m_bDigitalActive[ h - 1 ] = ( k % 13 ) != 5;
	}

	s_Controller.m_Move.eMode = k_EInputSourceMode_JoystickMove;
	s_Controller.m_Move.bActive = true;
	s_Controller.m_Move.x = RandomRange( -1200, 1200 ) / 1000.0f;   // past 1: the normalise
	s_Controller.m_Move.y = RandomRange( -1200, 1200 ) / 1000.0f;
	s_Controller.m_Camera.eMode = k_EInputSourceMode_AbsoluteMouse;
	s_Controller.m_Camera.bActive = true;
	int r = RandomRange( 0, 4 );
	s_Controller.m_Camera.x = r == 0 ? 0.0f : RandomRange( -400, 400 ) / 10.0f;
	s_Controller.m_Camera.y = r == 0 ? 0.0f : RandomRange( -400, 400 ) / 10.0f;
	if ( k == 40 )
		SetConVar( "sc_yaw_sensitivity", "1.7" );
	if ( k == 50 )
		SetConVar( "sc_pitch_sensitivity", "-0.5" );
}

// Segment 4, third person: [330, 420).
void ThirdPersonSegment( int i )
{
	int k = i - 330;
	switch ( k )
	{
	case 0:  s_Recorder.m_bController = false; Dispatch( "thirdperson" ); break;
	case 4:  Dispatch( "+camyawleft 300" ); Dispatch( "+camin 301" ); break;
	case 8:  Dispatch( "-camyawleft 300" ); Dispatch( "-camin 301" ); Dispatch( "+campitchup 302" ); Dispatch( "+camout 303" ); break;
	case 12: Dispatch( "-campitchup 302" ); Dispatch( "-camout 303" ); Dispatch( "+camyawright 304" ); Dispatch( "+campitchdown 305" ); break;
	case 16: Dispatch( "-camyawright 304" ); Dispatch( "-campitchdown 305" ); Dispatch( "snapto" ); break;
	case 20: Dispatch( "snapto" ); SetConVar( "thirdperson_platformer", "1" ); Dispatch( "+forward 87" ); break;
	case 26: Dispatch( "-forward 87" ); Dispatch( "+moveleft 65" ); Dispatch( "+right 131" ); break;
	case 30: Dispatch( "-moveleft 65" ); Dispatch( "-right 131" ); break;
	case 40: SetConVar( "joystick", "1" ); s_Recorder.m_nJoysticks = 1; break;
	case 48: SetConVar( "joystick", "0" ); s_Recorder.m_nJoysticks = 0; SetConVar( "thirdperson_platformer", "0" ); SetConVar( "thirdperson_screenspace", "1" ); SetConVar( "cam_idealyaw", "35" ); Dispatch( "+forward 87" ); Dispatch( "+moveright 68" ); break;
	case 56: Dispatch( "-forward 87" ); Dispatch( "-moveright 68" ); Dispatch( "+back 83" ); Dispatch( "+moveleft 65" ); break;
	case 62: Dispatch( "-back 83" ); Dispatch( "-moveleft 65" ); SetConVar( "joystick", "1" ); s_Recorder.m_nJoysticks = 1; break;
	case 70: SetConVar( "joystick", "0" ); s_Recorder.m_nJoysticks = 0; SetConVar( "thirdperson_screenspace", "0" ); SetConVar( "cam_ideallag", "0" ); break;
	case 76: SetConVar( "cam_ideallag", "4" ); Dispatch( "camortho" ); break;
	case 80: Dispatch( "firstperson" ); SetConVar( "cam_idealyaw", "0" ); break;
	default: break;
	}
	if ( k >= 30 && k < 48 )
		SetMouse( RandomRange( -60, 60 ), RandomRange( -60, 60 ) );
	if ( ( k >= 40 && k < 48 ) || ( k >= 62 && k < 70 ) )
		Axes();
}

} // namespace

//-----------------------------------------------------------------------------
void C6P_Log( const char *pFmt, ... )
{
	if ( !s_pFixture )
		return;
	va_list ap;
	va_start( ap, pFmt );
	vfprintf( s_pFixture, pFmt, ap );
	va_end( ap );
	fputc( '\n', s_pFixture );
}

bool C6P_Armed() { return s_pFixture != NULL; }

void C6P_NameButton( const kbutton_t *pButton, const char *pName )
{
	for ( int i = 0; i < s_nNames; ++i )
		if ( s_Names[ i ].p == pButton )
			return;
	if ( s_nNames < (int)ARRAYSIZE( s_Names ) )
		s_Names[ s_nNames++ ] = { pButton, pName };
}

const char *C6P_ButtonName( const kbutton_t *pButton )
{
	for ( int i = 0; i < s_nNames; ++i )
		if ( s_Names[ i ].p == pButton )
			return s_Names[ i ].name;
	return "?";
}

void C6P_BeforeCreateMove( int nSequence )
{
	if ( !s_pFixture )
		return;
	if ( s_nCommands <= 0 )
	{
		Disarm();
		return;
	}

	int i = s_nIndex;
	if ( i < 60 )
		KeySegment( i );
	else if ( i < 160 )
		MouseSegment( i );
	else if ( i < 260 )
		JoystickSegment( i );
	else if ( i < 330 )
		ControllerSegment( i );
	else
		ThirdPersonSegment( i );

	++s_nIndex;
	--s_nCommands;
}

void C6P_RumbleFrame();   // the c_rumble.cpp probe copy

void C6P_BeforeExtraMouseSample()
{
	C6P_RumbleFrame();
	if ( !s_pFixture )
		return;
	++s_nExtra;
	// Mouse arriving between commands, now and then, in the mouse segment.
	if ( s_nIndex >= 60 && s_nIndex < 160 && ( s_nExtra % 3 ) == 0 )
		SetMouse( RandomRange( -8, 8 ), RandomRange( -8, 8 ) );
}

CON_COMMAND( cs_cl_probe_input, "c6-input probe (debug): record the stock input code over N scripted user commands into $SOURCESHARP_C6_FIXTURE_DIR/input-stock.tsv" )
{
	if ( s_pFixture )
	{
		Msg( "[c6-input probe] already recording\n" );
		return;
	}
	const char *pDir = getenv( "SOURCESHARP_C6_FIXTURE_DIR" );
	if ( !pDir || !*pDir )
	{
		Msg( "[c6-input probe] SOURCESHARP_C6_FIXTURE_DIR is not set\n" );
		return;
	}
	char path[ 1024 ];
	Q_snprintf( path, sizeof( path ), "%s/input-stock.tsv", pDir );
	s_pFixture = fopen( path, "w" );
	if ( !s_pFixture )
	{
		Msg( "[c6-input probe] cannot open %s\n", path );
		return;
	}

	s_nCommands = args.ArgC() > 1 ? atoi( args[ 1 ] ) : 420;
	s_nIndex = 0;
	s_nExtra = 0;

	fprintf( s_pFixture, "# input-stock.tsv: STOCK in_main/in_mouse/in_joystick/in_steamcontroller/in_camera, recorded -- see README-input-stock.md.\n" );
	for ( int i = 0; i < (int)ARRAYSIZE( s_pszConVars ); ++i )
	{
		ConVar *pVar = g_pCVar->FindVar( s_pszConVars[ i ] );
		if ( pVar )
			fprintf( s_pFixture, "V\t%s\t%s\n", s_pszConVars[ i ], pVar->GetString() );
		else
			fprintf( s_pFixture, "#\tno convar %s\n", s_pszConVars[ i ] );
	}

	s_Recorder.m_pInner = inputsystem;
	inputsystem = &s_Recorder;
	g_pInputSystem = &s_Recorder;
	Msg( "[c6-input probe] recording %d commands into %s\n", s_nCommands, path );
}
