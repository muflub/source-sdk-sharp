//========= SourceSharp — two rulings that a later lane would undo ============//
//
// Lane f-srvshim cleared the server shim's five owed objects. Two of its
// decisions are REFUSALS of an edit that looks like an improvement, and a
// refusal written only in a comment is a refusal that survives until somebody
// tidies the comment. Both are pinned here.
//
// RULING 1 — `SourceSharp_EntitiesInSphere` KEEPS ITS REGISTRY WALK.
//
// The function sweeps twice: the SDK entity list, and `StandaloneEntities()`,
// the parallel hierarchy's registry. `anyentity.h`'s `CAnyEntitySphereQuery` is
// a spatial-partition enumerator that reaches BOTH hierarchies, so replacing the
// second loop with one `EnumerateElementsInSphere` is the obvious optimisation
// on a path marked `SOURCESHARP_TRACE_HOT`.
//
// IT WOULD SILENTLY REMOVE A CAPABILITY. `CStandaloneEntity::UpdatePartition`
// opens
//
//     const bool bIsSolid = ( Block().Head.SolidType != SOLID_NONE )
//                        || ( Block().Head.SolidFlags & FSOLID_TRIGGER ) != 0;
//     if ( !bIsSolid ) { ::partition->DestroyHandle( m_Partition ); ... return; }
//
// so an entity that is neither solid nor a trigger is OUT of the partition and
// no enumerator can find it. `shell_worldquery.cpp` names the case in its own
// words: "excluding the server-only ones would leave one env_fire unable to find
// the next one to spread to."
//
// So this file pins the REASON and the CONSEQUENCE together. Pinning only the
// consequence ("the loop is still there") would go green the day the reason
// changed, which is the day the loop becomes safe to delete.
//
// RULING 2 — the SDK sphere walk is GUARDED, not deleted, and the WORLD RELEASE
// in `SourceSharp_LevelClearEntityList` is NOT guarded.
//
// `world_release_test.cpp` exists because that release being a no-op faulted the
// engine on the SECOND `map` command and no gate saw it for weeks, every live
// gate having loaded one map. It sits two lines above a `gEntList.Clear()` that
// IS guarded, which is exactly the shape a future mechanical "guard the whole
// body" edit gets wrong.
//
// PROVEN RED, all four, by mutating the subject rather than the test:
//   * delete the `if ( !bIsSolid )` early-out from UpdatePartition
//         -> TheReasonTheRegistryWalkExists fails
//   * replace the StandaloneEntities() loop with CAnyEntitySphereQuery
//         -> TheSphereSweepStillWalksTheRegistry fails
//   * wrap the world release in the guard beside it
//         -> TheWorldReleaseIsOutsideTheGuard fails
//   * delete the guard around gEntList.Clear()
//         -> TheSdkListClearIsInsideTheGuard fails
//
//=============================================================================//
#include <gtest/gtest.h>

#include <dirent.h>

#include <fstream>
#include <string>
#include <vector>

