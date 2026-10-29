//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CServerEntityResolver's bodies (server_entity_resolver.h), and the free functions the
// code not yet handed a resolver still calls.

#include "server_entity_resolver.h"

#include "shell_standalone_player.h"
#include "server_entity_context.h"
#include "server_state.h"
#include "slot_table.h"
#include "ss_world.h"
#include "world_subject.h"
#include "ss_entityhandle.h"

SourceSharpEntity_t CServerEntityResolver::Resolve(uint32_t hEntity)
{
	SourceSharpEntity_t ent;
	ent.pBase = NULL;
	ent.pStandalone = NULL;
	ent.pWorld = NULL;

	if (hEntity == INVALID_EHANDLE_INDEX)
		return ent;

	// UnsafeFromIndex, not Init( entry, serial ): ENT_ENTRY_MASK comes from the
	// serial width, so decomposing by hand does not round-trip.
	IHandleEntity* pHandleEntity = EntityFromHandle(CBaseHandle::UnsafeFromIndex((int)hEntity));
	if (!pHandleEntity)
	{
		// An unresolved handle in the engine-invisible entry range: a shell-less
		// entity's, or a stale one.
		const int nEntry = (int)(hEntity & ENT_ENTRY_MASK);

		if (nEntry >= MAX_EDICTS && nEntry < NUM_ENT_ENTRIES)
			m_nInvisibleRangeReach++;

		return ent;
	}

	// The world first: it answers NULL to both asks below and would read as gone.
	ent.pWorld = WorldFromEntityHandle(pHandleEntity);

	if (ent.pWorld)
		return ent;

	if (IGameEntity* pGame = ss::GameEntityFromEntityHandle(m_Entities.m_pStaticProps, pHandleEntity))
		ent.pStandalone = SourceSharp_ShellOf(pGame);

	return ent;
}

CStandaloneEntity* CServerEntityResolver::Standalone(uint32_t hEntity)
{
	return Resolve(hEntity).pStandalone;
}

CStandalonePlayer* CServerEntityResolver::Player(uint32_t hEntity)
{
	CStandaloneEntity* pEntity = Standalone(hEntity);

	if (!pEntity || !pEntity->IsPlayer())
		return NULL;

	return SourceSharp_PlayerOf(pEntity);
}

// The one permitted downcast on an entity path. Only CStandaloneEntity pointers are
// ever bound, and Resolve checks the serial before any pointer is returned.
CStandaloneEntity* CServerEntityResolver::ShellFromHandle(const CBaseHandle& handle) const
{
	return static_cast<CStandaloneEntity*>(m_Slots.Resolve(handle));
}

// By entry index (the engine's route, no serial), through the handle resolver so there
// is only one cast site.
CStandaloneEntity* CServerEntityResolver::ShellFromIndex(int nEntry) const
{
	return ShellFromHandle(m_Slots.HandleOf(nEntry));
}

// No cast: GetRefEHandle is virtual, and a foreign IHandleEntity's handle fails the
// serial compare.
CStandaloneEntity* CServerEntityResolver::ShellFromHandleEntity(IHandleEntity* pHandleEntity) const
{
	if (!pHandleEntity)
		return NULL;

	return ShellFromHandle(pHandleEntity->GetRefEHandle());
}

// The two populations are disjoint, so the order is one of cost only.
IHandleEntity* CServerEntityResolver::EntityFromHandle(const CBaseHandle& handle) const
{
	if (!handle.IsValid())
		return NULL;

	if (CStandaloneEntity* pShell = ShellFromHandle(handle))
		return static_cast<IHandleEntity*>(static_cast<IServerEntity*>(pShell));

	if (CWorld* pWorld = WorldFromHandle(handle))
		return static_cast<IHandleEntity*>(static_cast<IServerEntity*>(pWorld));

	// No other population remains.
	return NULL;
}

CWorld* CServerEntityResolver::World() const
{
	return m_Entities.m_pLevel ? m_Entities.m_pLevel->m_pWorld : NULL;
}

// The world is not in the slot table, so its handle is compared directly. The packed
// compare checks the serial, which changes each map. Invalid handles are refused first
// so an unbound entity cannot match an unbound world.
CWorld* CServerEntityResolver::WorldFromHandle(const CBaseHandle& handle) const
{
	if (!handle.IsValid())
		return NULL;

	CWorld* pWorld = World();

	if (!pWorld || pWorld->GetRefEHandle() != handle)
		return NULL;

	return pWorld;
}

// An identity compare against the world (see world_subject.h).
CWorld* CServerEntityResolver::WorldFromEntityHandle(IHandleEntity* pHandleEntity) const
{
	CWorld* pWorld = World();
	return SourceSharp_IsTheWorldHandle(pHandleEntity, static_cast<const IHandleEntity*>(pWorld)) ? pWorld : NULL;
}
