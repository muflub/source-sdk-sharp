//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the HUD framework, reduced to a shim ================//
// SS_REASON a  a vtable the engine calls through
//
// Everything the stock HUD DREW is gone from this mod's build: forty-five files
// covering health, ammo, the crosshair, chat, damage indicators, weapon
// selection and the rest. This mod draws its HUD from C#.
//
// What could not simply be deleted is the framework those elements hung from.
// CHud is not a HUD — it is the object the engine and a dozen client files call
// into for lifecycle, icons, mouse sensitivity and render-group locking, and
// CHudElement is the base this mod's own C# HUD host derives from so that
// DECLARE_HUDELEMENT still creates it. Removing hud.cpp without a replacement
// leaves 43 undefined symbols, none of which are drawing.
//
// So this file replaces hud.cpp, hud_element_helper.cpp, hud_redraw.cpp and
// hud_msg.cpp with the smallest thing that satisfies those callers:
//
//   - the element list, so this mod's C# HUD is created and gets its lifecycle
//   - lifecycle forwarded to the elements and nothing else
//
// The behaviour is in C#. What is left here is the shape the SDK expects.
//
//=============================================================================//

#include "cbase.h"
#include "hud.h"
#include "hudelement.h"
#include "hud_element_helper.h"
#include "iclientmode.h"
#include <vgui_controls/Panel.h>

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

CHud gHUD;

// NO g_hFontTrebuchet24. It was supplied here as INVALID_FONT for "remaining
// client code that draws text", and there is none: every stock TU that reads it
// (message.cpp, vgui_textmessagepanel.cpp, the two hud_target_id.cpp) is off
// this link line, and the symbol had ZERO importers on both sides. Lane
// 4c-render deleted it. The managed HUD asks the surface for its own font
// (shell_hud.cpp's g_hHudFont); a global handle that is permanently invalid is
// a scheme lookup that silently draws nothing.

//-----------------------------------------------------------------------------
// CHudElementHelper — the DECLARE_HUDELEMENT registration list.
//
// Unchanged in spirit from the original: a static list built at load time, which
// CHud::Init walks to construct every declared element. This mod declares one.
//-----------------------------------------------------------------------------
CHudElementHelper *CHudElementHelper::m_sHelpers = NULL;

CHudElementHelper::CHudElementHelper( CHudElement *( *pfnCreate )( void ), int depth )
{
	m_pfnCreate = pfnCreate;
	m_iDepth = depth;

	// Ordered by depth, highest first, because the original did: element
	// creation order decides VGUI z-order among siblings.
	CHudElementHelper **ppList = &m_sHelpers;
	while ( *ppList && (*ppList)->m_iDepth > depth )
		ppList = &(*ppList)->m_pNext;

	m_pNext = *ppList;
	*ppList = this;
}

CHudElementHelper *CHudElementHelper::GetNext( void )
{
	return m_pNext;
}

void CHudElementHelper::CreateAllElements( void )
{
	for ( CHudElementHelper *p = m_sHelpers; p; p = p->GetNext() )
	{
		if ( p->m_pfnCreate )
			p->m_pfnCreate();
	}
}

//-----------------------------------------------------------------------------
// CHudElement.
//
// Registers itself with gHUD on construction and removes itself on destruction,
// which is the whole of what the base did that anything still depends on.
//-----------------------------------------------------------------------------
CHudElement::CHudElement( const char *pElementName )
{
	m_pElementName = pElementName;
	m_bActive = false;
	m_iHiddenBits = 0;
	m_bNeedsRemove = false;
	m_bIsParentedToClientDLLRootPanel = false;

	gHUD.AddHudElement( this );
}

CHudElement::~CHudElement()
{
	if ( m_bNeedsRemove )
		gHUD.RemoveHudElement( this );
}

void CHudElement::SetActive( bool bActive )
{
	m_bActive = bActive;
}

void CHudElement::SetHiddenBits( int iBits )
{
	m_iHiddenBits = iBits;
}

//-----------------------------------------------------------------------------
// Whether this element should draw at all.
//
// The hidden bits are the part worth keeping: the game hides the HUD wholesale
// during intermissions and scoreboards, and an element that ignored that would
// draw over them.
//-----------------------------------------------------------------------------
bool CHudElement::ShouldDraw( void )
{
	return !gHUD.IsHidden( m_iHiddenBits );
}

int CHudElement::GetRenderGroupPriority( void )
{
	return 0;
}

void CHudElement::SetNeedsRemove( bool needsremove )
{
	m_bNeedsRemove = needsremove;
}

// SetParentedToClientDLLRootPanel and the four render-group members stood
// here: no object on client.so's link edge imported them (nm -u, lane M1-B).

//-----------------------------------------------------------------------------
// CHud.
//-----------------------------------------------------------------------------
CHud::CHud()
{
	m_iKeyBits = 0;
	m_flMouseSensitivity = 0.0f;
	m_flMouseSensitivityFactor = 0.0f;
	m_flFOVSensitivityAdjust = 1.0f;
	m_bHudTexturesLoaded = false;
}

CHud::~CHud()
{
}

void CHud::Init( void )
{
	// Builds every declared element, which is how this mod's C# HUD host comes
	// into existence.
	CHudElementHelper::CreateAllElements();

	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->Init();
}

