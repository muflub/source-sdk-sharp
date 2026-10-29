//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The spatial partition's query callback. OnPreQuery/OnPostQuery are a read lock the
// engine takes around every partition query, from inside the query.

#ifndef CLIENT_DLL
#include "basehandle.h"
#include "igameentity.h"
#include "ispatialpartition.h"
#include "tier0/tslist.h"
#include "shell_serversystem.h"

#include <atomic>
#include <shared_mutex>
#include <thread>
#include <vector>

extern ISpatialPartition *partition;
#else

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"

#include "shell_clientsystem.h"

#include <atomic>
#include <shared_mutex>
#include <thread>
#include <vector>

#include "predictable_entity.h"

#endif // !CLIENT_DLL

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
	virtual void OnPreQuery_V1() { Assert( 0 ); }

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
	std::shared_mutex m_partitionMutex;
	// The thread holding the write lock; a default id (no thread) otherwise.
	std::atomic<std::thread::id> m_partitionWriteId{};
	// This thread's read-lock depth; per thread, as there is one list.
	static thread_local int t_readLockCount;
};

thread_local int CDirtySpatialPartitionEntityList::t_readLockCount = 0;

static CDirtySpatialPartitionEntityList s_DirtyKDTree( "CDirtySpatialPartitionEntityList" );

#ifdef CLIENT_DLL
CClientSystem *ClientSystem_DirtySpatialPartition()
{
	return &s_DirtyKDTree;
}
#endif

#ifndef CLIENT_DLL
// Lets the server's system list name this object, which has internal linkage.
CServerSystem *DirtySpatialPartitionGameSystem()
{
	return &s_DirtyKDTree;
}
#endif // !CLIENT_DLL

// Forces spatial partition updates, avoiding threading problems from lazy update.
#ifdef CLIENT_DLL
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

#ifdef CLIENT_DLL
#ifdef CLIENT_DLL
CDirtySpatialPartitionEntityList::CDirtySpatialPartitionEntityList( char const *name )
	: CClientSystem( name )
#else
CDirtySpatialPartitionEntityList::CDirtySpatialPartitionEntityList( char const *name )
	: CServerSystem( name )
#endif
#else
CDirtySpatialPartitionEntityList::CDirtySpatialPartitionEntityList( char const *name )
	: CServerSystem( name )
#endif
{
	m_DirtyEntities.Purge();
}

CDirtySpatialPartitionEntityList::~CDirtySpatialPartitionEntityList()
{
	m_DirtyEntities.Purge();
}

bool CDirtySpatialPartitionEntityList::Init()
{
	::partition->InstallQueryCallback( this );
	return true;
}

void CDirtySpatialPartitionEntityList::Shutdown()
{
	::partition->RemoveQueryCallback( this );
}

void CDirtySpatialPartitionEntityList::LevelShutdownPostEntity()
{
	m_DirtyEntities.RemoveAll();
}

// Makes sure all entries in the KD tree are in the correct position.
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

	// if you're holding a read lock, then these are entities that were still dirty after your trace started
	// or became dirty due to some other thread or callback. Updating them may cause corruption further up the
	// stack (e.g. partition iterator).  Ignoring the state change should be safe since it happened after the
	// trace was requested or was unable to be resolved in a previous attempt (still dirty).
	if ( m_DirtyEntities.Count() && !t_readLockCount )
	{
		std::vector<CBaseHandle> vecStillDirty;
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

				// Nothing fills the list; the drain still frees anything that arrives.
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

// Releases the read lock OnPreQuery took.
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
