//========= SourceSharp — the source scans left from the material proxies' lane ==//
//
// Lane c7-proxies wrote this file for the material proxies and recvproxy.cpp.
// The proxies are C# now (lane c5-proxies, plan_client_novalve.md §2 cluster
// 1): PlayerLogo's rules, the Alpha, IsNPC, Health and animated-texture facts
// and the cluster's renderable-not-entity scans left with the 22 proxy TUs and
// shell_materialproxies.cpp they read, and MaterialProxyFactsTests /
// MaterialProxyOracleTests hold that behaviour now. What stays is the
// scanner (PART 1) and, since C8 took recvproxy.cpp out of client.so, the
// facts that it and dt_utlvector_recv.cpp stay out (PART 2).
//
//=============================================================================//
#include "gtest/gtest.h"

#include <sys/stat.h>

#include <fstream>
#include <sstream>
#include <string>

//=============================================================================
// PART 1 — THE SOURCE SCAN
//=============================================================================

namespace
{

//-----------------------------------------------------------------------------
// IS THIS NEEDLE INSIDE A `#ifndef SS_CBASEENTITY_ALIAS` REGION?
//
// Added by the `residue` wave, and it exists because of that wave's own rule:
// A TEST MUST NOT READ A DEAD ARM AS ITS REFERENCE. The armed configuration
// defines SS_CBASEENTITY_ALIAS -- game/client/client_csharp.vpc:74 -- and it is
// the only configuration this tree builds, so text inside such a region is on
// disk and is never compiled.
//
// Six facts below point at stock's relocated line in the shim. What they assert
// about it is that it is THERE AND DEAD -- moved rather than dropped, and still
// not reachable -- and this helper is the whole difference between that and a
// claim about what the build does. Without it those facts would be reading a
// dead arm as evidence, which is the mistake that made them red in the first
// place.
//
// Nearest-marker scan. Sufficient because the regions it is asked about are
// flat one-level guards: the needle is inside when the last guard opener before
// it is nearer than the last `#endif` before it. It answers false for a needle
// that is not in the file at all, so every caller asserts presence separately.
//-----------------------------------------------------------------------------
bool IsInsideAliasGuard( const std::string &str, const std::string &strNeedle )
{
	const size_t at = str.find( strNeedle );
	if ( at == std::string::npos )
		return false;

	const size_t iOpen = str.rfind( "#ifndef SS_CBASEENTITY_ALIAS", at );
	if ( iOpen == std::string::npos )
		return false;

	const size_t iClose = str.rfind( "#endif", at );
	return iClose == std::string::npos || iClose < iOpen;
}

//-----------------------------------------------------------------------------
// THE WHOLE-FILE FORM OF THE SAME QUESTION, added by the `the-close` wave.
//
// IsInsideAliasGuard above answers "is THIS needle dead". It was written when
// the two shim files still had dead arms in them. THE CLOSE DELETED EVERY ONE
// (f288a5e11), so both files now carry no conditional compilation at all -- and
// that is a much stronger and much cheaper liveness statement than a per-needle
// scan: if a file has no `#if` family directive in it, EVERY line in it is
// compiled, and every needle this suite reads out of it is a live reference.
//
// So the facts below prove liveness twice over: TheShimsCompileEveryLineTheseFactsRead
// asserts the file-level property once, and each re-pointed fact asserts
// EXPECT_FALSE( IsInsideAliasGuard( ... ) ) beside its own needle, which is the
// per-needle proof finding 3 of .claude/entity-port/status/residue-tests.md
// asked for. The pair matters: the per-needle scan alone answers false for a
// needle that is not in the file, and the file-level count alone says nothing
// about a needle that has been deleted.
//
// Line-initial only, on the RAW text, because a `#` inside a string literal or
// mid-line is not a directive. `#include`, `#define` and `#pragma` do not
// select anything and are not counted.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// One function's body, so a count can be scoped to the function rather than to
// the file. Brace-matched from the signature's opening `{`.
//
// Answers an empty string for a signature that is not there, so every caller
// asserts non-empty first -- a count over nothing is zero, which is the answer
// a correct file would also give.
//-----------------------------------------------------------------------------
} // namespace

TEST( AliasGuardScanner, ItTellsAGuardedNeedleFromAnUnguardedOne )
{
	// THE INSTRUMENT, CHECKED. Six facts below rest on this answer and a helper
	// that returned the same value for everything would make all six pass
	// without measuring anything.
	const std::string kSample = "live_one();\n"
								"#ifndef SS_CBASEENTITY_ALIAS\n"
								"dead_one();\n"
								"#endif // !SS_CBASEENTITY_ALIAS\n"
								"after_one();\n";

	EXPECT_TRUE( IsInsideAliasGuard( kSample, "dead_one();" ) );
	EXPECT_FALSE( IsInsideAliasGuard( kSample, "live_one();" ) );
	EXPECT_FALSE( IsInsideAliasGuard( kSample, "after_one();" ) )
		<< "the scan ran past the #endif, which would report every later line in "
		   "a file as dead";
	EXPECT_FALSE( IsInsideAliasGuard( kSample, "not_in_the_file();" ) )
		<< "a needle that is absent must not read as guarded -- that answer would "
		   "let a deleted line pass as a relocated one";
}

//=============================================================================
// PART 2 — recvproxy.cpp AND dt_utlvector_recv.cpp LEFT client.so
//
// plan_client_novalve C8 (lane c6-input). recvproxy.cpp was de-entitied in
// place (lane c7-proxies) and then had no importer at all: nm over the
// client's link-line objects found no object but its own using any of its
// thirteen strong definitions, and dt_utlvector_recv.cpp's five likewise. Every
// recv table the engine walks is built by shell_recvtable.cpp from C#
// declarations, with the standard proxies of dt_recv.cpp. The seam
// recvproxy.cpp's every-tick proxies called, SourceSharp_RecvTableInterpolation
// AmountsChanged, was empty on purpose and left with its one caller.
//=============================================================================

namespace
{
} // namespace
