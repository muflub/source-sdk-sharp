//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// What the stock user-message hooks did after their reads, carried out over
// values managed code parsed. Every decision is the managed handler's.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "game/client/iviewport.h"
#include "iclientmode.h"
#include "hud.h"

#include "GameEventListener.h"
#include "shell_client_mode.h"
#include "shell_viewport_client.h"
#include "shell_client_engine_interfaces.h"

// Facts for managed code's VGUIMenu handler: bit 0, the viewport has a panel by
// that name; bit 1, the client mode allows the info (MOTD) panel now.
int32_t CSourceSharpClientMode::ClientViewportQuery(const char* pszPanel)
{
	int32_t nFacts = 0;

	if (m_pViewport && pszPanel && m_pViewport->FindPanelByName(pszPanel))
		nFacts |= 1;

	if (IsInfoPanelAllowed())
		nFacts |= 2;

	return nFacts;
}

// VGUIMenu's effects, each asked for by a bit of nFlags, in stock's order:
// 1 hand the panel its data (pPairs: name, NUL, value, NUL, ...), 2 take a
// screenshot in a second, 4 fire ds_screenshot client side, 8 tell the client
// mode the info panel was shown, 16 show (or hide, by bShow) the panel.
void CSourceSharpClientMode::ClientViewportShow(const char* pszPanel, int32_t bShow, const unsigned char* pPairs,
												int32_t nPairBytes, int32_t nFlags)
{
	if (!m_pViewport || !pszPanel)
		return;

	IViewPortPanel* pPanel = m_pViewport->FindPanelByName(pszPanel);
	if (!pPanel)
		return;

	if ((nFlags & 1) && pPairs && nPairBytes > 0)
	{
		KeyValues* pKeys = new KeyValues("data");

		const char* pszAt = (const char*)pPairs;
		const char* pszEnd = pszAt + nPairBytes;
		while (pszAt < pszEnd)
		{
			const char* pszName = pszAt;
			pszAt += strnlen(pszAt, pszEnd - pszAt) + 1;
			if (pszAt >= pszEnd)
				break;
			const char* pszValue = pszAt;
			pszAt += strnlen(pszAt, pszEnd - pszAt) + 1;
			pKeys->SetString(pszName, pszValue);
		}

		pPanel->SetData(pKeys);
		pKeys->deleteThis();
	}

	if (nFlags & 2)
		m_Hud.SetScreenShotTime(m_Engines.m_pGlobals->curtime + 1.0f);

	if (nFlags & 4)
	{
		if (IGameEvent* pEvent = m_Engines.m_pEvents ? m_Engines.m_pEvents->CreateEvent("ds_screenshot") : NULL)
		{
			pEvent->SetFloat("delay", 0.5f);
			m_Engines.m_pEvents->FireEventClientSide(pEvent);
		}
	}

	if (nFlags & 8)
	{
		InfoPanelDisplayed();
	}

	if (nFlags & 16)
		m_pViewport->ShowPanel(pPanel, bShow != 0);
}
