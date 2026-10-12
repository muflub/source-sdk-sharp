//====== SourceSharp — the engine's save-header field codec ==================//
//
// game/shared/save_field_codec.h encodes the `typedescription_t` arrays the
// ENGINE hands the game during a save — its own save header and its light
// styles. It is three field types, and that is a measurement rather than a
// choice: docs/managed-saverestore.md section 4a, one instrumented run at
// `-maxplayers 1`, all 37 fields the engine asked for.
//
// TWO KINDS OF FACT HERE AND THE SECOND KIND IS THE POINT.
//
// The round trips are easy and are four of these. The rest are about a field
// this codec does NOT know, because that is the failure with no symptom: a
// codec that skipped an unrecognised field would write a block one field short,
// and every later field would be read from the wrong offset — a map name out of
// the middle of a light style, restored without complaint. The measurement is
// of ONE engine build; a later one adding a field to its save header is exactly
// the case these guard.
//
// AND A THIRD KIND, ADDED WITH THE TWO TRANSFORMS. Four of the six types are a
// byte copy; FIELD_TIME and FIELD_POSITION_VECTOR are rebased, and each has a
// sentinel -- 0, and vec3_invalid -- that is passed through instead. A fact
// about a sentinel is the easiest vacuous test there is, because "0 in, 0 out"
// is also what a codec that did NOTHING answers. So every sentinel fact below
// first asserts that an ORDINARY value at the same base, or the same landmark,
// comes back CHANGED -- and the ones at FLT_MAX use a base no clock could hold,
// because FLT_MAX minus an ordinary base is FLT_MAX and the guard would have
// nothing to do. Both were found by save_field_codec_mutate_test.py rather than
// by reading; its docstring has the details.
//
// AND THE THREE NUMBERS ARE CHECKED AGAINST datamap.h ITSELF. save_field_codec.h
// is engine-free on purpose and therefore carries 5, 8 and 16 as its own
// #defines. A test that also wrote 5, 8 and 16 would agree with it for no
// reason; this one reads the enum out of public/datamap.h and counts.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sstream>
#include <vector>
#include <sys/stat.h>
#include <string.h>
#include <stdlib.h>
#include "gtest/gtest.h"

// THE CODEC IS IN shell_savegame.h SINCE plan_shim.md §5 3e. It was
// save_field_codec.h, this file's subject was that header, and the two became
// one because shell_savegame.h was the codec's only includer. Nothing about
// what is tested below changed: every SaveField_* function is the same inline
// body in the same order, and this header is still engine-free.
#include "../game/shared/shell_savegame.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
			return kCandidates[ i ];
	}
	return NULL;
}

// The value of one FIELD_* in public/datamap.h, counted from the enum rather
// than written down here. -1 if the name is not in it.
int DatamapValueOf( const char *pszName )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/";
	if ( !pszRoot )
		return -1;

	std::ifstream f( ( std::string( pszRoot ) + "/public/datamap.h" ).c_str() );
	std::stringstream ss;
	ss << f.rdbuf();
	const std::string text = ss.str();

	const size_t start = text.find( "FIELD_VOID = 0" );
	EXPECT_NE( start, std::string::npos ) << "datamap.h's fieldtype_t has moved";
	if ( start == std::string::npos )
		return -1;

	const size_t end = text.find( "FIELD_TYPECOUNT", start );
	EXPECT_NE( end, std::string::npos );

	int value = 0;
	size_t at = start;

	while ( at < end )
	{
		const size_t nameAt = text.find( "FIELD_", at );
		if ( nameAt == std::string::npos || nameAt >= end )
			break;

		size_t nameEnd = nameAt;
		while ( nameEnd < text.size()
			&& ( isalnum( (unsigned char)text[ nameEnd ] ) || text[ nameEnd ] == '_' ) )
			nameEnd++;

		const std::string name = text.substr( nameAt, nameEnd - nameAt );

		// A comment mentioning a FIELD_ by name is not an enumerator. The
		// enumerators are the ones followed by ',' or '=' on the same line.
		size_t after = nameEnd;
		while ( after < text.size() && ( text[ after ] == ' ' || text[ after ] == '\t' ) )
			after++;

		if ( after < text.size() && ( text[ after ] == ',' || text[ after ] == '=' ) )
		{
			if ( name == pszName )
				return value;
			value++;
		}

		at = nameEnd;
	}

	return -1;
}

// The shape SaveField_BlockBytes reads, which the game supplies over a real
// typedescription_t array and this test supplies over two vectors.
struct Fields
{
	std::vector< int > types;
	std::vector< int > sizes;

	int TypeAt( int i ) const { return types[ i ]; }
	int SizeAt( int i ) const { return sizes[ i ]; }
};

} // namespace

//=============================================================================
// The six numbers, against datamap.h rather than against themselves.
//=============================================================================

TEST( SaveFieldCodec, TheSixTypeNumbersAreDatamapsOwn )
{
	EXPECT_EQ( SOURCESHARP_FIELD_INTEGER, DatamapValueOf( "FIELD_INTEGER" ) );
	EXPECT_EQ( SOURCESHARP_FIELD_CHARACTER, DatamapValueOf( "FIELD_CHARACTER" ) );
	EXPECT_EQ( SOURCESHARP_FIELD_TIME, DatamapValueOf( "FIELD_TIME" ) );

	// The client's three, added when the engine's SaveClientState was measured
	// asking for a DECALLIST block per decal -- 76 of them on dm_lockdown.
	EXPECT_EQ( SOURCESHARP_FIELD_VECTOR, DatamapValueOf( "FIELD_VECTOR" ) );
	EXPECT_EQ( SOURCESHARP_FIELD_SHORT, DatamapValueOf( "FIELD_SHORT" ) );
	EXPECT_EQ( SOURCESHARP_FIELD_POSITION_VECTOR, DatamapValueOf( "FIELD_POSITION_VECTOR" ) );
}

