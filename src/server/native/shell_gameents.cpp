//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// IServerGameEnts: the edict/entity map and the transmit walk.

#include "shell_gameents.h"
#include "ss_util.h"
#include "server_class.h"

#include "datacache/imdlcache.h"
#include "shell_steporder.h"

#include "shell_boundary_probe.h"

#include "shell_server.h"
#include "imanaged_areas.h"

#include "toucher.h"
#include "shell_managed_areas.h"

void CServerGameEnts::SetDebugEdictBase( edict_t *base )
{
	SS_BOUNDARY_HIT( m_Probes, kBoundaryProbe_Ents_SetDebugEdictBase );

	m_pDebugEdictBase = base;
}

void CServerGameEnts::MarkEntitiesAsTouching( edict_t *e1, edict_t *e2 )
{
	SS_BOUNDARY_HIT( m_Probes, kBoundaryProbe_Ents_MarkEntitiesAsTouching );

	// No edict has a CBaseEntity behind it on this build.
	IGameEntity *entity = NULL;
	IGameEntity *entityTouched = NULL;
	if ( entity && entityTouched )
	{
		// HACKHACK: UNDONE: Pass in the trace here??!?!?
		trace_t tr;
		UTIL_ClearTrace( tr );
	}

	// At least one of the two is not a CBaseEntity. A touch is symmetric, so each
	// standalone side gets a link naming the other; an SDK side is told by the
	// standalone one's own link bookkeeping.
	const Toucher_t toucher1 = ToucherFromEntityHandle( e1->GetUnknown() );
	const Toucher_t toucher2 = ToucherFromEntityHandle( e2->GetUnknown() );

	if ( toucher1.pGame && toucher2.IsValid() )
		toucher1.pGame->Touch( toucher2 );

	if ( toucher2.pGame && toucher1.IsValid() )
		toucher2.pGame->Touch( toucher1 );
}

void CServerGameEnts::FreeContainingEntity( edict_t *e )
{
	SS_BOUNDARY_HIT( m_Probes, kBoundaryProbe_Ents_FreeContainingEntity );

	// No edict has a CBaseEntity behind it, so there is nothing to free; the edict is the engine's.
	(void)e;
}

edict_t *CServerGameEnts::BaseEntityToEdict( IGameEntity *pEnt )
{
	// One return, so the boundary probe sees every answer.
	edict_t *pResult = NULL;

	SS_BOUNDARY_HIT_PTR( m_Probes, kBoundaryProbe_Ents_BaseEntityToEdict, pResult );
	return pResult;
}

IGameEntity *CServerGameEnts::EdictToBaseEntity( edict_t *pEdict )
{
	// No edict has a CBaseEntity behind it. One return, for the boundary probe.
	IGameEntity *pResult = NULL;

	SS_BOUNDARY_HIT_PTR( m_Probes, kBoundaryProbe_Ents_EdictToBaseEntity, pResult );
	return pResult;
}

void CServerGameEnts::CheckTransmit( CCheckTransmitInfo *pInfo, const unsigned short *pEdictIndices, int nEdicts )
{
	SS_BOUNDARY_HIT( m_Probes, kBoundaryProbe_Ents_CheckTransmit );

	// NOTE: for speed's sake, this assumes that all networkables are CBaseEntities and that the edict list
	// is consecutive in memory. If either of these things change, then this routine needs to change, but
	// ideally we won't be calling any virtual from this routine. This speedy routine was added as an
	// optimization which would be nice to keep.
	edict_t *pBaseEdict = m_pEngine->PEntityOfEntIndex( 0 );

	// Read only by the Assert below.
	[[maybe_unused]] IGameEntity *pRecipientEntity = NULL;

	// Which hierarchy answers for this client is decided in C#. Edict 0 when the
	// engine names no client, as stock's fallback.
	const int nRecipientIndex = pInfo->m_pClientEnt ? (int)( pInfo->m_pClientEnt - pBaseEdict ) : 0;

	int skyBoxArea = 0;
	const bool bHaveRecipient = SkyboxAreaForClient( nRecipientIndex, &skyBoxArea ) != 0;

	Assert( bHaveRecipient && !pRecipientEntity );
	if ( !bHaveRecipient )
		return;

	// Armed after the early return, so a declined recipient doesn't spend the
	// one-shot capture. The set is copied at the end, since SetTransmit marks bits
	// this walk never touches.
	const bool bCaptureTransmit = m_TransmitSet.BeginCapture( nRecipientIndex );

	if ( bCaptureTransmit )
		m_TransmitSet.SetSkyboxArea( skyBoxArea );

	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );

	// Stock's CBasePlayer::ShouldTransmit first arm, decided in C#: the
	// recipient's own player is always sent. Marked before the walk, which skips
	// edicts already marked.
	if ( IManagedClients *pApi = m_Managed.m_pClients )
	{
		const int iAlways = pApi->ServerTransmitAlwaysForClient( nRecipientIndex );
		if ( iAlways > 0 && iAlways < m_pEngine->GetEntityCount() && !pBaseEdict[iAlways].IsFree() )
			pInfo->m_pTransmitEdict->Set( iAlways );
	}

	// A SourceTV client gets ordinary PVS-culled transmit; launches pass -nohltv.
	for ( int i = 0; i < nEdicts; i++ )
	{
		int iEdict = pEdictIndices[i];

		edict_t *pEdict = &pBaseEdict[iEdict];
		Assert( pEdict == m_pEngine->PEntityOfEntIndex( iEdict ) );
		int nFlags =
			pEdict->m_fStateFlags & ( FL_EDICT_DONTSEND | FL_EDICT_ALWAYS | FL_EDICT_PVSCHECK | FL_EDICT_FULLCHECK );

		// entity needs no transmit
		if ( nFlags & FL_EDICT_DONTSEND )
			continue;

		// entity is already marked for sending
		if ( pInfo->m_pTransmitEdict->Get( iEdict ) )
			continue;

		if ( nFlags & FL_EDICT_ALWAYS )
		{
			// mark entity for sending
			pInfo->m_pTransmitEdict->Set( iEdict );
			continue;
		}

		// Visibility alone: these entities have no ShouldTransmit, hierarchy or area.
		if ( !( nFlags & FL_EDICT_PVSCHECK ) )
			continue;

		ICollideable *pCollide = pEdict->GetCollideable();
		if ( !pCollide )
			continue;

		Vector vecMins, vecMaxs;
		pCollide->WorldSpaceSurroundingBounds( &vecMins, &vecMaxs );

		if ( m_pEngine->CheckBoxInPVS( vecMins, vecMaxs, pInfo->m_PVS, pInfo->m_nPVSSize ) )
			pInfo->m_pTransmitEdict->Set( iEdict );
	}

	if ( bCaptureTransmit )
	{
		COMPILE_TIME_ASSERT( CShellTransmitSet::kMaxEdicts == MAX_EDICTS );

		for ( int i = 0; i < MAX_EDICTS; i++ )
		{
			if ( pInfo->m_pTransmitEdict->Get( i ) )
				m_TransmitSet.Mark( i );
		}
	}
}

int CServerGameEnts::SkyboxAreaForClient( int iClientEdict, int *pOutArea ) const
{
	IManagedClients *pApi = m_bManagedLoaded ? m_Managed.m_pClients : NULL;
	if ( !pApi )
		return 0;

	int32_t nArea = 0;
	const int32_t nFound = pApi->ServerSkyboxAreaForClient( iClientEdict, &nArea );

	// Written only on a hit: area 0 is a real area.
	if ( nFound )
		*pOutArea = nArea;

	return nFound ? 1 : 0;
}
