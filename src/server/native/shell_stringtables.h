//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//=============================================================================//
// MOD-DECLARED NETWORK STRING TABLES -- plan_gamedll.md Part 13,
// plan_gamedll_after.md Phase A8.
//
// WHAT THIS IS NOT. It is not a replacement for the seven tables Phase 6
// creates natively in shell_stringtables.cpp. Those stay exactly where they
// are and the g_pStringTable* globals keep pointing at them, because 141
// native reads across this tree depend on them -- shell_te_fwd_s4.cpp reads
// g_pStringTableEffectDispatch on the temp-entity path (the message of the
// day wrote g_pStringTableInfoPanel until it retired, 2026-09-29) -- and every one of those reads is a
// dereference that a "port" of the seven would have to keep answering anyway.
// This is ADDITIVE: it is the route by which a MOD can declare a table of its
// own, which is what the IManagedRuntimeServer::CreateNetworkStringTables slot
// was reserved for and what Part 13 made conditional on somebody asking.
//
// ENGINE-FREE, and that is what makes it testable. networkstringtabledefs.h
// includes nothing and depends on nothing: INetworkStringTable and
// INetworkStringTableContainer are pure abstract classes over int and char*.
// So the bodies below can be compiled into a gtest against a fake container
// and the SHIPPED bodies are what runs there -- the same seam
// shell_gamedll_refusals.h and managed_runtime_server.h use, and for the same
// reason. shell_stringtables.cpp includes cbase.h's whole world and can never
// be linked into a test binary.
//
// NO STATE, AT ALL. The container is a constructor argument and the only
// field; there is nothing to remember between calls, because the ENGINE
// remembers the tables. The six entry points in shell_stringtables.cpp build
// one of these on the stack per call. That is the project rule (state lives in
// a class as a field) met by there being no state to place.
//
// THE HANDLE IS A TABLEID, NOT A POINTER. TABLEID is an int the engine already
// assigns to every table (INetworkStringTable::GetTableId) and can resolve
// back (INetworkStringTableContainer::GetTable). Crossing an
// INetworkStringTable* into managed code would put a raw engine pointer in a
// C# field, where it survives a hot reload as a stale address that still looks
// like a number; a TABLEID that no longer resolves answers NULL and is
// refused. The two failure modes are a segfault and a -1.
//=============================================================================//

#pragma once

#include "networkstringtabledefs.h"

//-----------------------------------------------------------------------------
// The console half. Declared, not defined -- see the header comment: tier0's
// Warning() in shell_stringtables.cpp, a recorder in the test.
//
// EVERY REFUSAL IS LOUD. A managed caller gets -1 back, and -1 handed to a mod
// that did not check it is an index that will be used as one. plan_server.md
// Part 5's rule again: a route that silently answers nothing and a route that
// works look identical from outside.
//-----------------------------------------------------------------------------
void ShellStringTables_Warning( const char *pMessage );

//-----------------------------------------------------------------------------
// The six operations, over one container.
//-----------------------------------------------------------------------------
class CShellStringTables
{
public:
	explicit CShellStringTables( INetworkStringTableContainer *pContainer )
		: m_pContainer( pContainer )
	{
	}

