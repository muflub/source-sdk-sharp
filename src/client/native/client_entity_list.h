//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The engine's VClientEntityList003 and the client's slot table behind it: a member of
// the client DLL object, which the engine's CreateInterface hands out. Nine index and
// handle lookups plus counts; the counts are maintained by Bind and Unbind. The
// resolvers below answer the client's own lookups from the same table.
//
// GetClientUnknownFromHandle must not answer NULL for a live entity: the engine's
// ClipRayToEntity calls GetCollideable() on the answer without a test.
// GetHighestEntityIndex is a loop bound for the engine's own tables, so it must never be
// too small.

#pragma once

#include "icliententitylist.h"

#include "slot_table.h"
#include "client_entity_counts.h"

class C_StandaloneEntity;
class IHandleEntity;

class CClientEntityList final : public IClientEntityList
{
public:
	CClientEntityList()
		: m_Counts(m_Slots)
	{
	}

	CClientEntityList(const CClientEntityList&) = delete;
	CClientEntityList& operator=(const CClientEntityList&) = delete;

	// IClientEntityList, in icliententitylist.h's order, which is the vtable the engine holds.
	IClientNetworkable* GetClientNetworkable(int entnum) override;
	IClientNetworkable* GetClientNetworkableFromHandle(CBaseHandle hEnt) override;
	IClientUnknown* GetClientUnknownFromHandle(CBaseHandle hEnt) override;
	IClientEntity* GetClientEntity(int entnum) override;
	IClientEntity* GetClientEntityFromHandle(CBaseHandle hEnt) override;
	int NumberOfEntities(bool bIncludeNonNetworkable) override;
	int GetHighestEntityIndex() override;
	void SetMaxEntities(int maxents) override;
	int GetMaxEntities() override;

	// A shell enters and leaves the world: C_StandaloneEntity's constructor and destructor
	// are the only callers.
	void Bind(C_StandaloneEntity* pShell, int nEntNum, int nSerialNum);
	void Unbind(C_StandaloneEntity* pShell);

	// The client's one downcast: Bind is the only route into the table and takes a
	// C_StandaloneEntity *. Resolve compares the serial.
	C_StandaloneEntity* ShellFromHandle(const CBaseHandle& handle) const;
	C_StandaloneEntity* ShellFromIndex(int nEntry) const;
	C_StandaloneEntity* ShellFromHandleEntity(IHandleEntity* pHandleEntity) const;

	// What a trace hit, as a shell; never a static prop.
	C_StandaloneEntity* ShellFromTraceHit(IHandleEntity* pHandleEntity) const;

	// Table first, then the world, then the stock-list remnant.
	IClientUnknown* UnknownFromIndex(int nEntry) const;
	IClientUnknown* UnknownFromHandle(const CBaseHandle& hEnt) const;

	// Index to handle: the occupant's own handle.
	CBaseHandle HandleFromIndex(int nEntry) const;

	// For cs_cl_slot_table. Not reset at level shutdown: every binding is removed by its
	// shell's destructor, so a non-zero count between maps is a leak.
	const CSourceSharpSlotTable& Slots() const { return m_Slots; }

private:
	CSourceSharpSlotTable m_Slots;
	CClientEntityCounts m_Counts;
	bool m_bWarnedUnresolved = false;
};

// The client DLL object's list, for the free resolvers that the code not yet handed the
// list still calls. Defined in shell_clientdll.cpp.
CClientEntityList& ShellClientDll_EntityList();
