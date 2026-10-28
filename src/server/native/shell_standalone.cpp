//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON f  a bit-level wire codec
//
// The per-tier ServerClasses every networked standalone entity uses.

#include "shell_standalone.h"
#include "shell_standalone_player.h"
#include "standalone_nettable.h"
#include "generic_nettable.h"
#include "generic_payload.h"
#include "sourcesharp.h"

#include <vector>

std::vector< CStandaloneEntity * > &StandaloneEntities();

static void BuildStandaloneProps( SendProp *pOut, int &nOut )
{
	CStandaloneEntity *pNull = NULL;

	// Our members' offsets in the shared list's order; names, order and encoding come from
	// standalone_nettable.h.
	int nOffsets[ STANDALONE_NET_FIELD_COUNT ];
	// Block fields use the same arithmetic as MarkBlockChanged: the engine matches change
	// offsets against SendProp offsets exactly.
	nOffsets[ STANDALONE_NET_FIELD_ORIGIN ]       = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( Origin );
	nOffsets[ STANDALONE_NET_FIELD_ANGLES ]       = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( Angles );
	nOffsets[ STANDALONE_NET_FIELD_MODELINDEX ]   = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( ModelIndex );
	nOffsets[ STANDALONE_NET_FIELD_EFFECTS ]      = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( Effects );
	nOffsets[ STANDALONE_NET_FIELD_INTERPFRAME ]  = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( InterpolationFrame );
	nOffsets[ STANDALONE_NET_FIELD_TYPEID ]       = (int)( (intp)&pNull->m_nTypeId );
	nOffsets[ STANDALONE_NET_FIELD_SCHEMAHASH ]   = (int)( (intp)&pNull->m_nSchemaHash );
	nOffsets[ STANDALONE_NET_FIELD_PAYLOADWORDS ] = (int)( (intp)&pNull->m_nPayloadWords );
	nOffsets[ STANDALONE_NET_FIELD_SOLIDTYPE ]    = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( SolidType );
	nOffsets[ STANDALONE_NET_FIELD_SOLIDFLAGS ]   = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( SolidFlags );
	nOffsets[ STANDALONE_NET_FIELD_SKIN ]         = CStandaloneEntity::BlockBase()
	                                              + (int)offsetof( EngineBlock_t, Client )
	                                              + (int)offsetof( EngineBlockClientTail_t, Skin );
	nOffsets[ STANDALONE_NET_FIELD_BODY ]         = CStandaloneEntity::BlockBase()
	                                              + (int)offsetof( EngineBlock_t, Client )
	                                              + (int)offsetof( EngineBlockClientTail_t, Body );

	int nCount = 0;
	const StandaloneNetField_t *pFields = StandaloneNetFields( &nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		switch ( pFields[i].kind )
		{
		case STANDALONE_NET_VECTOR:
			pOut[ nOut++ ] = SendPropVector( pFields[i].pName, nOffsets[i], 12, 32, SPROP_NOSCALE );
			break;

		case STANDALONE_NET_INT:
			pOut[ nOut++ ] = SendPropInt( pFields[i].pName, nOffsets[i], 4,
			                              pFields[i].nBits, SPROP_UNSIGNED );
			break;

		case STANDALONE_NET_STRING:
			pOut[ nOut++ ] = SendPropString( pFields[i].pName, nOffsets[i], pFields[i].nBits );
			break;
		}
	}
}

// One ServerClass per tier for the whole hierarchy, all registered at DLL init: the
// engine walks g_pServerClassHead once, and the class list is negotiated at connect.
static SendProp    *g_pTierProps[ SOURCESHARP_TIER_MAX ]   = { NULL };
static SendTable   *g_pTierTables[ SOURCESHARP_TIER_MAX ]  = { NULL };
static ServerClass *g_pTierClasses[ SOURCESHARP_TIER_MAX ] = { NULL };
static bool         g_bTiersRegistered = false;

