//====== SourceSharp — A MANAGED ENTITY EXISTS AND A TOOL CANNOT SEE IT ========
//
// Lane diag-visibility. Two diagnostics that walked the SDK hierarchy and
// therefore reported a world that was mostly absent, and the properties that
// keep them honest.
//
// WHY THIS MATTERS BEYOND TIDINESS. Every lane in this port uses these tools to
// decide whether its work happened. A diagnostic that silently omits managed
// entities is how a lane concludes "nothing was created" and goes looking for a
// bug that is not there; two lanes have already been misled that way.
//
//   report_entities  walked gEntList.FirstServerEntity()/NextServerEntity(),
//                    which is CEntInfo. Step 2c took the managed shells out of
//                    CEntInfo. MEASURED ON dm_lockdown BEFORE THE FIX: the
//                    command printed NOT ONE LINE while `./debug-api ents "*"` on
//                    the same running game read 471.
//
//   InvalidateMdlCache  walked ClientEntityList().FirstBaseEntity(), which
//                    answers C_BaseEntity. C_StandaloneEntity is not one.
//                    MEASURED: zero entities reached, no output at all, and a
//                    body that looked exactly like a working one.
//
// SOURCE-READ, and forced rather than lazy: every file below includes cbase.h's
// whole world and none can be linked into a gtest. The behavioural halves are
// the managed facts (ClientMdlCacheFlushTests, fifteen of them) and the live
// gate. What no other test can reach is the C++ shape, and it has properties a
// reader cannot check by eye and a compiler cannot check at all -- above all
// PreprocessorNesting below, which is the one that actually bit.
//
//=============================================================================//

#include <cctype>
#include <string>
#include <vector>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

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

// Every comment removed, so a fact about CODE cannot be answered by PROSE.
// Every file below describes at length the very operations it performs -- this
// one included -- so an unstripped search finds the paragraph explaining a
// thing and passes over a body that no longer does it. That is this tree's
// standing "check that cannot fail" shape, and there are 43 of them so far.
//
// NEWLINES ARE PRESERVED: PreprocessorNesting counts lines, and a stripper that
// collapsed them would report every conditional at line 1.
std::string StripComments( const std::string &src )
{
	std::string out;
	out.reserve( src.size() );

	for ( size_t i = 0; i < src.size(); )
	{
		if ( src[ i ] == '/' && i + 1 < src.size() && src[ i + 1 ] == '/' )
		{
			while ( i < src.size() && src[ i ] != '\n' ) i++;
			continue;
		}
		if ( src[ i ] == '/' && i + 1 < src.size() && src[ i + 1 ] == '*' )
		{
			i += 2;
			while ( i + 1 < src.size() && !( src[ i ] == '*' && src[ i + 1 ] == '/' ) )
			{
				if ( src[ i ] == '\n' )
					out += '\n';
				i++;
			}
			i = ( i + 1 < src.size() ) ? i + 2 : src.size();
			continue;
		}
		out += src[ i++ ];
	}

	return out;
}

//-----------------------------------------------------------------------------
// IS THIS LINE COMPILED WHEN <symbol> IS DEFINED?
//
// THE TEST THIS FILE EXISTS FOR. The first version of the client's binding was
// written beside the server's, under its own `#ifdef CLIENT_DLL` -- inside the
// 350-line `#ifndef CLIENT_DLL` that surrounds that whole region. The build was
// GREEN, the crossing FIRED, C# printed its line, and the count in it was 0.
// sourcesharp_gamesystem.cpp's own header records that trap costing an earlier
// lane one build; it cost this one a live run.
//
// Nothing else catches it. A compiler cannot: the block is simply not there. A
// grep cannot: the text is present and correct. Only the NESTING says so.
//
// A tiny evaluator: walk the conditionals, keep a stack, and answer whether the
// line at `at` survives with `pszSymbol` defined. Conditions this does not
// understand are treated as TAKEN, which is the conservative direction -- it
// can only make this fact easier to pass, never harder, so a pass is not an
// artefact of the parser giving up.
//-----------------------------------------------------------------------------
//
// TWO QUESTIONS, ONE WALK. "Not compiled on the client" and "compiled on the
// server" are DIFFERENT FACTS, and a server-only binding needs both: an
// `#ifdef CLIENT_DLL` nested inside the 350-line `#ifndef CLIENT_DLL` is
// compiled on NEITHER side, so it answers the first question correctly while
// binding nothing at all. Lane `abi-tail` found that by mutating a server-arm
// fact into exactly that shape and watching it stay GREEN.
bool CompiledWithSymbol( const std::string &src, size_t at, const char *pszSymbol,
                         bool bDefined )
{
	const std::string sym( pszSymbol );

	// live[] is whether each open conditional's current arm is taken.
	std::vector<bool> live;
	size_t line = 0;

	while ( line < src.size() && line <= at )
	{
		const size_t end = src.find( '\n', line );
		const std::string text = src.substr( line, ( end == std::string::npos )
		                                           ? std::string::npos : end - line );

		size_t h = text.find_first_not_of( " \t" );
		if ( h != std::string::npos && text[ h ] == '#' )
		{
			const std::string d = text.substr( h + 1 );
			const bool mentions = d.find( sym ) != std::string::npos;

			if ( d.compare( 0, 6, "ifndef" ) == 0 )
				live.push_back( mentions ? !bDefined : true );
			else if ( d.compare( 0, 5, "ifdef" ) == 0 )
				live.push_back( mentions ? bDefined : true );
			else if ( d.compare( 0, 2, "if" ) == 0 )
				live.push_back( true );
			else if ( d.compare( 0, 4, "else" ) == 0 && !live.empty() )
				live.back() = !live.back();
			else if ( d.compare( 0, 5, "endif" ) == 0 && !live.empty() )
				live.pop_back();
		}

		if ( end == std::string::npos )
			break;
		line = end + 1;
	}

	for ( size_t i = 0; i < live.size(); i++ )
	{
		if ( !live[ i ] )
			return false;
	}

	return true;
}

