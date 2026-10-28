//========= SourceSharp — vtable_slot_patch.h tests ===========================//
//
// The render call recorder (shell_render_trace_client.cpp) overwrites one slot
// of an engine object's vtable for a frame and puts it back. These facts hold
// that machinery against a class declared here, whose vtable the compiler puts
// in this binary's read-only data exactly as the engine's are in engine.so.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/shared/vtable_slot_patch.h"

#if defined( _MSC_VER )
#define SS_TEST_NOINLINE __declspec( noinline )
#else
#define SS_TEST_NOINLINE __attribute__( ( noinline ) )
#endif

namespace
{

class IGreeter
{
public:
	virtual ~IGreeter() {}

	virtual int First( int n ) = 0;
	virtual int Second( int n ) = 0;
};

class CGreeter : public IGreeter
{
public:
	int First( int n ) override { return n + 1; }

	int Second( int n ) override { return n + 2; }
};

class IOther
{
public:
	virtual ~IOther() {}

	virtual int Third() = 0;
};

class CBoth : public IGreeter, public IOther
{
public:
	int First( int n ) override { return n + 10; }

	int Second( int n ) override { return n + 20; }

	int Third() override { return 30; }
};

void *g_pOriginalFirst = nullptr;
int g_nHookCalls = 0;

int HookFirst( IGreeter *self, int n )
{
	++g_nHookCalls;
	return ( (int ( * )( IGreeter *, int ))g_pOriginalFirst )( self, n ) * 100;
}

// Called through the base pointer, so the compiler cannot devirtualise it.
SS_TEST_NOINLINE int CallFirst( IGreeter *p, int n )
{
	return p->First( n );
}

SS_TEST_NOINLINE int CallSecond( IGreeter *p, int n )
{
	return p->Second( n );
}

} // namespace

TEST( VTableSlotPatch, TheVtableStartsReadOnly )
{
	CGreeter greeter;
	void **pVtable = *(void ***)&greeter;
	int nProtection = VTableSlotPatch::PageProtection( pVtable );
	ASSERT_GE( nProtection, 0 ) << "no mapping holds the vtable";
	EXPECT_EQ( 0, nProtection & SS_PAGE_WRITE )
		<< "the test needs a read-only table to prove the store lifts and restores it";
}

TEST( VTableSlotPatch, AnInstalledHookRunsAndForwardsToTheOriginal )
{
	CGreeter greeter;
	ASSERT_EQ( 4, CallFirst( &greeter, 3 ) );

	VTableSlotPatch::Patch patch;
	ASSERT_TRUE( VTableSlotPatch::Install( &greeter, &IGreeter::First, (void *)&HookFirst, &patch ) );
	g_pOriginalFirst = patch.pOriginal;
	g_nHookCalls = 0;

	EXPECT_EQ( 400, CallFirst( &greeter, 3 ) );
	EXPECT_EQ( 1, g_nHookCalls );
	EXPECT_EQ( 5, CallSecond( &greeter, 3 ) ) << "the slot beside it is untouched";

	ASSERT_TRUE( VTableSlotPatch::Remove( patch ) );
	EXPECT_EQ( 4, CallFirst( &greeter, 3 ) );
	EXPECT_EQ( 1, g_nHookCalls ) << "the hook is gone once removed";
}

TEST( VTableSlotPatch, EveryObjectOfTheClassSeesThePatch )
{
	CGreeter a, b;
	VTableSlotPatch::Patch patch;
	ASSERT_TRUE( VTableSlotPatch::Install( &a, &IGreeter::First, (void *)&HookFirst, &patch ) );
	g_pOriginalFirst = patch.pOriginal;
	EXPECT_EQ( 400, CallFirst( &b, 3 ) ) << "the table is the class's, so a second object is hooked too";
	ASSERT_TRUE( VTableSlotPatch::Remove( patch ) );
	EXPECT_EQ( 4, CallFirst( &b, 3 ) );
}

TEST( VTableSlotPatch, TheProtectionIsPutBack )
{
	CGreeter greeter;
	void **pVtable = *(void ***)&greeter;
	const int nBefore = VTableSlotPatch::PageProtection( pVtable );
	ASSERT_GE( nBefore, 0 );

	VTableSlotPatch::Patch patch;
	ASSERT_TRUE( VTableSlotPatch::Install( &greeter, &IGreeter::First, (void *)&HookFirst, &patch ) );
	EXPECT_EQ( nBefore, patch.nProtection );
	EXPECT_EQ( nBefore, VTableSlotPatch::PageProtection( pVtable ) ) << "installed: the page is read-only again";
	ASSERT_TRUE( VTableSlotPatch::Remove( patch ) );
	EXPECT_EQ( nBefore, VTableSlotPatch::PageProtection( pVtable ) ) << "removed: the page is read-only again";
}

TEST( VTableSlotPatch, ASecondaryBasesTableIsPatchedThroughItsOwnPointer )
{
	CBoth both;
	IGreeter *pGreeter = &both;
	ASSERT_EQ( 13, CallFirst( pGreeter, 3 ) );
	VTableSlotPatch::Patch patch;
	ASSERT_TRUE( VTableSlotPatch::Install( pGreeter, &IGreeter::First, (void *)&HookFirst, &patch ) );
	g_pOriginalFirst = patch.pOriginal;
	EXPECT_EQ( 1300, CallFirst( pGreeter, 3 ) );
	EXPECT_EQ( 30, static_cast<IOther *>( &both )->Third() );
	ASSERT_TRUE( VTableSlotPatch::Remove( patch ) );
	EXPECT_EQ( 13, CallFirst( pGreeter, 3 ) );
}

TEST( VTableSlotPatch, AnAdjustedMemberIsRefused )
{
	CBoth both;
	VTableSlotPatch::Patch patch = { nullptr, nullptr, -1 };
	// A pointer to IOther's member converted to CBoth's carries a `this`
	// adjustment: its slot is numbered against IOther's table, not CBoth's.
	int ( CBoth::*pmf )() = &IOther::Third;
	EXPECT_FALSE( VTableSlotPatch::Install( &both, pmf, (void *)&HookFirst, &patch ) );
	EXPECT_EQ( nullptr, patch.pSlot ) << "nothing was written";
	EXPECT_EQ( 30, both.Third() );
}

TEST( VTableSlotPatch, ANullObjectIsRefused )
{
	VTableSlotPatch::Patch patch = { nullptr, nullptr, -1 };
	EXPECT_FALSE( VTableSlotPatch::Install( nullptr, &IGreeter::First, (void *)&HookFirst, &patch ) );
}
