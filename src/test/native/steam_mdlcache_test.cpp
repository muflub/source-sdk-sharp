//====== SourceSharp — Part 11a2's three gaps, and the halves that stayed native
//
// plan_gamedll_after.md Phase A2. `InvalidateMdlCache`,
// `GameServerSteamAPIActivated` and `GameServerSteamAPIShutdown` were `{}` with
// a NOT PORTED verdict; all three are ported now, each as a forward to managed
// plus one native step managed calls back.
//
// The MANAGED half has facts of its own (`ServerGameDllTests`, twelve of them):
// that the flush reaches the walk, that the Steam pair's order inverts, and
// that stock's dedicated-and-available condition survives as two managed
// questions. What no managed test can reach is the C++ underneath, and it has
// four properties that a reader cannot check by eye and a compiler cannot check
// at all:
//
//   THE ORDER INSIDE THE RE-ACQUIRE. `Clear()` then `Init()`, in that order,
//   because activation REPEATS -- the engine calls it again whenever Steam
//   becomes available -- and an Init over a filled context leaves whichever of
//   the eight pointers the new Init failed to replace. Transposed, it compiles,
//   links, runs and is wrong only after a reconnect.
//
//   ONE BODY FOR THE CLEAR. The level-shutdown step and
//   GameServerSteamAPIShutdown both clear the same context. Two `->Clear()`
//   call sites is TODO #5's refused shape -- "a second route to one answer is
//   two things to keep in agreement" -- so the level step FORWARDS.
//
//   THE WALK IS STOCK'S. `dynamic_cast<CBaseAnimating *>` over `gEntList`, and
//   the cast is load-bearing: `gEntList` holds `CBaseEntity *` and only the
//   CBaseAnimating branch owns a `CStudioHdr`, so a `static_cast` is a wrong
//   answer for every brush entity on the list.
//
//   NO STATE. Both files are pure steps over engine-owned globals. The
//   statics-audit gate (Phase A1) catches a file-scope `static`; it does not
//   catch a non-static file-scope global, and that is the shape a cached
//   "have we activated yet" flag would take.
//
// Source-read, and it is forced rather than lazy: both files include cbase.h's
// whole world and neither can ever be linked into a gtest. The behavioural half
// is the live gate -- see docs/gamedll-ledger.md, Phase A2.
//
//=============================================================================//

#include <string>
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

// Every `//` comment removed, so that a fact about CODE cannot be answered by
// prose. Both files below describe the very operations they perform, at length,
// so an uncomment-stripped search for "->Clear()" would find the paragraph
// explaining it and pass over a body that no longer does it. That is the exact
// shape of `TheDefaultTickIntervalHasOneDefinition`, number five in this tree's
// list of checks that cannot fail.
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
			while ( i + 1 < src.size() && !( src[ i ] == '*' && src[ i + 1 ] == '/' ) ) i++;
			i = ( i + 1 < src.size() ) ? i + 2 : src.size();
			continue;
		}
		out += src[ i++ ];
	}

	return out;
}

// ONE FILE BEHIND BOTH NAMES NOW. plan_shim.md §5 3a (lane 3b-enginebind)
// merged shell_files.cpp, shell_steamcontext.cpp, shell_mdlcache.cpp and
// shell_templates.cpp into shell_engineservices.cpp: four translation units
// each holding two or three leaves that ask one engine-owned global a question
// managed code cannot ask itself.
//
// THE TWO NAMES ARE KEPT rather than collapsed to one constant, because every
// fact below says which SUBJECT it is about and that is worth more than the
// saved line. A fact that reads kSteam is about the Steam context whether or
// not the model cache happens to share its file.
const char *kSteam    = "sourcesharp/game/server/shell_engineservices.cpp";
const char *kMdlCache = "sourcesharp/game/server/shell_engineservices.cpp";
const char *kShell    = "sourcesharp/game/server/shell_gamedll.cpp";

} // namespace