bool CompiledWhenDefined( const std::string &src, size_t at, const char *pszSymbol )
{
	return CompiledWithSymbol( src, at, pszSymbol, true );
}

bool CompiledWhenNotDefined( const std::string &src, size_t at, const char *pszSymbol )
{
	return CompiledWithSymbol( src, at, pszSymbol, false );
}

const char *kStandBase    = "sourcesharp/game/server/shell_standalone_base.cpp";
const char *kRelays       = "sourcesharp/game/server/standalone_relays.cpp";
const char *kGameSystem   = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";
// Since plan_engineapi_vtables C6c a server slot's binding is its shim-area
// method's forward, in the file sourcesharp_gamesystem.cpp includes inside
// `#ifndef CLIENT_DLL`. What side a binding is on is the side of that include.
const char *kAreasImpl    = "sourcesharp/game/shared/shell_shim_areas.inc";
const char *kAreasInclude = "#include \"shell_shim_areas.inc\"";
// And since plan_client_vtables CC-last the CLIENT's binding is its shim-area
// method's forward too, in the file included inside `#ifdef CLIENT_DLL`.
const char *kClientAreasImpl    = "sourcesharp/game/shared/shell_shim_areas_client.inc";
const char *kClientAreasInclude = "#include \"shell_shim_areas_client.inc\"";
// GENERATED by tools/entrypoint-decls from SourceSharpEngineApi's field
// signatures and from docs/engineapi-areas.tsv's per-side rows (plan_shim.md
// §5 3e; the fill block it first read is gone).
const char *kEntryPoints  = "sourcesharp/game/shared/sourcesharp_entrypoints.h";
const char *kClientShell  = "sourcesharp/game/client/shell_clientdll.cpp";
const char *kClientStand  = "sourcesharp/game/client/shell_standalone_client.cpp";
const char *kClientStandH = "sourcesharp/game/client/shell_standalone_client.h";

//-----------------------------------------------------------------------------
// HOW MANY GENERATED SHIM-AREA FORWARDS IN `inc` ARE `CShim...::<slot>( ... )`
// WITH THE ONE-LINE BODY `{ return ::<body>( ... ); }` -- or with ANY body when
// `body` is empty. Since plan_client_vtables CC-last a slot is wired by such a
// forward on each side: shell_shim_areas.inc on the server,
// shell_shim_areas_client.inc on the client (both GENERATED by tools/shim-areas
// from docs/engineapi-areas.tsv), and the struct fill that used to say
// `engineApi.<Slot> = <Body>;` is gone. Whitespace is ignored, so the
// generator's line wrapping is not part of the needle; the class name must
// carry "CShim", so the body's own `::<body>(` never counts as a slot.
//-----------------------------------------------------------------------------
int ForwardCount( const std::string &inc, const std::string &slot, const std::string &body )
{
	int n = 0;
	const std::string needle = "::" + slot + "(";
	for ( size_t at = inc.find( needle ); at != std::string::npos; at = inc.find( needle, at + 1 ) )
	{
		size_t b = at;
		while ( b > 0 && ( isalnum( (unsigned char)inc[ b - 1 ] ) || inc[ b - 1 ] == '_' ) )
			b--;
		if ( inc.substr( b, at - b ).find( "CShim" ) == std::string::npos )
			continue;

		const size_t semi = inc.find( ';', at );
		std::string window;
		for ( size_t k = at; k < inc.size() && k <= semi; k++ )
		{
			if ( !isspace( (unsigned char)inc[ k ] ) )
				window += inc[ k ];
		}
		if ( window.find( "){return::" + body ) != std::string::npos
		     && ( body.empty() || window.find( "){return::" + body + "(" ) != std::string::npos ) )
			n++;
	}
	return n;
}

} // namespace

//=============================================================================
// SUBJECT 1 -- report_entities
//=============================================================================

