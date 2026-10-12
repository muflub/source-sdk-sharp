//========= SourceSharp — tests for the bullet-through-glass rule ============//
//
// This one exists because of a crash, not because of a subtlety.
//
// FireBullets guarded its glass check on `tr.m_pEnt` — an IHandleEntity, which
// the engine fills for anything it can trace against — and then dereferenced
// `TraceEntity( tr )`, which is NULL for everything that is not a CBaseEntity.
// Two different questions, and the second answers NULL exactly when the first
// says yes. Shooting the skylight on dm_lockdown segfaulted the server, from
// normal play, remotely, every time.
//
// A test cannot fire a bullet. What it CAN pin down is the shape that made the
// crash impossible to write again: the rule takes a classname rather than an
// entity, so a caller with nothing to name what it hit has to pass NULL, and
// NULL has an answer here rather than a dereference at the call site.
//
// See public/bullet_glass.h.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../../public/bullet_glass.h"

namespace
{

// SF_BREAK_NO_BULLET_PENETRATION, from game/server/func_break.h. Restated
// rather than included because including it would drag the SDK into a test of
// nine lines of arithmetic; the call site passes the real one.
const int kNoPenetration = 0x0800;

//-----------------------------------------------------------------------------
// THE REGRESSION. Nothing could name what was hit — a static prop, or an entity
// on the parallel hierarchy in a build where nothing can answer for it — and the
// answer has to be "no", not a crash and not a guess.
//-----------------------------------------------------------------------------
TEST( BulletPassesThroughGlass, ClassnameNobodyCouldAnswerIsNotPenetrable )
{
	EXPECT_FALSE( BulletPassesThroughGlass( true, NULL, 0, kNoPenetration ) );
	EXPECT_FALSE( BulletPassesThroughGlass( true, NULL, kNoPenetration, kNoPenetration ) );
	EXPECT_FALSE( BulletPassesThroughGlass( false, NULL, 0, kNoPenetration ) );
	EXPECT_FALSE( BulletPassesThroughGlass( true, "", 0, kNoPenetration ) );
}

//-----------------------------------------------------------------------------
// What stock does, which is the whole of what this must keep doing.
//-----------------------------------------------------------------------------
TEST( BulletPassesThroughGlass, GlassBreakableLetsTheRoundThrough )
{
	EXPECT_TRUE( BulletPassesThroughGlass( true, "func_breakable", 0, kNoPenetration ) );
}

TEST( BulletPassesThroughGlass, MapperCanMakeTheGlassBulletproof )
{
	EXPECT_FALSE( BulletPassesThroughGlass( true, "func_breakable",
	                                        kNoPenetration, kNoPenetration ) );

	// Other flags set, but not that one: still penetrable. The bit is what is
	// asked about, not the value.
	EXPECT_TRUE( BulletPassesThroughGlass( true, "func_breakable",
	                                       ~kNoPenetration, kNoPenetration ) );
}

TEST( BulletPassesThroughGlass, SurfaceThatIsNotGlassStopsTheRound )
{
	EXPECT_FALSE( BulletPassesThroughGlass( false, "func_breakable", 0, kNoPenetration ) );
}

//-----------------------------------------------------------------------------
// EXACT, because stock's literal is exact.
//
// `ClassMatches( "func_breakable" )` carries no wildcard, so `func_breakable_surf`
// has never been penetrable in Source and must not become so here — a window
// that a round passes through instead of stopping in is a different game. The
// two classnames share a prefix, which is exactly how a comparison written in a
// hurry gets this wrong.
//-----------------------------------------------------------------------------
TEST( BulletPassesThroughGlass, BreakableSurfIsNotABreakable )
{
	EXPECT_FALSE( BulletPassesThroughGlass( true, "func_breakable_surf", 0, kNoPenetration ) );
}

TEST( BulletPassesThroughGlass, PrefixIsNotAMatchInEitherDirection )
{
	EXPECT_FALSE( BulletPassesThroughGlass( true, "func_break", 0, kNoPenetration ) );
	EXPECT_FALSE( BulletPassesThroughGlass( true, "func_breakables", 0, kNoPenetration ) );
	EXPECT_FALSE( BulletPassesThroughGlass( true, "prop_physics", 0, kNoPenetration ) );
}

//-----------------------------------------------------------------------------
// Case, because stock's comparison is case-insensitive: ClassMatches goes to
// NamesMatch, and a map that spells a classname with a capital in it has always
// still matched. A hand-edited map is the usual source of one.
//-----------------------------------------------------------------------------
TEST( BulletPassesThroughGlass, MatchesRegardlessOfCase )
{
	EXPECT_TRUE( BulletPassesThroughGlass( true, "Func_Breakable", 0, kNoPenetration ) );
	EXPECT_TRUE( BulletPassesThroughGlass( true, "FUNC_BREAKABLE", 0, kNoPenetration ) );
}

//-----------------------------------------------------------------------------
// The compare on its own, including both NULLs — the argument order the call
// site cannot produce, and the one a later caller will.
//-----------------------------------------------------------------------------
TEST( BulletGlassNameEquals, NullIsNeverEqualToAnything )
{
	EXPECT_FALSE( BulletGlassNameEquals( NULL, "func_breakable" ) );
	EXPECT_FALSE( BulletGlassNameEquals( "func_breakable", NULL ) );
	EXPECT_FALSE( BulletGlassNameEquals( NULL, NULL ) );
}

TEST( BulletGlassNameEquals, EmptyMatchesOnlyEmpty )
{
	EXPECT_TRUE( BulletGlassNameEquals( "", "" ) );
	EXPECT_FALSE( BulletGlassNameEquals( "", "func_breakable" ) );
	EXPECT_FALSE( BulletGlassNameEquals( "func_breakable", "" ) );
}

} // namespace
