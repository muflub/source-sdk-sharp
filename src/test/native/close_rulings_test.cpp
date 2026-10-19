//====== SourceSharp — the four rulings the CLOSE made, and what pins each ====//
//
// `tools/entity-census-patterns` is the only list `tools/entity-gates` counts,
// and the close changed five of its rows: three NARROWED, one WIDENED, and one
// instrument (`scan_sites`) learned to strip string literals. A narrowing is the
// most dangerous edit anyone can make to a gate — it lowers a number without
// touching the tree — so every one of them is pinned here.
//
// THE TEST IS NOT "the regex is the string I wrote". That is a tautology and
// this project has found more than forty of them. Each fact below drives the
// CHANGED REGEX over TEXT and asserts BOTH directions:
//
//     the thing the row still hunts       IS matched
//     the thing the ruling excused        IS NOT matched
//
// so a regression to the old bare pattern reddens the first half, and a wider
// "fix" that re-excuses an entity cast reddens the second.
//
// AND TWO PREMISES ARE PINNED IN THE TREE RATHER THAN IN A FIXTURE, because
// they are what the deletions in 4/N, 5/N and 7/N rest on:
//
//   * `CHandle<IGameEntity>`'s Get() AND Set() are both specialised, and neither
//     body is a cast. That is the whole of the EHANDLE ruling: §3.16's subject
//     is `(T*)CBaseHandle::Get()`, and this type does not do that.
//   * `EntityFromEntityHandle` and `TraceEntity` are GONE from the compiled
//     tree, and the neutral pair that replaced them asks `GetGameEntity()`.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <stdio.h>
#include <string.h>
#include <sys/stat.h>

#include <regex>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// The worktree root, found the way every other test here finds it.
//-----------------------------------------------------------------------------
const char *WorktreeRoot()
{
	static const char *kCandidates[] = { ".", "..", "../..", "../../.." };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::string szProbe = std::string( kCandidates[ i ] ) + "/tools/entity-census-patterns";
		struct stat st;
		if ( stat( szProbe.c_str(), &st ) == 0 && S_ISREG( st.st_mode ) )
			return kCandidates[ i ];
	}
	return NULL;
}

std::string ReadRoot( const char *pszRelative )
{
	const char *pszRoot = WorktreeRoot();
	if ( !pszRoot )
		return std::string();

	const std::string szPath = std::string( pszRoot ) + "/" + pszRelative;
	FILE *pFile = fopen( szPath.c_str(), "rb" );
	if ( !pFile )
		return std::string();

	std::string szOut;
	char rgchBuf[ 8192 ];
	size_t cRead;
	while ( ( cRead = fread( rgchBuf, 1, sizeof( rgchBuf ), pFile ) ) > 0 )
		szOut.append( rgchBuf, cRead );
	fclose( pFile );
	return szOut;
}

//-----------------------------------------------------------------------------
// One row of the pattern file, by name. Read with the separator the file
// documents — a TAB — and with the backslashes intact, which is the whole of
// that file's own warning: read without -r, `\b` becomes `b` and every count
// comes out wrong.
//-----------------------------------------------------------------------------
std::string PatternFor( const char *pszName )
{
	const std::string szBody = ReadRoot( "tools/entity-census-patterns" );
	const std::string szKey  = std::string( pszName ) + "\t";

	size_t iPos = 0;
	while ( iPos < szBody.size() )
	{
		const size_t iEnd  = szBody.find( '\n', iPos );
		const std::string szLine =
			szBody.substr( iPos, ( iEnd == std::string::npos ? szBody.size() : iEnd ) - iPos );
		iPos = ( iEnd == std::string::npos ) ? szBody.size() : iEnd + 1;

		if ( szLine.compare( 0, szKey.size(), szKey ) == 0 )
			return szLine.substr( szKey.size() );
	}
	return std::string();
}

//-----------------------------------------------------------------------------
// `grep -E` semantics, as closely as std::regex gives them. The one difference
// that matters here is `\b`, which POSIX ERE does not have and ECMAScript does,
// so ECMAScript is what these are compiled as — the same choice `scan_sites`
// makes when it keeps grep as the matcher rather than re-matching in awk.
//-----------------------------------------------------------------------------
bool Matches( const std::string &szPattern, const std::string &szText )
{
	if ( szPattern.empty() )
		return false;
	std::regex re( szPattern, std::regex::ECMAScript );
	return std::regex_search( szText, re );
}