//-----------------------------------------------------------------------------
// report_entities' native half is gone (server gc cull, 2026-10-01).
// ShellLessEntity_LiveCount and SourceSharp_StandaloneServerAt had no caller
// left, and server.so's --print-gc-sections link dropped both, so they were
// deleted. Each file is proved read first.
//-----------------------------------------------------------------------------
TEST( ReportEntitiesSources, TheNativeHalfIsGone )
{
	const std::string relays = StripComments( Read( kRelays ) );
	const std::string stand = StripComments( Read( kStandBase ) );
	ASSERT_NE( std::string::npos, relays.find( "const char *StandaloneEntity_Name(" ) ) << kRelays << " did not read";
	ASSERT_NE( std::string::npos, stand.find( "CStandaloneEntity" ) ) << kStandBase << " did not read";

	EXPECT_EQ( std::string::npos, relays.find( "int ShellLessEntity_LiveCount()" ) )
		<< "the shell-less count is defined again; say which caller needs it";
	EXPECT_EQ( std::string::npos, stand.find( "SourceSharp_StandaloneServerAt(" ) )
		<< "the standalone accessor is defined again; say which caller needs it";
}

//=============================================================================
// SUBJECT 2 -- InvalidateMdlCache on the client
//=============================================================================

//-----------------------------------------------------------------------------
// THE SHELL NO LONGER WALKS ANYTHING. It hands the engine's question to C#,
// which is what the SERVER half has done since plan_gamedll_after.md Phase A2
// and what LANE_BRIEF.md requires of a client interface.
//-----------------------------------------------------------------------------
TEST( ClientMdlCacheSources, TheShellForwardsToManagedAndDoesNotWalk )
{
	const std::string src = StripComments( Read( kClientShell ) );
	ASSERT_FALSE( src.empty() );

	const size_t at = src.find( "void CShellClientDLL::InvalidateMdlCache()" );
	ASSERT_NE( at, std::string::npos ) << "the method is gone";

	const std::string body = src.substr( at, 700 );

	EXPECT_NE( body.find( "ClientInvalidateMdlCache" ), std::string::npos )
		<< "the flush must arrive in C#, as the server's does";

	EXPECT_EQ( body.find( "FirstBaseEntity()" ), std::string::npos )
		<< "the C_BaseEntity walk must be GONE from here. It reached zero "
		<< "managed entities -- C_StandaloneEntity is not a C_BaseEntity -- and "
		<< "left the shell looking like a working implementation.";

	EXPECT_EQ( body.find( "dynamic_cast" ), std::string::npos )
		<< "and the dynamic_cast with it: plan_entity.md gate 3 counts these "
		<< "and requires zero.";
}

//-----------------------------------------------------------------------------
// THE SDK HALF WAS NEVER COMPILED, AND IS NOW GONE FROM THE SOURCE TOO.
//
// READ THE ASSERTIONS, NOT THE OLD NAME. This fact was
// `TheWalkCoversBothHierarchies` and it required
// `ClientEntityList().FirstBaseEntity()` to be present, on the argument that
// the SDK arm frees C_BaseAnimating::m_pStudioHdr and no managed walk reaches
// it. THAT ARM WAS INSIDE `#ifndef SS_CBASEENTITY_ALIAS` FOR ITS WHOLE LIFE.
// SS_CBASEENTITY_ALIAS is defined in every build the mod ships, so the arm was
// never compiled, never linked and never freed anything -- the fact was green
// and measuring text. `git show f288a5e11 -- sourcesharp/game/client/shell_clientdll.cpp`
// is the deletion; the guard is what makes it a no-op rather than a loss.
//
// So it is rewritten to the pair R1 asks for: the ABSENCE, plus the LIVE
// SUCCESSOR. The absence is checked over the WHOLE FILE and not just the walk's
// body -- a re-point that only looked inside the function would go green again
// if the arm came back in a dead guard three lines below it -- and the file's
// guard count is pinned at zero so "gone" cannot mean "moved into another arm
// the compiler will not see".
//-----------------------------------------------------------------------------
TEST( ClientMdlCacheSources, TheWalkIsTheStandaloneHierarchyAloneAndTheSdkArmIsGone )
{
	const std::string shell = StripComments( Read( kClientShell ) );
	ASSERT_FALSE( shell.empty() );

	const size_t at = shell.find( "SourceSharp_ClientEntitiesInvalidateMdlCacheAll( void )" );
	ASSERT_NE( at, std::string::npos ) << "the client's walk is gone";

	const std::string body = shell.substr( at, 1400 );

	// THE LIVE SUCCESSOR, first: the half that is compiled must still be here
	// and its count must still be what the function answers. "The SDK arm is
	// gone" on its own would pass over an empty function.
	EXPECT_NE( body.find( "SourceSharp_StandaloneClientsInvalidateBoneCaches()" ),
	           std::string::npos )
		<< "the standalone half is the ONLY half now; without it the walk reaches "
		<< "nothing at all and still returns a number.";
	EXPECT_NE( body.find( "return nInvalidated + SourceSharp_StandaloneClientsInvalidateBoneCaches();" ),
	           std::string::npos )
		<< "the standalone half's count must be what the walk RETURNS. A call "
		<< "whose result is dropped flushes the caches and reports zero, which is "
		<< "the state TheWalkAnswersHowManyItReached exists to stop.";

	// AND THE ABSENCE, over the whole file.
	EXPECT_EQ( shell.find( "FirstBaseEntity()" ), std::string::npos )
		<< "the stock-list walk is back in shell_clientdll.cpp. It cannot be "
		<< "revived here: C_StandaloneEntity is not a C_BaseEntity, so the list it "
		<< "walks is empty in this mod, and gate 2 counts naming that list.";
	EXPECT_EQ( shell.find( "SS_CBASEENTITY_ALIAS" ), std::string::npos )
		<< "a guarded arm is back in this file. An arm the compiler never sees is "
		<< "a fact that cannot fail -- which is exactly what the arm this fact "
		<< "used to require turned out to be.";
}

