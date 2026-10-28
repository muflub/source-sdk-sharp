//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// Shared definitions for rope.
#define ROPE_MAX_SEGMENTS 10

// Default rope gravity vector.

// Rope flags.
// Try to keep the rope dangling the same amount
// even as the rope length changes.
// Hack option to draw like a barbed wire.
// Collide with the world?
// Is the rope valid?
// Can the endpoints detach?
// No wind simulation on this rope.
// By default, ropes will simulate for a bit internally when they
// are created so they sag, but dynamically created ropes for things
// like harpoons don't want this.
// If this flag is set, then the second attachment must be a player.
// The rope will attach to "buff_attach" on the player's active weapon.
// (This is a flag because it requires special code on the client to
// find the weapon).
// Disable gravity on this rope.

// This is added to all rope slacks so when a level designer enters a
// slack of zero in the entity, it doesn't dangle so low.

// Rope shader IDs.

// Rope locked points
enum
{
	ROPE_LOCK_START_POINT = 0x1,
	ROPE_LOCK_END_POINT = 0x2,
	ROPE_LOCK_START_DIRECTION = 0x4,
	ROPE_LOCK_END_DIRECTION = 0x8,
};

// Rope attachment points.
#define ROPE_ATTACHMENT_START_POINT 1
#define ROPE_ATTACHMENT_END_POINT 2
