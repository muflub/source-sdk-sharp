//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The detail props' executor as one object, a member of the client DLL object. Every
// decision (parse, fades, sorts, quads, batches) is managed's; this holds the model props'
// renderables, the leaf system's tables, and the materials, render context and mesh that
// carry out managed's draws. With no managed load nothing is drawn, as for a level without
// detail props. The client systems table, the view executor and the leaf system are handed it.

#pragma once

#include "detailobjectsystem.h"
#include "bsptreedata.h"
#include "materialsystem/imesh.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "tier1/convar.h"
#include "utlvector.h"

#include "shell_managed_areas_client.h"

class CClientEngineInterfaces;
class CViewCameras;
class CShellDetailModel;

class CShellDetailProps final : public IDetailObjectSystem, public ISpatialLeafEnumerator
{
public:
	CShellDetailProps(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
					  const CViewCameras& cameras)
		: m_Areas(areas)
		, m_Engines(engines)
		, m_Cameras(cameras)
	{
	}

	CShellDetailProps(const CShellDetailProps&) = delete;
	CShellDetailProps& operator=(const CShellDetailProps&) = delete;

	// NULL outside a managed load.
	IManagedDetailPropsClient* Managed() const { return m_Areas.m_pDetailPropsClient; }

	virtual char const* Name() OVERRIDE;

	virtual void PostInit() OVERRIDE {}

	virtual void Shutdown() OVERRIDE {}

	virtual void OnSave() OVERRIDE {}

	virtual void OnRestore() OVERRIDE {}

	virtual void SafeRemoveIfDesired() OVERRIDE {}

	virtual bool IsPerFrame() OVERRIDE;

	virtual bool Init() OVERRIDE;

	virtual void LevelInitPreEntity() OVERRIDE;

	virtual void LevelInitPostEntity() OVERRIDE;

	virtual void LevelShutdownPreEntity() OVERRIDE;

	virtual void LevelShutdownPostEntity() OVERRIDE;

	virtual IClientRenderable* GetDetailModel(int idx) OVERRIDE;

	virtual void BuildDetailObjectRenderLists(const Vector& vViewOrigin) OVERRIDE;

	virtual void RenderOpaqueDetailObjects(int nLeafCount, LeafIndex_t* pLeafList) OVERRIDE;

	virtual void BeginTranslucentDetailRendering() OVERRIDE;

	virtual void RenderTranslucentDetailObjects(const Vector& viewOrigin, const Vector& viewForward,
												const Vector& viewRight, const Vector& viewUp, int nLeafCount,
												LeafIndex_t* pLeafList) OVERRIDE;

	virtual void RenderTranslucentDetailObjectsInLeaf(const Vector& viewOrigin, const Vector& viewForward,
													  const Vector& viewRight, const Vector& viewUp, int nLeaf,
													  const Vector* pVecClosestPoint) OVERRIDE;

	// A leaf in the view's sphere: marked, and its model props' managed states applied.
	virtual bool EnumerateLeaf(int leaf, intp context) OVERRIDE;

	void Precache(bool b);

	void Object(int i, void* pModel, const float* pOrigin, const float* pAngles);

	void Run(int leaf, int first, int count, bool bFast);

	void Proxy(void* pModel);

	void Model(int i, unsigned char alpha, bool bSetAngles, const float* pAngles, const float* pColor);

	void ModelAlpha(int object, unsigned char alpha);

	void Begin(int quads);

	void Vertex(const float* pXYZ, const unsigned char* pRGBA, const float* pST);

	void EndDraw();

private:
	enum
	{
		kFastList = 0,
		kOldList = 1,
		kFastInLeaf = 2,
		kOldInLeaf = 3
	};

	// One of stock's four translucent draws. A mesh with no room ends the draw with the
	// matrix still pushed, as stock's return did.
	void Draw(int nKind, const Vector& viewOrigin, const Vector& viewForward, const Vector& viewRight,
			  const Vector& viewUp, const int32_t* pLeaves, int nLeaves, const Vector* pClosest);

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	const CViewCameras& m_Cameras;

	struct State_t
	{
		unsigned char alpha;
		bool bSetAngles;
		float angles[3];
	};

	CUtlVector<CShellDetailModel*> m_Objects; // by object index; NULL for a sprite
	CUtlVector<CShellDetailModel*> m_Models; // the model props, by ordinal
	CUtlVector<State_t> m_States; // this view's, by ordinal
	CMaterialReference m_SpriteMaterial;
	CMaterialReference m_WireframeMaterial;
	ConVarRef m_matFullbright{"mat_fullbright", true};
	CMeshBuilder m_MeshBuilder;
	IMesh* m_pMesh = NULL;
	int m_nBuildWorldListNumber = 0;
	bool m_bPrecache = false;
};
