//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data

#include "precache_model.h"
#include "shell_managed_areas.h"
#include "shell_diagnostics.h"
#include "model_types.h"
#include "eiface.h"

#include "imanaged_areas.h"
#include "ss_string.h"

void CServerModelPrecache::ModelComponents(int nModelIndex)
{
	model_t* pModel = (model_t*)m_pModelInfo->GetModel(nModelIndex);
	if (!pModel || m_pModelInfo->GetModelType(pModel) != mod_studio)
	{
		return;
	}

	// Precaches the model's animation-event sounds and particle systems, in stock's order.
	if (IManagedResources* pArea = m_Managed.m_pResources)
		pArea->ModelPrecacheComponents(m_pModelInfo->GetModelName(pModel));
}

int CServerModelPrecache::Model(const char* name, bool bPreload)
{
	if (!name || !*name)
	{
		return -1;
	}

	if (IManagedResources* pArea = m_Managed.m_pResources)
		pArea->ModelPrecacheCheck(name, m_bAllowed);

	int idx = m_pEngine->PrecacheModel(name, bPreload);
	if (idx != -1)
	{
		ModelComponents(idx);
	}

	return idx;
}

int32_t CServerModelPrecache::PrecacheModel(const char* pszModel)
{
	// Hot: the recursive precache walk calls this per model every map change.
	SOURCESHARP_TRACE_HOT();

	if (!pszModel || !pszModel[0])
		return -1;

	return Model(pszModel);
}

void CServerModelPrecache::SentenceFile()
{
	if (m_bSentenceFile)
		return;

	m_pEngine->PrecacheSentenceFile("scripts/sentences.txt");
	m_bSentenceFile = true;
}
