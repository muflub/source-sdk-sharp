//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// Who owns a physics object, and what can be asked of them.
//
// VPhysics stores an opaque void * per object and the game decides what it
// means. The SDK's answer has always been "it is a CBaseEntity", written 24
// times in physics.cpp alone as a bare cast:
//
//     CBaseEntity *pEntity = static_cast<CBaseEntity *>( pObject->GetGameData() );
//
// That was true while every physics object belonged to a CBaseEntity. It is not
// any more: entities on the parallel hierarchy simulate too. Twenty of those 24
// sites happen to test the result for NULL; four did not, and one was inside
// _DEBUG, which is the build this mod runs.
//
// Handing back two pointers and letting the caller branch would only move the
// problem: the bridge's logic is not ABOUT what type an entity is, it is about
// collision groups, move types, touches and transforms. So this answers those
// questions directly and dispatches inside:
//
//     PhysicsOwner_t owner = PhysicsOwnerOf( pObject );
//
//     if ( !owner.IsValid() )        continue;              // nobody owns it
//     if ( owner.IsPlayer() )        ...                    // works for both
//     owner.VPhysicsUpdate( pObject );                      // works for both
//
// WHAT IS DELIBERATELY NOT HERE is the machinery that only a CBaseEntity has:
// datamaps and data objects, EFlags, the network property, shadow controllers,
// vehicles, the ragdoll solver, debug overlays. Those are not "operations on an
// entity" that the other kind could answer differently — they are the SDK's own
// internals, and a site reaching for one is a site that genuinely means "the
// SDK entity". It says so by using pBase, which stays public for exactly that.
//
// THE DISCRIMINATOR IS THE GAME DATA ITSELF (plan_entity.md §3.3, step 1h).
//
// It used to be a file-scope REGISTRY -- a CUtlVector of every simulated shell,
// walked per callback -- with the shells storing NULL as their game data so
// that an unconverted SDK site degraded to "no owner" rather than to a wild
// pointer. Every SDK site on this side has since been converted (physics.cpp
// asks PhysicsOwnerOf in 49 places and reads raw game data in none), so the
// registry was paying for a hazard that no longer exists, and plan.md §1a says
// nothing is a file-scope global.
//
// So a shell's object now carries its OWN identity: the packed CBaseHandle,
// plus one so that a handle of zero is not the "nobody" NULL, tagged in the top
// bit of the word so that it can never be confused with the CBaseEntity
// pointers the SDK still stores for the entity classes that survive.
// sourcesharp/game/shared/physics_gamedata.h holds that encoding and the whole
// of its reasoning; slot_table.h (§3.3) turns the handle back into the shell,
// serial-checked, so a STALE handle answers "no owner" instead of answering
// with whatever entity took over its entry.
//
//=============================================================================//
#ifndef PHYSICSOWNER_H
#define PHYSICSOWNER_H
#ifdef _WIN32
#pragma once
#endif

// The client's half of the entity. The server needs no entity class here:
// nothing below names CBaseEntity, so the server arm includes only the public
// interfaces it calls (plan_server_novalve.md P5: it compiles on the R5 set).
#ifdef CLIENT_DLL
	#include "c_baseentity.h"
#endif

#ifndef CLIENT_DLL
	#include "igameentity.h"
	#include "standalone_relays.h"
	#include "const.h"              // SolidType_t, which ICollideable.h names
	#include "mathlib/mathlib.h"    // matrix3x4_t, likewise
	#include "bspflags.h"           // MASK_SOLID
	#include "iservernetworkable.h" // GetNetworkable()->GetClassName()
	#include "iserverentity.h"
	#include "engine/ICollideable.h"
#else
	// The client has no parallel hierarchy: nothing over there creates a
	// physics object for one. The types still exist so shared code can be
	// written once, and answer "always a C_BaseEntity" on that side.
	class IGameEntity;
	class IServerEntity;
#endif

