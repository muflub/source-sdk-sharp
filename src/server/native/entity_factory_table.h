//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the port's own entity factory name table ============//
//
// plan_entity.md §3.5 says the factory dictionary is "not needed natively".
// That is the ENDPOINT, and this is not it: `p4-flip` measured the present,
// which is that `EntityFactoryDictionary()` is one of the four subsystems the
// MANAGED hierarchy itself runs on. Every `[ScriptedEntity]` C# declares
// installs a `CStandaloneScriptedClass` there
// (`shell_standalone_class.cpp`), worldspawn installs a `CWorldFactory`
// (`world_adapter.cpp`), the player is FOUND there at connect
// (`shell_standalone_player.cpp`) and `SOURCESHARP_LINK_ENTITY` installs there
// too (`shell_diagnostics.h`). Deleting it would not remove SDK legacy; it
// would stop the port creating its own entities.
//
// So this is a MOVE. The table below is what `CEntityFactoryDictionary`
// (`src/game/server/util.cpp:111`) was, owned by the shim instead of by a stock
// translation unit in the removal set, and reduced to the part that is
// genuinely mechanism: a name, a pointer, and four lookups.
//
// WHY IT IS A SEPARATE HEADER RATHER THAN A CLASS IN THE .cpp.
//
// `shell_standalone_class.cpp` includes `cbase.h` and cannot be linked into a
// gtest. A table that cannot be tested behaviourally would have been pinned by
// the only instrument left -- a scanner reading its own source text -- and this
// project has 43 recorded checks that could not fail, most of them that shape.
// Templated on the factory type and free of every engine header, this compiles
// into `entity_factory_table_test` against a fake factory and is exercised for
// real: install, re-install, collision, case, canonical name, enumeration.
//
// THE CASE RULE IS LOAD-BEARING AND IS NOT OBVIOUS FROM THE STOCK LINE.
// `CEntityFactoryDictionary::CEntityFactoryDictionary()` reads
// `m_Factories( true, 0, 128 )`, and `true` is 1, which `utldict.h:27` names
// `k_eDictCompareTypeCaseInsensitive`. A map writing `Info_Node` therefore
// finds the same factory as `info_node`, and the shim's own
// `FindStandaloneClass` agrees with it because that walk uses `Q_stricmp`.
// The table's comparator, ss::CaselessLess, is that compare, named.
//
//=============================================================================//

#ifndef SOURCESHARP_ENTITY_FACTORY_TABLE_H
#define SOURCESHARP_ENTITY_FACTORY_TABLE_H
#ifdef _WIN32
#pragma once
#endif

#include <map>
#include <string>
#include <vector>

#include "ss_containers.h"

//-----------------------------------------------------------------------------
// What Install() did with the name it was given.
//
// A RETURN RATHER THAN AN Assert(), which is the one behavioural difference
// from the stock dictionary and the reason it is here.
//
// `CEntityFactoryDictionary::InstallFactory` opens
// `Assert( FindFactory( pClassName ) == NULL )` and then inserts ANYWAY.  In a
// release build the assertion is compiled out and the duplicate key lands
// silently, so `m_Factories` ends up holding two entries under one name and
// `Find` answers whichever the tree happens to reach.  One `cs_sv_reload`
// used to do that 284 times -- measured, `shell_standalone_class.cpp`'s class
// comment -- and nothing ever removed any of them.
//
// The shim already carries a fix for its OWN installs (re-describe instead of
// re-install).  That fix cannot cover `world_adapter.cpp` or the
// `SOURCESHARP_LINK_ENTITY` registrations, which install from static
// initialisers this port does not drive.  Answering here makes the table safe
// for every caller instead of for one of them.
//-----------------------------------------------------------------------------
enum EntityFactoryInstall_t
{
	// The name was free and now holds this factory.
	kEntityFactoryInstalled = 0,

	// This exact pointer is ALREADY under this exact name. Nothing changed and
	// nothing is wrong: a reload re-declaring a class it already declared, and
	// the engine is still holding the pointer it was given the first time.
	kEntityFactoryAlreadyInstalled,

	// The name is taken by a DIFFERENT factory. The FIRST one is kept -- the
	// engine may already have handed instances out through it -- and the caller
	// is told, so it can print rather than corrupt the table.
	kEntityFactoryNameTaken,

	// No name, or an empty one. Stock would have inserted "" as a key.
	kEntityFactoryNoName
};

//-----------------------------------------------------------------------------
// The table itself. Four lookups, no iteration policy, no ownership.
//
// It does NOT own the factories, exactly as the stock dictionary did not: they
// are file-scope statics and `new`ed class objects that outlive the process's
// interest in them, and the engine holds the same pointers. It DOES own its
// copy of every name, as the `CUtlDict` it was did -- which is why the key can be
// handed back as the canonical spelling. A std::map under ss::CaselessLess,
// the dictionary's own compare (held to it by ss_containers_oracle_test), so
// this header instantiates no CUtlMemory and imports no g_pMemAlloc.
//-----------------------------------------------------------------------------
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

	//-------------------------------------------------------------------------
	// THE SPELLING THE FACTORY WAS INSTALLED UNDER, not the one asked for.
	//
	// NULL for a name nobody installed, which is where this differs from stock
	// and where stock was wrong. `CEntityFactoryDictionary::GetCannonicalName`
	// is one line --
	//
	//     return m_Factories.GetElementName( m_Factories.Find( pClassName ) );
	//
	// -- with no test of the Find. `GetElementName( InvalidIndex() )` indexes
	// the element vector at 0xFFFF, so an unknown classname is a read a long
	// way off the end of the map. Its three live callers are all in
	// `physics_main.cpp` (:1068, :2154, :2213), all inside Warning() calls
	// about an entity that is misbehaving -- which is to say the path that
	// reports a problem is the path that could turn it into a crash.
	//-------------------------------------------------------------------------
	const char *CanonicalName( const char *pClassName ) const
	{
		if ( !pClassName || !pClassName[0] )
			return NULL;

		typename Map_t::const_iterator it = m_Factories.find( pClassName );

		if ( it == m_Factories.end() )
			return NULL;

		return it->first.c_str();
	}

	//-------------------------------------------------------------------------
	// Enumeration, by DENSE index, so a caller with no CUtlDict can walk it --
	// the console command, and the managed side comparing this table against its
	// own declarations.
	//
	// IN INSTALL ORDER, kept in its own vector rather than read off the map.
	//
	// The vector is BEHAVIOUR now, not insurance. While this was a CUtlDict,
	// its node index happened to be the install order (`CUtlRBTree::NewNode`
	// allocates slots sequentially, and nothing is ever removed), so the
	// vector only guarded against a future uninstall. A std::map walks in KEY
	// order, so without m_Order the enumeration would come out sorted --
	// `EnumerationSurvivesTheDictionaryRebalancing` and
	// `EnumerationIsDenseAndInInstallOrder` are what say it does not.
	//
	// Install order is also the more useful order: it is the order the port
	// declared its classes in, so a managed reload that added one puts it last
	// rather than shuffling the whole list.
	//-------------------------------------------------------------------------
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

	// A map node never moves, so a pointer to one is as stable as the dict
	// index it replaces; entries are never removed.
	Map_t                                        m_Factories;
	std::vector< const typename Map_t::value_type * > m_Order;
};

#endif // SOURCESHARP_ENTITY_FACTORY_TABLE_H
