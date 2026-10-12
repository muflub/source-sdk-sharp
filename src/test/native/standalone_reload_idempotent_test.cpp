//========= SourceSharp — the registration survives a managed reload ==========//
//
// THE BUG. `cs_sv_reload` tears down the managed load context and brings it
// back, and C# re-declares every entity class on the way back up. The native
// factory dictionary is not part of that: it lives in the DLL and outlives the
// CLR. So the second declaration met a name that was already installed, and
// `CEntityFactoryDictionary::InstallFactory` (src/game/server/util.cpp:178)
// opens with
//
//     Assert( FindFactory( pClassName ) == NULL );
//
// MEASURED, on dm_lockdown at 182ad9c8: one cs_sv_reload -> 284 assertion
// lines, against exactly 284 classes in `./debug-api classes`. Reproduced
// identically by Phases 2, 9, A6 and A8 of four different plans, carried every
// time, and it is not cosmetic even with -assertlog: `CUtlDict::Insert` does
// not reject a duplicate key, so every reload also stacked another
// CStandaloneScriptedClass under a name whose old factory nothing removed.
//
// WHY THE FIX IS "DO NOT INSTALL TWICE" RATHER THAN "UNINSTALL FIRST".
// IEntityFactoryDictionary declares InstallFactory, Create, Destroy and
// GetCannonicalName. There is NO remove of any kind in the interface, and the
// dictionary that implements it is a class local to util.cpp -- a stock file
// this port is trying to stop compiling, not to extend. Registration is made
// idempotent by classname instead: the second declaration finds the factory it
// installed the first time and RE-DESCRIBES it, so a developer who added an
// output and typed cs_sv_reload still gets the new declaration.
//
// WHY THIS TEST IS A SOURCE READ. The function it is about needs cbase.h, the
// engine's entity factory and a running CLR; there is no unit harness in this
// tree that can call it. So this pins the SHAPE, and the behaviour is pinned
// live -- tools/live-gate now runs a cs_sv_reload and fails on any assertion
// after it. Neither one is sufficient alone and both are cheap.
//
// The same idiom as world_shim_test.cpp, including stripping comments before
// scanning: this file's subject carries paragraphs that spell the very strings
// asserted about, and a scan that could not tell prose from code would force
// the explanations out of the source.
//
//=============================================================================//

#include <fstream>
#include <string>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

const char *kScripted = "sourcesharp/game/server/shell_standalone_class.cpp";

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

std::string ReadRaw( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line; all += '\n';
	}
	return all;
}

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false; i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
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

int CountOf( const std::string &hay, const std::string &needle )
{
	if ( needle.empty() )
		return 0;

	int n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + needle.size() ) )
	{
		n++;
	}
	return n;
}

std::string Scripted()
{
	return Tight( StripComments( ReadRaw( kScripted ) ) );
}

} // namespace

//=============================================================================//
// 0. THE SUBJECT IS REACHABLE.
//
// First, because every assertion below is a substring search and a search over
// an empty string agrees with everything asked of it in the negative and
// nothing in the positive. A file this test cannot open must fail here, by
// name, and not read as nine quiet passes.
//=============================================================================//

TEST( StandaloneReload, TheSourcesAreReadable )
{
	ASSERT_NE( (const char *)NULL, SrcRoot() )
		<< "no src/ root found from the working directory; this test reads sources";

	const std::string scripted = Scripted();
	ASSERT_FALSE( scripted.empty() ) << kScripted << " is empty or unreadable";

	// The function this whole file is about, by its DEFINITION and not by a
	// prefix of it: `bool SourceSharp_InstallStandaloneClass(constchar*` cannot
	// match the declaration in the header, which is what an anchor on the bare
	// name would have done.
	ASSERT_EQ( 1, CountOf( scripted, "boolSourceSharp_InstallStandaloneClass(constchar*pClassName" ) )
		<< "the definition of SourceSharp_InstallStandaloneClass is not in "
		<< kScripted << " any more; this test is interrogating the wrong file";
}