//-----------------------------------------------------------------------------
// Running a shell command and collecting its output. popen reports the wait(2)
// status, so a test comparing the raw status against 1 compares against 256.
//-----------------------------------------------------------------------------
std::string Shell( const std::string &szCommand )
{
	FILE *pPipe = popen( ( szCommand + " 2>&1" ).c_str(), "r" );
	if ( !pPipe )
		return std::string();

	std::string szOut;
	char rgchBuf[ 4096 ];
	while ( fgets( rgchBuf, sizeof( rgchBuf ), pPipe ) )
		szOut += rgchBuf;
	pclose( pPipe );
	return szOut;
}

} // namespace

//=============================================================================//
// RULING 1 — `dynamic_cast` and `assert_cast` are TYPED now.
//
// They used to match ANY target. What is left under those spellings in a
// finished port is vgui and the client-system walk; counting those made the gate
// unreachable for a reason that is not entity coupling.
//=============================================================================//

TEST( CloseRulings, TheDynamicCastRowStillCatchesADynamicCastOntoAnEntity )
{
	const std::string szRe = PatternFor( "dynamic_cast" );
	ASSERT_FALSE( szRe.empty() ) << "the dynamic_cast row is missing from the pattern file";

	EXPECT_TRUE( Matches( szRe, "\tIGameEntity *p = dynamic_cast< IGameEntity * >( pThing );" ) )
		<< "the narrowed row stopped catching a dynamic_cast onto the port's own "
		   "entity interface, which is the one kind that can still appear";
	EXPECT_TRUE( Matches( szRe, "\tCBasePlayer *p = dynamic_cast<CBasePlayer*>( pEnt );" ) )
		<< "the narrowed row stopped catching a dynamic_cast onto a stock entity class";
	EXPECT_TRUE( Matches( szRe, "\tdynamic_cast< CStandaloneEntity * >( pGame )" ) )
		<< "the narrowed row stopped catching a dynamic_cast onto the shell";
}

TEST( CloseRulings, TheDynamicCastRowNoLongerCatchesAVguiPanel )
{
	const std::string szRe = PatternFor( "dynamic_cast" );
	ASSERT_FALSE( szRe.empty() );

	// The four real sites the ruling excused, copied from the files they are in.
	EXPECT_FALSE( Matches( szRe, "\tLabel *entry = dynamic_cast<Label *>(FindChildByName(textEntryName));" ) );
	EXPECT_FALSE( Matches( szRe, "\tvgui::ComboBox *box = dynamic_cast<vgui::ComboBox *>( panel );" ) );
	EXPECT_FALSE( Matches( szRe, "\tCNewParticleEffect *p = dynamic_cast< CNewParticleEffect*>( r );" ) );
	EXPECT_FALSE( Matches( szRe, "\tdynamic_cast< CClientSystemPerFrame * >( pSystem() );" ) );
}

TEST( CloseRulings, TheAssertCastRowIsTypedTheSameWayAndBothDirectionsHold )
{
	const std::string szRe = PatternFor( "assert_cast" );
	ASSERT_FALSE( szRe.empty() );

	EXPECT_TRUE( Matches( szRe, "\tassert_cast< C_BaseAnimating * >( pEntity )" ) )
		<< "assert_cast onto an entity stopped being a site";
	EXPECT_FALSE( Matches( szRe, "\tCLightmapDebugView v( assert_cast<CViewRender *>( ::view) );" ) )
		<< "assert_cast onto the view renderer is not an entity cast and must not count";
}

//=============================================================================//
// RULING 2 — EHANDLE. §3.16's subject is `(T*)CBaseHandle::Get()`; this type
// does not do that, and the two facts below are why.
//=============================================================================//

TEST( CloseRulings, TheEhandleRowStillCatchesATypedHandleOnAStockEntity )
{
	const std::string szRe = PatternFor( "EHANDLE" );
	ASSERT_FALSE( szRe.empty() );

	EXPECT_TRUE( Matches( szRe, "\tCHandle<CBaseEntity> m_hThing;" ) );
	EXPECT_TRUE( Matches( szRe, "\tCHandle< C_BaseAnimating > m_hOther;" ) );
	EXPECT_TRUE( Matches( szRe, "\tCHandle<CBasePlayer> m_hPlayer;" ) );
}

