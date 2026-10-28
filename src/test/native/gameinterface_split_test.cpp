//========= SourceSharp — gameinterface.cpp's last four interfaces ============//
//
// plan_gamedll_after.md Phase A9. The file that held four of the census's seven
// game-DLL interfaces is taken apart and deleted. This harness holds the parts
// of that the linker cannot.
//
// WHY A LINK IS NOT ENOUGH, said once for the whole file:
//
//   * an EXPOSE_ that goes missing is not a link error. Nothing in this binary
//     calls CreateInterface; the ENGINE does, at load, by version string, and a
//     game DLL that has stopped registering IServerGameEnts links, starts, and
//     fails when the engine asks. tools/gamedll-interface-census reads the
//     built .so for exactly this and these facts read the source that feeds it;
//   * an EXPOSE_ that is DUPLICATED is worse than one that is missing. Two
//     InterfaceReg entries for one version string do not collide at link time.
//     Both link, and which one the engine gets is decided by static-init order
//     across translation units -- a silent, nondeterministic wrong answer;
//   * the state that came across with each class had to change shape. Stock
//     kept it in file-scope globals and tools/statics-audit gates that shape
//     out of src/sourcesharp, so it is class fields here. A field that is
//     declared and never assigned compiles perfectly and is a dead setting.
//
// Source-read, and forced: every one of these files includes cbase.h and the
// whole entity stack, and none can be linked into a gtest binary.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

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

bool Exists( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return true;   // cannot tell; do not report a deletion that did not happen
	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), &st ) == 0;
}

std::string StripComments( const std::string &in )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < in.size() && in[ i ] == '*' && in[ i + 1 ] == '/' ) { bInBlock = false; i++; }
			continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '*' ) { bInBlock = true; i++; continue; }
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			if ( i < in.size() ) out += '\n';
			continue;
		}
		out += in[ i ];
	}
	return out;
}

std::string Tight( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

std::string Code( const char *pszRel ) { return Tight( StripComments( Read( pszRel ) ) ); }

int CountOf( const std::string &hay, const std::string &needle )
{
	int n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos; at = hay.find( needle, at + 1 ) )
		n++;
	return n;
}

const char *kEnts    = "sourcesharp/game/server/shell_gameents.cpp";
const char *kShared  = "sourcesharp/game/server/shell_sharedappsystems.cpp";
const char *kClients = "sourcesharp/game/server/shell_gameclients.cpp";
const char *kTags    = "sourcesharp/game/server/shell_gametags.cpp";
const char *kHeader  = "game/server/gameinterface.h";
const char *kGlobals = "sourcesharp/game/server/shell_engineboundary.cpp";
// WHERE THE RATIONALE LIVES NOW. It used to be a comment beside the $File line
// in the .vpc; the scripts were reformatted with every comment stripped out to
// this document, verbatim and keyed the same way. The convention is unchanged --
// a removed file is NAMED somewhere greppable, so a $File line deleted with no
// explanation is still distinguishable from one deleted by accident -- only the
// place it is named moved.

} // namespace

//=============================================================================//
// STEP 1a — IServerGameEnts
//=============================================================================//

TEST( GameInterfaceSplit, ServerGameEntsIsDefinedInItsOwnFile )
{
	const std::string code = Code( kEnts );
	ASSERT_FALSE( code.empty() );

	EXPECT_NE( code.find( "classCServerGameEnts:publicIServerGameEnts" ), std::string::npos )
		<< kEnts << " does not declare CServerGameEnts.";

	// All six virtuals. The engine calls every one of them through the vtable,
	// so a body left behind in the old file is a pure-virtual crash at load,
	// not a link error.
	EXPECT_NE( code.find( "voidCServerGameEnts::SetDebugEdictBase(edict_t*base)" ), std::string::npos );
	EXPECT_NE( code.find( "voidCServerGameEnts::MarkEntitiesAsTouching(" ), std::string::npos );
	EXPECT_NE( code.find( "voidCServerGameEnts::FreeContainingEntity(" ), std::string::npos );
	EXPECT_NE( code.find( "edict_t*CServerGameEnts::BaseEntityToEdict(" ), std::string::npos );
	// RENAMED, NOT MOVED: `CBaseEntity` is not a name any more -- the alias
	// header retired and the token now reads `IGameEntity`. The engine calls
	// this through the vtable either way, so the SIGNATURE is what has to be
	// here, and it is asserted in the spelling the file compiles under.
	EXPECT_NE( code.find( "IGameEntity*CServerGameEnts::EdictToBaseEntity(" ), std::string::npos );
	EXPECT_EQ( code.find( "CBaseEntity*CServerGameEnts::EdictToBaseEntity(" ), std::string::npos )
		<< "the old spelling is back -- there is no CBaseEntity to name";
	EXPECT_NE( code.find( "voidCServerGameEnts::CheckTransmit(" ), std::string::npos );
}

TEST( GameInterfaceSplit, ServerGameEntsRegistersExactlyOnce )
{
	const std::string code = Code( kEnts );
	ASSERT_FALSE( code.empty() );

	// Registered = its row in the factory's table (shell_factory.h), once.
	EXPECT_EQ( 1, CountOf( code, "return(IServerGameEnts*)&s_ServerGameEnts;" ) );
	EXPECT_EQ( 1, CountOf( code, "constSourceSharpExposedkExposeServerGameEnts={INTERFACEVERSION_SERVERGAMEENTS,SourceSharp_GetServerGameEnts};" ) )
		<< "IServerGameEnts is registered " << CountOf( code, "INTERFACEVERSION_SERVERGAMEENTS" )
		<< " time(s) in " << kEnts << ". The engine resolves a duplicate by "
		<< "static-init order, which is silent.";
}