	//-------------------------------------------------------------------------
	// Create a table, or answer the one that is already there.
	//
	// THE ALREADY-THERE CASE IS THE HOT-RELOAD CASE AND IT IS THE COMMON ONE.
	// cs_sv_reload tears the managed ALC down and builds it again; every
	// TABLEID a mod was holding goes with it. The ENGINE's tables do not go --
	// IServerGameDLL::CreateNetworkStringTables is called once per map load,
	// not once per managed reload -- so the mod's Initialize asks for its table
	// again and the name is already taken. Answering the existing id is the
	// only answer that is not a lie, and it is what the caller wanted.
	//
	// It WARNS as well as answering, because the other way to reach this line
	// is two different pieces of mod code both claiming one name, and that is a
	// real defect whose symptom would otherwise be one of them silently reading
	// the other's strings.
	//
	// nMaxEntries is ignored on that path, deliberately and out loud: the
	// engine sizes a table when it creates it and there is no resize.
	//-------------------------------------------------------------------------
	int Create( const char *pszName, int nMaxEntries )
	{
		if ( m_pContainer == NULL || pszName == NULL || pszName[ 0 ] == '\0' )
		{
			ShellStringTables_Warning( "[SourceSharp] string table create: no container or no name; refused\n" );
			return INVALID_STRING_TABLE;
		}

		INetworkStringTable *pExisting = m_pContainer->FindTable( pszName );

		if ( pExisting != NULL )
		{
			Warn2( "[SourceSharp] string table '", pszName,
			       "' already exists; answering the existing table rather than creating a second. "
			       "If two pieces of mod code claim this name, one of them is reading the other's strings.\n" );
			return pExisting->GetTableId();
		}

		if ( nMaxEntries <= 0 )
		{
			Warn2( "[SourceSharp] string table '", pszName, "': maxEntries must be positive; refused\n" );
			return INVALID_STRING_TABLE;
		}

		INetworkStringTable *pTable = m_pContainer->CreateStringTable( pszName, nMaxEntries );

		if ( pTable == NULL )
		{
			// The engine refuses a create outside the window
			// IServerGameDLL::CreateNetworkStringTables opens. That is the
			// whole reason the managed slot is called from there and from
			// nowhere else.
			Warn2( "[SourceSharp] string table '", pszName,
			       "': the engine refused to create it. Tables may only be created from "
			       "IServerModule's CreateNetworkStringTables, which is the one moment the "
			       "engine allows it.\n" );
			return INVALID_STRING_TABLE;
		}

		return pTable->GetTableId();
	}

	//-------------------------------------------------------------------------
	// The id of an existing table, by name. -1 when there is none.
	//
	// NOT A CONVENIENCE. This is how a mod gets its handle back after a hot
	// reload without asking for a create the engine would refuse, and it is how
	// managed code reads one of the seven native tables (Materials, say)
	// without this ABI having to expose seven more slots.
	//-------------------------------------------------------------------------
	int Find( const char *pszName ) const
	{
		if ( m_pContainer == NULL || pszName == NULL || pszName[ 0 ] == '\0' )
			return INVALID_STRING_TABLE;

		INetworkStringTable *pTable = m_pContainer->FindTable( pszName );

		return ( pTable != NULL ) ? pTable->GetTableId() : INVALID_STRING_TABLE;
	}

	//-------------------------------------------------------------------------
	// Add a string. Answers its index -- the one it already had if the table
	// held it, which is the engine's own behaviour and not a special case here.
	//
	// bIsServer is TRUE and is not a parameter. This ABI is server-only: the
	// slots are filled inside #ifndef CLIENT_DLL and left null on the client,
	// and a client-side add to a server table is the desync this whole
	// mechanism exists on the other side of.
	//-------------------------------------------------------------------------
	int AddString( int nTableId, const char *pszValue )
	{
		INetworkStringTable *pTable = Resolve( nTableId, "add a string to" );

		if ( pTable == NULL )
			return -1;

		if ( pszValue == NULL )
		{
			ShellStringTables_Warning( "[SourceSharp] string table add: null string; refused\n" );
			return -1;
		}

		int nIndex = pTable->AddString( true, pszValue );

		if ( nIndex == (int)INVALID_STRING_INDEX )
		{
			Warn2( "[SourceSharp] string table '", pTable->GetTableName(),
			       "' is full; the string was not added\n" );
			return -1;
		}

		return nIndex;
	}

	//-------------------------------------------------------------------------
	// The index of a string, or -1.
	//
	// -1 AND NOT INVALID_STRING_INDEX. That constant is an unsigned short of
	// 0xFFFF, which crosses this boundary as 65535 -- a plausible index into a
	// table whose max is 65536, and a positive number that every "if (i >= 0)"
	// in C# reads as success. Every miss on this ABI is -1.
	//-------------------------------------------------------------------------
	int FindStringIndex( int nTableId, const char *pszValue )
	{
		INetworkStringTable *pTable = Resolve( nTableId, "look a string up in" );

		if ( pTable == NULL || pszValue == NULL )
			return -1;

		int nIndex = pTable->FindStringIndex( pszValue );

		return ( nIndex == (int)INVALID_STRING_INDEX ) ? -1 : nIndex;
	}

