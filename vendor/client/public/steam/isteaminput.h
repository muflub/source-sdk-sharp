//====== Copyright 1996-2018, Valve Corporation, All rights reserved. =======
// Purpose: Steam Input is a flexible input API that supports over three hundred devices including all
//          common variants of Xbox, Playstation, Nintendo Switch Pro, and Steam Controllers.
//			For more info including a getting started guide for developers
//			please visit: https://partner.steamgames.com/doc/features/steam_controller

#pragma once
#define ISTEAMINPUT_H

#include "steam_api_common.h"

#define STEAM_INPUT_MAX_COUNT 16

#define STEAM_INPUT_MAX_ORIGINS 8

#define STEAM_INPUT_MAX_ACTIVE_LAYERS 16

// When sending an option to a specific controller handle, you can send to all devices via this command

enum EInputSourceMode
{
	k_EInputSourceMode_None,
	k_EInputSourceMode_Dpad,
	k_EInputSourceMode_Buttons,
	k_EInputSourceMode_FourButtons,
	k_EInputSourceMode_AbsoluteMouse,
	k_EInputSourceMode_RelativeMouse,
	k_EInputSourceMode_JoystickMove,
	k_EInputSourceMode_JoystickMouse,
	k_EInputSourceMode_JoystickCamera,
	k_EInputSourceMode_ScrollWheel,
	k_EInputSourceMode_Trigger,
	k_EInputSourceMode_TouchMenu,
	k_EInputSourceMode_MouseJoystick,
	k_EInputSourceMode_MouseRegion,
	k_EInputSourceMode_RadialMenu,
	k_EInputSourceMode_SingleButton,
	k_EInputSourceMode_Switches
};

enum ESteamInputType
{
	k_ESteamInputType_Unknown,
	k_ESteamInputType_SteamController,
	k_ESteamInputType_XBox360Controller,
	k_ESteamInputType_XBoxOneController,
	k_ESteamInputType_GenericGamepad, // DirectInput controllers
	k_ESteamInputType_PS4Controller,
	k_ESteamInputType_AppleMFiController, // Unused
	k_ESteamInputType_AndroidController, // Unused
	k_ESteamInputType_SwitchJoyConPair, // Unused
	k_ESteamInputType_SwitchJoyConSingle, // Unused
	k_ESteamInputType_SwitchProController,
	k_ESteamInputType_MobileTouch, // Steam Link App On-screen Virtual Controller
	k_ESteamInputType_PS3Controller, // Currently uses PS4 Origins
	k_ESteamInputType_PS5Controller, // Added in SDK 151
	k_ESteamInputType_SteamDeckController, // Added in SDK 153
	k_ESteamInputType_Count,
	k_ESteamInputType_MaximumPossibleValue = 255,
};

// InputHandle_t is used to refer to a specific controller.
// This handle will consistently identify a controller, even if it is disconnected and re-connected
typedef uint64 InputHandle_t;

#pragma pack(push, 1)

struct InputAnalogActionData_t
{
	// Type of data coming from this action, this will match what got specified in the action set
	EInputSourceMode eMode;

	// The current state of this action; will be delta updates for mouse actions
	float x, y;

	// Whether or not this action is currently available to be bound in the active action set
	bool bActive;
};

struct InputDigitalActionData_t
{
	// The current state of this action; will be true if currently pressed
	bool bState;

	// Whether or not this action is currently available to be bound in the active action set
	bool bActive;
};

struct InputMotionData_t
{
	// Sensor-fused absolute rotation; will drift in heading toward average
	float rotQuatX;
	float rotQuatY;
	float rotQuatZ;
	float rotQuatW;

	// Positional acceleration
	float posAccelX;
	float posAccelY;
	float posAccelZ;

	// Angular velocity
	float rotVelX;
	float rotVelY;
	float rotVelZ;
};

#pragma pack(pop)

// Purpose: Steam Input API
class ISteamInput
{
public:
	// Init and Shutdown must be called when starting/ending use of this interface.
	// if bExplicitlyCallRunFrame is called then you will need to manually call RunFrame
	// each frame, otherwise Steam Input will updated when SteamAPI_RunCallbacks() is called
	virtual bool Init(bool bExplicitlyCallRunFrame) = 0;
	virtual bool Shutdown() = 0;

	// Set the absolute path to the Input Action Manifest file containing the in-game actions
	// and file paths to the official configurations. Used in games that bundle Steam Input
	// configurations inside of the game depot instead of using the Steam Workshop
	virtual void unused_SetInputActionManifestFilePath() = 0;

