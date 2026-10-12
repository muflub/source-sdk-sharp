//========= SourceSharp — who is watching whom, asked of the list ============//
//
// `INotify` lets one entity hear about another: AddEntity( pNotify, pWatched )
// records the pair, and every destroy/system event about pWatched is fanned out
// to the entities watching it. The list of pairs is the whole of that state.
//
// Stock kept a SECOND copy of one bit of it. `AddEntity` set EFL_NOTIFY on the
// watched entity and the two report paths early-outed on IsEFlagSet(EFL_NOTIFY)
// before scanning. That bit is a cache of "there is at least one pair naming
// me", and it is an inexact one in stock's own terms: RemoveEntity and
// ClearEntity drop pairs and never clear the flag, so the cache is an
// OVER-approximation — set for entities the list no longer names. It
// over-approximates in the safe direction, which is why nobody noticed, and it
// is also why removing the cache cannot change what gets delivered: the gate
// only ever let through a superset of what the scan below it would match.
//
// WHY IT HAD TO GO, rather than merely being tidy. m_iEFlags is CBaseEntity's
// own member. EFlags are the SDK hierarchy's dirty-bit and lifetime bookkeeping
// (EFL_KILLME, EFL_DIRTY_*, EFL_NO_THINK_FUNCTION) and the parallel hierarchy
// was built so that none of it is needed — eager composition instead of dirty
// transforms, a registry instead of KILLME sweeps. An EFlag reached through an
// entity POINTER is therefore SDK-internal state crossing a boundary it has no
// business crossing, and plan_baseentity.md Phase 13's typedef — `using
// CBaseEntity = IGameEntity` — cannot compile such a site at all.
//
// The notify list was the one place an EFlag was reached for a fact the caller
// already owned: CNotifyList holds the pairs. Every other surviving EFlag site
// in this build is inside a CBaseEntity-only path (UTIL_RemoveImmediate's
// KILLME, CCollisionProperty's dirty bits, CServerNetworkProperty's teardown)
// and dies with the class rather than needing a boundary answer. This one did
// not — and entitylist.o's last link-time dependency on baseentity.o was the
// inline AddEFlags body reaching CBaseEntity::DispatchUpdateTransmitState()
// from exactly this call.
//
// Header-only and engine-free — templated on the CONTAINER, so the game passes
// a CUtlVector and the test passes its own — so the rule is checked by gtest
// (src/sourcesharp/test/notify_gate_test.cpp) rather than by watching an event
// fail to arrive.
//
//=============================================================================//
#ifndef SOURCESHARP_NOTIFY_GATE_H
#define SOURCESHARP_NOTIFY_GATE_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Does any recorded pair name this entity as the WATCHED one?
//
// This is the authority the EFlag was a copy of. TList needs Count() and
// operator[], and its element needs a pWatched member — CUtlVector<
// entitynotify_t > in the game, a stand-in in the test.
//
// Asking the list rather than the entity also closes the direction the cache
// was wrong in: an entity whose last watcher has been removed answers false
// here, and answered true through EFL_NOTIFY for the rest of the level.
//-----------------------------------------------------------------------------
template < typename TList, typename TEntity >
inline bool NotifyList_Watches( const TList &list, const TEntity *pWatched )
{
	for ( int i = 0; i < list.Count(); i++ )
	{
		if ( list[i].pWatched == pWatched )
			return true;
	}

	return false;
}

#endif // SOURCESHARP_NOTIFY_GATE_H
