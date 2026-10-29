//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON b  an interface resolved by name at load

// The CParticleMgr members the client still calls, as free functions over this
// file's state: the PMaterial table, the draw state, and Init, Term and
// LevelInit's calls into the particle library, whose manager is reached by name.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"

#include "particles/particles.h"
#include "particle_parse.h"

#include "particlemgr.h"

#include "shell_particle_mgr.h"

extern IParticleSystemQuery* g_pParticleSystemQuery;

// particlemgr.h's two value classes.
CParticleSubTextureGroup::CParticleSubTextureGroup()
{
	m_pPageMaterial = NULL;
}

CParticleSubTextureGroup::~CParticleSubTextureGroup() {}

CParticleSubTexture::CParticleSubTexture()
{
	m_tCoordMins[0] = m_tCoordMins[1] = 0;
	m_tCoordMaxs[0] = m_tCoordMaxs[1] = 1;
	m_pGroup = &m_DefaultGroup;
	m_pMaterial = NULL;

#ifdef _DEBUG
	m_szDebugName = NULL;
#endif
}

namespace
{

// CParticleMgr's members that outlived it, zero-initialised: the material
// system Init hands over, the table, and the draw state.
struct ShellParticleMgr_t
{
	IMaterialSystem* pMaterialSystem;
	CUtlVector<CParticleSubTextureGroup*>* pGroups; // lookup by page material
	CUtlDict<CParticleSubTexture*, unsigned short>* pSubTextures; // lookup by material name
	CParticleLightInfo light;
	VMatrix modelView;
};

ShellParticleMgr_t s_Mgr;

CUtlVector<CParticleSubTextureGroup*>& Groups()
{
	if (!s_Mgr.pGroups)
		s_Mgr.pGroups = new CUtlVector<CParticleSubTextureGroup*>;
	return *s_Mgr.pGroups;
}

CUtlDict<CParticleSubTexture*, unsigned short>& SubTextures()
{
	if (!s_Mgr.pSubTextures)
		s_Mgr.pSubTextures = new CUtlDict<CParticleSubTexture*, unsigned short>;
	return *s_Mgr.pSubTextures;
}

CParticleSubTextureGroup* FindOrAddSubTextureGroup(IMaterial* pPageMaterial)
{
	CUtlVector<CParticleSubTextureGroup*>& groups = Groups();
	for (int i = 0; i < groups.Count(); i++)
	{
		if (groups[i]->m_pPageMaterial == pPageMaterial)
			return groups[i];
	}

	CParticleSubTextureGroup* pGroup = new CParticleSubTextureGroup;
	groups.AddToTail(pGroup);
	pGroup->m_pPageMaterial = pPageMaterial;
	pPageMaterial->AddRef();

	return pGroup;
}

// A handle a level shutdown released, its material found again by name.
void RepairPMaterial(PMaterialHandle hMaterial)
{
	if (hMaterial->m_pMaterial != NULL || !s_Mgr.pMaterialSystem)
		return;

	CUtlDict<CParticleSubTexture*, unsigned short>& subTextures = SubTextures();
	const char* pMaterialName = NULL;
	for (int i = subTextures.First(); i != subTextures.InvalidIndex(); i = subTextures.Next(i))
	{
		if (subTextures[i] == hMaterial)
		{
			pMaterialName = subTextures.GetElementName(i);
			break;
		}
	}
	Assert(pMaterialName != NULL);
	if (!pMaterialName)
		return;

	IMaterial* pIMaterial = s_Mgr.pMaterialSystem->FindMaterial(pMaterialName, TEXTURE_GROUP_PARTICLE);
	hMaterial->m_pMaterial = pIMaterial;
	if (pIMaterial != NULL)
	{
		pIMaterial->AddRef();
		CMatRenderContextPtr pRenderContext(s_Mgr.pMaterialSystem);
		pRenderContext->Bind(pIMaterial, ShellParticleDraw_BindArg());

		IMaterial* pPageMaterial = pIMaterial->GetMaterialPage();
		if (pIMaterial->InMaterialPage() && pPageMaterial)
		{
			if (hMaterial->m_pGroup->m_pPageMaterial == NULL)
			{
				hMaterial->m_pGroup->m_pPageMaterial = pPageMaterial;
				pPageMaterial->AddRef();
			}
		}
		else
		{
			hMaterial->m_pGroup->m_pPageMaterial = pIMaterial;
		}
	}
}

// Every held material released; with bDelete the table goes with them (Term).
void ReleaseMaterials(bool bDelete)
{
	if (s_Mgr.pSubTextures)
	{
		CUtlDict<CParticleSubTexture*, unsigned short>& subTextures = *s_Mgr.pSubTextures;
		for (int i = subTextures.First(); i != subTextures.InvalidIndex(); i = subTextures.Next(i))
		{
			IMaterial* pMaterial = subTextures[i]->m_pMaterial;
			if (pMaterial)
				pMaterial->Release();
			subTextures[i]->m_pMaterial = NULL;
		}
		if (bDelete)
			subTextures.PurgeAndDeleteElements();
	}

	if (s_Mgr.pGroups)
	{
		CUtlVector<CParticleSubTextureGroup*>& groups = *s_Mgr.pGroups;
		for (int i = groups.Count(); --i >= 0;)
		{
			IMaterial* pMaterial = groups[i]->m_pPageMaterial;
			if (pMaterial)
				pMaterial->Release();
			groups[i]->m_pPageMaterial = NULL;
		}
		if (bDelete)
			groups.PurgeAndDeleteElements();
	}
}

} // namespace

