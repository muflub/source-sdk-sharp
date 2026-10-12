//====== SourceSharp — the save buffer's record layout =======================//
//
// sourcesharp/game/shared/shell_savegame.h lays out ONE SaveWriteFields call in
// the save buffer and reads it back on load. save_field_codec_test.cpp already
// covers the field codec underneath it; what is here is the FRAMING, which is
// `CSave::WriteFields`' byte for byte.
//
// AND THAT IS THE FACT THIS FILE EXISTS TO KEEP. The format is not a choice: an
// earlier version of the shim used a self-describing frame of its own, wrote a
// `.sav`, and segfaulted the engine on load — inside engine.so, on a register
// holding two bytes of that frame's own magic. On load the engine rebuilds
// the symbol table from the file and then reads a SHORT AT +2 OF THE GAME'S
// BLOCK as an index into it. So the two shorts in front of every record,
// and the symbol table behind them, are a contract with the engine and not an
// internal detail, and the facts below are what stop them drifting back.
//
// THE OMISSION FACTS ARE THE SUBTLE ONES. save_field_codec.h argues that an
// unrecognised field type must refuse the WHOLE block, because a flat layout
// that skipped one would read every later field from the wrong offset. That
// argument does not survive this format: every record carries its own name and
// its own width, so a field the writer omitted is a name the reader never sees.
// Stock depends on this — ShouldSaveField omits empty fields and adjusts the
// count. What the facts here pin is that omitting stays HONEST: the field is
// counted and named for the caller's warning, the block is still complete, and
// every later block still reads.
//
//=============================================================================//

#include <string.h>
#include <stddef.h>
#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"

#include "../game/shared/shell_savegame.h"

namespace
{

//-----------------------------------------------------------------------------
// The accessor the codec templates walk. In the game this reads a
// typedescription_t array; here it is four plain vectors, which is the whole
// reason shell_savegame.h is engine-free — a field type no engine has can be
// asked for.
//-----------------------------------------------------------------------------
class CFields
{
public:
	void Add( const char *pszName, int nType, int nSize, int nOffset )
	{
		m_Names.push_back( pszName );
		m_Types.push_back( nType );
		m_Sizes.push_back( nSize );
		m_Offsets.push_back( nOffset );
	}

	int Count() const { return (int)m_Types.size(); }

	const char *NameAt( int i ) const { return m_Names[ i ].c_str(); }
	int TypeAt( int i ) const   { return m_Types[ i ]; }
	int SizeAt( int i ) const   { return m_Sizes[ i ]; }
	int OffsetAt( int i ) const { return m_Offsets[ i ]; }

private:
	std::vector< std::string > m_Names;
	std::vector< int > m_Types;
	std::vector< int > m_Sizes;
	std::vector< int > m_Offsets;
};

//-----------------------------------------------------------------------------
// A symbol table with the same shape as the save's: sparse, hashed, and
// answering NULL for a token nothing ever interned — which is what a token that
// came off a file looks like.
//-----------------------------------------------------------------------------
class CFakeSymbols : public IShellSaveSymbols
{
public:
	CFakeSymbols() : m_Slots( 64 ) {}

	virtual unsigned short FindCreate( const char *pszToken )
	{
		const std::string s = pszToken ? pszToken : "";

		for ( size_t i = 0; i < m_Slots.size(); i++ )
		{
			if ( m_bUsed.count( i ) && m_Slots[ i ] == s )
				return (unsigned short)i;
		}

		for ( size_t i = 1; i < m_Slots.size(); i++ )
		{
			if ( !m_bUsed.count( i ) )
			{
				m_Slots[ i ] = s;
				m_bUsed.insert( i );
				return (unsigned short)i;
			}
		}

		return 0;
	}

	virtual const char *FromSymbol( int nToken )
	{
		if ( nToken < 0 || nToken >= (int)m_Slots.size() || !m_bUsed.count( (size_t)nToken ) )
			return NULL;

		return m_Slots[ nToken ].c_str();
	}

	int Defined() const { return (int)m_bUsed.size(); }

private:
	std::vector< std::string > m_Slots;
	std::set< size_t > m_bUsed;
};

// The engine's own save header, in the shape docs/managed-saverestore.md §4a
// measured: four FIELD_INTEGERs, one FIELD_TIME and two fixed-width strings.
struct SaveHeaderShape
{
	int   skillLevel;
	int   connectionCount;
	int   lightStyleCount;
	int   mapVersion;
	float time;
	char  mapName[ 32 ];
	char  skyName[ 32 ];
};

CFields SaveHeaderFields()
{
	CFields f;
	f.Add( "skillLevel",      SOURCESHARP_FIELD_INTEGER,   1,  (int)offsetof( SaveHeaderShape, skillLevel ) );
	f.Add( "connectionCount", SOURCESHARP_FIELD_INTEGER,   1,  (int)offsetof( SaveHeaderShape, connectionCount ) );
	f.Add( "lightStyleCount", SOURCESHARP_FIELD_INTEGER,   1,  (int)offsetof( SaveHeaderShape, lightStyleCount ) );
	f.Add( "mapVersion",      SOURCESHARP_FIELD_INTEGER,   1,  (int)offsetof( SaveHeaderShape, mapVersion ) );
	f.Add( "time",            SOURCESHARP_FIELD_TIME,      1,  (int)offsetof( SaveHeaderShape, time ) );
	f.Add( "mapName",         SOURCESHARP_FIELD_CHARACTER, 32, (int)offsetof( SaveHeaderShape, mapName ) );
	f.Add( "skyName",         SOURCESHARP_FIELD_CHARACTER, 32, (int)offsetof( SaveHeaderShape, skyName ) );
	return f;
}

SaveHeaderShape AHeader()
{
	SaveHeaderShape h;
	memset( &h, 0, sizeof( h ) );
	h.skillLevel = 2;
	h.connectionCount = 0;
	h.lightStyleCount = 15;
	h.mapVersion = 4;
	h.time = 123.5f;
	strcpy( h.mapName, "dm_lockdown" );
	strcpy( h.skyName, "sky_day01_01" );
	return h;
}

// The other block the engine asks for, fifteen times per save.
struct LightStyleShape
{
	int  index;
	char style[ 64 ];
};

CFields LightStyleFields()
{
	CFields f;
	f.Add( "index", SOURCESHARP_FIELD_INTEGER,   1,  (int)offsetof( LightStyleShape, index ) );
	f.Add( "style", SOURCESHARP_FIELD_CHARACTER, 64, (int)offsetof( LightStyleShape, style ) );
	return f;
}

// What a `SaveWriteFields` call costs on the wire: the count record, plus one
// record per field.
const int kHeaderBlock = ( SOURCESHARP_SAVE_RECORD_HEADER + 4 )        // count
                       + 4 * ( SOURCESHARP_SAVE_RECORD_HEADER + 4 )    // the ints
                       + 1 * ( SOURCESHARP_SAVE_RECORD_HEADER + 4 )    // the time
                       + 2 * ( SOURCESHARP_SAVE_RECORD_HEADER + 32 );  // the strings

const float kBaseTime = 100.0f;

// The destination every refusal must leave alone.
struct CUntouched
{
	SaveHeaderShape out;