namespace
{

const char *SourceRoot()
{
	static const char *kCandidates[] = { "sourcesharp", "src/sourcesharp", "../src/sourcesharp" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		DIR *pDir = opendir( kCandidates[ i ] );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

// Comment lines are BLANKED rather than dropped, so a reported index is the real
// line number and a rule about a comment cannot be satisfied by prose. Both
// matter here: ruling 1 is about a condition and ruling 2 is about which side of
// a `#ifndef` a statement is on, and this file's own header quotes both.
std::vector< std::string > CodeLines( const char *pszRelative )
{
	std::vector< std::string > out;

	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return out;

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRelative ).c_str() );
	if ( !file.is_open() )
		return out;

	std::string line;
	while ( std::getline( file, line ) )
	{
		const size_t nFirst = line.find_first_not_of( " \t" );
		if ( nFirst != std::string::npos && line.compare( nFirst, 2, "//" ) == 0 )
			out.push_back( std::string() );
		else
			out.push_back( line );
	}

	return out;
}

int FirstLineWith( const std::vector< std::string > &lines, const char *pszNeedle, int nFrom = 0 )
{
	for ( size_t i = (size_t)nFrom; i < lines.size(); i++ )
		if ( lines[ i ].find( pszNeedle ) != std::string::npos )
			return (int)i;
	return -1;
}

const char *kWorldQuery  = "game/server/shell_worldquery.cpp";
const char *kStandalone  = "game/server/shell_standalone_base.cpp";
const char *kLevelSteps  = "game/server/shell_level.cpp";

//=============================================================================//
// THE CHECKS THAT WOULD OTHERWISE NOT BE ABLE TO FAIL.
//
// Every rule below is about text in a file. A rename, a move or an unexpected
// working directory makes each file read as empty, and a rule about an empty
// file passes. This makes the emptiness the failure, for all three at once.
//=============================================================================//

TEST( SrvShimRulings, TheThreeFilesTheseRulingsAreAboutWereActuallyRead )
{
	ASSERT_TRUE( SourceRoot() != NULL )
		<< "no src/sourcesharp/ from here, so every rule below is vacuous";

	const char *kFiles[] = { kWorldQuery, kStandalone, kLevelSteps };

	for ( size_t i = 0; i < sizeof( kFiles ) / sizeof( kFiles[ 0 ] ); i++ )
	{
		const std::vector< std::string > lines = CodeLines( kFiles[ i ] );

		ASSERT_FALSE( lines.empty() ) << kFiles[ i ] << " read as empty";
		EXPECT_GT( lines.size(), 400u )
			<< kFiles[ i ] << " read as " << lines.size()
			<< " lines, which is not this file";
	}
}

//=============================================================================//
// RULING 1 — the registry walk, and the reason it cannot become an enumerator.
//=============================================================================//

// THE REASON. If this stops being true the ruling stops applying, and the loop
// below becomes safe to replace -- so the reason is pinned first and the
// consequence second.
TEST( SrvShimRulings, TheReasonTheRegistryWalkExists )
{
	const std::vector< std::string > lines = CodeLines( kStandalone );

	const int nAt = FirstLineWith( lines, "void CStandaloneEntity::UpdatePartition()" );
	ASSERT_GE( nAt, 0 ) << "CStandaloneEntity::UpdatePartition is gone from "
	                    << kStandalone << ", so this rule is watching nothing";

	// THE CONDITION'S TEXT, NOT THE PRESENCE OF ITS NAME. This rule first read
	// `find( "bIsSolid" )`, and the mutation `if ( false && !bIsSolid )` -- the
	// change that most exactly removes the behaviour -- left the name on the
	// page and the rule green. The harness caught it by mutating this function
	// rather than the test. Pinning the exact text is brittle on purpose:
	// drifting it should require opening this file and retiring the ruling
	// deliberately, which is the only thing a source-shape rule can honestly
	// buy. It is NOT a proof that a non-solid entity leaves the partition --
	// that would need a live partition -- it is a proof that the sentence the
	// ruling rests on has not been edited underneath it.
	bool bTest = false, bDestroy = false;
	for ( size_t i = (size_t)nAt; i < lines.size() && i < (size_t)nAt + 40; i++ )
	{
		const std::string &l = lines[ i ];

		if ( l.find( "SolidType != SOLID_NONE" ) != std::string::npos
		  && l.find( "bIsSolid" )                != std::string::npos )
			bTest = true;

		// The early-out itself, with nothing else in the condition.
		if ( l.find( "if ( !bIsSolid )" ) != std::string::npos )
			bDestroy = true;
	}

	EXPECT_TRUE( bTest )
		<< "UpdatePartition's solidity test is no longer "
		   "`bIsSolid = ( ... SolidType != SOLID_NONE ) || ( ... FSOLID_TRIGGER )`. "
		   "If solidity is no longer what decides partition membership, the "
		   "registry walk in shell_worldquery.cpp may be replaceable -- decide "
		   "that here rather than by editing around this rule.";
	EXPECT_TRUE( bDestroy )
		<< "UpdatePartition no longer takes the `if ( !bIsSolid )` early-out "
		   "that destroys the partition handle. If a non-solid standalone entity is now IN the "
		   "spatial partition, the registry walk in shell_worldquery.cpp's "
		   "SourceSharp_EntitiesInSphere is no longer load-bearing and "
		   "CAnyEntitySphereQuery may replace it -- but make that change "
		   "deliberately, and retire this rule with it.";
}

// THE CONSEQUENCE.
TEST( SrvShimRulings, TheSphereSweepStillWalksTheRegistry )
{
	const std::vector< std::string > lines = CodeLines( kWorldQuery );

	const int nAt = FirstLineWith( lines, "int32_t SourceSharp_EntitiesInSphere(" );
	ASSERT_GE( nAt, 0 ) << "SourceSharp_EntitiesInSphere is gone from " << kWorldQuery;

	bool bWalks = false;
	for ( size_t i = (size_t)nAt; i < lines.size() && i < (size_t)nAt + 90; i++ )
		if ( lines[ i ].find( "StandaloneEntities()" ) != std::string::npos )
			bWalks = true;

	EXPECT_TRUE( bWalks )
		<< "SourceSharp_EntitiesInSphere no longer walks StandaloneEntities(). "
		   "A partition enumerator cannot see a standalone entity that is "
		   "neither solid nor a trigger -- CStandaloneEntity::UpdatePartition "
		   "destroys its handle -- so an env_fire can no longer find the next "
		   "one to spread to.";
}

//=============================================================================//
// RULING 2 — which side of the guard each statement is on.
//=============================================================================//

// Whether a line is compiled ONLY while SS_CBASEENTITY_ALIAS is unarmed. Every
// other `#if` counts as compiled, which is the conservative direction: it can
// only move a line from "guarded" to "live".
std::vector< bool > UnarmedOnly( const std::vector< std::string > &lines )
{
	std::vector< bool > out( lines.size(), false );
	std::vector< bool > stack;
	int depth = 0;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string &l = lines[ i ];
		const size_t nAt = l.find_first_not_of( " \t" );
		const bool bDirective = ( nAt != std::string::npos && l[ nAt ] == '#' );

		if ( bDirective && l.find( "#if" ) != std::string::npos )
		{
			const bool bOurs = ( l.find( "ifndef SS_CBASEENTITY_ALIAS" ) != std::string::npos )
			                || ( l.find( "!defined( SS_CBASEENTITY_ALIAS" ) != std::string::npos );
			stack.push_back( bOurs );
			if ( bOurs )
				depth++;
			continue;
		}
		if ( bDirective && l.find( "#else" ) != std::string::npos && !stack.empty() )
		{
			if ( stack.back() ) { stack.back() = false; depth--; }
			continue;
		}
		if ( bDirective && l.find( "#endif" ) != std::string::npos )
		{
			if ( !stack.empty() ) { if ( stack.back() ) depth--; stack.pop_back(); }
			continue;
		}

		out[ i ] = ( depth > 0 );
	}