TEST( GameInterfaceSplit, TheDebugEdictBaseIsAFieldAndIsActuallyAssigned )
{
	const std::string code = Code( kEnts );
	ASSERT_FALSE( code.empty() );

	// The positive half FIRST: the field exists and the setter writes it. An
	// absence-only fact here would stay green if SetDebugEdictBase's body were
	// deleted outright.
	EXPECT_NE( code.find( "edict_t*m_pDebugEdictBase=NULL;" ), std::string::npos )
		<< "the debug edict base is not declared as a field of CServerGameEnts.";
	EXPECT_NE( code.find( "m_pDebugEdictBase=base;" ), std::string::npos )
		<< "SetDebugEdictBase does not assign the field, so the setting is dead.";

	EXPECT_EQ( code.find( "g_pDebugEdictBase" ), std::string::npos )
		<< "stock's file-scope global came across under its old name; "
		<< "tools/statics-audit gates that shape out of src/sourcesharp.";
}

// THE FORCE-TRANSMIT FACT IS RETIRED WITH ITS CONVAR -- plan_shim.md 5 3a,
// item 8, lane 3b-enginebind. It read:
//
//     ForceTransmitEntsIsStillRegisteredAndStillRead
//       "sv_force_transmit_ents" is still in kEnts
//       m_svForceTransmitEnts.GetBool() is still in kEnts
//
// and its reason was that "a cheat convar that silently stops existing reads
// as a working default" and "nothing reads the convar, so setting it does
// nothing". BOTH ARE RIGHT, AND TOGETHER THEY ARE WHY THE CONVAR IS GONE.
//
// The read this fact pinned was `const bool bWouldForceTransmit = bInPVS ||
// m_svForceTransmitEnts.GetBool();` followed by `(void)bWouldForceTransmit;`
// on the next line. So the second assertion was satisfied by a read whose
// value was discarded, and the convar was already in exactly the state the
// first assertion was written to prevent: registered, settable, and with no
// effect on anything. The fact was keeping the LETTER of its own claim while
// the claim itself was false.
//
// It is not replaced by a "the convar is gone" fact. What replaces it is that
// the name is absent from the tree, which
// TheTransmitWalkStillReadsTheNetworkProperty below now states as part of a
// claim that is still about live code.
//
// A REPLACEMENT CONVAR WOULD BE CAUGHT. If sv_force_transmit_ents is ever
// wanted again it belongs beside the managed transmit code that would read it,
// as a [ConVar] attribute -- not as a native declaration, which is the rule
// this item enforces.

