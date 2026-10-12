//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// What a volume query found, when not everything in a volume is a CBaseEntity.
//
// The spatial partition is the one part of this that was never confused. It
// holds IHandleEntity and asks ICollideable for geometry, so it finds every
// entity in the world whichever hierarchy the entity is on. What loses them is
// the line immediately after:
//
//     CBaseEntity *pEntity = gEntList.GetBaseEntity( pHandleEntity->GetRefEHandle() );
//     if ( pEntity ) ...
//
// CFlaggedEntitiesEnum does that, so CEntitySphereQuery does, so every caller of
// UTIL_EntitiesInSphere does — CGameRules::RadiusDamage among them. The entity
// was found and then dropped, which is why an explosion beside a managed prop
// did nothing and said nothing.
//
// This is that enumeration with the drop removed. Same shape as PhysicsOwner_t
// and EntityFilter_t and for the same reasons: a value type built per lookup, a
// pair of pointers of which exactly one is set, and questions answered by
// whichever kind this is rather than a branch at every call site.
//
// WHY IT IS NOT EntityFilter_t, since the two look alike. That one is resolved
// from a NAME and its pBase is a CBaseFilter, because a filter is an entity you
// look up in order to ask one question. This is resolved from an IHandleEntity
// and its pBase is a CBaseEntity, because a sweep does not look anything up —
// the partition hands it whatever is nearby and it must cope with all of it. The
// resolution, the base type and the questions all differ; what they share is the
// SHAPE, and that shape is PhysicsOwner_t's, which both are following rather
// than copying each other. If a common base ever earns its place it should be
// extracted from all three at once, not smuggled in by making one of them
// pretend to be another.
//
// WHAT IS ON IT is what a radius sweep asks a victim, and what it asks the
// thing standing in the way of one, and nothing else. Those are two roles for
// one type on purpose: a blocker is simply another entity of either kind, found
// by a trace instead of by the partition, and a second value type for it would
// be the same three fields with the same two arms. Each
// neutral answer is chosen so the code reading it does the ORDINARY thing: an
// entity outside the SDK can be hurt, has no faction to spare it, is not in
// water, and is not an NPC. It is not a CBaseEntity substitute — a site that
// wants the datamap, the touch links or DispatchTraceAttack's hitgroup machinery
// means the SDK entity, and says so by using pBase, which stays public.
//
// Server only, and deliberately not written to compile on the client: the
// client half of the parallel hierarchy does not join the spatial partition at
// all, so there is nothing over there for a volume query to find and a
// client-side version of this would be a promise it could not keep.
//
//=============================================================================//
#ifndef ANYENTITY_H
#define ANYENTITY_H
#ifdef _WIN32
#pragma once
#endif

#include "const.h"
#include "iserverentity.h"
#include "iservernetworkable.h"
#include "igameentity.h"
#include "standalone_relays.h"
#include "ss_entityhandle.h"   // ss::GameEntityFromEntityHandle (util_shared.h's inline)
#include "ss_shareddefs.h"    // ss::kMaxSphereQuery (util.h's MAX_SPHERE_QUERY)
#include "ispatialpartition.h"
#include "entity_name_match.h"
#include "collideable_center.h"
#include "shell_shot_path.h"   // EntityShot_TraceAttackToTriggers, below
#include "world_subject.h"      // the third arm; see that header

extern ISpatialPartition *partition;   // shell_engineboundary.cpp; util.h declared it before P5

//-----------------------------------------------------------------------------
// An entity from a volume query, on whichever hierarchy holds it.
//
// pHandle is kept alongside the two because it is the only identity they share,
// and a sweep compares candidates against things it was handed as handles: the
// entity to ignore, and whatever a trace stopped on.
//-----------------------------------------------------------------------------
struct AnyEntity_t
{
	IHandleEntity *pHandle;

	// The SDK entity, for the machinery only it has. NULL for the other kind.
	IGameEntity *pBase;

	// The entity outside the SDK. NULL for a CBaseEntity.
	IGameEntity *pGame;

