//========= SourceSharp — which control points a managed particle system gets ==//
//
// A particle system's operators address the world through NUMBERED CONTROL
// POINTS, and an operator whose point was never written reads (0, 0, 0) — the
// centre of the map. That is not a subtle mis-shape; it is the effect drawing
// somewhere else entirely, and it does not warn.
//
// THE DEFECT THIS EXISTS TO PREVENT, found by reading the .pcf rather than by
// looking at a flame. burning_character — stock's fire for something that is
// alight, and what ss_entityflame draws with — has "Position on Model Random"
// in its initializer list with control_point_number 1, and four of its five
// child systems name control point 1 too. That operator asks the particle query
// for random points on the MODEL bound to that control point. Nothing can be
// bound here: CNewParticleEffect::SetControlPointEntity takes a CBaseEntity and
// every entity on the parallel hierarchy is not one. So
// CParticleSystemQuery::GetRandomPointsOnControllingObjectHitBox falls through
// to its `if ( !bSucesss )` branch and answers m_ControlPoints[1].m_Position
// for every particle — the origin, unless something feeds that point.
//
// The eight env_fire_* systems ss_firesmoke and ss_plasma use reference control
// point 0 and nothing else. That was checked in the .pcf, not assumed, and it
// is why the DEFAULT here is bit 0 alone: it is exactly what
// SourceSharp_ParticleSystemStart fed before any of this existed, so no
// existing effect changes.
//
// WHY IT IS A HEADER. Two rules, both one line, both able to be wrong in a way
// nothing would report:
//
//   A mask that cleared bit 0 would strand the effect at the position it was
//   created at — the bug this file exists to prevent, inverted — so bit 0 is
//   forced on rather than trusted.
//
//   The walk over the mask must not run past MAX_PARTICLE_CONTROL_POINTS, and a
//   32-bit mask has 32 bits while the array has 64 entries, so the bound is the
//   MASK's, not the array's. Getting that backwards is a write past the end of
//   a live particle collection.
//
// Engine-free: this is arithmetic over an int.
//
//=============================================================================//
#ifndef SOURCESHARP_PARTICLE_FOLLOW_MASK_H
#define SOURCESHARP_PARTICLE_FOLLOW_MASK_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// What a system that nobody configured follows: control point 0, and nothing
// else.
//-----------------------------------------------------------------------------
enum { kSourceSharpParticleFollowDefault = 1 };

//-----------------------------------------------------------------------------
// The highest control point a 32-bit mask can name.
//
// MAX_PARTICLE_CONTROL_POINTS is 64 (public/particles/particles.h), so every
// bit of the mask names a point that exists and the walk cannot leave the
// array. It is the MASK that runs out first, which is the way round that is
// safe.
//-----------------------------------------------------------------------------
enum { kSourceSharpParticleMaxFollowPoint = 31 };

//-----------------------------------------------------------------------------
// The mask a caller's request actually becomes.
//
// Bit 0 is forced on. A caller that cleared it would leave the effect emitting
// at wherever it was created while its owner walked away — the same class of
// bug as an unfed point 1, and harder to notice because the flame is at least
// somewhere plausible.
//-----------------------------------------------------------------------------
inline int SourceSharpParticleFollow_Normalise( int nRequested )
{
	return nRequested | kSourceSharpParticleFollowDefault;
}

//-----------------------------------------------------------------------------
// Whether this control point is one the sweep keeps at the owner.
//
// Out-of-range points answer NO rather than being clamped: a point past the end
// of the mask is a point nobody asked for, and clamping would silently move a
// request from point 40 onto point 31.
//-----------------------------------------------------------------------------
inline bool SourceSharpParticleFollow_Wants( int nMask, int nPoint )
{
	if ( nPoint < 0 || nPoint > kSourceSharpParticleMaxFollowPoint )
		return false;

	return ( nMask & ( 1 << nPoint ) ) != 0;
}

#endif // SOURCESHARP_PARTICLE_FOLLOW_MASK_H
