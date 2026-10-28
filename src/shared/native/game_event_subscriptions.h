//========= SourceSharp — which game events this side already listens for ===//
//
// shell_gameevents.cpp's record, so a managed reload does not add the listener
// twice and get every event delivered twice. A header of its own so
// game_event_subscriptions_test drives the real decision:
//
//   * a name already held IN ANY CASE is not added again (the StrICmp walk it
//     replaced was case-blind, and so is the engine's event lookup: "Player_
//     Death" and "player_death" are one subscription, not two deliveries);
//   * a new name is handed to the engine, and recorded only if the engine
//     took it -- a refused name can be asked for again;
//   * Clear() forgets them all, with the listener's removal, so the side can
//     subscribe again.
//
//=============================================================================//

#pragma once

#include <set>
#include <string>

#include "ss_containers.h"

class CGameEventSubscriptions
{
public:
	enum Result_t
	{
		kAlreadyListening,   // held already, in some case: add was not called
		kSubscribed,         // add took it, and it is recorded
		kRefused             // add refused it: not recorded
	};

	template < class AddFn >
	Result_t Subscribe( const char *pEventName, AddFn add )
	{
		if ( m_Names.count( pEventName ) )
			return kAlreadyListening;

		if ( !add( pEventName ) )
			return kRefused;

		m_Names.insert( pEventName );
		return kSubscribed;
	}

	int Count() const { return (int)m_Names.size(); }
	void Clear() { m_Names.clear(); }

private:
	std::set< std::string, ss::CaselessLess > m_Names;
};
