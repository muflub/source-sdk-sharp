//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Purpose: SourceSharp — the two DispatchParticleEffect overloads that outlive
//          the entity component, and the §6 ruling that sent them to the wrong
//          place.
// plan_entity.md §6's "17 that LEAVE" table rules `particle_parse.cpp` out and
// names «`SourceSharp_ParticleSystemStart` / `Stop` / `FollowPoints`, already in
// the shim» as what does the thing afterwards. Lane `c8-flip` carried that
// forward as its client family 6 and called it "RE-POINT, cheapest family".
// IT IS NOT A RE-POINT, and this file is the measurement that says so rather
// than a paragraph asserting it. The shim trio takes a MANAGED ENTITY HANDLE and
// attaches ONE long-lived, followed particle system to that entity. The three
// §6-STAYS emitters that owe these two overloads want a ONE-SHOT effect at a
// world point, and two of the three pass no entity at all. So the ruling is
// RE-HOME: both bodies moved verbatim into
// sourcesharp/game/shared/particle_manifest.cpp, which already holds
// particle_parse.cpp's staying half and is already in BOTH targets.
// WHAT EACH TEST BELOW IS ACTUALLY ABOUT, since "the file compiles" is not it:
//     (the flip is a link question, and a duplicate is as fatal as a miss);
//     blood, slime splashes and tracers passes every link gate there is;
//     the `else` arm inside them that CLEARS the entity field -- losing that arm
//     is invisible unarmed and wrong armed;
//     reading §6 finds the refutation next to the code instead of repeating it.

#include <gtest/gtest.h>
#include "cmake_graph.h"

#include <dirent.h>

#include <algorithm>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// buildcpptests runs each binary with src/ as the working directory. A list
// rather than one path so that running the binary by hand from the repo root
// works too.
//-----------------------------------------------------------------------------
const char *RepoSrc()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		DIR *pDir = opendir( ( std::string( kCandidates[ i ] ) + "/sourcesharp" ).c_str() );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

//-----------------------------------------------------------------------------
// A file's lines with whole-line comments dropped. Every subject of this file
// is named repeatedly in the PROSE of the files it scans -- this test's own
// banner does it, and so do both moved banners -- and a check a comment can
// satisfy is a check about comments.
//-----------------------------------------------------------------------------
std::vector< std::string > CodeLines( const char *pszRelative )
{
	std::vector< std::string > out;

	const char *pszRoot = RepoSrc();
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
			continue;

		out.push_back( line );
	}

	return out;
}

// Every line of code in a file that contains pszNeedle, with its 1-based number.
struct Hit_t
{
	int         nLine;
	std::string strText;
};

std::vector< Hit_t > LinesNaming( const char *pszRelative, const char *pszNeedle )
{
	std::vector< Hit_t > out;
	const std::vector< std::string > lines = CodeLines( pszRelative );

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[ i ].find( pszNeedle ) == std::string::npos )
			continue;

		Hit_t hit;
		hit.nLine   = (int)i + 1;
		hit.strText = lines[ i ];
		out.push_back( hit );
	}

	return out;
}

std::string Joined( const std::vector< Hit_t > &hits )
{
	std::ostringstream out;
	for ( size_t i = 0; i < hits.size(); i++ )
		out << "\n    " << hits[ i ].nLine << ": " << hits[ i ].strText;
	return out.str();
}

//-----------------------------------------------------------------------------
// The body of a function, from the line that opens its definition to the brace
// that closes it, with whitespace squeezed out so the assertions below are
// about statements rather than about indentation.
//-----------------------------------------------------------------------------
std::string BodySqueezed( const char *pszRelative, const char *pszSignature )
{
	const std::vector< std::string > lines = CodeLines( pszRelative );

	size_t iStart = lines.size();
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[ i ].find( pszSignature ) != std::string::npos )
		{
			iStart = i;
			break;
		}
	}

	if ( iStart == lines.size() )
		return std::string();

	std::string out;
	int  nDepth  = 0;
	bool bOpened = false;

	for ( size_t i = iStart; i < lines.size(); i++ )
	{
		for ( size_t j = 0; j < lines[ i ].size(); j++ )
		{
			const char c = lines[ i ][ j ];

			if ( c == '{' ) { nDepth++; bOpened = true; }
			else if ( c == '}' ) nDepth--;
			else if ( bOpened && c != ' ' && c != '\t' && c != '\r' ) out += c;
		}

		if ( bOpened && nDepth == 0 )
			break;
	}

	return out;
}

