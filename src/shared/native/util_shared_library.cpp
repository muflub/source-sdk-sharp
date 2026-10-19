//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the shared utility library ==========================//
// SS_REASON f  a bit-level wire codec
//
// plan_entity.md Phase 7, step 7f. `game/shared/util_shared.cpp` compiles into
// BOTH server.so and client.so, and both link lines still asked it for a
// handful of small, entity-free utilities. Those move here, whole, with every
// declaration left in `game/shared/util_shared.h` — so not one caller on
// either side changed.
//
// WHAT MOVED, AND WHY EACH FAMILY MOVED WHOLE RATHER THAN ONE SYMBOL AT A TIME.
// A family here is "a function plus the state only it and its siblings can
// see". Splitting one across two translation units does not fail to build; it
// fails to WORK, because file-static storage does not follow the function out.
//
//   1. THE STRING PARSERS. `UTIL_StringToVector` is the cross-edge, but its
//      whole body is `UTIL_StringToFloatArray( pVector, 3, pString )`, and
//      `UTIL_StringToColor32` is `UTIL_StringToIntArray` the same way. Moving
//      only the two that are asked for would have converted this file's edge
//      into an edge on the helper next door and removed nothing — the shape
//      4e measured on the blood emitters and declined for the same reason.
//
//   2. THE VIOLENCE FAMILY. Four ConVars, `UTIL_IsLowViolence` and
//      `UTIL_ShouldShowBlood`. The ConVars are FILE STATICS: leaving them
//      behind means the predicates here read a different set of four, whose
//      value an operator can never change, and nothing about the build looks
//      different. `r_visualizetraces` at step 4e is the recorded instance of
//      exactly this.
//
//   3. and 4. THE STEAM UNIVERSE AND THE FILTERED PLAYER NAMES, which are
//      managed now (section 3 below says where).
//
//   5. Two constants of the build: `UTIL_GetActiveHolidayString` and
//      `UTIL_GetActiveOperationString`.
//
//   6. `GetCleanMapName`, named by 4e's handover as a free move it did not
//      have a shared-side home for. This is that home.
//
// FOUR MOD GUARDS WERE DELETED ON THE WAY IN, NOT CARRIED. `TF_DLL`,
// `TF_CLIENT_DLL` and `USES_ECON_ITEMS` are defined by NEITHER link line —
//
//     grep -oP '(?<=-D)[A-Za-z0-9_]+' src/_vpc_/ninja/sdk_csharp_debug.ninja | sort -u
//
// lists none of the three — and `tools/dead-conditionals` reports a shim file
// carrying one as a problem rather than allowing it to be ledgered: its
// allowlist takes PORTABILITY guards (WIN32) and SIDE markers (CLIENT_DLL) and
// says of a mod guard that "this is not that game and never will be". So the
// arms this build takes were kept and the others deleted, on that tool's own
// instruction and following 4e's two deletions at the same gate. This is the
// one respect in which the moved text is not stock's text, and it is the
// consequence of the code becoming the shim's rather than a change of mind
// about what it should do: every deleted arm was unreachable before the move
// and is unreachable after it. `tools/dead-conditionals` reads 0 problem(s).
//
//=============================================================================//

#ifdef CLIENT_DLL
#include "cbase.h"
#endif

#include "convar.h"
#include "managed_convar_ref.h"
#ifdef CLIENT_DLL
#include "gamerules.h"
#else
#include "igamerules.h"	// g_pGameRules; stock's CGameRules is not reached on the server
#endif
#ifdef CLIENT_DLL
#include "util_shared.h"
#endif

// p4-util: what the blood/tracer/decal block at the tail needs -- the random
// stream, IEffects, the particle-system index and the per-side temp-entity
// dispatch. Each is the include game/shared/util_shared.cpp used for it.
#include <vstdlib/random.h>
#ifdef CLIENT_DLL
#include "sharedInterface.h"
#endif
#ifdef CLIENT_DLL
#include "IEffects.h"
#endif

