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

std::string Trimmed( const std::string &s )
{
	const size_t a = s.find_first_not_of( " \t" );
	if ( a == std::string::npos )
		return std::string();
	const size_t b = s.find_last_not_of( " \t" );
	return s.substr( a, b - a + 1 );
}

std::vector< std::string > ReadLines( const std::string &strRelative )
{
	std::vector< std::string > lines;
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return lines;

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return lines;

	std::string line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		lines.push_back( line );
	}
	return lines;
}

bool Contains( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
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

std::string FileText( const std::string &strFile )
{
	const std::vector< std::string > lines = ReadLines( strFile );
	std::string s;
	for ( size_t i = 0; i < lines.size(); i++ )
		s += lines[ i ] + "\n";
	return s;
}

std::string FileCode( const std::string &strFile )
{
	return StripComments( FileText( strFile ) );
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

bool IsGuardOpen( const std::string &strLine, bool *pbArmedFirst )
{
	const std::string t = Trimmed( strLine );
	if ( t == "#ifndef SS_CBASEENTITY_ALIAS"
		|| t == "#if !defined( SS_CBASEENTITY_ALIAS )"
		|| t == "#if !defined(SS_CBASEENTITY_ALIAS)" )
	{
		*pbArmedFirst = false;
		return true;
	}
	if ( t == "#ifdef SS_CBASEENTITY_ALIAS"
		|| t == "#if defined( SS_CBASEENTITY_ALIAS )"
		|| t == "#if defined(SS_CBASEENTITY_ALIAS)" )
	{
		*pbArmedFirst = true;
		return true;
	}
	return false;
}

bool IsAnyConditionalOpen( const std::string &strLine )
{
	return Trimmed( strLine ).compare( 0, 3, "#if" ) == 0;
}
bool IsEndif( const std::string &strLine )
{
	return Trimmed( strLine ).compare( 0, 6, "#endif" ) == 0;
}
bool IsElse( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t == "#else" || t.compare( 0, 5, "#elif" ) == 0;
}

std::vector< Guard_t > GuardsIn( const std::string &strFile )
{
	const std::vector< std::string > lines = ReadLines( strFile );
	std::vector< Guard_t > out;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		bool bArmedFirst = false;
		if ( !IsGuardOpen( lines[ i ], &bArmedFirst ) )
			continue;

		Guard_t g;
		g.strFile = strFile;
		g.nLine = (int)i + 1;

		int nDepth = 0;
		bool bInElse = false;
		size_t j = i + 1;
		for ( ; j < lines.size(); j++ )
		{
			const std::string &L = lines[ j ];
			if ( IsAnyConditionalOpen( L ) ) { nDepth++; }
			else if ( IsEndif( L ) )
			{
				if ( nDepth == 0 ) { g.bClosed = true; g.nEndifLine = (int)j + 1; break; }
				nDepth--;
			}
			else if ( IsElse( L ) && nDepth == 0 )
			{
				g.bHasElse = true; bInElse = true; continue;
			}

			const bool bThisIsArmed = ( bInElse != bArmedFirst );
			if ( bThisIsArmed ) g.armed.push_back( L );
			else                g.unarmed.push_back( L );
		}
		out.push_back( g );
		i = j;
	}
	return out;
}

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

// The client world's compiled home since step 5e. Named once, because two
// spellings of one path is how a repoint half-lands.
const char *kClientWorldImpl = "sourcesharp/game/client/world_adapter_client.cpp";

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
TEST( CppAliasRetypeW1, ToucherOfPutsTheEntityOnTheGameSide )
{
	// THE GUARD IS GONE AND THE CLAIM IS UNCHANGED, which is the whole reason
	// this fact is re-pointed rather than retired. `the-close` 1/N (f288a5e11)
	// deleted the `#ifndef` arm; the armed initialiser it selected is now the
	// only statement in the function, and it is STILL the one statement no
	// compiler and no survey can make for you -- armed, `pBase` and `pGame`
	// have the same type, so `{ pEntity, pEntity, NULL }` and
	// `{ NULL, NULL, pEntity }` compile identically and mean opposite things.
	// The wrong one leaves gameinterface.cpp's `if ( toucher.pGame )` false for
	// every toucher built this way: no touches delivered at all, on a tree that
	// compiles and links clean.
	//
	// SO THE POSITION CHECK IS THE FACT, and with the arm gone it is asked of
	// the function body instead of the arm's text -- which is strictly wider,
	// because it also catches the wrong initialiser written anywhere else in
	// this file.
	// AND THE STATEMENT MOVED, in plan_server_native_v2 T2: toucher.cpp
	// went (ToucherOf's one importer sat behind a constant NULL), and its
	// body is now written in place in PhysicsOwner_t::AsToucher, the one
	// caller left. The fact follows it.
	const std::string code = FileCode( "sourcesharp/game/shared/physicsowner.h" );
	ASSERT_FALSE( code.empty() ) << "physicsowner.h read as nothing";

	// PRESENCE, and it is the positive anchor for everything else here.
	ASSERT_TRUE( Contains( code, "inline Toucher_t PhysicsOwner_t::AsToucher() const" ) )
		<< "AsToucher is gone, and with it the one place a toucher is built "
		   "from an entity pointer.";
	EXPECT_TRUE( Contains( code, "return toucher;" ) );

	EXPECT_FALSE( Contains( code, "{ pBase, pBase, NULL }" ) )
		<< "the SDK initialiser is back. pBase is the SDK entity and there is "
		   "no such thing in this build.";

	EXPECT_TRUE( Contains( code, "Toucher_t toucher = { NULL, NULL, pBase };" ) )
		<< "AsToucher must put the entity on pGame (THIRD) and leave pHandle "
		   "and pBase NULL. pBase and pGame are the same type, so this is the "
		   "one statement no compiler and no survey can make for you.";

	// ...and the two wrong orders that also compile are named, so a mutation
	// that swaps them cannot pass by satisfying some looser rule.
	EXPECT_FALSE( Contains( code, "{ NULL, pBase, NULL }" ) )
		<< "the entity is on pBase armed. There is no SDK entity armed.";
	EXPECT_FALSE( Contains( code, "{ pBase, NULL, pBase }" ) )
		<< "pHandle armed would be an IGameEntity * where an IHandleEntity * is "
		   "wanted — the no-route kernel, which this lane does not solve.";
}

//-----------------------------------------------------------------------------
// CLAIM 4 — the seven viewpostprocess asks became one function, in both
// directions. viewpostprocess.cpp has since left client.so (C7: its passes are
// managed's post program); the asks it made are the executor's now, through
// the same one function, and the old spelling must not come back there.
//-----------------------------------------------------------------------------
TEST( CppAliasRetypeW1, TheWorldRenderableAskIsMadeOnceAndOnlyOnce )
{
	EXPECT_TRUE( FileCode( "game/client/viewpostprocess.cpp" ).empty() )
		<< "viewpostprocess.cpp is back in the farm; this claim's seven asks were its";

	const std::string code = FileCode( "sourcesharp/game/client/shell_view_executor_client.cpp" );
	ASSERT_FALSE( code.empty() );
	EXPECT_GE( CountOf( code, "GetClientWorldRenderable()" ), 2u )
		<< "the post ops' world renderable (a rectangle's and a mesh's bind) asks the one function";
	EXPECT_EQ( 0u, CountOf( code, "GetClientWorldEntity()->GetClientRenderable()" ) )
		<< "an ask rotted back to the C_BaseEntity member. It compiles today and "
		   "fails armed, and no test but this one is looking.";
}

TEST( CppAliasRetypeW1, TheWorldRenderableIsDeclaredAndDefinedOnceAndNeedsNoArm )
{
	// Declared in the header its caller includes...
	EXPECT_TRUE( Contains( FileCode( "game/client/c_world.h" ),
						   "void *GetClientWorldRenderable();" ) )
		<< "the declaration is gone from c_world.h; the executor includes that "
		   "header and nothing else declares it";

	// ...defined ONCE, in the shim beside the world entity.
	const std::string cpp = FileCode( kClientWorldImpl );
	EXPECT_EQ( 1u, CountOf( cpp, "void *GetClientWorldRenderable()" ) );

	// AND THE ARM IS RETIRED, WHICH IS A CHANGE OF ANSWER AND NOT A RELAXATION.
	//
	// This used to require a two-armed region here: unarmed
	// `GetClientWorldEntity()->GetClientRenderable()`, armed `return NULL`,
	// because armed C_BaseEntity was IGameEntity and GetClientRenderable was not
	// on it. Step 5e made C_World its own IClientEntity -- which IS an
	// IClientRenderable -- so GetClientRenderable() is the world's own virtual
	// and answers `this` on BOTH arms. There is nothing left for the flip to
	// change, so a region here would be a region with no subject.
	//
	// THE POINT IS THAT THE ANSWER GOT BETTER, not that the check got weaker:
	// the armed side used to hand every screen-space material proxy a NULL
	// binding token, and now it hands them the world.
	EXPECT_TRUE( GuardsIn( kClientWorldImpl ).empty() )
		<< "the client world shim has grown an SS_CBASEENTITY_ALIAS region. "
		   "C_World is not on the SDK hierarchy any more, so the flip cannot "
		   "reach it -- an arm here is answering a question nobody is asking.";

	EXPECT_TRUE( Contains( cpp, "return GetClientWorldEntity()->GetClientRenderable();" ) )
		<< "GetClientWorldRenderable no longer answers the world's own "
		   "renderable. NULL is the documented default of "
		   "DrawScreenSpaceRectangle's parameter and it is survivable, but it is "
		   "a WORSE answer than the one this build can now give and it must not "
		   "arrive by accident.";
}

//-----------------------------------------------------------------------------
// CLAIM 5 — the client world entity is not constructed armed, and the SHUTDOWN
// is not guarded with it.
//
// `delete g_pClientWorld` is correct on both arms — deleting NULL is defined —
// and ClientWorldFactoryShutdown is called by the engine either way. Guarding it
// along with the constructor would be the tidy-looking edit and would leak on
// the unarmed side if the pairing ever drifted.
//-----------------------------------------------------------------------------
TEST( CppAliasRetypeW1, TheClientWorldIsConstructedOnBothArmsAndStillFreed )
{
	// THIS CLAIM INVERTED IN STEP 5e AND THE INVERSION IS THE POINT.
	//
	// It read: `new C_World` is behind an arm, and armed there is nothing to put
	// in the #else because C_World is abstract -- C_BaseEntity armed is
	// IGameEntity, six pure virtuals the class did not implement. C_World
	// implements the engine's client entity chain itself now, so it is
	// constructible on both arms and the region has no subject. An arm that
	// still refused to build one would leave the client with no world at all in
	// a build where it can perfectly well have one.
	const std::vector< Guard_t > guards = GuardsIn( kClientWorldImpl );
	ASSERT_EQ( 0u, guards.size() )
		<< "the client world shim carries alias-conditional regions again.";

	const std::vector< std::string > lines = ReadLines( kClientWorldImpl );
	ASSERT_FALSE( lines.empty() ) << kClientWorldImpl << " read as empty -- "
		"every absence below would be reported by an unopened file";

	int nNew = 0, nDelete = 0;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string code = StripComments( lines[ i ] );
		if ( Contains( code, "new C_World" ) )
			nNew = (int)i + 1;
		if ( Contains( code, "delete g_pClientWorld" ) )
			nDelete = (int)i + 1;
	}

	ASSERT_NE( 0, nNew ) << "ClientWorldFactoryInit no longer builds a world. "
		"The client would join a map with nothing at entity 0, and "
		"the engine's client-side trace dereferences what it finds "
		"there.";
	ASSERT_NE( 0, nDelete ) << "ClientWorldFactoryShutdown no longer frees the world";
}