TEST( GameInterfaceSplit, TheElevenAliasGuardsAreGoneAndSoAreTheEntityCallsTheyWrapped )
{
	// READ THE ASSERTIONS, NOT THE NAME. This fact was
	// EveryAliasGuardCameAcrossWithCheckTransmit and it counted ELEVEN
	// `#ifndef SS_CBASEENTITY_ALIAS\n` regions -- newline included, because a
	// bare needle is a prefix of `..._ALIAS_DROPPED` and this tree has already
	// found two checks that could not fail for exactly that reason. Its subject
	// was the move out of gameinterface.cpp: each guard wrapped a call that is
	// only legal when the entity really is a CBaseEntity, and one dropped in a
	// move would have been a wrong-vtable call rather than a compile error.
	//
	// THE ALIAS RETIRED, so there is no `SS_CBASEENTITY_ALIAS` to be armed or
	// unarmed and all eleven arms are deleted. Every one of them was the
	// UNARMED half -- text the compiler never saw -- so this is a deletion of
	// dead source and not a behaviour change.
	//
	// ABSENCE ALONE IS HALF A FACT: eleven zeroes would also pass over an empty
	// file, and the danger the old fact named has not gone away. It has inverted.
	// The hazard now is a call coming BACK: `pEnt->SetTransmit(...)` written
	// unguarded on a pointer this build's entities are not, which IS a
	// wrong-vtable call and still not a compile error. So both halves are
	// asserted: the guards are gone, the calls they wrapped are gone with them,
	// and the arms that DID compile are still here and still unconditional.
	const std::string raw  = Read( kEnts );
	const std::string code = Code( kEnts );
	ASSERT_FALSE( raw.empty() );
	ASSERT_FALSE( code.empty() );

	// A DIRECTIVE, NOT A MENTION. This counted every occurrence of the macro
	// NAME in the raw text, and the close's own note explaining what the deleted
	// region WAS has to spell that name to explain it -- so the fact went red
	// over a comment. That is "prose counts as code to a raw grep", the hazard
	// this project names in its wave brief, firing on the fact that exists to
	// catch it. Counted line-initially as a preprocessor directive instead, and
	// the mention count is kept BESIDE it as a floor so a file that stopped
	// explaining itself is still visible.
	int nAliasDirectives = 0;
	{
		size_t iPos = 0;
		while ( iPos < raw.size() )
		{
			const size_t iEnd  = raw.find( '\n', iPos );
			const std::string szLine =
				raw.substr( iPos, ( iEnd == std::string::npos ? raw.size() : iEnd ) - iPos );
			iPos = ( iEnd == std::string::npos ) ? raw.size() : iEnd + 1;

			const size_t iFirst = szLine.find_first_not_of( " \t" );
			if ( iFirst == std::string::npos || szLine[ iFirst ] != '#' )
				continue;
			if ( szLine.find( "SS_CBASEENTITY_ALIAS" ) != std::string::npos )
				nAliasDirectives++;
		}
	}
	EXPECT_EQ( 0, nAliasDirectives )
		<< "an SS_CBASEENTITY_ALIAS region is back in " << kEnts << ". Every "
		<< "compiled file carries zero directives on that macro, so whichever "
		<< "half of a new one is live is a check that cannot fail.";

	// GONE WITH THE GUARDS: the entity-typed calls, every spelling.
	EXPECT_EQ( 0, CountOf( code, "->SetTransmit(" ) )
		<< "SetTransmit is a CBaseEntity method called on a pointer that is not "
		<< "one in this build -- unguarded, it is a wrong-vtable call, not a "
		<< "compile error";
	EXPECT_EQ( 0, CountOf( code, "->ShouldTransmit(" ) );
	EXPECT_EQ( 0, CountOf( code, "->PhysicsMarkEntitiesAsTouching(" ) );
	EXPECT_EQ( 0, CountOf( code, "pEnt->edict()" ) );

	// STILL HERE AND NOW UNCONDITIONAL: the arms that were compiled all along.
	// If a "fix" ever re-guards this file, these go with the guards.
	EXPECT_EQ( 1, CountOf( code, "Assert(bHaveRecipient&&!pRecipientEntity);" ) )
		<< "the armed recipient assertion left with the guards -- it is the one "
		<< "that says the SDK side answered nothing, which is what this build's "
		<< "CheckTransmit relies on";
	EXPECT_EQ( 1, CountOf( code, "edict_t*pResult=NULL;" ) )
		<< "BaseEntityToEdict no longer answers NULL unconditionally";
	EXPECT_EQ( 1, CountOf( code, "IGameEntity*pResult=NULL;" ) )
		<< "EdictToBaseEntity no longer answers NULL unconditionally";

	// AND THE WALK NO LONGER READS A NETWORK PROPERTY (P5 lane E). It read
	// netProp->AreaNum() and netProp->IsInPVS() on the arm after
	// SourceSharp_SDKEntityFromUnknown, and that arm never ran: every
	// IServerUnknown::GetBaseEntity in server.so answers NULL, so the
	// visibility-only arm before it always continued. The netProp it read was a
	// CServerNetworkProperty, which server.so never constructs. What decides
	// is the edict's collideable bounds against the PVS, unconditionally.
	EXPECT_EQ( 0, CountOf( code, "netProp" ) );
	EXPECT_EQ( 0, CountOf( code, "CServerNetworkProperty" ) );
	EXPECT_EQ( 0, CountOf( code, "SourceSharp_SDKEntityFromUnknown" ) );
	EXPECT_EQ( 1, CountOf( code, "engine->CheckBoxInPVS(vecMins,vecMaxs,pInfo->m_PVS,pInfo->m_nPVSSize)" ) )
		<< "the visibility arm, the one the walk always took, is gone";

	// AND THE THIRD READ IS GONE, WITH THE CONVAR IT READ. It was
	// m_svForceTransmitEnts.GetBool(), assigned to a local that the next line
	// `(void)`-discarded; see the retired-fact note above
	// TheElevenAliasGuardsAreGone... for why a discarded read is not a reader.
	// Counted at ZERO rather than left unstated, because a re-declaration of
	// the convar would otherwise slip back in unremarked.
	EXPECT_EQ( 0, CountOf( code, "m_svForceTransmitEnts" ) );
	EXPECT_EQ( std::string::npos, code.find( "\"sv_force_transmit_ents\"" ) )
		<< "sv_force_transmit_ents is declared natively again. It is not "
		<< "engine-transmitted -- FCVAR_CHEAT|FCVAR_DEVELOPMENTONLY, never "
		<< "FCVAR_REPLICATED -- so if the knob is wanted back it belongs in "
		<< "managed code beside whatever reads it.";
}

//=============================================================================//
// STEP 1b — IServerDLLSharedAppSystems
//=============================================================================//

TEST( GameInterfaceSplit, SharedAppSystemsIsDefinedInItsOwnFile )
{
	const std::string code = Code( kShared );
	ASSERT_FALSE( code.empty() );

	EXPECT_NE( code.find( "classCServerDLLSharedAppSystems:publicIServerDLLSharedAppSystems" ), std::string::npos );
	EXPECT_EQ( 1, CountOf( code, "constSourceSharpExposedkExposeServerDllSharedAppSystems={SERVER_DLL_SHARED_APPSYSTEMS,SourceSharp_GetServerDllSharedAppSystems};" ) );
	EXPECT_EQ( 1, CountOf( code, "return(IServerDLLSharedAppSystems*)&s_ServerDLLSharedAppSystems;" ) );
}

TEST( GameInterfaceSplit, BothSharedAppSystemsAreStillNamed )
{
	const std::string code = Code( kShared );
	ASSERT_FALSE( code.empty() );

	// These two strings are the whole content of the interface. Losing one is a
	// DLL the engine never loads: no sound emitter system, or no scene file
	// cache, and neither reports anything at the point it goes missing.
	EXPECT_NE( code.find( "\"soundemittersystem\"DLL_EXT_STRING" ), std::string::npos );
	EXPECT_NE( code.find( "\"scenefilecache\"DLL_EXT_STRING" ), std::string::npos );
	EXPECT_NE( code.find( "SOUNDEMITTERSYSTEM_INTERFACE_VERSION" ), std::string::npos );
	EXPECT_NE( code.find( "SCENE_FILE_CACHE_INTERFACE_VERSION" ), std::string::npos );
}

//=============================================================================//
// STEP 2a — IServerGameClients, and all THREE of its version strings
//=============================================================================//

