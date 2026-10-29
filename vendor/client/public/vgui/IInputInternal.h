//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <vgui/IInput.h>

namespace vgui
{

typedef int HInputContext;

#define DEFAULT_INPUT_CONTEXT ((vgui::HInputContext)~0)

class IInputInternal : public IInput
{
public:
	// processes input for a frame
	virtual void RunFrame() = 0;

	virtual void unused_UpdateMouseFocus() = 0;

	// called when a panel becomes invalid
	virtual void unused_PanelDeleted() = 0;

	// inputs into vgui input handling
	virtual void unused_InternalCursorMoved() = 0; //expects input in surface space
	virtual void unused_InternalMousePressed() = 0;
	virtual void unused_InternalMouseDoublePressed() = 0;
	virtual void unused_InternalMouseReleased() = 0;
	virtual void unused_InternalMouseWheeled() = 0;
	virtual void unused_InternalKeyCodePressed() = 0;
	virtual void unused_InternalKeyCodeTyped() = 0;
	virtual void unused_InternalKeyTyped() = 0;
	virtual void unused_InternalKeyCodeReleased() = 0;

	// Creates/ destroys "input" contexts, which contains information
	// about which controls have mouse + key focus, for example.
	virtual void unused_CreateInputContext() = 0;
	virtual void unused_DestroyInputContext() = 0;

	// Associates a particular panel with an input context
	// Associating NULL is valid; it disconnects the panel from the context
	virtual void unused_AssociatePanelWithInputContext() = 0;

	// Activates a particular input context, use DEFAULT_INPUT_CONTEXT
	// to get the one normally used by VGUI
	virtual void unused_ActivateInputContext() = 0;

	// This method is called to post a cursor message to the current input context
	virtual void unused_PostCursorMessage() = 0;

	// Cursor position; this is the current position read from the input queue.
	// We need to set it because client code may read this during Mouse Pressed
	// events, etc.
	virtual void unused_UpdateCursorPosInternal() = 0;

	// Called to handle explicit calls to CursorSetPos after input processing is complete
	virtual void unused_HandleExplicitSetCursor() = 0;

	// Updates the internal key/mouse state associated with the current input context without sending messages
	virtual void unused_SetKeyCodeState() = 0;
	virtual void unused_SetMouseCodeState() = 0;
	virtual void unused_UpdateButtonState() = 0;
};

} // namespace vgui

#define VGUI_INPUTINTERNAL_INTERFACE_VERSION "VGUI_InputInternal001"
