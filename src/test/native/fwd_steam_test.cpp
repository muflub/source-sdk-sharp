//========= SourceSharp — libss_fwd_steam's callback, driven as Steam drives it //
//
// plan_engine_forwarders.md section 6. libss_fwd_steam builds the
// CCallbackBase Steam registers, so the compiler, not managed, orders its two
// Run overloads. Each fact calls the library's table the way managed does
// (SteamCallbackFwd: slot N is field N) and then the object the way Steam
// does: through CCallbackBase's virtuals, which reach whatever slot this
// compiler gave each overload. Both must land on the one managed-built
// method, IManagedSteamCallback::Run, with the payload unchanged.
//
// STEAM IS CCallbackMgr HERE. CCallbackBase names it its friend, and
// SteamAPI_RegisterCallback is what writes the registered flag and the id
// into the object; this file defines it to write them exactly so, which is
// the only way a test may touch those protected members of an object it did
// not build.
//
// fwd_steam.cpp is compiled into this binary, with fwd_engine.cpp and
// fwd_vphysics.cpp for the serve helper (the CMake row says why); the built
// library's export set is fwd_libraries_isolation_test's.
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_fwd.h"

#include "steam/steam_api_common.h"
#include "imanaged_steam_callback.h"

#include <stdint.h>

// What libsteam_api does to a callback it registers and unregisters.
class CCallbackMgr
{
public:
	static void Register( CCallbackBase *pCallback, int iCallback )
	{
		pCallback->m_nCallbackFlags |= CCallbackBase::k_ECallbackFlagsRegistered;
		pCallback->m_iCallback = iCallback;
	}

	static void Unregister( CCallbackBase *pCallback )
	{
		pCallback->m_nCallbackFlags &= ~CCallbackBase::k_ECallbackFlagsRegistered;
	}
};

namespace
{

// The table's slots, in SteamCallbackFwd's order.
enum
{
	kSizeOf,
	kConstruct,
	kIsRegistered,
	kCallbackId,
	kSteamCallbackSlots
};

const SSForwarderTable &SteamCallbackTable()
{
	const SSForwarderTable *pTable = SSFwdSteam_Table( "SSFwd_SteamCallback" );
	EXPECT_NE( nullptr, pTable );
	static const SSForwarderTable kNone = { nullptr, 0 };
	return pTable ? *pTable : kNone;
}

template < typename F >
F Slot( int nSlot )
{
	return reinterpret_cast< F >( SteamCallbackTable().ppSlots[ nSlot ] );
}

int SizeOf() { return Slot< int ( * )() >( kSizeOf )(); }

CCallbackBase *Build( void *pMemory, IManagedSteamCallback *pTarget, int nPayloadBytes )
{
	return Slot< CCallbackBase *( * )( void *, IManagedSteamCallback *, int ) >( kConstruct )( pMemory, pTarget, nPayloadBytes );
}

bool IsRegistered( CCallbackBase *pCallback ) { return Slot< bool ( * )( CCallbackBase * ) >( kIsRegistered )( pCallback ); }

int CallbackId( CCallbackBase *pCallback ) { return Slot< int ( * )( CCallbackBase * ) >( kCallbackId )( pCallback ); }

// The managed side: counts its runs and keeps the last payload.
class RecordingTarget final : public IManagedSteamCallback
{
public:
	void Release() override { ++nReleases; }
	int32_t Validate( const uint32_t *, int32_t ) override { return 0; }
	void Run( void *pvParam ) override
	{
		++nRuns;
		pLast = pvParam;
	}

	int nRuns = 0;
	int nReleases = 0;
	void *pLast = nullptr;
};

// Memory as managed allocates it: SizeOf bytes, pointer-aligned. Generous, so
// a SizeOf that answered too little shows as a mismatch, not a corruption.
struct Memory
{
	alignas( 16 ) unsigned char bytes[ 256 ];
};

} // namespace

TEST( FwdSteam, TheLibraryServesTheCallbackTableAndNothingUnderOtherNames )
{
	const SSForwarderTable &table = SteamCallbackTable();
	ASSERT_NE( nullptr, table.ppSlots );
	EXPECT_EQ( (int32_t)kSteamCallbackSlots, table.nSlots );

	EXPECT_EQ( nullptr, SSFwdSteam_Table( "SSFwd_SteamCallbac" ) );
	EXPECT_EQ( nullptr, SSFwdSteam_Table( "SSFwd_ModelInfo" ) );
	EXPECT_EQ( nullptr, SSFwdSteam_Table( nullptr ) );
}

TEST( FwdSteam, SizeOfIsACallbackBaseAndItsTwoMembersRoundedToAWord )
{
	const int nBytes = SizeOf();
	EXPECT_GE( nBytes, int( sizeof( CCallbackBase ) + sizeof( void * ) + sizeof( int ) ) );
	EXPECT_EQ( 0, nBytes % int( sizeof( void * ) ) );
	EXPECT_LE( nBytes, int( sizeof( Memory ) ) );
}

