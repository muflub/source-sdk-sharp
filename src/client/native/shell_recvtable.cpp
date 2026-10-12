//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's type table, and the tier ABI ==========//
// SS_REASON f  a bit-level wire codec
//
// This file registered the runtime-networked entity's ClientClasses. That shell
// is deleted — it was a C_BaseEntity, and step 6 makes C_BaseEntity an alias
// for IGameEntity, so `new C_SourceSharpNetEntity()` would not have compiled.
// The parallel hierarchy registers its own tiers in shell_standalone_client.cpp.
//
// What is left is the client's half of two ABI calls that were never about that
// shell — publishing a managed type and reporting a tier — plus the dump
// command over the table they build. The server's half is in
// shell_standalone.cpp, and the two must stay mirrors: a type published on one
// side and not the other is the failure with no symptom.
//
//=============================================================================//

#include "cbase.h"
#include "shell_standalone_client.h"
#include "shell_diagnostics.h"
#include "generic_nettable.h"
#include "generic_payload.h"
#include "sourcesharp.h"
#include "dt_recv.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// One tier, as this side registered it — the client's half of what NetTiers
// reads instead of projecting. Reports the props NATIVE BUILT rather than a
// formula: a boundary rule with two implementations drifts silently.
//-----------------------------------------------------------------------------
int32_t SourceSharp_NetTierInfo( int32_t nShell, int32_t iTier, const char **ppOutTable,
                                 int32_t *pOutBytes, int32_t *pOutWords,
                                 int32_t *pOutProps, int32_t *pOutLive )
{
	// No registration call: the hierarchy's tiers are registered at DLL init by
	// CSourceSharpStandaloneClientSystem, SourceSharp_StandaloneTierIsInUse
	// below forces them again, and everything else read here is a compile-time
	// constant in generic_nettable.h.
	if ( iTier < 0 || iTier >= SourceSharp_TierCount() )
		return 0;

	const int nWords = SourceSharp_TierWords( iTier );
	const int nReal  = SourceSharp_TierRealProps( nShell );

	if ( ppOutTable )
		*ppOutTable = SourceSharp_TierTableName( nShell, iTier );
	if ( pOutBytes )
		*pOutBytes = SourceSharp_Tiers( NULL )[ iTier ].nBytes;
	if ( pOutWords )
		*pOutWords = nWords;
	if ( pOutProps )
		*pOutProps = nReal + nWords;

	// "Live" is the tier entities are actually CREATED THROUGH, which is not
	// the same as the tier being registered. Every tier is registered so the
	// class list the handshake negotiates is the full set and a type can grow
	// into a bigger one without a rebuilt client — so a registered, empty tier
	// is the ordinary state and means "room to grow into". A panel that showed
	// every tier as live would be reporting the registration and calling it the
	// assignment.
	if ( pOutLive )
		*pOutLive = SourceSharp_StandaloneTierIsInUse( iTier ) ? 1 : 0;

	return 1;
}

//-----------------------------------------------------------------------------
// C# publishing one managed type, client half. The mirror of the server's, and
// it must stay one: the type table is what an arriving entity's type id is
// resolved through, and a name only one side published is an entity the other
// cannot pair with a managed class.
//-----------------------------------------------------------------------------
int32_t SourceSharp_NetPublishType( const char *pClassName, int32_t nShell,
                                    uint32_t nSchemaHash, int32_t nLayoutBytes,
                                    uint32_t *pOutTypeId )
{
	if ( pOutTypeId )
		*pOutTypeId = pClassName ? SourceSharp_HashClassName( pClassName ) : 0;

	if ( !pClassName || !pClassName[0] )
		return -1;

	// The retired shell is refused BY NAME rather than quietly taken for the
	// live one. Mirror of the server's; keep the two identical.
	if ( nShell == SS_SHELL_NETENTITY )
	{
		Warning( "[SourceSharp] type '%s': shell %d is the RETIRED runtime-networked "
		         "entity, which no longer exists; NOT published. A managed build "
		         "publishing on it is older than this native one.\n",
			pClassName, (int)nShell );
		return -1;
	}

	// THE TIER IS THE TYPE'S (Part 18) — the mirror of the server's, and it must
	// stay one. Both sides reach it by the same rule from the same declared
	// size, so a class is on the same tier on both without anything about the
	// tier being on the wire.
	const int iTier = SourceSharp_TierForSize( nLayoutBytes );

	if ( iTier < 0 )
	{
		Warning( "[SourceSharp] type '%s': %d layout byte(s) exceeds the largest tier; "
		         "NOT published\n", pClassName, nLayoutBytes );
		return -1;
	}

	if ( SourceSharp_NetTypes().Publish( pClassName, nShell, nSchemaHash,
	                                     nLayoutBytes, iTier ) < 0 )
	{
		Warning( "[SourceSharp] type '%s' (id 0x%08x) was REFUSED by the type table: either a "
		         "name collision with a type already published, or the table is full "
		         "(%d entries). Entities of it cannot be paired with a managed class.\n",
			pClassName, SourceSharp_HashClassName( pClassName ), SOURCESHARP_TYPES_MAX );
		return -1;
	}

	return iTier;
}

