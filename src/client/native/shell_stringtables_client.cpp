//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================
// SS_REASON b  an interface resolved by name at load
//
// The CLIENT's half of the mod-declared string-table route --
// plan_gamedll_after.md Phase A8, the owner's ruling after it:
//
//     THE SERVER ONLY CREATES. THE CLIENT ONLY FINDS, AND MAY BE LAZY.
//
// FOUR ENTRY POINTS, NOT SIX, and the two that are missing are the point.
// SourceSharp_StringTableCreate and SourceSharp_StringTableAddString are NOT
// defined here and their slots stay NULL on this side, so a client-side create
// or add is not a thing that can reach the engine at all: Engine.cs guards a
// null slot and answers -1. A client add to a server table is the desync string
// tables exist to prevent, and this is one of the three places it is refused --
// the other two are managed (StringTableRegistry is built writes:false on this
// side, and the handle says so by name before the boundary).
//
// The slots stay NULL rather than being filled with a no-op for the reason the
// LevelInit slots give three hundred lines away in sourcesharp_gamesystem.cpp:
// "a client that could call it and silently do nothing is worse than one where
// the null says so".
//
// THE LOGIC IS SHARED AND IS NOT COPIED. CShellStringTables lives in
// sourcesharp/game/server/shell_stringtables.h, is engine-free, and is what
// sourcesharp/test/stringtable_abi_test.cpp compiles and runs. The header's
// path says "server" because that is where it was written; what it depends on
// is INetworkStringTableContainer, which both sides have. A second copy of
// those bodies for this side is exactly the drift the shared header exists to
// prevent -- and it would be a second copy of the -1-not-INVALID_STRING_INDEX
// rule, which is the one failure on this ABI that looks like data.
//
// THE CONTAINER IS THIS SIDE'S. cdll_client_int.cpp:192 defines its own
// `networkstringtable`, filled from INTERFACENAME_NETWORKSTRINGTABLECLIENT --
// a different object from the server's, holding the client's replicated copies.
// That is why finding by name is the only thing that works here: the client
// never saw the create, and a table arrives when it replicates.
//
// NO FILE-SCOPE STATE. The object is a stack temporary holding one pointer.
//
//=============================================================================

#include "cbase.h"
#include "networkstringtabledefs.h"

// This side's container, declared where the client declares it. The server's
// half reaches the same name through enginecallback.h; the two are DIFFERENT
// objects, which is the whole reason this file exists.
#include "networkstringtable_clientdll.h"

// By path, and NOT copied into this directory. The header is engine-free and
// side-free -- it depends on INetworkStringTableContainer, which both sides
// have -- and its directory says "server" only because that is where it was
// written; the path is this repo's, where CMake compiles the file from. A copy
// here would be a second copy of the -1-not-
// INVALID_STRING_INDEX rule, which is the one failure on this ABI that looks
// like data.
#include "../../server/native/shell_stringtables.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// The console half the engine-free header declares. tier0's Warning, the same
// channel the server's half uses: it needs no frame, no level and no clean
// exit.
//-----------------------------------------------------------------------------
void ShellStringTables_Warning( const char *pMessage )
{
	if ( pMessage != NULL )
		Warning( "%s", pMessage );
}

//-----------------------------------------------------------------------------
// Find, and the three reads. Every body is one line, for the same reason the
// server's are: the part a gtest cannot reach has nothing in it.
//-----------------------------------------------------------------------------
int32_t SourceSharp_StringTableFind( const char *pszName )
{
	return CShellStringTables( networkstringtable ).Find( pszName );
}

int32_t SourceSharp_StringTableFindStringIndex( int32_t nTableId, const char *pszValue )
{
	return CShellStringTables( networkstringtable ).FindStringIndex( nTableId, pszValue );
}

int32_t SourceSharp_StringTableGetString( int32_t nTableId, int32_t nStringIndex, char *pOut, int32_t nOutSize )
{
	return CShellStringTables( networkstringtable ).GetString( nTableId, nStringIndex, pOut, nOutSize );
}

int32_t SourceSharp_StringTableGetNumStrings( int32_t nTableId )
{
	return CShellStringTables( networkstringtable ).GetNumStrings( nTableId );
}
