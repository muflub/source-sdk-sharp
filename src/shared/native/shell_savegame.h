//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// See docs/managed-saverestore.md, "1. What this header is, and why save/restore is still native".

#ifndef SOURCESHARP_SHELL_SAVEGAME_H
#define SOURCESHARP_SHELL_SAVEGAME_H
#ifdef _WIN32
#pragma once
#endif

#include <string.h>
#include <float.h>
#include <math.h>

//=============================================================================
// THE FIELD CODEC, which was save_field_codec.h until plan_shim.md §5 3e.
//
// One header rather than two. This file was its only includer, both are
// engine-free for the same reason, and a reader following a SaveWriteFields
// layout into the encoder that fills it had to open a second file to find four
// inline functions. The prose that argued each of its six field types is in
// docs/managed-saverestore.md now, keyed by section.
//=============================================================================

// See docs/managed-saverestore.md, "12. The engine's own save headers, encoded".


// FLT_MAX, and only FLT_MAX: both time sentinels and vec3_invalid are defined
// in terms of it. Not an engine header -- see the file comment.
#include <float.h>

//-----------------------------------------------------------------------------
// The six, by their datamap.h values, plus the answer for everything else.
//
// The NUMBERS are here rather than the enum, because this header does not
// include datamap.h -- see the file comment. save_field_codec_test.cpp asserts
// them against datamap.h's own text, so the two cannot drift.
//-----------------------------------------------------------------------------
enum SaveFieldKind_t
{
	kSaveFieldUnsupported = 0,   // and it is a REFUSAL, never a guess
	kSaveFieldInteger,           // FIELD_INTEGER  == 5
	kSaveFieldCharArray,         // FIELD_CHARACTER == 8, a fixed-width string
	kSaveFieldTime,              // FIELD_TIME     == 16, a float
	kSaveFieldShort,             // FIELD_SHORT    == 7,  two bytes
	kSaveFieldVector,            // FIELD_VECTOR   == 3,  three floats
	kSaveFieldPositionVector,    // FIELD_POSITION_VECTOR == 15, three floats
};

#define SOURCESHARP_FIELD_VECTOR           3
#define SOURCESHARP_FIELD_INTEGER          5
#define SOURCESHARP_FIELD_SHORT            7
#define SOURCESHARP_FIELD_CHARACTER        8
#define SOURCESHARP_FIELD_POSITION_VECTOR  15
#define SOURCESHARP_FIELD_TIME             16

inline SaveFieldKind_t SaveField_KindOf( int nFieldType )
{
	switch ( nFieldType )
	{
	case SOURCESHARP_FIELD_INTEGER:         return kSaveFieldInteger;
	case SOURCESHARP_FIELD_CHARACTER:       return kSaveFieldCharArray;
	case SOURCESHARP_FIELD_TIME:            return kSaveFieldTime;
	case SOURCESHARP_FIELD_SHORT:           return kSaveFieldShort;
	case SOURCESHARP_FIELD_VECTOR:          return kSaveFieldVector;
	case SOURCESHARP_FIELD_POSITION_VECTOR: return kSaveFieldPositionVector;
	default:                                return kSaveFieldUnsupported;
	}
}

// See docs/managed-saverestore.md, "13. How many bytes one field occupies in the block".
inline int SaveField_Bytes( int nFieldType, int nFieldSize )
{
	if ( nFieldSize < 1 )
		return -1;

	switch ( SaveField_KindOf( nFieldType ) )
	{
	case kSaveFieldInteger:
	case kSaveFieldTime:
		// Scalars, and only scalars. Four bytes each.
		return nFieldSize == 1 ? 4 : -1;

	case kSaveFieldShort:
		// Two, and stock writes `2 * fieldSize` -- but an ARRAY of them is
		// something no measured datamap asks for, so it is refused on the same
		// terms as an array of ints rather than guessed at.
		return nFieldSize == 1 ? 2 : -1;

	case kSaveFieldVector:
	case kSaveFieldPositionVector:
		// sizeof(Vector), which is three floats and no padding. Scalars only,
		// as above.
		return nFieldSize == 1 ? 12 : -1;

	case kSaveFieldCharArray:
		// The whole array, terminator included: the engine's `mapName[32]` is
		// thirty-two bytes in the struct and thirty-two in the block. Writing
		// only up to the NUL would be smaller and would make the block's
		// layout depend on its CONTENT, which is what makes a reader unable to
		// skip a field it does not want.
		return nFieldSize;

	case kSaveFieldUnsupported:
	default:
		return -1;
	}
}

// See docs/managed-saverestore.md, "14. Two of the six field types are not a byte copy".
#define SAVEFIELD_ZERO_TIME     ( FLT_MAX * -0.5f )
#define SAVEFIELD_INVALID_TIME  ( FLT_MAX * -1.0f )

