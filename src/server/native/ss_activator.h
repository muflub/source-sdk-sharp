//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// Who caused this, when not everything that can cause something is a
// CBaseEntity.
//
// Entity I/O has three names in it. The TARGET is dispatched to already —
// CEventQueue::ServiceEvents asks gEntList for both hierarchies and calls
// AcceptInput on whichever kind it found. The CALLER is the entity that owns the
// output. The ACTIVATOR is the one this file is about: the entity whose action
// set the whole chain going, carried unchanged from output to input to the next
// output, which is how a trigger tells a game_text WHICH player walked in.
//
// Every way of carrying one was spelled CBaseEntity, from the fire site to the
// delivery:
//
//     void FireOutput( variant_t, CBaseEntity *pActivator, CBaseEntity *pCaller, float );
//     void AddEvent( const char *, const char *, variant_t, float,
//                    CBaseEntity *pActivator, CBaseEntity *pCaller, int );
//     EHANDLE m_pActivator;                                    // held between the two
//
// So a managed entity reached each of those and was dropped there. Measured on
// the sandbox's turntable: func_trackautochange splits entirely on whether a
// func_tracktrain fired it, both branches are ported and correct, and the train
// branch was unreachable — the platform never moved, because the activator that
// would have named the train arrived as nothing.
//
// WHAT THIS DOES NOT DO, and the omission is the whole reason it is safe. The
// CBaseEntity side of entity I/O is not widened by one byte. Every existing
// reader still holds `inputdata.pActivator`, still typed CBaseEntity *, and for
// an activator on the other hierarchy it reads NULL — which is exactly what it
// read before this file existed, so no reader gains a case it has not already
// been running since 2004. COUNTED, because the size of that set is the whole
// argument: 165 sites read `.pActivator`, of which 11 dereference it, of which
// 10 are already NULL-guarded and the eleventh is an HL2 NPC behaviour this mod
// does not compile a path to. Nothing on that list changes.
//
// The reach is one-directional on purpose: an entity outside the SDK can now BE
// an activator, and can be told about one. It cannot be handed to a CBaseEntity
// input as one, because handing it over means a CBaseEntity * and there is no
// honest value for that field.
//
// WHY NOT AN EHANDLE for the stored half. CHandle<CBaseEntity>::Get()
// reinterpret-casts whatever the handle table returns, and both hierarchies
// share one table and one numbering — so an EHANDLE secretly naming the other
// kind hands its reader a pointer of the wrong type: the failure entityfilter.h
// described (that header is gone now), a crash where the old code had a NULL. The event queue therefore
// keeps its EHANDLE for the SDK half exactly as it was, and a bare CBaseHandle
// beside it for the other. Two fields, neither of which can lie about what it
// names.
//
// WHY A SIXTH TYPE OF THIS SHAPE. toucher.h asks this question of itself and
// answers it by what the holder ASKS: nobody asks a toucher anything, so its
// distinguishing property is where it is picked up and put down rather than its
// fields. The same test applied here gives a different answer, and it is the
// LIFETIME. Every other type of this shape is built per lookup from something
// live and dropped in the same call — physicsowner.h insists on that, and
// toucher.h and anyentity.h each repeat it (entityfilter.h did, until it went). An activator is the
// one that must be PUT DOWN AND PICKED UP AGAIN by something that is neither
// hierarchy's: the event queue holds it across an arbitrary delay and rebuilds
// it at delivery from a handle. So this is the only one of the six with a
// storage format as part of its contract, and that is what it is for. Reusing
// Toucher_t here would mean an output's activator arriving typed as somebody's
// StartTouch argument, and the next person to add a question to either would
// have to answer it for both.
//
// A REGISTRY RATHER THAN A TAGGED POINTER, like all five of its relatives: the
// game half is resolved through gEntList, so a site that has not been converted
// degrades to NULL rather than to a wild pointer, and converting one stays a
// safe independent step.
//
// Server only, like anyentity.h and toucher.h and for the same reason: entity
// I/O is a server mechanism and the client half of the parallel hierarchy does
// not exist.
//
//=============================================================================//
#ifndef SOURCESHARP_SS_ACTIVATOR_H
#define SOURCESHARP_SS_ACTIVATOR_H
#ifdef _WIN32
#pragma once
#endif

