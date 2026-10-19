//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// What game code needs from an entity that is NOT a CBaseEntity.
//
// The SDK reaches every entity through EntityFromEntityHandle, which returns a
// CBaseEntity * and NULL for anything else. One line in
// CTraceFilterSimple::ShouldHitEntity turns that NULL into "do not hit me":
//
//     CBaseEntity *pEntity = EntityFromEntityHandle( pHandleEntity );
//     if ( !pEntity )
//         return false;
//
// The engine has no such problem. It traces against anything in the spatial
// partition, because the partition holds IHandleEntity and asks ICollideable
// for the geometry. Measured: the same ray misses through UTIL_TraceLine and
// hits through enginetrace->TraceRay. So the entity is in the world and the
// SDK's filter is what cannot see it.
//
// This is the smallest thing that fixes that. It is deliberately NOT a
// CBaseEntity replacement — it holds exactly what the filter asks an entity
// for, and it grows only when something else is measured to need it.
//
// CBaseEntity does not implement it and does not have to. The filter tries the
// CBaseEntity path first, exactly as before, and only asks for this when that
// answers NULL — so nothing about a stock entity's layout, vtable or code path
// changes.
//
//=============================================================================//
#ifndef IGAMEENTITY_H
#define IGAMEENTITY_H
#ifdef _WIN32
#pragma once
#endif

#include "mathlib/vector.h"

class IPhysicsObject;

// Who walked into something, on whichever hierarchy holds them. Declared rather
// than included: a reference argument needs no definition, and the definition
// lives in a server header this one must stay above.
struct Toucher_t;

