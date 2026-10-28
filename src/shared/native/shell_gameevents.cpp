//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// Game events are forwarded to C#: the shell subscribes to what C# asks for.

#include "igameevents.h"

// Each DLL binds its own: the client's is a global, the server's its DLL tier's.
#ifdef CLIENT_DLL
extern IGameEventManager2 *gameeventmanager;

static IGameEventManager2 *EventManager()
{
	return gameeventmanager;
}
#else
#include "server_engine.h"

static IGameEventManager2 *EventManager()
{
	return ServerEvents();
}
#endif

#include "shell_diagnostics.h"

#include "game_event_subscriptions.h"
#include "imanaged_areas.h"
#include "imanaged_areas_client.h"

// One listener for every event; IGameEvent carries its own name.
class CSourceSharpEventListener : public IGameEventListener2
{
public:
	virtual void FireGameEvent( IGameEvent *pEvent ) OVERRIDE
	{
		if ( !pEvent )
			return;

		if ( SourceSharp_GetVerbosity() > 1 )
			Msg( "[SourceSharp] game event '%s'\n", pEvent->GetName() );

#ifndef CLIENT_DLL
		if ( IManagedEvents *pEvents = ManagedEvents() )
			pEvents->OnGameEvent( pEvent->GetName(), (void *)pEvent );
#else
		if ( IManagedEventsClient *pEvents = ManagedEventsClient() )
			pEvents->OnGameEvent( pEvent->GetName(), (void *)pEvent );
#endif
	}
};

static CSourceSharpEventListener g_SourceSharpEventListener;

// Names already subscribed, so a managed reload does not add the listener twice.
// Case-insensitive.
static CGameEventSubscriptions g_SubscribedEvents;

void SourceSharp_ListenForGameEvent( const char *pEventName )
{
	if ( !pEventName || !pEventName[0] || !EventManager() )
		return;

	// bServerSide as stock's CGameEventListener: true on the server.
#ifdef CLIENT_DLL
	static const bool kServerSide = false;
#else
	static const bool kServerSide = true;
#endif
	const CGameEventSubscriptions::Result_t eResult = g_SubscribedEvents.Subscribe(
		pEventName, []( const char *pName )
		{ return EventManager()->AddListener( &g_SourceSharpEventListener, pName, kServerSide ); } );

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

// Removes the listener before this DLL unloads: the engine keeps its listener
// list for the whole process. The subscribed names are cleared too, or a later
// load could never re-subscribe. Safe to call twice or without listening.
void SourceSharp_StopListeningForGameEvents()
{
	if ( EventManager() )
		EventManager()->RemoveListener( &g_SourceSharpEventListener );

	if ( g_SubscribedEvents.Count() > 0 && SourceSharp_GetVerbosity() > 0 )
	{
		Msg( "[SourceSharp] stopped listening for %d game event(s)\n", g_SubscribedEvents.Count() );
	}

	g_SubscribedEvents.Clear();
}

// Field accessors on the live IGameEvent for the callback in progress; a null
// handle or key answers the default.
int32_t SourceSharp_EventGetInt( void *hEvent, const char *pKey )
{
	IGameEvent *pEvent = (IGameEvent *)hEvent;
	return ( pEvent && pKey ) ? pEvent->GetInt( pKey ) : 0;
}

float SourceSharp_EventGetFloat( void *hEvent, const char *pKey )
{
	IGameEvent *pEvent = (IGameEvent *)hEvent;
	return ( pEvent && pKey ) ? pEvent->GetFloat( pKey ) : 0.0f;
}

int32_t SourceSharp_EventGetBool( void *hEvent, const char *pKey )
{
	IGameEvent *pEvent = (IGameEvent *)hEvent;
	return ( pEvent && pKey && pEvent->GetBool( pKey ) ) ? 1 : 0;
}

const char *SourceSharp_EventGetString( void *hEvent, const char *pKey )
{
	IGameEvent *pEvent = (IGameEvent *)hEvent;
	return ( pEvent && pKey ) ? pEvent->GetString( pKey ) : "";
}

#ifndef CLIENT_DLL
void SourceSharp_EventSetInt( void *hEvent, const char *pKey, int32_t nValue )
{
	if ( IGameEvent *pEvent = (IGameEvent *)hEvent; pEvent && pKey )
		pEvent->SetInt( pKey, nValue );
}

void SourceSharp_EventSetBool( void *hEvent, const char *pKey, int32_t bValue )
{
	if ( IGameEvent *pEvent = (IGameEvent *)hEvent; pEvent && pKey )
		pEvent->SetBool( pKey, bValue != 0 );
}

void SourceSharp_EventSetString( void *hEvent, const char *pKey, const char *pValue )
{
	if ( IGameEvent *pEvent = (IGameEvent *)hEvent; pEvent && pKey )
		pEvent->SetString( pKey, pValue ? pValue : "" );
}

#endif // !CLIENT_DLL
