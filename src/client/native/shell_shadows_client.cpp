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

} // namespace

int32_t CShadowsClient::InitMaterial(const char* pszName, const char* pszGroup)
{
	if (!pszName || !pszGroup)
		return 0;
	CMaterialReference* p = new CMaterialReference;
	p->Init(pszName, pszGroup);
	return m_Materials.Add(p);
}

void CShadowsClient::ShutdownMaterial(int32_t id)
{
	if (CMaterialReference* p = m_Materials.Remove(id))
	{
		p->Shutdown();
		delete p;
	}
}

IMaterial* CShadowsClient::Material(int32_t id)
{
	CMaterialReference* p = m_Materials.Find(id);
	return p ? (IMaterial*)*p : NULL;
}

int32_t CShadowsClient::InitRenderTarget(const SSShadowRenderTarget* pTarget)
{
	if (!pTarget || !pTarget->pszName)
		return 0;
	char szName[MAX_PATH];
	V_strncpy(szName, pTarget->pszName, sizeof(szName));
	CTextureReference* p = new CTextureReference;
	p->InitRenderTarget(pTarget->nWidth, pTarget->nHeight, (RenderTargetSizeMode_t)pTarget->nSizeMode,
						(ImageFormat)pTarget->nFormat, (MaterialRenderTargetDepth_t)pTarget->nDepth, pTarget->bHdr != 0,
						szName);
	return m_Textures.Add(p);
}

void CShadowsClient::ShutdownTexture(int32_t id)
{
	if (CTextureReference* p = m_Textures.Remove(id))
	{
		p->Shutdown();
		delete p;
	}
}

ITexture* CShadowsClient::Texture(int32_t id)
{
	CTextureReference* p = m_Textures.Find(id);
	return p ? (ITexture*)*p : NULL;
}

ITexture* CShadowsClient::FlashlightTextureAt(int32_t nIndex)
{
	return (nIndex >= 0 && nIndex < m_nFlashlightTextures) ? (ITexture*)*m_FlashlightTextures[nIndex] : NULL;
}

// The queue's sink takes a context.
void CShadowsClient::HandOverToManaged(const int32_t* pEntries, int32_t nCount, void* pContext)
{
	if (IManagedShadowsClient* pManaged = static_cast<CShadowsClient*>(pContext)->Managed())
		pManaged->Pending(pEntries, nCount);
}

IManagedShadowsClient* CShadowsClient::Target()
{
	IManagedShadowsClient* pManaged = Managed();
	if (!pManaged)
	{
		m_Queue.Clear();
		return NULL;
	}
	if (m_Queue.Count() > 0)
		m_Queue.HandOver();
	return pManaged;
}

// The manager for a call that carries the queue; with none, the queue is dropped.
IManagedShadowsClient* CShadowsClient::Carrier()
{
	IManagedShadowsClient* pManaged = Managed();
	if (!pManaged)
		m_Queue.Clear();
	return pManaged;
}

void CShadowsClient::RestoreRenderState(int)
{
	if (IManagedShadowsClient* pManaged = Target())
		pManaged->RestoreRenderState();
}

void CShadowsClient::AddRestoreFunc()
{
	materials->AddRestoreFunc(m_RestoreFunc.Get());
}

void CShadowsClient::RemoveRestoreFunc()
{
	materials->RemoveRestoreFunc(m_RestoreFunc.Get());
}

static int32_t ToolsMode()
{
	return CommandLine()->CheckParm("-tools") ? 1 : 0;
}

static IClientRenderable* RenderableOf(uint32_t hEntity)
{
	const CBaseHandle h((int)(hEntity & ENT_ENTRY_MASK), (int)(hEntity >> NUM_SERIAL_NUM_SHIFT_BITS));
	IClientUnknown* pUnknown = NULL;
	if (C_StandaloneEntity* pShell = SourceSharp_ClientShellFromHandle(h))
		pUnknown = pShell->GetIClientUnknown();
	if (!pUnknown)
		pUnknown = SourceSharp_LegacyListUnknownFromHandle(h);
	return pUnknown ? pUnknown->GetClientRenderable() : NULL;
}