const char *kShim      = "sourcesharp/game/shared/particle_manifest.cpp";
const char *kShimStart = "sourcesharp/game/client/shell_managed_particles.cpp";
// fx_blood.cpp's BloodImpactCallback is C# now (C5, lane c5-particles:
// BloodEffects.BloodImpact), starting its system through the one-shot slot.
const char *kBlood     = "sourcesharp/managed/SourceSharp.Client/Effects/BloodEffects.cs";
// fx_water.cpp left with lane c5-particles: its slime splash is WaterSplashes.GunshotSlimeSplash (C#).
const char *kWater     = "sourcesharp/managed/SourceSharp.Client/Effects/WaterSplashes.cs";
// fx_tracer.cpp's ParticleTracerCallback is C# now (C2, lane c2-fx); the
// dispatch it decides on is carried out by the effect list's executor.
const char *kTracer    = "sourcesharp/game/client/shell_effects_list_client.cpp";

// The two signatures, spelled once.
const char *kIntOverload =
	"void DispatchParticleEffect( int iEffectIndex, Vector vecOrigin, Vector vecStart, QAngle vecAngles, IGameEntity *pEntity )";
const char *kNameOverload =
	"void DispatchParticleEffect( const char *pszParticleName, Vector vecOrigin, QAngle vecAngles, IGameEntity *pEntity )";

//=============================================================================//
// THE CHECK THAT WOULD OTHERWISE NOT BE ABLE TO FAIL.
//
// Four of the tests below assert that something is ABSENT, and a file that could
// not be read is absent in exactly the same way. A rename, a move, or an
// unexpected working directory would turn the lot green and mean nothing.
//=============================================================================//
TEST( ParticleDispatchRehome, TheFilesTheScanIsAboutWereActuallyRead )
{
	ASSERT_TRUE( RepoSrc() != NULL )
		<< "no src/sourcesharp/ from here, so every scan below is vacuous";

	ASSERT_FALSE( CodeLines( kShim ).empty() )      << kShim;
	ASSERT_FALSE( CodeLines( kShimStart ).empty() ) << kShimStart;
	ASSERT_FALSE( CodeLines( kBlood ).empty() )     << kBlood;
	ASSERT_FALSE( CodeLines( kWater ).empty() )     << kWater;
	ASSERT_FALSE( CodeLines( kTracer ).empty() )    << kTracer;
	// The two target source lists are read through cmake_graph.h, which throws
	// on a missing, empty or stale input -- no read guard of ours is needed.
}

//=============================================================================//
// PART ONE — the re-home itself. A link question, so a duplicate is as fatal as
// a miss and both directions are asserted.
//=============================================================================//

TEST( ParticleDispatchRehome, BothOverloadsAreDefinedInTheShim )
{
	const std::vector< Hit_t > ints = LinesNaming( kShim, kIntOverload );
	const std::vector< Hit_t > names = LinesNaming( kShim, kNameOverload );

	EXPECT_EQ( (size_t)1, ints.size() )
		<< "the index-and-endpoints overload is not defined exactly once in "
		<< kShim << ":" << Joined( ints );

	EXPECT_EQ( (size_t)1, names.size() )
		<< "the name-and-origin overload is not defined exactly once in "
		<< kShim << ":" << Joined( names );
}

//-----------------------------------------------------------------------------
// THE CAPABILITY, not the link. COMMON.md: "a compile gate selects for
// deletion" -- `#ifndef SOURCESHARP`-ing these three call sites out would clear
// the same three owed pairs and would silently stop the game drawing blood,
// slime splashes and tracers.
//-----------------------------------------------------------------------------
TEST( ParticleDispatchRehome, TheThreeStayEmittersStillCallThem )
{
	EXPECT_FALSE( LinesNaming( kBlood, "oneShots.Start(" ).empty() )
		<< "BloodEffects.cs no longer starts a blood particle effect";

	// One call now: the port picks slime_splash_01/02/03 by size and starts it
	// once, where stock's three arms each dispatched their own.
	EXPECT_EQ( LinesNaming( kWater, "oneShots.Start(" ).size(), (size_t)1 )
		<< "WaterSplashes.cs no longer starts the slime splash's particle effect";

	EXPECT_FALSE( LinesNaming( kTracer, "DispatchParticleEffect(" ).empty() )
		<< "shell_effects_list_client.cpp no longer dispatches the tracer particle effect";
}

