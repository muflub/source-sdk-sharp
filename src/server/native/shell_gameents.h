//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IServerGameEnts: the edict/entity map and the transmit walk. The engine holds the
// root's forwarder (late_bound_game_ents.g.h) from load; this is the DLL tier's
// implementation it forwards to, handed what it calls.

#pragma once

class CServerEntityResolver;

#include "eiface.h"
#include "igameentity_fwd.h"

class CShellTransmitSet;
class IMDLCache;
struct CManagedAreas;

class CBoundaryProbe;

class CServerGameEnts final : public IServerGameEnts
{
public:
	CServerGameEnts( IVEngineServer *pEngine, IMDLCache *pMdlCache, const CManagedAreas &managed,
					 const bool &bManagedLoaded, CShellTransmitSet &transmitSet, CBoundaryProbe &probes,
					 CServerEntityResolver &entities )
		: m_pEngine( pEngine )
		, m_pMdlCache( pMdlCache )
		, m_Managed( managed )
		, m_bManagedLoaded( bManagedLoaded )
		, m_TransmitSet( transmitSet )
		, m_Probes( probes )
		, m_Entities( entities )
	{
	}

	CServerGameEnts( const CServerGameEnts & ) = delete;
	CServerGameEnts &operator=( const CServerGameEnts & ) = delete;

	void SetDebugEdictBase( edict_t *base ) override;
	void MarkEntitiesAsTouching( edict_t *e1, edict_t *e2 ) override;
	void FreeContainingEntity( edict_t * ) override;
	edict_t *BaseEntityToEdict( IGameEntity *pEnt ) override;
	IGameEntity *EdictToBaseEntity( edict_t *pEdict ) override;
	void CheckTransmit( CCheckTransmitInfo *pInfo, const unsigned short *pEdictIndices, int nEdicts ) override;

private:
	// For CheckTransmit: 1 with *pOutArea set to the client's 3D-skybox area, else 0.
	int SkyboxAreaForClient( int iClientEdict, int *pOutArea ) const;

	IVEngineServer *m_pEngine;
	IMDLCache *m_pMdlCache;
	const CManagedAreas &m_Managed;

	// False while the managed side is down, a reload included.
	const bool &m_bManagedLoaded;
	CShellTransmitSet &m_TransmitSet;

	// Counts the engine's calls into this object, for ss_boundary_probe_report.
	CBoundaryProbe &m_Probes;

	// What a touch report's edicts name.
	CServerEntityResolver &m_Entities;
	edict_t *m_pDebugEdictBase = NULL;
};
