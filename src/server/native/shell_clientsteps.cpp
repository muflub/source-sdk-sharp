//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================
// SS_REASON a  a vtable the engine calls through
//
// CLIENTEARPOSITION'S HIERARCHY QUESTION, AS TWO STEPS THAT DECIDE NOTHING.
//
// plan_server.md Phase 7, docs/plan-audit.md item C5. The clients step file
// the audit's C5 row names -- "a clients step file mirroring
// shell_level.cpp" -- opened with the sequence whose crossing is a
// DECISION and nothing else.
//
// WHAT CROSSED
// ============
//
// shell_gameclients.cpp's ClientEarPosition was an ordered disjunction over
// the two entity hierarchies, written as an #ifdef:
//
//     #ifdef SOURCESHARP
//     if ( CStandalonePlayer *p = CStandalonePlayer::FromEdict( pEdict ) )
//         { *pEarOrigin = p->EyePosition(); return; }
//     #endif
//     CBasePlayer *pPlayer = UTIL_SDKPlayerFromEdict( pEdict );
//     if ( pPlayer ) *pEarOrigin = pPlayer->EarPosition();
//     else           { Assert(0); *pEarOrigin = vec3_origin; }
//
// WHICH HIERARCHY ANSWERS FOR THIS CLIENT, AND IN WHICH ORDER, IS A DECISION.
// plan_gamedll.md Part 4's rule is that native exposes one irreducible step per
// crossing and a step is never a decision, so the two reads below each ask ONE
// hierarchy and answer yes-or-no; ServerClients.EarPositionForClient asks them
// in this tree's order, and that order is now a line of C# a reader can see.
//
// STANDALONE FIRST, WHICH IS THE OPPOSITE OF C4'S ORDER, AND DELIBERATELY SO.
// CheckTransmit's disjunction (shell_transmitsteps.cpp) is SDK-first because
// stock only reaches for the standalone player when GetContainingEntity came
// back NULL. This one is standalone-first because that is what the body said
// and because the two arms are not symmetric here: the SDK arm's fallback is
// an Assert(0), and Phase 10's comment records that the assert FIRED -- "hit
// the first time anything emitted a sound ON such a player, which the managed
// weapon machine now does every trigger pull". Reversing the order would put
// that assert back. The order is the observable part of the old code and this
// crossing exists to preserve it, not to improve on it.
//
// THE ASSERT STAYS NATIVE, and so does the vec3_origin fallback. They are what
// the caller does with a "no" and not part of the question; managed code that
// owned the Assert would be deciding when the ENGINE's diagnostic fires.
//
// EarPosition() VS EyePosition(): NOT THIS CROSSING'S BUSINESS. The SDK arm
// calls CBasePlayer::EarPosition and the standalone arm calls
// CStandalonePlayer::EyePosition, which is what shell_gameclients.cpp already
// did and what Phase 10 argued. Each step reproduces its own hierarchy's call
// exactly. If the two should agree, that is a question about CStandalonePlayer,
// answered where CStandalonePlayer is -- moving the disjunction does not make
// it this file's.
//
// ONE CROSSING PER CLIENT PER FRAME AT MOST. The engine calls
// ClientEarPosition to place the sound listener; it is not in any loop over
// entities. That is what makes it a crossing worth having, by the same measure
// that keeps CheckTransmit's 473-iteration walk native.
//
// NO FILE-SCOPE STATE.
//
//===========================================================================//

#include "eiface.h"
#include "edict.h"

extern IVEngineServer *engine;   // shell_engineboundary.cpp

#include "shell_standalone_player.h"


//-----------------------------------------------------------------------------
// THE STANDALONE HIERARCHY'S ANSWER. Phase 10's arm, unchanged except that the
// decision to reach for it FIRST has left shell_gameclients.cpp.
//
// 1 when this edict resolves to a standalone player, with pOutXYZ written; 0
// when it does not, with pOutXYZ UNTOUCHED.
//
// UNTOUCHED RATHER THAN ZEROED, for shell_transmitsteps.cpp's reason: the
// world origin is a real position, and ClientEarPosition's own miss path
// writes vec3_origin *after* an Assert. A step that zeroed on the way out
// would make "the listener is at the origin" and "no such client" the same
// three floats, and the caller has to tell them apart to know whether to
// assert.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientsStandalonePlayerEarPosition( int32_t iEdict, float *pOutXYZ )
{
	if ( pOutXYZ == NULL )
		return 0;

	edict_t *pEdict = engine->PEntityOfEntIndex( iEdict );
	if ( pEdict == NULL )
		return 0;

	CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEdict );
	if ( pStandalone == NULL )
		return 0;

	const Vector vecEars = pStandalone->EyePosition();
	pOutXYZ[ 0 ] = vecEars.x;
	pOutXYZ[ 1 ] = vecEars.y;
	pOutXYZ[ 2 ] = vecEars.z;
	return 1;
}

//-----------------------------------------------------------------------------
// THE SDK HIERARCHY'S ANSWER: NONE, ON EVERY PATH.
//
// server.so builds no CBasePlayer, so there is no SDK player to ask. This
// looked the edict up and asked UTIL_SDKPlayerFromEdict, which answered NULL
// for every edict, and the armed arm answered 0 even past that (EarPosition is
// defined in the struck baseplayer_shared.cpp). All four paths returned 0 and
// left pOutXYZ untouched. That is all the step does now. The lookup went with
// UTIL_SDKPlayerFromEdict (P5 lane E); the crossing stays, because
// ServerClients.EarPositionForClient asks it, and the order it asks in is C#'s.
// A step that cannot answer says so; it does not invent a position.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientsSdkPlayerEarPosition( int32_t iEdict, float *pOutXYZ )
{
	(void)iEdict;
	(void)pOutXYZ;
	return 0;
}
