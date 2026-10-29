//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// all flags clear makes default Half-Life explosion
// sprite will be drawn opaque (ensure that the sprite you send is a non-additive sprite)
// do not render dynamic lights
// do not play client explosion sound
// do not draw particles
// sprite will be drawn alpha
// rotate the sprite randomly
// do not draw a fireball
// do not draw smoke with the fireball

#define TE_BEAMPOINTS 0 // beam effect between two points
// additive sprite, plays 1 cycle
// disk that expands to max radius over lifetime
// cylinder that expands to max radius over lifetime
// create a line of decaying beam segments until entity stops moving
// connect a beam ring to two entities

// Fades according to viewpoint
