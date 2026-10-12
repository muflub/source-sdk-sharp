//========= SourceSharp — the 3D skybox reaches the renderer ==================//
//
// The defect-skybox lane. `fog_wire_test.cpp`'s twin, one wire over, and
// deliberately the same six links in the same order — the two features fail the
// same way and a reader who has understood one has understood both.
//
// THE DEFECT, AND IT WAS TWO FAULTS RATHER THAN ONE. The 3D skybox did not
// render and had not since the flip; it was the LAST entry in
// ContractBaseline.OpenDefects. Step 7c measured it, refused to paper over it
// with a zeroed struct and made it a printed not-shipped row.
//
//   * REACHABILITY (lane phase4-exit). UpdateAllClientData walked
//     UTIL_PlayerByIndex, whose helper UTIL_SDKPlayerFromEdict returns NULL
//     unconditionally under SOURCESHARP, so the loop ran maxClients times and
//     its body never ran once: ClientData_Update never ran and
//     SourceSharp_FillSkyboxParams -- its only caller -- was never reached.
//   * THE WIRE (this lane). That fix landed and the SERVER has been filling
//     MvLocal().m_skybox3d every frame ever since -- into a struct nothing
//     sends. CStandalonePlayer does not network CPlayerLocalData; it networks
//     PlayerNetState, and PlayerNetState carried no skybox.
//
// Measured live on ss_sandbox before the wire existed: 12,270 frames set a sky
// view up, the engine said a 3D skybox was VISIBLE from the eye on all 12,270
// of them, and SourceSharp_ViewerSkyboxParams answered NULL every single time.
//
// WHY A FILE OF ASSERTIONS RATHER THAN A LOOK AT THE SCREEN. Three different
// states render as one screenshot: a map with no sky_camera, a map with no sky
// FACE over the player, and a wire that has stopped working. That is how this
// went unnoticed for the life of the port. So every link is asserted apart, and
// each is a place the state silently stops reaching the renderer:
//
//   1. the native side KEEPS what is pushed and hands back a pointer to it;
//   2. NULL clears, and a cleared viewer answers NULL rather than a zeroed
//      struct -- area 0 is a REAL visleaf area, which is 7c's objection;
//   3. the ABI slot is wired, on the CLIENT and only there;
//   4. the managed client's pass is CALLED from the frame stage;
//   5. the server's service is REGISTERED with the frame driver;
//   6. the renderer's own draw is counted, because "it is on the screen" is not
//      a reading a headless gate can take.
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

const char *const kViewFacts   = "sourcesharp/game/client/shell_view_facts.cpp";
const char *const kGameSystem  = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";
const char *const kClientAreas = "sourcesharp/game/shared/shell_shim_areas_client.inc";
const char *const kServerAreas = "sourcesharp/game/shared/shell_shim_areas.inc";
const char *const kClientAreasH = "sourcesharp/game/shared/ishim_areas_client.h";
const char *const kHost        = "sourcesharp/host/sourcesharp.h";
const char *const kAreas       = "sourcesharp/game/shared/ishim_areas.h";
const char *const kNetState    = "sourcesharp/managed/SourceSharp.Shared/PlayerNetState.cs";
const char *const kSkyState    = "sourcesharp/managed/SourceSharp.Shared/SkyState.cs";
const char *const kClientMod   = "sourcesharp/managed/SourceSharp.Client/ClientModule.cs";
const char *const kServerMod   = "sourcesharp/managed/SourceSharp.Server/ServerModule.cs";
const char *const kWorldSkybox = "sourcesharp/managed/SourceSharp.Server/Entities/Sky/WorldSkybox.cs";

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
// 1 and 2: THE NATIVE SIDE KEEPS IT, AND NULL IS NOT AREA 255.
//=============================================================================//

TEST( SkyboxWire, TheViewerSkyboxIsAnsweredFromWhatWasPushed )
{
	const std::string code = ReadCode( kViewFacts );
	const std::string body = BodyOf( code, "sky3dparams_t *SourceSharp_ViewerSkyboxParams()" );

	ASSERT_FALSE( body.empty() ) << "SourceSharp_ViewerSkyboxParams was not found";

	EXPECT_NE( body.find( "return &g_ViewerSkybox;" ), std::string::npos )
		<< "SourceSharp_ViewerSkyboxParams does not hand back the pushed copy. "
		   "Before this lane its whole body was a ledger line and `return NULL;`, "
		   "and CSkyboxView::PreRender3dSkyboxWorld returned at its `if ( !pbp )` "
		   "line on every frame.";

	EXPECT_NE( code.find( "void SourceSharp_SetViewerSkyboxParams" ), std::string::npos )
		<< "nothing on this side accepts a pushed skybox, so there is nothing "
		   "for the function above to hand back.";
}

