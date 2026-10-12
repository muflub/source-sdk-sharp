//========= SourceSharp — the rules class answers an interface ================//
//
// CSourceSharpGameRules derives from IGameRules and nothing else. The chain was
// CMultiplayRules -> CGameRules -> IGameRules, and each step was taken for the
// same reason: the slots the base supplied were either answered here or reached
// by nothing.
//
// This file is a SOURCE test, not a runtime one, because what it checks is a
// shape: which base a class names, which members it declares, and what a build
// file lists. None of that can be observed from a running server, and all of it
// can regress silently -- re-adding a base compiles perfectly.
//
//============================================================================//

#include <string>
#include <vector>
#include <set>
#include <fstream>
#include <sstream>
#include <regex>

#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

std::string Read( const char *pszRel )
{
	// The test binary runs from the tree root's build directory; walk up until
	// the path resolves, so this does not depend on where ninja put it.
	std::string prefix;
	for ( int i = 0; i < 6; i++ )
	{
		std::ifstream f( prefix + "src/" + pszRel );
		if ( f.good() )
		{
			std::stringstream ss;
			ss << f.rdbuf();
			return ss.str();
		}
		prefix += "../";
	}
	return "";
}

// Comments removed. Every check in this file is about what the CODE says, and
// the files it reads explain themselves at length -- the header's own comments
// name "public CGameRules" and "CAutoGameSystemPerFrame" while describing what
// was removed, which is exactly the text these tests look for. Searching the
// raw file finds the explanation and reports it as the thing being explained.
std::string StripComments( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );

	for ( size_t i = 0; i < in.size(); )
	{
		if ( in[i] == '/' && i + 1 < in.size() && in[i + 1] == '/' )
		{
			while ( i < in.size() && in[i] != '\n' ) i++;
		}
		else if ( in[i] == '/' && i + 1 < in.size() && in[i + 1] == '*' )
		{
			i += 2;
			while ( i + 1 < in.size() && !( in[i] == '*' && in[i + 1] == '/' ) ) i++;
			i += 2;
		}
		else
		{
			out += in[i++];
		}
	}

	return out;
}

const char *kHeader = "sourcesharp/game/shared/shell_gamerules.h";
const char *kSource = "sourcesharp/game/shared/shell_gamerules.cpp";
const char *kIface  = "sourcesharp/game/shared/igamerules.h";
// WHERE THE RATIONALE LIVES NOW. It used to be a comment beside the $File line
// in the .vpc; the scripts were reformatted with every comment stripped out to
// this document, verbatim and keyed the same way. The convention is unchanged --
// a removed file is NAMED somewhere greppable, so a $File line deleted with no
// explanation is still distinguishable from one deleted by accident -- only the
// place it is named moved.

} // namespace

//-----------------------------------------------------------------------------
// The base.
//-----------------------------------------------------------------------------
TEST( GameRulesInterface, TheClassDerivesFromTheInterfaceAndNothingElse )
{
	const std::string h = StripComments( Read( kHeader ) );
	ASSERT_FALSE( h.empty() ) << "cannot read " << kHeader;

	EXPECT_NE( h.find( "class CSourceSharpGameRules : public IGameRules" ), std::string::npos )
		<< "CSourceSharpGameRules no longer derives from IGameRules alone. Every "
		   "base it has ever had was removed because the slots it supplied were "
		   "answered here or reached by nothing; a new one is a claim that "
		   "stopped being true.";

	// And specifically not the two it used to have. Checked by name rather than
	// inferred from the line above, because `public IGameRules, public CFoo`
	// contains that line perfectly well.
	EXPECT_EQ( h.find( "public CGameRules" ), std::string::npos )
		<< "CGameRules is a base again.";
	EXPECT_EQ( h.find( "public CMultiplayRules" ), std::string::npos )
		<< "CMultiplayRules is a base again.";
	EXPECT_EQ( h.find( "CAutoGameSystemPerFrame" ), std::string::npos )
		<< "the rules object is a game system again. It stopped being one when "
		   "the managed frame moved to CSourceSharpSystem's own hooks; putting "
		   "it back gives the frame two drivers.";
}

