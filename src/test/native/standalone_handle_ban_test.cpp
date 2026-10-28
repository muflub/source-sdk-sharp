//========= SourceSharp — the typed handle over the shell cannot be written ==//
//
// plan_entity.md Phase 1 step 1e's gate has two halves:
//
//   "the census reads zero CHandle< / EHANDLE in compiled server shim TUs"
//        -- a TEXT measurement, taken by tools/entity-census and pinned in
//           docs/entity-census.txt. It says the typed handles are gone TODAY.
//
//   "a compile-must-fail probe that instantiating CHandle<CStandaloneEntity>
//    in a shim TU is refused"
//        -- a BUILD property. It says they cannot come back.
//
// This file is the second half, and the reason it is a compiled program rather
// than a gtest assertion is the reason every must-not-compile leg here exists:
// nothing you can write inside a passing test says "this type cannot be
// instantiated". An assertion that names the banned type does not compile
// either, and an assertion that names anything else passes whether the ban is
// present or absent — a check that cannot fail.
//
// SO THE PROBE IS TWO LEGS AND NEITHER IS SUFFICIENT ALONE. src/buildcpptests
// compiles this ONE source twice:
//
//   standalone_handle_ban        -DSTANDALONE_HANDLE_BAN_NOCOMPILE=1
//                                must NOT compile, at "incomplete type"
//   standalone_handle_ban_absent -DSTANDALONE_HANDLE_BAN_NOCOMPILE=1
//                                -DSTANDALONE_HANDLE_BAN_ABSENT=1
//                                must compile AND RUN
//
// The second leg is the control and it is not decoration: a refusal on its own
// is evidence of nothing, because a typo refuses just as loudly and at a
// message nobody reads. The two legs differ by exactly one #include, so the
// refusal is attributable to the ban and to nothing else.
//
// ENGINE-FREE, like every leg in this file's neighbourhood: the real
// game/shared/ehandle.h and public/basehandle.h, reached through the one-TU
// cbase.h in sourcesharp/test/entitylist_stub. It is the REAL CHandle that is
// banned here, not a stand-in for it — a stand-in would drift from the
// template it stands for silently, which is the failure
// docs/… fixture-must-be-checked-against-native records.
//
//=============================================================================

#include <stdlib.h>
#include <string>
#include <fstream>
#include <sstream>

#include "gtest/gtest.h"

//-----------------------------------------------------------------------------
// The subject.
//
// CStandaloneEntity is DECLARED and never defined here, exactly as the ban
// header declares it. That is deliberate: it makes the point that the ban is
// about the HANDLE and not about the shell. An incomplete T is a perfectly
// legal argument to CHandle<T> — stock instantiates CHandle over forward
// declarations all over the tree — so the refusal below cannot be blamed on
// the shell being incomplete. Remove the ban include and the same source
// compiles, which is what the control leg measures.
//-----------------------------------------------------------------------------
class CStandaloneEntity;

#include "basehandle.h"
#include "ehandle.h"

#if !defined( STANDALONE_HANDLE_BAN_ABSENT )
#include "standalone_handle_ban.h"
#endif

#if defined( STANDALONE_HANDLE_BAN_NOCOMPILE )

//-----------------------------------------------------------------------------
// THE ILL-FORMED PROGRAM. One object of the banned type.
//
// With the ban included this is
//     error: aggregate 'CHandle<CStandaloneEntity> g_hShell' has incomplete
//     type and cannot be defined
// and with STANDALONE_HANDLE_BAN_ABSENT it is an ordinary object that compiles
// and runs.
//-----------------------------------------------------------------------------
CHandle< CStandaloneEntity > g_hShell;

TEST( StandaloneHandleBan, TheControlLegCompilesTheBannedTypeWhenTheBanIsAbsent )
{
	// Reached only by the control leg. It exists so that leg RUNS something
	// rather than merely linking: a control that compiles an unused object
	// would still pass if the compiler folded it away.
	EXPECT_FALSE( g_hShell.IsValid() )
		<< "a default-constructed handle is invalid; if this fails, the control "
		   "leg is not exercising CHandle at all";
}

#else // !STANDALONE_HANDLE_BAN_NOCOMPILE

//-----------------------------------------------------------------------------
// WHAT THE BAN DELIBERATELY STILL ALLOWS.
//
// An incomplete type can be named through a pointer or a reference, and this
// header does not try to stop that — the language offers no way to, and there
// would be nothing to gain: nothing can dereference the pointer, call a member
// or take a sizeof. Pinned so that a later, wider ban (one that broke this)
// would be a deliberate change rather than a silent one.
//-----------------------------------------------------------------------------
TEST( StandaloneHandleBan, APointerToTheBannedTypeIsStillNameable )
{
	CHandle< CStandaloneEntity > *pBanned = 0;

	EXPECT_EQ( (void *)0, (void *)pBanned );
}

//-----------------------------------------------------------------------------
// The ban is a DECLARATION, never a definition. `template<> class
// CHandle<CStandaloneEntity> {};` would be a defined specialization — an empty
// class you CAN instantiate — and every probe above would go green while the
// property they stand for was gone.
//-----------------------------------------------------------------------------
static std::string ReadFile( const char *pszPath )
{
	std::ifstream in( pszPath );
	std::stringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

// The native tests' manifest is this repo's src/test/native/CMakeLists.txt,
// which the farm does not carry; ctest names the checkout in SOURCESHARP_REPO.
// Unset reads as empty, which the caller asserts against.
static std::string ReadManifest()
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	if ( !pszRepo || !*pszRepo )
		return std::string();
	return ReadFile( ( std::string( pszRepo ) + "/src/test/native/CMakeLists.txt" ).c_str() );
}

//-----------------------------------------------------------------------------
// The ban is REACHED, and it is reached from the header that makes the shell
// nameable.
//
// A ban in a file nothing includes is a ban on nothing. shell_standalone.h is
// the placement that cannot be worked around: a TU that can spell
// CStandaloneEntity has read that header, and therefore this one.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// BOTH LEGS EXIST IN THE TEST MANIFEST (src/test/native/CMakeLists.txt; the
// bash script buildcpptests until Phase 6b), and the control is the one that
// would be quietly dropped.
//
// Deleting the must-not-compile leg is loud -- a probe that is gone stops
// printing. Deleting the CONTROL is silent: the refusal still happens, still
// matches, and nothing left says WHY it happens. So both names are pinned.
//-----------------------------------------------------------------------------
TEST( StandaloneHandleBan, TheBuildScriptCarriesBothLegs )
{
	const std::string s = ReadManifest();

	ASSERT_FALSE( s.empty() ) << "src/test/native/CMakeLists.txt is missing or "
		"SOURCESHARP_REPO is unset (ctest sets it)";

	// At the start of a line and up to the end of the name: see the client
	// twin of this fact for the mutation that made both anchors necessary.
	EXPECT_NE( std::string::npos,
	           s.find( "\nss_must_not_compile(standalone_handle_ban\n" ) )
		<< "the refusal leg is gone; nothing checks that the ban bans anything.";

	EXPECT_NE( std::string::npos,
	           s.find( "\nss_cpptest(standalone_handle_ban_absent\n" ) )
		<< "the CONTROL leg is gone. Without it the refusal is evidence of "
		   "nothing -- a typo in the probe refuses just as loudly.";
}

#endif // STANDALONE_HANDLE_BAN_NOCOMPILE
