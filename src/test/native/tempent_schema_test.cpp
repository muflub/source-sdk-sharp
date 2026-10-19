//========= SourceSharp — tests for the temp entity description ==============//
//
// plan_tmp.md Part 9, gate 2. Three things about a temp entity can be quietly
// wrong, and every one of them is arithmetic over the description rather than
// anything a running game would report:
//
//   a WIDTH one bit off, which is a bitstream that stops making sense partway
//     through and produces no log line anywhere;
//   an OFFSET outside the blob, which networks whatever is past it as a
//     plausible number;
//   a worst case over the engine's 192-byte event cap, which is a Host_Error
//     during a firefight rather than a refusal at startup.
//
// So all three live in tempent_schema.h with no engine headers under them, and
// this is what checks them. Nothing here links against dt_send.cpp; the half
// that needs a real SendTable is cs_sv_te_dump, and it checks the ENGINE's own
// view of the built props rather than recomputing this one.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/client/tempent_schema.h"

#include <stdio.h>
#include <string.h>

namespace
{

//-----------------------------------------------------------------------------
// A field, spelled the way a table row reads.
//-----------------------------------------------------------------------------
SourceSharpTeField_t Field( const char *pName, int nKind, int nOffset, int nSize,
                            int nBits, int nFlags,
                            int nBitsFrom = SOURCESHARP_TE_BITS_LITERAL,
                            float flLow = 0.0f, float flHigh = 0.0f )
{
	SourceSharpTeField_t f;
	memset( &f, 0, sizeof( f ) );

	f.pName      = pName;
	f.nKind      = nKind;
	f.nOffset    = nOffset;
	f.nSizeBytes = nSize;
	f.nBits      = nBits;
	f.nBitsFrom  = nBitsFrom;
	f.nFlags     = nFlags;
	f.flLow      = flLow;
	f.flHigh     = flHigh;

	return f;
}

SourceSharpTeClass_t Class( const char *pClassName, const char *pTableName,
                            const char *pBaseTable, int nBlobBytes,
                            const SourceSharpTeField_t *pFields, int nFieldCount,
                            int nRegisterClass = 1 )
{
	SourceSharpTeClass_t c;
	memset( &c, 0, sizeof( c ) );

	c.pClassName     = pClassName;
	c.pTableName     = pTableName;
	c.pBaseTable     = pBaseTable;
	c.nBlobBytes     = nBlobBytes;
	c.nRegisterClass = nRegisterClass;
	c.pFields        = pFields;
	c.nFieldCount    = nFieldCount;

	return c;
}

//=============================================================================//
// THE RESTATED CONSTANTS, CHECKED AGAINST THE HEADERS THAT OWN THEM.
//
// tempent_schema.h deliberately includes no engine headers, so it restates
// COORD_INTEGER_BITS and seven other widths. docs/writing-tests.md records what
// happens to a restated constant nobody checks: SupplyTiers() described three
// tiers for several steps after native had grown to seven, and nothing failed,
// because the only suite reading it also supplied it.
//
// So this READS the owning headers off disk and asserts the agreement, which is
// the same trick AbiParityTests uses on sourcesharp.h. It SKIPS with a printed
// reason when the source tree is not beside the binary rather than failing —
// but a skip that is silent is a check that stopped running, so it says so.
//=============================================================================//

// Walks up from the working directory looking for a file. The runner starts in
// src/, so one level is usually enough; the loop is for whoever runs the binary
// by hand from somewhere else.
bool ReadHeader( const char *pRelative, char *pOut, size_t nOutBytes )
{
	static const char *kPrefixes[] = { "", "../", "../../", "../../../" };

	for ( size_t i = 0; i < sizeof( kPrefixes ) / sizeof( kPrefixes[0] ); i++ )
	{
		char szPath[512];
		snprintf( szPath, sizeof( szPath ), "%s%s", kPrefixes[i], pRelative );

		FILE *pFile = fopen( szPath, "rb" );
		if ( !pFile )
			continue;

		const size_t nRead = fread( pOut, 1, nOutBytes - 1, pFile );
		fclose( pFile );

		pOut[ nRead ] = '\0';
		return true;
	}

	return false;
}

// The integer that follows "#define <name>" in a header's text, or -1.
int DefineValue( const char *pText, const char *pName )
{
	char szNeedle[128];
	snprintf( szNeedle, sizeof( szNeedle ), "#define\t%s", pName );

	const char *pAt = strstr( pText, pName );

	while ( pAt )
	{
		// Only a real #define line, not a use of the name.
		const char *pLine = pAt;
		while ( pLine > pText && pLine[-1] != '\n' )
			pLine--;

		if ( strncmp( pLine, "#define", 7 ) == 0 )
		{
			const char *pValue = pAt + strlen( pName );

			while ( *pValue == ' ' || *pValue == '\t' )
				pValue++;

			if ( *pValue >= '0' && *pValue <= '9' )
				return atoi( pValue );
		}

		pAt = strstr( pAt + 1, pName );
	}

	return -1;
}

TEST( TempEntSchemaParity, TheCoordConstantsAreTheOnesCoordsizeDeclares )
{
	char szText[ 32 * 1024 ];

	if ( !ReadHeader( "public/coordsize.h", szText, sizeof( szText ) ) )
	{
		printf( "[  SKIPPED ] public/coordsize.h not beside the binary; "
		        "the restated coord widths are UNCHECKED in this run\n" );
		return;
	}

	EXPECT_EQ( SOURCESHARP_TE_COORD_INTEGER_BITS,
	           DefineValue( szText, "COORD_INTEGER_BITS" ) );
	EXPECT_EQ( SOURCESHARP_TE_COORD_FRACTIONAL_BITS,
	           DefineValue( szText, "COORD_FRACTIONAL_BITS" ) );
	EXPECT_EQ( SOURCESHARP_TE_COORD_INTEGER_BITS_MP,
	           DefineValue( szText, "COORD_INTEGER_BITS_MP" ) );
	EXPECT_EQ( SOURCESHARP_TE_COORD_FRACTIONAL_BITS_MP_LOWPREC,
	           DefineValue( szText, "COORD_FRACTIONAL_BITS_MP_LOWPRECISION" ) );
	EXPECT_EQ( SOURCESHARP_TE_NORMAL_FRACTIONAL_BITS,
	           DefineValue( szText, "NORMAL_FRACTIONAL_BITS" ) );
}

TEST( TempEntSchemaParity, TheSymbolicWidthsAreTheOnesTheirHeadersDeclare )
{
	char szText[ 64 * 1024 ];

	if ( ReadHeader( "public/const.h", szText, sizeof( szText ) ) )
	{
		EXPECT_EQ( SOURCESHARP_TE_MAX_EDICT_BITS,   DefineValue( szText, "MAX_EDICT_BITS" ) );
		EXPECT_EQ( SOURCESHARP_TE_MODEL_INDEX_BITS, DefineValue( szText, "SP_MODEL_INDEX_BITS" ) );

		// EF_MAX_BITS is an enumerator rather than a #define, so it is read as
		// text either way; NUM_NETWORKED_EHANDLE_BITS is an expression over two
		// other symbols and cannot be read as a literal at all. Both are pinned
		// by the arithmetic below instead of by a scan that would answer -1 and
		// pass.
		EXPECT_TRUE( strstr( szText, "EF_MAX_BITS = 10" ) != NULL )
			<< "EF_MAX_BITS is still 10 in public/const.h";

		EXPECT_EQ( SOURCESHARP_TE_EHANDLE_BITS,
		           SOURCESHARP_TE_MAX_EDICT_BITS + DefineValue( szText, "NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS" ) )
			<< "NUM_NETWORKED_EHANDLE_BITS is MAX_EDICT_BITS plus the serial bits";
	}
	else
	{
		printf( "[  SKIPPED ] public/const.h not beside the binary; "
		        "MAX_EDICT_BITS and friends are UNCHECKED in this run\n" );
	}

	if ( ReadHeader( "game/shared/effect_dispatch_data.h", szText, sizeof( szText ) ) )
	{
		EXPECT_EQ( SOURCESHARP_TE_EFFECT_FLAG_BITS,
		           DefineValue( szText, "MAX_EFFECT_FLAG_BITS" ) );
		EXPECT_EQ( SOURCESHARP_TE_EFFECT_STRING_BITS,
		           DefineValue( szText, "MAX_EFFECT_DISPATCH_STRING_BITS" ) );
	}
	else
	{
		printf( "[  SKIPPED ] game/shared/effect_dispatch_data.h not beside the binary; "
		        "the effect dispatch widths are UNCHECKED in this run\n" );
	}

	if ( ReadHeader( "game/server/baseanimating.h", szText, sizeof( szText ) ) )
	{
		EXPECT_EQ( SOURCESHARP_TE_ANIM_SKIN_BITS,
		           DefineValue( szText, "ANIMATION_SKIN_BITS" ) );
	}
	else
	{
		printf( "[  SKIPPED ] game/server/baseanimating.h not beside the binary; "
		        "ANIMATION_SKIN_BITS is UNCHECKED in this run\n" );
	}
}

//=============================================================================//
// WIDTHS.
//=============================================================================//

TEST( TempEntWidth, SymbolicWidthsResolveToWhatStockSpells )
{
	EXPECT_EQ( 11, SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_MAX_EDICT, 0 ) );
	EXPECT_EQ( 13, SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_MODEL_INDEX, 0 ) );
	EXPECT_EQ( 10, SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_EF_MAX, 0 ) );
	EXPECT_EQ( 10, SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_ANIM_SKIN, 0 ) );
	EXPECT_EQ(  8, SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_EFFECT_FLAG, 0 ) );
	EXPECT_EQ( 10, SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_EFFECT_STRING, 0 ) );
	EXPECT_EQ( 21, SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_EHANDLE, 0 ) );

	// Q_log2( MAX_PLAYERS ) — 33 players, so 5 bits, which is what
	// te_playerdecal.cpp asks for. NOT six: Q_log2 is the position of the
	// highest set bit, not the width needed to hold the value.
	EXPECT_EQ( 5, SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_MAX_PLAYERS_LOG2, 0 ) );

	// A literal stays a literal, which is the whole of the rule: only the
	// widths that are SYMBOLS in stock's own table travel symbolically.
	EXPECT_EQ( 4,  SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_LITERAL, 4 ) );
	EXPECT_EQ( 24, SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_LITERAL, 24 ) );

	// A kind nobody knows answers -1 rather than a plausible number.
	EXPECT_EQ( -1, SourceSharpTe_ResolveBits( SOURCESHARP_TE_BITS_COUNT, 8 ) );
	EXPECT_EQ( -1, SourceSharpTe_ResolveBits( 99, 8 ) );
}

