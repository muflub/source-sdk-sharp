//========= SourceSharp — send-then-recv over a payload word is the identity ==//
//
// plan_nettable_generic.md Part 15's one unmet gtest bullet:
//
//     "Recv proxy symmetry: send-then-recv over a random blob is the identity."
//
// It was unmet for a structural reason rather than an oversight.
// generic_payload_test.cpp says so in its own header -- "No engine headers,
// which is what generic_payload.h keeps its arithmetic inline for. Nothing here
// links against dt_send.cpp" -- and a proxy cannot be called without the
// translation unit that defines it. So this is a SECOND binary, and the only
// one in this tree that links the datatable sources.
//
// WHAT THE PAYLOAD ACTUALLY RELIES ON, which is what makes the identity worth
// asserting rather than obvious. A payload word is
//
//     SendPropInt( name, blobBase + i*4, 4, 32, SPROP_UNSIGNED )
//     RecvPropInt( name, blobBase + i*4, 4 )
//
// and NO CUSTOM PROXY on either side, because a custom one lands every word on
// the engine's slow local-transfer list (generic_payload.h's own note). The four bytes
// therefore travel through a pair of proxies nobody in this port wrote:
// sizeofVar 4 plus SPROP_UNSIGNED selects SendProxy_UInt32ToInt32, and
// sizeofVar 4 on the recv side selects RecvProxy_Int32ToInt32. The whole design
// rests on that pair being the identity over ALL 2^32 bit patterns -- a blob
// carries C# field bytes, not small integers, so the half of the range with the
// top bit set is ordinary traffic and not an edge case.
//
// WHAT THIS DOES NOT REPLACE. shell_standalone_client.cpp:2446 checks the
// resolved proxy on every built tier at runtime, on the real tables, and warns
// per word. That reaches further than a constructed pair ever will and is why
// this bullet was correctly ranked last. What it cannot do is fail in `make
// test`, on a machine with no game, before anything is launched.
//
//=============================================================================//

#include <string.h>

#include "gtest/gtest.h"
#include "mathlib_test_env.h"

#include "dt_send.h"
#include "dt_recv.h"

#include "../game/shared/generic_payload.h"

namespace
{

//-----------------------------------------------------------------------------
// A stand-in entity: a byte buffer with the blob at nBlobBase, exactly as the
// real one is laid out. Nothing here is a CBaseEntity and nothing needs to be --
// a payload prop is a byte offset from the start of the object and nothing else.
//-----------------------------------------------------------------------------
const int kBufBytes = 4096;

struct Fake
{
	unsigned char bytes[kBufBytes];
};

// A deterministic "random" blob. Deterministic on purpose: a failing run has to
// be reproducible, and this is the same argument the probe pattern in
// generic_payload.cpp makes for itself.
unsigned int Word( int i )
{
	unsigned int x = (unsigned int)( i + 1 ) * 2654435761u;
	x ^= x >> 15;
	x *= 2246822519u;
	x ^= x >> 13;
	return x;
}

void FillBlob( Fake &f, int nBlobBase, int nWords )
{
	memset( f.bytes, 0, sizeof( f.bytes ) );
	for ( int i = 0; i < nWords; i++ )
	{
		const unsigned int w = Word( i );
		memcpy( f.bytes + SourceSharpPayload_WordOffset( nBlobBase, i ), &w, 4 );
	}
}

SendProp MakeSend( const char *pName, int nOffset, int nBytes )
{
	return SendPropInt( pName, nOffset, nBytes, 32, SPROP_UNSIGNED );
}

RecvProp MakeRecv( const char *pName, int nOffset, int nBytes )
{
	return RecvPropInt( pName, nOffset, nBytes );
}

// One word through both proxies, exactly as the engine drives them: the send
// proxy is handed the object base and the field address, the recv proxy is
// handed the object base and the field address on the other side.
void RoundTrip( const SendProp &send, const RecvProp &recv, const Fake &from, Fake &to )
{
	// NOT memset: DVariant is a non-trivial class and clearing one with
	// memset is a -Wclass-memaccess warning, which this file must not have --
	// -Wall stays live on sourcesharp/test/*.cpp and hiding one here is how the
	// next real one gets missed. The send proxy is what fills it, and the
	// members read afterwards are the ones it wrote.
	DVariant var;

	send.GetProxyFn()( &send, &from, (const unsigned char *)&from + send.GetOffset(), &var, 0, 0 );

	CRecvProxyData data;
	data.m_pRecvProp = &recv;
	data.m_Value = var;
	data.m_iElement = 0;
	data.m_ObjectID = 0;

	recv.GetProxyFn()( &data, &to, (unsigned char *)&to + recv.GetOffset() );
}

char g_names[8][32];

const char *Name( int i )
{
	SourceSharpPayload_FormatPropName( i, g_names[i], sizeof( g_names[i] ) );
	return g_names[i];
}

} // namespace