#ifdef CLIENT_DLL
	#include "c_baseplayer.h"
	#include <vgui/ILocalize.h>
	extern vgui::ILocalize *g_pVGuiLocalize;
	#include "c_te_effect_dispatch.h"
	// The slot table's index -> handle ask (§3.3). This file is in
	// client_csharp.vpc and server_csharp.vpc only, so there is no
	// !SOURCESHARP arm to keep.
	#include "shell_cliententitylist.h"
#else
	#include "ss_effect_dispatch.h"
	using ss::CEffectData;	// the server's own record (ss_effect_dispatch.h); the client keeps its CEffectData
	#include "ss_effects.h"	// ss::Effects(), the server's effects (no engine reads IEffects)
#endif

// For the shared-random family below: the CRC the seed hash is built on, the
// prediction seed it hashes, and vstdlib's Random* (NOT rand(), which is what
// stock's own comment in SharedRandomVector warns about).
#ifndef CLIENT_DLL
// What the server half used from cbase.h / util_shared.h / sharedInterface.h:
// the UTIL_* declarations (ss_util.h, R5-only), the shared constants
// (ss_shareddefs.h, oracle-checked) and the engine's random stream, named
// random_valve under POSIX as sharedInterface.h names it.
#include "ss_util.h"
#include "ss_shareddefs.h"
extern IUniformRandomStream *random_valve;
#define random random_valve
// shareddefs.h's names for the constants this file uses, from ss_shareddefs.h.
using ss::BLOOD_COLOR_RED;
using ss::BLOOD_COLOR_MECH;
using ss::DONT_BLEED;
#define LANGUAGE_GERMAN            ss::kLanguageGerman
#define TRACER_FLAG_WHIZ           ss::kTracerFlagWhiz
#define TRACER_FLAG_USEATTACHMENT  ss::kTracerFlagUseAttachment
#define TRACER_DONT_USE_ATTACHMENT ss::kTracerDontUseAttachment
// Functions server.so defines (util.h / util_shared.h / particle_parse.h
// declared them): UTIL_Tracer and UTIL_BloodImpact below in this file,
// UTIL_Smoke in server_util_library.cpp, GetParticleSystemIndex in
// shell_stringtables.cpp. Stock's signatures and defaults.
// (Parameters unnamed: the definitions below are the ones tests find by text.)
void UTIL_Tracer( const Vector &, const Vector &, int = 0, int = TRACER_DONT_USE_ATTACHMENT,
                  float = 0, bool = false, const char * = NULL, int = 0 );
void UTIL_BloodImpact( const Vector &, const Vector &, int, int );
void UTIL_Smoke( const Vector &, const float, const float );
int GetParticleSystemIndex( const char * );
#endif
#include "checksum_crc.h"
#include "entity_prediction_seed.h"
#include <vstdlib/random.h>
#include "ss_string.h"


