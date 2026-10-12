//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — who a view punch reaches, and how hard ==============//
//
// Screen shake, screen fade and view punch were three loops over the player
// slots in util.cpp, and each of them decided TWO things per player: whether
// this player is in it at all, and at what strength. Both decisions are pure
// arithmetic over numbers the caller already has, and neither of them needs an
// entity — so they are here, with no engine in them, and are checked by
// view_effect_reach_test.cpp rather than by shooting a rocket at a wall.
//
// WHY THIS FILE EXISTS AT ALL, given that stock had the same arithmetic inline
// and it was correct: because for the whole of this port those loops resolved
// their subject as a CBasePlayer and got NULL, so the `continue` at the top ran
// for every slot and NONE of this arithmetic was ever reached. The functions
// below were never wrong; they were never executed. That is the failure mode
// the whole NULL-subject inventory is about, and it is invisible precisely
// because a rule nobody runs cannot produce a wrong answer.
//
// ONLY THE PUNCH IS LEFT. The shake and the fade are user messages, and C#'s
// ScreenEffects decides their reach and sends them (user-message payload
// model, 2026-10-04); its facts are ScreenEffectsTests. The shake rules that
// stood here (ShakeAmplitude, ShakeReachesPlayer, ShakeTransmits) had no
// native caller after that and went (lane HC, 2026-10-04).
//
//   punch   radius <= 0 means EVERYONE at full angle; inside the radius the
//           angle falls linearly to zero at the edge; outside it the player is
//           skipped. Measured from the player's ORIGIN rather than from the
//           centre of their bounding box, which is stock's split and is kept.
//
// Shared, header-only, and free of engine headers on purpose: the server calls
// it and the test calls it, and nothing here may need a CBaseEntity to be true.
//
//=============================================================================//
#ifndef SOURCESHARP_VIEW_EFFECT_REACH_H
#define SOURCESHARP_VIEW_EFFECT_REACH_H
#ifdef _WIN32
#pragma once
#endif

namespace ViewEffectReach
{

//-----------------------------------------------------------------------------
// The sentinel a falloff answers with when the player is outside the radius.
//
// A VALUE rather than a bool-and-out-param because stock's loop already
// branched on `< 0`, and reading that as "skip" keeps the walk identical to
// the code it replaces. Anything negative means out of range; this is the one
// produced.
//-----------------------------------------------------------------------------
const float OUT_OF_RANGE = -1.0f;

inline bool InRange( float flStrength )
{
	return flStrength >= 0.0f;
}

//-----------------------------------------------------------------------------
// Stock's view-punch falloff, taking the distance rather than the two points.
// It scales the ANGLE, and so answers a multiplier.
//
// Input  : flDistance - between the punch's centre and the player, >= 0
//          flRadius - 0 or less means everyone, unscaled
// Output : 0..1 to scale the punch angle by, or OUT_OF_RANGE
//-----------------------------------------------------------------------------
inline float PunchScale( float flDistance, float flRadius )
{
	if ( flRadius <= 0.0f )
		return 1.0f;

	if ( flDistance > flRadius )
		return OUT_OF_RANGE;

	return 1.0f - ( flDistance / flRadius );
}

//-----------------------------------------------------------------------------
// The on-ground gate. A punch is one shove with no state, so it checks the
// ground every time (C#'s shake checks it only when STARTING one).
//-----------------------------------------------------------------------------
inline bool PunchReachesPlayer( bool bOnGround, bool bPunchInAir )
{
	return bOnGround || bPunchInAir;
}

} // namespace ViewEffectReach

#endif // SOURCESHARP_VIEW_EFFECT_REACH_H