	CUntouched() { memset( &out, 0xcd, sizeof( out ) ); }

	bool StillUntouched() const
	{
		const unsigned char *p = (const unsigned char *)&out;
		for ( size_t i = 0; i < sizeof( out ); i++ )
		{
			if ( p[ i ] != 0xcd )
				return false;
		}
		return true;
	}
};

} // namespace

//-----------------------------------------------------------------------------
// The wire format, which is a contract with the engine.
//-----------------------------------------------------------------------------
TEST( SaveBlockFormat, ABlockOpensWithAFourByteCountRecordNamingTheBlock )
{
	// THE TWO SHORTS THE ENGINE READS. It resolves the one at +2 through the
	// symbol table it rebuilt from the file, and strcmp's the answer against
	// its own literal — measured in engine.so, and the reason this format is
	// not a choice. A change here is a change to that contract.
	const CFields f = SaveHeaderFields();
	const SaveHeaderShape in = AHeader();

	char buf[ 512 ];
	CShellSaveCursor cur( buf, sizeof( buf ) );
	CFakeSymbols syms;

	ASSERT_GT( ShellSave_WriteFieldBlock( cur, syms, "Save Header", &in, f, f.Count(),
	                                      kBaseTime, NULL, NULL, NULL ), 0 );

	short nSize = 0, nToken = 0;
	int nCount = 0;
	memcpy( &nSize, buf + 0, 2 );
	memcpy( &nToken, buf + 2, 2 );
	memcpy( &nCount, buf + 4, 4 );

	EXPECT_EQ( 4, nSize ) << "the first record of a block is always the int that counts the rest";
	EXPECT_STREQ( "Save Header", syms.FromSymbol( nToken ) )
		<< "the token at +2 must resolve to the block's name through the save's "
		   "own symbol table -- this is the short the engine reads";
	EXPECT_EQ( f.Count(), nCount );
}

TEST( SaveBlockFormat, EveryFieldRecordCarriesItsOwnWidthAndItsOwnName )
{
	const CFields f = LightStyleFields();

	LightStyleShape in;
	memset( &in, 0, sizeof( in ) );
	in.index = 7;
	strcpy( in.style, "mmnmmommommnonmmonqnmmo" );

	char buf[ 512 ];
	CShellSaveCursor cur( buf, sizeof( buf ) );
	CFakeSymbols syms;

	ASSERT_GT( ShellSave_WriteFieldBlock( cur, syms, "LIGHTSTYLE", &in, f, f.Count(),
	                                      kBaseTime, NULL, NULL, NULL ), 0 );

	// Past the count record: the first field record.
	short nSize = 0, nToken = 0;
	memcpy( &nSize, buf + 8, 2 );
	memcpy( &nToken, buf + 10, 2 );

	EXPECT_EQ( 4, nSize ) << "index is one int";
	EXPECT_STREQ( "index", syms.FromSymbol( nToken ) );

	memcpy( &nSize, buf + 8 + 4 + 4, 2 );
	memcpy( &nToken, buf + 8 + 4 + 4 + 2, 2 );

	EXPECT_EQ( 64, nSize ) << "the WHOLE fixed-width array, terminator and all";
	EXPECT_STREQ( "style", syms.FromSymbol( nToken ) );
}

TEST( SaveBlockFormat, TheBlockIsExactlyItsRecordsAndNothingMore )
{
	const CFields f = SaveHeaderFields();
	const SaveHeaderShape in = AHeader();

	char buf[ 512 ];
	CShellSaveCursor cur( buf, sizeof( buf ) );
	CFakeSymbols syms;

	EXPECT_EQ( kHeaderBlock, ShellSave_WriteFieldBlock( cur, syms, "Save Header", &in,
	                                                    f, f.Count(), kBaseTime,
	                                                    NULL, NULL, NULL ) );
	EXPECT_EQ( kHeaderBlock, cur.Pos() )
		<< "the cursor the caller then advances by must be where the block ends";
}

//-----------------------------------------------------------------------------
// The round trip, which is what the engine's save/load pair actually is.
//-----------------------------------------------------------------------------
TEST( SaveBlockRoundTrip, TheEnginesSaveHeaderComesBackFieldForField )
{
	const CFields f = SaveHeaderFields();
	const SaveHeaderShape in = AHeader();

	char buf[ 512 ];
	CFakeSymbols syms;

	CShellSaveCursor wr( buf, sizeof( buf ) );
	const int nWritten = ShellSave_WriteFieldBlock( wr, syms, "Save Header", &in, f, f.Count(),
	                                                kBaseTime, NULL, NULL, NULL );
	ASSERT_EQ( kHeaderBlock, nWritten );

	SaveHeaderShape out;
	memset( &out, 0xcd, sizeof( out ) );

	CShellSaveCursor rd( buf, nWritten );
	const char *pszRefusal = "unset";
	const int nRead = ShellSave_ReadFieldBlock( rd, syms, "Save Header", &out, f, f.Count(),
	                                            kBaseTime, &pszRefusal );

	ASSERT_EQ( nWritten, nRead ) << ( pszRefusal ? pszRefusal : "" );
	EXPECT_TRUE( pszRefusal == NULL ) << "a success must clear the refusal reason";

	EXPECT_EQ( in.skillLevel, out.skillLevel );
	EXPECT_EQ( in.connectionCount, out.connectionCount );
	EXPECT_EQ( in.lightStyleCount, out.lightStyleCount );
	EXPECT_EQ( in.mapVersion, out.mapVersion );
	EXPECT_EQ( in.time, out.time );
	EXPECT_STREQ( in.mapName, out.mapName );
	EXPECT_STREQ( in.skyName, out.skyName );
}

TEST( SaveBlockRoundTrip, AFixedWidthStringCarriesItsWholeArrayAndNotJustTheText )
{
	// The whole 64 bytes, terminator and trailing bytes included — because a
	// block whose LAYOUT depended on its CONTENT is one a reader cannot skip
	// through, and skipping is exactly what the reader does with a field it
	// does not want.
	const CFields f = LightStyleFields();

	LightStyleShape in;
	memset( &in, 0, sizeof( in ) );
	in.index = 7;
	strcpy( in.style, "mmnmmommommnonmmonqnmmo" );
	in.style[ 60 ] = 'Z';   // past the terminator, and it still travels

	char buf[ 512 ];
	CFakeSymbols syms;

	CShellSaveCursor wr( buf, sizeof( buf ) );
	const int nWritten = ShellSave_WriteFieldBlock( wr, syms, "LIGHTSTYLE", &in, f, f.Count(),
	                                                kBaseTime, NULL, NULL, NULL );
	ASSERT_GT( nWritten, 0 );

	LightStyleShape out;
	memset( &out, 0, sizeof( out ) );

	CShellSaveCursor rd( buf, nWritten );
	ASSERT_EQ( nWritten, ShellSave_ReadFieldBlock( rd, syms, "LIGHTSTYLE", &out, f, f.Count(),
	                                               kBaseTime, NULL ) );

	EXPECT_EQ( 7, out.index );
	EXPECT_STREQ( in.style, out.style );
	EXPECT_EQ( 'Z', out.style[ 60 ] );
}

