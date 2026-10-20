//====== SourceSharp — two temp-entity evictions, and what makes each falsifiable =//
//
// TWO SECTIONS. The first is plan_entity.md step 7b, three stock TUs; the second
// is plan_shim.md Phase 1f, the seventeen per-lane forwarders and stock c_te.cpp.
// They share this file because they share their whole argument: THE LINK CANNOT
// SEE AN EVICTION. A build that compiles is the one thing an eviction always
// produces, so the claim underneath one has to be written down as something that
// can be false, or the green build is the only evidence there is.
//
// ===== SECTION ONE: step 7b =====
//
// plan_entity.md Phase 7 step 7b evicts `c_basetempentity.cpp`,
// `c_te_particlesystem.cpp` and `c_te_explosion.cpp` from
// the client build (then `game/client/client_csharp.vpc`,
// now client_sourcesharp in src/client/native/CMakeLists.txt). This file is what makes the three claims
// underneath that eviction falsifiable, because the LINK cannot see any of
// them: a build that compiles is the one thing an eviction always produces, and
// "a compile gate selects for deletion" is this tree's own recorded lesson.
//
// THE THREE CLAIMS, one per file, and what would make each of them false:
//
//   c_basetempentity.cpp  "nothing in this build makes a C_BaseTempEntity, so
//                          the two static lists were permanently empty and the
//                          four walks over them in shell_clientdll.cpp did
//                          nothing." FALSE the moment any compiled client TU
//                          derives from that class again -- and a derivation is
//                          a source fact with no symbol behind it, so the
//                          compiled TU list is scanned for one.
//
//   c_te_particlesystem.cpp "CTEParticleRenderer MOVED to the shim, verbatim."
//                          FALSE if the move became a rewrite, and false in a
//                          worse way if the stock copy is still on disk: two
//                          definitions of one body, in two files, in two builds,
//                          is the drift this tree keeps paying for. There is
//                          exactly one, and it is the shim's.
//
//   c_te_explosion.cpp     "the one body this build reached, CExplosionOverlay::
//                          Update, moved; the rest of the file is uncompiled
//                          REFERENCE material." The reference half is
//                          ragdoll_blast_seam_test.cpp's subject. What is here
//                          is the other half: the overlay has one definition in
//                          the tree.
//
// THE STRIPPER IS LOAD-BEARING, not tidiness. shell_clientdll.cpp now carries
// FOUR comments that spell `C_BaseTempEntity::PrecacheTempEnts()` and its
// siblings, explaining where the calls went; so does this file's own prose. A
// scan over raw source would read every one of those explanations as the
// violation it is describing, and the fix would be to delete the explanation.
// TheStripperIsLoadBearing below asserts the raw text DOES contain what the
// stripped text must not, so a stripper that quietly stopped working fails
// rather than passing everything.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <fstream>
#include <set>
#include <string>
#include <vector>
#include <sys/stat.h>

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

bool Exists( const std::string &strRelative )
{
	const char *root = SrcRoot();
	if ( !root )
		return false;

	struct stat st;
	return stat( ( std::string( root ) + "/" + strRelative ).c_str(), &st ) == 0
		&& S_ISREG( st.st_mode );
}

std::string Read( const std::string &strRelative )
{
	const char *root = SrcRoot();
	if ( !root )
		return std::string();

	std::ifstream f( ( std::string( root ) + "/" + strRelative ).c_str() );
	if ( !f.good() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( f ) ),
	                    std::istreambuf_iterator< char >() );
}

// Comments out, string literals kept. Same shape as ragdoll_blast_seam_test.cpp's.
std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	bool bInLine = false;
	bool bInString = false;
	char cQuote = '\0';

	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		const char c = strIn[ i ];
		const char cNext = ( i + 1 < strIn.size() ) ? strIn[ i + 1 ] : '\0';

		if ( bInBlock )
		{
			if ( c == '*' && cNext == '/' ) { bInBlock = false; i++; }
			else if ( c == '\n' ) { out += c; }
			continue;
		}

		if ( bInLine )
		{
			if ( c == '\n' ) { bInLine = false; out += c; }
			continue;
		}

		if ( bInString )
		{
			out += c;
			if ( c == '\\' && cNext != '\0' ) { out += cNext; i++; }
			else if ( c == cQuote ) { bInString = false; }
			continue;
		}

		if ( c == '/' && cNext == '*' ) { bInBlock = true; i++; continue; }
		if ( c == '/' && cNext == '/' ) { bInLine = true; i++; continue; }

		if ( c == '"' || c == '\'' ) { bInString = true; cQuote = c; }

		out += c;
	}

	return out;
}

