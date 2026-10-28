//=============================================================================//
// THE CLIENT FLIP'S RULING ON c_baseentity_statics.cpp's FREE FUNCTIONS, RUN --
// CLIENT_FLIP lane 2 (`c8-entitystatics`), plan_entity.md §7.
//
// TWO HALVES, AND THEY ARE DIFFERENT KINDS OF EVIDENCE.
//
// THE COUNTER AND THE TABLE ARE RUN. Whether a refusal counts, whether it says
// something, whether a second refusal of one row opens a second row, whether
// every row carries a reason AND a successor -- these are properties of a BODY
// and of DATA, and they are checked by compiling the SHIPPED header
// (sourcesharp/game/client/shell_entitystatics.h) and running it. The ruling
// table lives in that header precisely so this file reads the rows the game
// ships rather than a transcription of them; a transcription is a copy that
// drifts, which is how a §7 declaration quietly stops matching its code.
//
// THE ARRANGEMENT IS SOURCE-READ. Which call sites survive in
// shell_clientdll.cpp, and that the four fully-retired functions are called
// from nowhere, cannot be linked: that file includes cbase.h's whole world.
// So it is read as text -- and read for BOTH directions, because a needle that
// only asserts absence passes on a file somebody emptied and a needle that only
// asserts presence passes on a file somebody duplicated a line into.
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <sstream>
#include <algorithm>

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <set>

#include "shell_entitystatics.h"

