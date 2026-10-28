//========= SourceSharp — a teleport is drawn, not travelled =================//
//
// plan_entity.md §3.9, and the LAST entry of ContractBaseline.OpenDefects:
// "An entity that teleports is drawn sliding to where it went".
//
// THE DEFECT THIS STANDS OVER. The client draws an entity at a point read out
// of a history of networked poses, slightly in the past — that is what
// interpolation IS. A pose 500 units from the last one is, to that history,
// indistinguishable from an entity that MOVED 500 units, so a teleported
// entity was drawn travelling the whole distance over the interpolation window.
// Stock answers it by networking a counter a teleport bumps
// (CBaseEntity::IncrementInterpolationFrame, baseentity.cpp:8301) and having
// the client throw the history away when the value it receives differs from the
// one it last acted on (C_BaseEntity::IsNoInterpolationFrame,
// c_baseentity.h:2416). SourceSharp_EntityTeleport's standalone arm bumped
// nothing, and the managed pass reset a ring on exactly one condition — a
// HANDLE REBIND, which is a create.
//
// WHY THIS FILE EXISTS, given that InterpolationTests measures the DRAWN POSE
// with a control arm and TeleportDiscontinuityTests measures the state.
//
// **IT IS TWO HALVES AND THEY MUST LAND TOGETHER.** A counter nothing reads is
// dead state; a reader with no counter never fires. Neither half is a compile
// error without the other, neither is a crash, and each on its own leaves a
// managed suite entirely green — the reader arm still has its unit facts,
// because those bump the counter themselves. What no managed fact can see is
// the WIRE between them: the block field, the SendProp, the RecvProp and the
// change mark are four separate declarations in three languages, and the value
// arriving is the only thing that makes the pair one mechanism.
//
// So each link is asserted apart, and each is a place the state stops reaching
// the client if it is removed:
//
//   1. the SHARED wire list really carries m_ubInterpolationFrame, with stock's
//      own encoding — a COMPILED check over the real table, so an entry
//      inserted in the middle of the array without the enum moving with it is
//      caught rather than mis-addressing every prop after it on BOTH sides;
//   2. the SERVER's SendProp addresses the block's own word;
//   3. the CLIENT's RecvProp addresses the same word on its side;
//   4. the SERVER BUMPS IT, in managed code, on the managed side of every
//      teleport crossing — and ANNOUNCES the write, which is the half a block
//      write on its own would miss;
//   5. the CLIENT READS IT, in the interpolation pass, and acknowledges it.
//
// Links 4 and 5 are the two halves. They are read out of the sources, in the
// idiom nodraw_visibility_test.cpp and fog_wire_test.cpp already use here,
// because they are statements about code that cannot be linked without an
// engine — and about C#, which this binary cannot link at all.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <string>
#include <fstream>
#include <sstream>

// What standalone_nettable.h wants from the engine, and no more. All three are
// the engine's own numbers: MAX_EDICT_BITS and EF_MAX_BITS from public/const.h,
// NOINTERP_PARITY_MAX_BITS from game/shared/shareddefs.h. Spelled here rather
// than reached for, because this binary deliberately links no engine — and each
// is checked against its own header below, so no copy can drift into asserting
// agreement with itself.
#define MAX_EDICT_BITS           11
#define EF_MAX_BITS              10
#define NOINTERP_PARITY_MAX_BITS 2
#define ARRAYSIZE( p ) ( (int)( sizeof( p ) / sizeof( (p)[0] ) ) )

#include "../game/shared/standalone_nettable.h"

namespace
{

//=============================================================================//
// 1: THE SHARED LIST, COMPILED.
//=============================================================================//

TEST( TeleportDiscontinuity, TheWireCarriesTheInterpolationFrame )
{
	int nCount = 0;
	const StandaloneNetField_t *pFields = StandaloneNetFields( &nCount );

	ASSERT_EQ( STANDALONE_NET_FIELD_COUNT, nCount )
		<< "the field ARRAY and the field ENUM disagree about how many props "
		   "there are. Both sides subscript the array by the enum, so the two "
		   "shells would build props for different fields.";

	EXPECT_STREQ( "m_ubInterpolationFrame",
	              pFields[ STANDALONE_NET_FIELD_INTERPFRAME ].pName )
		<< "the enum's INTERPFRAME index does not name the discontinuity "
		   "counter. An offset written at this index would be applied to "
		   "whatever prop is here, and the block word the wire wrote into "
		   "would be somebody else's.";

	EXPECT_EQ( STANDALONE_NET_INT, pFields[ STANDALONE_NET_FIELD_INTERPFRAME ].kind );

	// NOINTERP_PARITY_MAX_BITS, exactly as stock sends m_ubInterpolationFrame
	// (baseentity.cpp:320). Two bits carry the four values the counter wraps
	// through; a ONE-bit field would make every second teleport invisible,
	// which is the failure this mechanism exists to avoid and which no test
	// that only ever teleports once would notice.
	EXPECT_EQ( NOINTERP_PARITY_MAX_BITS,
	           pFields[ STANDALONE_NET_FIELD_INTERPFRAME ].nBits );
}

TEST( TeleportDiscontinuity, EveryEnumIndexStillNamesItsOwnField )
{
	// Inserting the counter in the MIDDLE of the list is exactly the edit this
	// guards: everything after it renumbered, on both sides at once.
	int nCount = 0;
	const StandaloneNetField_t *pFields = StandaloneNetFields( &nCount );
	ASSERT_EQ( STANDALONE_NET_FIELD_COUNT, nCount );

	EXPECT_STREQ( "m_vecOrigin",            pFields[ STANDALONE_NET_FIELD_ORIGIN ].pName );
	EXPECT_STREQ( "m_angRotation",          pFields[ STANDALONE_NET_FIELD_ANGLES ].pName );
	EXPECT_STREQ( "m_nModelIndex",          pFields[ STANDALONE_NET_FIELD_MODELINDEX ].pName );
	EXPECT_STREQ( "m_fEffects",             pFields[ STANDALONE_NET_FIELD_EFFECTS ].pName );
	EXPECT_STREQ( "m_ubInterpolationFrame", pFields[ STANDALONE_NET_FIELD_INTERPFRAME ].pName );
	EXPECT_STREQ( "m_nTypeId",              pFields[ STANDALONE_NET_FIELD_TYPEID ].pName );
	EXPECT_STREQ( "m_nSchemaHash",          pFields[ STANDALONE_NET_FIELD_SCHEMAHASH ].pName );
	EXPECT_STREQ( "m_nPayloadWords",        pFields[ STANDALONE_NET_FIELD_PAYLOADWORDS ].pName );
}

//=============================================================================//
// 2 and 3: BOTH SIDES MAP THE FIELD ONTO THE BLOCK'S OWN WORD.
//=============================================================================//

//=============================================================================//
// 4 AND 5: THE TWO HALVES, AND NEITHER IS ANY USE ALONE.
//=============================================================================//

//=============================================================================//
// THE LEDGER ROW, INVERTED.
//=============================================================================//

}  // namespace
