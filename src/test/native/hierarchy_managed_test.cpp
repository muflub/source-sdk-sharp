//========= SourceSharp — parenting is managed, and the native side proves it ==//
//
// plan_entity.md step 2h landed SourceSharp.Abi/Hierarchy.cs with 52 facts over
// it, and left the native policy running beside it because the slots did not
// exist yet. This file is the check that the native half is GONE rather than
// duplicated — the thing §1 names as what is wrong with the tree:
//
//     "The parallel entity exists next to the stock one rather than instead of
//      it, and every seam between the two is a place for a silent wrong answer."
//
// WHY A TEXT CHECK AND NOT A BEHAVIOURAL ONE. Every fact here is about code that
// does NOT exist, and a behavioural test cannot see an absence: a native
// registry standing beside the managed one would compose every child twice and
// the game would look CORRECT — the two composers agree, because one is a port
// of the other — right up until they disagreed about one link. There is no run
// that catches that and no assertion to write about it. What can be checked is
// that the second composer is not there.
//
// THE FACTS ARE PAIRED WITH WHAT THEY ADMIT, so none of them is a needle that a
// rename would satisfy:
//
//   * the registry is gone            AND no CUtlVector survives in the file
//   * the arithmetic is gone          AND the four mathlib calls it was made of
//                                     do not appear
//   * the hot relays are guarded      AND the guard is a BLOCK word, named
//   * IsParented crosses nothing      AND it reads ParentHandle
//   * two entry points went away      AND nothing anywhere still calls them
//   * native never writes ChildCount  except the one line that zeroes it at
//                                     construction
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>

#include <fstream>
#include <string>
#include <vector>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string Read( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line;
		all += '\n';
	}
	return all;
}

//-----------------------------------------------------------------------------
// COMMENTS STRIPPED, and it is not tidiness. This file's header, and the
// header of the file it reads, both NAME the things that must be gone —
// g_ParentLinks, StandaloneHierarchy_RunFrame, the arithmetic — because saying
// what was removed is how the removal stays understood. Against raw text every
// fact below would fail on the prose that explains it.
//-----------------------------------------------------------------------------
std::string CodeOnly( const std::string &strFile )
{
	std::string out;
	bool bBlock = false, bLine = false, bStr = false, bChr = false;

	for ( size_t i = 0; i < strFile.size(); i++ )
	{
		const char c = strFile[ i ];
		const char n = ( i + 1 < strFile.size() ) ? strFile[ i + 1 ] : '\0';

		if ( bBlock ) { if ( c == '*' && n == '/' ) { bBlock = false; i++; } continue; }
		if ( bLine )  { if ( c == '\n' ) { bLine = false; out += '\n'; } continue; }
		if ( bStr )   { out += c; if ( c == '\\' ) { out += n; i++; } else if ( c == '"' ) bStr = false; continue; }
		if ( bChr )   { out += c; if ( c == '\\' ) { out += n; i++; } else if ( c == '\'' ) bChr = false; continue; }

		if ( c == '/' && n == '*' ) { bBlock = true; i++; continue; }
		if ( c == '/' && n == '/' ) { bLine = true; continue; }
		if ( c == '"' )  { bStr = true; out += c; continue; }
		if ( c == '\'' ) { bChr = true; out += c; continue; }

		out += c;
	}

	return out;
}

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
		|| ( c >= '0' && c <= '9' ) || c == '_';
}

// A WHOLE-WORD find, because every needle here is an identifier and a substring
// match would be satisfied by a longer name that means something else.
bool ContainsWord( const std::string &strHay, const std::string &strWord )
{
	size_t at = strHay.find( strWord );
	while ( at != std::string::npos )
	{
		const bool bLeft  = ( at == 0 ) || !IsIdentChar( strHay[ at - 1 ] );
		const size_t end  = at + strWord.size();
		const bool bRight = ( end >= strHay.size() ) || !IsIdentChar( strHay[ end ] );

		if ( bLeft && bRight )
			return true;

		at = strHay.find( strWord, at + 1 );
	}

	return false;
}

