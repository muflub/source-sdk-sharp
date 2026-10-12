//========= SourceSharp — what each weapon does when fired, by class name ====//
//
// The table shell_shared.cpp holds for SourceSharp_RegisterWeaponType and the
// three readers the attack path asks mid-shot (fire rate, spread, pellets).
// A header of its own so weapon_type_table_test drives the real one.
//
// Keyed by class name, CASE-BLIND (ss::CaselessLess, the StrICmp compare the
// linear walk it replaced used), and updated in place on re-registration: both
// sides register the same table, and the client is loaded after the server on
// a listen server. The name is kept whole; the walk it replaced copied it into
// a 64-byte field, so a name of 64 characters or more was stored cut short and
// never found again. A map node never moves, so a Find answer stays valid
// across later registrations.
//
//=============================================================================//

#ifndef SOURCESHARP_WEAPON_TYPE_TABLE_H
#define SOURCESHARP_WEAPON_TYPE_TABLE_H
#ifdef _WIN32
#pragma once
#endif

#include <map>
#include <string>

#include "ss_containers.h"

struct WeaponStats_t
{
	float flFireRate;
	float flSpreadDegrees;
	int   nPellets;
};

class CWeaponTypeTable
{
public:
	// True for a class not seen before; false when an existing entry (under
	// any case) took the new numbers and kept its first spelling.
	bool Register( const char *pClassName, const WeaponStats_t &stats )
	{
		return m_Types.insert_or_assign( pClassName, stats ).second;
	}

	// NULL for an empty or unregistered name.
	const WeaponStats_t *Find( const char *pClassName ) const
	{
		if ( !pClassName || !pClassName[0] )
			return NULL;

		Map_t::const_iterator it = m_Types.find( pClassName );
		return ( it != m_Types.end() ) ? &it->second : NULL;
	}

	int Count() const { return (int)m_Types.size(); }

private:
	typedef std::map< std::string, WeaponStats_t, ss::CaselessLess > Map_t;
	Map_t m_Types;
};

#endif // SOURCESHARP_WEAPON_TYPE_TABLE_H