//-----------------------------------------------------------------------------
// The warning half, defined here rather than linked from tier0 -- the seam the
// header declares. It is what lets this assert that a refusal SAYS something as
// well as counting: a counter nobody reads until they think to is not what §7
// asked for.
//-----------------------------------------------------------------------------
namespace
{

struct RecordedWarning
{
	std::string strFunction;
	std::string strReason;
	int         nTotal;
};

std::vector< RecordedWarning > g_Warnings;

std::string ReadSource( const char *pRelative )
{
	// Relative to src/, which is where the harness runs these binaries from --
	// the same convention shell_clientdll_test.cpp already uses.
	std::ifstream in( pRelative );
	EXPECT_TRUE( in.good() ) << "cannot open " << pRelative;
	std::ostringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

//-----------------------------------------------------------------------------
// A CALL, NOT A MENTION. Every one of these names appears in this tree inside
// prose -- the retirement notes in shell_clientdll.cpp name the very functions
// they retired, on purpose, so a reader meets the ruling where the call used to
// be. A raw substring needle would count those and report every retired
// function as still called.
//
// So: count `Name(` occurrences on lines that are not comment lines. That is
// what "prose counts as code to a raw grep" means here, and briefs/COMMON.md
// lists it as a standing hazard because it has already bitten this port.
//-----------------------------------------------------------------------------
int CountCalls( const std::string &strSource, const std::string &strName )
{
	const std::string strNeedle = strName + "(";
	int nCalls = 0;

	std::istringstream lines( strSource );
	std::string strLine;

	while ( std::getline( lines, strLine ) )
	{
		const size_t iFirst = strLine.find_first_not_of( " \t" );
		if ( iFirst != std::string::npos &&
		     strLine.compare( iFirst, 2, "//" ) == 0 )
			continue;

		for ( size_t i = strLine.find( strNeedle );
		      i != std::string::npos;
		      i = strLine.find( strNeedle, i + 1 ) )
			nCalls++;
	}

	return nCalls;
}

const char *const kStaticsSource = "sourcesharp/game/client/shell_entitystatics.cpp";

// No function in shell_entitystatics.cpp refuses at runtime any more: the last
// refusal site, EntityInterp_UpdateFrameGate, went to managed code on
// 2026-10-25 as InterpolationGate (SourceSharp.Client), with the two facts
// that read it (BothSurvivingRefusalsSayWhoRefusedAndWhy and
// TheInterpolationRefusalIsOnTheClosingEdgeAndNotEveryFrame).

} // namespace

//=============================================================================//
// THE RULING TABLE IS RETIRED, PHASE 1c, AND TWELVE FACTS WITH IT. WHAT IS
// ASSERTED INSTEAD IS THE THING §7 ACTUALLY ASKS FOR.
//
// WHAT WAS HERE. Twelve facts over a fifteen-row table in shell_entitystatics.h
// and the CShellEntityStaticsRulings counter beside it:
//
//   EveryRowCarriesAReasonAndASuccessor       TheToolRecordingRowAdmitsNothingReplacesIt
//   EveryFunctionTheFlipStrandsHasARow        ARefusalCountsAndWarns
//   TheAbsSaveStackPairIsRuledWithItsGroup    ASecondRefusalOfOneRowOpensNoSecondRow
//   TheAllocatorAndTheAbsAccessorsMoved       RefusingANameThatIsNotInTheTableCountsNothing
//   TheSixFrameLoopFunctionsRetired           TwoCountersShareNothing
//   TheTwoRulingsAccountForEveryRow           TheRowCountIsSizeofDerived
//
// The table lived in the header exactly so that these could read THE ROWS THE
// GAME SHIPS rather than a transcription, and the counter class was engine-free
// exactly so that its BODY could be run here. Both of those were the right call
// for what they were checking.
//
// WHY THEY GO. Thirteen of the fifteen rows describe work that is FINISHED --
// a function that moved into shell_entitystatics.cpp, or one that retired with
// a hierarchy that no longer exists. A row that cannot fire is a paragraph, and
// the facts above were checking that the paragraphs were well-formed: that each
// carried a reason, that the reason was non-empty, that the row count matched a
// sizeof. None of that is a claim about what the build DOES.
//
// The four counter facts (ARefusalCountsAndWarns and its three siblings) were
// claims about behaviour -- but about the behaviour of the COUNTER, which is
// the instrument, and the instrument is what Phase 1c removes. cs_cl_entitystatics,
// the only thing that ever read a count, is gone; nothing under tools/, in
// managed code or in the other DLL ever drove it.
//
// WHAT §7 ACTUALLY ASKS FOR IS THE REFUSAL, and no row can fire now: the last,
// the interpolation frame gate, became managed code on 2026-10-25
// (InterpolationGate) and its two refusal facts went with it;
// EntityTool_RecordEntities is deleted (SFM is dead, 2026-10-19).
//=============================================================================//

//=============================================================================//
// THE ARRANGEMENT, SOURCE-READ.
//=============================================================================//

//-----------------------------------------------------------------------------
// THE FOUR THAT RETIRED OUTRIGHT ARE CALLED FROM NOWHERE, and the two that
// retired but stayed reachable ARE still called. Both directions in one fact,
// because either alone passes on a file somebody broke the other way.
//-----------------------------------------------------------------------------
TEST( ShellEntityStatics, TheClientDllCallsTheTwoSurvivorsAndNeitherOfTheFour )
{
	const std::string strDll = ReadSource( "sourcesharp/game/client/shell_clientdll.cpp" );
	ASSERT_FALSE( strDll.empty() );

	EXPECT_EQ( 0, CountCalls( strDll, "EntityInterp_UpdateFrameGate" ) )
		<< "shell_clientdll.cpp calls the native frame gate again; the policy "
		   "is SourceSharp.Client's InterpolationGate (2026-10-25).";
	// SFM is dead (user ruling 2026-10-19): the tool-recording pass is
	// deleted, call and definition both.
	EXPECT_EQ( 0, CountCalls( strDll, "EntityTool_RecordEntities" ) )
		<< "shell_clientdll.cpp calls EntityTool_RecordEntities again; the tool "
		   "framework it posted to is deleted.";
	EXPECT_EQ( 0, CountCalls( ReadSource( kStaticsSource ), "EntityTool_RecordEntities" ) )
		<< "shell_entitystatics.cpp defines EntityTool_RecordEntities again.";
	EXPECT_NE( ReadSource( kStaticsSource ).find( "int32_t SourceSharp_ClientEntitiesUpdateVisibilityAll( void )" ),
	           std::string::npos )
		<< "the statics file did not read, so the zero above is not evidence.";

	static const char *const kGone[] =
	{
		"EntityInterp_InterpolateServerEntities",
		"EntityInterp_MarkAimEntsDirty",
		"EntityInterp_CalcAimEntPositions",
		"EntityInterp_CheckCLInterpChanged",
		"EntityRender_AddVisibleEntities",
	};

	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_EQ( 0, CountCalls( strDll, kGone[ i ] ) )
			<< kGone[ i ] << " retired with the hierarchy and must not be "
			   "called. (Its NAME appears in the retirement note where the call "
			   "used to be, which is prose and not a call.)";
	}
}

