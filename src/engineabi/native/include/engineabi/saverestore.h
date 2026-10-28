//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the save/restore records the engine hands the game DLL
//
// plan_server_novalve.md 3.1 / P5 step 1: replaces public/saverestoretypes.h for
// server.so's own translation units. The engine allocates the save block, the
// game DLL constructs a CSaveRestoreData in it (IServerGameDLL::SaveInit), and
// from then on BOTH sides read and write it by field: the engine walks levelInfo
// and the entity table, the game DLL fills the buffer and the symbol table. So
// these records keep their Valve names and their exact layout, which
// tools/abi-parity (K2, docs/abi-parity.tsv) compares against upstream's header
// compiled as the engine compiles it (no GAME_DLL).
//
// What is NOT carried over, and why:
//   * The entity table's handle is the engine's view, EHandlePlaceholder_t: the
//     game-side EHANDLE view needs ehandle.h, and nothing in server.so reads it.
//   * CGameSaveRestoreInfo's entity-to-index hash is held as storage of its exact
//     size and alignment, zeroed, not as a CUtlHash. Upstream builds it with
//     1024 buckets at construction; its only readers are BuildEntityHash and
//     GetEntityIndex, which upstream compiles only for the game DLL with
//     ehandle.h seen, and server.so writes no entity table. The engine's
//     PurgeEntityHash on a zeroed hash frees nothing. Constructing it here
//     allocated through g_pMemAlloc, in a block the engine then owns.
//   * Only the members server.so calls are declared. The method bodies are
//     written for this file; the symbol hash is bit-for-bit upstream's, because
//     the engine resolves the same tokens in the same table
//     (saverestore_symbol_oracle_test holds it to upstream).
//=============================================================================//
#pragma once

#include "const.h" // MAX_MAP_NAME_SAVE
#include "string_t.h"
#include "mathlib/vector.h"

struct edict_t;
class IGameEntity;

//-----------------------------------------------------------------------------
// The save buffer and its token table.
//-----------------------------------------------------------------------------
class CSaveRestoreSegment
{
public:
	CSaveRestoreSegment()
		: pBaseData( nullptr )
		, pCurrentData( nullptr )
		, size( 0 )
		, bufferSize( 0 )
		, tokenCount( 0 )
		, pTokens( nullptr )
	{
	}

	// The buffer.
	void Init( void *pNewBase, int nBytes )
	{
		pBaseData = static_cast<char *>( pNewBase );
		pCurrentData = pBaseData;
		size = 0;
		bufferSize = nBytes;
	}

	int BytesAvailable() const { return bufferSize - size; }

	int SizeBuffer() const { return bufferSize; }

	int GetCurPos() { return size; }

	char *AccessCurPos() { return pCurrentData; }

	void MoveCurPos( int nBytes )
	{
		pCurrentData += nBytes;
		size += nBytes;
	}

	// The token table: an open-addressed hash of string pointers the engine
	// shares, so the slot a token lands in is part of the save format.
	void InitSymbolTable( char **pNewTokens, int nTokens )
	{
		tokenCount = nTokens;
		pTokens = pNewTokens;
		memset( pTokens, 0, sizeof( *pTokens ) * nTokens );
	}

	int SizeSymbolTable() { return tokenCount; }

	// The slot holding pszToken, claiming the first empty one on the probe
	// sequence when it is new. 0 when the table is full (upstream's answer, with
	// a warning there).
	unsigned short FindCreateSymbol( const char *pszToken )
	{
		const unsigned int start = HashString( pszToken ) % static_cast<unsigned int>( tokenCount );
		for ( int probe = 0; probe < tokenCount; probe++ )
		{
			const int slot = static_cast<int>( ( start + probe ) % static_cast<unsigned int>( tokenCount ) );
			if ( pTokens[slot] == nullptr || strcmp( pTokens[slot], pszToken ) == 0 )
			{
				pTokens[slot] = const_cast<char *>( pszToken );
				return static_cast<unsigned short>( slot );
			}
		}
		return 0;
	}

	const char *StringFromSymbol( int token )
	{
		return ( token >= 0 && token < tokenCount ) ? pTokens[token] : "<<illegal>>";
	}

