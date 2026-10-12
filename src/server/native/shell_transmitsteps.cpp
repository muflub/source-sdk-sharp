//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================
// SS_REASON f  a bit-level wire codec
//
// CHECKTRANSMIT'S RECIPIENT QUESTION, AS TWO STEPS THAT DECIDE NOTHING.
//
// plan_server.md Phase 8b, docs/plan-audit.md item C4:
//
//     CheckTransmit is one step plus ServerSkyboxAreaForClient, and it is
//     deliberately the last sequence in the plan to cross. It is the only
//     method here whose regression is SILENT AND PARTIAL -- entities that stop
//     being sent to one client in one area -- which is the hardest kind of
//     fault to attribute after the fact and the reason Gate 5 records a
//     transmit set, not just a step order.
//
// WHAT CROSSED, AND WHAT DID NOT
// ==============================
//
// stock (gameinterface.cpp:2554-2586, carried into shell_gameents.cpp) opened
// CheckTransmit with a disjunction:
//
//     CBaseEntity *pRecipientEntity = GetContainingEntity( pClientEnt ? ... : pBaseEdict );
//     bool bStandaloneRecipient = false;
//     int  nStandaloneSkyBoxArea = 0;
//     if ( !pRecipientEntity )
//         if ( CStandalonePlayer *p = CStandalonePlayer::FromEdict( pClientEnt ) )
//             { bStandaloneRecipient = true; nStandaloneSkyBoxArea = p->MvLocal().m_skybox3d.area; }
//     if ( !pRecipientEntity && !bStandaloneRecipient )
//         return;
//     ...
//     const int skyBoxArea = pRecipientPlayer ? pRecipientPlayer->m_Local.m_skybox3d.area
//                                             : nStandaloneSkyBoxArea;
//
// WHICH HIERARCHY ANSWERS FOR THIS CLIENT, AND IN WHICH ORDER, IS A DECISION.
// plan_gamedll.md Part 4's rule is that native exposes one irreducible step per
// crossing and a step is never a decision, so the two reads below each ask ONE
// hierarchy and answer yes-or-no; ServerSkyboxAreaForClient asks them in
// stock's order, and that order is now a line of C# a reader can see.
//
// THE LOOP DID NOT CROSS, and the audit's C4 row says so in its own words:
// "keep CheckTransmit's loop native per the plan's own rule -- the work is one
// step plus ServerSkyboxAreaForClient, not a transliteration". The walk runs
// once per edict per client per frame; on dm_lockdown that is 473 iterations a
// frame per client, and it DECIDES nothing that is not already an
// FL_EDICT_* flag. The recipient question runs once per client per frame.
//
// NO IsPlayer() GUARD, AND THAT IS NOT AN OVERSIGHT
// =================================================
//
// Stock reads m_Local.m_skybox3d.area off a raw static_cast<CBasePlayer*> of
// whatever GetContainingEntity returned. The only thing standing between that
// cast and a non-player is an Assert, which -DNDEBUG deletes -- and on the
// edict-0 fallback path (pInfo->m_pClientEnt == NULL) the cast lands on
// worldspawn. Adding a guard here would change behaviour, in the one method of
// this plan whose regression is silent and partial, on a path nobody has
// measured. So the cast is reproduced exactly and this paragraph is the record.
//
// NO FILE-SCOPE STATE.
//
//===========================================================================//

#include "edict.h"
#include "eiface.h"

extern IVEngineServer *engine;
extern CGlobalVars *gpGlobals;
#include "eiface.h"
#include "edict.h"

#include "shell_standalone_player.h"


//-----------------------------------------------------------------------------
// THE SDK HIERARCHY'S ANSWER.
//
// 1 when this edict resolves to an SDK entity, with *pOutArea written; 0 when it
// does not, with *pOutArea UNTOUCHED.
//
// UNTOUCHED RATHER THAN ZEROED, because area 0 is a real visleaf area. A step
// that wrote 0 on the way out would make "the world's first area" and "no such
// recipient" the same two bytes, and CheckTransmit RETURNS EARLY for one of
// them and walks 473 edicts for the other.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntitiesSdkPlayerSkyboxArea( int32_t iEdict, int32_t *pOutArea )
{
	if ( pOutArea == NULL )
		return 0;

	edict_t *pEdict = engine->PEntityOfEntIndex( iEdict );
	if ( pEdict == NULL )
		return 0;

	// NO SDK PLAYER BEHIND ANY EDICT, so this step has always answered 0.
	// GetContainingEntity ANSWERED A CONSTANT NULL in this build: it is
	// SourceSharp_SDKEntityFromUnknown< IServerEntity >, which bottoms out in
	// IServerUnknown::GetBaseEntity(), and every implementation of that in
	// server.so returns NULL (the close read all three out of the binary). The
	// function is gone; the NULL it answered is written down instead.
	// The stock cast that followed -- `static_cast<CBasePlayer *>( pEntity )`
	// on to `m_Local.m_skybox3d.area` -- was unreachable with it. The standalone
	// hierarchy's answer is the function immediately below, and the decision to
	// prefer it is in C#.
	(void)pEdict;
	return 0;
}

//-----------------------------------------------------------------------------
// THE STANDALONE HIERARCHY'S ANSWER. Phase 9a's arm, unchanged except that the
// decision to reach for it has left this file.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntitiesStandalonePlayerSkyboxArea( int32_t iEdict, int32_t *pOutArea )
{
	if ( pOutArea == NULL )
		return 0;

	edict_t *pEdict = engine->PEntityOfEntIndex( iEdict );
	if ( pEdict == NULL )
		return 0;

	CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEdict );
	if ( pStandalone == NULL )
		return 0;

	*pOutArea = pStandalone->MvLocal().m_skybox3d.area;
	return 1;
}