	//-------------------------------------------------------------------------
	// THE WORLD, WHICH IS ON NEITHER OF THOSE TWO HIERARCHIES.
	//
	// Set for worldspawn and for nothing else. The other two are exclusive of
	// each other and this one is NOT exclusive of pBase -- TODAY IT IS SET
	// ALONGSIDE IT, because today CWorld is still a CBaseEntity and pBase finds
	// it too. That overlap is deliberate and is what makes this arm testable
	// and provable live BEFORE the un-derive rather than after: every reader
	// that already reads pBase keeps the answer it had, and every reader
	// taught to ask pWorld gets the right answer on both sides of T3.
	//
	// After T3 the overlap ends: pBase goes NULL for the world and this is the
	// only arm left holding it. That is the whole mechanism -- the day the
	// inheritance goes, nothing here has to change.
	//
	// FORWARD-DECLARED RATHER THAN INCLUDED. world.h drags in
	// shell_world_nettable.h and world_net_sync.h, and this header is included
	// by every volume query and every trace filter on the server. Nothing here
	// dereferences a CWorld, so nothing here needs its definition.
	//-------------------------------------------------------------------------
	CWorld *pWorld;

	// THE WORLD COUNTS AS SOMETHING. `IsValid()` is what CAnyEntitiesEnum uses
	// to decide the partition handed it an entity at all, and what several
	// callers use to decide there is anything there -- so leaving the world out
	// of it is precisely the "hit the world and hit nothing answer the same"
	// defect this arm exists to close.
	bool IsValid() const { return pBase != NULL || pGame != NULL || pWorld != NULL; }

	// Whether this is the world, asked of a resolved value rather than of a
	// raw handle. Same fact, and the callers that hold one of these should not
	// have to hold the handle as well.
	bool IsWorld() const { return pWorld != NULL; }

	// Whether this is the entity something else named. Compared as handles
	// rather than as CBaseEntity pointers, which is how the two kinds came to
	// be confused in the first place: trace_t::m_pEnt and the ignore argument
	// are both IHandleEntity, and only one kind survived the cast that made
	// them look like CBaseEntity.
	bool Is( const IHandleEntity *pOther ) const { return pHandle == pOther; }

	const char *GetClassname() const
	{

		// The networkable's answer, which is the same string IGameEntity used
		// to duplicate. The cast is the one BodyTarget below already makes:
		// on the server the handle IS the server entity, one address.
		//
		// THE CONDITION GAINED `|| pWorld` AND WAS NOT WIDENED TO BARE
		// `pHandle`, and the difference is a crash.
		//
		// A STATIC PROP REACHES THIS FUNCTION. It is in the same partition, it
		// arrives in the same field, and AnyEntityOf answers it with all three
		// arms NULL -- so `pHandle` alone is TRUE for something that has no
		// IServerEntity at all, and `(IServerEntity *)` on it is a cast to a
		// vtable that is not there. The guard is not testing "did we resolve
		// something"; it is naming the kinds for which this cast is legal, and
		// the world is now one more of them.
		//
		// Both named kinds are reachable through an edict, which is what makes
		// them answer IServerNetworkable -- so "worldspawn" comes back through
		// exactly the route "prop_physics" comes back through.
		if ( ( pGame || pWorld ) && pHandle )
			return ( (IServerEntity *)pHandle )->GetNetworkable()->GetClassName();

		return "";
	}

	//-------------------------------------------------------------------------
	// What a blast asks before it decides this is a victim.
	//-------------------------------------------------------------------------

	// Everything on the parallel hierarchy is OFFERED damage and answers for
	// itself — CStandaloneEntity::TakeDamage forwards to managed code and
	// reports what landed — so there is no second mechanism this could mean.
	// The same answer SourceSharp_EntityGetState gives, for the same reason.
	//
	// THE WORLD IS THE ONE KIND FOR WHICH THE NEUTRAL IS WRONG, and it is the
	// only neutral on this type that the third arm has to override.
	//
	// "Everything outside the SDK is offered damage" is right for the parallel
	// hierarchy, which forwards to managed code and reports what landed. The
	// world has nothing to forward to and stock's answer is unambiguous:
	// CBaseEntity's constructor leaves m_takedamage at DAMAGE_NO and CWorld
	// never changes it, so `pBase->m_takedamage != DAMAGE_NO` is FALSE for
	// worldspawn today. Without this arm, T3 would silently flip a blast's
	// victim list to include the level itself -- a RadiusDamage that hurts the
	// map, which is not a crash and would show up only as damage numbers that
	// do not add up.
	//
	// Checked before the SDK arm rather than after, so it is the answer today
	// as well as after T3, and today it agrees with the arm it precedes.
	//
	bool CanTakeDamage() const
	{
		if ( pWorld )
			return false;

		return true;
	}