// Complete rather than forward declared: this asks an object whether the
// solver will move it, which is one of the questions a not-solid clip brush
// decides on.
#include "vphysics_interface.h"

// IPhysicsConstraint, for PhysicsConstraintOwner below. Complete rather than
// forward declared for the same reason IPhysicsObject is: the reader asks the
// constraint for its game data. 345 lines over vphysics_interface.h and
// mathlib, both of which every includer of this file already has.
#include "vphysics/constraints.h"

// What a game-data word MEANS: NULL, a packed handle plus one, or an SDK
// pointer. Arithmetic and one tag bit, engine-free on purpose so that the
// boundary is testable without a map -- see the header's own block.
#include "physics_gamedata.h"

// worldspawn, which PhysicsOwner_t carries a third arm for. Forward declared
// rather than included: nothing here dereferences one, and world.h is server
// only while this header is shared.
class CWorld;

//-----------------------------------------------------------------------------
// One of the two is set for an object the game owns, and neither for one it
// does not — an object the solver made for itself, or one whose entity has
// already been freed.
//
// A value type: it is built per lookup, holds no state of its own, and is meant
// to be used and dropped rather than stored. Storing one across a frame would
// be storing an entity pointer without an EHANDLE, which is the bug this file
// exists to stop.
//-----------------------------------------------------------------------------
struct PhysicsOwner_t
{
	// The SDK entity, for the machinery only it has. NULL for the other kind,
	// and a site that uses this directly is saying "I mean a CBaseEntity".
	IGameEntity *pBase;

	// The entity outside the SDK. NULL for a CBaseEntity, and always NULL on
	// the client.
	IGameEntity *pGame;

	// worldspawn, which is on neither of those. Set for the ONE object the
	// world's brush model owns and for nothing else; always NULL on the client,
	// where the world is still a C_BaseEntity and arrives through pBase.
	//
	// See PhysicsOwnerOf below for why it is a compare and not a lookup, and
	// physics.cpp for why the world object's game data is NULL.
	CWorld *pWorld;

	// The same entity's engine-facing face, set with pGame and never alone.
	// Kept because the questions below that used to be IGameEntity duplicates
	// — classname, origin, index — are answered by the interfaces this one
	// reaches: IServerNetworkable and ICollideable. Phase 11's rule, applied:
	// ask the face that already answers, never a second copy.
	IServerEntity *pServer;

	// The object this owner was looked up FROM, which is the one the caller is
	// asking about. Kept because some of the questions below are about the
	// object rather than the entity, and only one hierarchy can be asked for an
	// entity's object at all — IGameEntity deliberately has no VPhysicsGetObject.
	IPhysicsObject *pObject;

	bool IsValid() const { return pBase != NULL || pGame != NULL || pWorld != NULL; }

	// Whether this object is the world's. The question several readers of this
	// type were spelling as `pBase == GetWorldEntity()`.
	bool IsWorld() const { return pWorld != NULL; }

	// A stable identity for this owner, for the places that use the game data
	// as a KEY rather than as an entity — the collision-disable pair hash among
	// them. Without it every entity outside the SDK shares the single key NULL,
	// and disabling collisions between one pair would disable them between all
	// of them.
	void *Key() const
	{
		if ( pBase )
			return static_cast< void * >( pBase );
#ifndef CLIENT_DLL
		if ( pWorld )
			return static_cast< void * >( pWorld );
#endif
#ifndef CLIENT_DLL
		return static_cast< void * >( pGame );
#else
		return NULL;
#endif
	}

	// The one face BOTH hierarchies have, for the places that need an identity
	// rather than an entity -- a trace to ignore, a per-entity throttle keyed by
	// handle. Key() answers the same question as a `void *`; this answers it as
	// the type the engine's own collision code takes.
	//
	// pServer first because it is the arm that survives: it is set only for the
	// hierarchy outside the SDK, and IServerEntity -> IServerUnknown ->
	// IHandleEntity is one single-inheritance chain, so the conversion adjusts
	// nothing. The pBase arm is the SDK's and dies with the class.
	IHandleEntity *ToHandleEntity() const
	{
#ifndef CLIENT_DLL
		if ( pServer )
			return pServer;
#endif
		return NULL;
	}