static void RegisterStandaloneTiers( void )
{
	if ( g_bTiersRegistered )
		return;

	g_bTiersRegistered = true;

	int nShared = 0;
	StandaloneNetFields( &nShared );

	const int nBlobBase = SourceSharp_StandaloneBlobOffset();

	Msg( "[SourceSharp] standalone: blob offset %d (entity shell %d, player shell %d)\n",
		nBlobBase, (int)sizeof( CStandaloneEntity ), (int)sizeof( CStandalonePlayer ) );

	for ( int iTier = 0; iTier < SourceSharp_TierCount(); iTier++ )
	{
		const int nWords = SourceSharp_TierWords( iTier );
		const char *pClassName = SourceSharp_TierClassName( SS_SHELL_STANDALONE, iTier );
		const char *pTableName = SourceSharp_TierTableName( SS_SHELL_STANDALONE, iTier );

		if ( nWords < 0 || !SourceSharpPayload_OffsetsFit( nBlobBase, nWords ) )
		{
			Warning( "[SourceSharp] standalone tier '%s': %d word(s) will not fit at blob "
			         "base %d; NOT registered — every entity on the hierarchy would then "
			         "have no class at all\n", pTableName, nWords, nBlobBase );
			continue;
		}

		g_pTierProps[ iTier ] = new SendProp[ nShared + nWords ];

		int nBuilt = 0;
		BuildStandaloneProps( g_pTierProps[ iTier ], nBuilt );

		if ( SourceSharpPayload_BuildSendProps( g_pTierProps[ iTier ] + nBuilt,
		                                        nWords, nBlobBase ) != nWords )
		{
			Warning( "[SourceSharp] standalone tier '%s': payload build refused %d word(s); "
			         "not registered\n", pTableName, nWords );
			delete[] g_pTierProps[ iTier ];
			g_pTierProps[ iTier ] = NULL;
			continue;
		}

		nBuilt += nWords;

		g_pTierTables[ iTier ]  = new SendTable( g_pTierProps[ iTier ], nBuilt, pTableName );
		g_pTierClasses[ iTier ] = new ServerClass( pClassName, g_pTierTables[ iTier ] );

		int nSlow = 0;

		for ( int i = 0; i < nWords; i++ )
		{
			if ( !SourceSharpPayload_IsFastSendProxy( g_pTierTables[ iTier ]->GetProp( nShared + i ) ) )
				nSlow++;
		}

		Msg( "[SourceSharp] standalone tier %d '%s' -> '%s': %d real + %d payload word(s) "
		     "= %d prop(s), %d payload byte(s) at blob base %d\n",
			iTier, pClassName, pTableName, nShared, nWords, nBuilt,
			SourceSharp_Tiers( NULL )[ iTier ].nBytes, nBlobBase );

		if ( nSlow > 0 )
		{
			Warning( "[SourceSharp] standalone tier '%s': %d of %d payload word(s) did NOT "
			         "resolve to m_UInt32ToInt32 and would be copied slowly, proxies and "
			         "all\n", pTableName, nSlow, nWords );
		}
	}
}

// Whether a ServerClass is one of the tiers, by identity rather than by name.
bool SourceSharp_IsStandaloneTierServerClass( const ServerClass *pClass )
{
	if ( pClass == NULL )
		return false;

	for ( int i = 0; i < SOURCESHARP_TIER_MAX; i++ )
	{
		if ( g_pTierClasses[ i ] == pClass )
			return true;
	}

	return false;
}

// Whether any published class of a shell is on a tier.
static bool SourceSharp_TierHasPublishedTypes( int nShell, int iTier )
{
	const CSourceSharpTypeTable &types = SourceSharp_NetTypes();

	for ( int i = 0; i < types.Count(); i++ )
	{
		const SourceSharpNetType_t *pType = types.At( i );

		if ( pType && pType->nShell == nShell && pType->iTier == iTier )
			return true;
	}

	return false;
}

