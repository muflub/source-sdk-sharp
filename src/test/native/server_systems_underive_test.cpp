//====== SourceSharp — IGameSystem leaves the server ==========================//
//
// plan_gamedll.md Phase 7a. The phase is a SUBTRACTION and its gate is a COUNT,
// for one reason stated in the plan and worth repeating at the top of the file
// that enforces it:
//
//   A LEFTOVER REGISTRANT IS SILENT. A CAutoGameSystem whose InitAllSystems is
//   never called does not warn, does not fail to link and does not run. It
//   simply never initialises, and whatever it was for stops working somewhere
//   else, later, at a distance from the edit that caused it.
//
// The shipped gate is on the BINARY: zero CAutoGameSystem/CBaseGameSystem
// symbols in server.so, filtered by symbol type. These facts are the source
// half, and they exist because the binary gate has two blind spots:
//
//   * it says a symbol is absent, not WHY. A count that goes to zero because
//     somebody guarded a whole file out looks the same as one that goes to zero
//     because every system moved to a base that does not register.
//   * it cannot run before the build. These fail on the edit.
//
// THE TWO LOAD-BEARING FACTS ARE THE ONES A GREP CANNOT MAKE, and both are
// about #ifdef arms:
//
//   1. TheStockBasesAreNamedOnlyInsideAClientArm. Ten of these systems live in
//      files the CLIENT compiles too (T1), and the client still has IGameSystem
//      and still calls InitAllSystems. So each of those files still contains
//      the string "CAutoGameSystem" -- inside #ifdef CLIENT_DLL. A grep cannot
//      tell that from a live registration on the server, which is exactly why
//      Phase 7a's own gate is a symbol count and not a grep. This walks the
//      conditionals and answers the question the grep cannot.
//
//   2. TheBaseDeclaresExactlyTheStagesTheWalkersDispatch. The whole risk of the
//      row shape is a stage that exists on the base and is never walked, or a
//      walk of a stage nothing implements. Both are silent. This holds the two
//      lists equal in both directions.
//
//=============================================================================//

#include <algorithm>
#include <fstream>
#include <set>
#include <string>
#include <vector>
#include <sys/stat.h>

#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string Read( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), std::ios::binary );
	EXPECT_TRUE( file.is_open() ) << "could not open " << pszRel
		<< " -- every invariant about it would pass vacuously";
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

// Comments stripped first and always: every needle below would otherwise match
// the paragraph explaining what it is for, which is this tree's running list of
// checks that cannot fail.
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

std::vector<std::string> Lines( const std::string &in )
{
	std::vector<std::string> out;
	std::string cur;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( in[ i ] == '\n' ) { out.push_back( cur ); cur.clear(); }
		else if ( in[ i ] != '\r' ) cur += in[ i ];
	}
	out.push_back( cur );
	return out;
}

std::string Trim( const std::string &s )
{
	size_t a = s.find_first_not_of( " \t" );
	if ( a == std::string::npos ) return std::string();
	size_t b = s.find_last_not_of( " \t" );
	return s.substr( a, b - a + 1 );
}

//-----------------------------------------------------------------------------
// A CONDITIONAL TRACKER, and it is the instrument this whole file exists for.
//
// For every line of a file it answers: is this line inside a region that is
// live when CLIENT_DLL is DEFINED, and is it inside a region live when
// CLIENT_DLL is NOT defined? Both can be true (a line under no conditional at
// all); neither can be true (a line under `#if 0`, or under two contradictory
// arms).
//
// It tracks NESTING of every preprocessor conditional, not only the CLIENT_DLL
// ones -- a CLIENT_DLL arm inside an unrelated #ifdef still has to nest
// correctly or the arms after it are attributed to the wrong side. Conditions
// it does not understand contribute "either side is possible", which is the
// direction that fails safe: an unknown condition never lets a live server-side
// registration be reported as client-only.
//-----------------------------------------------------------------------------
struct Arm
{
	bool m_bServerPossible;   // this arm can be live with CLIENT_DLL undefined
	bool m_bClientPossible;   // ...and with CLIENT_DLL defined
	bool m_bWasClientTest;    // the #if tested CLIENT_DLL, so #else flips it
	bool m_bSeenElse;
};

bool TestsClientDll( const std::string &expr )
{
	return expr.find( "CLIENT_DLL" ) != std::string::npos;
}