TEST( SaveBlockRoundTrip, BlocksWrittenBackToBackReadBackInTheSameOrder )
{
	// The shape the engine actually drives: one Save Header, then fifteen
	// LIGHTSTYLEs, all sequential in one buffer with one cursor.
	const CFields fh = SaveHeaderFields();
	const CFields fl = LightStyleFields();

	char buf[ 8192 ];
	CFakeSymbols syms;
	CShellSaveCursor wr( buf, sizeof( buf ) );

	const SaveHeaderShape header = AHeader();
	ASSERT_GT( ShellSave_WriteFieldBlock( wr, syms, "Save Header", &header, fh, fh.Count(),
	                                      kBaseTime, NULL, NULL, NULL ), 0 );

	for ( int i = 0; i < 15; i++ )
	{
		LightStyleShape ls;
		memset( &ls, 0, sizeof( ls ) );
		ls.index = i;
		ls.style[ 0 ] = (char)( 'a' + i );

		ASSERT_GT( ShellSave_WriteFieldBlock( wr, syms, "LIGHTSTYLE", &ls, fl, fl.Count(),
		                                      kBaseTime, NULL, NULL, NULL ), 0 );
	}

	const int nTotal = wr.Pos();
	CShellSaveCursor rd( buf, nTotal );

	SaveHeaderShape gotHeader;
	memset( &gotHeader, 0, sizeof( gotHeader ) );
	ASSERT_GT( ShellSave_ReadFieldBlock( rd, syms, "Save Header", &gotHeader, fh, fh.Count(),
	                                     kBaseTime, NULL ), 0 );
	EXPECT_STREQ( "dm_lockdown", gotHeader.mapName );

	for ( int i = 0; i < 15; i++ )
	{
		LightStyleShape ls;
		memset( &ls, 0xcd, sizeof( ls ) );

		ASSERT_GT( ShellSave_ReadFieldBlock( rd, syms, "LIGHTSTYLE", &ls, fl, fl.Count(),
		                                     kBaseTime, NULL ), 0 ) << "light style " << i;

		EXPECT_EQ( i, ls.index );
		EXPECT_EQ( (char)( 'a' + i ), ls.style[ 0 ] );
	}

	EXPECT_EQ( nTotal, rd.Pos() ) << "the reads must consume exactly what the writes produced";
}

//-----------------------------------------------------------------------------
// FIELD_TIME, the one field whose bytes on the wire are not the struct's bytes.
//-----------------------------------------------------------------------------
TEST( SaveTimeField, ATimeIsStoredAsADeltaFromTheSavesBaseTime )
{
	// The reason for the whole dance: a `.sav` loaded into a level whose clock
	// starts somewhere else must not resurrect absolute think times.
	const float flStored = ShellSave_EncodeTime( 130.0f, 100.0f );

	EXPECT_FLOAT_EQ( 30.0f, flStored );
	EXPECT_FLOAT_EQ( 130.0f, ShellSave_DecodeTime( flStored, 100.0f ) );
	EXPECT_FLOAT_EQ( 30.0f, ShellSave_DecodeTime( flStored, 0.0f ) )
		<< "loaded against a different base, it rebases -- which is the point";
}

TEST( SaveTimeField, AZeroTimeRestoresAsZeroAndNotAsTheMomentOfTheSave )
{
	// Stock's ZERO_TIME sentinel. Without it a zero would be written as
	// `0 - baseTime` and restored as the base time, so "never" would become
	// "now" on every load.
	const float flStored = ShellSave_EncodeTime( 0.0f, 100.0f );

	EXPECT_NE( 0.0f, flStored ) << "zero is not stored as a zero delta";
	EXPECT_FLOAT_EQ( 0.0f, ShellSave_DecodeTime( flStored, 100.0f ) );
	EXPECT_FLOAT_EQ( 0.0f, ShellSave_DecodeTime( flStored, 7.0f ) )
		<< "and it stays zero under any base";
}

TEST( SaveTimeField, TheTwoSentinelsAreNotRebased )
{
	EXPECT_FLOAT_EQ( SOURCESHARP_INVALID_TIME,
	                 ShellSave_EncodeTime( SOURCESHARP_INVALID_TIME, 100.0f ) );
	EXPECT_FLOAT_EQ( SOURCESHARP_INVALID_TIME,
	                 ShellSave_DecodeTime( SOURCESHARP_INVALID_TIME, 100.0f ) );
	EXPECT_FLOAT_EQ( FLT_MAX, ShellSave_EncodeTime( FLT_MAX, 100.0f ) );
	EXPECT_FLOAT_EQ( FLT_MAX, ShellSave_DecodeTime( FLT_MAX, 100.0f ) );
}

TEST( SaveTimeField, ARealTimeIsNeverAllowedToRebaseToZero )
{
	// A time exactly equal to the base would encode as 0.0, which decodes
	// through the ZERO_TIME arm as "never". Stock nudges it, and so does this.
	const float flStored = ShellSave_EncodeTime( 100.0f, 100.0f );

	EXPECT_NE( 0.0f, flStored );
	EXPECT_NEAR( 100.0f, ShellSave_DecodeTime( flStored, 100.0f ), 0.01f );
}

//-----------------------------------------------------------------------------
// Omission — the behaviour that reverses save_field_codec.h's argument, and the
// one most likely to be "corrected" back by somebody reading only that header.
//-----------------------------------------------------------------------------
TEST( SaveBlockOmission, AFieldTypeThisBuildCannotEncodeIsOmittedNamedAndCounted )
{
	// FIELD_STRING, which is a `string_t` -- an index into an engine string
	// pool rather than bytes -- so it is not something this codec can widen to
	// without a way to carry the pool.
	//
	// IT USED TO BE FIELD_POSITION_VECTOR HERE, and the swap is the history
	// worth keeping: 15 was measured live coming out of the CLIENT's DECALLIST
	// block, was refused by this path exactly as designed, and is now ENCODED.
	// A refusal fact whose subject gets implemented stops testing anything, and
	// a fact that quietly starts passing for that reason is the failure this
	// project has a name for.
	CFields f = SaveHeaderFields();
	f.Add( "targetname", 2 /* FIELD_STRING */, 1, 0 );

	const SaveHeaderShape in = AHeader();

	char buf[ 512 ];
	CShellSaveCursor cur( buf, sizeof( buf ) );
	CFakeSymbols syms;

	int nOmitted = -1;
	const char *pszOmitted = NULL;
	int nOmittedType = -1;

	const int nWritten = ShellSave_WriteFieldBlock( cur, syms, "Save Header", &in, f, f.Count(),
	                                                kBaseTime, &nOmitted, &pszOmitted,
	                                                &nOmittedType );

	EXPECT_EQ( kHeaderBlock, nWritten )
		<< "the block is still written, and is exactly the block without that field";
	EXPECT_EQ( 1, nOmitted );
	EXPECT_STREQ( "targetname", pszOmitted )
		<< "the caller has to be able to name WHICH field, or the warning is "
		   "about the block rather than about the type";
	EXPECT_EQ( 2, nOmittedType );
}

