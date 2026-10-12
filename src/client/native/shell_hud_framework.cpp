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
//   - icons that resolve to nothing, because no element asks for one any more
//   - render groups that always succeed, because nothing competes for one
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

void CHudElement::SetParentedToClientDLLRootPanel( bool parented )
{
	m_bIsParentedToClientDLLRootPanel = parented;
}

// Render groups exist so elements can hide each other. Nothing competes for
// screen space here — there is one element — so these are all no-ops.
void CHudElement::RegisterForRenderGroup( const char * ) {}
void CHudElement::UnregisterForRenderGroup( const char * ) {}
void CHudElement::HideLowerPriorityHudElementsInGroup( const char * ) {}
void CHudElement::UnhideLowerPriorityHudElementsInGroup( const char * ) {}

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

	InitColors( NULL );

	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->Init();
}

//-----------------------------------------------------------------------------
// The three colours the rest of the client still reads.
//
// Hardcoded rather than read from ClientScheme.res: the scheme entries exist to
// let a mod restyle the stock HUD, and there is no stock HUD left to restyle.
//-----------------------------------------------------------------------------
void CHud::InitColors( vgui::IScheme * )
{
	m_clrNormal   = Color( 255, 208, 64,  255 );
	m_clrCaution  = Color( 255, 48,  0,   255 );
	m_clrYellowish = Color( 255, 160, 0,  255 );
}

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

void CHud::OnRestore( void )
{
	ResetHUD();
}

void CHud::Think( void )
{
	ProcessInput( true );
}

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

//-----------------------------------------------------------------------------
// Looks an element up by name.
//
// Deliberately silent when it finds nothing. The original asserted, which is
// correct for a mod that has the elements it asks for and wrong for one that
// does not: a lookup for a HUD this mod chose to remove is an answer, not a
// failure, and asserting on it stops a debug build dead.
//-----------------------------------------------------------------------------
CHudElement *CHud::FindElement( const char *pName )
{
	for ( int i = 0; i < m_HudList.Count(); i++ )
	{
		if ( V_stricmp( m_HudList[i]->GetName(), pName ) == 0 )
			return m_HudList[i];
	}

	return NULL;
}

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

//-----------------------------------------------------------------------------
// Icons.
//
// The stock icons came from a sprite sheet the removed elements drew from, so
// there is nothing left to look up and nothing left that looks. Returning NULL
// is the honest answer; every caller already handles it, because a missing icon
// was always possible.
//-----------------------------------------------------------------------------
CHudTexture *CHud::GetIcon( const char * )
{
	return NULL;
}

CHudTexture *CHud::AddUnsearchableHudIconToList( CHudTexture & )
{
	return NULL;
}

CHudTexture *CHud::AddSearchableHudIconToList( CHudTexture & )
{
	return NULL;
}

void CHud::RefreshHudTextures( void )
{
}

//-----------------------------------------------------------------------------
// Render groups: which elements hide which others.
//
// With one element there is nothing to arbitrate, so a group is never locked
// and every lock succeeds.
//-----------------------------------------------------------------------------
int CHud::LookupRenderGroupIndexByName( const char * )
{
	return -1;
}

int CHud::RegisterForRenderGroup( const char * )
{
	return -1;
}

int CHud::AddHudRenderGroup( const char * )
{
	return -1;
}

bool CHud::DoesRenderGroupExist( int )
{
	return false;
}

bool CHud::LockRenderGroup( int, CHudElement * )
{
	return true;
}

bool CHud::UnlockRenderGroup( int, CHudElement * )
{
	return true;
}

bool CHud::IsRenderGroupLockedFor( CHudElement *, int )
{
	return false;
}

//-----------------------------------------------------------------------------
// Drawing helpers the stock elements used. Nothing draws through them now.
//-----------------------------------------------------------------------------
void CHud::DrawProgressBar( int, int, int, int, float, Color &, unsigned char )
{
}

void CHud::DrawIconProgressBar( int, int, CHudTexture *, CHudTexture *, float, Color &, int )
{
}

//-----------------------------------------------------------------------------
// CHudTexture.
//
// Icons never resolve, so an icon can never be drawn — but the type is still
// named by code that holds one, so its methods have to exist.
//-----------------------------------------------------------------------------
void CHudTexture::Precache( void )
{
}

void CHudTexture::DrawSelf( int, int, const Color & ) const
{
}

void CHudTexture::DrawSelf( int, int, int, int, const Color & ) const
{
}

void LoadHudTextures( CUtlDict< CHudTexture *, int > &, const char *, const unsigned char * )
{
}

void FreeHudTextureList( CUtlDict< CHudTexture *, int > &list )
{
	list.PurgeAndDeleteElements();
}

//-----------------------------------------------------------------------------
// Closed captions and the prediction dump panel.
//
// Both were HUD elements, so both left the build with the rest. Their entry
// points survive because scene playback, commentary nodes and the prediction
// code still name them — but every one of those call sites already asks
// GET_HUDELEMENT for the panel first and gets NULL, so none of these bodies can
// actually run. They exist to satisfy the linker, not to do anything.
//
// Defined here rather than by guarding six call sites across four SDK files:
// fewer edits to code this mod otherwise leaves alone.
//-----------------------------------------------------------------------------
#include "hud_closecaption.h"
#include "hud_pdump.h"

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
//     connected client through GetClientConVarValue. sound_emitter.cpp reaches
//     it with cvar->FindVar("closecaption") and two shim files hold
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

bool CHudCloseCaption::ProcessCaption( char const *, float, bool, bool )
{
	return false;
}

void CHudCloseCaption::ProcessCaptionDirect( char const *, float, bool )
{
}

void CHudCloseCaption::ProcessSentenceCaptionStream( char const * )
{
}

void CHudCloseCaption::Lock( void )
{
}

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
