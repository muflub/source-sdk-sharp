//========= SourceSharp — libss_fwd_engine's forwarders, each to its method ==//
//
// plan_engine_forwarders.md. Every slot of a forwarder table must reach the
// method its name says, with the arguments it was handed, and return what the
// method returned. The forwarders are generated, and a slot that called the
// wrong overload, dropped an argument or swapped two of them would compile.
//
// THE FAKE ENGINE OBJECT IS A VTABLE ARRAY, and each recorder sits at the slot
// the COMPILER says the method occupies (VTableHook::MemberOf), never at a
// number written here. That keeps the test honest on both ABIs: under MSVC the
// overloads are reordered and the destructor takes one slot, under Itanium
// two, and in both cases the forwarder's `self->Method()` and MemberOf agree
// only if the forwarder called the method it claims to. A fake class that
// implemented IVModelInfo would need all of its pure virtuals; the array needs
// only the ones forwarded, and the rest are left null, so a forwarder that
// called anything else would fault rather than pass.
//
// The recorders are free functions taking `self` first. For these methods that
// is the member call's convention on both x64 ABIs: `this` is the first
// integer argument, and none of them returns a class by value.
//
// fwd_engine.cpp is compiled into this binary (the CMake row says why); the
// built library's export set is fwd_engine_isolation_test's.
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_fwd.h"
#include "vtable_hook.h"

#include "engine/ivmodelinfo.h"
#include "mathlib/vector.h"

#include <string.h>

namespace
{

// What the last recorder saw.
struct Seen
{
	const char *pszMethod = nullptr;
	const void *pSelf = nullptr;
	intptr_t args[ 3 ] = { 0, 0, 0 };
};

Seen g_seen;

// Distinct answers, so a forwarder returning another method's value is caught.
const model_t *const kModel = reinterpret_cast< const model_t * >( 0x1000 );
vcollide_t *const kCollide = reinterpret_cast< vcollide_t * >( 0x2000 );
const studiohdr_t *const kFound = reinterpret_cast< const studiohdr_t * >( 0x3000 );
byte *const kBlock = reinterpret_cast< byte * >( 0x4000 );
studiohdr_t *const kStudio = reinterpret_cast< studiohdr_t * >( 0x5000 );
CPhysCollide *const kTerrain = reinterpret_cast< CPhysCollide * >( 0x6000 );

void Note( const char *pszMethod, const void *pSelf, intptr_t a0 = 0, intptr_t a1 = 0, intptr_t a2 = 0 )
{
	g_seen.pszMethod = pszMethod;
	g_seen.pSelf = pSelf;
	g_seen.args[ 0 ] = a0;
	g_seen.args[ 1 ] = a1;
	g_seen.args[ 2 ] = a2;
}

const model_t *Rec_GetModel( void *self, int index ) { Note( "GetModel", self, index ); return kModel; }
int Rec_GetModelIndex( void *self, const char *name ) { Note( "GetModelIndex", self, (intptr_t)name ); return 41; }
const char *Rec_GetModelName( void *self, const model_t *model ) { Note( "GetModelName", self, (intptr_t)model ); return "models/x.mdl"; }
vcollide_t *Rec_GetVCollideByModel( void *self, const model_t *model ) { Note( "GetVCollide(model)", self, (intptr_t)model ); return nullptr; }
vcollide_t *Rec_GetVCollideByIndex( void *self, int index ) { Note( "GetVCollide(int)", self, index ); return kCollide; }
void Rec_GetModelBounds( void *self, const model_t *model, Vector &mins, Vector &maxs )
{
	Note( "GetModelBounds", self, (intptr_t)model, (intptr_t)&mins, (intptr_t)&maxs );
	mins.Init( -1, -2, -3 );
	maxs.Init( 1, 2, 3 );
}
int Rec_GetModelFrameCount( void *self, const model_t *model ) { Note( "GetModelFrameCount", self, (intptr_t)model ); return 7; }
int Rec_GetModelType( void *self, const model_t *model ) { Note( "GetModelType", self, (intptr_t)model ); return 3; }
const char *Rec_GetModelKeyValueText( void *self, const model_t *model ) { Note( "GetModelKeyValueText", self, (intptr_t)model ); return "kv"; }
const studiohdr_t *Rec_FindModelByName( void *self, const studiohdr_t *hdr, void **cache, const char *name )
{
	Note( "FindModel(hdr, cache, name)", self, (intptr_t)hdr, (intptr_t)cache, (intptr_t)name );
	return kFound;
}
const studiohdr_t *Rec_FindModelByCache( void *self, void *cache ) { Note( "FindModel(cache)", self, (intptr_t)cache ); return nullptr; }
byte *Rec_GetAnimBlock( void *self, const studiohdr_t *hdr, int block ) { Note( "GetAnimBlock", self, (intptr_t)hdr, block ); return kBlock; }
studiohdr_t *Rec_GetStudiomodel( void *self, const model_t *model ) { Note( "GetStudiomodel", self, (intptr_t)model ); return kStudio; }
CPhysCollide *Rec_GetCollideForVirtualTerrain( void *self, int index ) { Note( "GetCollideForVirtualTerrain", self, index ); return kTerrain; }

// The fake: word 0 points at a table as long as the compiler says IVModelInfo
// is, with a recorder at each forwarded method's slot (and at the two
// overloads' siblings, so calling the wrong overload is SEEN, not a fault).
struct FakeModelInfo
{
	void **vtable;
	void *slots[ 64 ];