	//-------------------------------------------------------------------------
	// What the physics bridge actually asks, answered by whichever kind this
	// is. Every one of these was a call on a bare cast before.
	//-------------------------------------------------------------------------
	const char *GetClassname() const
	{
#ifndef CLIENT_DLL
		if ( pServer ) return pServer->GetNetworkable()->GetClassName();
#endif
		return "";
	}

	// The name a mapper gave it, or its classname when it has none — which is
	// what CBaseEntity::GetDebugName does, and is why the two are one method.
	const char *GetDebugName() const
	{
#ifndef CLIENT_DLL
		if ( pGame )
		{
			const char *pName = StandaloneEntity_Name( pGame );
			return ( pName && pName[0] ) ? pName : pServer->GetNetworkable()->GetClassName();
		}
#endif
		return "";
	}

	int GetCollisionGroup() const
	{
		if ( pBase ) return pBase->GetCollisionGroup();
#ifndef CLIENT_DLL
		if ( pGame ) return pGame->GetCollisionGroup();
#endif
		return COLLISION_GROUP_NONE;
	}

	const Vector &GetAbsOrigin() const
	{
#ifndef CLIENT_DLL
		if ( pServer ) return pServer->GetCollideable()->GetCollisionOrigin();
#endif
		return vec3_origin;
	}

	bool IsPlayer() const
	{
#ifndef CLIENT_DLL
		if ( pGame ) return StandaloneEntity_IsPlayer( pGame );
#endif
		return false;
	}

	int entindex() const
	{
#ifndef CLIENT_DLL
		if ( pServer )
		{
			// -1 for a server-only entity with no edict, exactly as the old
			// EntIndex answered. For a networked one the handle's entry index
			// IS the edict index — AddNetworkableEntity was handed
			// IndexOfEdict at registration, so the two cannot differ.
			if ( !pServer->GetNetworkable()->GetEdict() )
				return -1;
			return pServer->GetRefEHandle().GetEntryIndex();
		}
#endif
		return -1;
	}

	//-------------------------------------------------------------------------
	// Answers that only one kind has, given a neutral value by the other rather
	// than a branch at the call site.
	//
	// Each neutral value is chosen to make the code that reads it do the
	// ORDINARY thing, not to make it skip: an entity outside the SDK is not
	// pushed by a mover, is not an NPC, and can be hurt.
	//-------------------------------------------------------------------------
	// NOT A NEUTRAL ANY MORE (physics stock parity, step C2). This was the
	// constant MOVETYPE_NONE, and the collision record copies it: managed
	// PhysicsCollisionRules.Responds requires a MOVETYPE_VPHYSICS side, as
	// stock's VPhysicsCollision does, so it refused every live collision and
	// the managed impact response never ran. A managed entity answers what
	// shell_worldquery.cpp already reports to C# for it (standalone_movetype.h).
	MoveType_t GetMoveType() const
	{
#ifndef CLIENT_DLL
		if ( pGame ) return (MoveType_t)StandaloneEntity_MoveType( pGame );
#endif
		return MOVETYPE_NONE;
	}

	bool IsNPC() const { return false; }

	bool CanTakeDamage() const
	{
		return true;
	}

	// What kind of water this is standing in, as the CONTENTS_* mask the SDK
	// tracks per entity. The fluid callbacks read it for one bit -- whether the
	// splash should be slime rather than water.
	//
	// The neutral is anyentity.h's, chosen for the same question one file over:
	// an entity outside the SDK tracks no water level, so it reads as plain
	// water. A DIVERGENCE, stated, rather than a second mechanism -- closing it
	// means giving that hierarchy water tracking, not inventing a number here.
	int GetWaterType() const
	{
		return 0;
	}

