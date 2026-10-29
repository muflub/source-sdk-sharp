//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON d  a layout the engine reads at fixed offsets
// The native side of the managed client shadow manager. The engine keeps pointers to the
// shadow materials, render targets and flashlight textures, and the forwarder library
// cannot allocate them, so they are held here by id. Also the material system's restore
// callback, the kClientSystems[] row, the shells' queue and the library's environment.
#include <stdio.h>
#include <stdlib.h>
#include "tier0/dbg.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "materialsystem/imesh.h"
#include "iviewrender.h"
#include "view_shared.h"
#include "tier0/icommandline.h"
#include "tier1/utlvector.h"
#include "bitmap/imageformat.h"
#include "mathlib/vector.h"
#include "particles/particles.h"
#include "tier0/platform.h"
#include "tier1/refcount.h"
#include "tier1/strtools.h"
#include "basehandle.h"
#include "cdll_client_int.h"
#include "const.h"
#include "iclientunknown.h"
#include "ivrenderview.h"
#include "texture_group_names.h"
#include "imanaged_areas_client.h"
#include "shell_clientsystem.h"
#include "shell_cliententitylist.h"
#include "shell_standalone_client.h"
#include "slot_table.h"
#include "shell_render_trace_client.h"
#include "shell_shadows_client.h"
#include "shadow_pending_queue.h"
#include "fwd_shadows.h"
#include "sourcesharp_entrypoints.h"

namespace
{

// The references, by id (0 is none). A freed slot is reused; a live slot's id is never
// handed out.
template <typename T>
class CReferenceTable
{
public:
	int32_t Add(T* p)
	{
		for (int i = 0; i < m_Slots.Count(); ++i)
		{
			if (!m_Slots[i])
			{
				m_Slots[i] = p;
				return i + 1;
			}
		}
		m_Slots.AddToTail(p);
		return m_Slots.Count();
	}

	T* Find(int32_t id) const { return (id >= 1 && id <= m_Slots.Count()) ? m_Slots[id - 1] : NULL; }

