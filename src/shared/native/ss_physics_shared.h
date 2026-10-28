//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The parts of game/shared/physics_shared.h server.so still needs: the vphysics
// pointers, the game-flag bits (same values as FVPHYSICS_*) and a few constants.

#pragma once

#include "vcollide_parse.h" // IVPhysicsKeyHandler

namespace ss
{
// The game's bits in IPhysicsObject::GetGameFlags; VPhysics stores them and reads none.
enum PhysGameFlag : unsigned short
{
	kPhysFlagSliceDamage = 0x0001, // FVPHYSICS_DMG_SLICE
	kPhysFlagConstraintStatic = 0x0002, // FVPHYSICS_CONSTRAINT_STATIC: pinned to the world
	kPhysFlagPlayerHeld = 0x0004, // FVPHYSICS_PLAYER_HELD
	kPhysFlagPartOfRagdoll = 0x0008, // FVPHYSICS_PART_OF_RAGDOLL
	kPhysFlagMultiObjectEntity = 0x0010, // FVPHYSICS_MULTIOBJECT_ENTITY
	kPhysFlagHeavyObject = 0x0020, // FVPHYSICS_HEAVY_OBJECT
	kPhysFlagPenetrating = 0x0040, // FVPHYSICS_PENETRATING
	kPhysFlagNoPlayerPickup = 0x0080, // FVPHYSICS_NO_PLAYER_PICKUP
	kPhysFlagWasThrown = 0x0100, // FVPHYSICS_WAS_THROWN
	kPhysFlagDissolveDamage = 0x0200, // FVPHYSICS_DMG_DISSOLVE
	kPhysFlagNoImpactDamage = 0x0400, // FVPHYSICS_NO_IMPACT_DMG
	kPhysFlagNoNpcImpactDamage = 0x0800, // FVPHYSICS_NO_NPC_IMPACT_DMG
	// Ours, in a bit stock leaves free: forces collision with any moveable
	// MOVETYPE_VPHYSICS object (func_clip_vphysics).
	kPhysFlagForceCollideMoveable = 0x1000,
	kPhysFlagNoSelfCollisions = 0x8000, // FVPHYSICS_NO_SELF_COLLISIONS
};

// physics_shared.h's FLUID_TIME_MAX: how long, in seconds, a splash is remembered.
constexpr float kFluidTimeMax = 2.0f;

// physics_shared.h's ENERGY_VOLUME_SCALE: impact energy to sound volume.
constexpr float kEnergyVolumeScale = 1.0f / 15500.0f;

} // namespace ss

// physics_shared.h's MASS10_SPEED2ENERGY: the energy of a 10 kg mass at a speed.
// A macro, as stock's is, so a constant speed stays a constant at -O0.
#define SS_MASS10_SPEED2ENERGY( speed ) ( ( speed ) * ( speed ) * 10 )