	// Whether the solver is what decides where this goes. The SDK spells that as
	// a move type; the other hierarchy does not spell it at all, because a
	// physics object is the only thing that ever moves one of those — an object
	// is in the registry BECAUSE its entity simulates, and nothing else puts it
	// there.
	bool IsVPhysicsSimulated() const
	{
		return ( pGame != NULL );
	}

	// Whether the solver will actually take this anywhere, which is what
	// separates a physics prop from a static brush that happens to have an
	// object.
	//
	// The SDK asks the ENTITY, which answers for the first of its objects — a
	// ragdoll has several, and that first one is what has always been tested
	// here. The other hierarchy has exactly one object per entity, so the object
	// this owner was looked up from is the same answer by the only route
	// available.
	bool IsVPhysicsMoveable() const
	{

		return pObject && pObject->IsMoveable();
	}

	// What it does to a trace, and to something that runs into it.
	//
	// Both kinds have a real answer — the SDK's on its collision property, the
	// other one's on ICollideable — and this type holds neither of those
	// interfaces. The neutral is therefore not a stand-in: it is what InitPhysics
	// sets the instant it gives one of those entities an object at all, which is
	// SOLID_VPHYSICS and solid. An entity that later stops being solid to the
	// world is a divergence recorded elsewhere, not a second answer to keep in
	// step with this one.
	// A managed entity answers from its own collideable -- the block -- as
	// stock's ShouldCollide reads pEntity->GetSolid()/GetSolidFlags() (physics
	// stock parity A6). It used to answer the neutral, so SetNotSolid and a
	// trigger's FSOLID_TRIGGER never reached the solver.
	SolidType_t GetSolid() const
	{
#ifndef CLIENT_DLL
		if ( pServer ) return (SolidType_t)pServer->GetCollideable()->GetSolid();
#endif
		return SOLID_VPHYSICS;
	}

	int GetSolidFlags() const
	{
#ifndef CLIENT_DLL
		if ( pServer ) return pServer->GetCollideable()->GetSolidFlags();
#endif
		return 0;
	}

	// What this entity treats as solid when something asks whether it can be in
	// a place. MASK_SOLID is CBaseEntity's own answer; the overrides all belong
	// to things that move THROUGH something the rest of the world stops at — an
	// NPC, a player in a vehicle — and neither is on the other hierarchy.
	unsigned int PhysicsSolidMaskForEntity() const
	{
		// CBasePlayer's override (player.cpp:3451): what a player treats as
		// solid includes the player clips.
		if ( IsPlayer() )
			return (unsigned int)MASK_PLAYERSOLID;

		return (unsigned int)MASK_SOLID;
	}

#ifndef CLIENT_DLL
	// Whether this entity insists on colliding with the other one even though
	// the ordinary rules would separate them. One entity in the SDK takes that
	// decision — func_clip_vphysics, an invisible brush whose whole job is to
	// stop physics objects and which is FSOLID_NOT_SOLID so that nothing else
	// notices it.
	//
	// The override is handed this whole type rather than a CBaseEntity, so the
	// brush can be asked about an entity from either hierarchy. It used to take
	// a CBaseEntity and there was no other way to ask it, which meant a pair
	// with a managed prop in it got no override at all and the not-solid rule
	// separated the two — the brush was there and props went through it.
	//
	// Only the SDK side can hold the opinion: nothing on the other hierarchy is
	// a clip brush, and one that wanted to be would be answering from C#.
	bool ForcesCollisionWith( const PhysicsOwner_t &other ) const
	{
		return false;
	}
#endif