//-----------------------------------------------------------------------------
// The ARM THIS BUILD COMPILES, and the reason this function exists at all.
//
// The first run of NoCompiledClientTuDerivesFromCBaseTempEntity failed, and it
// was RIGHT to: c_te_effect_dispatch.cpp still spells
// `class C_TEEffectDispatch : public C_BaseTempEntity`. But that block is inside
// `#ifndef SOURCESHARP`, and this build defines SOURCESHARP -- measured, not
// assumed:
//
//     grep -oP '-D\K\w+' (VPC's ninja graph, at the time) | sort -u | grep ^SOURCESHARP
//     -> SOURCESHARP, SOURCESHARP_SHELL_GAMEDLL
//
// (Since Phase 6b the same fact is cmake_graph::CompileDefinitions( kClient ).)
//
// So the text is in the file and is not in the build, and a scan that cannot
// tell those apart answers a question nobody asked. It drops the `#ifndef
// SOURCESHARP` arms and keeps the `#ifdef SOURCESHARP` ones, counting depth
// through every other conditional so a nested `#endif` closes the right thing.
//-----------------------------------------------------------------------------
// True when `line` starts with `prefix` AND the identifier ends there --
// `#ifndef SOURCESHARP` yes, `#ifndef SOURCESHARP_FOO_H` no.
bool EndsToken( const std::string &line, const std::string &prefix )
{
	if ( line.compare( 0, prefix.size(), prefix ) != 0 )
		return false;

	if ( line.size() == prefix.size() )
		return true;

	const char c = line[ prefix.size() ];
	return !( ( c >= 'A' && c <= 'Z' ) || ( c >= 'a' && c <= 'z' )
		|| ( c >= '0' && c <= '9' ) || c == '_' );
}

std::string SourceSharpArm( const std::string &strIn )
{
	std::string out;
	std::vector< bool > stack;      // true == this level is a SOURCESHARP guard
	std::vector< bool > keep;       // and this is whether its current arm is kept
	int nSuppressed = 0;            // how many enclosing levels are dropping text

	size_t at = 0;
	while ( at <= strIn.size() )
	{
		size_t nl = strIn.find( '\n', at );
		const bool bLast = ( nl == std::string::npos );
		if ( bLast )
			nl = strIn.size();

		const std::string line = strIn.substr( at, nl - at );

		// The first non-space run, so "  #ifndef SOURCESHARP" is seen.
		size_t i = line.find_first_not_of( " \t" );
		const std::string trimmed = ( i == std::string::npos ) ? std::string() : line.substr( i );

		bool bDirective = false;

		if ( trimmed.compare( 0, 1, "#" ) == 0 )
		{
			// The token has to END there. `#ifndef SOURCESHARP_TEMPENT_FORWARDS_H`
			// is an include guard, not this port's build arm, and treating it as
			// one would suppress a whole file and make every absence below read
			// as satisfied.
			const bool bIfndefSs = EndsToken( trimmed, "#ifndef SOURCESHARP" );
			const bool bIfdefSs  = EndsToken( trimmed, "#ifdef SOURCESHARP" );

			if ( bIfndefSs || bIfdefSs )
			{
				bDirective = true;
				stack.push_back( true );
				keep.push_back( bIfdefSs );
				if ( !keep.back() )
					nSuppressed++;
			}
			else if ( trimmed.find( "#if" ) == 0 )
			{
				bDirective = true;
				stack.push_back( false );
				keep.push_back( true );
			}
			else if ( trimmed.find( "#else" ) == 0 && !stack.empty() )
			{
				bDirective = true;
				if ( stack.back() )
				{
					if ( !keep.back() )
						nSuppressed--;
					keep.back() = !keep.back();
					if ( !keep.back() )
						nSuppressed++;
				}
			}
			else if ( trimmed.find( "#endif" ) == 0 && !stack.empty() )
			{
				bDirective = true;
				if ( stack.back() && !keep.back() )
					nSuppressed--;
				stack.pop_back();
				keep.pop_back();
			}
		}

		// A dropped arm keeps its newlines so line numbers in a failure message
		// still mean something.
		if ( nSuppressed == 0 && !bDirective )
			out += line;

		out += "\n";

		if ( bLast )
			break;

		at = nl + 1;
	}

	return out;
}