// true if the directive is a plain positive test of CLIENT_DLL and nothing else
bool IsPlainClientTest( const std::string &directive, const std::string &expr, bool *pbPositive )
{
	if ( !TestsClientDll( expr ) )
		return false;

	// Anything with a second term is not something this tracker will guess at.
	if ( expr.find( "&&" ) != std::string::npos || expr.find( "||" ) != std::string::npos )
		return false;

	if ( directive == "ifdef" )  { *pbPositive = true;  return true; }
	if ( directive == "ifndef" ) { *pbPositive = false; return true; }
	if ( directive == "if" )
	{
		const bool bNegated = expr.find( '!' ) != std::string::npos;
		*pbPositive = !bNegated;
		return true;
	}
	return false;
}

struct Where
{
	bool m_bServer;
	bool m_bClient;
};

std::vector<Where> Regions( const std::string &text )
{
	const std::vector<std::string> lines = Lines( text );
	std::vector<Where> out;
	std::vector<Arm> stack;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trim( lines[ i ] );
		bool bDirective = ( !t.empty() && t[ 0 ] == '#' );

		std::string directive, expr;
		if ( bDirective )
		{
			const std::string rest = Trim( t.substr( 1 ) );
			const size_t sp = rest.find_first_of( " \t(" );
			directive = ( sp == std::string::npos ) ? rest : rest.substr( 0, sp );
			expr = ( sp == std::string::npos ) ? std::string() : Trim( rest.substr( sp ) );
		}

		if ( bDirective && ( directive == "if" || directive == "ifdef" || directive == "ifndef" ) )
		{
			Arm arm;
			arm.m_bSeenElse = false;
			bool bPositive = false;
			if ( IsPlainClientTest( directive, expr, &bPositive ) )
			{
				arm.m_bWasClientTest = true;
				arm.m_bClientPossible = bPositive;
				arm.m_bServerPossible = !bPositive;
			}
			else
			{
				arm.m_bWasClientTest = false;
				arm.m_bClientPossible = true;
				arm.m_bServerPossible = true;
			}
			stack.push_back( arm );
		}
		else if ( bDirective && ( directive == "else" || directive == "elif" ) && !stack.empty() )
		{
			Arm &arm = stack.back();
			arm.m_bSeenElse = true;
			if ( arm.m_bWasClientTest && directive == "else" )
			{
				const bool bClient = arm.m_bClientPossible;
				arm.m_bClientPossible = !bClient;
				arm.m_bServerPossible = bClient;
			}
			else
			{
				arm.m_bClientPossible = true;
				arm.m_bServerPossible = true;
			}
		}

		// The directive line itself is attributed to the region it OPENS, so
		// that `#ifdef CLIENT_DLL` on its own line is not reported as a
		// server-side mention of CLIENT_DLL.
		Where w;
		w.m_bServer = true;
		w.m_bClient = true;
		for ( size_t d = 0; d < stack.size(); d++ )
		{
			w.m_bServer = w.m_bServer && stack[ d ].m_bServerPossible;
			w.m_bClient = w.m_bClient && stack[ d ].m_bClientPossible;
		}
		out.push_back( w );

		if ( bDirective && directive == "endif" && !stack.empty() )
			stack.pop_back();
	}

	EXPECT_TRUE( stack.empty() ) << "unbalanced preprocessor conditionals";
	return out;
}

//-----------------------------------------------------------------------------
// Every file the SERVER compiles that this phase touched, and whether the
// CLIENT compiles it too.
//
// DERIVED, not copied from plan_gamedll.md T1: object -> source out of the
// ninja graph for both projects, intersected with the classes that actually
// have a vtable in server.so. T1 lists eight .cpp files; the measured answer is
// ten sites and two of them are HEADERS T1 does not name -- ragdoll_shared.h
// and gamestats.h, holding two of the six systems Part 9a never names either.
//-----------------------------------------------------------------------------
// plan_gamedll_after.md Phase A3 added a THIRD kind. decals.cpp used to be a
// shared site; it left the server build (server_csharp.vpc then) when
// CDecalEmitterSystem became managed, so the server does not compile it at
// all and its stock base is correct UNGUARDED. That classification is not
// taken on trust -- the fact below reads server_sourcesharp's source list
// (src/server/native/CMakeLists.txt, through cmake_graph.h) and refuses a
// kClientOnly file the server still compiles, because otherwise the kind would be a way to wave a live file
// through the whole check.
enum SiteKind
{
	kShared,       // both projects compile it: a stock base only inside a CLIENT_DLL arm
	kServerOnly,   // neither stock base may be named at all, guarded or not
	kClientOnly,   // the SERVER's project does not compile it; unguarded is right
};