//-----------------------------------------------------------------------------
// The bodies still do the thing. A moved body that was hollowed out on the way
// satisfies every test above.
//-----------------------------------------------------------------------------
TEST( ParticleDispatchRehome, TheMovedIndexBodyStillFillsAndDispatches )
{
	const std::string body = BodySqueezed( kShim, kIntOverload );

	ASSERT_FALSE( body.empty() ) << "the index overload's body was not found";

	EXPECT_TRUE( body.find( "data.m_nHitBox=iEffectIndex;" ) != std::string::npos )
		<< "the effect index is no longer written into the wire struct. Body:\n" << body;
	EXPECT_TRUE( body.find( "data.m_vOrigin=vecOrigin;" ) != std::string::npos )
		<< "the origin is no longer written. Body:\n" << body;
	EXPECT_TRUE( body.find( "data.m_vStart=vecStart;" ) != std::string::npos )
		<< "the start point is no longer written -- this is the field the "
		   "tracer's whole shape comes from. Body:\n" << body;
	EXPECT_TRUE( body.find( "data.m_vAngles=vecAngles;" ) != std::string::npos )
		<< "the angles are no longer written. Body:\n" << body;
	EXPECT_TRUE( body.find( "DispatchEffect(\"ParticleEffect\",data);" ) != std::string::npos )
		<< "the body no longer reaches the client-effect registry, which is the "
		   "§6 STAY (c_te_effect_dispatch.cpp) this re-home rests on. Body:\n" << body;
}

TEST( ParticleDispatchRehome, TheMovedNameBodyStillResolvesTheNameAndDelegates )
{
	const std::string body = BodySqueezed( kShim, kNameOverload );

	ASSERT_FALSE( body.empty() ) << "the name overload's body was not found";

	EXPECT_TRUE( body.find( "GetParticleSystemIndex(pszParticleName)" ) != std::string::npos )
		<< "the name is no longer turned into an index. That lookup is "
		   "particles/particles.h's, not particle_parse.cpp's, which is why "
		   "this overload could move at all. Body:\n" << body;
	EXPECT_TRUE( body.find( "DispatchParticleEffect(iIndex,vecOrigin,vecOrigin,vecAngles,pEntity)" )
		!= std::string::npos )
		<< "the name overload no longer delegates to the index one -- note it "
		   "passes vecOrigin TWICE, so a one-point effect has start == origin. "
		   "Body:\n" << body;
}

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. There is no guard left to be outside of.
//
// This fact used to pin WHERE the index overload closed its
// `#ifndef SS_CBASEENTITY_ALIAS` -- on the `else`, so that the entity-clearing
// arm stayed outside it. That guard was one of the 453 arms the compiler never
// saw (CBaseEntity WAS IGameEntity, so SS_CBASEENTITY_ALIAS was always
// defined), and it has been deleted along with the entity arm it wrapped.
//
// THE CLAIM IS UNCHANGED AND IS NOW UNCONDITIONAL: the entity field is CLEARED
// before the dispatch, on BOTH sides, and is never left at whatever CEffectData
// was constructed with. What used to make that true was "the guard closes on
// the `else`"; what makes it true now is "there is no guard and the clear is
// the only arm". Both halves are asserted -- the absence of the guard, and the
// clear-then-dispatch that the guard existed to protect -- because either one
// alone passes over a body that lost the other.
//-----------------------------------------------------------------------------
TEST( ParticleDispatchRehome, BothMovedBodiesClearTheEntityFieldOutsideTheGuard )
{
	const char *kBodies[] = { kIntOverload, kNameOverload };

	for ( size_t i = 0; i < 2; i++ )
	{
		const std::string body = BodySqueezed( kShim, kBodies[ i ] );

		ASSERT_FALSE( body.empty() ) << kBodies[ i ];

		if ( i == 1 )
		{
			// The name overload delegates; it has no guard of its own, and
			// growing one would mean it had stopped delegating.
			EXPECT_TRUE( body.find( "#ifndefSS_CBASEENTITY_ALIAS" ) == std::string::npos )
				<< "the name overload has grown an entity guard, so it is no "
				   "longer the three-line delegation that moved. Body:\n" << body;
			continue;
		}

		// Absence half: the dead guard, and the entity arm it wrapped, are
		// gone from the index overload too -- not just from the delegating one.
		EXPECT_TRUE( body.find( "#ifndefSS_CBASEENTITY_ALIAS" ) == std::string::npos )
			<< "the index overload has grown an SS_CBASEENTITY_ALIAS guard "
			   "back. That macro is always defined; an arm under it is source "
			   "the compiler never sees and a fact anchored in it cannot fail. "
			   "Body:\n" << body;
		EXPECT_TRUE( body.find( "PATTACH_CUSTOMORIGIN" ) == std::string::npos )
			<< "the entity arm is back in the index overload. It read an "
			   "entity this side of the port does not have. Body:\n" << body;

		// Live half, and the whole point of the fact: the entity field is
		// cleared on BOTH sides and the clear is the statement immediately
		// before the dispatch, so nothing can slip between them.
		//
		// BodySqueezed eats the braces (they drive its depth counter), so the
		// shape below is the per-side clear followed directly by the dispatch.
		EXPECT_TRUE( body.find( "#ifdefCLIENT_DLLdata.m_hEntity=NULL;#elsedata.m_nEntIndex=0;#endif"
		                        "DispatchEffect(\"ParticleEffect\",data);" )
			!= std::string::npos )
			<< "the index overload no longer clears the entity field "
			   "immediately before dispatching -- m_hEntity on the client, "
			   "m_nEntIndex on the server. Either the clear went or something "
			   "was inserted between it and the dispatch. Body:\n" << body;
	}
}

