//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
//=============================================================================

#ifndef ISCENEFILECACHE_H
#define ISCENEFILECACHE_H
#ifdef _WIN32
#pragma once
#endif

#include "interface.h"
#include "appframework/IAppSystem.h"

// the file cache can support persisting some calcs
struct SceneCachedData_t
{
	unsigned int	msecs;
	int				numSounds;
	int				sceneId;
};

class ISceneFileCache : public IAppSystem
{
public:

	// async implemenation
	virtual void unused_GetSceneBufferSize() = 0;
	virtual bool		GetSceneData( char const *filename, byte *buf, size_t bufsize ) = 0;

	// persisted scene data, returns true if valid, false otherwise
	virtual void unused_GetSceneCachedData() = 0;
	virtual void unused_GetSceneCachedSound() = 0;
	virtual void unused_GetSceneString() = 0;

	// Physically reloads image from disk
	virtual void unused_Reload() = 0;
};

#define SCENE_FILE_CACHE_INTERFACE_VERSION "SceneFileCache002"

#endif // ISCENEFILECACHE_H
