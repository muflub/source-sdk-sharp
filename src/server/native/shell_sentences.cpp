//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the sentence group emits, and nothing else ==========//
// SS_REASON b  an interface resolved by name at load
//
// WHAT THIS FILE IS. The live remainder of game/server/sound.cpp, moved out of
// the stock tree by plan_entity.md step 4a, with the dead remainder deleted
// rather than carried across.
//
// sound.cpp was two subsystems in one file. The entity half — CAmbientGeneric,
// ~1200 lines — was deleted by an earlier batch and is C#'s
// (Entities/Env/AmbientGeneric.cs); the free functions stayed, and they are the
// only reason the file was still on the link line. There is no entity here, no
// LINK_ENTITY_TO_CLASS, no datadesc and no send table, so 4a moves the three
// the linker actually wants and drops the vpc line.
//
// WHY THREE AND NOT THIRTEEN. sound.cpp defined ten functions past these three
// and NOTHING in the server link referenced any of them. Measured, per symbol,
// with tools/4a-owes over the debug server objects at 249ea124:
//
//     == sound: 4 symbols owed
//       SoundEmitterSystem (1)     SENTENCEG_Lookup
//       baseentity (1)             SENTENCEG_Lookup
//       shell_entity_orphans (1)   SENTENCEG_PlaySequentialSz
//       world_adapter (1)          SENTENCEG_Init
//
// The ten that are gone: SENTENCEG_PlayRndI, SENTENCEG_PickRndSz,
// SENTENCEG_PlaySentenceIndex, SENTENCEG_PlayRndSz, SENTENCEG_Stop (already
// `#if 0` in stock), SENTENCEG_GetIndex, UTIL_EmitSoundSuit,
// UTIL_EmitGroupIDSuit, UTIL_EmitGroupnameSuit and TEXTURETYPE_Find. Their
// callers are the NPC talker code and player.cpp, all excluded from
// server_csharp.vpc, and the one call that is IN a compiled file --
// TEXTURETYPE_Find at gamemovement.cpp:939 -- is inside stock's own `/* FIXME
// FIXME: Does this need to be hooked up? */` block and compiles to nothing.
// Confirmed rather than assumed: `nm -C -u gamemovement.o` names neither
// TEXTURETYPE_Find nor any SENTENCEG_.
//
// DELETED, NOT STUBBED. plan_entity.md §7: "The stub files must be empty at the
// phase exit, and a behaviour that is refused is a ContractBaseline entry with
// a reason, never a silent no-op." The DECLARATIONS stay in game/server/sound.h
// and game/server/util.h, deliberately, so that anyone who puts a caller back
// gets a link error naming the symbol. That is the same answer this tree
// already gives for UTIL_RestartAmbientSounds, and for the same reason: an
// empty stub would be the silent one.
//
// WHAT IS STILL NATIVE HERE, AND WHAT IS NOT. Which sentence group to play and
// when is a decision, and it is already the caller's. What is left is
// mechanism: pick from a group through the engine's own sentence tables, and
// emit through a PAS recipient filter. plan_entity.md §6 keeps "trace filters,
// partition enumerators, recipient filters" native because they are callback
// vtables the engine drives.
//
// THE TWO CALLERS ARE BOTH SOMEBODY ELSE'S FILE, and both are recorded in
// .claude/entity-port/requests/4a.md rather than changed here:
//
//   * SENTENCEG_Init is called from CWorld::Precache in world_adapter.cpp
//     (step 4g). It is one engine call, PrecacheSentenceFile, and the managed
//     side already has the vtable slot for it (IVEngineServer slot 5).
//   * SENTENCEG_PlaySequentialSz's only caller is PlayLockSounds in
//     shell_entity_orphans.cpp (step 4f), whose whole body is inside
//     `#ifndef SS_CBASEENTITY_ALIAS` and whose own comment records that
//     NOTHING CALLS IT: its callers were three sites in CBasePropDoor, and
//     armed CBasePropDoor does not exist. It goes when that function goes.
//
//=============================================================================//

// The include list is what the three surviving functions need, measured by
// removing each and reading the error, and it is four shorter than sound.cpp's:
// physics.h went with TEXTURETYPE_Find's `physprops`, ai_speech.h with the suit
// speaker's `g_AIFriendliesTalkSemaphore`, game.h with its `suitvolume`, and
// vstdlib/random.h with the suit speaker's pitch jitter. recipientfilter.h
// stays, because the one surviving emit still builds a CPASAttenuationFilter.
#include "ss_util.h"            // engine, ss::ENTINDEX
#include "ss_recipient_filter.h"
#include "engine/IEngineSound.h"

// The lifted sound statics. They used to be reached as CBaseEntity::Foo()
// through one-line forwarders in game/shared/baseentity_sound_forwarders.cpp;
// that quarantine is retired and the call below names GameSound_* directly.
#include "gamesound.h"




// ==================== SENTENCE GROUPS, UTILITY FUNCTIONS  ======================================

int fSentencesInit = false;

// ===================== SENTENCE GROUPS, MAIN ROUTINES ========================

// play sentences in sequential order from sentence group.  Reset after last sentence.

// open sentences.txt, scan for groups, build rgsentenceg
// Should be called from world spawn, only works on the
// first call and is ignored subsequently.
void SENTENCEG_Init()
{
	if (fSentencesInit)
		return;

	engine->PrecacheSentenceFile( "scripts/sentences.txt" );
	fSentencesInit = true;
}

// SENTENCEG_Lookup WENT TO C# (plan_server_native_v2 T4a): its one caller,
// UTIL_EmitAmbientSound's "!SENTENCE" arm, is the managed SoundEmitter's
// UtilEmitAmbient, which asks IVEngineServer::SentenceIndexFromName through its
// forwarder for the name past the '!'.
