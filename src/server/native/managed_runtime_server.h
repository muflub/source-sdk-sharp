//=============================================================================//
// The server's runtime loader -- plan_gamedll.md Part 2, Part 3 and Part 3b.
//
// (Until plan_engineapi_vtables D5 this header also held the adapter class;
// the paragraphs below describe why it was a header, and stay as its history.)
//
// WHY A HEADER AT ALL, when the plan says "one file, one class". Because the
// two properties this class has to have are properties of its BODIES, and a
// property of a body can only be tested by running it:
//
//   * every method is one forward and nothing else, and
//   * it holds no copy of the managed table (Part 3b point 3).
//
// The second is the one that matters and the one that cannot be checked by
// reading. `m_Api = *SourceSharp_GetManagedApi()` in a constructor compiles,
// links, passes every existing test, and breaks on the first `cs_sv_reload`
// with function pointers into an unloaded assembly. So the class lives in a
// header with no engine include in it, `managed_runtime_server.cpp` holds the
// instance and the loader, and sourcesharp/test/managed_runtime_server_test.cpp
// compiles this same header against a table it can rewrite underneath the
// adapter. Nothing here is a stand-in for the shipped class: it IS the shipped
// class.
//
// ENGINE-FREE, deliberately. `sourcesharp_gamesystem.h` declares interfaces and
// nothing else, and `sourcesharp.h` is the host's, which has no Source
// dependency by construction. Adding a `cbase.h` to this file would end the
// test.
//=============================================================================//

#pragma once

#include "sourcesharp_gamesystem.h"
#include "sourcesharp.h"


// The symmetric partner of LoadManagedRuntime, factored out of
// CSourceSharpSystem::Shutdown so both sides reach the same teardown.
void SourceSharp_UnloadManagedSide( void );

// THE ADAPTER IS GONE (plan_engineapi_vtables D5). IManagedRuntimeServer is
// managed-built now: ManagedVtableGenerator writes its table from
// SourceSharp.Server's ServerRuntimeBinding, and the shell calls it through
// ManagedRuntimeServer(), which every load and reload acquires again from the
// managed root. What the adapter guaranteed by re-reading the table on every
// call -- nothing kept across a reload -- the accessor guarantees by holding
// no pointer in the shell at all.