//=============================================================================//
// 1. THE ASSERTION THIS IS ABOUT IS NO LONGER COMPILED -- AND THE FIX STANDS.
//
// This section used to read: "the assertion is still in the stock file", with
// its own note that "if that assertion goes away -- somebody edits the stock
// file, or the file finally leaves the build -- the reason for the shape goes
// with it and this test should say so rather than go on defending a shape
// nothing requires."
//
// It has gone away. Lane sub-factory moved `EntityFactoryDictionary()` into the
// shim, and `CEntityFactoryDictionary` -- the class, the accessor and the two
// console commands over it -- is now inside `#ifndef SOURCESHARP` in util.cpp.
// VERIFIED IN THE BINARY, not inferred: `nm -C --defined-only
// game/mod_sharp/bin/linux64/server.so | grep -c 'CEntityFactoryDictionary::'`
// answers 0, where it answered 14 before the move.
//
// THAT IS EXACTLY THE SHAPE THIS FILE EXISTS TO CATCH, TURNED ON ITSELF. A
// source-text scanner over a stock file cannot tell a compiled line from a
// guarded one, so the old fact went on passing about text nothing builds --
// green, and about nothing. It is replaced rather than deleted, because the
// question it was asking is still worth asking: is the shape of the idempotence
// fix still required by what the build actually compiles?
//
// It is, and MORE than before. The shim dictionary answers a re-install instead
// of asserting (entity_factory_table.h, kEntityFactoryAlreadyInstalled), so the
// 284 assertion lines cannot come back from that direction -- but the interface
// still has no uninstall, the factory objects still outlive the CLR, and
// `Describe`-instead-of-reinstall is still what keeps a reloaded class's
// declaration current. Sections 2 and 3 below pin that, unchanged.
//=============================================================================//

TEST( StandaloneReload, TheLiveDictionaryIsTheShimsAndAnswersAReinstall )
{
	const std::string s = Scripted();
	ASSERT_FALSE( s.empty() );

	// The accessor the whole build resolves to, in the shim.
	// ss:: since P5 lane E: the interface is server.so's own (ss_entityfactory.h).
	EXPECT_EQ( 1, CountOf( s, "ss::IEntityFactoryDictionary*ss::EntityFactoryDictionary()" ) )
		<< "shell_standalone_class.cpp no longer defines EntityFactoryDictionary(). "
		   "Either it moved again, or util.cpp's copy is back";

	// And it answers a duplicate install instead of asserting through it, which
	// is what makes the 284 lines impossible from the dictionary's side as well
	// as from the registration's.
	EXPECT_EQ( 1, CountOf( s, "casekEntityFactoryAlreadyInstalled:" ) )
		<< "the shim dictionary no longer has an arm for a factory that is "
		   "already installed under the same name";

	EXPECT_EQ( 1, CountOf( s, "casekEntityFactoryNameTaken:" ) )
		<< "the shim dictionary no longer has an arm for a DIFFERENT factory "
		   "under a name that is taken. Stock asserted and then inserted "
		   "anyway, leaving two entries under one name";
}

TEST( StandaloneReload, ThereIsStillNothingToUninstallWith )
{
	// The interface is ss_entityfactory.h's since P5 lane E (util.h's copy is
	// no longer what server.so implements); proved read by its install.
	const std::string util = Tight( StripComments( ReadRaw( "sourcesharp/game/server/ss_entityfactory.h" ) ) );
	ASSERT_FALSE( util.empty() ) << "ss_entityfactory.h is empty or unreadable";
	ASSERT_EQ( 1, CountOf( util, "virtualvoidInstallFactory(" ) )
		<< "ss_entityfactory.h no longer declares InstallFactory; not the interface this is about";

	// The other half of why the fix lives on our side of the boundary, and it
	// is asked of the INTERFACE now rather than of the stock implementation --
	// which is where it always belonged: the interface is what both
	// dictionaries have to satisfy.
	EXPECT_EQ( 0, CountOf( util, "UninstallFactory" ) )
		<< "IEntityFactoryDictionary grew an uninstall. Draining the table on "
		   "managed unload is now possible and is the better fix";
}

//=============================================================================//
// 2. REGISTRATION IS IDEMPOTENT BY CLASSNAME.
//=============================================================================//

TEST( StandaloneReload, AnAlreadyInstalledClassIsRedescribedNotReinstalled )
{
	const std::string s = Scripted();
	ASSERT_FALSE( s.empty() );

	EXPECT_EQ( 1, CountOf( s, "if(CStandaloneScriptedClass*pExisting=FindStandaloneClass(pClassName))" ) )
		<< "SourceSharp_InstallStandaloneClass no longer asks whether the class "
		   "is already installed. One cs_sv_reload fires "
		   "Assert(FindFactory(pClassName)==NULL) once per declared class -- 284 "
		   "of them, measured";

	EXPECT_EQ( 1, CountOf( s, "pExisting->Describe(pClassName,ppOutputNames,nOutputCount,bServerOnly," ) )
		<< "an already-installed class is no longer re-described, so a class "
		   "edited in C# and reloaded keeps the declaration it was born with";
}