// The hierarchy's largest declared blob size, checked against the largest tier. Returns
// the payload word count the greediest class will carry.
int32_t SourceSharp_NetHierarchyDeclare( int32_t nBlobBytes )
{
	// Idempotent; the first managed startup call that touches the hierarchy.
	RegisterStandaloneTiers();

	const int iTier = SourceSharp_TierForSize( nBlobBytes );

	if ( iTier < 0 )
	{
		Warning( "[SourceSharp] standalone: %d declared blob byte(s) exceeds the largest tier "
		         "(%d); that class has NO table it can use and its entities will not "
		         "replicate\n", nBlobBytes,
			SourceSharp_TierBytes( SourceSharp_TierCount() - 1 ) );
		return -1;
	}

	const int nWords = SourceSharp_TierWords( iTier );

	Msg( "[SourceSharp] standalone: the greediest declaration is %d byte(s) -> tier %d '%s' "
	     "(%d bytes, %d payload word(s)); every OTHER class is sized to its own tier, "
	     "blob base %d\n",
		nBlobBytes, iTier, SourceSharp_TierTableName( SS_SHELL_STANDALONE, iTier ),
		SourceSharp_TierBytes( iTier ), nWords, SourceSharp_StandaloneBlobOffset() );

	return nWords;
}

// C# publishing one managed type: a name, its layout hash and size. Answers the type's
// tier. pOutTypeId hands back native's FNV-1a of the name so C# can check its own.
int32_t SourceSharp_NetPublishType( const char *pClassName, int32_t nShell,
                                    uint32_t nSchemaHash, int32_t nLayoutBytes,
                                    uint32_t *pOutTypeId )
{
	if ( pOutTypeId )
		*pOutTypeId = pClassName ? SourceSharp_HashClassName( pClassName ) : 0;

	if ( !pClassName || !pClassName[0] )
		return -1;

	// Mirror of the client's refusal in shell_recvtable.cpp; keep the two identical.
	if ( nShell == SS_SHELL_NETENTITY )
	{
		Warning( "[SourceSharp] type '%s': shell %d is the RETIRED runtime-networked "
		         "entity, which no longer exists; NOT published. A managed build "
		         "publishing on it is older than this native one.\n",
			pClassName, (int)nShell );
		return -1;
	}

	RegisterStandaloneTiers();

	const int iTier = SourceSharp_TierForSize( nLayoutBytes );

	if ( iTier < 0 )
	{
		Warning( "[SourceSharp] type '%s': %d layout byte(s) exceeds the largest tier; "
		         "NOT published, and its entities will carry no state\n",
			pClassName, nLayoutBytes );
		return -1;
	}

	if ( SourceSharp_NetTypes().Publish( pClassName, nShell, nSchemaHash, nLayoutBytes, iTier ) < 0 )
	{
		// A name collision (same FNV-1a) or a full table.
		Warning( "[SourceSharp] type '%s' (id 0x%08x) was REFUSED by the type table: either a "
		         "name collision with a type already published, or the table is full "
		         "(%d entries). Its entities cannot be paired with a managed class.\n",
			pClassName, SourceSharp_HashClassName( pClassName ), SOURCESHARP_TYPES_MAX );
		return -1;
	}

	return iTier;
}

// One tier as native registered it, with the props native actually built.
int32_t SourceSharp_NetTierInfo( int32_t nShell, int32_t iTier, const char **ppOutTable,
                                 int32_t *pOutBytes, int32_t *pOutWords,
                                 int32_t *pOutProps, int32_t *pOutLive )
{
	RegisterStandaloneTiers();

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

	// Live: some published type is on this tier. Every tier is registered so types can grow
	// without a rebuilt client, so registered-and-empty is normal.
	if ( pOutLive )
	{
		*pOutLive = SourceSharp_TierHasPublishedTypes( nShell, iTier ) ? 1 : 0;
	}

	return 1;
}

