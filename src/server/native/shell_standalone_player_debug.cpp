//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Debug builds only; NativeCommands declares the matching rows under #if DEBUG.

#include "ss_util.h"

#include "shell_standalone_player.h"
#include "movement_ground.h"
#include "native_command_body.h"
#include "ss_string.h"

extern CGlobalVars *gpGlobals;

// What the player is standing on, its hierarchy and how fast it moves, read from
// native movement state directly. Stock's groundlist cannot see standalone
// entities. tools/ reference it by name.
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
			 ( pPlayer->MvGetFlags() & FL_ONGROUND ) != 0, pPlayer->MvGetBaseVelocity().x,
			 pPlayer->MvGetBaseVelocity().y, pPlayer->MvGetBaseVelocity().z,
			 // .Get(): a CNetworkVar passed to varargs is not converted to float.
			 pPlayer->MvLocal().m_flFallVelocity.Get() );
		return;
	}

	const Vector vecGround = MovementGroundVelocity( pGround );

	IGameEntity *pGameEntity = ss::GameEntityFromEntityHandle( pGround );

	const char *pszHierarchy = pGameEntity ? "CStandaloneEntity" : "neither";

	const char *pszClass = pGameEntity ? StandaloneEntity_Name( pGameEntity ) : "?";

	Msg( "[ss_ground] player at %.1f %.1f %.1f  ground=%s (%s)  onground=%d  "
		 "groundvel=(%.1f %.1f %.1f)  base=(%.1f %.1f %.1f)  fall=%.1f\n",
		 pPlayer->GetAbsOrigin().x, pPlayer->GetAbsOrigin().y, pPlayer->GetAbsOrigin().z, pszHierarchy, pszClass,
		 ( pPlayer->MvGetFlags() & FL_ONGROUND ) != 0, vecGround.x, vecGround.y, vecGround.z,
		 pPlayer->MvGetBaseVelocity().x, pPlayer->MvGetBaseVelocity().y, pPlayer->MvGetBaseVelocity().z,
		 pPlayer->MvLocal().m_flFallVelocity.Get() );
}