	T* Remove(int32_t id)
	{
		T* p = Find(id);
		if (p)
			m_Slots[id - 1] = NULL;
		return p;
	}

private:
	CUtlVector<T*> m_Slots;
};

CReferenceTable<CMaterialReference> s_Materials;
CReferenceTable<CTextureReference> s_Textures;

// Held for the DLL's life so the pointer the engine keeps in a FlashlightState_t stays
// referenced.
const int kMaxFlashlightTextures = 4;
CTextureReference* s_FlashlightTextures[kMaxFlashlightTextures];
char s_szFlashlightTextureNames[kMaxFlashlightTextures][MAX_PATH];
int s_nFlashlightTextures = 0;

int32_t InitMaterial(const char* pszName, const char* pszGroup)
{
	if (!pszName || !pszGroup)
		return 0;
	CMaterialReference* p = new CMaterialReference;
	p->Init(pszName, pszGroup);
	return s_Materials.Add(p);
}

void ShutdownMaterial(int32_t id)
{
	if (CMaterialReference* p = s_Materials.Remove(id))
	{
		p->Shutdown();
		delete p;
	}
}

IMaterial* Material(int32_t id)
{
	CMaterialReference* p = s_Materials.Find(id);
	return p ? (IMaterial*)*p : NULL;
}

int32_t InitRenderTarget(const char* pszName, int32_t w, int32_t h, int32_t nSizeMode, int32_t nFormat, int32_t nDepth,
						 int32_t bHdr)
{
	if (!pszName)
		return 0;
	char szName[MAX_PATH];
	V_strncpy(szName, pszName, sizeof(szName));
	CTextureReference* p = new CTextureReference;
	p->InitRenderTarget(w, h, (RenderTargetSizeMode_t)nSizeMode, (ImageFormat)nFormat,
						(MaterialRenderTargetDepth_t)nDepth, bHdr != 0, szName);
	return s_Textures.Add(p);
}

void ShutdownTexture(int32_t id)
{
	if (CTextureReference* p = s_Textures.Remove(id))
	{
		p->Shutdown();
		delete p;
	}
}

ITexture* Texture(int32_t id)
{
	CTextureReference* p = s_Textures.Find(id);
	return p ? (ITexture*)*p : NULL;
}

ITexture* FlashlightTexture(int32_t nIndex)
{
	return (nIndex >= 0 && nIndex < s_nFlashlightTextures) ? (ITexture*)*s_FlashlightTextures[nIndex] : NULL;
}

// The queue rides in PreRender's arguments; any other call hands it over first, so its
// entries reach the manager in order before the call's own work.
CShadowPendingQueue s_Queue;

void HandOverToManaged(const int32_t* pEntries, int32_t nCount, void*)
{
	if (IManagedShadowsClient* pManaged = ManagedShadowsClient())
		pManaged->Pending(pEntries, nCount);
}

IManagedShadowsClient* Target()
{
	IManagedShadowsClient* pManaged = ManagedShadowsClient();
	if (!pManaged)
	{
		s_Queue.Clear();
		return NULL;
	}
	if (s_Queue.Count() > 0)
		s_Queue.HandOver();
	return pManaged;
}

// The manager for a call that carries the queue; with none, the queue is dropped.
IManagedShadowsClient* Carrier()
{
	IManagedShadowsClient* pManaged = ManagedShadowsClient();
	if (!pManaged)
		s_Queue.Clear();
	return pManaged;
}

void ShadowRestoreFunc(int)
{
	if (IManagedShadowsClient* pManaged = Target())
		pManaged->RestoreRenderState();
}

void AddRestoreFunc()
{
	materials->AddRestoreFunc(ShadowRestoreFunc);
}

void RemoveRestoreFunc()
{
	materials->RemoveRestoreFunc(ShadowRestoreFunc);
}

int32_t ToolsMode()
{
	return CommandLine()->CheckParm("-tools") ? 1 : 0;
}

IClientRenderable* RenderableOf(uint32_t hEntity)
{
	const CBaseHandle h((int)(hEntity & ENT_ENTRY_MASK), (int)(hEntity >> NUM_SERIAL_NUM_SHIFT_BITS));
	IClientUnknown* pUnknown = NULL;
	if (C_StandaloneEntity* pShell = SourceSharp_ClientShellFromHandle(h))
		pUnknown = pShell->GetIClientUnknown();
	if (!pUnknown)
		pUnknown = SourceSharp_LegacyListUnknownFromHandle(h);
	return pUnknown ? pUnknown->GetClientRenderable() : NULL;
}

void UpdateShadowDepthTexture(ITexture* pRenderTarget, ITexture* pDepthTexture, const CViewSetup& shadowView)
{
	view->UpdateShadowDepthTexture(pRenderTarget, pDepthTexture, shadowView);
}

// cl_drawshadowtexture's quad: one w by h quad, counter-clockwise from the origin.
void DrawTextureQuad(IMaterial* pMaterial, float w, float h)
{
	CMatRenderContextPtr pRenderContext(materials);
	pRenderContext->Bind(pMaterial);
	IMesh* pMesh = pRenderContext->GetDynamicMesh(true);

	const float kCorners[4][2] = {{0.0f, 0.0f}, {1.0f, 0.0f}, {1.0f, 1.0f}, {0.0f, 1.0f}};
	CMeshBuilder mesh;
	mesh.Begin(pMesh, MATERIAL_QUADS, 1);
	for (int i = 0; i < 4; ++i)
	{
		mesh.Position3f(kCorners[i][0] * w, kCorners[i][1] * h, 0.0f);
		mesh.TexCoord2f(0, kCorners[i][0], kCorners[i][1]);
		mesh.Color4ub(0, 0, 0, 0);
		mesh.AdvanceVertex();
	}
	mesh.End();
	pMesh->Draw();
}

SSShadowEnv s_Env;

void BuildEnv()
{
	s_Env.ppShadowMgr = &shadowmgr;
	s_Env.ppStaticPropMgr = &staticpropmgr;
	s_Env.ppMaterials = &materials;
	s_Env.ppHardwareConfig = &g_pMaterialSystemHardwareConfig;
	s_Env.ppModelRender = &modelrender;
	s_Env.ppRender = &render;
	s_Env.ppEngine = &engine;
	s_Env.ppModelInfo = &modelinfo;
	s_Env.ppDebugOverlay = &debugoverlay;
	s_Env.ppGlobals = reinterpret_cast<CGlobalVarsBase* const*>(&gpGlobals);
	s_Env.pfnInitMaterial = InitMaterial;
	s_Env.pfnShutdownMaterial = ShutdownMaterial;
	s_Env.pfnMaterial = Material;
	s_Env.pfnInitRenderTarget = InitRenderTarget;
	s_Env.pfnShutdownTexture = ShutdownTexture;
	s_Env.pfnTexture = Texture;
	s_Env.pfnFlashlightTexture = FlashlightTexture;
	s_Env.pfnAddRestoreFunc = AddRestoreFunc;
	s_Env.pfnRemoveRestoreFunc = RemoveRestoreFunc;
	s_Env.pfnToolsMode = ToolsMode;
	s_Env.pfnRenderableOf = RenderableOf;
	s_Env.pfnUpdateShadowDepthTexture = UpdateShadowDepthTexture;
	s_Env.pfnDrawTextureQuad = DrawTextureQuad;
}

// The five stages stock's CClientShadowMgr had bodies for, each one managed call. With
// no managed load Init does not refuse.
class CShadowMgrGameSystem final : public CClientSystemPerFrame
{
public:
	CShadowMgrGameSystem()
		: CClientSystemPerFrame("CClientShadowMgr")
	{
	}