// TRUE for the two types whose bytes on the wire are not the bytes in the
// struct. These cannot go through SaveField_Copy -- there is nowhere in its
// arguments to put a base time or a landmark -- so it refuses them and
// SaveField_Encode / SaveField_Decode are the way they are carried.
inline bool SaveField_NeedsContext( int nFieldType )
{
	const SaveFieldKind_t kind = SaveField_KindOf( nFieldType );

	return kind == kSaveFieldTime || kind == kSaveFieldPositionVector;
}

// Stock's CSave::WriteTime, for one element.
inline float SaveField_EncodeTime( float flValue, float flBaseTime )
{
	// 0 means NEVER and is stored as the sentinel, never as a delta.
	if ( flValue == 0.0f )
		return SAVEFIELD_ZERO_TIME;

	if ( flValue == SAVEFIELD_INVALID_TIME || flValue == FLT_MAX )
		return flValue;

	float flDelta = flValue - flBaseTime;

	// "never allow a time to become zero due to rebasing" -- a stored 0 is a
	// real time an instant either side of the base, and it would restore as
	// ZERO_TIME's "never" instead.
	if ( flDelta < 0.001f && flDelta > -0.001f )
		flDelta = 0.001f;

	return flDelta;
}

// Stock's CRestore::ReadTime, for one element.
inline float SaveField_DecodeTime( float flStored, float flBaseTime )
{
	if ( flStored == SAVEFIELD_ZERO_TIME )
		return 0.0f;

	if ( flStored == SAVEFIELD_INVALID_TIME || flStored == FLT_MAX )
		return flStored;

	return flStored + flBaseTime;
}

// `tmp != vec3_invalid` -- and Vector's operator!= is all three components, so
// a vector with FLT_MAX in ONE of them is an ordinary position and IS
// transformed. This is that comparison and not a cheaper one.
inline bool SaveField_IsInvalidPosition( const float *pVec )
{
	return pVec[ 0 ] == FLT_MAX && pVec[ 1 ] == FLT_MAX && pVec[ 2 ] == FLT_MAX;
}

// Stock's CSave::WritePositionVector, for one element. `pOut` and `pIn` may be
// the same array.
inline void SaveField_EncodePosition( float *pOut, const float *pIn,
                                      const float *pLandmark )
{
	const bool bInvalid = SaveField_IsInvalidPosition( pIn );

	for ( int i = 0; i < 3; i++ )
		pOut[ i ] = bInvalid ? pIn[ i ] : pIn[ i ] - pLandmark[ i ];
}

// Stock's CRestore::ReadPositionVector, for one element.
inline void SaveField_DecodePosition( float *pOut, const float *pIn,
                                      const float *pLandmark )
{
	const bool bInvalid = SaveField_IsInvalidPosition( pIn );

	for ( int i = 0; i < 3; i++ )
		pOut[ i ] = bInvalid ? pIn[ i ] : pIn[ i ] + pLandmark[ i ];
}

//-----------------------------------------------------------------------------
// What the caller has that this header does not: the save's base time and its
// landmark. A build that does no level transitions passes vec3_origin for the
// landmark and the transform is the identity -- but it is the CALLER that gets
// to say so, and the two sentinels are honoured either way, which is the part
// a zero landmark does not make true by itself.
//-----------------------------------------------------------------------------
struct SaveFieldContext_t
{
	float flBaseTime;
	float vecLandmark[ 3 ];
};

inline SaveFieldContext_t SaveField_Context( float flBaseTime, float flX,
                                             float flY, float flZ )
{
	SaveFieldContext_t ctx;

	ctx.flBaseTime = flBaseTime;
	ctx.vecLandmark[ 0 ] = flX;
	ctx.vecLandmark[ 1 ] = flY;
	ctx.vecLandmark[ 2 ] = flZ;

	return ctx;
}

// Unaligned-safe, and dependency-free: the caller's field lives at whatever
// offset its datamap gives it, so neither end can be assumed float-aligned.
inline float SaveField_LoadFloat( const void *pAt )
{
	float flValue = 0.0f;
	unsigned char *pOut = (unsigned char *)&flValue;
	const unsigned char *pIn = (const unsigned char *)pAt;

	for ( int i = 0; i < 4; i++ )
		pOut[ i ] = pIn[ i ];

	return flValue;
}

inline void SaveField_StoreFloat( void *pAt, float flValue )
{
	unsigned char *pOut = (unsigned char *)pAt;
	const unsigned char *pIn = (const unsigned char *)&flValue;

	for ( int i = 0; i < 4; i++ )
		pOut[ i ] = pIn[ i ];
}