//-----------------------------------------------------------------------------
// An entity game code can reason about without knowing what it is.
//
// ON THE SERVER, BUILT ON STOCK PUBLIC HEADERS, ITS NAME IS CBaseEntity
// (plan_server_native_v2 D1, user ruling 2026-10-18: "can alias the two").
// Stock iserverunknown.h, iservernetworkable.h, eiface.h and gametrace.h
// name the game's entity CBaseEntity and only forward-declare it; server.so
// has no other class of that name, so this one is it, and IGameEntity is the
// same type under the name the rest of the tree spells. The client keeps
// IGameEntity: its headers are the patched ones, and the alias is per side.
//
// SS_STOCK_PUBLIC_HEADERS marks the stock build. Until D3 points server.so's
// include root at the pristine tree, only tools/server-stock-headers defines
// it; the patched headers forward-declare `class IGameEntity`, which an alias
// of the same name could not sit beside.
//-----------------------------------------------------------------------------
#if defined( GAME_DLL ) && defined( SS_STOCK_PUBLIC_HEADERS )
abstract_class CBaseEntity
#else
abstract_class IGameEntity
#endif
{
public:
	// What CTraceFilterSimple asks an entity for.
	virtual bool ShouldCollide( int collisionGroup, int contentsMask ) const = 0;
	virtual int  GetCollisionGroup() const = 0;

	//-------------------------------------------------------------------------
	// What a trace result USED to be asked here, and no longer is.
	//
	// GetAbsOrigin, GetAbsAngles, EntIndex and GetClassname were duplicates
	// from the day they arrived: ICollideable::GetCollisionOrigin and
	// GetCollisionAngles read the same fields, IServerNetworkable::GetClassName
	// is the same string, and the edict behind IServerNetworkable::GetEdict
	// carries the same index. The membership rule's companion — a method
	// answerable through ICollideable, IServerEntity, IServerNetworkable or
	// IHandleEntity is answered THERE — always named them, and Phase 11
	// collected them. Every caller holds, or is handed, the IServerEntity it
	// resolved this interface from, and asks the face that already answered.
	//
	// Also absent for the same reason, and never here at all: GetSolid on
	// ICollideable, GetModelIndex on IServerEntity, GetRefEHandle on
	// IHandleEntity. An entity that implements those already answers them,
	// and duplicating them here would invite the two to disagree.
	//-------------------------------------------------------------------------

	//-------------------------------------------------------------------------
	// Being hurt, being a player, and what the map's spawnflags said.
	//
	// NOT here, deliberately, and no longer anywhere on this boundary.
	// TakeDamage, IsPlayer and GetSpawnFlags used to be methods of this
	// interface and failed its membership rule — the SDK's bullet and damage
	// paths ask them, the engine never does. They moved to
	// IEntityDamageTarget, the migration interface named for that caller, and
	// when that interface's callers came down to relays the interface and its
	// accessor died: the bullet path and the blast sweeps now ask
	// StandaloneEntity_TakeDamage / _IsPlayer / _SpawnFlags in
	// standalone_relays.h, which cross to the concrete shell in shell code
	// and die with the callers themselves.
	//-------------------------------------------------------------------------

	//-------------------------------------------------------------------------
	// Being talked to — by the SDK's I/O system, never by the engine.
	//
	// NOT here, deliberately, and no longer anywhere on this boundary.
	// GetEntityName and AcceptInput used to be methods of this interface and
	// failed its membership rule: outputs resolve their targets through
	// gEntList by name and the event queue delivers the input, and all of
	// that is game code in the game DLL. They moved to IEntityIo, the
	// migration interface named for that caller, and when its callers came
	// down to relays the interface and its accessor died: the event queue
	// and the name-printing shims now ask StandaloneEntity_DeliverInput /
	// _Name in standalone_relays.h, which die with cbase.cpp's event queue
	// exactly as the interface was dated to.
	//-------------------------------------------------------------------------

	//-------------------------------------------------------------------------
	// Being placed by a map — by the SDK's map loader, never by the engine.
	//
	// NOT here, deliberately, and no longer anywhere on this boundary.
	// KeyValue, Spawn, SetAbsOrigin, SetAbsAngles and Remove used to be
	// methods of this interface and failed its membership rule: the engine
	// hands the game a text blob at level load, and everything after that —
	// mapentities.cpp walking the keys, point_template stamping copies at
	// computed transforms, ent_create acting like a one-line map — is game
	// code in the game DLL. They moved to IEntityMapPlaceable, the migration
	// interface named for those callers, and when its callers came down to
	// relays the interface and its accessor died: placement now asks
	// StandaloneEntity_ApplyMapKey / _PlaceAndSpawn / _Remove in
	// standalone_relays.h, which die with those callers exactly as the
	// interface was dated to.
	//-------------------------------------------------------------------------

	//-------------------------------------------------------------------------
	// THE TWELVE, and why none of them is here.
	//
	// plan_baseentity.md Phase 13's survey compiles the whole tree against an
	// IGameEntity-only CBaseEntity and histograms what breaks. Twelve members at
	// the head of that histogram had, for a long time, no answer written down
	// anywhere except "the phase that removes its caller" — and that dating is
	// what kept failing, because the phase kept not arriving and the twelve kept
	// being re-audited. What follows is the answer for each, in the code rather
	// than in the plan. NONE of them passes the membership rule: the engine
	// never initiates any of these calls, so `IGameEntity` stays at six and
	// GameEntitySurfaceTests still pins six by name.
	//
	// The four shapes an answer takes here: an EXISTING INTERFACE already
	// answers it; a FREE RELAY in standalone_relays.h answers it; a DUAL VALUE
	// TYPE (PhysicsOwner_t, AnyEntity_t, Toucher_t) answers it; or the question
	// is CBaseEntity-INTERNAL and dies with the class, in which case what is
	// named below is the thing that made that true rather than a future date.
	//
	//   VPhysicsGetObject   Never crosses, and physicsowner.h says why: the
	//                       engine asks the OBJECT for its entity, never the
	//                       entity for its object. Code that starts at the
	//                       solver holds PhysicsOwner_t::pObject. Game code that
	//                       genuinely starts at an entity asks
	//                       StandaloneEntity_PhysicsObject.
	//
	//   GetMoveType         Never crosses. PhysicsOwner_t::IsVPhysicsSimulated
	//                       is the dual question with a stated neutral: over
	//                       here an object is in the registry BECAUSE its entity
	//                       simulates, so there is nothing a move type would
	//                       add. The one filter that branched on MOVETYPE_PUSH
	//                       is answered by ShouldCollide above — managed policy
	//                       already owns it.
	//
	//   GetFlags            Never crosses. FL_* is SDK movement state. The one
	//                       retype that mattered landed in Phase 9b: the
	//                       standalone player carries m_fFlags behind
	//                       IMovementPlayer::MvGetFlags, which is where every
	//                       player-subject asker belongs.
	//
	//   EyePosition         An ACTOR concept, and the standalone player already
	//                       answers it (shell_standalone_player.h), through
	//                       SourceSharp_EntityGetEyePosition for the ABI and
	//                       through ClientSetupVisibility's standalone arm in
	//                       production. The SDK sites that remain resolve a
	//                       player EDICT through GetContainingEntity, which
	//                       answers NULL here — so they are already inert for
	//                       this hierarchy rather than waiting on anything.
	//
	//   GetAbsVelocity      Answered: StandaloneEntity_Velocity, the velocity
	//                       managed holds on the entity (its object's while it
	//                       has one, its own member otherwise). Do not add a
	//                       second spelling; the divergence this closed was two
	//                       answers wearing one name.
	//
	//   IsEFlagSet          Never cross. EFlags are the SDK's dirty-bit and
	//   AddEFlags           lifetime bookkeeping (EFL_KILLME, EFL_DIRTY_*,
	//                       EFL_NO_THINK_FUNCTION) and this hierarchy was built
	//                       so that none of it is needed: eager composition
	//                       instead of dirty transforms, a registry instead of
	//                       KILLME sweeps. Every remaining site is inside a
	//                       CBaseEntity-only path. The ONE that was not — the
	//                       notify list caching "somebody watches me" as
	//                       EFL_NOTIFY on the watched entity, when CNotifyList
	//                       holds the list — is notify_gate.h now, and taking it
	//                       out is what let entitylist.o leave the consumer set.
	//
	//   GetTeamNumber       Teams are game policy and are not ported. The
	//                       standalone player answers 0 ("unassigned") through
	//                       SourceSharp_GetPlayerTeam's standalone arm and C#
	//                       owns teams when they arrive. Zero SDK-side asks
	//                       remain: the last three were CollectPlayers /
	//                       CollectHumanPlayers, whose every caller is a TF file
	//                       this build has never compiled, and nav_ladder.h's
	//                       IsUsableByTeam, reached only by two stray includes.
	//
	//   m_takedamage        A FIELD cannot sit on an abstract boundary; a
	//                       QUESTION can, and both dual value types carry it —
	//                       AnyEntity_t::CanTakeDamage (!= DAMAGE_NO, the blast
	//                       sweeps' threshold) and PhysicsOwner_t::CanTakeDamage
	//                       (> DAMAGE_EVENTS_ONLY, the crush path's). Writes stay
	//                       on the concrete type on purpose: setting damage mode
	//                       is an SDK-side act and this hierarchy's gate is
	//                       managed policy.
	//
	//   m_debugOverlays     Never crosses — same field-versus-question clause,
	//                       and nothing over here reads it. The whole subsystem
	//                       is CBaseEntity's own end to end: client.cpp's
	//                       ent_text family writes the bits,
	//                       CBaseEntity::DrawDebugGeometryOverlays reads them,
	//                       ndebugoverlay.cpp clears them. The single part that
	//                       reached OUTSIDE the class was DrawAllDebugOverlays'
	//                       gEntList walk — which was also the only part that
	//                       could be, and was, wrong about what it was holding —
	//                       and it is gone. This hierarchy's diagnostic surface
	//                       is the debug-api and the web UI.
	//
	//   SetTransmit         Not engine-reached: the engine hands CheckTransmit
	//                       the list, and the marking recursion (parents,
	//                       lighting origins, beam attachments, screens) is
	//                       SDK-internal. This hierarchy TRANSMITS ON VISIBILITY
	//                       ALONE and already does, in production —
	//                       CheckTransmit's `!pEnt` branch IS its transmit path.
	//                       No relay, because there is nothing for such an entity
	//                       to mark beyond what visibility already marks.
	//
	//   GetMoveParent       Never crosses. This hierarchy is a handle-keyed
	//                       registry resolved through the virtual
	//                       GetGameEntity() precisely so that no raw parent
	//                       pointer exists to hand out —
	//                       shell_standalone_hierarchy.cpp's whole argument. A
	//                       clause that branches on it is VACUOUS over here
	//                       rather than unimplemented, which is a different
	//                       thing and is why push_blocker_rule.h says so out
	//                       loud instead of quietly passing false.
	//-------------------------------------------------------------------------

	//-------------------------------------------------------------------------
	// Being walked into.
	//
	// The engine finds the overlap itself — SolidMoved enumerates the trigger
	// list in the spatial partition and reports each pair back through
	// IServerGameEnts::MarkEntitiesAsTouching. That callback resolved both
	// edicts to CBaseEntity and did nothing when either answered NULL, so an
	// entity outside the SDK could sit in the trigger list, be found, and never
	// hear about it.
	//
	// Called every tick the two overlap, which is what CBaseEntity's own Touch
	// does — a pickup has to account for that rather than assume one call.
	//
	// StartTouch and EndTouch are the EDGES, and they are separate calls
	// because they arrive from a different place. Touch is an overlap report
	// renewed every tick and stops arriving when the overlap ends, so leaving
	// is an absence and the implementation infers it. VPhysics triggers are the
	// other way round: the solver reports entry and exit exactly once each and
	// says nothing in between, so those two transitions have nowhere else to
	// go — see CCollisionEvent::ObjectEnterTrigger.
	//
	// Routing an entry through Touch instead would be wrong rather than
	// merely inelegant: one overlap report is indistinguishable from the last
	// one before something left, so the link would expire on the next tick and
	// a leave would be reported with the object still inside.
	//
	// The toucher is a Toucher_t rather than a CBaseEntity, and that is the
	// whole of what "an entity outside the SDK could not be a toucher" cost.
	// These three took a CBaseEntity for as long as every toucher was one; a
	// managed prop entering a managed trigger reached the call site and was
	// dropped there for want of a way to name it.
	//
	// WIDENED HERE, rather than joined by a second spelling the way
	// CBaseEntity's three are. That asymmetry is deliberate and the reason is
	// arithmetic: the SDK has 53 StartTouch overrides that must each stay
	// convertible on their own, and this interface has one implementor and no
	// legacy at all. A narrow spelling kept alongside a wide one here would be
	// two ways to say the same thing with a rule about which to use, forever.
	//-------------------------------------------------------------------------
	virtual void Touch( const Toucher_t &toucher ) = 0;
	virtual void StartTouch( const Toucher_t &toucher ) = 0;
	virtual void EndTouch( const Toucher_t &toucher ) = 0;

	//-------------------------------------------------------------------------
	// Being simulated.
	//
	// The solver reports every object it moved once a tick, and the SDK's loop
	// copies the result onto the entity through CBaseEntity::VPhysicsUpdate.
	// This is the same call for an entity that is not one, so that loop can do
	// its job without knowing which kind it has — see PhysicsOwner_t.
	//-------------------------------------------------------------------------
	virtual void VPhysicsUpdate( IPhysicsObject *pObject ) = 0;

protected:
#if defined( GAME_DLL ) && defined( SS_STOCK_PUBLIC_HEADERS )
	virtual ~CBaseEntity() {}
#else
	virtual ~IGameEntity() {}
#endif
};

#if defined( GAME_DLL ) && defined( SS_STOCK_PUBLIC_HEADERS )
using IGameEntity = CBaseEntity;
#endif

#endif // IGAMEENTITY_H
