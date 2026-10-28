// plan_engineapi_vtables Phase D3, reshaped by plan_managed_vtable_reload.md
// L1: native accepts a managed-built object only when the object's own
// Validate agrees with the slot hashes this binary was compiled with.

#include "gtest/gtest.h"

#include <string.h>

#include <type_traits>

#include "imanaged_root.h"
#include "managed_accept.h"
#include "shell_managed_areas.h"
#include "vtable_hook.h"

namespace
{

// A stand-in for a managed-built root: an object whose first word is its
// table (plan_forwarders_everywhere.md phase 4), as the block managed builds
// is. Validate answers as the generated managed thunk does
// (ManagedVtable.ValidateSlots) from the hashes it was "built" with, and
// counts its calls so a test sees the stimulus arrive.
class FakeRoot : public IManagedRoot
{
public:
	explicit FakeRoot( const uint32_t *pHashes, int32_t nHashes )
		: IManagedRoot( &kTable )
		, m_pHashes( pHashes )
		, m_nHashes( nHashes )
	{
	}

	int m_nCalls = 0;

private:
	static FakeRoot *Of( void *self ) { return static_cast<FakeRoot *>( self ); }

	static void Released( IManagedObject * ) {}

	static void *Acquired( IManagedRoot *, const char * ) { return NULL; }

	static int32_t Validated( IManagedObject *self, const uint32_t *pSlotHashes, int32_t nSlots )
	{
		FakeRoot *root = Of( self );
		++root->m_nCalls;
		if ( !pSlotHashes || nSlots <= 0 )
			return 0;
		const int32_t nShared = nSlots < root->m_nHashes ? nSlots : root->m_nHashes;
		for ( int32_t i = 0; i < nShared; ++i )
		{
			if ( pSlotHashes[i] != root->m_pHashes[i] )
				return i;
		}
		return nSlots == root->m_nHashes ? -1 : nShared;
	}

	static const SSManagedRootTable kTable;

	const uint32_t *m_pHashes;
	int32_t m_nHashes;
};

const SSManagedRootTable FakeRoot::kTable = { &Released, &Validated, &Acquired };

} // namespace

TEST( ManagedAccept, TheTablePutsReleaseThenValidateFirst )
{
	// The control for everything below: the fixed slots the accept relies on,
	// in the table managed writes, whose order is the TSV's (no compiler
	// chooses it: plan_forwarders_everywhere.md phase 4).
	EXPECT_EQ( 0 * sizeof( void * ), offsetof( SSManagedRootTable, Release ) );
	EXPECT_EQ( 1 * sizeof( void * ), offsetof( SSManagedRootTable, Validate ) );
	EXPECT_EQ( 2 * sizeof( void * ), offsetof( SSManagedRootTable, Acquire ) );
	EXPECT_EQ( 3, kIManagedRootSlotCount );
}

TEST( ManagedAccept, MatchingHashesAreAccepted )
{
	FakeRoot root( kIManagedRootSlotHashes, kIManagedRootSlotCount );
	char why[128] = "unchanged";
	EXPECT_EQ(
		&root,
		ManagedAccept<IManagedRoot>( &root, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) ) );
	EXPECT_STREQ( "", why );
	EXPECT_EQ( 1, root.m_nCalls );
}

TEST( ManagedAccept, ARetypedSlotIsRefusedNamingIt )
{
	uint32_t built[3];
	memcpy( built, kIManagedRootSlotHashes, sizeof( built ) );
	built[2] ^= 1;
	FakeRoot root( built, 3 );

	char why[160] = {};
	EXPECT_EQ(
		nullptr,
		ManagedAccept<IManagedRoot>( &root, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) ) );
	EXPECT_NE( nullptr, strstr( why, "managed's slot 2 differs" ) ) << why;
	EXPECT_NE( nullptr, strstr( why, "3 slot(s) compiled" ) ) << why;
}

TEST( ManagedAccept, AManagedTableShortByOneIsRefused )
{
	FakeRoot root( kIManagedRootSlotHashes, kIManagedRootSlotCount - 1 );
	char why[160] = {};
	EXPECT_EQ(
		nullptr,
		ManagedAccept<IManagedRoot>( &root, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) ) );
	EXPECT_NE( nullptr, strstr( why, "managed's slot 2 differs" ) ) << why;
}

TEST( ManagedAccept, AManagedTableLongByOneIsRefused )
{
	const uint32_t built[] = { kIManagedRootSlotHashes[0], kIManagedRootSlotHashes[1], kIManagedRootSlotHashes[2],
							   0x1234 };
	FakeRoot root( built, 4 );
	char why[160] = {};
	EXPECT_EQ(
		nullptr,
		ManagedAccept<IManagedRoot>( &root, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) ) );
	EXPECT_NE( nullptr, strstr( why, "managed's slot 3 differs" ) ) << why;
}

