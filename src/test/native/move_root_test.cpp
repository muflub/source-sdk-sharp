//========= SourceSharp — the top of a move-parent chain =====================//
//
// `GetRootMoveParent()` is an out-of-line member of the class the client flip
// removes, and three files that STAY called it -- the trace filter's sibling
// exclusion in util_shared.cpp, collisionproperty.cpp, and
// game/client/physics.cpp. The walk moved into a header because its body never
// needed the class: `GetMoveParent()` is inline on both sides.
//
// WHAT IS WORTH CHECKING IS NOT THE WALK, IT IS THE TWO EDGES.
//
//   * an entity with NO parent must answer ITSELF, not NULL. The whole use is
//     "do these two share a root", and a NULL root would make every unparented
//     pair look like siblings -- a trace filter that refuses everything.
//   * a CYCLE must terminate. Stock's loop is unbounded, which is safe in a
//     tree its own hierarchy code maintains and is not safe in a build with two
//     hierarchies. An unbounded walk hangs inside a physics callback with no
//     output at all; a bounded one gives a wrong answer, and a wrong answer can
//     be seen.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/move_root.h"

namespace
{

struct StubEntity
{
	StubEntity *pParent;
	explicit StubEntity( StubEntity *p = 0 ) : pParent( p ) {}
	StubEntity *GetMoveParent() const { return pParent; }
};

//-----------------------------------------------------------------------------
// No parent: the answer is the entity itself. This is stock's answer and the
// sibling test depends on it.
//-----------------------------------------------------------------------------
TEST( MoveRoot, AnUnparentedEntityIsItsOwnRoot )
{
	StubEntity lone;
	EXPECT_EQ( &lone, SourceSharpRootMoveParent( &lone ) );
}

//-----------------------------------------------------------------------------
// One parent.
//-----------------------------------------------------------------------------
TEST( MoveRoot, OneParentIsTheRoot )
{
	StubEntity root;
	StubEntity child( &root );
	EXPECT_EQ( &root, SourceSharpRootMoveParent( &child ) );
}

//-----------------------------------------------------------------------------
// A chain is walked to the TOP, not to the first parent. A weapon on a hand on
// a player is three deep and the filter wants the player.
//-----------------------------------------------------------------------------
TEST( MoveRoot, AChainIsWalkedToTheTop )
{
	StubEntity root;
	StubEntity mid( &root );
	StubEntity leaf( &mid );
	EXPECT_EQ( &root, SourceSharpRootMoveParent( &leaf ) );
}

//-----------------------------------------------------------------------------
// TWO ENTITIES IN ONE CHAIN SHARE A ROOT. This is the answer the sibling
// exclusion is actually built on, so it is a fact of its own and not a
// corollary somebody has to notice.
//-----------------------------------------------------------------------------
TEST( MoveRoot, SiblingsShareARoot )
{
	StubEntity root;
	StubEntity a( &root );
	StubEntity b( &root );
	EXPECT_EQ( SourceSharpRootMoveParent( &a ), SourceSharpRootMoveParent( &b ) );
}

//-----------------------------------------------------------------------------
// TWO SEPARATE TREES DO NOT. The other half of the same rule: a filter whose
// root test answered "same" for everything would refuse every trace.
//-----------------------------------------------------------------------------
TEST( MoveRoot, TwoTreesDoNotShareARoot )
{
	StubEntity rootA, rootB;
	StubEntity a( &rootA );
	StubEntity b( &rootB );
	EXPECT_NE( SourceSharpRootMoveParent( &a ), SourceSharpRootMoveParent( &b ) );
}

//-----------------------------------------------------------------------------
// A CYCLE TERMINATES. Stock's loop does not; this is the one deliberate
// difference from it, and the reason is that a hang inside a trace filter
// inside a physics callback produces no output at all.
//-----------------------------------------------------------------------------
TEST( MoveRoot, ACycleTerminates )
{
	StubEntity a, b;
	a.pParent = &b;
	b.pParent = &a;

	StubEntity *pRoot = SourceSharpRootMoveParent( &a );
	EXPECT_TRUE( pRoot == &a || pRoot == &b );
}

//-----------------------------------------------------------------------------
// A one-node cycle -- an entity parented to itself -- terminates too. It is the
// cheapest cycle to create by accident and the loop above steps through it
// differently from the two-node case.
//-----------------------------------------------------------------------------
TEST( MoveRoot, ASelfParentTerminates )
{
	StubEntity a;
	a.pParent = &a;
	EXPECT_EQ( &a, SourceSharpRootMoveParent( &a ) );
}

//-----------------------------------------------------------------------------
// NULL in, NULL out. The trace filter constructs from a handle that may not
// resolve.
//-----------------------------------------------------------------------------
TEST( MoveRoot, NullAnswersNull )
{
	StubEntity *pNothing = NULL;
	EXPECT_TRUE( SourceSharpRootMoveParent( pNothing ) == NULL );
}

//-----------------------------------------------------------------------------
// The bound is the caller's, and a chain longer than it stops early rather than
// walking on. Pinned so that the bound is a decision and not an accident: at
// depth 2 over a chain of 5, the walk must not have reached the top.
//-----------------------------------------------------------------------------
TEST( MoveRoot, TheWalkStopsAtTheGivenDepth )
{
	StubEntity e[ 6 ];
	for ( int i = 1; i < 6; i++ )
		e[ i ].pParent = &e[ i - 1 ];

	EXPECT_EQ( &e[ 0 ], SourceSharpRootMoveParent( &e[ 5 ], 64 ) );
	EXPECT_EQ( &e[ 3 ], SourceSharpRootMoveParent( &e[ 5 ], 2 ) );
}

} // namespace
