//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_standalone.cpp's diagnostic console commands ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching rows under #if DEBUG.
//
//=============================================================================//

#include "edict.h"            // edict_t::m_fStateFlags
#include "shell_standalone.h"
#include "tier1/convar.h"     // CCommand
#include "native_command_body.h"
#include "ss_string.h"

#include <algorithm>
#include <vector>
#include "ss_entityhandle.h"   // ss::GameEntityFromEntityHandle: the one read of a handle's entity face

IServerNetworkable *CreateNetworkableEntityByName( const char *className );
std::vector< CStandaloneEntity * > &StandaloneEntities();

//-----------------------------------------------------------------------------
// Spawns one at a position, which is what makes the client half testable: an
// entity outside the PVS is never transmitted at all.
//-----------------------------------------------------------------------------
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

	CStandaloneEntity *pEnt = SourceSharp_ShellOf(
		ss::GameEntityFromEntityHandle( pNet->GetEntityHandle() ) );

	if ( !pEnt )
		return;

	char szOrigin[ 128 ];
	ss::Format( szOrigin, sizeof( szOrigin ), "%s %s %s", args[1], args[2], args[3] );

	// Through KeyValue rather than by assignment, so this takes the same path a
	// map does — an entity spawned by hand that skipped the keyvalues would be
	// testing something the map never does.
	pEnt->KeyValue( "origin", szOrigin );

	// A model only if one was ASKED for, which is the point: with no key the
	// managed class supplies its own from OnSpawn, and that path is the one
	// worth exercising by default. Passing one here tests the other path — a
	// map overriding what the class would have chosen.
	if ( args.ArgC() >= 6 )
		pEnt->KeyValue( "model", args[5] );

	pEnt->Spawn();
}

