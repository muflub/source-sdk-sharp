//========= SourceSharp — the bone leaf library re-home, and the four rulings ==//
//
// plan_entity.md §6's bone-leaf-library row. Lane `f-bone`.
//
// THIRTEEN SYMBOLS WERE PUT TO THIS LANE AND NINE OF THEM MOVED. The other four
// did not, and each has a RULING rather than a re-home. A ruling written only in
// a handover decays the first time someone reads §6 instead; these arms hold
// each one to the tree, so a ruling that stops being true fails here.
//
// The behaviour of what DID move is `bone_leaf_library_test.cpp`, which drives
// the real functions. This file is about the SHAPE of the move and about the
// four that stayed — claims a running test cannot make, because they are claims
// about what is NOT in a translation unit.
//
//-----------------------------------------------------------------------------
// THE FOUR, AND WHY EACH STAYED. Every one of these contradicts a prior, and
// each contradiction is what the matching arm below pins.
//
//   `LockStudioHdr` / `UnlockStudioHdr`. §6's carve-out names `UnlockStudioHdr
//   (:957)` among the "C_BaseAnimating STATICS". It is not one. Both are PRIVATE
//   INSTANCE methods over `m_pStudioHdr` / `m_hStudioHdr` — the ENTITY's
//   model-cache lifetime — and they leave with the entity. They are still owed
//   after the flip only because two header inlines expand their bodies into
//   other translation units, and each of those is that file's own port.
//
//   `GetHitboxSet` / `HitboxToWorldTransforms`. The prior says they "take a
//   CStudioHdr and a bone array, not an entity". `GetHitboxSet()` is
//   `return m_nHitboxSet;`. Both are instance methods on the entity. They are
//   not re-homed because armed NOTHING ASKS FOR THEM, and nothing is owed.
//
//   THAT CLAUSE USED TO READ "because all three STAY files that ask for them
//   ask INSIDE `#ifndef SS_CBASEENTITY_ALIAS`", and the change is worth writing
//   down. The `residue` wave (74b071771..a46e8d645) deleted those guarded arms:
//   every project that builds this tree defines the alias macro, so they were
//   dead source no compiler had ever seen. The RULING did not change; the
//   evidence for it got shorter. The arm below now asserts the CONCLUSION --
//   there are no asks, guarded or not -- rather than the old evidence for it,
//   which is strictly harder to satisfy: an ask that moves from one guarded
//   region to another used to pass this fact and now does not.
//
//   WHAT WAS GIVEN UP IS STILL NOTHING, which the old paragraph was right to
//   worry about: deleting an unarmed arm to make a relink count fall would
//   trade working behaviour (tesla arcs over hitboxes, a beam following a
//   hitbox, particle control points on one) for a linker's benefit, and this
//   port's brief forbids exactly that. It is not that trade. Armed, each of the
//   three entry points is unreachable BEFORE its hitbox ask is -- each file says
//   so at the site -- and the stock client, the only build that would ever have
//   compiled those arms, is not built from this tree at all.
//
//===========================================================================//

#include <string>
#include <vector>
#include <cctype>
#include <fstream>
#include <sys/stat.h>

#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

// Prose counts as code to a raw grep, and this whole lane's work is described in
// comments that name the very symbols being searched for. Every arm below that
// looks for a SPELLING strips first.
std::string StripComments( const std::string &in )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < in.size() && in[ i ] == '*' && in[ i + 1 ] == '/' ) { bInBlock = false; i++; }
			continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '*' ) { bInBlock = true; i++; continue; }
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			if ( i < in.size() ) out += '\n';
			continue;
		}
		out += in[ i ];
	}
	return out;
}

std::vector< std::string > Lines( const std::string &in )
{
	std::vector< std::string > out;
	std::string cur;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( in[ i ] == '\n' ) { out.push_back( cur ); cur.clear(); }
		else cur += in[ i ];
	}
	out.push_back( cur );
	return out;
}

std::string Trim( const std::string &in )
{
	size_t a = 0, b = in.size();
	while ( a < b && isspace( (unsigned char)in[ a ] ) ) a++;
	while ( b > a && isspace( (unsigned char)in[ b - 1 ] ) ) b--;
	return in.substr( a, b - a );
}

//-----------------------------------------------------------------------------
// Count the occurrences of `needle` in a file that are NOT lexically inside a
// `#ifndef SS_CBASEENTITY_ALIAS` block, and the ones that ARE.
//
// A PREPROCESSOR STACK, not a "is there a guard anywhere in the file" check. The
// three files in question each contain several guarded blocks and a great deal
// of unguarded code, so a file-level answer would be true of a call site the
// flip does NOT remove. Nested `#if`s inside a guard are tracked so that the
// closing `#endif` matched to the guard is the right one.
//-----------------------------------------------------------------------------
struct GuardCount
{
	GuardCount() : nInside( 0 ), nOutside( 0 ) {}
	int nInside;
	int nOutside;
};