TEST( StandaloneReload, ThereIsExactlyOneInstallFactoryCallAndTheGuardIsAboveIt )
{
	const std::string s = Scripted();
	ASSERT_FALSE( s.empty() );

	// One install site. Two would be two ways to reach the assertion and only
	// one of them guarded.
	EXPECT_EQ( 1, CountOf( s, "EntityFactoryDictionary()->InstallFactory(" ) )
		<< "there is more than one InstallFactory call in this file";

	// ORDER, not just presence: a guard placed after the install is a guard
	// that never runs first, and both strings would still be "there".
	const size_t guard   = s.find( "if(CStandaloneScriptedClass*pExisting=FindStandaloneClass(pClassName))" );
	const size_t install = s.find( "EntityFactoryDictionary()->InstallFactory(" );
	ASSERT_NE( std::string::npos, guard );
	ASSERT_NE( std::string::npos, install );
	EXPECT_LT( guard, install )
		<< "the already-installed guard sits BELOW the InstallFactory call, so "
		   "the install happens first and the assertion fires anyway";
}

//=============================================================================//
// 3. WHAT THE RE-DESCRIPTION MUST AND MUST NOT MOVE.
//
// The description is overwritten in place, and it has to be: every entity of
// the class that is standing when C# comes back holds a POINTER to this very
// StandaloneClassDesc_t (CStandaloneEntity::m_pClass, set in PostConstruct).
// Two consequences are load-bearing and neither is obvious from the code.
//=============================================================================//

TEST( StandaloneReload, TheClassnameIsClaimedOnceAndNeverRepointed )
{
	const std::string s = Scripted();
	ASSERT_FALSE( s.empty() );

	EXPECT_EQ( 1, CountOf( s, "if(!m_Desc.pClassName)m_Desc.pClassName=Keep(pClassName);" ) )
		<< "the classname is copied again on every re-description. "
		   "InstallFactory keyed the dictionary on the FIRST pointer and "
		   "GetClassName would start answering a second one -- same characters, "
		   "different addresses, and an hour to work out why";
}

TEST( StandaloneReload, TheRegistryHoldsFactoriesRatherThanASecondCopyOfTheNames )
{
	const std::string s = Scripted();
	ASSERT_FALSE( s.empty() );

	EXPECT_EQ( 1, CountOf( s, "std::unordered_map<std::string,CStandaloneScriptedClass*,ss::CaselessHash,ss::CaselessEqual>g_StandaloneClassNames;" ) )
		<< "the standalone registry is not a vector of factories. It was a "
		   "CUtlVector<char*> of strdup'd names, which grew by one leaked string "
		   "per class per reload and could not answer WHICH factory to "
		   "re-describe";

	EXPECT_EQ( 0, CountOf( s, "g_StandaloneClassNames.emplace(Keep(pClassName)" ) )
		<< "a name is still being strdup'd into the registry on every "
		   "registration, so every reload leaks one per declared class";
}

//=============================================================================//
// 4. THE TIER LOOKUP IS RE-ARMED BY A RE-DESCRIPTION.
//
// m_Desc.iTier caches the answer from the FIRST Create. A reload can move a
// class to a different tier -- that is what a reload is for -- and a cache that
// survived it would allocate every later entity of that class at the old
// length. -1 is the "not looked up yet" value ResolveTier tests for.
//=============================================================================//

TEST( StandaloneReload, ARedescriptionRearmsTheTierLookup )
{
	const std::string s = Scripted();
	ASSERT_FALSE( s.empty() );

	// In Describe, which both the constructor and the reload path run.
	EXPECT_EQ( 1, CountOf( s, "m_Desc.iTier=-1;" ) )
		<< "the tier cache is not reset by a re-description, so a class whose "
		   "layout moved across a reload keeps allocating at its old length";

	EXPECT_EQ( 1, CountOf( s, "if(m_Desc.iTier>=0)returnm_Desc.iTier;" ) )
		<< "ResolveTier no longer tests the cache against -1, so the value "
		   "Describe writes has stopped meaning \"not looked up yet\"";
}
