//========= SourceSharp — the ground a trace found, and who is allowed to =====//
//              throw it away
//
// THE NULL-SUBJECT BUG CLASS, ground edition. `TraceEntity( tr )` is the
// CBaseEntity half of the paired lookup in game/shared/util_shared.h: it
// answers NULL for anything that is not a CBaseEntity, and in this build the
// parallel (standalone / managed) hierarchy is most of what an entity can land
// on. So
//
//     SetGroundEntity( TraceEntity( trace ) );
//
// means "an SDK entity that settles onto a managed floor records NO ground".
//
// AND IT IS WORSE THAN "records no ground", which is what the note at
// game/server/util.cpp:340-378 says. CBaseEntity::CanStandOn is
//
//     virtual bool CanStandOn( CBaseEntity *pSurface ) const
//         { return (pSurface && !pSurface->IsStandable()) ? false : true; }
//                                                    (game/server/baseentity.h:1122)
//
// so CanStandOn( NULL ) returns TRUE. At physics_main.cpp:1110 the guard is
// `if ( CanStandOn( TraceEntity( trace ) ) )`, and a NULL from a managed floor
// does not skip that block -- it ENTERS it. The code affirmatively decides the
// entity can stand there and then calls SetGroundEntity( NULL ), whose tail
// runs `RemoveFlag( FL_ONGROUND )`. The entity is marked airborne while
// standing on something, which is what then costs it friction, fall damage and
// the base velocity a moving floor hands its rider.
//
// THE INVENTORY OF LIVE SITES IS RETIRED. It pinned two
// `SetGroundEntity( TraceEntity( ... ) )` sites in game/server/physics_main.cpp,
// a stock file neither client.so nor server.so compiles; with the farm no
// longer carrying unused stock files there is nothing left for it to count.
//
// WHAT THE WIDENING ACTUALLY NEEDS -- three blockers, and the recorded one is
// wrong about which:
//
//   1. NOT storage. game/server/util.cpp:359-361 says "there is nowhere on a
//      CBaseEntity to STORE a standalone ground". There is:
//      CNetworkHandleBase stores a CBaseHandle (public/networkvar.h:593), which
//      is hierarchy-agnostic and already holds any entity in the table. The
//      store is not what loses the information -- TraceEntity() loses it before
//      the handle is ever reached.
//
//   2. The READ BACK. CNetworkHandleBase::DerefHandle() routes through
//      CHandle<Type>::Get() (networkvar.h:656-663). Phase 13 step 1 specialised
//      CHandle<IGameEntity>::Get() to ask (baseentity.h:3021), NOT
//      CHandle<CBaseEntity>::Get(), so the ground handle's reads are still the
//      generic reinterpret. THAT is what would hand a garbage pointer to every
//      GetGroundEntity() caller -- the hazard player.h:283 describes, correctly,
//      and attributes to the wrong half of the operation.
//
//   3. The BOOKKEEPING, which nobody recorded at all. Even with 1 and 2 solved,
//      SetGroundEntity's tail is CBaseEntity-only: ground->AddEntityToGroundList,
//      PhysicsNotifyOtherOfGroundRemoval, EndGroundContact, and the VPhysics
//      held-object release that reads ground->GetMoveType() and
//      ground->VPhysicsGetObject(). IGameEntity has exactly six pure virtuals
//      and may not grow more, so a standalone ground needs a RELAY, the way
//      game/shared/movement_ground.h answered the same question for the
//      movement loop rather than putting it on the interface.
//
// A SOURCE SCANNER, like soundemitter_lift_test and physicslink_lift_test, and
// for the same reason: the claim is about the text of files that need a whole
// engine to run.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <fstream>
#include <string>
#include <vector>

namespace
{

const char *SourceRoot()
{
	static const char *kCandidates[] = { "game", "src/game", "../src/game" };
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

std::string ReadGameFile( const char *pszRelative )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += "/";
	strPath += pszRelative;

	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

//-----------------------------------------------------------------------------
// Plain occurrence count, used where a needle is a SUFFIX of its own live
// successor and a bare `find(...) == npos` could therefore never pass. Equal
// counts is the shape that survives that: every occurrence of the short
// spelling is part of the long one.
//-----------------------------------------------------------------------------
int CountAll( const std::string &strHay, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t at = strHay.find( strNeedle ); at != std::string::npos;
	      at = strHay.find( strNeedle, at + 1 ) )
	{
		n++;
	}
	return n;
}

} // namespace

//=============================================================================