//-----------------------------------------------------------------------------
// THE STEAM GAME SERVER CONTEXT LEFT server.so -- plan_server_novalve.md P2.5.
//
// Five facts stood here about CSteamGameServerAPIContext's re-acquire (Clear
// then Init), its one Clear, the level step that forwarded to it and the stats
// step. All of it is C#'s own now (SourceSharp.Abi.SteamGameServerFlat, the
// flat API; ServerGameDllTests pin the order: re-acquire on activation, stats
// only on a dedicated server with a game server, the clear after the
// pre-clear pass). Nothing stays native: the three slots are retired.
//-----------------------------------------------------------------------------
TEST( SteamContextSources, TheContextIsGoneAndSoAreTheStubs )
{
	const std::string src = StripComments( Read( kSteam ) );
	ASSERT_FALSE( src.empty() );

	EXPECT_EQ( std::string::npos, src.find( "steamgameserverapicontext" ) );
	EXPECT_EQ( std::string::npos, src.find( "steam/" ) );
	// plan_engineapi_vtables Phase 0: the three retired slots' stubs are gone too.
	EXPECT_EQ( std::string::npos, src.find( "SourceSharp_SteamGameServerReacquire" ) );
	EXPECT_EQ( std::string::npos, src.find( "SourceSharp_SteamGameServerClear" ) );
	EXPECT_EQ( std::string::npos, src.find( "SourceSharp_SteamGameServerRequestGameplayStats" ) );

	// The retention half: the managed path exists and binds the engine's copy.
	// An instance per server load since P1 run 3, in Server.Interop: it opens
	// the library through its loader, and the process loader is
	// LoadedLibrary's, which never loads one: RTLD_NOLOAD on Linux,
	// GetModuleHandleW on Windows.
	const std::string flat = Read( "sourcesharp/managed/SourceSharp.Server.Interop/SteamGameServerFlat.cs" );
	ASSERT_FALSE( flat.empty() );
	EXPECT_NE( std::string::npos, flat.find( "_library = _loader.OpenLoaded(Library);" ) );
	EXPECT_NE( std::string::npos, flat.find( "public IntPtr OpenLoaded(string file) => LoadedLibrary.Open(file);" ) );

	const std::string loaded = Read( "sourcesharp/managed/SourceSharp.Shared.Interop/LoadedLibrary.cs" );
	ASSERT_FALSE( loaded.empty() );
	EXPECT_NE( std::string::npos, loaded.find(
		"=> OperatingSystem.IsWindows() ? GetModuleHandleW(file) : DlOpen(file, RTLD_NOW | RTLD_NOLOAD);" ) );
}

//-----------------------------------------------------------------------------
// NEITHER FILE HOLDS STATE.
//
// A1's statics-audit fails a file-scope `static`, and its own closing note says
// what it does NOT catch: a non-static file-scope global. That is exactly the
// shape a cached "have we activated yet" flag or a remembered availability
// would take, and both files are pure steps over globals the engine owns.
//-----------------------------------------------------------------------------
TEST( SteamContextSources, NeitherFileHoldsStateOfItsOwn )
{
	// kSteam and kMdlCache are THE SAME FILE since the engine-services merge,
	// so this walks it once rather than twice. The claim is unchanged and its
	// scope is wider: nothing at file scope in any of the four merged bodies.
	const char *kFiles[] = { kSteam };

	for ( size_t f = 0; f < sizeof( kFiles ) / sizeof( kFiles[ 0 ] ); f++ )
	{
		const std::string src = StripComments( Read( kFiles[ f ] ) );
		ASSERT_FALSE( src.empty() );

		// Every line at column zero that is not a preprocessor line, a
		// declaration of one of this file's own functions, or a brace.
		size_t line = 0;
		while ( line < src.size() )
		{
			const size_t end = src.find( '\n', line );
			const std::string text = src.substr( line, ( end == std::string::npos )
			                                           ? std::string::npos : end - line );
			line = ( end == std::string::npos ) ? src.size() : end + 1;

			if ( text.empty() || text[ 0 ] == ' ' || text[ 0 ] == '\t' )
				continue;
			if ( text[ 0 ] == '#' || text[ 0 ] == '{' || text[ 0 ] == '}' )
				continue;

			// A definition -- `type SourceSharp_Foo( void )` -- is not state.
			if ( text.find( "SourceSharp_" ) != std::string::npos )
				continue;

			// AND TWO DEFINITIONS THAT ARE NOT SPELT SourceSharp_ AT ALL,
			// because they are STOCK's names and have to be: Templates_AddNamed
			// and Templates_Add answer declarations in
			// game/server/TemplateEntities.h, so renaming them to fit this
			// file's prefix would break the seam they exist to be.
			//
			// They arrived with the engine-services merge (plan_shim.md 5 3a,
			// lane 3b-enginebind) and this whitelist had to widen for them --
			// which is the honest fix rather than the alternative, because the
			// CLAIM is unchanged: a function definition is not state, and what
			// this fact refuses is a remembered flag the statics-audit gate
			// cannot see. Both are pure forwards with no storage.
			if ( text.find( "int Templates_Add" ) == 0 )
				continue;

			// AND ONE DECLARATION OF ANOTHER FILE'S GLOBAL: `extern IFileSystem
			// *filesystem;`, which cbase.h supplied before this file left it
			// (plan_server_novalve.md P5). An extern without an initializer
			// declares storage that shell_engineboundary.cpp owns; it adds
			// none here, so the claim is unchanged.
			if ( text.compare( 0, 7, "extern " ) == 0 && text.find( '=' ) == std::string::npos )
				continue;

			FAIL() << kFiles[ f ] << " has a file-scope statement that is not one "
			       << "of its own step definitions: \"" << text << "\". These two "
			       << "files are pure steps over engine-owned globals; a remembered "
			       << "flag here is state the statics-audit gate cannot see, "
			       << "because that gate is on `static` and this would not be one.";
		}
	}
}