	// Classify() WAS HERE (factions: CLASS_NONE for everything outside the
	// SDK). Nothing called it, and it was the header's only use of Class_T, so
	// it went (P5 lane E) rather than keep baseentity.h's enum reachable.

	// Whether a blast can reach it through the surface, which stock spells as
	// "are we both wet or both dry". An entity outside the SDK has no water
	// level to track, so it reads as dry: hurt by a blast in the air, spared by
	// one underwater. A divergence rather than a second answer — closing it
	// means the water tracking, not a different number here.
	int GetWaterLevel() const
	{
		return 0;
	}

	bool IsNPC() const { return false; }

	//-------------------------------------------------------------------------
	// What the SHARED TRACE FILTERS ask, and they are the reason these three
	// are here rather than in the caller.
	//
	// util_shared.cpp's filter family resolved every candidate with
	// EntityFromEntityHandle and then treated NULL as "not there": five filters
	// returned false for an entity outside the SDK, so a managed prop was
	// invisible to every LOS trace, every classname-skip trace and every
	// "everything but NPCs" trace in the game. A sixth, CTraceFilterLOS, did not
	// check at all and dereferenced the NULL.
	//-------------------------------------------------------------------------

	// Whether it stops a line of sight. The SDK carries a per-entity bit that a
	// handful of entities clear (grates, some breakables) and everything else
	// leaves set, so the neutral is the majority answer AND the safe one: a
	// thing in the world blocks sight until something says otherwise.
	bool BlocksLOS() const
	{
		return true;
	}

	// Whether it is a player. The parallel hierarchy has exactly one kind that
	// can be, and the relay is what the bullet path already asks.
	bool IsPlayer() const
	{
		return pGame ? StandaloneEntity_IsPlayer( pGame ) : false;
	}

	// The classname test the filters use, with stock's own matching rule.
	// FClassnameIs is EntityNames_Match over GetNetworkable()->GetClassName()
	// (baseentity.h) and GetClassname() above is that same string for both
	// kinds, so this is stock's answer reached by stock's route.
	bool ClassMatches( const char *pszClassname ) const
	{
		return EntityNames_Match( pszClassname, GetClassname() );
	}

	//-------------------------------------------------------------------------
	// What a blast asks about the thing STANDING IN THE WAY of a victim.
	//
	// A sweep builds one of these for the victim it found and a second for
	// whatever its trace stopped on — see BlastBlockerCounts in
	// blast_blocking.h. The two questions below are the whole of what the
	// blocking rule asks, and neither had an answer for this hierarchy: the
	// rule resolved the trace hit with TraceEntity, got NULL, and skipped
	// itself entirely. A managed crate absorbed nothing.
	//-------------------------------------------------------------------------

	// The solver's object, or NULL for something the solver does not move.
	// Weighed rather than merely counted: stock scales what a blocker absorbs
	// by its mass, so a light crate shields a little and a heavy one shields
	// everything.
	IPhysicsObject *VPhysicsGetObject() const
	{
		return pGame ? StandaloneEntity_PhysicsObject( pGame ) : NULL;
	}

	// Who owns it, as a handle — which is what Is() above compares, and what
	// the other kind stores natively. The SDK's GetOwnerEntity answers a
	// CBaseEntity and upcasts; there is no second identity involved, the chain
	// being single inheritance.
	IHandleEntity *GetOwnerHandle() const
	{
		return pGame ? StandaloneEntity_OwnerHandle( pGame ) : NULL;
	}

