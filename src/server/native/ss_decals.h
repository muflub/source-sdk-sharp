//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The server's decal lookups; the decal table itself is managed.

#pragma once

namespace ss
{

// The decal's index in the engine's decal precache list, or -1 (also for an
// empty or NULL name).
int DecalIndexForName( const char *pszDecalName );

} // namespace ss