//-----------------------------------------------------------------------------
// 0. `hl2_episodic`, RELOCATED FROM game/shared/util_shared.cpp
//
// plan_shim.md Phase 2c, lane 2-utilshared. This ConVar was the last thing
// holding `game/shared/util_shared.cpp` in either vpc for a reason other than
// blood: its only importer on either link line was `ragdoll_shared.o`, on both
// sides, and that is a stock row another lane owns. Relocating the DEFINITION
// here rather than moving the READER means ragdoll_shared.cpp does not change
// at all -- it links the same `extern ConVar hl2_episodic` out of a different
// object.
//
// Verbatim, including the guard and the comment, from util_shared.cpp:61-82:
//
//   A REPLICATED ConVar whose definition sat in a file the client flip removes
//   from both vpcs. Its one surviving reader is
//   `CRagdollLowViolenceManager::SetLowViolence` (ragdoll_shared.cpp:63), which
//   uses it to pick which set of Citadel map names gets low-violence ragdolls
//   turned back off; its other reader, baseentity.cpp, leaves with the
//   hierarchy.
//
//   IT IS ALSO POLICY, and belongs on the managed side eventually: the value
//   decides a content rule (which maps are exempt from low-violence ragdoll
//   fading) by hardcoded map name.
//
// THAT LAST PARAGRAPH IS STILL THE RULING AND THIS LANE DOES NOT CLOSE IT.
// `hl2_episodic` is policy and wants `ConVars.cs`, but its one reader is
// `ragdoll_shared.cpp`, which is lane 2-physics's file in this same phase --
// so moving the read is 2-physics's step to take and moving the declaration
// without it would leave a replicated ConVar registered twice. It is HERE, in
// both vpcs, registered by both DLLs exactly as before, and it is a relocation
// and not a decision.
//
// It is in this file rather than in ragdoll_shared.cpp's own neighbourhood for
// the reason its original comment gives: it is a game-wide fact and not a
// ragdoll one -- "which HL2 is this" -- and a second reader would have no
// reason to look for it under ragdolls.
//-----------------------------------------------------------------------------
// THE GUARD DID NOT COME WITH IT, AND THAT IS THIS FILE'S STANDING RULE rather
// than a decision taken here: see "FOUR MOD GUARDS WERE DELETED ON THE WAY IN,
// NOT CARRIED" in the banner above. `tools/dead-conditionals` reports a shim
// file carrying a mod guard as a problem it will not let you ledger -- its
// allowlist takes PORTABILITY guards (WIN32) and SIDE markers (CLIENT_DLL) and
// says of a mod guard that "this is not that game and never will be" -- and
// dead_conditionals_test.cpp fails the build on it. This is the fifth such
// deletion into this file.
//
// `HL2_EPISODIC` is defined by NEITHER link line:
//
//     grep -oP '(?<=-D)[A-Za-z0-9_]+' src/_vpc_/ninja/sdk_csharp_debug.ninja \
//       | sort -u | grep -c HL2_EPISODIC        ->  0
//
// so the `#else` was the arm both DLLs already compiled and the `#ifdef` arm
// was unreachable before this move and is unreachable after it. The VALUE the
// game registers is unchanged: "0".
//
// AND THE DECLARATION IS C#'S NOW, on both sides (SourceSharp.Server and
// SourceSharp.Client HL2EpisodicConVar), plan_server_novalve.md P2.3b. The
// ruling above had waited on ragdoll_shared.cpp's read, which has since gone:
// no object on either link line imports `hl2_episodic`.

//-----------------------------------------------------------------------------
// 1. THE STRING PARSERS
//-----------------------------------------------------------------------------

// UTIL_StringToFloatArray AND UTIL_StringToVector WERE HERE, and C9 K3 deleted
// both. Their one importer was shell_propdata_system.o, which left the server
// in P5 and the client in C9 K3 and is deleted. Measured at that commit with
// `nm -u` over every object in each target's compile_commands.json and every
// archive on build.ninja's link lines: 0 undefined references to either, on
// either side. They went as a pair for the reason below.
//
// UTIL_StringToIntArray AND UTIL_StringToColor32 WERE HERE, and plan_shim.md
// Phase 2c, lane 2-utilshared, deleted both. Lane H2 read them as dead and the
// reading holds on this tree, twice over:
//
//     tools/shim-census --mtime-policy off who-imports \
//         src/sourcesharp/game/shared/util_shared_library.cpp
//       -> neither symbol appears in any importer's list, either side
//     nm -C -u _vpc_/ninja/debug/objs/{server,client}_sourcesharp/*.o \
//       | grep -c 'UTIL_StringToIntArray\|UTIL_StringToColor32'
//       -> 0 and 0
//
// The second command is there because H2 raised the right caveat about its own
// method: a per-function census sees only out-of-line symbols, so something
// reached entirely inline from a header would read as unused when it is not.
// Both are out-of-line and declared in game/shared/util_shared.h, so a cross-TU
// call cannot avoid emitting an undefined reference.
//
// THEIR CALLERS ARE IN THE TREE AND IN NEITHER VPC: game/server/saverestore_gamedll.cpp:93
// and game/shared/baseentity_shared.cpp:384. The banner's argument for moving
// the string parsers as a family (UTIL_StringToColor32 is UTIL_StringToIntArray
// the same way UTIL_StringToVector is UTIL_StringToFloatArray) is what made them
// come across; with no compiled caller for either, the pair goes as a pair and
// UTIL_StringToVector's own pair stayed while shell_propdata_system.o imported
// it, and went as a pair when that file did (above).
//
// The DECLARATIONS stay in game/shared/util_shared.h. That header is stock's and
// this lane does not own it, and a non-vpc caller still reads it.