TEST( SaveFieldCodec, TheReaderItselfFindsSomethingAndDistinguishesTypes )
{
	// The vacuity guard for the fact above: a reader that answered -1 for
	// everything would make it fail, but one that answered the SAME number for
	// everything would pass it only by luck. Three distinct values and one
	// known absence.
	EXPECT_EQ( 0, DatamapValueOf( "FIELD_VOID" ) );
	EXPECT_EQ( 1, DatamapValueOf( "FIELD_FLOAT" ) );
	EXPECT_EQ( -1, DatamapValueOf( "FIELD_NOT_A_REAL_TYPE" ) );
}

//=============================================================================
// The six the engine asks for.
//=============================================================================

TEST( SaveFieldCodec, AnIntegerIsFourBytes )
{
	EXPECT_EQ( kSaveFieldInteger, SaveField_KindOf( SOURCESHARP_FIELD_INTEGER ) );
	EXPECT_EQ( 4, SaveField_Bytes( SOURCESHARP_FIELD_INTEGER, 1 ) );
}

TEST( SaveFieldCodec, ATimeIsFourBytesAndNotEight )
{
	// FIELD_TIME is a float the engine fixes up across a level transition, not
	// a double and not a tick count. Eight would silently shift every field
	// after it.
	EXPECT_EQ( kSaveFieldTime, SaveField_KindOf( SOURCESHARP_FIELD_TIME ) );
	EXPECT_EQ( 4, SaveField_Bytes( SOURCESHARP_FIELD_TIME, 1 ) );
}

TEST( SaveFieldCodec, ACharArrayIsItsWholeDeclaredWidth )
{
	// The engine's mapName is char[32] and occupies thirty-two bytes in the
	// block whatever it holds. Writing only up to the NUL would make the
	// block's LAYOUT depend on its CONTENT, and a reader could no longer skip a
	// field without decoding it.
	EXPECT_EQ( kSaveFieldCharArray, SaveField_KindOf( SOURCESHARP_FIELD_CHARACTER ) );
	EXPECT_EQ( 32, SaveField_Bytes( SOURCESHARP_FIELD_CHARACTER, 32 ) );
	EXPECT_EQ( 64, SaveField_Bytes( SOURCESHARP_FIELD_CHARACTER, 64 ) );
}

TEST( SaveFieldCodec, AShortIsTwoBytesAndNotFour )
{
	// DECALLIST's entityIndex. Four would be the obvious guess from the name
	// and would shift every record after it by two -- which, in a format whose
	// records carry their own widths, is caught rather than silent, but only
	// because the width came from HERE.
	EXPECT_EQ( kSaveFieldShort, SaveField_KindOf( SOURCESHARP_FIELD_SHORT ) );
	EXPECT_EQ( 2, SaveField_Bytes( SOURCESHARP_FIELD_SHORT, 1 ) );
}

TEST( SaveFieldCodec, TheTwoVectorTypesAreTwelveBytesAndAreNotTheSameType )
{
	// Three floats, no padding. They are separate kinds rather than one,
	// because a POSITION vector is landmark-relative on a level transition and
	// a plain one never is -- the widths agree today and the meaning does not.
	EXPECT_EQ( kSaveFieldVector, SaveField_KindOf( SOURCESHARP_FIELD_VECTOR ) );
	EXPECT_EQ( kSaveFieldPositionVector, SaveField_KindOf( SOURCESHARP_FIELD_POSITION_VECTOR ) );
	EXPECT_NE( SaveField_KindOf( SOURCESHARP_FIELD_VECTOR ),
	           SaveField_KindOf( SOURCESHARP_FIELD_POSITION_VECTOR ) );

	EXPECT_EQ( 12, SaveField_Bytes( SOURCESHARP_FIELD_VECTOR, 1 ) );
	EXPECT_EQ( 12, SaveField_Bytes( SOURCESHARP_FIELD_POSITION_VECTOR, 1 ) );
}

TEST( SaveFieldCodec, TheClientsDecalListSizesToWhatWasMeasured )
{
	// The block the engine asks the CLIENT for, 76 times on dm_lockdown, and
	// the one that corrected section 4a's "it is three": two fields this codec
	// always had and three it did not.
	Fields decal;
	decal.types = { SOURCESHARP_FIELD_POSITION_VECTOR, SOURCESHARP_FIELD_CHARACTER,
	                SOURCESHARP_FIELD_SHORT, SOURCESHARP_FIELD_INTEGER,
	                SOURCESHARP_FIELD_VECTOR };
	decal.sizes = { 1, 128, 1, 1, 1 };

	EXPECT_EQ( 12 + 128 + 2 + 4 + 12, SaveField_BlockBytes( decal, 5 ) );
}

//=============================================================================
// Everything else is a refusal. This is the half with teeth.
//=============================================================================

TEST( SaveFieldCodec, AnUnknownTypeIsRefusedRatherThanSkipped )
{
	// THE FAILURE WITH NO SYMPTOM. A skipped field writes a block one field
	// short, and every later field is then read from the wrong offset -- a map
	// name out of the middle of a light style, restored without complaint.
	//
	// Swept across the whole enum rather than spot-checked, because the case
	// that matters is a type NOBODY HAS THOUGHT OF: a later engine adding one.
	for ( int type = 0; type < 64; type++ )
	{
		if ( type == SOURCESHARP_FIELD_INTEGER
			|| type == SOURCESHARP_FIELD_CHARACTER
			|| type == SOURCESHARP_FIELD_TIME
			|| type == SOURCESHARP_FIELD_VECTOR
			|| type == SOURCESHARP_FIELD_SHORT
			|| type == SOURCESHARP_FIELD_POSITION_VECTOR )
			continue;

		EXPECT_EQ( kSaveFieldUnsupported, SaveField_KindOf( type ) )
			<< "field type " << type << " is not one of the six that were "
			<< "measured, so it must be refused";
		EXPECT_EQ( -1, SaveField_Bytes( type, 1 ) )
			<< "field type " << type << " answered a byte count";
	}

	// AND THE TWO THAT ARE STILL MISSING ARE NAMED, because they are the two a
	// reader of this file will reach for next and neither is in any measured
	// datamap: a FIELD_STRING is a string_t, which is an interned engine index
	// and not bytes, and a FIELD_EHANDLE is an entity reference, which needs
	// the entity table this port deliberately leaves empty
	// (docs/managed-saverestore.md 2d).
	EXPECT_EQ( kSaveFieldUnsupported, SaveField_KindOf( DatamapValueOf( "FIELD_STRING" ) ) );
	EXPECT_EQ( kSaveFieldUnsupported, SaveField_KindOf( DatamapValueOf( "FIELD_EHANDLE" ) ) );
}

