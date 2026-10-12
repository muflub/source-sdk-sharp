//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//====== SourceSharp — the save buffer, on the engine's side of the fence ====//
// SS_REASON f  a bit-level wire codec
//
// shell_savegame.h is the record LAYOUT and has no engine in it. This file is
// the part that does: it allocates the buffer through the engine's own
// allocator, places a CSaveRestoreData in it, and drives the layout over the
// segment's cursor and symbol table.
//
// ONE FILE, BOTH SIDES. The server's IServerGameDLL cluster and the client's
// IBaseClientDLL cluster are the same fourteen questions asked twice, and the
// only difference at this level is which interface carries SaveAllocMemory. A
// second copy would be a second place for the wire format to drift, and the
// format is not ours -- see shell_savegame.h on why it is CSave's, measured off
// a segfault rather than chosen.
//
// WHY IT IS A SEPARATE TRANSLATION UNIT from shell_gamedll.cpp.
// public/saverestoretypes.h does not compile without cbase.h on the server:
// `entitytable_t::hEnt` is a CBaseEntity-typed handle when ehandle.h's include
// guard has been seen and an
// `EHandlePlaceholder_t` when it has not, and CGameSaveRestoreInfo::
// BuildEntityHash calls `.Get()` on it under `#ifdef GAME_DLL`.
// shell_gamedll.cpp deliberately has no cbase.h -- Phase 7a's whole point is
// that the shell is the game DLL without being the game -- so it keeps holding
// `CSaveRestoreData` as the incomplete type eiface.h forward-declares, and
// every dereference is here.
//
// WHAT STAGE 1 DELIBERATELY DOES NOT DO, so that a later reader does not take
// its absence for an oversight:
//
//   * NO ENTITY TABLE. `tableCount` stays 0. docs/managed-saverestore.md §2d
//     enumerated every engine dereference of the table -- five, touching four
//     fields -- and every loop over it degenerates at zero. The table is the
//     entity-pointer namespace for FIELD_EHANDLE / FIELD_CLASSPTR / FIELD_EDICT
//     inside a DATAMAP, and this port's entities do not go through a datamap:
//     they are SourceSharp.Abi's SavedEntityCodec, which carries its own
//     references. Writing no entity data is the other half's job, not this one's.
//
//   * NOTHING IS FREED HERE. The engine owns the lifetime -- it frees the save
//     data when the save or load finishes, which is why stock's ::SaveInit has
//     no matching ::SaveExit either. The one SaveFreeMemory below is on the
//     failure path, where the engine never sees the pointer to free it.
//
//=============================================================================//

#ifdef CLIENT_DLL
#include "cbase.h"
#include "cdll_int.h"
#else
#include "eiface.h"
#endif

#include "datamap.h"
#ifdef CLIENT_DLL
#include "saverestoretypes.h"
#else
// The engine's view of the save block, re-declared for server.so and held to
// upstream's layout by tools/abi-parity (plan_server_novalve.md P5 wave 9).
#include "engineabi/saverestore.h"
#endif

#include "sourcesharp.h"
#include "shell_savegame.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E4: the server's IManagedSaveRestore
#include "ss_string.h"


namespace
{

//-----------------------------------------------------------------------------
// The accessor shell_savegame.h's templates walk, over a typedescription_t
// array.
//
// This is the ONLY place that knows a save field is a `typedescription_t`. The
// codec above it takes a name and three integers per field and could as easily
// be handed a managed description, which is what keeps its tests free of an
// engine.
//-----------------------------------------------------------------------------
class CTypeDescriptionFields
{
public:
	explicit CTypeDescriptionFields( const typedescription_t *pFields )
		: m_pFields( pFields )
	{
	}

	int TypeAt( int i ) const   { return (int)m_pFields[ i ].fieldType; }
	int SizeAt( int i ) const   { return (int)m_pFields[ i ].fieldSize; }
	int OffsetAt( int i ) const { return m_pFields[ i ].fieldOffset[ TD_OFFSET_NORMAL ]; }

