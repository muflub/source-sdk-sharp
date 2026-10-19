//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — what the link still owes, and nothing else =========//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// Phase 1b merged three files into this one: shell_ai_absent.cpp (189),
// shell_ai_orphans.cpp (321) and shell_entity_orphans.cpp (420). Between them
// they defined 40-odd symbols; ELEVEN of those are still imported by an object
// on the server link line, and those eleven are what is below. The rest were
// stubs for callers that earlier phases had already deleted.
//
// THIS FILE'S EXIT CONDITION IS ZERO AND IT IS NOT REACHED -- see "WHAT STOPS
// THIS FILE REACHING ZERO" at the bottom of this header.
//
// THE MEMBERSHIP RULE, which is the three files' rule kept rather than a new
// one: a definition belongs here only while an object ON THE NINJA GRAPH'S LINK
// LINE imports it. Not a file in _vpc_/ninja/debug/objs/ -- that directory keeps
// the objects of translation units the build has since evicted, and joining
// against those is what let the three files drift to 90% dead. Re-measure when a
// stock translation unit leaves server_csharp.vpc, and delete what stops being
// named in the same commit.
//
// It is a FLOOR and not a proof, for the recorded reason: a virtual call emits
// no undefined symbol. It is sound here because nothing in this build emits a
// vtable for any of these classes. RE-MEASURED at 5034fb3e6 rather than
// restated: `nm -C -A` over all 108 server and all 253 client objects the two
// link lines name, grepped for `(vtable|typeinfo) for
// (CNodeEnt|CAI_BaseNPC|CSkyCamera|CAI_SchedulesManager|CAI_NetworkManager|
// CEnvMicrophone|CEnvPlayerSurfaceTrigger)`, prints NOTHING on either side. The
// same command asked about CStandaloneEntity prints eight rows, which is the
// control that stops this being a check that cannot fail.
//
//=============================================================================
// WHAT IS LEFT, AND WHO IS STILL WAITING FOR IT.
//
// Measured at 5034fb3e6 by `tools/shim-census --mtime-policy off who-imports
// sourcesharp/game/server/shell_absent.cpp` over the objects the link line
// names -- 108 server, 253 client, 0 ghosts. SEVEN objects import ELEVEN
// symbols. The client answer is vacuous rather than informative: this is not a
// client translation unit.
//
//   SYMBOL                                      IMPORTED BY         DIES WITH
//   ------------------------------------------- ------------------- -----------
//   g_iszFuncBrushClassname                     world_adapter       lane S2
//   PostFrameNavigationSystem                   shell_serversystems lane S2
//     (+ CPostFrameNavigationHook's four members, which are that object's
//      body: the row in kServerSystems[] is shell_serversystems.cpp:346)
//   DrawMessageEntities                         shell_debugoverlays lane S3
//   CEnvPlayerSurfaceTrigger::SetPlayerSurface  shell_movement      lane H1
//   RadiusDamage                                shell_worldquery    lane S3
//   UTIL_BloodSpray                             shell_worldquery    lane S3
//   EffectsPrecache                             NOBODY -- see below
//
// GetRecentNPCSpeech left it in P5 too: it answered 0 on every call, and
// GetBugReportInfo, its one caller, now writes the empty report directly.
// FOUR ROWS LEFT THIS TABLE IN P5 (plan_server_novalve.md): InitializeAINetworks,
// LoadAllSchedules and g_AI_SchedulesManager when world_adapter stopped calling
// the empty AI loaders (lane E 27), and CEnvMicrophone::OnSoundPlayed when
// sound_emitter's calls went. `nm --undefined-only` over every server object
// named none of the four, and gc-sections had already dropped all but the
// manager object; their definitions, and CPostFrameNavigationHook's
// EnqueueEntityNavigationQuery (no caller either), are deleted.
//
// EffectsPrecache is the exception that makes the rule readable: it is named by
// no object at all, because PRECACHE_REGISTER_FN registers it from a static
// constructor. An importer count cannot see a self-registration, so it is here
// on a hand sweep and not on a measurement. Deleting it would leave six game
// sounds unprecached and the first play of each silent.
//
//=============================================================================
// WHAT STOPS THIS FILE REACHING ZERO -- lane 5a-physresidue, plan_shim.md 4a
// item 11.
//
// The item was written as "its survivors lost their last importers when
// mapentities.cpp was cut", and FIVE of them had. Those five are deleted:
// CNodeEnt::Spawn( const char * ), CAI_BaseNPC::TaskComplete,
// NWCEdit::RememberEntityPosition, SceneManager_ClientActive and
// GetCurrentSkyCamera -- each 0 importers on BOTH link lines, measured per
// mangled symbol, not per file.
//
// The remaining eleven are NOT waiting on mapentities and never were. Six of
// their seven importers are the port's own shim objects and are as live as the
// shim is -- shell_worldquery's RadiusDamage is env_explosion's blast and
// EntityFlame's burn tick, sound_emitter's OnSoundPlayed runs on every
// EmitSound (until P5), shell_movement's SetPlayerSurface runs whenever the material under
// a player changes. None of that is residue; it is this project's inverted
// shape, C++ kept so that C# can ask native for something.
//
// So the exit condition is zero and zero is not reachable by out-waiting
// mapentities. Each remaining row goes when its named lane ports its importer,
// which is the right-hand column and is unchanged. This file does not get to
// zero before those do.
//=============================================================================//