//-----------------------------------------------------------------------------
// The branch order of the engine's prop encoder, which is the one thing about this function
// that is easy to get backwards and expensive when it is.
//
// SendPropFloat ORs SPROP_NOSCALE into every prop declared with nBits <= 0
// (dt_send.cpp:274), so stock's COORD floats arrive here carrying both flags.
// Reading NOSCALE first prices them at 32 where the engine spends 22, and
// DT_TEBeamSpline — sixteen of them times three — would then be refused for
// 1584 bits it does not use.
//-----------------------------------------------------------------------------
TEST( TempEntWidth, CoordBeatsNoScaleBecauseTheEncoderChecksItFirst )
{
	const int nBoth = SOURCESHARP_TE_FLAG_COORD | SOURCESHARP_TE_FLAG_NOSCALE;

	EXPECT_EQ( 22, SourceSharpTe_FloatWireBits( -1, nBoth ) );
	EXPECT_EQ( 22, SourceSharpTe_FloatWireBits( -1, SOURCESHARP_TE_FLAG_COORD ) );
	EXPECT_EQ( 32, SourceSharpTe_FloatWireBits( -1, SOURCESHARP_TE_FLAG_NOSCALE ) );
}

TEST( TempEntWidth, EveryFloatEncodingCostsWhatBitbufSpends )
{
	// WriteBitCoord: intval flag, fractval flag, sign, 14 integer, 5 fraction.
	EXPECT_EQ( 22, SourceSharpTe_FloatWireBits( -1, SOURCESHARP_TE_FLAG_COORD ) );

	// WriteBitCoordMP, out of the MP bounds, which is the worst case.
	EXPECT_EQ( 22, SourceSharpTe_FloatWireBits( -1, SOURCESHARP_TE_FLAG_COORD_MP ) );
	EXPECT_EQ( 20, SourceSharpTe_FloatWireBits( -1, SOURCESHARP_TE_FLAG_COORD_MP_LOWPRECISION ) );
	EXPECT_EQ( 17, SourceSharpTe_FloatWireBits( -1, SOURCESHARP_TE_FLAG_COORD_MP_INTEGRAL ) );

	// WriteBitNormal: one sign bit and eleven fractional.
	EXPECT_EQ( 12, SourceSharpTe_FloatWireBits( -1, SOURCESHARP_TE_FLAG_NORMAL ) );

	// The clamped-range form is whatever width was declared.
	EXPECT_EQ(  8, SourceSharpTe_FloatWireBits( 8, SOURCESHARP_TE_FLAG_ROUNDDOWN ) );
	EXPECT_EQ( 10, SourceSharpTe_FloatWireBits( 10, SOURCESHARP_TE_FLAG_ROUNDUP ) );
	EXPECT_EQ( 16, SourceSharpTe_FloatWireBits( 16, 0 ) );
}