TEST( SaveBlockOmission, TheClientsThreeDecalTypesAreNoLongerOmitted )
{
	// THE OTHER HALF OF THE FACT ABOVE, and the reason the swap is safe. These
	// three were what the omission path was measured on -- FIELD_VECTOR,
	// FIELD_SHORT and FIELD_POSITION_VECTOR, out of the engine's DECALLIST --
	// and encoding them was what took the load path from a segfault to a
	// complete walk. If one of them regressed to being omitted, the fact above
	// would go on passing on FIELD_STRING and say nothing about it.
	CFields f;
	f.Add( "position",          SOURCESHARP_FIELD_POSITION_VECTOR, 1, 0 );
	f.Add( "entityIndex",       SOURCESHARP_FIELD_SHORT,           1, 16 );
	f.Add( "impactPlaneNormal", SOURCESHARP_FIELD_VECTOR,          1, 20 );

	char scratch[ 64 ];
	memset( scratch, 0, sizeof( scratch ) );

	char buf[ 256 ];
	CShellSaveCursor wr( buf, sizeof( buf ) );
	CFakeSymbols syms;

	int nOmitted = -1;
	const int nWritten = ShellSave_WriteFieldBlock( wr, syms, "DECALLIST", scratch, f, f.Count(),
	                                                kBaseTime, &nOmitted, NULL, NULL );

	EXPECT_EQ( 0, nOmitted );
	EXPECT_EQ( ( SOURCESHARP_SAVE_RECORD_HEADER + 4 )
	           + ( SOURCESHARP_SAVE_RECORD_HEADER + 12 )
	           + ( SOURCESHARP_SAVE_RECORD_HEADER + 2 )
	           + ( SOURCESHARP_SAVE_RECORD_HEADER + 12 ), nWritten );
}

TEST( SaveBlockOmission, AnOmittedFieldDoesNotDisturbTheFieldsAroundIt )
{
	// The property that makes omitting safe HERE and unsafe in a flat layout:
	// every record carries its own name, so a missing one is a name the reader
	// never sees rather than an offset error.
	CFields f = SaveHeaderFields();
	f.Add( "targetname", 2 /* FIELD_STRING */, 1, 0 );

	const SaveHeaderShape in = AHeader();

	char buf[ 512 ];
	CFakeSymbols syms;

	CShellSaveCursor wr( buf, sizeof( buf ) );
	const int nWritten = ShellSave_WriteFieldBlock( wr, syms, "Save Header", &in, f, f.Count(),
	                                                kBaseTime, NULL, NULL, NULL );
	ASSERT_GT( nWritten, 0 );

	SaveHeaderShape out;
	memset( &out, 0xcd, sizeof( out ) );

	CShellSaveCursor rd( buf, nWritten );
	ASSERT_EQ( nWritten, ShellSave_ReadFieldBlock( rd, syms, "Save Header", &out, f, f.Count(),
	                                               kBaseTime, NULL ) );

	EXPECT_EQ( in.skillLevel, out.skillLevel );
	EXPECT_STREQ( in.mapName, out.mapName );
	EXPECT_STREQ( in.skyName, out.skyName );
	EXPECT_EQ( in.time, out.time );
}

TEST( SaveBlockOmission, ABlockOfNothingButUnencodableFieldsIsStillAWellFormedBlock )
{
	// The case that matters for the STREAM: an empty block is still a block, so
	// the engine's next read lands where the engine thinks it does. A REFUSED
	// block would take those bytes out from under it.
	//
	// Both types here are ones no widening can reach without new machinery -- a
	// string_t is an index into an engine pool, and an EHANDLE needs the entity
	// table this port deliberately leaves empty -- so this fact does not expire
	// the way the FIELD_POSITION_VECTOR one did.
	CFields f;
	f.Add( "targetname",  2 /* FIELD_STRING  */, 1, 0 );
	f.Add( "m_hOwner",   13 /* FIELD_EHANDLE */, 1, 8 );

	char scratch[ 64 ];
	memset( scratch, 0, sizeof( scratch ) );

	char buf[ 256 ];
	CShellSaveCursor wr( buf, sizeof( buf ) );
	CFakeSymbols syms;

	int nOmitted = 0;
	const int nWritten = ShellSave_WriteFieldBlock( wr, syms, "DECALLIST", scratch, f, f.Count(),
	                                                kBaseTime, &nOmitted, NULL, NULL );

	EXPECT_EQ( SOURCESHARP_SAVE_RECORD_HEADER + 4, nWritten )
		<< "the count record and nothing else";
	EXPECT_EQ( 2, nOmitted );

	int nCount = -1;
	memcpy( &nCount, buf + 4, 4 );
	EXPECT_EQ( 0, nCount ) << "and the count has to say so, or the reader looks "
	                          "for records that are not there";
}

//-----------------------------------------------------------------------------
// Read refusals.
//-----------------------------------------------------------------------------
TEST( SaveBlockRead, ABlockWrittenUnderAnotherNameIsRefusedAndTheCursorDoesNotMove )
{
	// The failure this is really about: the engine reading one more LIGHTSTYLE
	// than was written, and landing on whatever block came next.
	const CFields f = SaveHeaderFields();
	const SaveHeaderShape in = AHeader();

	char buf[ 512 ];
	CFakeSymbols syms;
	CShellSaveCursor wr( buf, sizeof( buf ) );
	const int nWritten = ShellSave_WriteFieldBlock( wr, syms, "Save Header", &in, f, f.Count(),
	                                                kBaseTime, NULL, NULL, NULL );
	ASSERT_GT( nWritten, 0 );

	CUntouched dest;
	CShellSaveCursor rd( buf, nWritten );
	const char *pszRefusal = NULL;

	EXPECT_EQ( -1, ShellSave_ReadFieldBlock( rd, syms, "LIGHTSTYLE", &dest.out, f, f.Count(),
	                                         kBaseTime, &pszRefusal ) );
	EXPECT_TRUE( pszRefusal != NULL );
	EXPECT_TRUE( dest.StillUntouched() );
	EXPECT_EQ( 0, rd.Pos() )
		<< "stock rewinds the header it read, so the caller's cursor is where a "
		   "recovery would have to start";
}