static void UpdateShadowDepthTexture(ITexture* pRenderTarget, ITexture* pDepthTexture, const CViewSetup& shadowView)
{
	view->UpdateShadowDepthTexture(pRenderTarget, pDepthTexture, shadowView);
}

// cl_drawshadowtexture's quad: one w by h quad, counter-clockwise from the origin.
static void DrawTextureQuad(IMaterial* pMaterial, float w, float h)
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

void CShadowsClient::BuildEnv()
{
	m_Env.ppShadowMgr = &shadowmgr;
	m_Env.ppStaticPropMgr = &staticpropmgr;
	m_Env.ppMaterials = &materials;
	m_Env.ppHardwareConfig = &g_pMaterialSystemHardwareConfig;
	m_Env.ppModelRender = &modelrender;
	m_Env.ppRender = &render;
	m_Env.ppEngine = &engine;
	m_Env.ppModelInfo = &modelinfo;
	m_Env.ppDebugOverlay = &debugoverlay;
	m_Env.ppGlobals = reinterpret_cast<CGlobalVarsBase* const*>(&gpGlobals);
	m_Env.pfnInitMaterial = m_InitMaterial.Get();
	m_Env.pfnShutdownMaterial = m_ShutdownMaterial.Get();
	m_Env.pfnMaterial = m_Material.Get();
	m_Env.pfnInitRenderTarget = m_InitRenderTarget.Get();
	m_Env.pfnShutdownTexture = m_ShutdownTexture.Get();
	m_Env.pfnTexture = m_Texture.Get();
	m_Env.pfnFlashlightTexture = m_FlashlightTexture.Get();
	m_Env.pfnAddRestoreFunc = m_AddRestoreFunc.Get();
	m_Env.pfnRemoveRestoreFunc = m_RemoveRestoreFunc.Get();
	m_Env.pfnToolsMode = ToolsMode;
	m_Env.pfnRenderableOf = RenderableOf;
	m_Env.pfnUpdateShadowDepthTexture = UpdateShadowDepthTexture;
	m_Env.pfnDrawTextureQuad = DrawTextureQuad;
}

// The five stages stock's CClientShadowMgr had bodies for, each one managed call. With
// no managed load Init does not refuse.
char const* CShadowsClient::CSystemRow::Name()
{
	return "CClientShadowMgr";
}

bool CShadowsClient::CSystemRow::Init()
{
	IManagedShadowsClient* pManaged = m_Shadows.Target();
	return !pManaged || pManaged->Init() != 0;
}

void CShadowsClient::CSystemRow::Shutdown()
{
	if (IManagedShadowsClient* pManaged = m_Shadows.Target())
		pManaged->Shutdown();
}

void CShadowsClient::CSystemRow::LevelInitPreEntity()
{
	if (IManagedShadowsClient* pManaged = m_Shadows.Target())
		pManaged->LevelInitPreEntity();
}

void CShadowsClient::CSystemRow::LevelShutdownPostEntity()
{
	if (IManagedShadowsClient* pManaged = m_Shadows.Target())
		pManaged->LevelShutdownPostEntity();
}

void CShadowsClient::CSystemRow::PreRender()
{
	m_Shadows.PreRender();
}

// Built on the first ask: the engine interfaces it points at are set after load.
void* CShadowsClient::AcquireEnv()
{
	if (!m_Env.pfnRenderableOf)
		BuildEnv();
	m_Queue.SetSink(HandOverToManaged, this);
	return &m_Env;
}

void CShadowsClient::MarkDirty(const CBaseHandle& hEntity, bool bTextureDirty)
{
	m_Queue.SetSink(HandOverToManaged, this);
	m_Queue.Dirty((uint32_t)hEntity.ToInt(), bTextureDirty ? 1 : 0);
}

void CShadowsClient::Refresh(const CBaseHandle& hEntity, int nShadowType, int nShadowFlags)
{
	m_Queue.SetSink(HandOverToManaged, this);
	m_Queue.Refresh((uint32_t)hEntity.ToInt(), (nShadowType & 0xFF) | ((nShadowFlags & 0xFFFF) << 8));
}

