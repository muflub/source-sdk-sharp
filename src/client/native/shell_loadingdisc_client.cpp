//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the loading and paused plaques' native half =========//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve C3. Stock's CLoadingDisc (game/client/
// vgui_loadingdiscpanel.cpp) made two vgui EditablePanels; vgui_int.cpp's
// the panels are gone, and so is the rule that showed them: the managed UI's
// LoadingDisc decides from the engine facts shell_vgui_client.cpp's
// VGui_PreRender forwards (IManagedUiClient::PlaqueState). vgui_int's panel
// creation and destruction still reach this object; it has nothing to do.
//=============================================================================//

#include "iloadingdisc.h"
#include "tier0/platform.h"
#include "vgui/VGUI.h"

class CSourceSharpLoadingDisc : public ILoadingDisc
{
public:
	virtual void Create( vgui::VPANEL parent ) OVERRIDE {}
	virtual void Destroy( void ) OVERRIDE {}

	// Nothing calls these now: shell_vgui_client.cpp's VGui_PreRender hands the
	// managed UI the engine's facts, and the plaques are decided there.
	virtual void SetLoadingVisible( bool bVisible ) OVERRIDE {}
	virtual void SetPausedVisible( bool bVisible, float flExpireTime ) OVERRIDE {}
};

static CSourceSharpLoadingDisc g_LoadingDisc;
ILoadingDisc *loadingdisc = &g_LoadingDisc;
