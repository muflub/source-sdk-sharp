// The game events this side listens for, so a managed reload does not subscribe twice.
// Names compare case-insensitively, as the engine's lookup does; a refused name is not recorded.

#pragma once

#include <set>

#include "ss_containers.h"

class CGameEventSubscriptions
{
public:
	enum Result_t
	{
		kAlreadyListening, // held already, in some case: add was not called
		kSubscribed, // add took it, and it is recorded
		kRefused // add refused it: not recorded
	};

	template <class AddFn>
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
	std::set<std::string, ss::CaselessLess> m_Names;
};