//-----------------------------------------------------------------------------
// Every translation unit client_sourcesharp compiles, farm-relative, read
// through cmake_graph.h (compile_commands.json cross-checked against
// src/client/native/CMakeLists.txt). Until Phase 6b this walked the `$File`
// rows of game/client/client_csharp.vpc.
//-----------------------------------------------------------------------------
std::vector< std::string > ClientCompiledFiles()
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kClient );
	return std::vector< std::string >( compiled.begin(), compiled.end() );
}

const char *kEvicted[] =
{
	"game/client/c_basetempentity.cpp",
	"game/client/c_te_particlesystem.cpp",
	"game/client/c_te_explosion.cpp",
};

// The native replacement that stays: the evicted temp entities' backends.
// (shell_explosion_overlay.cpp, c_te_explosion.cpp's overlay, left with the
// glow overlays' port, C7: CExplosionOverlay is the managed GlowOverlaySystem's
// RampOverlay.)
const char *kReplacements[] =
{
	"sourcesharp/game/client/shell_te_backends_client.cpp",
};

//-----------------------------------------------------------------------------
// THE LIST REALLY WAS READ, asserted without a magic number.
//
// Each sweep below is for something that must NOT be there, and a reader that
// returned nothing passes all of them perfectly. Under VPC this was a second,
// independent reading of client_csharp.vpc (loose `$File` line count) that had
// to agree with the quote-walking parser. RETIRED IN 6b: cmake_graph.h's
// CompiledSources already reads the list twice (compile_commands.json against
// the CMakeLists source list) and throws when they disagree or either is
// empty. What stays is the proof-of-read: the client shell, which no eviction
// can take, must be in the list the sweep walks.
//-----------------------------------------------------------------------------
const char *const kClientProofOfRead = "sourcesharp/game/client/shell_clientdll.cpp";

void AssertTheClientFileListParsed( const std::vector< std::string > &files )
{
	bool bFound = false;
	for ( size_t i = 0; i < files.size(); i++ )
	{
		if ( files[ i ] == kClientProofOfRead )
			bFound = true;
	}

	ASSERT_TRUE( bFound )
		<< "client_sourcesharp's compiled list does not name " << kClientProofOfRead
		<< ", so what was read is not the client's source list -- and the sweep "
		   "below would then report a clean tree having read none of it.";
}

} // namespace

//-----------------------------------------------------------------------------
// The membership half.
//-----------------------------------------------------------------------------
TEST( TempEntEvicted, TheThreeStockTusAreOutOfTheClientBuild )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kClientProofOfRead ) )
		<< "client_sourcesharp does not compile " << kClientProofOfRead
		<< " -- the reader is not reading the client's source list";

	for ( size_t i = 0; i < sizeof( kEvicted ) / sizeof( kEvicted[ 0 ] ); i++ )
	{
		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kEvicted[ i ] ) )
			<< kEvicted[ i ] << " is out of the client build "
			   "(client_sourcesharp, src/client/native/CMakeLists.txt)";
	}
}

TEST( TempEntEvicted, TheTwoShimReplacementsAreInTheClientBuild )
{
	for ( size_t i = 0; i < sizeof( kReplacements ) / sizeof( kReplacements[ 0 ] ); i++ )
	{
		EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kReplacements[ i ] ) )
			<< kReplacements[ i ] << " is compiled -- an eviction whose replacement is "
			   "not in the build is a capability that stopped being shipped";
	}
}

// RETIRED IN 6b: TempEntEvicted.TheVpcStillBalancesItsBraces. It guarded
// client_csharp.vpc's `$Folder { }` syntax against a `$File` removal that took
// a brace with it. VPC is gone; a CMake source list has no such nesting, and a
// malformed CMakeLists fails the configure step itself.

//-----------------------------------------------------------------------------
// c_basetempentity.cpp: the claim is that nothing makes one any more.
//
// This is the only check in the file that could stop the eviction being a
// silent capability loss, and it is a SOURCE scan because a class derivation
// emits no symbol a linker could be asked about.
//-----------------------------------------------------------------------------
TEST( TempEntEvicted, NoCompiledClientTuDerivesFromCBaseTempEntity )
{
	const std::vector< std::string > files = ClientCompiledFiles();

	ASSERT_NO_FATAL_FAILURE( AssertTheClientFileListParsed( files ) );

	std::vector< std::string > offenders;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		const std::string body = SourceSharpArm( StripComments( Read( files[ i ] ) ) );

		if ( body.empty() )
			continue;

		if ( body.find( "public C_BaseTempEntity" ) != std::string::npos )
			offenders.push_back( files[ i ] );
	}

	std::string strList;
	for ( size_t i = 0; i < offenders.size(); i++ )
		strList += "\n    " + offenders[ i ];

	EXPECT_TRUE( offenders.empty() )
		<< "nothing this build compiles derives from C_BaseTempEntity, which is why "
		   "s_pTempEntities and s_pDynamicEntities are permanently empty and why the "
		   "class could be evicted at all. These do:" << strList;
}

