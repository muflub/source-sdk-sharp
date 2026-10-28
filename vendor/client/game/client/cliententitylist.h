//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#include "tier0/dbg.h"
#include "icliententitylist.h"
#include "iclientunknown.h"
#include "utllinkedlist.h"
#include "utlvector.h"
#include "icliententityinternal.h"
#include "ispatialpartition.h"
#include "cdll_util.h"
#include "entitylist_base.h"
#include "utlmap.h"

class C_Beam;
class C_BaseEntity;

		// The entity thinks it's in the PVS.
		// Accumulated as different views are rendered during the frame and used to notify the entity if
										// it is not in the PVS anymore (at the end of the frame).
		// The entity thinks it's in the PVS.
							   
class IClientEntityListener;

abstract_class C_BaseEntityClassList
{
public:
	C_BaseEntityClassList();
	~C_BaseEntityClassList();
	virtual void LevelShutdown() = 0;

	C_BaseEntityClassList *m_pNextClassList;
};

template< class T >
class C_EntityClassList : public C_BaseEntityClassList
{
public:
	virtual void LevelShutdown()  { m_pClassList = NULL; }

	void Insert( T *pEntity )
	{
		pEntity->m_pNext = m_pClassList;
		m_pClassList = pEntity;
	}

	void Remove( T *pEntity )
	{
		T **pPrev = &m_pClassList;
		T *pCur = *pPrev;
		while ( pCur )
		{
			if ( pCur == pEntity )
			{
				*pPrev = pCur->m_pNext;
				return;
			}
			pPrev = &pCur->m_pNext;
			pCur = *pPrev;
		}
	}

	static T *m_pClassList;
};

// Maximum size of entity list

// This is the IClientEntityList implemenation. It serves two functions:
// 1. It converts server entity indices into IClientNetworkables for the engine.
// 2. It provides a place to store IClientUnknowns and gives out ClientEntityHandle_t's
//    so they can be indexed and retreived. For example, this is how static props are referenced
//    by the spatial partition manager - it doesn't know what is being inserted, so it's 
//	  given ClientEntityHandle_t's, and the handlers for spatial partition callbacks can
//    use the client entity list to look them up and check for supported interfaces.
class CClientEntityList : public CBaseEntityList, public IClientEntityList
{
friend class C_BaseEntityIterator;
friend class C_AllBaseEntityIterator;

public:
	// Constructor, destructor
								CClientEntityList( void );
	virtual 					~CClientEntityList( void );

	void						Release();		// clears everything and releases entities

// Implement IClientEntityList
public:

	virtual IClientNetworkable*	GetClientNetworkable( int entnum );
	virtual IClientEntity*		GetClientEntity( int entnum );

	virtual int					NumberOfEntities( bool bIncludeNonNetworkable = false );

	virtual IClientUnknown*		GetClientUnknownFromHandle( ClientEntityHandle_t hEnt );
	virtual IClientNetworkable*	GetClientNetworkableFromHandle( ClientEntityHandle_t hEnt );
	virtual IClientEntity*		GetClientEntityFromHandle( ClientEntityHandle_t hEnt );

	virtual int					GetHighestEntityIndex( void );

	virtual void				SetMaxEntities( int maxents );
	virtual int					GetMaxEntities( );

// CBaseEntityList overrides.
protected:

	virtual void OnAddEntity( IHandleEntity *pEnt, CBaseHandle handle );
	virtual void OnRemoveEntity( IHandleEntity *pEnt, CBaseHandle handle );

// Internal to client DLL.
public:

	// Is a handle valid?
	bool					IsHandleValid( ClientEntityHandle_t handle ) const;

	class CPVSNotifyInfo
	{
	public:
		IPVSNotify *m_pNotify;
		IClientRenderable *m_pRenderable;
		unsigned char m_InPVSStatus;				// Combination of the INPVS_ flags.
		unsigned short m_PVSNotifiersLink;			// Into m_PVSNotifyInfos.
	};

	CUtlVector<IClientEntityListener *>	m_entityListeners;

private:

	// Cached info for networked entities.
	struct EntityCacheInfo_t
	{
		// Cached off because GetClientNetworkable is called a *lot*
		IClientNetworkable *m_pNetworkable;
		unsigned short m_BaseEntitiesIndex;	// Index into m_BaseEntities (or m_BaseEntities.InvalidIndex() if none).
	};

	// Current count
	int					m_iNumServerEnts;
	// Max allowed
	int					m_iMaxServerEnts;

	int					m_iNumClientNonNetworkable;

	// Current last used slot
	int					m_iMaxUsedServerIndex;

	// This holds fast lookups for special edicts.
	EntityCacheInfo_t	m_EntityCacheInfo[NUM_ENT_ENTRIES];

	// For fast iteration.
	CUtlLinkedList<C_BaseEntity*, unsigned short> m_BaseEntities;

private:

	// These entities want to know when they enter and leave the PVS (server entities
	// already can get the equivalent notification with NotifyShouldTransmit, but client
	// entities have to get it this way).
	CUtlLinkedList<CPVSNotifyInfo,unsigned short> m_PVSNotifyInfos;
	CUtlMap<IClientUnknown*,unsigned short,unsigned short> m_PVSNotifierMap;	// Maps IClientUnknowns to indices into m_PVSNotifyInfos.
};

// Use this to iterate over *all* (even dormant) the C_BaseEntities in the client entity list.
class C_AllBaseEntityIterator
{
public:
	C_AllBaseEntityIterator();

	C_BaseEntity* Next();	// keep calling this until it returns null.

private:
	unsigned short m_CurBaseEntity;
};

class C_BaseEntityIterator
{
public:
	C_BaseEntityIterator();

	C_BaseEntity* Next();	// keep calling this until it returns null.

private:
	unsigned short m_CurBaseEntity;
};

// Inline methods
inline bool	CClientEntityList::IsHandleValid( ClientEntityHandle_t handle ) const
{
	return handle.Get() != 0;
}

inline IClientUnknown* CClientEntityList::GetClientUnknownFromHandle( ClientEntityHandle_t hEnt )
{
	return (IClientUnknown*)LookupEntity( hEnt );
}

// Implement this class and register with entlist to receive entity create/delete notification
class IClientEntityListener
{
public:
	virtual void unused_OnEntityCreated() = 0;
	//virtual void OnEntitySpawned( C_BaseEntity *pEntity ) {};
	virtual void unused_OnEntityDeleted() = 0;
};
