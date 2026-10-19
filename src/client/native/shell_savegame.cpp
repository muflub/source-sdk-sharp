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
// CLIENT.SO'S ALONE since plan_server_native_v2 T4c: the server's half of
// this file is the managed SaveGameSession over SaveBlockCodec
// (src/server/interop), which writes the same records -- see shell_savegame.h
// on why the format is CSave's, measured off a segfault rather than chosen.
// The client's IBaseClientDLL cluster (its decal and music blocks) still
// encodes here, through IVEngineClient's SaveAllocMemory.
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

#include "cbase.h"
#include "cdll_int.h"

#include "datamap.h"
#include "saverestoretypes.h"

#include "sourcesharp.h"
#include "shell_savegame.h"
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
