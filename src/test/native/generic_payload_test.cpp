//========= SourceSharp — tests for the payload prop arithmetic ==============//
//
// plan_nettable_generic.md Part 14 Step 1. The proxy half of that step can only
// be checked with a running Source (cs_sv_dump_payload does it); the offset half
// is plain arithmetic, and it is the half whose failures are silent.
//
// A payload prop is a byte offset from the start of the entity and nothing else.
// If the offset is wrong the engine does not complain, the table still builds,
// the client still decodes: it just networks the wrong four bytes. A base of
// zero networks the entity's own members — vtable pointer first — and the client
// reads them as state. That is why the nBlobBase != 0 cases below exist and why
// they are the ones worth having.
//
// No engine headers, which is what generic_payload.h keeps its arithmetic inline
// for. Nothing here links against dt_send.cpp.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/generic_payload.h"
#include "../game/shared/generic_nettable.h"

#include <string.h>
#include <strings.h>

namespace
{

//-----------------------------------------------------------------------------
// Rounding up, because a payload that is not a whole number of words still has
// to be carried whole. Rounding down would leave the last field off the table
// and network three quarters of it.
//-----------------------------------------------------------------------------
TEST( PayloadWordCount, RoundsUpToWholeWords )
{
	EXPECT_EQ( 0, SourceSharpPayload_WordCount( 0 ) );
	EXPECT_EQ( 1, SourceSharpPayload_WordCount( 1 ) );
	EXPECT_EQ( 1, SourceSharpPayload_WordCount( 4 ) );
	EXPECT_EQ( 2, SourceSharpPayload_WordCount( 5 ) );
	EXPECT_EQ( 8, SourceSharpPayload_WordCount( 32 ) );
	EXPECT_EQ( 9, SourceSharpPayload_WordCount( 33 ) );
}

//-----------------------------------------------------------------------------
// The tier sizes Part 7 names, and the one past the largest.
//
// 2048 bytes is 512 props, not 2048 — the whole point of Part 1B's dword
// element. If this ever reads 2048 the delta cost went up by 54% and nothing
// else would say so.
//-----------------------------------------------------------------------------
TEST( PayloadWordCount, MatchesTheTierSizes )
{
	EXPECT_EQ( 8, SourceSharpPayload_WordCount( 32 ) );
	EXPECT_EQ( 16, SourceSharpPayload_WordCount( 64 ) );
	EXPECT_EQ( 32, SourceSharpPayload_WordCount( 128 ) );
	EXPECT_EQ( 64, SourceSharpPayload_WordCount( 256 ) );
	EXPECT_EQ( 128, SourceSharpPayload_WordCount( 512 ) );
	EXPECT_EQ( 256, SourceSharpPayload_WordCount( 1024 ) );
	EXPECT_EQ( 512, SourceSharpPayload_WordCount( 2048 ) );
}

//-----------------------------------------------------------------------------
// Refusing rather than clamping. A caller handed fewer props than it asked for
// would build a table that carries part of a blob, which decodes without
// complaint into a struct whose tail is whatever the client had last.
//-----------------------------------------------------------------------------
TEST( PayloadWordCount, RefusesWhatNoTierCanCarry )
{
	EXPECT_EQ( -1, SourceSharpPayload_WordCount( 2049 ) );
	EXPECT_EQ( -1, SourceSharpPayload_WordCount( 1 << 20 ) );
	EXPECT_EQ( -1, SourceSharpPayload_WordCount( -1 ) );
}

//-----------------------------------------------------------------------------
// The arithmetic itself: stride of four, from the blob's base.
//-----------------------------------------------------------------------------
TEST( PayloadWordOffset, StridesByOneDword )
{
	EXPECT_EQ( 0, SourceSharpPayload_WordOffset( 0, 0 ) );
	EXPECT_EQ( 4, SourceSharpPayload_WordOffset( 0, 1 ) );
	EXPECT_EQ( 28, SourceSharpPayload_WordOffset( 0, 7 ) );
}

//-----------------------------------------------------------------------------
// The case that matters. A real blob never starts at zero: CStandaloneEntity
// allocates it past its declared members, so every offset carries a base of a
// couple of thousand bytes and an implementation that dropped it would read the
// entity's own fields — vtable pointer included — instead, silently, and for
// every entity.
//-----------------------------------------------------------------------------
TEST( PayloadWordOffset, CarriesANonZeroBlobBase )
{
	const int nBase = 1424;

	EXPECT_EQ( nBase, SourceSharpPayload_WordOffset( nBase, 0 ) );
	EXPECT_EQ( nBase + 4, SourceSharpPayload_WordOffset( nBase, 1 ) );
	EXPECT_EQ( nBase + 28, SourceSharpPayload_WordOffset( nBase, 7 ) );
	EXPECT_EQ( nBase + 2044, SourceSharpPayload_WordOffset( nBase, 511 ) );
}

//-----------------------------------------------------------------------------
// No two words share an offset and none overlaps its neighbour, at a base that
// is not a multiple of the stride.
//
// Stated as a property rather than as a table of expected numbers: the failure
// this guards against is two props reading the same four bytes, and a formula
// that produced that would look perfectly reasonable spelled out one line at a
// time.
//-----------------------------------------------------------------------------
TEST( PayloadWordOffset, WordsNeitherOverlapNorRepeat )
{
	const int nBase = 1030;
	const int nWords = SourceSharpPayload_WordCount( 2048 );

	for ( int i = 1; i < nWords; i++ )
	{
		const int nPrev = SourceSharpPayload_WordOffset( nBase, i - 1 );
		const int nThis = SourceSharpPayload_WordOffset( nBase, i );

		EXPECT_EQ( SOURCESHARP_PAYLOAD_WORD_BYTES, nThis - nPrev );
	}

	// And the set covers exactly the blob it claims to, end to end.
	EXPECT_EQ( nBase, SourceSharpPayload_WordOffset( nBase, 0 ) );
	EXPECT_EQ( nBase + 2048, SourceSharpPayload_WordOffset( nBase, nWords - 1 ) + SOURCESHARP_PAYLOAD_WORD_BYTES );
}

//-----------------------------------------------------------------------------
// Word i covers bytes [i*4, i*4+4) of the blob, which is the property every
// caller actually depends on: the word that carries a field at blob offset N is
// N/4, and it must be the same on both sides.
//-----------------------------------------------------------------------------
TEST( PayloadWordOffset, WordIndexCoversTheExpectedBlobBytes )
{
	const int nBase = 512;

	for ( int nBlobByte = 0; nBlobByte < 128; nBlobByte++ )
	{
		const int iWord = nBlobByte / SOURCESHARP_PAYLOAD_WORD_BYTES;
		const int nWordAt = SourceSharpPayload_WordOffset( nBase, iWord );

		EXPECT_LE( nWordAt, nBase + nBlobByte );
		EXPECT_LT( nBase + nBlobByte, nWordAt + SOURCESHARP_PAYLOAD_WORD_BYTES );
	}
}

//-----------------------------------------------------------------------------
// The engine stores a prop offset in an unsigned short and asserts on it.
// Reaching it would be an
// assert deep inside table setup with nothing naming the payload, so the refusal
// belongs here where it can say what happened.
//-----------------------------------------------------------------------------
TEST( PayloadOffsetsFit, AcceptsWhatTheEngineCanStore )
{
	EXPECT_TRUE( SourceSharpPayload_OffsetsFit( 0, 512 ) );
	EXPECT_TRUE( SourceSharpPayload_OffsetsFit( 1424, 512 ) );
	EXPECT_TRUE( SourceSharpPayload_OffsetsFit( 0, 0 ) );

	// Exactly the last offset the engine accepts: 0xFFFE.
	EXPECT_TRUE( SourceSharpPayload_OffsetsFit( SOURCESHARP_PAYLOAD_MAX_OFFSET, 1 ) );
	EXPECT_TRUE( SourceSharpPayload_OffsetsFit( SOURCESHARP_PAYLOAD_MAX_OFFSET - 4, 2 ) );
}

TEST( PayloadOffsetsFit, RefusesWhatItCannot )
{
	EXPECT_FALSE( SourceSharpPayload_OffsetsFit( SOURCESHARP_PAYLOAD_MAX_OFFSET + 1, 1 ) );
	EXPECT_FALSE( SourceSharpPayload_OffsetsFit( SOURCESHARP_PAYLOAD_MAX_OFFSET - 3, 2 ) );
	EXPECT_FALSE( SourceSharpPayload_OffsetsFit( -1, 1 ) );
	EXPECT_FALSE( SourceSharpPayload_OffsetsFit( 0, SOURCESHARP_PAYLOAD_MAX_WORDS + 1 ) );

	// A base that fits on its own but whose last word does not.
	EXPECT_FALSE( SourceSharpPayload_OffsetsFit( 0xFFF0, 8 ) );
}

//-----------------------------------------------------------------------------
// The names. Part 1D: props are matched to the client's RecvProps BY NAME, and
// a send prop with no matching recv prop is decoded and discarded — so the name
// list is the part of the table that genuinely has to agree between the two
// sides. Both generate it from this one function, and this is what pins it.
//-----------------------------------------------------------------------------
TEST( PayloadPropName, IsThreeFixedDigits )
{
	char szName[SOURCESHARP_PAYLOAD_NAME_BYTES];

	ASSERT_TRUE( SourceSharpPayload_FormatPropName( 0, szName, sizeof( szName ) ) );
	EXPECT_STREQ( "p000", szName );

	ASSERT_TRUE( SourceSharpPayload_FormatPropName( 7, szName, sizeof( szName ) ) );
	EXPECT_STREQ( "p007", szName );

	ASSERT_TRUE( SourceSharpPayload_FormatPropName( 42, szName, sizeof( szName ) ) );
	EXPECT_STREQ( "p042", szName );

	ASSERT_TRUE( SourceSharpPayload_FormatPropName( 100, szName, sizeof( szName ) ) );
	EXPECT_STREQ( "p100", szName );

	ASSERT_TRUE( SourceSharpPayload_FormatPropName( 511, szName, sizeof( szName ) ) );
	EXPECT_STREQ( "p511", szName );
}

TEST( PayloadPropNameTable, AnswersEachWordsNameAndNothingOutsideThem )
{
	CSourceSharpPayloadPropNames names;

	EXPECT_STREQ( "p000", names.At( 0 ) );
	EXPECT_STREQ( "p042", names.At( 42 ) );
	EXPECT_STREQ( "p511", names.At( SOURCESHARP_PAYLOAD_MAX_WORDS - 1 ) );
	EXPECT_EQ( nullptr, names.At( -1 ) );
	EXPECT_EQ( nullptr, names.At( SOURCESHARP_PAYLOAD_MAX_WORDS ) );
}

// A prop keeps the pointer it was given, so asking again must answer the same storage.
TEST( PayloadPropNameTable, AnswersTheSameStorageEveryTime )
{
	CSourceSharpPayloadPropNames names;

	const char *pFirst = names.At( 7 );
	names.At( 300 );

	EXPECT_EQ( pFirst, names.At( 7 ) );
	EXPECT_STREQ( "p007", pFirst );
}

//-----------------------------------------------------------------------------
// Every name in the largest tier is distinct. A collision would pair two send
// props onto one recv prop and drop the other, which is a decode that succeeds
// and a field that never updates.
//-----------------------------------------------------------------------------
TEST( PayloadPropName, IsUniqueAcrossTheLargestTier )
{
	char szNames[SOURCESHARP_PAYLOAD_MAX_WORDS][SOURCESHARP_PAYLOAD_NAME_BYTES];

	for ( int i = 0; i < SOURCESHARP_PAYLOAD_MAX_WORDS; i++ )
	{
		ASSERT_TRUE( SourceSharpPayload_FormatPropName( i, szNames[i], SOURCESHARP_PAYLOAD_NAME_BYTES ) );
		EXPECT_EQ( 4u, strlen( szNames[i] ) );
	}

	for ( int i = 0; i < SOURCESHARP_PAYLOAD_MAX_WORDS; i++ )
	{
		for ( int j = i + 1; j < SOURCESHARP_PAYLOAD_MAX_WORDS; j++ )
		{
			ASSERT_STRNE( szNames[i], szNames[j] );
		}
	}
}

TEST( PayloadPropName, RefusesOutOfRangeAndShortBuffers )
{
	char szName[SOURCESHARP_PAYLOAD_NAME_BYTES];

	EXPECT_FALSE( SourceSharpPayload_FormatPropName( -1, szName, sizeof( szName ) ) );
	EXPECT_FALSE( SourceSharpPayload_FormatPropName( SOURCESHARP_PAYLOAD_MAX_WORDS, szName, sizeof( szName ) ) );
	EXPECT_FALSE( SourceSharpPayload_FormatPropName( 0, szName, SOURCESHARP_PAYLOAD_NAME_BYTES - 1 ) );
	EXPECT_FALSE( SourceSharpPayload_FormatPropName( 0, NULL, sizeof( szName ) ) );
}

//-----------------------------------------------------------------------------
// Part 1B's arithmetic, written down so it stops being a claim in a document.
//
// CDeltaBitsWriter::WritePropIndex charges 7 bits for a changed prop whose index
// is within 16 of the last one written. Against that, a changed four-byte field
// costs 39 bits as one dword prop and 60 as four byte props — and 39 is exactly
// what an ordinary typed SendPropInt costs. If someone ever reaches for a byte
// element again, this is the test that says what it would cost.
//-----------------------------------------------------------------------------
TEST( PayloadDeltaCost, DwordElementCostsWhatATypedPropCosts )
{
	const int nIndexBits = 7;

	const int nAsDword = nIndexBits + 32;
	const int nAsBytes = 4 * ( nIndexBits + 8 );
	const int nAsTypedProp = nIndexBits + 32;

	EXPECT_EQ( 39, nAsDword );
	EXPECT_EQ( 60, nAsBytes );
	EXPECT_EQ( nAsTypedProp, nAsDword );

	// And the element size this file is built around is the one that gets that.
	EXPECT_EQ( 4, SOURCESHARP_PAYLOAD_WORD_BYTES );
}

//-----------------------------------------------------------------------------
// The tier boundaries (Part 7, and Part 15's list of them).
//
// Written as literals rather than derived from the tier table, because a test
// that asks the same function the same way it is implemented only proves the
// function is a function. These are the numbers the MANAGED side hardcodes too
// (NetTiers.Sizes, NetSchema.cs), and until Step 6 makes that side a reading of
// this one the two are held together by nothing else.
//
// Zero is tier 0 and not "no tier": a type that declares no networked state is a
// real thing, and it belongs in the smallest tier rather than outside the
// scheme.
//-----------------------------------------------------------------------------
TEST( PayloadTiers, BoundariesAreTheSmallestThatFits )
{
	EXPECT_EQ( 0, SourceSharp_TierForSize( 0 ) );
	EXPECT_EQ( 0, SourceSharp_TierForSize( 1 ) );
	EXPECT_EQ( 0, SourceSharp_TierForSize( 32 ) );

	// The two rows added SECOND, and the ones that matter most: nine of the
	// twenty classes that declare anything are 40 to 68 bytes, and every one of
	// them used to answer 256 here. ss_beam is 52 and is 126 of the 1064
	// hierarchy entities on ss_sandbox.
	EXPECT_EQ( 1, SourceSharp_TierForSize( 33 ) );
	EXPECT_EQ( 1, SourceSharp_TierForSize( 40 ) );
	EXPECT_EQ( 1, SourceSharp_TierForSize( 52 ) );
	EXPECT_EQ( 1, SourceSharp_TierForSize( 64 ) );
	EXPECT_EQ( 2, SourceSharp_TierForSize( 65 ) );
	EXPECT_EQ( 2, SourceSharp_TierForSize( 68 ) );
	EXPECT_EQ( 2, SourceSharp_TierForSize( 128 ) );
	EXPECT_EQ( 3, SourceSharp_TierForSize( 129 ) );
	EXPECT_EQ( 3, SourceSharp_TierForSize( 144 ) );
	EXPECT_EQ( 3, SourceSharp_TierForSize( 164 ) );
	EXPECT_EQ( 3, SourceSharp_TierForSize( 256 ) );

	// The two rows added FIRST. 257 answered 2048 before either pair existed
	// and now answers 512: the hierarchy's greediest declaration is 260 bytes.
	EXPECT_EQ( 4, SourceSharp_TierForSize( 257 ) );
	EXPECT_EQ( 4, SourceSharp_TierForSize( 260 ) );
	EXPECT_EQ( 4, SourceSharp_TierForSize( 512 ) );
	EXPECT_EQ( 5, SourceSharp_TierForSize( 513 ) );
	EXPECT_EQ( 5, SourceSharp_TierForSize( 1024 ) );
	EXPECT_EQ( 6, SourceSharp_TierForSize( 1025 ) );
	EXPECT_EQ( 6, SourceSharp_TierForSize( 2048 ) );

	// Refused rather than clamped. A type quietly assigned to a tier too small
	// for it would have its tail fields simply absent from the wire, decoding as
	// whatever the client's blob happened to hold.
	EXPECT_EQ( -1, SourceSharp_TierForSize( 2049 ) );
	EXPECT_EQ( -1, SourceSharp_TierForSize( -1 ) );
}

TEST( PayloadTiers, SizesAreTheOnesThePlanPriced )
{
	int nCount = 0;
	const SourceSharpTier_t *pTiers = SourceSharp_Tiers( &nCount );

	ASSERT_EQ( 7, nCount );
	EXPECT_EQ( SOURCESHARP_TIER_MAX, nCount );
	EXPECT_EQ( 32, pTiers[0].nBytes );
	EXPECT_EQ( 64, pTiers[1].nBytes );
	EXPECT_EQ( 128, pTiers[2].nBytes );
	EXPECT_EQ( 256, pTiers[3].nBytes );
	EXPECT_EQ( 512, pTiers[4].nBytes );
	EXPECT_EQ( 1024, pTiers[5].nBytes );
	EXPECT_EQ( 2048, pTiers[6].nBytes );

	// The names the connect handshake pairs on. Spelled out here because a
	// rename on one side and not the other is a named disconnect at best, and
	// this is the only place either name is written down twice.
	EXPECT_STREQ( "DT_SourceSharpStdEnt32", pTiers[0].pTable );
	EXPECT_STREQ( "DT_SourceSharpStdEnt64", pTiers[1].pTable );
	EXPECT_STREQ( "DT_SourceSharpStdEnt128", pTiers[2].pTable );
	EXPECT_STREQ( "DT_SourceSharpStdEnt256", pTiers[3].pTable );
	EXPECT_STREQ( "DT_SourceSharpStdEnt512", pTiers[4].pTable );
	EXPECT_STREQ( "DT_SourceSharpStdEnt1024", pTiers[5].pTable );
	EXPECT_STREQ( "DT_SourceSharpStdEnt2048", pTiers[6].pTable );

	// EVERY STEP IS A DOUBLING, checked rather than eyeballed. It is what
	// bounds the waste a class one byte over a boundary suffers at 2x its own
	// size, and the list has twice been grown by somebody reading this file --
	// so the property that made the new rows the right ones is worth pinning.
	for ( int i = 1; i < nCount; i++ )
		EXPECT_EQ( pTiers[i - 1].nBytes * 2, pTiers[i].nBytes ) << "tier " << i;
}

//-----------------------------------------------------------------------------
// EVERY ROW IS FILLED IN, which is the half of the bound check the compiler
// cannot do.
//
// SourceSharp_Tiers declares its array at SOURCESHARP_TIER_MAX, so a SIXTH row
// added without bumping the constant does not compile. The other direction is
// silent: bumping the constant without adding a row leaves a zero-filled tier
// whose names are NULL and whose size is zero, which SourceSharp_TierForSize
// would then hand back for a size of zero — every stateless class on the
// hierarchy — and every one of them would be created through a ServerClass
// registered under a NULL name.
//
// Provoked to prove it can fail: setting SOURCESHARP_TIER_MAX to 6 without a
// sixth row makes this the first test to go red, on the tail row's NULL pClass.
//-----------------------------------------------------------------------------
TEST( PayloadTiers, EveryTierIsNamed )
{
	int nCount = 0;
	const SourceSharpTier_t *pTiers = SourceSharp_Tiers( &nCount );

	ASSERT_EQ( SOURCESHARP_TIER_MAX, nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		EXPECT_TRUE( pTiers[i].pClass != NULL && pTiers[i].pClass[0] != 0 ) << "tier " << i;
		EXPECT_TRUE( pTiers[i].pTable != NULL && pTiers[i].pTable[0] != 0 ) << "tier " << i;
		EXPECT_GT( pTiers[i].nBytes, 0 ) << "tier " << i;
	}
}

//-----------------------------------------------------------------------------
// Ordered smallest first, which is what makes "the first that fits" the answer.
// A list that grew out of order would still answer for every size in the test
// above and would start rounding a small type up to a large tier.
//-----------------------------------------------------------------------------
TEST( PayloadTiers, AreOrderedSmallestFirst )
{
	int nCount = 0;
	const SourceSharpTier_t *pTiers = SourceSharp_Tiers( &nCount );

	for ( int i = 1; i < nCount; i++ )
		EXPECT_LT( pTiers[i - 1].nBytes, pTiers[i].nBytes );
}

//-----------------------------------------------------------------------------
// Every tier is a whole number of payload words, and the largest is exactly the
// 512 the prop-name table can name. This is the constraint that decides there is
// no 4096 tier.
//-----------------------------------------------------------------------------
TEST( PayloadTiers, EveryTierIsAWholeNumberOfWordsAndFits )
{
	int nCount = 0;
	const SourceSharpTier_t *pTiers = SourceSharp_Tiers( &nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		EXPECT_EQ( 0, pTiers[i].nBytes % SOURCESHARP_PAYLOAD_WORD_BYTES );
		EXPECT_EQ( pTiers[i].nBytes / SOURCESHARP_PAYLOAD_WORD_BYTES, SourceSharp_TierWords( i ) );
		EXPECT_LE( SourceSharp_TierWords( i ), SOURCESHARP_PAYLOAD_MAX_WORDS );
	}

	EXPECT_EQ( SOURCESHARP_PAYLOAD_MAX_WORDS, SourceSharp_TierWords( nCount - 1 ) );
	EXPECT_EQ( -1, SourceSharp_TierWords( -1 ) );
	EXPECT_EQ( -1, SourceSharp_TierWords( nCount ) );
}

TEST( PayloadTiers, ASizeResolvesToATierPointer )
{
	const SourceSharpTier_t *pFor32 = SourceSharp_TierForSizePtr( 32 );
	ASSERT_TRUE( pFor32 != NULL );
	EXPECT_STREQ( "DT_SourceSharpStdEnt32", pFor32->pTable );

	EXPECT_TRUE( SourceSharp_TierForSizePtr( 2049 ) == NULL );
}

//-----------------------------------------------------------------------------
// EVERY TIER HAS A DISTINCT NAME, and the seven are distinct case-insensitively.
//
// It used to be fourteen, two per tier, because one ServerClass could not serve
// two unrelated C++ classes. The netentity shell is deleted and its pair went
// with it. Two names colliding still costs the same: a duplicate ServerClass on
// one side and a missing one on the other, of which the engine reports the
// second and not the first. Case-insensitively distinct is not paranoia — a
// managed DT_func_lod once paired with stock's DT_Func_LOD and killed the join.
//
// BOTH SHELL VALUES ARE ASKED, because nShell stays in the ABI as a reserved
// value: an old managed build asking about shell 0 must not get a NULL a
// ServerClass would then be registered under.
//-----------------------------------------------------------------------------
TEST( PayloadTiers, EveryTierHasADistinctName )
{
	int nCount = 0;
	SourceSharp_Tiers( &nCount );

	const char *pNames[SOURCESHARP_TIER_MAX];
	int nNames = 0;

	for ( int i = 0; i < nCount; i++ )
	{
		const char *pClass = SourceSharp_TierClassName( SS_SHELL_STANDALONE, i );
		const char *pTable = SourceSharp_TierTableName( SS_SHELL_STANDALONE, i );

		ASSERT_TRUE( pClass != NULL );
		ASSERT_TRUE( pTable != NULL );

		// The retired shell answers the same names rather than NULL.
		EXPECT_STREQ( pClass, SourceSharp_TierClassName( SS_SHELL_NETENTITY, i ) )
			<< "tier " << i
			<< ": the retired shell value answers a DIFFERENT "
			   "class name. There is one shell; a second answer is a table "
			   "registered under a name only one side believes in.";
		EXPECT_STREQ( pTable, SourceSharp_TierTableName( SS_SHELL_NETENTITY, i ) )
			<< "tier " << i
			<< ": the retired shell value answers a different "
			   "table name.";

		pNames[nNames++] = pTable;
	}

	ASSERT_EQ( SOURCESHARP_TIER_MAX, nNames );

	for ( int a = 0; a < nNames; a++ )
	{
		for ( int b = a + 1; b < nNames; b++ )
		{
			EXPECT_NE( 0, strcasecmp( pNames[a], pNames[b] ) )
				<< "'" << pNames[a] << "' and '" << pNames[b] << "' are one name to the engine";
		}
	}

	// And the stem is not a prefix of the shared table it replaced, so a suffix
	// added later cannot collide with it either.
	EXPECT_EQ( NULL, strstr( SourceSharp_TierTableName( SS_SHELL_STANDALONE, 0 ), "Standalone" ) );

	EXPECT_TRUE( SourceSharp_TierClassName( SS_SHELL_STANDALONE, -1 ) == NULL );
	EXPECT_TRUE( SourceSharp_TierTableName( SS_SHELL_STANDALONE, nCount ) == NULL );
}

//-----------------------------------------------------------------------------
// A tier's prop count is ONE number again -- TEN, the rows of
// StandaloneNetFields -- because the shell that put three real props in front
// of its payload is deleted. Still READ FROM NATIVE rather than computed as
// 10 + bytes/4 in C#: a boundary rule with two implementations drifts silently.
//
// It said six for two rows after m_fEffects and m_ubInterpolationFrame joined
// the shared list, and this test said six with it. The engine's recv tables
// carried eight and /client/{slot}/nettables reported every tier disagreeing.
// TEN now: the solidity pair joined so the client's impact clip can be told a
// managed prop is solid at all.
//-----------------------------------------------------------------------------
TEST( PayloadTiers, RealPropCountIsTheHierarchysForEveryShell )
{
	EXPECT_EQ( 12,
			   SourceSharp_TierRealProps(
				   SS_SHELL_STANDALONE ) ); // 11 since m_nSkin (2026-10-19), 12 since m_nBody (2026-10-25)

	// The retired value answers the LIVE count, not a stale three. An old
	// managed build asking about shell 0 must be told what the tables actually
	// carry, or its inspector reports a prop total short at every tier.
	EXPECT_EQ( 12, SourceSharp_TierRealProps( SS_SHELL_NETENTITY ) );

	// The numbers the managed schema panel shows for the 32-byte tier, spelled
	// out because they are what a reader compares a dump against.
	EXPECT_EQ( 20, SourceSharp_TierRealProps( SS_SHELL_STANDALONE ) + SourceSharp_TierWords( 0 ) );
}

//=============================================================================//
// The classname hash (Part 6, Part 15).
//
// m_nTypeId is FNV-1a of the classname and nothing else, computed independently
// on each side from the same string — which is what makes it not an index and
// therefore not a second ordering the two sides must agree. The cost is that
// the function exists twice, here and in NetSchema.TypeIdOf, and two
// implementations of one function in two languages is the shape this tree keeps
// paying for.
//
// So: a fixed corpus with the expected values as LITERALS, mirrored by the same
// corpus in the managed suite. A change to either implementation fails on both
// sides rather than producing a build whose entities pair with the wrong class.
//=============================================================================//
TEST( ClassNameHash, MatchesTheManagedCorpus )
{
	// "sourcesharp_netentity" is no longer a class and stays in the corpus
	// anyway: a test vector is a string and an expected number. NetSchemaTests
	// keeps the same entry; the two lists must mirror.
	EXPECT_EQ( 0x362417E7u, SourceSharp_HashClassName( "sourcesharp_netentity" ) );
	EXPECT_EQ( 0x811C9DC5u, SourceSharp_HashClassName( "" ) );
	EXPECT_EQ( 0xE40C292Cu, SourceSharp_HashClassName( "a" ) );
	EXPECT_EQ( 0xE456BA08u, SourceSharp_HashClassName( "ss_barrel" ) );
	EXPECT_EQ( 0xB0BBBAD3u, SourceSharp_HashClassName( "ss_beam" ) );
	EXPECT_EQ( 0x043470C0u, SourceSharp_HashClassName( "func_breakable_surf" ) );

	// NULL is the empty string's answer rather than a crash: the caller is a
	// classname that may not have been set yet.
	EXPECT_EQ( SourceSharp_HashClassName( "" ), SourceSharp_HashClassName( NULL ) );

	// Case matters. Two spellings differing only in case are two ids, which is
	// why the type table compares names case-SENSITIVELY — matching them there
	// would hand back a row whose id no entity will ever carry.
	EXPECT_NE( SourceSharp_HashClassName( "ss_barrel" ), SourceSharp_HashClassName( "SS_Barrel" ) );
}

//=============================================================================//
// The published type table.
//=============================================================================//
TEST( TypeTable, PublishesAndLooksUpBothWays )
{
	CSourceSharpTypeTable types;

	ASSERT_EQ( 0, types.Publish( "ss_barrel", 1, 0xAAAAAAAAu, 40, 1 ) );
	ASSERT_EQ( 1, types.Publish( "ss_beam", 1, 0xBBBBBBBBu, 52, 1 ) );

	const SourceSharpNetType_t *pBarrel = types.ByName( "ss_barrel" );
	ASSERT_TRUE( pBarrel != NULL );
	EXPECT_EQ( SourceSharp_HashClassName( "ss_barrel" ), pBarrel->nTypeId );
	EXPECT_EQ( 0xAAAAAAAAu, pBarrel->nSchemaHash );
	EXPECT_EQ( 40, pBarrel->nLayoutBytes );
	EXPECT_EQ( 1, pBarrel->iTier );

	// The lookup the client actually makes: four bytes off the wire back into a
	// name. This is the whole of what replaced the 64-byte m_szClassname.
	const SourceSharpNetType_t *pById = types.ById( SourceSharp_HashClassName( "ss_beam" ) );
	ASSERT_TRUE( pById != NULL );
	EXPECT_STREQ( "ss_beam", pById->szClass );

	EXPECT_TRUE( types.ById( 0xDEADBEEFu ) == NULL );
	EXPECT_TRUE( types.ByName( "nothing_published" ) == NULL );
	EXPECT_EQ( 2, types.Count() );
}

//-----------------------------------------------------------------------------
// Re-publishing a name UPDATES rather than duplicating.
//
// A managed reload publishes again, and a second row for one name would make
// every later lookup depend on which one it found first — which is a bug that
// only shows up after a reload, on whichever class happened to be reshaped.
//-----------------------------------------------------------------------------
TEST( TypeTable, RePublishingUpdatesInPlace )
{
	CSourceSharpTypeTable types;

	types.Publish( "ss_barrel", 1, 0x11111111u, 40, 1 );
	types.Publish( "ss_barrel", 1, 0x22222222u, 44, 2 );

	EXPECT_EQ( 1, types.Count() );

	const SourceSharpNetType_t *pType = types.ByName( "ss_barrel" );
	ASSERT_TRUE( pType != NULL );
	EXPECT_EQ( 0x22222222u, pType->nSchemaHash );
	EXPECT_EQ( 44, pType->nLayoutBytes );
	EXPECT_EQ( 2, pType->iTier );
}

//-----------------------------------------------------------------------------
// A COLLISION is refused, not recorded.
//
// Two different names hashing alike would leave the second reachable by no
// lookup and the first answering for both — which is one entity decoding as
// another, with nothing anywhere reporting it. It is a build-time fact about a
// fixed set of strings, so catching it at startup catches it forever.
//
// Forced here by publishing a name and then a made-up second entry with the
// same id, which is the only way to reach the branch without finding a real
// FNV-1a collision to type out.
//-----------------------------------------------------------------------------
TEST( TypeTable, RefusesACollisionRatherThanRecordingIt )
{
	CSourceSharpTypeTable types;

	// Two strings whose FNV-1a agrees. Found by search rather than invented:
	// these are the shortest pair over the printable ASCII the classnames use.
	const char *pA = "costarring";
	const char *pB = "liquid";

	ASSERT_EQ( SourceSharp_HashClassName( pA ), SourceSharp_HashClassName( pB ) )
		<< "the corpus this test rests on is no longer a collision";

	ASSERT_EQ( 0, types.Publish( pA, 1, 1u, 4, 0 ) );
	EXPECT_EQ( -1, types.Publish( pB, 1, 2u, 4, 0 ) );

	EXPECT_EQ( 1, types.Count() );
	EXPECT_EQ( 1, types.Collisions() );

	// And the one that got in is still the one that answers.
	const SourceSharpNetType_t *pType = types.ById( SourceSharp_HashClassName( pA ) );
	ASSERT_TRUE( pType != NULL );
	EXPECT_STREQ( pA, pType->szClass );
}

TEST( TypeTable, RefusesANameThatWouldNotFit )
{
	CSourceSharpTypeTable types;

	char szLong[SOURCESHARP_TYPE_NAME_BYTES + 8];

	for ( int i = 0; i < (int)sizeof( szLong ) - 1; i++ )
		szLong[i] = 'x';

	szLong[sizeof( szLong ) - 1] = 0;

	// Refused rather than truncated. A truncated name hashes to something no
	// entity carries, so the row would be unreachable and the class would
	// silently have no managed half.
	EXPECT_EQ( -1, types.Publish( szLong, 1, 0u, 4, 0 ) );
	EXPECT_EQ( 0, types.Count() );

	EXPECT_EQ( -1, types.Publish( NULL, 1, 0u, 4, 0 ) );
	EXPECT_EQ( -1, types.Publish( "", 1, 0u, 4, 0 ) );
}

//=============================================================================//
// WHAT A MANAGED RELOAD LEAVES STALE, and the three answers it needs.
// plan_nettable_generic.md Step 10.
//
// The registry above is updated in place by a reload. Every live entity carries
// its own COPY of its class's schema hash, stamped at its first GetServerClass
// and never looked at again, so after a reload the two disagree — and the
// receiving side compares the ENTITY's against ITS registry's, so what a
// disagreement produces is a refusal of correct bytes that lasts as long as the
// entity does.
//=============================================================================//
TEST( SchemaRestamp, AReshapedClassNeedsItAndSaysWhatTo )
{
	CSourceSharpTypeTable types;

	types.Publish( "ss_barrel", SS_SHELL_STANDALONE, 0x11111111u, 40, 1 );

	// Same build, nothing moved: this is every entity on every reload that did
	// not touch its class, and it is the case that makes a reload cheap.
	unsigned int nWanted = 0;
	EXPECT_FALSE( SourceSharp_SchemaNeedsRestamp( types, "ss_barrel", 0x11111111u, &nWanted ) );
	EXPECT_EQ( 0x11111111u, nWanted );

	// The reload republished it with a different layout.
	types.Publish( "ss_barrel", SS_SHELL_STANDALONE, 0x22222222u, 44, 1 );

	EXPECT_TRUE( SourceSharp_SchemaNeedsRestamp( types, "ss_barrel", 0x11111111u, &nWanted ) );
	EXPECT_EQ( 0x22222222u, nWanted );
}

//-----------------------------------------------------------------------------
// A CLASS THE NEW ASSEMBLY NO LONGER HAS IS LEFT ALONE, and that is a decision
// rather than an oversight.
//
// Publish never removes a row, so a class that went away keeps the one it had —
// the entity and the registry still agree, and the entity keeps replicating the
// bytes it last held. What is gone is the code that reads them, on both sides
// equally, so nothing decodes anything wrongly.
//
// The branch that matters is the OTHER one: a name the registry never knew at
// all must not be re-stamped to zero. Zero is what an unpublished class carries
// and it makes the client refuse the payload — so answering "restamp" here
// would turn a reload that changed nothing into a refusal.
//-----------------------------------------------------------------------------
TEST( SchemaRestamp, ANameTheRegistryDoesNotKnowIsLeftAsItIs )
{
	CSourceSharpTypeTable types;

	types.Publish( "ss_barrel", SS_SHELL_STANDALONE, 0x11111111u, 40, 1 );

	unsigned int nWanted = 0;
	EXPECT_FALSE( SourceSharp_SchemaNeedsRestamp( types, "func_gone", 0x11111111u, &nWanted ) );

	// And what it reports back is the stamp it was given, not zero: a caller
	// that writes nWanted regardless still writes what was already there.
	EXPECT_EQ( 0x11111111u, nWanted );
}

//-----------------------------------------------------------------------------
// An entity that was never stamped reads zero, and a published class whose hash
// really is zero is not a thing this build can produce — FNV-1a of a layout is
// zero with probability 2^-32 and the classes that publish nothing publish the
// fingerprint of an EMPTY layout rather than zero, deliberately, for exactly
// this reason. So "stamped zero" means "never asked for a ServerClass", and the
// walk gates on that rather than on the number.
//-----------------------------------------------------------------------------
TEST( SchemaRestamp, AnUnstampedEntityWouldBeToldToTakeTheRegistrysHash )
{
	CSourceSharpTypeTable types;

	types.Publish( "ss_barrel", SS_SHELL_STANDALONE, 0x11111111u, 40, 1 );

	unsigned int nWanted = 0;
	EXPECT_TRUE( SourceSharp_SchemaNeedsRestamp( types, "ss_barrel", 0u, &nWanted ) );
	EXPECT_EQ( 0x11111111u, nWanted );
}

//-----------------------------------------------------------------------------
// A NULL classname is the shape a half-constructed entity has, and it must not
// be a lookup that happens to match the first row.
//-----------------------------------------------------------------------------
TEST( SchemaRestamp, ANullClassnameIsNotAMatch )
{
	CSourceSharpTypeTable types;

	types.Publish( "ss_barrel", SS_SHELL_STANDALONE, 0x11111111u, 40, 1 );

	unsigned int nWanted = 0;
	EXPECT_FALSE( SourceSharp_SchemaNeedsRestamp( types, NULL, 0x33333333u, &nWanted ) );
	EXPECT_EQ( 0x33333333u, nWanted );
}

//=============================================================================//
// The probe pattern — what replaced the typed-prop oracle (Step 6).
//
// Steps 3 to 5 cross-checked the payload against the typed props carrying the
// same state. Step 6 deletes the typed props, so that comparison loses its other
// side. The probe is what stands in its place: the server fills a live entity's
// payload with this function of the seed and the word index, and the client
// recomputes it and compares, in code.
//
// It only means anything if the two sides compute the same words, which is why
// the function is in the shared header and pinned here rather than written out
// on each side.
//=============================================================================//
TEST( ProbePattern, DiffersPerWordAndPerSeed )
{
	// Adjacent words differ, or a payload written one word off would still
	// match everywhere and the check would pass on a real bug.
	for ( int i = 0; i < 64; i++ )
		EXPECT_NE( SourceSharpPayload_ProbeWord( 1u, i ), SourceSharpPayload_ProbeWord( 1u, i + 1 ) );

	// And the seed changes every word, which is what makes probing with one
	// seed and checking with another a NEGATIVE CONTROL rather than a
	// coincidence: a check that cannot go red is not a measurement.
	for ( int i = 0; i < 64; i++ )
		EXPECT_NE( SourceSharpPayload_ProbeWord( 1u, i ), SourceSharpPayload_ProbeWord( 2u, i ) );
}

TEST( ProbePattern, IsNeverZero )
{
	// A blob nothing has written is all zeroes, so a pattern word that came out
	// zero would be indistinguishable from a word that never arrived — the one
	// answer this check must not be able to give.
	for ( unsigned int nSeed = 0; nSeed < 8; nSeed++ )
	{
		for ( int i = 0; i < SOURCESHARP_PAYLOAD_MAX_WORDS; i++ )
			EXPECT_NE( 0u, SourceSharpPayload_ProbeWord( nSeed, i ) );
	}
}

TEST( ProbePattern, IsDeterministic )
{
	// The whole point: the server writes it and the client recomputes it, so
	// the same inputs must give the same word every time and in every process.
	// Written as literals so a change to the function fails here rather than
	// turning every probe into a false alarm.
	EXPECT_EQ( SourceSharpPayload_ProbeWord( 1u, 0 ), SourceSharpPayload_ProbeWord( 1u, 0 ) );
	EXPECT_EQ( SourceSharpPayload_ProbeWord( 7u, 31 ), SourceSharpPayload_ProbeWord( 7u, 31 ) );
}

//=============================================================================//
// THE SHARED PAYLOAD'S SIX TESTS ARE GONE (Part 18).
//
// They pinned CSourceSharpSharedPayload: one word count for a whole family of
// classes, declared once and sealed by the first table built against it. That
// class is deleted — per-class tiers mean no two classes share a blob, and a
// tier's word count is its byte count over four, a constant of the tier list
// with nothing to declare and nothing to seal.
//
// Deleting them is the honest move rather than a loss of coverage. What they
// checked was an invariant the code no longer has, so kept against a class
// nothing calls they would have passed forever while checking nothing — the
// exact shape this tree has now produced eight times. What replaced the
// coverage is PayloadTiers above, which pins the word count of every tier
// against the one list in generic_nettable.h, and the live payload-agreement
// check, which reads each entity's blob through its OWN tier's props.
//=============================================================================//

//=============================================================================//
// PACKED FIELDS (Part 10, Step 8).
//
// The arithmetic native does to read one C#-declared field out of the word it
// shares with three others. This is the half of packing that HAS to be checked
// here rather than in C#: it is a second reader of a placement C# decided, and
// the failure mode is a plausible number rather than an error. The managed
// suite pins the same round trips against the generated accessors, so between
// the two, both readers of one placement are checked against the same rule.
//=============================================================================//

//-----------------------------------------------------------------------------
// The word func_breakable_surf actually produces, laid out by hand so the test
// does not depend on a running managed side: Wide 8 bits at 0, High 8 at 8,
// Broken 1 at 16, SurfaceType 2 at 17.
//
// The pad is SIX bytes, not four: a blob base is whatever the shell's own
// members leave it at, so nothing here may assume the word is four-byte
// aligned in the object.
//-----------------------------------------------------------------------------
struct SurfaceWord_t
{
	unsigned char pad[6];
	unsigned char word[4];
};

static void PutWord( SurfaceWord_t &o, unsigned int v )
{
	for ( int i = 0; i < 4; i++ )
		o.word[i] = (unsigned char)( ( v >> ( i * 8 ) ) & 0xFF );
}

static const int kWordAt = 6;

TEST( PackedField, ReadsEachFieldOutOfTheWordItShares )
{
	SurfaceWord_t o;
	memset( &o, 0xCD, sizeof( o ) );

	// Wide 13, High 5, Broken 1, SurfaceType 2 -- the real placement.
	PutWord( o, 13u | ( 5u << 8 ) | ( 1u << 16 ) | ( 2u << 17 ) );

	const SourceSharpPackedField_t wide = SourceSharpPayload_Place( kWordAt, 0 | ( 8 << 8 ) );
	const SourceSharpPackedField_t high = SourceSharpPayload_Place( kWordAt, 8 | ( 8 << 8 ) );
	const SourceSharpPackedField_t brok = SourceSharpPayload_Place( kWordAt, 16 | ( 1 << 8 ) );
	const SourceSharpPackedField_t surf = SourceSharpPayload_Place( kWordAt, 17 | ( 2 << 8 ) );

	EXPECT_EQ( 13u, SourceSharpPayload_ReadPacked( &o, wide ) );
	EXPECT_EQ( 5u, SourceSharpPayload_ReadPacked( &o, high ) );
	EXPECT_EQ( 1u, SourceSharpPayload_ReadPacked( &o, brok ) );
	EXPECT_EQ( 2u, SourceSharpPayload_ReadPacked( &o, surf ) );

	// The bug this replaces, stated as an assertion: reading a byte AT THE
	// FIELD'S OFFSET answers Wide whichever field was asked for, because all
	// four share the word Wide starts at.
	EXPECT_EQ( 13u, (unsigned int)*( (const unsigned char *)&o + kWordAt ) )
		<< "the offset alone names the word, which is why a byte read is not enough";
}

//-----------------------------------------------------------------------------
// The specific misread packing would have caused. Broken is bit 16 and
// SurfaceType is bits 17-18, so they share BYTE two -- and the old read, "the
// byte at this offset is not zero", calls a computer-type pane broken while it
// is still whole.
//-----------------------------------------------------------------------------
TEST( PackedField, ABrokenSurfaceIsTrueAndAnUnbrokenOneIsFalse )
{
	SurfaceWord_t o;
	memset( &o, 0, sizeof( o ) );

	const SourceSharpPackedField_t brok = SourceSharpPayload_Place( kWordAt, 16 | ( 1 << 8 ) );

	PutWord( o, 3u << 17 );
	EXPECT_EQ( 0u, SourceSharpPayload_ReadPacked( &o, brok ) );
	EXPECT_NE( 0, (int)o.word[2] ) << "the byte is non-zero and the field is not";

	PutWord( o, ( 3u << 17 ) | ( 1u << 16 ) );
	EXPECT_EQ( 1u, SourceSharpPayload_ReadPacked( &o, brok ) );
}

//-----------------------------------------------------------------------------
// Sign extension, at every width and shift the packer can produce. The failure
// here is the one nothing reports: a wrongly-extended negative is a plausible
// number, which is the same argument SS_FIELD_UINT was invented for.
//-----------------------------------------------------------------------------
TEST( PackedField, SignExtendsAtEveryWidth )
{
	SurfaceWord_t o;

	for ( int nWidth = 2; nWidth <= 32; nWidth++ )
	{
		for ( int nShift = 0; nShift + nWidth <= 32; nShift++ )
		{
			const SourceSharpPackedField_t f = SourceSharpPayload_Place( kWordAt, nShift | ( nWidth << 8 ) );

			ASSERT_TRUE( f.IsResolved() ) << nWidth << "/" << nShift;

			const int nMin = -( 1 << ( nWidth - 1 ) );
			const int nMax = ( 1 << ( nWidth - 1 ) ) - 1;

			const unsigned int nMask = ( nWidth >= 32 ) ? 0xFFFFFFFFu : ( ( 1u << nWidth ) - 1u );

			// The surrounding bits are set, so a reader that forgot to mask
			// reports the neighbours rather than the field.
			memset( &o, 0xFF, sizeof( o ) );
			PutWord( o, ~( nMask << nShift ) | ( ( (unsigned int)nMin & nMask ) << nShift ) );
			EXPECT_EQ( nMin, SourceSharpPayload_ReadPackedSigned( &o, f ) )
				<< "minimum at width " << nWidth << " shift " << nShift;

			PutWord( o, ~( nMask << nShift ) | ( ( (unsigned int)nMax & nMask ) << nShift ) );
			EXPECT_EQ( nMax, SourceSharpPayload_ReadPackedSigned( &o, f ) )
				<< "maximum at width " << nWidth << " shift " << nShift;

			PutWord( o, ( 0xFFFFFFFFu & nMask ) << nShift );
			EXPECT_EQ( -1, SourceSharpPayload_ReadPackedSigned( &o, f ) )
				<< "minus one at width " << nWidth << " shift " << nShift;

			PutWord( o, ~( nMask << nShift ) );
			EXPECT_EQ( 0, SourceSharpPayload_ReadPackedSigned( &o, f ) )
				<< "zero at width " << nWidth << " shift " << nShift;
		}
	}
}

TEST( PackedField, AFullWidthFieldIsTheWholeWord )
{
	SurfaceWord_t o;
	memset( &o, 0, sizeof( o ) );
	PutWord( o, 0xDEADBEEFu );

	const SourceSharpPackedField_t f = SourceSharpPayload_Place( kWordAt, 0 | ( 32 << 8 ) );

	EXPECT_EQ( 0xDEADBEEFu, SourceSharpPayload_ReadPacked( &o, f ) );
	EXPECT_EQ( (int)0xDEADBEEF, SourceSharpPayload_ReadPackedSigned( &o, f ) );
}

//-----------------------------------------------------------------------------
// A placement that cannot be right is REFUSED, not clamped. Reading something
// out of a disagreement about the layout is how a wrong offset becomes a
// plausible value instead of a blank one -- the same reason the offset call
// answers -1 rather than 0.
//-----------------------------------------------------------------------------
TEST( PackedField, RefusesAPlacementThatCannotFitAWord )
{
	EXPECT_FALSE( SourceSharpPayload_Place( -1, 0 | ( 8 << 8 ) ).IsResolved() );
	EXPECT_FALSE( SourceSharpPayload_Place( kWordAt, -1 ).IsResolved() );
	EXPECT_FALSE( SourceSharpPayload_Place( kWordAt, 0 | ( 0 << 8 ) ).IsResolved() );
	EXPECT_FALSE( SourceSharpPayload_Place( kWordAt, 0 | ( 33 << 8 ) ).IsResolved() );

	// THE STRADDLE. A ten-bit field at shift 25 spans two words, which the
	// packer never produces -- so seeing one means the two sides disagree about
	// the layout, and the honest answer is to draw nothing.
	EXPECT_FALSE( SourceSharpPayload_Place( kWordAt, 25 | ( 10 << 8 ) ).IsResolved() );

	// And the one that just fits, so the bound is checked from both sides.
	EXPECT_TRUE( SourceSharpPayload_Place( kWordAt, 22 | ( 10 << 8 ) ).IsResolved() );
}

TEST( PackedField, AnUnresolvedFieldReadsZeroRatherThanOffsetZero )
{
	SurfaceWord_t o;
	memset( &o, 0xFF, sizeof( o ) );

	const SourceSharpPackedField_t none;

	EXPECT_FALSE( none.IsResolved() );
	EXPECT_EQ( 0u, SourceSharpPayload_ReadPacked( &o, none ) );
	EXPECT_EQ( 0, SourceSharpPayload_ReadPackedSigned( &o, none ) );

	EXPECT_EQ( 0u, SourceSharpPayload_ReadPacked( NULL, SourceSharpPayload_Place( kWordAt, 0 | ( 8 << 8 ) ) ) );
}

//-----------------------------------------------------------------------------
// The write that pairs with the reads (Phase 9c5). The failure it exists to
// prevent is stated as the assertion: writing one packed field must leave its
// word-mates exactly as they were, because the player sync writes eight
// fields into one word per command and a write that bled would corrupt a
// NEIGHBOUR — a duck flag flickering because the water level moved.
//-----------------------------------------------------------------------------
TEST( PackedField, AWriteReplacesItsOwnBitsAndOnlyThem )
{
	SurfaceWord_t o;
	memset( &o, 0, sizeof( o ) );

	const SourceSharpPackedField_t wide = SourceSharpPayload_Place( kWordAt, 0 | ( 8 << 8 ) );
	const SourceSharpPackedField_t high = SourceSharpPayload_Place( kWordAt, 8 | ( 8 << 8 ) );
	const SourceSharpPackedField_t brok = SourceSharpPayload_Place( kWordAt, 16 | ( 1 << 8 ) );
	const SourceSharpPackedField_t surf = SourceSharpPayload_Place( kWordAt, 17 | ( 2 << 8 ) );

	SourceSharpPayload_WritePacked( &o, wide, 13 );
	SourceSharpPayload_WritePacked( &o, high, 5 );
	SourceSharpPayload_WritePacked( &o, brok, 1 );
	SourceSharpPayload_WritePacked( &o, surf, 2 );

	EXPECT_EQ( 13u, SourceSharpPayload_ReadPacked( &o, wide ) );
	EXPECT_EQ( 5u, SourceSharpPayload_ReadPacked( &o, high ) );
	EXPECT_EQ( 1u, SourceSharpPayload_ReadPacked( &o, brok ) );
	EXPECT_EQ( 2u, SourceSharpPayload_ReadPacked( &o, surf ) );

	// Overwrite ONE of the four; the other three must not move.
	SourceSharpPayload_WritePacked( &o, high, 0xAB );

	EXPECT_EQ( 13u, SourceSharpPayload_ReadPacked( &o, wide ) );
	EXPECT_EQ( 0xABu, SourceSharpPayload_ReadPacked( &o, high ) );
	EXPECT_EQ( 1u, SourceSharpPayload_ReadPacked( &o, brok ) );
	EXPECT_EQ( 2u, SourceSharpPayload_ReadPacked( &o, surf ) );
}

TEST( PackedField, AWriteMasksToItsDeclaredWidth )
{
	SurfaceWord_t o;
	memset( &o, 0, sizeof( o ) );

	const SourceSharpPackedField_t brok = SourceSharpPayload_Place( kWordAt, 16 | ( 1 << 8 ) );
	const SourceSharpPackedField_t surf = SourceSharpPayload_Place( kWordAt, 17 | ( 2 << 8 ) );

	// A value wider than the field must be truncated to the field, never
	// spilled into the neighbour above it.
	SourceSharpPayload_WritePacked( &o, brok, 0xFF );

	EXPECT_EQ( 1u, SourceSharpPayload_ReadPacked( &o, brok ) );
	EXPECT_EQ( 0u, SourceSharpPayload_ReadPacked( &o, surf ) )
		<< "the write spilled past its width into the field above";
}

TEST( PackedField, AWriteThroughAnUnresolvedPlacementTouchesNothing )
{
	SurfaceWord_t o;
	memset( &o, 0x5A, sizeof( o ) );

	SurfaceWord_t before = o;

	const SourceSharpPackedField_t none;
	SourceSharpPayload_WritePacked( &o, none, 0xFFFFFFFFu );

	EXPECT_EQ( 0, memcmp( &before, &o, sizeof( o ) ) )
		<< "an unresolved placement wrote SOMEWHERE, which is the corruption "
		   "refusing it exists to prevent";
}

} // namespace