TEST( GameInterfaceSplit, AllThreeClientVersionStringsAreStillAnswered )
{
	const std::string code = Code( kClients );
	ASSERT_FALSE( code.empty() );

	// The engine picks ONE of these by name at load. 003 and 004 are older
	// engines; dropping either is not a link error and not a startup error on
	// THIS engine -- it is a server that stops loading under a different one.
	EXPECT_EQ( 1, CountOf( code, "constSourceSharpExposedkExposeServerGameClients003={INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_3,SourceSharp_GetServerGameClients003};" ) );
	EXPECT_EQ( 1, CountOf( code, "return(IServerGameClients003*)&s_ServerGameClients;" ) );
	EXPECT_EQ( 1, CountOf( code, "constSourceSharpExposedkExposeServerGameClients004={INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_4,SourceSharp_GetServerGameClients004};" ) );
	EXPECT_EQ( 1, CountOf( code, "return(IServerGameClients004*)&s_ServerGameClients;" ) );
	EXPECT_EQ( 1, CountOf( code, "constSourceSharpExposedkExposeServerGameClients={INTERFACEVERSION_SERVERGAMECLIENTS,SourceSharp_GetServerGameClients};" ) );
	EXPECT_EQ( 1, CountOf( code, "return(IServerGameClients*)&s_ServerGameClients;" ) );

	// All three name the SAME object. Three singletons would be three
	// independent copies of m_pClientPutInServerOverride, and which one the
	// engine held would decide whether the bot hook worked.
	EXPECT_EQ( 1, CountOf( code, "CServerGameClientss_ServerGameClients;" ) )
		<< "there is not exactly one CServerGameClients object in " << kClients;
}

TEST( GameInterfaceSplit, TheClientConnectionPathCameAcrossWhole )
{
	const std::string code = Code( kClients );
	ASSERT_FALSE( code.empty() );

	// The four the engine drives to get a player from "connecting" to "playing".
	// A body left behind in the deleted file is a pure virtual, and the visible
	// symptom is a rejected connection.
	EXPECT_NE( code.find( "boolCServerGameClients::ClientConnect(" ), std::string::npos );
	EXPECT_NE( code.find( "voidCServerGameClients::ClientPutInServer(" ), std::string::npos );
	EXPECT_NE( code.find( "voidCServerGameClients::ClientActive(" ), std::string::npos );
	EXPECT_NE( code.find( "voidCServerGameClients::ClientSpawned(" ), std::string::npos );
	EXPECT_NE( code.find( "voidCServerGameClients::ClientDisconnect(" ), std::string::npos );

	// And the per-frame ones, whose loss is a live server that stops moving.
	EXPECT_NE( code.find( "floatCServerGameClients::ProcessUsercmds(" ), std::string::npos );
	EXPECT_NE( code.find( "voidCServerGameClients::ClientSetupVisibility(" ), std::string::npos );
}

TEST( GameInterfaceSplit, TheBotHookWritesTheFieldOnTheRegisteredObject )
{
	const std::string code = Code( kClients );
	ASSERT_FALSE( code.empty() );

	// Positive half first. ClientPutInServerOverride is declared in
	// gameinterface.h and its whole job is to write this one field; if the free
	// function stopped naming the singleton the three EXPOSE_ lines hand the
	// engine, it would write a different object's field and the hook would be
	// silently ignored.
	//
	// THE FREE SETTER WENT IN THE SERVER GC CULL (2026-10-01): nothing called
	// ClientPutInServerOverride, and --print-gc-sections dropped it. That left
	// the field always NULL, so the dead-state sweep after took the field, its
	// typedef and the branch that read it: ClientPutInServer calls straight
	// through. A bot hook coming back brings its setter, field and reader
	// together, and these assertions with it.
	EXPECT_EQ( code.find( "voidClientPutInServerOverride(" ), std::string::npos )
		<< "ClientPutInServerOverride is defined again; say which caller needs it.";
	EXPECT_EQ( code.find( "m_pClientPutInServerOverride=fn;" ), std::string::npos )
		<< "the override has a setter again; say which caller needs it.";
	EXPECT_EQ( code.find( "m_pClientPutInServerOverride" ), std::string::npos )
		<< "the bot hook's field is back; say which caller sets it.";
	EXPECT_NE( code.find( "kBoundaryProbe_Clients_ClientPutInServer);::ClientPutInServer(pEntity,playername);" ), std::string::npos )
		<< "ClientPutInServer no longer hands the engine's call straight to ::ClientPutInServer.";

	EXPECT_EQ( code.find( "g_pClientPutInServerOverride" ), std::string::npos )
		<< "stock's file-scope static came across under its old name.";
}

