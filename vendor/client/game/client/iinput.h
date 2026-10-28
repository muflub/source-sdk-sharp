//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

class bf_write;
class bf_read;
class CUserCmd;
class C_BaseCombatWeapon;
struct kbutton_t;

struct CameraThirdData_t
{
	float	m_flPitch;
	float	m_flYaw;
	float	m_flDist;
	float	m_flLag;
	Vector	m_vecHullMin;
	Vector	m_vecHullMax;
};

abstract_class IInput
{
public:
	// Initialization/shutdown of the subsystem
	virtual	void		Init_All() = 0;
	virtual void		Shutdown_All() = 0;
	// Latching button states
	virtual int			GetButtonBits( int ) = 0;
	// Create movement command
	virtual void		CreateMove ( int sequence_number, float input_sample_frametime, bool active ) = 0;
	virtual void		ExtraMouseSample( float frametime, bool active ) = 0;
	virtual bool		WriteUsercmdDeltaToBuffer( bf_write *buf, int from, int to, bool isnewcommand ) = 0;
	virtual void		EncodeUserCmdToBuffer( bf_write& buf, int slot ) = 0;
	virtual void		DecodeUserCmdFromBuffer( bf_read& buf, int slot ) = 0;

	virtual CUserCmd	*GetUserCmd( int sequence_number ) = 0;

	virtual void		MakeWeaponSelection( C_BaseCombatWeapon *weapon ) = 0;

	// Retrieve key state
	virtual float		KeyState ( kbutton_t *key ) = 0;
	// Issue key event
	virtual int			KeyEvent( int eventcode, ButtonCode_t keynum, const char *pszCurrentBinding ) = 0;
	// Look for key
	virtual kbutton_t	*FindKey( const char *name ) = 0;

	// Issue commands from controllers
	virtual void		ControllerCommands() = 0;
	// Extra initialization for some joysticks
	virtual void		Joystick_Advanced() = 0;
	virtual void		Joystick_SetSampleTime( float frametime ) = 0;
	virtual void		IN_SetSampleTime( float frametime ) = 0;

	// Accumulate mouse delta
	virtual void		AccumulateMouse() = 0;
	// Activate/deactivate mouse
	virtual void		ActivateMouse() = 0;
	virtual void		DeactivateMouse() = 0;

	// Clear mouse state data
	virtual void		ClearStates() = 0;
	// Retrieve lookspring setting
	virtual float		GetLookSpring() = 0;

	// Retrieve mouse position
	virtual void		GetFullscreenMousePos( int *mx, int *my, int *unclampedx = 0, int *unclampedy = 0 ) = 0;
	virtual void		SetFullscreenMousePos( int mx, int my ) = 0;
	virtual void		ResetMouse() = 0;
	virtual	float		GetLastForwardMove() = 0;
	virtual	float		Joystick_GetForward() = 0;
	virtual	float		Joystick_GetSide() = 0;
	virtual	float		Joystick_GetPitch() = 0;
	virtual	float		Joystick_GetYaw() = 0;

	// Third Person camera ( TODO/FIXME:  Move this to a separate interface? )
	virtual void		CAM_Think() = 0;
	virtual int			CAM_IsThirdPerson() = 0;
	virtual void		CAM_ToThirdPerson() = 0;
	virtual void		CAM_ToFirstPerson() = 0;
	virtual void		CAM_StartMouseMove() = 0;
	virtual void		CAM_EndMouseMove() = 0;
	virtual void		CAM_StartDistance() = 0;
	virtual void		CAM_EndDistance() = 0;
	virtual int			CAM_InterceptingMouse() = 0;

	// orthographic camera info	( TODO/FIXME:  Move this to a separate interface? )
	virtual void		CAM_ToOrthographic() = 0;
	virtual	bool		CAM_IsOrthographic() const = 0;
	virtual	void		CAM_OrthographicSize( float& w, float& h ) const = 0;

	virtual void		SetPreferredGameActionSet( GameActionSet_t action_set ) = 0;
	virtual GameActionSet_t GetPreferredGameActionSet() = 0;
	virtual void		SetGameActionSetFlags( GameActionSetFlags_t action_set_flags ) = 0;

	virtual void		LevelInit() = 0;

	// Causes an input to have to be re-pressed to become active
	virtual void		ClearInputButton( int bits ) = 0;

	virtual	void		CAM_SetCameraThirdData( CameraThirdData_t *pCameraData, const QAngle &vecCameraOffset ) = 0;
	virtual void		CAM_CameraThirdThink() = 0;

	virtual	bool		EnableJoystickMode() = 0;

	virtual bool		IsSteamControllerActive() = 0;
};

extern ::IInput *input;
