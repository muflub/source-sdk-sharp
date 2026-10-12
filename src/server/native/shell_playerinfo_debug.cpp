//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_playerinfo.cpp's diagnostic console command ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
//=============================================================================//

#include "eiface.h"
#include "edict.h"
#include "game/server/iplayerinfo.h"
#include "tier1/interface.h"      // Sys_GetFactoryThis

extern IVEngineServer *engine;
extern CGlobalVars *gpGlobals;

#include "shell_playerinfo.h"     // ShellPlayerInfo_Refusals, _OrphanAnswers
#include "native_command_body.h"

//-----------------------------------------------------------------------------
// A PLUGIN-STYLE QUERY, and the "plugin-style" is the point.
//
// It does NOT call s_ShellPlayerInfoManager. It goes through CreateInterface by
// name, which is the route utils/serverplugin_sample takes through
// gameServerFactory -- the ONLY route anything outside this DLL has, and the
// one step 0e established has no in-process caller at all. Calling the object
// directly would prove the object works and say nothing about whether the
// engine's factory can find it, which is the half that has never been exercised
// in this build: playerinfomanager.cpp is not in server_csharp.vpc, so
// PlayerInfoManager002 answered nothing here until this step.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_playerinfo )
{
	int nStatus = IFACE_OK;
	void *pFace = Sys_GetFactoryThis()( INTERFACEVERSION_PLAYERINFOMANAGER, &nStatus );

	if ( pFace == NULL || nStatus != IFACE_OK )
	{
		Msg( "[SourceSharp] playerinfo: the factory does not answer %s (status %d)\n",
		     INTERFACEVERSION_PLAYERINFOMANAGER, nStatus );
		return;
	}

	IPlayerInfoManager *pManager = static_cast< IPlayerInfoManager * >( pFace );

	Msg( "[SourceSharp] playerinfo: %s answered; globals %s\n",
	     INTERFACEVERSION_PLAYERINFOMANAGER,
	     ( pManager->GetGlobalVars() != NULL ) ? "yes" : "no" );

	int nFound = 0;

	for ( int i = 1; i <= gpGlobals->maxClients; i++ )
	{
		edict_t *pEdict = engine->PEntityOfEntIndex( i );

		if ( pEdict == NULL )
			continue;

		IPlayerInfo *pInfo = pManager->GetPlayerInfo( pEdict );

		if ( pInfo == NULL )
			continue;

		nFound++;

		const Vector vecOrigin = pInfo->GetAbsOrigin();
		const QAngle angEyes   = pInfo->GetAbsAngles();

		Msg( "  slot %d '%s' userid %d health %d frags %d deaths %d "
		     "dead %d hltv %d replay %d observer %d fake %d\n",
		     i, pInfo->GetName(), pInfo->GetUserID(), pInfo->GetHealth(),
		     pInfo->GetFragCount(), pInfo->GetDeathCount(),
		     pInfo->IsDead() ? 1 : 0, pInfo->IsHLTV() ? 1 : 0,
		     pInfo->IsReplay() ? 1 : 0, pInfo->IsObserver() ? 1 : 0,
		     pInfo->IsFakeClient() ? 1 : 0 );
		Msg( "         origin %.1f %.1f %.1f  angles %.1f %.1f %.1f  model '%s'\n",
		     vecOrigin.x, vecOrigin.y, vecOrigin.z,
		     angEyes.x, angEyes.y, angEyes.z, pInfo->GetModelName() );
	}

	Msg( "[SourceSharp] playerinfo: %d of %d slots answered; "
	     "%d refusals, %d orphan player states handed out\n",
	     nFound, gpGlobals->maxClients,
	     ShellPlayerInfo_Refusals().Total(),
	     ShellPlayerInfo_OrphanAnswers() );

	const CShellPlayerInfoRefusals &refusals = ShellPlayerInfo_Refusals();

	for ( int i = 0; i < refusals.MethodCount(); i++ )
	{
		Msg( "  refused %s x%d\n", refusals.MethodAt( i ), refusals.CountAt( i ) );
	}
}
