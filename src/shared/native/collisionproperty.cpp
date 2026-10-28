//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
//
// SOURCESHARP, plan_shim.md Phase 2b (lane 2-physics) — RELOCATED, REASON (a).
//
// The file did not change; its path did, in both vpcs. Phase 2's exit condition
// is that `server_csharp.vpc` names no row under `$SRCDIR\game\`, and a file
// that must stay native leaves that tree rather than the build.
//
// (a), A VTABLE THE ENGINE CALLS THROUGH. `CDirtySpatialPartitionEntityList`
// is an `IPartitionQueryCallback` installed into `::partition` at Init (:180)
// and removed at Shutdown (:186). Its `OnPreQuery`/`OnPostQuery` pair is a READ
// LOCK the engine takes around EVERY spatial-partition query — not an entity
// operation, and not something a C# object can be on the far side of, because
// the engine enters it from inside its own query.
//
// LANE N1's `plan_shim_rulings.tsv` ROW SAYS **DELETE**, "a lock around a list
// nothing fills". The measurement behind that is right and the verdict does not
// follow from it. Re-measured here:
//
//   * THE DIRTY LIST REALLY HAS NO WRITER. `AddEntity` had an empty body and
//     no call site anywhere in `src/` (deleted by the server gc cull, 2026-10-01:
//     neither DLL kept it); its old writer,
//     `CCollisionProperty::MarkPartitionHandleDirty`, is declared at
//     collisionproperty.h:102 and DEFINED NOWHERE, and its three callers
//     (baseentity_shared.cpp:1493, tf_dropped_weapon.cpp:190,
//     tf_weaponbase.cpp:6963) are in translation units neither vpc compiles.
//     `m_DirtyEntities` is provably, permanently empty.
//
//   * THE LOCK IS NOT THE LIST. Deleting the file deletes three externally
//     linked symbols with live callers —
//     `UpdateDirtySpatialPartitionEntities()` (shell_new_particles_client.cpp's
//     frame: UpdateNewEffects' call, C7 particles phase 3),
//     `DirtySpatialPartitionGameSystem()` (shell_serversystems.cpp:353) and
//     `ClientSystem_DirtySpatialPartition()` (shell_clientsystems.cpp:378) —
//     and takes the engine's query-callback registration with them. That is a
//     link failure in both DLLs and a behaviour change in the engine's query
//     path, neither of which the empty list implies.
//
//   * AND THE TREE ALREADY RULED IT. `shell_clientsystems.cpp:286` carries
//     `// RULING | CDirtySpatialPartitionEntityList | STAYS | ... `, which
//     `client_flip_ruling_test.cpp` parses and enforces, and :245-247 spells
//     out what STAYS means: "de-entitied in place, never deleted".
//
// So the verdict here is RELOCATE, not DELETE. The vacuous half — the list, its
// drain and `AddEntity`'s empty body (since deleted) — is deletable on its own and is NOT
// deleted in this commit, because nine gtests assert its text positively
// (cpp_alias_shard_t3_test.cpp:643/:915, client_entity_resolve_test.cpp:982/:989
// among them) and gutting it is a content change that belongs in its own step,
// not folded into a move.
//
//=============================================================================//

#ifndef CLIENT_DLL
// SERVER.SO ON THE R5 SET (plan_server_novalve.md P5, lane E): what the server
// half uses, in place of cbase.h and the game/ headers it used to share with
// the client's block below.
#include "basehandle.h"
#include "igameentity.h"
#include "ispatialpartition.h"
#include "tier0/tslist.h"
#include "shell_serversystem.h"	// plan_gamedll.md Phase 7a / T1

#include <atomic>
#include <shared_mutex>
#include <thread>
#include <vector>

extern ISpatialPartition *partition;
#else
// The client's set, in its old order; the server arms it carried went with
// the server's move above.
#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "igamesystem.h"
#include "shell_clientsystem.h"
#include "worldsize.h"
#include "physics_shared.h"
#include "sharedInterface.h"
#include <vstdlib/random.h>
#include "collisionproperty.h"
#include "igamesystem.h"
#include "utlvector.h"
#include "tier0/tslist.h"

#include <atomic>
#include <shared_mutex>
#include <thread>
#include <vector>