TEST( SaveFieldCodec, AnArrayOfScalarsIsRefusedRatherThanGuessedAt )
{
	// The engine has not asked for one. This codec encodes what was measured
	// and says so when asked for more -- rather than answering 4*n, which would
	// be a guess that happens to compile.
	EXPECT_EQ( -1, SaveField_Bytes( SOURCESHARP_FIELD_INTEGER, 4 ) );
	EXPECT_EQ( -1, SaveField_Bytes( SOURCESHARP_FIELD_TIME, 2 ) );
	EXPECT_EQ( -1, SaveField_Bytes( SOURCESHARP_FIELD_SHORT, 3 ) );
	EXPECT_EQ( -1, SaveField_Bytes( SOURCESHARP_FIELD_VECTOR, 2 ) );
	EXPECT_EQ( -1, SaveField_Bytes( SOURCESHARP_FIELD_POSITION_VECTOR, 2 ) );
}

TEST( SaveFieldCodec, ANonPositiveCountIsRefused )
{
	EXPECT_EQ( -1, SaveField_Bytes( SOURCESHARP_FIELD_INTEGER, 0 ) );
	EXPECT_EQ( -1, SaveField_Bytes( SOURCESHARP_FIELD_CHARACTER, -1 ) );

	// AND THE CASE THAT ACTUALLY TESTS THE GUARD, which the two above do not:
	// delete `if ( nFieldSize < 1 ) return -1;` and both of them STILL answer
	// -1 by a later path -- the integer arm's `nFieldSize == 1 ? 4 : -1`, and
	// the char arm returning nFieldSize, which for -1 is -1. A review found
	// this fact passing for the wrong reason.
	//
	// A char array of ZERO is the one that separates them: without the guard it
	// answers 0, SaveField_Copy then returns TRUE having copied nothing, and
	// the block silently carries a zero-width field -- which is the whole
	// failure this file argues against.
	EXPECT_EQ( -1, SaveField_Bytes( SOURCESHARP_FIELD_CHARACTER, 0 ) );

	char szOut[ 4 ] = { 1, 2, 3, 4 };
	const char szIn[ 4 ] = { 9, 9, 9, 9 };

	EXPECT_FALSE( SaveField_Copy( szOut, szIn, SOURCESHARP_FIELD_CHARACTER, 0, 4 ) )
	    << "a zero-width field must be refused, not 'copied' as nothing";
	EXPECT_EQ( 1, szOut[ 0 ] ) << "and nothing may be written on that refusal";
}

//=============================================================================
// The copy, and the block.
//=============================================================================

TEST( SaveFieldCodec, AFieldRoundTripsByteForByte )
{
	const int nIn = 0x11223344;
	int nOut = 0;

	ASSERT_TRUE( SaveField_Copy( &nOut, &nIn, SOURCESHARP_FIELD_INTEGER, 1, 4 ) );
	EXPECT_EQ( nIn, nOut );

	char szIn[ 32 ] = "dm_lockdown";
	char szOut[ 32 ] = { 0 };

	ASSERT_TRUE( SaveField_Copy( szOut, szIn, SOURCESHARP_FIELD_CHARACTER, 32, 32 ) );
	EXPECT_STREQ( "dm_lockdown", szOut );

	// ...and the bytes PAST the terminator come too, because the field is its
	// whole declared width. A copy that stopped at the NUL would leave the rest
	// of the destination as whatever was there.
	for ( int i = 0; i < 32; i++ )
		EXPECT_EQ( szIn[ i ], szOut[ i ] ) << "byte " << i;
}

TEST( SaveFieldCodec, AShortBufferCopiesNothingAtAll )
{
	// Not "copies what fits". A partial field reads back as a plausible wrong
	// value, which is the whole class of failure this codec is arranged
	// against.
	char szIn[ 32 ] = "dm_lockdown";
	char szOut[ 32 ] = { 0 };

	EXPECT_FALSE( SaveField_Copy( szOut, szIn, SOURCESHARP_FIELD_CHARACTER, 32, 31 ) );

	for ( int i = 0; i < 32; i++ )
		EXPECT_EQ( 0, szOut[ i ] ) << "byte " << i << " was written after a refusal";
}

TEST( SaveFieldCodec, AnUnknownTypeCopiesNothingEither )
{
	int nOut = 0x5A5A5A5A;
	const int nIn = 1;

	EXPECT_FALSE( SaveField_Copy( &nOut, &nIn, 99, 1, 4 ) );
	EXPECT_EQ( 0x5A5A5A5A, nOut );
}