// SIX INCLUDES WENT WITH THE FIVE DELETED DEFINITIONS (lane 5a-physresidue):
// ai_node.h and ai_initutils.h were CNodeEnt's, wcedit.h was NWCEdit's, and
// SkyCamera.h -- with the player.h that existed ONLY to be in front of it,
// because SkyCamera.h does not include its own sky3dparams_t dependency -- was
// GetCurrentSkyCamera's. ai_speech.h and sceneentity.h went with
// GetRecentNPCSpeech (P5).
// THE R5 SET (plan_server_novalve.md P5): cbase.h, the AI headers,
// env_player_surface_trigger.h, envmicrophone.h, soundent.h and takedamageinfo.h
// went. What they gave is server.so's own below: ss::CPostFrameNavigationHook,
// ss::SetPlayerSurface and the ss_shareddefs.h constants.
#include "shell_serversystem.h"   // CServerSystemPerFrame
#include "string_t.h"
#include "utlvector.h"
#include "ss_shareddefs.h"        // DONT_BLEED, kDmgBlast, the SOUND_* bits and channel
#include "ss_soundent.h"
#include "igamerules.h"
#include "ss_precache_register.h"
#include "gamesound.h"
#include "ss_effect_dispatch.h"
#include "sourcesharp.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E17: the server's IManagedPlayerCommands
#include "ss_takedamageinfo.h"	// server.so's damage info

uint32_t SourceSharp_HandleFromEntity( IGameEntity *pEntity );
extern CGlobalVars *gpGlobals;
class CFunctor;


//=============================================================================
// THE AI, ABSENT (was shell_ai_absent.cpp).
//
// Phase 7 is decided: this mod will not port Source's AI, it will design its own
// in C#. None of ai_*.cpp is in the build, and these are what lets the rest link
// without it.
//
// THE LOUD STUBS AND THE AI_ABSENT MACRO ARE GONE -- plan_shim.md 4a item 11,
// lane 5a-physresidue. Every stub in this file that ASSERTED was one of the
// three "UNREACHABLE" ones, and all three were kept for mapentities.o, which
// Phase 2c evicted from server_csharp.vpc. A stub kept for a caller that has
// left is not a loud stub; it is a definition with no creditor, which is the
// drift the membership rule at the top of this file exists to prevent. So the
// deletion is that rule applied, not an exception to it.
//
// What survives is the other kind and says so at each body: "empty because
// there is genuinely nothing to do" is a claim about the behaviour, and the
// distinction from "empty because unreachable" is kept by there no longer
// being any of the second kind here to confuse it with.
//=============================================================================

string_t g_iszFuncBrushClassname = NULL_STRING;

//=============================================================================
// THE DEFERRED-NAVIGATION GAME SYSTEM (was shell_ai_orphans.cpp).
//
// Not AI behaviour: a game system that happened to live in ai_basenpc.cpp,
// registered unconditionally before any entity exists. Its row is
// shell_serversystems.cpp:346, and stubbing the accessor to NULL put a null
// pointer in the system list -- which is what the assert caught on the first
// launch. With no NPCs nothing ever queues a query, so it runs and does nothing,
// which is the behaviour and not a stand-in for it.
//
// The body is NOT lifted from ai_basenpc.cpp: the real one drives a job queue
// for NPC navigation, and with an empty queue the lifted version and this one do
// exactly the same nothing -- without dragging the job system in behind it.
//=============================================================================

// CPostFrameNavigationHook went (plan_server_native_min P1): its whole body
// was a bool toggle nothing read (m_Functors never filled,
// m_bGameFrameRunning never read); the kServerSystems[] row and its accessor
// went with it. The SetPlayerSurface relay below is not part of the hook.

