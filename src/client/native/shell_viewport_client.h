//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The client viewport: a raw vgui2 panel the HUD and menu roots hang from. It hides "all"
// on game_newmap and re-lays out the client mode when the root resizes.
#pragma once

#include "shell_rawpanel_client.h"
#include "game/client/iviewport.h"
#include "igameevents.h"

class CSourceSharpViewport : public CSourceSharpRawPanel, public IViewPort, public IGameEventListener2
{
public:
	CSourceSharpViewport();
	virtual ~CSourceSharpViewport();

	void Start(IGameEventManager2* pGameEventManager);

	virtual void OnThink() OVERRIDE;
	virtual void OnScreenSizeChanged(int nOldWide, int nOldTall) OVERRIDE;

	virtual void UpdateAllPanels() OVERRIDE {}

	virtual void ShowPanel(const char* pName, bool state) OVERRIDE;
	virtual void ShowPanel(IViewPortPanel* pPanel, bool state) OVERRIDE;

	virtual void ShowBackGround(bool bShow) OVERRIDE {}

	virtual IViewPortPanel* FindPanelByName(const char* szPanelName) OVERRIDE { return NULL; }

	virtual IViewPortPanel* GetActivePanel() OVERRIDE { return NULL; }

	virtual void PostMessageToPanel(const char* pName, KeyValues* pKeyValues) OVERRIDE;

	virtual void FireGameEvent(IGameEvent* event) OVERRIDE;

private:
	void OnRootSize(int nWide, int nTall);

	IGameEventManager2* m_pGameEventManager;
	int m_nRootWide;
	int m_nRootTall;
};