	//-------------------------------------------------------------------------
	// Being simulated, and being touched. Both hierarchies do these; only the
	// spelling differs, which is precisely what this type is for.
	//-------------------------------------------------------------------------
	void VPhysicsUpdate( IPhysicsObject *pObject ) const
	{
		if ( pBase ) { pBase->VPhysicsUpdate( pObject ); return; }
#ifndef CLIENT_DLL
		if ( pGame ) pGame->VPhysicsUpdate( pObject );
#endif
	}

	// Being touched, which is the one thing on this type that the two builds
	// genuinely cannot share.
	//
	// Toucher_t is server-only, because the client half of the parallel
	// hierarchy does not exist: over there a toucher is a C_BaseEntity and can
	// be nothing else. Giving C_BaseEntity a parallel entry point to match
	// would be a promise with nothing on that side able to keep it — the same
	// reason anyentity.h declines to compile on the client at all. So the
	// client keeps exactly the code it had, and only the server's grew.
#ifdef CLIENT_DLL
	// ARMED, THE CLIENT HAS NO TOUCH PATH AT ALL, and that is this block's own
	// reasoning carried one step further rather than a new decision. The note
	// above says the client half of the parallel hierarchy does not exist, so a
	// client toucher "is a C_BaseEntity and can be nothing else" -- and armed
	// there is no C_BaseEntity to be. IGameEntity::StartTouch takes a Toucher_t,
	// which is server-only for exactly the reason given above, so there is no
	// argument this call could be given here. It becomes a no-op, which is the
	// same answer the client already gave for a pair it could not resolve.
	void StartTouch( const PhysicsOwner_t &other ) const
	{
		(void)other;
	}

	void EndTouch( const PhysicsOwner_t &other ) const
	{
		(void)other;
	}
#else
	// Defined at the foot of this file rather than here, because a toucher's
	// own header includes this one — it needs to know what an entity is, and a
	// physics owner is where the server already says so. The class body cannot
	// name a type that has not been defined yet, so the bodies wait.
	void StartTouch( const PhysicsOwner_t &other ) const;
	void EndTouch( const PhysicsOwner_t &other ) const;

	// This owner seen as the thing that walked in.
	Toucher_t AsToucher() const;
#endif
};

#if !defined( CLIENT_DLL )
//-----------------------------------------------------------------------------
// THE SHELL A GAME-DATA HANDLE NAMES, or NULL.
//
// TAKES THE HANDLE AND NOT THE OBJECT, which is the shape that keeps the cast
// census honest: the decode lives here, in a header, and the definition -- in
// sourcesharp/game/server/shell_physicsobjects.cpp -- is a slot-table resolve
// with no GetGameData call and no cast in it at all. §3.16 family 4 is
// closed by the pair rather than by either half.
//
// NULL for three different things, all of which mean "do not touch this
// object's entity": the handle is invalid, nothing is bound at its entry, or
// the serials disagree because the entry has been reused. The third is the one
// worth naming -- a stale handle must answer NULL and NEVER the new occupant,
// which is what slot_table.h's Resolve is for and what
// physics_gamedata_test.cpp pins.
//-----------------------------------------------------------------------------
IServerEntity *PhysicsStandaloneOwner( const CBaseHandle &handle );

//-----------------------------------------------------------------------------
// THE THIRD ARM, HERE TOO. worldspawn is on neither hierarchy
// (plan_cworld_trace.md T3), so the game data does not carry it either --
// physics.cpp stores NULL as the world object's game data precisely so that
// nothing casts it to a class it is not.
//
// The world's object is a SINGLETON (g_PhysWorldObject), so this is a pointer
// compare and not a lookup: the same shape, and the same reason, as
// world_subject.h's SourceSharp_IsTheWorld.
//
// Defined in physics.cpp, which is where g_PhysWorldObject is assigned.
//-----------------------------------------------------------------------------
CWorld *PhysicsWorldOwner( const IPhysicsObject *pObject );

