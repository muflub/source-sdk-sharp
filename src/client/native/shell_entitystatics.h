//=============================================================================//
// THE PORT'S OWN HOME FOR c_baseentity_statics.cpp's TENANTS — CLIENT_FLIP
// lane 2 (`c8-entitystatics`), plan_entity.md §6 and §7.
//
// WHAT THIS FILE IS FOR. `shell_clientdll.cpp` — the port's own IBaseClientDLL —
// called thirteen free functions that were DEFINED inside the two translation
// units the client flip deletes:
//
//     game/client/c_baseentity.cpp          EntityMem_Alloc  EntityMem_AllocDbg
//                                           EntityMem_Free
//     game/client/c_baseentity_statics.cpp  EntityAbs_ (four, plus Push/Pop)
//                                           EntityInterp_ (four)
//                                           EntityRender_AddVisibleEntities
//                                           EntityTool_RecordEntities (deleted)
//
// THEY ARE NOT ALL THE SAME KIND OF THING, AND SEPARATING THEM IS THE LANE.
// Some are the port's own mechanism wearing a stock name and must MOVE; others
// walk an SDK registry that no managed entity is ever in, and RETIRE with the
// hierarchy. The ruling for each, with the evidence, is the table in
// shell_entitystatics.cpp — and it is PRINTED, by `cs_cl_entitystatics`, which
// is the whole point of it existing.
//
// §7: "a behaviour that is refused is a ContractBaseline entry with a reason,
// never a silent no-op." A retirement whose only record is a deleted call site
// is a silent no-op with extra steps. Every row below carries the reason it was
// ruled that way AND what does the thing afterwards, and the retired rows that
// CAN still be exercised at runtime count and warn when they are.
//
// ENGINE-FREE, exactly as shell_clientdll_refusals.h is engine-free and for its
// reason: the property that matters is a property of the BODY — that a refusal
// counts, that a second refusal of the same row does not open a new one, that
// every row carries a reason — and a property of a body can only be checked by
// running it. shell_entitystatics.cpp includes cbase.h's whole world and can
// never be linked into a gtest, so the table and its counter live HERE and
// sourcesharp/test/shell_entitystatics_test.cpp compiles THIS header. Nothing
// there is a stand-in: it is the shipped class and the shipped table.
//
// The warning is the one thing that needs the engine, so it is DECLARED here
// and defined by whoever links this: tier0's Warning() in the .cpp, a recorder
// in the test.
//=============================================================================//

#ifndef SHELL_ENTITYSTATICS_H
#define SHELL_ENTITYSTATICS_H

#ifdef _WIN32
#pragma once
#endif

#include <stddef.h>
#include <string.h>

//=============================================================================//
// THE RULING TABLE AND ITS READOUT WENT IN PHASE 1c. WHAT REPLACED THEM, AND
// WHY §7 IS STILL SATISFIED.
//
// WHAT WAS HERE. A fifteen-row table -- every free function
// c_baseentity_statics.cpp used to own, each row carrying its ruling (MOVED or
// RETIRED), the REASON it was ruled that way, and WHAT DOES THE THING NOW --
// plus CShellEntityStaticsRulings, a counter over those rows, and
// `cs_cl_entitystatics`, which printed the whole table. The table lived in the
// header rather than the .cpp so that shell_entitystatics_test.cpp could read
// THE ROWS THE GAME SHIPS instead of a transcription.
//
// THIRTEEN OF THE FIFTEEN ROWS DESCRIBED WORK THAT IS FINISHED. Their function
// either moved into this file (where it is, and where a reader finds it) or
// retired with a hierarchy that no longer exists -- and a row that cannot fire
// is a paragraph, not a declaration. Those thirteen have nothing left to
// declare at runtime and are documentation of a completed migration.
//
// ONE ROW CAN STILL FIRE, and §7 -- "a behaviour that is refused is a
// ContractBaseline entry with a reason, never a silent no-op" -- is about
// exactly that one. The reason and successor strings that used to sit in the
// table are now at the refusal site itself, in EntityInterp_UpdateFrameGate in
// shell_entitystatics.cpp, where the code that refuses is:
//
//   EntityInterp_InterpolateServerEntities -- refused on the CLOSING EDGE of
//     the interpolation gate, once per close, not per call.
//
// The other, EntityTool_RecordEntities (refused when a tool was recording), is
// deleted with the tool framework: SFM is dead (user ruling 2026-10-19).
//
// SO THE THING §7 ASKS FOR IS STRICTLY CLOSER TO THE CODE than it was: a
// reader who reaches a refusal message now finds the reason in the function
// that printed it, rather than in a table indexed by an enum in another file.
// What is gone is the READOUT over rulings nothing can exercise --
// `cs_cl_entitystatics`, which nothing under tools/, in managed code or in the
// other DLL ever drove.
//=============================================================================//

//-----------------------------------------------------------------------------
// THE ALLOCATOR THAT MOVED, and the reason this header declares it at all.
//
// c_baseentity_statics.h declared these three and that header goes with the
// flip. The port's own world shell allocates through them
// (world_adapter_client.cpp:236,256,261 — lane 1's file, which reaches this
// header through client_csharp.vpc's $AdditionalIncludeDirectories) so the
// declarations have to outlive the SDK header that used to carry them.
//
// The bodies are unchanged. What DID change is recorded at the definitions:
// MEM_ALLOC_CREDIT() credits __FILE__ and the file is now this one.
//-----------------------------------------------------------------------------
void *EntityMem_Alloc( size_t stAllocateBlock );
void *EntityMem_AllocDbg( size_t stAllocateBlock, const char *pFileName, int nLine );
void  EntityMem_Free( void *pMem );

//-----------------------------------------------------------------------------
// THE FRAME GATE, which is the live half of what was
// EntityInterp_InterpolateServerEntities.
//
// Stock's function did two things in one body: it computed WHETHER
// interpolation runs this frame, and it then ran it over two SDK lists. The
// second half retires with those lists; the first half is a real, frame-level
// policy — cl_interpolate, a paused engine, timedemo playback, and a net
// channel that has heard nothing for half a second — and it does NOT retire.
// It writes EntityInterp_EnabledRef() in game/shared/entity_abs_query_state.h,
// which the flip does not delete.
//
// SEE THE REFUSAL AT ITS DEFINITION: the value is computed and correct, and
// nothing on the managed interpolation path reads it yet. That is a declared
// gap with an owner, not a quiet one.
//-----------------------------------------------------------------------------
void EntityInterp_UpdateFrameGate();

// EntityTool_RecordEntities, the tool-recording pass, is deleted: SFM is dead
// (user ruling 2026-10-19).

#endif // SHELL_ENTITYSTATICS_H