//-----------------------------------------------------------------------------
// AND THE PARALLEL HALF IS IN THE SHIM FILE THAT OWNS THE LIST, which is the
// other side of a split that is NOT arbitrary: client_entity_resolve_test.cpp's
// clause-2 gate waives shell_clientdll.cpp by name for the iteration of the
// stock list and waives no other shim file. Writing both halves in one place
// would have meant adding a row to that waiver.
//-----------------------------------------------------------------------------
TEST( ClientMdlCacheSources, TheStandaloneHalfWalksTheHierarchysOwnList )
{
	// THE CLIENT'S SKELETON IS MANAGED (plan physics_bones_native_zero 1.2,
	// client half): the standalone half has no native cache to clear and
	// answers 0, and the flush reaches the skeletons in C# -- ClientModule's
	// InvalidateMdlCache calls ClientSkeletons.InvalidateMdlCache and adds
	// what it reached to the count (ClientSkeletonsTests.
	// AModelCacheFlushInvalidatesEverySkeletonAndDropsTheImages).
	const std::string src = StripComments( Read( kClientStand ) );
	ASSERT_FALSE( src.empty() );

	const size_t at = src.find( "SourceSharp_StandaloneClientsInvalidateBoneCaches( void )" );
	ASSERT_NE( at, std::string::npos ) << "the standalone half is gone";

	const std::string body = src.substr( at, 300 );

	EXPECT_NE( body.find( "return 0;" ), std::string::npos );
	EXPECT_EQ( body.find( "InvalidateBoneCache()" ), std::string::npos )
		<< "a native bone cache is being cleared again: there should be none";

	const std::string managed = StripComments( Read( "sourcesharp/managed/SourceSharp.Client/ClientModule.cs" ) );
	ASSERT_FALSE( managed.empty() );
	EXPECT_NE( managed.find( "MdlCacheEntitiesInvalidated += _skeletons?.InvalidateMdlCache() ?? 0;" ),
	           std::string::npos )
		<< "the client module's flush no longer reaches the managed skeletons";

	EXPECT_EQ( body.find( "ClientEntityList()" ), std::string::npos )
		<< "and it must NOT name the stock client list: this file is not on "
		<< "client_entity_resolve_test.cpp's waiver, and putting it there to "
		<< "make a walk fit is widening a gate rather than meeting it.";
}

//-----------------------------------------------------------------------------
// IT ANSWERS HOW MANY. The number is the whole difference between an empty body
// and a working one, from outside.
//-----------------------------------------------------------------------------
TEST( ClientMdlCacheSources, TheWalkAnswersHowManyItReached )
{
	const std::string src = StripComments( Read( kClientShell ) );
	ASSERT_FALSE( src.empty() );

	EXPECT_NE( src.find( "int32_t SourceSharp_ClientEntitiesInvalidateMdlCacheAll( void )" ),
	           std::string::npos )
		<< "the walk must answer a COUNT rather than void. A flush leaves no "
		<< "trace a running game can be asked about, so a void walk that reached "
		<< "nothing is indistinguishable from one that worked -- which is exactly "
		<< "the state this half was found in.";
}

//-----------------------------------------------------------------------------
// THERE IS NO TYPE TEST LEFT TO MAKE A VIRTUAL, BECAUSE THERE IS ONE HIERARCHY.
//
// READ THE ASSERTIONS, NOT THE OLD NAME. This was
// `TheTypeTestIsAVirtualAndNotACast` and it REQUIRED `GetBaseAnimating()` in
// the walk -- gate 3's remedy for the dynamic_cast that used to be there. The
// branch it named lived in the same `#ifndef SS_CBASEENTITY_ALIAS` arm as the
// stock-list walk above and was never compiled either; both went at f288a5e11.
//
// The gate-3 property it was really defending is UNCHANGED and is asserted
// here in full: no cast of any gated spelling reaches an entity in this walk.
// What has changed is why that is true. It used to be true because a virtual
// stood in for a cast; it is now true because every entity the walk reaches is
// a C_StandaloneEntity and the method it calls is that type's own -- no
// narrowing is being done, so there is nothing for a cast to do.
//-----------------------------------------------------------------------------
TEST( ClientMdlCacheSources, TheWalkNarrowsNothingSoItCastsNothing )
{
	const std::string src = StripComments( Read( kClientShell ) );
	ASSERT_FALSE( src.empty() );

	const size_t at = src.find( "SourceSharp_ClientEntitiesInvalidateMdlCacheAll( void )" );
	ASSERT_NE( at, std::string::npos );

	const std::string body = src.substr( at, 1200 );

	// THE LIVE SUCCESSOR: the walk delegates to the hierarchy's own list, whose
	// element type already has the method. Without this clause the three
	// absences below would all hold over an empty function.
	EXPECT_NE( body.find( "SourceSharp_StandaloneClientsInvalidateBoneCaches" ), std::string::npos )
		<< "the walk delegates to the standalone hierarchy, which is what makes "
		<< "'no cast' a description of working code rather than of no code.";

	EXPECT_EQ( body.find( "dynamic_cast" ), std::string::npos )
		<< "no dynamic_cast may come back with the walk; gate 3 counts them and "
		<< "requires zero.";
	EXPECT_EQ( body.find( "static_cast<C_BaseAnimating" ), std::string::npos )
		<< "and a static_cast is a WRONG answer for every entity that is not one";
	EXPECT_EQ( body.find( "GetBaseAnimating()" ), std::string::npos )
		<< "GetBaseAnimating is a C_BaseEntity member, and this walk reaches no "
		<< "C_BaseEntity. A branch on it here would be a branch that is never "
		<< "taken, dressed as a type test.";
}

