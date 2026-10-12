//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ========================================================//
// SS_REASON g  leaf math over mdl/bsp data
//
// ragdoll_library.cpp -- the physics step's two forwarders (see
// ragdoll_library.h). The bodies, their table, their creation and their pose
// are SourceSharp.Client's ClientRagdolls (plan_physics_bones_native_zero
// Phase 3); fork game/client/physics.cpp calls these, and each is one ask of
// IManagedRagdollsClient. With no managed side there are no bodies, so each
// answers 0.
//=============================================================================//

#include "cbase.h"

#include "ragdoll_library.h"

#include "imanaged_areas_client.h"

int RagdollLibrary_StepPoses( void )
{
	IManagedRagdollsClient *pRagdolls = ManagedRagdollsClient();
	return pRagdolls ? pRagdolls->StepPoses() : 0;
}

int RagdollLibrary_DestroyAll( void )
{
	IManagedRagdollsClient *pRagdolls = ManagedRagdollsClient();
	return pRagdolls ? pRagdolls->DestroyAll() : 0;
}