struct Site
{
	const char *m_pszFile;
	SiteKind m_eKind;
};

// SIX ROWS LEFT when the farm stopped carrying Valve stock that neither
// client.so nor server.so compiles: game/shared/physics_main_shared.cpp and
// game/server/{entitylist,soundent,TemplateEntities,toolframework_server,util}.cpp.
// A file no DLL compiles has no arm for this fact to be about, and every row
// here is opened and must read non-empty.
const Site kSites[] =
{
	// shared -- the base swap is #ifdef CLIENT_DLL on the server's half only
	{ "sourcesharp/game/shared/collisionproperty.cpp",              kShared },
	// RELOCATED in plan_shim.md Phase 2c -- the path moved, the kind did not:
	// both projects still compile it, and its stock base is still only named
	// inside a CLIENT_DLL arm.
	{ "sourcesharp/game/shared/gamestringpool.cpp",     kShared },
	// props_shared.h's row went 2026-10-19: no DLL compiles it (ninja deps of
	// both targets, debug), so it had no arm for this fact to be about. So did
	// ragdoll_shared.h's: the client compiles it, but its system's base is
	// gone and only a comment names one. gamestats.h is the client's alone
	// since the server stopped including it (same measurement): unguarded is
	// right there, and kClientOnly's fact refuses it if the server compiles it
	// again.
	{ "game/shared/gamestats.h",                        kClientOnly },
	// sound_emitter.cpp's row (kClientOnly since plan_server_native_v2 T4a's
	// precache path) went with the file: the client's emit path is C#'s, and its
	// CSoundEmitterSystem row is shell_soundemitter_client.cpp's, over
	// SourceSharp's CClientSystem rather than a stock base.
	// client-only, and this classification was STALE. soundenvelope.cpp left
	// server_csharp.vpc in plan_gamedll_after.md Phase A3's SECOND pair, the same
	// way decals.cpp left in the first -- the server's sound envelope manager is
	// sourcesharp/game/server/shell_soundenvelope.cpp -- and this row went on
	// saying kShared. It did not matter while both sides named stock's base
	// unguarded; plan_cworld_trace.md T4 made it matter, because the accessor the
	// client's table needs is unguarded in a file the row claimed the server
	// compiles. The kClientOnly branch PINS the claim against the server build
	// rather than trusting it.
	// (soundenvelope.cpp left the client too -- sound cluster 17, lane c2-sims:
	// shell_soundenvelope_client.cpp -- so no DLL compiles it and its row went.)
	{ "sourcesharp/game/shared/sourcesharp_gamesystem.cpp", kShared },

	// client-only -- plan_gamedll_after.md Phase A3. A SHARED site until
	// CDecalEmitterSystem became managed and decals.cpp left the server's
	// project. Since plan_client_novalve C2 (lane c2-sims) decals.cpp left the
	// client too; the client's decalsystem is this binding, whose kClientSystems
	// base is right here and right unguarded.
	{ "sourcesharp/game/client/shell_decals_client.cpp", kClientOnly },

	// server-only -- the stock base must not appear at all
	{ "sourcesharp/game/server/physics.cpp",                        kServerOnly },
	// game/server/globalstate.cpp STOOD HERE and left the build entirely in
	// plan_shim.md Phase 2c: its table was already GlobalStateTable.cs, its last
	// caller (CWorld::Spawn's two platform globals) is now that class's own
	// level-init stage, and its two ConCommands are [ConCommand] classes in
	// SourceSharp.Server/ServerAdminCommands.cs. The row is removed rather than
	// left as a kServerOnly entry because every fact below OPENS the file and
	// asserts its text is not empty -- an absent file would fail this suite for
	// the reason the deletion exists.
	// game/server/soundent.h and game/server/ai_basenpc.h STOOD HERE. Both are
	// compiled by nothing since plan_server_novalve.md P5 and left the farm in
	// P6: a header no TU includes has no server line for a base to be named on.
	// Neither row contributed to nChecked (a kServerOnly file's whole fact is
	// that it names no base), so the floors below are unchanged by their going.
	{ "sourcesharp/game/server/precache_model.cpp",     kServerOnly },
	{ "sourcesharp/game/server/shell_server.cpp",       kServerOnly },
	{ "sourcesharp/game/server/shell_standalone.cpp",   kServerOnly },
	// shell_serversystems.cpp STOOD HERE and left with kServerSystems[]
	// (plan_server_native_v2 T4e); the native systems' stages are
	// shell_level.cpp's steps, which name no stock base either.
	{ "sourcesharp/game/server/shell_level.cpp",        kServerOnly },
};