TEST( TempEntEvicted, TheClientShellNoLongerWalksTheEmptyTempEntityLists )
{
	const std::string shell = Read( "sourcesharp/game/client/shell_clientdll.cpp" );

	ASSERT_FALSE( shell.empty() ) << "shell_clientdll.cpp is on disk";

	const std::string stripped = StripComments( shell );

	EXPECT_EQ( stripped.find( "C_BaseTempEntity::" ), std::string::npos )
		<< "the four PrecacheTempEnts / ClearDynamicTempEnts / CheckDynamicTempEnts "
		   "calls are gone with the class";
	EXPECT_EQ( stripped.find( "c_basetempentity.h" ), std::string::npos )
		<< "and so is the include, which is what would otherwise keep the header alive "
		   "in this TU";
}

TEST( TempEntEvicted, TheStripperIsLoadBearing )
{
	const std::string shell = Read( "sourcesharp/game/client/shell_clientdll.cpp" );

	ASSERT_FALSE( shell.empty() ) << "shell_clientdll.cpp is on disk";

	// The previous test would pass over a file that never mentioned the class at
	// all, and it would ALSO pass if StripComments returned an empty string. Both
	// are checks that cannot fail. shell_clientdll.cpp deliberately keeps four
	// comments naming the calls it lost, so the raw text must still carry them.
	EXPECT_NE( shell.find( "C_BaseTempEntity::PrecacheTempEnts" ), std::string::npos )
		<< "the RAW file still explains, in a comment, what left and why";

	const std::string stripped = StripComments( shell );

	// WHAT THIS USED TO BE, AND WHY IT IS NOT THAT ANY MORE.
	//
	// It was `EXPECT_GT( stripped.size(), shell.size() / 2 )` -- a SIZE PROXY
	// for "the stripper returned a file". The property it actually tested is
	// "this file is less than half comments", which is a fact about prose
	// density and not about StripComments at all, in a tree whose own briefs
	// say comments and .vpc prose are load-bearing.
	//
	// It was 174 bytes from firing before anybody touched it. Measured at
	// entity-port's tip e27c719c0: raw 106,646, stripped 53,497, ratio 0.502
	// against a half of 53,323. CLIENT_FLIP lane 2 added the §7 retirement
	// rulings this file now carries where the retired calls used to be, and
	// tipped it to 0.496 -- so the next lane to add a paragraph here would have
	// gone red for writing a comment, and the cheapest way to get green would
	// have been to DELETE THE RULINGS. A check that pays you to remove
	// explanations is worse than no check.
	//
	// So it asks the question it meant, in four parts that fail in both
	// directions. This is strictly stronger than the ratio: an empty return, a
	// no-op return, a stripper that ate real code, and one that left comments
	// behind are each caught by name, and none of them depends on how much
	// prose the file happens to carry.
	ASSERT_FALSE( stripped.empty() )
		<< "StripComments returned nothing, so the assertions in the test above "
		   "were all passing on an empty string";

	EXPECT_LT( stripped.size(), shell.size() )
		<< "StripComments removed NOTHING from a file that certainly has "
		   "comments in it, so it is a no-op and the test above is vacuous";

	EXPECT_NE( stripped.find( "void OnRenderStart()" ), std::string::npos )
		<< "the stripper ate real code: OnRenderStart's definition is not "
		   "inside a comment and must survive stripping";

	EXPECT_EQ( stripped.find( "C_BaseTempEntity::PrecacheTempEnts" ), std::string::npos )
		<< "the stripper left comment text behind -- the raw file carries that "
		   "name ONLY in a comment, asserted above, so finding it after "
		   "stripping means comments are not being removed";
}

//-----------------------------------------------------------------------------
// c_te_particlesystem.cpp: the claim is a MOVE, and exactly one copy.
//-----------------------------------------------------------------------------
TEST( TempEntEvicted, TheStockParticleEmitterIsGoneFromDisk )
{
	EXPECT_FALSE( Exists( "game/client/c_te_particlesystem.cpp" ) )
		<< "the whole of what this build compiled in that TU moved to the shim, so "
		   "leaving the file on disk would be two definitions of one body in two builds";

	EXPECT_TRUE( Exists( "game/client/c_te_particlesystem.h" ) )
		<< "the HEADER stays: four compiled TUs include it and three of them belong to "
		   "step 7a. Stock headers outlive their .cpp across this phase (7g's precedent: "
		   "gamestats.cpp went, gamestats.h stayed)";
}