TEST( GameInterfaceSplit, TheUsercmdMoveClampIsStillRegisteredAndStillApplied )
{
	const std::string code = Code( kClients );
	ASSERT_FALSE( code.empty() );

	// THE DECLARATION MOVED TO MANAGED and the READ DID NOT -- plan_shim.md
	// 5 3a, item 8. The convar's flags were literally 0: no FCVAR_REPLICATED,
	// so no client sees it, and nothing in the engine reads the name. That
	// makes it a server-side clamp, which is policy, and policy is C#'s.
	//
	// SO THIS FACT NOW HAS TWO HALVES AND NEEDS BOTH. A move that dropped the
	// native read would leave the clamp unregistered on the only side that
	// applies it; a move that left the native DECLARATION in place would be a
	// duplicate registration of a name managed code already owns, which is the
	// failure this tree keeps finding.
	EXPECT_EQ( std::string::npos, code.find( "\"sv_max_usercmd_move_magnitude\",\"1000\"" ) )
		<< "shell_gameclients.cpp declares the convar again -- a copy rather "
		<< "than a move, and a duplicate registration of a name "
		<< "SourceSharp.Server/ConVars.cs already holds.";

	// AND SINCE plan_server_novalve.md P2.2 THE CLAMP IS C#'s TOO: it sits
	// inside the usercmd decode, which moved whole. So the native file must not
	// read the convar any more -- a leftover read would be a second, unused
	// resolution of a name -- and the managed decoder must clamp all three
	// axes, fed from this side's own ConVar.
	EXPECT_EQ( std::string::npos, code.find( "sv_max_usercmd_move_magnitude.GetFloat()" ) )
		<< "shell_gameclients.cpp still reads the clamp's convar; the decode "
		<< "and its clamp are C#'s (UserCmdCodec.Decode).";

	const std::string codec = Code( "sourcesharp/managed/SourceSharp.Shared.Interop/UserCmdCodec.cs" );
	ASSERT_FALSE( codec.empty() );
	// Three comparisons, one per axis. Two would clamp two axes and leave the
	// third open, which is a working server with a hole in it.
	EXPECT_EQ( 3, CountOf( codec, ">maxMoveMagnitude" ) )
		<< "UserCmdCodec.Decode does not clamp all three axes.";

	const std::string module = Code( "sourcesharp/managed/SourceSharp.Server/ServerModule.cs" );
	ASSERT_FALSE( module.empty() );
	EXPECT_NE( module.find( "_playerCommands.MaxMoveMagnitude=()=>moveMagnitude.Value;" ), std::string::npos )
		<< "the decode's clamp is not fed from the managed ConVar.";

	// AND THE MANAGED SIDE ACTUALLY DECLARES IT. Without this the two facts
	// above are satisfied by a CManagedConVarRef pointing at a name nothing
	// registers -- which resolves to nothing and clamps at whatever the ref
	// answers for an unregistered convar.
	const std::string cs = Code( "sourcesharp/managed/SourceSharp.Server/ConVars.cs" );
	ASSERT_FALSE( cs.empty() );
	EXPECT_NE( cs.find( "[ConVar(\"sv_max_usercmd_move_magnitude\",\"1000\"," ), std::string::npos )
		<< "SourceSharp.Server/ConVars.cs does not declare the convar the "
		<< "native clamp reads by name.";
}

//=============================================================================//
// STEP 2b — IServerGameTags, and the header registration it came out of
//=============================================================================//

TEST( GameInterfaceSplit, ServerGameTagsRegistersFromACppAndNotAHeader )
{
	const std::string code = Code( kTags );
	ASSERT_FALSE( code.empty() );

	EXPECT_NE( code.find( "classCServerGameTags:publicIServerGameTags" ), std::string::npos );
	EXPECT_EQ( 1, CountOf( code, "constSourceSharpExposedkExposeServerGameTags={INTERFACEVERSION_SERVERGAMETAGS,SourceSharp_GetServerGameTags};" ) );
	EXPECT_EQ( 1, CountOf( code, "return(IServerGameTags*)&s_ServerGameTags;" ) );
	EXPECT_NE( code.find( "voidCServerGameTags::GetTaggedConVarList(" ), std::string::npos );
}

TEST( GameInterfaceSplit, TheHeaderRegistersNothingAtAll )
{
	// THE WHOLE POINT OF STEP 2b. An EXPOSE_ in a header is one registration
	// per INCLUDING translation unit -- six of them, measured with nm on the
	// built server.so, for "ServerGameTags001" alone. Duplicate InterfaceReg
	// entries for one version string do not collide at link time; they are
	// resolved by static-initialisation order.
	//
	// THE HEADER IS GONE, which is the strong form of "registers nothing".
	// game/server/gameinterface.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6, so the scan of its
	// text for EXPOSE_SINGLE_INTERFACE and a CServerGameTags body became an
	// assertion that it is not there at all -- anchored on a sibling in the
	// same directory that IS there, so a probe that cannot see game/server
	// does not pass it.
	EXPECT_TRUE( Exists( "game/shared/shareddefs.h" ) )
		<< "game/shared/shareddefs.h is not readable, so the absence below "
		<< "proves nothing about game/server.";
	EXPECT_FALSE( Exists( kHeader ) )
		<< kHeader << " is readable from the farm again. It once registered "
		<< "interfaces from a HEADER -- one singleton and one InterfaceReg per "
		<< "including translation unit -- and nothing compiles it now.";
}