	// NEVER NULL, because it becomes a symbol-table key on the way out and the
	// engine resolves it back by name on the way in. An unnamed field would
	// intern an empty token and match every other unnamed one.
	const char *NameAt( int i ) const
	{
		return m_pFields[ i ].fieldName ? m_pFields[ i ].fieldName : "<unnamed>";
	}

private:
	const typedescription_t *m_pFields;
};

//-----------------------------------------------------------------------------
// The save's own symbol table, behind the codec's interface.
//
// Both methods are CSaveRestoreSegment's, with ONE difference: StringFromSymbol
// asserts on an out-of-range token and this answers NULL. That token comes off
// a FILE -- it is the two bytes in front of every record -- so it is hostile
// input, and an assert is not a way to handle hostile input in a shipped build.
//-----------------------------------------------------------------------------
class CSegmentSymbols : public IShellSaveSymbols
{
public:
	explicit CSegmentSymbols( CSaveRestoreData *pSave ) : m_pSave( pSave ) {}

	virtual unsigned short FindCreate( const char *pszToken )
	{
		return m_pSave->FindCreateSymbol( pszToken );
	}

	virtual const char *FromSymbol( int nToken )
	{
		if ( nToken < 0 || nToken >= m_pSave->SizeSymbolTable() )
			return NULL;

		return m_pSave->StringFromSymbol( nToken );
	}

private:
	CSaveRestoreData *m_pSave;
};

} // namespace

//-----------------------------------------------------------------------------
// SaveInit's body.
//
// Stock's ::SaveInit (game/shared/saverestore.cpp:3100) is the shape, with the
// entity table taken out and one bug not carried: stock frees `pSaveMemory`
// when the TOKEN allocation fails, by which point that variable already holds
// the null the failure returned, so the buffer it meant to release leaks. The
// buffer pointer is kept in its own variable here.
//-----------------------------------------------------------------------------
CSaveRestoreData *ShellSave_Init( ShellSaveEngine_t *pEngine, int nSize, float flCurTime )
{
	if ( pEngine == NULL )
	{
		Warning( "[SourceSharp] SaveInit: no engine interface -- the save cannot be sized "
		         "and will abort SILENTLY at the engine's call site\n" );
		return NULL;
	}

	// THE ENGINE PASSES 0 AND WE CHOOSE -- docs/managed-saverestore.md §4a's
	// correction. A caller that does pass a size gets it, which costs nothing
	// and keeps this honest if a later engine starts having an opinion.
	if ( nSize <= 0 )
		nSize = SOURCESHARP_SAVE_BUFFER_BYTES;

	void *pBuffer = pEngine->SaveAllocMemory( sizeof( CSaveRestoreData ) + nSize, sizeof( char ) );

	if ( pBuffer == NULL )
	{
		Warning( "[SourceSharp] SaveInit: SaveAllocMemory refused %d bytes -- returning NULL, "
		         "which aborts the whole save with no file and no further message\n",
		         (int)( sizeof( CSaveRestoreData ) + nSize ) );
		return NULL;
	}

	CSaveRestoreData *pSaveData = MakeSaveRestoreData( pBuffer );
	pSaveData->Init( (char *)( pSaveData + 1 ), nSize );	// skip the save structure

	void *pTokens = pEngine->SaveAllocMemory( SOURCESHARP_SAVE_TOKEN_COUNT, sizeof( char * ) );

	if ( pTokens == NULL )
	{
		Warning( "[SourceSharp] SaveInit: SaveAllocMemory refused the %d-entry token table -- "
		         "returning NULL, which aborts the whole save with no file and no further "
		         "message\n", (int)SOURCESHARP_SAVE_TOKEN_COUNT );
		pEngine->SaveFreeMemory( pBuffer );
		return NULL;
	}

	pSaveData->InitSymbolTable( (char **)pTokens, SOURCESHARP_SAVE_TOKEN_COUNT );

	// NO InitEntityTable CALL, and that is the scope reduction rather than an
	// omission -- see the file header. CGameSaveRestoreInfo's constructor has
	// already left pTable NULL and tableCount 0, which is the state §2d
	// measured the engine tolerating on every path it has.

	pSaveData->levelInfo.time = flCurTime;
	pSaveData->levelInfo.vecLandmarkOffset = vec3_origin;
	pSaveData->levelInfo.fUseLandmark = false;

	// ZERO, AND THE ENGINE HAS NO UPPER BOUND OF ITS OWN -- §4a's second
	// constraint. `levelList` is levellist_t[MAX_LEVEL_CONNECTIONS], the engine
	// loops `connectionCount` ADJACENCY blocks with no cap, and a game
	// answering more than sixteen walks off the end of that array. Stage 1
	// builds no adjacency list at all, so zero is both correct and the only
	// value this file ever writes.
	pSaveData->levelInfo.connectionCount = 0;

	return pSaveData;
}

