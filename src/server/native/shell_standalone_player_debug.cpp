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
#include "player_name_match.h"      // PlayerNameMatch
#include "native_command_body.h"
#include "ss_string.h"

extern CGlobalVars *gpGlobals;

// The player-slot helpers util.h declared; shell_playerslots.cpp and
// shell_commandclient.cpp define them.
const char *UTIL_PlayerNameBySlot( int nSlot );
int UTIL_PlayerUserIdBySlot( int nSlot );
int UTIL_PlayerSlotByName( const char *pName );
int UTIL_PlayerSlotByPartialName( const char *pName );
int UTIL_PlayerSlotByCommandArg( const char *pArg );
void UTIL_GetPlayerConnectionInfo( int playerIndex, int &ping, int &packetloss );

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

//-----------------------------------------------------------------------------
// WHO DOES THIS WORD NAME.
//
// Every by-name player lookup -- UTIL_PlayerByName, ByPartialName, ByUserId,
// ByCommandArg -- has answered nobody for every argument since the port began,
// because all four resolve a slot as a CBasePlayer and this build makes none.
// They say so now, and UTIL_PlayerSlotBy* (util.h) is what answers instead.
//
// THIS COMMAND EXISTS SO THAT THE ANSWER CAN BE SEEN, and that is the whole of
// its justification. A lookup with no caller in this build is exactly the shape
// that rots: the family it replaces was broken for months and nothing noticed,
// because nothing was asking it anything. One command that asks is the
// difference between a fix and a claim -- and it is the only live evidence
// available for this half of the verdict, because every SDK caller of the
// original four is in a file server_csharp.vpc evicts.
//
// It also shows the divergence worth knowing: AMBIGUOUS is a distinct answer
// here. Stock returns the same NULL for "nobody is called that" and for "two
// people are, say which one you mean".
//-----------------------------------------------------------------------------
static const char *WhoisAnswer( int nSlot, char *pOut, int nOutBytes )
{
	if ( nSlot == PlayerNameMatch::AMBIGUOUS )
		ss::StrCopy( pOut, "AMBIGUOUS -- more than one player matched", nOutBytes );
	else if ( nSlot == PlayerNameMatch::NOBODY )
		ss::StrCopy( pOut, "nobody", nOutBytes );
	else
		ss::Format( pOut, nOutBytes, "slot %d (%s)", nSlot, UTIL_PlayerNameBySlot( nSlot ) );

	return pOut;
}

SS_NATIVE_COMMAND( cs_sv_whois )
{
	char szAnswer[ 160 ];

	Msg( "connected players:\n" );

	for ( int i = 1; i <= gpGlobals->maxClients; i++ )
	{
		if ( !CStandalonePlayer::FromIndex( i ) )
			continue;

		// The ping too, because UTIL_GetPlayerConnectionInfo is the fifth member
		// of util.cpp's slot-walk family and the one with nothing visible to
		// give it away: it asked UTIL_PlayerByIndex for a CBasePlayer, got
		// NULL, and reported ping 0 and loss 0 for everybody. Whether it CAN be
		// measured is a separate fact and is printed rather than assumed -- the
		// listen-server host has no net channel to itself, so zero is the
		// honest answer there and must not be mistaken for the old silence.
		int nPing = 0, nLoss = 0;
		UTIL_GetPlayerConnectionInfo( i, nPing, nLoss );

		const bool bHasChannel = engine->GetPlayerNetInfo( i ) != NULL;

		Msg( "  slot %2d  userid %-5d  ping %-4d loss %-3d %s  \"%s\"\n",
		     i, UTIL_PlayerUserIdBySlot( i ), nPing, nLoss,
		     bHasChannel ? "(net channel)   " : "(no net channel)",
		     UTIL_PlayerNameBySlot( i ) );
	}

	if ( args.ArgC() < 2 )
	{
		Msg( "cs_sv_whois <name|#userid|partial> -- nothing asked\n" );
		return;
	}

	const char *pArg = args.Arg( 1 );

	Msg( "  \"%s\" names:\n", pArg );
	Msg( "    exact name    %s\n",
	     WhoisAnswer( UTIL_PlayerSlotByName( pArg ), szAnswer, sizeof( szAnswer ) ) );
	Msg( "    partial name  %s\n",
	     WhoisAnswer( UTIL_PlayerSlotByPartialName( pArg ), szAnswer, sizeof( szAnswer ) ) );
	Msg( "    command arg   %s\n",
	     WhoisAnswer( UTIL_PlayerSlotByCommandArg( pArg ), szAnswer, sizeof( szAnswer ) ) );

	// SOURCESHARP, Phase 2c lane 2-utilshared: the old spelling used to be
	// printed here side by side, so the difference was a reading rather than a
	// claim. `UTIL_PlayerByCommandArg` is gone -- this Msg was its ONLY importer
	// on either link line (tools/shim-census who-imports
	// src/game/shared/util_shared.cpp), and a diagnostic that exists to show a
	// function answering NULL is not a reason to keep the function.
	//
	// Nothing is lost from the command: the three UTIL_PlayerSlotBy* answers
	// above are the ones that resolve, and the removed line's whole content was
	// the constant "NULL".
}
