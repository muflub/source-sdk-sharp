//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON f  a bit-level wire codec
// Purpose: implements various common send proxies

#include "dt_send.h"
#include "basetypes.h"
#include "string_t.h"
#include "slot_table.h"   // SourceSharp_EntityFromHandle: what CBaseHandle::Get answers on the server
// team.h: removed with team.cpp, which is out of the build entirely
// (server_csharp.vpc). SendProxy_OnlyToTeam, the one thing here that ever
// named a CTeam, is gone with it -- see where it was, below.


void SendProxy_EHandleToInt( const SendProp *pProp, const void *pStruct, const void *pVarData, DVariant *pOut, int iElement, int objectID)
{
	CBaseHandle *pHandle = (CBaseHandle*)pVarData;

	if ( pHandle && SourceSharp_EntityFromHandle( *pHandle ) )
	{
		int iSerialNum = pHandle->GetSerialNumber() & ( (1 << NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS) - 1 );
		pOut->m_Int = pHandle->GetEntryIndex() | (iSerialNum << MAX_EDICT_BITS);
	}
	else
	{
		pOut->m_Int = INVALID_NETWORKED_EHANDLE_VALUE;
	}
}

SendProp SendPropBool(
	const char *pVarName,
	int offset,
	int sizeofVar )
{
	Assert( sizeofVar == sizeof( bool ) );
	return SendPropInt( pVarName, offset, sizeofVar, 1, SPROP_UNSIGNED );
}


SendProp SendPropEHandle(
	const char *pVarName,
	int offset,
	int sizeofVar,
	int flags,
	SendVarProxyFn proxyFn )
{
	return SendPropInt( pVarName, offset, sizeofVar, NUM_NETWORKED_EHANDLE_BITS, SPROP_UNSIGNED|flags, proxyFn );
}

//-----------------------------------------------------------------------------
// SendProxy_OnlyToTeam -- DELETED, with its registration and the team.h include
// that was its only reason for being in this file.
//
// It was already dead twice over: guarded out by #ifndef SOURCESHARP, and
// referenced by no send table in either build arm. What it did was read
// `pEntity->GetTeam()` and cut the recipient list down to that team's players.
//
// THE HONEST NOTE IT CARRIED SURVIVES, because it corrected something that is
// still written down elsewhere. igameentity.h's ledger claims "zero SDK-side
// asks remain"; this was one, and the reason it had no answer is that a send
// proxy's `pStruct` is the send table's own object -- under the Phase 13 alias
// that is IGameEntity -- so reading a team off it would read an offset
// belonging to a different class. There is no boundary route either: teams are
// game policy that C# will own, and the standalone player answers 0
// ("unassigned") through SourceSharp_GetPlayerTeam.
//
// So the claim was wrong by one, and is now right by deletion rather than by
// exception. When teams come back on the managed side they come back as a
// managed recipient filter, not as this.
//-----------------------------------------------------------------------------

#define TIME_BITS 24

// This table encodes edict data.
#if 0
static void SendProxy_Time( const SendProp *pProp, const void *pStruct, const void *pVarData, DVariant *pOut, int iElement, int objectID )
{
	float clock_base = floor( gpGlobals->curtime );
	float t = *( float * )pVarData;
	float dt = t - clock_base;
	int addt = Floor2Int( 1000.0f * dt + 0.5f );
	// TIME_BITS bits gives us TIME_BITS-1 bits plus sign bit
	int maxoffset = 1 << ( TIME_BITS - 1);

	addt = clamp( addt, -maxoffset, maxoffset );

	pOut->m_Int = addt;
}
#endif

// SOURCESHARP: SendPropPredictableId and its SendProxy_PredictableIdToInt were
// here, inside `#if !defined( NO_ENTITY_PREDICTION )`, and plan_shim.md Phase
// 1d deleted them.
//
// They were the ONLY importer of game/shared/predictableid.cpp on this link
// line -- one symbol, CPredictableId::GetRaw -- and nothing imported THEM:
// tools/shim-census reports 0 objects importing SendPropPredictableId across
// all 143 server objects. A prop builder nobody calls builds a property no
// SendTable carries, so the entity-prediction id was never on the wire in this
// build. If a table ever needs one it needs a MANAGED id rather than this one:
// there is no managed twin of CPredictableId, and that is the finding rather
// than an oversight.