TEST( SaveFieldCodec, ANullPointerIsRefusedBeforeAnyByteMoves )
{
	// THE GUARD THAT HAD NO TEST, and it was save_field_codec_mutate_test.py
	// that said so: delete `if ( pDest == 0 || pSrc == 0 ) return false;` from
	// SaveField_Copy and every one of the sixteen facts in this file stayed
	// GREEN. Not one of them passed a null, so the check was unfalsifiable from
	// in here and the header's "FALSE when the buffer cannot hold the field"
	// was describing behaviour nothing held it to.
	//
	// It is not a hypothetical case. SaveField_Copy takes void*, which is what
	// lets it copy a field of any type in either direction -- and the cost of
	// that is that a caller passing the wrong thing arrives HERE as a null
	// rather than being stopped by the compiler. Without the guard the copy
	// loop does not refuse: it writes the field's bytes through address zero.
	int nDest = 0x11223344;
	const int nSource = 0x55667788;

	EXPECT_FALSE( SaveField_Copy( NULL, &nSource, SOURCESHARP_FIELD_INTEGER, 1, 4 ) )
	    << "a null destination must be refused, not written through";
	EXPECT_FALSE( SaveField_Copy( &nDest, NULL, SOURCESHARP_FIELD_INTEGER, 1, 4 ) )
	    << "a null source must be refused, not read through";

	// ...and the refusal is total, on the same terms as every other one here.
	EXPECT_EQ( 0x11223344, nDest ) << "a refused copy may not write";

	// There WAS an EXPECT_EQ on nSource here, and it could not fail: nSource is
	// const and reaches SaveField_Copy as a const void*, so "the source was not
	// written" is guaranteed by the type system rather than by the guard under
	// test. In a file whose whole subject is refusals, an assertion that holds
	// no matter what the code does is the one thing that must not be left in.

	// Both at once, and with a type this codec does not know, so the null check
	// is reached whichever of the two guards is asked first.
	EXPECT_FALSE( SaveField_Copy( NULL, NULL, 99, 1, 4 ) );
}

TEST( SaveFieldCodec, TheEnginesOwnSaveHeaderSizesToWhatWasMeasured )
{
	// The real block, from docs/managed-saverestore.md section 4a: four
	// integers, a time, and two char[32].
	Fields header;
	header.types = { SOURCESHARP_FIELD_INTEGER, SOURCESHARP_FIELD_INTEGER,
	                 SOURCESHARP_FIELD_INTEGER, SOURCESHARP_FIELD_INTEGER,
	                 SOURCESHARP_FIELD_TIME,
	                 SOURCESHARP_FIELD_CHARACTER, SOURCESHARP_FIELD_CHARACTER };
	header.sizes = { 1, 1, 1, 1, 1, 32, 32 };

	EXPECT_EQ( 4 * 4 + 4 + 32 + 32, SaveField_BlockBytes( header, 7 ) );

	// And a light style: an int and a char[64].
	Fields lightstyle;
	lightstyle.types = { SOURCESHARP_FIELD_INTEGER, SOURCESHARP_FIELD_CHARACTER };
	lightstyle.sizes = { 1, 64 };

	EXPECT_EQ( 4 + 64, SaveField_BlockBytes( lightstyle, 2 ) );
}

TEST( SaveFieldCodec, OneUnknownFieldRefusesTheWholeBlock )
{
	// All-or-nothing, and this is why: a block that encoded the fields it
	// recognised and dropped one is a block whose every later field is read
	// from the wrong offset.
	Fields mixed;
	mixed.types = { SOURCESHARP_FIELD_INTEGER, 99, SOURCESHARP_FIELD_INTEGER };
	mixed.sizes = { 1, 1, 1 };

	EXPECT_EQ( -1, SaveField_BlockBytes( mixed, 3 ) );

	// ...and the same three without the unknown one is a real answer, so the
	// refusal above is about the type rather than about the shape.
	mixed.types[ 1 ] = SOURCESHARP_FIELD_INTEGER;
	EXPECT_EQ( 12, SaveField_BlockBytes( mixed, 3 ) );
}

//=============================================================================
// THE TWO THAT ARE NOT A BYTE COPY.
//
// Every fact below is about a transform, and a transform's tests have one
// failure mode above all others: they pass for a no-op. "Zero came back as
// zero" is what a sentinel does AND what a plain copy does, and only one of
// those is being claimed. So each sentinel fact here first asserts that an
// ORDINARY value at the same base -- or the same landmark -- comes back
// CHANGED. Without that line the fact is about nothing, and the mutation arm
// for the guard it names would stay green.
//
// Both bases differ and both landmarks differ, for the same reason: at base 0
// the delta IS the value, so a copy passes every time fact in this file.
//=============================================================================

TEST( SaveFieldCodec, ANonzeroTimeIsRebasedOntoTheBaseItIsLoadedWith )
{
	// The entire point of the delta. A think scheduled ten seconds out is saved
	// in a session whose clock reads 100 and restored into one whose clock
	// reads 500; it has to still be ten seconds out.
	const float flStored = SaveField_EncodeTime( 110.0f, 100.0f );

	EXPECT_FLOAT_EQ( 10.0f, flStored ) << "a time goes down as a delta, not as itself";

	const float flBack = SaveField_DecodeTime( flStored, 500.0f );

	EXPECT_FLOAT_EQ( 510.0f, flBack );

	// And what a byte copy would have answered, named so the fact above cannot
	// be satisfied by one.
	EXPECT_NE( 110.0f, flBack ) << "a plain copy's answer, which is the bug";
}

TEST( SaveFieldCodec, AZeroTimeIsStillZeroAcrossAChangedBase )
{
	// FIRST, THE TEETH. If an ordinary time at these two bases came back
	// unchanged there would be no transform to have a sentinel in, and the
	// assertion below would hold for a codec that did nothing at all.
	const float flOrdinary =
		SaveField_DecodeTime( SaveField_EncodeTime( 7.0f, 100.0f ), 500.0f );

	ASSERT_NE( 7.0f, flOrdinary )
	    << "these two bases must actually move a time, or the next line proves nothing";

	// NOW the sentinel. 0 means NEVER -- no think scheduled -- and a delta of
	// -100 restored against 500 would make it a think 400 seconds in the past.
	const float flNever =
		SaveField_DecodeTime( SaveField_EncodeTime( 0.0f, 100.0f ), 500.0f );

	EXPECT_FLOAT_EQ( 0.0f, flNever ) << "0 in, 0 out, whatever the base does";
}