//-----------------------------------------------------------------------------
// THE TEN ABS CALL SITES SURVIVED. The four accessors moved rather than retired
// precisely because shell_clientdll.cpp drives them; a lane that moved the
// bodies and dropped the calls would pass every fact above and change the
// frame's behaviour.
//-----------------------------------------------------------------------------
TEST( ShellEntityStatics, TheClientDllStillDrivesTheAbsQueryFlags )
{
	const std::string strDll = ReadSource( "sourcesharp/game/client/shell_clientdll.cpp" );
	ASSERT_FALSE( strDll.empty() );

	EXPECT_EQ( 4, CountCalls( strDll, "EntityAbs_SetQueriesValid" ) );
	EXPECT_EQ( 4, CountCalls( strDll, "EntityAbs_SetRecomputationsEnabled" ) );
	EXPECT_EQ( 2, CountCalls( strDll, "EntityAbs_AreQueriesValid" ) );
	EXPECT_EQ( 2, CountCalls( strDll, "EntityAbs_AreRecomputationsEnabled" ) );
}

//=============================================================================//
//
// CLIENT FAMILY 9 — THE TWO RENDERING TOGGLES, AND THE FACTS THAT FAIL IF
// EITHER STOPS BEING REGISTERED. Lane `f-block`, plan_entity.md §6's ConVar row.
//
// WHY THIS NEEDS ITS OWN KIND OF CHECK. A ConVar that stops existing is
// invisible to every other test in this tree. It is not a build error: the
// reader's `extern` is satisfied by any definition anywhere on the link line,
// and when the defining translation unit leaves, the link simply fails somewhere
// else or -- worse -- another definition is found. It is not a runtime error
// either: the console variable is just not there, and `GetBool()` on an
// unregistered ConVar answers its constructed default rather than complaining.
// So the rendering toggle silently stops working and nothing reports it.
//
// A ConVar in this build is REGISTERED if and only if three things are true:
//
//   1. it is CONSTRUCTED, exactly once, at static-initialisation time --
//      which means FILE SCOPE. A function-local ConVar is constructed on first
//      use, which is AFTER CShellClientDLL::Init calls ConVar_Register(
//      FCVAR_CLIENTDLL ) once, so it never registers at all.
//   2. it is constructed exactly ONCE across the whole client link line. Two
//      ConVars of one name is a duplicate registration, and the engine keeps
//      the first -- so the toggle a reader binds to and the toggle the console
//      sets can be different objects.
//   3. the translation unit holding it is COMPILED -- i.e. it is on
//      client_sourcesharp's source list (src/client/native/CMakeLists.txt;
//      client_csharp.vpc's $File list until Phase 6b). This is the one the
//      flip breaks, and it is why these two moved at all.
//
// The facts below check all three, plus that each toggle still has the staying
// reader that is the reason it was worth keeping. They are source-read for
// convar_notify_test.cpp:58-60's stated reason: the registry lives in the
// engine's closed CCvar, and a fake ICvar reproducing it would be a fixture
// standing in for native, which drifts silently.
//
//=============================================================================//

