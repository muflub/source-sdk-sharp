//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// detailprops_stub/'s stand-in for game/client/detailobjectsystem.h: the same
// interface, over a game system that declares only the members the harness
// calls (upstream's IGameSystem has more, which CDetailObjectSystem defines as
// plain members either way).
#pragma once

#include "detail_harness.h"

class IGameSystem
{
public:
	virtual ~IGameSystem() {}

	virtual bool Init() = 0;
	virtual void LevelInitPreEntity() = 0;
	virtual void LevelInitPostEntity() = 0;
	virtual void LevelShutdownPreEntity() = 0;
	virtual void LevelShutdownPostEntity() = 0;
};

abstract_class IDetailObjectSystem : public IGameSystem
{
public:
	virtual IClientRenderable* GetDetailModel(int idx) = 0;
	virtual void BuildDetailObjectRenderLists(const Vector& vViewOrigin) = 0;
	virtual void RenderOpaqueDetailObjects(int nLeafCount, LeafIndex_t* pLeafList) = 0;
	virtual void BeginTranslucentDetailRendering() = 0;
	virtual void RenderTranslucentDetailObjects(const Vector& viewOrigin, const Vector& viewForward,
												const Vector& viewRight, const Vector& viewUp, int nLeafCount,
												LeafIndex_t* pLeafList) = 0;
	virtual void RenderTranslucentDetailObjectsInLeaf(const Vector& viewOrigin, const Vector& viewForward,
													  const Vector& viewRight, const Vector& viewUp, int nLeaf,
													  const Vector* pVecClosestPoint) = 0;
};

IDetailObjectSystem* DetailObjectSystem();
