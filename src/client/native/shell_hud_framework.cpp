//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The HUD framework the engine and client code call into (CHud and CHudElement). The HUD itself is drawn by managed code.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"

#include "hudelement.h"

#include "client_entity.h"

CHud gHUD;

// Registers itself with gHUD on construction and removes itself on destruction.
CHudElement::CHudElement(const char* pElementName)
{
	m_pElementName = pElementName;
	m_bActive = false;
	m_iHiddenBits = 0;
	m_bNeedsRemove = false;
	m_bIsParentedToClientDLLRootPanel = false;

	gHUD.AddHudElement(this);
}

CHudElement::~CHudElement()
{
	if (m_bNeedsRemove)
		gHUD.RemoveHudElement(this);
}

void CHudElement::SetActive(bool bActive)
{
	m_bActive = bActive;
}

void CHudElement::SetHiddenBits(int iBits)
{
	m_iHiddenBits = iBits;
}

// The game hides the HUD wholesale during intermissions and scoreboards.
bool CHudElement::ShouldDraw()
{
	return !gHUD.IsHidden(m_iHiddenBits);
}

int CHudElement::GetRenderGroupPriority()
{
	return 0;
}

void CHudElement::SetNeedsRemove(bool needsremove)
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

CHud::~CHud() {}

void CHud::Init()
{
	for (int i = 0; i < m_HudList.Count(); i++)
		m_HudList[i]->Init();
}

void CHud::VidInit()
{
	for (int i = 0; i < m_HudList.Count(); i++)
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
	for (int i = 0; i < m_HudList.Count(); i++)
		m_HudList[i]->LevelInit();
}

void CHud::LevelShutdown()
{
	for (int i = 0; i < m_HudList.Count(); i++)
		m_HudList[i]->LevelShutdown();
}

void CHud::ResetHUD()
{
	for (int i = 0; i < m_HudList.Count(); i++)
		m_HudList[i]->Reset();
}

void CHud::ProcessInput(bool bActive)
{
	if (!bActive)
		return;

	for (int i = 0; i < m_HudList.Count(); i++)
	{
		if (m_HudList[i]->IsActive())
			m_HudList[i]->ProcessInput();
	}
}

void CHud::UpdateHud(bool bActive)
{
	ProcessInput(bActive);
}

void CHud::AddHudElement(CHudElement* pHudElement)
{
	if (!pHudElement)
		return;

	m_HudList.AddToTail(pHudElement);
	pHudElement->SetNeedsRemove(true);
}

void CHud::RemoveHudElement(CHudElement* pHudElement)
{
	m_HudList.FindAndRemove(pHudElement);
}

bool CHud::IsHidden(int iHudFlags)
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
ConVar closecaption("closecaption", "0", FCVAR_ARCHIVE | FCVAR_USERINFO, "Enable close captioning.");