namespace
{

//-----------------------------------------------------------------------------
// Brace depth at a position, counting only braces that are CODE: not inside a
// // comment, a /* */ comment, a "string" or a 'char'. Depth 0 is file scope,
// which is fact 1's whole subject -- and a naive counter gets it wrong on this
// very file, whose ConVar help strings contain no braces but whose neighbours'
// prose does.
//-----------------------------------------------------------------------------
int BraceDepthAt( const std::string &s, size_t at )
{
	int nDepth = 0;
	bool bLine = false, bBlock = false, bStr = false, bChr = false;

	for ( size_t i = 0; i < at && i < s.size(); i++ )
	{
		const char c = s[i];
		const char n = ( i + 1 < s.size() ) ? s[i + 1] : '\0';

		if ( bLine )                   { if ( c == '\n' ) bLine = false; continue; }
		if ( bBlock )                  { if ( c == '*' && n == '/' ) { bBlock = false; i++; } continue; }
		if ( bStr )
		{
			if ( c == '\\' ) { i++; continue; }
			if ( c == '"' ) bStr = false;
			continue;
		}
		if ( bChr )
		{
			if ( c == '\\' ) { i++; continue; }
			if ( c == '\'' ) bChr = false;
			continue;
		}

		if ( c == '/' && n == '/' ) { bLine  = true; i++; continue; }
		if ( c == '/' && n == '*' ) { bBlock = true; i++; continue; }
		if ( c == '"' )             { bStr   = true; continue; }
		if ( c == '\'' )            { bChr   = true; continue; }

		if ( c == '{' ) nDepth++;
		else if ( c == '}' ) nDepth--;
	}

	return nDepth;
}

//-----------------------------------------------------------------------------
// Every position at which `strFile` DEFINES the named ConVar at file scope.
//
// A DEFINITION AND NOT AN extern, AND NOT PROSE. `extern ConVar name;` has no
// `(` and is skipped by the needle; a comment naming the ConVar is skipped by
// the depth walker's comment handling only for braces, so the needle itself is
// required to be outside a comment -- which is what the `//` scan does.
//
// The needle tolerates the spacing this tree actually uses: `ConVar name(`,
// `ConVar  name(` (two spaces -- c_baseentity.cpp wrote r_drawmodeldecals that
// way and shell_entitystatics_test.cpp:478 already had to match it) and a tab.
//-----------------------------------------------------------------------------
std::vector< size_t > FileScopeConVarDefs( const std::string &strFile,
                                           const std::string &strName )
{
	std::vector< size_t > out;
	const std::string strTail = strName + "(";

	for ( size_t i = strFile.find( strTail ); i != std::string::npos;
	      i = strFile.find( strTail, i + 1 ) )
	{
		// Walk back over the spacing to find the `ConVar` keyword.
		size_t j = i;
		while ( j > 0 && ( strFile[j - 1] == ' ' || strFile[j - 1] == '\t' ) )
			j--;

		const std::string kKeyword = "ConVar";
		if ( j < kKeyword.size() ) continue;
		if ( strFile.compare( j - kKeyword.size(), kKeyword.size(), kKeyword ) != 0 )
			continue;

		// Not a `static ConVar` and not an `extern ConVar` -- static is fine
		// (still file scope, still registers); extern is a declaration and
		// defines nothing. Reject only the latter.
		size_t k = j - kKeyword.size();
		while ( k > 0 && ( strFile[k - 1] == ' ' || strFile[k - 1] == '\t' ) )
			k--;
		const std::string kExtern = "extern";
		if ( k >= kExtern.size() &&
		     strFile.compare( k - kExtern.size(), kExtern.size(), kExtern ) == 0 )
			continue;

		// Not inside a // comment: scan back to the line start.
		const size_t iLine = strFile.rfind( '\n', i );
		const size_t iFrom = ( iLine == std::string::npos ) ? 0 : iLine + 1;
		if ( strFile.substr( iFrom, i - iFrom ).find( "//" ) != std::string::npos )
			continue;

		if ( BraceDepthAt( strFile, i ) != 0 )
			continue;

		out.push_back( i );
	}

	return out;
}

//-----------------------------------------------------------------------------
// Every source path client_sourcesharp compiles, farm-relative, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/client/native/CMakeLists.txt). Until Phase 6b this walked BOTH `$File`
// spellings of client_csharp.vpc (plain rows and one backslash-continued row);
// the reader now throws on a missing, empty or stale list itself.
//-----------------------------------------------------------------------------
std::vector< std::string > ClientCompiledSources()
{
	const std::set< std::string > s = cmake_graph::CompiledSources( cmake_graph::kClient );
	return std::vector< std::string >( s.begin(), s.end() );
}

// The proof-of-read: a port file no eviction can take. It used to be the
// path only the vpc's continuation row carried, which proved the walker had
// followed the second spelling; that spelling is gone with VPC, and the file
// is still compiled, so it stays as the anchor.
// (bone_setup.cpp until it left at plan_physics_bones_native_zero Phase 3;
// collisionutils.cpp until C7 cluster 12 S2 deleted it. steamid.cpp is the
// client's SteamID class, which the engine's handshake needs as long as there
// is a client.)
const char *const kClientProofOfRead = "sourcesharp/common/steamid.cpp";

const char *const kEntityStatics = "sourcesharp/game/client/shell_entitystatics.cpp";

} // namespace

