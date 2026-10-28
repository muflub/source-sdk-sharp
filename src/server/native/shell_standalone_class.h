//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — which classnames the parallel hierarchy hosts =======//
//
// REGISTRATION, AND NOTHING ELSE. plan_entity.md's Phase 1 exit asks for
// `shell_standalone_scripted.*` to be gone, and this pair is one of the two
// halves it split into: the classnames the parallel hierarchy hosts, the
// factory that makes their instances, the override mask each carries, and the
// blob offset past the largest shell. The other half is the op routing, which
// is `shell_dispatch.cpp` and needs no header — every function it defines is
// declared by `shell_standalone.h` or the game system.
//
// WHAT USED TO BE HERE. `shell_standalone_scripted.h` declared this same
// registration surface, and before step 1f it also carried
// `CStandaloneScriptedEntity` — a subclass of `CStandaloneEntity` whose only
// members were thirteen relays, one per ABI slot, each calling into managed
// code unconditionally. §3.1 says there is exactly one shell class per side and
// it is `final`; §3.4 says the routing is one `Dispatch` slot with an op number
// and a per-class override bitmask. 1f deleted the class; this split finishes
// the job by separating "which classes exist" from "how a call reaches one",
// which are two questions with two answers and were one file only by history.
//
//=============================================================================//
#pragma once

#include "shell_standalone.h"

//-----------------------------------------------------------------------------
// Registers a classname on the parallel hierarchy.
//
// Called from the one place that decides which shell a C#-declared class gets,
// SourceSharp_RegisterScriptedEntity. Returns false when the name could not be
// taken, which the caller reports rather than silently leaving the class
// unregistered.
//-----------------------------------------------------------------------------
bool SourceSharp_InstallStandaloneClass( const char *pClassName, const char **ppOutputNames,
                                         int nOutputCount, bool bServerOnly,
                                         SolidType_t nSolidType, int nSolidFlags,
                                         bool bTouchEveryTick, bool bPlayer );

//-----------------------------------------------------------------------------
// WHICH OPS A REGISTERED CLASS OVERRIDES -- the far side of the RegisterClassOps
// ABI slot (plan_entity.md §3.4).
//
// A bitmask over EntityOp_t, stored on the class descriptor every instance of
// that class holds a pointer to. Returns false for a name that was never
// registered, which the caller ignores: managed code may legitimately describe
// a class the native side has not been told about yet.
//
// SEPARATE FROM the install call above because the mask is the one thing a
// class declares that a RELOAD can change without the class list changing --
// somebody adds an OnThink and types cs_sv_reload.
//-----------------------------------------------------------------------------
bool SourceSharp_SetStandaloneClassOps( const char *pClassName, uint32_t nOpMask );

// The resolve family — SourceSharpEntity_t, SourceSharp_ResolveEntity and its
// typed projections — lives in shell_resolve.h, one header for one job.
#include "shell_resolve.h"


// The next entity of a classname on the parallel hierarchy, continuing from one.
// NULL both when there is no next and when the entity to continue from has since
// been removed — see the definition for why those answer the same way.
CStandaloneEntity *StandaloneEntities_FindByClassname( CStandaloneEntity *pStartAfter,
                                                       const char *pClassName );

//-----------------------------------------------------------------------------
// THE FACTORY DICTIONARY, ENUMERATED.
//
// `EntityFactoryDictionary()` is the shim's as of lane sub-factory -- see the
// last section of shell_standalone_class.cpp for why it moved off
// src/game/server/util.cpp rather than being deleted with it. Its interface
// (util.h:98) has no way to ask what is IN it, which is why stock's
// `dumpentityfactories` had to downcast to the concrete class. These five
// answer it without a cast.
//
// They are also the bodies the ABI slot requested in
// .claude/entity-port/abi-requests/sub-factory.md binds to. Managed code
// cannot see this table today and says so in its own comment
// (DevCommandsLive.cs:34, "the native factory dictionary is not reachable from
// here"), so `ent_create` warns about a classname it cannot check instead of
// refusing one it knows nothing answers to.
//-----------------------------------------------------------------------------

// How many classnames have a factory installed, and the i'th of them in INSTALL
// order. NULL past the end rather than a read off it.
int         SourceSharp_EntityFactoryCount();
const char *SourceSharp_EntityFactoryNameAt( int i );

// The classnames something asked to create that nothing answers to, and how
// many times each was asked for. Cumulative for the life of the process: a
// refusal is a fact about the BUILD, not about the map that happened to name
// it, so it is not reset between levels.
//
// On dm_lockdown this is three names -- func_useableladder, info_ladder_dismount
// and info_node -- because their definers (game/shared/func_ladder.cpp,
// ai_initutils.cpp) are not rows of server_csharp.vpc. Players cannot use
// ladders on that map and could not before this lane; see
// .claude/entity-port/status/p4-flip.md §2, which found it by grepping a
// console log. This is the same fact, asked for.
int         SourceSharp_EntityFactoryRefusalCount();
const char *SourceSharp_EntityFactoryRefusalNameAt( int i );
int         SourceSharp_EntityFactoryRefusalTimesAt( int i );

// The i'th factory's entity size, 0 past the end -- the detail column of
// `dumpentityfactories` and `dump_entity_sizes` (shell_standalone_class_debug.cpp,
// debug builds only), which cannot see the file-static dictionary itself.
size_t      SourceSharp_EntityFactorySizeAt( int i );
