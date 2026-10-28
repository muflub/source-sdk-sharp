//====== SourceSharp — plan_server_all.md: the nine zero-consumer files ======//
//
// docs/server_eviction_graph.md lists nine stock objects that `nm` says nobody
// asks for. Section 8 of the plan warns that a zero external surface is a
// PROBE CANDIDATE and not a free deletion, and it was right nine times out of
// nine: two were evicted and SEVEN are traps, of four different kinds.
//
//   DELETED
//     playerinfomanager.cpp   publishes PlayerInfoManager002/001 and
//                             BotManager001 through CreateInterface, which is
//                             invisible to nm (trap 2). The grep answers it:
//                             those strings appear ONLY in
//                             serverplugin_empty.dll, a WINDOWS sample plugin.
//                             Nothing this platform can load asks.
//     studio_shared.cpp       five out-of-line studiohdr_t / virtualgroup_t
//                             methods. Genuinely unreferenced, and checked with
//                             the collector OFF so that "nothing calls it" is
//                             not "the caller was collected too".
//
//   KEPT, each for a different reason nm cannot see
//     plugin_check.cpp        same macro as playerinfomanager, opposite answer:
//                             PluginHelpersCheck001 IS in engine.so.
//     randoverride.cpp        it INTERPOSES libc. It defines rand() and srand(),
//                             so the engine's deterministic RNG is used instead
//                             of the C library's. Remove it and the link still
//                             succeeds -- LIBC ANSWERS INSTEAD -- and eight
//                             objects silently change random number generator.
//                             This one is not in section 8's list of five and
//                             should be.
//     networkvar.cpp          one bool. Zero readers in release, TWENTY-SIX in
//                             debug: g_bUseNetworkVars is read by the _DEBUG
//                             network-var assertions. Section 8's trap 4, the
//                             reason evict_probe takes a MODE.
//     interval.cpp            reads as zero and `make debug GC_SECTIONS=0`
//                             names four references. See
//                             server_wave2_lifts_test.cpp.
//     effect_dispatch_data.cpp DT_EffectData has to leave both sides in ONE
//                             commit or the client join hangs (section 7).
//                             OVERTURNED BY MEASUREMENT at plan_entity.md step
//                             4a: neither DLL contains a CTEEffectDispatch
//                             class at all, so the mechanism has nothing to
//                             fire on. The server row is gone; see the test
//                             below, which now asserts the absence.
//     memoverride.cpp         the allocator override, and it does not even come
//                             from this project's file list.
//     stdafx.cpp              creates the precompiled header.
//
// WHAT THIS FILE IS FOR. Every "kept" above is a file that will read as free to
// the next person who runs the probe. The probe will tell them the same thing
// it told this lane. These tests are the note back.
//
//=============================================================================//

#include <algorithm>
#include <set>
#include <string>
#include <vector>
#include <fstream>
#include <sys/stat.h>
#include <cstdio>

#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

// WHAT THE SERVER COMPILES is server_sourcesharp's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/server/native/CMakeLists.txt). Farm-relative paths.
//
// Matched by BASENAME, under any directory, and never as a suffix -- see
// server_wave2_lifts_test.cpp for why (a relocated file must still answer, and
// "c_baseentity.cpp" must not answer for "baseentity.cpp").
bool ServerCompiles( const char *pszBasename )
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kServer );
	const std::string base( pszBasename );
	for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
	{
		const size_t slash = it->find_last_of( '/' );
		if ( ( slash == std::string::npos ? *it : it->substr( slash + 1 ) ) == base )
			return true;
	}
	return false;
}

// The proof-of-read for every absence below: the port's own shim, which no
// eviction can take.
const char *const kServerProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

} // namespace