//-----------------------------------------------------------------------------
// A normal vector sends x, y and ONE BIT for z, which the receiver
// reconstructs. Three times a float would be 36 bits where the engine spends 25,
// and over sixteen array elements that is the difference between a table that
// fits and one that Host_Errors.
//-----------------------------------------------------------------------------
TEST( TempEntWidth, ANormalVectorIsTwoFloatsAndASignBit )
{
	EXPECT_EQ( 66, SourceSharpTe_VectorWireBits( -1, SOURCESHARP_TE_FLAG_COORD ) );
	EXPECT_EQ( 25, SourceSharpTe_VectorWireBits( -1, SOURCESHARP_TE_FLAG_NORMAL ) );
	EXPECT_EQ( 96, SourceSharpTe_VectorWireBits( -1, SOURCESHARP_TE_FLAG_NOSCALE ) );
}

//-----------------------------------------------------------------------------
// DT_TESparks, field by field, against plan_tmp.md Part 6.
//
//   DT_TEParticleSystem   m_vecOrigin[0..2]  float -1 COORD
//   DT_TESparks           m_nMagnitude       4u
//                         m_nTrailLength     4u
//                         m_vecDir           vec -1 COORD
//-----------------------------------------------------------------------------
TEST( TempEntWidth, SparksFieldsCostWhatPartSixSays )
{
	SourceSharpTeField_t origin =
		Field( "m_vecOrigin", SOURCESHARP_TE_PROP_FLOAT, 0, 4, -1,
		       SOURCESHARP_TE_FLAG_COORD | SOURCESHARP_TE_FLAG_VECTOR_ELEM );
	origin.nElems      = 3;
	origin.nElemStride = 4;

	const SourceSharpTeField_t magnitude =
		Field( "m_nMagnitude", SOURCESHARP_TE_PROP_INT, 12, 4, 4,
		       SOURCESHARP_TE_FLAG_UNSIGNED );

	const SourceSharpTeField_t dir =
		Field( "m_vecDir", SOURCESHARP_TE_PROP_VECTOR, 20, 12, -1,
		       SOURCESHARP_TE_FLAG_COORD );

	EXPECT_EQ( 22, SourceSharpTe_PropValueBits( &origin ) );
	EXPECT_EQ( 66, SourceSharpTe_FieldValueBits( &origin ) );
	EXPECT_EQ(  4, SourceSharpTe_FieldValueBits( &magnitude ) );
	EXPECT_EQ( 66, SourceSharpTe_FieldValueBits( &dir ) );
}

