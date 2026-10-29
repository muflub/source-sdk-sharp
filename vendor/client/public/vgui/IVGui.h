//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "tier1/interface.h"
#include <vgui/VGUI.h>

#include "appframework/IAppSystem.h"

class KeyValues;
class IVEngineClient;

namespace vgui
{

// safe handle to a panel - can be converted to and from a VPANEL
typedef uint32 HPanel;
typedef uint32 HContext;

enum : vgui::HContext
{
	DEFAULT_VGUI_CONTEXT = ( (vgui::HContext)~0 )
};

// Purpose: Interface to core vgui components
class IVGui : public IAppSystem
{
public:
	// activates vgui message pump
	virtual void Start() = 0;

	// signals vgui to Stop running
	virtual void Stop() = 0;

	// returns true if vgui is current active
	virtual void unused_IsRunning() = 0;

	// runs a single frame of vgui
	virtual void RunFrame() = 0;

	// broadcasts "ShutdownRequest" "id" message to all top-level panels in the app
	virtual void unused_ShutdownMessage() = 0;

	// panel allocation
	virtual VPANEL AllocPanel() = 0;
	virtual void FreePanel( VPANEL panel ) = 0;

	// debugging prints
	virtual void unused_DPrintf() = 0;
	virtual void unused_DPrintf2() = 0;
	virtual void unused_SpewAllActivePanelNames() = 0;

	// safe-pointer handle methods
	virtual void unused_PanelToHandle() = 0;
	virtual void unused_HandleToPanel() = 0;
	virtual void unused_MarkPanelForDeletion() = 0;

	// makes panel receive a 'Tick' message every frame (~50ms, depending on sleep times/framerate)
	// panel is automatically removed from tick signal list when it's deleted
	virtual void AddTickSignal( VPANEL panel, int intervalMilliseconds = 0 ) = 0;
	virtual void RemoveTickSignal( VPANEL panel ) = 0;

	// message sending
	virtual void PostMessage( VPANEL target, KeyValues *params, VPANEL from, float delaySeconds = 0.0f ) = 0;

	// Creates/ destroys vgui contexts, which contains information
	// about which controls have mouse + key focus, for example.
	virtual void unused_CreateContext() = 0;
	virtual void unused_DestroyContext() = 0;

	// Associates a particular panel with a vgui context
	// Associating NULL is valid; it disconnects the panel from the context
	virtual void unused_AssociatePanelWithContext() = 0;

	// Activates a particular context, use DEFAULT_VGUI_CONTEXT
	// to get the one normally used by VGUI
	virtual void unused_ActivateContext() = 0;

	// whether to sleep each frame or not, true = sleep
	virtual void unused_SetSleep() = 0;

	// data accessor for above
	virtual void unused_GetShouldVGuiControlSleep() = 0;

	// enables VR mode
	virtual void unused_SetVRMode() = 0;
	virtual void unused_GetVRMode() = 0;

	// add a tick signal like above, but to the head of the list of tick signals
	virtual void unused_AddTickSignalToHead() = 0;

	virtual void unused_GetVGUIEngine() = 0;

	virtual void InvalidateMdlCache() = 0;
	virtual void unused_GetMdlCacheSerial() = 0;
};

#define VGUI_IVGUI_INTERFACE_VERSION "VGUI_ivgui008"

};