	//-------------------------------------------------------------------------
	// One string, into the CALLER's buffer.
	//
	// The three string answers' convention (ServerBrowserMapOverride and
	// friends, Phase 5): write into the buffer, return the byte count WITHOUT
	// the terminator, -1 for no answer. An answer that does not fit is refused
	// rather than truncated -- a truncated string table entry is a material
	// path or a scene name that names a different file.
	//-------------------------------------------------------------------------
	int GetString( int nTableId, int nStringIndex, char *pOut, int nOutSize )
	{
		if ( pOut == NULL || nOutSize <= 0 )
			return -1;

		pOut[ 0 ] = '\0';

		INetworkStringTable *pTable = Resolve( nTableId, "read a string from" );

		if ( pTable == NULL )
			return -1;

		if ( nStringIndex < 0 || nStringIndex >= pTable->GetNumStrings() )
			return -1;

		const char *pszValue = pTable->GetString( nStringIndex );

		if ( pszValue == NULL )
			return -1;

		int nLen = 0;
		while ( pszValue[ nLen ] != '\0' )
			nLen++;

		if ( nLen + 1 > nOutSize )
		{
			Warn2( "[SourceSharp] string table '", pTable->GetTableName(),
			       "': an entry does not fit the caller's buffer and was NOT truncated\n" );
			return -1;
		}

		for ( int i = 0; i <= nLen; i++ )
			pOut[ i ] = pszValue[ i ];

		return nLen;
	}

	//-------------------------------------------------------------------------
	// How many strings a table holds, or -1 for a table that is not there.
	//
	// -1 AND NOT 0. "the table does not exist" and "the table is empty" are
	// different answers, and collapsing them makes a mod that asked for the
	// wrong name iterate zero entries and conclude its table is empty.
	//-------------------------------------------------------------------------
	int GetNumStrings( int nTableId )
	{
		INetworkStringTable *pTable = Resolve( nTableId, "count the strings in" );

		return ( pTable == NULL ) ? -1 : pTable->GetNumStrings();
	}

private:
	//-------------------------------------------------------------------------
	// A TABLEID back to a table, LOUDLY. A stale id is the failure mode a hot
	// reload produces and it is the one worth naming: the number still looks
	// like a table.
	//-------------------------------------------------------------------------
	INetworkStringTable *Resolve( int nTableId, const char *pWhat )
	{
		if ( m_pContainer == NULL )
		{
			ShellStringTables_Warning( "[SourceSharp] string table: no container; refused\n" );
			return NULL;
		}

		if ( nTableId == INVALID_STRING_TABLE )
			return NULL;

		INetworkStringTable *pTable = m_pContainer->GetTable( nTableId );

		if ( pTable == NULL )
		{
			Warn2( "[SourceSharp] string table: cannot ", pWhat,
			       " a table id the engine does not know. A id kept across a level change or a "
			       "managed reload is stale -- ask for it again by name.\n" );
		}

		return pTable;
	}

	//-------------------------------------------------------------------------
	// Three pieces, one line. There is no Warning( fmt, ... ) here on purpose:
	// the header is engine-free, so it has no tier0 and no printf, and a
	// variadic of its own would be a second formatter to keep in agreement with
	// the one the shell already has.
	//-------------------------------------------------------------------------
	void Warn2( const char *pA, const char *pB, const char *pC )
	{
		enum { kMax = 512 };
		char buf[ kMax ];
		int n = 0;

		Append( buf, n, kMax, pA );
		Append( buf, n, kMax, pB );
		Append( buf, n, kMax, pC );

		buf[ n ] = '\0';
		ShellStringTables_Warning( buf );
	}

	static void Append( char *pBuf, int &n, int nMax, const char *pText )
	{
		if ( pText == NULL )
			return;

		while ( *pText != '\0' && n < nMax - 1 )
			pBuf[ n++ ] = *pText++;
	}

	INetworkStringTableContainer *m_pContainer;
};
