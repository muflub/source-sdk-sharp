//=============================================================================//
// CLIENTEARPOSITION'S CROSSING, SOURCE-READ -- plan_server.md Phase 7,
// docs/plan-audit.md item C5.
//
// WHY THIS FILE IS ALL SOURCE READS AND NOT ONE RUNNING BODY.
//
// The two steps in shell_clientsteps.cpp need `engine`, `edict_t`,
// `UTIL_SDKPlayerFromEdict`, `CBasePlayer::EarPosition` and
// `CStandalonePlayer` -- the entire server. Nothing in that file can ever be
// linked into a gtest, exactly as shell_transmitsteps.cpp cannot, and
// shell_transmitsteps_test.cpp reads that file for the same reason.
//
// The BEHAVIOUR of the crossing is checked elsewhere, and this file is a
// different kind of check rather than a substitute for any of it:
//
//   ServerClientsTests (xUnit, 7 facts) -- the ORDER the two hierarchies are
//   asked in, over a strict engine double, including the one fact that pins
//   the order as the REVERSE of the skybox crossing's.
//
//   ManagedRuntimeServerSources.TheNewSlotsAreTheTailOfTheManagedTable and
//   TransmitCrossing.TheTwoEngineSlotsLeftTheEngineTableForAServerArea -- that the
//   slots were APPENDED and not inserted.
//
// What is left, and what is here, is the ARRANGEMENT: that the disjunction
// actually left C++, that each step reads ONE hierarchy, that the
// SS_CBASEENTITY_ALIAS arm went WITH THE READ, and that the Assert stayed with
// the caller. Every one of those is a property of text that compiles and runs
// identically whether or not it holds -- which is what a source read is for.
//=============================================================================//

#include <fstream>
#include <string>

#include "gtest/gtest.h"

namespace
{

std::string Read( const std::string &strPath )
{
	std::ifstream in( strPath.c_str() );
	EXPECT_TRUE( in.good() ) << "cannot read " << strPath;

	std::string strOut, strLine;
	while ( std::getline( in, strLine ) )
	{
		strOut += strLine;
		strOut += "\n";
	}
	return strOut;
}

//-----------------------------------------------------------------------------
// Comment-stripped, and it is load-bearing rather than tidy: every one of these
// files EXPLAINS the crossing in prose that names the same symbols the
// assertions look for. shell_gameclients.cpp's own header now contains the
// words "CStandalonePlayer::FromEdict" inside a paragraph saying that reach
// LEFT -- so a raw search for it would find the sentence and report the code.
//-----------------------------------------------------------------------------
std::string Stripped( const std::string &strPath )
{
	std::ifstream in( strPath.c_str() );
	EXPECT_TRUE( in.good() ) << "cannot read " << strPath;

	std::string strOut, strLine;
	while ( std::getline( in, strLine ) )
	{
		const size_t at = strLine.find( "//" );
		if ( at != std::string::npos )
			strLine = strLine.substr( 0, at );
		strOut += strLine;
		strOut += "\n";
	}
	return strOut;
}

int CountOccurrences( const std::string &strHay, const std::string &strNeedle )
{
	int n = 0;
	size_t at = 0;
	while ( ( at = strHay.find( strNeedle, at ) ) != std::string::npos )
	{
		n++;
		at += strNeedle.size();
	}
	return n;
}

//-----------------------------------------------------------------------------
// The body of one function, comment-stripped: from its signature to the first
// line that starts a new one. Every assertion below that is about WHERE
// something sits needs this -- shell_gameclients.cpp has twenty other bodies
// and several of them reach for CStandalonePlayer legitimately, so a
// whole-file count would answer about the file and not about the method.
//-----------------------------------------------------------------------------
std::string FunctionBody( const std::string &strFile, const std::string &strSignature )
{
	const size_t at = strFile.find( strSignature );
	EXPECT_NE( std::string::npos, at ) << "cannot find " << strSignature;
	if ( at == std::string::npos )
		return "";

	// To the next line that begins in column 0 with a non-brace, non-blank --
	// which in this file is the next definition or comment block.
	size_t end = strFile.find( "\n}\n", at );
	EXPECT_NE( std::string::npos, end ) << strSignature << " has no closing brace";
	return strFile.substr( at, end == std::string::npos ? std::string::npos : end - at );
}

const char *kSteps   = "sourcesharp/game/server/shell_clientsteps.cpp";
const char *kClients = "sourcesharp/game/server/shell_gameclients.cpp";
const char *kAbi     = "sourcesharp/host/sourcesharp.h";
// The server's slots since plan_engineapi_vtables C6c: shim-area methods.
const char *kAreas     = "sourcesharp/game/shared/ishim_areas.h";
const char *kAreasImpl = "sourcesharp/game/shared/shell_shim_areas.inc";
const char *kEarSig  = "void CServerGameClients::ClientEarPosition(";

} // namespace

