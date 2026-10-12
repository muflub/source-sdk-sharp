//========= SourceSharp — the typed handle over the CLIENT shell cannot be ===//
//========= written =========================================================//
//
// plan_entity.md Phase 5 step 5d's gate is 1e's, client side, and it has the
// same two halves:
//
//   "the census reads zero CHandle< / EHANDLE in compiled client shim TUs"
//        -- a TEXT measurement, taken by tools/entity-census over the client
//           column of its object table and pinned in docs/entity-census.txt.
//           It says the typed handles are gone TODAY.
//
//   "a compile-must-fail probe that instantiating CHandle<C_StandaloneEntity>
//    in a shim TU is refused"
//        -- a BUILD property. It says they cannot come back.
//
// This file is the second half. Nothing you can write inside a PASSING test
// says "this type cannot be instantiated": an assertion that names the banned
// type does not compile either, and an assertion that names anything else
// passes whether the ban is present or absent -- the check that cannot fail.
//
// SO THE PROBE IS TWO LEGS AND NEITHER IS SUFFICIENT ALONE. src/buildcpptests
// compiles this ONE source twice:
//
//   standalone_handle_ban_client        -DSTANDALONE_HANDLE_BAN_CLIENT_NOCOMPILE=1
//                                       must NOT compile, at "incomplete type"
//   standalone_handle_ban_client_absent -DSTANDALONE_HANDLE_BAN_CLIENT_NOCOMPILE=1
//                                       -DSTANDALONE_HANDLE_BAN_CLIENT_ABSENT=1
//                                       must compile AND RUN
//
// The control is not decoration and it is the leg that would be dropped
// silently: deleting the refusal leg stops something printing, deleting the
// control breaks nothing that prints while leaving the refusal attributable to
// anything at all. The two legs differ by exactly one #include.
//
// ENGINE-FREE: the real game/shared/ehandle.h and public/basehandle.h, reached
// through the one-TU cbase.h in sourcesharp/test/entitylist_stub, the same
// route 1e's server twin and slot_table_test take. It is the REAL CHandle that
// is banned here rather than a stand-in that would drift from it silently.
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
// C_StandaloneEntity is DECLARED and never defined here, exactly as the ban
// header declares it, and that is deliberate: it makes the point that the ban
// is about the HANDLE and not about the shell. An incomplete T is a perfectly
// legal argument to CHandle<T> -- stock instantiates CHandle over forward
// declarations all over the client -- so the refusal below cannot be blamed on
// the shell being incomplete. Remove the ban include and the same source
// compiles, which is exactly what the control leg measures.
//-----------------------------------------------------------------------------
class C_StandaloneEntity;

#include "basehandle.h"
#include "ehandle.h"

#if !defined( STANDALONE_HANDLE_BAN_CLIENT_ABSENT )
#include "standalone_handle_ban_client.h"
#endif

#if defined( STANDALONE_HANDLE_BAN_CLIENT_NOCOMPILE )

//-----------------------------------------------------------------------------
// THE ILL-FORMED PROGRAM. One object of the banned type.
//
// With the ban included this is
//     error: aggregate 'CHandle<C_StandaloneEntity> g_hClientShell' has
//     incomplete type and cannot be defined
// and with STANDALONE_HANDLE_BAN_CLIENT_ABSENT it is an ordinary object that
// compiles and runs.
//-----------------------------------------------------------------------------
CHandle< C_StandaloneEntity > g_hClientShell;

TEST( StandaloneHandleBanClient, TheControlLegCompilesTheBannedTypeWhenTheBanIsAbsent )
{
	// Reached only by the control leg. It exists so that leg RUNS something
	// rather than merely linking: a control that compiled an unused object
	// would still pass if the compiler folded it away.
	EXPECT_FALSE( g_hClientShell.IsValid() )
		<< "a default-constructed handle is invalid; if this fails, the control "
		   "leg is not exercising CHandle at all";
}

#else // !STANDALONE_HANDLE_BAN_CLIENT_NOCOMPILE

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
// WHAT THE BAN DELIBERATELY STILL ALLOWS.
//
// An incomplete type can be named through a pointer or a reference, and this
// header does not try to stop that -- the language offers no way to, and there
// would be nothing to gain: nothing can dereference the pointer, call a member
// or take a sizeof. Pinned so a later, wider ban that broke this would be a
// deliberate change rather than a silent one.
//-----------------------------------------------------------------------------
TEST( StandaloneHandleBanClient, APointerToTheBannedTypeIsStillNameable )
{
	CHandle< C_StandaloneEntity > *pBanned = 0;

	EXPECT_EQ( (void *)0, (void *)pBanned );
}