// The library's setup, as CParticleMgr's Init, Term and LevelInit made it.
bool ShellParticleMgr_Init(IMaterialSystem* pMaterials)
{
	ShellParticleMgr_Term();

	s_Mgr.pMaterialSystem = pMaterials;

	g_pParticleSystemMgr->Init(g_pParticleSystemQuery);
	g_pParticleSystemMgr->AddBuiltinSimulationOperators();
	g_pParticleSystemMgr->AddBuiltinRenderingOperators();

	// Send true to load the sheets.
	ParseParticleEffects(true, false);
	return true;
}

void ShellParticleMgr_Term()
{
	ReleaseMaterials(true);

	g_pParticleSystemMgr->UncacheAllParticleSystems();
	if (s_Mgr.pMaterialSystem)
		s_Mgr.pMaterialSystem->UncacheUnusedMaterials();
	s_Mgr.pMaterialSystem = NULL;
}

void ShellParticleMgr_LevelInit()
{
	g_pParticleSystemMgr->SetLastSimulationTime(gpGlobals->curtime);
}

// Handles leak from level to level, as stock's did: their next use re-acquires
// the material.
void ShellParticleMgr_LevelShutdown()
{
	ReleaseMaterials(false);
}

// The PMaterial table: CParticleMgr::GetPMaterial and PMaterialToIMaterial.
PMaterialHandle ShellParticleMaterial_Find(const char* pMaterialName)
{
	if (!s_Mgr.pMaterialSystem)
	{
		Assert(false);
		return NULL;
	}

	CUtlDict<CParticleSubTexture*, unsigned short>& subTextures = SubTextures();
	int hMat = subTextures.Find(pMaterialName);
	if (hMat != subTextures.InvalidIndex())
	{
		RepairPMaterial(subTextures[hMat]);
		return subTextures[hMat];
	}

	IMaterial* pIMaterial = s_Mgr.pMaterialSystem->FindMaterial(pMaterialName, TEXTURE_GROUP_PARTICLE);
	if (!pIMaterial)
		return NULL;

	pIMaterial->AddRef();

	CMatRenderContextPtr pRenderContext(s_Mgr.pMaterialSystem);
	pRenderContext->Bind(pIMaterial, ShellParticleDraw_BindArg());

	hMat = subTextures.Insert(pMaterialName);
	CParticleSubTexture* pSubTexture = new CParticleSubTexture;
	subTextures[hMat] = pSubTexture;
	pSubTexture->m_pMaterial = pIMaterial;

#ifdef _DEBUG
	int iNameLength = V_strlen(pMaterialName) + 1;
	pSubTexture->m_szDebugName = new char[iNameLength];
	memcpy(pSubTexture->m_szDebugName, pMaterialName, iNameLength);
#endif

	// A material in a page shares the page's group and maps to its part of
	// the page; one in no page is its own group.
	IMaterial* pPageMaterial = pIMaterial->GetMaterialPage();
	if (pIMaterial->InMaterialPage() && pPageMaterial)
	{
		float flOffset[2], flScale[2];
		pIMaterial->GetMaterialOffset(flOffset);
		pIMaterial->GetMaterialScale(flScale);

		pSubTexture->m_tCoordMins[0] = (0 * flScale[0] + flOffset[0]) * pPageMaterial->GetMappingWidth();
		pSubTexture->m_tCoordMaxs[0] = (1 * flScale[0] + flOffset[0]) * pPageMaterial->GetMappingWidth();

		pSubTexture->m_tCoordMins[1] = (0 * flScale[1] + flOffset[1]) * pPageMaterial->GetMappingHeight();
		pSubTexture->m_tCoordMaxs[1] = (1 * flScale[1] + flOffset[1]) * pPageMaterial->GetMappingHeight();

		pSubTexture->m_pGroup = FindOrAddSubTextureGroup(pPageMaterial);
	}
	else
	{
		pSubTexture->m_pGroup = &pSubTexture->m_DefaultGroup;
		pSubTexture->m_DefaultGroup.m_pPageMaterial = pIMaterial;
		pPageMaterial = pIMaterial; // for the texcoords' scale

		pSubTexture->m_tCoordMins[0] = pSubTexture->m_tCoordMins[1] = 0;
		pSubTexture->m_tCoordMaxs[0] = pIMaterial->GetMappingWidth();
		pSubTexture->m_tCoordMaxs[1] = pIMaterial->GetMappingHeight();
	}

	// The texcoords, half a texel in from each edge.
	pSubTexture->m_tCoordMins[0] = (pSubTexture->m_tCoordMins[0] + 0.5f) / pPageMaterial->GetMappingWidth();
	pSubTexture->m_tCoordMins[1] = (pSubTexture->m_tCoordMins[1] + 0.5f) / pPageMaterial->GetMappingHeight();
	pSubTexture->m_tCoordMaxs[0] = (pSubTexture->m_tCoordMaxs[0] - 0.5f) / pPageMaterial->GetMappingWidth();
	pSubTexture->m_tCoordMaxs[1] = (pSubTexture->m_tCoordMaxs[1] - 0.5f) / pPageMaterial->GetMappingHeight();

	return pSubTexture;
}

IMaterial* ShellParticleMaterial_Material(PMaterialHandle hMaterial)
{
	if (!hMaterial)
		return NULL;
	RepairPMaterial(hMaterial);
	return hMaterial->m_pMaterial;
}

// The draw state.
VMatrix& ShellParticleDraw_ModelView()
{
	return s_Mgr.modelView;
}

void ShellParticleDraw_GetLight(CParticleLightInfo& info)
{
	info = s_Mgr.light;
}

void ShellParticleDraw_SetLight(const CParticleLightInfo& info)
{
	s_Mgr.light = info;
}

void* ShellParticleDraw_BindArg()
{
	return &s_Mgr;
}
