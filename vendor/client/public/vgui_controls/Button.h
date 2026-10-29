//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <vgui_controls/Label.h>

namespace vgui
{

class Button : public Label
{
	DECLARE_CLASS_SIMPLE(Button, Label);

public:
	// You can optionally pass in the panel to send the click message to and the name of the command to send to that panel.
	Button(Panel* parent, const char* panelName, const char* text, Panel* pActionSignalTarget = NULL,
		   const char* pCmd = NULL);
	Button(Panel* parent, const char* panelName, const wchar_t* text, Panel* pActionSignalTarget = NULL,
		   const char* pCmd = NULL);
	~Button();

private:
	void Init();

public:
	// Set armed state.
	virtual void unused_SetArmed() = 0;
	// Check armed state
	virtual void unused_IsArmed() = 0;

	// Check depressed state
	virtual void unused_IsDepressed() = 0;
	// Set button force depressed state.
	virtual void unused_ForceDepressed() = 0;
	// Set button depressed state with respect to the force depressed state.
	virtual void unused_RecalculateDepressedState() = 0;

	// Set button selected state.
	virtual void unused_SetSelected() = 0;
	// Check selected state
	virtual void unused_IsSelected() = 0;

	virtual void unused_SetBlink() = 0;
	virtual void unused_IsBlinking() = 0;

	//Set whether or not the button captures all mouse input when depressed.
	virtual void unused_SetUseCaptureMouse() = 0;
	// Check if mouse capture is enabled.
	virtual void unused_IsUseCaptureMouseEnabled() = 0;

	// Activate a button click.
	MESSAGE_FUNC(DoClick, "PressButton");

	MESSAGE_FUNC(OnHotkey, "Hotkey") { DoClick(); }

	// Set button to be mouse clickable or not.
	virtual void unused_SetMouseClickEnabled() = 0;
	// Check if button is mouse clickable
	virtual void unused_IsMouseClickEnabled() = 0;

	// sets the how this button activates
	enum ActivationType_t
	{
		ACTIVATE_ONPRESSEDANDRELEASED, // normal button behaviour
		ACTIVATE_ONPRESSED, // menu buttons, toggle buttons
		ACTIVATE_ONRELEASED, // menu items
	};

	virtual void unused_SetButtonActivationType() = 0;

	// Message targets that the button has been pressed
	virtual void unused_FireActionSignal() = 0;
	// Perform graphical layout of button
	virtual void PerformLayout();

	virtual bool RequestInfo(KeyValues* data);

	virtual void unused_CanBeDefaultButton() = 0;

	// Set this button to be the button that is accessed by default when the user hits ENTER or SPACE
	MESSAGE_FUNC_INT(SetAsDefaultButton, "SetAsDefaultButton", state);
	// Set this button to be the button that is currently accessed by default when the user hits ENTER or SPACE
	MESSAGE_FUNC_INT(SetAsCurrentDefaultButton, "SetAsCurrentDefaultButton", state);

	// Respond when key focus is received
	virtual void OnSetFocus();
	// Respond when focus is killed
	virtual void OnKillFocus();

	// Set button border attribute enabled, controls display of button.
	virtual void unused_SetButtonBorderEnabled() = 0;

	// Set default button colors.
	virtual void unused_SetDefaultColor() = 0;
	// Set armed button colors
	virtual void unused_SetArmedColor() = 0;
	// Set selected button colors
	virtual void unused_SetSelectedColor() = 0;
	// Set depressed button colors
	virtual void unused_SetDepressedColor() = 0;
	// Set blink button color
	virtual void unused_SetBlinkColor() = 0;

	// Get button foreground color
	virtual void unused_GetButtonFgColor() = 0;
	// Get button background color
	virtual void unused_GetButtonBgColor() = 0;

	// Set default button border attributes.
	virtual void unused_SetDefaultBorder() = 0;
	// Set depressed button border attributes.
	virtual void unused_SetDepressedBorder() = 0;
	// Set key focused button border attributes.
	virtual void unused_SetKeyFocusBorder() = 0;

	// Set the command to send when the button is pressed
	// Set the panel to send the command to with AddActionSignalTarget()
	virtual void SetCommand(const char* command);
	// Set the message to send when the button is pressed
	virtual void SetCommand(KeyValues* message);

	/* CUSTOM MESSAGE HANDLING
		"PressButton"	- makes the button act as if it had just been pressed by the user (just like DoClick())
			input: none		
	*/

	virtual void OnCursorEntered();
	virtual void OnCursorExited();
	virtual void SizeToContents();

	virtual void unused_GetCommand() = 0;

	virtual void ApplySettings(KeyValues* inResourceData);
	virtual void NavigateTo();
	virtual void NavigateFrom();

	virtual void unused_IsToggleButton() = 0;

protected:
	virtual void unused_DrawFocusBorder() = 0;

	// Paint button on screen
	virtual void Paint();
	// Get button border attributes.
	virtual void unused_GetBorder() = 0;

	virtual void ApplySchemeSettings(IScheme* pScheme);
	MESSAGE_FUNC_INT(OnSetState, "SetState", state);

	virtual void OnMousePressed(MouseCode code);
	virtual void OnMouseDoublePressed(MouseCode code);
	virtual void OnMouseReleased(MouseCode code);
	virtual void OnKeyCodePressed(KeyCode code);
	virtual void OnKeyCodeReleased(KeyCode code);

	// Get control settings for editing
	virtual void GetSettings(KeyValues* outResourceData);
	virtual const char* GetDescription();

protected:
	enum ButtonFlags_t
	{
		ARMED = 0x0001,
		DEPRESSED = 0x0002,
		FORCE_DEPRESSED = 0x0004,
		BUTTON_BORDER_ENABLED = 0x0008,
		USE_CAPTURE_MOUSE = 0x0010,
		BUTTON_KEY_DOWN = 0x0020,
		DEFAULT_BUTTON = 0x0040,
		SELECTED = 0x0080,
		DRAW_FOCUS_BOX = 0x0100,
		BLINK = 0x0200,
		ALL_FLAGS = 0xFFFF,
	};

	CUtlFlags<unsigned short> _buttonFlags; // see ButtonFlags_t
	int _mouseClickMask;
	KeyValues* _actionMessage;
	ActivationType_t _activationType;

	IBorder* _defaultBorder;
	IBorder* _depressedBorder;
	IBorder* _keyFocusBorder;

	Color _defaultFgColor, _defaultBgColor;
	Color _armedFgColor, _armedBgColor;
	Color _selectedFgColor, _selectedBgColor;
	Color _depressedFgColor, _depressedBgColor;
	Color _keyboardFocusColor;
	Color _blinkFgColor;

	bool _paint;

	unsigned short m_sArmedSoundName, m_sDepressedSoundName, m_sReleasedSoundName;
	bool m_bSelectionStateSaved;
	bool m_bStaySelectedOnClick;
	bool m_bStayArmedOnClick;
};

} // namespace vgui