// See docs/managed-saverestore.md, "15. Copy one field's bytes in either direction".
inline bool SaveField_Copy( void *pDest, const void *pSrc, int nFieldType,
                            int nFieldSize, int nBytesAvailable )
{
	const int nBytes = SaveField_Bytes( nFieldType, nFieldSize );

	if ( nBytes < 0 || nBytes > nBytesAvailable )
		return false;

	if ( pDest == 0 || pSrc == 0 )
		return false;

	// The two whose wire bytes are not the struct's bytes. There is no base
	// time and no landmark in this call, so a copy of one of them is a value
	// that is silently wrong rather than a field that is loudly missing --
	// SaveField_Encode / SaveField_Decode take the context and do it properly.
	if ( SaveField_NeedsContext( nFieldType ) )
		return false;

	unsigned char *pOut = (unsigned char *)pDest;
	const unsigned char *pIn = (const unsigned char *)pSrc;

	for ( int i = 0; i < nBytes; i++ )
		pOut[ i ] = pIn[ i ];

	return true;
}

//-----------------------------------------------------------------------------
// One field, struct -> block (Encode) and block -> struct (Decode).
//
// Every type goes through these; four of them arrive at the same byte copy
// SaveField_Copy does, and the two that need the context are transformed here
// and nowhere else. Same refusals as SaveField_Copy, and the same promise: on
// FALSE, not one byte of the destination is touched.
//-----------------------------------------------------------------------------
inline bool SaveField_Encode( void *pDest, const void *pSrc, int nFieldType,
                              int nFieldSize, int nBytesAvailable,
                              const SaveFieldContext_t &ctx )
{
	const int nBytes = SaveField_Bytes( nFieldType, nFieldSize );

	if ( nBytes < 0 || nBytes > nBytesAvailable )
		return false;

	if ( pDest == 0 || pSrc == 0 )
		return false;

	const SaveFieldKind_t kind = SaveField_KindOf( nFieldType );

	if ( kind == kSaveFieldTime )
	{
		SaveField_StoreFloat( pDest,
			SaveField_EncodeTime( SaveField_LoadFloat( pSrc ), ctx.flBaseTime ) );
		return true;
	}

	if ( kind == kSaveFieldPositionVector )
	{
		float vecIn[ 3 ];
		float vecOut[ 3 ];

		for ( int i = 0; i < 3; i++ )
			vecIn[ i ] = SaveField_LoadFloat( (const unsigned char *)pSrc + i * 4 );

		SaveField_EncodePosition( vecOut, vecIn, ctx.vecLandmark );

		for ( int i = 0; i < 3; i++ )
			SaveField_StoreFloat( (unsigned char *)pDest + i * 4, vecOut[ i ] );

		return true;
	}

	return SaveField_Copy( pDest, pSrc, nFieldType, nFieldSize, nBytesAvailable );
}

inline bool SaveField_Decode( void *pDest, const void *pSrc, int nFieldType,
                              int nFieldSize, int nBytesAvailable,
                              const SaveFieldContext_t &ctx )
{
	const int nBytes = SaveField_Bytes( nFieldType, nFieldSize );

	if ( nBytes < 0 || nBytes > nBytesAvailable )
		return false;

	if ( pDest == 0 || pSrc == 0 )
		return false;

	const SaveFieldKind_t kind = SaveField_KindOf( nFieldType );

	if ( kind == kSaveFieldTime )
	{
		SaveField_StoreFloat( pDest,
			SaveField_DecodeTime( SaveField_LoadFloat( pSrc ), ctx.flBaseTime ) );
		return true;
	}

	if ( kind == kSaveFieldPositionVector )
	{
		float vecIn[ 3 ];
		float vecOut[ 3 ];

		for ( int i = 0; i < 3; i++ )
			vecIn[ i ] = SaveField_LoadFloat( (const unsigned char *)pSrc + i * 4 );

		SaveField_DecodePosition( vecOut, vecIn, ctx.vecLandmark );

		for ( int i = 0; i < 3; i++ )
			SaveField_StoreFloat( (unsigned char *)pDest + i * 4, vecOut[ i ] );

		return true;
	}

	return SaveField_Copy( pDest, pSrc, nFieldType, nFieldSize, nBytesAvailable );
}

//-----------------------------------------------------------------------------
// How many bytes a whole block of fields needs, or -1 if ANY of them is a type
// this codec does not know.
//
// The all-or-nothing answer is the point: a block that encoded the fields it
// recognised and dropped one is a block whose every later field is read from
// the wrong offset.
//-----------------------------------------------------------------------------
template < typename TFieldReader >
inline int SaveField_BlockBytes( const TFieldReader &fields, int nCount )
{
	int nTotal = 0;

	for ( int i = 0; i < nCount; i++ )
	{
		const int nBytes = SaveField_Bytes( fields.TypeAt( i ), fields.SizeAt( i ) );

		if ( nBytes < 0 )
			return -1;

		nTotal += nBytes;
	}

	return nTotal;
}


// See docs/managed-saverestore.md, "2. The buffer this shell asks the engine for in SaveInit".
#define SOURCESHARP_SAVE_BUFFER_BYTES ( 4 * 1024 * 1024 )