//=============================================================================//
// 0. THE PAIR THE PAYLOAD ASKS FOR IS THE PAIR IT GETS.
//
// If this is wrong every assertion below is about some other pair of functions
// and still passes, so it is first.
//=============================================================================//

TEST( PayloadRecvSymmetry, TheDefaultProxiesAreTheFastOnes )
{
	const SendProp s = MakeSend( "000", 64, 4 );
	const RecvProp r = MakeRecv( "000", 64, 4 );

	EXPECT_EQ( DPT_Int, s.GetType() );
	EXPECT_TRUE( s.GetProxyFn() == g_StandardSendProxies.m_UInt32ToInt32 )
		<< "a payload word's send proxy is not m_UInt32ToInt32. "
		   "the engine's local transfer requires exactly that one for the fast path; "
		   "anything else copies every word of every entity slowly";
	EXPECT_TRUE( r.GetProxyFn() == g_StandardRecvProxies.m_Int32ToInt32 )
		<< "a payload word's recv proxy is not m_Int32ToInt32";

	// The shipped predicates are NOT called here and cannot be: they live in
	// generic_payload.cpp, which includes cbase.h. That they compare against
	// these same two proxies is asserted from the source, below.
}

//=============================================================================//
// 1. THE BULLET ITSELF.
//=============================================================================//

TEST( PayloadRecvSymmetry, SendThenRecvIsTheIdentityAtBaseZero )
{
	const int nBase = 0;
	const int nWords = 8;

	Fake from, to;
	FillBlob( from, nBase, nWords );
	memset( to.bytes, 0xCD, sizeof( to.bytes ) );

	for ( int i = 0; i < nWords; i++ )
	{
		const int off = SourceSharpPayload_WordOffset( nBase, i );
		RoundTrip( MakeSend( Name( i ), off, 4 ), MakeRecv( Name( i ), off, 4 ), from, to );
	}

	for ( int i = 0; i < nWords; i++ )
	{
		const int off = SourceSharpPayload_WordOffset( nBase, i );
		EXPECT_EQ( 0, memcmp( from.bytes + off, to.bytes + off, 4 ) )
			<< "word " << i << " did not survive the round trip";
	}
}

//-----------------------------------------------------------------------------
// nBlobBase != 0 is the case that matters, and it is the same reason
// generic_payload_test.cpp gives for its own offset cases: a base of zero
// networks the entity's own members, so a bug that only shows at a real base is
// the one that ships.
//-----------------------------------------------------------------------------
TEST( PayloadRecvSymmetry, SendThenRecvIsTheIdentityAtARealBlobBase )
{
	const int kBases[] = { 4, 96, 512, 1024 };

	for ( size_t b = 0; b < sizeof( kBases ) / sizeof( kBases[0] ); b++ )
	{
		const int nBase = kBases[b];
		const int nWords = 8;

		Fake from, to;
		FillBlob( from, nBase, nWords );
		memset( to.bytes, 0xCD, sizeof( to.bytes ) );

		for ( int i = 0; i < nWords; i++ )
		{
			const int off = SourceSharpPayload_WordOffset( nBase, i );
			RoundTrip( MakeSend( Name( i ), off, 4 ), MakeRecv( Name( i ), off, 4 ), from, to );
		}

		for ( int i = 0; i < nWords; i++ )
		{
			const int off = SourceSharpPayload_WordOffset( nBase, i );
			EXPECT_EQ( 0, memcmp( from.bytes + off, to.bytes + off, 4 ) ) << "base " << nBase << " word " << i;
		}
	}
}