// CLAIM 7 went with entity_bases.cpp (plan_server_native_min P1): the file
// was compiled by nothing, included a cbase.h the farm lacks, and its four
// bodies answered questions armed code can no longer ask -- no ServerClass
// instantiates the four helper bases, so no caller exists to check the
// derivations against.
TEST( CppAliasRetypeW1Scanner, StripCommentsRemovesBothCommentKinds )
{
	EXPECT_EQ( "a\nb", StripComments( "a// gone\nb" ).substr( 0, 3 ) );
	EXPECT_EQ( "ab", StripComments( "a/* gone */b" ) );
	EXPECT_EQ( "a", StripComments( "a/* multi\nline\ngone */" ) );
	// ...and does not eat code.
	EXPECT_EQ( "x / y", StripComments( "x / y" ) );
}

TEST( CppAliasRetypeW1Scanner, StrippingIsWhatStopsTheProseFalsePositive )
{
	// This shard's armed arms are all preceded by prose naming the member they
	// stopped calling. Demonstrated on synthetic input so the mechanism is
	// proven whether or not the tree currently happens to trip it.
	const std::string withProse =
		"#ifndef SS_CBASEENTITY_ALIAS\n"
		"\treturn GetClientWorldEntity()->GetClientRenderable();\n"
		"#else\n"
		"\t// GetClientRenderable is a C_BaseEntity member and is gone armed.\n"
		"\treturn NULL;\n"
		"#endif\n";
	EXPECT_TRUE( Contains( withProse, "GetClientRenderable" ) )
		<< "the raw text DOES contain the needle, in a comment";
	EXPECT_FALSE( Contains( StripComments( withProse ).substr(
							  StripComments( withProse ).find( "#else" ) ),
						  "GetClientRenderable" ) )
		<< "stripped, the armed arm does not";
}

