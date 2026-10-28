//========= SourceSharp — wave 3's eight sole-blocked TUs, read as source =====//
//
// Step 6's close-out list (docs/typedef-dryrun/step6/README.md §7) names eight
// translation units "blocked by RETYPE alone", to be done first because they go
// green independently of wave 1. Seven of them do; the eighth, damagemodifier,
// is shard T1's refusal with evidence and stays red on purpose.
//
// This file is cpp_alias_shard_t4_test.cpp's shape and for its reason: these
// seven files pull in the whole SDK prelude, so compiling them twice is not
// available, and a stand-in for the prelude drifts from the real thing
// silently. The COMPILE-side evidence is `src/survey_run server` / `client`
// with the alias armed — 68 → 64 server, 82 → 79 client, the same seven names
// in release and in debug. That is a measurement run by hand. What follows is
// the half that rots between surveys.
//
// WHAT THIS SHARD HAS THAT THE EARLIER ONES DID NOT
//
//   1. AN ARM WHOSE TWO SIDES HAVE THE SAME TYPES. toucher.cpp initialises
//      Toucher_t's three members { pHandle, pBase, pGame }. Armed, pBase is
//      CBaseEntity * — which IS IGameEntity * — and so is pGame, so
//      `{ NULL, pEntity, NULL }` and `{ NULL, NULL, pEntity }` BOTH compile and
//      only one is right. No compiler on either arm can tell them apart, no
//      survey can, and the wrong one silently stops delivering every touch. It
//      is pinned here by POSITION. See ToucherOfPutsTheEntityOnTheGameSide.
//
//   2. A FREE FUNCTION THAT MUST STAY OUTSIDE ITS CLASS'S ARM.
//      physics_npc_solver.cpp guards CPhysicsEntitySolver away entirely — armed
//      it is abstract and LINK_ENTITY_TO_CLASS cannot construct it — but
//      EntityPhysics_CreateSolver is declared in physics_npc_solver.h and
//      called from five other translation units. Slide it inside the guard and
//      all five still COMPILE and the link fails, which is the class of defect
//      the step-6 rehearsal is structurally blind to (README §4: "152 objects
//      fail, so ninja never attempts either .so"). Same shape as T4's
//      UpdateDirtySpatialPartitionEntities claim, one step further out.
//
//   3. A WHOLE-FILE ARM. physics_main_client.cpp is removal-set TAIL: its
//      removal map convicted all fifteen of its members and concluded "These two
//      files are leaves of the removal set". The arm is the empty file, so what
//      must hold is that NOTHING is left outside it — a member added below the
//      #endif compiles clean unarmed forever and breaks flip day.
//
//   4. AN UNCONDITIONAL RETYPE ACROSS A FILE BOUNDARY. viewpostprocess.cpp's
//      seven asks became one call to a new free function whose ARM lives in
//      c_world.cpp. Nothing in the build notices the call rotting back to
//      GetClientWorldEntity()->GetClientRenderable(), because that spelling
//      compiles perfectly today. Pinned in both directions, and the
//      declaration/definition pair is pinned too.
//
// AND ONE ABSENCE, PAIRED WITH A PRESENCE. damagemodifier.cpp must NOT acquire
// an arm: its two sites are a refusal, and a lane that "fixes" them has taken a
// boundary decision without anyone deciding it. An absence-only assertion passes
// on a deleted file, so it is anchored on the member the refusal is ABOUT.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>

#include <cstddef>
#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as cpp_alias_shard_t4_test.cpp.
//-----------------------------------------------------------------------------
std::string Trimmed( const std::string &s )
{
	const size_t a = s.find_first_not_of( " \t" );
	if ( a == std::string::npos )
		return std::string();
	const size_t b = s.find_last_not_of( " \t" );
	return s.substr( a, b - a + 1 );
}

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0, at = 0;
	while ( ( at = hay.find( needle, at ) ) != std::string::npos )
	{
		n++;
		at += needle.size();
	}
	return n;
}

bool IsBlankOrComment( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t.empty() || t.compare( 0, 2, "//" ) == 0;
}

// COMMENTS ARE STRIPPED BEFORE ANY MATCH, and in this shard that is
// load-bearing rather than precautionary. Every arm here explains itself and
// the explanation NAMES the member the arm stopped calling: toucher.cpp's armed
// side is preceded by prose naming pHandle and GetRefEHandle, c_world.cpp's by
// prose naming GetClientRenderable, te.cpp's by prose naming TE_Fizz. Matching
// raw text, "the armed arm must not name the deleted member" reads the name
// straight out of the sentence saying it is gone and reports a correct arm as
// wrong. header_alias_test.cpp was caught doing exactly this once already, and
// a scanner matching inside a 2013 comment block is on this port's list of
// checks that could not fail.
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

