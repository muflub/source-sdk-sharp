//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

// Entity op relays: native entity events to managed, and managed ops back to native.
// Each relay first tests the class's override bit, so a class that does not
// override an op costs no crossing for it.

#include "shell_standalone_player.h"
#include "entity_op.h"
#include "slot_table.h"

#include "imanaged_areas.h"
#include "server_entity_context.h"
#include "shell_managed_areas.h"
#include "ss_takedamageinfo.h"
#include "shell_entity_lifecycle.h"
#include "server_network.h"
#include "server_entity_resolver.h"
uint32_t SourceSharp_HandleFromEntity( IGameEntity *pEntity );

// hOther zeroed would be a handle to the world, so "no entity" is set explicitly.
static SourceSharpOpArgs EmptyOpArgs()
{
	SourceSharpOpArgs args;

	memset( &args, 0, sizeof( args ) );
	args.hOther = (uint32_t)INVALID_EHANDLE_INDEX;

	return args;
}

// Attaches the entity's blob, so managed does not need a round trip for it.
static int DispatchOp( CStandaloneEntity *pEntity, int nOp, SourceSharpOpArgs &args )
{
	IManagedEntities *pApi = pEntity->Context().m_Managed.m_Areas.m_pEntities;

	if ( !pApi )
		return 0;

	args.pBlob = pEntity->DataBlob();
	args.nBlobBytes = pEntity->BlobBytes();

	return pApi->Dispatch( pEntity->ManagedHandle(), nOp, &args );
}

// Spawn and KeyValue carry the classname, since they arrive before managed has an
// instance. Neither can be switched off by the override bit (ENTITY_OP_ALWAYS_MASK).
void CStandaloneEntity::OnManagedSpawn()
{
	if ( !ClassOverridesOp( ENTITY_OP_SPAWN ) )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();
	args.pText0 = TypeName();

	DispatchOp( this, ENTITY_OP_SPAWN, args );
}

bool CStandaloneEntity::OnManagedKeyValue( const char *szKeyName, const char *szValue )
{
	if ( !ClassOverridesOp( ENTITY_OP_KEY_VALUE ) )
		return false;

	SourceSharpOpArgs args = EmptyOpArgs();
	args.pText0 = TypeName();
	args.pText1 = szKeyName;
	args.pText2 = szValue;

	return DispatchOp( this, ENTITY_OP_KEY_VALUE, args ) != 0;
}

// A projectile ran into something. pOther may be NULL; it crosses as an invalid handle.
void CStandaloneEntity::OnManagedImpact( IHandleEntity *pOther )
{
	if ( !ClassOverridesOp( ENTITY_OP_IMPACT ) )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();
	args.hOther = SourceSharp_HandleFromAnyEntity( pOther );

	DispatchOp( this, ENTITY_OP_IMPACT, args );
}

// It arrived, or met something it could not move through.
void CStandaloneEntity::OnManagedMoveDone()
{
	if ( !ClassOverridesOp( ENTITY_OP_MOVE_DONE ) )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();

	DispatchOp( this, ENTITY_OP_MOVE_DONE, args );
}

void CStandaloneEntity::OnManagedBlocked( IHandleEntity *pOther )
{
	if ( !ClassOverridesOp( ENTITY_OP_BLOCKED ) )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();
	args.hOther = SourceSharp_HandleFromAnyEntity( pOther );

	DispatchOp( this, ENTITY_OP_BLOCKED, args );
}

// Native is freeing a shell (client disconnect), so managed cannot learn of it
// from its own removal pass. Call before the delete: the op reads the entity's
// handle and blob.
void SourceSharp_AnnounceStandaloneRemoved( CStandaloneEntity *pEntity )
{
	if ( !pEntity )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();

	DispatchOp( pEntity, ENTITY_OP_REMOVED, args );
}

// Damage is forwarded whole; managed decides how much lands.
int CStandaloneEntity::OnManagedDamage( const ss::CTakeDamageInfo &info )
{
	if ( !ClassOverridesOp( ENTITY_OP_DAMAGE ) )
		return 0;

	// Both zero for damage with no place, such as trigger_hurt.
	const Vector &vecPos = info.GetDamagePosition();
	const Vector &vecForce = info.GetDamageForce();

	SourceSharpOpArgs args = EmptyOpArgs();
	args.hOther = SourceSharp_HandleFromEntity( info.GetAttacker( Context().Resolver() ) );
	args.flFloat0 = info.GetDamage();
	args.nInt0 = info.GetDamageType();
	args.pVec0 = vecPos.Base();
	args.pVec1 = vecForce.Base();

	return DispatchOp( this, ENTITY_OP_DAMAGE, args );
}

void CStandaloneEntity::OnManagedKilled( IGameEntity *pAttacker )
{
	if ( !ClassOverridesOp( ENTITY_OP_KILLED ) )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();
	args.hOther = SourceSharp_HandleFromEntity( pAttacker );

	DispatchOp( this, ENTITY_OP_KILLED, args );
}

// Managed ops sent to a native shell. For ENTITY_OP_POSITION_CHANGED, pVec0 is
// the previous origin; NULL means it did not move, which fires no trigger edges.
int32_t CServerEntityLifecycle::EntityDispatch( uint32_t hEntity, int32_t nOp, const SourceSharpOpArgs *pArgs )
{
	CStandaloneEntity *pShell = m_Entities.Resolver().ShellFromHandle( CBaseHandle::UnsafeFromIndex( (int)hEntity ) );

	if ( !pShell )
		return 0;

	switch ( nOp )
	{
		case ENTITY_OP_POSITION_CHANGED:
		{
			const Vector vecPrevOrigin = ( pArgs && pArgs->pVec0 )
				? Vector( pArgs->pVec0[0], pArgs->pVec0[1], pArgs->pVec0[2] )
				: pShell->Block().Head.Origin;

			pShell->OnPositionChanged( vecPrevOrigin );
			return 1;
		}

		default:
			// An op this build does not know: a newer managed side. Nothing happened.
			return 0;
	}
}

// Marks one engine-block field changed. nBlockOffset is relative to the block.
// A handle not on this hierarchy is a no-op.
void CServerNetwork::EntityMarkBlockChanged( uint32_t hEntity, int32_t nBlockOffset )
{
	CStandaloneEntity *pShell = m_Entities.ShellFromHandle( CBaseHandle::UnsafeFromIndex( (int)hEntity ) );

	if ( !pShell )
		return;

	pShell->MarkBlockChanged( nBlockOffset );
}