TEST( SaveBlockRead, BytesThatAreNotABlockHeaderAtAllAreRefused )
{
	// A cursor out of step, or a `.sav` from a build with a different format.
	char buf[ 64 ];
	memset( buf, 0x5a, sizeof( buf ) );

	const CFields f = SaveHeaderFields();
	CFakeSymbols syms;
	CUntouched dest;
	CShellSaveCursor rd( buf, sizeof( buf ) );
	const char *pszRefusal = NULL;

	EXPECT_EQ( -1, ShellSave_ReadFieldBlock( rd, syms, "Save Header", &dest.out, f, f.Count(),
	                                         kBaseTime, &pszRefusal ) );
	EXPECT_TRUE( pszRefusal != NULL );
	EXPECT_TRUE( dest.StillUntouched() );
	EXPECT_EQ( 0, rd.Pos() );
}

TEST( SaveBlockRead, EveryTruncationOfABlockIsRefusedOrEmptyAndNeverHalfFilled )
{
	// The sweep, in the shape SavedEntityCodecTests uses for the same reason: a
	// `.sav` is a file a player can truncate, copy between machines or
	// hand-edit, and every prefix of a valid block is a thing this can be
	// handed.
	const CFields f = SaveHeaderFields();
	const SaveHeaderShape in = AHeader();

	char buf[ 512 ];
	CFakeSymbols syms;
	CShellSaveCursor wr( buf, sizeof( buf ) );
	const int nWritten = ShellSave_WriteFieldBlock( wr, syms, "Save Header", &in, f, f.Count(),
	                                                kBaseTime, NULL, NULL, NULL );
	ASSERT_EQ( kHeaderBlock, nWritten );

	for ( int nCut = 0; nCut < nWritten; nCut++ )
	{
		SaveHeaderShape out;
		memset( &out, 0xcd, sizeof( out ) );

		CShellSaveCursor rd( buf, nCut );
		const char *pszRefusal = NULL;

		const int n = ShellSave_ReadFieldBlock( rd, syms, "Save Header", &out, f, f.Count(),
		                                        kBaseTime, &pszRefusal );

		ASSERT_EQ( -1, n ) << "a " << nCut << "-byte prefix of a " << nWritten
		                   << "-byte block was accepted";
		ASSERT_TRUE( pszRefusal != NULL ) << "at " << nCut;

		// AND THE DESTINATION NEVER CARRIES A VALUE OUT OF A TRUNCATED FILE,
		// which is the property that matters and is NOT "it is untouched".
		// A refusal before the block header is read leaves the struct exactly as
		// the caller had it (0xcd here); a refusal after it leaves the struct
		// CLEARED, because stock empties the fields before reading records so
		// that a field the file does not carry restores as zero rather than as
		// whatever was in the caller's memory. Both are correct. What is not is
		// a name assembled from bytes that are not there.
		ASSERT_STRNE( "dm_lockdown", out.mapName ) << "at " << nCut;
		ASSERT_STRNE( "sky_day01_01", out.skyName ) << "at " << nCut;

		const bool bCleared = ( out.mapName[ 0 ] == 0 );
		const bool bUntouched = ( (unsigned char)out.mapName[ 0 ] == 0xcd );
		ASSERT_TRUE( bCleared || bUntouched )
			<< "at " << nCut << ": a " << nCut << "-byte prefix left the "
			<< "destination neither as it was nor cleared";
	}

	// ...and the whole thing still reads, so the sweep is not passing because
	// the block was unreadable all along.
	SaveHeaderShape out;
	memset( &out, 0, sizeof( out ) );
	CShellSaveCursor rd( buf, nWritten );
	EXPECT_EQ( nWritten, ShellSave_ReadFieldBlock( rd, syms, "Save Header", &out, f, f.Count(),
	                                               kBaseTime, NULL ) );
	EXPECT_STREQ( "dm_lockdown", out.mapName );
}

TEST( SaveBlockRead, AFieldTheFileCarriesAndThisBuildDoesNotIsSkippedByItsOwnWidth )
{
	// A `.sav` from a build whose datamap had one field more. The width in
	// front of the record is what makes the NEXT one findable, which is the
	// whole reason the format has one.
	CFields fWide = SaveHeaderFields();
	fWide.Add( "extra", SOURCESHARP_FIELD_INTEGER, 1, (int)offsetof( SaveHeaderShape, skillLevel ) );

	const SaveHeaderShape in = AHeader();

	char buf[ 512 ];
	CFakeSymbols syms;
	CShellSaveCursor wr( buf, sizeof( buf ) );
	const int nWritten = ShellSave_WriteFieldBlock( wr, syms, "Save Header", &in, fWide,
	                                                fWide.Count(), kBaseTime, NULL, NULL, NULL );
	ASSERT_GT( nWritten, 0 );

	const CFields fNarrow = SaveHeaderFields();
	SaveHeaderShape out;
	memset( &out, 0xcd, sizeof( out ) );

	CShellSaveCursor rd( buf, nWritten );
	EXPECT_EQ( nWritten, ShellSave_ReadFieldBlock( rd, syms, "Save Header", &out, fNarrow,
	                                               fNarrow.Count(), kBaseTime, NULL ) );
	EXPECT_STREQ( "dm_lockdown", out.mapName )
		<< "the fields around the unknown one still restore";
	EXPECT_STREQ( "sky_day01_01", out.skyName );
}

TEST( SaveBlockRead, AFieldThisBuildHasAndTheFileDoesNotRestoresAsZero )
{
	// The other direction, and stock's: the destination is emptied before the
	// records are read, so a field the file never carried is zero rather than
	// whatever the caller's struct happened to hold.
	CFields fNarrow;
	fNarrow.Add( "skillLevel", SOURCESHARP_FIELD_INTEGER, 1, (int)offsetof( SaveHeaderShape, skillLevel ) );

	SaveHeaderShape in = AHeader();

	char buf[ 512 ];
	CFakeSymbols syms;
	CShellSaveCursor wr( buf, sizeof( buf ) );
	const int nWritten = ShellSave_WriteFieldBlock( wr, syms, "Save Header", &in, fNarrow,
	                                                fNarrow.Count(), kBaseTime, NULL, NULL, NULL );
	ASSERT_GT( nWritten, 0 );

	const CFields fWide = SaveHeaderFields();
	SaveHeaderShape out;
	memset( &out, 0xcd, sizeof( out ) );

	CShellSaveCursor rd( buf, nWritten );
	ASSERT_EQ( nWritten, ShellSave_ReadFieldBlock( rd, syms, "Save Header", &out, fWide,
	                                               fWide.Count(), kBaseTime, NULL ) );

	EXPECT_EQ( 2, out.skillLevel ) << "the one field the file has";
	EXPECT_STREQ( "", out.mapName ) << "and the ones it does not are zero, not 0xcd";
	EXPECT_EQ( 0, out.mapVersion );
}

