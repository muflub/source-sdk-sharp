// Bookkeeping for the managed console's native carriers: ids are dense, rising and
// never reused; names are looked up case-insensitively. The carriers live for the
// process because the engine holds pointers into them.

#pragma once

#include <unordered_map>

#include "ss_containers.h"

template <class T>
class CConsoleRegistry
{
public:
	CConsoleRegistry()
		: m_nNextId( 0 )
	{
	}

	// The id the next Add will hand out, so a carrier can be built knowing it.
	int NextId() const { return m_nNextId; }

	// Records a carrier built with NextId() under pName; answers that id.
	int Add( const char *pName, T *pCarrier )
	{
		const int nId = m_nNextId++;
		m_ById.emplace( nId, pCarrier );
		m_IdByName.emplace( pName, nId );
		return nId;
	}

	// The id registered under pName in any case, or -1.
	int IdOf( const char *pName ) const
	{
		if ( !pName )
			return -1;

		NameIndex_t::const_iterator it = m_IdByName.find( pName );
		return ( it != m_IdByName.end() ) ? it->second : -1;
	}

	// The carrier under nId, or NULL.
	T *ById( int nId ) const
	{
		typename ById_t::const_iterator it = m_ById.find( nId );
		return ( it != m_ById.end() ) ? it->second : NULL;
	}

	T *ByName( const char *pName ) const { return ById( IdOf( pName ) ); }

	int Count() const { return (int)m_ById.size(); }

	// Delete every carrier and forget every name, once the engine holds none of
	// them (after UnregisterConCommands). Ids are not handed out again: a
	// carrier registered after this gets the next one, never a reused one.
	void DeleteAll()
	{
		for ( typename ById_t::value_type &entry : m_ById )
			delete entry.second;
		m_ById.clear();
		m_IdByName.clear();
	}

private:
	typedef std::unordered_map<int, T *> ById_t;
	typedef std::unordered_map<std::string, int, ss::CaselessHash, ss::CaselessEqual> NameIndex_t;

	ById_t m_ById;
	NameIndex_t m_IdByName;
	int m_nNextId;
};