	// Synchronize API state with the latest Steam Input action data available. This
	// is performed automatically by SteamAPI_RunCallbacks, but for the absolute lowest
	// possible latency, you call this directly before reading controller state.
	// Note: This must be called from somewhere before GetConnectedControllers will
	// return any handles
	virtual void RunFrame(bool bReservedValue = true) = 0;

	// Waits on an IPC event from Steam sent when there is new data to be fetched from
	// the data drop. Returns true when data was recievied before the timeout expires.
	// Useful for games with a dedicated input thread
	virtual void unused_BWaitForData() = 0;

	// Returns true if new data has been received since the last time action data was accessed
	// via GetDigitalActionData or GetAnalogActionData. The game will still need to call
	// SteamInput()->RunFrame() or SteamAPI_RunCallbacks() before this to update the data stream
	virtual void unused_BNewDataAvailable() = 0;

	// Enumerate currently connected Steam Input enabled devices - developers can opt in controller by type (ex: Xbox/Playstation/etc) via
	// the Steam Input settings in the Steamworks site or users can opt-in in their controller settings in Steam.
	// handlesOut should point to a STEAM_INPUT_MAX_COUNT sized array of InputHandle_t handles
	// Returns the number of handles written to handlesOut
	virtual void unused_GetConnectedControllers() = 0;

	// CALLBACKS

	// Controller configuration loaded - these callbacks will always fire if you have
	// a handler. Note: this is called within either SteamInput()->RunFrame or by SteamAPI_RunCallbacks
	STEAM_CALL_BACK(SteamInputConfigurationLoaded_t)

	// Enable SteamInputDeviceConnected_t and SteamInputDeviceDisconnected_t callbacks.
	// Each controller that is already connected will generate a device connected
	// callback when you enable them
	virtual void unused_EnableDeviceCallbacks() = 0;

	// Controller Connected - provides info about a single newly connected controller
	// Note: this is called within either SteamInput()->RunFrame or by SteamAPI_RunCallbacks
	STEAM_CALL_BACK(SteamInputDeviceConnected_t)

	// Controller Disconnected - provides info about a single disconnected controller
	// Note: this is called within either SteamInput()->RunFrame or by SteamAPI_RunCallbacks
	STEAM_CALL_BACK(SteamInputDeviceDisconnected_t)

	// Controllers using Gamepad emulation (XInput, DirectInput, etc) will be seated in the order that
	// input is sent by the device. This callback will fire on first input for each device and when the
	// a user has manually changed the order via the Steam overlay. This also has the device type info
	// so that you can change out glyph sets without making additional API calls
	STEAM_CALL_BACK(SteamInputGamepadSlotChange_t)

	// Enable SteamInputActionEvent_t callbacks. Directly calls your callback function
	// for lower latency than standard Steam callbacks. Supports one callback at a time.
	// Note: this is called within either SteamInput()->RunFrame or by SteamAPI_RunCallbacks
	virtual void unused_EnableActionEventCallbacks() = 0;

	// ACTION SETS

	// Lookup the handle for an Action Set. Best to do this once on startup, and store the handles for all future API calls.
	virtual void unused_GetActionSetHandle() = 0;

	// Reconfigure the controller to use the specified action set (ie 'Menu', 'Walk' or 'Drive')
	// This is cheap, and can be safely called repeatedly. It's often easier to repeatedly call it in
	// your state loops, instead of trying to place it in all of your state transitions.
	virtual void unused_ActivateActionSet() = 0;
	virtual void unused_GetCurrentActionSet() = 0;

	// ACTION SET LAYERS
	virtual void unused_ActivateActionSetLayer() = 0;
	virtual void unused_DeactivateActionSetLayer() = 0;
	virtual void unused_DeactivateAllActionSetLayers() = 0;

	// Enumerate currently active layers.
	// handlesOut should point to a STEAM_INPUT_MAX_ACTIVE_LAYERS sized array of InputActionSetHandle_t handles
	// Returns the number of handles written to handlesOut
	virtual void unused_GetActiveActionSetLayers() = 0;

	// ACTIONS

	// Lookup the handle for a digital action. Best to do this once on startup, and store the handles for all future API calls.
	virtual void unused_GetDigitalActionHandle() = 0;

	// Returns the current state of the supplied digital game action
	virtual void unused_GetDigitalActionData() = 0;

