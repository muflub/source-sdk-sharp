//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "igamesystem.h"
#include "utllinkedlist.h"
#include "cliententitylist.h"
#include "iclientthinkable.h"
#include "utlrbtree.h"

#define CLIENT_THINK_ALWAYS	-1293

#define INVALID_THINK_HANDLE ClientThinkList()->GetInvalidThinkHandle()

class CClientThinkList : public IGameSystemPerFrame
{
public:

							CClientThinkList();
	virtual					~CClientThinkList();
	
	virtual char const		*Name() { return "CClientThinkList"; }
	virtual bool			IsPerFrame() { return true; }

	// Use to initialize your think handles in IClientThinkables.
	ClientThinkHandle_t		GetInvalidThinkHandle();

// IClientSystem implementation.
public:

	virtual bool Init();
	virtual void PostInit() {};
	virtual void Shutdown();
	virtual void LevelInitPreEntity();
	virtual void LevelInitPostEntity() {}
	virtual void LevelShutdownPreEntity();
	virtual void LevelShutdownPostEntity();
	virtual void PreRender();
	virtual void PostRender() { }
	virtual void Update( float frametime );
	virtual void OnSave() {}
	virtual void OnRestore() {}
	virtual void SafeRemoveIfDesired() {}

private:
	struct ThinkEntry_t
	{
		ClientEntityHandle_t	m_hEnt;
		float					m_flNextClientThink;
		float					m_flLastClientThink;
		int						m_nIterEnum;
	};

	struct ThinkListChanges_t
	{
		ClientEntityHandle_t	m_hEnt;
		ClientThinkHandle_t		m_hThink;
		float					m_flNextTime;
	};

// Internal stuff.
private:

private:
	CUtlLinkedList<ThinkEntry_t, unsigned short>	m_ThinkEntries;

	CUtlVector<ClientEntityHandle_t>	m_aDeleteList;
	CUtlVector<ThinkListChanges_t>		m_aChangeList;

	// Makes sure the entries are thinked once per frame in the face of hierarchy
	int m_nIterEnum;
	bool m_bInThinkLoop;
};

// Inlines.

inline ClientThinkHandle_t CClientThinkList::GetInvalidThinkHandle()
{
	return (ClientThinkHandle_t)(uintp)m_ThinkEntries.InvalidIndex();
}

inline CClientThinkList* ClientThinkList()
{
	extern CClientThinkList g_ClientThinkList;
	return &g_ClientThinkList;
}
