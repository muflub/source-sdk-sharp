//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the server's game string pool storage =============//
//
// plan_server_novalve.md P3, tier1 off the link; both sides since C9 (the
// client's took stringpool.o and generichash.o onto its link). What tier1's
// CStringPool gave CGameStringPool (gamestringpool.cpp) and nothing more: one copy per string,
// found case-insensitively (CStringPool's StrLess was Q_stricmp), at an address
// that never moves until FreeAll (a std::set node does not move, and neither
// does the text of the std::string in it).
//
// std::string, not strdup/free: tier0/memdbgon.h rewrites strdup to
// MemAlloc_StrDup (tier0's allocator) and leaves free alone, so a TU that
// includes it (gamestringpool.cpp does) allocated with one allocator and freed
// with another. memoverride made the two the same; plan_server_novalve.md P4
// removes memoverride. operator new/delete are rewritten by nothing.
// server_string_pool_test pins each of those.
//
//=============================================================================//

#ifndef SERVER_STRING_POOL_H
#define SERVER_STRING_POOL_H
#ifdef _WIN32
#pragma once
#endif

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

protected:
	// Transparent, so a lookup by const char * builds no std::string.
	struct Less
	{
		typedef void is_transparent;
		static const char *C( const char *p )        { return p; }
		static const char *C( const std::string &s ) { return s.c_str(); }
		template < class A, class B >
		bool operator()( const A &a, const B &b ) const { return ss::StrICmp( C( a ), C( b ) ) < 0; }
	};
	typedef std::set< std::string, Less > Set;

	Set m_Strings;
};

#endif // SERVER_STRING_POOL_H