	return out;
}

// How deeply nested in ANY preprocessor conditional each line is. Where
// UnarmedOnly answers "compiled only while SS_CBASEENTITY_ALIAS is unarmed",
// this answers the weaker and now more useful question: "is this line
// unconditional at all". A line at depth 0 is compiled in every configuration
// this file has, which is what ruling 2's surviving half is about.
std::vector< int > ConditionalDepth( const std::vector< std::string > &lines )
{
	std::vector< int > out( lines.size(), 0 );
	int depth = 0;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string &l = lines[ i ];
		const size_t nAt = l.find_first_not_of( " \t" );
		const bool bDirective = ( nAt != std::string::npos && l[ nAt ] == '#' );

		if ( bDirective && l.find( "#if" ) != std::string::npos )
		{
			depth++;
			continue;
		}
		if ( bDirective && l.find( "#endif" ) != std::string::npos )
		{
			if ( depth > 0 )
				depth--;
			continue;
		}
		if ( bDirective && l.find( "#else" ) != std::string::npos )
			continue;
		if ( bDirective && l.find( "#elif" ) != std::string::npos )
			continue;

		out[ i ] = depth;
	}

	return out;
}

TEST( SrvShimRulings, TheGuardReaderSeesBothArmsOfAFileItIsGivenAndThisFileHasNoArmsLeft )
{
	// READ THE ASSERTIONS, NOT THE NAME, AND NOTE THAT THIS SELF-TEST HAD TO
	// MOVE OFF THE FILE IT WAS MEASURING.
	//
	// The reader is the instrument, and an instrument that always answers "not
	// guarded" would make the rules below pass for the wrong reason. It used to
	// be proved on shell_level.cpp itself, which carried guarded lines and
	// unguarded ones and had to report some of each.
	//
	// THAT FILE HAS NO GUARDED LINES ANY MORE. Every `#ifndef
	// SS_CBASEENTITY_ALIAS` arm in it was text the compiler never saw -- the
	// alias was armed in every configuration this tree builds -- and the close
	// deleted all of them with the other 453. Proving the reader on that file
	// now would be proving it against a file with nothing to find, which is the
	// exact vacuity the self-test exists to prevent.
	//
	// So the instrument is proved on a fixture it is HANDED, where both arms are
	// written out and the answer is known, and the file's emptiness is asserted
	// separately as a fact about the file.
	std::vector< std::string > fixture;
	fixture.push_back( "\tint nLive = 1;" );                        // 0  live
	fixture.push_back( "\t#ifndef SS_CBASEENTITY_ALIAS" );
	fixture.push_back( "\tgEntList.Clear();" );                     // 2  guarded
	fixture.push_back( "\t#ifdef DEBUG" );
	fixture.push_back( "\tAssert( 0 );" );                          // 4  guarded, nested
	fixture.push_back( "\t#endif" );
	fixture.push_back( "\t#else" );
	fixture.push_back( "\tint nArmed = 2;" );                       // 7  live (the armed arm)
	fixture.push_back( "\t#endif" );
	fixture.push_back( "\tint nAlsoLive = 3;" );                    // 9  live

	// A SECOND REGION WITH NO `#else`, AND A MUTATION IS WHY IT IS HERE. The
	// block above closes its guard through `#else`, which is where the reader
	// takes depth back to zero -- so nothing in it ever exercised the `#endif`
	// path, and a reader that NEVER decremented at #endif passed this fixture
	// unchanged. Every guard in the tree this reader is pointed at is an
	// `#ifndef SS_CBASEENTITY_ALIAS` with no else, which is exactly the shape
	// that was going unmeasured.
	fixture.push_back( "\t#ifndef SS_CBASEENTITY_ALIAS" );
	fixture.push_back( "\tgEntList.CleanupDeleteList();" );          // 11 guarded
	fixture.push_back( "\t#endif" );
	fixture.push_back( "\tint nLiveAfterElselessGuard = 4;" );       // 13 live

	const std::vector< bool > read = UnarmedOnly( fixture );
	ASSERT_EQ( fixture.size(), read.size() );

	EXPECT_FALSE( read[ 0 ] ) << "the reader calls an unconditional line guarded";
	EXPECT_TRUE ( read[ 2 ] ) << "the reader misses the #ifndef arm -- every "
	                             "`is inside the guard` rule would pass for free";
	EXPECT_TRUE ( read[ 4 ] ) << "the reader loses the guard at a NESTED #ifdef, "
	                             "so a guarded line with a debug block around it "
	                             "reads as live";
	EXPECT_FALSE( read[ 7 ] ) << "the reader calls the ARMED arm guarded -- it is "
	                             "the arm this build compiles";
	EXPECT_FALSE( read[ 9 ] ) << "the reader never leaves the guard at #else, so "
	                             "the whole rest of a file reads as guarded";

	EXPECT_TRUE ( read[ 11 ] ) << "the reader misses an #ifndef arm that has no "
	                              "#else -- which is the shape of every alias "
	                              "guard this reader was ever pointed at";
	EXPECT_FALSE( read[ 13 ] ) << "the reader never leaves an ELSELESS guard at "
	                              "#endif, so everything after the first such "
	                              "region in a file reads as guarded";

	// AND THE FILE ITSELF NOW HAS NOTHING FOR IT TO FIND. Comment lines are
	// blanked by CodeLines, so the paragraphs in shell_level.cpp that
	// EXPLAIN the retired guard do not answer for the code.
	const std::vector< std::string > lines = CodeLines( kLevelSteps );
	ASSERT_FALSE( lines.empty() );

	const std::vector< bool > dead = UnarmedOnly( lines );
	int nGuarded = 0, nLive = 0;
	for ( size_t i = 0; i < dead.size(); i++ )
	{
		if ( lines[ i ].empty() )
			continue;
		if ( dead[ i ] ) nGuarded++; else nLive++;
	}

	EXPECT_EQ( 0, nGuarded )
		<< "an SS_CBASEENTITY_ALIAS arm is back in " << kLevelSteps << ". The "
		<< "macro it names does not exist any more, so whichever half is live "
		<< "is a check that cannot fail -- and the rules below read the file "
		<< "assuming there is nothing to be on the wrong side of.";
	EXPECT_GT( nLive, 100 )
		<< "the reader found " << nLive << " live lines in " << kLevelSteps
		<< "; it is reporting everything as guarded, or the file is not this "
		   "file";
}