//-----------------------------------------------------------------------------
// THE CLIENT SHELL'S INVALIDATOR CLEARS BOTH THINGS, and it is the server's
// two lines rather than a new idea (shell_standalone.h:588).
//-----------------------------------------------------------------------------
TEST( ClientMdlCacheSources, TheClientShellsInvalidatorMirrorsTheServers )
{
	// Both shells' invalidators went with their native skeletons (plan
	// physics_bones_native_zero 1.2, each side's half). The client's is
	// ClientSkeletons.InvalidateMdlCache: every state's key reset and its
	// attachments marked stale (BoneEntityState.InvalidateBoneCache, the two
	// lines this fact used to read), and the model images dropped.
	const std::string src = StripComments( Read( kClientStandH ) );
	ASSERT_GT( src.size(), 5000u );

	EXPECT_EQ( src.find( "void InvalidateBoneCache()" ), std::string::npos )
		<< "a native bone cache is back on the client shell";
	EXPECT_EQ( src.find( "m_BoneCacheKey" ), std::string::npos );
}

//=============================================================================
// THE ONE THAT ACTUALLY BIT
//=============================================================================

//-----------------------------------------------------------------------------
// THE CLIENT'S BINDING IS COMPILED ON THE CLIENT.
//
// Written beside the server's, it lands inside the 350-line
// `#ifndef CLIENT_DLL` -- and then the build is green, the crossing fires, C#
// prints its line, and the number in it is 0. Nothing but the NESTING says so.
//-----------------------------------------------------------------------------
TEST( ClientMdlCacheSources, TheClientBindingIsNotInsideTheServerOnlyBlock )
{
	const std::string src = StripComments( Read( kGameSystem ) );
	const std::string impl = StripComments( Read( kClientAreasImpl ) );
	ASSERT_FALSE( src.empty() );
	ASSERT_FALSE( impl.empty() );

	// The client's binding is CShimModelsClient's forward since
	// plan_client_vtables CC-last; the struct fill that held
	// `engineApi.EntitiesInvalidateMdlCacheAll = ...` is gone.
	ASSERT_EQ( 1, ForwardCount( impl, "EntitiesInvalidateMdlCacheAll",
	                            "SourceSharp_ClientEntitiesInvalidateMdlCacheAll" ) )
		<< "the client arm of EntitiesInvalidateMdlCacheAll is gone; with it "
		<< "unbound, Engine.cs answers its null check and the flush reaches "
		<< "nothing on this side -- silently.";

	const size_t at = src.find( kClientAreasInclude );
	ASSERT_NE( at, std::string::npos ) << "the client's area forwards are not included";

	EXPECT_TRUE( CompiledWhenDefined( src, at, "CLIENT_DLL" ) )
		<< "the client's forwards are included inside a conditional that is "
		<< "FALSE when CLIENT_DLL is defined -- almost certainly the "
		<< "`#ifndef CLIENT_DLL` around the server's. It compiles, it links, the "
		<< "game runs, and the slot stays NULL. This trap cost an earlier lane "
		<< "one build; it cost lane diag-visibility one live run.";

	// AND IT IS THE CLIENT'S ALONE. SourceSharp_ClientEntitiesInvalidateMdlCacheAll
	// walks g_StandaloneClients, which does not exist on the server. Added by
	// `abi-tail`: the check above is satisfied by a region compiled on BOTH
	// sides as well as by one compiled on the client, and those are different
	// facts.
	EXPECT_FALSE( CompiledWhenNotDefined( src, at, "CLIENT_DLL" ) )
		<< "the client's forwards are also compiled on the SERVER, where the body "
		<< "they name does not exist";
}