TEST( SaveFieldCodec, AZeroTimeIsStoredAsTheSentinelRatherThanAsItsDelta )
{
	// The write side of the same guard, on its own, because the round trip
	// above would also pass if BOTH sides were wrong in the same direction.
	const float flStored = SaveField_EncodeTime( 0.0f, 100.0f );

	EXPECT_FLOAT_EQ( SAVEFIELD_ZERO_TIME, flStored );
	EXPECT_NE( -100.0f, flStored ) << "the delta, which is what it must NOT be";
}

TEST( SaveFieldCodec, TheTwoUnrebasedTimesArePassedThroughBothWays )
{
	// Stock's WriteTime and ReadTime both leave INVALID_TIME and FLT_MAX
	// alone. The ordinary value first, as above, so that "unchanged" is a
	// claim about these two rather than about the function.
	ASSERT_NE( 7.0f, SaveField_EncodeTime( 7.0f, 100.0f ) );

	EXPECT_FLOAT_EQ( SAVEFIELD_INVALID_TIME,
	                 SaveField_EncodeTime( SAVEFIELD_INVALID_TIME, 100.0f ) );
	EXPECT_FLOAT_EQ( FLT_MAX, SaveField_EncodeTime( FLT_MAX, 100.0f ) );

	ASSERT_NE( 7.0f, SaveField_DecodeTime( 7.0f, 500.0f ) );

	EXPECT_FLOAT_EQ( SAVEFIELD_INVALID_TIME,
	                 SaveField_DecodeTime( SAVEFIELD_INVALID_TIME, 500.0f ) );
	EXPECT_FLOAT_EQ( FLT_MAX, SaveField_DecodeTime( FLT_MAX, 500.0f ) );

	// AND NOW THE PART THAT ACTUALLY TESTS THE GUARD, because every line above
	// this one passes with the guard DELETED and the mutation harness said so:
	// both sentinels are ±FLT_MAX, one ulp up there is about 1.0e32, and
	// `FLT_MAX - 100.0f` is therefore FLT_MAX exactly. Rebasing them at a base
	// a game clock could hold is arithmetic with no result.
	//
	// So the base here is one no clock holds. It is not a realistic save; it is
	// the smallest change that makes "passed through" mean something, and the
	// value it perturbs by (1.0e34, a hundred ulps) is chosen to clear gtest's
	// own four-ulp tolerance -- which is also why these four are EXPECT_EQ.
	// "Untransformed" is a claim about the bits and not about being close.
	const float flHuge = 1.0e34f;

	EXPECT_EQ( SAVEFIELD_INVALID_TIME,
	           SaveField_EncodeTime( SAVEFIELD_INVALID_TIME, -flHuge ) );
	EXPECT_EQ( FLT_MAX, SaveField_EncodeTime( FLT_MAX, flHuge ) );

	EXPECT_EQ( SAVEFIELD_INVALID_TIME,
	           SaveField_DecodeTime( SAVEFIELD_INVALID_TIME, flHuge ) );
	EXPECT_EQ( FLT_MAX, SaveField_DecodeTime( FLT_MAX, -flHuge ) );

	// ...and the same base DOES move an ordinary time, so the four above are
	// about the sentinels rather than about a base too small to matter.
	ASSERT_NE( 7.0f, SaveField_EncodeTime( 7.0f, -flHuge ) );
	ASSERT_NE( 7.0f, SaveField_DecodeTime( 7.0f, flHuge ) );
}

TEST( SaveFieldCodec, ARealTimeAtTheBaseNeverStoresAsZero )
{
	// Stock: "never allow a time to become zero due to rebasing". A think
	// scheduled for exactly now has a delta of 0.0, and a stored 0.0 is not
	// ZERO_TIME -- it decodes as the base -- but it is one rounding step from
	// looking like one, and stock nudges it clear rather than relying on that.
	const float flStored = SaveField_EncodeTime( 100.0f, 100.0f );

	EXPECT_NE( 0.0f, flStored );
	EXPECT_FLOAT_EQ( 0.001f, flStored );

	// Just under the threshold from the other side, which is the case that
	// separates the clamp from a plain `== 0` special case.
	EXPECT_FLOAT_EQ( 0.001f, SaveField_EncodeTime( 99.9995f, 100.0f ) );

	// And a time far enough out is untouched, so the clamp is a floor rather
	// than the whole function.
	EXPECT_FLOAT_EQ( 5.0f, SaveField_EncodeTime( 105.0f, 100.0f ) );
}

TEST( SaveFieldCodec, APositionIsRebasedOntoTheLandmarkItIsLoadedWith )
{
	const float vecSaveLandmark[ 3 ]    = { 100.0f, 200.0f, 300.0f };
	const float vecRestoreLandmark[ 3 ] = { 500.0f, 600.0f, 700.0f };

	const float vecIn[ 3 ] = { 110.0f, 210.0f, 310.0f };
	float vecStored[ 3 ] = { 0.0f, 0.0f, 0.0f };

	SaveField_EncodePosition( vecStored, vecIn, vecSaveLandmark );

	EXPECT_FLOAT_EQ( 10.0f, vecStored[ 0 ] );
	EXPECT_FLOAT_EQ( 10.0f, vecStored[ 1 ] );
	EXPECT_FLOAT_EQ( 10.0f, vecStored[ 2 ] );

	float vecBack[ 3 ] = { 0.0f, 0.0f, 0.0f };
	SaveField_DecodePosition( vecBack, vecStored, vecRestoreLandmark );

	EXPECT_FLOAT_EQ( 510.0f, vecBack[ 0 ] );
	EXPECT_FLOAT_EQ( 610.0f, vecBack[ 1 ] );
	EXPECT_FLOAT_EQ( 710.0f, vecBack[ 2 ] );

	EXPECT_NE( 110.0f, vecBack[ 0 ] ) << "a plain copy's answer";
}