//-----------------------------------------------------------------------------
bool ShellSave_WriteFields( CSaveRestoreData *pSave, const char *pszName, const void *pBase,
                            const typedescription_t *pFields, int nCount )
{
	if ( pSave == NULL || pszName == NULL || pBase == NULL || pFields == NULL )
	{
		Warning( "[SourceSharp] SaveWriteFields: refused '%s' -- a null argument\n",
		         pszName ? pszName : "<null>" );
		return false;
	}

	const CTypeDescriptionFields fields( pFields );
	CSegmentSymbols symbols( pSave );
	CShellSaveCursor cursor( pSave->AccessCurPos(), pSave->BytesAvailable() );

	int nOmitted = 0;

	const int nWritten = ShellSave_WriteFieldBlock( cursor, symbols, pszName, pBase,
	                                                fields, nCount, pSave->GetBaseTime(),
	                                                &nOmitted, NULL, NULL );

	if ( nWritten < 0 )
	{
		Warning( "[SourceSharp] SaveWriteFields: block '%s' (%d fields) did not fit -- "
		         "%d bytes left in a %d-byte buffer. The block is NOT in the save.\n",
		         pszName, nCount, pSave->BytesAvailable(), pSave->SizeBuffer() );
		return false;
	}

	if ( nOmitted > 0 )
	{
		// LOUD, AND NOT A REFUSAL OF THE BLOCK -- see shell_savegame.h. Each
		// record carries its own name and width, so a field this build cannot
		// encode is a field the reader never sees rather than an offset error;
		// refusing the whole block would take it out of a stream the engine
		// counts.
		//
		// EVERY OMITTED FIELD IS NAMED, not just the first, and that is a
		// measurement decision rather than thoroughness for its own sake. The
		// first live run of this warning said "'position' is fieldType 15" on a
		// block short THREE of five -- which is a message that names one third
		// of what a reader has to implement, and sends whoever acts on it back
		// for a second run to learn the rest.
		char szOmitted[ 256 ];
		szOmitted[ 0 ] = 0;

		for ( int i = 0; i < nCount; i++ )
		{
			if ( SaveField_Bytes( (int)pFields[ i ].fieldType, (int)pFields[ i ].fieldSize ) >= 0 )
				continue;

			char szOne[ 64 ];
			ss::Format( szOne, sizeof( szOne ), "%s%s(type %d)",
			            szOmitted[ 0 ] ? ", " : "",
			            pFields[ i ].fieldName ? pFields[ i ].fieldName : "<unnamed>",
			            (int)pFields[ i ].fieldType );
			ss::StrAppend( szOmitted, szOne, sizeof( szOmitted ), COPY_ALL_CHARACTERS );
		}

		Warning( "[SourceSharp] SaveWriteFields: block '%s' is short %d of %d field(s) -- "
		         "%s. This build does not encode those types (save_field_codec.h); "
		         "everything else in the block was written.\n",
		         pszName, nOmitted, nCount, szOmitted );
	}

	pSave->MoveCurPos( nWritten );
	return true;
}

//-----------------------------------------------------------------------------
bool ShellSave_ReadFields( CSaveRestoreData *pSave, const char *pszName, void *pBase,
                           const typedescription_t *pFields, int nCount )
{
	if ( pSave == NULL || pszName == NULL || pBase == NULL || pFields == NULL )
	{
		Warning( "[SourceSharp] SaveReadFields: refused '%s' -- a null argument\n",
		         pszName ? pszName : "<null>" );
		return false;
	}

	const CTypeDescriptionFields fields( pFields );
	CSegmentSymbols symbols( pSave );
	CShellSaveCursor cursor( pSave->AccessCurPos(), pSave->BytesAvailable() );

	const char *pszRefusal = NULL;

	const int nRead = ShellSave_ReadFieldBlock( cursor, symbols, pszName, pBase,
	                                            fields, nCount, pSave->GetBaseTime(),
	                                            &pszRefusal );

	if ( nRead < 0 )
	{
		Warning( "[SourceSharp] SaveReadFields: REFUSED block '%s' (%d fields) -- %s. "
		         "The struct is left as the engine gave it rather than half-filled "
		         "(cursor %d, %d bytes of save data left)\n",
		         pszName, nCount, pszRefusal ? pszRefusal : "no reason given",
		         pSave->GetCurPos(), pSave->BytesAvailable() );
		return false;
	}

	pSave->MoveCurPos( nRead );
	return true;
}

