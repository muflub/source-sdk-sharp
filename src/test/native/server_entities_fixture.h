// A server entity context and its resolver over nothing: no engine, no level, an empty slot
// table. For a subject the DLL tier hands its resolver or context; a test that compiles it
// links server_entity_resolver.cpp and precache_model.cpp.

#pragma once

#include "server_entity_context.h"
#include "server_entity_resolver.h"
#include "server_state.h"
#include "shell_managed_areas.h"
#include "precache_model.h"
#include "slot_table.h"

struct CTestServerEntities
{
	CTestServerEntities() { m_Entities.m_pResolver = &m_Resolver; }

	CTestServerEntities(const CTestServerEntities&) = delete;
	CTestServerEntities& operator=(const CTestServerEntities&) = delete;

	CManagedHolders m_Holders{};
	CServerPlayersDll m_Players;
	unsigned short m_nWorldSerial = 0;
	bool m_bManagedLoaded = false;
	unsigned int m_nManagedGeneration = 1;
	CServerModelPrecache m_Precache{nullptr, nullptr, m_Holders.m_Areas};
	CServerNetTables m_NetTables;
	CSourceSharpSlotTable m_SlotTable;
	CSourceSharpEntitySlots m_EntitySlots;
	CServerEntityContext m_Entities{nullptr,      nullptr,        nullptr,    nullptr,          nullptr,
									nullptr,      nullptr,        m_Holders,  m_bManagedLoaded, m_nManagedGeneration,
									m_Players,    m_nWorldSerial, m_Precache, m_NetTables,      m_SlotTable,
									m_EntitySlots};
	int32_t m_nInvisibleRangeReach = 0;
	CServerEntityResolver m_Resolver{m_SlotTable, m_Entities, m_nInvisibleRangeReach};
};