// The same owner as a TOUCHER, which the shell has to build rather than this
// header: a toucher carries the handle as well as the interface, and only the
// concrete type can produce both — they are reached by different casts and
// IGameEntity is not one of them. shell_standalone_class.h splits its own
// pair of lookups for exactly this reason.
struct Toucher_t;
Toucher_t PhysicsStandaloneToucher( const CBaseHandle &handle );
#endif

//-----------------------------------------------------------------------------
// Who owns a game-data word.
//
// ONE READ OF THE GAME DATA AND THEN A DECISION, which is the whole of step 1h.
// The word says which arm it belongs to (physics_gamedata.h): a tagged word is
// a handle and is answered ONLY from the slot table, an untagged one is the
// SDK's own pointer, and NULL is nobody.
//
// THE TAGGED ARM RETURNS WITHOUT FALLING THROUGH, and that is the line that
// matters. A stale handle -- the entity freed while the solver still holds its
// object, which is the window physics.cpp's deferred event drain lives in --
// resolves to NULL, and "no owner" is what every reader of this type already
// tests for. Letting it fall through to the cast below would hand back the
// handle reinterpreted as a CBaseEntity *, which is the exact bug §3.16
// family 4 exists to remove.
//
// pObject is the object the word was READ FROM, and it is NULL for a constraint
// (see PhysicsConstraintOwner) -- a constraint is not an object and the two
// questions this type answers about one, IsVPhysicsMoveable and AsToucher, say
// so by reading NULL.
//-----------------------------------------------------------------------------
#if !defined( CLIENT_DLL )
//-----------------------------------------------------------------------------
// The owner a managed entity's handle names, with no object: the tagged arm
// below, and what a queue that outlives the solver callback (the buffered
// contact touches, physics.cpp) resolves at dispatch. A stale handle answers
// no owner, never the entry's new occupant.
//-----------------------------------------------------------------------------
inline PhysicsOwner_t PhysicsOwnerOfHandle( const CBaseHandle &handle, IPhysicsObject *pObject = NULL )
{
	PhysicsOwner_t owner = { NULL, NULL, NULL, NULL, pObject };

	owner.pServer = PhysicsStandaloneOwner( handle );

	if ( owner.pServer )
		owner.pGame = owner.pServer->GetGameEntity();

	return owner;
}
#endif

inline PhysicsOwner_t PhysicsOwnerOfGameData( void *pGameData, IPhysicsObject *pObject )
{
	PhysicsOwner_t owner = { NULL, NULL, NULL, NULL, pObject };

#if !defined( CLIENT_DLL )
	if ( PhysicsGameDataIsHandle( pGameData ) )
	{
		owner = PhysicsOwnerOfHandle( PhysicsHandleFromGameData( pGameData ), pObject );
		return owner;
	}
#else
	// THE CLIENT'S MANAGED OWNERS (plan_client_novalve C6, cluster 9): a
	// client ragdoll a managed entity owns carries the same tagged handle word
	// the server's objects do. Its owner is managed's to answer, through the
	// collision-event sink managed installs on the client's environment, so
	// here it is no SDK owner -- never the word cast to an IGameEntity *,
	// which the first virtual call through pBase would fault on.
	if ( PhysicsGameDataIsHandle( pGameData ) )
		return owner;
#endif

	owner.pBase = static_cast< IGameEntity * >( pGameData );
	return owner;
}

//-----------------------------------------------------------------------------
// Who owns an object.
//-----------------------------------------------------------------------------
inline PhysicsOwner_t PhysicsOwnerOf( IPhysicsObject *pObject )
{
	if ( !pObject )
	{
		PhysicsOwner_t none = { NULL, NULL, NULL, NULL, NULL };
		return none;
	}

#if !defined( CLIENT_DLL )
	// The world FIRST, and it is one pointer compare. Asked before the game
	// data for the same reason every world arm added in T2 is asked first: it
	// is the cheapest of the three and it is exact. physics.cpp stores NULL as
	// the world object's game data, so there is nothing here to read for it.
	CWorld *pWorld = PhysicsWorldOwner( pObject );

	if ( pWorld )
	{
		PhysicsOwner_t world = { NULL, NULL, pWorld, NULL, pObject };
		return world;
	}
#endif

	return PhysicsOwnerOfGameData( pObject->GetGameData(), pObject );
}