const size_t kSiteCount = sizeof( kSites ) / sizeof( kSites[ 0 ] );

const char *kBaseHeader = "sourcesharp/game/server/shell_serversystem.h";
// The native systems' stages, one IShimLevel step each, since kServerSystems[]
// (shell_serversystems.cpp) went managed at plan_server_native_v2 T4e; the
// order is ServerSystems.cs.
const char *kStepsFile  = "sourcesharp/game/server/shell_level.cpp";
const char *kOrderFile  = "sourcesharp/managed/SourceSharp.Server/ServerSystems.cs";
// WHERE THE RATIONALE LIVES NOW. It used to be a comment beside the $File line
// in the .vpc; the scripts were reformatted with every comment stripped out to
// this document, verbatim and keyed the same way. The convention is unchanged --
// a removed file is NAMED somewhere greppable, so a $File line deleted with no
// explanation is still distinguishable from one deleted by accident -- only the
// place it is named moved.
// The one surviving guarded IGameSystem call, EndRestoreEntities'
// OnRestoreAllSystems. It was in game/server/gameinterface.cpp until
// plan_gamedll_after.md Phase A9 step 4 deleted that file; the restore trio
// went to shell_restoreentities.cpp and the guard went with it.

// Every position of a needle in a text, as line numbers.
std::vector<size_t> LinesContaining( const std::string &text, const char *pszNeedle )
{
	const std::vector<std::string> lines = Lines( text );
	std::vector<size_t> out;
	for ( size_t i = 0; i < lines.size(); i++ )
		if ( lines[ i ].find( pszNeedle ) != std::string::npos )
			out.push_back( i );
	return out;
}

int CountOf( const std::string &hay, const std::string &needle )
{
	int n = 0;
	size_t at = hay.find( needle );
	while ( at != std::string::npos ) { n++; at = hay.find( needle, at + needle.size() ); }
	return n;
}

} // namespace


//=============================================================================//
// 1. The T1 fact: a stock base may be named only where the CLIENT compiles it
//=============================================================================//

