//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The listen server spawn's targeted material reload, a member of the client DLL object:
// it patches IFileSystem's AddSearchPath, BeginMapAccess and EndMapAccess and
// IMaterialSystem's ReloadMaterials so managed code can narrow the spawn's reload. The
// engine calls each slot with its own object as `this`, so each hook is a thunk bound to
// this object (bound_callback.h). Each original runs first or instead, so with no managed
// answer every call behaves as stock. Uninstall before this object goes: the slots name
// its thunks.

#pragma once

#include "filesystem.h"
#include "materialsystem/imaterialsystem.h"
#include "utlvector.h"

#include "bound_callback.h"
#include "vtable_slot_patch.h"

struct CManagedHoldersClient;

class CMapMaterialReload
{
public:
	explicit CMapMaterialReload(const CManagedHoldersClient& managed)
		: m_Managed(managed)
	{
	}

	CMapMaterialReload(const CMapMaterialReload&) = delete;
	CMapMaterialReload& operator=(const CMapMaterialReload&) = delete;

	// False, with nothing patched, when a hooked method is not a plain virtual of its
	// interface or a slot's page refuses the store.
	bool Install(IFileSystem* pFileSystem, IMaterialSystem* pMaterials);
	void Uninstall();

private:
	typedef CBoundCallback<void(IFileSystem*, const char*, const char*, SearchPathAdd_t)> AddSearchPathHook_t;
	typedef CBoundCallback<void(IFileSystem*)> MapAccessHook_t;
	typedef CBoundCallback<void(IMaterialSystem*, const char*)> ReloadMaterialsHook_t;

	void AddSearchPath(IFileSystem* pThis, const char* pPath, const char* pPathID, SearchPathAdd_t addType);
	void BeginMapAccess(IFileSystem* pThis);
	void EndMapAccess(IFileSystem* pThis);
	void ReloadMaterials(IMaterialSystem* pThis, const char* pSubString);

	const CManagedHoldersClient& m_Managed;

	VTableSlotPatch::Patch m_AddSearchPath = {};
	VTableSlotPatch::Patch m_BeginMapAccess = {};
	VTableSlotPatch::Patch m_EndMapAccess = {};
	VTableSlotPatch::Patch m_ReloadMaterials = {};

	// Managed code's plan, newline-joined names; sized at Install.
	CUtlVector<char> m_Names;

	AddSearchPathHook_t m_AddSearchPathHook{
		this, &AddSearchPathHook_t::Trampoline<CMapMaterialReload, &CMapMaterialReload::AddSearchPath>};
	MapAccessHook_t m_BeginMapAccessHook{
		this, &MapAccessHook_t::Trampoline<CMapMaterialReload, &CMapMaterialReload::BeginMapAccess>};
	MapAccessHook_t m_EndMapAccessHook{
		this, &MapAccessHook_t::Trampoline<CMapMaterialReload, &CMapMaterialReload::EndMapAccess>};
	ReloadMaterialsHook_t m_ReloadMaterialsHook{
		this, &ReloadMaterialsHook_t::Trampoline<CMapMaterialReload, &CMapMaterialReload::ReloadMaterials>};
};