//-----------------------------------------------------------------------------
// THE CLIENTS CLASS LEFT THE HEADER TOO, at plan_entity.md step 4g — and this
// is a MOVE, not a second copy, so both halves are asserted.
//
// A9 left the declaration in gameinterface.h on a named ground: "shell_server.cpp
// defines GetPlayerLimits against it, and four out-of-build mod files override
// that one method the same way." The four are base_gameinterface.cpp,
// hl2mp_gameinterface.cpp, sdk_gameinterface.cpp and tf_gameinterface.cpp, and
// none of them is compiled by server_sourcesharp — so what the header was sharing with
// stock, it was not sharing with anything compiled. shell_server.cpp still
// defines GetPlayerLimits and now includes the shim header to do it.
//-----------------------------------------------------------------------------
TEST( GameInterfaceSplit, TheClientsClassIsDeclaredInTheShimAndNotTheStockHeader )
{
	// The stock half is retired: game/server/gameinterface.h is compiled by
	// nothing since plan_server_novalve.md P5 and left the farm in P6
	// (TheHeaderRegistersNothingAtAll pins it absent).
	const std::string shim = Code( "sourcesharp/game/server/shell_gameclients.h" );
	ASSERT_FALSE( shim.empty() );

	EXPECT_NE( shim.find( "classCServerGameClients:publicIServerGameClients" ), std::string::npos )
		<< "the shim header does not declare CServerGameClients, so "
		<< "shell_server.cpp's GetPlayerLimits has nothing to attach to.";

	// THE FIRST FIELD IS GONE TOO: the bot hook, always NULL once the server
	// gc cull deleted its setters, went in the dead-state sweep with its typedef.
	EXPECT_EQ( shim.find( "m_pClientPutInServerOverride" ), std::string::npos )
		<< "the shim header declares the bot hook's field again.";
	EXPECT_EQ( shim.find( "ClientPutInServerOverrideFn" ), std::string::npos )
		<< "the shim header declares the bot hook's typedef again.";

	// THE SECOND FIELD IS GONE AND IS REQUIRED TO BE. It was
	// `ConVar m_svMaxUsercmdMoveMagnitude;`, and item 8 moved the declaration
	// to managed code; a field left here would be the duplicate registration
	// the move exists to remove.
	EXPECT_EQ( shim.find( "ConVarm_svMaxUsercmdMoveMagnitude;" ), std::string::npos )
		<< "the shim header declares the moved convar again.";
}

//-----------------------------------------------------------------------------
// ...and the shim header does not register anything either, which is the SAME
// rule the Tags move was made under. Asserted separately because "the class
// moved" and "the move did not carry an EXPOSE_ into a header" are two facts,
// and a header with an EXPOSE_ in it is one singleton per including TU with
// nothing anywhere reporting it.
//-----------------------------------------------------------------------------
TEST( GameInterfaceSplit, TheShimClientsHeaderRegistersNothing )
{
	const std::string shim = Code( "sourcesharp/game/server/shell_gameclients.h" );
	ASSERT_FALSE( shim.empty() );

	EXPECT_EQ( shim.find( "EXPOSE_SINGLE_INTERFACE" ), std::string::npos );

	// The three rows are all still in the .cpp, one apiece (the factory's
	// table, shell_factory.h; the EXPOSE_ macros are gone from server.so).
	const std::string cpp = Code( "sourcesharp/game/server/shell_gameclients.cpp" );
	ASSERT_FALSE( cpp.empty() );
	EXPECT_EQ( 3, CountOf( cpp, "constSourceSharpExposedkExposeServerGameClients" ) )
		<< "the three engine vintages 003, 004 and the current version string "
		<< "are what keeps this DLL loadable by more than one engine.";
}

//=============================================================================//
// STEP 3 — stock's CServerGameDLL is deleted
//=============================================================================//

TEST( GameInterfaceSplit, TheVersionAssumptionMovedToTheClassThatAnswersIt )
{
	// COMPILE_TIME_ASSERT( INTERFACEVERSION_SERVERGAMEDLL_INT == 12 ) sat under
	// stock's EXPOSE_ and would have been deleted with it. It is the one thing
	// in that block worth keeping: the three-version arrangement below it
	// assumes 12, and a bumped SDK would change the number silently. It is
	// beside CShellGameDll now, which is the class that answers the string.
	const std::string shell = Code( "sourcesharp/game/server/shell_gamedll.cpp" );
	ASSERT_FALSE( shell.empty() );

	EXPECT_NE( shell.find( "COMPILE_TIME_ASSERT(INTERFACEVERSION_SERVERGAMEDLL_INT==12)" ), std::string::npos )
		<< "the version assumption was deleted with stock's registration rather "
		<< "than carried to the class that now answers ServerGameDLL012.";
}

TEST( GameInterfaceSplit, IsEngineThreadedIsGoneFromTheServerAndStillLivesOnTheClient )
{
	// A DELETION, not a move, and the two halves have to be asserted together
	// or the fact reads as "the server lost a function".
	//
	// The server's whole body was `if ( g_pcv_ThreadMode ) return
	// g_pcv_ThreadMode->GetBool(); return false;`, and the ONLY thing that ever
	// assigned that file static was stock's DLLInit. Without DLLInit it answers
	// false for the life of the process. It had zero consumers in the compiled
	// server set.
	//
	// The server half -- gameinterface.h not declaring it -- is retired:
	// game/server/gameinterface.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6
	// (TheHeaderRegistersNothingAtAll pins it absent).

	// The CLIENT's is untouched, and this half is what stops the deletion being
	// read as a cross-DLL regression: c_baseentity.cpp really does call it.
	const std::string cli = Code( "game/client/cdll_client_int.h" );
	ASSERT_FALSE( cli.empty() );
	EXPECT_NE( cli.find( "boolIsEngineThreaded();" ), std::string::npos )
		<< "the CLIENT's IsEngineThreaded is gone too -- that is a different "
		<< "decision and this phase did not take it.";
}

// TheRestoreTailSaysWhyItIsNotRePointedAtTheShell went with
// shell_restoreentities.cpp (plan_server_native_min P1): EndRestoreEntities
// returned on its first line forever and the whole path is deleted; there is
// no tail left to keep loud and nothing to re-point. The managed save/restore
// codec (P3c) writes its own refusal.

//=============================================================================//
// STEP 4 — the residue is rehomed and the file is deleted
//=============================================================================//

// TheRestoreTrioIsWholeAndItsStateIsFields went with the file it read
// (plan_server_native_min P1): the trio is deleted whole, not kept-whole.

