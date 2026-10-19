//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_standalone_player.cpp's diagnostic console commands ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching rows under #if DEBUG.
//
//=============================================================================//

#include "ss_util.h"                // engine
#include "ss_entityhandle.h"        // ss::GameEntityFromEntityHandle
#include "shell_standalone_player.h" // StandalonePlayer_CommandClient
#include "movement_ground.h"        // MovementGroundVelocity
#include "native_command_body.h"
#include "ss_string.h"

extern CGlobalVars *gpGlobals;


// cs_sv_playershadow is managed's since plan_physics_bones_native_zero 2.5
// (PlayerShadowCommand): the counters are PlayerShadowBodies', and live /
// outlived cross through SourceSharp_PlayerShadowCounts.

//-----------------------------------------------------------------------------
// THE ss_* SPELLINGS ARE MANAGED TOO, except ss_ground below.
//
// ss_kill, ss_hurtme, ss_setpos and ss_noclip each tokenised a formatted string
// and fed it back through the verb table in shell_standalone_player.cpp, which
// is a console command talking to itself through a parser. They are
// [ConCommand] classes in SourceSharp.Server/PlayerCheatCommands.cs now,
// sharing a body with the stock spelling rather than restating it -- two
// spellings of one command that are separately implemented are two commands
// waiting to disagree.
//
// ss_ground STAYS NATIVE, and not for want of a crossing: what it prints is
// native movement state with no managed mirror -- MvGetFlags, MvGetBaseVelocity,
// MvLocal().m_flFallVelocity and the ground entity's hierarchy -- and the whole
// value of the instrument is that it reads those directly rather than being told
// what managed code believes. tools/ references it by name.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// What the player is standing on, and how fast it is going.
//
// The instrument the ground divergence was measured with, kept because the
// measurement is the evidence. "A rider on a managed platform reads zero ground
// velocity" is not visible from outside the server: the rider is carried anyway
// (Riders.Carry moves them by the brush's step), so the eye sees a lift working
// while FL_ONGROUND is clear, the fall keeps accumulating and the base velocity
// a floor hands its passenger is computed against a floor the movement loop
// cannot see.
//
// It prints the HIERARCHY of the ground on purpose. The two failures this tells
// apart -- a ground the loop could not resolve, and a ground resolved but
// answering zero -- looked identical before, and one of them was mistaken for
// the other twice in this tree's own notes.
//
// stock's `groundlist` is the thing this replaces, and why it could not be
// used: it walks gEntList and resolves by CBaseEntity::Instance, so in a build
// where the player and every brush mover are on the parallel hierarchy it
// prints "--- 0 links" and nothing else, for every entity in the map.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( ss_ground )
{
	CStandalonePlayer *pPlayer = StandalonePlayer_CommandClient();

	if ( !pPlayer )
	{
		Msg( "[ss_ground] no standalone player issued this\n" );
		return;
	}

	IHandleEntity *pGround = pPlayer->MvGetGroundEntity();

	if ( !pGround )
	{
		Msg( "[ss_ground] player at %.1f %.1f %.1f  ground=NONE  onground=%d  "
		     "base=(%.1f %.1f %.1f)  fall=%.1f\n",
		     pPlayer->GetAbsOrigin().x, pPlayer->GetAbsOrigin().y, pPlayer->GetAbsOrigin().z,
		     ( pPlayer->MvGetFlags() & FL_ONGROUND ) != 0,
		     pPlayer->MvGetBaseVelocity().x, pPlayer->MvGetBaseVelocity().y,
		     pPlayer->MvGetBaseVelocity().z,
		     // .Get(), and it is not decoration. m_flFallVelocity is a
		     // CNetworkVarBase<float>, a class -- and a class handed to a
		     // varargs "..." is not converted to the float it wraps. It went
		     // into the SSE argument slot as four bytes and %f read eight out
		     // again, so every ss_ground line printed a fall velocity made
		     // half of the register's previous contents. -Wformat caught it;
		     // nothing else could have.
		     pPlayer->MvLocal().m_flFallVelocity.Get() );
		return;
	}

	const Vector vecGround = MovementGroundVelocity( pGround );

	// ONE HIERARCHY, so one arm. This used to ask EntityFromEntityHandle first
	// and print "CBaseEntity" when it answered -- which it never did.
	IGameEntity *pGameEntity = ss::GameEntityFromEntityHandle( pGround );

	const char *pszHierarchy = pGameEntity ? "CStandaloneEntity" : "neither";

	const char *pszClass = pGameEntity ? StandaloneEntity_Name( pGameEntity ) : "?";

	Msg( "[ss_ground] player at %.1f %.1f %.1f  ground=%s (%s)  onground=%d  "
	     "groundvel=(%.1f %.1f %.1f)  base=(%.1f %.1f %.1f)  fall=%.1f\n",
	     pPlayer->GetAbsOrigin().x, pPlayer->GetAbsOrigin().y, pPlayer->GetAbsOrigin().z,
	     pszHierarchy, pszClass,
	     ( pPlayer->MvGetFlags() & FL_ONGROUND ) != 0,
	     vecGround.x, vecGround.y, vecGround.z,
	     pPlayer->MvGetBaseVelocity().x, pPlayer->MvGetBaseVelocity().y,
	     pPlayer->MvGetBaseVelocity().z,
	     pPlayer->MvLocal().m_flFallVelocity.Get() );   // see above
}

// cs_sv_whois went with shell_playerslots.cpp (plan_server_native_min P1):
// the UTIL_PlayerSlotBy* lookups it existed to show answering were its sole
// referrer and are managed state now (PlayerNames / PlayerPing.Sample); the
// command is a debug-API route if it is wanted back (Decision 5).