TEST( SaveBlockRead, ANegativeFieldCountOffAFileIsRefusedRatherThanLoopedOn )
{
	const CFields f = SaveHeaderFields();
	CFakeSymbols syms;

	char buf[ 64 ];
	memset( buf, 0, sizeof( buf ) );

	const short nSize = 4;
	const short nToken = (short)syms.FindCreate( "Save Header" );
	const int nCount = -12345;
	memcpy( buf + 0, &nSize, 2 );
	memcpy( buf + 2, &nToken, 2 );
	memcpy( buf + 4, &nCount, 4 );

	CUntouched dest;
	CShellSaveCursor rd( buf, sizeof( buf ) );
	const char *pszRefusal = NULL;

	EXPECT_EQ( -1, ShellSave_ReadFieldBlock( rd, syms, "Save Header", &dest.out, f, f.Count(),
	                                         kBaseTime, &pszRefusal ) );
	EXPECT_TRUE( pszRefusal != NULL );
	EXPECT_TRUE( dest.StillUntouched() );
}

TEST( SaveBlockRead, ANullSourceOrStructIsRefusedRatherThanDereferenced )
{
	const CFields f = SaveHeaderFields();
	CFakeSymbols syms;
	char buf[ 64 ];
	memset( buf, 0, sizeof( buf ) );
	SaveHeaderShape out;

	CShellSaveCursor rdNull( NULL, 64 );
	EXPECT_EQ( -1, ShellSave_ReadFieldBlock( rdNull, syms, "Save Header", &out, f, f.Count(),
	                                         kBaseTime, NULL ) );

	CShellSaveCursor rd( buf, sizeof( buf ) );
	EXPECT_EQ( -1, ShellSave_ReadFieldBlock( rd, syms, "Save Header", NULL, f, f.Count(),
	                                         kBaseTime, NULL ) );
	EXPECT_EQ( -1, ShellSave_ReadFieldBlock( rd, syms, NULL, &out, f, f.Count(),
	                                         kBaseTime, NULL ) );
}

//-----------------------------------------------------------------------------
// The cursor, which is what every bound above is actually made of.
//-----------------------------------------------------------------------------
TEST( SaveCursor, AnOverrunPoisonsItRatherThanClamping )
{
	// The property the truncation sweep rests on: once a read has run off the
	// end, no LATER read may succeed, or a truncated file produces a struct
	// filled from whichever fields happened to fit.
	char buf[ 8 ];
	memset( buf, 0, sizeof( buf ) );

	CShellSaveCursor cur( buf, 4 );
	int nJunk = 0;

	EXPECT_FALSE( cur.Read( &nJunk, 8 ) );
	EXPECT_TRUE( cur.Overflowed() );
	EXPECT_FALSE( cur.Read( &nJunk, 1 ) ) << "and nothing after it moves either";
	EXPECT_FALSE( cur.Skip( 1 ) );
	EXPECT_TRUE( cur.Peek( 1 ) == NULL );
}

TEST( SaveCursor, ANullBufferIsOverflowedFromTheStart )
{
	CShellSaveCursor cur( NULL, 1024 );
	int nJunk = 0;

	EXPECT_TRUE( cur.Overflowed() );
	EXPECT_FALSE( cur.Read( &nJunk, 4 ) );
	EXPECT_FALSE( cur.Write( &nJunk, 4 ) );
}

TEST( SaveCursor, AWriteThatWouldOverrunWritesNothingAtAll )
{
	char buf[ 16 ];
	memset( buf, 0x5a, sizeof( buf ) );

	CShellSaveCursor cur( buf, 4 );
	const int nValue = 0x11223344;

	EXPECT_FALSE( cur.Write( &nValue, 8 ) );
	for ( size_t i = 0; i < sizeof( buf ); i++ )
		ASSERT_EQ( (char)0x5a, buf[ i ] ) << "byte " << i;
}

//-----------------------------------------------------------------------------
// Names, which are the keys the engine resolves.
//-----------------------------------------------------------------------------
TEST( SaveFieldNames, TheyMatchCaseInsensitivelyBecauseCRestoreDoes )
{
	EXPECT_TRUE( ShellSave_NamesMatch( "mapName", "MAPNAME" ) );
	EXPECT_TRUE( ShellSave_NamesMatch( "", "" ) );
	EXPECT_FALSE( ShellSave_NamesMatch( "mapName", "mapNames" ) )
		<< "a prefix is not a match -- that is the half-restore this prevents";
	EXPECT_FALSE( ShellSave_NamesMatch( "mapName", NULL ) )
		<< "a token with no string behind it matches nothing, which is what a "
		   "token off another build's file looks like";
}

//-----------------------------------------------------------------------------
// The buffer size SaveInit asks for.
//-----------------------------------------------------------------------------
TEST( SaveBufferSize, ItHoldsAFullMeasuredSaveManyTimesOver )
{
	// MEASURED LIVE ON dm_lockdown AT `-maxplayers 1`, AND THE NUMBER MOVED BY
	// TWO ORDERS OF MAGNITUDE IN STAGE 2. The engine's own structs are 1,380
	// bytes; the managed entity block beside them is 152,795 for 482 records.
	// The old figure here was 5,439 and it is what made 512 KiB look like a
	// two-hundred-times margin when it had become a three-times one.
	const int nMeasuredSave = 1380 + 152795;

	EXPECT_LT( nMeasuredSave * 10, SOURCESHARP_SAVE_BUFFER_BYTES )
		<< "a measured save is " << nMeasuredSave << " bytes against a "
		<< SOURCESHARP_SAVE_BUFFER_BYTES << "-byte buffer";
}

//-----------------------------------------------------------------------------
// THE BLOB BLOCK -- the managed entity state, framed like stock and opaque
// inside.
//
// A SECOND KIND OF BLOCK, and the facts that matter are the ones about its
// EDGES: a payload larger than a record's `short` width, a file that stops in
// the middle of it, and a save that carries no such block at all -- which is
// every `.sav` this build wrote before stage 2 and which must still load.
//-----------------------------------------------------------------------------
namespace
{

std::vector< unsigned char > Pattern( int nBytes )
{
	std::vector< unsigned char > v( (size_t)nBytes );

	for ( int i = 0; i < nBytes; i++ )
		v[ (size_t)i ] = (unsigned char)( ( i * 31 + 7 ) & 0xff );

	return v;
}

} // namespace

TEST( SaveBlobBlock, ItComesBackByteForByte )
{
	std::vector< unsigned char > buffer( 4096 );
	CFakeSymbols symbols;

	const std::vector< unsigned char > payload = Pattern( 900 );

	CShellSaveCursor write( &buffer[ 0 ], (int)buffer.size() );
	ASSERT_GT( ShellSave_WriteBlobBlock( write, symbols, "SSENTITY",
	                                     &payload[ 0 ], (int)payload.size() ), 0 );

	CShellSaveCursor read( &buffer[ 0 ], write.Pos() );
	std::vector< unsigned char > back( payload.size() );
	const char *pszRefusal = NULL;

	EXPECT_EQ( (int)payload.size(),
	           ShellSave_ReadBlobBlock( read, symbols, "SSENTITY", &back[ 0 ],
	                                    (int)back.size(), &pszRefusal ) );
	EXPECT_TRUE( pszRefusal == NULL );
	EXPECT_EQ( 0, memcmp( &payload[ 0 ], &back[ 0 ], payload.size() ) );
}

