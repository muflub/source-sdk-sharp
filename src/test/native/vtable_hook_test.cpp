//========= SourceSharp — vtable slot tests ===================================//
//
// MemberOf is tested against a class declared here rather than against an
// engine interface, because the property that matters can be checked without
// one: that a slot worked out from a pointer-to-member is the slot the compiler
// actually used.
//
// The class below is shaped like IFileSystem on purpose -- two bases, the
// interesting methods on the second one, and its own virtuals after the
// interface's. That shape is where the traps are: a method declared by a base
// is numbered against the BASE's vtable, and reports adjust != 0 to say so.
//
// WHAT THIS FILE NO LONGER CLAIMS. Two whole halves, retired with the code they
// pinned. "Measuring a vtable" -- four facts over VTableHook::Length and
// IsExecutable -- and "Installing" -- nine facts over VTableHook::CTable's
// clone/patch/install/restore sequence. Both existed for one caller,
// shell_filesystem.cpp's file-open recorder, which had no reader and is gone.
// vtable_hook.cpp went with it, so neither Length, IsExecutable, OffsetOf nor
// CTable exists to be a claim about.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/shared/vtable_hook.h"

namespace
{

//-----------------------------------------------------------------------------
// A stand-in for the engine's filesystem, with the same inheritance shape.
//-----------------------------------------------------------------------------
class IThing
{
public:
	virtual ~IThing() {}
	virtual int  Connect() = 0;
	virtual int  Shutdown() = 0;
};

class IReader
{
public:
	virtual int  Open( const char *name ) = 0;
	virtual bool Exists( const char *name ) = 0;
	virtual int  Read( int handle ) = 0;
};

class IFace : public IThing, public IReader
{
public:
	virtual int  Search( const char *path ) = 0;
	virtual int  Count() = 0;
};

// The concrete class carries virtuals of its own past the end of the interface,
// exactly as CFileSystem_Stdio does, and calls one of them from inside another.
// A clone that stopped at the interface would fail here and only here.
class CThing : public IFace
{
public:
	CThing() : m_nOpened( 0 ) {}

	virtual int  Connect() { return 1; }
	virtual int  Shutdown() { return 2; }

	virtual int  Open( const char *name ) { m_nOpened++; return Decorate( (int)strlen( name ) ); }
	virtual bool Exists( const char *name ) { return name != NULL && name[0] == 'y'; }
	virtual int  Read( int handle ) { return handle * 2; }

	virtual int  Search( const char *path ) { return path != NULL ? 7 : 0; }
	virtual int  Count() { return m_nOpened; }

	// Past the interface, and reached through the vtable from Open above.
	virtual int  Decorate( int value ) { return value + 100; }
	virtual int  Unused1() { return 0; }
	virtual int  Unused2() { return 0; }

	int m_nOpened;
};

//-----------------------------------------------------------------------------
// Hooks. Plain functions taking the object first, which is what the ABI makes of
// a member function.
//-----------------------------------------------------------------------------
typedef int  ( *OpenFn )( void *self, const char *name );
typedef bool ( *ExistsFn )( void *self, const char *name );
typedef int  ( *SearchFn )( void *self, const char *path );

OpenFn   g_pfnOpen = NULL;
ExistsFn g_pfnExists = NULL;
SearchFn g_pfnSearch = NULL;

int  g_nOpenSeen = 0;
int  g_nExistsSeen = 0;
int  g_nSearchSeen = 0;
const char *g_pLastName = NULL;

class VTableHookTest : public ::testing::Test
{
protected:
	void SetUp()
	{
		g_nOpenSeen = g_nExistsSeen = g_nSearchSeen = 0;
		g_pLastName = NULL;
		g_pfnOpen = NULL;
		g_pfnExists = NULL;
		g_pfnSearch = NULL;
	}
};

//-----------------------------------------------------------------------------
// Working out where a method lives.
//-----------------------------------------------------------------------------

// The whole design rests on this: the slot is taken from the compiler, so a
// header that is wrong is wrong in the same way for the hook and for the caller.
TEST_F( VTableHookTest, FindsTheSlotOfAVirtualMethod )
{
	VTableHook::Member connect = VTableHook::MemberOf( &IFace::Connect );

	EXPECT_TRUE( connect.isVirtual );
	EXPECT_GE( connect.slot, 0 );

	// Different methods land in different slots, and the second of two declared
	// together is one further along.
	VTableHook::Member shutdown = VTableHook::MemberOf( &IFace::Shutdown );
	EXPECT_EQ( connect.slot + 1, shutdown.slot );
}

// The trap, pinned down.
//
// `&IFace::Open` looks like it names a member of IFace. It does not -- Open is
// inherited, so it names IReader's, numbered against IReader's vtable, and it
// reports an adjustment of zero because relative to IReader there is none.
//
// Believing both at once is the whole failure mode: a small slot number and
// "the primary table" combine into a write that lands on an unrelated method.
// This test exists so that if a compiler ever starts answering differently,
// something says so here rather than in a game.
TEST_F( VTableHookTest, AnInheritedMethodIsNumberedAgainstTheClassThatDeclaredIt )
{
	VTableHook::Member throughDerived = VTableHook::MemberOf( &IFace::Open );
	VTableHook::Member throughBase = VTableHook::MemberOf( &IReader::Open );

	// The same member either way, and no adjustment on either.
	EXPECT_EQ( throughBase.slot, throughDerived.slot );
	EXPECT_EQ( 0, (int)throughDerived.adjust );
	EXPECT_TRUE( throughDerived.isVirtual );

	// And that slot is small -- it indexes IReader's table, not IFace's, which
	// is why a caller that wants a count must ask about the class that DECLARES
	// the last method. The row that bounded it against VTableHook::Length( the
	// primary table ) went with Length itself.
	EXPECT_LT( throughDerived.slot, 16 );
}


TEST_F( VTableHookTest, MethodsDeclaredTogetherAreConsecutive )
{
	VTableHook::Member open = VTableHook::MemberOf( &IReader::Open );
	VTableHook::Member exists = VTableHook::MemberOf( &IReader::Exists );
	VTableHook::Member read = VTableHook::MemberOf( &IReader::Read );

	EXPECT_EQ( open.slot + 1, exists.slot );
	EXPECT_EQ( open.slot + 2, read.slot );
	EXPECT_EQ( open.adjust, exists.adjust );
	EXPECT_EQ( open.adjust, read.adjust );
}

TEST_F( VTableHookTest, ANonVirtualMemberIsRefused )
{
	struct Plain { void Method() {} };

	VTableHook::Member member = VTableHook::MemberOf( &Plain::Method );
	EXPECT_FALSE( member.isVirtual );
	EXPECT_EQ( -1, member.slot );
}

} // namespace