	//-------------------------------------------------------------------------
	// Where to aim at it.
	//
	// CBaseEntity::BodyTarget is WorldSpaceCenter for everything that is not a
	// character, and a prop is not one. The other kind has no such method but
	// it does have an ICollideable — the interface the partition already used
	// to find it — and WorldSpaceCenter is, on both sides,
	// CollisionProp()->WorldSpaceCenter(), which reads nothing but that same
	// interface. So this is stock's number by stock's route, without asking
	// IGameEntity to grow a geometry method it would only ever answer one way.
	//
	// IT USED TO BE THE MIDPOINT OF WorldSpaceSurroundingBounds, and that is a
	// DIFFERENT POINT — the OBB's centre carried through
	// CollisionToWorldTransform, versus the centre of the axis-aligned box that
	// encloses the entity. The two agree only when the OBB is centred on the
	// origin or the entity is unrotated; CStandaloneEntity is exactly the case
	// where they part, because its surrounding bounds are
	// `m_vecOrigin + m_vecMins .. m_vecOrigin + m_vecMaxs` — never rotated —
	// while its CollisionToWorldTransform is AngleMatrix( m_angRotation,
	// m_vecOrigin ) and is (shell_standalone_base.cpp:SetAbsTransform). A blast
	// beside a rotated managed prop with an off-centre OBB was tracing its line
	// of sight at a point the prop is not at, so the trace could stop on
	// scenery and dock the damage the prop should have taken.
	//
	// collideable_center_adapter_test pins the adapter, including that these two
	// quantities really do differ on that shape — a check that would otherwise
	// be unfalsifiable, since both are "a centre".
	//-------------------------------------------------------------------------
	Vector BodyTarget( const Vector &posSrc, bool bNoisy ) const
	{

		if ( !pHandle )
			return posSrc;

		ICollideable *pCollide = ( (IServerEntity *)pHandle )->GetCollideable();
		if ( !pCollide )
			return posSrc;

		return UTIL_CollideableWorldSpaceCenter( pCollide );
	}

	//-------------------------------------------------------------------------
	// Being hurt, in the two ways a sweep does it.
	//-------------------------------------------------------------------------
	int TakeDamage( const ss::CTakeDamageInfo &info ) const
	{
		return pGame ? StandaloneEntity_TakeDamage( pGame, info ) : 0;
	}

	// The traced form, which exists so a hit can name WHERE it landed —
	// hitgroups, armour, tracer decals. None of that is on the other side, and
	// inventing a hitgroup for a crate would be inventing a decision. So the
	// blast lands as plain damage, which is what stock already does for
	// anything the trace did not stop on.
	void DispatchTraceAttack( const ss::CTakeDamageInfo &info, const Vector &vecDir, trace_t *ptr ) const
	{

		if ( pGame )
			StandaloneEntity_TakeDamage( pGame, info );
	}

	// Triggers along the path of the hit, which is how a volume learns that
	// something was shot through it. It walks the entity's touch links and the
	// parallel hierarchy keeps none — see igameentity.h on why StartTouch and
	// EndTouch are absent from it. Nothing, rather than a second mechanism to
	// keep in step.
	// The call names the FREE function now. It always could have: the body
	// takes no entity of any kind -- it builds a Ray_t from the two points and
	// hands it to CTriggerTraceEnum, which reaches both ends of every hit
	// through the enumerator's arguments. The class scope was being used as a
	// namespace, which is why the lift to EntityShot_TraceAttackToTriggers was
	// possible at all and why shell_ai_orphans_forwarders.cpp could be retired.
	//
	// `if ( pBase )` STAYS, and it is not redundant even though the callee
	// ignores the subject: it is what keeps this behaviour-identical. Only a
	// CBaseEntity walked triggers before, and a parallel-hierarchy entity must
	// keep not walking them until something decides otherwise on purpose.
	//
	// The `#ifndef SS_CBASEENTITY_ALIAS` that used to wrap this is gone with the
	// member call it was protecting. It compiled the whole trigger walk OUT of
	// an armed build -- correct as a way to keep the header parsing, wrong as
	// behaviour, and now unnecessary: a free function is not a class scope, so
	// there is nothing here for the alias to break.
	void TraceAttackToTriggers( const ss::CTakeDamageInfo &info, const Vector &start,
	                            const Vector &end, const Vector &dir ) const
	{
		if ( pBase )
			EntityShot_TraceAttackToTriggers( info, start, end, dir );
	}
};