TEST( SrvShimRulings, TheWorldReleaseIsOutsideTheGuard )
{
	// READ THE ASSERTION, NOT JUST THE NAME. This used to ask the guard reader
	// whether the release was inside `#ifndef SS_CBASEENTITY_ALIAS`, and with
	// every such arm deleted that reader can no longer answer anything but
	// "live" -- which would make this a check that cannot fail. It asks the
	// stronger question instead: the release is UNCONDITIONAL, at preprocessor
	// depth 0, so no `#if` of any kind has been drawn around it.
	const std::vector< std::string > lines = CodeLines( kLevelSteps );
	const std::vector< int > depth = ConditionalDepth( lines );

	const int nAt = FirstLineWith( lines, "pWorld->GetNetworkable()->Release()" );
	ASSERT_GE( nAt, 0 )
		<< "the world release is gone from " << kLevelSteps
		<< ". world_release_test.cpp says what that costs: the SECOND `map` "
		   "command faults inside the engine on its first trace.";

	EXPECT_EQ( 0, depth[ nAt ] )
		<< kLevelSteps << ":" << ( nAt + 1 )
		<< " the world release is inside a preprocessor conditional. It is "
		   "NOT gEntList's -- plan_entity.md §3.3 moved the world onto its own "
		   "handle, which is why it is a line here rather than the first "
		   "iteration of Clear(). Guarded on any macro, the world would go "
		   "unreleased in some configuration and the next map's "
		   "CWorldFactory::Create could not build one.";

	// And it is still the LAST thing that function does, rather than a line
	// left behind in a body somebody emptied around it.
	// THE STEP IS A VTABLE METHOD NOW, not a free function. plan_shim.md 5 3a
	// (lane 3b-enginebind) folded shell_levelshim.cpp into shell_levelsteps.cpp
	// as shell_level.cpp, so the needle is the method the step became rather
	// than the SourceSharp_Level...() it used to forward from.
	const int nFunc = FirstLineWith( lines, "virtual void ClearEntityList() OVERRIDE" );
	ASSERT_GE( nFunc, 0 ) << "IShimLevel::ClearEntityList is gone from " << kLevelSteps;
	EXPECT_LT( nFunc, nAt )
		<< "the world release is no longer inside ClearEntityList";
}