//-----------------------------------------------------------------------------
// 2. THE VIOLENCE FAMILY.
//
// THE FOUR DECLARATIONS ARE MANAGED NOW; the two predicates below are not, and
// the split is deliberate rather than partial.
//
// What moved is what a `ConVar` line actually IS -- the name, the default "1",
// the flags and the help text. Those four values are the tuning knob, and while
// they were file statics in this translation unit no managed code could read
// them, set them or list them. They are declared in
// SourceSharp.Server/GameVars.cs and SourceSharp.Client/ConVars.cs now, one per
// DLL, which is exactly the two registrations these four `static ConVar` lines
// produced when this shared file was compiled into both.
//
// What did NOT move is UTIL_IsLowViolence and UTIL_ShouldShowBlood, because
// they have callers in both game DLLs and in stock TUs this build still
// compiles (shell_te_backends_client.cpp:924 on the client,
// server_util_library.cpp:509 on the server), and a predicate asked per blood
// effect is not a thing to cross the boundary for. They read the same convars
// out of the engine's own registry instead -- see managed_convar_ref.h, and in
// particular its note on why a plain ConVarRef here would silently read zero
// forever.
//
// The four refs keep the ORDER the predicates already relied on: this file
// still owns the rule, and the rule is unchanged to the character.
//-----------------------------------------------------------------------------
static CManagedConVarRef	violence_hblood( "violence_hblood" );
static CManagedConVarRef	violence_hgibs( "violence_hgibs" );
static CManagedConVarRef	violence_ablood( "violence_ablood" );
static CManagedConVarRef	violence_agibs( "violence_agibs" );

bool UTIL_ShouldShowBlood( int color )
{
	if ( color != DONT_BLEED )
	{
		if ( color == BLOOD_COLOR_RED )
		{
			return violence_hblood.GetBool();
		}
		else
		{
			return violence_ablood.GetBool();
		}
	}
	return false;
}

//-----------------------------------------------------------------------------
// 3. AND 4. THE STEAM UNIVERSE AND THE FILTERED PLAYER NAMES: MANAGED
//
// plan_client_novalve C6, Steam to managed (lane c6-input): GetSteamUtils,
// GetUniverse and the four UTIL_GetFilteredPlayerName* left this file. The
// filter is SteamPlayerNameFilter, behind the ONE player-name source the UI
// reads (IPlayerNames); the universe is ClientSteamContext's. Their only
// importers were shell_playerresource.cpp's GetPlayerName and
// shell_clientdll.cpp's GetSteamIDForPlayerIndex (nm -u over client.so's
// objects), and both went with them, so client.so no longer links
// libsteam_api.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// 5. TWO CONSTANTS OF THIS BUILD (UTIL_GetActiveHolidayString/OperationString):
//    client-only, so in src/client/native/client_util_library.cpp since
//    plan_server_native_v2 T2 (2026-10-18), with UTIL_IsLowViolence.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// 6. THE MAP NAME CLEANER
//
// Stock strips a "workshop/" or "maps/workshop/" prefix and a ".ugc" suffix
// under TF_DLL / TF_CLIENT_DLL. Neither is on either link line, so stock
// already answered its argument unchanged here; that is what it does now, with
// the unreachable arm and its four #defines gone.
//-----------------------------------------------------------------------------


//-----------------------------------------------------------------------------
// 7. THE SHARED-RANDOM FAMILY: client-only, so in
//    src/client/native/client_util_library.cpp since plan_server_native_v2 T2
//    (2026-10-18), with the banner that says why it left util_shared.cpp.
//-----------------------------------------------------------------------------