TEST( SaveFieldCodec, AnInvalidPositionSurvivesAChangedLandmark )
{
	// THE LANDMARKS ARE ABSURD ON PURPOSE, and the first version of this fact
	// used sensible ones and proved nothing. vec3_invalid is FLT_MAX in all
	// three, one ulp up there is about 1.0e32, and subtracting a landmark of
	// 100 from FLT_MAX gives FLT_MAX back exactly -- so the sentinel guard had
	// no observable effect and the mutation arm that deleted it stayed GREEN.
	// These two landmarks are far enough apart to move a value at FLT_MAX,
	// which is the only way "passed through untransformed" is a claim.
	const float vecSaveLandmark[ 3 ]    = { 1.0e34f, 2.0e34f, 3.0e34f };
	const float vecRestoreLandmark[ 3 ] = { -4.0e34f, -5.0e34f, -6.0e34f };

	const float vecInvalid[ 3 ] = { FLT_MAX, FLT_MAX, FLT_MAX };
	float vecStored[ 3 ] = { 0.0f, 0.0f, 0.0f };
	float vecBack[ 3 ] = { 0.0f, 0.0f, 0.0f };

	// THE TEETH, one per side: an ordinary position at THESE landmarks has to
	// move, or the two sides below are guarding a transform that is not
	// happening.
	const float vecOrdinary[ 3 ] = { 1.0f, 2.0f, 3.0f };
	float vecMoved[ 3 ] = { 0.0f, 0.0f, 0.0f };

	SaveField_EncodePosition( vecMoved, vecOrdinary, vecSaveLandmark );
	ASSERT_NE( 1.0f, vecMoved[ 0 ] ) << "this landmark must move a position on save";

	SaveField_DecodePosition( vecMoved, vecOrdinary, vecRestoreLandmark );
	ASSERT_NE( 1.0f, vecMoved[ 0 ] ) << "and this one on restore";

	// The save side alone: vec3_invalid means NO POSITION, and a landmark that
	// moves everything else must not move it. EXPECT_EQ rather than
	// EXPECT_FLOAT_EQ -- untransformed is a claim about the bits, and gtest's
	// four-ulp tolerance would accept a rebased value that landed near enough.
	SaveField_EncodePosition( vecStored, vecInvalid, vecSaveLandmark );

	EXPECT_EQ( FLT_MAX, vecStored[ 0 ] );
	EXPECT_EQ( FLT_MAX, vecStored[ 1 ] );
	EXPECT_EQ( FLT_MAX, vecStored[ 2 ] );

	// The restore side alone, which stock guards too -- verified in
	// CRestore::ReadPositionVector rather than assumed from the save side:
	// `if ( pValue[i] != vec3_invalid ) pValue[i] += basePosition;`.
	SaveField_DecodePosition( vecBack, vecInvalid, vecRestoreLandmark );

	EXPECT_EQ( FLT_MAX, vecBack[ 0 ] );
	EXPECT_EQ( FLT_MAX, vecBack[ 1 ] );
	EXPECT_EQ( FLT_MAX, vecBack[ 2 ] );

	// And the round trip through both, which is what a decal actually takes.
	SaveField_EncodePosition( vecStored, vecInvalid, vecSaveLandmark );
	SaveField_DecodePosition( vecBack, vecStored, vecRestoreLandmark );

	EXPECT_EQ( FLT_MAX, vecBack[ 0 ] );
	EXPECT_EQ( FLT_MAX, vecBack[ 1 ] );
	EXPECT_EQ( FLT_MAX, vecBack[ 2 ] );
}

TEST( SaveFieldCodec, TheInvalidPositionIsAllThreeComponentsAndNotOne )
{
	// Vector's operator!= is all three, so a position with FLT_MAX in ONE
	// component is an ordinary position and IS rebased. A cheaper test on x
	// alone would pass every other fact in this file.
	const float vecLandmark[ 3 ] = { 100.0f, 200.0f, 300.0f };
	const float vecOnlyX[ 3 ] = { FLT_MAX, 210.0f, 310.0f };
	float vecStored[ 3 ];

	EXPECT_FALSE( SaveField_IsInvalidPosition( vecOnlyX ) );

	SaveField_EncodePosition( vecStored, vecOnlyX, vecLandmark );

	EXPECT_FLOAT_EQ( 10.0f, vecStored[ 1 ] ) << "the ordinary components rebase";
	EXPECT_FLOAT_EQ( 10.0f, vecStored[ 2 ] );

	const float vecAllThree[ 3 ] = { FLT_MAX, FLT_MAX, FLT_MAX };
	EXPECT_TRUE( SaveField_IsInvalidPosition( vecAllThree ) );
}

TEST( SaveFieldCodec, APlainVectorIsNotRebasedAndAPositionVectorIs )
{
	// The two twelve-byte types, told apart by what the codec DOES with them
	// rather than by their width. FIELD_VECTOR is a direction -- a decal's
	// impact plane normal -- and rebasing one would tilt it.
	const SaveFieldContext_t ctx = SaveField_Context( 0.0f, 100.0f, 200.0f, 300.0f );

	const float vecIn[ 3 ] = { 110.0f, 210.0f, 310.0f };
	float vecPlain[ 3 ] = { 0.0f, 0.0f, 0.0f };
	float vecPosition[ 3 ] = { 0.0f, 0.0f, 0.0f };

	ASSERT_TRUE( SaveField_Encode( vecPlain, vecIn, SOURCESHARP_FIELD_VECTOR, 1, 12, ctx ) );
	ASSERT_TRUE( SaveField_Encode( vecPosition, vecIn, SOURCESHARP_FIELD_POSITION_VECTOR, 1, 12, ctx ) );

	EXPECT_FLOAT_EQ( 110.0f, vecPlain[ 0 ] ) << "a normal is copied, never rebased";
	EXPECT_FLOAT_EQ( 210.0f, vecPlain[ 1 ] );
	EXPECT_FLOAT_EQ( 310.0f, vecPlain[ 2 ] );

	EXPECT_FLOAT_EQ( 10.0f, vecPosition[ 0 ] ) << "a position is";
	EXPECT_FLOAT_EQ( 10.0f, vecPosition[ 1 ] );
	EXPECT_FLOAT_EQ( 10.0f, vecPosition[ 2 ] );
}