// The body of one free function, brace-counted from its signature.
std::string BodyOf( const std::string &strFile, const std::string &strSignature )
{
	const size_t start = strFile.find( strSignature );
	if ( start == std::string::npos )
		return std::string();

	const size_t open = strFile.find( '{', start );
	if ( open == std::string::npos )
		return std::string();

	int nDepth = 0;
	for ( size_t i = open; i < strFile.size(); i++ )
	{
		if ( strFile[ i ] == '{' )
			nDepth++;
		else if ( strFile[ i ] == '}' && --nDepth == 0 )
			return strFile.substr( open, i - open + 1 );
	}

	return std::string();
}

const char *const kHierarchy = "sourcesharp/game/server/shell_standalone_hierarchy.cpp";

//-----------------------------------------------------------------------------
// THE PARENTING HALF OF THE FILE, and there is a reason it has to be told from
// the other one.
//
// Two very different things live in shell_standalone_hierarchy.cpp: the
// SERVER'S SKELETON -- EnsureBones, the attachment reads, SetPoseSequence --
// which is native because a posed skeleton is bone_setup on the engine's clock,
// and parenting, which is a handful of relays. The skeleton composes bone
// matrices, so it uses ConcatTransforms and SetIdentityMatrix legitimately and
// always did.
//
// A check over the whole file would therefore report parenting's arithmetic as
// still present when what it found was the skeleton's, and the honest fix is
// not to drop the check but to say where the boundary is. HierarchyApi() is the
// first thing in the parenting half and nothing above it mentions the managed
// table at all.
//-----------------------------------------------------------------------------
std::string ParentingHalfOf( const std::string &strCode )
{
	const size_t at = strCode.find( "HierarchyApi" );

	return ( at == std::string::npos ) ? std::string() : strCode.substr( at );
}

//=============================================================================//
// FACT 1. THE REGISTRY IS GONE.
//=============================================================================//

//-----------------------------------------------------------------------------
// Every name the native registry was made of.
//
// THE TYPE AS WELL AS THE VECTORS, because a registry whose vector was renamed
// still needs a link struct to hold, and the struct is the harder thing to
// bring back by accident.
//-----------------------------------------------------------------------------
TEST( ParentingIsManaged, TheNativeRegistryIsGone )
{
	const std::string code = CodeOnly( Read( kHierarchy ) );

	ASSERT_FALSE( code.empty() ) << kHierarchy << " could not be read.";

	static const char *const kRegistry[] = {
		"StandaloneParentLink_t",
		"g_ParentLinks",
		"StandalonePendingAttachment_t",
		"g_PendingAttachments",
	};

	for ( size_t i = 0; i < sizeof( kRegistry ) / sizeof( kRegistry[ 0 ] ); i++ )
	{
		EXPECT_FALSE( ContainsWord( code, kRegistry[ i ] ) )
			<< kRegistry[ i ] << " is back in " << kHierarchy << ". The parenting"
			<< " registry is SourceSharp.Abi/Hierarchy.cs; a second one here is a"
			<< " second composer that agrees with the first until it does not.";
	}
}

//-----------------------------------------------------------------------------
// And nothing that could BE one. A registry is a container of links, and the
// file holds no container at all now — the closest thing left is the skeleton's
// bone arrays, which are members of the entity rather than file statics.
//-----------------------------------------------------------------------------
TEST( ParentingIsManaged, TheFileHoldsNoContainerOfItsOwn )
{
	const std::string code = CodeOnly( Read( kHierarchy ) );

	ASSERT_FALSE( code.empty() );

	// StandaloneEntities() returns a reference to the shim's vector and is
	// declared here; a DEFINITION of one would be storage.
	EXPECT_EQ( std::string::npos, code.find( "static CUtlVector" ) )
		<< kHierarchy << " declares a file-static container again.";

	EXPECT_EQ( std::string::npos, code.find( "CUtlVectorFixedGrowable" ) )
		<< kHierarchy << " is collecting links or orphans again. Both walks that"
		<< " needed that are Hierarchy.ParentMoved and Hierarchy.RunFrame now.";
}

