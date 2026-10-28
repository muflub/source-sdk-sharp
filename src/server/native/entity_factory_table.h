//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Classname to entity factory table. Lookups are case-insensitive, as stock's
// CUtlDict( true ) is, so a map's "Info_Node" finds "info_node".

#pragma once

#include <map>
#include <string>
#include <vector>

#include "ss_containers.h"

// What Install() did with the name it was given. A duplicate is reported rather
// than inserted, so a release build cannot hold two entries under one name.
enum EntityFactoryInstall_t
{
	kEntityFactoryInstalled = 0,

	// The same pointer under the same name: a reload re-declaring a class.
	kEntityFactoryAlreadyInstalled,

	// A different factory holds the name. The first is kept, since the engine may
	// already have created instances through it.
	kEntityFactoryNameTaken,

	kEntityFactoryNoName
};

// Does not own the factories; owns its copy of every name.
template < class T >
class CEntityFactoryTable
{
public:
	EntityFactoryInstall_t Install( T *pFactory, const char *pClassName )
	{
		if ( !pClassName || !pClassName[0] )
			return kEntityFactoryNoName;

		typename Map_t::const_iterator it = m_Factories.find( pClassName );

		if ( it != m_Factories.end() )
		{
			return ( it->second == pFactory )
				? kEntityFactoryAlreadyInstalled
				: kEntityFactoryNameTaken;
		}

		m_Order.push_back( &*m_Factories.emplace( pClassName, pFactory ).first );
		return kEntityFactoryInstalled;
	}

	T *Find( const char *pClassName ) const
	{
		if ( !pClassName || !pClassName[0] )
			return NULL;

		typename Map_t::const_iterator it = m_Factories.find( pClassName );

		if ( it == m_Factories.end() )
			return NULL;

		return it->second;
	}

	// The spelling the factory was installed under. NULL for an unknown name.
	const char *CanonicalName( const char *pClassName ) const
	{
		if ( !pClassName || !pClassName[0] )
			return NULL;

		typename Map_t::const_iterator it = m_Factories.find( pClassName );

		if ( it == m_Factories.end() )
			return NULL;

		return it->first.c_str();
	}

	// Enumeration by dense index, in install order.
	int Count() const { return (int)m_Order.size(); }

	const char *NameAt( int i ) const
	{
		if ( i < 0 || i >= Count() )
			return NULL;

		return m_Order[i]->first.c_str();
	}

	T *FactoryAt( int i ) const
	{
		if ( i < 0 || i >= Count() )
			return NULL;

		return m_Order[i]->second;
	}

private:
	typedef std::map< std::string, T *, ss::CaselessLess > Map_t;

	// A map node never moves, so a pointer to one stays valid; entries are never removed.
	Map_t                                        m_Factories;
	std::vector< const typename Map_t::value_type * > m_Order;
};
