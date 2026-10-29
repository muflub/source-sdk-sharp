//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// Hooks IFileSystem's AddSearchPath, BeginMapAccess and EndMapAccess and
// IMaterialSystem's ReloadMaterials so managed code can narrow the spawn's
// material reload. Each original runs first or instead, so with no managed
// answer every call behaves as stock. Only a listen server's spawn reaches the
// hooks. Uninstalled at Shutdown: the hooks live in client.so and must not
// outlive it.
#include "filesystem.h"
#include "materialsystem/imaterialsystem.h"

#include "cdll_client_int.h"

#include "shell_managed_areas_client.h"
#include "vtable_hook.h"
#include "ss_platform.h"

namespace
{
// The hooks are called with the engine's object as `this`, so they reach their
// state through this pointer. Set by Install, cleared by Uninstall.
struct MapMaterialReloadHooks
{
	void** pFileSystemVtbl = nullptr;
	void** pMaterialsVtbl = nullptr;
	int nAddSearchPath = -1, nBeginMapAccess = -1, nEndMapAccess = -1, nReloadMaterials = -1;
	void *pAddSearchPath = nullptr, *pBeginMapAccess = nullptr, *pEndMapAccess = nullptr, *pReloadMaterials = nullptr;

	// Managed code's plan, newline-joined names; too large for the stack.
	char szNames[256 * 1024];
};

MapMaterialReloadHooks* s_pHooks = nullptr;

typedef void (*AddSearchPathFn)(IFileSystem*, const char*, const char*, SearchPathAdd_t);
typedef void (*MapAccessFn)(IFileSystem*);
typedef void (*ReloadMaterialsFn)(IMaterialSystem*, const char*);

void HookAddSearchPath(IFileSystem* pThis, const char* pPath, const char* pPathID, SearchPathAdd_t addType)
{
	((AddSearchPathFn)s_pHooks->pAddSearchPath)(pThis, pPath, pPathID, addType);

	// The client has no read slot on the engine filesystem, so managed code reads
	// the pak as a file on disk.
	char szFull[MAX_PATH];
	const char* pFull =
		(pPath && pPathID && pThis->RelativePathToFullPath(pPath, pPathID, szFull, sizeof(szFull))) ? szFull : pPath;

	if (IManagedRuntimeClient* pRuntime = ManagedRuntimeClient())
		pRuntime->MapMaterialSearchPath(pFull, pPathID, addType == PATH_ADD_TO_HEAD ? 1 : 0);
}

void HookBeginMapAccess(IFileSystem* pThis)
{
	((MapAccessFn)s_pHooks->pBeginMapAccess)(pThis);

	if (IManagedRuntimeClient* pRuntime = ManagedRuntimeClient())
		pRuntime->MapMaterialMapAccess(1);
}

void HookEndMapAccess(IFileSystem* pThis)
{
	((MapAccessFn)s_pHooks->pEndMapAccess)(pThis);

	if (IManagedRuntimeClient* pRuntime = ManagedRuntimeClient())
		pRuntime->MapMaterialMapAccess(0);
}

// -1 (or no managed side) makes the call unchanged; otherwise each
// newline-separated name is reloaded alone.
void HookReloadMaterials(IMaterialSystem* pThis, const char* pSubString)
{
	ReloadMaterialsFn pOriginal = (ReloadMaterialsFn)s_pHooks->pReloadMaterials;

	IManagedRuntimeClient* pRuntime = ManagedRuntimeClient();
	const int nWritten =
		pRuntime ? pRuntime->MapMaterialReloadPlan(pSubString, s_pHooks->szNames, sizeof(s_pHooks->szNames)) : -1;

	if (nWritten < 0 || nWritten >= (int)sizeof(s_pHooks->szNames))
	{
		pOriginal(pThis, pSubString);
		return;
	}

	s_pHooks->szNames[nWritten] = '\0';
	for (char* p = s_pHooks->szNames; *p;)
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

bool Patch(void** pVtbl, int nSlot, void* pNew, void** ppOld)
{
	if (!SS_PageProtect(&pVtbl[nSlot], SS_PAGE_READ | SS_PAGE_WRITE))
		return false;
	if (ppOld)
		*ppOld = pVtbl[nSlot];
	pVtbl[nSlot] = pNew;
	SS_PageProtect(&pVtbl[nSlot], SS_PAGE_READ);
	return true;
}

template <typename T>
int SlotOf(T pMember)
{
	const VTableHook::Member m = VTableHook::MemberOf(pMember);
	return (m.isVirtual && m.adjust == 0) ? m.slot : -1;
}
} // namespace

void SourceSharp_UninstallMapMaterialReload();

bool SourceSharp_InstallMapMaterialReload(IFileSystem* pFileSystem, IMaterialSystem* pMaterials)
{
	if (s_pHooks || !pFileSystem || !pMaterials)
		return false;

	MapMaterialReloadHooks* p = new MapMaterialReloadHooks;
	p->nAddSearchPath = SlotOf(&IFileSystem::AddSearchPath);
	p->nBeginMapAccess = SlotOf(&IFileSystem::BeginMapAccess);
	p->nEndMapAccess = SlotOf(&IFileSystem::EndMapAccess);
	p->nReloadMaterials = SlotOf(&IMaterialSystem::ReloadMaterials);
	if (p->nAddSearchPath < 0 || p->nBeginMapAccess < 0 || p->nEndMapAccess < 0 || p->nReloadMaterials < 0)
	{
		Warning(
			"[SourceSharp] map materials: a hooked method is not a plain virtual of its interface; not installed\n");
		delete p;
		return false;
	}
	p->pFileSystemVtbl = *(void***)pFileSystem;
	p->pMaterialsVtbl = *(void***)pMaterials;

	s_pHooks = p;
	const bool bOk = Patch(p->pFileSystemVtbl, p->nAddSearchPath, (void*)&HookAddSearchPath, &p->pAddSearchPath) &&
		Patch(p->pFileSystemVtbl, p->nBeginMapAccess, (void*)&HookBeginMapAccess, &p->pBeginMapAccess) &&
		Patch(p->pFileSystemVtbl, p->nEndMapAccess, (void*)&HookEndMapAccess, &p->pEndMapAccess) &&
		Patch(p->pMaterialsVtbl, p->nReloadMaterials, (void*)&HookReloadMaterials, &p->pReloadMaterials);
	if (!bOk)
	{
		Warning("[SourceSharp] map materials: could not patch a vtable; not installed\n");
		SourceSharp_UninstallMapMaterialReload();
		return false;
	}
	Msg("[SourceSharp] map materials: the spawn's ReloadMaterials( NULL ) is targeted (plan_map_load P1)\n");
	return true;
}

void SourceSharp_UninstallMapMaterialReload()
{
	MapMaterialReloadHooks* p = s_pHooks;
	if (!p)
		return;
	if (p->pAddSearchPath)
		Patch(p->pFileSystemVtbl, p->nAddSearchPath, p->pAddSearchPath, nullptr);
	if (p->pBeginMapAccess)
		Patch(p->pFileSystemVtbl, p->nBeginMapAccess, p->pBeginMapAccess, nullptr);
	if (p->pEndMapAccess)
		Patch(p->pFileSystemVtbl, p->nEndMapAccess, p->pEndMapAccess, nullptr);
	if (p->pReloadMaterials)
		Patch(p->pMaterialsVtbl, p->nReloadMaterials, p->pReloadMaterials, nullptr);
	s_pHooks = nullptr;
	delete p;
}