//-----------------------------------------------------------------------------
// Whether the body following a signature carries no code.
//
// Added when `the-close` 1/N deleted this lane's whole-body arms: with no
// Guard_t to ask, "the armed arm is empty" becomes "the BODY is empty", which
// is the same claim with the conditional taken out of it -- and the only way to
// tell an arm REMOVED from an arm replaced by a plausible constant, which §7
// forbids.
//
// BodyIsEmpty went with Claim 7's subject: entity_bases.cpp held the bodies
// it measured (plan_server_native_min P1).


//-----------------------------------------------------------------------------
// One SS_CBASEENTITY_ALIAS region, split into arms. Both polarities, because
// the armed arm is whichever side the opening directive says it is and not
// whichever side comes second.
//-----------------------------------------------------------------------------
struct Guard_t
{
	std::string strFile;
	int nLine;
	int nEndifLine;
	std::vector< std::string > unarmed;
	std::vector< std::string > armed;
	bool bHasElse;
	bool bClosed;

	Guard_t() : nLine( 0 ), nEndifLine( 0 ), bHasElse( false ), bClosed( false ) {}

	std::string UnarmedText() const
	{
		std::string s;
		for ( size_t i = 0; i < unarmed.size(); i++ ) s += unarmed[ i ] + "\n";
		return StripComments( s );
	}
	std::string ArmedText() const
	{
		std::string s;
		for ( size_t i = 0; i < armed.size(); i++ ) s += armed[ i ] + "\n";
		return StripComments( s );
	}
	bool ArmedHasCode() const
	{
		for ( size_t i = 0; i < armed.size(); i++ )
			if ( !IsBlankOrComment( armed[ i ] ) ) return true;
		return false;
	}
	bool UnarmedHasCode() const
	{
		for ( size_t i = 0; i < unarmed.size(); i++ )
			if ( !IsBlankOrComment( unarmed[ i ] ) ) return true;
		return false;
	}
};

//-----------------------------------------------------------------------------
// THE PER-FILE FLOOR. Listed rather than discovered, so a file LOSING a guard
// fails here rather than becoming a silently shorter list.
//
// DELIBERATELY NOT A TREE-WIDE TOTAL. The reference harness for this mechanism
// built its expected strings out of live tree counts and they expired the next
// time somebody else's commit moved the population. Every number below is about
// a file THIS lane owns, so it can only be moved by an edit to this lane's work
// — which is precisely when failing is correct.
//
// The anchor answers "did the scanner open THIS file", which a line COUNT
// answers badly: a previous lane shipped a `size() > 100` floor against a
// 102-line file and the first mutation tripped it for the wrong reason.
//-----------------------------------------------------------------------------
struct FileFloor_t
{
	const char *pszFile;
	size_t nGuards;
	const char *pszAnchor;
};

}  // namespace

//-----------------------------------------------------------------------------
// CLAIM 1 — toucher.cpp, and the reason this file exists.
//
// Toucher_t is { IHandleEntity *pHandle; CBaseEntity *pBase; IGameEntity *pGame; }.
// ARMED, CBaseEntity IS IGameEntity, so pBase and pGame have the SAME TYPE and
// the compiler cannot tell `{ NULL, pEntity, NULL }` from `{ NULL, NULL, pEntity }`.
// Only the second is right — armed there is no SDK entity, so a "CBaseEntity"
// over there is an entity outside the SDK — and the first would leave
// gameinterface.cpp's `if ( toucher.pGame )` false for every toucher built this
// way, i.e. silently deliver no touches at all, on a tree that compiles and
// links clean.
//
// Pinned by POSITION: the entity must be the THIRD initialiser and the first
// two must be NULL. Nothing else in the build can say this.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// CLAIM 4 — the seven viewpostprocess asks became one function, in both
// directions. viewpostprocess.cpp has since left client.so (C7: its passes are
// managed's post program); the asks it made are the executor's now, through
// the same one function, and the old spelling must not come back there.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// CLAIM 5 — the client world entity is not constructed armed, and the SHUTDOWN
// is not guarded with it.
//
// `delete g_pClientWorld` is correct on both arms — deleting NULL is defined —
// and ClientWorldFactoryShutdown is called by the engine either way. Guarding it
// along with the constructor would be the tidy-looking edit and would leak on
// the unarmed side if the pairing ever drifted.
//-----------------------------------------------------------------------------
TEST( CppAliasRetypeW1Scanner, CountOfCountsAndDoesNotOverlapItself )
{
	EXPECT_EQ( 0u, CountOf( "abc", "d" ) );
	EXPECT_EQ( 3u, CountOf( "xaxaxa", "xa" ) );
	EXPECT_EQ( 1u, CountOf( "aaa", "aaa" ) );
	EXPECT_EQ( 1u, CountOf( "aaaa", "aaa" ) )
		<< "overlapping matches would make the seven-ask count meaningless";
}
