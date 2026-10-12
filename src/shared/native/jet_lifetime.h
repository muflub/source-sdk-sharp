//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — how long a puff of a jet lives ====================//
//
// Two particle entities, env_steam and env_smokestack, each derive one number
// from the same pair of map keys:
//
//   C_SteamJet     m_Lifetime    = m_JetLength / m_Speed      (a duration)
//   C_SmokeStack   m_InvLifetime = m_Speed / m_JetLength      (its reciprocal)
//
// It is the time a puff takes to cross the jet, and it becomes every particle's
// die time. Stock computes each in two places and guards neither, which was safe
// there for one reason only: stock's env_steam and env_smokestack are NETWORKED
// entities whose values arrive from a server that ran the map's keys through a
// C++ class with real constructor defaults. In this build both are C# entities,
// their Speed and JetLength are plain auto-properties with NO INITIALISER, and a
// map that does not set those keys sends zero for both.
//
//   0 / 0   = NaN
//   80 / 0  = +Inf
//   0 / 120 = 0     — finite, and still fatal one layer down
//
// The last one is why this is a rule and not an `if (x != 0)`. Both renderers
// divide by, or multiply into, the result again:
//
//   C_SteamJet::RenderParticles    sin( m_Lifetime * PI / m_DieTime )
//   C_SmokeStack::RenderParticles  FLerp( m_StartSize, m_EndSize,
//                                         m_Lifetime * m_InvLifetime )
//
// so a zero is a divide by zero in one and an Inf-times-zero in the other, and
// what comes out is NaN either way. The smokestack's is the worse of the two
// because the value it poisons is the particle's SIZE, and the size is what the
// mesh builder is handed:
//
//   RenderParticle_Color255SizeNormalAngle:  pos.x + (-ca + sa) * size
//
// which is `CVertexBuilder::Position3f` and
// `Assert( IsFinite(x) && IsFinite(y) && IsFinite(z) )`. Neither renderer can
// reject it on the way past: both early out on an alpha test written as
// `if ( alpha < 0.5f ) return;`, and every comparison with NaN is false, so a
// NaN alpha walks straight through the guard that exists to discard invisible
// particles. So does the death test — `if ( tLifetime >= 1 ) RemoveParticle` —
// which means the bad particles are also immortal.
//
// The invariant is therefore not "do not divide by zero". It is:
//
//   THE LIFETIME IS ALWAYS FINITE AND STRICTLY POSITIVE, AND SO IS ITS INVERSE.
//
// Engine-free, so the rule can be tested without a running Source: it is
// arithmetic over two floats.
//
//=============================================================================//

#ifndef SOURCESHARP_JET_LIFETIME_H
#define SOURCESHARP_JET_LIFETIME_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// What a jet nobody configured is worth.
//
// Stock's C_SteamJet constructor sets m_JetLength = 80 and m_Speed = 120 before
// any value arrives; stock's C_SmokeStack sets 180 and 30. Using each class's
// own pair means an unconfigured jet looks like stock's unconfigured jet rather
// than like a number somebody chose to make an assert stop firing.
//-----------------------------------------------------------------------------
#define SOURCESHARP_STEAMJET_DEFAULT_LENGTH     80.0f
#define SOURCESHARP_STEAMJET_DEFAULT_SPEED     120.0f

#define SOURCESHARP_SMOKESTACK_DEFAULT_LENGTH  180.0f
#define SOURCESHARP_SMOKESTACK_DEFAULT_SPEED    30.0f

//-----------------------------------------------------------------------------
// Finite, without mathlib.
//
// x != x is true only for NaN. The magnitude test catches both infinities.
// Written out rather than calling IsFinite() so this header needs nothing but
// itself and can be compiled into a test binary with no engine behind it.
//-----------------------------------------------------------------------------
inline bool SourceSharp_JetIsUsable( float f )
{
	if ( f != f )
		return false;                       // NaN

	// A float's largest finite magnitude. Anything at or past it is an infinity.
	const float flHuge = 3.402823466e+38f;

	return ( f <= flHuge ) && ( f >= -flHuge );
}

//-----------------------------------------------------------------------------
// The shared core: a quotient that is guaranteed finite and strictly positive.
//
// Both inputs must be finite and strictly positive for the division to mean
// anything — a jet of no length has no crossing time, and a jet of no speed
// never crosses — so either way the honest answer is "this jet was not
// configured", and the caller's default is what stock would have shown for an
// unconfigured one.
//
// The result is re-checked rather than trusted. A finite numerator divided by a
// finite denominator can still overflow to infinity (1e38 / 1e-38 does) and can
// underflow to zero, which is the divide-by-zero case one layer down wearing a
// different hat. Both come back as the default.
//
// GUARANTEE: the return value is always finite and strictly positive.
//-----------------------------------------------------------------------------
inline float SourceSharp_JetQuotient( float flNumerator, float flDenominator,
                                      float flDefaultNumerator,
                                      float flDefaultDenominator )
{
	const float flDefault = flDefaultNumerator / flDefaultDenominator;

	if ( !SourceSharp_JetIsUsable( flNumerator ) || flNumerator <= 0.0f )
		return flDefault;

	if ( !SourceSharp_JetIsUsable( flDenominator ) || flDenominator <= 0.0f )
		return flDefault;

	const float flResult = flNumerator / flDenominator;

	// Overflow, underflow to zero, or a denormal too small to divide by later.
	if ( !SourceSharp_JetIsUsable( flResult ) || flResult <= 0.0f )
		return flDefault;

	return flResult;
}

//-----------------------------------------------------------------------------
// C_SteamJet::m_Lifetime — seconds a puff lives.
//-----------------------------------------------------------------------------
inline float SourceSharp_SteamJetLifetime( float flJetLength, float flSpeed )
{
	return SourceSharp_JetQuotient( flJetLength, flSpeed,
	                                SOURCESHARP_STEAMJET_DEFAULT_LENGTH,
	                                SOURCESHARP_STEAMJET_DEFAULT_SPEED );
}

//-----------------------------------------------------------------------------
// C_SmokeStack::m_InvLifetime — the RECIPROCAL, because the renderer multiplies
// by it rather than dividing. Same rule, arguments the other way up: here the
// SPEED is the numerator and the LENGTH is the denominator, so it is a jet of no
// length rather than a jet of no speed that makes this infinite.
//
// This is the one the vertex assert came out of. A map-placed env_smokestack
// with no JetLength key gave m_InvLifetime = speed/0 = +Inf, the renderer
// FLerped the particle's size with it, and the size reached Position3f.
//-----------------------------------------------------------------------------
inline float SourceSharp_SmokeStackInvLifetime( float flSpeed, float flJetLength )
{
	return SourceSharp_JetQuotient( flSpeed, flJetLength,
	                                SOURCESHARP_SMOKESTACK_DEFAULT_SPEED,
	                                SOURCESHARP_SMOKESTACK_DEFAULT_LENGTH );
}

#endif // SOURCESHARP_JET_LIFETIME_H
