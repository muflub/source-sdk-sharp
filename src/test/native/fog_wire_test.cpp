//========= SourceSharp — the world fog reaches the client ====================//
//
// plan_entity.md step 8e-render.
//
// THE DEFECT. World fog did not render on this client and had not since the
// flip. Step 7c measured it, refused to paper over it with a zeroed struct, and
// made it a printed not-shipped row; step 7a confirmed it. The cause is the
// WIRE and not any one file: in stock, fog reaches a client through
// DT_FogController -- CFogController's own send table -- plus a handle in
// CPlayerLocalData. fogcontroller.cpp is entirely `#ifndef SOURCESHARP` here
// and env_fog_controller is EnvFogController.cs, so server.so declares no
// DT_FogController and there was NO SENDER for either half. The numbers existed
// on the server and CRendering3dView::EnableWorldFog read fog off every frame.
//
// (The 3D SKYBOX was filed beside it and was NOT the same fault, which is why
// this file used to assert that its ledger row was untouched -- so that "the
// fog is fixed" could not quietly be read as "both are". It took two lanes and
// two separate faults: phase4-exit fixed the reachability half
// (UpdateAllClientData never ran, because UTIL_PlayerByIndex cannot answer on
// this build) and defect-skybox fixed the wire half (CStandalonePlayer networks
// PlayerNetState, not CPlayerLocalData). The row is retired, item 6 below is
// the INVERSE of what it was, and skybox_wire_test.cpp is that wire's own file
// -- built the same way, out of the same six links.)
//
// WHY A FILE OF ASSERTIONS RATHER THAN A LOOK AT THE SCREEN. Fog that never
// arrives renders as no fog. A map with no fog controller renders as no fog. An
// effect that draws nothing does not crash. The two states are one screenshot
// and one exit code, which is exactly how this went unnoticed for the life of
// the port. So every link is asserted apart, and each is a place the state
// silently stops reaching the renderer:
//
//   1. the native side KEEPS what is pushed and hands back a pointer to it;
//   2. NULL clears, and a cleared viewer answers NULL rather than a zeroed
//      struct -- the distinction 7c refused to lose;
//   3. the ABI slot is wired, on the CLIENT and only there;
//   4. the managed client's pass is CALLED from the frame stage;
//   5. the server's service is REGISTERED with the frame driver;
//   6. the skybox's ledger row is RETIRED -- see the note above, and
//      skybox_wire_test.cpp.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <cctype>
#include <string>
#include <vector>
#include <fstream>
#include <sstream>