// 0xfff, which is stock's, and for stock's stated reason: "Assume a maximum of
// 4K-1 symbol table entries". Unlike the first version of this file, the table
// is now USED -- every block and every field name goes through it, because that
// is what the engine reads back.
#define SOURCESHARP_SAVE_TOKEN_COUNT  0xfff

// The block header and the per-field record header: two shorts each
// (`SaveRestoreRecordHeader_t`).
#define SOURCESHARP_SAVE_RECORD_HEADER 4

// See docs/managed-saverestore.md, "3. The two time sentinels, and why a stored time is relative".
#define SOURCESHARP_ZERO_TIME    SAVEFIELD_ZERO_TIME
#define SOURCESHARP_INVALID_TIME SAVEFIELD_INVALID_TIME

//-----------------------------------------------------------------------------
// The save's symbol table, as this codec needs it.
//
// An interface rather than the concrete CSaveRestoreSegment, for the reason the
// whole header is engine-free: a test can hand it a table and read back what
// was interned, which is the only way to check that a block header names its
// block.
//-----------------------------------------------------------------------------
class IShellSaveSymbols
{
public:
	virtual ~IShellSaveSymbols() {}

	// The token for a name, creating it if this is the first time. Matches
	// CSaveRestoreSegment::FindCreateSymbol.
	virtual unsigned short FindCreate( const char *pszToken ) = 0;

	// The name behind a token, or NULL. Matches StringFromSymbol, except that
	// this one is allowed to answer NULL for a token that has none -- which is
	// exactly what a file from another build can carry.
	virtual const char *FromSymbol( int nToken ) = 0;
};

// See docs/managed-saverestore.md, "4. A bounded cursor over the save buffer".
class CShellSaveCursor
{
public:
	CShellSaveCursor( void *pBase, int nBytes )
		: m_pBase( (unsigned char *)pBase ), m_nBytes( nBytes < 0 ? 0 : nBytes ),
		  m_nAt( 0 ), m_bOverflowed( pBase == 0 )
	{
	}

	int Pos() const         { return m_nAt; }
	int Available() const   { return m_nBytes - m_nAt; }
	bool Overflowed() const { return m_bOverflowed; }

	bool Write( const void *pData, int nBytes )
	{
		if ( m_bOverflowed || nBytes < 0 || nBytes > Available() )
		{
			m_bOverflowed = true;
			return false;
		}

		memcpy( m_pBase + m_nAt, pData, (size_t)nBytes );
		m_nAt += nBytes;
		return true;
	}

	bool Read( void *pData, int nBytes )
	{
		if ( m_bOverflowed || nBytes < 0 || nBytes > Available() )
		{
			m_bOverflowed = true;
			return false;
		}

		memcpy( pData, m_pBase + m_nAt, (size_t)nBytes );
		m_nAt += nBytes;
		return true;
	}

	// The bytes at the cursor, without moving it. NULL when they are not all
	// there, which is how a caller peeks a record header safely.
	const unsigned char *Peek( int nBytes ) const
	{
		if ( m_bOverflowed || nBytes < 0 || nBytes > Available() )
			return 0;
		return m_pBase + m_nAt;
	}

	bool Skip( int nBytes )
	{
		if ( m_bOverflowed || nBytes < 0 || nBytes > Available() )
		{
			m_bOverflowed = true;
			return false;
		}

		m_nAt += nBytes;
		return true;
	}

	// Stock's CRestore rewinds four bytes when a block header names the wrong
	// block, so the caller can try to make sense of it. Never past the start.
	void Rewind( int nBytes )
	{
		if ( nBytes > m_nAt )
			nBytes = m_nAt;
		m_nAt -= nBytes;
	}

private:
	unsigned char *m_pBase;
	int m_nBytes;
	int m_nAt;
	bool m_bOverflowed;
};

//-----------------------------------------------------------------------------
// Case-insensitive name compare, because CRestore::FindField uses stricmp and
// a field found by one build and missed by the other is the silent half-restore
// this whole file exists to prevent. Written out rather than taken from
// tier1/strtools.h so that the header stays engine-free.
//-----------------------------------------------------------------------------
inline bool ShellSave_NamesMatch( const char *pszA, const char *pszB )
{
	if ( pszA == 0 || pszB == 0 )
		return false;

	for ( ; *pszA && *pszB; pszA++, pszB++ )
	{
		char a = *pszA;
		char b = *pszB;

		if ( a >= 'A' && a <= 'Z' ) a = (char)( a - 'A' + 'a' );
		if ( b >= 'A' && b <= 'Z' ) b = (char)( b - 'A' + 'a' );

		if ( a != b )
			return false;
	}

	return *pszA == *pszB;
}

// See docs/managed-saverestore.md, "5. Who creates the player on a savegame load".
inline bool ShellSave_LoadGameNeedsPlayer( bool bLoadGame, bool bPlayerAlreadyBound )
{
	return bLoadGame && !bPlayerAlreadyBound;
}

