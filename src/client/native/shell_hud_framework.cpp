//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The HUD framework the engine and client code call into (CHud, CHudElement and the
// DECLARE_HUDELEMENT list). The HUD itself is drawn by managed code.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
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
#include "hud.h"
#include "hudelement.h"
#include "hud_element_helper.h"
#include "tier1/convar.h"
#include "tier1/iconvar.h"
#include "tier1/utlvector.h"
#include "client_entity.h"

CHud gHUD;

// The DECLARE_HUDELEMENT registration list, built at load and walked by CHud::Init.
CHudElementHelper *CHudElementHelper::m_sHelpers = NULL;

CHudElementHelper::CHudElementHelper( CHudElement *( *pfnCreate )(), int depth )
{
	m_pfnCreate = pfnCreate;
	m_iDepth = depth;

	// Ordered by depth, highest first: creation order decides VGUI z-order among siblings.
	CHudElementHelper **ppList = &m_sHelpers;
	while ( *ppList && (*ppList)->m_iDepth > depth )
		ppList = &(*ppList)->m_pNext;

	m_pNext = *ppList;
	*ppList = this;
}

CHudElementHelper *CHudElementHelper::GetNext()
{
	return m_pNext;
}

void CHudElementHelper::CreateAllElements()
{
	for ( CHudElementHelper *p = m_sHelpers; p; p = p->GetNext() )
	{
		if ( p->m_pfnCreate )
			p->m_pfnCreate();
	}
}

// Registers itself with gHUD on construction and removes itself on destruction.
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

// The game hides the HUD wholesale during intermissions and scoreboards.
bool CHudElement::ShouldDraw()
{
	return !gHUD.IsHidden( m_iHiddenBits );
}

int CHudElement::GetRenderGroupPriority()
{
	return 0;
}

void CHudElement::SetNeedsRemove( bool needsremove )
{
	m_bNeedsRemove = needsremove;
}

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

void CHud::Init()
{
	// Builds every declared element, which creates the managed HUD host.
	CHudElementHelper::CreateAllElements();

	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->Init();
}

void CHud::VidInit()
{
	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->VidInit();

	ResetHUD();
}

void CHud::Shutdown()
{
	// Elements own their VGUI panels; the list only borrows them.
	m_HudList.RemoveAll();
}

void CHud::LevelInit()
{
	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->LevelInit();
}

void CHud::LevelShutdown()
{
	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->LevelShutdown();
}

void CHud::ResetHUD()
{
	for ( int i = 0; i < m_HudList.Count(); i++ )
		m_HudList[i]->Reset();
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

bool CHud::IsHidden( int iHudFlags )
{
	// Always hidden: the local player carries no hide flags and the managed HUD never asks.
	return true;
}

float CHud::GetSensitivity()
{
	return m_flMouseSensitivity;
}

float CHud::GetFOVSensitivityAdjust()
{
	return m_flFOVSensitivityAdjust;
}

// The engine resolves closecaption by name in its client-convar query, and the server
// reads it from a connected client.
ConVar closecaption( "closecaption", "0", FCVAR_ARCHIVE | FCVAR_USERINFO, "Enable close captioning." );