//-----------------------------------------------------------------------------
// Every interface member is answered.
//
// The compiler already enforces this -- an unimplemented pure virtual makes the
// class abstract and REGISTER_GAMERULES_CLASS's `new` fails. What it does NOT
// enforce is that the answer is the class's own rather than inherited, which is
// the property that disappeared with the base and the reason to state it.
//-----------------------------------------------------------------------------
TEST( GameRulesInterface, EveryInterfaceMemberIsDeclaredOnTheClass )
{
	const std::string iface = StripComments( Read( kIface ) );
	const std::string h = StripComments( Read( kHeader ) );
	ASSERT_FALSE( iface.empty() );
	ASSERT_FALSE( h.empty() );

	const std::regex pure( R"(virtual[^;]*?\b(\w+)\s*\([^;]*?\)\s*(?:const\s*)?=\s*0\s*;)" );

	std::set< std::string > names;
	for ( std::sregex_iterator it( iface.begin(), iface.end(), pure ), end; it != end; ++it )
	{
		const std::string name = ( *it )[1].str();

		// The destructor matches the same shape as a member -- `virtual
		// ~IGameRules() = 0` captures "IGameRules" -- and is not one.
		if ( name == "IGameRules" )
			continue;

		names.insert( name );
	}

	// A FLOOR, NOT A COUNT, and it was re-based at plan_shim.md §5 3e when the
	// interface lost the eighteen slots no compiled translation unit asked
	// for. It exists for one reason: if the regex above ever stops matching,
	// `names` is empty, `missing` is empty and every check below passes
	// vacuously. What it must NOT become is an assertion about how many slots
	// the interface has -- that is a number this phase is deliberately
	// reducing, and a floor written at the current value turns every
	// legitimate removal into a red test about nothing.
	//
	// 28 parse on the server arm and 13 on the client's (the #else branch is
	// two slots plus the shared seventeen). The floor is well under both.
	ASSERT_GE( names.size(), 12u )
		<< "only " << names.size() << " pure virtuals parsed out of igamerules.h; "
		   "the scan has stopped seeing them and every check below would pass "
		   "vacuously";

	std::vector< std::string > missing;
	for ( const std::string &n : names )
	{
		// The declaration on the class, in any arm.
		const std::regex decl( "\\b" + n + "\\s*\\(" );
		if ( !std::regex_search( h, decl ) )
			missing.push_back( n );
	}

	EXPECT_TRUE( missing.empty() )
		<< missing.size() << " IGameRules member(s) are not declared on "
		<< "CSourceSharpGameRules, so they are answered by something else: "
		<< [ & ]{ std::string s; for ( const auto &m : missing ) { s += m; s += " "; } return s; }();
}