#include "tier1/convar.h"
#include "recvproxy.h"
// The dirty-partition drain resolves its handles here. Lane `f-list`.
#include "shell_cliententitylist.h"

#include "predictable_entity.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "move_root.h"
#include "data_object_access.h"
#endif // !CLIENT_DLL


//-----------------------------------------------------------------------------
// KD tree query callbacks
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
class CDirtySpatialPartitionEntityList : public CClientSystem, public IPartitionQueryCallback
#else
class CDirtySpatialPartitionEntityList : public CServerSystem, public IPartitionQueryCallback
#endif
{
public:
	CDirtySpatialPartitionEntityList( char const *name );

	// Members of IGameSystem
	virtual bool Init();
	virtual void Shutdown();
	virtual void LevelShutdownPostEntity();

	// Members of IPartitionQueryCallback
	virtual void OnPreQuery_V1()	{ Assert( 0 ); }
	virtual void OnPreQuery( SpatialPartitionListMask_t listMask );
	virtual void OnPostQuery( SpatialPartitionListMask_t listMask );

	~CDirtySpatialPartitionEntityList();
	void LockPartitionForRead()
	{
		if ( t_readLockCount == 0 )
		{
			m_partitionMutex.lock_shared();
		}
		t_readLockCount++;
	}
	void UnlockPartitionForRead()
	{
		t_readLockCount--;
		if ( t_readLockCount == 0 )
		{
			m_partitionMutex.unlock_shared();
		}
	}


private:
	CTSListWithFreeList<CBaseHandle> m_DirtyEntities;
	std::shared_mutex	 m_partitionMutex;
	// The thread holding the write lock; a default id (no thread) otherwise.
	std::atomic< std::thread::id > m_partitionWriteId{};
	// This thread's read-lock depth. Per thread rather than per list: there is
	// one list (s_DirtyKDTree), as tier0's CThreadLocalInt member was.
	static thread_local int t_readLockCount;
};

thread_local int CDirtySpatialPartitionEntityList::t_readLockCount = 0;


//-----------------------------------------------------------------------------
// Singleton instance
//-----------------------------------------------------------------------------
static CDirtySpatialPartitionEntityList s_DirtyKDTree( "CDirtySpatialPartitionEntityList" );

#ifdef CLIENT_DLL
// plan_cworld_trace.md T4: kClientSystems[] is the only thing that reaches this.
CClientSystem *ClientSystem_DirtySpatialPartition() { return &s_DirtyKDTree; }
#endif

#ifndef CLIENT_DLL
//-----------------------------------------------------------------------------
// plan_gamedll.md Part 3a / Phase 7 -- one accessor, so the shell's named list
// `kServerSystems[]` (sourcesharp/game/server/shell_serversystems.cpp) can name
// this system instead of relying on the CAutoGameSystem constructor's append,
// which is link order.
//
// The object above has INTERNAL LINKAGE, so this function is the only way to
// reach it from another translation unit. Nothing else changes: it still
// registers itself, it is simply no longer the registration that decides when
// it runs.
//-----------------------------------------------------------------------------
CServerSystem *DirtySpatialPartitionGameSystem()
{
	return &s_DirtyKDTree;
}
#endif // !CLIENT_DLL



//-----------------------------------------------------------------------------
// Force spatial partition updates (to avoid threading problems caused by lazy update)
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void UpdateDirtySpatialPartitionEntities()
{
	SpatialPartitionListMask_t listMask;
#ifdef CLIENT_DLL
	listMask = PARTITION_CLIENT_GAME_EDICTS;
#else
	listMask = PARTITION_SERVER_GAME_EDICTS;
#endif
	s_DirtyKDTree.OnPreQuery( listMask );
	s_DirtyKDTree.OnPostQuery( listMask );
}
#endif


//-----------------------------------------------------------------------------
// Purpose: Constructor.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
#ifdef CLIENT_DLL
CDirtySpatialPartitionEntityList::CDirtySpatialPartitionEntityList( char const *name ) : CClientSystem( name )
#else
CDirtySpatialPartitionEntityList::CDirtySpatialPartitionEntityList( char const *name ) : CServerSystem( name )
#endif
#else
CDirtySpatialPartitionEntityList::CDirtySpatialPartitionEntityList( char const *name ) : CServerSystem( name )
#endif
{
	m_DirtyEntities.Purge();
}