	template < typename Fn >
	void Put( Fn pmf, void *pRecorder )
	{
		const int slot = VTableHook::MemberOf( pmf ).slot;
		ASSERT_GE( slot, 0 );
		ASSERT_LT( slot, 64 );
		slots[ slot ] = pRecorder;
	}

	FakeModelInfo()
	{
		memset( slots, 0, sizeof( slots ) );
		vtable = slots;
		Put( &IVModelInfo::GetModel, (void *)&Rec_GetModel );
		Put( &IVModelInfo::GetModelIndex, (void *)&Rec_GetModelIndex );
		Put( &IVModelInfo::GetModelName, (void *)&Rec_GetModelName );
		Put( static_cast< vcollide_t *( IVModelInfo::* )( const model_t * ) >( &IVModelInfo::GetVCollide ),
		     (void *)&Rec_GetVCollideByModel );
		Put( static_cast< vcollide_t *( IVModelInfo::* )( int ) >( &IVModelInfo::GetVCollide ),
		     (void *)&Rec_GetVCollideByIndex );
		Put( &IVModelInfo::GetModelBounds, (void *)&Rec_GetModelBounds );
		Put( &IVModelInfo::GetModelFrameCount, (void *)&Rec_GetModelFrameCount );
		Put( &IVModelInfo::GetModelType, (void *)&Rec_GetModelType );
		Put( &IVModelInfo::GetModelKeyValueText, (void *)&Rec_GetModelKeyValueText );
		Put( static_cast< const studiohdr_t *( IVModelInfo::* )( const studiohdr_t *, void **, const char * ) const >(
		         &IVModelInfo::FindModel ),
		     (void *)&Rec_FindModelByName );
		Put( static_cast< const studiohdr_t *( IVModelInfo::* )( void * ) const >( &IVModelInfo::FindModel ),
		     (void *)&Rec_FindModelByCache );
		Put( &IVModelInfo::GetAnimBlock, (void *)&Rec_GetAnimBlock );
		Put( &IVModelInfo::GetStudiomodel, (void *)&Rec_GetStudiomodel );
		Put( &IVModelInfo::GetCollideForVirtualTerrain, (void *)&Rec_GetCollideForVirtualTerrain );
	}
};

const SSForwarderTable &ModelInfoTable()
{
	const SSForwarderTable *pTable = SSFwdEngine_Table( "SSFwd_ModelInfo" );
	EXPECT_NE( nullptr, pTable );
	static const SSForwarderTable s_empty = { nullptr, 0 };
	return pTable ? *pTable : s_empty;
}

// Slot N of the table, as the generated order names it
// (EngineForwarders.g.cs's ModelInfoFwd lists the same order).
enum ModelInfoSlot
{
	kGetModel, kGetModelIndex, kGetModelName, kGetVCollide_2, kGetModelBounds,
	kGetModelFrameCount, kGetModelType, kGetModelKeyValueText, kFindModel,
	kGetAnimBlock, kGetStudiomodel, kGetCollideForVirtualTerrain, kModelInfoSlots
};

template < typename Fn >
Fn Slot( int slot )
{
	const SSForwarderTable &table = ModelInfoTable();
	return slot < table.nSlots ? reinterpret_cast< Fn >( table.ppSlots[ slot ] ) : nullptr;
}

} // namespace