TEST( ManagedAccept, ANullObjectIsRefusedWithoutACall )
{
	char why[128] = {};
	EXPECT_EQ(
		nullptr,
		ManagedAccept<IManagedRoot>( nullptr, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) ) );
	EXPECT_NE( nullptr, strstr( why, "no object" ) ) << why;
}

TEST( ManagedAccept, NoCompiledHashesIsRefusedWithoutAskingTheObject )
{
	FakeRoot root( kIManagedRootSlotHashes, kIManagedRootSlotCount );
	char why[128] = {};
	EXPECT_EQ( nullptr, ManagedAccept<IManagedRoot>( &root, nullptr, 0, why, sizeof( why ) ) );
	EXPECT_NE( nullptr, strstr( why, "no compiled slot hashes" ) ) << why;
	EXPECT_EQ( 0, root.m_nCalls );
}

//-----------------------------------------------------------------------------
// plan_managed_vtable_reload.md L1b: every managed-built interface derives from
// IManagedObject, so native accepts and releases any of them through the base.
//-----------------------------------------------------------------------------
namespace
{

// Records what the holder held at the moment Release ran.
class ReleaseProbe : public IManagedRoot
{
public:
	ReleaseProbe()
		: IManagedRoot( &kTable )
	{
	}

	IManagedRoot **m_ppHolder = NULL;
	int m_nReleases = 0;
	bool m_bHolderWasNullAtRelease = false;

private:
	static void Released( IManagedObject *self )
	{
		ReleaseProbe *probe = static_cast<ReleaseProbe *>( self );
		++probe->m_nReleases;
		probe->m_bHolderWasNullAtRelease = probe->m_ppHolder && *probe->m_ppHolder == NULL;
	}

	static int32_t Validated( IManagedObject *, const uint32_t *, int32_t ) { return -1; }

	static void *Acquired( IManagedRoot *, const char * ) { return NULL; }

	static const SSManagedRootTable kTable;
};

const SSManagedRootTable ReleaseProbe::kTable = { &Released, &Validated, &Acquired };

} // namespace

TEST( ManagedAccept, EveryGeneratedInterfaceSharesTheBasesTwoSlots )
{
	static_assert( std::is_base_of<IManagedObject, IManagedRoot>::value, "the root derives from the base" );
	EXPECT_EQ( 0 * sizeof( void * ), offsetof( SSManagedObjectTable, Release ) );
	EXPECT_EQ( 1 * sizeof( void * ), offsetof( SSManagedObjectTable, Validate ) );
	// No virtual anywhere: the object is the table pointer and nothing else,
	// the first word of the block managed builds.
	static_assert( !std::is_polymorphic<IManagedRoot>::value, "a managed-built interface has no vtable" );
	EXPECT_EQ( sizeof( void * ), sizeof( IManagedRoot ) );
}

TEST( ManagedAccept, TheBaseAcceptAnswersTheSameObject )
{
	FakeRoot root( kIManagedRootSlotHashes, kIManagedRootSlotCount );
	char why[128] = "unchanged";
	IManagedObject *pObject =
		ManagedAcceptObject( &root, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) );
	EXPECT_EQ( static_cast<IManagedObject *>( &root ), pObject );
	EXPECT_EQ( (void *)&root, (void *)pObject ) << "the base is not at offset 0";
	EXPECT_EQ( 1, root.m_nCalls );
}

TEST( ManagedAccept, ReleaseNullsTheHolderBeforeReleasing )
{
	ReleaseProbe probe;
	IManagedRoot *pHolder = &probe;
	probe.m_ppHolder = &pHolder;

	ManagedRelease( pHolder );

	EXPECT_EQ( nullptr, pHolder );
	EXPECT_EQ( 1, probe.m_nReleases );
	EXPECT_TRUE( probe.m_bHolderWasNullAtRelease ) << "Release ran while the holder still named the object";
}

TEST( ManagedAccept, ReleasingAnEmptyHolderCallsNothing )
{
	IManagedRoot *pHolder = NULL;
	ManagedRelease( pHolder );
	EXPECT_EQ( nullptr, pHolder );
}

//-----------------------------------------------------------------------------
// THE SHUTDOWN ORDER. Native releases the managed root and its areas BEFORE the
// Shutdown slot, because that slot unloads the module: released after it, the
// module's own safety net had already freed the handles ("N managed-built
// object(s) were still bound at shutdown"), native's release then found them
// released twice, and the handles kept the server's load context alive through
// its unload. Source-read: the function needs the engine to run.
//-----------------------------------------------------------------------------
#include <fstream>
#include <sstream>
#include <string>