//-----------------------------------------------------------------------------
// The two that went.
//-----------------------------------------------------------------------------
TEST( ServerWave3, PlayerInfoManagerIsOutOfTheBuild )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "the reader is not reading server_sourcesharp's source list";

	EXPECT_FALSE( ServerCompiles( "playerinfomanager.cpp" ) )
		<< "playerinfomanager.cpp is back in the server build. It publishes "
		<< "PlayerInfoManager001/002 and BotManager001 through "
		<< "EXPOSE_SINGLE_INTERFACE_GLOBALVAR, and the only binary in the "
		<< "installed game that names any of those strings is "
		<< "serverplugin_empty.dll -- a WINDOWS sample plugin. If a Linux "
		<< "server plugin is now in scope, that is the consumer and this is a "
		<< "real decision to revisit rather than a test to edit.";

	EXPECT_FALSE( ServerCompiles( "studio_shared.cpp" ) )
		<< "studio_shared.cpp is back. Its five out-of-line studiohdr_t and "
		<< "virtualgroup_t methods were unreferenced with --gc-sections OFF, "
		<< "which is the only way that claim means anything.";
}

// plugin_check.cpp's two facts went with the file (plan_server_native_min P1,
// Decision 1: third-party plugins are dropped, user ruling 2026-10-11). The
// risk they guarded -- engine.so carries the PluginHelpersCheck001 string and
// asks for it -- is recorded in docs/server_evictions.md and engine-boundary.md
// section 6; the deletion is the ruling, not an oversight, and the headless
// start is the check that the engine tolerates the NULL.

//-----------------------------------------------------------------------------
// randoverride.cpp: zero consumers because LIBC WOULD ANSWER INSTEAD.
//
// This is not in section 8's list of five ways the measurement lies, and it is
// the most dangerous kind: removing the file does not break the link and does
// not warn. It changes which random number generator its callers use.
//
// The server's callers are gone (the eight objects that carried an undefined
// `rand` -- EntityDissolve, RagdollBoogie, entitylist_base, gib, physics,
// physics_fx, physics_prop_ragdoll, player_pickup -- left or stopped calling
// it), so the file left server.so with vstdlib_random_port.cpp and lives in
// src/client/native (user ruling 2026-10-12: what the server no longer needs
// is client-only). The danger is still the same one: so the server half is
// "nothing on server.so's link imports rand or srand", and the client half
// keeps the interposition where its callers are.
//-----------------------------------------------------------------------------
namespace
{
// `nm -u` over every object on a target's link edge: whether one imports name.
int ImportersOf( const char *pszTarget, const char *pszName, size_t *pnObjects )
{
	const std::string strBuild = cmake_graph::BuildDir();
	std::string cmd = "nm -u";
	size_t n = 0;
	for ( const std::string &rel : cmake_graph::LinkObjects( pszTarget ) )
	{
		if ( rel.size() > 2 && rel.compare( rel.size() - 2, 2, ".o" ) == 0 )
		{
			cmd += " '" + ( rel[ 0 ] == '/' ? rel : strBuild + "/" + rel ) + "'";
			n++;
		}
	}
	*pnObjects = n;
	FILE *pPipe = popen( ( cmd + " 2>/dev/null" ).c_str(), "r" );
	if ( !pPipe )
		return -1;
	int nHits = 0;
	char buf[ 1024 ];
	const std::string want = std::string( " U " ) + pszName + "\n";
	while ( fgets( buf, sizeof( buf ), pPipe ) )
	{
		const std::string line( buf );
		if ( line.size() >= want.size() && line.compare( line.size() - want.size(), want.size(), want ) == 0 )
			nHits++;
	}
	pclose( pPipe );
	return nHits;
}
} // namespace

TEST( ServerWave3, NothingOnTheServerLinkCallsRandSoRandOverrideLeftIt )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "the reader is not reading server_sourcesharp's source list";
	EXPECT_FALSE( ServerCompiles( "randoverride.cpp" ) )
		<< "randoverride.cpp is back in the server build; it is client-only (user ruling 2026-10-12)";

	size_t nObjects = 0;
	// Proof of read: the client's link does import rand (particlemgr, view, ...).
	EXPECT_GT( ImportersOf( cmake_graph::kClient, "rand", &nObjects ), 0 );
	ASSERT_GT( nObjects, 100u );

	EXPECT_EQ( 0, ImportersOf( cmake_graph::kServer, "rand", &nObjects ) )
		<< "a server object calls rand() again. Without randoverride.cpp libc "
		<< "answers it, not the engine's deterministic RandomInt, and nothing "
		<< "warns: bring randoverride.cpp and vstdlib_random_port.cpp back to "
		<< "the server (or ask managed code) before this goes green.";
	EXPECT_EQ( 0, ImportersOf( cmake_graph::kServer, "srand", &nObjects ) );
	// 80, not 100: the server's link edge is 100 objects since
	// plan_server_native_v2 T4e (shell_serversystems.o left).
	ASSERT_GT( nObjects, 80u );
}

