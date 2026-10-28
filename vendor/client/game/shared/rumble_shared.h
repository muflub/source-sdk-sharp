//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once
#ifndef RUMBLE_SHARED_H
#define RUMBLE_SHARED_H

#define RUMBLE_FLAGS_NONE				0x0000
 // Stop any instance of this type of effect that's already playing.
 // Make this effect loop.
 // If this effect is already playing, restart it.
 // Apply DATA to this effect if already playing, but don't restart.
 // Don't play this effect if it is already playing.
 // Amplitude scale will be randomly chosen. Between 10% and 100%
 // Data is the initial scale to start this effect ( * 100 )

enum
{
// DO NOT CHANGE THE ORDER OF ANY OF THESE ENUMS
// DO NOT INSERT ANY ITEMS
	RUMBLE_INVALID = -1,

	RUMBLE_STOP_ALL	= 0,		// Cease all current rumbling effects.

	// Weapons
	RUMBLE_PISTOL,
	RUMBLE_357,
	RUMBLE_SMG1,
	RUMBLE_AR2,
	RUMBLE_SHOTGUN_SINGLE,
	RUMBLE_SHOTGUN_DOUBLE,
	RUMBLE_AR2_ALT_FIRE,

// YOU MAY INSERT/REARRANGE ITEMS FROM HERE DOWN, AS YOU SEE FIT
	RUMBLE_RPG_MISSILE,

	RUMBLE_CROWBAR_SWING,

	// Vehicles
	RUMBLE_AIRBOAT_GUN,
	RUMBLE_JEEP_ENGINE_LOOP,
	
	RUMBLE_FLAT_LEFT,
	RUMBLE_FLAT_RIGHT,
	RUMBLE_FLAT_BOTH,

	// Damage
	RUMBLE_DMG_LOW,
	RUMBLE_DMG_MED,
	RUMBLE_DMG_HIGH,

	// Fall damage
	RUMBLE_FALL_LONG,
	RUMBLE_FALL_SHORT,

	RUMBLE_PHYSCANNON_OPEN,
	RUMBLE_PHYSCANNON_PUNT,
	RUMBLE_PHYSCANNON_LOW,
	RUMBLE_PHYSCANNON_MEDIUM,
	RUMBLE_PHYSCANNON_HIGH,

	RUMBLE_PORTALGUN_LEFT,
	RUMBLE_PORTALGUN_RIGHT,
	RUMBLE_PORTAL_PLACEMENT_FAILURE,

	NUM_RUMBLE_EFFECTS, // THIS MUST BE LAST!!!
};

#endif//RUMBLE_SHARED_H