TEST( ServerSystemsUnderive, TheStockBasesAreNamedOnlyInsideAClientArm )
{
	static const char *kStockBases[] = { "CAutoGameSystem", "CBaseGameSystem" };

	int nChecked = 0;
	int nClientArmMentions = 0;

	for ( size_t f = 0; f < kSiteCount; f++ )
	{
		const std::string text = StripComments( Read( kSites[ f ].m_pszFile ) );
		ASSERT_FALSE( text.empty() ) << kSites[ f ].m_pszFile;

		const std::vector<Where> where = Regions( text );

		// A kClientOnly site's classification is PINNED, not trusted. The kind
		// says "the server's project does not compile this", which is what
		// makes an unguarded stock base correct there -- so if the server DOES
		// compile it, the kind is a way to wave a live file past every check
		// below, and that is the one thing this fact must not allow.
		if ( kSites[ f ].m_eKind == kClientOnly )
		{
			// Proof-of-read first: the server's own shell, so "not compiled"
			// below is not answered by a reader that saw the wrong list.
			ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer,
				"sourcesharp/game/server/shell_gamedll.cpp" ) )
				<< "server_sourcesharp (src/server/native/CMakeLists.txt) is not "
				   "the server's source list";

			// EXACT PATH, not a substring: a bare basename would match
			// shell_decals.cpp -- this tree has already been bitten by exactly
			// that shape, where an anchor on "class CRagdollProp" was satisfied
			// for its whole life by CRagdollPropAttached because the name is a
			// prefix.
			EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kSites[ f ].m_pszFile ) )
				<< kSites[ f ].m_pszFile << " is classified client-only and the "
				   "SERVER (server_sourcesharp) still compiles it. Either the classification "
				   "is wrong or the file came back, and both make every "
				   "unguarded stock base in it a link error";

			// AND THE CLASSIFICATION IS NOT VACUOUS EITHER: the file has to
			// name A GAME-SYSTEM BASE, or "unguarded is right here" is a
			// permission granted to nothing and the site is dead weight.
			//
			// RE-TAKEN at plan_cworld_trace.md T4. This used to require a STOCK
			// base, which was right while the client still had one; T4 took
			// stock's base off the client too, so decals.cpp names CClientSystem
			// now. The property this line is for -- "this site is about
			// something" -- is unchanged; what counts as a base is not.
			EXPECT_GT( CountOf( text, "CAutoGameSystem" ) + CountOf( text, "CBaseGameSystem" )
			         + CountOf( text, "CClientSystem" ), 0 )
				<< kSites[ f ].m_pszFile << " names no game-system base at all, so "
				   "it has no reason to be a site at all";
			continue;
		}

		for ( size_t b = 0; b < 2; b++ )
		{
			const std::vector<size_t> hits = LinesContaining( text, kStockBases[ b ] );
			for ( size_t h = 0; h < hits.size(); h++ )
			{
				const size_t line = hits[ h ];
				ASSERT_LT( line, where.size() );
				nChecked++;

				EXPECT_FALSE( where[ line ].m_bServer )
					<< kSites[ f ].m_pszFile << ":" << ( line + 1 )
					<< " names " << kStockBases[ b ] << " on a line the SERVER "
					   "compiles. That base's constructor appends the object to "
					   "the anonymous list in igamesystem.cpp, which is no "
					   "longer on the server's link line -- so this is a link "
					   "error today, and a system that registers into a list "
					   "nobody walks the moment somebody puts that file back";

				if ( where[ line ].m_bClient )
					nClientArmMentions++;
			}
		}

		// THE SAME SCAN OVER THE TWO SOURCESHARP BASES, and after
		// plan_cworld_trace.md T4 this is the half that carries the weight.
		//
		// The loop above now finds NOTHING on most of these files, because both
		// sides have left stock's base behind -- so on its own it is a fact that
		// passes because there is nothing to look at, which is this tree's
		// running failure. What replaced stock's base is two bases, one per side,
		// and each one is a COMPILE ERROR on the other side by construction:
		// shell_serversystem.h #errors under CLIENT_DLL and shell_clientsystem.h
		// #errors without it. So the arrangement T1 is about is still exactly
		// here, with both arms live, and it is checkable in both directions.
		for ( size_t b = 0; b < 2 && kSites[ f ].m_eKind == kShared; b++ )
		{
			// SHARED FILES ONLY, and that is a real distinction rather than a
			// convenience. `Regions` marks an UNGUARDED line as belonging to both
			// arms, which is correct -- and in a one-sided file it means every
			// unconditional line reads as "the client compiles this" when the
			// client does not compile the file at all. Asking the arm question of
			// a file only one project builds is asking a question with no subject;
			// the kServerOnly branch below asks the RIGHT one of those files,
			// which is "does this name the other side's base anywhere".
			static const char *kOurBases[] = { "CServerSystem", "CClientSystem" };
			const bool bIsClientBase = ( b == 1 );

			const std::vector<size_t> hits = LinesContaining( text, kOurBases[ b ] );
			for ( size_t h = 0; h < hits.size(); h++ )
			{
				const size_t line = hits[ h ];
				ASSERT_LT( line, where.size() );
				nChecked++;

				if ( bIsClientBase )
				{
					EXPECT_FALSE( where[ line ].m_bServer )
						<< kSites[ f ].m_pszFile << ":" << ( line + 1 )
						<< " names CClientSystem on a line the SERVER compiles. "
						   "shell_clientsystem.h refuses to define it there, so this "
						   "is a build failure -- and the arrangement it breaks is "
						   "T1's, pointing the way plan_cworld_trace.md T4 turned it";
					if ( where[ line ].m_bClient )
						nClientArmMentions++;
				}
				else
				{
					EXPECT_FALSE( where[ line ].m_bClient )
						<< kSites[ f ].m_pszFile << ":" << ( line + 1 )
						<< " names CServerSystem on a line the CLIENT compiles. "
						   "shell_serversystem.h refuses to define it there -- this is "
						   "T1's original direction and it has not stopped mattering";
				}
			}
		}

		// A server-only file must not name either base anywhere at all, which
		// is a stronger statement than "not on a server line" and is the one
		// that catches a base parked inside a CLIENT_DLL arm of a file the
		// client never compiles -- dead text that reads as a live decision.
		if ( kSites[ f ].m_eKind == kServerOnly )
		{
			EXPECT_EQ( 0, CountOf( text, "CAutoGameSystem" ) ) << kSites[ f ].m_pszFile;
			EXPECT_EQ( 0, CountOf( text, "CBaseGameSystem" ) ) << kSites[ f ].m_pszFile;
			// and, since T4, not the CLIENT's base either -- a server-only file
			// naming it is dead text that would not compile if it were live.
			EXPECT_EQ( 0, CountOf( text, "CClientSystem" ) ) << kSites[ f ].m_pszFile;
		}
	}

	// AND THE SCAN FOUND SOMETHING. A zero here would mean the tracker matched
	// nothing and every EXPECT_FALSE above passed vacuously -- which is the
	// state this fact was actually IN for the length of plan_cworld_trace.md T4's
	// first build, and is why the counter exists.
	EXPECT_GT( nChecked, 10 )
		<< "the conditional scan found no mention of any game-system base in any "
		   "of the " << kSiteCount << " files. Either the files moved or the "
		   "scanner is broken -- and a broken scanner passes this whole fact";

	// RE-TAKEN at T4, and it is the same statement about a different base. It
	// used to read "the shared systems must keep STOCK's base on the client
	// side", which was true while the client still compiled igamesystem.cpp and
	// is now exactly backwards. What has to stay true is that the scanner sees
	// BOTH ARMS: a shared file's client half names a base, and this fact can tell
	// which half it is in. Without it, every EXPECT_FALSE above could be passing
	// because the arm tracker answers "server: false" for every line in the file.
	EXPECT_GT( nClientArmMentions, 0 )
		<< "no file names a game-system base on a line the CLIENT compiles. The "
		   "shared systems still have a base on that side -- CClientSystem since "
		   "plan_cworld_trace.md T4 -- so a zero means the ARM TRACKER is not "
		   "tracking, and the server half of this fact is passing vacuously";
}