TEST( CloseRulings, TheEhandleRowNoLongerCatchesThePortsOwnHandle )
{
	const std::string szRe = PatternFor( "EHANDLE" );
	ASSERT_FALSE( szRe.empty() );

	EXPECT_FALSE( Matches( szRe, "\tEHANDLE m_hTargetEntity;" ) )
		<< "EHANDLE is CHandle<IGameEntity>, whose Get and Set are specialised";
	EXPECT_FALSE( Matches( szRe, "\tCHandle<IGameEntity> m_h;" ) );
}

TEST( CloseRulings, BothHalvesOfTheGameEntityHandleAreSpecialised )
{
	// THE PREMISE UNDER THE EHANDLE RULING. If either specialisation goes,
	// CHandle<IGameEntity> falls back on the generic `(T*)CBaseHandle::Get()`
	// and the generic `reinterpret_cast<const IHandleEntity*>` — which is the
	// cast §3.16 family 1 is about, on a pointer whose boundary view is 24
	// measured bytes from the address the handle table stores.
	const std::string szEhandle = ReadRoot( "src/game/shared/ehandle.h" );
	ASSERT_FALSE( szEhandle.empty() ) << "game/shared/ehandle.h is missing";
	EXPECT_NE( std::string::npos,
	           szEhandle.find( "void CHandle<IGameEntity>::Set( const IGameEntity* pVal )" ) )
		<< "the Set specialisation is gone; the generic reinterpret_cast is back";

	// The SERVER half read game/server/baseentity.h, which is compiled by
	// nothing since plan_server_novalve.md P5 and left the farm in P6; it is
	// retired with its subject.
	const std::string szClient = ReadRoot( "src/game/client/c_baseentity.h" );
	ASSERT_FALSE( szClient.empty() );
	EXPECT_NE( std::string::npos, szClient.find( "IGameEntity *CHandle<IGameEntity>::Get() const" ) )
		<< "the CLIENT Get specialisation is gone; the generic C cast is back";
}

TEST( CloseRulings, NeitherSpecialisationIsACastOntoTheEntity )
{
	// Existing is not enough: a specialisation that did the same C cast would
	// pass the fact above and be exactly the bug. Each body must ASK.
	const std::string szEhandle = ReadRoot( "src/game/shared/ehandle.h" );
	const size_t iSet = szEhandle.find( "void CHandle<IGameEntity>::Set( const IGameEntity* pVal )" );
	ASSERT_NE( std::string::npos, iSet );
	const std::string szSetBody = szEhandle.substr( iSet, 220 );
	EXPECT_NE( std::string::npos, szSetBody.find( "SourceSharp_EntityHandle" ) )
		<< "Set no longer reaches the entity-list scan:\n" << szSetBody;
	EXPECT_EQ( std::string::npos, szSetBody.find( "reinterpret_cast" ) )
		<< "Set reinterprets again:\n" << szSetBody;

	// The server Get body was read out of game/server/baseentity.h, which is
	// compiled by nothing since plan_server_novalve.md P5 and left the farm in
	// P6; that half is retired with its subject.
}

//=============================================================================//
// RULING 3 — EntityFromHandle keeps naming the RETIRED spellings.
//=============================================================================//

TEST( CloseRulings, TheEntityFromHandleRowStillCatchesTheRetiredSpellings )
{
	const std::string szRe = PatternFor( "EntityFromHandle" );
	ASSERT_FALSE( szRe.empty() );

	EXPECT_TRUE( Matches( szRe, "\tIGameEntity *p = EntityFromEntityHandle( pHandleEntity );" ) )
		<< "re-introducing EntityFromEntityHandle would no longer be a red gate";
	EXPECT_TRUE( Matches( szRe, "\tIGameEntity *p = TraceEntity( tr );" ) )
		<< "re-introducing TraceEntity would no longer be a red gate";
}

TEST( CloseRulings, TheEntityFromHandleRowDoesNotCatchTheNeutralPairThatReplacedThem )
{
	const std::string szRe = PatternFor( "EntityFromHandle" );
	ASSERT_FALSE( szRe.empty() );

	EXPECT_FALSE( Matches( szRe, "\tIGameEntity *p = GameEntityFromEntityHandle( pHandleEntity );" ) );
	EXPECT_FALSE( Matches( szRe, "\tIGameEntity *p = TraceGameEntity( tr );" ) );

	// and UTIL_TraceEntity was never a site: `\b` does not fire after '_'.
	EXPECT_FALSE( Matches( szRe, "\tUTIL_TraceEntity( pEnt, a, b, mask, &tr );" ) );
}