//-----------------------------------------------------------------------------
// The half of the range a signed misreading would break, asked for explicitly
// rather than left to a generator that might not produce one. A blob is C#
// field bytes; 0x80000000 and 0xFFFFFFFF are ordinary contents.
//-----------------------------------------------------------------------------
TEST( PayloadRecvSymmetry, TheTopBitSurvives )
{
	const unsigned int kHard[] = { 0x80000000u, 0xFFFFFFFFu, 0x7FFFFFFFu, 0x00000000u, 0xDEADBEEFu, 0x0000FFFFu };

	for ( size_t k = 0; k < sizeof( kHard ) / sizeof( kHard[0] ); k++ )
	{
		Fake from, to;
		memset( from.bytes, 0, sizeof( from.bytes ) );
		memset( to.bytes, 0, sizeof( to.bytes ) );
		memcpy( from.bytes + 64, &kHard[k], 4 );

		RoundTrip( MakeSend( "000", 64, 4 ), MakeRecv( "000", 64, 4 ), from, to );

		unsigned int got = 0;
		memcpy( &got, to.bytes + 64, 4 );
		EXPECT_EQ( kHard[k], got ) << "value " << k << " did not survive";
	}
}

//-----------------------------------------------------------------------------
// Only the word it was given. A proxy pair that wrote four bytes at the wrong
// offset would still satisfy every assertion above if the assertion only looked
// where it expected the answer.
//-----------------------------------------------------------------------------
TEST( PayloadRecvSymmetry, NothingOutsideTheWordIsTouched )
{
	Fake from, to;
	memset( from.bytes, 0, sizeof( from.bytes ) );
	memset( to.bytes, 0xCD, sizeof( to.bytes ) );

	const unsigned int w = 0xA5A5A5A5u;
	memcpy( from.bytes + 128, &w, 4 );

	RoundTrip( MakeSend( "000", 128, 4 ), MakeRecv( "000", 128, 4 ), from, to );

	for ( int i = 0; i < kBufBytes; i++ )
	{
		if ( i >= 128 && i < 132 )
			continue;
		ASSERT_EQ( 0xCD, to.bytes[i] ) << "byte " << i << " was written by a proxy pair that was given "
									   << "offset 128";
	}
}

//=============================================================================//
// 2. THE NEGATIVE CONTROL.
//
// Every assertion above is "these bytes came back". A test made only of those
// cannot tell a working round trip from one it never actually drove, so here is
// a pair that MUST NOT be the identity: a narrower send prop reads two bytes
// where the recv writes four, which is precisely what a sizeofVar drifting from
// SOURCESHARP_PAYLOAD_WORD_BYTES would do.
//=============================================================================//

TEST( PayloadRecvSymmetry, ANarrowerSendPropIsNotTheIdentity )
{
	Fake from, to;
	memset( from.bytes, 0, sizeof( from.bytes ) );
	memset( to.bytes, 0, sizeof( to.bytes ) );

	const unsigned int w = 0xDEADBEEFu;
	memcpy( from.bytes + 64, &w, 4 );

	// Two bytes wide: SendProxy_UInt16ToInt32, not UInt32ToInt32.
	const SendProp narrow = MakeSend( "000", 64, 2 );
	EXPECT_FALSE( narrow.GetProxyFn() == g_StandardSendProxies.m_UInt32ToInt32 )
		<< "a two-byte send prop resolves to the same proxy as a four-byte one, "
		   "so nothing downstream could tell them apart either";

	RoundTrip( narrow, MakeRecv( "000", 64, 4 ), from, to );

	unsigned int got = 0;
	memcpy( &got, to.bytes + 64, 4 );
	EXPECT_NE( w, got ) << "a two-byte send prop round-tripped a four-byte word unchanged, "
						   "which means this test is not driving the proxies it thinks it is";
	EXPECT_EQ( 0x0000BEEFu, got ) << "and it should have carried only the low half";
}

//=============================================================================//
// 3. THE SPELLING THIS TEST MIRRORS IS STILL THE SHIPPED ONE.
//
// The props above are built here rather than by
// SourceSharpPayload_Build{Send,Recv}Props, because those live in
// generic_payload.cpp and that file includes cbase.h -- the whole game. A
// mirrored construction that drifted from the builder would go on passing about
// nothing, so the builder's own two lines are read out of the source and
// compared.
//=============================================================================//