//=============================================================================//
// FACT 2. THE ARITHMETIC IS GONE.
//=============================================================================//

//-----------------------------------------------------------------------------
// The four mathlib calls parenting's composition was made of, which are also
// the four sourcesharp/test/parent_compose_cases.txt was generated from.
//
// MatrixAngles SURVIVES and is not on the list: SourceSharp_EntityGetAttachmentFrame
// reads a CBaseAnimating's attachment out as an origin and a facing, which is a
// conversion rather than a composition. The distinction is exactly the one the
// golden table records — a composition is Concat and an Invert.
//-----------------------------------------------------------------------------
TEST( ParentingIsManaged, TheCompositionArithmeticIsGone )
{
	const std::string code = ParentingHalfOf( CodeOnly( Read( kHierarchy ) ) );

	ASSERT_FALSE( code.empty() ) << "the parenting half of " << kHierarchy
	                             << " could not be found.";

	static const char *const kArithmetic[] = {
		"ConcatTransforms",
		"MatrixInvert",
		"SetIdentityMatrix",
	};

	for ( size_t i = 0; i < sizeof( kArithmetic ) / sizeof( kArithmetic[ 0 ] ); i++ )
	{
		EXPECT_FALSE( ContainsWord( code, kArithmetic[ i ] ) )
			<< kArithmetic[ i ] << " is back in " << kHierarchy << ". The"
			<< " composition is Hierarchy.Compose and Hierarchy.MeasureOffset,"
			<< " pinned to this file's former arithmetic by"
			<< " sourcesharp/test/parent_compose_cases.txt.";
	}
}

//-----------------------------------------------------------------------------
// THE BOUNDARY THE FACT ABOVE RESTS ON, asserted rather than assumed.
//
// ParentingHalfOf cuts at the first mention of HierarchyApi. If the skeleton
// ever grew one -- or if the parenting half were reordered so that something
// else came first -- the cut would move and the arithmetic check would silently
// start covering less. So: the skeleton is above the cut, and the relays are
// below it.
//-----------------------------------------------------------------------------
TEST( ParentingIsManaged, TheParentingHalfBeginsWhereTheSkeletonEnds )
{
	const std::string code = CodeOnly( Read( kHierarchy ) );
	const std::string parenting = ParentingHalfOf( code );

	ASSERT_FALSE( parenting.empty() );

	// Below the cut: every relay.
	EXPECT_TRUE( ContainsWord( parenting, "StandaloneHierarchy_ParentMoved" ) );
	// StandaloneHierarchy_EntityRemoved had no caller and went in the server gc
	// cull (2026-10-01; --print-gc-sections dropped it).
	EXPECT_FALSE( ContainsWord( parenting, "StandaloneHierarchy_EntityRemoved" ) );
	EXPECT_TRUE( ContainsWord( parenting, "SourceSharp_EntitySetParent" ) );

	// Above it: the skeleton, which is not parenting's and is not being checked
	// for an absence of anything.
	const std::string skeleton = code.substr( 0, code.size() - parenting.size() );

	// What is left of the skeleton above the cut is its one thunk (plan
	// physics_bones_native_zero 1.2: the rest is managed), and nothing of it
	// below.
	EXPECT_TRUE( ContainsWord( skeleton, "TestHitboxes" ) )
		<< "CStandaloneEntity::TestHitboxes is no longer above the parenting half."
		<< " The cut ParentingHalfOf makes is wrong, and the arithmetic check"
		<< " below it is covering the wrong text.";
	EXPECT_TRUE( ContainsWord( skeleton, "ManagedBones" ) );
	EXPECT_FALSE( ContainsWord( code, "EnsureBones" ) )
		<< "a native skeleton build is back in the hierarchy file";
}