//-----------------------------------------------------------------------------
// FACT 1 of 5 — each toggle is defined EXACTLY ONCE across every translation
// unit the client compiles. Two definitions is a duplicate registration; zero
// is the toggle having stopped existing, which is the whole failure this lane
// exists to prevent.
//-----------------------------------------------------------------------------
TEST( ClientFamily9ConVars, EachToggleIsDefinedExactlyOnceInTheWholeClientBuild )
{
	const std::vector< std::string > vecSources = ClientCompiledSources();

	// The reader itself must not be the thing that is broken: a reader that
	// silently matched nothing would make every count below 0 and every
	// "exactly once" assertion fail for the wrong reason.
	//
	// The liveness floor that stood here (>= 250 paths, moved once from 280
	// when THE FLIP took fifteen rows out) is RETIRED IN 6b: cmake_graph
	// cross-checks compile_commands.json against the CMakeLists list and
	// throws on an empty or short one. What stays is the proof-of-read.
	ASSERT_NE( std::find( vecSources.begin(), vecSources.end(),
	                      std::string( kClientProofOfRead ) ),
	           vecSources.end() )
		<< "client_sourcesharp's compiled list does not name " << kClientProofOfRead
		<< ", so it is not the client's source list";

	// NEITHER NAME NOW, and the two got here by different roads.
	//
	// r_drawmodeldecals left this family in Phase 1c because it was never the
	// game DLL's to declare: the ENGINE declares it (the name is in the
	// shipped engine.so), and the engine's constructor runs first, so
	// the copy here was a duplicate registration nobody's console ever bound
	// to. Its absence has a fact of its own below.
	//
	// vcollide_wireframe left at lane 1e-abi, and it left UPWARDS rather than
	// sideways: it is a managed [ConVar] now
	// (SourceSharp.Client/ConVars.cs), which is what plan_shim.md §6's row asks
	// of every tuning value. Phase 1c could not move it for exactly one reason
	// -- the toggle draws nothing by itself, its whole effect is its CHANGE
	// CALLBACK, and a managed OnChanged had no ABI slot for "re-evaluate every
	// client shell's visibility". 1e-abi appended EntitiesUpdateVisibilityAll
	// and the blocker went.
	//
	// SO THE INVARIANT FLIPPED FOR THIS NAME TOO: zero native definitions, a
	// managed declaration, and readers that resolve BY NAME. All three are
	// asserted -- zero alone would be satisfied by a toggle that had simply
	// stopped existing, which is the failure this whole family was written to
	// catch.
	const char *const kNames[] = { "vcollide_wireframe", "r_drawmodeldecals" };

	for ( size_t n = 0; n < sizeof( kNames ) / sizeof( kNames[ 0 ] ); n++ )
	{
		int nDefs = 0;
		std::string strWhere;

		for ( size_t i = 0; i < vecSources.size(); i++ )
		{
			std::ifstream in( vecSources[i].c_str() );
			if ( !in.good() ) continue;
			std::ostringstream ss;
			ss << in.rdbuf();

			const size_t nHere =
				FileScopeConVarDefs( ss.str(), kNames[n] ).size();

			if ( nHere > 0 )
			{
				nDefs += (int)nHere;
				strWhere += " " + vecSources[i];
			}
		}

		EXPECT_EQ( 0, nDefs )
			<< kNames[n] << " is defined " << nDefs << " time(s) in the compiled "
			<< "client (" << strWhere << "). Neither of these is the game DLL's "
			<< "to declare any more -- one is the engine's and one is managed "
			<< "code's -- and a second registration of a name is neither a "
			<< "compile error nor a link error: the console sets one object and "
			<< "the reader can be bound to the other.";
	}

	// AND BOTH STILL EXIST SOMEWHERE, which the absences above cannot say. A
	// toggle declared in no place at all reads as its type's zero everywhere,
	// and a rendering toggle that reads as zero looks exactly like one somebody
	// turned off.
	const std::string strManaged =
		ReadSource( "sourcesharp/managed/SourceSharp.Client/ConVars.cs" );
	ASSERT_FALSE( strManaged.empty() )
		<< "cannot read SourceSharp.Client/ConVars.cs";

	EXPECT_NE( strManaged.find( "ConVar(\"vcollide_wireframe\"" ), std::string::npos )
		<< "vcollide_wireframe is declared in neither C++ nor C#, so the console "
		   "variable does not exist at all and every reader gets 0.";
}