//-----------------------------------------------------------------------------
// plan_managed_vtable_reload.md L2: everything native holds from managed is one
// CManagedHolders on the game DLL object, and the object's destructor asserts
// it was emptied.
//-----------------------------------------------------------------------------
namespace
{

} // namespace

TEST( ManagedHolders, AZeroedSetIsEmpty )
{
	CManagedHolders holders = {};
	EXPECT_TRUE( ManagedHolders_Empty( holders ) );
}

TEST( ManagedHolders, EveryHolderAloneMakesTheSetNonEmpty )
{
	// Every field is a pointer, so the set is a run of words; setting each in
	// turn proves the check names every one, including areas added later.
	static_assert( sizeof( CManagedHolders ) % sizeof( void * ) == 0, "holders are pointers only" );
	const size_t nWords = sizeof( CManagedHolders ) / sizeof( void * );
	ASSERT_GE( nWords, 3u ) << "the root, the runtime and at least one area";

	int nChecked = 0;
	for ( size_t i = 0; i < nWords; ++i )
	{
		CManagedHolders holders = {};
		void *pWords[sizeof( CManagedHolders ) / sizeof( void * )];
		memcpy( pWords, &holders, sizeof( holders ) );
		pWords[i] = &holders;
		memcpy( &holders, pWords, sizeof( holders ) );

		EXPECT_FALSE( ManagedHolders_Empty( holders ) ) << "word " << i << " is not checked";
		++nChecked;
	}
	EXPECT_EQ( (int)nWords, nChecked );
}

//-----------------------------------------------------------------------------
// plan_forwarders_everywhere.md phase 4: a managed-built interface has no
// virtual. Each method is an inline forwarder that calls its own slot of the
// table managed wrote, the object first and the arguments in order.
//-----------------------------------------------------------------------------
namespace
{

struct SoundEntCalls
{
	int nSlot = -1;
	void *pSelf = nullptr;
	int32_t nA = 0;
	float flB = 0.0f;
};

SoundEntCalls g_SoundEnt;

void SoundEntInit( IManagedSoundEnt *self, int32_t nMaxClients, float flCurTime )
{
	g_SoundEnt.nSlot = 2;
	g_SoundEnt.pSelf = self;
	g_SoundEnt.nA = nMaxClients;
	g_SoundEnt.flB = flCurTime;
}

void SoundEntShutdown( IManagedSoundEnt *self )
{
	g_SoundEnt.nSlot = 3;
	g_SoundEnt.pSelf = self;
}

void SoundEntReset( IManagedSoundEnt *self, int32_t nIndex )
{
	g_SoundEnt.nSlot = 4;
	g_SoundEnt.pSelf = self;
	g_SoundEnt.nA = nIndex;
}

void SoundEntReleased( IManagedObject *self )
{
	g_SoundEnt.nSlot = 0;
	g_SoundEnt.pSelf = self;
}

int32_t SoundEntValidated( IManagedObject *self, const uint32_t *, int32_t nSlots )
{
	g_SoundEnt.nSlot = 1;
	g_SoundEnt.pSelf = self;
	return nSlots;
}

const SSManagedSoundEntTable kSoundEntTable = {
	&SoundEntReleased, &SoundEntValidated, &SoundEntInit, &SoundEntShutdown, &SoundEntReset,
};

} // namespace

TEST( ManagedAccept, EachMethodCallsItsOwnSlotWithTheObjectAndItsArguments )
{
	IManagedSoundEnt area( &kSoundEntTable );

	area.ServerSoundEntInit( 24, 1.5f );
	EXPECT_EQ( 2, g_SoundEnt.nSlot );
	EXPECT_EQ( &area, g_SoundEnt.pSelf );
	EXPECT_EQ( 24, g_SoundEnt.nA );
	EXPECT_FLOAT_EQ( 1.5f, g_SoundEnt.flB );

	area.ServerSoundEntShutdown();
	EXPECT_EQ( 3, g_SoundEnt.nSlot );

	area.ServerSoundEntResetClientSound( 9 );
	EXPECT_EQ( 4, g_SoundEnt.nSlot );
	EXPECT_EQ( 9, g_SoundEnt.nA );

	// And the base's two, through the base, on the same table.
	IManagedObject *pObject = &area;
	EXPECT_EQ( kIManagedSoundEntSlotCount,
			   pObject->Validate( kIManagedSoundEntSlotHashes, kIManagedSoundEntSlotCount ) );
	EXPECT_EQ( 1, g_SoundEnt.nSlot );
	pObject->Release();
	EXPECT_EQ( 0, g_SoundEnt.nSlot );
	EXPECT_EQ( static_cast<void *>( &area ), g_SoundEnt.pSelf );
}
