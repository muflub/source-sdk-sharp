//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Debug builds only; NativeCommands declares the matching rows under #if DEBUG.

#include "edict.h"
#include "shell_standalone.h"
#include "tier1/convar.h"
#include "native_command_body.h"
#include "ss_string.h"

#include "ss_entityhandle.h"

IServerNetworkable *CreateNetworkableEntityByName( const char *className );
std::vector<CStandaloneEntity *> &StandaloneEntities();

// Spawns one at a position (an entity outside the PVS is never transmitted).
SS_NATIVE_COMMAND( cs_standalone_spawn )
{
	if ( args.ArgC() < 4 )
	{
		Msg( "usage: cs_standalone_spawn <x> <y> <z> [classname] [model]\n" );
		return;
	}

	const char *pClassName = ( args.ArgC() >= 5 ) ? args[4] : "ss_barrel";

	IServerNetworkable *pNet = CreateNetworkableEntityByName( pClassName );
	if ( !pNet )
	{
		Warning( "[SourceSharp] no factory for '%s'\n", pClassName );
		return;
	}

	CStandaloneEntity *pEnt = SourceSharp_ShellOf( ss::GameEntityFromEntityHandle( pNet->GetEntityHandle() ) );

	if ( !pEnt )
		return;

	char szOrigin[128];
	ss::Format( szOrigin, sizeof( szOrigin ), "%s %s %s", args[1], args[2], args[3] );

	// Through KeyValue, the path a map takes.
	pEnt->KeyValue( "origin", szOrigin );

	// Without a model key the managed class supplies its own in OnSpawn.
	if ( args.ArgC() >= 6 )
		pEnt->KeyValue( "model", args[5] );

	pEnt->Spawn();
}
