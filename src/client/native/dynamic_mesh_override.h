//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The shader API's dynamic mesh taken off a vertex buffer override before a level's
// meshes are freed.
#pragma once

#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialsystem.h"

// BindBatch leaves the dynamic mesh holding a world vertex buffer as its override, and the
// next buffered draw flushes through it before setting a format: after the world's meshes
// are freed, a read of freed memory (crash-uidraw). A fetch with no override drops it while
// the buffer lives. Single-threaded at a level change; the queued context ignores it.
inline void SourceSharp_DropDynamicMeshOverride(IMaterialSystem* pMaterials)
{
	if (!pMaterials)
		return;
	IMaterial* pWhite = pMaterials->FindMaterial("vgui/white", TEXTURE_GROUP_OTHER, false);
	if (!pWhite || pWhite->IsErrorMaterial())
		return;
	CMatRenderContextPtr pRenderContext(pMaterials);
	pRenderContext->GetDynamicMesh(true, NULL, NULL, pWhite);
}
