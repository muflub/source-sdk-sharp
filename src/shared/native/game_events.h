//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The game events managed listens for: one listener the engine calls for every event
// managed subscribed to, handed on to this side's managed events area, and the field
// accessors on the event in progress. One per DLL, owned by its DLL tier, which hands
// the shim areas its address; both DLLs compile it, each over its own areas.

#pragma once

#include "igameevents.h"

#include "ishim_areas.h"
#include "game_event_subscriptions.h"

#ifdef CLIENT_DLL
class IManagedEventsClient;
typedef IManagedEventsClient ManagedEventsArea_t;
typedef IShimEvents GameEventsArea_t;
#else
class IManagedEvents;
typedef IManagedEvents ManagedEventsArea_t;
typedef IShimEventsServer GameEventsArea_t;
#endif

class CGameEvents final : public GameEventsArea_t
{
public:
	// ppManager is the engine's event manager slot, NULL until the engine hands it over;
	// ppManaged this load's managed events area, NULL outside a load. bServerSide is
	// stock CGameEventListener's: true on the server.
	CGameEvents( IGameEventManager2 *const &ppManager, ManagedEventsArea_t *const &ppManaged, bool bServerSide )
		: m_ppManager( ppManager )
		, m_Listener( ppManaged )
		, m_bServerSide( bServerSide )
	{
	}

	CGameEvents( const CGameEvents & ) = delete;
	CGameEvents &operator=( const CGameEvents & ) = delete;

	void ListenForGameEvent( const char *pEventName ) override;
	int32_t EventGetInt( void *hEvent, const char *pKey ) override;
	float EventGetFloat( void *hEvent, const char *pKey ) override;
	int32_t EventGetBool( void *hEvent, const char *pKey ) override;
	const char *EventGetString( void *hEvent, const char *pKey ) override;
#ifndef CLIENT_DLL
	void EventSetInt( void *hEvent, const char *pKey, int32_t nValue ) override;
	void EventSetBool( void *hEvent, const char *pKey, int32_t bValue ) override;
	void EventSetString( void *hEvent, const char *pKey, const char *pValue ) override;
#endif

	// Removes the listener before this DLL unloads: the engine keeps its listener list
	// for the whole process. The subscribed names are cleared too, or a later load could
	// never re-subscribe. Safe to call twice or without listening. A managed reload keeps
	// both: the listener reads whichever load's area is current.
	void StopListening();

	int SubscribedCount() const { return m_Subscribed.Count(); }

private:
	// One listener for every event; IGameEvent carries its own name.
	class CListener final : public IGameEventListener2
	{
	public:
		explicit CListener( ManagedEventsArea_t *const &ppManaged )
			: m_ppManaged( ppManaged )
		{
		}

		void FireGameEvent( IGameEvent *pEvent ) override;

	private:
		ManagedEventsArea_t *const &m_ppManaged;
	};

	IGameEventManager2 *const &m_ppManager;
	CListener m_Listener;
	const bool m_bServerSide;

	// Names already subscribed, so a managed reload does not add the listener twice.
	// Case-insensitive.
	CGameEventSubscriptions m_Subscribed;
};