//-----------------------------------------------------------------------------
// networkvar.cpp: section 8's trap 4, and the reason evict_probe takes a MODE.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The remaining three, so that "zero consumers" never again reads as "free".
//-----------------------------------------------------------------------------
TEST( ServerWave3, TheOtherZeroConsumerFilesAreStillCompiled )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "the reader is not reading server_sourcesharp's source list";

	// stdafx.cpp WAS asserted here, as a zero-consumer file that had to stay:
	// it created the precompiled header the project's translation units were
	// built against, and defined no symbol by design. plan_shim.md 0b took the
	// precompiled header out, which leaves that file with neither a consumer
	// nor a job -- so the row, and the file, are gone, and the assertion is
	// inverted rather than deleted.
	EXPECT_FALSE( ServerCompiles( "stdafx.cpp" ) )
		<< "stdafx.cpp is compiled into server_sourcesharp again. It is `#include "
		<< "\"cbase.h\"` and nothing else; with no precompiled header "
		<< "in the target it compiles cbase.h into an object nothing links "
		<< "for, and with one it means the precompiled header is back.";

	// effect_dispatch_data.cpp WAS asserted here and is not any more, and this
	// is a deliberate decision with a measurement behind it rather than a
	// relaxed assertion.
	//
	// The claim was section 7 of plan_server_all.md's: DT_EffectData "has to
	// leave both sides in ONE commit", because a REGISTERED SERVER CLASS with no
	// client counterpart hangs the join. That mechanism needs a class, and
	// there is no such class on either side of THIS build:
	//
	//     $ nm -C game/mod_sharp/bin/linux64/server.so | grep -c TEEffectDispatch
	//     0
	//     $ nm -C game/mod_sharp/bin/linux64/client.so | grep -c TEEffectDispatch
	//     0
	//
	// te_effect_dispatch.cpp left the server with plan_tmp.md Part 7, and
	// c_te_effect_dispatch.cpp's IMPLEMENT_CLIENTCLASS_EVENT_DT -- the only
	// thing that references REFERENCE_RECV_TABLE( DT_EffectData ) -- is inside
	// `#ifndef SOURCESHARP` and is not compiled. What the server had left was a
	// bare SendTable no ServerClass pointed at, and a bare table is not in the
	// class list the join matches. TE_DispatchEffect marshals every field by
	// hand into a managed temp-entity forward now (shell_te_forwards.cpp) and asks
	// the table nothing.
	//
	// So plan_entity.md step 4a took the SERVER row out. The file stays on disk
	// and stays in the client build (client_sourcesharp). effects_cluster_test.cpp carries this
	// file's row now, in both directions, and the live gate's `join` step is
	// what proves it.
	EXPECT_FALSE( ServerCompiles( "effect_dispatch_data.cpp" ) )
		<< "effect_dispatch_data.cpp is in the SERVER build again. Step 4a took "
		<< "it out; if something needs it back, the question to ask is which "
		<< "object started referencing DT_EffectData (tools/4a-owes).";

	// memoverride.cpp USED TO BE asserted absent from server_csharp.vpc: it
	// arrived from Valve's stock source_dll_base.vpc, and naming it in the
	// project's own list as well was how a translation unit got compiled twice.
	// There is no stock base any more -- server_sourcesharp's list is the only
	// place a source can come from. plan_server_novalve.md P4.8 took
	// public/tier0/memoverride.cpp out of it (server.so uses glibc directly),
	// so the list names it ZERO times now.
	const std::vector< std::string > listed = cmake_graph::ListedSources( cmake_graph::kServer );
	EXPECT_FALSE( listed.empty() ) << "the server's source list read empty";
	EXPECT_EQ( 0, (int)std::count( listed.begin(), listed.end(), std::string( "public/tier0/memoverride.cpp" ) ) )
		<< "src/server/native/CMakeLists.txt lists public/tier0/memoverride.cpp "
		<< "again: server.so would route its allocations to tier0 (plan 3.6).";
}