//-----------------------------------------------------------------------------
// THE MDL-CACHE WALK IS STOCK'S, AND THE dynamic_cast IS GONE.
//
// This fact said the opposite until plan_entity.md step 4d, and the reason it
// changed is gate 3: the plan counts every way of turning something into an
// entity pointer and requires ZERO, with dynamic_cast on the list by name. The
// old argument -- gEntList holds CBaseEntity *, only the CBaseAnimating branch
// owns a CStudioHdr, so a static_cast would be a wrong answer for every brush
// entity -- was correct and is not what replaced it.
//
// What replaced it is a VIRTUAL CALL, which is not a cast at all.
// CBaseEntity::GetBaseAnimating answers 0 (baseentity.h:1030) and
// CBaseAnimating's override answers `this` (baseanimating.h:83), so the answer
// is identical for every entity on the list and the brush entity still gets its
// NULL -- from the type itself rather than from a runtime type walk.
//-----------------------------------------------------------------------------
TEST( MdlCacheWalkSources, TheSdkWalkIsGoneWithItsListAndTheStandaloneWalkIsTheWholeFunction )
{
	// READ THE ASSERTIONS, NOT THE NAME. This was
	// TheWalkIsStocksWalkOverTheEntityList, and it required stock's walk
	// verbatim: start at gEntList.FirstEnt(), step with gEntList.NextEnt(),
	// call InvalidateMdlCache() on what the branch found.
	//
	// THE LIST IS GONE. entitylist.cpp was in the removal set and is out of the
	// build; `nm -C server.so` finds gEntList neither defined NOR undefined. The
	// SDK arm was inside this file's own guarded arm and the close deleted it
	// with the other 453 -- which is exactly what the comment in the body
	// predicted: "armed there is neither a list to walk nor a skeleton to find
	// at the end of it".
	//
	// SO THE FACT BECOMES THE OTHER HALF OF ITS OWN SENTENCE: "the standalone
	// walk below is not a fallback here; armed it is the whole function, and it
	// already counts the same way". Absence alone would pass over an emptied
	// function, and an emptied function is the exact failure this whole file
	// exists to catch -- A2's note calls it "the whole of Phase A2's
	// difficulty": the engine flushing its model cache leaves no trace a running
	// game can be asked about, so an empty body and a working one read the same
	// from outside. The COUNT is what tells them apart, and it is asserted here.
	const std::string src = StripComments( Read( kMdlCache ) );
	ASSERT_FALSE( src.empty() );

	const size_t at = src.find( "SourceSharp_EntitiesInvalidateMdlCacheAll" );
	ASSERT_NE( at, std::string::npos ) << "the walk is gone";

	// GONE: the list, and the member call that needed the SDK hierarchy.
	EXPECT_EQ( src.find( "gEntList." ), std::string::npos )
		<< "the SDK walk is back. gEntList is not in server.so at all -- nm "
		<< "finds it neither defined nor undefined -- so this is an undefined "
		<< "reference, or a second entity list.";
	EXPECT_EQ( src.find( "->InvalidateMdlCache()", at ), std::string::npos )
		<< "InvalidateMdlCache is CBaseAnimating's and left with the class; "
		<< "what a standalone entity has is a bone cache, not an m_pStudioHdr.";

	// LIVE: the standalone registry walk, which is now the whole function, and
	// the count that makes an empty body distinguishable from a working one.
	EXPECT_NE( src.find( "StandaloneEntities()", at ), std::string::npos )
		<< "the walk no longer reaches the standalone registry, so nothing is "
		<< "invalidated at all and a model reload appears not to take.";
	// The skeletons the flush makes stale are MANAGED since plan
	// physics_bones_native_zero 1.2 (ServerSkeletons.InvalidateMdlCache, from
	// the same managed flush: ServerSkeletonsTests and ServerGameDllTests.
	// AModelCacheFlushInvalidatesTheManagedSkeletons). What stays here is the count.
	EXPECT_NE( src.find( "nInvalidated = (int32_t)standalones.size();", at ), std::string::npos )
		<< "the walk stopped counting. The number is what C# prints beside "
		<< "\"model cache flushed (#N)\", and without it an empty body and a "
		<< "working one read identically from outside the process.";
	EXPECT_NE( src.find( "return nInvalidated;", at ), std::string::npos )
		<< "the count is no longer returned.";
}