//-----------------------------------------------------------------------------
// And the cycle walk, which is the other decision the file used to make.
//-----------------------------------------------------------------------------
TEST( ParentingIsManaged, TheCycleRefusalIsGone )
{
	const std::string code = CodeOnly( Read( kHierarchy ) );

	ASSERT_FALSE( code.empty() );

	EXPECT_FALSE( ContainsWord( code, "WouldCycle" ) )
		<< "The cycle refusal is Hierarchy.WouldCycle, and its message names both"
		<< " ends. A native one would refuse a different set of links.";
}

//=============================================================================//
// FACT 3. THE HOT RELAYS ARE GUARDED, ON A BLOCK WORD.
//=============================================================================//

//-----------------------------------------------------------------------------
// The three relays a frame can reach many times over, and the guard each one
// opens with.
//
// THIS IS THE FACT THE CROSSING BUDGET RESTS ON. StandaloneHierarchy_ParentMoved
// is called at the tail of EVERY position write of EVERY entity on this
// hierarchy; unguarded it is one inbound crossing per moving entity per frame,
// which step 2h's ABI request called out as far more than lane 0c's baseline of
// 149.816 per frame. A guard that was quietly dropped would not fail any
// behavioural test — the game would be CORRECT and slow.
//-----------------------------------------------------------------------------
struct GuardedRelay_t
{
	const char *pszSignature;
	const char *pszGuard;
};

const GuardedRelay_t kGuarded[] = {
	{ "void StandaloneHierarchy_ParentMoved(",   "CarriesAnything" },
	{ "void StandaloneHierarchy_ChildMoved(",    "IsCarried"       },
	// StandaloneHierarchy_EntityRemoved's two rows went with it: nothing called
	// it (server gc cull, 2026-10-01).
};

TEST( ParentingIsManaged, EveryHotRelayIsGuardedBeforeItCrosses )
{
	const std::string code = CodeOnly( Read( kHierarchy ) );

	ASSERT_FALSE( code.empty() );

	for ( size_t i = 0; i < sizeof( kGuarded ) / sizeof( kGuarded[ 0 ] ); i++ )
	{
		const std::string body = BodyOf( code, kGuarded[ i ].pszSignature );

		ASSERT_FALSE( body.empty() )
			<< kGuarded[ i ].pszSignature << " is not in " << kHierarchy;

		EXPECT_TRUE( ContainsWord( body, kGuarded[ i ].pszGuard ) )
			<< kGuarded[ i ].pszSignature << " no longer tests "
			<< kGuarded[ i ].pszGuard << " before crossing. Every position write"
			<< " on this hierarchy reaches one of these.";
	}
}

//-----------------------------------------------------------------------------
// And the guard is a word in the ENGINE BLOCK rather than a native cache of it.
//
// A native copy would be state managed code writes and native reads through a
// second path — the drift §7 names — and it is the obvious shortcut the day
// somebody wants the guard to answer something the block does not hold.
//-----------------------------------------------------------------------------
TEST( ParentingIsManaged, TheGuardsAreBlockWordsAndNotANativeCache )
{
	const std::string code = CodeOnly( Read( kHierarchy ) );

	ASSERT_FALSE( code.empty() );

	const std::string carries = BodyOf( code, "static bool CarriesAnything(" );
	const std::string carried = BodyOf( code, "static bool IsCarried(" );

	ASSERT_FALSE( carries.empty() );
	ASSERT_FALSE( carried.empty() );

	EXPECT_TRUE( ContainsWord( carries, "ChildCount" ) )
		<< "CarriesAnything no longer reads Block().Head.ChildCount.";
	EXPECT_TRUE( ContainsWord( carries, "Block" ) )
		<< "CarriesAnything no longer reads the block at all.";

	EXPECT_TRUE( ContainsWord( carried, "ParentHandle" ) )
		<< "IsCarried no longer reads Block().Head.ParentHandle.";
	EXPECT_TRUE( ContainsWord( carried, "Block" ) )
		<< "IsCarried no longer reads the block at all.";
}

