//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "igameevents.h"
#include "shell_client_engine_interfaces.h"

// A safer method than inheriting straight from IGameEventListener2.
// Avoids requiring the user to remove themselves as listeners in
// their deconstructor, and sets the serverside variable based on
// our dll location.
class CGameEventListener : public IGameEventListener2
{
public:
	explicit CGameEventListener(const CClientEngineInterfaces& engines)
		: m_ListenerEngines(engines)
		, m_bRegisteredForEvents(false)
	{
	}

	~CGameEventListener() { StopListeningForAllEvents(); }

	void ListenForGameEvent(const char* name)
	{
		m_bRegisteredForEvents = true;

		bool bServerSide = false;
		if (m_ListenerEngines.m_pEvents)
		{
			m_ListenerEngines.m_pEvents->AddListener(this, name, bServerSide);
		}

		AssertMsg1(m_ListenerEngines.m_pEvents, "Failed to subscribe to event %s!", name);
	}

	void StopListeningForAllEvents()
	{
		// remove me from list
		if (m_bRegisteredForEvents)
		{
			if (m_ListenerEngines.m_pEvents)
				m_ListenerEngines.m_pEvents->RemoveListener(this);
			m_bRegisteredForEvents = false;
		}
	}

	// Intentionally abstract
	virtual void FireGameEvent(IGameEvent* event) = 0;

private:
	// Have we registered for any events?
	const CClientEngineInterfaces& m_ListenerEngines;
	bool m_bRegisteredForEvents;
};