void CShadowsClient::Destroy(const CBaseHandle& hEntity)
{
	m_Queue.SetSink(HandOverToManaged, this);
	m_Queue.Destroy((uint32_t)hEntity.ToInt());
}

// Each op is one managed call inside the render trace's shadow mark.
void CShadowsClient::AdvanceFrame()
{
	RenderTrace::ShadowEnter("shadow.AdvanceFrame");
	if (IManagedShadowsClient* pManaged = Carrier())
	{
		int32_t pPending[CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX];
		const int32_t nPending = m_Queue.Take(pPending);
		pManaged->AdvanceFrame(pPending, nPending);
	}
	RenderTrace::ShadowLeave();
}

void CShadowsClient::PreRender()
{
	RenderTrace::ShadowEnter("shadow.PreRender");
	if (IManagedShadowsClient* pManaged = Carrier())
	{
		int32_t pPending[CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX];
		const int32_t nPending = m_Queue.Take(pPending);
		pManaged->PreRender(pPending, nPending);
	}
	RenderTrace::ShadowLeave();
}

void CShadowsClient::ComputeShadowTextures(int nLeafCount, const LeafIndex_t* pLeafList)
{
	char szLine[64];
	V_snprintf(szLine, sizeof(szLine), "shadow.ComputeShadowTextures\tleaves=%d", nLeafCount);
	RenderTrace::ShadowEnter(szLine);
	if (IManagedShadowsClient* pManaged = Carrier())
	{
		int32_t pPending[CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX];
		const int32_t nPending = m_Queue.Take(pPending);
		pManaged->ComputeShadowTextures(pLeafList, nLeafCount, pPending, nPending);
	}
	RenderTrace::ShadowLeave();
}

void CShadowsClient::ComputeShadowDepthTextures(const CViewSetup& view)
{
	RenderTrace::ShadowEnter("shadow.ComputeShadowDepthTextures");
	if (IManagedShadowsClient* pManaged = Carrier())
	{
		const float flView[10] = {view.origin.x, view.origin.y, view.origin.z, view.angles.x, view.angles.y,
								  view.angles.z, view.zNear,    view.zFar,     view.fov,      view.m_flAspectRatio};
		int32_t pPending[CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX];
		const int32_t nPending = m_Queue.Take(pPending);
		pManaged->ComputeShadowDepthTextures(flView, 10, pPending, nPending);
	}
	RenderTrace::ShadowLeave();
}

void CShadowsClient::UnlockAllShadowDepthTextures()
{
	RenderTrace::ShadowNote("shadow.UnlockAllShadowDepthTextures");
	if (IManagedShadowsClient* pManaged = Carrier())
	{
		int32_t pPending[CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX];
		const int32_t nPending = m_Queue.Take(pPending);
		pManaged->UnlockAllShadowDepthTextures(pPending, nPending);
	}
}

void CShadowsClient::RenderShadowTexture(int w, int h)
{
	if (IManagedShadowsClient* pManaged = Target())
		pManaged->RenderShadowTexture(w, h);
}

// The texture table is the environment's, so a FlashlightState_t the manager hands the
// engine names the same reference.
int32_t CShadowsClient::FlashlightTexture(const char* pszName)
{
	if (!pszName || !pszName[0])
		return -1;

	for (int i = 0; i < m_nFlashlightTextures; i++)
	{
		if (!V_strcmp(m_szFlashlightTextureNames[i], pszName))
			return i;
	}

	if (m_nFlashlightTextures >= kMaxFlashlightTextures)
		return -1;

	const int i = m_nFlashlightTextures++;
	V_strncpy(m_szFlashlightTextureNames[i], pszName, sizeof(m_szFlashlightTextureNames[i]));
	m_FlashlightTextures[i] = new CTextureReference;
	m_FlashlightTextures[i]->Init(pszName, TEXTURE_GROUP_OTHER, true);
	return i;
}

int32_t SourceSharp_FlashlightSupportsBorderColor()
{
	return (g_pMaterialSystemHardwareConfig && g_pMaterialSystemHardwareConfig->SupportsBorderColor()) ? 1 : 0;
}