//-----------------------------------------------------------------------------
// Purpose: Deconstructor.
//-----------------------------------------------------------------------------
CDirtySpatialPartitionEntityList::~CDirtySpatialPartitionEntityList()
{
	m_DirtyEntities.Purge();
}

//-----------------------------------------------------------------------------
// Initialization, shutdown
//-----------------------------------------------------------------------------
bool CDirtySpatialPartitionEntityList::Init()
{
	::partition->InstallQueryCallback( this );
	return true;
}

void CDirtySpatialPartitionEntityList::Shutdown()
{
	::partition->RemoveQueryCallback( this );
}


//-----------------------------------------------------------------------------
// Members of IGameSystem
//-----------------------------------------------------------------------------
void CDirtySpatialPartitionEntityList::LevelShutdownPostEntity()
{
	m_DirtyEntities.RemoveAll();
}


//-----------------------------------------------------------------------------
// Makes sure all entries in the KD tree are in the correct position
//-----------------------------------------------------------------------------
void CDirtySpatialPartitionEntityList::OnPreQuery( SpatialPartitionListMask_t listMask )
{
#ifdef CLIENT_DLL
	const int validMask = PARTITION_CLIENT_GAME_EDICTS;
#else
	const int validMask = PARTITION_SERVER_GAME_EDICTS;
#endif

	if ( !( listMask & validMask ) )
		return;

	if ( m_partitionWriteId.load() == std::this_thread::get_id() )
		return;

	// AN EMPTY `#ifdef CLIENT_DLL` / `#endif` STOOD HERE containing only the
	// note below, left by the residue wave when it deleted the arm the note was
	// about. A conditional with nothing in it is a directive a guard census
	// counts and a reader stops to parse; the note is worth keeping and the
	// shell is not.
	//
	// It said: nested rather than `#if defined(CLIENT_DLL) && !defined(SS_...)`,
	// because survey_arm_alias.py's negative control matched
	// `#ifndef SS_CBASEENTITY_ALIAS` as a whole line and nothing else, so a
	// compound condition was a guard `defeat` could not reach and the site would
	// have stayed hidden in the control run.

	// if you're holding a read lock, then these are entities that were still dirty after your trace started
	// or became dirty due to some other thread or callback. Updating them may cause corruption further up the
	// stack (e.g. partition iterator).  Ignoring the state change should be safe since it happened after the 
	// trace was requested or was unable to be resolved in a previous attempt (still dirty).
	if ( m_DirtyEntities.Count() && !t_readLockCount )
	{
		std::vector< CBaseHandle > vecStillDirty;
		m_partitionMutex.lock();
		m_partitionWriteId = std::this_thread::get_id();
		CTSListWithFreeList<CBaseHandle>::Node_t *pCurrent, *pNext;
		while ( ( pCurrent = m_DirtyEntities.Detach() ) != NULL )
		{
			while ( pCurrent )
			{
				CBaseHandle handle = pCurrent->elem;
				pNext = (CTSListWithFreeList<CBaseHandle>::Node_t *)pCurrent->Next;
				m_DirtyEntities.FreeNode( pCurrent );
				pCurrent = pNext;

				// ARMED THIS LOOP RUNS ZERO TIMES — m_DirtyEntities.Count() is
				// nought above: nothing pushes (AddEntity, the only writer,
				// had no caller and is deleted). The body is guarded rather than the
				// loop so that the DRAIN still happens: if a handle ever did
				// arrive it would still be detached and freed, and the list
				// would not grow without bound behind a `#ifndef`.
				//
				// Both spellings of the ask go: EFL_SETTING_UP_BONES is SDK
				// dirty-bit state igameentity.h declines by name ("EFlags are
				// the SDK's dirty-bit and lifetime bookkeeping"), and
				// CollisionProp() is the accessor that dies with the class.
			}
		}
		if ( !vecStillDirty.empty() )
		{
			for ( const CBaseHandle &handle : vecStillDirty )
			{
				m_DirtyEntities.PushItem( handle );
			}
		}
		m_partitionWriteId = std::thread::id();
		m_partitionMutex.unlock();
	}
	LockPartitionForRead();
}