//-----------------------------------------------------------------------------
// ON THE CLIENT'S LINK LINE ONLY. When this was written the server called the
// name-and-origin overload too (physics.cpp, item_world.cpp). Neither does now:
// item_world.cpp is not built and physics.cpp names neither overload, so nm over
// every server_sourcesharp object finds no reference to DispatchParticleEffect,
// and particle_manifest.cpp left the server's list (P5 lane SP). Re-adding it
// would put a Valve-header TU back into server.so for two dead functions.
//-----------------------------------------------------------------------------
TEST( ParticleDispatchRehome, TheShimFileIsOnTheClientLinkLine )
{
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kShim ) )
		<< "particle_manifest.cpp is not compiled into client_sourcesharp "
		   "(src/client/native/CMakeLists.txt)";
}

TEST( ParticleDispatchRehome, TheShimFileIsNotOnTheServerLinkLine )
{
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kShim ) )
		<< "particle_manifest.cpp is compiled into server_sourcesharp again "
		   "(src/server/native/CMakeLists.txt); no server object calls "
		   "either DispatchParticleEffect overload";
}

//=============================================================================//
// PART TWO — why §6's ruling could not be carried out, pinned next to the code
// so the next lane reading that table finds the refutation rather than
// repeating the attempt.
//=============================================================================//

//-----------------------------------------------------------------------------
// The shim entry point §6 named takes a HANDLE AND A NAME. It has no origin
// parameter, no angle parameter and no start parameter, so there is no spelling
// of a re-point that preserves what the three emitters pass.
//-----------------------------------------------------------------------------
TEST( ParticleRepointWasImpossible, TheNamedShimEntryPointTakesAHandleAndAName )
{
	const std::vector< Hit_t > decl =
		LinesNaming( kShimStart, "int32_t SourceSharp_ParticleSystemStart( uint32_t hOwner, const char *pszName )" );

	ASSERT_EQ( (size_t)1, decl.size() )
		<< "SourceSharp_ParticleSystemStart's definition is not where §6's "
		   "ruling was checked against it, so this refutation is stale:"
		<< Joined( decl );

	const std::string body =
		BodySqueezed( kShimStart, "int32_t SourceSharp_ParticleSystemStart( uint32_t hOwner, const char *pszName )" );

	ASSERT_FALSE( body.empty() );

	// It RESOLVES A MANAGED ENTITY and refuses without one. That is the whole
	// distance between it and a one-shot effect at a world point.
	EXPECT_TRUE( body.find( "SourceSharp_StandaloneFromHandle(hOwner)" ) != std::string::npos )
		<< "SourceSharp_ParticleSystemStart no longer resolves an entity from "
		   "the handle, which would change §6's ruling. Body:\n" << body;
	EXPECT_TRUE( body.find( "if(!pOwner)return0;" ) != std::string::npos )
		<< "SourceSharp_ParticleSystemStart no longer refuses when there is no "
		   "owner entity. Body:\n" << body;
}

//-----------------------------------------------------------------------------
// And the other half: two of the three call sites have no entity to hand it.
// `DispatchParticleEffect( name, origin, angles )` -- three arguments, and the
// fourth defaults to NULL in particle_parse.h.
//-----------------------------------------------------------------------------
TEST( ParticleRepointWasImpossible, TwoOfTheThreeEmittersPassNoEntityAtAll )
{
	// The blood half is C# now (BloodEffects.BloodImpact): it starts a
	// one-shot at the impact point, with no entity, which is this test's
	// reading carried over. Its call names no owner.
	const std::vector< Hit_t > blood = LinesNaming( kBlood, "oneShots.Start(" );
	const std::vector< Hit_t > water = LinesNaming( kWater, "oneShots.Start(" );

	ASSERT_FALSE( blood.empty() );
	ASSERT_FALSE( water.empty() );

	for ( size_t i = 0; i < blood.size(); i++ )
	{
		EXPECT_EQ( std::string::npos, blood[ i ].strText.find( "Handle" ) )
			<< "BloodEffects.cs's one-shot now names an owner, so the "
			   "\"no owner to re-point onto\" reading is no longer this call:"
			<< Joined( blood );
	}

	// The slime splash names no owner either: a name, the origin, the control
	// points and the three axes -- no handle among them.
	for ( size_t i = 0; i < water.size(); i++ )
	{
		EXPECT_EQ( std::string::npos, water[ i ].strText.find( "Handle" ) )
			<< "WaterSplashes.cs's slime splash now names an owner:"
			<< Joined( water );
	}
}

} // namespace