//=============================================================================
// THE DISJUNCTION LEFT.
//=============================================================================

TEST( ClientsCrossing, ClientEarPositionNoLongerNamesEitherHierarchy )
{
	// THE WHOLE SHAPE OF C5'S FIRST CROSSING, IN ONE ASSERTION. What used to be
	// in this body was an `#ifdef SOURCESHARP` reaching for
	// CStandalonePlayer::FromEdict and falling through to
	// UTIL_SDKPlayerFromEdict. That pair WAS the decision -- which hierarchy
	// answers, and in which order -- and a step is never one.
	//
	// If it comes back, it comes back as a plausible local fix to a bug in this
	// method, and the C# body would go on being called and go on being right
	// and go on being ignored.
	const std::string body = FunctionBody( Stripped( kClients ), kEarSig );

	EXPECT_EQ( 0, CountOccurrences( body, "CStandalonePlayer" ) )
		<< "ClientEarPosition names CStandalonePlayer again -- the disjunction "
		   "C5 moved into C# has grown a second copy in C++";
	EXPECT_EQ( 0, CountOccurrences( body, "UTIL_SDKPlayerFromEdict" ) )
		<< "ClientEarPosition resolves an SDK player again";
	EXPECT_EQ( 0, CountOccurrences( body, "EarPosition()" ) )
		<< "ClientEarPosition calls a hierarchy's EarPosition directly again";
}

TEST( ClientsCrossing, ClientEarPositionAsksCSharpExactlyOnce )
{
	const std::string body = FunctionBody( Stripped( kClients ), kEarSig );

	// EXACTLY ONE ASK. Two would be two crossings for one answer and two
	// chances for the second to disagree with the first.
	EXPECT_EQ( 1, CountOccurrences( body, "SourceSharp_EarPositionForClient(" ) );
}

TEST( ClientsCrossing, TheAssertStayedWithTheCaller )
{
	// The Assert(0) is what the CALLER does with a "no" -- a diagnostic about
	// the ENGINE having named a client this game cannot describe. Managed code
	// that owned it would be deciding when an engine assert fires, and a
	// crossing that dropped it would delete stock's only signal that the
	// listener is being placed at the world origin.
	const std::string body = FunctionBody( Stripped( kClients ), kEarSig );

	EXPECT_EQ( 1, CountOccurrences( body, "Assert(0)" ) );
	EXPECT_EQ( 1, CountOccurrences( body, "vec3_origin" ) );

	// And the steps do NOT carry it: a step that asserted would fire once per
	// client per frame for every standalone player, because the SDK step misses
	// on every one of them BY DESIGN.
	const std::string steps = Stripped( kSteps );
	EXPECT_EQ( 0, CountOccurrences( steps, "Assert(" ) );
}

//=============================================================================
// EACH STEP READS ONE HIERARCHY.
//=============================================================================

TEST( ClientsCrossing, TheStandaloneStepIsTheOnlyStepAndReadsOneHierarchy )
{
	const std::string steps = Stripped( kSteps );

	// ONE STEP since plan_server_native_v2 T2. The SDK step read no hierarchy
	// (server.so builds no CBasePlayer) and answered 0, so it was deleted with
	// its slot and EngineBinding answers "no SDK ears" itself.
	EXPECT_EQ( 1, CountOccurrences(
		steps, "int32_t SourceSharp_ClientsStandalonePlayerEarPosition(" ) );
	EXPECT_EQ( 0, CountOccurrences( steps, "SourceSharp_ClientsSdkPlayerEarPosition(" ) )
		<< "the SDK ear step is back; there is no SDK player on the server";

	EXPECT_EQ( 1, CountOccurrences( steps, "CStandalonePlayer::FromEdict" ) );
	EXPECT_EQ( 0, CountOccurrences( steps, "UTIL_SDKPlayerFromEdict" ) );
}