//-----------------------------------------------------------------------------
// Which kind this is.
//
// The SDK's own lookup goes first because it is the one that already happened
// everywhere, and the second is only reached when the first answers NULL: a
// CBaseEntity returns NULL from GetGameEntity, so the two can never both
// succeed and the common path pays for one lookup exactly as before.
//
// THE WORLD IS ASKED FIRST AND DOES NOT SUPPRESS THE OTHERS, which is the one
// place this function departs from "exactly one arm is set".
//
//   * FIRST, because it is a compare against one pointer -- cheaper than either
//     virtual call below it -- and because an arm that only runs when both
//     others fail is dead code until T3 and therefore untested until T3.
//
//   * NOT EXCLUSIVE, because today the world still IS a CBaseEntity and pBase
//     must keep finding it. Every existing reader of this type reads pBase, and
//     making the world stop answering there TODAY would change live behaviour
//     ahead of the change that is supposed to cause it. So today a world hit
//     sets pWorld AND pBase; after T3 it sets pWorld alone, and no reader that
//     was taught to ask pWorld notices the difference.
//
// A STATIC PROP STILL ANSWERS ALL THREE NULL: the world check is an identity
// compare, so it is false for a prop exactly as EntityFromEntityHandle's
// staticpropmgr question is.
//-----------------------------------------------------------------------------
inline AnyEntity_t AnyEntityOf( IHandleEntity *pHandleEntity )
{
	AnyEntity_t ent = { pHandleEntity, NULL, NULL, NULL };

	if ( !pHandleEntity )
		return ent;

	ent.pWorld = SourceSharp_WorldFromEntityHandle( pHandleEntity );

	// pBase IS ALWAYS NULL IN THIS BUILD, and now it says so rather than
	// computing it. It used to be `EntityFromEntityHandle( pHandleEntity )`,
	// which bottoms out in `IServerUnknown::GetBaseEntity()` -- and every
	// implementation of that in server.so returns a constant NULL
	// (CStandaloneEntity, CWorld, CWorldNetworkable; read out of the binary).
	// The field is kept because ent.IsValid(), ClassMatches and the
	// world_adapter diagnostic all read the three-arm shape; it is a deletion
	// candidate and the close's handover names it as one.
	ent.pBase = NULL;

	ent.pGame = ss::GameEntityFromEntityHandle( pHandleEntity );

	return ent;
}

//-----------------------------------------------------------------------------
// The partition enumerator, without the drop.
//
// Handles are stored rather than resolved pairs, so a caller's list is the same
// size in bytes as CFlaggedEntitiesEnum's and the resolution happens once per
// candidate either way.
//-----------------------------------------------------------------------------
class CAnyEntitiesEnum : public IPartitionEnumerator
{
public:
	CAnyEntitiesEnum( IHandleEntity **pList, int listMax )
		: m_pList( pList ), m_listMax( listMax ), m_count( 0 )
	{
	}

	virtual IterationRetval_t EnumElement( IHandleEntity *pHandleEntity ) OVERRIDE
	{
		// Resolved here only to decide whether it is an entity at all. A static
		// prop is in the partition and is not one — the case
		// EntityFromEntityHandle already rejected, and the reason this cannot
		// simply keep everything the partition offers.
		if ( !AnyEntityOf( pHandleEntity ).IsValid() )
			return ITERATION_CONTINUE;

		if ( m_count >= m_listMax )
		{
			AssertMsgOnce( 0, "reached enumerated list limit.  Increase limit or decrease radius" );
			return ITERATION_STOP;
		}

		m_pList[m_count++] = pHandleEntity;
		return ITERATION_CONTINUE;
	}

	int GetCount() const { return m_count; }

private:
	IHandleEntity **m_pList;
	int             m_listMax;
	int             m_count;
};

//-----------------------------------------------------------------------------
// CEntitySphereQuery's twin, answering for both kinds.
//
// The same cursor shape, the same fixed-size list on the stack, and the same
// MAX_SPHERE_QUERY — so a sweep that overflowed before overflows in the same
// place, which is what keeps a converted site comparable with an unconverted
// one.
//-----------------------------------------------------------------------------
class CAnyEntitySphereQuery
{
public:
	CAnyEntitySphereQuery( const Vector &center, float radius )
	{
		CAnyEntitiesEnum sphereEnum( m_pList, ARRAYSIZE( m_pList ) );
		::partition->EnumerateElementsInSphere( PARTITION_ENGINE_NON_STATIC_EDICTS,
		                                        center, radius, false, &sphereEnum );

		m_listIndex = 0;
		m_listCount = sphereEnum.GetCount();
	}

	// Valid until NextEntity and no further: the entity behind it may be
	// removed by the damage the loop deals. The same contract as the SDK's,
	// which holds raw entity pointers across the same window.
	AnyEntity_t GetCurrentEntity() const
	{
		if ( m_listIndex < m_listCount )
			return AnyEntityOf( m_pList[m_listIndex] );

		AnyEntity_t none = { NULL, NULL, NULL, NULL };
		return none;
	}

	inline void NextEntity() { m_listIndex++; }

private:
	int            m_listIndex;
	int            m_listCount;
	IHandleEntity *m_pList[ss::kMaxSphereQuery];
};

#endif // ANYENTITY_H
