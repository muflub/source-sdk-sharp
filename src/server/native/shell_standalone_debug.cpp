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
#include "ss_mapentities.h"   // MapEntity_CreateFromKeys, MapEntity_SpawnPendingNonBaseEntities
#include "native_command_body.h"
#include "ss_string.h"

#include <algorithm>
#include <vector>

IServerNetworkable *CreateNetworkableEntityByName( const char *className );
std::vector< CStandaloneEntity * > &StandaloneEntities();

//-----------------------------------------------------------------------------
// A standalone entity by the name the map gave it.
//
// There is no index to ask: the entity list is IHandleEntity-typed and its
// FindEntityByName walks CBaseEntity, which correctly skips every entity on this
// hierarchy. A linear walk of a few hundred is the right cost for a console
// command and for a once-per-level resolve pass, and nothing else needs it.
//-----------------------------------------------------------------------------
static CStandaloneEntity *StandaloneByName( const char *pName )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	// The first in list order, as the walk it replaced answered.
	auto it = std::find_if( standalones.begin(), standalones.end(),
		[pName]( CStandaloneEntity *pEnt ) { return !ss::StrICmp( pEnt->GetEntityName(), pName ); } );

	return ( it != standalones.end() ) ? *it : NULL;
}

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
		static_cast< IServerEntity * >( pNet->GetEntityHandle() )->GetGameEntity() );

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

//-----------------------------------------------------------------------------
// Parenting, by hand.
//
// Both by NAME, because that is what a map writes and because the console is
// where the awkward cases get tried: parenting to something that does not exist,
// to a CBaseEntity, to an entity's own child. Each of those has a message rather
// than an assert, and this is how they are reached without editing a map.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_standalone_parent )
{
	if ( args.ArgC() < 2 )
	{
		Msg( "usage: cs_standalone_parent <child> [parent]   (no parent unparents)\n" );
		return;
	}

	CStandaloneEntity *pChild = StandaloneByName( args[1] );

	if ( !pChild )
	{
		Warning( "[SourceSharp] no standalone entity called '%s'\n", args[1] );
		return;
	}

	if ( args.ArgC() < 3 )
	{
		StandaloneHierarchy_ClearParent( pChild );
		Msg( "[SourceSharp] '%s' has no parent now\n", args[1] );
		return;
	}

	CStandaloneEntity *pParent = StandaloneByName( args[2] );

	if ( !pParent )
	{
		// ONE MESSAGE NOW, AND THE SECOND ONE WAS NOT LOST -- it stopped being
		// a distinct case. The arm removed here asked the global entity list's
		// own FindEntityByName
		// so that "named, but by the other hierarchy" could be reported
		// separately from "no such name". plan_entity.md 3.3 moves every
		// find-by-name to the managed directory, which knows only the entities
		// it drives, so there is no second search left to tell the two apart
		// with and a name the directory does not hold is simply not a name.
		Warning( "[SourceSharp] no standalone entity called '%s'\n", args[2] );
		return;
	}

	if ( StandaloneHierarchy_SetParent( pChild, pParent ) )
		Msg( "[SourceSharp] '%s' now follows '%s'\n", args[1], args[2] );
}

SS_NATIVE_COMMAND( cs_standalone_mapspawn )
{
	// One map block's pairs, ALREADY PARSED (plan_server_novalve.md P5: native
	// reads no map text). Block order, classname included, as the map loader's
	// walk would hand them over.
	static const char *const kKeys[] =
		{ "classname", "targetname", "origin", "health",
		  // An output aimed at one of the map's OWN entities, which is the point:
		  // a standalone entity firing at a map entity through the ordinary event
		  // queue, with nothing in between knowing the difference.
		  "OnDestroyed", "hammerid" };
	static const char *const kValues[] =
		{ "ss_barrel", "ss_test_target", "-3000 3200 100", "250",
		  "showers_gate3,Open,,0,-1", "12345" };

	Msg( "[SourceSharp] map: creating a block's entity the way the map loader would\n" );

	IGameEntity *pEntity = MapEntity_CreateFromKeys( kValues[ 0 ], kKeys, kValues,
	                                                 (int)ARRAYSIZE( kKeys ) );

	Msg( "[SourceSharp] map: %s\n",
		pEntity ? "created and queued a standalone ss_barrel" : "no registered class answered ss_barrel" );

	// The map loader calls this after its ordered spawn pass. Only one block is
	// being created here, so it is called directly — same function, same moment
	// in the sequence.
	MapEntity_SpawnPendingNonBaseEntities();
}

//-----------------------------------------------------------------------------
// The server's half of the viewmodel meter: what each carried-pair entity is
// actually wearing HERE, with the transmit facts the client's dormancy depends
// on. cs_cl_viewmodel_report answers for the client's copy; when the two
// disagree, this is the side that says which one moved.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_viewmodel_report )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();
	int nFound = 0;

	for ( CStandaloneEntity *pEnt : standalones )
	{
		const char *pClass = pEnt->GetClassname();

		if ( ss::StrICmp( pClass, "ss_weapon" ) && ss::StrICmp( pClass, "ss_viewmodel" ) )
			continue;

		nFound++;

		Vector vecMins, vecMaxs;
		pEnt->WorldSpaceSurroundingBounds( &vecMins, &vecMaxs );

		edict_t *pEdict = pEnt->GetEdict();

		Msg( "[SourceSharp] server %s: entity %d, model '%s' (index %d), origin (%.1f %.1f %.1f), "
		     "pvs box (%.1f %.1f %.1f)-(%.1f %.1f %.1f), edict flags 0x%x\n",
			pClass,
			pEnt->EntIndex(),
			pEnt->GetModelNameString(),
			pEnt->GetModelIndex(),
			pEnt->GetCollisionOrigin().x, pEnt->GetCollisionOrigin().y, pEnt->GetCollisionOrigin().z,
			vecMins.x, vecMins.y, vecMins.z,
			vecMaxs.x, vecMaxs.y, vecMaxs.z,
			pEdict ? pEdict->m_fStateFlags : 0 );
	}

	if ( !nFound )
		Msg( "[SourceSharp] server viewmodel report: no ss_weapon or ss_viewmodel exists\n" );
}