TEST( SaveBlobBlock, APayloadWiderThanARecordIsChunkedAndStillExact )
{
	// THE FACT THE WHOLE CHUNKING EXISTS FOR. A record's width is a `short`, so
	// one record cannot carry more than 32,767 bytes -- and the measured
	// managed block for dm_lockdown is 152,795. A codec that wrote the length
	// as a short would wrap it silently.
	std::vector< unsigned char > buffer( 400 * 1024 );
	CFakeSymbols symbols;

	const std::vector< unsigned char > payload = Pattern( 152795 );

	CShellSaveCursor write( &buffer[ 0 ], (int)buffer.size() );
	ASSERT_GT( ShellSave_WriteBlobBlock( write, symbols, "SSENTITY",
	                                     &payload[ 0 ], (int)payload.size() ), 0 );

	CShellSaveCursor read( &buffer[ 0 ], write.Pos() );
	std::vector< unsigned char > back( payload.size() );

	EXPECT_EQ( (int)payload.size(),
	           ShellSave_ReadBlobBlock( read, symbols, "SSENTITY", &back[ 0 ],
	                                    (int)back.size(), NULL ) );
	EXPECT_EQ( 0, memcmp( &payload[ 0 ], &back[ 0 ], payload.size() ) );
}

TEST( SaveBlobBlock, SizingDoesNotConsumeTheBlock )
{
	// SIZE-THEN-FILL, and the property that makes it usable: the sizing call
	// leaves the cursor exactly where it found it, so the filling call reads
	// the same block rather than whatever follows it.
	std::vector< unsigned char > buffer( 4096 );
	CFakeSymbols symbols;

	const std::vector< unsigned char > payload = Pattern( 500 );

	CShellSaveCursor write( &buffer[ 0 ], (int)buffer.size() );
	ShellSave_WriteBlobBlock( write, symbols, "SSENTITY", &payload[ 0 ], 500 );

	CShellSaveCursor read( &buffer[ 0 ], write.Pos() );

	EXPECT_EQ( 500, ShellSave_ReadBlobBlock( read, symbols, "SSENTITY", NULL, 0, NULL ) );
	EXPECT_EQ( 0, read.Pos() ) << "the sizing call must not move the cursor";

	std::vector< unsigned char > back( 500 );
	EXPECT_EQ( 500, ShellSave_ReadBlobBlock( read, symbols, "SSENTITY", &back[ 0 ], 500, NULL ) );
	EXPECT_EQ( write.Pos(), read.Pos() ) << "the filling call consumes it exactly once";
}

TEST( SaveBlobBlock, ASaveWithNoSuchBlockIsNotAnError )
{
	// EVERY `.sav` THIS BUILD WROTE BEFORE STAGE 2 IS THIS CASE, and it must
	// still load. The cursor sits at the end of the game's buffer, there is no
	// block header to read, and the answer is "no block" rather than a refusal.
	std::vector< unsigned char > buffer( 64 );
	CFakeSymbols symbols;

	CShellSaveCursor read( &buffer[ 0 ], 0 );
	const char *pszRefusal = NULL;

	EXPECT_EQ( kShellSaveNoBlock,
	           ShellSave_ReadBlobBlock( read, symbols, "SSENTITY", NULL, 0, &pszRefusal ) );
	EXPECT_TRUE( pszRefusal == NULL ) << "an absent block is not damage";
}

TEST( SaveBlobBlock, AnotherBlocksHeaderIsLeftWhereItWas )
{
	// The reader must not swallow bytes it does not understand: the engine
	// walks the game's buffer sequentially, so a cursor moved past somebody
	// else's block moves every block after it.
	std::vector< unsigned char > buffer( 4096 );
	CFakeSymbols symbols;

	SaveHeaderShape header;
	memset( &header, 0, sizeof( header ) );
	const CFields fields = SaveHeaderFields();

	CShellSaveCursor write( &buffer[ 0 ], (int)buffer.size() );
	ASSERT_GT( ShellSave_WriteFieldBlock( write, symbols, "Save Header", &header,
	                                      fields, fields.Count(), 0.0f,
	                                      NULL, NULL, NULL ), 0 );

	CShellSaveCursor read( &buffer[ 0 ], write.Pos() );

	EXPECT_EQ( kShellSaveNoBlock,
	           ShellSave_ReadBlobBlock( read, symbols, "SSENTITY", NULL, 0, NULL ) );
	EXPECT_EQ( 0, read.Pos() ) << "somebody else's block is left for them";
}

TEST( SaveBlobBlock, EveryTruncationIsRefusedAndNothingIsCopied )
{
	// THE SWEEP, and it is the fact worth having: a file can stop at any byte.
	// A prefix that still parses as a shorter payload would hand
	// SavedEntityCodec a truncated block, and its own trailing-byte and count
	// checks would then be arguing with damage rather than with a version.
	std::vector< unsigned char > buffer( 4096 );
	CFakeSymbols symbols;

	const std::vector< unsigned char > payload = Pattern( 300 );

	CShellSaveCursor write( &buffer[ 0 ], (int)buffer.size() );
	const int nBlock = ShellSave_WriteBlobBlock( write, symbols, "SSENTITY",
	                                             &payload[ 0 ], 300 );
	ASSERT_GT( nBlock, 0 );

	for ( int nCut = 1; nCut < nBlock; nCut++ )
	{
		CShellSaveCursor read( &buffer[ 0 ], nCut );
		std::vector< unsigned char > back( 300, 0xEE );

		const int nAnswer = ShellSave_ReadBlobBlock( read, symbols, "SSENTITY",
		                                             &back[ 0 ], 300, NULL );

		EXPECT_NE( 300, nAnswer ) << "a block cut at " << nCut
			<< " must not read as a whole one";

		if ( nAnswer < 0 )
		{
			for ( size_t i = 0; i < back.size(); i++ )
				ASSERT_EQ( 0xEE, back[ i ] ) << "a refusal copied byte " << i;
		}
	}
}

TEST( SaveBlobBlock, ANegativeChunkCountIsRefused )
{
	// The count comes off a file. Written by hand rather than by the writer,
	// because the writer cannot produce it -- which is the point.
	std::vector< unsigned char > buffer( 64, 0 );
	CFakeSymbols symbols;

	const short nSize = 4;
	const short nToken = (short)symbols.FindCreate( "SSENTITY" );
	const int nChunks = -1;

	memcpy( &buffer[ 0 ], &nSize, 2 );
	memcpy( &buffer[ 2 ], &nToken, 2 );
	memcpy( &buffer[ 4 ], &nChunks, 4 );

	CShellSaveCursor read( &buffer[ 0 ], 8 );
	const char *pszRefusal = NULL;

	EXPECT_EQ( -1, ShellSave_ReadBlobBlock( read, symbols, "SSENTITY", NULL, 0, &pszRefusal ) );
	EXPECT_TRUE( pszRefusal != NULL );
	EXPECT_EQ( 0, read.Pos() );
}