TEST( FwdSteam, ConstructBuildsTheCallbackInTheMemoryItIsGiven )
{
	Memory memory;
	RecordingTarget target;

	CCallbackBase *pCallback = Build( memory.bytes, &target, 8 );

	EXPECT_EQ( static_cast< void * >( memory.bytes ), static_cast< void * >( pCallback ) );
	EXPECT_FALSE( IsRegistered( pCallback ) );
	EXPECT_EQ( 0, CallbackId( pCallback ) );
	EXPECT_EQ( 0, target.nRuns );
}

TEST( FwdSteam, ConstructRefusesNoMemoryOrNoTarget )
{
	Memory memory;
	RecordingTarget target;

	EXPECT_EQ( nullptr, Build( nullptr, &target, 8 ) );
	EXPECT_EQ( nullptr, Build( memory.bytes, nullptr, 8 ) );
}

TEST( FwdSteam, SteamsRunReachesTheManagedRunWithThePayload )
{
	Memory memory;
	RecordingTarget target;
	CCallbackBase *pCallback = Build( memory.bytes, &target, 8 );
	unsigned char payload[ 8 ] = { 5, 0, 0, 0, 1, 0, 0, 0 };

	pCallback->Run( payload );

	EXPECT_EQ( 1, target.nRuns );
	EXPECT_EQ( static_cast< void * >( payload ), target.pLast );
}

TEST( FwdSteam, TheCallResultOverloadReachesTheSameManagedRun )
{
	Memory memory;
	RecordingTarget target;
	CCallbackBase *pCallback = Build( memory.bytes, &target, 4 );
	unsigned char payload[ 4 ] = {};

	pCallback->Run( payload, true, SteamAPICall_t( 99 ) );

	EXPECT_EQ( 1, target.nRuns );
	EXPECT_EQ( static_cast< void * >( payload ), target.pLast );
}

TEST( FwdSteam, TheSizeSteamChecksIsThePayloadSizeConstructWasGiven )
{
	Memory memory;
	RecordingTarget target;

	EXPECT_EQ( 264, Build( memory.bytes, &target, 264 )->GetCallbackSizeBytes() );
	EXPECT_EQ( 0, target.nRuns );   // answered natively, no call into managed
}

TEST( FwdSteam, EachCallbackRunsItsOwnTarget )
{
	Memory a, b;
	RecordingTarget targetA, targetB;
	CCallbackBase *pA = Build( a.bytes, &targetA, 1 );
	CCallbackBase *pB = Build( b.bytes, &targetB, 4 );
	unsigned char payload[ 4 ] = {};

	pB->Run( payload );

	EXPECT_EQ( 0, targetA.nRuns );
	EXPECT_EQ( 1, targetB.nRuns );
	EXPECT_EQ( 1, pA->GetCallbackSizeBytes() );
	EXPECT_EQ( 4, pB->GetCallbackSizeBytes() );
}

TEST( FwdSteam, IsRegisteredAndTheIdFollowWhatSteamWrites )
{
	Memory memory;
	RecordingTarget target;
	CCallbackBase *pCallback = Build( memory.bytes, &target, 1 );

	CCallbackMgr::Register( pCallback, 101 );
	EXPECT_TRUE( IsRegistered( pCallback ) );
	EXPECT_EQ( 101, CallbackId( pCallback ) );
	EXPECT_EQ( 101, pCallback->GetICallback() );

	CCallbackMgr::Unregister( pCallback );
	EXPECT_FALSE( IsRegistered( pCallback ) );
}

TEST( FwdSteam, NoCallbackIsNotRegisteredAndHasNoId )
{
	EXPECT_FALSE( IsRegistered( nullptr ) );
	EXPECT_EQ( 0, CallbackId( nullptr ) );
}

TEST( FwdSteam, NeitherReleaseNorValidateIsCalledByTheCallback )
{
	// Managed releases the block itself, after Steam has let go of the
	// callback; nothing native validates it.
	Memory memory;
	RecordingTarget target;
	CCallbackBase *pCallback = Build( memory.bytes, &target, 1 );
	unsigned char payload[ 1 ] = {};

	pCallback->Run( payload );
	pCallback->Run( payload, false, SteamAPICall_t( 0 ) );

	EXPECT_EQ( 2, target.nRuns );
	EXPECT_EQ( 0, target.nReleases );
}

TEST( FwdSteam, AGameDllThatLinksTheLibraryServesItsTable )
{
	// SS_FWD_SERVES_STEAM is what linking ss_fwd_steam defines, as client.so
	// does: the serve helper then answers the Steam table as its own slot
	// array and count.
	int32_t nEntries = -1;
	void *pServed = SSFwd_Serve( "SSFwd_SteamCallback", &nEntries );
	ASSERT_NE( nullptr, pServed );
	EXPECT_EQ( (int32_t)kSteamCallbackSlots, nEntries );
	EXPECT_EQ( SteamCallbackTable().ppSlots, *static_cast< void *const *const * >( pServed ) );

	// The other families still come back from their own libraries.
	EXPECT_NE( nullptr, SSFwd_Serve( "SSFwd_ModelInfo", nullptr ) );
	EXPECT_NE( nullptr, SSFwd_Serve( "SSFwd_PhysicsObject", nullptr ) );
}