namespace
{

std::string Read( const char *pRelative )
{
	std::ifstream f( pRelative );
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

// Comment-free. Every file here carries long arguments in comments, and this
// project has found THREE checks satisfied by a needle that had come to rest
// inside one.
std::string ReadCode( const char *pRelative )
{
	const std::string src = Read( pRelative );
	std::string out;
	out.reserve( src.size() );

	bool bLine = false, bBlock = false;

	for ( size_t i = 0; i < src.size(); i++ )
	{
		if ( bLine )
		{
			if ( src[i] == '\n' ) { bLine = false; out += '\n'; }
			continue;
		}
		if ( bBlock )
		{
			if ( src[i] == '*' && i + 1 < src.size() && src[i+1] == '/' ) { bBlock = false; i++; }
			continue;
		}
		if ( src[i] == '/' && i + 1 < src.size() && src[i+1] == '/' ) { bLine = true; i++; continue; }
		if ( src[i] == '/' && i + 1 < src.size() && src[i+1] == '*' ) { bBlock = true; i++; continue; }

		out += src[i];
	}

	return out;
}

std::string BodyOf( const std::string &code, const char *pSignature )
{
	const size_t at = code.find( pSignature );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = code.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	const size_t close = code.find( "\n}", open );
	if ( close == std::string::npos )
		return std::string();

	return code.substr( open + 1, close - open - 1 );
}

const char *const kViewFacts  = "sourcesharp/game/client/shell_view_facts.cpp";
const char *const kGameSystem = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";
const char *const kClientAreas = "sourcesharp/game/shared/shell_shim_areas_client.inc";
const char *const kServerAreas = "sourcesharp/game/shared/shell_shim_areas.inc";
const char *const kClientAreasH = "sourcesharp/game/shared/ishim_areas_client.h";
const char *const kHost       = "sourcesharp/host/sourcesharp.h";
const char *const kNetState   = "sourcesharp/managed/SourceSharp.Shared/PlayerNetState.cs";
const char *const kClientMod  = "sourcesharp/managed/SourceSharp.Client/ClientModule.cs";
const char *const kServerMod  = "sourcesharp/managed/SourceSharp.Server/ServerModule.cs";
const char *const kWorldFog   = "sourcesharp/managed/SourceSharp.Server/Entities/Env/WorldFog.cs";

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

//-----------------------------------------------------------------------------
// WHICH DLLs COMPILE THE LINE AT `at`: kOnClient | kOnServer, from the
// CLIENT_DLL conditionals above it, nesting and `#else` included. A condition
// on anything else is taken as compiled on both, the conservative direction.
// `#ifdef CLIENT_DLL` nested inside `#ifndef CLIENT_DLL` is compiled on
// NEITHER side, and answers 0.
//-----------------------------------------------------------------------------
const int kOnClient = 1;
const int kOnServer = 2;

int SidesAt( const std::string &src, size_t at )
{
	std::vector< int > stack;
	size_t line = 0;
	while ( line < at && line < src.size() )
	{
		size_t end = src.find( '\n', line );
		if ( end == std::string::npos )
			end = src.size();
		size_t s = line;
		while ( s < end && ( src[ s ] == ' ' || src[ s ] == '\t' ) )
			s++;
		const std::string l = src.substr( s, end - s );

		if ( l.compare( 0, 17, "#ifdef CLIENT_DLL" ) == 0 )       stack.push_back( kOnClient );
		else if ( l.compare( 0, 18, "#ifndef CLIENT_DLL" ) == 0 ) stack.push_back( kOnServer );
		else if ( l.compare( 0, 3, "#if" ) == 0 )                 stack.push_back( kOnClient | kOnServer );
		else if ( l.compare( 0, 5, "#elif" ) == 0 && !stack.empty() )
			stack.back() = kOnClient | kOnServer;
		else if ( l.compare( 0, 5, "#else" ) == 0 && !stack.empty() )
			stack.back() = stack.back() == kOnClient ? kOnServer
			             : stack.back() == kOnServer ? kOnClient : kOnClient | kOnServer;
		else if ( l.compare( 0, 6, "#endif" ) == 0 && !stack.empty() )
			stack.pop_back();

		line = end + 1;
	}

	int sides = kOnClient | kOnServer;
	for ( size_t i = 0; i < stack.size(); i++ )
		sides &= stack[ i ];
	return sides;
}

//=============================================================================//
// 1 and 2: THE NATIVE SIDE KEEPS IT, AND NULL IS NOT "FOG OFF".
//=============================================================================//

TEST( FogWire, TheViewerFogIsAnsweredFromWhatWasPushed )
{
	const std::string code = ReadCode( kViewFacts );
	const std::string body = BodyOf( code, "fogparams_t *SourceSharp_ViewerFogParams()" );

	ASSERT_FALSE( body.empty() ) << "SourceSharp_ViewerFogParams was not found";

	EXPECT_NE( body.find( "return &g_ViewerFog;" ), std::string::npos )
		<< "SourceSharp_ViewerFogParams does not hand back the pushed copy. Before "
		   "step 8e-render its whole body was a ledger line and `return NULL;`, "
		   "and every fog read in viewrender.cpp answered 'no fog'.";

	EXPECT_NE( code.find( "void SourceSharp_SetViewerFogParams" ), std::string::npos )
		<< "nothing on this side accepts a pushed fog, so there is nothing for "
		   "the function above to hand back.";
}

TEST( FogWire, ANullPushClearsRatherThanZeroing )
{
	const std::string body = BodyOf( ReadCode( kViewFacts ),
	                                 "void SourceSharp_SetViewerFogParams" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "g_bHaveViewerFog = false;" ), std::string::npos )
		<< "a NULL push does not clear the viewer's fog, so a level change or a "
		   "map with no fog controller leaves the PREVIOUS fog being rendered.";
}

TEST( FogWire, NothingPushedIsStillNullAndNotAZeroedStruct )
{
	// THE DISTINCTION STEP 7c REFUSED TO LOSE, and the one a well-meaning
	// simplification removes first. A zeroed fogparams_t reads as "fog enabled
	// = false, start 0, end 0" -- a REAL answer meaning "no fog" -- so
	// returning one would make "this map has no fog" and "nobody asked" the
	// same screenshot again, which is the state the whole step exists to leave.
	const std::string body = BodyOf( ReadCode( kViewFacts ),
	                                 "fogparams_t *SourceSharp_ViewerFogParams()" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "if ( !g_bHaveViewerFog )" ), std::string::npos );
	EXPECT_NE( body.find( "return NULL;" ), std::string::npos )
		<< "a viewer with nothing pushed is answered with a struct rather than "
		   "NULL. Every GetFog* helper in viewrender.cpp handles the NULL; none "
		   "of them can tell a zeroed struct from a real 'no fog'.";
}