// InitColors stood here: the three colours it set had no reader once the last
// fork TU left client.so (lane M1-B).

void CHud::VidInit( void )
{
	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->VidInit();

	ResetHUD();
}

void CHud::Shutdown( void )
{
	// Elements own their own VGUI panels and are freed with them; the list only
	// holds borrowed pointers.
	m_HudList.RemoveAll();
}

void CHud::LevelInit( void )
{
	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->LevelInit();
}

void CHud::LevelShutdown( void )
{
	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->LevelShutdown();
}

void CHud::ResetHUD( void )
{
	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->Reset();
}

// OnRestore and Think stood here, with no importer (lane M1-B).

void CHud::ProcessInput( bool bActive )
{
	if ( !bActive )
		return;

	for ( int i = 0; i < m_HudList.Count(); i++ )
	{
		if ( m_HudList[i]->IsActive() )
			m_HudList[i]->ProcessInput();
	}
}

void CHud::UpdateHud( bool bActive )
{
	ProcessInput( bActive );
}

void CHud::AddHudElement( CHudElement *pHudElement )
{
	if ( !pHudElement )
		return;

	m_HudList.AddToTail( pHudElement );
	pHudElement->SetNeedsRemove( true );
}

void CHud::RemoveHudElement( CHudElement *pHudElement )
{
	m_HudList.FindAndRemove( pHudElement );
}

// FindElement stood here. Its callers were GET_HUDELEMENT lookups for elements
// this client never creates (CHudCrosshair, CHudCloseCaption), which answered
// NULL; the engine slots that made them say so directly now (lane M1-B).

bool CHud::IsHidden( int iHudFlags )
{
	// 13c: the hide flags lived on C_BasePlayer's local data, and the local
	// player has not been one since the flip — so this has answered "hidden"
	// for every flagged element all along (the managed HUD paints its own
	// panel and never asks). Kept as the honest constant it already was;
	// per-element hiding returns as a field on the standalone player's
	// declared state if a rule ever wants it.
	return true;
}

float CHud::GetSensitivity( void )
{
	return m_flMouseSensitivity;
}

float CHud::GetFOVSensitivityAdjust( void )
{
	return m_flFOVSensitivityAdjust;
}

// THE ICONS, THE RENDER GROUPS, THE PROGRESS BARS AND CHudTexture'S BODIES
// stood here, every one answering "none" (NULL, -1, false, no draw) for the
// stock elements that drew through them. No object on client.so's link edge
// imported any of them, nor LoadHudTextures or FreeHudTextureList (nm -u,
// lane M1-B).

//-----------------------------------------------------------------------------
// Closed captions.
//
// The caption element left the build with the rest, and its four stub bodies
// went once their last caller did (IBaseClientDLL's two caption slots, which
// asked GET_HUDELEMENT for it and got NULL; lane M1-B).
//-----------------------------------------------------------------------------

// Off, and honestly so: there is no caption element left to display anything.
// Declared here because the elements that owned them are gone and half a dozen
// files still read them.
//
// ONE OF THE TWO STAYS NATIVE, and which one is measured rather than argued.
// plan_shim.md Phase 5 (lane 5d) separated them:
//
//   * `closecaption` STAYS. The shipped engine.so resolves it BY NAME -- it sits
//     in the engine's own client-convar query block beside cl_predict and
//     cl_lagcompensation -- and shell_recipientfilter.cpp:631 reads it off a
//     connected client through GetClientConVarValue. The managed client
//     emitter reads it by name and two shim files hold
//     `extern ConVar closecaption`, so the declaration is load-bearing for the
//     link as well. That is §6's one permitted reason to declare a ConVar in
//     C++: the engine itself resolves the name.
//
//   * `english` WENT, to SourceSharp.Client/MoveVars.cs. It appears NOWHERE in
//     engine.so, and no translation unit on either link line reads it -- its two
//     apparent hits are a comment at teammenu.cpp:203 and an unrelated
//     caption-token table at public/sentence.cpp:359. FCVAR_USERINFO is
//     transmitted by FLAG and not by name, so the managed declaration is sent to
//     the server exactly as this one was. This OVERTURNS plan_shim.md §7, which
//     lists english among the protocol keeps.
//
// The §6 row that covers closecaption is the close-caption and HUD-element
// registry row.
ConVar closecaption( "closecaption", "0", FCVAR_ARCHIVE | FCVAR_USERINFO, "Enable close captioning." );

// THE PREDICTION DUMP PANEL IS NOT HERE, AND IT WAS. CPDumpPanel::Clear,
// ::DumpEntity and GetPDumpPanel() were three empty/NULL bodies supplied so
// that game/client/prediction.cpp would link. That file is not on this link
// line -- `grep -c '/prediction\.o$'` over the client.so link line reads 0 --
// and neither is hud_pdump.cpp, which is where they came from. All three had
// ZERO importers on both link lines; lane 4c-render deleted them.
//
// If the stock predictor ever comes back, it wants hud_pdump.cpp itself rather
// than three stubs: the panel's job is to show what prediction changed per
// field, and a NULL panel makes `cl_pdump` silently do nothing.