//-----------------------------------------------------------------------------
// AND THE SERVER'S IS COMPILED ON THE SERVER -- the same check, the other way,
// so a fix that moved the client's binding by moving the whole region cannot
// pass the fact above by breaking the server.
//-----------------------------------------------------------------------------
TEST( ClientMdlCacheSources, TheServerBindingIsStillCompiledOnTheServer )
{
	const std::string src = StripComments( Read( kGameSystem ) );
	const std::string impl = StripComments( Read( kAreasImpl ) );
	ASSERT_FALSE( src.empty() );
	ASSERT_FALSE( impl.empty() );

	ASSERT_NE( impl.find( "{ return ::SourceSharp_EntitiesInvalidateMdlCacheAll(" ), std::string::npos )
		<< "the server arm is gone";
	const size_t at = src.find( kAreasInclude );
	ASSERT_NE( at, std::string::npos ) << "the server's area forwards are not included";

	EXPECT_FALSE( CompiledWhenDefined( src, at, "CLIENT_DLL" ) )
		<< "the SERVER's walk must not be bound on the client: it walks gEntList "
		<< "and g_Standalones, neither of which exists there.";

	// AND IT IS BOUND ON THE SERVER. Added by `abi-tail` after mutating a
	// server-arm binding into an `#ifdef CLIENT_DLL` nested inside the
	// server-only block and watching the check above stay GREEN: that region is
	// compiled on neither side, so "not on the client" was true of a binding
	// that never happens.
	EXPECT_TRUE( CompiledWhenNotDefined( src, at, "CLIENT_DLL" ) )
		<< "the server's binding is not compiled on the server either -- almost "
		<< "certainly an `#ifdef CLIENT_DLL` nested inside the `#ifndef "
		<< "CLIENT_DLL` that surrounds it, which is compiled on NEITHER side.";
}