TEST( FwdEngine, TheLibraryServesTheModelInfoTableAndNothingUnderOtherNames )
{
	const SSForwarderTable &table = ModelInfoTable();
	ASSERT_EQ( (int)kModelInfoSlots, table.nSlots );
	ASSERT_NE( nullptr, table.ppSlots );

	EXPECT_EQ( nullptr, SSFwdEngine_Table( "SSFwd_NoSuchTable" ) );
	EXPECT_EQ( nullptr, SSFwdEngine_Table( "ssfwd_modelinfo" ) );   // names are exact
	EXPECT_EQ( nullptr, SSFwdEngine_Table( nullptr ) );
}

TEST( FwdEngine, EverySlotIsADistinctFunction )
{
	const SSForwarderTable &table = ModelInfoTable();
	for ( int i = 0; i < table.nSlots; i++ )
	{
		EXPECT_NE( nullptr, table.ppSlots[ i ] ) << "slot " << i;
		for ( int j = 0; j < i; j++ )
			EXPECT_NE( table.ppSlots[ j ], table.ppSlots[ i ] ) << "slots " << j << " and " << i;
	}
}

TEST( FwdEngine, GetModelForwardsTheIndexAndReturnsTheModel )
{
	FakeModelInfo fake;
	auto fn = Slot< const model_t *( * )( void *, int ) >( kGetModel );
	ASSERT_NE( nullptr, fn );
	EXPECT_EQ( kModel, fn( &fake, 17 ) );
	EXPECT_STREQ( "GetModel", g_seen.pszMethod );
	EXPECT_EQ( &fake, g_seen.pSelf );
	EXPECT_EQ( 17, g_seen.args[ 0 ] );
}

TEST( FwdEngine, GetModelIndexForwardsTheName )
{
	FakeModelInfo fake;
	const char *pszName = "models/a.mdl";
	EXPECT_EQ( 41, Slot< int ( * )( void *, const char * ) >( kGetModelIndex )( &fake, pszName ) );
	EXPECT_STREQ( "GetModelIndex", g_seen.pszMethod );
	EXPECT_EQ( (intptr_t)pszName, g_seen.args[ 0 ] );
}

TEST( FwdEngine, GetModelNameForwardsTheModel )
{
	FakeModelInfo fake;
	EXPECT_STREQ( "models/x.mdl", Slot< const char *( * )( void *, const model_t * ) >( kGetModelName )( &fake, kModel ) );
	EXPECT_STREQ( "GetModelName", g_seen.pszMethod );
	EXPECT_EQ( (intptr_t)kModel, g_seen.args[ 0 ] );
}

TEST( FwdEngine, GetVCollide2IsTheIntOverload )
{
	// The overload MSVC reorders: the table's GetVCollide_2 must reach
	// GetVCollide( int ), never GetVCollide( const model_t * ).
	FakeModelInfo fake;
	EXPECT_EQ( kCollide, Slot< vcollide_t *( * )( void *, int ) >( kGetVCollide_2 )( &fake, 5 ) );
	EXPECT_STREQ( "GetVCollide(int)", g_seen.pszMethod );
	EXPECT_EQ( 5, g_seen.args[ 0 ] );
}

TEST( FwdEngine, GetModelBoundsForwardsBothOutVectorsByReference )
{
	FakeModelInfo fake;
	Vector mins, maxs;
	Slot< void ( * )( void *, const model_t *, Vector *, Vector * ) >( kGetModelBounds )( &fake, kModel, &mins, &maxs );
	EXPECT_STREQ( "GetModelBounds", g_seen.pszMethod );
	EXPECT_EQ( (intptr_t)kModel, g_seen.args[ 0 ] );
	EXPECT_EQ( (intptr_t)&mins, g_seen.args[ 1 ] );
	EXPECT_EQ( (intptr_t)&maxs, g_seen.args[ 2 ] );
	EXPECT_EQ( Vector( -1, -2, -3 ), mins );
	EXPECT_EQ( Vector( 1, 2, 3 ), maxs );
}

