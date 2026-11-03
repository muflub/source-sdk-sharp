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
#include "shell_client_engine_interfaces.h"

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

CUtlVector<CParticleSubTextureGroup*>& CParticleMgrClient::Groups()
{
	if (!m_pGroups)
		m_pGroups = new CUtlVector<CParticleSubTextureGroup*>;
	return *m_pGroups;
}

CUtlDict<CParticleSubTexture*, unsigned short>& CParticleMgrClient::SubTextures()
{
	if (!m_pSubTextures)
		m_pSubTextures = new CUtlDict<CParticleSubTexture*, unsigned short>;
	return *m_pSubTextures;
}

CParticleSubTextureGroup* CParticleMgrClient::FindOrAddSubTextureGroup(IMaterial* pPageMaterial)
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
void CParticleMgrClient::RepairPMaterial(PMaterialHandle hMaterial)
{
	if (hMaterial->m_pMaterial != NULL || !m_pMaterialSystem)
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

	IMaterial* pIMaterial = m_pMaterialSystem->FindMaterial(pMaterialName, TEXTURE_GROUP_PARTICLE);
	hMaterial->m_pMaterial = pIMaterial;
	if (pIMaterial != NULL)
	{
		pIMaterial->AddRef();
		CMatRenderContextPtr pRenderContext(m_pMaterialSystem);
		pRenderContext->Bind(pIMaterial, BindArg());

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
void CParticleMgrClient::ReleaseMaterials(bool bDelete)
{
	if (m_pSubTextures)
	{
		CUtlDict<CParticleSubTexture*, unsigned short>& subTextures = *m_pSubTextures;
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

	if (m_pGroups)
	{
		CUtlVector<CParticleSubTextureGroup*>& groups = *m_pGroups;
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

// The library's setup, as CParticleMgr's Init, Term and LevelInit made it.
bool CParticleMgrClient::Init(IMaterialSystem* pMaterials, IParticleSystemQuery* pQuery)
{
	Term();

	m_pMaterialSystem = pMaterials;

	m_Engines.m_pParticleMgr->Init(pQuery);
	m_Engines.m_pParticleMgr->AddBuiltinSimulationOperators();
	m_Engines.m_pParticleMgr->AddBuiltinRenderingOperators();

	return true;
}

void CParticleMgrClient::Term()
{
	ReleaseMaterials(true);

	m_Engines.m_pParticleMgr->UncacheAllParticleSystems();
	if (m_pMaterialSystem)
		m_pMaterialSystem->UncacheUnusedMaterials();
	m_pMaterialSystem = NULL;
}

void CParticleMgrClient::LevelInit()
{
	m_Engines.m_pParticleMgr->SetLastSimulationTime(m_Engines.m_pGlobals->curtime);
}

// Handles leak from level to level, as stock's did: their next use re-acquires
// the material.
void CParticleMgrClient::LevelShutdown()
{
	ReleaseMaterials(false);
}

// The PMaterial table: CParticleMgr::GetPMaterial and PMaterialToIMaterial.
PMaterialHandle CParticleMgrClient::FindMaterial(const char* pMaterialName)
{
	if (!m_pMaterialSystem)
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

	IMaterial* pIMaterial = m_pMaterialSystem->FindMaterial(pMaterialName, TEXTURE_GROUP_PARTICLE);
	if (!pIMaterial)
		return NULL;

	pIMaterial->AddRef();

	CMatRenderContextPtr pRenderContext(m_pMaterialSystem);
	pRenderContext->Bind(pIMaterial, BindArg());

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

IMaterial* CParticleMgrClient::Material(PMaterialHandle hMaterial)
{
	if (!hMaterial)
		return NULL;
	RepairPMaterial(hMaterial);
	return hMaterial->m_pMaterial;
}