	// Get the origin(s) for a digital action within an action set. Returns the number of origins supplied in originsOut. Use this to display the appropriate on-screen prompt for the action.
	// originsOut should point to a STEAM_INPUT_MAX_ORIGINS sized array of EInputActionOrigin handles. The EInputActionOrigin enum will get extended as support for new controller controllers gets added to
	// the Steam client and will exceed the values from this header, please check bounds if you are using a look up table.
	virtual void unused_GetDigitalActionOrigins() = 0;

	// Returns a localized string (from Steam's language setting) for the user-facing action name corresponding to the specified handle
	virtual void unused_GetStringForDigitalActionName() = 0;

	// Lookup the handle for an analog action. Best to do this once on startup, and store the handles for all future API calls.
	virtual void unused_GetAnalogActionHandle() = 0;

	// Returns the current state of these supplied analog game action
	virtual void unused_GetAnalogActionData() = 0;

	// Get the origin(s) for an analog action within an action set. Returns the number of origins supplied in originsOut. Use this to display the appropriate on-screen prompt for the action.
	// originsOut should point to a STEAM_INPUT_MAX_ORIGINS sized array of EInputActionOrigin handles. The EInputActionOrigin enum will get extended as support for new controller controllers gets added to
	// the Steam client and will exceed the values from this header, please check bounds if you are using a look up table.
	virtual void unused_GetAnalogActionOrigins() = 0;

	// Get a local path to a PNG file for the provided origin's glyph.
	virtual void unused_GetGlyphPNGForActionOrigin() = 0;

	// Get a local path to a SVG file for the provided origin's glyph.
	virtual void unused_GetGlyphSVGForActionOrigin() = 0;

	// Get a local path to an older, Big Picture Mode-style PNG file for a particular origin
	virtual void unused_GetGlyphForActionOrigin_Legacy() = 0;

	// Returns a localized string (from Steam's language setting) for the specified origin.
	virtual void unused_GetStringForActionOrigin() = 0;

	// Returns a localized string (from Steam's language setting) for the user-facing action name corresponding to the specified handle
	virtual void unused_GetStringForAnalogActionName() = 0;

	// Stop analog momentum for the action if it is a mouse action in trackball mode
	virtual void unused_StopAnalogActionMomentum() = 0;

	// Returns raw motion data from the specified device
	virtual void unused_GetMotionData() = 0;

	// OUTPUTS

	// Trigger a vibration event on supported controllers - Steam will translate these commands into haptic pulses for Steam Controllers
	virtual void unused_TriggerVibration() = 0;

	// Trigger a vibration event on supported controllers including Xbox trigger impulse rumble - Steam will translate these commands into haptic pulses for Steam Controllers
	virtual void unused_TriggerVibrationExtended() = 0;

	// Send a haptic pulse, works on Steam Deck and Steam Controller devices
	virtual void unused_TriggerSimpleHapticEvent() = 0;

	// Set the controller LED color on supported controllers. nFlags is a bitmask of values from ESteamInputLEDFlag - 0 will default to setting a color. Steam will handle
	// the behavior on exit of your program so you don't need to try restore the default as you are shutting down
	virtual void unused_SetLEDColor() = 0;

	// Trigger a haptic pulse on a Steam Controller - if you are approximating rumble you may want to use TriggerVibration instead.
	// Good uses for Haptic pulses include chimes, noises, or directional gameplay feedback (taking damage, footstep locations, etc).
	virtual void unused_Legacy_TriggerHapticPulse() = 0;

	// Trigger a haptic pulse with a duty cycle of usDurationMicroSec / usOffMicroSec, unRepeat times. If you are approximating rumble you may want to use TriggerVibration instead.
	// nFlags is currently unused and reserved for future use.
	virtual void unused_Legacy_TriggerRepeatedHapticPulse() = 0;

	// Utility functions available without using the rest of Steam Input API

	// Invokes the Steam overlay and brings up the binding screen if the user is using Big Picture Mode
	// If the user is not in Big Picture Mode it will open up the binding in a new window
	virtual void unused_ShowBindingPanel() = 0;

	// Returns the input type for a particular handle - unlike EInputActionOrigin which update with Steam and may return unrecognized values
	// ESteamInputType will remain static and only return valid values from your SDK version
	virtual void unused_GetInputTypeForHandle() = 0;

	// Returns the associated controller handle for the specified emulated gamepad - can be used with the above 2 functions
	// to identify controllers presented to your game over Xinput. Returns 0 if the Xinput index isn't associated with Steam Input
	virtual void unused_GetControllerForGamepadIndex() = 0;

	// Returns the associated gamepad index for the specified controller, if emulating a gamepad or -1 if not associated with an Xinput index
	virtual void unused_GetGamepadIndexForController() = 0;