//-----------------------------------------------------------------------------
// The five symbols that came with it.
//-----------------------------------------------------------------------------
TEST( GameRulesInterface, TheSymbolsGameRulesCppUsedToSupplyAreDefinedHere )
{
	// COMMENT-STRIPPED, and that is not tidiness. This fact asserts that a
	// symbol is ABSENT, and the file explains at length where it went -- and
	// the paragraph beside `ConVar skill(` quotes the managed declaration that
	// replaced it and was reverted. Prose counting as code to a raw grep has
	// bitten this port repeatedly, and it bit this very test once: read raw, it
	// found `ConVar skill(` inside the comment saying the convar had moved and
	// reported the move as not having happened. The positive entries below are
	// real code and survive the strip unchanged.
	const std::string s = StripComments( Read( kSource ) );
	ASSERT_FALSE( s.empty() );

	// Evicting gamerules.cpp left exactly these undefined on the server.
	//
	// FOUR, NOT FIVE. `bool IsExplosionTraceBlocked(` was the fifth and it is
	// gone, deleted with its one caller by the shim wave's policy-rules lane:
	// CGameRules::RadiusDamage moved to SourceSharp.Server/BlastSweep.cs, and
	// the comment over IsExplosionTraceBlocked already recorded that
	// RadiusDamage was the only thing that called it. It is not a symbol
	// something else now supplies -- it is a symbol nothing asks for, in any
	// compiled TU on either side.
	//
	// `ConVar skill(` STAYS, and plan_shim.md §5 3e re-measured WHY. It was
	// moved to C# once, the live gate caught that a managed `skill` cannot be
	// set from the console, and it came back -- and the reason written down
	// then was wrong. It is not the client's copy of stock gamerules.cpp:
	// ENGINE.SO DECLARES IT ("Game skill level (1-3)." is in the shipped
	// binary), and engine.so is loaded before either game
	// DLL. No later phase frees the name. The declaration below is therefore
	// already a duplicate of the engine's; taking it out belongs with the
	// ConVar ledger, which is what decides what `cvarlist` shows.
	//
	// `ConVar old_radius_damage(` LEFT at the same step, and its entry left
	// with it -- SourceSharp.Server.OldRadiusDamageConVar declares it now and
	// BlastSweep, its only reader, always asked for it by name.
	//
	// The list is SHORTENED rather than left with a dead entry, because an
	// entry pinning a symbol nobody asks for is a check that can only ever fail
	// for the wrong reason.
	//
	// plan_server_novalve.md P2.3b took both ConVars out: `skill` is the
	// engine's and its duplicate was deleted, and g_Language is declared in C#
	// (SourceSharp.Server.GLanguageConVar) with by-name refs at its readers.
	// Their absence is asserted below, beside IsExplosionTraceBlocked's.
	for ( const char *pszDef : { "IGameRules *g_pGameRules = NULL;" } )
	{
		EXPECT_NE( s.find( pszDef ), std::string::npos )
			<< "shell_gamerules.cpp no longer defines `" << pszDef << "`. It was "
			   "one of the symbols evicting gamerules.cpp left undefined on "
			   "the server, and nothing else defines it.";
	}

	// AND THE ONE THAT WENT IS REALLY GONE, which the loop above cannot say: a
	// list that stops naming something passes whether or not the thing is
	// there.
	EXPECT_EQ( s.find( "ConVar skill(" ), std::string::npos )
		<< "shell_gamerules.cpp declares `skill` again; the engine owns it.";
	EXPECT_EQ( s.find( "ConVar g_Language(" ), std::string::npos )
		<< "shell_gamerules.cpp declares g_Language again; C# does.";

	EXPECT_EQ( s.find( "bool IsExplosionTraceBlocked(" ), std::string::npos )
		<< "IsExplosionTraceBlocked is back in shell_gamerules.cpp. Its one "
		   "caller, CGameRules::RadiusDamage, is managed policy now "
		   "(BlastSweep.cs), so a definition here has no caller -- and three "
		   "of its four clauses were already dead before it went.";

	// THE OLD DEPENDENCY, INVERTED -- plan_shim.md §5 3e.
	//
	// This block used to assert that client_csharp.vpc STILL named stock
	// gamerules.cpp, on the theory that the client's duplicate declaration of
	// `skill` was what kept this file's copy necessary, and that the copy could
	// move to C# on the day that row left.
	//
	// IT WAS A CHECK THAT COULD NOT FAIL. It opened "src/game/client/..."
	// relative to the process's own directory rather than through Read() like
	// every other fact in this file, the stream was not good, and
	// `if ( !client.str().empty() )` skipped the whole assertion silently. The
	// row it asserted had ALREADY gone and the test went on passing.
	//
	// So it is inverted and re-pointed, and read through Read() so it cannot
	// skip: the client does NOT compile stock gamerules.cpp, and what blocks a
	// managed `skill` is engine.so's own declaration, which no phase of this
	// port removes.
	{
		// client_sourcesharp's source list, read through cmake_graph.h
		// (src/client/native/CMakeLists.txt), which throws rather than skips
		// on a missing, empty or stale input. The proof-of-read is the port's
		// own replacement for the very file whose absence is asserted.
		ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kSource ) )
			<< "client_sourcesharp does not compile " << kSource << ", so it is "
			   "not the source list this test thinks it is.";

		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/shared/gamerules.cpp" ) )
			<< "the client compiles stock gamerules.cpp again. It declares its "
			   "own ConVar skill, g_Language and old_radiusdamage, and this "
			   "file's server-only guard is what stops the duplicates -- so a "
			   "restored row is two declarations of each replicated name in one "
			   "image.";
	}
}