// Checks that each payload word's SendProp offset reads the entity's blob. False with
// *pOutWords == 0 for an entity whose class carries no payload.
bool SourceSharp_StandalonePayloadAgrees( CStandaloneEntity *pEntity,
                                          int *pOutWords, int *pOutFirstBadWord )
{
	if ( pOutWords )
		*pOutWords = 0;
	if ( pOutFirstBadWord )
		*pOutFirstBadWord = -1;

	if ( !pEntity )
		return false;

	// The entity's own tier's table.
	const int iTier = pEntity->Tier();

	if ( iTier < 0 || iTier >= SourceSharp_TierCount() )
		return false;

	SendTable *pTable = g_pTierTables[ iTier ];

	if ( !pTable )
		return false;

	int nShared = 0;
	StandaloneNetFields( &nShared );

	const int nWords = SourceSharp_TierWords( iTier );

	if ( nWords <= 0 )
		return false;

	if ( pOutWords )
		*pOutWords = nWords;

	const unsigned char *pObject = (const unsigned char *)pEntity;
	const unsigned char *pBlob   = pEntity->DataBlob();
	const int nBlobBytes = pEntity->BlobBytes();

	bool bAgrees = true;

	for ( int i = 0; i < nWords; i++ )
	{
		const SendProp *pProp = pTable->GetProp( nShared + i );
		const int nWordEnd = ( i + 1 ) * SOURCESHARP_PAYLOAD_WORD_BYTES;

		// A word past the blob would make the engine read past the object.
		if ( nWordEnd > nBlobBytes )
		{
			bAgrees = false;

			if ( pOutFirstBadWord && *pOutFirstBadWord < 0 )
				*pOutFirstBadWord = i;

			continue;
		}

		if ( memcmp( pObject + pProp->GetOffset(),
		               pBlob + i * SOURCESHARP_PAYLOAD_WORD_BYTES,
		               SOURCESHARP_PAYLOAD_WORD_BYTES ) == 0 )
		{
			continue;
		}

		bAgrees = false;

		if ( pOutFirstBadWord && *pOutFirstBadWord < 0 )
			*pOutFirstBadWord = i;
	}

	return bAgrees;
}

// The same check over the hierarchy, printed. The SDK's entity list skips standalone
// entities, so this file walks its own.
void SourceSharp_StandalonePayloadAgreeReport( int nWantedIndex, int *pOutChecked, int *pOutAgreed )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	int nShared = 0;
	StandaloneNetFields( &nShared );

	for ( CStandaloneEntity *pEnt : standalones )
	{
		if ( nWantedIndex >= 0 && pEnt->EntIndex() != nWantedIndex )
			continue;

		int nWords = 0;
		int nFirstBad = -1;
		const bool bAgrees = SourceSharp_StandalonePayloadAgrees( pEnt, &nWords, &nFirstBad );

		if ( nWords <= 0 )
		{
			if ( nWantedIndex >= 0 )
			{
				Msg( "[SourceSharp]   standalone %d '%s': no payload props on its table\n",
					pEnt->EntIndex(), pEnt->GetClassname() );
			}
			continue;
		}

		if ( pOutChecked )
			(*pOutChecked)++;

		if ( bAgrees )
		{
			if ( pOutAgreed )
				(*pOutAgreed)++;

			Msg( "[SourceSharp]   standalone %d '%s': %d payload word(s) land where the blob is\n",
				pEnt->EntIndex(), pEnt->GetClassname(), nWords );
		}
		else
		{
			Warning( "[SourceSharp]   standalone %d '%s': payload word %d DISAGREES with the "
			         "blob (%d word(s) checked)\n",
				pEnt->EntIndex(), pEnt->GetClassname(), nFirstBad, nWords );
		}

		if ( nWantedIndex < 0 )
			continue;

		const int iTier = pEnt->Tier();
		SendTable *pTable = ( iTier >= 0 && iTier < SourceSharp_TierCount() )
			? g_pTierTables[ iTier ] : NULL;

		for ( int i = 0; pTable && i < nWords; i++ )
		{
			const SendProp *pProp = pTable->GetProp( nShared + i );
			const unsigned char *pAtProp = (const unsigned char *)pEnt + pProp->GetOffset();
			const unsigned char *pAtBlob = pEnt->DataBlob() + i * SOURCESHARP_PAYLOAD_WORD_BYTES;

			Msg( "[SourceSharp]     %-6s offset=%-5d prop=%02x%02x%02x%02x  "
			     "blob=%02x%02x%02x%02x  %s  proxy=%s\n",
				pProp->GetName(), pProp->GetOffset(),
				pAtProp[0], pAtProp[1], pAtProp[2], pAtProp[3],
				pAtBlob[0], pAtBlob[1], pAtBlob[2], pAtBlob[3],
				memcmp( pAtProp, pAtBlob, SOURCESHARP_PAYLOAD_WORD_BYTES ) == 0 ? "ok" : "DIFFERS",
				SourceSharpPayload_SendProxyName( pProp ) );
		}
	}
}