	// Rotate right by 4, xor in each character as a SIGNED char widened to
	// unsigned (so bytes >= 0x80 set the high bits), as upstream's _rotr loop.
	static unsigned int HashString( const char *pszToken )
	{
		unsigned int hash = 0;
		for ( ; *pszToken; pszToken++ )
		{
			hash = ( hash >> 4 ) | ( hash << 28 );
			hash ^= static_cast<unsigned int>( static_cast<int>( static_cast<signed char>( *pszToken ) ) );
		}
		return hash;
	}

private:
	char *pBaseData; // start of the save data
	char *pCurrentData; // the next byte to read or write
	int size; // pCurrentData - pBaseData
	int bufferSize; // bytes in the buffer
	int tokenCount; // slots in pTokens
	char **pTokens; // the token table (sparse)
};

//-----------------------------------------------------------------------------
// The level records the engine walks.
//-----------------------------------------------------------------------------
struct levellist_t
{
	char mapName[MAX_MAP_NAME_SAVE];
	char landmarkName[32];
	edict_t *pentLandmark;
	Vector vecLandmarkOrigin;
};

#define MAX_LEVEL_CONNECTIONS 16 // encoded in the low 16 bits of entitytable_t::flags

// The engine's spelling of an entity handle: it cannot see ehandle.h.
struct EHandlePlaceholder_t
{
	uint32_t i;
};

struct entitytable_t
{
	int id; // ordinal, for entity <-> pointer conversions
	int edictindex; // the edict an entity must be restored into (players, world)
	int saveentityindex; // the entity index at save time
	int restoreentityindex; // the entity index given at restore time
	EHandlePlaceholder_t hEnt;
	int location; // offset of the entity's data from the base
	int size; // bytes of the entity's data
	int flags; // FENTTABLE_*, and the level-transition mask
	string_t classname;
	string_t globalname;
	Vector landmarkModelSpace;
	string_t modelname;
};

#define FENTTABLE_PLAYER 0x80000000
#define FENTTABLE_REMOVED 0x40000000
#define FENTTABLE_MOVEABLE 0x20000000
#define FENTTABLE_GLOBAL 0x10000000
#define FENTTABLE_PLAYERCHILD 0x08000000
#define FENTTABLE_LEVELMASK 0x0000FFFF

struct saverestorelevelinfo_t
{
	int connectionCount; // entries used in levelList
	levellist_t levelList[MAX_LEVEL_CONNECTIONS];
	int fUseLandmark;
	char szLandmarkName[20];
	Vector vecLandmarkOffset;
	float time;
	char szCurrentMapName[MAX_MAP_NAME_SAVE];
	int mapVersion;
};

//-----------------------------------------------------------------------------
// The game's half of the save block.
//-----------------------------------------------------------------------------
class CGameSaveRestoreInfo
{
public:
	CGameSaveRestoreInfo()
		: tableCount( 0 )
		, pTable( nullptr )
		, m_pCurrentEntity( nullptr )
	{
		// Zeroed as bytes, as upstream does: value-initialising would run
		// Vector's constructor instead.
		memset( static_cast<void *>( &levelInfo ), 0, sizeof( levelInfo ) );
		modelSpaceOffset.Init( 0, 0, 0 );
		memset( m_EntityToIndex, 0, sizeof( m_EntityToIndex ) );
	}

	float GetBaseTime() const { return levelInfo.time; }

	saverestorelevelinfo_t levelInfo;
	Vector modelSpaceOffset; // for global brush entities modelled in other coordinate systems

private:
	// Held for the engine, which reads them; server.so writes no entity table.
	// [[maybe_unused]] on clang only: gcc 10 (CI) ignores it on a member, warning.
#if defined( __clang__ )
#define SS_HELD [[maybe_unused]]
#else
#define SS_HELD
#endif
	SS_HELD int tableCount; // entries in pTable
	SS_HELD entitytable_t *pTable; // one per entity
	SS_HELD IGameEntity *m_pCurrentEntity; // the entity being saved, during its save only
#undef SS_HELD
	// Upstream's CUtlHash< CHashElement, ... >: the engine's PurgeEntityHash is
	// the only touch it gets on server.so's block, and a zeroed one is empty.
	alignas( 8 ) unsigned char m_EntityToIndex[40];
};

class CSaveRestoreData : public CSaveRestoreSegment, public CGameSaveRestoreInfo
{
public:
	CSaveRestoreData()
		: bAsync( false )
	{
	}

	bool bAsync;
};

inline CSaveRestoreData *MakeSaveRestoreData( void *pMemory )
{
	return new ( pMemory ) CSaveRestoreData;
}
