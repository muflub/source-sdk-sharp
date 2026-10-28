//========= SourceSharp — tier1 container semantics on the standard library ==//
//
// server.so stops importing tier0's g_pMemAlloc (plan_server_novalve.md P4):
// tier1's CUtlMemory grows and frees through it, because tier0/memdbgon.h
// rewrites malloc/realloc/free inside utlmemory.h. Our own containers move to
// the standard library, and where a tier1 container had a semantic the
// standard one does not, it is restated here and held to the pristine tier1
// by ss_containers_oracle_test:
//
//   CaselessLess  CUtlDict's k_eDictCompareTypeCaseInsensitive and utlmap.h's
//                 CaselessStringLessThan: V_stricmp( a, b ) < 0. As a std::map
//                 or std::set comparator it gives the same equivalence (Find)
//                 and the same key order (FirstInorder/NextInorder).
//
//   CaselessHash  the same equivalence for std::unordered_map/set:
//   CaselessEqual CaselessEqual is V_stricmp( a, b ) == 0, and CaselessHash
//                 hashes the ASCII-lowercased bytes, so names equal under it
//                 hash equal. Only ASCII folds, as in V_stricmp.
//
//   StringPool    owned copies of strings, one per distinct string (case-
//                 sensitive), whose c_str() never moves until the pool goes:
//                 an unordered_set's elements are nodes, so a rehash moves no
//                 string. For names a struct or the engine keeps a pointer to.
//
//=============================================================================//

#pragma once

#include <cstddef>
#include <string>
#include <unordered_set>

#include "ss_string.h"

namespace ss
{

struct CaselessLess
{
	using is_transparent = void;

	bool operator()( const char *a, const char *b ) const { return StrICmp( a, b ) < 0; }
	bool operator()( const std::string &a, const std::string &b ) const
	{
		return StrICmp( a.c_str(), b.c_str() ) < 0;
	}
	bool operator()( const std::string &a, const char *b ) const { return StrICmp( a.c_str(), b ) < 0; }
	bool operator()( const char *a, const std::string &b ) const { return StrICmp( a, b.c_str() ) < 0; }
};

struct CaselessEqual
{
	bool operator()( const std::string &a, const std::string &b ) const
	{
		return StrICmp( a.c_str(), b.c_str() ) == 0;
	}
};

struct CaselessHash
{
	// FNV-1a over the ASCII-lowercased bytes.
	size_t operator()( const std::string &s ) const
	{
		size_t h = (size_t)14695981039346656037ull;
		for ( unsigned char c : s )
		{
			h ^= AsciiLower( c );
			h *= (size_t)1099511628211ull;
		}
		return h;
	}
};

class StringPool
{
public:
	const char *Intern( const char *psz ) { return m_Strings.emplace( psz ).first->c_str(); }
	int Count() const { return (int)m_Strings.size(); }

private:
	std::unordered_set< std::string > m_Strings;
};

} // namespace ss
