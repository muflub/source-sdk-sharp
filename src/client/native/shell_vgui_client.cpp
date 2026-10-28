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
#include "iloadingdisc.h"
#include "ifpspanel.h"
#include "imessagechars.h"
#include "inetgraphpanel.h"
#include "idebugoverlaypanel.h"

#include "vgui/ISurface.h"
#include "vgui/IVGui.h"
#include "vgui/IPanel.h"
#include "vgui/IInputInternal.h"

#include "imanaged_areas_client.h"

vgui::IInputInternal *g_InputInternal = NULL;

// in_mouse.cpp's cursor reads and writes, through vgui's input (not in text mode).
void GetVGUICursorPos( int &x, int &y )
{
	g_pVGuiInput->GetCursorPos( x, y );
}

void SetVGUICursorPos( int x, int y )
{
	if ( !g_bTextMode )
		g_pVGuiInput->SetCursorPos( x, y );
}

static void DestroyGlobalPanels()
{
	netgraphpanel->Destroy();
	debugoverlaypanel->Destroy();
	fps->Destroy();
	messagechars->Destroy();
	loadingdisc->Destroy();
}

// The panels a video-mode change rebuilds: every global one but the centre print.
static void CreateModePanels()
{
	const vgui::VPANEL gameTools = enginevgui->GetPanel( PANEL_CLIENTDLL_TOOLS );
	const vgui::VPANEL tools = enginevgui->GetPanel( PANEL_TOOLS );
	loadingdisc->Create( gameTools );
	messagechars->Create( gameTools );
	fps->Create( tools );
	netgraphpanel->Create( tools );
	debugoverlaypanel->Create( gameTools );
}

static void OnVideoModeChange()
{
	DestroyGlobalPanels();
	CreateModePanels();
}

bool VGui_Startup( CreateInterfaceFn appSystemFactory )
{
	// What vgui_controls' VGui_InitInterfacesList checked: tier3's vgui interfaces.
	if ( !g_pVGui || !g_pVGuiInput || !g_pVGuiPanel || !g_pVGuiSurface || !g_pVGuiSchemeManager || !g_pVGuiSystem )
	{
		Warning( "the client's vgui is missing a required interface\n" );
		return false;
	}

	g_InputInternal = (vgui::IInputInternal *)appSystemFactory( VGUI_INPUTINTERNAL_INTERFACE_VERSION, NULL );
	if ( !g_InputInternal )
		return false;

	static bool s_bModeCallback = false;
	if ( !s_bModeCallback )
	{
		s_bModeCallback = true;
		g_pMaterialSystem->AddModeChangeCallBack( &OnVideoModeChange );
	}

	VGUI_CreateClientDLLRootPanel();
	return VGui_GetClientDLLRootPanel() != 0;
}

void VGui_CreateGlobalPanels()
{
	internalCenterPrint->Create( enginevgui->GetPanel( PANEL_CLIENTDLL_TOOLS ) );
	CreateModePanels();
}

void VGui_Shutdown()
{
	VGUI_DestroyClientDLLRootPanel();
	DestroyGlobalPanels();
	internalCenterPrint->Destroy();

	if ( g_pClientMode )
		g_pClientMode->VGui_Shutdown();

	// Let vgui free the panels marked for deletion above.
	g_pVGui->RunFrame();
}

// The engine facts the managed UI decides the loading and paused plaques from, sent when
// they change. The seconds left on a timed pause, -1 without a timer.
static int s_nPlaqueFacts = -1;
static int s_nPausedSeconds = -1;

void VGui_PreRender()
{
	const int nFacts = ( engine->IsDrawingLoadingImage() ? 1 : 0 ) | ( engine->IsPlayingDemo() ? 2 : 0 ) |
		( enginevgui->IsGameUIVisible() ? 4 : 0 ) | ( engine->IsPaused() ? 8 : 0 ) |
		( engine->IsTakingScreenshot() ? 16 : 0 );
	const float flExpire = engine->GetPausedExpireTime();
	// The expiry is on the engine's clock, which engine->Time() answers.
	const int nSeconds = flExpire != -1.f ? (int)( flExpire - engine->Time() ) : -1;
	if ( nFacts == s_nPlaqueFacts && nSeconds == s_nPausedSeconds )
		return;
	IManagedUiClient *pUi = ManagedUiClient();
	if ( !pUi )
		return;
	s_nPlaqueFacts = nFacts;
	s_nPausedSeconds = nSeconds;
	pUi->PlaqueState( nFacts, nSeconds );
}

void VGui_PostRender() {}

// The HUD's size: the client panel's, or the screen's before there is one.
void GetHudSize( int &w, int &h )
{
	g_pVGuiSurface->GetScreenSize( w, h );
	if ( const vgui::VPANEL hudParent = enginevgui->GetPanel( PANEL_CLIENTDLL ) )
		g_pVGuiPanel->GetSize( hudParent, w, h );
}