TEST( TempEntEvicted, TheParticleEmitterIsCSharpAndItsShimIsGone )
{
	// It MOVED to the shim verbatim (step 7b) and then was PORTED (lane
	// c5-particles): CTEParticleRenderer, TE_BloodStream and the debug line are
	// SourceSharp.Client's TeParticles, replayed against teparticles-stock.tsv.gz.
	// The shim copy must be gone, not left beside the port.
	EXPECT_FALSE( Exists( "sourcesharp/game/client/shell_te_particle_render.cpp" ) )
		<< "shell_te_particle_render.cpp is back; the emitter is C#'s (TeParticles)";
	// Positive control of the same probe: a shim file that stays.
	EXPECT_TRUE( Exists( "sourcesharp/game/client/shell_te_backends_client.cpp" ) );
}

//-----------------------------------------------------------------------------
// c_te_explosion.cpp: the overlay moved to the shim (step 7b), and then to C#
// (C7 glow overlays: the managed GlowOverlaySystem's RampOverlay).
//-----------------------------------------------------------------------------
TEST( TempEntEvicted, TheExplosionOverlayIsCSharpAndNoCompiledTuDefinesIt )
{
	const std::vector< std::string > files = ClientCompiledFiles();

	ASSERT_NO_FATAL_FAILURE( AssertTheClientFileListParsed( files ) );

	std::vector< std::string > definers;
	int nBackends = 0;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		const std::string body = SourceSharpArm( StripComments( Read( files[ i ] ) ) );

		if ( body.find( "CExplosionOverlay::Update" ) != std::string::npos ||
		     body.find( "new CExplosionOverlay" ) != std::string::npos )
			definers.push_back( files[ i ] );
		// Positive control of the same sweep: a backend that stays, found by its definition.
		if ( body.find( "void SourceSharp_ClientEnergySplash(" ) != std::string::npos )
			nBackends++;
	}

	EXPECT_EQ( nBackends, 1 ) << "the sweep finds the energy splash's one definition";
	EXPECT_TRUE( definers.empty() )
		<< "no compiled TU defines or makes the native explosion overlay; the first is " << ( definers.empty() ? "" : definers[ 0 ] );
}

//=============================================================================//
// plan_shim.md PHASE 1f — THE SEVENTEEN PER-LANE FORWARDERS BECOME TWO.
//
// A SECOND EVICTION IN THE SAME FILE, and it is here rather than in a new one
// because it rests on exactly the argument the three above rest on: the LINK
// CANNOT SEE ANY OF IT. A build that compiles is the one thing an eviction
// always produces, and "a compile gate selects for deletion" is this tree's own
// recorded lesson. What is gone:
//
//   server  shell_te_fwd_{a,b,c,d,e,f,s1,s4}.cpp   2,388 lines, 40 TE_*
//   client  shell_te_fwd_{c1,c4,ca,cd,ce,cn,cp,cx,pool2}.cpp  2,469 lines, 52 TE_*
//   client  game/client/c_te.cpp (stock)           the CTempEntsSystem vtable
//
// and what replaced it is shell_te_forwards.cpp (18 forwards: the 17 with a live
// importer, plus TE_DispatchEffect, whose only caller is in the same file) and
// shell_te_forwards_client.cpp (TE_Bubbles, TE_Smoke, and an ITempEntsSystem
// whose other 38 overrides are empty).
//
// THE ONE CLAIM A CENSUS COULD NOT MAKE, and the reason the client half of this
// section exists at all. All 52 client forwards were imported by exactly one
// object, c_te.o, so a consumer count said "alive" about every one of them. What
// actually reached them was a VIRTUAL CALL through `te`, which emits no symbol
// for any instrument to count. The deletion therefore rests on a SOURCE fact --
// how many `te->` call sites there are and which -- and a source fact with no
// symbol behind it is what a test like this one is for.
//
// AND IT IS THE FACT MOST LIKELY TO GO STALE SILENTLY. The 38 empty overrides
// compile, link and run. A `te->Explosion(...)` added tomorrow would build
// clean, do nothing, and say nothing;
// TheOnlyTeArrowCallsLeftAreTheThreeThatForward below is the only thing in the
// tree that would notice.
//=============================================================================//

