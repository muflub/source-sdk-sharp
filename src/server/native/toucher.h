//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// Who walked in, when not everything that can walk into something is a
// CBaseEntity.
//
// A touch has two ends. The RECEIVER — the trigger, the pickup, the volume being
// entered — is dispatched to already: gameinterface.cpp asks which hierarchy
// each side of an engine overlap report is on, and PhysicsOwner_t does the same
// for the solver's enter and leave events. What neither could do is name the
// entity that ARRIVED, because every way of handing one over is spelled
// CBaseEntity:
//
//     virtual void StartTouch( CBaseEntity *pOther );      // 53 of these
//     virtual void StartTouch( CBaseEntity *pOther ) = 0;  // and IGameEntity's
//
// So a managed prop entering a trigger reached the call site and was dropped
// there, in both directions and by both delivery paths. This is that argument,
// spelled as something both hierarchies can be.
//
// IT IS HALF THE BUG, and the smaller half. Nothing on the parallel hierarchy
// ever asked the engine which triggers it was standing in, so the overlap this
// could not carry was mostly never found in the first place — see
// CStandaloneEntity::TouchTriggers and ShouldTouchTrigger, which are the other
// half and were measured together. Either one alone still reports nothing,
// which is why neither was visible until the other was fixed.
//
// WHY A FIFTH TYPE OF THIS SHAPE, and specifically why not AnyEntity_t, which
// has the same three members and could be built here without adding a line to
// it. Because the fields are not what a type of this shape IS. The other four
// all exist so that whoever HOLDS one can ask it something — what a blast asks
// a victim, what the physics bridge asks an owner, what a trigger asks a
// filter. Nobody asks a toucher anything: it is picked up at the point the
// engine or the solver reports an overlap and put down again inside somebody
// else's StartTouch, unread in between.
//
// That difference decides where the next work lands, which is the real reason
// it matters. Converting CBaseTrigger — the step this deliberately does not
// take — needs a toucher to answer IsPlayer, GetFlags, GetMoveType and
// GetCollisionGroup, because PassesTriggerFilters asks all four. Those belong
// on the argument a trigger was handed. Putting them on AnyEntity_t instead
// would be growing "what a radius sweep asks a victim" into something else
// entirely, which is the smuggling its own header asks nobody to do. So the
// questions have a place to arrive, and it is here.
//
// WHAT IS DELIBERATELY NOT HERE, today, is any of those questions. No site
// converted in this pass ASKS a toucher anything — they carry it, and the one
// that needs the SDK entity says so by naming pBase, which stays public for
// exactly that. An accessor written for a caller that does not exist yet is an
// answer nobody has checked against a live game.
//
// pHandle is kept for the same reason AnyEntity_t keeps one: it is the only
// identity the two kinds share, and a touch RELATIONSHIP outlives the frame it
// was reported in. A link stores the handle and rebuilds the pair next frame,
// which is the same per-lookup discipline PhysicsOwner_t insists on and not an
// exception to it.
//
// Server only, like anyentity.h and for the same reason: the client half of the
// parallel hierarchy does not exist, so over there a toucher is a C_BaseEntity
// and nothing else can be one.
//
//=============================================================================//
#ifndef TOUCHER_H
#define TOUCHER_H
#ifdef _WIN32
#pragma once
#endif

#include "basehandle.h"
#include "ihandleentity.h"
#include "igameentity.h"
#include "shell_resolve.h"

//-----------------------------------------------------------------------------
// The entity that arrived, on whichever hierarchy holds it.
//
// A value type built per report and dropped again, exactly as PhysicsOwner_t
// describes. Neither pointer survives being stored; the handle is what does.
//-----------------------------------------------------------------------------
struct Toucher_t
{
	IHandleEntity *pHandle;

	// The SDK entity, and what every unconverted StartTouch override already
	// takes. NULL for the other kind.
	IGameEntity *pBase;

	// The entity outside the SDK. NULL for a CBaseEntity.
	IGameEntity *pGame;

	bool IsValid() const { return pBase != NULL || pGame != NULL; }

	// What a link stores. Both hierarchies are in the same handle table under
	// the same numbering — a standalone entity is not a second kind of thing
	// with a second one — so this is one value that names either.
	//
	// NOT an EHANDLE. CHandle<CBaseEntity>::Get() reinterpret-casts whatever
	// the table returns, so a handle secretly naming the other kind hands its
	// reader a pointer of the wrong type: the failure entityfilter.h described
	// (since deleted), a crash where the old code had a NULL.
	CBaseHandle GetRefEHandle() const
	{
		return pHandle ? pHandle->GetRefEHandle() : CBaseHandle();
	}
};

//-----------------------------------------------------------------------------
// The toucher a caller already has.
//
// No lookup: a site that holds a CBaseEntity knows what it holds, and every
// existing touch site is one of those.
//
// Declared here, DEFINED IN toucher.cpp. Its body is the only line in this
// header that needs a CBaseEntity to be an IHandleEntity, and this header is
// read by 26 of the server's translation units. toucher.cpp says why that
// matters and what the typedef does to it.
//-----------------------------------------------------------------------------
Toucher_t ToucherOf( IGameEntity *pEntity );

//-----------------------------------------------------------------------------
// The toucher behind something the engine handed over, which is where the two
// kinds are still mixed: an edict, a partition element, a trace result.
//
// The SDK's own lookup goes first and the second is only reached when it
// answers NULL, so a CBaseEntity costs exactly what it cost before. The same
// two-step AnyEntityOf makes, written again rather than borrowed, because
// borrowing it would mean this header depends on what a radius sweep decided.
//-----------------------------------------------------------------------------
inline Toucher_t ToucherFromEntityHandle( IHandleEntity *pHandleEntity )
{
	Toucher_t toucher = { pHandleEntity, NULL, NULL };

	if ( !pHandleEntity )
		return toucher;

	// pBase is always NULL here for the reason anyentity.h's AnyEntityOf gives:
	// every GetBaseEntity() in server.so returns a constant NULL.
	toucher.pBase = NULL;

	toucher.pGame = SourceSharp_GameEntityFromHandleEntity( pHandleEntity );

	return toucher;
}

//-----------------------------------------------------------------------------
// The toucher a link remembered.
//
// Rebuilt rather than kept, so an entity removed while it was standing inside
// something resolves to nothing here and the link is dropped — which is what
// every EHANDLE reader in the SDK already does with a stale handle.
//-----------------------------------------------------------------------------
inline Toucher_t ToucherFromHandle( const CBaseHandle &handle )
{
	// CBaseHandle::Get() -- the handle's own read, and the one every typed
	// handle in the tree already performs. It replaces a LookupEntity call on
	// the game's entity list,
	// which was the SAME table reached by naming the game's list object, and
	// naming it cost this header an `#include "entitylist.h"` that 26 server
	// translation units then paid for. plan_entity.md 3.3's rule is that the
	// shim holds handles and resolves on use; nothing here needs the LIST.
	return ToucherFromEntityHandle( handle.Get() );
}

#endif // TOUCHER_H