	char const* Name() override { return "CClientShadowMgr"; }

	bool Init() override
	{
		IManagedShadowsClient* pManaged = Target();
		return !pManaged || pManaged->Init() != 0;
	}

	void Shutdown() override
	{
		if (IManagedShadowsClient* pManaged = Target())
			pManaged->Shutdown();
	}

	void LevelInitPreEntity() override
	{
		if (IManagedShadowsClient* pManaged = Target())
			pManaged->LevelInitPreEntity();
	}

	void LevelShutdownPostEntity() override
	{
		if (IManagedShadowsClient* pManaged = Target())
			pManaged->LevelShutdownPostEntity();
	}

	void PreRender() override { ShellShadows_PreRender(); }
};

CShadowMgrGameSystem s_GameSystem;

} // namespace

// Built on the first ask: the engine interfaces it points at are set after load.
void* ShadowEnv_Acquire()
{
	if (!s_Env.pfnRenderableOf)
		BuildEnv();
	s_Queue.SetSink(HandOverToManaged, NULL);
	return &s_Env;
}

void ShellShadows_MarkDirty(const CBaseHandle& hEntity, bool bTextureDirty)
{
	s_Queue.SetSink(HandOverToManaged, NULL);
	s_Queue.Dirty((uint32_t)hEntity.ToInt(), bTextureDirty ? 1 : 0);
}

void ShellShadows_Refresh(const CBaseHandle& hEntity, int nShadowType, int nShadowFlags)
{
	s_Queue.SetSink(HandOverToManaged, NULL);
	s_Queue.Refresh((uint32_t)hEntity.ToInt(), (nShadowType & 0xFF) | ((nShadowFlags & 0xFFFF) << 8));
}

void ShellShadows_Destroy(const CBaseHandle& hEntity)
{
	s_Queue.SetSink(HandOverToManaged, NULL);
	s_Queue.Destroy((uint32_t)hEntity.ToInt());
}

int ShellShadows_PendingCount()
{
	return s_Queue.Count();
}

const int32_t* ShellShadows_PendingEntries()
{
	return s_Queue.Entries();
}

