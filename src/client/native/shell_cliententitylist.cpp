//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// CClientEntityList, the engine's VClientEntityList003 over the client's slot table.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"

#include "client_entity_list.h"
#include "shell_cliententitylist.h"
#include "shell_standalone_client.h"

#include "handle_cross.h"

#include "shell_boundary_probe.h"
#include "shell_standalone_clients.h"
#include "shell_client_engine_interfaces.h"

C_StandaloneEntity* CClientEntityList::ShellFromHandle(const CBaseHandle& handle) const
{
	return static_cast<C_StandaloneEntity*>(m_Slots.Resolve(handle));
}

// By entry index, which carries no serial; through the handle resolver so the
// cast stays in one place.
C_StandaloneEntity* CClientEntityList::ShellFromIndex(int nEntry) const
{
	return ShellFromHandle(m_Slots.HandleOf(nEntry));
}

// IHandleEntity to shell, by the object's own handle.
C_StandaloneEntity* CClientEntityList::ShellFromHandleEntity(IHandleEntity* pHandleEntity) const
{
	if (!pHandleEntity)
		return NULL;

	return ShellFromHandle(pHandleEntity->GetRefEHandle());
}

// Static props are filtered before the table.
C_StandaloneEntity* CClientEntityList::ShellFromTraceHit(IHandleEntity* pHandleEntity) const
{
	return SourceSharp_CrossHandleToClientShell<C_StandaloneEntity>(
		pHandleEntity, [this](IHandleEntity* pHit) { return ShellFromHandleEntity(pHit); },
		[this](IHandleEntity* pHit) { return m_Engines.m_pStaticProps->IsStaticProp(pHit); });
}

IClientUnknown* CClientEntityList::UnknownFromIndex(int nEntry) const
{
	C_StandaloneEntity* pShell = ShellFromIndex(nEntry);

	if (pShell)
		return pShell->GetIClientUnknown();

	// The world is not in the table.
	if (IClientUnknown* pWorld = m_Engines.m_pWorld->FromIndex(nEntry))
		return pWorld;

	return SourceSharp_LegacyListUnknownFromIndex(nEntry);
}

IClientUnknown* CClientEntityList::UnknownFromHandle(const CBaseHandle& hEnt) const
{
	C_StandaloneEntity* pShell = ShellFromHandle(hEnt);

	if (pShell)
		return pShell->GetIClientUnknown();

	// The serial is compared, so the previous map's world does not resolve.
	if (IClientUnknown* pWorld = m_Engines.m_pWorld->FromHandle(hEnt))
		return pWorld;

	return SourceSharp_LegacyListUnknownFromHandle(hEnt);
}

// The busiest method on either interface; the engine tolerates NULL.
IClientNetworkable* CClientEntityList::GetClientNetworkable(int entnum)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_ClientList_GetClientNetworkable);

	IClientUnknown* pUnknown = UnknownFromIndex(entnum);
	return pUnknown ? pUnknown->GetClientNetworkable() : NULL;
}

// No engine caller; NULL for an unresolved handle, as stock answered.
IClientNetworkable* CClientEntityList::GetClientNetworkableFromHandle(CBaseHandle hEnt)
{
	IClientUnknown* pUnknown = UnknownFromHandle(hEnt);

	IClientNetworkable* pResult = pUnknown ? pUnknown->GetClientNetworkable() : NULL;
	SS_BOUNDARY_HIT_PTR(m_Probes, kBoundaryProbe_ClientList_GetClientNetworkableFromHandle, pResult);
	return pResult;
}

// The engine dereferences this answer in its own trace without a test, so
// a miss is reported once per session.
IClientUnknown* CClientEntityList::GetClientUnknownFromHandle(CBaseHandle hEnt)
{
	IClientUnknown* pResult = UnknownFromHandle(hEnt);

	if (!pResult && hEnt.IsValid() && !m_bWarnedUnresolved)
	{
		m_bWarnedUnresolved = true;
		Warning("[SourceSharp] IClientEntityList::GetClientUnknownFromHandle: "
				"entry %d serial %d resolves to NOTHING. The engine's trace "
				"dereferences this without a test -- if "
				"the caller was engine.so the next thing that happens is a "
				"crash inside the engine. Said once per session.\n",
				hEnt.GetEntryIndex(), hEnt.GetSerialNumber());
	}

	SS_BOUNDARY_HIT_PTR(m_Probes, kBoundaryProbe_ClientList_GetClientUnknownFromHandle, pResult);
	return pResult;
}