	// Returns a localized string (from Steam's language setting) for the specified Xbox controller origin.
	virtual void unused_GetStringForXboxOrigin() = 0;

	// Get a local path to art for on-screen glyph for a particular Xbox controller origin
	virtual void unused_GetGlyphForXboxOrigin() = 0;

	// Get the equivalent ActionOrigin for a given Xbox controller origin this can be chained with GetGlyphForActionOrigin to provide future proof glyphs for
	// non-Steam Input API action games. Note - this only translates the buttons directly and doesn't take into account any remapping a user has made in their configuration
	virtual void unused_GetActionOriginFromXboxOrigin() = 0;

	// Convert an origin to another controller type - for inputs not present on the other controller type this will return k_EInputActionOrigin_None
	// When a new input type is added you will be able to pass in k_ESteamInputType_Unknown and the closest origin that your version of the SDK recognized will be returned
	// ex: if a Playstation 5 controller was released this function would return Playstation 4 origins.
	virtual void unused_TranslateActionOrigin() = 0;

	// Get the binding revision for a given device. Returns false if the handle was not valid or if a mapping is not yet loaded for the device
	virtual void unused_GetDeviceBindingRevision() = 0;

	// Get the Steam Remote Play session ID associated with a device, or 0 if there is no session associated with it
	// See isteamremoteplay.h for more information on Steam Remote Play sessions
	virtual void unused_GetRemotePlaySessionID() = 0;

	// Get a bitmask of the Steam Input Configuration types opted in for the current session. Returns ESteamInputConfigurationEnableType values.
	// Note: user can override the settings from the Steamworks Partner site so the returned values may not exactly match your default configuration
	virtual void unused_GetSessionInputConfigurationSettings() = 0;

	// Set the trigger effect for a DualSense controller
	virtual void unused_SetDualSenseTriggerEffect() = 0;
};

#define STEAMINPUT_INTERFACE_VERSION "SteamInput006"

STEAM_DEFINE_USER_INTERFACE_ACCESSOR(ISteamInput*, SteamInput, STEAMINPUT_INTERFACE_VERSION);

#if defined(VALVE_CALLBACK_PACK_SMALL)
#pragma pack(push, 4)
#elif defined(VALVE_CALLBACK_PACK_LARGE)
#pragma pack(push, 8)
#else
#error steam_api_common.h should define VALVE_CALLBACK_PACK_xxx
#endif

// Purpose: called when a new controller has been connected, will fire once
// per controller if multiple new controllers connect in the same frame
struct SteamInputDeviceConnected_t
{
	enum
	{
		k_iCallback = k_iSteamControllerCallbacks + 1
	};

	InputHandle_t m_ulConnectedDeviceHandle; // Handle for device
};

// Purpose: called when a new controller has been connected, will fire once
// per controller if multiple new controllers connect in the same frame
struct SteamInputDeviceDisconnected_t
{
	enum
	{
		k_iCallback = k_iSteamControllerCallbacks + 2
	};

	InputHandle_t m_ulDisconnectedDeviceHandle; // Handle for device
};

// Purpose: called when a controller configuration has been loaded, will fire once
// per controller per focus change for Steam Input enabled controllers
struct SteamInputConfigurationLoaded_t
{
	enum
	{
		k_iCallback = k_iSteamControllerCallbacks + 3
	};

	AppId_t m_unAppID;
	InputHandle_t m_ulDeviceHandle; // Handle for device
	CSteamID m_ulMappingCreator; // May differ from local user when using
	// an unmodified community or official config
	uint32 m_unMajorRevision; // Binding revision from In-game Action File.
	// Same value as queried by GetDeviceBindingRevision
	uint32 m_unMinorRevision;
	bool m_bUsesSteamInputAPI; // Does the configuration contain any Analog/Digital actions?
	bool m_bUsesGamepadAPI; // Does the configuration contain any Xinput bindings?
};

// Purpose: called when controller gamepad slots change - on Linux/macOS these
// slots are shared for all running apps.
struct SteamInputGamepadSlotChange_t
{
	enum
	{
		k_iCallback = k_iSteamControllerCallbacks + 4
	};

	AppId_t m_unAppID;
	InputHandle_t m_ulDeviceHandle; // Handle for device
	ESteamInputType m_eDeviceType; // Type of device
	int m_nOldGamepadSlot; // Previous GamepadSlot - can be -1 controller doesn't uses gamepad bindings
	int m_nNewGamepadSlot; // New Gamepad Slot - can be -1 controller doesn't uses gamepad bindings
};

#pragma pack(pop)