//-----------------------------------------------------------------------------
// The ban is a DECLARATION, never a definition. `template<> class
// CHandle<C_StandaloneEntity> {};` would be a defined specialization -- an
// empty class you CAN instantiate -- and the must-not-compile leg would go
// green while the property it stands for was gone.
//-----------------------------------------------------------------------------
TEST( StandaloneHandleBanClient, TheSpecialisationIsDeclaredAndNotDefined )
{
	const std::string h = ReadFile( "sourcesharp/game/client/standalone_handle_ban_client.h" );

	ASSERT_FALSE( h.empty() ) << "standalone_handle_ban_client.h is missing";

	EXPECT_NE( std::string::npos,
	           h.find( "template<> class CHandle< C_StandaloneEntity >;" ) )
		<< "the ban is gone or respelled. plan_entity.md 5d's gate is that "
		   "instantiating CHandle<C_StandaloneEntity> in a client shim TU is "
		   "REFUSED, and this declaration is the whole of the refusal.";

	EXPECT_EQ( std::string::npos,
	           h.find( "template<> class CHandle< C_StandaloneEntity >\n{" ) )
		<< "the ban has been given a BODY. A defined specialisation is a class "
		   "you can instantiate, so the must-not-compile leg would go green "
		   "while the property it stands for was gone.";
}

//-----------------------------------------------------------------------------
// The ban is REACHED, and from the header that makes the client shell
// nameable. A ban in a file nothing includes is a ban on nothing.
//-----------------------------------------------------------------------------
TEST( StandaloneHandleBanClient, ShellStandaloneClientIncludesTheBan )
{
	const std::string h = ReadFile( "sourcesharp/game/client/shell_standalone_client.h" );

	ASSERT_FALSE( h.empty() ) << "shell_standalone_client.h is missing";

	EXPECT_NE( std::string::npos,
	           h.find( "#include \"standalone_handle_ban_client.h\"" ) )
		<< "shell_standalone_client.h no longer includes the ban, so a client "
		   "shim TU can name C_StandaloneEntity without ever having read it "
		   "and 5d's gate is unenforced.";
}

//-----------------------------------------------------------------------------
// IT IS THE CLIENT'S OWN BAN, not the server's re-read.
//
// The two shells are two classes and an explicit specialization names the type
// it specializes, so a single shared header cannot ban both. This fact would go
// red if somebody "de-duplicated" the two headers into one -- which would leave
// one of the two sides unbanned while every probe stayed green.
//-----------------------------------------------------------------------------
TEST( StandaloneHandleBanClient, TheServerBanNamesTheServerShellAndNotThisOne )
{
	const std::string srv = ReadFile( "sourcesharp/game/server/standalone_handle_ban.h" );

	ASSERT_FALSE( srv.empty() ) << "the server's twin is missing";

	EXPECT_NE( std::string::npos,
	           srv.find( "template<> class CHandle< CStandaloneEntity >;" ) )
		<< "1e's server ban is gone or respelled.";

	EXPECT_EQ( std::string::npos, srv.find( "C_StandaloneEntity" ) )
		<< "the server ban names the CLIENT shell. If the two headers have been "
		   "merged, one side's ban is a forward declaration of a class that "
		   "does not exist in that DLL -- a ban on nothing, with every probe "
		   "still green.";
}

//-----------------------------------------------------------------------------
// BOTH LEGS EXIST IN THE TEST MANIFEST (src/test/native/CMakeLists.txt; the
// bash script buildcpptests until Phase 6b), and the control is the one that
// would be quietly dropped.
//-----------------------------------------------------------------------------
TEST( StandaloneHandleBanClient, TheBuildScriptCarriesBothLegs )
{
	const std::string s = ReadManifest();

	ASSERT_FALSE( s.empty() ) << "src/test/native/CMakeLists.txt is missing or "
		"SOURCESHARP_REPO is unset (ctest sets it)";

	// AT THE START OF A LINE, and that is not pedantry. A bare substring search
	// passes on `# ss_cpptest(...` and on `_disabled_ss_cpptest(...` -- the
	// needle is still IN the file, so the leg reads as present while nothing
	// runs it. Verified by mutation (on the bash form): renaming the invocation
	// with a prefix left this fact GREEN until the newline was put in front of
	// it. And UP TO THE END OF THE NAME (the newline after it), so a renamed
	// `..._absent_disabled` is not read as the leg either.
	EXPECT_NE( std::string::npos,
	           s.find( "\nss_must_not_compile(standalone_handle_ban_client\n" ) )
		<< "the refusal leg is gone, commented out, or renamed; nothing checks "
		   "that the client ban bans anything.";

	EXPECT_NE( std::string::npos,
	           s.find( "\nss_cpptest(standalone_handle_ban_client_absent\n" ) )
		<< "the CONTROL leg is gone, commented out, or renamed. Without it the "
		   "refusal is evidence of nothing -- a typo in the probe refuses just "
		   "as loudly.";
}

#endif // STANDALONE_HANDLE_BAN_CLIENT_NOCOMPILE
