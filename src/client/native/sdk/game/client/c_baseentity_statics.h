// C_BaseEntity's frame-loop statics as free functions. The bodies stay in
// c_baseentity.cpp beside the lists and convars they read; the class-scope
// names remain as inline forwarders in c_baseentity.h.
#pragma once
#define C_BASEENTITY_STATICS_H

// The aim-ent pass: clear DIRTY_ABSTRANSFORM on every aim-ent, then recompute
// them. CalcAimEntPositions is a friend of C_BaseEntity (CalcAbsolutePosition is private).
void EntityInterp_MarkAimEntsDirty();
void EntityInterp_CalcAimEntPositions();

// The interpolation pass: republish s_bInterpolate, re-derive interpolation
// amounts when alternate-ticks or threading changed, then run the two lists.
void EntityInterp_InterpolateServerEntities();
void EntityInterp_ProcessTeleportList();
void EntityInterp_ProcessInterpolatedList();

// Warns about entities missing from g_InterpolationList. Empty unless
// INTERPOLATEDVAR_PARANOID_MEASUREMENT is defined; then it reads a private member.
void EntityInterp_CheckParanoidMeasurement();

// Adds unacknowledged client-created predictables to the leaf system so they
// draw until the real entity arrives.
void EntityRender_AddVisibleEntities();

// Hands every recording entity to the tool framework for this frame.
void EntityTool_RecordEntities();

// Republishes the effective interpolation when cl_interp or cl_interp_ratio changed.
void EntityInterp_CheckCLInterpChanged();

// The entity allocator: MemAlloc, zeroed. The operators stay inline members
// (a free operator new would replace the global one). The bodies stay in
// c_baseentity.cpp: MEM_ALLOC_CREDIT records __FILE__, and they sit inside its
// memdbgoff/memdbgon bracket.
void* EntityMem_Alloc(size_t stAllocateBlock);
void EntityMem_Free(void* pMem);

// Client-side IsFloating, which is always false: the client has no fluid density.
bool EntityWater_ClientIsFloating();