TEST( CloseRulings, TheRetiredPairIsActuallyGoneFromTheSharedHeader )
{
	// The row above is a TRIPWIRE, and a tripwire over text nobody deleted is
	// theatre. This is the other half: the two functions really are not there.
	const std::string szUtil = ReadRoot( "src/game/shared/util_shared.h" );
	ASSERT_FALSE( szUtil.empty() ) << "game/shared/util_shared.h is missing";

	EXPECT_EQ( std::string::npos, szUtil.find( "inline IGameEntity *EntityFromEntityHandle(" ) )
		<< "EntityFromEntityHandle is declared again";
	EXPECT_EQ( std::string::npos, szUtil.find( "inline IGameEntity *TraceEntity(" ) )
		<< "TraceEntity is declared again";

	EXPECT_NE( std::string::npos, szUtil.find( "inline IGameEntity *GameEntityFromEntityHandle(" ) )
		<< "the neutral resolver is gone and nothing replaced it";
	EXPECT_NE( std::string::npos, szUtil.find( "inline IGameEntity *TraceGameEntity(" ) )
		<< "the neutral trace accessor is gone and nothing replaced it";
}

TEST( CloseRulings, TheNeutralResolverAsksTheVirtualRatherThanCasting )
{
	// WHY it is not a site: it calls GetGameEntity(), which is a virtual the
	// object answers for itself, on BOTH sides. If that body ever becomes a
	// cast, the ruling that took it out of the gated family stops holding.
	const std::string szUtil = ReadRoot( "src/game/shared/util_shared.h" );
	const size_t i = szUtil.find( "inline IGameEntity *GameEntityFromEntityHandle( IHandleEntity *pHandleEntity )" );
	ASSERT_NE( std::string::npos, i );
	const std::string szBody = szUtil.substr( i, 700 );

	EXPECT_NE( std::string::npos, szBody.find( "GetGameEntity()" ) )
		<< "the resolver stopped asking:\n" << szBody;
	EXPECT_NE( std::string::npos, szBody.find( "GetIClientEntity()" ) )
		<< "the CLIENT arm is gone, so shared trace code resolves a managed "
		   "entity to nothing again:\n" << szBody;
	EXPECT_EQ( std::string::npos, szBody.find( "GetBaseEntity()" ) )
		<< "the resolver went back to the method every implementation of which "
		   "answers a constant NULL:\n" << szBody;
}

//=============================================================================//
// RULING 4 — THE WIDENING, which is the one that found work.
//=============================================================================//

TEST( CloseRulings, EveryTypedCastRowToleratesALeadingConst )
{
	// `static_cast< const C_StandaloneEntity * >` was invisible to this file
	// until the close, and there was a real one in the tree.
	static const char *const kRows[] = { "cstyle", "static_cast", "reinterpret_cast",
	                                     "dynamic_cast", "assert_cast" };
	static const char *const kText[] = {
		"\t( const CBaseEntity * )p",
		"\tstatic_cast< const C_StandaloneEntity * >( pEnt )",
		"\treinterpret_cast< const CBaseEntity * >( p )",
		"\tdynamic_cast< const C_BaseEntity * >( p )",
		"\tassert_cast< const CBasePlayer * >( p )",
	};
	for ( size_t i = 0; i < sizeof( kRows ) / sizeof( kRows[ 0 ] ); i++ )
	{
		const std::string szRe = PatternFor( kRows[ i ] );
		ASSERT_FALSE( szRe.empty() ) << kRows[ i ];
		EXPECT_TRUE( Matches( szRe, kText[ i ] ) )
			<< kRows[ i ] << " stopped seeing a const-qualified cast: " << kText[ i ];
	}
}

TEST( CloseRulings, EveryTypedCastRowAlsoNamesTheHierarchyThatActuallyEXISTS )
{
	// The stock entity classes are gone from this tree, so the rows naming them
	// are tripwires. THIS is the half that can still fire: IGameEntity is the
	// hierarchy the port has.
	static const char *const kRows[] = { "cstyle", "static_cast", "reinterpret_cast",
	                                     "dynamic_cast", "assert_cast" };
	static const char *const kText[] = {
		"\t( IGameEntity * )pThing",
		"\tstatic_cast< IGameEntity * >( pThing )",
		"\treinterpret_cast< IGameEntity * >( pThing )",
		"\tdynamic_cast< IGameEntity * >( pThing )",
		"\tassert_cast< IGameEntity * >( pThing )",
	};
	for ( size_t i = 0; i < sizeof( kRows ) / sizeof( kRows[ 0 ] ); i++ )
	{
		const std::string szRe = PatternFor( kRows[ i ] );
		ASSERT_FALSE( szRe.empty() ) << kRows[ i ];
		EXPECT_TRUE( Matches( szRe, kText[ i ] ) )
			<< kRows[ i ] << " does not name IGameEntity, so the only entity "
			<< "hierarchy this build has is ungated: " << kText[ i ];
	}
}

