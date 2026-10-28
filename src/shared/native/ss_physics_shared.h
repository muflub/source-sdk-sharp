//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the shared physics globals, as server.so's R5 TUs see them
//
// plan_server_novalve.md P5 (lane SP). game/shared/physics_shared.h is a fork
// header; a translation unit on the R5 include set reaches what it needs of it
// here instead:
//   * the vphysics pointers server.so holds (declared here; defined in
//     game/server/physics.cpp since physics_shared.cpp left, 2.6);
//   * VPhysics game-flag bits, respelled in namespace ss. They are the game's
//     own bits in IPhysicsObject::GetGameFlags (VPhysics stores them and reads
//     none), and ss_physics_shared_test holds each to physics_shared.h's value;
//   * the two impact-energy scales physics_shared.cpp's sounds use.
//   * FLUID_TIME_MAX.
//
// physics_shared.cpp's world build, solid key handler (CSolidSetDefaults /
// g_SolidSetup), default object params, collision-pair switches and the
// native pair hash are managed's or gone (plan_physics_bones_native_zero 2.1,
// 2.6): ServerPhysicsWorld, SolidKeyDefaults, EntityPairCollisions. So are
// PhysSetGameFlags / PhysClearGameFlags, whose last caller was the native
// EntityAddPhysicsGameFlags forward (EntityPhysicsMotion.AddGameFlags,
// 2026-10-12).
//=============================================================================//

#pragma once

#include "vphysics_interface.h"
#include "vcollide_parse.h"     // IVPhysicsKeyHandler

// Defined in game/server/physics.cpp: the pointers the physics module handed
// this DLL (plan_physics_bones_native_zero 2.6).
extern IPhysicsObject			*g_PhysWorldObject;
extern IPhysics					*physics;
extern IPhysicsCollision		*physcollision;
extern IPhysicsEnvironment		*physenv;
extern IPhysicsSurfaceProps		*physprops;

namespace ss
{

// The game's bits in IPhysicsObject::GetGameFlags (physics_shared.h's
// FVPHYSICS_*, same values).
enum PhysGameFlag : unsigned short
{
	kPhysFlagSliceDamage        = 0x0001,	// FVPHYSICS_DMG_SLICE
	kPhysFlagConstraintStatic   = 0x0002,	// FVPHYSICS_CONSTRAINT_STATIC: pinned to the world
	kPhysFlagPlayerHeld         = 0x0004,	// FVPHYSICS_PLAYER_HELD
	kPhysFlagPartOfRagdoll      = 0x0008,	// FVPHYSICS_PART_OF_RAGDOLL
	kPhysFlagMultiObjectEntity  = 0x0010,	// FVPHYSICS_MULTIOBJECT_ENTITY
	kPhysFlagHeavyObject        = 0x0020,	// FVPHYSICS_HEAVY_OBJECT
	kPhysFlagPenetrating        = 0x0040,	// FVPHYSICS_PENETRATING
	kPhysFlagNoPlayerPickup     = 0x0080,	// FVPHYSICS_NO_PLAYER_PICKUP
	kPhysFlagWasThrown          = 0x0100,	// FVPHYSICS_WAS_THROWN
	kPhysFlagDissolveDamage     = 0x0200,	// FVPHYSICS_DMG_DISSOLVE
	kPhysFlagNoImpactDamage     = 0x0400,	// FVPHYSICS_NO_IMPACT_DMG
	kPhysFlagNoNpcImpactDamage  = 0x0800,	// FVPHYSICS_NO_NPC_IMPACT_DMG
	// SourceSharp's own, in a bit stock leaves free: this object forces
	// collision with any moveable MOVETYPE_VPHYSICS object (func_clip_vphysics,
	// physics stock parity A5; read by the collision rule table).
	kPhysFlagForceCollideMoveable = 0x1000,
	kPhysFlagNoSelfCollisions   = 0x8000,	// FVPHYSICS_NO_SELF_COLLISIONS
};

// physics_shared.h's FLUID_TIME_MAX: how long, in seconds, a splash is remembered.
constexpr float kFluidTimeMax = 2.0f;

// physics_shared.h's ENERGY_VOLUME_SCALE: impact energy to sound volume.
constexpr float kEnergyVolumeScale = 1.0f / 15500.0f;

} // namespace ss

// physics_shared.h's MASS10_SPEED2ENERGY: the energy of a 10 kg mass at a speed.
// A macro, as stock's is, so a constant speed stays a constant at -O0.
#define SS_MASS10_SPEED2ENERGY( speed ) ( ( speed ) * ( speed ) * 10 )