//=============================================================================//
// OFFSETS AND STORAGE.
//=============================================================================//

//-----------------------------------------------------------------------------
// The storage size decides the READ PROXY, not the wire width. SendPropInt
// picks SendProxy_Int8ToInt32 for a size of one and Int32ToInt32 for four
// (dt_send.cpp:492), so a byte field described as four bytes reads three bytes
// of whatever comes after it and sends a plausible number.
//-----------------------------------------------------------------------------
TEST( TempEntBlob, StorageSizeMustBeOneTheProxiesKnow )
{
	SourceSharpTeField_t f = Field( "m_nCount", SOURCESHARP_TE_PROP_INT, 0, 1, 8,
	                                SOURCESHARP_TE_FLAG_UNSIGNED );
	EXPECT_EQ( 1, SourceSharpTe_FieldBlobBytes( &f ) );

	f.nSizeBytes = 2;
	EXPECT_EQ( 2, SourceSharpTe_FieldBlobBytes( &f ) );

	f.nSizeBytes = 4;
	EXPECT_EQ( 4, SourceSharpTe_FieldBlobBytes( &f ) );

	// Three and eight are not sizes SendPropInt has a proxy for; it asserts and
	// falls back to the eight-bit one, which reads one byte of a four-byte
	// field. Refused here instead.
	f.nSizeBytes = 3;
	EXPECT_EQ( -1, SourceSharpTe_FieldBlobBytes( &f ) );

	f.nSizeBytes = 8;
	EXPECT_EQ( -1, SourceSharpTe_FieldBlobBytes( &f ) );
}

//-----------------------------------------------------------------------------
// ONE FIELD, N PROPS, which is what SENDINFO_VECTORELEM and SENDINFO_ARRAYELEM
// both expand to in stock's tables: DT_TEParticleSystem's m_vecOrigin is three
// SendPropFloats, DT_TEBreakModel's m_angRotation is three SendPropAngles, and
// DT_TEShatterSurface's m_uchFrontColor is three bytes.
//
// The C# side declares ONE field in each case — a Vector, a QAngle, a colour —
// so the writer has one thing to keep in step rather than three.
//-----------------------------------------------------------------------------
TEST( TempEntBlob, OneFieldCanBeThreePropsAndCostsThreePropsWorth )
{
	SourceSharpTeField_t origin =
		Field( "m_vecOrigin", SOURCESHARP_TE_PROP_FLOAT, 0, 4, -1,
		       SOURCESHARP_TE_FLAG_COORD | SOURCESHARP_TE_FLAG_VECTOR_ELEM );
	origin.nElems      = 3;
	origin.nElemStride = 4;

	EXPECT_EQ(  3, SourceSharpTe_FieldPropCount( &origin ) );
	EXPECT_EQ( 22, SourceSharpTe_PropValueBits( &origin ) );
	EXPECT_EQ( 66, SourceSharpTe_FieldValueBits( &origin ) );
	EXPECT_EQ( 12, SourceSharpTe_FieldBlobBytes( &origin ) );

	// Three colour bytes, the SENDINFO_ARRAYELEM shape: same mechanism, no
	// negated offsets.
	SourceSharpTeField_t colour =
		Field( "m_uchFrontColor", SOURCESHARP_TE_PROP_INT, 0, 1, 8,
		       SOURCESHARP_TE_FLAG_UNSIGNED );
	colour.nElems      = 3;
	colour.nElemStride = 1;

	EXPECT_EQ(  3, SourceSharpTe_FieldPropCount( &colour ) );
	EXPECT_EQ( 24, SourceSharpTe_FieldValueBits( &colour ) );
	EXPECT_EQ(  3, SourceSharpTe_FieldBlobBytes( &colour ) );

	// A stride shorter than the element overlaps the props, so one field's
	// bytes are read as another's — and the number that comes out is plausible.
	colour.nElemStride = 0;
	EXPECT_EQ( -1, SourceSharpTe_FieldBlobBytes( &colour ) );

	// One prop is still one prop, and needs no stride at all.
	SourceSharpTeField_t single =
		Field( "m_nMagnitude", SOURCESHARP_TE_PROP_INT, 0, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );
	EXPECT_EQ( 1, SourceSharpTe_FieldPropCount( &single ) );
	EXPECT_EQ( 4, SourceSharpTe_FieldBlobBytes( &single ) );
}

TEST( TempEntBlob, AVectorIsTwelveBytesAndAFloatIsFour )
{
	SourceSharpTeField_t v = Field( "m_vecDir", SOURCESHARP_TE_PROP_VECTOR, 0, 12, -1,
	                                SOURCESHARP_TE_FLAG_COORD );
	EXPECT_EQ( 12, SourceSharpTe_FieldBlobBytes( &v ) );

	v.nSizeBytes = 4;
	EXPECT_EQ( -1, SourceSharpTe_FieldBlobBytes( &v ) );

	SourceSharpTeField_t f = Field( "m_fScale", SOURCESHARP_TE_PROP_FLOAT, 0, 4, 8,
	                                SOURCESHARP_TE_FLAG_ROUNDDOWN );
	EXPECT_EQ( 4, SourceSharpTe_FieldBlobBytes( &f ) );

	f.nSizeBytes = 12;
	EXPECT_EQ( -1, SourceSharpTe_FieldBlobBytes( &f ) );
}