TEST( MdlCacheWalkSources, TheTypeTestIsGoneEntirelyAndStillIsNotACast )
{
	// READ THE ASSERTIONS, NOT THE NAME, AND READ THE HISTORY WITH THEM.
	//
	// This fact has said three different things. First: the branch is a
	// dynamic_cast, because gEntList held CBaseEntity * and only the
	// CBaseAnimating branch owns a CStudioHdr, so a static_cast would be a
	// wrong answer for every brush entity -- which is most of a map. Then, at
	// plan_entity.md step 4d: the branch is `->GetBaseAnimating()`, a VIRTUAL,
	// which gives the identical answer (0 on the base, `this` on the override)
	// and is not a cast at all -- gate 3 counts every way of turning something
	// into an entity pointer and requires zero, with dynamic_cast on the list
	// by name.
	//
	// NOW THERE IS NO BRANCH. The list the branch was over is gone, and the one
	// walk left is over `std::vector< CStandaloneEntity * >` -- already typed, so
	// InvalidateBoneCache() is an ordinary member call on the element type and
	// there is nothing to test, cast or ask. That is gate 3's end state rather
	// than a retreat from it, so the two bans STAY -- a cast here would be a
	// cast back into a hierarchy this function no longer touches -- and the
	// typed walk that made them unnecessary is asserted beside them.
	const std::string src = StripComments( Read( kMdlCache ) );
	ASSERT_FALSE( src.empty() );

	EXPECT_EQ( src.find( "dynamic_cast" ), std::string::npos )
		<< "shell_engineservices.cpp contains a dynamic_cast outside a comment. "
		<< "plan_entity.md gate 3 counts these and requires zero; this was the "
		<< "server shim's family-5 site.";

	EXPECT_EQ( src.find( "static_cast<CBaseAnimating" ), std::string::npos )
		<< "a static_cast to CBaseAnimating * is a wrong answer for every "
		<< "entity that is not one, and there is no list here holding any.";

	EXPECT_EQ( src.find( "->GetBaseAnimating()" ), std::string::npos )
		<< "the SDK branch is back. GetBaseAnimating is a CBaseEntity member "
		<< "and left with the class; the walk it branched inside left with "
		<< "gEntList.";

	// THE TYPED WALK THAT MAKES ALL THREE UNNECESSARY. Without this the three
	// absences above would pass over a deleted file.
	EXPECT_NE( src.find( "const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();" ),
	           std::string::npos )
		<< "the walk no longer holds its elements at their own type, so the "
		<< "question the casts above were answering is back.";
	EXPECT_NE( src.find( "nInvalidated = (int32_t)standalones.size();" ), std::string::npos )
		<< "the typed list is no longer what the count is taken from.";
}

