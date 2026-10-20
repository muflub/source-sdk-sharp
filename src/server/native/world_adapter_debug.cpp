//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — world_adapter.cpp's diagnostic console commands ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching rows under #if DEBUG.
//
//=============================================================================//

#include "ss_world.h"           // CWorld, GetWorldEntity

#include "native_command_body.h"

//-----------------------------------------------------------------------------
// The world's networked state, ON DEMAND.
//
// CWorld::Spawn prints it once per level load, which is the evidence that the
// map's keys landed. This is the other question: what does the world hold NOW.
// They are different questions the moment anything writes a key at runtime --
// `ent_fire worldspawn AddOutput "detailmaterial x"` reaches CWorld::KeyValue
// exactly as the map parse does -- and the load-time line cannot answer it.
//
// The client's half of this needs no command: C_World::OnDataChanged prints
// whenever the ten values CHANGE, which is once at the create and then only
// when something moved them. That asymmetry is not an oversight -- a console
// command delivered through engine->ServerCommand reaches a SERVER concommand
// and not a client one (docs/gamedll-ledger.md Phase A6 row 1, measured three
// ways in one process), so a cs_cl_ command here would be a gate nobody could
// run headlessly.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_world )
{
	CWorld *pWorld = GetWorldEntity();

	if ( pWorld == NULL )
	{
		Msg( "[SourceSharp] no world entity -- is a map loaded?\n" );
		return;
	}

	pWorld->ReportNetState();
}