//=============================================================================//
// THE REGISTRY'S REFUSALS.
//=============================================================================//

// Nothing exists outside the registry in these tests unless a case says so.
bool NoExternalBases( const char *, void * ) { return false; }
bool EveryExternalBase( const char *, void * ) { return true; }

TEST( TempEntRegistry, TakesAWellFormedClass )
{
	CSourceSharpTeRegistry registry;
	registry.SetExternalBaseResolver( NoExternalBases, NULL );

	SourceSharpTeField_t fields[3];
	fields[0] = Field( "m_nMagnitude",   SOURCESHARP_TE_PROP_INT, 0, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );
	fields[1] = Field( "m_nTrailLength", SOURCESHARP_TE_PROP_INT, 4, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );
	fields[2] = Field( "m_vecDir",       SOURCESHARP_TE_PROP_VECTOR, 8, 12, -1, SOURCESHARP_TE_FLAG_COORD );

	const SourceSharpTeClass_t desc = Class( "CTESparks", "DT_TESparks", NULL, 20, fields, 3 );

	int nWhy = -1;
	const int iHandle = registry.Add( &desc, &nWhy );

	EXPECT_EQ( 0, iHandle );
	EXPECT_EQ( SOURCESHARP_TE_ACCEPTED, nWhy );
	EXPECT_EQ( 1, registry.Count() );
	EXPECT_EQ( 0, registry.FindByClassName( "CTESparks" ) );
	EXPECT_EQ( 0, registry.FindByTableName( "DT_TESparks" ) );
	EXPECT_EQ( -1, registry.FindByClassName( "CTEBubbles" ) );

	// The description is COPIED. It arrives in managed memory that the GC may
	// move, while a SendProp keeps the pointer it was handed for the life of
	// the process.
	EXPECT_NE( desc.pClassName, registry.At( 0 )->pClassName );
	EXPECT_STREQ( "CTESparks", registry.At( 0 )->pClassName );
	EXPECT_NE( fields[0].pName, registry.At( 0 )->pFields[0].pName );
	EXPECT_STREQ( "m_nMagnitude", registry.At( 0 )->pFields[0].pName );
}

//-----------------------------------------------------------------------------
// A SECOND CLASS OF ONE NAME. The failure it prevents has no symptom: two
// ServerClasses of one name go into the list the engine negotiates at connect,
// and the client begins instantiating and stops with nothing in any log (plan
// 2E, shell_standalone.cpp:1083).
//-----------------------------------------------------------------------------
TEST( TempEntRegistry, RefusesASecondClassOfTheSameName )
{
	CSourceSharpTeRegistry registry;
	registry.SetExternalBaseResolver( NoExternalBases, NULL );

	SourceSharpTeField_t field =
		Field( "m_nMagnitude", SOURCESHARP_TE_PROP_INT, 0, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );

	const SourceSharpTeClass_t first  = Class( "CTESparks", "DT_TESparks",  NULL, 4, &field, 1 );
	const SourceSharpTeClass_t second = Class( "CTESparks", "DT_TESparks2", NULL, 4, &field, 1 );

	int nWhy = -1;
	EXPECT_EQ( 0, registry.Add( &first, &nWhy ) );
	EXPECT_EQ( SOURCESHARP_TE_ACCEPTED, nWhy );

	EXPECT_EQ( -1, registry.Add( &second, &nWhy ) );
	EXPECT_EQ( SOURCESHARP_TE_REFUSED_DUP_CLASS, nWhy );
	EXPECT_EQ( 1, registry.Count() );
}

TEST( TempEntRegistry, RefusesASecondTableOfTheSameName )
{
	CSourceSharpTeRegistry registry;
	registry.SetExternalBaseResolver( NoExternalBases, NULL );

	SourceSharpTeField_t field =
		Field( "m_nMagnitude", SOURCESHARP_TE_PROP_INT, 0, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );

	const SourceSharpTeClass_t first  = Class( "CTESparks",  "DT_TESparks", NULL, 4, &field, 1 );
	const SourceSharpTeClass_t second = Class( "CTESparks2", "DT_TESparks", NULL, 4, &field, 1 );

	int nWhy = -1;
	EXPECT_EQ(  0, registry.Add( &first, &nWhy ) );
	EXPECT_EQ( -1, registry.Add( &second, &nWhy ) );
	EXPECT_EQ( SOURCESHARP_TE_REFUSED_DUP_TABLE, nWhy );
}

//-----------------------------------------------------------------------------
// TWO PROPS OF ONE NAME INSIDE ONE TABLE. The client matches send props to recv
// props BY NAME, so the second is paired against the first's recv prop and one
// of the two values is decoded into the wrong field — silently, and with a
// plausible number in it.
//-----------------------------------------------------------------------------
TEST( TempEntRegistry, RefusesTwoPropsOfOneNameInOneTable )
{
	CSourceSharpTeRegistry registry;
	registry.SetExternalBaseResolver( NoExternalBases, NULL );

	SourceSharpTeField_t fields[2];
	fields[0] = Field( "m_nCount", SOURCESHARP_TE_PROP_INT, 0, 4, 8, SOURCESHARP_TE_FLAG_UNSIGNED );
	fields[1] = Field( "m_nCount", SOURCESHARP_TE_PROP_INT, 4, 4, 8, SOURCESHARP_TE_FLAG_UNSIGNED );

	const SourceSharpTeClass_t desc = Class( "CTEThing", "DT_TEThing", NULL, 8, fields, 2 );

	int nWhy = -1;
	EXPECT_EQ( -1, registry.Add( &desc, &nWhy ) );
	EXPECT_EQ( SOURCESHARP_TE_REFUSED_DUP_FIELD, nWhy );
	EXPECT_EQ( 0, registry.Count() );
}

