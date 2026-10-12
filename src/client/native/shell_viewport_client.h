//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client viewport, a raw panel ==================//
//
// plan_client_novalve C3 step 4. Stock's CBaseViewport was a vgui_controls
// EditablePanel that built every viewport panel. Every panel by name is the
// managed UI's (IManagedUiClient::ViewportPanel), so the viewport is a raw
// vgui2 panel (shell_rawpanel_client.h) that the HUD and menu roots hang from,
// that hides "all" on game_newmap as stock's did, and that asks the client mode
// to lay it out again when the client root resizes.
//=============================================================================//
#ifndef SHELL_VIEWPORT_CLIENT_H
#define SHELL_VIEWPORT_CLIENT_H

#include "shell_rawpanel_client.h"
#include "game/client/iviewport.h"
#include "igameevents.h"

class CSourceSharpViewport : public CSourceSharpRawPanel, public IViewPort, public IGameEventListener2
{
public:
	CSourceSharpViewport();
	virtual ~CSourceSharpViewport();

	void Start( IGameEventManager2 *pGameEventManager );

	// ---- the raw panel's hooks ----
	virtual void OnThink() OVERRIDE;
	virtual void OnScreenSizeChanged( int nOldWide, int nOldTall ) OVERRIDE;

	// ---- IViewPort ----
	virtual void UpdateAllPanels( void ) OVERRIDE {}
	virtual void ShowPanel( const char *pName, bool state ) OVERRIDE;
	virtual void ShowPanel( IViewPortPanel *pPanel, bool state ) OVERRIDE;
	virtual void ShowBackGround( bool bShow ) OVERRIDE {}
	virtual IViewPortPanel *FindPanelByName( const char *szPanelName ) OVERRIDE { return NULL; }
	virtual IViewPortPanel *GetActivePanel( void ) OVERRIDE { return NULL; }
	virtual void PostMessageToPanel( const char *pName, KeyValues *pKeyValues ) OVERRIDE;

	// ---- IGameEventListener2 ----
	virtual void FireGameEvent( IGameEvent *event ) OVERRIDE;

private:
	void OnRootSize( int nWide, int nTall );

	IGameEventManager2 *m_pGameEventManager;
	int m_nRootWide;
	int m_nRootTall;
};

#endif // SHELL_VIEWPORT_CLIENT_H