TEST( GameInterfaceSplit, EveryEngineGlobalIsStillDefinedExactlyOnce )
{
	const std::string code = Code( kGlobals );
	ASSERT_FALSE( code.empty() );

	// The eight with the most consumers in the compiled server set, measured
	// with nm on gameinterface.o before the move: gpGlobals 53, engine 52,
	// enginetrace 21, modelinfo 20, staticpropmgr 20, g_pSharedChangeInfo 16,
	// random 14, filesystem 13. A definition that goes missing IS a link error,
	// but one that is quietly given internal linkage -- `static` in front of it
	// -- is not: it links, and every extern in every other file resolves to a
	// different, permanently-NULL object.
	static const char *kGlobalDefs[] = {
		"CGlobalVars*gpGlobals;",
		"IVEngineServer*engine=NULL;",
		"IEngineTrace*enginetrace=NULL;",
		"IVModelInfo*modelinfo=NULL;",
		"IStaticPropMgrServer*staticpropmgr=NULL;",
		"CSharedEdictChangeInfo*g_pSharedChangeInfo=NULL;",
		// sharedInterface.h's `random`, by its link name (the header #defines it).
		"IUniformRandomStream*random_valve=NULL;",
		"IFileSystem*filesystem=NULL;",
		"INetworkStringTableContainer*networkstringtable=NULL;",
		"ISpatialPartition*partition=NULL;",
		// `CTimedEventMgrg_NetworkPropertyEventMgr;` was the fourteenth entry
		// here. plan_shim.md Phase 1d deleted the global with
		// game/server/timedeventmgr.cpp -- nothing could ever register with it
		// -- so it is no longer one of the globals this file must define, and
		// an entry for a global that must not exist is the opposite claim.
		// "floatg_flServerCurTime=0.0f;" went the same way in
		// plan_server_native_v2 T6: its one reader is in neither DLL.
	};
	for ( size_t i = 0; i < sizeof( kGlobalDefs ) / sizeof( kGlobalDefs[ 0 ] ); i++ )
	{
		EXPECT_EQ( 1, CountOf( code, kGlobalDefs[ i ] ) )
			<< kGlobals << " does not define `" << kGlobalDefs[ i ]
			<< "` exactly once.";
		// and NOT with internal linkage, which links and answers NULL forever
		EXPECT_EQ( code.find( std::string( "static" ) + kGlobalDefs[ i ] ), std::string::npos )
			<< "`" << kGlobalDefs[ i ] << "` is static -- every extern in every "
			<< "other translation unit now resolves to a different object.";
	}

	// g_MapEntityRefs was an entry too. Its only writer, CMapLoadEntityFilter,
	// is used by no compiled TU and nothing read it, so it was deleted.
	EXPECT_EQ( code.find( "g_MapEntityRefs" ), std::string::npos )
		<< kGlobals << " defines g_MapEntityRefs again; nothing writes or reads it.";

	// g_VguiScreenStringOps was the fifteenth entry. Its one reader,
	// vguiscreen.cpp's datadesc, is not compiled into server.so, so it was
	// deleted with the Init that was its only reference -- and stays gone.
	EXPECT_EQ( code.find( "g_VguiScreenStringOps" ), std::string::npos )
		<< kGlobals << " defines g_VguiScreenStringOps again; nothing reads it.";

	// CBaseEdict's out-of-line member. The const overload went in the server gc
	// cull (2026-10-01): no server object called it.
	EXPECT_NE( code.find( "IChangeInfoAccessor*CBaseEdict::GetChangeAccessor(){" ), std::string::npos );
	EXPECT_EQ( code.find( "constIChangeInfoAccessor*CBaseEdict::GetChangeAccessor()const{" ), std::string::npos );
}

TEST( GameInterfaceSplit, AllSevenStringTablePointersCameAcross )
{
	const std::string code = Code( kGlobals );
	ASSERT_FALSE( code.empty() );

	// shell_stringtables.cpp CREATES these seven; nothing else defines the
	// pointers. One left behind is a null table that every AddString anywhere
	// in the binary walks into.
	static const char *kTables[] = {
		"g_pStringTableParticleEffectNames", "g_pStringTableEffectDispatch",
		"g_pStringTableVguiScreen", "g_pStringTableMaterials",
		"g_pStringTableInfoPanel", "g_pStringTableClientSideChoreoScenes",
		"g_pStringTableServerMapCycle",
	};
	for ( size_t i = 0; i < sizeof( kTables ) / sizeof( kTables[ 0 ] ); i++ )
	{
		EXPECT_NE( code.find( std::string( "INetworkStringTable*" ) + kTables[ i ] + "=NULL;" ),
		           std::string::npos )
			<< kTables[ i ] << " is not defined in " << kGlobals;
	}
}