TEST( ClientsCrossing, TheAliasArmsReadIsGoneAndSoIsTheSdkStep )
{
	// READ THE ASSERTIONS, NOT THE NAME. This fact was
	// TheAliasArmWentWithTheReadAndNotWithTheMethod, and it pinned the ONE
	// `#ifndef SS_CBASEENTITY_ALIAS` region in shell_clientsteps.cpp: the guard
	// existed because CBasePlayer::EarPosition is defined in the struck
	// baseplayer_shared.cpp, so it belonged beside the CALL, and the crossing
	// moved it out of shell_gameclients.cpp into the step file with the call.
	//
	// THE ALIAS RETIRED. `CBaseEntity` is not a name any more, and with it went
	// every arm the compiler never saw -- here, the `#ifndef` half that read
	// `pPlayer->EarPosition()` into pOutXYZ and returned 1. It was dead text:
	// the alias was armed, so the compiled behaviour was always the `#else`,
	// `return 0`. That `return 0` is now the whole body and is asserted below.
	//
	// The guard's ABSENCE alone would pass over an empty file, so what the guard
	// was defending is asserted with it: the read is still in the step file's
	// step and not in the method, and the step still asks the SDK hierarchy.
	const std::string steps = Stripped( kSteps );

	// GONE: the guard, and the unarmed read it protected.
	EXPECT_EQ( 0, CountOccurrences( steps, "SS_CBASEENTITY_ALIAS" ) )
		<< "an SS_CBASEENTITY_ALIAS region is back in the step file -- the alias "
		   "it names does not exist any more, so this arm cannot be armed";
	EXPECT_EQ( 0, CountOccurrences( steps, "pPlayer->EarPosition()" ) );

	// The SDK step itself is gone (plan_server_native_v2 T2): it answered 0
	// with no read in it, so the guard has nothing left to stand beside.
	EXPECT_EQ( 0, CountOccurrences( steps, "SourceSharp_ClientsSdkPlayerEarPosition(" ) );
	EXPECT_EQ( 1, CountOccurrences( steps, "int32_t SourceSharp_ClientsStandalonePlayerEarPosition(" ) )
		<< "the step file did not read as itself";

	// AND THE METHOD STILL DOES NOT DO THE READ ITSELF. That was the other half
	// of "with the read, not with the method": no guard region, and no SDK read
	// either -- ClientEarPosition asks through the step pair and keeps only the
	// miss path.
	const std::string body = FunctionBody( Stripped( kClients ), kEarSig );
	EXPECT_EQ( 0, CountOccurrences( body, "SS_CBASEENTITY_ALIAS" ) );
	EXPECT_EQ( 0, CountOccurrences( body, "EarPosition()" ) )
		<< "ClientEarPosition reads a hierarchy directly again -- the read "
		   "belongs beside the step, which is where the guard used to live";
	EXPECT_EQ( 1, CountOccurrences( body, "SourceSharp_EarPositionForClient(" ) );
}

TEST( ClientsCrossing, TheStepDoesNotZeroItsOutParameterOnAMiss )
{
	// The world origin is a real position. A step that wrote three zeroes on
	// the way out would make "the listener is at the origin" and "no such
	// client" the same three floats -- and the caller ASSERTS on one of them.
	//
	// Read as: a step that writes pOutXYZ writes it exactly three times, all of
	// them after its hierarchy answered. Three writes and no more means no
	// clearing pass.
	//
	// READ THE ASSERTIONS, NOT THE COUNT. This was SIX -- three per step -- when
	// both steps could write. The SDK step's three were in the unarmed
	// `#ifndef SS_CBASEENTITY_ALIAS` arm the compiler never saw, and that arm is
	// deleted; the SDK step now answers 0 and writes NOTHING, which is still the
	// property this fact is about, so its zero is asserted rather than dropped.
	const std::string steps = Stripped( kSteps );

	// The SDK step that wrote nothing is gone (plan_server_native_v2 T2), so
	// the file's three writes are all the standalone step's.
	const size_t standalone = steps.find( "int32_t SourceSharp_ClientsStandalonePlayerEarPosition(" );
	ASSERT_NE( std::string::npos, standalone );

	EXPECT_EQ( 3, CountOccurrences( steps, "pOutXYZ[ " ) )
		<< "the step that answers writes pOutXYZ a number of times other than "
		   "3 -- a zeroing pass on the miss path is the shape this catches";
	EXPECT_EQ( 3, CountOccurrences( steps.substr( standalone ), "pOutXYZ[ " ) );
}

//=============================================================================
// THE ABI, APPENDED.
//=============================================================================