//-----------------------------------------------------------------------------
// THE CLIENT'S RECV TABLES AS THE ENGINE WALKS THEM, one prop per index, for
// the dev API's /client/{slot}/nettables (plan_client_novalve §4.4).
//
// Mechanism only: this reports what the tables hold, and managed decides what
// agrees with what. Every RecvTable reachable from the ClientClass list, each
// once (a table a DPT_DataTable prop names is listed where it was first
// reached), props in table order. iIndex past the end fills nothing; the
// answer is always the total, so a caller asks index 0 and then walks.
//
// Walked per call rather than cached, so there is no static to keep in step
// with a class list the engine owns; the route is a debug read.
//-----------------------------------------------------------------------------
static void CopyName( char *pOut, int32_t nBytes, const char *pName )
{
	if ( pOut && nBytes > 0 )
		V_strncpy( pOut, pName ? pName : "", nBytes );
}

int32_t SourceSharp_ClientRecvProp( int32_t iIndex, char *pOutClass, int32_t nClassBytes,
                                    char *pOutTable, int32_t nTableBytes, char *pOutProp,
                                    int32_t nPropBytes, int32_t *pOutType, int32_t *pOutFlags,
                                    int32_t *pOutOffset, int32_t *pOutElements )
{
	CUtlVector< RecvTable * > tables;
	CUtlVector< const char * > owners;

	for ( ClientClass *pClass = g_pClientClassHead; pClass; pClass = pClass->m_pNext )
	{
		if ( !pClass->m_pRecvTable || tables.Find( pClass->m_pRecvTable ) >= 0 )
			continue;

		tables.AddToTail( pClass->m_pRecvTable );
		owners.AddToTail( pClass->GetName() );
	}

	// Breadth over the nested tables, appended as they are found.
	for ( int t = 0; t < tables.Count(); t++ )
	{
		RecvTable *pTable = tables[t];
		for ( int i = 0; i < pTable->GetNumProps(); i++ )
		{
			RecvTable *pNested = pTable->GetProp( i )->GetDataTable();
			if ( pNested && tables.Find( pNested ) < 0 )
			{
				tables.AddToTail( pNested );
				owners.AddToTail( "" );
			}
		}
	}

	int32_t nTotal = 0;
	for ( int t = 0; t < tables.Count(); t++ )
	{
		RecvTable *pTable = tables[t];
		for ( int i = 0; i < pTable->GetNumProps(); i++, nTotal++ )
		{
			if ( nTotal != iIndex )
				continue;

			const RecvProp *pProp = pTable->GetProp( i );
			CopyName( pOutClass, nClassBytes, owners[t] );
			CopyName( pOutTable, nTableBytes, pTable->GetName() );
			CopyName( pOutProp, nPropBytes, pProp->GetName() );
			if ( pOutType )     *pOutType = (int32_t)pProp->GetType();
			if ( pOutFlags )    *pOutFlags = pProp->GetFlags();
			if ( pOutOffset )   *pOutOffset = pProp->GetOffset();
			if ( pOutElements ) *pOutElements = pProp->GetNumElements();
		}
	}

	return nTotal;
}

//-----------------------------------------------------------------------------
// The schema-hash refusals this client has made (SourceSharpSchema_Accept):
// the send/recv agreement as the payload decoder judged it. Returns how many
// types were refused; fills one when iIndex is in range, and the refused
// update count always.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientSchemaRefusal( int32_t iIndex, uint32_t *pOutTypeId, uint32_t *pOutExpected,
                                         uint32_t *pOutSeen, int32_t *pOutCount, char *pOutClass,
                                         int32_t nClassBytes, int32_t *pOutRefusedUpdates )
{
	if ( pOutRefusedUpdates )
		*pOutRefusedUpdates = SourceSharpSchema_RefusedUpdates();

	int nTypes = SourceSharpSchema_RefusedTypes();
	if ( iIndex >= 0 && iIndex < nTypes )
	{
		unsigned int nTypeId = 0, nExpected = 0, nSeen = 0;
		const char *pClassName = NULL;
		int nCount = 0;
		if ( SourceSharpSchema_Refusal( iIndex, &nTypeId, &nExpected, &nSeen, &pClassName, &nCount ) )
		{
			if ( pOutTypeId )   *pOutTypeId = nTypeId;
			if ( pOutExpected ) *pOutExpected = nExpected;
			if ( pOutSeen )     *pOutSeen = nSeen;
			if ( pOutCount )    *pOutCount = nCount;
			CopyName( pOutClass, nClassBytes, pClassName );
		}
	}

	return nTypes;
}