TEST( CppAliasRetypeW1Scanner, BothGuardPolaritiesAreRecognised )
{
	bool bArmedFirst = true;
	EXPECT_TRUE( IsGuardOpen( "#ifndef SS_CBASEENTITY_ALIAS", &bArmedFirst ) );
	EXPECT_FALSE( bArmedFirst );
	EXPECT_TRUE( IsGuardOpen( "\t#ifdef SS_CBASEENTITY_ALIAS", &bArmedFirst ) );
	EXPECT_TRUE( bArmedFirst );
	EXPECT_TRUE( IsGuardOpen( "#if !defined( SS_CBASEENTITY_ALIAS )", &bArmedFirst ) );
	EXPECT_FALSE( bArmedFirst );
	EXPECT_TRUE( IsGuardOpen( "#if defined( SS_CBASEENTITY_ALIAS )", &bArmedFirst ) );
	EXPECT_TRUE( bArmedFirst );
	EXPECT_FALSE( IsGuardOpen( "#ifndef SOMETHING_ELSE", &bArmedFirst ) );
	// A guard whose macro is RENAMED is what the negative control does to the
	// whole tree; the scanner must stop seeing it, or "0 defeated" is a check
	// that cannot fail.
	EXPECT_FALSE( IsGuardOpen( "#ifndef SS_CBASEENTITY_ALIAS_DEFEATED", &bArmedFirst ) );
}

TEST( CppAliasRetypeW1Scanner, CountOfCountsAndDoesNotOverlapItself )
{
	EXPECT_EQ( 0u, CountOf( "abc", "d" ) );
	EXPECT_EQ( 3u, CountOf( "xaxaxa", "xa" ) );
	EXPECT_EQ( 1u, CountOf( "aaa", "aaa" ) );
	EXPECT_EQ( 1u, CountOf( "aaaa", "aaa" ) )
		<< "overlapping matches would make the seven-ask count meaningless";
}
