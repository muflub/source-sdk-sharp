//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "utlsymbol.h"

template <class T>
class CUtlStringMap
{
public:
	CUtlStringMap( bool caseInsensitive = true ) : m_SymbolTable( 0, 32, caseInsensitive )
	{
	}

	unsigned int Count() const
	{
		Assert( m_Vector.Count() == m_SymbolTable.GetNumStrings() );
		return m_Vector.Count();
	}

	bool Defined( const char *pString ) const
	{
		return m_SymbolTable.Find( pString ) != UTL_INVAL_SYMBOL;
	}

	UtlSymId_t Find( const char *pString ) const
	{
		return m_SymbolTable.Find( pString );
	}

	static UtlSymId_t InvalidIndex()
	{
		return UTL_INVAL_SYMBOL;
	}

	int GetNumStrings() const
	{
		return m_SymbolTable.GetNumStrings();
	}

	const char *String( int n )	const
	{
		return m_SymbolTable.String( n );
	}

	// Clear all of the data from the map
	void Clear()
	{
		m_Vector.RemoveAll();
		m_SymbolTable.RemoveAll();
	}

	void Purge()
	{
		m_Vector.Purge();
		m_SymbolTable.RemoveAll();
	}

	void PurgeAndDeleteElements()
	{
		m_Vector.PurgeAndDeleteElements();
		m_SymbolTable.RemoveAll();
	}

private:
	CUtlVector<T> m_Vector;
	CUtlSymbolTable m_SymbolTable;
};