//-----------------------------------------------------------------------------
// FACT 3 of 5 — RETIRED WITH THE CODE IT PINNED (lane 1e-abi).
//
// It was BothToggleDefinitionsAreAtFileScopeSoTheyRegisterAtStaticInit, and it
// said: the surviving native definition is at FILE SCOPE, so it is constructed
// during static initialisation and is on the pending list when
// ConVar_Register( FCVAR_CLIENTDLL ) runs — a function-local ConVar compiles,
// links, reads correctly at its default, and never appears in the console.
//
// There is no native definition left for it to be about. vcollide_wireframe is
// a managed [ConVar] and registration is ConVarRegistry's, which registers
// every declared convar in one pass; the static-initialisation hazard is a
// property of the C++ ConVar constructor and does not exist on that path. The
// fact would have had to `ASSERT_EQ( 1u, ... )` on a vector that is now empty,
// which is a check that can only fail — no more a claim about the build than
// one that can only pass.
//
// WHAT REPLACES IT is the pair above: zero native definitions (FACT 1) and a
// managed declaration that exists (FACT 1's second half). The registration
// hazard itself is ConVarRegistry's own and is covered by its tests.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// AND THE ONE THAT LEFT THE FAMILY: r_drawmodeldecals IS THE ENGINE'S NAME AND
// THE GAME DLL DECLARES IT NOWHERE.
//
// THIS IS THE FACT PHASE 1c REPLACED THREE HALF-FACTS WITH, and it is a
// different claim from the ones it replaces rather than a narrowed one. Family
// 9 treated this toggle as the game DLL's to keep registered; the measurement
// says otherwise: engine.so registers r_drawmodeldecals itself (`strings -a`
// finds the name in the shipped binary) and reads it for static props.
//
// engine.so loads and runs its static initialisers before client.so does, so
// the engine's object is what the console sets and what every ConVarRef binds
// to. The game DLL's copy carried FCVAR_ALLOWED_IN_COMPETITIVE and that flag
// therefore applied to nothing. Two registrations of one name is exactly the
// hazard the family's own EachToggleIsDefinedExactlyOnceInTheWholeClientBuild
// exists to catch -- it just could not see across the DLL boundary.
//
// SO THE INVARIANT IS THE OPPOSITE ONE: zero definitions in the client, and a
// reader that resolves BY NAME rather than through an `extern` (which would
// need a definition and would not find one).
//-----------------------------------------------------------------------------
TEST( ClientFamily9ConVars, TheEngineOwnedToggleIsDeclaredByNoClientTranslationUnit )
{
	const std::vector< std::string > vecSources = ClientCompiledSources();
	ASSERT_NE( std::find( vecSources.begin(), vecSources.end(),
	                      std::string( kClientProofOfRead ) ),
	           vecSources.end() )
		<< "client_sourcesharp's compiled list does not name " << kClientProofOfRead;

	int nDefs = 0;
	std::string strWhere;

	for ( size_t i = 0; i < vecSources.size(); i++ )
	{
		std::ifstream in( vecSources[i].c_str() );
		if ( !in.good() ) continue;
		std::ostringstream ss;
		ss << in.rdbuf();

		const size_t nHere = FileScopeConVarDefs( ss.str(), "r_drawmodeldecals" ).size();

		if ( nHere > 0 )
		{
			nDefs += (int)nHere;
			strWhere += " " + vecSources[i];
		}
	}

	EXPECT_EQ( 0, nDefs )
		<< "r_drawmodeldecals is declared by a compiled client translation unit ("
		<< strWhere << "). The engine already declares this name "
		<< "(engine.so registers it) and its static initialisers run "
		<< "first, so this is a duplicate registration: the console sets the "
		<< "engine's object and any flags declared here apply to nothing.";

	// AND THE PORT'S READER RESOLVES IT BY NAME. An `extern ConVar` binds to a
	// DEFINITION, so it cannot reach the engine's at all -- the absence above
	// would be satisfied by a client that simply fails to link, and by one that
	// silently stopped gating decals.
	const std::string strDecals =
		ReadSource( "sourcesharp/game/client/shell_te_backends_client.cpp" );
	ASSERT_FALSE( strDecals.empty() );

	EXPECT_EQ( std::string::npos, strDecals.find( "extern ConVar r_drawmodeldecals" ) )
		<< "the decal arm declares r_drawmodeldecals with an extern again. An "
		   "extern needs a definition on this link line and there is none.";
	// The reader is managed since lane M1-C: DecalConVarsWatch watches the
	// engine's ConVar BY NAME (IShimConVarsClient), and the studio arm reads it.
	std::string strWatch;
	{
		std::ifstream watch( ( cmake_graph::RepoRoot() + "/src/client/managed/TempEnts/DecalSettings.cs" ).c_str() );
		std::stringstream text;
		text << watch.rdbuf();
		strWatch = text.str();
	}
	ASSERT_FALSE( strWatch.empty() ) << "DecalSettings.cs did not read";
	EXPECT_NE( strWatch.find( "public const string ModelDecalsName = \"r_drawmodeldecals\";" ),
	           std::string::npos )
		<< "the decal settings no longer resolve r_drawmodeldecals by name out of "
		   "the engine's registry, so they are reading something else or nothing.";
}