//-----------------------------------------------------------------------------
// ...and they are server-only, which is the part that is easy to get wrong.
//-----------------------------------------------------------------------------
TEST( GameRulesInterface, ThoseDefinitionsAreGuardedServerOnly )
{
	const std::string s = Read( kSource );
	ASSERT_FALSE( s.empty() );

	const size_t defs = s.find( "IGameRules *g_pGameRules = NULL;" );
	ASSERT_NE( defs, std::string::npos );

	// The nearest #ifndef CLIENT_DLL above the definitions, with no #endif
	// between it and them.
	const size_t guard = s.rfind( "#ifndef CLIENT_DLL", defs );
	ASSERT_NE( guard, std::string::npos )
		<< "the definitions are not inside any #ifndef CLIENT_DLL.";

	const size_t between = s.find( "#endif", guard );
	EXPECT_GT( between, defs )
		<< "an #endif closes the guard before the definitions, so they are "
		   "compiled on the client too -- and the client still compiles "
		   "gamerules.cpp, so its link finds two of each. This is not "
		   "hypothetical: the first version of this block had no guard at all "
		   "and the client link failed on exactly that.";
}

//-----------------------------------------------------------------------------
// The registry is gone, and the wire contract it carried is not.
//-----------------------------------------------------------------------------
TEST( GameRulesInterface, TheRulesObjectIsCreatedDirectlyWithNoRegistry )
{
	const std::string src = StripComments( Read( kSource ) );
	ASSERT_FALSE( src.empty() );

	EXPECT_EQ( src.find( "REGISTER_GAMERULES_CLASS" ), std::string::npos )
		<< "the rules class registers itself in CGameRulesRegister's list again. "
		   "That list is a name->factory map for a build holding several rules "
		   "classes; this mod has one, so the lookup could only return it.";

	EXPECT_EQ( src.find( "CGameRulesRegister" ), std::string::npos )
		<< "the registry type is referenced again.";

	// Constructed directly instead, on BOTH sides -- the server when the level
	// installs rules, the client when the string table tells it to.
	//
	// COUNTED, not merely found. There are two constructions and they are in
	// opposite #ifdef arms, so searching for one hit lets either side lose its
	// construction while the other keeps the test green. Mutating away the
	// server's is exactly what got through the first version of this.
	size_t built = 0;
	for ( size_t at = src.find( "new CSourceSharpGameRules()" );
	      at != std::string::npos;
	      at = src.find( "new CSourceSharpGameRules()", at + 1 ) )
	{
		built++;
	}

	EXPECT_EQ( built, 2u )
		<< "found " << built << " construction(s) of the rules object, want 2 -- "
		   "one per side. The server builds it when the level installs rules and "
		   "the client when the string table tells it to; a side with none has "
		   "a NULL g_pGameRules and dereferences it everywhere.";
}

