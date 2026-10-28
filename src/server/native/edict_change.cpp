//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// edict.h's CBaseEdict::StateChanged, with the accessor and change list handed in.

#include "edict_change.h"
#include "edict.h"

void SourceSharp_MarkEdictChanged( IChangeInfoAccessor *pAccessor, CSharedEdictChangeInfo &shared, edict_t *pEdict,
								   int nOffset )
{
	if ( nOffset < 0 )
	{
		pEdict->m_fStateFlags |= FL_EDICT_CHANGED | FL_FULL_EDICT_CHANGED;
		pAccessor->SetChangeInfoSerialNumber( 0 );
		return;
	}

	if ( pEdict->m_fStateFlags & FL_FULL_EDICT_CHANGED )
		return;

	pEdict->m_fStateFlags |= FL_EDICT_CHANGED;

	CSharedEdictChangeInfo *pShared = &shared;
	const unsigned short offset = (unsigned short)nOffset;

	// This frame's change info is already the edict's: add the offset once.
	if ( pAccessor->GetChangeInfoSerialNumber() == pShared->m_iSerialNumber )
	{
		CEdictChangeInfo &info = pShared->m_ChangeInfos[pAccessor->GetChangeInfo()];

		for ( unsigned short i = 0; i < info.m_nChangeOffsets; i++ )
		{
			if ( info.m_ChangeOffsets[i] == offset )
				return;
		}

		if ( info.m_nChangeOffsets < MAX_CHANGE_OFFSETS )
		{
			info.m_ChangeOffsets[info.m_nChangeOffsets++] = offset;
			return;
		}
	}
	else if ( pShared->m_nChangeInfos < MAX_EDICT_CHANGE_INFOS )
	{
		// The first change this frame: take the next change info.
		pAccessor->SetChangeInfo( pShared->m_nChangeInfos );
		pShared->m_nChangeInfos++;
		pAccessor->SetChangeInfoSerialNumber( pShared->m_iSerialNumber );

		CEdictChangeInfo &info = pShared->m_ChangeInfos[pAccessor->GetChangeInfo()];
		info.m_ChangeOffsets[0] = offset;
		info.m_nChangeOffsets = 1;
		return;
	}

	// No room to remember the offset: the whole edict is sent.
	pAccessor->SetChangeInfoSerialNumber( 0 );
	pEdict->m_fStateFlags |= FL_FULL_EDICT_CHANGED;
}
