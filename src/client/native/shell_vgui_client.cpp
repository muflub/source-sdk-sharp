//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The client's vgui start, shutdown and global panels, without vgui_controls.
// SS_REASON b  an interface resolved by name at load

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "vgui_int.h"
#include "ienginevgui.h"
#include "iclientmode.h"
#include "vguicenterprint.h"

#include "vgui/ISurface.h"
#include "vgui/IVGui.h"
#include "vgui/IPanel.h"
#include "vgui/IInputInternal.h"

#include "imanaged_areas_client.h"
#include "shell_hud_ui_client.h"
#include "shell_client_engine_interfaces.h"
#include "shell_vgui_object.h"
#include "shell_rawpanel_client.h"

// in_mouse.cpp's cursor reads and writes, through vgui's input (not in text mode).
void GetVGUICursorPos(const CClientEngineInterfaces& engines, int& x, int& y)
{
	engines.m_pVGuiInput->GetCursorPos(x, y);
}

void SetVGUICursorPos(const CClientEngineInterfaces& engines, int x, int y)
{
	if (!engines.m_bTextMode)
		engines.m_pVGuiInput->SetCursorPos(x, y);
}

CVguiClient::CVguiClient(const CClientEngineInterfaces& engines)
	: m_Engines(engines)
{
}

bool CVguiClient::Startup(CreateInterfaceFn appSystemFactory)
{
	// What vgui_controls' VGui_InitInterfacesList checked: tier3's vgui interfaces.
	if (!m_Engines.m_pVGuiCore || !m_Engines.m_pVGuiInput || !m_Engines.m_pVGuiPanel || !m_Engines.m_pVGuiSurface ||
		!m_Engines.m_pVGuiSchemes || !m_Engines.m_pVGuiSystem)
	{
		Warning("the client's vgui is missing a required interface\n");
		return false;
	}

	if (!appSystemFactory(VGUI_INPUTINTERNAL_INTERFACE_VERSION, NULL))
		return false;

	// VGUI clips a child to its parent, so the root must fill the screen, from creation and
	// again whenever the screen size changes.
	m_pRootPanel = new CSourceSharpRawPanel(m_Engines, "SourceSharp Root Panel");
	m_pRootPanel->SetParent(m_Engines.m_pVGui->GetPanel(PANEL_CLIENTDLL));
	m_pRootPanel->SetPaintEnabled(false);
	m_pRootPanel->SetCursor(vgui::dc_none);
	m_pRootPanel->SetZPos(0);
	m_pRootPanel->SetVisible(true);
	m_pRootPanel->SetKeyBoardInputEnabled(false);
	m_pRootPanel->SetMouseInputEnabled(false);

	m_pRootPanel->FillScreen();
	int nScreenW, nScreenH;
	m_pRootPanel->GetSize(nScreenW, nScreenH);

	Msg("[SourceSharp] VGUI root panel sized %dx%d\n", nScreenW, nScreenH);
	return RootPanel() != 0;
}

vgui::VPANEL CVguiClient::RootPanel() const
{
	return m_pRootPanel ? m_pRootPanel->GetVPanel() : 0;
}

void CVguiClient::Shutdown()
{
	delete m_pRootPanel;
	m_pRootPanel = nullptr;

	if (m_Engines.m_pClientMode)
		m_Engines.m_pClientMode->VGui_Shutdown();

	// Let vgui free the panels marked for deletion above.
	m_Engines.m_pVGuiCore->RunFrame();
}

// The engine facts the managed UI decides the loading and paused plaques from, sent when
// they change. The seconds left on a timed pause, -1 without a timer.
void CHudUiClient::PlaquePreRender()
{
	const int nFacts = (m_Engines.m_pEngine->IsDrawingLoadingImage() ? 1 : 0) |
		(m_Engines.m_pEngine->IsPlayingDemo() ? 2 : 0) | (m_Engines.m_pVGui->IsGameUIVisible() ? 4 : 0) |
		(m_Engines.m_pEngine->IsPaused() ? 8 : 0) | (m_Engines.m_pEngine->IsTakingScreenshot() ? 16 : 0);
	const float flExpire = m_Engines.m_pEngine->GetPausedExpireTime();
	// The expiry is on the engine's clock, which engine->Time() answers.
	const int nSeconds = flExpire != -1.f ? (int)(flExpire - m_Engines.m_pEngine->Time()) : -1;
	if (nFacts == m_nPlaqueFacts && nSeconds == m_nPausedSeconds)
		return;
	IManagedUiClient* pUi = Managed();
	if (!pUi)
		return;
	m_nPlaqueFacts = nFacts;
	m_nPausedSeconds = nSeconds;
	pUi->PlaqueState(nFacts, nSeconds);
}

void VGui_PostRender() {}

// The HUD's size: the client panel's, or the screen's before there is one.
void GetHudSize(const CClientEngineInterfaces& engines, int& w, int& h)
{
	engines.m_pVGuiSurface->GetScreenSize(w, h);
	if (const vgui::VPANEL hudParent = engines.m_pVGui->GetPanel(PANEL_CLIENTDLL))
		engines.m_pVGuiPanel->GetSize(hudParent, w, h);
}
