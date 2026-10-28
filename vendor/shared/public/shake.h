//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Network data for screen shake and screen fade.

#pragma once

// Commands for the screen shake effect.

struct ScreenShake_t
{
	int		command;
	float	amplitude;
	float	frequency;
	float	duration;
};

		// Just here so we don't pass 0 into the function
		// Fade out (not in)
		// Modulate (don't blend)
		// ignores the duration, stays faded out until new ScreenFade message received
		// Purges all other fades, replacing them with this one

#define SCREENFADE_FRACBITS		9		// which leaves 16-this for the integer part
// This structure is sent over the net to describe a screen fade event
struct ScreenFade_t
{
	unsigned short 	duration;		// FIXED 16 bit, with SCREENFADE_FRACBITS fractional, seconds duration
	unsigned short 	holdTime;		// FIXED 16 bit, with SCREENFADE_FRACBITS fractional, seconds duration until reset (fade & hold)
	short			fadeFlags;		// flags
	byte			r, g, b, a;		// fade to color ( max alpha )
};
