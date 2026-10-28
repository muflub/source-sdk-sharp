//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The server's effects interface, server.so's own type: no engine reads it. Implemented
// by CEffectsServer in shell_te_forwards.cpp.

#pragma once

namespace ss
{

class IEffects
{
public:
	// Smoke sprites at origin: scale is sprite scale * 10.
	virtual void Smoke( const Vector &origin, int modelIndex, float scale, float framerate ) = 0;

	virtual void Sparks( const Vector &position, int nMagnitude = 1, int nTrailLength = 1,
						 const Vector *pvecDir = NULL ) = 0;

protected:
	~IEffects() {}
};

// The server's effects object (shell_te_forwards.cpp). Never NULL.
IEffects *Effects();

} // namespace ss