//=============================================================================
// The two doors: SaveField_Copy, which cannot carry a base, and
// SaveField_Encode / SaveField_Decode, which can.
//=============================================================================

TEST( SaveFieldCodec, ThePlainCopyRefusesTheTwoTypesItCannotCarryABaseFor )
{
	// There is nowhere in SaveField_Copy's arguments to put a base time or a
	// landmark, so a copy of one of these two is a silently wrong VALUE rather
	// than a loudly missing field -- and this codec's whole argument is that
	// the silent one is the failure worth arranging against.
	float flTime = 12.0f;
	float flOut = 99.0f;

	EXPECT_FALSE( SaveField_Copy( &flOut, &flTime, SOURCESHARP_FIELD_TIME, 1, 4 ) );
	EXPECT_FLOAT_EQ( 99.0f, flOut ) << "and nothing is written on that refusal";

	const float vecIn[ 3 ] = { 1.0f, 2.0f, 3.0f };
	float vecOut[ 3 ] = { 9.0f, 9.0f, 9.0f };

	EXPECT_FALSE( SaveField_Copy( vecOut, vecIn, SOURCESHARP_FIELD_POSITION_VECTOR, 1, 12 ) );
	EXPECT_FLOAT_EQ( 9.0f, vecOut[ 0 ] );

	// The other four still go through it, so the refusal is about those two
	// rather than about the function having stopped working.
	int nIn = 7;
	int nOut = 0;

	EXPECT_TRUE( SaveField_Copy( &nOut, &nIn, SOURCESHARP_FIELD_INTEGER, 1, 4 ) );
	EXPECT_EQ( 7, nOut );

	EXPECT_TRUE( SaveField_Copy( vecOut, vecIn, SOURCESHARP_FIELD_VECTOR, 1, 12 ) );
	EXPECT_FLOAT_EQ( 1.0f, vecOut[ 0 ] );
}

TEST( SaveFieldCodec, EncodeCarriesATimeThroughTheTransformAndNotThroughACopy )
{
	// The same rebasing as the first fact, but reached the way the caller
	// reaches it: by field type, off a datamap, with the context the caller
	// holds. The bases differ, so a copy cannot pass this.
	const SaveFieldContext_t save    = SaveField_Context( 100.0f, 0.0f, 0.0f, 0.0f );
	const SaveFieldContext_t restore = SaveField_Context( 500.0f, 0.0f, 0.0f, 0.0f );

	const float flIn = 110.0f;
	float flStored = 0.0f;
	float flBack = 0.0f;

	ASSERT_TRUE( SaveField_Encode( &flStored, &flIn, SOURCESHARP_FIELD_TIME, 1, 4, save ) );
	ASSERT_TRUE( SaveField_Decode( &flBack, &flStored, SOURCESHARP_FIELD_TIME, 1, 4, restore ) );

	EXPECT_FLOAT_EQ( 10.0f, flStored );
	EXPECT_FLOAT_EQ( 510.0f, flBack );
	EXPECT_NE( 110.0f, flBack ) << "what SaveField_Copy would have answered";
}

TEST( SaveFieldCodec, EncodeKeepsTheZeroTimeSentinelAcrossAChangedBase )
{
	const SaveFieldContext_t save    = SaveField_Context( 100.0f, 0.0f, 0.0f, 0.0f );
	const SaveFieldContext_t restore = SaveField_Context( 500.0f, 0.0f, 0.0f, 0.0f );

	// THE TEETH first, through the same two calls the sentinel goes through.
	const float flOrdinary = 7.0f;
	float flTmp = 0.0f;
	float flMoved = 0.0f;

	ASSERT_TRUE( SaveField_Encode( &flTmp, &flOrdinary, SOURCESHARP_FIELD_TIME, 1, 4, save ) );
	ASSERT_TRUE( SaveField_Decode( &flMoved, &flTmp, SOURCESHARP_FIELD_TIME, 1, 4, restore ) );
	ASSERT_NE( 7.0f, flMoved ) << "these bases must move a time, or the rest proves nothing";

	const float flNever = 0.0f;
	float flBack = 1.0f;

	ASSERT_TRUE( SaveField_Encode( &flTmp, &flNever, SOURCESHARP_FIELD_TIME, 1, 4, save ) );
	ASSERT_TRUE( SaveField_Decode( &flBack, &flTmp, SOURCESHARP_FIELD_TIME, 1, 4, restore ) );

	EXPECT_FLOAT_EQ( 0.0f, flBack );
}

