//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Whether an entity handle is the world. worldspawn is on neither entity hierarchy, so
// both arms of the partition answer NULL for it, and NULL also means "hit nothing".
// The world is a singleton, so this is an identity compare against it; callers ask it
// before the two-armed partition.
#pragma once

class IHandleEntity;

// The rule, with the singleton passed in so it can be tested. Both pointers are NULL
// during level load, so a plain == would call every trace that hit nothing the world.
inline bool SourceSharp_IsTheWorldHandle(const IHandleEntity* pHandleEntity, const IHandleEntity* pWorldHandle)
{
	// Nothing is not the world, even when there is no world.
	if (!pHandleEntity || !pWorldHandle)
		return false;

	return pHandleEntity == pWorldHandle;
}

#ifdef CLIENT_DLL
// Whether this handle is the world; NULL answers false. The client's, over its own
// singleton; the server's callers compare against the level's world they are handed.
bool SourceSharp_IsTheWorld(const IHandleEntity* pHandleEntity);
#endif