//=============================================================================//
// FACT 4. IsParented COSTS NO CROSSING.
//=============================================================================//

//-----------------------------------------------------------------------------
// Step 2h's ABI request asked for a slot for this. It does not need one, and
// this is what says so: the body reads the block and resolves the handle, and
// never touches the managed table.
//
// THE SECOND HALF IS THE ONE THAT CAN ROT. Adding a crossing here is the easy
// way to answer a question the block does not hold, and nothing about the game
// would look different — a call reached by UpdateFlight and by every world query
// would simply have become an inbound crossing per parented entity per frame.
//-----------------------------------------------------------------------------
TEST( ParentingIsManaged, IsParentedIsAnsweredFromTheBlockWithNoCrossing )
{
	const std::string code = CodeOnly( Read( kHierarchy ) );

	ASSERT_FALSE( code.empty() );

	const std::string body = BodyOf( code, "bool StandaloneHierarchy_IsParented(" );

	ASSERT_FALSE( body.empty() );

	EXPECT_TRUE( ContainsWord( body, "IsCarried" ) )
		<< "IsParented no longer reads the block's ParentHandle.";

	EXPECT_FALSE( ContainsWord( body, "HierarchyApi" ) )
		<< "IsParented crosses into managed code again. It is answered from"
		<< " EngineBlockHead::ParentHandle, which managed code writes -- that is"
		<< " what §3.2 means by 'answered from memory that managed code writes'.";
}

//-----------------------------------------------------------------------------
// The same claim about the entity API's own getter, which is the same word.
//-----------------------------------------------------------------------------
TEST( ParentingIsManaged, EntityGetParentIsAnsweredFromTheBlockWithNoCrossing )
{
	const std::string code = CodeOnly( Read( kHierarchy ) );

	ASSERT_FALSE( code.empty() );

	const std::string body = BodyOf( code, "uint32_t SourceSharp_EntityGetParent(" );

	ASSERT_FALSE( body.empty() );

	EXPECT_TRUE( ContainsWord( body, "ParentHandle" ) );
	EXPECT_FALSE( ContainsWord( body, "HierarchyApi" ) )
		<< "EntityGetParent crosses again for a value that is one load away.";
}

//=============================================================================//
// FACT 5. THE TWO ENTRY POINTS THAT WENT AWAY ARE GONE FROM THE WHOLE TREE.
//=============================================================================//

//-----------------------------------------------------------------------------
// Not "gone from the file" — gone from every file that could still call them.
//
// A relay left behind and never called is dead code; a CALL left behind with no
// definition does not link, which is loud. What this catches is the third
// state: a call re-added to native, which would run the sweep or the teardown
// from two places. Composing every swept link twice per frame is not visible in
// a screenshot, and emptying the registry twice is not visible at all.
//-----------------------------------------------------------------------------
TEST( ParentingIsManaged, TheSweepAndTheTeardownAreDrivenFromManagedCodeAlone )
{
	static const char *const kFiles[] = {
		"sourcesharp/game/server/shell_standalone_hierarchy.cpp",
		"sourcesharp/game/server/shell_standalone_base.cpp",
		"sourcesharp/game/server/shell_standalone.cpp",
		"sourcesharp/game/server/shell_standalone.h",
		"sourcesharp/game/server/shell_worldquery.cpp",
		"sourcesharp/game/server/shell_mapparse.cpp",
	};

	int nRead = 0;

	for ( size_t i = 0; i < sizeof( kFiles ) / sizeof( kFiles[ 0 ] ); i++ )
	{
		const std::string raw = Read( kFiles[ i ] );

		ASSERT_FALSE( raw.empty() ) << kFiles[ i ] << " could not be read.";
		nRead++;

		const std::string code = CodeOnly( raw );

		EXPECT_FALSE( ContainsWord( code, "StandaloneHierarchy_RunFrame" ) )
			<< kFiles[ i ] << " calls or declares StandaloneHierarchy_RunFrame."
			<< " The sweep runs in SourceSharp.Server's StandaloneHierarchy,"
			<< " between the directory's think pass and StandaloneFinish.";

		EXPECT_FALSE( ContainsWord( code, "StandaloneHierarchy_LevelShutdown" ) )
			<< kFiles[ i ] << " calls or declares StandaloneHierarchy_LevelShutdown."
			<< " The registry is emptied in OnLevelShutdownPreEntity.";

		EXPECT_FALSE( ContainsWord( code, "StandaloneHierarchy_SetParentEx" ) )
			<< kFiles[ i ] << " calls StandaloneHierarchy_SetParentEx, which was"
			<< " the native link-maker. Hierarchy.SetParent is the one now.";
	}

	// A loop over nothing is green, so the count is asserted.
	EXPECT_EQ( (int)( sizeof( kFiles ) / sizeof( kFiles[ 0 ] ) ), nRead );
}

