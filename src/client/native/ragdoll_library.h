//========= SourceSharp ========================================================//
//
// ragdoll_library.h -- what is left of plan_entity.md 6e's ragdoll library
// after plan_physics_bones_native_zero Phase 3.
//
// The client's jointed bodies are SourceSharp.Client's: ClientRagdolls holds
// them by owner, RagdollSolver is ragdoll_shared.cpp and CRagdoll ported, and
// every vphysics call is a direct call through a generated vtable. What stays
// here is what a FORK file calls and this repo cannot edit: game/client/
// physics.cpp includes this header and calls the three functions below --
// StepPoses after the solver's Simulate, SetTailResolver( NULL ) and
// DestroyAll before DestroyEnvironment. The first two are forwarders into
// IManagedRagdollsClient; the resolver has nothing left to resolve.
//
// When fork physics.cpp leaves client.so (the client physics lane), this
// header and ragdoll_library.cpp leave with it.
//=============================================================================//

#ifndef SOURCESHARP_RAGDOLL_LIBRARY_H
#define SOURCESHARP_RAGDOLL_LIBRARY_H

#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>

struct EngineBlockClientTail_t;

// The physics step's pose write: managed's ClientRagdolls.StepPoses, one
// crossing a step. Answers how many bodies were written.
int RagdollLibrary_StepPoses( void );

// Every body taken apart, before the environment goes: managed's
// ClientRagdolls.DestroyAll. Answers how many.
int RagdollLibrary_DestroyAll( void );

// The render-tail resolver the native step used. Managed's bodies find their
// owner's tail themselves (by handle, checking the serial), so there is
// nothing to install; physics.cpp's SetTailResolver( NULL ) at level shutdown
// is a no-op.
typedef EngineBlockClientTail_t *( *RagdollTailResolverFn )( uint32_t nOwner );

inline void RagdollLibrary_SetTailResolver( RagdollTailResolverFn ) {}

#endif // SOURCESHARP_RAGDOLL_LIBRARY_H