TEST( GroundTraceSubject, CanStandOnStillReturnsTrueForNull )
{
	// READ THE ASSERTIONS, NOT THE NAME. The name is kept because the lane
	// ledger keys on it; what it asserts is now that the line it was named for
	// IS GONE, together with the thing that made the bug reachable.
	//
	// WHAT THIS USED TO ASSERT, and it was the load-bearing half of the banner
	// at the top of this file: CBaseEntity::CanStandOn was
	//
	//     virtual bool CanStandOn( CBaseEntity *pSurface ) const
	//         { return (pSurface && !pSurface->IsStandable()) ? false : true; }
	//
	// so CanStandOn( NULL ) returned TRUE, and physics_main.cpp's guard ENTERED
	// its block for a managed floor instead of skipping it.
	//
	// WHY IT WENT RED. Lane `the-close` retired the alias: `CBaseEntity` is not
	// a name any more (0bc727ed9) and the whole inline block that declaration
	// lived in went with it. game/server/baseentity.h was 632 lines then and
	// declared no such class; it is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6, which is the stronger
	// form of "CanStandOn is gone from it".
	//
	// THE SUCCESSOR PROPERTY, IN TWO HALVES, BECAUSE THE ABSENCE ALONE WOULD
	// ALSO BE TRUE OF AN EMPTY HEADER:
	//
	//   1. the declaration is gone, AND SO IS THE FREE FUNCTION THAT SUPPLIED
	//      ITS NULL. `TraceEntity` was deleted (d3a0bfa3a) after being proved a
	//      constant NULL -- it bottomed out in GetBaseEntity(), and all eight
	//      implementations of that in the two DLLs return NULL. So the argument
	//      the severity claim was about no longer has a producer;
	//
	//   2. the route that IS compiled resolves the ground the way this file's
	//      sibling fact has always said it should -- through the trace's
	//      IHandleEntity. game/shared/movement_ground.h's
	//      MovementGroundFromTrace asks SourceSharp_IsTheWorld and then
	//      TraceGameEntity, which answers on BOTH sides, and returns the handle
	//      rather than NULL for a managed floor.
	//
	// A sibling the farm still links proves the reader looks in the right
	// directory, so the absence after it is not a reader that sees nothing.
	ASSERT_FALSE( ReadGameFile( "shared/shareddefs.h" ).empty() )
		<< "game/shared/shareddefs.h could not be read; the absence below would "
		   "pass vacuously";

	EXPECT_TRUE( ReadGameFile( "server/baseentity.h" ).empty() )
		<< "game/server/baseentity.h is readable from the farm again. It is the "
		   "header that declared CanStandOn; if the NULL arm came back with it, "
		   "site 1110 ENTERS its block for a managed floor once more and the "
		   "banner at the top of this file is live again rather than historical "
		   "-- re-derive the severity before trusting either note.";

	// THE PRODUCER OF THE NULL, ALSO GONE. Both spellings are asserted rather
	// than just the absence: the deleted name and the live successor differ by
	// an INFIX (`TraceEntity` / `TraceGameEntity`), so neither contains the
	// other and a lone absence check would be satisfied by a tree in which the
	// whole paired-lookup family had been deleted and nothing put in its place.
	const std::string strShared = ReadGameFile( "shared/util_shared.h" );
	ASSERT_FALSE( strShared.empty() )
		<< "game/shared/util_shared.h could not be read";

	EXPECT_EQ( 0, CountAll( strShared, "TraceEntity( const trace_t" ) )
		<< "util_shared.h defines TraceEntity over a trace again. It answered a "
		   "constant NULL for every entity in the game -- it bottomed out in "
		   "GetBaseEntity(), and all eight implementations of that in the two "
		   "DLLs return NULL -- which is the whole of this file's subject.";

	EXPECT_EQ( 1, CountAll( strShared, "TraceGameEntity( const trace_t" ) )
		<< "the live successor is not defined exactly once. Without it nothing "
		   "resolves what a trace hit on either hierarchy, and the absence "
		   "above is a deletion rather than a replacement.";

	// AND THE LIVE SUCCESSOR, in the one file on this route the build compiles.
	const std::string strGround = ReadGameFile( "../sourcesharp/game/shared/movement_ground.h" );
	ASSERT_FALSE( strGround.empty() )
		<< "game/shared/movement_ground.h could not be read";

	EXPECT_NE( std::string::npos, strGround.find( "if ( TraceGameEntity( tr ) != NULL )" ) )
		<< "MovementGroundFromTrace no longer resolves the ground through "
		   "TraceGameEntity. That is the arm that answers for the parallel "
		   "hierarchy on BOTH sides; without it a player standing on a managed "
		   "entity resolves to no ground again, which is this file's bug by "
		   "another route.";

	EXPECT_NE( std::string::npos, strGround.find( "return ss::TraceHit( tr );" ) )
		<< "MovementGroundFromTrace no longer returns the trace's own "
		   "IHandleEntity. Returning anything narrower is how the ground was "
		   "lost in the first place.";
}