TEST( ClientsCrossing, TheTwoEngineSlotsLeftTheEngineTableForAServerArea )
{
	// They were APPENDED behind C4's skybox pair, and this fact held that order.
	// plan_engineapi_vtables C6c moved every server-only slot out of the struct:
	// these are IShimPlayers methods now (the skybox pair is IShimEntitiesServer's),
	// and tools/vtable-layout checks each area's order against its managed mirror.
	// plan_client_vtables CC-last then deleted the struct itself, so neither can
	// come back as a field of it: the header declares no engine struct at all,
	// and no function pointer of either name anywhere.
	const std::string abi = Read( kAbi );
	const std::string areas = Read( kAreas );

	ASSERT_NE( std::string::npos, abi.find( "(*SourceSharpLogFn)" ) )
		<< "the control: the header read declares the ABI's one surviving function "
		   "pointer, found by the same \"(*Name)\" needle as the absences below";
	EXPECT_EQ( std::string::npos, abi.find( "} SourceSharpManagedApi;" ) );
	EXPECT_EQ( std::string::npos, abi.find( "} SourceSharpEngineApi;" ) );
	EXPECT_EQ( std::string::npos, abi.find( "(*ClientsStandalonePlayerEarPosition)" ) );
	EXPECT_EQ( std::string::npos, abi.find( "(*ClientsSdkPlayerEarPosition)" ) );

	// The SDK ask left the area too (plan_server_native_v2 T2).
	EXPECT_NE( std::string::npos, areas.find( " ClientsStandalonePlayerEarPosition(" ) );
	EXPECT_EQ( std::string::npos, areas.find( " ClientsSdkPlayerEarPosition(" ) );
}

TEST( ClientsCrossing, TheTwoAsksAreAreaMethodsNotStructSlots )
{
	// They were appended to SourceSharpManagedApi in this order. Since
	// plan_engineapi_vtables E10 they are methods of the managed-built
	// IManagedClients, E-last removed the struct's fields and
	// plan_client_vtables CE-last the struct itself.
	const std::string abi = Read( kAbi );
	EXPECT_EQ( std::string::npos, abi.find( "} SourceSharpManagedApi;" ) );
	EXPECT_EQ( std::string::npos, abi.find( "(*ServerSkyboxAreaForClient)" ) );
	EXPECT_EQ( std::string::npos, abi.find( "(*ServerEarPositionForClient)" ) );
	EXPECT_NE( std::string::npos, abi.find( "(*SourceSharpLogFn)" ) )
		<< "the control: the header's surviving function pointer is found by the same needle";

	const std::string areas = Read( "sourcesharp/game/shared/imanaged_areas.h" );
	const size_t sky = areas.find( " ServerSkyboxAreaForClient(" );
	const size_t ears = areas.find( " ServerEarPositionForClient(" );
	ASSERT_NE( std::string::npos, sky );
	ASSERT_NE( std::string::npos, ears );
	EXPECT_LT( sky, ears ) << "the area keeps the order the slots were appended in";
}

TEST( ClientsCrossing, TheStepIsWiredIntoTheEngineTable )
{
	// A step nobody wires is a null slot, and a null engine slot is a call
	// through a null pointer at the first sound the server emits.
	// Since C6c the server's area method is the wiring: it must forward here.
	const std::string impl = Read( kAreasImpl );

	EXPECT_TRUE( impl.find(
		"{ return ::SourceSharp_ClientsStandalonePlayerEarPosition( iEdict, pOutXYZ ); }" )
		!= std::string::npos );
	EXPECT_TRUE( impl.find( "SourceSharp_ClientsSdkPlayerEarPosition" ) == std::string::npos )
		<< "the SDK ear step is wired again; it was deleted with its slot";
}

TEST( ClientsCrossing, TheEarAskIsAnAreaTheLoadRequires )
{
	// A null there puts every listener at the world origin and asserts about
	// it, which in a release build is silent. Since plan_engineapi_vtables E10
	// the ask is a method of the managed-built IManagedClients, which the load
	// refuses to go on without (acquired at its compiler's count), and the
	// retired struct slot is not required any more.
	const std::string acquire = Read( "sourcesharp/game/shared/shell_managed_areas.inc" );
	const std::string required = Read( "sourcesharp/game/server/managed_runtime_server.cpp" );

	EXPECT_EQ( 1, CountOccurrences( acquire, "ManagedAccept< IManagedClients >" ) );
	EXPECT_EQ( 0, CountOccurrences( required, "f( ServerEarPositionForClient )" ) );
}