//=============================================================================//
// 2. The base declares exactly the stages the walkers dispatch
//=============================================================================//

TEST( ServerSystemsUnderive, TheBaseDeclaresExactlyTheStagesTheStepsRun )
{
	const std::string base  = StripComments( Read( kBaseHeader ) );
	const std::string steps = StripComments( Read( kStepsFile ) );
	ASSERT_FALSE( base.empty() );
	ASSERT_FALSE( steps.empty() );

	// The stages the BASE offers: every `virtual <type> Name()` in the header,
	// less the destructor and less Name, which is the system's label rather
	// than a dispatch point.
	std::set<std::string> declared;
	const std::vector<std::string> lines = Lines( base );
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trim( lines[ i ] );
		if ( t.compare( 0, 8, "virtual " ) != 0 ) continue;
		if ( t.find( '~' ) != std::string::npos ) continue;

		const size_t paren = t.find( "()" );
		if ( paren == std::string::npos ) continue;
		size_t start = t.find_last_of( " *&", paren - 1 );
		if ( start == std::string::npos ) continue;
		const std::string name = t.substr( start + 1, paren - start - 1 );

		if ( name.empty() || name == "Name" ) continue;
		declared.insert( name );
	}

	ASSERT_GE( declared.size(), 5u )
		<< "the base declares almost no stages -- the parse is broken, and a "
		   "broken parse makes both directions below pass vacuously";

	// The stages the STEPS run: every `PhysicsGameSystem()->X(` and
	// `GameStringSystem()->X(` in shell_level.cpp (plan_server_native_v2 T4e:
	// the walks went, each stage is one IShimLevel step).
	std::set<std::string> dispatched;
	static const char *kPrefixes[] = { "PhysicsGameSystem()->", "GameStringSystem()->" };
	for ( size_t p = 0; p < 2; p++ )
	{
		const std::string prefix = kPrefixes[ p ];
		size_t at = steps.find( prefix );
		while ( at != std::string::npos )
		{
			const size_t start = at + prefix.size();
			size_t end = start;
			while ( end < steps.size()
				&& ( isalnum( (unsigned char)steps[ end ] ) || steps[ end ] == '_' ) )
				end++;
			dispatched.insert( steps.substr( start, end - start ) );
			at = steps.find( prefix, end );
		}
	}

	// A stage on the base that no step runs is a hook a system can implement
	// and watch never fire -- the shape of the bug Phase 7 existed to prevent.
	for ( std::set<std::string>::const_iterator it = declared.begin(); it != declared.end(); ++it )
	{
		EXPECT_TRUE( dispatched.count( *it ) == 1 )
			<< "CServerSystem declares " << *it << " and no step in "
			<< kStepsFile << " runs it. A system overriding it would "
			   "compile, link, and never run";
	}

	// And the other way: the parse guard. If the needle stops matching,
	// `dispatched` collapses and the loop above fails loudly.
	EXPECT_GE( dispatched.size(), 5u )
		<< "the step scan found almost nothing -- it is the needle that broke";
}