// The engine tolerates NULL; the trace stores it in m_pEnt.
IClientEntity* CClientEntityList::GetClientEntity(int entnum)
{
	IClientUnknown* pUnknown = UnknownFromIndex(entnum);

	IClientEntity* pResult = pUnknown ? pUnknown->GetIClientEntity() : NULL;
	SS_BOUNDARY_HIT_PTR(m_Probes, kBoundaryProbe_ClientList_GetClientEntity, pResult);
	return pResult;
}

IClientEntity* CClientEntityList::GetClientEntityFromHandle(CBaseHandle hEnt)
{
	IClientUnknown* pUnknown = UnknownFromHandle(hEnt);

	IClientEntity* pResult = pUnknown ? pUnknown->GetIClientEntity() : NULL;
	SS_BOUNDARY_HIT_PTR(m_Probes, kBoundaryProbe_ClientList_GetClientEntityFromHandle, pResult);
	return pResult;
}

// Maintained by Bind and Unbind, never derived by walking entries.
int CClientEntityList::NumberOfEntities(bool bIncludeNonNetworkable)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_ClientList_NumberOfEntities);

	// The world is a networked entity outside the table; it counts under
	// both arguments.
	return SourceSharp_ClientCountWithWorld(m_Engines.m_pWorld->Handle(),
											m_Counts.NumberOfEntities(bIncludeNonNetworkable));
}

// A loop bound for the engine's own tables: too small and entities stop
// drawing. Maintained by Bind and Unbind.
int CClientEntityList::GetHighestEntityIndex()
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_ClientList_GetHighestEntityIndex);

	// The world raises it from -1 to 0 on a map with nothing else bound.
	return SourceSharp_ClientHighestWithWorld(m_Engines.m_pWorld->Handle(), m_Counts.HighestEntityIndex());
}

// Stored and handed back, as stock did.
void CClientEntityList::SetMaxEntities(int maxents)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_ClientList_SetMaxEntities);

	m_Counts.SetMaxEntities(maxents);
}

int CClientEntityList::GetMaxEntities()
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_ClientList_GetMaxEntities);

	return m_Counts.MaxEntities();
}

CBaseHandle CClientEntityList::HandleFromIndex(int nEntry) const
{
	const CBaseHandle hSlot = m_Slots.HandleOf(nEntry);

	if (hSlot.IsValid())
		return hSlot;

	// Entry 0 is not in the table.
	const CBaseHandle hWorld = m_Engines.m_pWorld->Handle();

	if (SourceSharp_ClientWorldAnswersForIndex(hWorld, nEntry))
		return hWorld;

	IClientUnknown* pLegacy = SourceSharp_LegacyListUnknownFromIndex(nEntry);

	return pLegacy ? pLegacy->GetRefEHandle() : CBaseHandle();
}

// LegacyListBind first, because it writes the shell's m_RefEHandle, which the table then
// adopts; the client binds only at the server's handle and never chooses a serial. A
// refused bind means two live shells claim one entry; it is reported.
void CClientEntityList::Bind(C_StandaloneEntity* pShell, int nEntNum, int nSerialNum)
{
	if (!pShell)
		return;

	// Without the handle the engine cannot look the entity up to unpack into
	// it, and PostDataUpdate never runs.
	const CBaseHandle hEnt = SourceSharp_LegacyListBind(pShell->GetIClientUnknown(), nEntNum, nSerialNum);

	if (!m_Slots.BindAt(hEnt, pShell))
	{
		Warning("[SourceSharp] client slot table REFUSED the bind for entity %d "
				"(serial %d): the entry is already bound, outside the table, or the "
				"handle is invalid. This entity will resolve to nothing.\n",
				nEntNum, nSerialNum);
		return;
	}

	m_Counts.OnBound(hEnt.GetEntryIndex());
}

// By the shell's own handle, read before either half is torn down. The table is unbound
// first, mirroring Bind.
void CClientEntityList::Unbind(C_StandaloneEntity* pShell)
{
	if (!pShell)
		return;

	const CBaseHandle hEnt = pShell->GetRefEHandle();

	if (hEnt.IsValid())
	{
		const int nEntry = hEnt.GetEntryIndex();

		// Only if this shell is what is bound there: a refused bind never owned it.
		if (ShellFromHandle(hEnt) == pShell)
		{
			m_Slots.Unbind(nEntry);
			m_Counts.OnUnbound(nEntry);
		}
	}

	SourceSharp_LegacyListUnbind(hEnt);
}