//=============================================================================//
// THE PREMISE UNDER 4/N, 5/N AND 7/N — no GetBaseEntity() answers an SDK entity.
//
// Four whole gated families were deleted as unreachable on the strength of this
// one fact. It was read out of the two linked binaries at the time; here it is
// read out of the SOURCE, so it is checked on every run rather than once.
//
// ONE OVERRIDE ANSWERS `this` SINCE plan_server_native_v2 T1.3, and the premise
// still holds: CStandaloneEntity's GetBaseEntity() is its own entity face, the
// server's one accessor once the patch's GetGameEntity() went, and no SDK entity
// exists for it to be confused with. What the deleted families needed was that
// no caller reads GetBaseEntity() as "the SDK's CBaseEntity"; the entity gate's
// GetBaseEntity family (tools/entity-census-patterns) holds the call sites in
// shim TUs at zero, so the reads are handle_cross.h's partition and
// ss_entityhandle.h's crossing. The client's override and the world's two
// still answer NULL.
//=============================================================================//

TEST( CloseRulings, EveryGetBaseEntityImplementationInTheTreeAnswersNull )
{
	struct Site_t { const char *pszFile; const char *pszSignature; const char *pszAnswer; };
	static const Site_t kSites[] = {
		{ "src/sourcesharp/game/server/shell_standalone.h",        "GetBaseEntity() OVERRIDE", "return this" },
		{ "src/sourcesharp/game/client/shell_standalone_client.h", "GetBaseEntity() OVERRIDE", "return NULL" },
		// Was src/game/server/world.h, which is compiled by nothing since
		// plan_server_novalve.md P5 and left the farm in P6. CWorld and
		// CWorldNetworkable moved to our ss_world.h (P5 lane E), so the same two
		// overrides are read from where the server now compiles them.
		{ "src/sourcesharp/game/server/ss_world.h",                "GetBaseEntity() OVERRIDE", "return NULL" },
	};

	int nChecked = 0;
	for ( size_t i = 0; i < sizeof( kSites ) / sizeof( kSites[ 0 ] ); i++ )
	{
		const std::string szBody = ReadRoot( kSites[ i ].pszFile );
		ASSERT_FALSE( szBody.empty() ) << kSites[ i ].pszFile;

		int nInThisFile = 0;
		size_t iPos = 0;
		while ( ( iPos = szBody.find( kSites[ i ].pszSignature, iPos ) ) != std::string::npos )
		{
			const size_t iEnd = szBody.find( '\n', iPos );
			const std::string szLine = szBody.substr( iPos, iEnd - iPos );
			iPos = iEnd;

			// A DECLARATION IS NOT A BODY, and ss_world.h has one of each: CWorld's
			// GetBaseEntity is inline at its declaration, CWorldNetworkable's is
			// declared there and defined out of line further down. Reading the
			// declaration line for "return NULL" failed the fact against a
			// perfectly correct file -- and, worse, would have PASSED had the
			// out-of-line body been changed to return something. So a
			// declaration sends the sweep looking for the definition.
			if ( szLine.find( '{' ) != std::string::npos )
			{
				EXPECT_NE( std::string::npos, szLine.find( kSites[ i ].pszAnswer ) )
					<< kSites[ i ].pszFile << ": a GetBaseEntity() that does not answer '"
					<< kSites[ i ].pszAnswer << "', which is the premise four deleted gate "
					<< "families rested on:\n  " << szLine;
				nChecked++; nInThisFile++;
				continue;
			}

			// `...::GetBaseEntity()` further down the same file, and the next
			// non-blank, non-brace line of it.
			const size_t iDef = szBody.find( "::GetBaseEntity()" );
			ASSERT_NE( std::string::npos, iDef )
				<< kSites[ i ].pszFile << ": GetBaseEntity() is DECLARED and never defined "
				<< "in this file, so this fact cannot see what it answers";
			const std::string szDef = szBody.substr( iDef, 160 );
			EXPECT_NE( std::string::npos, szDef.find( "return NULL" ) )
				<< kSites[ i ].pszFile << ": the out-of-line GetBaseEntity() does not answer "
				<< "NULL:\n" << szDef;
			nChecked++; nInThisFile++;
		}

		// PER FILE, not only in total. A total-only guard does not fire: with one
		// of three signatures broken this fact still counted 3 (ss_world.h carries
		// two), passed, and checked one file fewer than it claims. Mutation-found.
		EXPECT_GE( nInThisFile, 1 )
			<< kSites[ i ].pszFile << ": the signature '" << kSites[ i ].pszSignature
			<< "' matched nothing, so this file was not checked at all";
	}

	// THE COUNTER THAT REPORTS ZERO IS THE ONE TO DISTRUST. If the signatures
	// stop matching, every EXPECT above runs zero times and this fact passes
	// while checking nothing.
	EXPECT_EQ( 4, nChecked )
		<< "the sweep matched " << nChecked << " GetBaseEntity() overrides where it "
		   "expects exactly four (CStandaloneEntity, C_StandaloneEntity, and CWorld's "
		   "two). A different number means the text moved and this fact is measuring "
		   "something other than what it says.";
}