//-----------------------------------------------------------------------------
// FIELD_TIME's rebasing, both directions, out of CSave::WriteTime and
// CRestore::ReadTime. A time is stored as a DELTA from the save's base time so
// that a level loaded later does not resurrect absolute think times; the two
// sentinels are the cases that must not be rebased.
//-----------------------------------------------------------------------------
inline float ShellSave_EncodeTime( float flValue, float flBaseTime )
{
	// FORWARDED. This was a line-for-line copy of SaveField_EncodeTime, found
	// by the codec's own mutation harness once both existed. Two copies of a
	// transform is the shape where a fix travels to one and not the other, and
	// this file already includes the codec -- so the copy bought nothing.
	return SaveField_EncodeTime( flValue, flBaseTime );
}

inline float ShellSave_DecodeTime( float flStored, float flBaseTime )
{
	// Forwarded, for the reason above.
	return SaveField_DecodeTime( flStored, flBaseTime );
}

//-----------------------------------------------------------------------------
// One field record, written.
//
// FALSE means the buffer would not hold it, and the cursor is poisoned; the
// caller's block is then unfinishable, which is the correct outcome because a
// block whose `count` promises more records than the buffer holds is a block
// nothing can read.
//-----------------------------------------------------------------------------
inline bool ShellSave_WriteRecord( CShellSaveCursor &cur, IShellSaveSymbols &syms,
                                   const char *pszName, const void *pData, int nBytes )
{
	if ( nBytes < 0 || nBytes > 32767 )
		return false;

	const short nSize = (short)nBytes;
	const short nToken = (short)syms.FindCreate( pszName );

	if ( !cur.Write( &nSize, 2 ) )
		return false;
	if ( !cur.Write( &nToken, 2 ) )
		return false;

	return nBytes == 0 || cur.Write( pData, nBytes );
}

// See docs/managed-saverestore.md, "6. One SaveWriteFields call".
template < typename TFieldReader >
inline int ShellSave_WriteFieldBlock( CShellSaveCursor &cur, IShellSaveSymbols &syms,
                                      const char *pszName, const void *pBase,
                                      const TFieldReader &fields, int nCount,
                                      float flBaseTime,
                                      int *pnOmitted, const char **ppszOmittedName,
                                      int *pnOmittedType )
{
	int nOmittedIgnored = 0;
	const char *pszNameIgnored = 0;
	int nTypeIgnored = -1;

	if ( pnOmitted == 0 )       pnOmitted = &nOmittedIgnored;
	if ( ppszOmittedName == 0 ) ppszOmittedName = &pszNameIgnored;
	if ( pnOmittedType == 0 )   pnOmittedType = &nTypeIgnored;

	*pnOmitted = 0;
	*ppszOmittedName = 0;
	*pnOmittedType = -1;

	if ( pszName == 0 || pBase == 0 || nCount < 0 )
		return -1;

	const int nStart = cur.Pos();

	// The count record goes down first with a placeholder, exactly as stock
	// does it: the real number is not known until the walk is over, and the
	// header has to be in front of the records rather than behind them.
	int nWritten = -1;
	if ( !ShellSave_WriteRecord( cur, syms, pszName, &nWritten, 4 ) )
		return -1;

	nWritten = 0;

	for ( int i = 0; i < nCount; i++ )
	{
		const int nType = fields.TypeAt( i );
		const int nSize = fields.SizeAt( i );
		const int nBytes = SaveField_Bytes( nType, nSize );

		if ( nBytes < 0 )
		{
			// A type this build does not encode. Counted and named for the
			// caller's warning; NOT written, and NOT counted in `count`, so the
			// stream stays exactly as long as the header says it is.
			if ( *pnOmitted == 0 )
			{
				*ppszOmittedName = fields.NameAt( i );
				*pnOmittedType = nType;
			}
			( *pnOmitted )++;
			continue;
		}

		const unsigned char *pField = (const unsigned char *)pBase + fields.OffsetAt( i );

		if ( SaveField_KindOf( nType ) == kSaveFieldTime )
		{
			// Rebased, one element at a time. Stock's WriteTime, and the only
			// field whose bytes on the wire are not the struct's bytes.
			float flStored = 0.0f;
			memcpy( &flStored, pField, 4 );
			flStored = ShellSave_EncodeTime( flStored, flBaseTime );

			if ( !ShellSave_WriteRecord( cur, syms, fields.NameAt( i ), &flStored, 4 ) )
				return -1;
		}
		else
		{
			if ( !ShellSave_WriteRecord( cur, syms, fields.NameAt( i ), pField, nBytes ) )
				return -1;
		}

		nWritten++;
	}

	// Go back and stamp the real count over the placeholder. Stock rewinds the
	// segment and re-writes the whole record; this writes the four bytes it
	// actually put there, which cannot get the arithmetic wrong.
	const int nEnd = cur.Pos();
	cur.Rewind( nEnd - nStart - SOURCESHARP_SAVE_RECORD_HEADER );
	if ( !cur.Write( &nWritten, 4 ) )
		return -1;
	if ( !cur.Skip( nEnd - cur.Pos() ) )
		return -1;

	return nEnd - nStart;
}

