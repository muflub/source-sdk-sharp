//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "const.h"
#include "basehandle.h"
#include "utllinkedlist.h"
#include "ihandleentity.h"

class CEntInfo
{
public:
	IHandleEntity	*m_pEntity;
	int				m_SerialNumber;
	CEntInfo		*m_pPrev;
	CEntInfo		*m_pNext;

};

class CBaseEntityList
{
public:
	CBaseEntityList();
	~CBaseEntityList();
	
	// ehandles use this in their Get() function to produce a pointer to the entity.
	IHandleEntity* LookupEntity( const CBaseHandle &handle ) const;

	static CBaseHandle InvalidHandle();

// Overridables.
protected:

	// These are notifications to the derived class. It can cache info here if it wants.
	virtual void OnAddEntity( IHandleEntity *pEnt, CBaseHandle handle );
	
	// It is safe to delete the entity here. We won't be accessing the pointer after
	// calling OnRemoveEntity.
	virtual void OnRemoveEntity( IHandleEntity *pEnt, CBaseHandle handle );

private:

private:
	
	class CEntInfoList
	{
	public:
		CEntInfoList();

		const CEntInfo	*Head() const { return m_pHead; }
		const CEntInfo	*Tail() const { return m_pTail; }
		CEntInfo		*Head() { return m_pHead; }
		CEntInfo		*Tail() { return m_pTail; }
		void			AddToHead( CEntInfo *pElement ) { LinkAfter( NULL, pElement ); }
		void			AddToTail( CEntInfo *pElement ) { LinkBefore( NULL, pElement ); }

		void LinkBefore( CEntInfo *pBefore, CEntInfo *pElement );
		void LinkAfter( CEntInfo *pBefore, CEntInfo *pElement );
		void Unlink( CEntInfo *pElement );
		bool IsInList( CEntInfo *pElement );
	
	private:
		CEntInfo		*m_pHead;
		CEntInfo		*m_pTail;
	};

	// The first MAX_EDICTS entities are networkable. The rest are client-only or server-only.
	CEntInfo m_EntPtrArray[NUM_ENT_ENTRIES];
	CEntInfoList	m_activeList;
	CEntInfoList	m_freeNonNetworkableList;
};

// Inlines.

inline IHandleEntity* CBaseEntityList::LookupEntity( const CBaseHandle &handle ) const
{
	if ( handle.m_Index == INVALID_EHANDLE )
		return NULL;

	const CEntInfo *pInfo = &m_EntPtrArray[ handle.GetEntryIndex() ];
	if ( pInfo && pInfo->m_SerialNumber == handle.GetSerialNumber() )
		return pInfo->m_pEntity;
	else
		return NULL;
}

inline CBaseHandle CBaseEntityList::InvalidHandle()
{
	return INVALID_EHANDLE;
}

extern CBaseEntityList *g_pEntityList;