//-----------------------------------------------------------------------------
// The readouts.
//-----------------------------------------------------------------------------
int ShellSave_CurPos( CSaveRestoreData *pSave )
{
	return pSave ? pSave->GetCurPos() : -1;
}

int ShellSave_BytesAvailable( CSaveRestoreData *pSave )
{
	return pSave ? pSave->BytesAvailable() : -1;
}

int ShellSave_SymbolTableSize( CSaveRestoreData *pSave )
{
	return pSave ? pSave->SizeSymbolTable() : -1;
}

int ShellSave_SymbolsDefined( CSaveRestoreData *pSave )
{
	if ( pSave == NULL )
		return -1;

	const int nSlots = pSave->SizeSymbolTable();
	int nDefined = 0;

	for ( int i = 0; i < nSlots; i++ )
	{
		if ( pSave->StringFromSymbol( i ) != NULL )
			nDefined++;
	}

	return nDefined;
}

//-----------------------------------------------------------------------------
// THE BLOB BLOCK, on the engine's side.
//
// shell_savegame.h has the layout and the two passes; these two are the pair of
// lines that put the segment's cursor and symbol table under them, exactly as
// ShellSave_WriteFields and ShellSave_ReadFields do for a field block.
//-----------------------------------------------------------------------------
int ShellSave_WriteBlob( CSaveRestoreData *pSave, const char *pszName,
                         const void *pData, int nBytes )
{
	if ( pSave == NULL || pszName == NULL || nBytes < 0 )
	{
		Warning( "[SourceSharp] save: refused blob block '%s' -- a null argument\n",
		         pszName ? pszName : "<null>" );
		return -1;
	}

	if ( nBytes == 0 )
		return 0;

	CSegmentSymbols symbols( pSave );
	CShellSaveCursor cursor( pSave->AccessCurPos(), pSave->BytesAvailable() );

	const int nWritten = ShellSave_WriteBlobBlock( cursor, symbols, pszName, pData, nBytes );

	if ( nWritten < 0 )
	{
		Warning( "[SourceSharp] save: blob block '%s' (%d payload bytes) did not fit -- "
		         "%d bytes left in a %d-byte buffer. The block is NOT in the save, and "
		         "neither is anything after it.\n",
		         pszName, nBytes, pSave->BytesAvailable(), pSave->SizeBuffer() );
		return -1;
	}

	pSave->MoveCurPos( nWritten );
	return nWritten;
}

int ShellSave_ReadBlob( CSaveRestoreData *pSave, const char *pszName,
                        void *pOut, int nMax )
{
	if ( pSave == NULL || pszName == NULL )
		return -1;

	const int nBefore = pSave->GetCurPos();

	CSegmentSymbols symbols( pSave );
	CShellSaveCursor cursor( pSave->AccessCurPos(), pSave->BytesAvailable() );

	const char *pszRefusal = NULL;
	const int nPayload = ShellSave_ReadBlobBlock( cursor, symbols, pszName, pOut, nMax,
	                                              &pszRefusal );

	if ( nPayload == -1 )
	{
		Warning( "[SourceSharp] load: REFUSED blob block '%s' -- %s. Nothing was read "
		         "(cursor %d, %d bytes of save data left)\n",
		         pszName, pszRefusal ? pszRefusal : "no reason given",
		         nBefore, pSave->BytesAvailable() );
		return -1;
	}

	if ( nPayload == kShellSaveNoBlock )
		return kShellSaveNoBlock;

	// The cursor moved only if the copy actually happened -- ReadBlobBlock's
	// contract. Asking the cursor rather than recomputing it from nPayload is
	// what makes the sizing call and the filling call one line here.
	pSave->MoveCurPos( cursor.Pos() );
	return nPayload;
}

#ifndef CLIENT_DLL

//-----------------------------------------------------------------------------
// THE MANAGED HALF: the server's IManagedSaveRestore (E4), asked afresh at each
// call since a reload replaces it. Server only: the client's arm here sat inside
// this #ifndef and never compiled; IManagedSaveRestoreClient (CE7) keeps the slots.
//-----------------------------------------------------------------------------
namespace
{
struct CManagedSaveSide
{
	IManagedSaveRestore *pArea = ManagedSaveRestore();

