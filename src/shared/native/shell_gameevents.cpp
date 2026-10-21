//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — game events routed into C# ==========================//
// SS_REASON a  a vtable the engine calls through
//
// Game events are game logic: who died, who connected, who changed team. They
// belong in C#, so the shell here does nothing but subscribe to what C# asks for
// and forward what fires.
//
// This also replaces the reason the mod derived from CEventLog. That base class
// existed to listen for a hardcoded set of events, and an inert stub of it
// crashed the server: something later did SetInt("userid", ...) on an event that
// was never registered. Listening for the same events from here satisfies that
// requirement without inheriting the rest of CEventLog's behaviour.
//
// Events are looked up by name at dispatch, so unlike user messages the
// registration order carries no meaning — this is a subscription, not a
// protocol.
//
//=============================================================================//

#include "igameevents.h"

// The engine's game event manager: each DLL defines its own (the server in
// shell_engineboundary.cpp), declared here rather than through a game header.
extern IGameEventManager2 *gameeventmanager;

#include "shell_diagnostics.h"
#include "sourcesharp.h"
#include "ss_string.h"
#include "game_event_subscriptions.h"
#include "imanaged_areas.h"          // plan_engineapi_vtables E12: the server's IManagedEvents
#include "imanaged_areas_client.h"   // plan_client_vtables CE3: IManagedEventsClient

//-----------------------------------------------------------------------------
// Forwards subscribed events to C#.
//
// A single listener for every event rather than one per event: IGameEvent
// carries its own name, so there is nothing to disambiguate — which is why this
// needs none of the per-index thunk machinery the user messages did.
//-----------------------------------------------------------------------------
class CSourceSharpEventListener : public IGameEventListener2
{
public:
	virtual void FireGameEvent( IGameEvent *pEvent ) OVERRIDE
	{
		if ( !pEvent )
			return;

		if ( SourceSharp_GetVerbosity() > 1 )
			Msg( "[SourceSharp] game event '%s'\n", pEvent->GetName() );

		// The side's managed-built area: E12 on the server, plan_client_vtables
		// CE3 on the client.
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

// Names we already subscribed to, so a managed reload does not add the listener
// twice and get every event delivered twice. Case-blind, as the StrICmp walk
// it replaced was: game_event_subscriptions.h.
static CGameEventSubscriptions g_SubscribedEvents;

//-----------------------------------------------------------------------------
void SourceSharp_ListenForGameEvent( const char *pEventName )
{
	if ( !pEventName || !pEventName[0] || !gameeventmanager )
		return;

	// bServerSide as stock's CGameEventListener: true on the server, or the
	// server's managed listeners never hear what the server fires.
#ifdef CLIENT_DLL
	static const bool kServerSide = false;
#else
	static const bool kServerSide = true;
#endif
	const CGameEventSubscriptions::Result_t eResult = g_SubscribedEvents.Subscribe( pEventName,
		[]( const char *pName ) { return gameeventmanager->AddListener( &g_SourceSharpEventListener, pName, kServerSide ); } );

	if ( eResult == CGameEventSubscriptions::kAlreadyListening )
	{
		if ( SourceSharp_GetVerbosity() > 1 )
			Msg( "[SourceSharp] already listening for '%s'; kept\n", pEventName );
		return;
	}

	if ( eResult == CGameEventSubscriptions::kRefused )
	{
		Warning( "[SourceSharp] could not listen for game event '%s' — "
		         "no such event is declared in the game's event files\n", pEventName );
		return;
	}

	if ( SourceSharp_GetVerbosity() > 0 )
		Msg( "[SourceSharp] listening for game event '%s'\n", pEventName );
}

//-----------------------------------------------------------------------------
// AND THE OTHER HALF, which was missing.
//
// AddListener hands the ENGINE's event manager a pointer to an object that
// lives in THIS DLL, and nothing ever took it back. The engine's
// CGameEventManager keeps its listener list for the whole process -- the
// engine initialises its game-event manager at startup and purges the list
// only at its own shutdown, not per map -- so the
// subscription outlives every map change, which is what makes it work, and
// would have outlived the DLL, which is what makes it a bug.
//
// EXACTLY the argument SourceSharp_RemoveFileSystemHook is already called for
// at the top of SourceSharp_UnloadManagedSide, in the same place and for the
// same reason: "an object the engine owns is pointing at a vtable that lives in
// this DLL, and this DLL is about to go away". This is the second such object
// and it had no such call.
//
// The subscribed list is purged with it rather than left standing, and that
// matters more than tidiness: the list is what makes ListenForGameEvent skip a
// name it has already added. Removing the listener and keeping the names would
// leave a side that can never re-subscribe -- silent, and it would look like C#
// had stopped raising events.
//
// Safe to call twice and safe to call having never listened: RemoveListener on
// a listener that is not in the list is a no-op walk in the engine, and purging
// an empty vector is nothing.
//-----------------------------------------------------------------------------
void SourceSharp_StopListeningForGameEvents( void )
{
	if ( gameeventmanager )
		gameeventmanager->RemoveListener( &g_SourceSharpEventListener );

	if ( g_SubscribedEvents.Count() > 0 && SourceSharp_GetVerbosity() > 0 )
	{
		Msg( "[SourceSharp] stopped listening for %d game event(s)\n",
			g_SubscribedEvents.Count() );
	}

	g_SubscribedEvents.Clear();
}

//-----------------------------------------------------------------------------
// Field accessors. The handle is the live IGameEvent for the callback in
// progress; a null handle or key returns the default rather than faulting.
//-----------------------------------------------------------------------------
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
