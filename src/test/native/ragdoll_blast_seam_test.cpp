//====== SourceSharp — behaviour ledger row C1's SEAM, from the native side ===//
//
// The blast push on ragdolls is C# (ClientTeExplosion.AffectRagdolls), and the
// eleven [Fact]s in TempEntParticleClientTests hold the BEHAVIOUR: the falloff,
// the cut, the mask, the wall, the impulse to the unit. What is here is the
// four ways this seam can be wrong that no managed test can see, because every
// one of them is on the far side of a slot that a managed test replaces with a
// double.
//
//   (1) A TRACE MASK NAMED ON ONE ARM ONLY. sourcesharp.h says it itself, twice:
//       "a value the server's arm did not know would have been read there as
//       the default (brushes only) and answered a plausible wrong number in
//       silence". Nothing guarded that until this file. A managed test cannot
//       see it at all -- TraceMask.ShotHull crosses as the integer 4 whatever
//       the arm on the other side does with it.
//
//   (2) THE WRONG PHYSICS OBJECT. C_ClientRagdoll::ImpactTrace pushes the object
//       VPhysicsGetObject answers with, which for a ragdoll is ELEMENT ZERO
//       (CRagdoll::Init hands VPhysicsSetObject m_ragdoll.list[0].pObject,
//       ragdoll.cpp:120). Pushing every element instead, or element N, is a body
//       that flies apart rather than one that is thrown -- and from C# it is one
//       bool coming back either way.
//
//   (3) THE CREATION TICK STAMPED IN THE WRONG PLACE. BecomeRagdoll has seven
//       early returns above the point the body exists. A stamp above them marks
//       an entity that never became a ragdoll as one made this tick, which makes
//       the managed path skip it -- and it skips it FOREVER only on the tick it
//       failed, so the symptom is one blast in sixty-six doing nothing. Stock
//       stamps after the body exists (c_baseanimating.cpp:359 and :4948) and so
//       does this.
//
//   (4) CRagdollExplosionEnumerator BEING TIDIED AWAY. It has no caller now that
//       the push is managed, and it is the only copy of this arithmetic in the
//       tree that this port did not write: TheImpulseNumbersAreStocksOwnReadOffDisk
//       reads the falloff, the cut and the 32 out of it and compares them with
//       the C#. Take it out -- move it inside the SOURCESHARP guard, delete the
//       file, or "port" it into the shim -- and that managed [Fact] is either
//       reading nothing or reading this port's own restatement of its own
//       arithmetic. Still green, and no longer a comparison.
//
//       PHASE 7 STEP 7b CHANGED WHAT KEEPS IT HERE, and the change is worth
//       being exact about because it is the kind that quietly empties a check.
//       c_te_explosion.cpp is no longer COMPILED: 7b took it out of
//       client_csharp.vpc, because the only body in it this build reached was
//       CExplosionOverlay::Update, which moved to the shim. So "still compiled"
//       is no longer the reason the enumerator is safe -- being ON DISK, outside
//       the guard, and not duplicated anywhere is. The test below asserts that
//       state directly rather than the one it used to.
//
// Same standard as tempent_pool_test.cpp and tempent_spawn_seam_test.cpp: this
// reads the tree off disk, because a seam is a claim about two files agreeing.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <cctype>
#include <fstream>
#include <set>
#include <string>
#include <vector>
#include <sys/stat.h>
#include <iterator>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "..", "../src", "src" };
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

// Comments OUT, and this is not tidiness -- it is the difference between this
// file working and this file being one of the forty-four checks in this tree
// that could not fail.
//
// The first mutation run against EveryTraceMaskIsMappedOnBothArms deleted the
// server arm's SOURCESHARP_TRACE_SHOT_HULL branch and the test STAYED GREEN,
// because the paragraph explaining the branch names the mask two lines above
// it. A search over raw source finds a mask that is only ever MENTIONED. Every
// read below is stripped.
std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;

	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false; i++;
			}
			continue;
		}

		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}

		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}

		out += strIn[ i ];
	}

	return out;
}