TEST( GameInterfaceSplit, TheTwoConVarsWithReadersElsewhereSurvivedAndTheThirdWasDropped )
{
	const std::string code = Code( kGlobals );
	ASSERT_FALSE( code.empty() );

	// A9 step 3 deleted TWELVE convars whose only readers were stock's
	// CServerGameDLL. These three have readers in other files, so deleting them
	// would be a link error for one and a silent behaviour change for the
	// other two.
	//
	// ONE OF THEM IS DECLARED IN MANAGED CODE NOW, and one turned out to be
	// the engine's own (see below). What this test defends is
	// that the SETTING still exists somewhere, which is what "survived" meant
	// when it was written -- so the two are asserted against the managed
	// declaration, and against their ABSENCE here, which together say they
	// MOVED rather than that they were duplicated. A convar declared twice is
	// neither a compile error nor a link error.
	const std::string cs = Read( "sourcesharp/managed/SourceSharp.Server/ConVars.cs" );
	ASSERT_FALSE( cs.empty() ) << "SourceSharp.Server/ConVars.cs did not load";

	EXPECT_NE( cs.find( "[ConVar(\"ai_post_frame_navigation\", \"0\")]" ), std::string::npos )
		<< "ai_basenpc.cpp and ai_navigator.cpp read this one; its declaration "
		   "must exist even though neither .cpp is in this build.";
	EXPECT_EQ( code.find( "\"ai_post_frame_navigation\"" ), std::string::npos )
		<< "it is declared in managed code AND still here, so there are two.";

	// sv_unlockedchapters is the ENGINE's (no game-DLL flag in the live
	// table): declared in neither place, since a declaration would alias the
	// engine's own (convar_ownership.h). The setting survives as the engine's.
	EXPECT_EQ( cs.find( "[ConVar(\"sv_unlockedchapters\"" ), std::string::npos )
		<< "sv_unlockedchapters is the engine's; a managed declaration aliases it.";
	EXPECT_EQ( code.find( "\"sv_unlockedchapters\"" ), std::string::npos )
		<< "sv_unlockedchapters is the engine's; a native declaration aliases it.";

	// THE THIRD ONE IS GONE, AND THE ASSERTION ABOUT IT IS INVERTED.
	//
	// It used to read `EXPECT_NE( code.find( "ConVar*sv_maxreplay=NULL;" ) ... )`
	// with the reason "player.cpp dereferences sv_maxreplay after a NULL test".
	// That reason stopped being true before this line was written: player.cpp
	// is not on either link line, and the census over the ninja graph reports
	// 0 importers of sv_maxreplay on both sides. The claim it defended was a
	// claim about a reader that does not exist in this build.
	//
	// plan_shim.md step 5c deleted the pointer with the rest of Replay, so the
	// surviving claim is the ABSENCE -- a ratchet, not a description. It goes
	// red if anyone reintroduces the storage.
	EXPECT_EQ( code.find( "sv_maxreplay" ), std::string::npos )
		<< kGlobals << " defines sv_maxreplay again. HLTV and Replay are "
		   "dropped (plan_shim.md 5c) and nothing on either link line reads it.";
}

TEST( GameInterfaceSplit, TheChainedActivateFlagsAreGone )
{
	const std::string raw = Read( kGlobals );
	ASSERT_FALSE( raw.empty() );
	// Proof of read: the file still defines the engine's global block pointer.
	ASSERT_NE( raw.find( "CGlobalVars *gpGlobals" ), std::string::npos )
		<< kGlobals << " read as something other than the boundary file";

	// Their three users -- baseentity.cpp's CBaseEntity::Activate,
	// hl2/npc_citizen17.cpp and shell_level.cpp's activate-all-entities
	// bracketing -- are all gone from the build, and no object on server.so's
	// link imported either flag (plan_server_native_v2 T5, nm U), so the
	// definitions went too. One coming back would be a global nobody reads.
	EXPECT_EQ( raw.find( "bool g_bReceivedChainedActivate;" ), std::string::npos )
		<< "the chained-activate pair is defined in " << kGlobals << " again";
	EXPECT_EQ( raw.find( "bool g_bCheckForChainedActivate;" ), std::string::npos )
		<< "the chained-activate pair is defined in " << kGlobals << " again";
}

//=============================================================================//
// The build graph: both files are compiled.
//=============================================================================//

TEST( GameInterfaceSplit, BothNewFilesAreInTheServerProject )
{
	// server_sourcesharp's source list, read through cmake_graph.h
	// (src/server/native/CMakeLists.txt). All six are presence checks, so a
	// reader that saw nothing fails every one.
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kEnts ) )
		<< kEnts << " is not compiled by server_sourcesharp "
		<< "(src/server/native/CMakeLists.txt), so IServerGameEnts "
		<< "is not registered by anything.";
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kShared ) ) << kShared;
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kClients ) ) << kClients;
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kTags ) ) << kTags;
	// kRestore left the list with shell_restoreentities.cpp (P1).
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kGlobals ) ) << kGlobals;
}

TEST( GameInterfaceSplit, NoSecondCopyOfEitherClassIsLeftBehind )
{
	// gameinterface.cpp is deleted by step 4 of this phase. While it exists,
	// a class body left in BOTH files is a duplicate InterfaceReg for one
	// version string, which links cleanly and is resolved by static-init order.
	if ( !Exists( "game/server/gameinterface.cpp" ) )
		return;

	const std::string old = Code( "game/server/gameinterface.cpp" );
	ASSERT_FALSE( old.empty() );

	EXPECT_EQ( old.find( "classCServerGameEnts:publicIServerGameEnts" ), std::string::npos )
		<< "CServerGameEnts is declared in gameinterface.cpp again -- a copy "
		<< "rather than a move.";
	EXPECT_EQ( old.find( "classCServerDLLSharedAppSystems:publicIServerDLLSharedAppSystems" ), std::string::npos );
	EXPECT_EQ( old.find( "INTERFACEVERSION_SERVERGAMEENTS" ), std::string::npos );
	EXPECT_EQ( old.find( "SERVER_DLL_SHARED_APPSYSTEMS" ), std::string::npos );
	EXPECT_EQ( old.find( "INTERFACEVERSION_SERVERGAMECLIENTS" ), std::string::npos );
	EXPECT_EQ( old.find( "INTERFACEVERSION_SERVERGAMETAGS" ), std::string::npos );
	EXPECT_EQ( old.find( "CServerGameClients::" ), std::string::npos )
		<< "a CServerGameClients method body is back in gameinterface.cpp -- a "
		<< "second definition of a virtual is a link error, but a body that is "
		<< "the ONLY one and is in the file A9 deletes is a pure-virtual call.";
}