//-----------------------------------------------------------------------------
// A LATE REGISTRATION. A ServerClass links itself into g_pServerClassHead when
// it is constructed and the engine walks that list exactly once; one built after
// the walk is simply not in the list, and the first effect that claims it sends
// the engine into a table that was never flattened. There is no warning and no
// log line — shell_standalone.cpp:70 records the game printing an edict number
// and then jumping to address zero.
//-----------------------------------------------------------------------------
TEST( TempEntRegistry, RefusesAnythingAfterTheWindowShuts )
{
	CSourceSharpTeRegistry registry;
	registry.SetExternalBaseResolver( NoExternalBases, NULL );

	SourceSharpTeField_t field =
		Field( "m_nMagnitude", SOURCESHARP_TE_PROP_INT, 0, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );

	const SourceSharpTeClass_t early = Class( "CTESparks", "DT_TESparks", NULL, 4, &field, 1 );
	const SourceSharpTeClass_t late  = Class( "CTESmoke",  "DT_TESmoke",  NULL, 4, &field, 1 );

	int nWhy = -1;
	EXPECT_EQ( 0, registry.Add( &early, &nWhy ) );
	EXPECT_FALSE( registry.IsClosed() );

	registry.Close();

	EXPECT_TRUE( registry.IsClosed() );
	EXPECT_EQ( -1, registry.Add( &late, &nWhy ) );
	EXPECT_EQ( SOURCESHARP_TE_REFUSED_CLOSED, nWhy );
	EXPECT_EQ( 1, registry.Count() );
	EXPECT_EQ( -1, registry.FindByClassName( "CTESmoke" ) );
}

//-----------------------------------------------------------------------------
// A FIELD OUTSIDE THE BLOB. It does not crash and it does not warn: the prop
// reads whatever follows the blob in memory and networks it as state.
//-----------------------------------------------------------------------------
TEST( TempEntRegistry, RefusesAFieldPastTheEndOfTheBlob )
{
	CSourceSharpTeRegistry registry;
	registry.SetExternalBaseResolver( NoExternalBases, NULL );

	SourceSharpTeField_t fields[2];
	fields[0] = Field( "m_nMagnitude", SOURCESHARP_TE_PROP_INT, 0, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );
	fields[1] = Field( "m_vecDir", SOURCESHARP_TE_PROP_VECTOR, 4, 12, -1, SOURCESHARP_TE_FLAG_COORD );

	// The vector needs bytes 4..15 and the blob stops at 12.
	const SourceSharpTeClass_t tooSmall = Class( "CTESparks", "DT_TESparks", NULL, 12, fields, 2 );

	int nWhy = -1;
	EXPECT_EQ( -1, registry.Add( &tooSmall, &nWhy ) );
	EXPECT_EQ( SOURCESHARP_TE_REFUSED_OFFSET, nWhy );

	const SourceSharpTeClass_t exact = Class( "CTESparks", "DT_TESparks", NULL, 16, fields, 2 );
	EXPECT_EQ( 0, registry.Add( &exact, &nWhy ) );
	EXPECT_EQ( SOURCESHARP_TE_ACCEPTED, nWhy );

	// And a negative offset, which is what a VECTOR_ELEM prop's DECLARED offset
	// looks like once the engine has it. The description carries the positive
	// one; the negation happens where the SendProp is built, not here.
	SourceSharpTeField_t negative =
		Field( "m_fBad", SOURCESHARP_TE_PROP_FLOAT, -4, 4, 8, 0 );
	const SourceSharpTeClass_t backwards = Class( "CTEBad", "DT_TEBad", NULL, 16, &negative, 1 );

	EXPECT_EQ( -1, registry.Add( &backwards, &nWhy ) );
	EXPECT_EQ( SOURCESHARP_TE_REFUSED_OFFSET, nWhy );
}