TEST( FwdEngine, GetModelFrameCountAndGetModelTypeAreNotSwapped )
{
	// Same signature, adjacent methods: a swap would compile and pass a count.
	FakeModelInfo fake;
	EXPECT_EQ( 7, Slot< int ( * )( void *, const model_t * ) >( kGetModelFrameCount )( &fake, kModel ) );
	EXPECT_STREQ( "GetModelFrameCount", g_seen.pszMethod );
	EXPECT_EQ( 3, Slot< int ( * )( void *, const model_t * ) >( kGetModelType )( &fake, kModel ) );
	EXPECT_STREQ( "GetModelType", g_seen.pszMethod );
}

TEST( FwdEngine, GetModelKeyValueTextForwardsTheModel )
{
	FakeModelInfo fake;
	EXPECT_STREQ( "kv", Slot< const char *( * )( void *, const model_t * ) >( kGetModelKeyValueText )( &fake, kModel ) );
	EXPECT_STREQ( "GetModelKeyValueText", g_seen.pszMethod );
}

TEST( FwdEngine, FindModelIsTheThreeArgumentOverloadWithItsArgumentsInOrder )
{
	FakeModelInfo fake;
	void *cache = nullptr;
	const char *pszName = "inc.mdl";
	const studiohdr_t *pHdr = reinterpret_cast< const studiohdr_t * >( 0x7000 );
	EXPECT_EQ( kFound, Slot< const studiohdr_t *( * )( void *, const studiohdr_t *, void **, const char * ) >( kFindModel )(
	                       &fake, pHdr, &cache, pszName ) );
	EXPECT_STREQ( "FindModel(hdr, cache, name)", g_seen.pszMethod );
	EXPECT_EQ( (intptr_t)pHdr, g_seen.args[ 0 ] );
	EXPECT_EQ( (intptr_t)&cache, g_seen.args[ 1 ] );
	EXPECT_EQ( (intptr_t)pszName, g_seen.args[ 2 ] );
}

TEST( FwdEngine, GetAnimBlockForwardsTheHeaderAndTheBlock )
{
	FakeModelInfo fake;
	const studiohdr_t *pHdr = reinterpret_cast< const studiohdr_t * >( 0x7000 );
	EXPECT_EQ( kBlock, Slot< byte *( * )( void *, const studiohdr_t *, int ) >( kGetAnimBlock )( &fake, pHdr, 9 ) );
	EXPECT_STREQ( "GetAnimBlock", g_seen.pszMethod );
	EXPECT_EQ( (intptr_t)pHdr, g_seen.args[ 0 ] );
	EXPECT_EQ( 9, g_seen.args[ 1 ] );
}

TEST( FwdEngine, GetStudiomodelForwardsTheModel )
{
	FakeModelInfo fake;
	EXPECT_EQ( kStudio, Slot< studiohdr_t *( * )( void *, const model_t * ) >( kGetStudiomodel )( &fake, kModel ) );
	EXPECT_STREQ( "GetStudiomodel", g_seen.pszMethod );
}

TEST( FwdEngine, GetCollideForVirtualTerrainForwardsTheIndex )
{
	FakeModelInfo fake;
	EXPECT_EQ( kTerrain, Slot< CPhysCollide *( * )( void *, int ) >( kGetCollideForVirtualTerrain )( &fake, 12 ) );
	EXPECT_STREQ( "GetCollideForVirtualTerrain", g_seen.pszMethod );
	EXPECT_EQ( 12, g_seen.args[ 0 ] );
}

TEST( FwdEngine, TheInternalFactoryServesATableAsItsSlotArrayWithItsCount )
{
	// SSFwdEngine_Serve is what both game DLLs' internal factory answers with:
	// the object's first word must be the slot array (managed reads the table
	// through it) and the count the table's own.
	int32_t nEntries = -1;
	void *pServed = SSFwdEngine_Serve( "SSFwd_ModelInfo", &nEntries );
	ASSERT_NE( nullptr, pServed );
	EXPECT_EQ( (int32_t)kModelInfoSlots, nEntries );
	EXPECT_EQ( ModelInfoTable().ppSlots, *static_cast< void *const *const * >( pServed ) );
}

TEST( FwdEngine, TheInternalFactoryLeavesTheCountAloneForANameItDoesNotServe )
{
	int32_t nEntries = -1;
	EXPECT_EQ( nullptr, SSFwdEngine_Serve( "VModelInfoServer004", &nEntries ) );
	EXPECT_EQ( -1, nEntries );
	// And a caller that wants no count may pass none.
	EXPECT_NE( nullptr, SSFwdEngine_Serve( "SSFwd_ModelInfo", nullptr ) );
}

