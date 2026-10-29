//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// Game events are forwarded to C#: the shell subscribes to what C# asks for (game_events.h).

#include "game_events.h"

#include "shell_diagnostics.h"
#include "imanaged_areas.h"
#include "imanaged_areas_client.h"

void CGameEvents::CListener::FireGameEvent( IGameEvent *pEvent )
{
	if ( !pEvent )
		return;

	if ( SourceSharp_GetVerbosity() > 1 )
		Msg( "[SourceSharp] game event '%s'\n", pEvent->GetName() );

	if ( ManagedEventsArea_t *pEvents = m_ppManaged )
		pEvents->OnGameEvent( pEvent->GetName(), (void *)pEvent );
}

void CGameEvents::ListenForGameEvent( const char *pEventName )
{
	IGameEventManager2 *pManager = m_ppManager;
	if ( !pEventName || !pEventName[0] || !pManager )
		return;

	const CGameEventSubscriptions::Result_t eResult = m_Subscribed.Subscribe(
		pEventName, [&]( const char *pName ) { return pManager->AddListener( &m_Listener, pName, m_bServerSide ); } );

	if ( eResult == CGameEventSubscriptions::kAlreadyListening )
	{
		if ( SourceSharp_GetVerbosity() > 1 )
			Msg( "[SourceSharp] already listening for '%s'; kept\n", pEventName );
		return;
	}

	if ( eResult == CGameEventSubscriptions::kRefused )
	{
		Warning( "[SourceSharp] could not listen for game event '%s' — "
				 "no such event is declared in the game's event files\n",
				 pEventName );
		return;
	}

	if ( SourceSharp_GetVerbosity() > 0 )
		Msg( "[SourceSharp] listening for game event '%s'\n", pEventName );
}

void CGameEvents::StopListening()
{
	if ( IGameEventManager2 *pManager = m_ppManager )
		pManager->RemoveListener( &m_Listener );

	if ( m_Subscribed.Count() > 0 && SourceSharp_GetVerbosity() > 0 )
	{
		Msg( "[SourceSharp] stopped listening for %d game event(s)\n", m_Subscribed.Count() );
	}

	m_Subscribed.Clear();
}

// Field accessors on the live IGameEvent for the callback in progress; a null
// handle or key answers the default.
int32_t CGameEvents::EventGetInt( void *hEvent, const char *pKey )
{
	IGameEvent *pEvent = (IGameEvent *)hEvent;
	return ( pEvent && pKey ) ? pEvent->GetInt( pKey ) : 0;
}

float CGameEvents::EventGetFloat( void *hEvent, const char *pKey )
{
	IGameEvent *pEvent = (IGameEvent *)hEvent;
	return ( pEvent && pKey ) ? pEvent->GetFloat( pKey ) : 0.0f;
}

int32_t CGameEvents::EventGetBool( void *hEvent, const char *pKey )
{
	IGameEvent *pEvent = (IGameEvent *)hEvent;
	return ( pEvent && pKey && pEvent->GetBool( pKey ) ) ? 1 : 0;
}

const char *CGameEvents::EventGetString( void *hEvent, const char *pKey )
{
	IGameEvent *pEvent = (IGameEvent *)hEvent;
	return ( pEvent && pKey ) ? pEvent->GetString( pKey ) : "";
}

#ifndef CLIENT_DLL
void CGameEvents::EventSetInt( void *hEvent, const char *pKey, int32_t nValue )
{
	if ( IGameEvent *pEvent = (IGameEvent *)hEvent; pEvent && pKey )
		pEvent->SetInt( pKey, nValue );
}

void CGameEvents::EventSetBool( void *hEvent, const char *pKey, int32_t bValue )
{
	if ( IGameEvent *pEvent = (IGameEvent *)hEvent; pEvent && pKey )
		pEvent->SetBool( pKey, bValue != 0 );
}

void CGameEvents::EventSetString( void *hEvent, const char *pKey, const char *pValue )
{
	if ( IGameEvent *pEvent = (IGameEvent *)hEvent; pEvent && pKey )
		pEvent->SetString( pKey, pValue ? pValue : "" );
}

#endif // !CLIENT_DLL