//-----------------------------------------------------------------------------
// AND THE HALF THE WALK NEVER HAD.
//
// The walk frees CBaseAnimating::m_pStudioHdr. A STANDALONE entity has none --
// it builds a CStudioHdr on the stack per ask -- so nothing about it was stale
// and it was left out. Its BONE CACHE is stale: m_CachedBones holds matrices
// built out of the old studio data, and BoneCacheKey_t is keyed on the model
// INDEX, which a model flush does not change. So every standalone entity kept
// posing itself from a skeleton that no longer existed, with the same symptom
// this whole file was written to fix: a model reload that appears not to take.
//
// Reachable only since step 4d, because until then a standalone entity had no
// bones to be stale.
//-----------------------------------------------------------------------------
// The standalone bone caches still go on a flush -- in MANAGED code since
// plan_physics_bones_native_zero 1.2 -- and native holds none to go.
TEST( MdlCacheWalkSources, TheStandaloneBoneCachesGoTooAndNeedNoCast )
{
	const std::string src = StripComments( Read( kMdlCache ) );
	ASSERT_FALSE( src.empty() );

	const size_t at = src.find( "SourceSharp_EntitiesInvalidateMdlCacheAll" );
	ASSERT_NE( at, std::string::npos );

	EXPECT_NE( src.find( "StandaloneEntities()", at ), std::string::npos )
		<< "the flush must also walk the standalone list. A standalone entity "
		<< "has no m_pStudioHdr, so the gEntList walk above does nothing for "
		<< "it -- and its bone cache is keyed on the model INDEX, which a "
		<< "flush does not change.";

	EXPECT_EQ( src.find( "InvalidateBoneCache", at ), std::string::npos )
		<< "a native bone cache is back; the skeleton is ServerSkeletons', which "
		<< "the managed flush invalidates.";
}

//-----------------------------------------------------------------------------
// THE WALK IS NOT GUARDED ON ANYTHING.
//
// Stock's has no condition at all, and the tempting addition -- skip dormant
// entities, the way the ACTIVATE walk does -- would be wrong here. A dormant
// entity still holds its CStudioHdr, and the studio data underneath has gone
// whether or not the entity is currently being networked.
//-----------------------------------------------------------------------------
TEST( MdlCacheWalkSources, TheWalkSkipsNothing )
{
	const std::string src = StripComments( Read( kMdlCache ) );
	ASSERT_FALSE( src.empty() );

	const size_t at = src.find( "SourceSharp_EntitiesInvalidateMdlCacheAll" );
	ASSERT_NE( at, std::string::npos );

	EXPECT_EQ( src.find( "IsDormant", at ), std::string::npos )
		<< "the flush walk must not skip dormant entities. The activate walk "
		<< "does and this one must not: a dormant entity is still holding a "
		<< "CStudioHdr over studio data that has gone, and it is read the "
		<< "moment the entity comes back.";
}

//-----------------------------------------------------------------------------
// AND THE THREE SHELL METHODS FORWARD RATHER THAN ANSWERING.
//
// The complement of the gamedll_ledger fact, from the other side: that one
// asserts the VERDICT above each body still matches the body. This asserts that
// the native step is reached from managed and NOT from the shell, which is what
// makes the engine's question arrive in C# at all. Calling the step directly
// from the shell would work, would keep the models correct, and would put a
// decision back in C++ where the whole port exists to remove it.
//-----------------------------------------------------------------------------
TEST( MdlCacheWalkSources, TheShellDoesNotCallTheNativeStepsItself )
{
	const std::string shell = StripComments( Read( kShell ) );
	ASSERT_FALSE( shell.empty() );

	const char *kSteps[] = { "SourceSharp_EntitiesInvalidateMdlCacheAll" };

	for ( size_t i = 0; i < sizeof( kSteps ) / sizeof( kSteps[ 0 ] ); i++ )
	{
		EXPECT_EQ( shell.find( kSteps[ i ] ), std::string::npos )
			<< "shell_gamedll.cpp calls " << kSteps[ i ] << " directly. These "
			<< "steps are reached from C#; a shell that calls one itself has "
			<< "answered the engine in C++, which works, keeps every gate green, "
			<< "and is the arrangement this whole port exists to remove.";
	}
}