//-----------------------------------------------------------------------------
// FACT 4 of 5 — the translation unit is one client_sourcesharp actually
// COMPILES. It used to hold the definitions; it holds the visibility SWEEP now,
// and the failure is the same shape either way: a perfect body in a file nobody
// builds is a symbol that does not exist, and the slot it is assigned to would
// not link.
//-----------------------------------------------------------------------------
TEST( ClientFamily9ConVars, TheTranslationUnitThatNowHoldsThemIsCompiledIntoTheClient )
{
	const std::vector< std::string > vecSources = ClientCompiledSources();

	EXPECT_NE( std::find( vecSources.begin(), vecSources.end(),
	                      std::string( kEntityStatics ) ),
	           vecSources.end() )
		<< "shell_entitystatics.cpp is not in client_sourcesharp's source list "
		   "(src/client/native/CMakeLists.txt), so "
		   "the visibility sweep the managed toggle calls is not compiled.";
}

//-----------------------------------------------------------------------------
// FACT 5 of 5 — each toggle still has the STAYING reader that is the reason it
// was worth keeping. A registered ConVar nobody reads is a console variable
// that does nothing, which is the same loss wearing a different shape.
//
// Read counts, not mentions: both files carry prose naming the ConVar.
//-----------------------------------------------------------------------------
TEST( ClientFamily9ConVars, EachToggleStillHasTheStayingReaderItWasKeptFor )
{
	// vcollide_wireframe: §6 kept viewrender.cpp (view render) and ragdoll.cpp
	// (the ragdoll builder). ragdoll.cpp left client.so in plan
	// physics_bones_native_zero Phase 3, and viewrender.cpp at C7 step 3: the
	// reader is the render plan now. The executor reads the toggle by name
	// each frame into the plan's inputs (kRenderCvars, a ConVarRef made per
	// read, so a miss at load is not cached), and the view program hands it to
	// both static-prop batches (DrawStaticPropsBatch's I1), as stock's two
	// DrawStaticProps calls did.
	const std::string strExecutor = ReadSource( "sourcesharp/game/client/shell_view_executor_client.cpp" );
	ASSERT_FALSE( strExecutor.empty() );
	const size_t nTable = strExecutor.find( "const char *const kRenderCvars[] =" );
	ASSERT_NE( std::string::npos, nTable ) << "the executor has no render convar table";
	const size_t nTableEnd = strExecutor.find( "};", nTable );
	EXPECT_NE( std::string::npos, strExecutor.substr( nTable, nTableEnd - nTable ).find( "\"vcollide_wireframe\"" ) )
		<< "the executor no longer reads vcollide_wireframe into the plan's inputs";
	EXPECT_NE( std::string::npos, strExecutor.find( "ConVarRef var( kRenderCvars[i], true );" ) )
		<< "the executor's convar reads are no longer by name at the read";

	std::string strProgram;
	{
		std::ifstream program( ( cmake_graph::RepoRoot() + "/src/client/managed/Render/ViewDrawProgram.cs" ).c_str() );
		std::stringstream text;
		text << program.rdbuf();
		strProgram = text.str();
	}
	ASSERT_FALSE( strProgram.empty() );
	EXPECT_NE( std::string::npos, strProgram.find( "input.Cvar(RenderCvar.vcollide_wireframe) != 0f" ) )
		<< "the view program no longer reads vcollide_wireframe";
	int nBatches = 0;
	for ( size_t at = strProgram.find( "DrawStaticPropsBatch, [depthMode, c.VcollideWireframe ? 1 : 0]" ); at != std::string::npos;
	      at = strProgram.find( "DrawStaticPropsBatch, [depthMode, c.VcollideWireframe ? 1 : 0]", at + 1 ) )
		nBatches++;
	EXPECT_EQ( 2, nBatches ) << "the view program's two static-prop batches no longer pass the wireframe flag";
	EXPECT_FALSE( std::ifstream( "game/client/ragdoll.cpp" ).good() )
		<< "game/client/ragdoll.cpp is in the farm again; check whether it reads vcollide_wireframe";
	EXPECT_FALSE( std::ifstream( "game/client/viewrender.cpp" ).good() )
		<< "game/client/viewrender.cpp is in the farm again; it read vcollide_wireframe itself";

	// r_drawmodeldecals: the staying reader is the port's own studio-decal arm,
	// managed since lane M1-C (ClientEntityDecal.AddStudioDecal over
	// DecalSettings, the ENGINE's convar watched by name -- see
	// TheEngineOwnedToggleIsDeclaredByNoClientTranslationUnit for why the
	// definition this used to bind to was never the one anybody was setting).
	std::string strArm;
	{
		std::ifstream arm( ( cmake_graph::RepoRoot() + "/src/client/managed/TempEnts/ClientEntityDecal.cs" ).c_str() );
		std::stringstream text;
		text << arm.rdbuf();
		strArm = text.str();
	}
	ASSERT_FALSE( strArm.empty() ) << "ClientEntityDecal.cs did not read";
	EXPECT_NE( std::string::npos, strArm.find( "if (!settings.ModelDecals)" ) )
		<< "AddStudioDecal no longer gates on r_drawmodeldecals; stock's own "
		   "AddStudioDecal does, so dropping it would paint decals a player had "
		   "turned off.";
}