namespace
{

const char *kPerLaneForwarders[] =
{
	"sourcesharp/game/server/shell_te_fwd_a.cpp",
	"sourcesharp/game/server/shell_te_fwd_b.cpp",
	"sourcesharp/game/server/shell_te_fwd_c.cpp",
	"sourcesharp/game/server/shell_te_fwd_d.cpp",
	"sourcesharp/game/server/shell_te_fwd_e.cpp",
	"sourcesharp/game/server/shell_te_fwd_f.cpp",
	"sourcesharp/game/server/shell_te_fwd_s1.cpp",
	"sourcesharp/game/server/shell_te_fwd_s4.cpp",
	"sourcesharp/game/client/shell_te_fwd_c1.cpp",
	"sourcesharp/game/client/shell_te_fwd_c4.cpp",
	"sourcesharp/game/client/shell_te_fwd_ca.cpp",
	"sourcesharp/game/client/shell_te_fwd_cd.cpp",
	"sourcesharp/game/client/shell_te_fwd_ce.cpp",
	"sourcesharp/game/client/shell_te_fwd_cn.cpp",
	"sourcesharp/game/client/shell_te_fwd_cp.cpp",
	"sourcesharp/game/client/shell_te_fwd_cx.cpp",
	"sourcesharp/game/client/shell_te_fwd_pool2.cpp",
};

// The three ITempEntsSystem overrides the client forwarder implements. Measured
// with tools/shim-census plus a read of every `te->` on the client link line at
// c5eab9deb; lane C2's analysis had the COUNT right and the SET wrong -- it
// named Decal, whose only cited caller, baseentity_shared.cpp, is a server
// object and is not on the client link line at all.
const char *kForwardedTeCalls[] = { "Bubbles", "Smoke", "DispatchEffect" };

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
		|| ( c >= '0' && c <= '9' ) || c == '_';
}

} // namespace

TEST( TempEntForwardersCollapsed, TheSeventeenPerLaneForwardersAreOutOfBothBuilds )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/server/shell_te_forwards.cpp" ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) is read and names its forwarder";
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_te_forwards_client.cpp" ) )
		<< "client_sourcesharp (src/client/native/CMakeLists.txt) is read and names its forwarder";

	for ( size_t i = 0; i < sizeof( kPerLaneForwarders ) / sizeof( kPerLaneForwarders[ 0 ] ); i++ )
	{
		const std::string rel = kPerLaneForwarders[ i ];

		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, rel ) )
			<< rel << " is out of the server build";
		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, rel ) )
			<< rel << " is out of the client build";
	}
}

TEST( TempEntForwardersCollapsed, TheSeventeenPerLaneForwardersAreGoneFromDisk )
{
	// A ROW REMOVED WITH THE FILE LEFT BEHIND is drift this tree keeps paying
	// for: the next reader greps, finds a forward, and believes it is compiled.
	for ( size_t i = 0; i < sizeof( kPerLaneForwarders ) / sizeof( kPerLaneForwarders[ 0 ] ); i++ )
	{
		EXPECT_FALSE( Exists( kPerLaneForwarders[ i ] ) )
			<< kPerLaneForwarders[ i ] << " is still on disk, uncompiled, with forwards in it";
	}
}

TEST( TempEntForwardersCollapsed, TheTwoReplacementsAreCompiledOnTheirOwnSides )
{
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/server/shell_te_forwards.cpp" ) )
		<< "the collapsed server forwarder is compiled -- an eviction whose replacement "
		   "is not in the build is a capability that stopped being shipped";
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_te_forwards_client.cpp" ) )
		<< "and so is the client's";

	EXPECT_TRUE( Exists( "sourcesharp/game/server/shell_te_forwards.cpp" ) );
	EXPECT_TRUE( Exists( "sourcesharp/game/client/shell_te_forwards_client.cpp" ) );

	// AND NEITHER SIDE COMPILES THE OTHER'S. The two use the same forward ids
	// and DIFFERENT SourceSharpTe_Forward signatures -- the server's takes a
	// filter and a delay and sends, the client's takes neither and plays -- so a
	// crossed row is a link error at best and the wrong effect at worst.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/client/shell_te_forwards_client.cpp" ) )
		<< "the server does not compile the client's forwarder";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/server/shell_te_forwards.cpp" ) )
		<< "and the client does not compile the server's";
}