//-----------------------------------------------------------------------------
// THE FOUR FOG COUNTERS ARE RETIRED, PHASE 1c.
//
// WHAT TheCountersExistBecauseTheFeatureIsInvisibleWhenItFails ASSERTED: that
// g_nFogPushed and g_nFogNull were incremented, that all four came back out
// through SourceSharp_ViewerFogCounts, and that cs_cl_fog_report existed. Its
// own comment recorded how the last of those was found -- by mutation,
// deleting one of the four assignments left every other assertion green.
//
// WHY IT IS RETIRED AND NOT RE-POINTED. Its subject is gone in full. The four
// counters were reachable only through SourceSharp_ViewerFogCounts, whose only
// caller was cs_cl_fog_report; with the console command deleted, nothing in
// either DLL, in managed code or in tools/ could read any of them. There is no
// weaker or differently-spelled version of "the counters are reported" left to
// assert -- the thing that would have read them does not exist.
//
// AND THE BEHAVIOUR IT WAS GUARDING IS ALREADY PINNED, one test above this
// comment, by NothingPushedIsStillNullAndNotAZeroedStruct: the distinction the
// counters made visible at runtime -- "this map has no fog" versus "nobody
// pushed any" -- is made STRUCTURALLY by SourceSharp_ViewerFogParams answering
// NULL rather than a zeroed fogparams_t, and that is the property that makes
// the two distinguishable at all. A counter could only report it; the NULL is
// what makes it true.
//-----------------------------------------------------------------------------

//=============================================================================//
// 3: THE SLOT, ON THE CLIENT AND ONLY THERE.
//=============================================================================//

TEST( FogWire, TheSlotIsDeclaredOnTheAbi )
{
	const std::string code = ReadCode( kHost );

	// A method of the client-only IShimViewClient since plan_client_vtables
	// CC-last deleted the engine API struct it was a field of.
	const std::string areas = ReadCode( kClientAreasH );
	ASSERT_FALSE( areas.empty() ) << kClientAreasH << " did not open";
	EXPECT_NE( areas.find( "virtual void SetViewerFogParams( const SourceSharpFogParams *pParams ) = 0;" ),
	           std::string::npos );

	// NAMED rather than an anonymous typedef, because the client's declaration
	// block forward-declares it and a forward declaration of an anonymous
	// typedef is a conflicting declaration. That cost one build.
	EXPECT_NE( code.find( "typedef struct SourceSharpFogParams" ), std::string::npos );
}