// Writes one dword into a live entity's blob and marks it changed: the negative control
// for the payload checks. Called by cs_sv_poke_payload.
bool SourceSharp_StandalonePokePayload( int nIndex, int iWord, uint32 nValue )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	for ( CStandaloneEntity *pEnt : standalones )
	{
		if ( pEnt->EntIndex() != nIndex )
			continue;

		const int nBlobBytes = pEnt->BlobBytes();

		if ( iWord < 0 || ( iWord + 1 ) * SOURCESHARP_PAYLOAD_WORD_BYTES > nBlobBytes )
		{
			Warning( "[SourceSharp] word %d is outside a %d-byte blob (%d word(s))\n",
				iWord, nBlobBytes, nBlobBytes / SOURCESHARP_PAYLOAD_WORD_BYTES );
			return true;
		}

		unsigned char *pAt = pEnt->DataBlob() + iWord * SOURCESHARP_PAYLOAD_WORD_BYTES;
		memcpy( pAt, &nValue, SOURCESHARP_PAYLOAD_WORD_BYTES );

		// The engine skips the encode for an edict nothing marked.
		pEnt->NetworkStateChanged();

		Msg( "[SourceSharp] standalone %d '%s': blob word %d (offset %d) = 0x%08x\n",
			nIndex, pEnt->GetClassname(), iWord,
			iWord * SOURCESHARP_PAYLOAD_WORD_BYTES, nValue );

		return true;
	}

	return false;
}

// Fills a live entity's payload with the probe pattern, a function of the seed and word
// index only, so the client can recompute it (SourceSharpPayload_ProbeWord).
bool SourceSharp_StandaloneProbePayload( int nIndex, uint32 nSeed, int *pOutWords )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	for ( CStandaloneEntity *pEnt : standalones )
	{
		if ( pEnt->EntIndex() != nIndex )
			continue;

		const int nWords = SourceSharp_TierWords( pEnt->Tier() );

		unsigned char *pBlob = pEnt->DataBlob();

		for ( int i = 0; i < nWords; i++ )
		{
			const uint32 nWord = (uint32)SourceSharpPayload_ProbeWord( nSeed, i );
			memcpy( pBlob + i * SOURCESHARP_PAYLOAD_WORD_BYTES, &nWord,
			          SOURCESHARP_PAYLOAD_WORD_BYTES );
		}

		// The engine skips the encode for an edict nothing marked.
		pEnt->NetworkStateChanged();

		if ( pOutWords )
			*pOutWords = nWords;

		Msg( "[SourceSharp] standalone %d '%s': %d payload word(s) filled with probe seed "
		     "0x%08x\n", nIndex, pEnt->GetClassname(), nWords, nSeed );

		return true;
	}

	return false;
}

// Breaks an entity's schema hash on purpose so the client's refusal path can be
// exercised; on a listen server the hashes otherwise always agree.
bool SourceSharp_StandaloneBreakSchema( int nIndex, uint32 nHash )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	for ( CStandaloneEntity *pEnt : standalones )
	{
		if ( pEnt->EntIndex() != nIndex )
			continue;

		// Stamp first, or the first stamp would overwrite the broken value.
		pEnt->GetServerClass();

		const uint32 nWas = pEnt->m_nSchemaHash;
		pEnt->m_nSchemaHash = nHash;
		pEnt->NetworkStateChanged();

		Msg( "[SourceSharp] standalone %d '%s': schema hash 0x%08x -> 0x%08x; the client must "
		     "now REFUSE its payload and say so\n",
			nIndex, pEnt->GetClassname(), nWas, nHash );

		return true;
	}

	return false;
}