TEST( TempEntForwardersCollapsed, StockCTeIsOutOfTheClientBuildAndTheShimDefinesTe )
{
	// Exact farm-relative paths now, so c_te_legacytempents.cpp (which STAYS
	// for now) cannot be matched by the needle.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/c_te.cpp" ) )
		<< "stock c_te.cpp is out of the client build; its CTempEntsSystem was the only "
		   "thing that reached the 52 client forwards";

	// AND THE READER IS SHOWN TO SEE THE CLIENT LIST, because an absence
	// answered by a reader that saw nothing would pass the line above for free.
	// The control is the shim's forwarder now, the file this fact's last half
	// reads: clientshadowmgr.cpp held the job until it, the client's last fork
	// TU, left client.so with C7 cluster 12 S2 (2026-10-19), and no game/ row
	// is left to take it. Asked through the same Compiles() as the absence, so
	// a reader that saw no list (or the wrong one) fails here.
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_te_forwards_client.cpp" ) )
		<< "shell_te_forwards_client.cpp is still compiled -- the reader sees the "
		   "client list. c_te_effect_dispatch.cpp, then cdll_util.cpp, then "
		   "clientleafsystem.cpp, then clientshadowmgr.cpp were this control until "
		   "C2, C8, C7 cluster 12 L2 and C7 cluster 12 S2 evicted them";

	// Exactly one definition of `te` in the client's sources, and it is the
	// shim's: two would be a link error, none an undefined reference in
	// cdll_util.cpp and c_te_effect_dispatch.cpp, which still call through it.
	const std::string shim = StripComments(
		Read( "sourcesharp/game/client/shell_te_forwards_client.cpp" ) );

	ASSERT_FALSE( shim.empty() ) << "the client forwarder is on disk";

	EXPECT_NE( shim.find( "ITempEntsSystem *te =" ), std::string::npos )
		<< "the client forwarder defines the `te` global c_te.cpp used to, so the two "
		   "objects that import it still link";
}

TEST( TempEntForwardersCollapsed, TheOnlyTeArrowCallsLeftAreTheThreeThatForward )
{
	// THIS IS THE FACT THE WHOLE CLIENT DELETION RESTS ON. 38 of the 41
	// ITempEntsSystem overrides have empty bodies because nothing calls them. A
	// call added to any of them compiles, links, runs and does nothing, in
	// silence. Nothing else in this tree would say so.
	const std::vector< std::string > files = ClientCompiledFiles();

	ASSERT_NO_FATAL_FAILURE( AssertTheClientFileListParsed( files ) );

	std::vector< std::string > offenders;
	int nFound = 0;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		const std::string body = SourceSharpArm( StripComments( Read( files[ i ] ) ) );

		if ( body.empty() )
			continue;

		size_t at = 0;
		while ( ( at = body.find( "te->", at ) ) != std::string::npos )
		{
			// `state->`, `pSprite->`, `m_flRate->` -- the `te` has to be a whole
			// identifier, so what precedes it cannot continue one.
			if ( at > 0 && IsIdentChar( body[ at - 1 ] ) )
			{
				at += 4;
				continue;
			}

			size_t end = at + 4;
			while ( end < body.size() && IsIdentChar( body[ end ] ) )
				end++;

			const std::string method = body.substr( at + 4, end - at - 4 );
			nFound++;

			bool bForwarded = false;
			for ( size_t k = 0; k < sizeof( kForwardedTeCalls ) / sizeof( kForwardedTeCalls[ 0 ] ); k++ )
			{
				if ( method == kForwardedTeCalls[ k ] )
					bForwarded = true;
			}

			if ( !bForwarded )
				offenders.push_back( files[ i ] + ": te->" + method );

			at = end;
		}
	}

	// THE COUNT, so a stripper that started eating everything cannot report a
	// clean sweep. Three until C8 (lane c6-input): cdll_util.cpp's two left with
	// it, so ONE is what there is now, the effect dispatch's (where
	// c_te_effect_dispatch.cpp's went in C2). Finding none means the scan broke
	// rather than that the calls went away.
	EXPECT_EQ( nFound, 1 )
		<< "there is one `te->` call site on the client link line. A different "
		   "number is either a new call or a scan that stopped working";

	std::string strList;
	for ( size_t i = 0; i < offenders.size(); i++ )
		strList += "\n    " + offenders[ i ];

	EXPECT_TRUE( offenders.empty() )
		<< "every `te->` this build compiles reaches one of the three overrides that "
		   "forward. The rest are EMPTY, so a call to one of them does nothing and says "
		   "nothing -- either give it a forward in shell_te_forwards_client.cpp, or do "
		   "not make the call. These do neither:" << strList;
}