// See docs/managed-saverestore.md, "7. One SaveReadFields call".
template < typename TFieldReader >
inline int ShellSave_ReadFieldBlock( CShellSaveCursor &cur, IShellSaveSymbols &syms,
                                     const char *pszName, void *pBase,
                                     const TFieldReader &fields, int nCount,
                                     float flBaseTime, const char **ppszRefusal )
{
	const char *pszIgnored = 0;
	if ( ppszRefusal == 0 )
		ppszRefusal = &pszIgnored;

	*ppszRefusal = 0;

	if ( pszName == 0 || pBase == 0 || nCount < 0 )
	{
		*ppszRefusal = "a null buffer, struct or block name";
		return -1;
	}

	const int nStart = cur.Pos();

	short nSize = 0;
	short nToken = 0;
	int nSaved = 0;

	if ( !cur.Read( &nSize, 2 ) || !cur.Read( &nToken, 2 ) )
	{
		*ppszRefusal = "the save data ends inside a block header";
		return -1;
	}

	if ( nSize != 4 )
	{
		// Stock Verify()s this: the first record of a block is always the int
		// that counts the rest.
		cur.Rewind( cur.Pos() - nStart );
		*ppszRefusal = "no block header here -- the first record is not an int";
		return -1;
	}

	if ( (unsigned short)nToken != syms.FindCreate( pszName ) )
	{
		const char *pszFound = syms.FromSymbol( nToken );
		cur.Rewind( cur.Pos() - nStart );
		*ppszRefusal = pszFound ? pszFound : "a block written under another name";
		return -1;
	}

	if ( !cur.Read( &nSaved, 4 ) )
	{
		*ppszRefusal = "the save data ends inside a block's field count";
		return -1;
	}

	if ( nSaved < 0 )
	{
		cur.Rewind( cur.Pos() - nStart );
		*ppszRefusal = "a negative field count, which came off a file";
		return -1;
	}

	// PASS ONE: is the whole block actually here? Nothing is written, and a
	// failure rewinds to where the caller's cursor started.
	const int nAfterHeader = cur.Pos();

	for ( int i = 0; i < nSaved; i++ )
	{
		short nWalkSize = 0;
		short nWalkToken = 0;

		if ( !cur.Read( &nWalkSize, 2 ) || !cur.Read( &nWalkToken, 2 ) )
		{
			cur.Rewind( cur.Pos() - nStart );
			*ppszRefusal = "the save data ends inside a field record header";
			return -1;
		}

		if ( nWalkSize < 0 )
		{
			cur.Rewind( cur.Pos() - nStart );
			*ppszRefusal = "a negative field width, which came off a file";
			return -1;
		}

		if ( !cur.Skip( nWalkSize ) )
		{
			cur.Rewind( cur.Pos() - nStart );
			*ppszRefusal = "the save data ends inside a field record";
			return -1;
		}
	}

	const int nEnd = cur.Pos();
	cur.Rewind( nEnd - nAfterHeader );

	// Stock clears the destination before reading, so that a field the file
	// does not carry restores as zero rather than as whatever the caller's
	// struct happened to hold. Only now, with the block proved complete.
	for ( int i = 0; i < nCount; i++ )
	{
		const int nClear = SaveField_Bytes( fields.TypeAt( i ), fields.SizeAt( i ) );

		if ( nClear > 0 )
			memset( (unsigned char *)pBase + fields.OffsetAt( i ), 0, (size_t)nClear );
	}

	// PASS TWO: decode. Nothing below can fail on a bound -- pass one proved
	// every byte is there -- which is why it does not have to unwind.
	for ( int i = 0; i < nSaved; i++ )
	{
		short nRecSize = 0;
		short nRecToken = 0;

		if ( !cur.Read( &nRecSize, 2 ) || !cur.Read( &nRecToken, 2 ) )
		{
			*ppszRefusal = "the save data ends inside a field record header";
			return -1;
		}

		const char *pszField = syms.FromSymbol( nRecToken );
		int nAt = -1;

		for ( int j = 0; j < nCount && nAt < 0; j++ )
		{
			if ( ShellSave_NamesMatch( fields.NameAt( j ), pszField ) )
				nAt = j;
		}

		const int nBytes = ( nAt >= 0 )
		                 ? SaveField_Bytes( fields.TypeAt( nAt ), fields.SizeAt( nAt ) )
		                 : -1;

		// Skipped when this build has no such field, cannot decode its type, or
		// disagrees about its width. The width in the file is what advances the
		// cursor in every one of those cases, which is the property that makes
		// the next record findable.
		if ( nAt < 0 || nBytes != (int)nRecSize )
		{
			if ( !cur.Skip( nRecSize ) )
			{
				*ppszRefusal = "the save data ends inside a field record";
				return -1;
			}
			continue;
		}

		unsigned char *pField = (unsigned char *)pBase + fields.OffsetAt( nAt );

		if ( SaveField_KindOf( fields.TypeAt( nAt ) ) == kSaveFieldTime )
		{
			float flStored = 0.0f;
			if ( !cur.Read( &flStored, 4 ) )
			{
				*ppszRefusal = "the save data ends inside a time field";
				return -1;
			}

			const float flValue = ShellSave_DecodeTime( flStored, flBaseTime );
			memcpy( pField, &flValue, 4 );
		}
		else if ( !cur.Read( pField, nBytes ) )
		{
			*ppszRefusal = "the save data ends inside a field";
			return -1;
		}
	}

	return cur.Pos() - nStart;
}