//-----------------------------------------------------------------------------
// A BASE NOBODY HAS. A datatable prop pointing at a table that does not exist
// builds a table the client cannot resolve; the resolver is the one hook that
// lets a STAGED port work, because a lane landing DT_TESparks before
// DT_TEParticleSystem has moved is the ordinary state of this port for its
// whole length.
//-----------------------------------------------------------------------------
TEST( TempEntRegistry, RefusesABaseNobodyHasAndTakesOneNativeStillOwns )
{
	SourceSharpTeField_t field =
		Field( "m_nMagnitude", SOURCESHARP_TE_PROP_INT, 0, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );

	const SourceSharpTeClass_t derived =
		Class( "CTESparks", "DT_TESparks", "DT_TEParticleSystem", 4, &field, 1 );

	{
		CSourceSharpTeRegistry registry;
		registry.SetExternalBaseResolver( NoExternalBases, NULL );

		int nWhy = -1;
		EXPECT_EQ( -1, registry.Add( &derived, &nWhy ) );
		EXPECT_EQ( SOURCESHARP_TE_REFUSED_BASE, nWhy );
	}

	{
		CSourceSharpTeRegistry registry;
		registry.SetExternalBaseResolver( EveryExternalBase, NULL );

		int nWhy = -1;
		EXPECT_EQ( 0, registry.Add( &derived, &nWhy ) );
		EXPECT_EQ( SOURCESHARP_TE_ACCEPTED, nWhy );
	}

	{
		// And by registering the base here first, which is what the finished
		// port looks like.
		CSourceSharpTeRegistry registry;
		registry.SetExternalBaseResolver( NoExternalBases, NULL );

		SourceSharpTeField_t origin =
			Field( "m_vecOrigin", SOURCESHARP_TE_PROP_FLOAT, 0, 4, -1, SOURCESHARP_TE_FLAG_COORD );
		origin.nElems      = 3;
		origin.nElemStride = 4;

		const SourceSharpTeClass_t base =
			Class( "CTEParticleSystem", "DT_TEParticleSystem", NULL, 12, &origin, 1, 0 );

		int nWhy = -1;
		EXPECT_EQ( 0, registry.Add( &base, &nWhy ) );
		EXPECT_EQ( 1, registry.Add( &derived, &nWhy ) );
		EXPECT_EQ( SOURCESHARP_TE_ACCEPTED, nWhy );

		// A base table is a table and NOT a ServerClass while stock's own class
		// of that name is still registered. Two ServerClasses of one name is the
		// join that hangs.
		EXPECT_EQ( 0, registry.At( 0 )->nRegisterClass );
		EXPECT_EQ( 1, registry.At( 1 )->nRegisterClass );
	}
}

//=============================================================================//
// THE 192-BYTE CEILING.
//=============================================================================//

//-----------------------------------------------------------------------------
// DT_TESparks against the engine's event cap, with its base, which is the whole of what
// S-Phase 1 sends.
//
// Four props of value bits — three COORD floats at 22, two 4-bit ints, one
// COORD vector at 66 — plus seven index bits each and the message framing. The
// EXACT number is asserted rather than "it fits", because "it fits" would still
// pass if every width in the table were 32.
//-----------------------------------------------------------------------------
TEST( TempEntCeiling, SparksIsPricedExactly )
{
	CSourceSharpTeRegistry registry;
	registry.SetExternalBaseResolver( NoExternalBases, NULL );

	SourceSharpTeField_t origin =
		Field( "m_vecOrigin", SOURCESHARP_TE_PROP_FLOAT, 0, 4, -1, SOURCESHARP_TE_FLAG_COORD );
	origin.nElems      = 3;
	origin.nElemStride = 4;

	const SourceSharpTeClass_t base =
		Class( "CTEParticleSystem", "DT_TEParticleSystem", NULL, 12, &origin, 1, 0 );

	SourceSharpTeField_t sparks[3];
	sparks[0] = Field( "m_nMagnitude",   SOURCESHARP_TE_PROP_INT, 12, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );
	sparks[1] = Field( "m_nTrailLength", SOURCESHARP_TE_PROP_INT, 16, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );
	sparks[2] = Field( "m_vecDir",       SOURCESHARP_TE_PROP_VECTOR, 20, 12, -1, SOURCESHARP_TE_FLAG_COORD );

	const SourceSharpTeClass_t desc =
		Class( "CTESparks", "DT_TESparks", "DT_TEParticleSystem", 32, sparks, 3 );

	int nWhy = -1;
	ASSERT_EQ( 0, registry.Add( &base, &nWhy ) );
	ASSERT_EQ( 1, registry.Add( &desc, &nWhy ) );

	//  framing 32
	//  + 3 * ( 7 + 22 )   the base's COORD floats     =  87
	//  + 2 * ( 7 +  4 )   the two 4-bit ints          =  22
	//  +     ( 7 + 66 )   the COORD direction vector  =  73
	//  = 214 bits, 27 bytes, against a 1536-bit ceiling
	EXPECT_EQ( 214, registry.WorstCaseBits( 1 ) );
	EXPECT_LT( registry.WorstCaseBits( 1 ), SOURCESHARP_TE_MAX_EVENT_BITS );
}

//-----------------------------------------------------------------------------
// DT_TEBeamSpline, the table this ceiling exists for.
//
// plan 2B-bis prices it as a generic payload at 49 dwords — 196 bytes against a
// 192-byte cap, a Host_Error on the first spline ever sent — and stock's
// bit-packed form at about 132. This is that arithmetic, and it is what says
// SPROP_COORD is load-bearing rather than decoration.
//-----------------------------------------------------------------------------
TEST( TempEntCeiling, TheSplineFitsPackedAndWouldNotAsPlainFloats )
{
	SourceSharpTeField_t fields[2];
	fields[0] = Field( "m_nPoints", SOURCESHARP_TE_PROP_INT, 0, 4, 5, SOURCESHARP_TE_FLAG_UNSIGNED );
	fields[1] = Field( "m_vecPoints", SOURCESHARP_TE_PROP_ARRAY, 4, 0, -1, SOURCESHARP_TE_FLAG_COORD );
	fields[1].nElems      = 16;
	fields[1].nElemStride = 12;

	{
		CSourceSharpTeRegistry registry;
		registry.SetExternalBaseResolver( NoExternalBases, NULL );

		const SourceSharpTeClass_t desc =
			Class( "CTEBeamSpline", "DT_TEBeamSpline", NULL, 4 + 16 * 12, fields, 2 );

		int nWhy = -1;
		ASSERT_EQ( 0, registry.Add( &desc, &nWhy ) );

		//  framing 32 + ( 7 + 5 ) + ( 7 + 16 * 66 + 5 ) = 1112 bits, 139 bytes
		EXPECT_EQ( 1112, registry.WorstCaseBits( 0 ) );
		EXPECT_LT( registry.WorstCaseBits( 0 ), SOURCESHARP_TE_MAX_EVENT_BITS );
	}

	{
		// The same table with the coord flag dropped — sixteen vectors of three
		// whole floats, which is what a generic payload would have made of it.
		CSourceSharpTeRegistry registry;
		registry.SetExternalBaseResolver( NoExternalBases, NULL );

		fields[1].nFlags = SOURCESHARP_TE_FLAG_NOSCALE;

		const SourceSharpTeClass_t desc =
			Class( "CTEBeamSpline", "DT_TEBeamSpline", NULL, 4 + 16 * 12, fields, 2 );

		int nWhy = -1;
		EXPECT_EQ( -1, registry.Add( &desc, &nWhy ) );
		EXPECT_EQ( SOURCESHARP_TE_REFUSED_TOO_BIG, nWhy );
		EXPECT_EQ( 0, registry.Count() );
	}
}