//=============================================================================
// THE LAST TENANTS OF THE MAP-ENTITY FILES (was shell_entity_orphans.cpp).
//=============================================================================

//-----------------------------------------------------------------------------
// The handle spelling of env_player_surface_trigger's funnel (its static
// CEnvPlayerSurfaceTrigger::SetPlayerSurface until P5), which is the one
// the movement loop calls (Phase 9a): the loop holds a CBaseHandle for ANY
// player it moves, and the old resolve through UTIL_PlayerByIndex silently
// skipped every player that was not a CBasePlayer.
//-----------------------------------------------------------------------------
namespace ss { void SetPlayerSurface( const CBaseHandle &hPlayer, char gameMaterial ); }

void ss::SetPlayerSurface( const CBaseHandle &hPlayer, char gameMaterial )
{
	// Ignore players in the air (stops bunny hoppers escaping triggers)
	if ( gameMaterial == 0 )
		return;

	if ( !hPlayer.IsValid() )
		return;

	// E17: the server's managed-built area.
	if ( IManagedPlayerCommands *pCommands = ManagedPlayerCommands() )
	{
		pCommands->OnPlayerSurfaceChanged( (uint32_t)hPlayer.ToInt(),
		                                   (int32_t)gameMaterial );
	}
}

//-----------------------------------------------------------------------------
// point_message's overlay pass, called once a frame from CServerGameDLL::Think.
// g_MessageEntities is an always-empty list; the managed point_message draws
// through the debug-overlay path from C#, so there is nothing to sweep.
//-----------------------------------------------------------------------------
// DrawMessageEntities went (plan_server_native_min P1): an empty body; the
// managed point_message draws through the debug-overlay path from C#.


//-----------------------------------------------------------------------------
// GetCurrentSkyCamera IS GONE, AND IT IS THE SECOND HALF OF A PAIR --
// plan_shim.md 4a item 11, lane 5a-physresidue.
//
// GetSkyCameraList went in Phase 1b when CBaseEntity::GetEntitySkybox stopped
// being linked. This one outlived it by exactly one caller: shell_playerlocaldata
// .cpp's ClientData_Update, which asked for the camera and fell through to
// SourceSharp_FillSkyboxParams on the NULL it always got. Lane 3b-enginebind
// deleted that function -- its only caller was behind a hardcoded NULL -- and
// StandaloneClientData_Update, the live twin that replaced it, calls
// SourceSharp_FillSkyboxParams directly and never asks for a camera at all.
//
// So the answer did not change; the question stopped being asked. Nothing on
// either link line names the symbol.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Blood in the air, at a point and in a direction. From effects.cpp, verbatim.
// Its only caller in this build is SourceSharp_BloodEffect in
// shell_worldquery.cpp, serving the managed env_blood and the managed bullet
// impacts -- this project's "inverted" shape: not a live C++ subsystem, but a
// C++ free function kept so that C# can ask native to draw something.
//
// DONT_BLEED is a legitimate colour and means exactly this; the check is stock's
// and is duplicated in SourceSharp_BloodEffect deliberately.
//-----------------------------------------------------------------------------
void UTIL_BloodSpray( const Vector &pos, const Vector &dir, int color, int amount, int flags )
{
	if( color == ss::DONT_BLEED )
		return;

	ss::CEffectData	data;

	data.m_vOrigin = pos;
	data.m_vNormal = dir;
	data.m_flScale = (float)amount;
	data.m_fFlags = flags;
	data.m_nColor = color;

	DispatchEffect( "bloodspray", data );
}

//-----------------------------------------------------------------------------
// The sounds effects.cpp precached, verbatim. NAMED BY NOTHING and kept anyway:
// PRECACHE_REGISTER_FN registers it from a static constructor, so no importer
// count can see it, and without it six game sounds the managed side plays go
// unprecached and the first play of each is silent.
//
// The TF_DLL arm went with the file rather than being carried: it precached
// three models for tempfx, and this mod is not TF.
//-----------------------------------------------------------------------------
void EffectsPrecache( void *pUser )
{
	GameSound_PrecacheScript( "Underwater.BulletImpact" );

	GameSound_PrecacheScript( "FX_RicochetSound.Ricochet" );

	GameSound_PrecacheScript( "Physics.WaterSplash" );
	GameSound_PrecacheScript( "BaseExplosionEffect.Sound" );
	GameSound_PrecacheScript( "Splash.SplashSound" );

	if ( gpGlobals->maxClients > 1 )
	{
		GameSound_PrecacheScript( "HudChat.Message" );
	}
}

SS_PRECACHE_REGISTER_FN( EffectsPrecache );
