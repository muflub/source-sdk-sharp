//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "inputsystem/AnalogCode.h"

#include "steam/isteamcontroller.h"

// Main interface for input. This is a low-level interface
#define INPUTSYSTEM_INTERFACE_VERSION	"InputSystemVersion001"
abstract_class IInputSystem : public IAppSystem
{
public:
	// Attach, detach input system from a particular window
	// This window should be the root window for the application
	// Only 1 window should be attached at any given time.
	virtual void unused_AttachToWindow() = 0;
	virtual void unused_DetachFromWindow() = 0;

	// Enables/disables input. PollInputState will not update current 
	// button/analog states when it is called if the system is disabled.
	virtual void unused_EnableInput() = 0;

	// Enables/disables the windows message pump. PollInputState will not
	// Peek/Dispatch messages if this is disabled
	virtual void unused_EnableMessagePump() = 0;

	// Polls the current input state
	virtual void unused_PollInputState() = 0;

	// Gets the time of the last polling in ms
	virtual void unused_GetPollTick() = 0;

	// Is a button down? "Buttons" are binary-state input devices (mouse buttons, keyboard keys)
	virtual bool IsButtonDown( ButtonCode_t code ) const = 0;

	// Returns the tick at which the button was pressed and released
	virtual void unused_GetButtonPressedTick() = 0;
	virtual void unused_GetButtonReleasedTick() = 0;

	// Gets the value of an analog input device this frame
	// Includes joysticks, mousewheel, mouse
	virtual int GetAnalogValue( AnalogCode_t code ) const = 0;

	// Gets the change in a particular analog input device this frame
	// Includes joysticks, mousewheel, mouse
	virtual void unused_GetAnalogDelta() = 0;

	// Returns the input events since the last poll
	virtual void unused_GetEventCount() = 0;
	virtual void unused_GetEventData() = 0;

	// Posts a user-defined event into the event queue; this is expected
	// to be called in overridden wndprocs connected to the root panel.
	virtual void unused_PostUserEvent() = 0;

	// Returns the number of joysticks
	virtual int GetJoystickCount() const = 0;

	// Enable/disable joystick, it has perf costs
	virtual void unused_EnableJoystickInput() = 0;

	// Enable/disable diagonal joystick POV (simultaneous POV buttons down)
	virtual void unused_EnableJoystickDiagonalPOV() = 0;

	// Sample the joystick and append events to the input queue
	virtual void unused_SampleDevices() = 0;

	virtual void SetRumble( float fLeftMotor, float fRightMotor, int userId = INVALID_USER_ID ) = 0;
	virtual void StopRumble() = 0;

	// Resets the input state
	virtual void unused_ResetInputState() = 0;

	// Sets a player as the primary user - all other controllers will be ignored.
	virtual void unused_SetPrimaryUserId() = 0;

	// Convert back + forth between ButtonCode/AnalogCode + strings
	virtual const char *ButtonCodeToString( ButtonCode_t code ) const = 0;
	virtual void unused_AnalogCodeToString() = 0;
	virtual void unused_StringToButtonCode() = 0;
	virtual void unused_StringToAnalogCode() = 0;

	// Sleeps until input happens. Pass a negative number to sleep infinitely
	virtual void unused_SleepUntilInput() = 0;

	// Convert back + forth between virtual codes + button codes
	// FIXME: This is a temporary piece of code
	virtual void unused_VirtualKeyToButtonCode() = 0;
	virtual void unused_ButtonCodeToVirtualKey() = 0;
	virtual void unused_ScanCodeToButtonCode() = 0;

	// How many times have we called PollInputState?
	virtual void unused_GetPollCount() = 0;

	// Sets the cursor position
	virtual void unused_SetCursorPosition() = 0;

	// NVNT get address to haptics interface
	virtual void unused_GetHapticsInterfaceAddress() = 0;

	virtual void unused_SetNovintPure() = 0;

	// read and clear accumulated raw input values
	virtual bool GetRawMouseAccumulators( int& accumX, int& accumY ) = 0;

	// tell the input system that we're not a game, we're console text mode.
	// this is used for dedicated servers to not initialize joystick system.
	// this needs to be called before CInputSystem::Init (e.g. in PreInit of
	// some system) if you want ot prevent the joystick system from ever
	// being initialized.
	virtual void unused_SetConsoleTextMode() = 0;

	virtual ISteamController* SteamControllerInterface() = 0;
	virtual void unused_GetNumSteamControllersConnected() = 0;
	virtual bool IsSteamControllerActive() = 0;
	virtual void unused_IsSteamControllerConnected() = 0;
	virtual void unused_GetSteamControllerIndexForSlot() = 0;
	virtual void unused_GetRadialMenuStickValues() = 0;
	virtual void ActivateSteamControllerActionSetForSlot( uint64 nSlot, GameActionSet_t eActionSet ) = 0;
	virtual ControllerActionSetHandle_t GetActionSetHandle( GameActionSet_t eActionSet ) = 0;
	virtual ControllerActionSetHandle_t GetActionSetHandle( const char* szActionSet ) = 0;

	// Gets the action origin (i.e. which physical input) maps to the given action for the given action set
	virtual EControllerActionOrigin GetSteamControllerActionOrigin( const char* action, GameActionSet_t action_set ) = 0;
	virtual EControllerActionOrigin GetSteamControllerActionOrigin( const char* action, ControllerActionSetHandle_t action_set_handle ) = 0;

	// Maps a Steam Controller action origin to a string (consisting of a single character) in our SC icon font
	virtual void unused_GetSteamControllerFontCharacterForActionOrigin() = 0;

	// Maps a Steam Controller action origin to a short text string (e.g. "X", "LB", "LDOWN") describing the control.
	// Prefer to actually use the icon font wherever possible.
	virtual void unused_GetSteamControllerDescriptionForActionOrigin() = 0;

	// This is called with "true" by dedicated server initialization (before calling Init) in order to
	// force us to skip initialization of Steam (which messes up dedicated servers).
	virtual void unused_SetSkipControllerInitialization() = 0;

};