//=============================================================================//
// THE INSTRUMENT REPAIR — `scan_sites` strips STRING LITERALS now.
//
// `game/server/physics.cpp:506` is a §7 REFUSAL MESSAGE, text this build PRINTS,
// and it names the two symbols that hold the shadow list in place: "...would
// carry entitem_t's EHANDLE and g_EntListMemPool into a file §6 keeps". That
// sentence raised the EHANDLE row by one on the server, forever, and the only
// way to lower it was to reword a refusal. The row's own comment already carried
// the ruling for `//`: "a gate that cannot tell a comment from a declaration is
// a gate somebody passes by rewording a comment".
//
// DRIVEN THROUGH THE TOOL'S OWN FUNCTION, not a copy of it. `scan_sites` is
// sed'd out of tools/entity-gates and sourced, so a fixture cannot drift from
// its subject: if the function changes, this runs the changed one.
//=============================================================================//

TEST( CloseRulings, ScanSitesSeesARealCastAndIgnoresOneInAStringOrAComment )
{
	const char *pszRoot = WorktreeRoot();
	ASSERT_TRUE( pszRoot != NULL );

	const std::string szDir = "/tmp/close_rulings_scan_fixture";
	Shell( "rm -rf '" + szDir + "' && mkdir -p '" + szDir + "'" );

	// Three occurrences of ONE spelling: code, string, comment.
	const std::string szProbe =
		"void f( void *p )\n"
		"{\n"
		"\tIGameEntity *a = static_cast< IGameEntity * >( p );\n"
		"\tMsg( \\\"static_cast< IGameEntity * >( p ) is what the refusal says\\\" );\n"
		"\t// static_cast< IGameEntity * >( p ) in a comment\n"
		"}\n";
	Shell( "printf '%b' \"" + szProbe + "\" > '" + szDir + "/probe.cpp'" );
	Shell( "echo probe.cpp > '" + szDir + "/list.txt'" );

	const std::string szTool = std::string( pszRoot ) + "/tools/entity-gates";
	Shell( "sed -n '/^scan_sites() {/,/^}/p' '" + szTool + "' > '" + szDir + "/scan.sh'" );

	const std::string szRe = PatternFor( "static_cast" );
	ASSERT_FALSE( szRe.empty() );

	const std::string szOut = Shell(
		"export LC_ALL=C; SRC_ABS='" + szDir + "'; . '" + szDir + "/scan.sh'; "
		"scan_sites '" + szDir + "/list.txt' '" + szRe + "'" );

	// The code site, and ONLY the code site.
	EXPECT_NE( std::string::npos, szOut.find( "probe.cpp:3" ) )
		<< "scan_sites stopped seeing a real cast, which is the direction that "
		   "turns the gate green over a tree that is not finished:\n" << szOut;
	EXPECT_EQ( std::string::npos, szOut.find( "probe.cpp:4" ) )
		<< "scan_sites counts a cast spelling inside a STRING LITERAL, so a §7 "
		   "refusal message raises gate 3 and the only fix is to reword the "
		   "refusal:\n" << szOut;
	EXPECT_EQ( std::string::npos, szOut.find( "probe.cpp:5" ) )
		<< "scan_sites counts a cast spelling inside a COMMENT:\n" << szOut;

	Shell( "rm -rf '" + szDir + "'" );
}