TEST( TempEntForwardersCollapsed, TheForwardIdTableCoversEveryIdTheTwoForwardersUse )
{
	// The ids are array indices in all but name -- tempent_forwards.h says so --
	// and Phase 1f RENUMBERED them, which that header calls its one exception to
	// append-only. An id a forwarder uses and the header lacks would not
	// compile; an id the header declares and no forwarder uses is a dead row,
	// and a dead row is how the next renumber gets it wrong.
	const std::string header = Read( "sourcesharp/game/shared/tempent_forwards.h" );

	ASSERT_FALSE( header.empty() ) << "tempent_forwards.h is on disk";

	const std::string both =
		Read( "sourcesharp/game/server/shell_te_forwards.cpp" ) +
		Read( "sourcesharp/game/client/shell_te_forwards_client.cpp" );

	ASSERT_FALSE( both.empty() ) << "both forwarder files are on disk";

	std::vector< std::string > unused;
	std::vector< std::string > reservedButUsed;
	int nDeclared = 0;
	int nReserved = 0;

	const std::string kName = "SOURCESHARP_TE_FWD_";
	size_t at = 0;
	while ( ( at = header.find( kName, at ) ) != std::string::npos )
	{
		size_t end = at;
		while ( end < header.size() && IsIdentChar( header[ end ] ) )
			end++;

		const std::string name = header.substr( at, end - at );
		at = end;

		// The header names each id once in the enum and once more inside the
		// #ifndef guard's own spelling; the guard is SOURCESHARP_TEMPENT_...,
		// which does not start with this prefix, so every hit here is a row.
		if ( name == "SOURCESHARP_TE_FWD_COUNT" )
			continue;

		nDeclared++;

		// A RESERVED row (the server gc cull deleted its forwarder and kept the
		// id so nothing renumbers) is the opposite claim: NO forward may use it.
		const size_t eol = header.find( '\n', at );
		const std::string rest = header.substr( at, eol == std::string::npos ? std::string::npos : eol - at );
		const bool bReserved = rest.find( "RESERVED" ) != std::string::npos;

		if ( bReserved )
		{
			nReserved++;
			if ( both.find( name ) != std::string::npos )
				reservedButUsed.push_back( name );
		}
		else if ( both.find( name ) == std::string::npos )
			unused.push_back( name );
	}

	EXPECT_EQ( nDeclared, 18 )
		<< "tempent_forwards.h declares eighteen ids after Phase 1f. A different number "
		   "is a real change and this count moves with it -- deliberately, because "
		   "renumbering is the thing that header warns about";

	// The four the server gc cull reserved: ARMOR_RICOCHET, BEAM_POINTS,
	// BUBBLE_TRAIL, ENERGY_SPLASH; the three plan_server_native_v2 T4b
	// reserved: DUST, METAL_SPARKS, MUZZLE_FLASH (their ss::IEffects callers had
	// none); and the two T4d reserved: EXPLOSION, LARGE_FUNNEL (their callers
	// were slots EffectsOverTempEnts had replaced); and the two T4f reserved:
	// BSP_DECAL, PROJECT_DECAL (their caller was the PaintDecal slot, which
	// EffectsOverTempEnts answers itself now). Moves only with a deliberate
	// change.
	EXPECT_EQ( nReserved, 11 )
		<< "tempent_forwards.h marks eleven ids RESERVED since plan_server_native_v2 T4f";

	std::string strList;
	for ( size_t i = 0; i < unused.size(); i++ )
		strList += "\n    " + unused[ i ];

	EXPECT_TRUE( unused.empty() )
		<< "every declared id that is not RESERVED is passed by a forward on one side "
		   "or the other. These are declared and unused -- delete the forward's id or "
		   "mark it RESERVED:" << strList;

	std::string strReserved;
	for ( size_t i = 0; i < reservedButUsed.size(); i++ )
		strReserved += "\n    " + reservedButUsed[ i ];

	EXPECT_TRUE( reservedButUsed.empty() )
		<< "a RESERVED id is passed by a forward again. Either the forward is back and "
		   "the RESERVED mark comes off, or the forward is using a dead id:" << strReserved;
}

// RETIRED IN 6b: TempEntForwardersCollapsed.BothVpcsStillBalanceTheirBraces.
// It guarded the `$Folder { }` syntax of server_csharp.vpc and
// client_csharp.vpc. VPC is gone; a CMake source list has no such nesting, and
// a malformed CMakeLists fails the configure step itself.
