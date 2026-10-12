//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the server's effects, as server.so owns them
//
// plan_server_novalve.md P5. game/shared/IEffects.h's IEffects is reached in
// server.so only through the g_pEffects global: "IEffects001" is in no engine
// binary (all 90 .so files under Source SDK Base 2013 Multiplayer searched; it
// is only in the stock game DLLs, each exposing its own copy), so no engine
// reads this vtable and the type is server.so's own. Per the plan's naming rule
// it lives in namespace ss, on public/ headers alone.
//
// Only the five methods a server translation unit calls are declared: Smoke,
// Sparks, Dust, MuzzleFlash and MetalSparks. There is no IPredictionSystem base:
// the suppress-host rule those methods applied is SourceSharpTe_SuppressFilter
// (shell_tempents.cpp), which nothing reaches through this interface.
//
// shell_te_forwards.cpp's CEffectsServer implements it; ss::Effects() answers
// that object. The Valve IEffects, its g_pEffects global and its IEffects001
// expose left server.so with their last caller (P5 lane SP).
//=============================================================================//

#ifndef SOURCESHARP_SS_EFFECTS_H
#define SOURCESHARP_SS_EFFECTS_H
#ifdef _WIN32
#pragma once
#endif

#include "mathlib/vector.h"

namespace ss
{

class IEffects
{
public:
	// Smoke sprites at origin: scale is sprite scale * 10.
	virtual void Smoke( const Vector &origin, int modelIndex, float scale, float framerate ) = 0;

	virtual void Sparks( const Vector &position, int nMagnitude = 1, int nTrailLength = 1,
	                     const Vector *pvecDir = NULL ) = 0;

	virtual void Dust( const Vector &pos, const Vector &dir, float size, float speed ) = 0;

	virtual void MuzzleFlash( const Vector &vecOrigin, const QAngle &vecAngles, float flScale,
	                          int iType ) = 0;

	// Like a ricochet, without the sound.
	virtual void MetalSparks( const Vector &position, const Vector &direction ) = 0;

protected:
	~IEffects() {}
};

// The server's effects object (shell_te_forwards.cpp). Never NULL.
IEffects *Effects();

} // namespace ss

#endif // SOURCESHARP_SS_EFFECTS_H