	bool CanCollect() const { return pArea != NULL; }
	bool CanRestore() const { return pArea != NULL; }
	int32_t Collect( char *pBuffer, int32_t nBytes ) const { return pArea->SaveCollectEntities( pBuffer, nBytes ); }
	int32_t Restore( const char *pBuffer, int32_t nBytes ) const { return pArea->SaveRestoreEntities( pBuffer, nBytes ); }
};
} // namespace

bool ShellSave_WriteManagedEntities( CSaveRestoreData *pSave )
{
	const CManagedSaveSide side;

	if ( pSave == NULL || !side.CanCollect() )
	{
		// A build with no managed side is a build with no managed entities. It
		// is not a failed save; there is simply nothing of ours in it.
		Msg( "[SourceSharp][save] Save  -- no managed side; 0 entity bytes\n" );
		return true;
	}

	// SIZE, THEN FILL -- SaveCollectEntities' contract, which is
	// AnimListActivityNames'. It answers the bytes the WHOLE block needs and
	// writes nothing unless every one of them fits, so a short buffer comes
	// back as the required size rather than as a truncated world.
	const int nBytes = (int)side.Collect( NULL, 0 );

	if ( nBytes < 0 )
	{
		// -1 is "the records could not be encoded at all" -- a value too long
		// for the format. It refuses the WHOLE save rather than dropping one
		// record, because a save that silently omits an entity is the failure
		// that survives to the load.
		Warning( "[SourceSharp] save: the managed side refused to encode its entities. "
		         "No entity block is written, and the .sav will restore no managed "
		         "state.\n" );
		return false;
	}

	if ( nBytes == 0 )
	{
		Msg( "[SourceSharp][save] Save  -- 0 managed entity bytes (nothing live)\n" );
		return true;
	}

	// A plain array rather than a CUtlBuffer: the block is written once per
	// save, the size is known exactly before the allocation, and nothing here
	// grows.
	char *pBuffer = new char[ nBytes ];

	const int nFilled = (int)side.Collect( pBuffer, nBytes );

	if ( nFilled != nBytes )
	{
		// The two calls disagreed. That is a managed side whose entity set
		// changed between them, which cannot happen inside one engine call --
		// so it is a bug rather than a race, and it is named rather than
		// papered over by writing whichever number is smaller.
		Warning( "[SourceSharp] save: SaveCollectEntities sized %d bytes and then wrote "
		         "%d. Nothing is written -- the two calls must agree.\n",
		         nBytes, nFilled );
		delete[] pBuffer;
		return false;
	}

	const int nBlock = ShellSave_WriteBlob( pSave, SOURCESHARP_SAVE_ENTITY_BLOCK,
	                                        pBuffer, nBytes );
	delete[] pBuffer;

	if ( nBlock < 0 )
		return false;

	Msg( "[SourceSharp][save] Save  -- managed entity block '%s': %d payload bytes in "
	     "%d block bytes\n", SOURCESHARP_SAVE_ENTITY_BLOCK, nBytes, nBlock );
	return true;
}

int ShellSave_RestoreManagedEntities( CSaveRestoreData *pSave )
{
	const CManagedSaveSide side;

	if ( pSave == NULL || !side.CanRestore() )
		return kShellSaveNoBlock;

	const int nPayload = ShellSave_ReadBlob( pSave, SOURCESHARP_SAVE_ENTITY_BLOCK, NULL, 0 );

	if ( nPayload == kShellSaveNoBlock )
	{
		Msg( "[SourceSharp][save] Restore -- this save carries no managed entity block; "
		     "the map's own state is all there is\n" );
		return kShellSaveNoBlock;
	}

	if ( nPayload < 0 )
		return -1;	// ShellSave_ReadBlob has already named it

	if ( nPayload == 0 )
		return 0;

	char *pBuffer = new char[ nPayload ];

	const int nRead = ShellSave_ReadBlob( pSave, SOURCESHARP_SAVE_ENTITY_BLOCK,
	                                      pBuffer, nPayload );

	if ( nRead != nPayload )
	{
		Warning( "[SourceSharp] load: the managed entity block sized %d bytes and read "
		         "%d. NOTHING has been restored.\n", nPayload, nRead );
		delete[] pBuffer;
		return -1;
	}

	const int nRestored = (int)side.Restore( pBuffer, nPayload );
	delete[] pBuffer;

	return nRestored;
}

#endif // !CLIENT_DLL