TEST( SaveFieldCodec, EncodeKeepsTheInvalidPositionAcrossAChangedLandmark )
{
	// TWO LANDMARK PAIRS, and the second one is absurd for the reason the
	// transform fact above gives: a sentinel at FLT_MAX cannot be seen to
	// survive a landmark too small to move it.
	//
	// FIRST PAIR, ordinary, and the values are PINNED rather than merely
	// changed. "It moved" was all this fact asserted at first, and the encode
	// side alone satisfies that -- so the arm that replaced the DECODE
	// transform with a copy stayed GREEN. The exact number is what holds the
	// restore side to anything.
	const SaveFieldContext_t save    = SaveField_Context( 0.0f, 100.0f, 200.0f, 300.0f );
	const SaveFieldContext_t restore = SaveField_Context( 0.0f, 500.0f, 600.0f, 700.0f );

	const float vecOrdinary[ 3 ] = { 1.0f, 2.0f, 3.0f };
	float vecTmp[ 3 ] = { 0.0f, 0.0f, 0.0f };
	float vecMoved[ 3 ] = { 0.0f, 0.0f, 0.0f };

	ASSERT_TRUE( SaveField_Encode( vecTmp, vecOrdinary, SOURCESHARP_FIELD_POSITION_VECTOR, 1, 12, save ) );
	EXPECT_FLOAT_EQ( -99.0f, vecTmp[ 0 ] ) << "1 - 100, and the save landmark is subtracted";

	ASSERT_TRUE( SaveField_Decode( vecMoved, vecTmp, SOURCESHARP_FIELD_POSITION_VECTOR, 1, 12, restore ) );
	EXPECT_FLOAT_EQ( 401.0f, vecMoved[ 0 ] ) << "-99 + 500, and the restore landmark is added";
	EXPECT_FLOAT_EQ( 402.0f, vecMoved[ 1 ] );
	EXPECT_FLOAT_EQ( 403.0f, vecMoved[ 2 ] );

	// SECOND PAIR, big enough to move a value at FLT_MAX -- and the sentinel
	// still comes back bit for bit, through the same two calls by field type.
	const SaveFieldContext_t bigSave =
		SaveField_Context( 0.0f, 1.0e34f, 2.0e34f, 3.0e34f );
	const SaveFieldContext_t bigRestore =
		SaveField_Context( 0.0f, -4.0e34f, -5.0e34f, -6.0e34f );

	ASSERT_TRUE( SaveField_Encode( vecTmp, vecOrdinary, SOURCESHARP_FIELD_POSITION_VECTOR, 1, 12, bigSave ) );
	ASSERT_NE( 1.0f, vecTmp[ 0 ] ) << "the teeth: this landmark moves an ordinary position";

	const float vecInvalid[ 3 ] = { FLT_MAX, FLT_MAX, FLT_MAX };
	float vecBack[ 3 ] = { 0.0f, 0.0f, 0.0f };

	ASSERT_TRUE( SaveField_Encode( vecTmp, vecInvalid, SOURCESHARP_FIELD_POSITION_VECTOR, 1, 12, bigSave ) );
	EXPECT_EQ( FLT_MAX, vecTmp[ 0 ] ) << "and leaves vec3_invalid alone";

	ASSERT_TRUE( SaveField_Decode( vecBack, vecTmp, SOURCESHARP_FIELD_POSITION_VECTOR, 1, 12, bigRestore ) );

	EXPECT_EQ( FLT_MAX, vecBack[ 0 ] );
	EXPECT_EQ( FLT_MAX, vecBack[ 1 ] );
	EXPECT_EQ( FLT_MAX, vecBack[ 2 ] );
}

TEST( SaveFieldCodec, EncodeRoundTripsTheFourPlainTypesByteForByte )
{
	// A non-zero context, deliberately: the four that are not transformed must
	// be unaffected by one.
	const SaveFieldContext_t ctx = SaveField_Context( 100.0f, 1.0f, 2.0f, 3.0f );

	const int nIn = 0x11223344;
	int nOut = 0;

	ASSERT_TRUE( SaveField_Encode( &nOut, &nIn, SOURCESHARP_FIELD_INTEGER, 1, 4, ctx ) );
	EXPECT_EQ( nIn, nOut );

	const short sIn = 0x0102;
	short sOut = 0;

	ASSERT_TRUE( SaveField_Decode( &sOut, &sIn, SOURCESHARP_FIELD_SHORT, 1, 2, ctx ) );
	EXPECT_EQ( sIn, sOut );

	const char szIn[ 32 ] = "dm_lockdown";
	char szOut[ 32 ] = { 0 };

	ASSERT_TRUE( SaveField_Encode( szOut, szIn, SOURCESHARP_FIELD_CHARACTER, 32, 32, ctx ) );
	EXPECT_EQ( 0, memcmp( szIn, szOut, 32 ) );
}

TEST( SaveFieldCodec, EncodeAndDecodeRefuseWhatTheCopyRefuses )
{
	// The bounds are the copy's, and they have to hold on the transformed path
	// too -- where the bytes are written by SaveField_StoreFloat rather than by
	// the copy loop, so nothing about them is inherited.
	const SaveFieldContext_t ctx = SaveField_Context( 100.0f, 1.0f, 2.0f, 3.0f );

	float flIn = 12.0f;
	float flOut = 99.0f;

	EXPECT_FALSE( SaveField_Encode( &flOut, &flIn, SOURCESHARP_FIELD_TIME, 1, 3, ctx ) )
	    << "a four-byte field into three bytes";
	EXPECT_FLOAT_EQ( 99.0f, flOut ) << "and not one byte written";

	EXPECT_FALSE( SaveField_Decode( &flOut, &flIn, SOURCESHARP_FIELD_TIME, 1, 3, ctx ) );
	EXPECT_FLOAT_EQ( 99.0f, flOut );

	float vecOut[ 3 ] = { 9.0f, 9.0f, 9.0f };
	const float vecIn[ 3 ] = { 1.0f, 2.0f, 3.0f };

	EXPECT_FALSE( SaveField_Encode( vecOut, vecIn, SOURCESHARP_FIELD_POSITION_VECTOR, 1, 11, ctx ) );
	EXPECT_FLOAT_EQ( 9.0f, vecOut[ 0 ] );

	EXPECT_FALSE( SaveField_Encode( &flOut, &flIn, 99, 1, 4, ctx ) ) << "an unknown type";
	EXPECT_FALSE( SaveField_Decode( &flOut, &flIn, 99, 1, 4, ctx ) );
	EXPECT_FALSE( SaveField_Encode( NULL, &flIn, SOURCESHARP_FIELD_TIME, 1, 4, ctx ) );
	EXPECT_FALSE( SaveField_Decode( &flOut, NULL, SOURCESHARP_FIELD_TIME, 1, 4, ctx ) );
	EXPECT_FLOAT_EQ( 99.0f, flOut );
}
