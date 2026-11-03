//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// CMapMaterialReload (shell_map_material_reload.h). Only a listen server's spawn
// reaches the hooks. Uninstalled at Shutdown: the hooks live in client.so and must
// not outlive it.
#include "shell_map_material_reload.h"

#include "cdll_client_int.h"

#include "shell_managed_areas_client.h"

void CMapMaterialReload::AddSearchPath(IFileSystem* pThis, const char* pPath, const char* pPathID,
									   SearchPathAdd_t addType)
{
	typedef void (*AddSearchPathFn)(IFileSystem*, const char*, const char*, SearchPathAdd_t);
	((AddSearchPathFn)m_AddSearchPath.pOriginal)(pThis, pPath, pPathID, addType);

	// The client has no read slot on the engine filesystem, so managed code reads
	// the pak as a file on disk.
	char szFull[MAX_PATH];
	const char* pFull =
		(pPath && pPathID && pThis->RelativePathToFullPath(pPath, pPathID, szFull, sizeof(szFull))) ? szFull : pPath;

	if (IManagedRuntimeClient* pRuntime = m_Managed.m_pRuntimeClient)
		pRuntime->MapMaterialSearchPath(pFull, pPathID, addType == PATH_ADD_TO_HEAD ? 1 : 0);
}

void CMapMaterialReload::BeginMapAccess(IFileSystem* pThis)
{
	typedef void (*MapAccessFn)(IFileSystem*);
	((MapAccessFn)m_BeginMapAccess.pOriginal)(pThis);

	if (IManagedRuntimeClient* pRuntime = m_Managed.m_pRuntimeClient)
		pRuntime->MapMaterialMapAccess(1);
}

void CMapMaterialReload::EndMapAccess(IFileSystem* pThis)
{
	typedef void (*MapAccessFn)(IFileSystem*);
	((MapAccessFn)m_EndMapAccess.pOriginal)(pThis);

	if (IManagedRuntimeClient* pRuntime = m_Managed.m_pRuntimeClient)
		pRuntime->MapMaterialMapAccess(0);
}

// -1 (or no managed side) makes the call unchanged; otherwise each
// newline-separated name is reloaded alone.
void CMapMaterialReload::ReloadMaterials(IMaterialSystem* pThis, const char* pSubString)
{
	typedef void (*ReloadMaterialsFn)(IMaterialSystem*, const char*);
	ReloadMaterialsFn pOriginal = (ReloadMaterialsFn)m_ReloadMaterials.pOriginal;

	char* pNames = m_Names.Base();
	const int nNames = m_Names.Count();
	IManagedRuntimeClient* pRuntime = m_Managed.m_pRuntimeClient;
	const int nWritten = pRuntime ? pRuntime->MapMaterialReloadPlan(pSubString, pNames, nNames) : -1;

	if (nWritten < 0 || nWritten >= nNames)
	{
		pOriginal(pThis, pSubString);
		return;
	}

	pNames[nWritten] = '\0';
	for (char* p = pNames; *p;)
	{
		char* pEnd = strchr(p, '\n');
		if (pEnd)
			*pEnd = '\0';
		if (*p)
			pOriginal(pThis, p);
		if (!pEnd)
			break;
		p = pEnd + 1;
	}
}

bool CMapMaterialReload::Install(IFileSystem* pFileSystem, IMaterialSystem* pMaterials)
{
	if (m_AddSearchPath.pSlot || !pFileSystem || !pMaterials)
		return false;

	if (!m_AddSearchPathHook.Get() || !m_BeginMapAccessHook.Get() || !m_EndMapAccessHook.Get() ||
		!m_ReloadMaterialsHook.Get())
	{
		Warning("[SourceSharp] map materials: a hook's thunk page was refused; not installed\n");
		return false;
	}

	m_Names.SetCount(256 * 1024);

	const bool bOk = VTableSlotPatch::Install(pFileSystem, &IFileSystem::AddSearchPath,
											  (void*)m_AddSearchPathHook.Get(), &m_AddSearchPath) &&
		VTableSlotPatch::Install(pFileSystem, &IFileSystem::BeginMapAccess, (void*)m_BeginMapAccessHook.Get(),
								 &m_BeginMapAccess) &&
		VTableSlotPatch::Install(pFileSystem, &IFileSystem::EndMapAccess, (void*)m_EndMapAccessHook.Get(),
								 &m_EndMapAccess) &&
		VTableSlotPatch::Install(pMaterials, &IMaterialSystem::ReloadMaterials, (void*)m_ReloadMaterialsHook.Get(),
								 &m_ReloadMaterials);
	if (!bOk)
	{
		Warning("[SourceSharp] map materials: a hooked method is not a plain virtual of its interface, or its "
				"vtable refused the patch; not installed\n");
		Uninstall();
		return false;
	}
	Msg("[SourceSharp] map materials: the spawn's ReloadMaterials( NULL ) is targeted (plan_map_load P1)\n");
	return true;
}

void CMapMaterialReload::Uninstall()
{
	VTableSlotPatch::Patch* pPatches[] = {&m_AddSearchPath, &m_BeginMapAccess, &m_EndMapAccess, &m_ReloadMaterials};
	for (VTableSlotPatch::Patch* pPatch : pPatches)
	{
		if (pPatch->pSlot)
			VTableSlotPatch::Remove(*pPatch);
		*pPatch = {};
	}
	m_Names.Purge();
}
