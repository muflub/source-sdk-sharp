//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's user-message bodies ====================//
// SS_REASON b  an interface resolved by name at load
//
// plan_side_isolation §2.2a. The client's user messages are C#'s: the id a
// message arrives under, the key it maps to and the managed handler. What
// stays native is what C# calls back through IShimUserMessagesClient: carry
// out what the stock hooks did, over values C#
// has parsed (the user-message payload model, 2026-10-04: Shake and Fade on
// the view effects, Rumble, VGUIMenu's viewport panel, VoiceMask and
// RequestState on the voice status). No bf_read crosses back; every decision
// those hooks made is C#'s handler's.
//
// The dispatch itself is IBaseClientDLL::DispatchUserMessage's one forward in
// shell_clientdll.cpp, which hands C# the payload copied out whole as one
// BitBlock (C9, user ruling 2026-10-12): nothing is read back. The registry, the 64 per-index trampolines and the
// dispatch loop that were shell_usermessage_registry.cpp and
// shell_usermessages.cpp are evicted (docs/client_evictions.md).
//
// NO NATIVE HOOK ROUTE (lane HC, 2026-10-04). The six stock HOOK_MESSAGE
// sites are `#ifndef SOURCESHARP` in the fork and c_user_message_register.cpp
// left client.so, so no compiled TU calls usermessages->HookMessage. The
// forwarder, the `usermessages` object it was reached through, the
// UserMessageRunHook slot and C#'s hook list are gone with it.
//
//=============================================================================//

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
#include "client_entity.h"
#include "game/client/iviewport.h"
#include "iclientmode.h"
#include "hud.h"
#include "igameevents.h"
#include "tier1/KeyValues.h"
#include "GameEventListener.h"
#include "cdll_client_int.h"
#include "globalvars_base.h"
#include "sourcesharp.h"
extern IClientMode *GetClientModeNormal();   // shell_client.cpp

//-----------------------------------------------------------------------------
// THE STOCK HOOKS' EFFECTS, carried out over values C# parsed. Each body is
// the call the hook made after its reads (clientmode_shared.cpp,
// voice_status.cpp), and nothing it decided. Shake and Fade are managed's
// view effects since C7; Rumble is the managed mixer's.
//-----------------------------------------------------------------------------

// The two facts __MsgFunc_VGUIMenu read off native state, for C# to decide
// with: bit 0, the viewport has a panel by that name; bit 1, the client mode
// allows the info (MOTD) panel just now.
int32_t SourceSharp_ClientViewportQuery( const char *pszPanel )
{
	int32_t nFacts = 0;

	if ( gViewPortInterface && pszPanel && gViewPortInterface->FindPanelByName( pszPanel ) )
		nFacts |= 1;

	IClientMode *pMode = GetClientModeNormal();
	if ( pMode && pMode->IsInfoPanelAllowed() )
		nFacts |= 2;

	return nFacts;
}

// __MsgFunc_VGUIMenu's effects, each one asked for by C#'s handler in a bit of
// nFlags: 1 hand the panel its data (pPairs: a name, a NUL, a value, a NUL,
// and so on), 2 take a screenshot in a second, 4 fire ds_screenshot client
// side, 8 tell the client mode the info panel was shown, 16 show (or hide, by
// bShow) the panel. In stock's order.
void SourceSharp_ClientViewportShow( const char *pszPanel, int32_t bShow, const unsigned char *pPairs,
                                     int32_t nPairBytes, int32_t nFlags )
{
	if ( !gViewPortInterface || !pszPanel )
		return;

	IViewPortPanel *pPanel = gViewPortInterface->FindPanelByName( pszPanel );
	if ( !pPanel )
		return;

	if ( ( nFlags & 1 ) && pPairs && nPairBytes > 0 )
	{
		KeyValues *pKeys = new KeyValues( "data" );

		const char *pszAt  = (const char *)pPairs;
		const char *pszEnd = pszAt + nPairBytes;
		while ( pszAt < pszEnd )
		{
			const char *pszName = pszAt;
			pszAt += strnlen( pszAt, pszEnd - pszAt ) + 1;
			if ( pszAt >= pszEnd )
				break;
			const char *pszValue = pszAt;
			pszAt += strnlen( pszAt, pszEnd - pszAt ) + 1;
			pKeys->SetString( pszName, pszValue );
		}

		pPanel->SetData( pKeys );
		pKeys->deleteThis();
	}

	if ( nFlags & 2 )
		gHUD.SetScreenShotTime( gpGlobals->curtime + 1.0f );

	if ( nFlags & 4 )
	{
		if ( IGameEvent *pEvent = gameeventmanager ? gameeventmanager->CreateEvent( "ds_screenshot" ) : NULL )
		{
			pEvent->SetFloat( "delay", 0.5f );
			gameeventmanager->FireEventClientSide( pEvent );
		}
	}

	if ( nFlags & 8 )
	{
		if ( IClientMode *pMode = GetClientModeNormal() )
			pMode->InfoPanelDisplayed();
	}

	if ( nFlags & 16 )
		gViewPortInterface->ShowPanel( pPanel, bShow != 0 );
}

// __MsgFunc_VoiceMask and __MsgFunc_RequestState are SourceSharp.Client's
// VoiceStatus end to end (C3 step 4): no native call is left for them.
