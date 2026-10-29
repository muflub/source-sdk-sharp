//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "igameevents.h"
extern IGameEventManager2* gameeventmanager;

// A safer method than inheriting straight from IGameEventListener2.
// Avoids requiring the user to remove themselves as listeners in
// their deconstructor, and sets the serverside variable based on
// our dll location.
class CGameEventListener : public IGameEventListener2
{
public:
	CGameEventListener()
		: m_bRegisteredForEvents(false)
	{
	}

	~CGameEventListener() { StopListeningForAllEvents(); }

	void ListenForGameEvent(const char* name)
	{
		m_bRegisteredForEvents = true;

		bool bServerSide = false;
		if (gameeventmanager)
		{
			gameeventmanager->AddListener(this, name, bServerSide);
		}

		AssertMsg1(gameeventmanager, "Failed to subscribe to event %s!", name);
	}

	void StopListeningForAllEvents()
	{
		// remove me from list
		if (m_bRegisteredForEvents)
		{
			if (gameeventmanager)
				gameeventmanager->RemoveListener(this);
			m_bRegisteredForEvents = false;
		}
	}

	// Intentionally abstract
	virtual void FireGameEvent(IGameEvent* event) = 0;

private:
	// Have we registered for any events?
	bool m_bRegisteredForEvents;
};
