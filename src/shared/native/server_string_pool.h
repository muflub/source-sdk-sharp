//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The game string pool's storage: one copy per string, found case-insensitively, at
// an address that never moves until FreeAll. std::string rather than strdup/free,
// because memdbgon.h rewrites strdup to tier0's allocator but leaves free alone.

#pragma once

#include "ss_string.h"

#include <set>
#include <string>

class CServerStringPool
{
public:
	const char *Allocate( const char *pszValue )
	{
		Set::iterator it = m_Strings.find( pszValue );
		if ( it != m_Strings.end() )
			return it->c_str();

		return m_Strings.emplace_hint( it, pszValue )->c_str();
	}

	void FreeAll() { m_Strings.clear(); }

	int Count() const { return (int)m_Strings.size(); }

	// Every pooled string, in order.
	template <typename Fn>
	void Visit( Fn fnVisit ) const
	{
		for ( typename Set::const_iterator it = m_Strings.begin(); it != m_Strings.end(); ++it )
			fnVisit( it->c_str() );
	}

protected:
	// Transparent, so a lookup by const char * builds no std::string.
	struct Less
	{
		typedef void is_transparent;

		static const char *C( const char *p ) { return p; }

		static const char *C( const std::string &s ) { return s.c_str(); }

		template <class A, class B>
		bool operator()( const A &a, const B &b ) const
		{
			return ss::StrICmp( C( a ), C( b ) ) < 0;
		}
	};

	typedef std::set<std::string, Less> Set;

	Set m_Strings;
};