//=============================================================================//
// 3. The base itself registers with nothing
//=============================================================================//

TEST( ServerSystemsUnderive, TheServerBaseHasNoConstructorSideEffectAndNoState )
{
	const std::string base = StripComments( Read( kBaseHeader ) );
	ASSERT_FALSE( base.empty() );

	// THE ONE PROPERTY THE WHOLE PHASE RESTS ON. Constructing one of these must
	// append to nothing: kServerSystems[] is the only thing that decides what
	// runs, and a base that registered would be a second opinion beside it.
	EXPECT_EQ( 0, CountOf( base, "Add(" ) );
	EXPECT_EQ( 0, CountOf( base, "Add( this )" ) );
	EXPECT_EQ( 0, CountOf( base, "m_pNext" ) )
		<< "the base has an intrusive list pointer -- that is stock's "
		   "CAutoGameSystem shape and it exists for exactly one purpose";

	// No file-scope state, which is this project's standing rule for
	// SourceSharp C++ and is also the reason igamesystem.cpp could not simply
	// be kept: it carried five statics, two of them the lists.
	const std::vector<std::string> lines = Lines( base );
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trim( lines[ i ] );
		EXPECT_TRUE( t.compare( 0, 7, "static " ) != 0 )
			<< kBaseHeader << ":" << ( i + 1 ) << " declares file-scope state";
	}

	// The save cluster is ABSENT ON PURPOSE, and it is recorded here so the
	// absence reads as a decision rather than an oversight. Adding OnSave back
	// would put a stage on the base that nothing can call: this build's engine
	// answers "Can't save multiplayer games", and shell_gamedll.cpp refuses
	// fifteen save methods out loud.
	EXPECT_EQ( 0, CountOf( base, "OnSave" ) );
	EXPECT_EQ( 0, CountOf( base, "OnRestore" ) );
	EXPECT_EQ( 0, CountOf( base, "SafeRemoveIfDesired" ) );

	// (Its client-include refusal went 2026-10-25: a server-only header carries
	// no CLIENT_DLL guard, user ruling; src/server/native is on no client
	// target's include path.)
}


//=============================================================================//
// 5. Stock's own game-system calls are all behind the SOURCESHARP guard
//=============================================================================//

TEST( ServerSystemsUnderive, StocksGameSystemCallsAreAllOutOfTheBuild )
{
	// READ THE ASSERTIONS, NOT THE NAME -- the name is still exactly right and
	// the way it is true changed underneath it.
	//
	// THIS FACT USED TO WALK A GUARD STACK. gameinterface.cpp kept stock's four
	// IGameSystem::Add calls and its nine *AllSystems calls under
	// `#ifndef SOURCESHARP`, and this fact proved every occurrence was inside
	// one of those arms -- because a grep for "IGameSystem" answers about a
	// whole file and says nothing about whether any occurrence is live.
	//
	// TWELVE WENT WITH STOCK'S CServerGameDLL at plan_gamedll_after.md Phase A9
	// step 3. The thirteenth, EndRestoreEntities' OnRestoreAllSystems, moved to
	// shell_restoreentities.cpp with the restore trio and kept its guard -- and
	// the close has now DELETED that arm too, with the other 452 arms the
	// compiler never saw. There is no guard left to walk, because there is no
	// call left to guard.
	//
	// "THE LAST ONE IS GONE" ALONE WOULD PASS OVER AN EMPTY FILE, and it would
	// also pass if EndRestoreEntities had been deleted wholesale -- which is the
	// opposite of what this phase decided. So the absence is asserted with what
	// it left standing: the function, its kept-whole body, and the ONE driver of
	// server systems this build has.
	// The file-walking half went with shell_restoreentities.cpp itself
	// (plan_server_native_min P1): the whole restore path is deleted, so there
	// is neither an IGameSystem:: call nor a kept-whole body left to count.
	// What must not come back is pinned on the live successor below.
	// AND THE LIVE SUCCESSOR, which is the whole reason stock's calls had to be
	// out of the build: server systems are driven by ONE written-down order.
	// That order is managed's since plan_server_native_v2 T4e (ServerSystems),
	// and its native half is one step per stage, which calls no stock driver.
	const std::string steps = StripComments( Read( kStepsFile ) );
	const std::string order = StripComments( Read( kOrderFile ) );
	ASSERT_FALSE( steps.empty() );
	ASSERT_FALSE( order.empty() );

	EXPECT_EQ( 0, CountOf( steps, "IGameSystem::" ) )
		<< "the native steps call stock's driver -- that list is anonymous and "
		   "unwalkable here";
	EXPECT_EQ( 1, CountOf( order, "public sealed class ServerSystems(" ) )
		<< "the one written-down order is gone";
	EXPECT_GE( CountOf( steps, "PhysicsGameSystem()->" ), 5 )
		<< "the native steps run almost no stage";
}