//------------------------------------------------------------------------------
// SOURCESHARP, Phase 4 exit lane p4-util — the vector<->angle family and the
// `developer` ConVar, out of game/shared/util_shared.cpp.
//
// Moved for CYCLE MEMBERSHIP rather than for the census: game/shared/
// debugoverlay_shared.cpp owed SCC 11 four sole-definer symbols and two of them
// were UTIL_VecToYaw and UTIL_YawToVector; game/server/util.cpp owed
// util_shared.cpp exactly one and it was `developer`. Neither was ever a
// shim->stock cross-edge, so neither shows in tools/entity-census -- they show
// in tools/p4util-holders --owes, which is the number that decides whether the
// vpc flip has to be one commit.
//
// The ConVar came whole rather than being declared extern over there: a second
// ConVar registered under the same name is not a compile error and not a link
// error, and neither the console nor the game says which one it read.
//
// Pure math, no engine and no entity in any of the five.
//------------------------------------------------------------------------------

// `developer` WAS DECLARED HERE, AND THE ENGINE DECLARES IT TOO: engine.so,
// loaded before either game DLL, registers the name first, so this was a
// duplicate that changed nothing (`strings engine.so | grep -x developer`).
// Deleted in plan_server_novalve.md P2.3b, on both sides; no object on either
// link line imports the symbol, and every reader asks the engine by name
// (shell_debugoverlays.cpp's ConVarRef, shell_gamedll_convars.cpp's FindVar).

// THE VECTOR<->ANGLE FAMILY WAS HERE -- UTIL_VecToYaw and UTIL_VecToPitch, both
// overloads each, and UTIL_YawToVector -- and plan_shim.md Phase 2c, lane
// 2-utilshared, deleted all five. Lane H2 read them as dead; measured on this
// tree they are:
//
//     nm -C -u _vpc_/ninja/debug/objs/{server,client}_sourcesharp/*.o \
//       | grep -c 'UTIL_VecToYaw\|UTIL_VecToPitch\|UTIL_YawToVector'
//       -> 0 and 0, and tools/shim-census who-imports agrees for the whole file
//
// THE REASON THEY WERE HERE AT ALL IS SPENT, and the banner above says what it
// was: they moved for CYCLE MEMBERSHIP rather than for the census --
// game/shared/debugoverlay_shared.cpp owed SCC 11 four sole-definer symbols and
// two of them were UTIL_VecToYaw and UTIL_YawToVector. debugoverlay_shared.cpp
// has a row in NEITHER vpc now, so there is no cycle left to be a member of.
// Their other callers in the tree -- ai_basenpc.cpp, ai_motor.cpp,
// ai_movesolver.cpp, ai_pathfinder.cpp, ai_networkmanager.cpp,
// ai_behavior_standoff.cpp, baseanimating.cpp -- are uncompiled too.
//
// `developer` STAYS, and its note above says why; it is a diagnostic the engine
// reads and it is not part of this family. game/server/util.cpp's debt to this
// file was `developer` alone, so nothing that moved for the other reason goes
// here.
//
// The DECLARATIONS stay in game/shared/util_shared.h, which is stock's header.

//------------------------------------------------------------------------------
// SOURCESHARP, Phase 4 exit lane p4-util — the blood, tracer and decal
// emitters, out of game/shared/util_shared.cpp, byte-for-byte and with both
// #ifdef CLIENT_DLL arms carried verbatim.
//
// The family moved WHOLE, including UTIL_BloodImpact which no shim TU names:
// it is defined in util_shared.cpp and called by UTIL_BloodDrips, so leaving it
// behind is the conversion 4e measured and declined, one symbol to the left.
// The tombstone in util_shared.cpp records why the rest of 4e's measurement no
// longer holds.
//------------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Purpose: Make a tracer effect using the old, non-particle system, tracer effects.
//-----------------------------------------------------------------------------
void UTIL_Tracer( const Vector &vecStart, const Vector &vecEnd, int iEntIndex, 
				 int iAttachment, float flVelocity, bool bWhiz, const char *pCustomTracerName, int iParticleID )
{
	CEffectData data;
	data.m_vStart = vecStart;
	data.m_vOrigin = vecEnd;
#ifdef CLIENT_DLL
	// LANE sub-entitylist: index -> handle, THROUGH THE SLOT TABLE.
	//
	// plan_entity.md clause 2 -- no compiled TU may name the stock client
	// entity list -- and this was the last such name in a game/shared shim
	// file. SourceSharp_ClientHandleFromIndex is stock's own EntIndexToHandle
	// with its primitive swapped: it asks CSourceSharpSlotTable::HandleOf and
	// falls back to the legacy list's `pUnk ? pUnk->GetRefEHandle() :
	// CBaseHandle()` for as long as stock client entities exist. Stock's
	// `entnum < -1` guard needs no restating -- HandleOf refuses an entry
	// outside the table and CBaseEntityList::LookupEntityByNetworkIndex
	// refuses a negative index, so every negative reaches the same invalid
	// handle it always did, and CBaseHandle() and INVALID_EHANDLE are the same
	// m_Index (basehandle.h:87,92).
	//
	// Same answer today -- every shell is in both -- and no edit needed when
	// the seam goes. fx.cpp and particlemgr.cpp took this exact move at
	// step p7-exit.
	data.m_hEntity = SourceSharp_ClientHandleFromIndex( iEntIndex );
#else
	data.m_nEntIndex = iEntIndex;
#endif
	data.m_flScale = flVelocity;
	data.m_nHitBox = iParticleID;

	// Flags
	if ( bWhiz )
	{
		data.m_fFlags |= TRACER_FLAG_WHIZ;
	}

	if ( iAttachment != TRACER_DONT_USE_ATTACHMENT )
	{
		data.m_fFlags |= TRACER_FLAG_USEATTACHMENT;
		data.m_nAttachmentIndex = iAttachment;
	}

	// Fire it off
	if ( pCustomTracerName )
	{
		DispatchEffect( pCustomTracerName, data );
	}
	else
	{
		DispatchEffect( "Tracer", data );
	}
}


