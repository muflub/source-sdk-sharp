//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the server's decal names, as server.so reaches them
//
// plan_server_novalve.md P5 (lane SP). game/shared/decals.h's
// IDecalEmitterSystem is reached in server.so only through the `decalsystem`
// global, and no engine reads it: the decal table is SourceSharp.Server's
// DecalEmitter, and shell_decals.cpp answers through the managed resources
// area. So the server's own entry points live in namespace ss, on public/
// headers alone, and an R5 translation unit calls these.
//
// shell_decals.cpp defines them; its IDecalEmitterSystem forwards here until
// the last caller of `decalsystem` has switched.
//=============================================================================//

#ifndef SOURCESHARP_SS_DECALS_H
#define SOURCESHARP_SS_DECALS_H
#ifdef _WIN32
#pragma once
#endif

namespace ss
{

// The decal's index in the engine's decal precache list, or -1 (also for an
// empty or NULL name).
int DecalIndexForName( const char *pszDecalName );

// DecalNameForIndex and DecalTranslateForGameMaterial went in the server gc
// cull (2026-10-01): nothing called them.

} // namespace ss

#endif // SOURCESHARP_SS_DECALS_H
