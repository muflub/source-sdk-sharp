// tier1 container semantics restated on the standard library.
// CaselessLess/CaselessEqual/CaselessHash match V_stricmp (ASCII folding only).
// StringPool keeps one copy per distinct string; c_str() is stable until the pool goes.

#pragma once

#include <string>
#include <unordered_set>

#include "ss_string.h"

namespace ss
{

struct CaselessLess
{
	using is_transparent = void;

	bool operator()( const char *a, const char *b ) const { return StrICmp( a, b ) < 0; }

	bool operator()( const std::string &a, const std::string &b ) const { return StrICmp( a.c_str(), b.c_str() ) < 0; }

	bool operator()( const std::string &a, const char *b ) const { return StrICmp( a.c_str(), b ) < 0; }

	bool operator()( const char *a, const std::string &b ) const { return StrICmp( a, b.c_str() ) < 0; }
};

struct CaselessEqual
{
	bool operator()( const std::string &a, const std::string &b ) const { return StrICmp( a.c_str(), b.c_str() ) == 0; }
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
	std::unordered_set<std::string> m_Strings;
};

} // namespace ss