// After a managed reload: re-stamps every stamped entity whose class was reshaped, zeroes
// its blob (its bytes follow the old layout) and marks the edict. An entity whose type
// grew past the tier it was allocated at is stamped zero, which receivers refuse.
void SourceSharp_StandaloneResyncSchema( int *pOutChecked, int *pOutRestamped )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	int nChecked   = 0;
	int nRestamped = 0;

	for ( CStandaloneEntity *pEnt : standalones )
	{
		// Never stamped; its first GetServerClass reads the current registry.
		if ( !pEnt->HasServerClass() )
			continue;

		nChecked++;

		unsigned int nWanted = 0;

		if ( !SourceSharp_SchemaNeedsRestamp( SourceSharp_NetTypes(), pEnt->GetClassname(),
		                                      pEnt->m_nSchemaHash, &nWanted ) )
		{
			continue;
		}

		const uint32 nWas       = pEnt->m_nSchemaHash;
		const int    nBlobBytes = pEnt->BlobBytes();

		if ( nBlobBytes > 0 && pEnt->DataBlob() )
			memset( pEnt->DataBlob(), 0, nBlobBytes );

		const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ByName( pEnt->GetClassname() );

		if ( pType && pType->iTier > pEnt->Tier() )
		{
			pEnt->m_nSchemaHash   = 0;
			pEnt->m_nPayloadWords = 0;
			pEnt->NetworkStateChanged();
			nRestamped++;

			Warning( "[SourceSharp] reload: standalone %d '%s' grew past the tier it was "
			         "ALLOCATED at (%d byte(s), tier %d -> tier %d): its blob cannot hold "
			         "the new layout, so it carries no schema hash and every receiving side "
			         "will refuse its payload. Create it again to get the new width.\n",
				pEnt->EntIndex(), pEnt->GetClassname(), nBlobBytes,
				pEnt->Tier(), pType->iTier );
			continue;
		}

		pEnt->RestampServerClass();
		pEnt->NetworkStateChanged();
		nRestamped++;

		Msg( "[SourceSharp] reload: standalone %d '%s' reshaped -- schema hash 0x%08x -> "
		     "0x%08x, %d blob byte(s) cleared\n",
			pEnt->EntIndex(), pEnt->GetClassname(), nWas, pEnt->m_nSchemaHash, nBlobBytes );
	}

	// Added, not assigned: one caller passes the same counters to both shells' walks.
	if ( pOutChecked )
		*pOutChecked += nChecked;

	if ( pOutRestamped )
		*pOutRestamped += nRestamped;
}

// The tier this entity was allocated at, so the table matches its blob exactly.
// Cached because the engine asks per entity per transmit.
ServerClass *CStandaloneEntity::GetServerClass()
{
	if ( m_pServerClass )
		return m_pServerClass;

	// Clamped: an out-of-range tier would index the static array out of bounds.
	const int iTier = ( m_iTier >= 0 && m_iTier < SourceSharp_TierCount() ) ? m_iTier : 0;

	m_pServerClass = g_pTierClasses[ iTier ];

	// Stamped here because the classname (factory) and type table (managed startup) both
	// exist by the first time the engine asks.
	const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ByName( GetClassname() );

	if ( pType )
	{
		m_nSchemaHash = pType->nSchemaHash;

		m_nPayloadWords = (uint32)SourceSharp_TierWords( iTier );
	}
	else
	{
		// A class nobody published carries a zero hash, which the client refuses.
		m_nSchemaHash   = 0;
		m_nPayloadWords = 0;

		static bool s_bWarned = false;

		if ( !s_bWarned )
		{
			s_bWarned = true;
			Warning( "[SourceSharp] standalone '%s' has no published type; its entities will "
			         "carry type id 0x%08x with no schema hash, and the client will refuse "
			         "their payload\n", GetClassname(), m_nTypeId );
		}
	}

	return m_pServerClass;
}