//-----------------------------------------------------------------------------
// A table whose base this side cannot see is not priced at all, and that is a
// different answer from "it fits".
//
// Judging a size that was never computed is how a legal table gets refused for a
// number nobody produced — and during the staged port, every derived table whose
// base is still stock's is in exactly that position.
//-----------------------------------------------------------------------------
TEST( TempEntCeiling, ATableWithANativeBaseIsNotPricedRatherThanPricedAsZero )
{
	CSourceSharpTeRegistry registry;
	registry.SetExternalBaseResolver( EveryExternalBase, NULL );

	SourceSharpTeField_t field =
		Field( "m_nMagnitude", SOURCESHARP_TE_PROP_INT, 0, 4, 4, SOURCESHARP_TE_FLAG_UNSIGNED );

	const SourceSharpTeClass_t desc =
		Class( "CTESparks", "DT_TESparks", "DT_TEParticleSystem", 4, &field, 1 );

	int nWhy = -1;
	ASSERT_EQ( 0, registry.Add( &desc, &nWhy ) );

	EXPECT_EQ( -1, registry.WorstCaseBits( 0 ) );
}

TEST( TempEntCeiling, RefusalsHaveWordsOnThem )
{
	EXPECT_STREQ( "accepted", SourceSharpTe_RefusalText( SOURCESHARP_TE_ACCEPTED ) );

	for ( int nWhy = SOURCESHARP_TE_REFUSED_CLOSED; nWhy <= SOURCESHARP_TE_REFUSED_TOO_BIG; nWhy++ )
	{
		EXPECT_STRNE( "refused", SourceSharpTe_RefusalText( nWhy ) )
			<< "refusal " << nWhy << " says which one it is";
	}

	EXPECT_STREQ( "refused", SourceSharpTe_RefusalText( 4242 ) );
}

//-----------------------------------------------------------------------------
// The prop-name pool (SourceSharpTe_InternPropName). A SendProp keeps the name
// pointer it was handed, so the pool's answers must be one pointer per name,
// and stay put while the pool grows.
//-----------------------------------------------------------------------------
TEST( TempEntPropNames, TheSameNameTwiceIsTheSamePointer )
{
	SourceSharpTePropNamePool_t pool;
	char szFirst[] = "m_vecOrigin[1]";
	const char *pA = SourceSharpTe_InternPropName( pool, szFirst );
	szFirst[0] = 'X';   // the caller's buffer is not what was kept
	const char *pB = SourceSharpTe_InternPropName( pool, "m_vecOrigin[1]" );

	EXPECT_EQ( pA, pB );
	EXPECT_STREQ( "m_vecOrigin[1]", pA );
	EXPECT_EQ( 1u, pool.size() );
}

TEST( TempEntPropNames, DifferentNamesAndDifferentCaseAreDifferentPointers )
{
	SourceSharpTePropNamePool_t pool;
	const char *pA = SourceSharpTe_InternPropName( pool, "m_nModelIndex" );
	const char *pB = SourceSharpTe_InternPropName( pool, "m_nmodelindex" );
	const char *pC = SourceSharpTe_InternPropName( pool, "m_vecOrigin" );

	EXPECT_NE( pA, pB ) << "the pool is case-sensitive, as the strcmp walk it replaced";
	EXPECT_NE( pA, pC );
	EXPECT_STREQ( "m_nModelIndex", pA );
	EXPECT_STREQ( "m_nmodelindex", pB );
}

TEST( TempEntPropNames, APointerSurvivesManyLaterInserts )
{
	SourceSharpTePropNamePool_t pool;
	const char *pFirst = SourceSharpTe_InternPropName( pool, "m_flRadius" );

	char szName[ 32 ];
	for ( int i = 0; i < 5000; i++ )
	{
		snprintf( szName, sizeof( szName ), "m_prop%04d", i );
		SourceSharpTe_InternPropName( pool, szName );
	}

	ASSERT_EQ( 5001u, pool.size() );
	EXPECT_STREQ( "m_flRadius", pFirst );
	EXPECT_EQ( pFirst, SourceSharpTe_InternPropName( pool, "m_flRadius" ) );
}

} // namespace
