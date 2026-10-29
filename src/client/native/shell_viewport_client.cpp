//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The client viewport, a raw panel.
// SS_REASON a  a vtable the engine calls through

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "iclientmode.h"
#include "ienginevgui.h"

#include "vgui_controls/Controls.h"

#include "shell_viewport_client.h"
#include "imanaged_areas_client.h"

IViewPort* gViewPortInterface = NULL;

CSourceSharpViewport::CSourceSharpViewport()
	: CSourceSharpRawPanel("SourceSharpViewport")
	, m_pGameEventManager(NULL)
	, m_nRootWide(-1)
	, m_nRootTall(-1)
{
	gViewPortInterface = this;
	SetPaintEnabled(false);
	SetKeyBoardInputEnabled(false);
	SetMouseInputEnabled(false);
	// Fill the screen until the client mode lays it out; the HUD and menu roots under it
	// are clipped to it.
	FillScreen();
}

CSourceSharpViewport::~CSourceSharpViewport()
{
	if (gViewPortInterface == this)
		gViewPortInterface = NULL;
	if (m_pGameEventManager)
		m_pGameEventManager->RemoveListener(this);
}

void CSourceSharpViewport::Start(IGameEventManager2* pGameEventManager)
{
	m_pGameEventManager = pGameEventManager;
	if (m_pGameEventManager)
		m_pGameEventManager->AddListener(this, "game_newmap", false);
}

void CSourceSharpViewport::OnThink()
{
	int nWide = 0, nTall = 0;
	vgui::ipanel()->GetSize(enginevgui->GetPanel(PANEL_CLIENTDLL), nWide, nTall);
	OnRootSize(nWide, nTall);
}

void CSourceSharpViewport::OnScreenSizeChanged(int nOldWide, int nOldTall)
{
	if (g_pClientMode)
		g_pClientMode->Layout();
}

void CSourceSharpViewport::OnRootSize(int nWide, int nTall)
{
	const bool bResized = nWide != m_nRootWide || nTall != m_nRootTall;
	m_nRootWide = nWide;
	m_nRootTall = nTall;
	if (bResized && g_pClientMode)
		g_pClientMode->Layout();
}

// By name: the managed UI opens or closes its screen for it.
void CSourceSharpViewport::ShowPanel(const char* pName, bool state)
{
	if (IManagedUiClient* pUi = ManagedUiClient())
	{
		if (pName)
			pUi->ViewportPanel(pName, state ? 1 : 0);
	}
}

// A panel object, which only its own code holds: shown as it asks.
void CSourceSharpViewport::ShowPanel(IViewPortPanel* pPanel, bool state)
{
	if (pPanel)
		pPanel->ShowPanel(state);
}

// No native panel by name, so the message is only freed.
void CSourceSharpViewport::PostMessageToPanel(const char* pName, KeyValues* pKeyValues)
{
	if (pKeyValues)
		pKeyValues->deleteThis();
}

void CSourceSharpViewport::FireGameEvent(IGameEvent* event)
{
	if (!V_strcmp(event->GetName(), "game_newmap"))
		ShowPanel(PANEL_ALL, false);
}