//-----------------------------------------------------------------------------
// UTIL_BloodDrips, RELOCATED -- AND THE CLIENT HALF DID NOT COME WITH IT.
//
// plan_shim.md Phase 2c, lane 2-utilshared. The tombstone that stood here said
// this body could not move because on the CLIENT it reaches two stock symbols:
// `UTIL_Smoke` in game/client/cdll_util.cpp and `g_Language` in
// game/shared/gamerules.cpp. BOTH HALVES OF THAT ARE NOW ANSWERED, and neither
// was answered by wishing:
//
//   * `g_Language` is no longer game/shared/gamerules.cpp's. It is
//     sourcesharp/game/shared/shell_gamerules.cpp:80, which has a row in BOTH
//     vpcs, so the read below resolves inside the shim on both sides and
//     gamerules.cpp is not in either vpc at all.
//
//   * `UTIL_Smoke` IS still game/client/cdll_util.cpp's on the client -- and
//     the client does not call this function. Measured, not assumed:
//
//         tools/shim-census --mtime-policy off who-imports \
//             src/game/shared/util_shared.cpp
//
//     reads ONE client importer of that whole translation unit and its symbol
//     is `hl2_episodic`. `UTIL_BloodDrips` has zero client importers. So the
//     server arm moves and the client arm is DELETED rather than carried, and
//     the cross-edge the old tombstone refused to create is never created.
//
// THIS IS THE RULING LANE S3 ASKED FOR ON `UTIL_Smoke`, and it is the census
// that makes it, not a reading. `_Z10UTIL_SmokeRK6Vectorff` has exactly one
// importer per side and on BOTH sides it is `util_shared.o` -- this body. So:
//
//   * the client's `UTIL_Smoke` (cdll_util.cpp:375) loses its LAST consumer
//     here. cdll_util.cpp itself stays: five other client objects still import
//     other symbols from it.
//   * the server's `UTIL_Smoke` (sourcesharp/game/server/server_util_library.cpp:435)
//     keeps exactly one consumer, and after this commit that consumer is in a
//     SHIM file instead of a stock shared one. S3's stated worry -- "moving it
//     changes a shared TU, and the client half must not move with the server
//     half" -- is therefore dissolved rather than deferred again: there is no
//     client half left to move, and the server half's only caller is code S3
//     may move without touching stock at all.
//
// AND IT DOES NOT GO TO MANAGED IN THIS LANE, which is the other half of the
// ruling. Lane N2 ruled this body MOVE to SourceSharp.Server/EffectsOverTempEnts.cs
// and it is right about the destination: every line is policy. Two of its four
// callees are not there yet -- `UTIL_Smoke` is lane S3's Phase 3 step and
// `UTIL_BloodImpact` (immediately below) is lane H2's -- so a managed
// UTIL_BloodDrips today would need a NEW ABI slot per callee, which S3 and H2
// would retire again within the phase. The family crosses as a family, after
// its callees; this commit puts it beside them in the meantime.
//
// ONE PREMISE WRITTEN DOWN BECAUSE IT IS NOW STALE ELSEWHERE:
// SourceSharp.Tests/BloodEffectPortabilityTests.cs says the blocker is
// `g_pGameRules->IsMultiplayer()` with "no managed route". There is one, and it
// runs the other way: `GameRulesPolicy.Multiplayer` is pushed native through
// `RegisterGameRulesPolicy`, and `CSourceSharpGameRules::IsMultiplayer` reads
// `s_Policy.bMultiplayer` (shell_gamerules.cpp:416). The managed side is the
// DECLARATION, so a managed copy would not be hardcoding a base class's answer.
// That row is blocked on UTIL_Smoke and UTIL_BloodImpact, not on IsMultiplayer.
//
// `g_Language` IS READ HERE, and lane H1's Phase 3e step moves that ConVar to
// managed and needs one `ManagedConVarRef` at its reading site. The site used to
// be util_shared.cpp:534. It is in this file now, in the body below.
//-----------------------------------------------------------------------------
#ifndef CLIENT_DLL
// g_Language IS DECLARED IN C# (SourceSharp.Server.GLanguageConVar),
// plan_server_novalve.md P2.3b; this reads it by name.
static CManagedConVarRef s_Language( "g_Language" );