TEST( SkyboxWire, ANullPushClearsRatherThanZeroing )
{
	const std::string body = BodyOf( ReadCode( kViewFacts ),
	                                 "void SourceSharp_SetViewerSkyboxParams" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "g_bHaveViewerSkybox = false;" ), std::string::npos )
		<< "a NULL push does not clear the viewer's skybox, so a level change "
		   "leaves the PREVIOUS map's miniature world being drawn -- at the "
		   "previous map's visleaf area, which is how a corner of another level "
		   "ends up in the sky.";
}

TEST( SkyboxWire, NothingPushedIsStillNullAndNotAZeroedStruct )
{
	// THE DISTINCTION STEP 7c REFUSED TO LOSE, and it is sharper here than it
	// was for the fog. A zeroed sky3dparams_t has area 0, and 0 is a REAL
	// visleaf area -- the engine's "there is no sky camera" is 255 -- so
	// returning one would send CSkyboxView at a camera that is not there rather
	// than at nothing.
	const std::string body = BodyOf( ReadCode( kViewFacts ),
	                                 "sky3dparams_t *SourceSharp_ViewerSkyboxParams()" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "if ( !g_bHaveViewerSkybox )" ), std::string::npos );
	EXPECT_NE( body.find( "return NULL;" ), std::string::npos )
		<< "a viewer with nothing pushed is answered with a struct rather than "
		   "NULL. PreRender3dSkyboxWorld handles the NULL; it cannot tell a "
		   "zeroed struct from a sky camera standing in area 0.";
}

TEST( SkyboxWire, TheLerpTargetsAreTheCurrentValuesAndNotZero )
{
	// The same four lines SourceSharp_SetViewerFogParams carries and the same
	// reason: viewrender.cpp's GetFogColor blends colorPrimary towards
	// colorPrimaryLerpTo whenever lerptime is in the future, so a black target
	// beside a live colour is one changed comparison away from a skybox fog
	// that fades to black on its own.
	const std::string body = BodyOf( ReadCode( kViewFacts ),
	                                 "void SourceSharp_SetViewerSkyboxParams" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "g_ViewerSkybox.fog.colorPrimaryLerpTo   = g_ViewerSkybox.fog.colorPrimary;" ),
	           std::string::npos );
	EXPECT_NE( body.find( "g_ViewerSkybox.fog.lerptime             = 0.0f;" ),
	           std::string::npos )
		<< "lerptime is not zeroed, so the client believes a transition is in "
		   "flight towards targets nobody set.";
}

//=============================================================================//
// 3: THE SLOT, ON THE CLIENT AND ONLY THERE.
//=============================================================================//

TEST( SkyboxWire, TheSlotIsDeclaredOnTheAbi )
{
	const std::string code = ReadCode( kHost );

	// A method of the client-only IShimViewClient since plan_client_vtables
	// CC-last deleted the engine API struct it was a field of.
	const std::string areas = ReadCode( kClientAreasH );
	ASSERT_FALSE( areas.empty() ) << kClientAreasH << " did not open";
	EXPECT_NE( areas.find( "virtual void SetViewerSkyboxParams( const SourceSharpSkyboxParams *pParams ) = 0;" ),
	           std::string::npos );

	// NAMED rather than an anonymous typedef, because the client's declaration
	// block forward-declares it and a forward declaration of an anonymous
	// typedef is a conflicting declaration. It cost a build in this lane, as
	// SourceSharpFogParams' own comment said it would.
	EXPECT_NE( code.find( "typedef struct SourceSharpSkyboxParams" ), std::string::npos );

	// AND IT IS THE SAME STRUCT THE SERVER'S SLOT TAKES. One map's sky camera
	// making one round trip; a second declaration of one layout is drift
	// waiting to happen. The server's slot is a shim-area method since
	// plan_engineapi_vtables C6c.
	EXPECT_NE( ReadCode( kAreas ).find( "virtual void SetSkyboxParams( const SourceSharpSkyboxParams * pParams ) = 0;" ),
	           std::string::npos );
}

TEST( SkyboxWire, TheSlotIsWiredInTheClientRegionAndNotTheServers )
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

	ASSERT_EQ( 1, ForwardCount( client, "SetViewerSkyboxParams", "SourceSharp_SetViewerSkyboxParams" ) )
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
		   "the skybox still does not draw.";

	// AND NOT ALSO ON THE SERVER. One wiring, in one region; the control
	// proves the server's file was read and the needle can match in it.
	EXPECT_EQ( 0, ForwardCount( server, "SetViewerSkyboxParams", "" ) )
		<< "the slot is wired on the server too.";
	EXPECT_EQ( 1, ForwardCount( server, "GetCurrentTime", "SourceSharp_GetCurrentTime" ) )
		<< "the server's forwards did not read as forwards; the absence above "
		   "is not evidence";
}