// See docs/managed-saverestore.md, "8. A blob block, and what makes one self-describing".
#define SOURCESHARP_SAVE_BLOB_CHUNK 30000

// The record name every chunk of a blob block is interned under. One name for
// all of them: the reader walks them positionally, in the order `count` says,
// and a per-chunk name would be a second thing to keep in step.
#define SOURCESHARP_SAVE_BLOB_FIELD "BLOB"

//-----------------------------------------------------------------------------
// Write one. Returns the bytes the block occupies, or -1 if it would not fit
// -- in which case the cursor is poisoned and the caller's save is over, which
// is the right outcome: a block whose `count` promises chunks the buffer does
// not hold is a block nothing can read.
//-----------------------------------------------------------------------------
inline int ShellSave_WriteBlobBlock( CShellSaveCursor &cur, IShellSaveSymbols &syms,
                                     const char *pszName, const void *pData, int nBytes )
{
	if ( pszName == 0 || nBytes < 0 || ( nBytes > 0 && pData == 0 ) )
		return -1;

	const int nStart = cur.Pos();

	int nChunks = 0;
	for ( int nAt = 0; nAt < nBytes; nAt += SOURCESHARP_SAVE_BLOB_CHUNK )
		nChunks++;

	if ( !ShellSave_WriteRecord( cur, syms, pszName, &nChunks, 4 ) )
		return -1;

	const unsigned char *pBytes = (const unsigned char *)pData;

	for ( int nAt = 0; nAt < nBytes; nAt += SOURCESHARP_SAVE_BLOB_CHUNK )
	{
		int nThis = nBytes - nAt;
		if ( nThis > SOURCESHARP_SAVE_BLOB_CHUNK )
			nThis = SOURCESHARP_SAVE_BLOB_CHUNK;

		if ( !ShellSave_WriteRecord( cur, syms, SOURCESHARP_SAVE_BLOB_FIELD,
		                             pBytes + nAt, nThis ) )
			return -1;
	}

	return cur.Pos() - nStart;
}

// See docs/managed-saverestore.md, "9. Reading a blob back, size-then-fill".
#define kShellSaveNoBlock (-2)

inline int ShellSave_ReadBlobBlock( CShellSaveCursor &cur, IShellSaveSymbols &syms,
                                    const char *pszName, void *pOut, int nMax,
                                    const char **ppszRefusal )
{
	const char *pszIgnored = 0;
	if ( ppszRefusal == 0 )
		ppszRefusal = &pszIgnored;

	*ppszRefusal = 0;

	if ( pszName == 0 || nMax < 0 || ( nMax > 0 && pOut == 0 ) )
	{
		*ppszRefusal = "a null buffer or a negative size";
		return -1;
	}

	const int nStart = cur.Pos();

	short nSize = 0;
	short nToken = 0;
	int nChunks = 0;

	if ( !cur.Read( &nSize, 2 ) || !cur.Read( &nToken, 2 ) )
	{
		// The save data ends here. That is what a file with no managed block
		// looks like, and it is not damage.
		cur.Rewind( cur.Pos() - nStart );
		return kShellSaveNoBlock;
	}

	if ( nSize != 4 || (unsigned short)nToken != syms.FindCreate( pszName ) )
	{
		cur.Rewind( cur.Pos() - nStart );
		return kShellSaveNoBlock;
	}

	if ( !cur.Read( &nChunks, 4 ) )
	{
		cur.Rewind( cur.Pos() - nStart );
		*ppszRefusal = "the save data ends inside the block's chunk count";
		return -1;
	}

	if ( nChunks < 0 )
	{
		cur.Rewind( cur.Pos() - nStart );
		*ppszRefusal = "a negative chunk count, which came off a file";
		return -1;
	}

	// PASS ONE: how big is the payload, and is all of it here? Nothing is
	// copied and nothing is believed until every chunk has been walked --
	// shell_savegame.h's ReadFieldBlock makes the same two passes and for the
	// same reason. A truncated last chunk must refuse the WHOLE block rather
	// than hand back a prefix, because the payload's own codec would then read
	// a prefix as a short entity list rather than as damage.
	const int nAfterHeader = cur.Pos();
	int nPayload = 0;

	for ( int i = 0; i < nChunks; i++ )
	{
		short nChunkSize = 0;
		short nChunkToken = 0;

		if ( !cur.Read( &nChunkSize, 2 ) || !cur.Read( &nChunkToken, 2 ) )
		{
			cur.Rewind( cur.Pos() - nStart );
			*ppszRefusal = "the save data ends inside a chunk header";
			return -1;
		}

		if ( nChunkSize < 0 || !cur.Skip( nChunkSize ) )
		{
			cur.Rewind( cur.Pos() - nStart );
			*ppszRefusal = "a chunk runs past the end of the save data";
			return -1;
		}

		nPayload += nChunkSize;
	}

	const int nEnd = cur.Pos();

	if ( nMax < nPayload )
	{
		// The sizing call. Nothing copied, nothing consumed.
		cur.Rewind( nEnd - nStart );
		return nPayload;
	}

	// PASS TWO: copy. Nothing below can fail on a bound.
	cur.Rewind( nEnd - nAfterHeader );

	unsigned char *pBytes = (unsigned char *)pOut;
	int nAt = 0;

	for ( int i = 0; i < nChunks; i++ )
	{
		short nChunkSize = 0;
		short nChunkToken = 0;

		cur.Read( &nChunkSize, 2 );
		cur.Read( &nChunkToken, 2 );

		if ( nChunkSize > 0 )
			cur.Read( pBytes + nAt, nChunkSize );

		nAt += nChunkSize;
	}

	return nPayload;
}