//-----------------------------------------------------------------------------
// Who owns a CONSTRAINT.
//
// A constraint carries its own game data, separate from either object's, and
// the SDK read it with a bare `(CBaseEntity *)` cast in the one callback
// VPhysics makes about one (ConstraintBroken). It is the same word with the
// same meaning, so it is read the same way -- and pObject is NULL because a
// constraint is not an object and there is no honest answer to "is this
// moveable" for one.
//
// NOTHING IN THIS TREE SETS IT TODAY. Every SetGameData on a constraint came
// from a CBaseEntity-derived class in physconstraint.cpp / physobj.cpp /
// physgun.cpp, none of which is compiled any more, and managed constraints do
// not set it at all -- so this answers "nobody" for every constraint that
// exists. That is a stated gap (shell_constraints.cpp says so at the callback),
// and closing it is now a SetGameData call rather than a new mechanism.
//-----------------------------------------------------------------------------
inline PhysicsOwner_t PhysicsConstraintOwner( IPhysicsConstraint *pConstraint )
{
	if ( !pConstraint )
	{
		PhysicsOwner_t none = { NULL, NULL, NULL, NULL, NULL };
		return none;
	}

	return PhysicsOwnerOfGameData( pConstraint->GetGameData(), NULL );
}


#ifndef CLIENT_DLL
#include "toucher.h"

//-----------------------------------------------------------------------------
// An owner, as a toucher.
//
// An owner of neither kind — an object the solver made for itself, or one whose
// entity has already been freed — is a toucher of neither, which is the value
// every reader of this already tests for.
//-----------------------------------------------------------------------------
inline Toucher_t PhysicsOwner_t::AsToucher() const
{
	if ( pBase )
		return ToucherOf( pBase );

	// The same handle PhysicsOwnerOf decoded, decoded again rather than carried
	// as a sixth member of this struct: the struct is aggregate-initialised and
	// copied by value all over physics.cpp, and a member nobody reads except
	// here would be a field to keep in step for one call site.
	if ( pGame && pObject )
		return PhysicsStandaloneToucher( PhysicsHandleFromGameData( pObject->GetGameData() ) );

	// An owner rebuilt from a handle with no object (a buffered contact touch,
	// physics.cpp UpdateTouchEvents) names the same entity through that handle,
	// which is what the object's game data encodes.
	if ( pGame && pServer )
		return PhysicsStandaloneToucher( pServer->GetRefEHandle() );

	Toucher_t none = { NULL, NULL, NULL };
	return none;
}

//-----------------------------------------------------------------------------
// Being told that something entered this volume, and that it has left.
//
// The toucher used to be spelled CBaseEntity at both ends, so two entities
// outside the SDK touching each other was skipped for want of one to hand over
// — a managed prop entering a managed physics trigger reported nothing at all.
// Toucher_t is the argument that was missing, and these two are the whole of
// what closing that gap costs here.
//
// An SDK trigger goes through StartTouchAny, which hands a CBaseEntity toucher
// straight to the override it already has. So a pair that worked before works
// identically, and a pair with an entity outside the SDK in it now arrives
// somewhere instead of being dropped one frame earlier.
//-----------------------------------------------------------------------------
inline void PhysicsOwner_t::StartTouch( const PhysicsOwner_t &other ) const
{
	const Toucher_t toucher = other.AsToucher();

	if ( pGame ) pGame->StartTouch( toucher );
}

inline void PhysicsOwner_t::EndTouch( const PhysicsOwner_t &other ) const
{
	const Toucher_t toucher = other.AsToucher();

	if ( pGame ) pGame->EndTouch( toucher );
}
#endif

#endif // PHYSICSOWNER_H