//=============================================================================//
// 6. CSourceSharpSystem is the client's, and the server's four bodies are not
//=============================================================================//

TEST( ServerSystemsUnderive, TheManagedGameSystemIsTheClientsAndItsBodiesAreNot )
{
	const char *kShared = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";
	const std::string text = StripComments( Read( kShared ) );
	ASSERT_FALSE( text.empty() );

	const std::vector<Where> where = Regions( text );

	// The class and its instance: CLIENT ONLY. Part 3 -- on a server build the
	// type is not instantiated and does not exist, which is the strongest form
	// of this phase's goal: not "a game system nobody registers" but no game
	// system.
	const std::vector<size_t> cls = LinesContaining( text, "class CSourceSharpSystem" );
	ASSERT_EQ( 1u, cls.size() );
	EXPECT_FALSE( where[ cls[ 0 ] ].m_bServer )
		<< "CSourceSharpSystem is compiled into the server again. It is a "
		   "CAutoGameSystemPerFrame: its constructor registers it, and the "
		   "kServerSystems[] row drives the same managed points, so every "
		   "managed participant would run twice a frame";
	EXPECT_TRUE( where[ cls[ 0 ] ].m_bClient )
		<< "CSourceSharpSystem left the CLIENT too. The client has no other "
		   "route to the managed runtime -- SourceSharp_ClientEarlyInit reaches "
		   "this object -- so the client would come up with no C# at all";

	const std::vector<size_t> inst = LinesContaining( text, "static CSourceSharpSystem g_SourceSharpSystem;" );
	ASSERT_EQ( 1u, inst.size() );
	EXPECT_FALSE( where[ inst[ 0 ] ].m_bServer );
	EXPECT_TRUE( where[ inst[ 0 ] ].m_bClient );

	// The four shared bodies: SERVER ONLY, and they must NOT have followed the
	// class into the client's guard. That is the failure that still links --
	// the client would compile them and the server's kServerSystems[] row would
	// have nothing to call, which is a link error, but a body moved into a
	// CLIENT_DLL arm of a file the server also compiles reads as fine.
	//
	// One body is left since plan_server_native_v2 T4e: the pre-entity edge the
	// world's precache reaches. The level and frame edges went with
	// kServerSystems[]; ServerSystems calls those itself.
	static const char *kBodies[] =
	{
		"void SourceSharp_ManagedLevelInitPreEntity(",
	};
	for ( size_t b = 0; b < sizeof( kBodies ) / sizeof( kBodies[ 0 ] ); b++ )
	{
		const std::vector<size_t> at = LinesContaining( text, kBodies[ b ] );
		ASSERT_EQ( 1u, at.size() ) << kBodies[ b ] << " -- expected exactly one definition";
		EXPECT_TRUE( where[ at[ 0 ] ].m_bServer )
			<< kBodies[ b ] << " is not compiled on the server. It is the "
			   "kServerSystems[] row's body";
		EXPECT_FALSE( where[ at[ 0 ] ].m_bClient )
			<< kBodies[ b ] << " is compiled on the client too -- these are the "
			   "server's event points and the client has its own";
	}
}
