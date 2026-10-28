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
	int nTotal;
};

std::vector<RecordedWarning> g_Warnings;

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
//-----------------------------------------------------------------------------
// THE TEN ABS CALL SITES SURVIVED. The four accessors moved rather than retired
// precisely because shell_clientdll.cpp drives them; a lane that moved the
// bodies and dropped the calls would pass every fact above and change the
// frame's behaviour.
//-----------------------------------------------------------------------------
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
//-----------------------------------------------------------------------------
// Every source path client_sourcesharp compiles, farm-relative, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/client/native/CMakeLists.txt). Until Phase 6b this walked BOTH `$File`
// spellings of client_csharp.vpc (plain rows and one backslash-continued row);
// the reader now throws on a missing, empty or stale list itself.
//-----------------------------------------------------------------------------
std::vector<std::string> ClientCompiledSources()
{
	const std::set<std::string> s = cmake_graph::CompiledSources( cmake_graph::kClient );
	return std::vector<std::string>( s.begin(), s.end() );
}

const char *const kEntityStatics = "sourcesharp/game/client/shell_entitystatics.cpp";

} // namespace

//-----------------------------------------------------------------------------
// FACT 1 of 5 — each toggle is defined EXACTLY ONCE across every translation
// unit the client compiles. Two definitions is a duplicate registration; zero
// is the toggle having stopped existing, which is the whole failure this lane
// exists to prevent.
//-----------------------------------------------------------------------------
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
//-----------------------------------------------------------------------------
// FACT 4 of 5 — the translation unit is one client_sourcesharp actually
// COMPILES. It used to hold the definitions; it holds the visibility SWEEP now,
// and the failure is the same shape either way: a perfect body in a file nobody
// builds is a symbol that does not exist, and the slot it is assigned to would
// not link.
//-----------------------------------------------------------------------------
TEST( ClientFamily9ConVars, TheTranslationUnitThatNowHoldsThemIsCompiledIntoTheClient )
{
	const std::vector<std::string> vecSources = ClientCompiledSources();

	EXPECT_NE( std::find( vecSources.begin(), vecSources.end(), std::string( kEntityStatics ) ), vecSources.end() )
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