//-----------------------------------------------------------------------------
// THE GATE THAT DECIDES WHETHER A LOAD HAS A PLAYER IN IT.
//
// ShellSave_LoadGameNeedsPlayer's header says why: on a load-game the engine
// does not call ClientPutInServer, so nothing has created the player and
// ClientActive has to. Stock does not need
// this because ITS Restore builds the player out of the save's entity table
// and this port writes no entity table.
//
// FOUR FACTS FOR FOUR INPUTS, AND THE POSITIVE ONE FIRST. A gate is only worth
// testing if the input would otherwise be handled: the "true" case is what
// makes the three "false" cases mean something, because a predicate that
// answered false to everything would satisfy them all.
//
// The creation itself is NOT here and cannot be -- it needs the entity
// factory, the edict array and the managed runtime, which is why
// shell_gameclients.cpp has no gtest of its own (shell_clientsteps_test.cpp
// says the same about the file next to it). What is here is the half with two
// silent wrong answers.
//-----------------------------------------------------------------------------
TEST( LoadGamePlayer, ALoadWithNoPlayerBoundNeedsOne )
{
	EXPECT_TRUE( ShellSave_LoadGameNeedsPlayer( true, false ) );
}

TEST( LoadGamePlayer, ALoadThatALREADYHasAPlayerIsLeftAlone )
{
	// The engine would have to have called ClientPutInServer after all, or a
	// previous pass created one. Building a second player onto a live client's
	// edict is the worse of the two wrong answers.
	EXPECT_FALSE( ShellSave_LoadGameNeedsPlayer( true, true ) );
}

TEST( LoadGamePlayer, AFreshConnectIsNotThisArmsBusiness )
{
	// bLoadGame false: ClientPutInServer HAS been called and is where the
	// player comes from. Answering true here would double-create on every
	// ordinary join, which is why the flag is the first term and not a
	// belt-and-braces second one.
	EXPECT_FALSE( ShellSave_LoadGameNeedsPlayer( false, false ) );
}

TEST( LoadGamePlayer, AFreshConnectWithItsPlayerAlreadyStandingIsAlsoNo )
{
	EXPECT_FALSE( ShellSave_LoadGameNeedsPlayer( false, true ) );
}

//=============================================================================
// THE TWO HEADERS ARE ONE, AND THE PROSE THAT LEFT IS FINDABLE.
//
// plan_shim.md §5 3e. shell_savegame.h and save_field_codec.h were 1,478 lines
// between them and 620 of those were comment. The two are one header now, and
// every run of eight or more comment lines became a section of
// docs/managed-saverestore.md with a pointer left where it stood.
//
// WHAT CAN GO WRONG, AND WHY NEITHER FAILURE IS VISIBLE ANY OTHER WAY:
//
//   * A POINTER TO A SECTION THAT IS NOT THERE. The header compiles, every
//     test passes, and a reader following the note finds nothing. This is the
//     ordinary fate of a comment that names a document: somebody renames a
//     heading and the twelve references to it are not in the diff.
//
//   * THE SPLIT COMING BACK. Re-adding save_field_codec.h and an #include of
//     it compiles and links and passes every functional test in this file --
//     and leaves two headers where the argument for one was that a reader
//     following a SaveWriteFields layout into the encoder that fills it should
//     not have to open a second file.
//=============================================================================

namespace
{

std::string ReadRelative( const char *pRelative )
{
	static const char *kPrefixes[] = { "", "../", "../../" };
	for ( size_t i = 0; i < sizeof( kPrefixes ) / sizeof( kPrefixes[ 0 ] ); i++ )
	{
		std::ifstream in( ( std::string( kPrefixes[ i ] ) + pRelative ).c_str() );
		if ( in )
		{
			std::ostringstream ss;
			ss << in.rdbuf();
			return ss.str();
		}
	}
	return std::string();
}

} // namespace

TEST( ShellSaveGameLayout, TheCodecHeaderIsNotBack )
{
	// The file itself. `ifstream` on a path that does not exist fails, which is
	// the whole check -- there is no content to compare.
	std::ifstream codec( "src/sourcesharp/game/shared/save_field_codec.h" );
	std::ifstream codec2( "sourcesharp/game/shared/save_field_codec.h" );
	std::ifstream codec3( "../src/sourcesharp/game/shared/save_field_codec.h" );

	EXPECT_FALSE( codec.good() || codec2.good() || codec3.good() )
		<< "save_field_codec.h is back. It was merged into shell_savegame.h at "
		   "plan_shim.md §5 3e because this header was its only includer and "
		   "both are engine-free; two headers is two places to look for four "
		   "inline functions.";

	const std::string header = ReadRelative( "src/sourcesharp/game/shared/shell_savegame.h" );
	ASSERT_FALSE( header.empty() ) << "shell_savegame.h did not open";

	EXPECT_EQ( header.find( "#include \"save_field_codec.h\"" ), std::string::npos )
		<< "shell_savegame.h includes save_field_codec.h again.";

	// And the codec really is in here, rather than the include having been
	// deleted and the bodies with it.
	for ( const char *pszFn : { "SaveField_KindOf", "SaveField_Bytes",
	                            "SaveField_Encode", "SaveField_Decode",
	                            "SaveField_Copy", "SaveField_Context" } )
	{
		EXPECT_NE( header.find( pszFn ), std::string::npos )
			<< pszFn << " is not in shell_savegame.h. The merge dropped it "
			   "rather than moving it.";
	}
}

TEST( ShellSaveGameLayout, EveryPointerNamesASectionThatExists )
{
	const std::string header = ReadRelative( "src/sourcesharp/game/shared/shell_savegame.h" );
	const std::string doc = ReadRelative( "docs/managed-saverestore.md" );

	ASSERT_FALSE( header.empty() ) << "shell_savegame.h did not open";
	ASSERT_FALSE( doc.empty() )
		<< "docs/managed-saverestore.md did not open. The header points into it "
		   "fifteen times and shell_gamedll.cpp names it as well; a missing "
		   "document turns every one of those into a dead reference.";

	const std::string kNeedle = "See docs/managed-saverestore.md, \"";

	int nFound = 0;
	for ( size_t pos = header.find( kNeedle ); pos != std::string::npos;
	      pos = header.find( kNeedle, pos + 1 ) )
	{
		const size_t open = pos + kNeedle.size();
		const size_t close = header.find( '"', open );
		ASSERT_NE( close, std::string::npos ) << "an unterminated pointer at " << pos;

		const std::string title = header.substr( open, close - open );
		nFound++;

		EXPECT_NE( doc.find( "## " + title ), std::string::npos )
			<< "shell_savegame.h points at docs/managed-saverestore.md section \""
			<< title << "\", which has no heading there. A renamed heading takes "
			   "every reference to it with it, and none of them is in the diff.";
	}

	// A FLOOR, so a scan that stopped matching cannot pass by finding nothing.
	EXPECT_GE( nFound, 10 )
		<< "only " << nFound << " pointers parsed out of shell_savegame.h; the "
		   "scan has stopped seeing them and the check above is vacuous.";
}