// Whether this side holds the entity a handle names, serial and all.
int32_t CStandaloneClients::EntityIsValid(uint32_t hEntity)
{
	// Splitting the invalid handle would hand Init a serial out of range.
	if (hEntity == INVALID_EHANDLE_INDEX)
		return 0;

	const CBaseHandle handle((int)(hEntity & ENT_ENTRY_MASK), (int)(hEntity >> NUM_SERIAL_NUM_SHIFT_BITS));

	return EntityList().UnknownFromHandle(handle) ? 1 : 0;
}

IClientEntity* CClientEntityList::EntityFromIndex(int nEntry) const
{
	IClientUnknown* pUnknown = UnknownFromIndex(nEntry);
	return pUnknown ? pUnknown->GetIClientEntity() : NULL;
}

IClientNetworkable* CClientEntityList::NetworkableFromHandle(const CBaseHandle& hEnt) const
{
	IClientUnknown* pUnknown = UnknownFromHandle(hEnt);
	return pUnknown ? pUnknown->GetClientNetworkable() : NULL;
}

IClientEntity* CClientEntityList::EntityFromHandle(const CBaseHandle& hEnt) const
{
	IClientUnknown* pUnknown = UnknownFromHandle(hEnt);
	return pUnknown ? pUnknown->GetIClientEntity() : NULL;
}

IClientRenderable* CClientEntityList::RenderableFromHandle(const CBaseHandle& hEnt) const
{
	IClientUnknown* pUnknown = UnknownFromHandle(hEnt);
	return pUnknown ? pUnknown->GetClientRenderable() : NULL;
}

// `bound` should return to zero between maps; `refused` is a bind that did not happen.
void CClientEntityList::SlotTableCommand(const CCommand& args)
{
	const CSourceSharpSlotTable& table = Slots();

	Msg("[SourceSharp] client slot table: %d entries (%d edict, %d engine-invisible)\n",
		CSourceSharpSlotTable::EntryCount(), MAX_EDICTS, CSourceSharpSlotTable::EntryCount() - MAX_EDICTS);
	Msg("  bound %d  binds %d  unbinds %d  refused %d\n", table.BoundCount(), table.Binds(), table.Unbinds(),
		table.Refused());
	Msg("  highest networked entry %d  max entities %d\n", GetHighestEntityIndex(), GetMaxEntities());
}

// The client's engine-boundary probe counters; client.so's are separate from server.so's.
void CClientEntityList::BoundaryProbeReportCommand(const CCommand& args)
{
	Msg("[SourceSharp] engine-boundary probe (client): map '%s'\n",
		m_Engines.m_pEngine->GetLevelName() ? m_Engines.m_pEngine->GetLevelName() : "(none)");

	for (int id = 0; id < kBoundaryProbeCount; id++)
	{
		char szLine[2048];
		m_Probes.FormatLine(id, szLine, sizeof(szLine));
		Msg("  %s\n", szLine);
	}
}

void CClientEntityList::BoundaryProbeResetCommand(const CCommand& args)
{
	m_Probes.Reset();
	Msg("[SourceSharp] engine-boundary probe (client): counters zeroed\n");
}

// What is left of the stock client entity list. The bind assigns the shell's
// handle from the server's entry and serial; the rest answer nothing.
CBaseHandle SourceSharp_LegacyListBind(IClientUnknown* pUnknown, int nEntNum, int nSerialNum)
{
	if (!pUnknown)
		return CBaseHandle();

	const CBaseHandle hEnt(nEntNum, nSerialNum);

	pUnknown->SetRefEHandle(hEnt);

	return hEnt;
}

void SourceSharp_LegacyListUnbind(const CBaseHandle& hEnt)
{
	// Both callers clear the handle themselves.
	(void)hEnt;
}

IClientUnknown* SourceSharp_LegacyListUnknownFromIndex(int nEntry)
{
	(void)nEntry;
	return NULL;
}

IClientUnknown* SourceSharp_LegacyListUnknownFromHandle(const CBaseHandle& hEnt)
{
	(void)hEnt;
	return NULL;
}