void UTIL_BloodDrips( const Vector &origin, const Vector &direction, int color, int amount )
{
	if ( !UTIL_ShouldShowBlood( color ) )
		return;

	if ( color == DONT_BLEED || amount == 0 )
		return;

	if ( s_Language.GetInt() == LANGUAGE_GERMAN && color == BLOOD_COLOR_RED )
		color = 0;

	if ( g_pGameRules->IsMultiplayer() )
	{
		// scale up blood effect in multiplayer for better visibility
		amount *= 5;
	}

	if ( amount > 255 )
		amount = 255;

	if (color == BLOOD_COLOR_MECH)
	{
		ss::Effects()->Sparks(origin);
		if (random->RandomFloat(0, 2) >= 1)
		{
			UTIL_Smoke(origin, random->RandomInt(10, 15), 10);
		}
	}
	else
	{
		// Normal blood impact
		UTIL_BloodImpact( origin, direction, color, amount );
	}
}
#endif // !CLIENT_DLL

//-----------------------------------------------------------------------------
// TOMBSTONE, step 7f. UTIL_IsLowViolence, UTIL_ShouldShowBlood and the four
// violence_* ConVars they read moved to
// sourcesharp/game/shared/util_shared_library.cpp. The ConVars are FILE
// STATICS, so the predicates and the storage had to travel together: leaving
// the four here would have given the moved predicates a second, unreachable
// set of four whose value no operator could change.
//
// So did the string parsers (UTIL_StringToFloatArray and its three callers,
// all deleted since),
// GetCleanMapName, UTIL_GetActiveHolidayString and
// UTIL_GetActiveOperationString. Every declaration stays in util_shared.h, so
// no caller changed. (GetSteamUtils, GetUniverse and the client's four
// UTIL_GetFilteredPlayerName overloads moved here too, and then to managed:
// section 3.)
//-----------------------------------------------------------------------------

// UTIL_DecalTrace and UTIL_BloodDecalTrace went in plan_server_native_v2 T2:
// the first's subject was a constant NULL, so no blood decal was ever painted.
// Stock's arm is managed BloodDecals now, painting through the decal system.

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : &pos - 
//			&dir - 
//			color - 
//			amount - 
//-----------------------------------------------------------------------------
void UTIL_BloodImpact( const Vector &pos, const Vector &dir, int color, int amount )
{
	CEffectData	data;

	data.m_vOrigin = pos;
	data.m_vNormal = dir;
	data.m_flScale = (float)amount;
	data.m_nColor = (unsigned char)color;

	DispatchEffect( "bloodimpact", data );
}