TEST( SrvShimRulings, TheSdkListClearIsGoneAndTheWorldReleaseIsTheWholeOfTheClear )
{
	// READ THE ASSERTIONS, NOT THE NAME. This was
	// TheSdkListClearIsInsideTheGuard: `gEntList.Clear()` sat two lines below
	// the world release and had to be on the OTHER side of
	// `#ifndef SS_CBASEENTITY_ALIAS`, because entitylist.cpp is a removal-set
	// row and armed there is no gEntList to clear.
	//
	// ARMED IS ALL THERE IS NOW. The guard went with the other 453 dead arms
	// and the call inside it went with it -- `nm -C server.so` finds gEntList
	// neither defined NOR undefined, so there is no list and no reference to
	// one. The ruling this fact protects is UNCHANGED in substance and its
	// subject has become an absence: what must stay is that the release keeps
	// standing alone, which is the half a mechanical "guard the whole body"
	// edit got wrong and a mechanical "delete the whole body" edit would too.
	const std::vector< std::string > lines = CodeLines( kLevelSteps );
	ASSERT_FALSE( lines.empty() );

	EXPECT_EQ( -1, FirstLineWith( lines, "gEntList." ) )
		<< kLevelSteps << " reads gEntList again. There is no such object in "
		   "server.so -- nm finds it neither defined nor undefined -- so this "
		   "is an undefined reference, or a second entity list.";

	// THE LIVE SUCCESSOR, in the same function: the world release, which is now
	// the whole of what LevelClearEntityList does besides recording the step.
	// Asserting only the absence would pass over a function somebody emptied.
	// THE STEP IS A VTABLE METHOD NOW, not a free function. plan_shim.md 5 3a
	// (lane 3b-enginebind) folded shell_levelshim.cpp into shell_levelsteps.cpp
	// as shell_level.cpp, so the needle is the method the step became rather
	// than the SourceSharp_Level...() it used to forward from.
	const int nFunc = FirstLineWith( lines, "virtual void ClearEntityList() OVERRIDE" );
	ASSERT_GE( nFunc, 0 ) << "IShimLevel::ClearEntityList is gone from " << kLevelSteps;

	const int nRelease = FirstLineWith( lines, "pWorld->GetNetworkable()->Release()", nFunc );
	EXPECT_GE( nRelease, 0 )
		<< "the world release is no longer in SourceSharp_LevelClearEntityList. "
		   "world_release_test.cpp says what that costs: the SECOND `map` "
		   "command faults inside the engine on its first trace.";
	EXPECT_GE( FirstLineWith( lines, "if ( CWorld *pWorld = GetWorldEntity() )", nFunc ), 0 )
		<< "the world is no longer looked up through GetWorldEntity() before "
		   "being released.";
}