TEST( GameRulesInterface, TheStringTableContractSurvivedTheRegistry )
{
	const std::string src = StripComments( Read( kSource ) );
	ASSERT_FALSE( src.empty() );

	// The registry was machinery; this is a WIRE CONTRACT. It is the only thing
	// that ever constructs the client's rules object, and without it
	// g_pGameRules is NULL on the client where cdll_client_int.cpp,
	// cam_thirdperson.cpp, voice_status.cpp and c_world.cpp dereference it.
	// The quoted LITERAL, not the bare word. `OnGameRulesCreationStringChanged`
	// -- the callback's own name -- contains "GameRulesCreation" as a substring,
	// so searching for the word stays green when the table is renamed to
	// something the server and client no longer agree on. That is not
	// hypothetical: it is what the first version of this check did.
	EXPECT_NE( src.find( "\"GameRulesCreation\"" ), std::string::npos )
		<< "the GameRulesCreation string table is gone or renamed. The name is a "
		   "wire contract -- the server creates the table under it and the client "
		   "finds it by it -- so the two must spell it identically. If they do "
		   "not, the client is never told to build its rules object and every "
		   "g_pGameRules-> on that side is a null dereference.";

	EXPECT_NE( src.find( "SetStringChangedCallback" ), std::string::npos )
		<< "the client no longer listens for the table change, so it never "
		   "creates its rules object.";

	EXPECT_NE( src.find( "AddString" ), std::string::npos )
		<< "the server no longer writes the table, so the client's callback "
		   "never fires.";
}

TEST( GameRulesInterface, GameRulesRegisterCppIsInNeitherBuild )
{
	// Both sides' source lists, read through cmake_graph.h: server_sourcesharp
	// (src/server/native/CMakeLists.txt) and client_sourcesharp
	// (src/client/native/CMakeLists.txt).
	//
	// Positive control first: an absence assertion over a list that could not
	// be read passes for the wrong reason.
	//
	// THE CONTROL MOVED, AND THE ABSENCE IT GUARDS DID NOT. It used to be
	// `$SRCDIR\game\shared\gamerules.cpp`, and THE FLIP TOOK THAT ROW OUT OF
	// client_csharp.vpc -- at which point the control was reporting exactly what
	// it warns about, an unreadable file list, for a file list that reads fine.
	// A stock TU is not a control for this port; removing stock TUs is what the
	// port does. The control is now the PORT'S OWN replacement for that very
	// file, sourcesharp/game/shared/shell_gamerules.cpp, which is the file whose
	// direct construction the absence below protects -- so it cannot leave the
	// build without this test having no subject at all.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kSource ) )
		<< "server_sourcesharp does not compile the port's own " << kSource
		<< ", so it is not the source list this test thinks it is.";
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kSource ) )
		<< "client_sourcesharp does not compile the port's own " << kSource
		<< ", so it is not the source list this test thinks it is.";

	// By the path's TAIL, as the old client needle was: a copy under any
	// directory counts.
	const std::string strTail = "/gamerules_register.cpp";
	const char *kTargets[] = { cmake_graph::kServer, cmake_graph::kClient };
	for ( size_t t = 0; t < 2; t++ )
	{
		const std::set< std::string > src = cmake_graph::CompiledSources( kTargets[ t ] );
		for ( std::set< std::string >::const_iterator it = src.begin(); it != src.end(); ++it )
		{
			EXPECT_FALSE( it->size() >= strTail.size()
				&& it->compare( it->size() - strTail.size(), strTail.size(), strTail ) == 0 )
				<< kTargets[ t ] << " compiles " << *it << " again, and its half "
				   "collides with the direct construction in shell_gamerules.cpp.";
		}
	}
}

