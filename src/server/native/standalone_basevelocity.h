//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// What "push this thing along" does to the base-velocity channel.
// Base velocity is the engine's word for "carried by something else" — a
// conveyor, a lift, a blast — and it is the only velocity that survives the
// movement code, which recomputes ordinary velocity from the player's input
// every tick. SourceSharp_EntityAddBaseVelocity is the crossing that writes it,
// and until now it wrote it for CBaseEntity subjects only: it resolved `.pBase`
// and returned in silence for anything on the parallel hierarchy. This mod's
// player IS on the parallel hierarchy, so Riders.Ride — the whole reason belts
// and conveyors move anybody — pushed nobody, said nothing, and had done so for
// as long as the standalone player has existed.
// The fix is two arms, and two arms is exactly the shape that drifts: one gets
// the ground-release nudge and the other does not, or one accumulates and the
// other overwrites, and the difference shows up as a belt that works for crates
// and not for people. So the rule is HERE, once, engine-free, and both arms call
// it. Gtested in src/sourcesharp/test/standalone_basevelocity_test.cpp.
// TWO THINGS ARE DECIDED, and both are stock's:
//   ACCUMULATE, DO NOT OVERWRITE. FL_BASEVELOCITY means "somebody already added
//   to this channel on this tick and it has not been consumed yet". Two belts
//   overlapping, or a belt and a blast on the same tick, must sum — overwriting
//   means the last writer of the tick wins and the other push vanishes. Without
//   the flag the member holds a stale value the movement code is about to fold
//   away, so adding to it would double-count last tick's push.
//   AN UPWARD PUSH HAS TO UNSTICK THE FEET FIRST. A player standing on the
//   floor absorbs an upward push entirely: the move code sees them supported and
//   cancels it. So anything with a positive Z clears the ground entity and lifts
//   the origin one unit, which is what stock's own push paths do.

#ifndef SOURCESHARP_STANDALONE_BASEVELOCITY_H
#define SOURCESHARP_STANDALONE_BASEVELOCITY_H

//-----------------------------------------------------------------------------
// The accumulated channel value after this push.
//
// flCurrent* is only read when bHasUnconsumed is set; a caller passing garbage
// with the flag clear still gets the right answer, which is deliberate — the
// two facts arrive from different places and forgetting to pair them should not
// be able to invent velocity.
//-----------------------------------------------------------------------------
inline void StandaloneBaseVelocity_Accumulate( bool bHasUnconsumed,
                                               float flCurrentX, float flCurrentY, float flCurrentZ,
                                               float flPushX, float flPushY, float flPushZ,
                                               float *pOutX, float *pOutY, float *pOutZ )
{
	*pOutX = bHasUnconsumed ? flCurrentX + flPushX : flPushX;
	*pOutY = bHasUnconsumed ? flCurrentY + flPushY : flPushY;
	*pOutZ = bHasUnconsumed ? flCurrentZ + flPushZ : flPushZ;
}

//-----------------------------------------------------------------------------
// Whether the subject has to be lifted off whatever it is standing on before
// the push above can mean anything.
//
// Tested against the ACCUMULATED z rather than the raw push, so that a downward
// push added to a larger upward one still releases the ground — the sum is what
// the movement code will see, and it is the sum that has to survive.
//-----------------------------------------------------------------------------
inline bool StandaloneBaseVelocity_ReleasesGround( float flAccumulatedZ, bool bOnGround )
{
	return flAccumulatedZ > 0.0f && bOnGround;
}

// How far the origin is raised when the ground is released. One unit: enough to
// leave the two-unit ground trace's reach on the next tick, small enough not to
// be a teleport. Stock's number.
#define SOURCESHARP_BASEVELOCITY_UNSTICK 1.0f

#endif // SOURCESHARP_STANDALONE_BASEVELOCITY_H
