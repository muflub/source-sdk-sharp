//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The time a puff takes to cross an env_steam or env_smokestack jet, always finite and
// strictly positive. A zero or NaN here reaches the particle size the mesh builder is
// handed, and NaN passes the renderers' alpha and death tests, so bad particles never die.

#pragma once

// Stock's constructor defaults for each class, used when a map leaves the keys unset.
#define SOURCESHARP_STEAMJET_DEFAULT_LENGTH     80.0f
#define SOURCESHARP_STEAMJET_DEFAULT_SPEED     120.0f

#define SOURCESHARP_SMOKESTACK_DEFAULT_LENGTH  180.0f
#define SOURCESHARP_SMOKESTACK_DEFAULT_SPEED    30.0f

// Finite, without mathlib, so the header compiles into a test binary with no engine.
inline bool SourceSharp_JetIsUsable( float f )
{
	if ( f != f )
		return false;                       // NaN

	// A float's largest finite magnitude. Anything at or past it is an infinity.
	const float flHuge = 3.402823466e+38f;

	return ( f <= flHuge ) && ( f >= -flHuge );
}

// A quotient that is always finite and strictly positive. Non-positive inputs, and an
// overflow or underflow in the result, all answer the caller's default.
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

// C_SteamJet::m_Lifetime: seconds a puff lives.
inline float SourceSharp_SteamJetLifetime( float flJetLength, float flSpeed )
{
	return SourceSharp_JetQuotient( flJetLength, flSpeed,
	                                SOURCESHARP_STEAMJET_DEFAULT_LENGTH,
	                                SOURCESHARP_STEAMJET_DEFAULT_SPEED );
}

// C_SmokeStack::m_InvLifetime: the reciprocal, because the renderer multiplies by it.
inline float SourceSharp_SmokeStackInvLifetime( float flSpeed, float flJetLength )
{
	return SourceSharp_JetQuotient( flSpeed, flJetLength,
	                                SOURCESHARP_SMOKESTACK_DEFAULT_SPEED,
	                                SOURCESHARP_SMOKESTACK_DEFAULT_LENGTH );
}