std::string ReadRaw( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	const std::string strPath = std::string( pszRoot ) + "/" + strRelative;
	std::ifstream file( strPath.c_str(), std::ios::binary );

	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

// The default read: source with every comment removed. See StripComments.
std::string Read( const std::string &strRelative )
{
	const std::string all = ReadRaw( strRelative );
	return all.empty() ? all : StripComments( all );
}

// Every SOURCESHARP_TRACE_* name declared in the SourceSharpTraceMask enum, in
// declaration order.
//
// PARSED RATHER THAN LISTED, which is the whole point of the first test below.
// A hand-kept list here would have to be edited by the same lane that adds a
// mask, in the same commit, and would therefore agree with a lane that added
// one to the enum and one arm -- which is the failure this file exists for.
std::vector<std::string> DeclaredTraceMasks( const std::string &strHeader )
{
	std::vector<std::string> names;

	const size_t begin = strHeader.find( "SOURCESHARP_TRACE_BRUSHES = 0" );
	const size_t end = strHeader.find( "} SourceSharpTraceMask;" );

	if ( begin == std::string::npos || end == std::string::npos || end < begin )
		return names;

	const std::string body = strHeader.substr( begin, end - begin );
	const std::string needle = "SOURCESHARP_TRACE_";

	for ( size_t at = body.find( needle ); at != std::string::npos; at = body.find( needle, at + 1 ) )
	{
		// Only a DECLARATION -- the name followed by an '=' with nothing but
		// spaces between. The comments in that block name the values too, and a
		// mention is not a member.
		size_t stop = at;
		while ( stop < body.size()
			&& ( isalnum( (unsigned char)body[ stop ] ) || body[ stop ] == '_' ) )
		{
			stop++;
		}

		size_t after = stop;
		while ( after < body.size() && body[ after ] == ' ' )
			after++;

		if ( after >= body.size() || body[ after ] != '=' )
			continue;

		const std::string name = body.substr( at, stop - at );
		bool bSeen = false;

		for ( size_t i = 0; i < names.size(); i++ )
			bSeen = bSeen || names[ i ] == name;

		if ( !bSeen )
			names.push_back( name );
	}

	return names;
}

} // namespace

//-----------------------------------------------------------------------------
// (1) The mask vocabulary, on both arms.
//-----------------------------------------------------------------------------
TEST( RagdollBlastSeam, EveryTraceMaskIsMappedOnBothArms )
{
	const std::string header = Read( "sourcesharp/host/sourcesharp.h" );
	const std::string server = Read( "sourcesharp/game/server/shell_worldquery.cpp" );
	const std::string client = Read( "sourcesharp/game/client/shell_worldquery_client.cpp" );

	ASSERT_FALSE( header.empty() ) << "sourcesharp.h is on disk";
	ASSERT_FALSE( server.empty() ) << "the server's trace arm is on disk";
	ASSERT_FALSE( client.empty() ) << "the client's trace arm is on disk";

	const std::vector<std::string> masks = DeclaredTraceMasks( header );

	// The parse proving it parsed. Four existed before row C1 added a fifth, so
	// anything under five means the walk went wrong rather than that the
	// vocabulary shrank.
	ASSERT_GE( masks.size(), 5u )
		<< "the SourceSharpTraceMask enum parsed (" << masks.size() << " found)";

	for ( size_t i = 0; i < masks.size(); i++ )
	{
		// The default arm IS the brushes case on both sides -- neither writes
		// `if ( nMask == SOURCESHARP_TRACE_BRUSHES )`, they fall through to it --
		// so that one member is exempt and every other one is not.
		if ( masks[ i ] == "SOURCESHARP_TRACE_BRUSHES" )
			continue;

		EXPECT_NE( server.find( masks[ i ] ), std::string::npos )
			<< masks[ i ] << " is mapped on the SERVER's arm. sourcesharp.h says what "
			   "happens when it is not: the value falls through to the default, "
			   "brushes only, and answers a plausible wrong number in silence";

		EXPECT_NE( client.find( masks[ i ] ), std::string::npos )
			<< masks[ i ] << " is mapped on the CLIENT's arm, for the same reason";
	}
}

TEST( RagdollBlastSeam, TheBlastMaskIsShotHullAndNotShot )
{
	const std::string client = Read( "sourcesharp/game/client/shell_worldquery_client.cpp" );
	const std::string flags = Read( "public/bspflags.h" );

	ASSERT_FALSE( client.empty() ) << "the client's trace arm is on disk";
	ASSERT_FALSE( flags.empty() ) << "bspflags.h is on disk";

	EXPECT_NE( client.find( "nTraceMask = MASK_SHOT_HULL;" ), std::string::npos )
		<< "SOURCESHARP_TRACE_SHOT_HULL reaches MASK_SHOT_HULL and not MASK_SHOT";

	// AND THE TWO ARE NOT THE SAME MASK, read out of the engine's own header
	// rather than asserted. If they ever became equal this test would be
	// checking nothing, and the managed [Fact] that pins TraceMask.ShotHull
	// would be checking nothing either.
	EXPECT_NE( flags.find( "#define MASK_SHOT_HULL" ), std::string::npos )
		<< "MASK_SHOT_HULL is a mask of its own in bspflags.h";
	EXPECT_NE( flags.find( "CONTENTS_GRATE" ), std::string::npos )
		<< "and the grate bit it carries and MASK_SHOT does not is a real contents bit";
}