#include "basehandle.h"
#include "ihandleentity.h"

// Only the template constructor names CHandle, and it is instantiated at the
// call site, where ehandle.h is in scope. Declared, so that this header needs
// nothing outside public/ (plan_server_novalve.md P5).
template < class T > class CHandle;

#include "igameentity.h"
class IGameEntity;

//-----------------------------------------------------------------------------
// The entity that caused this, on whichever hierarchy holds it.
//
// DELIBERATELY LIGHT. This header is reached from entityoutput.h, which
// baseentity.h includes from inside its own guard — so CBaseEntity is an
// incomplete type here and everything inline below has to be true without it.
// That is why the conversion is a pointer copy with no upcast, and why the
// resolution helpers are declared rather than defined.
//-----------------------------------------------------------------------------
struct Activator_t
{
	// The SDK entity, and what every unconverted reader of an activator already
	// takes. NULL for the other kind, which is the degrade this whole file
	// rests on.
	IGameEntity *pBase;

	// The entity outside the SDK. NULL for a CBaseEntity.
	IGameEntity *pGame;

	// The game half's identity, which only the concrete type can supply:
	// IGameEntity is not an IHandleEntity and the two are reached by different
	// casts. Set with pGame and never alone.
	IHandleEntity *pGameHandle;

	Activator_t() : pBase( NULL ), pGame( NULL ), pGameHandle( NULL ) {}

	// IMPLICIT, and that is the reason the widened signatures cost no call-site
	// churn: every one of the 731 existing FireOutput calls passes a
	// CBaseEntity * and keeps compiling and keeps meaning what it meant.
	Activator_t( IGameEntity *pEntity )
		: pBase( pEntity ), pGame( NULL ), pGameHandle( NULL ) {}

	// The same, for the twenty-odd sites that keep their activator in a handle
	// and hand it straight to FireOutput. Without this they would each need a
	// .Get(): an EHANDLE reaches CBaseEntity * through a conversion operator,
	// and two user-defined conversions in a row is not a thing C++ will do.
	// A template so it needs no complete type here and gets one at every call.
	template < class T >
	Activator_t( const CHandle< T > &handle )
		: pBase( handle.Get() ), pGame( NULL ), pGameHandle( NULL ) {}

	Activator_t( IGameEntity *pEntity, IHandleEntity *pHandle )
		: pBase( NULL ), pGame( pEntity ), pGameHandle( pHandle ) {}

	bool IsValid() const { return pBase != NULL || pGame != NULL; }

	// What the game half is stored AS. Empty for an SDK activator, whose
	// identity the event queue's own EHANDLE already holds, and empty for no
	// activator at all — the two are the same to a reader of this.
	CBaseHandle GetGameRefEHandle() const
	{
		return pGameHandle ? pGameHandle->GetRefEHandle() : CBaseHandle();
	}
};

//-----------------------------------------------------------------------------
// The game half an event remembered, rebuilt rather than kept.
//
// Defined in cbase.cpp: the lookup is gEntList's and entitylist.h cannot be
// reached from here. An entity removed while an event naming it was in flight
// resolves to nothing, which is what every EHANDLE reader in the SDK already
// does with a stale handle.
//-----------------------------------------------------------------------------
Activator_t ActivatorFromGameHandle( const CBaseHandle &handle );

//-----------------------------------------------------------------------------
// The two halves an event stored, back together.
//
// The SDK half is the EHANDLE the queue always kept, so an event with an
// ordinary activator costs one null-handle test more than it did.
//-----------------------------------------------------------------------------
Activator_t ActivatorOf( IGameEntity *pBase, const CBaseHandle &hGame );

#endif // SOURCESHARP_SS_ACTIVATOR_H