TEST( FogWire, TheSlotIsWiredInTheClientRegionAndNotTheServers )
{
	// Since plan_client_vtables CC-last a client slot is wired by its
	// shim-area method's generated forward in shell_shim_areas_client.inc, and
	// the REGION it is wired in is where sourcesharp_gamesystem.cpp includes
	// that file. The server's forwards are shell_shim_areas.inc.
	const std::string code   = ReadCode( kGameSystem );
	const std::string client = ReadCode( kClientAreas );
	const std::string server = ReadCode( kServerAreas );
	ASSERT_FALSE( code.empty() );
	ASSERT_FALSE( client.empty() );
	ASSERT_FALSE( server.empty() );

	ASSERT_EQ( 1, ForwardCount( client, "SetViewerFogParams", "SourceSharp_SetViewerFogParams" ) )
		<< "the slot is declared and never pointed at anything (or wired twice), "
		   "so every push from managed code is dropped by EngineApi's null guard "
		   "in silence.";

	// THE ENCLOSING GUARD OF THE INCLUDE, evaluated by walking the
	// conditionals above it rather than by comparing against the position of
	// some other line. A position comparison is what the first version of
	// fog_wire_test did, and it was wrong in a way that looked right: the
	// client's block sits AFTER the server's in the file, so "before the
	// server's marker" was false for a correctly placed line. The client's
	// include is the `#else` of `#ifndef CLIENT_DLL`, which a bare
	// nearest-`#ifdef` search cannot see at all.
	const size_t inc = code.find( "\n#include \"shell_shim_areas_client.inc\"" );
	ASSERT_NE( inc, std::string::npos )
		<< "sourcesharp_gamesystem.cpp does not include the client's forwards";

	EXPECT_EQ( kOnClient, SidesAt( code, inc + 1 ) )
		<< "the client's forwards are not compiled on the client alone. Inside "
		   "a `#ifndef CLIENT_DLL` block they are compiled away on the CLIENT -- "
		   "which is the only side that renders. The slot stays null there and "
		   "the fog still does not draw. This file's own comment records the same trap for the string tables.";

	// AND NOT ALSO ON THE SERVER. One wiring, in one region; the control
	// proves the server's file was read and the needle can match in it.
	EXPECT_EQ( 0, ForwardCount( server, "SetViewerFogParams", "" ) )
		<< "the slot is wired on the server too.";
	EXPECT_EQ( 1, ForwardCount( server, "GetCurrentTime", "SourceSharp_GetCurrentTime" ) )
		<< "the server's forwards did not read as forwards; the absence above "
		   "is not evidence";
}

//=============================================================================//
// 4 and 5: THE TWO CALLS WITH NO OTHER TRIGGER.
//
// Both of these are the shape this tree has already found TWICE -- a stage with
// a driver, a dispatcher and its own tests, and no production caller, which
// passes every check that exists and does nothing.
//=============================================================================//

TEST( FogWire, TheClientsFogPassIsCalledFromTheNetUpdateStage )
{
	const std::string code = ReadCode( kClientMod );
	const std::string body = BodyOf( code, "public void OnStage(ClientFrameStage stage)" );

	ASSERT_FALSE( body.empty() ) << "ClientModule.OnStage was not found";

	EXPECT_NE( body.find( "_worldFog?.Pass()" ), std::string::npos )
		<< "NOTHING CALLS THE FOG PASS. The class exists, its tests pass, and no "
		   "fog reaches the renderer -- and the game runs perfectly, which is "
		   "exactly what the tree looked like while the defect was open.";

	// ON THE STAGE THE DECODE FINISHES ON, beside the render policy, so the fog
	// drawn this frame is the fog that arrived this frame.
	const size_t stage = body.find( "ClientFrameStage.NetUpdatePostDataUpdateEnd" );
	const size_t pass  = body.find( "_worldFog?.Pass()" );
	const size_t render = body.find( "ClientFrameStage.RenderStart" );

	ASSERT_NE( stage, std::string::npos );
	ASSERT_NE( render, std::string::npos );
	EXPECT_GT( pass, stage );
	EXPECT_LT( pass, render )
		<< "the fog pass has moved to a later stage than the decode's, so what "
		   "is drawn is one frame stale.";
}

TEST( FogWire, TheClientsFogIsForgottenAtLevelInit )
{
	const std::string body = BodyOf( ReadCode( kClientMod ),
	                                 "public void LevelInit(string mapName)" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "_worldFog?.LevelInit()" ), std::string::npos )
		<< "the push is compared against what was last pushed, so a new map "
		   "whose fog happens to equal the old map's would never be pushed and "
		   "the renderer would hold the previous level's numbers.";
}

TEST( FogWire, TheServersFogServiceIsRegistered )
{
	const std::string code = ReadCode( kServerMod );

	EXPECT_NE( code.find( "_services.Create<WorldFog>()" ), std::string::npos )
		<< "WorldFog is never constructed, so the frame driver resolves no "
		   "participant for it and no player is ever given a fog.";

	EXPECT_NE( code.find( "_services.AddInstance(_worldFog)" ), std::string::npos )
		<< "WorldFog is constructed and not registered. FrameDriver resolves its "
		   "participants ONCE, out of the container -- an unregistered service "
		   "with a perfectly good OnPreClientUpdate is never called.";
}