TEST( SrvShimRulings, TheSdkActivateWalkIsGoneAndTheStepSurvivesItAsARecordedNoOp )
{
	// READ THE ASSERTIONS, NOT THE NAME. This was
	// TheActivateWalkIsInsideTheGuard: `for ( CBaseEntity *pClass =
	// gEntList.FirstEnt(); ... gEntList.NextEnt( pClass ) )` had to be inside
	// `#ifndef SS_CBASEENTITY_ALIAS`, because it answers only for the SDK
	// hierarchy -- plan_entity.md §3.3, the slot table "cannot iterate, has no
	// listeners, and knows no names" -- and its managed counterpart,
	// _spawner.ActivateAll(), already runs beside it in
	// ServerGameDll.ServerActivate.
	//
	// The guarded arm is deleted with the other 453, so the walk is gone and
	// _spawner.ActivateAll() is the whole activation pass, which is what the
	// armed build always ran. WHAT MUST NOT HAPPEN NOW is the step itself
	// quietly disappearing: an empty body still has to be CALLED and still has
	// to record that it ran, or the managed side's pairing loses its anchor and
	// the absence of an activation pass looks like an activation pass that did
	// nothing.
	const std::vector< std::string > lines = CodeLines( kLevelSteps );
	ASSERT_FALSE( lines.empty() );

	EXPECT_EQ( -1, FirstLineWith( lines, "gEntList.NextEnt(" ) )
		<< "the SDK activate walk is back in " << kLevelSteps << ". It answers "
		   "only for a hierarchy this build has no entities on, and gEntList is "
		   "not in server.so at all.";
	EXPECT_EQ( -1, FirstLineWith( lines, "gEntList.FirstEnt(" ) );
	// The two DEBUG flags' `extern` DECLARATIONS survive this walk's deletion
	// and are deliberately not asserted away here: they are defined in
	// shell_engineboundary.cpp and CBaseEntity::Activate is what sets the second,
	// so whether they still have a reader is that file's question, not this
	// ruling's. What must not come back is the walk's own BRACKETING -- an
	// assignment and an AssertMsg with no loop between them assert about an
	// entity nothing activated.
	EXPECT_EQ( -1, FirstLineWith( lines, "g_bCheckForChainedActivate = " ) )
		<< "the walk's DEBUG chained-Activate bracketing is back without the "
		   "walk, so it brackets nothing.";
	EXPECT_EQ( -1, FirstLineWith( lines, "AssertMsg( g_bReceivedChainedActivate" ) )
		<< "the walk's chained-Activate assertion is back without the walk.";

	// LIVE: the step is still a SLOT -- and this is the half that changed, so
	// read the change before the needle.
	//
	// THE SLOT IS RETIRED AND IS NO LONGER CALLED (plan_shim.md 5 3a, lane
	// 3b-enginebind, a row in docs/abi-retirements.md). This fact's old
	// message said "the managed activation pass is paired with this call;
	// without it there is nothing for ServerGameDll.ServerActivate to pair
	// with" -- and that pairing is exactly what went: every line of stock's
	// walk reached the evicted hierarchy, so the native half was empty and
	// _spawner.ActivateAll() is the WHOLE activation pass rather than one side
	// of a pair.
	//
	// AND THE SLOT ITSELF LEFT in the serialised ABI pass (2026-10-20), which
	// renumbered IShimLevel once with tools/engine-forwarders --reorder: what is
	// pinned now is that the method is GONE, with a live step found by the same
	// read as the control.
	EXPECT_EQ( -1, FirstLineWith( lines, "ActivateAllEntities" ) )
		<< "IShimLevel::ActivateAllEntities is back in " << kLevelSteps;
	ASSERT_GE( FirstLineWith( lines, "virtual void SetPrecacheAllowed( int32_t bAllowed ) OVERRIDE" ), 0 )
		<< "the control: a live level step is not found in " << kLevelSteps;

	// THE SECOND HALF OF THIS FACT IS RETIRED. It read
	// `ShellGameDll_RecordStep( "LevelActivateAllEntities" )` and said "the
	// activation step no longer records that it ran, so a step that stopped
	// being called would look exactly like this one" -- which was true of the
	// step-order ring, and the ring is gone. It recorded into a buffer whose
	// only reader was to be `tools/gate5`, and neither that tool nor the
	// docs/gate5/* baselines it was to diff against was ever written; the
	// Makefile records gate5's removal in b6c429e1. So the recording half was
	// never evidence anybody could read, and the assertion was pinning an
	// instrument rather than the step.
	//
	// WHAT ANSWERS THE SAME QUESTION NOW is managed: _spawner.ActivateAll() is
	// the whole activation pass, in ServerGameDll.ServerActivate.
}

} // namespace