// ThePushLandsOnElementZeroAndNotOnEveryLimb LEFT with the native body (plan physics_bones_native_zero Phase 3):
// the client's ragdolls are SourceSharp.Client's ClientRagdolls, and ClientRagdollsTests.ABlastPushesTheRootAndRestartsTheSleepClock holds the push on element zero.


// TheCreationTickIsStampedOnlyAfterTheBodyExists LEFT with the native body (plan physics_bones_native_zero Phase 3):
// the client's ragdolls are SourceSharp.Client's ClientRagdolls, and ClientRagdollsTests.ABodyMadeThisTickSaysSo and the refusal facts hold the tick (stamped only on a body that was built).


//-----------------------------------------------------------------------------
// (4b) STEP 7b: the file is a REFERENCE now, and nothing restates it.
//
// Two claims, and neither is implied by the one above. The first is that
// c_te_explosion.cpp is out of client_sourcesharp -- so a reader who finds this
// file and assumes the enumerator runs is wrong, and so is anyone who "fixes" a
// dead function by deleting it. The second is the one that would empty the
// managed [Fact] without touching this file at all: if the port ever writes its
// OWN CRagdollExplosionEnumerator into the shim, TheImpulseNumbersAreStocksOwnReadOffDisk
// stops comparing the C# with stock and starts comparing it with a C++
// transcription of itself.
//-----------------------------------------------------------------------------
TEST( RagdollBlastSeam, TheReferenceFileIsUncompiledAndUnduplicated )
{
	// What the client compiles: client_sourcesharp's source list, read through
	// cmake_graph.h (src/client/native/CMakeLists.txt). The proof-of-read is the
	// overlay's new home, which this same test needs compiled.
	const std::set< std::string > client = cmake_graph::CompiledSources( cmake_graph::kClient );

	ASSERT_TRUE( client.count( "sourcesharp/game/client/shell_explosion_overlay.cpp" ) )
		<< "client_sourcesharp does not compile shell_explosion_overlay.cpp -- either the "
		   "reader is not reading the client's source list or the overlay's home left it";

	// By the path's TAIL, as the old vpc needle was: a copy under any directory counts.
	const std::string strTail = "/c_te_explosion.cpp";
	for ( std::set< std::string >::const_iterator it = client.begin(); it != client.end(); ++it )
	{
		EXPECT_FALSE( it->size() >= strTail.size()
			&& it->compare( it->size() - strTail.size(), strTail.size(), strTail ) == 0 )
			<< *it << " is compiled by client_sourcesharp. c_te_explosion.cpp is NOT a "
			   "compiled TU of this build -- 7b evicted it, and it is kept only as the "
			   "reference the falloff numbers are read out of";
	}

	// The overlay that WAS the file's one reachable body, in its new home.
	const std::string overlay = Read( "sourcesharp/game/client/shell_explosion_overlay.cpp" );

	ASSERT_FALSE( overlay.empty() )
		<< "shell_explosion_overlay.cpp is where CExplosionOverlay::Update went";
	EXPECT_NE( overlay.find( "bool CExplosionOverlay::Update( void )" ), std::string::npos )
		<< "and it carries the definition, which two shim TUs need a vtable for";

	// And the arithmetic is NOT restated anywhere under sourcesharp/.
	EXPECT_EQ( overlay.find( "CRagdollExplosionEnumerator::" ), std::string::npos )
		<< "the enumerator did not come with it";
	EXPECT_EQ( overlay.find( "m_flMagnitude" ), std::string::npos )
		<< "and neither did the falloff -- a second copy of stock's arithmetic inside "
		   "this port is a comparison of the port with itself";
}

// BothSlotsAreDeclaredWiredAndDefined LEFT with the native body (plan physics_bones_native_zero Phase 3):
// the client's ragdolls are SourceSharp.Client's ClientRagdolls, and the two IShimStandaloneClient slots left the ABI with it.