//=============================================================================//
// 4 and 5: THE TWO CALLS WITH NO OTHER TRIGGER.
//
// The shape this tree has already found THREE times -- a stage with a driver, a
// dispatcher and its own tests, and no production caller, which passes every
// check that exists and does nothing.
//=============================================================================//

TEST( SkyboxWire, TheClientsSkyboxPassIsCalledFromTheNetUpdateStage )
{
	const std::string code = ReadCode( kClientMod );
	const std::string body = BodyOf( code, "public void OnStage(ClientFrameStage stage)" );

	ASSERT_FALSE( body.empty() ) << "ClientModule.OnStage was not found";

	EXPECT_NE( body.find( "_skybox?.Pass()" ), std::string::npos )
		<< "NOTHING CALLS THE SKYBOX PASS. The class exists, its tests pass, and "
		   "no skybox reaches the renderer -- and the game runs perfectly, which "
		   "is exactly what the tree looked like while the defect was open.";

	// ON THE STAGE THE DECODE FINISHES ON, so what is drawn this frame is what
	// arrived this frame.
	const size_t stage  = body.find( "ClientFrameStage.NetUpdatePostDataUpdateEnd" );
	const size_t pass   = body.find( "_skybox?.Pass()" );
	const size_t render = body.find( "ClientFrameStage.RenderStart" );

	ASSERT_NE( stage, std::string::npos );
	ASSERT_NE( render, std::string::npos );
	EXPECT_GT( pass, stage );
	EXPECT_LT( pass, render )
		<< "the skybox pass has moved to a later stage than the decode's, so "
		   "what is drawn is one frame stale.";
}

TEST( SkyboxWire, TheClientsSkyboxIsForgottenAtLevelInit )
{
	const std::string body = BodyOf( ReadCode( kClientMod ),
	                                 "public void LevelInit(string mapName)" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "_skybox?.LevelInit()" ), std::string::npos )
		<< "the push is compared against what was last pushed, so a new map "
		   "whose sky camera happens to equal the old map's would never be "
		   "pushed and the renderer would hold the previous level's area.";
}

TEST( SkyboxWire, TheServersSkyboxServiceIsRegistered )
{
	const std::string code = ReadCode( kServerMod );

	EXPECT_NE( code.find( "_services.Create<WorldSkybox>()" ), std::string::npos )
		<< "WorldSkybox is never constructed, so the frame driver resolves no "
		   "participant for it and no player is ever given a skybox.";

	EXPECT_NE( code.find( "_services.AddInstance(_worldSkybox)" ), std::string::npos )
		<< "WorldSkybox is constructed and not registered. FrameDriver resolves "
		   "its participants ONCE, out of the container -- an unregistered "
		   "service with a perfectly good OnPreClientUpdate is never called.";
}

TEST( SkyboxWire, TheServersSkyboxRunsAtStocksOwnPoint )
{
	const std::string code = ReadCode( kWorldSkybox );

	EXPECT_NE( code.find( "void IFrameLifecycle.OnPreClientUpdate" ), std::string::npos )
		<< "the skybox is not published at PreClientUpdate. That is where "
		   "stock's UpdateAllClientData runs, and the sky camera is the WHOLE of "
		   "what its ClientData_Update does -- playerlocaldata.cpp's body is the "
		   "sky camera and nothing else.";

	EXPECT_NE( code.find( "IGameLifecycle" ), std::string::npos )
		<< "nothing forgets the skybox between levels.";
}

//=============================================================================//
// 6: THE RENDERER'S OWN DRAW IS COUNTED.
//
// "The gate is green" is not a rendered thing. 8e-render's dust defect passed
// every counter it had while drawing nothing, so the marks are placed where the
// renderer actually commits and their control arm is r_3dsky.
//=============================================================================//

//-----------------------------------------------------------------------------
// THE TWO MARKS AND THE SIX COUNTERS THEY FED ARE RETIRED, PHASE 1c, AND THE
// FACT THAT REPLACES THEM IS ABOUT THE RENDER PATH RATHER THAN THE COUNTING.
//
// WHAT THE OLD TWO ASSERTED. TheDrawIsMarkedWhereTheRendererCommits required
// SourceSharp_SkyboxWorldDrawn() inside CSkyboxView::DrawInternal and
// SourceSharp_SkyboxSetupMark() plus COMPILE_TIME_ASSERT(
// SKYBOX_3DSKYBOX_VISIBLE == 1 ) inside CSkyboxView::Setup;
// EveryCounterIsReportedAndNotJustIncremented required all six counters to
// come back out through SourceSharp_ViewerSkyboxCounts, and the console
// command to exist.
//
// WHY THEY ARE NOT RE-POINTED. There is nothing to re-point them at. The six
// counters were readable only through SourceSharp_ViewerSkyboxCounts, whose
// only caller was cs_cl_skybox_report, and with that console command gone no
// code in either DLL, no managed assembly and no tool could read any of them.
// The marks existed only to bump them. So the subject of both assertions is
// gone, and the COMPILE_TIME_ASSERT that "earned its keep" earned it by
// pinning an argument to a function that no longer exists.
//
// WHAT IS WORTH KEEPING IS THE SHAPE OF THE RENDER PATH THE MARKS WERE PLACED
// ON, because that is what a future reader would want and it is a claim about
// the build rather than about an instrument: the miniature world is drawn at
// the END of DrawInternal, past r_3dsky, past the area-255 refusal and past
// DrawWorld; and Setup holds BOTH the engine's visibility answer and the
// parameters, which is why it was the one site that could tell "this map has
// no sky face over the viewer" from "the wire is broken". Asserted below.
//-----------------------------------------------------------------------------