//-----------------------------------------------------------------------------
// THE EVALUATOR ITSELF IS CHECKED. A nesting test whose parser always answered
// TRUE would pass both facts above forever, which is precisely the shape of the
// 43 checks-that-cannot-fail this tree has already found.
//-----------------------------------------------------------------------------
TEST( PreprocessorNesting, ItAnswersFalseInsideAnIfndefOfTheSymbol )
{
	const std::string src = "#ifndef CLIENT_DLL\nTARGET\n#endif\n";

	EXPECT_FALSE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, ItAnswersTrueInsideAnIfdefOfTheSymbol )
{
	const std::string src = "#ifdef CLIENT_DLL\nTARGET\n#endif\n";

	EXPECT_TRUE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, ItSeesThroughNestingToAnOuterIfndef )
{
	// THE EXACT SHAPE OF THE BUG: an `#ifdef CLIENT_DLL` that is correct in
	// itself and dead because of what encloses it.
	const std::string src =
		"#ifndef CLIENT_DLL\n#ifdef CLIENT_DLL\nTARGET\n#endif\n#endif\n";

	EXPECT_FALSE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, AClosedBlockDoesNotReachPastItsEndif )
{
	const std::string src = "#ifndef CLIENT_DLL\n#endif\nTARGET\n";

	EXPECT_TRUE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, AnElseFlipsTheArm )
{
	const std::string src = "#ifndef CLIENT_DLL\n#else\nTARGET\n#endif\n";

	EXPECT_TRUE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, AConditionAboutSomethingElseDoesNotExcludeTheLine )
{
	const std::string src = "#ifndef GAME_DLL\nTARGET\n#endif\n";

	EXPECT_TRUE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

//=============================================================================
// THE `abi-tail` MERGE'S FIVE BINDINGS, AT THE SAME SITE AND FOR THE SAME
// REASON.
//
// The entity factory dictionary's five slots are SERVER ONLY -- there is no
// factory dictionary on the client. So they take the opposite arm of the same
// trap: a binding written where the CLIENT arm can see it would bind five
// server-only bodies the client cannot link, and one written outside a
// conditional at all would try to bind them on both.
//
// A NULL SLOT IS "NOT ASKED", NOT "EMPTY", and that is why the arm matters
// rather than being tidiness. `EntityFactories.Available` answers false with
// the slots null, and every consumer keeps its present behaviour. A CLIENT that
// bound them and got zero would answer "the table is empty" -- and an empty
// table refuses `ent_create prop_physics` for a class that works.
//=============================================================================

namespace
{

const char *kFactoryBindings[] =
{
	"{ return ::SourceSharp_EntityFactoryCount(",
	"{ return ::SourceSharp_EntityFactoryNameAt(",
	"{ return ::SourceSharp_EntityFactoryRefusalCount(",
	"{ return ::SourceSharp_EntityFactoryRefusalNameAt(",
	"{ return ::SourceSharp_EntityFactoryRefusalTimesAt(",
};

const int kFactoryBindingCount =
	(int)( sizeof( kFactoryBindings ) / sizeof( kFactoryBindings[0] ) );

} // namespace

//-----------------------------------------------------------------------------
// ALL FIVE ARE BOUND. Four of five is not a partial win: EntityFactories.Refresh
// walks the names with Count and stops at the refusal ledger without all three
// of its slots, so a missing one is a table that reads as complete and is not.
//-----------------------------------------------------------------------------
TEST( EntityFactoryAbiTail, AllFiveSlotsAreBound )
{
	const std::string src = StripComments( Read( kAreasImpl ) );
	ASSERT_FALSE( src.empty() );

	for ( int i = 0; i < kFactoryBindingCount; i++ )
	{
		EXPECT_NE( src.find( kFactoryBindings[i] ), std::string::npos )
			<< "the factory slot binding is gone: " << kFactoryBindings[i]
			<< " -- with it unbound EntityFactories answers Available == false "
			<< "and the managed table cannot be asked anything at all.";
	}
}

//-----------------------------------------------------------------------------
// AND EVERY ONE OF THEM IS ON THE SERVER ARM. This is the check the
// diag-visibility lane lost a live run to, taken the other way round: these
// bodies live in shell_standalone_class.cpp, which the client does not compile.
//-----------------------------------------------------------------------------
TEST( EntityFactoryAbiTail, EveryFactorySlotIsBoundOnTheServerArmOnly )
{
	// Every binding is in the forwards file (AllFiveSlotsAreBound), so its side
	// is the side of the one line that includes that file.
	const std::string src = StripComments( Read( kGameSystem ) );
	ASSERT_FALSE( src.empty() );
	EXPECT_EQ( src.find( "engineApi.EntityFactory" ), std::string::npos )
		<< "a factory slot is bound in the struct fill, which is the client's since C6c";

	const size_t at = src.find( kAreasInclude );
	ASSERT_NE( at, std::string::npos ) << "the server's area forwards are not included";

	EXPECT_FALSE( CompiledWhenDefined( src, at, "CLIENT_DLL" ) )
		<< "the factory slots are bound where the CLIENT compiles them -- the "
		<< "dictionary is server-side, and a client that bound these and got zero "
		<< "would read the table as EMPTY rather than as UNASKED, which refuses "
		<< "classnames that work.";

	// AND THE OTHER HALF OF THE QUESTION, which is not the same fact. An
	// `#ifdef CLIENT_DLL` nested inside the surrounding `#ifndef CLIENT_DLL`
	// is compiled on NEITHER side: it passes the check above while binding
	// nothing at all, and the slots then stay unbound on the one side that
	// needs them. That mutation was GREEN against the check above alone.
	EXPECT_TRUE( CompiledWhenNotDefined( src, at, "CLIENT_DLL" ) )
		<< "the factory slots are bound where the SERVER does NOT compile them -- "
		<< "almost certainly an `#ifdef CLIENT_DLL` nested inside the server-only block.";
}

//-----------------------------------------------------------------------------
// THE DECLARATIONS ARE ON THE SERVER ARM TOO. A declaration outside the
// conditional compiles on both sides and only fails at LINK, on the client, in
// a build nobody runs until they do.
//
// THE SUBJECT MOVED AT plan_shim.md §5 3e AND THE CLAIM DID NOT. These five
// were hand-written prototypes in sourcesharp_gamesystem.cpp's server arm. They
// are GENERATED now, into sourcesharp_entrypoints.h by tools/entrypoint-decls,
// which puts each declaration on the side the FILL BLOCK wires it -- so
// "declared where only the server compiles it" is a property the generator
// produces rather than one a person maintains, and this fact is what says it
// produced the right one. Read with the same conditional evaluator, because the
// generated header uses the same guard spellings.
//
// AND THE MOVE CORRECTED THEM, which is the generator's whole argument. The
// hand-written five said `int` and `int i`; SourceSharpEngineApi's fields say
// `int32_t` and `int32_t index`. The two are the same type on this platform, so
// nothing broke and nothing would have -- which is exactly why a second
// spelling of a signature is worth removing rather than reviewing.
//-----------------------------------------------------------------------------
TEST( EntityFactoryAbiTail, TheFactoryExternsAreDeclaredOnTheServerArmOnly )
{
	const std::string src = StripComments( Read( kEntryPoints ) );
	ASSERT_FALSE( src.empty() )
		<< "sourcesharp_entrypoints.h did not open. It is generated and "
		   "committed; run tools/entrypoint-decls --write.";

	const char *pszDecls[] =
	{
		"int32_t SourceSharp_EntityFactoryCount( void );",
		"const char *SourceSharp_EntityFactoryNameAt( int32_t index );",
		"int32_t SourceSharp_EntityFactoryRefusalCount( void );",
		"const char *SourceSharp_EntityFactoryRefusalNameAt( int32_t index );",
		"int32_t SourceSharp_EntityFactoryRefusalTimesAt( int32_t index );",
	};

	for ( int i = 0; i < 5; i++ )
	{
		const size_t at = src.find( pszDecls[i] );
		ASSERT_NE( at, std::string::npos )
			<< "the extern declaration is gone: " << pszDecls[i];

		EXPECT_FALSE( CompiledWhenDefined( src, at, "CLIENT_DLL" ) )
			<< "declared where the client compiles it: " << pszDecls[i];
		EXPECT_TRUE( CompiledWhenNotDefined( src, at, "CLIENT_DLL" ) )
			<< "declared where the SERVER does not compile it, which leaves the "
			<< "binding below it with no declaration: " << pszDecls[i];
	}
}

//-----------------------------------------------------------------------------
// THE BODIES EXIST, where the declarations say they do. A binding to a name
// nothing defines is a link error on the server -- but only once something
// stops the linker folding it, and this says which file owes it.
//-----------------------------------------------------------------------------
TEST( EntityFactoryAbiTail, TheFiveBodiesAreDefinedInTheShimsClassFile )
{
	const std::string src = StripComments(
		Read( "sourcesharp/game/server/shell_standalone_class.cpp" ) );
	ASSERT_FALSE( src.empty() );

	EXPECT_NE( src.find( "int SourceSharp_EntityFactoryCount()" ), std::string::npos );
	EXPECT_NE( src.find( "const char *SourceSharp_EntityFactoryNameAt( int i )" ), std::string::npos );
	EXPECT_NE( src.find( "int SourceSharp_EntityFactoryRefusalCount()" ), std::string::npos );
	EXPECT_NE( src.find( "const char *SourceSharp_EntityFactoryRefusalNameAt( int i )" ), std::string::npos );
	EXPECT_NE( src.find( "int SourceSharp_EntityFactoryRefusalTimesAt( int i )" ), std::string::npos );
}

//-----------------------------------------------------------------------------
// THE TWO INDEXED READS REFUSE AN OUT-OF-RANGE INDEX rather than reading off
// the end. Stock's GetCannonicalName did not test its own Find and reached
// GetElementName( InvalidIndex() ) -- a read of the element vector at 0xFFFF.
//-----------------------------------------------------------------------------
TEST( EntityFactoryAbiTail, TheIndexedReadsBoundsCheckTheirIndex )
{
	const std::string src = StripComments(
		Read( "sourcesharp/game/server/shell_standalone_class.cpp" ) );
	ASSERT_FALSE( src.empty() );

	const size_t at = src.find( "const char *SourceSharp_EntityFactoryRefusalNameAt( int i )" );
	ASSERT_NE( at, std::string::npos );

	const std::string body = src.substr( at, 300 );

	EXPECT_NE( body.find( "i < 0 || i >= (int)d.m_RefusedOrder.size()" ), std::string::npos )
		<< "the refusal name read no longer bounds its index";
	EXPECT_NE( body.find( "return NULL;" ), std::string::npos )
		<< "and out of range must answer NULL, which is the contract "
		<< "sourcesharp.h states and EntityFactories.Refresh's walk stops on";
}

//=============================================================================
// AND THE OTHER HALF OF THE EVALUATOR IS CHECKED TOO.
//
// `CompiledWhenNotDefined` is a second question over the same walk, added by
// lane `abi-tail` because a server-only binding needs BOTH answers and the
// mutation that proved it -- five bindings wrapped in an `#ifdef CLIENT_DLL`
// inside the server-only block -- was green against `CompiledWhenDefined`
// alone. A parser that always answered TRUE would put it straight back.
//=============================================================================

TEST( PreprocessorNesting, NotDefinedAnswersTrueInsideAnIfndefOfTheSymbol )
{
	const std::string src = "#ifndef CLIENT_DLL\nTARGET\n#endif\n";

	EXPECT_TRUE( CompiledWhenNotDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, NotDefinedAnswersFalseInsideAnIfdefOfTheSymbol )
{
	const std::string src = "#ifdef CLIENT_DLL\nTARGET\n#endif\n";

	EXPECT_FALSE( CompiledWhenNotDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

//-----------------------------------------------------------------------------
// THE EXACT SHAPE OF THE MIRROR BUG, and the reason this question exists: a
// region compiled on NEITHER side. `CompiledWhenDefined` says false, which
// reads as "correctly server-only"; this says false too, which is what makes
// the pair able to tell "server-only" from "nowhere".
//-----------------------------------------------------------------------------
TEST( PreprocessorNesting, ARegionCompiledOnNeitherSideAnswersFalseToBoth )
{
	const std::string src =
		"#ifndef CLIENT_DLL\n#ifdef CLIENT_DLL\nTARGET\n#endif\n#endif\n";

	const size_t at = src.find( "TARGET" );

	EXPECT_FALSE( CompiledWhenDefined( src, at, "CLIENT_DLL" ) );
	EXPECT_FALSE( CompiledWhenNotDefined( src, at, "CLIENT_DLL" ) );
}

//-----------------------------------------------------------------------------
// AND AN UNCONDITIONAL LINE ANSWERS TRUE TO BOTH, so the pair is not two ways
// of spelling "false".
//-----------------------------------------------------------------------------
TEST( PreprocessorNesting, AnUnconditionalLineAnswersTrueToBoth )
{
	const std::string src = "TARGET\n";
	const size_t at = src.find( "TARGET" );

	EXPECT_TRUE( CompiledWhenDefined( src, at, "CLIENT_DLL" ) );
	EXPECT_TRUE( CompiledWhenNotDefined( src, at, "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, NotDefinedFlipsOnAnElseToo )
{
	const std::string src = "#ifndef CLIENT_DLL\n#else\nTARGET\n#endif\n";

	EXPECT_FALSE( CompiledWhenNotDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, NotDefinedTreatsAnUnrelatedConditionAsTaken )
{
	const std::string src = "#ifndef GAME_DLL\nTARGET\n#endif\n";

	EXPECT_TRUE( CompiledWhenNotDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}