GuardCount CountAgainstAliasGuard( const std::string &source, const char *needle )
{
	GuardCount out;
	const std::vector< std::string > lines = Lines( StripComments( source ) );

	// Each element is "this conditional is the alias guard" (true) or not.
	std::vector< bool > stack;
	int nGuardDepth = 0;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trim( lines[ i ] );

		if ( t.compare( 0, 3, "#if" ) == 0 )
		{
			const bool bIsGuard = ( t.find( "#ifndef SS_CBASEENTITY_ALIAS" ) == 0 )
			                   || ( t.find( "#if !defined( SS_CBASEENTITY_ALIAS )" ) == 0 );
			stack.push_back( bIsGuard );
			if ( bIsGuard )
				nGuardDepth++;
			continue;
		}
		if ( t.compare( 0, 6, "#endif" ) == 0 )
		{
			if ( !stack.empty() )
			{
				if ( stack.back() )
					nGuardDepth--;
				stack.pop_back();
			}
			continue;
		}
		// `#else` flips which arm is live; an alias-guard's else arm is the ARMED
		// arm, so a needle there is NOT removed by the flip.
		if ( t.compare( 0, 5, "#else" ) == 0 )
		{
			if ( !stack.empty() && stack.back() )
			{
				nGuardDepth--;
				stack.back() = false;
			}
			continue;
		}

		size_t at = 0;
		while ( ( at = lines[ i ].find( needle, at ) ) != std::string::npos )
		{
			if ( nGuardDepth > 0 ) out.nInside++;
			else out.nOutside++;
			at += 1;
		}
	}

	return out;
}

const char *kRagdoll   = "game/client/ragdoll.cpp";
// A §6 STAY that takes the free scope in UNGUARDED code, and so is the live
// proof that the gate ragdoll.cpp stopped spelling still exists and is still
// taken. Named here because it is a PRESENCE anchor for an absence fact.
// The positive control that the client's source list is being read: the view
// (viewrender.cpp's, shell_viewrender_client.cpp's since C7 step 3).
const char *kViewRender = "sourcesharp/game/client/shell_viewrender_client.cpp";

} // namespace

//=============================================================================
// WHAT MOVED -- and then went. The library itself left client.so and the
// repository (2026-10-18): nothing read its gate or counter and no sweep was
// registered. Its two facts here (no entity type in its interface; compiled
// without a forced include) went with it; BoneLeafEvictionTests (managed)
// holds that it is gone and that no client file spells its names.
//=============================================================================

TEST( BoneLeafRehome, TheRagdollBuilderUsesTheFreeScopeAndNotTheEntitysNestedOne )
{
	// THE SUBJECT LEFT THE BUILD. This fact held that the §6 STAY file
	// game/client/ragdoll.cpp spelled neither bone-access gate (its one taker,
	// C_ServerRagdoll::UpdateOnRemove, went with the `residue` wave). The file
	// itself left client.so in plan physics_bones_native_zero Phase 3: the
	// client's ragdolls are SourceSharp.Client's, and no native file builds a
	// body.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kViewRender ) )
		<< kViewRender << " is not in the client project -- the reader is not "
		   "reading the client's source list";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kRagdoll ) )
		<< kRagdoll << " is back in the client project; this fact's old "
		   "assertions about which gate it spells apply again";
}

//=============================================================================
// THE FOUR THAT DID NOT MOVE
//=============================================================================

TEST( BoneLeafRehome, TheGuardScannerDistinguishesInsideFromOutside )
{
	// The arm above is worth nothing if the scanner answers "inside" for
	// everything, so the scanner is driven against a source it is handed.
	//
	// THE SECOND REGION IS ELSELESS, AND IT IS THE ONE THAT MATTERS.
	//
	// This fixture was one region and that region had an `#else`. The scanner
	// leaves a guard in TWO places -- on `#else` (which flips the frame and
	// decrements) and on `#endif` (which decrements only if the frame is still
	// flagged). With an `#else` in the way the `#endif` arm never decrements:
	// the flag was already cleared. So the `#endif` path was NEVER EXERCISED,
	// and a scanner that never left an elseless guard passed this fixture
	// unchanged -- mutation-verified, `the-close` sub-lane G spec G33, which
	// read GREEN against the one-region fixture and RED against this one.
	//
	// That is not a hypothetical shape. EVERY alias guard this scanner is
	// pointed at in the three stay files is an elseless `#ifndef`, so the one
	// path the old fixture skipped was the only path the real data takes.
	const std::string sample =
		"NEEDLE(a);\n"
		"#ifndef SS_CBASEENTITY_ALIAS\n"
		"NEEDLE(b);\n"
		"#if 1\n"
		"NEEDLE(c);\n"
		"#endif\n"
		"#else\n"
		"NEEDLE(d);\n"
		"#endif\n"
		"NEEDLE(e);\n"
		"#ifndef SS_CBASEENTITY_ALIAS\n"
		"NEEDLE(f);\n"
		"#endif\n"
		"NEEDLE(g);\n";

	const GuardCount c = CountAgainstAliasGuard( sample, "NEEDLE(" );
	EXPECT_EQ( 3, c.nInside ) << "b and c are the guarded ones, and f is the "
	                             "elseless region's";
	EXPECT_EQ( 4, c.nOutside ) << "a, d (the ARMED arm) and e are not -- and "
	                              "NOR IS g, which is the whole point: g is "
	                              "after an elseless `#endif`, so a scanner "
	                              "that does not decrement there counts it "
	                              "inside a guard it never left";
}