// THE TWO RENDER-PATH FACTS THAT STOOD HERE LEFT WITH viewrender.cpp (C7
// step 3). They read CSkyboxView::Setup, PreRender3dSkyboxWorld and
// DrawInternal; the 3D sky view is the render plan's now (RenderPlanner's
// SkyboxSetup and SkyboxDrawInternal: r_3dsky and r_skybox, the sky's
// presence and the area-255 refusal before any op, the world drawn inside
// the pushed sky view), held by RenderPlannerTests'
// AVisible3DSkyDrawsFirstWithItsOwnNearPlaneAndForcedLeaf and the frozen
// frames' facts, and the live gate's frozen-frame compares.

//=============================================================================//
// The wire itself.
//=============================================================================//

TEST( SkyboxWire, ThePlayerNetworksTheSkyboxAndDoesNotPredictIt )
{
	const std::string code = Read( kNetState );
	ASSERT_FALSE( code.empty() ) << "PlayerNetState.cs could not be read";

	static const char *kFields[] = {
		"SkyValid", "SkyOrigin", "SkyScale", "SkyArea", "SkyFogEnable",
		"SkyFogBlend", "SkyFogRadial", "SkyFogDirPrimary", "SkyFogColorPrimary",
		"SkyFogColorSecondary", "SkyFogStart", "SkyFogEnd", "SkyFogMaxDensity" };

	for ( size_t i = 0; i < sizeof( kFields ) / sizeof( kFields[0] ); i++ )
	{
		EXPECT_NE( code.find( std::string( "public partial " ) + "" ), std::string::npos );
		EXPECT_NE( code.find( kFields[ i ] ), std::string::npos )
			<< "PlayerNetState no longer declares " << kFields[ i ]
			<< "; the skybox cannot reach a client that is not sent it.";
	}

	// NOT PREDICTED. On the comment-stripped text, so the paragraph explaining
	// why cannot satisfy it, and searched from the END of the last field before
	// the skybox block rather than from SkyValid's own position -- an attribute
	// written ABOVE a field sits outside a window that starts at the field, and
	// that is the mutation which left fog_wire_test's version of this green.
	const std::string stripped = ReadCode( kNetState );
	const size_t lastFog = stripped.find( "public partial float FogMaxDensity" );
	ASSERT_NE( lastFog, std::string::npos )
		<< "FogMaxDensity is no longer the last field before the skybox block, "
		   "so the window this test searches no longer means what it says";

	const size_t afterIt = stripped.find( '\n', lastFog );
	ASSERT_NE( afterIt, std::string::npos );

	EXPECT_EQ( stripped.find( "[Predicted]", afterIt ), std::string::npos )
		<< "a skybox field is marked [Predicted]. The movement loop neither "
		   "reads nor writes one, so it would sit on the misprediction ring for "
		   "a value that can only ever agree.";
}

TEST( SkyboxWire, ValidAndArea255AreTwoDifferentFacts )
{
	// The one modelling decision in this wire, and the one a simplification
	// removes first: collapsing "no sky camera" onto "area 255" would lose the
	// difference between a map that has none and a camera that is somewhere the
	// engine cannot place, and those want different words in a bug report.
	const std::string code = ReadCode( kSkyState );
	ASSERT_FALSE( code.empty() ) << "SkyState.cs could not be read";

	EXPECT_NE( code.find( "bool Valid," ), std::string::npos );
	EXPECT_NE( code.find( "int Area," ), std::string::npos )
		<< "SkyState no longer carries the area and the validity bit as "
		   "separate facts.";

	EXPECT_NE( code.find( "state.SkyValid = Valid;" ), std::string::npos );
	EXPECT_NE( code.find( "Valid: state.SkyValid," ), std::string::npos )
		<< "the validity bit does not survive the round trip through the blob, "
		   "so every client reads SkyValid false and no skybox is ever pushed.";
}

} // namespace