//-----------------------------------------------------------------------------
// Makes sure all entries in the KD tree are in the correct position
//-----------------------------------------------------------------------------
void CDirtySpatialPartitionEntityList::OnPostQuery( SpatialPartitionListMask_t listMask )
{
#ifdef CLIENT_DLL
	if ( !( listMask & PARTITION_CLIENT_GAME_EDICTS ) )
		return;
#else
	if ( !( listMask & PARTITION_SERVER_GAME_EDICTS ) )
		return;
#endif

	if ( m_partitionWriteId.load() != std::thread::id() )
		return;

	UnlockPartitionForRead();
}


//=============================================================================
// SOURCESHARP, Phase 13 step 5 — WHY THE WHOLE CCollisionProperty HALF OF THIS
// FILE IS ONE ARM, AND WHY THE HALF ABOVE IS NOT.
//
// CCollisionProperty is CBaseEntity::m_Collision and nothing else. It is
// declared `CNetworkVarEmbedded( CCollisionProperty, m_Collision )` at
// baseentity.h:2032 and c_baseentity.h:1670, and the ONLY way to reach one is
// the entity's own accessor pair, CollisionProp() at baseentity.h:525-526 /
// 2831-2836 and c_baseentity.h:419-420 / 1904-1909. Every holder in the tree
// writes `->CollisionProp()` — trains.cpp:398, func_break.cpp:1053,
// baseentity.cpp:3370, ai_basehumanoid.cpp:114, ai_navigator.cpp:3463,
// npc_advisor.cpp:1534, the tf_pushentity.cpp pairs — and there is no other
// member of this type, no local, and no `new` anywhere.
//
// So ARMED THERE IS NO WAY TO OBTAIN ONE. `using CBaseEntity = IGameEntity`
// takes CollisionProp() with the class and every definition below becomes
// UNREACHABLE rather than wrong. Not one of them can be entered, so not one of
// them has a "parallel answer" to state — which is exactly why they are one arm
// with one reason instead of eighty guards each implying a parallel entity
// arrives here and is answered differently. It does not arrive.
//
// AND THE PARALLEL HIERARCHY HAS ITS OWN COLLIDEABLE, so this is not a gap that
// something later fills. CStandaloneEntity implements ICollideable directly —
// shell_standalone.h:312 says its answer is "CCollisionProperty's answer, which
// the engine reads identically whoever gives it" — and shell_standalone.h:506
// re-inserts into the spatial partition eagerly on every solidity change,
// "which follows CCollisionProperty::UpdatePartition rule for rule". Eagerly is
// the point: there is no lazy dirty list on that side, which is why the sweep
// above is vacuous armed rather than merely disabled.
//
// WHAT STAYS OUTSIDE THIS GUARD, and each for a stated reason:
//
//   UpdateDirtySpatialPartitionEntities()  shell_new_particles_client.cpp's
//       frame calls it by name (UpdateNewEffects' call, carried there since C7
//       particles phase 3), so removing the definition would be a
//       LINK failure rather than a compile one. Armed it drives
//       a permanently-empty list and is a no-op, which is the correct answer.
//
//   CDirtySpatialPartitionEntityList       it is an IPartitionQueryCallback the
//       engine calls, and its OnPreQuery / OnPostQuery pair is a READ LOCK
//       around every partition query, not an entity operation. The lock has to
//       keep pairing armed. Only the parts that touch an SDK entity are guarded
//       — the drain's body (AddEntity's push went with AddEntity).
//
// WHAT THIS MEANS FOR STEP 6, and it is a finding rather than a tidy-up: this
// file is REMOVAL-SET-SHAPED below this line. s5_kind.py's REMOVAL-SET rule
// keys on the scope line naming `CBaseEntity::`, so it sees the 305 out-of-line
// bodies of the doomed class itself and cannot see the bodies of a class that
// is only ever a SUB-OBJECT of it — the mechanical misfile step5_cpp_shards.md
// §2a.1 describes, one level down. 81 of this file's 85 armed sites are that
// shape and were priced as retype. Step 6 should carry the CCollisionProperty
// half out with baseentity.cpp / c_baseentity.cpp; splitting the file is not a
// change a retype shard should make on its own, and `vpc_removal_gate.sh` fails
// the build on any `-$File` not listed in expected_noop_removals.txt.
//=============================================================================