// See docs/managed-saverestore.md, "10. The shell's side of the engine's save cluster".
//
// CLIENT ONLY since plan_server_native_v2 T4c: shell_savegame.cpp is client.so's
// (src/client/native); the server's save cluster is the managed SaveGameSession,
// and server.so includes this header for ShellSave_LoadGameNeedsPlayer alone.
#ifdef CLIENT_DLL
class CSaveRestoreData;
struct typedescription_t;

// The engine's save allocator is `SaveAllocMemory( num, size )` on
// IVEngineClient (public/cdll_int.h:400).
class IVEngineClient;
typedef IVEngineClient ShellSaveEngine_t;

// Allocate and place the save buffer. NULL only on an allocation failure, and
// the caller must say so out loud: docs/managed-saverestore.md §4a records that
// BOTH engine call sites abort the save silently on a NULL -- and the CLIENT's
// SaveInit answering NULL was measured taking the whole `.sav` with it, with no
// file and no message, which is how §5 item 4 stopped being a prediction.
CSaveRestoreData *ShellSave_Init( ShellSaveEngine_t *pEngine, int nSize, float flCurTime );

// One SaveWriteFields / SaveReadFields, in stock's record format. FALSE on a
// refusal, having already named it through Warning().
bool ShellSave_WriteFields( CSaveRestoreData *pSave, const char *pszName, const void *pBase,
                            const typedescription_t *pFields, int nCount );
bool ShellSave_ReadFields( CSaveRestoreData *pSave, const char *pszName, void *pBase,
                           const typedescription_t *pFields, int nCount );

// THE MANAGED ENTITY BLOCK, both directions.
//
// ShellSave_WriteBlob answers the bytes the block occupies (0 when there is
// nothing to write, which is not a failure) or -1 having already warned.
// ShellSave_ReadBlob answers the payload it copied out, kShellSaveNoBlock when
// this save carries no such block, or -1 on damage -- and NOTHING is copied on
// either of the last two.
int ShellSave_WriteBlob( CSaveRestoreData *pSave, const char *pszName,
                         const void *pData, int nBytes );
int ShellSave_ReadBlob( CSaveRestoreData *pSave, const char *pszName,
                        void *pOut, int nMax );

// THE BLOCK NAME the managed entity state is written under, in one place
// because the writer and the reader are in two functions and a second literal
// is a second thing to get wrong. Interned through the save's symbol table like
// every other block name, which is what the engine resolves on load.
#define SOURCESHARP_SAVE_ENTITY_BLOCK "SSENTITY"

// Readouts, for the trace. Cheap accessors over the segment, so that
// shell_gamedll.cpp can report what a save cost without holding a complete
// CSaveRestoreData.
int ShellSave_CurPos( CSaveRestoreData *pSave );
int ShellSave_BytesAvailable( CSaveRestoreData *pSave );

// How many entries of the symbol table are populated, and how many slots it
// has. THE MEASUREMENT docs/managed-saverestore.md §6 item 2 asks for: the
// engine rebuilds this table from the file on load and resolves block names
// through it, so a load that arrives with an empty one is a load that cannot
// name a single block.
int ShellSave_SymbolsDefined( CSaveRestoreData *pSave );
int ShellSave_SymbolTableSize( CSaveRestoreData *pSave );
#endif // CLIENT_DLL

#endif // SOURCESHARP_SHELL_SAVEGAME_H