//=============================================================================//
// FACT 6. NATIVE NEVER WRITES ChildCount, EXCEPT TO ZERO IT AT CONSTRUCTION.
//=============================================================================//

//-----------------------------------------------------------------------------
// The word is managed code's, and this is what keeps it so.
//
// TWO WRITERS WOULD BE THE WORST KIND OF WRONG. Native raising the count for a
// link managed does not hold means a crossing per position write for ever;
// native LOWERING it for a link managed does hold means a parent that silently
// stops carrying its children, with the registry still saying it does. Neither
// prints anything.
//
// The one write allowed is the constructor's `head.ChildCount = 0`, and it is
// allowed because it is not bookkeeping: a freshly allocated blob holds whatever
// the allocator left, and an uninitialised guard word reads as "this entity has
// children" often enough to matter.
//-----------------------------------------------------------------------------
TEST( ParentingIsManaged, NativeWritesChildCountOnlyToZeroItAtConstruction )
{
	static const char *const kFiles[] = {
		"sourcesharp/game/server/shell_standalone_hierarchy.cpp",
		"sourcesharp/game/server/shell_standalone_base.cpp",
		"sourcesharp/game/server/shell_standalone.cpp",
		"sourcesharp/game/client/shell_standalone_client.cpp",
	};

	int nWrites = 0;

	for ( size_t i = 0; i < sizeof( kFiles ) / sizeof( kFiles[ 0 ] ); i++ )
	{
		const std::string code = CodeOnly( Read( kFiles[ i ] ) );

		ASSERT_FALSE( code.empty() ) << kFiles[ i ] << " could not be read.";

		size_t at = code.find( "ChildCount" );

		while ( at != std::string::npos )
		{
			// Everything after the name up to the next semicolon: an assignment
			// shows as '=' that is not '==' or '!='.
			const size_t end = code.find( ';', at );
			const std::string rest = code.substr( at, ( end == std::string::npos )
			                                           ? std::string::npos : end - at );

			const size_t eq = rest.find( '=' );

			if ( eq != std::string::npos
			     && rest.compare( eq, 2, "==" ) != 0
			     && ( eq == 0 || rest[ eq - 1 ] != '!' ) )
			{
				nWrites++;

				EXPECT_NE( std::string::npos, rest.find( "0" ) )
					<< kFiles[ i ] << " writes ChildCount a value other than zero:"
					<< " \"" << rest << "\". Managed code owns this word.";
			}

			at = code.find( "ChildCount", at + 1 );
		}
	}

	// EXACTLY ONE, and a second would be a second owner. Counted rather than
	// bounded, because "at most one" is also true of none — and none means the
	// blob's guard word is whatever the allocator left.
	EXPECT_EQ( 1, nWrites )
		<< "Native writes to EngineBlockHead::ChildCount: " << nWrites
		<< ". Exactly one is right, and it is head.ChildCount = 0 in"
		<< " CStandaloneEntity's construction.";
}

} // namespace