//-----------------------------------------------------------------------------
// THE EIGHTEEN SLOTS NO COMPILED TRANSLATION UNIT ASKS FOR — plan_shim.md §5 3e.
//
// IGameRules' own header says what it is: "every question the build asks the
// game rules, and nothing else". Eighteen of its forty-seven members were not
// questions the build asked. Each was inherited from stock's CGameRules when
// that base was dropped, and the call sites that would have reached them are in
// files that are in neither build -- game/server/client.cpp for the chat members,
// game/shared/baseentity_shared.cpp for GetAmmoDamage, gamerules.cpp:1244 for
// SaveConvar.
//
// HOW THE LIST WAS CHOSEN, because a virtual call emits no symbol and
// tools/shim-census therefore cannot answer this one. Two scans over the
// SOURCES of every object on the ninja link line: the first required the
// receiver to be spelled one of the known rules-pointer names, the second
// dropped that entirely and matched any `Slot(` at all, over-counting on
// purpose so that it could not MISS. The second found five sites the first had
// not, and three of them were real (CreateCustomNetworkStringTables,
// Damage_GetNoPhysicsForce, and an unrelated engine->ClientCommand) -- which is
// why the list is eighteen and not the twenty the first scan alone produced.
//
// WHY A SOURCE SCAN AND NOT JUST THE COMPILER. The compiler proved the removal:
// both DLLs link. What it cannot prove is that they STAY removed. Re-adding a
// pure virtual to IGameRules and an empty body to the class compiles, links and
// runs, and leaves a member that reads live and is asked by nothing -- which is
// exactly what these eighteen were.
//-----------------------------------------------------------------------------
TEST( GameRulesInterface, TheSlotsWithNoCallerAreNotBack )
{
	const std::string iface = StripComments( Read( kIface ) );
	const std::string h = StripComments( Read( kHeader ) );
	ASSERT_FALSE( iface.empty() );
	ASSERT_FALSE( h.empty() );

	static const char *const kGone[] =
	{
		"GetAmmoDamage", "GetEncryptionKey", "GetGameType",
		"IsManualMapChangeOkay", "SaveConvar", "AllowDamage", "CheckChatText",
		"ClientCommand", "ClientSettingsChanged", "FlPlayerFallDamage",
		"FlPlayerFallDeathDoesScreenFade", "GetAutoAimMode", "GetChatFormat",
		"GetChatLocation", "GetChatPrefix", "IsTeamplay", "PlayerCanHearChat",
		"WeaponTraceEntity",
	};

	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		const std::regex decl( std::string( "\\b" ) + kGone[ i ] + "\\s*\\(" );
		EXPECT_FALSE( std::regex_search( iface, decl ) )
			<< kGone[ i ] << " is declared on IGameRules again. No translation "
			   "unit this build compiles asks the rules object for it, so what "
			   "it adds is a vtable slot and a body that read live and answer "
			   "nobody.";
		EXPECT_FALSE( std::regex_search( h, decl ) )
			<< kGone[ i ] << " is declared on CSourceSharpGameRules again.";
	}
}

//-----------------------------------------------------------------------------
// THE TAGGED-CONVAR MECHANISM WENT AS A WHOLE, which is the part of the removal
// most likely to be half-done.
//
// Three methods and a vector. SaveConvar was the only WRITER, and the only
// caller it ever had was gamerules.cpp:1244. Removing the writer and keeping
// HasSavedConvar, RevertSavedConvars and m_SavedConvars would leave a mechanism
// that a reader would take for live: the destructor still calling
// RevertSavedConvars(), the vector still declared, and every convar the rules
// "changed" still restored -- over a list that can no longer be added to.
//-----------------------------------------------------------------------------
TEST( GameRulesInterface, TheSavedConvarMechanismLeftWholeRatherThanHalfway )
{
	const std::string h = StripComments( Read( kHeader ) );
	const std::string cpp = StripComments( Read( kSource ) );
	ASSERT_FALSE( h.empty() );
	ASSERT_FALSE( cpp.empty() );

	static const char *const kParts[] =
	{
		"SaveConvar", "HasSavedConvar", "RevertSavedConvars", "m_SavedConvars",
	};

	for ( size_t i = 0; i < sizeof( kParts ) / sizeof( kParts[ 0 ] ); i++ )
	{
		EXPECT_EQ( h.find( kParts[ i ] ), std::string::npos )
			<< kParts[ i ] << " is back in shell_gamerules.h.";
		EXPECT_EQ( cpp.find( kParts[ i ] ), std::string::npos )
			<< kParts[ i ] << " is back in shell_gamerules.cpp. The one writer "
			   "of this list had no caller, so anything left of the mechanism "
			   "reverts a list that cannot be added to.";
	}
}
