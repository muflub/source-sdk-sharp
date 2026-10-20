//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — relays for SDK callers that have no shell ==========//
// SS_REASON a  a vtable the engine calls through
//
// The definitions behind standalone_relays.h, and the ONE place the crossing
// from IGameEntity down to CStandaloneEntity is spelled.
//
// The cast is sound for the reason StandaloneFilterAnswer recorded when it
// made the same one: IGameEntity deliberately does not redeclare the shell's
// members, and every IGameEntity in this build is a CStandaloneEntity — the
// class list in shell_standalone.h is the whole hierarchy. A second
// implementor would make every one of these casts wrong, which is why the
// interface's header says there must not be one.
//
// Every function here forwards and returns; nothing decides. The decisions —
// what a key means, how much damage lands, what an input does — are already
// on the other side of OnManaged*, in C#.
//
//=============================================================================//

#include "vphysics_interface.h"
#include "standalone_relays.h"
#include "shell_standalone.h"
#include "shell_standalone_player.h"   // SourceSharp_PlayerOf, MvGetMoveType
#include "standalone_movetype.h"
#include "shell_resolve.h"           // SourceSharp_ResolveEntity
#include "managed_runtime_server.h"
#include "shellless_find.h"
#include "game_entity_relays.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E9: IManagedEntities

// memdbgon must be the last include file in a .cpp file!!!
#include "sourcesharp.h"

//-----------------------------------------------------------------------------
// The crossing, stated once. Callers guarantee non-NULL the same way they did
// when these were accessors on the entity itself: every site tests the pointer
// it resolved before asking anything of it.
//-----------------------------------------------------------------------------
// PROMOTED TO shell_standalone.h, where the same conversion now happens for the
// whole server instead of fifteen times in eight files. These two forwards keep
// the local spelling every relay in this file already uses.
static CStandaloneEntity *ShellOf( IGameEntity *pEnt )
{
	return SourceSharp_ShellOf( pEnt );
}

static const CStandaloneEntity *ShellOf( const IGameEntity *pEnt )
{
	return SourceSharp_ShellOf( pEnt );
}

//=============================================================================//
// I/O.
//=============================================================================//

const char *StandaloneEntity_Name( const IGameEntity *pEnt )
{
	return ShellOf( pEnt )->GetEntityName();
}

//=============================================================================//
// The same I/O, for an entity that has no C++ object at all -- plan_entity.md
// step 8e-entfire.
//
// standalone_relays.h carries the argument for why these exist. What is worth
// saying here is what they are NOT: they are not a second entity list. Nothing
// native holds a shell-less entity, and nothing native is going to -- the
// managed directory is the index, and these ask it. The handles that come back
// live exactly as long as the loop that uses them, which is why they are read
// into a local array and never stored.
//=============================================================================//

//=============================================================================//
// Damage.
//=============================================================================//

int StandaloneEntity_TakeDamage( IGameEntity *pEnt, const ss::CTakeDamageInfo &info )
{
	return ShellOf( pEnt )->TakeDamage( info );
}

bool StandaloneEntity_IsPlayer( const IGameEntity *pEnt )
{
	return ShellOf( pEnt )->IsPlayer();
}

int StandaloneEntity_MoveType( const IGameEntity *pEnt )
{
	// SourceSharp_PlayerOf takes a non-const shell; nothing here writes through it.
	CStandaloneEntity *pShell = ShellOf( const_cast< IGameEntity * >( pEnt ) );
	CStandalonePlayer *pPlayer = pShell->IsPlayer() ? SourceSharp_PlayerOf( pShell ) : NULL;

	return StandaloneMoveType_Report( pPlayer != NULL,
	                                  pPlayer ? (int)pPlayer->MvGetMoveType() : 0,
	                                  pShell->GetPhysics() != NULL,
	                                  pShell->Block().Head.MoveType );
}

IPhysicsObject *StandaloneEntity_PhysicsObject( const IGameEntity *pEnt )
{
	return ShellOf( pEnt )->GetPhysics();
}

//-----------------------------------------------------------------------------
// game_entity_relays.h's server half. The shell answers it on IServerEntity,
// which is the face igameentity.h points at for this question.
//-----------------------------------------------------------------------------
int GameEntity_ModelIndex( const IGameEntity *pEnt )
{
	return pEnt ? ShellOf( pEnt )->GetModelIndex() : 0;
}

CBaseHandle GameEntity_RefHandle( const IGameEntity *pEnt )
{
	if ( !pEnt )
		return CBaseHandle();

	// ShellOf's cast, then the face that already answers. The conversion from
	// the shell to IHandleEntity is the compiler's, performed with the whole
	// layout visible, which is the one thing a (T*) cast from an IGameEntity *
	// could never be.
	return ShellOf( pEnt )->GetRefEHandle();
}

IHandleEntity *StandaloneEntity_OwnerHandle( const IGameEntity *pEnt )
{
	return ShellOf( pEnt )->OwnerHandleEntity();
}

//=============================================================================//
// Map placement.
//=============================================================================//

bool StandaloneEntity_ApplyMapKey( IGameEntity *pEnt, const char *szKeyName,
                                   const char *szValue )
{
	return ShellOf( pEnt )->KeyValue( szKeyName, szValue );
}

void StandaloneEntity_Place( IGameEntity *pEnt, const Vector *pOrigin,
                             const QAngle *pAngles )
{
	CStandaloneEntity *pShell = ShellOf( pEnt );

	if ( pOrigin )
		pShell->SetAbsOrigin( *pOrigin );

	if ( pAngles )
		pShell->SetAbsAngles( *pAngles );
}

void StandaloneEntity_PlaceAndSpawn( IGameEntity *pEnt, const Vector *pOrigin,
                                     const QAngle *pAngles )
{
	StandaloneEntity_Place( pEnt, pOrigin, pAngles );

	ShellOf( pEnt )->Spawn();
}

//=============================================================================//
// Movement.
//=============================================================================//

void StandaloneEntity_HoldMoveVelocity( IGameEntity *pEnt, const Vector &vecVelocity )
{
	ShellOf( pEnt )->HoldMoveVelocity( vecVelocity );
}

Vector StandaloneEntity_Velocity( const IGameEntity *pEnt )
{
	return ShellOf( pEnt )->MoveVelocity();
}