// Each op is one managed call inside the render trace's shadow mark.
void ShellShadows_AdvanceFrame()
{
	RenderTrace::ShadowEnter("shadow.AdvanceFrame");
	if (IManagedShadowsClient* pManaged = Carrier())
	{
		int32_t pPending[CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX];
		const int32_t nPending = s_Queue.Take(pPending);
		pManaged->AdvanceFrame(pPending, nPending);
	}
	RenderTrace::ShadowLeave();
}

void ShellShadows_PreRender()
{
	RenderTrace::ShadowEnter("shadow.PreRender");
	if (IManagedShadowsClient* pManaged = Carrier())
	{
		int32_t pPending[CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX];
		const int32_t nPending = s_Queue.Take(pPending);
		pManaged->PreRender(pPending, nPending);
	}
	RenderTrace::ShadowLeave();
}

void ShellShadows_ComputeShadowTextures(int nLeafCount, const LeafIndex_t* pLeafList)
{
	char szLine[64];
	V_snprintf(szLine, sizeof(szLine), "shadow.ComputeShadowTextures\tleaves=%d", nLeafCount);
	RenderTrace::ShadowEnter(szLine);
	if (IManagedShadowsClient* pManaged = Carrier())
	{
		int32_t pPending[CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX];
		const int32_t nPending = s_Queue.Take(pPending);
		pManaged->ComputeShadowTextures(pLeafList, nLeafCount, pPending, nPending);
	}
	RenderTrace::ShadowLeave();
}

void ShellShadows_ComputeShadowDepthTextures(const CViewSetup& view)
{
	RenderTrace::ShadowEnter("shadow.ComputeShadowDepthTextures");
	if (IManagedShadowsClient* pManaged = Carrier())
	{
		const float flView[10] = {view.origin.x, view.origin.y, view.origin.z, view.angles.x, view.angles.y,
								  view.angles.z, view.zNear,    view.zFar,     view.fov,      view.m_flAspectRatio};
		int32_t pPending[CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX];
		const int32_t nPending = s_Queue.Take(pPending);
		pManaged->ComputeShadowDepthTextures(flView, 10, pPending, nPending);
	}
	RenderTrace::ShadowLeave();
}

void ShellShadows_UnlockAllShadowDepthTextures()
{
	RenderTrace::ShadowNote("shadow.UnlockAllShadowDepthTextures");
	if (IManagedShadowsClient* pManaged = Carrier())
	{
		int32_t pPending[CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX];
		const int32_t nPending = s_Queue.Take(pPending);
		pManaged->UnlockAllShadowDepthTextures(pPending, nPending);
	}
}

void ShellShadows_RenderShadowTexture(int w, int h)
{
	if (IManagedShadowsClient* pManaged = Target())
		pManaged->RenderShadowTexture(w, h);
}

CClientSystem* ClientShadowMgrGameSystem()
{
	return &s_GameSystem;
}

// The texture table is the environment's, so a FlashlightState_t the manager hands the
// engine names the same reference.
int32_t SourceSharp_FlashlightTexture(const char* pszName)
{
	if (!pszName || !pszName[0])
		return -1;

	for (int i = 0; i < s_nFlashlightTextures; i++)
	{
		if (!V_strcmp(s_szFlashlightTextureNames[i], pszName))
			return i;
	}

	if (s_nFlashlightTextures >= kMaxFlashlightTextures)
		return -1;

	const int i = s_nFlashlightTextures++;
	V_strncpy(s_szFlashlightTextureNames[i], pszName, sizeof(s_szFlashlightTextureNames[i]));
	s_FlashlightTextures[i] = new CTextureReference;
	s_FlashlightTextures[i]->Init(pszName, TEXTURE_GROUP_OTHER, true);
	return i;
}

int32_t SourceSharp_FlashlightSupportsBorderColor()
{
	return (g_pMaterialSystemHardwareConfig && g_pMaterialSystemHardwareConfig->SupportsBorderColor()) ? 1 : 0;
}
