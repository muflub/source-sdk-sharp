//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client's vgui start, shutdown and panels ======//
// SS_REASON b  an interface resolved by name at load
//
// plan_client_novalve C3 step 4. Stock's vgui_int.cpp started vgui for the
// client through vgui_controls (VGui_InitInterfacesList, a property converter
// for HUD textures, the panel-animation dump) and made the global panels.
// No panel here is a vgui_controls panel now, so this asks for what the
// client uses directly: tier3 connected the vgui interfaces before this runs,
// the input-internal interface comes from the factory (stock's comment: the
// vgui screen code needs it), and the root panel and the global panels are
// made and destroyed as stock's were, also around a video-mode change. The
// loading and paused plaques are decided by the managed UI from the engine
// facts VGui_PreRender forwards.
//=============================================================================//

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
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
#include "materialsystem/imaterialsystem.h"
#include "tier3/tier3.h"
#include "vgui/ISurface.h"
#include "vgui/IVGui.h"
#include "vgui/IPanel.h"
#include "vgui/IInputInternal.h"
#include "vgui/IInput.h"
#include "tier0/dbg.h"
#include "tier1/interface.h"
#include "tier2/tier2.h"
#include "vgui/VGUI.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "imanaged_areas_client.h"

// memdbgon must be the last include file in a .cpp file!!!

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

void VGui_CreateGlobalPanels( void )
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

// The engine facts the loading and paused plaques are decided from, to the
// managed UI (IManagedUiClient::PlaqueState) when they change; the rule is
// SourceSharp.Client's LoadingDisc (stock's VGui_PreRender, which viewrender.cpp
// calls each frame). The seconds left on a timed pause, -1 without a timer.
static int s_nPlaqueFacts = -1;
static int s_nPausedSeconds = -1;

void VGui_PreRender()
{
	const int nFacts = ( engine->IsDrawingLoadingImage() ? 1 : 0 ) | ( engine->IsPlayingDemo() ? 2 : 0 )
		| ( enginevgui->IsGameUIVisible() ? 4 : 0 ) | ( engine->IsPaused() ? 8 : 0 )
		| ( engine->IsTakingScreenshot() ? 16 : 0 );
	const float flExpire = engine->GetPausedExpireTime();
	// The expiry is on the engine's clock (tier0's Plat_FloatTime, which
	// engine->Time() answers), so it is compared with that clock.
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

void VGui_PostRender()
{
}

// The HUD's size: the client panel's, or the screen's before there is one.
void GetHudSize( int &w, int &h )
{
	g_pVGuiSurface->GetScreenSize( w, h );
	if ( const vgui::VPANEL hudParent = enginevgui->GetPanel( PANEL_CLIENTDLL ) )
		g_pVGuiPanel->GetSize( hudParent, w, h );
}