TEST( FogWire, TheServersFogRunsAtStocksOwnPoint )
{
	const std::string code = ReadCode( kWorldFog );

	EXPECT_NE( code.find( "void IFrameLifecycle.OnPreClientUpdate" ), std::string::npos )
		<< "the fog is not published at PreClientUpdate. That is where stock's "
		   "UpdateAllClientData runs -- 'fills in CClientData values for local "
		   "player just before sending over wire' -- and copying per-viewer view "
		   "state into the player is exactly its job.";

	EXPECT_NE( code.find( "IGameLifecycle" ), std::string::npos )
		<< "nothing forgets the fog between levels.";
}

//=============================================================================//
// The wire itself.
//=============================================================================//

TEST( FogWire, ThePlayerNetworksTheFogAndDoesNotPredictIt )
{
	const std::string code = Read( kNetState );
	ASSERT_FALSE( code.empty() ) << "PlayerNetState.cs could not be read";

	static const char *kFields[] = {
		"FogValid", "FogEnable", "FogBlend", "FogRadial", "FogDirPrimary",
		"FogColorPrimary", "FogColorSecondary", "FogStart", "FogEnd",
		"FogFarZ", "FogMaxDensity" };

	for ( size_t i = 0; i < sizeof( kFields ) / sizeof( kFields[0] ); i++ )
	{
		EXPECT_NE( code.find( std::string( "partial " ) ), std::string::npos );
		EXPECT_NE( code.find( kFields[ i ] ), std::string::npos )
			<< "PlayerNetState no longer declares " << kFields[ i ]
			<< "; the fog cannot reach a client that is not sent it.";
	}

	// NOT PREDICTED. Asserted on the comment-stripped text so the paragraph
	// explaining why cannot satisfy it.
	//
	// FROM THE END OF THE LAST NON-FOG FIELD, not from FogValid's own position.
	// Searching from FogValid meant an attribute written ABOVE it was after the
	// start of the search and before the field -- i.e. outside the window -- and
	// the mutation that added `[Predicted]` to FogValid left this green. The
	// fog block is the tail of the class, so everything after WaterJumpVel's
	// declaration is fog and nothing else.
	const std::string stripped = ReadCode( kNetState );
	const size_t lastPredicted = stripped.find( "public partial Vector WaterJumpVel" );
	ASSERT_NE( lastPredicted, std::string::npos )
		<< "WaterJumpVel is no longer the last field before the fog block, so "
		   "the window this test searches no longer means what it says";

	const size_t afterIt = stripped.find( '\n', lastPredicted );
	ASSERT_NE( afterIt, std::string::npos );

	EXPECT_EQ( stripped.find( "[Predicted]", afterIt ), std::string::npos )
		<< "a fog field is marked [Predicted]. The movement loop neither reads "
		   "nor writes one, so it would sit on the misprediction ring for a "
		   "value that can only ever agree.";
}

//=============================================================================//
// AND THE SKYBOX'S ROW IS RETIRED, which is the INVERSE of what this file used
// to assert here.
//
// The assertion is kept rather than deleted, inverted rather than dropped, for
// the reason the fog's own row was: the ledger's whole purpose is that a
// screenshot cannot tell "this map has no sky camera" from "nobody asked", and
// a row that simply DISAPPEARS from the tests leaves a closed gap and a broken
// wire looking identical again. So the fact still runs, and it now fails if the
// row comes back OR if the body stops handing over a real answer.
//=============================================================================//

TEST( FogWire, TheSkyboxsNotShippedRowIsRetired )
{
	const std::string body = BodyOf( ReadCode( kViewFacts ),
	                                 "sky3dparams_t *SourceSharp_ViewerSkyboxParams()" );
	ASSERT_FALSE( body.empty() );

	EXPECT_EQ( body.find( "SourceSharp_NotShipped" ), std::string::npos )
		<< "the 3D skybox is back on the not-shipped ledger. Two lanes closed "
		   "two separate faults to retire that row -- phase4-exit the "
		   "reachability half, defect-skybox the wire -- and a re-added row "
		   "means one of them has come undone. Body begins: '"
		<< body.substr( 0, 80 ) << "'";

	// AND IT HANDS BACK A REAL ANSWER. Deleting the row alone would satisfy the
	// assertion above while leaving `return NULL;` as the whole body, which is
	// the SAME defect with the printing removed -- strictly worse than the row.
	EXPECT_NE( body.find( "return &g_ViewerSkybox;" ), std::string::npos )
		<< "SourceSharp_ViewerSkyboxParams does not hand back the pushed copy.";
}

} // namespace
