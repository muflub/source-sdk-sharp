//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — handle-based entity access ==========================//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// C# addresses entities by HANDLE, never by raw index.
//
// Source reuses entity slots, so an index alone is unsafe: a stale index
// silently addresses whichever entity now occupies the slot. CBaseHandle packs
// the slot index together with a serial number, and resolving one revalidates
// that serial — a handle to a destroyed entity fails closed instead of
// returning the wrong entity.
//
// This is the design from plan.md Part 4, caveat 4, and it deliberately reuses
// Source's own CBaseHandle rather than inventing a parallel handle table.
//
//=============================================================================//

#include "ss_util.h"   // engine; ss::ENTINDEX, ss::UTIL_TraceLine, UTIL_VarArgs
#include "shell_standalone_player.h"
#include "basehandle.h"
#include "sourcesharp.h"
#include "shell_diagnostics.h"
#include "ss_mapentities.h"   // MapEntity_CreateFromKeys, MapEntity_ClaimPendingNonBaseEntity
#include "ss_shareddefs.h"   // ss::kDamageYes
#include "vphysics_interface.h"
#include "standalone_relays.h"
#include "game_entity_relays.h"

#include "collision_group_names.h"
#include "physics_bridge_refusal.h"
// The one CBaseEntity that carries a managed blob; see EntityGetDataBlob.
#include "ss_world.h"
#include "entity_name_match.h"   // EntityNames_Match, for the world arm of the classname walk
#include "ss_takedamageinfo.h"	// server.so's damage info


//-----------------------------------------------------------------------------
// Resolving is shell_resolve.h's: one lookup, serial revalidated, both
// hierarchies answered at once. This file used to keep a private
// SourceSharp_ResolveHandle beside the shared one — the fourth spelling of the
// same question — and every function below asked twice, once per hierarchy.
//-----------------------------------------------------------------------------
#include "shell_standalone_class.h"
#include "slot_table.h"
#include "edict.h"
#include "ss_string.h"

#include <algorithm>
#include <vector>

// shareddefs.h's DAMAGE_YES, by its ss_shareddefs.h name. A using-declaration
// rather than ss:: at the use, because entity_shell_inert_slots_test reads a
// `::` in a slot body as the body reaching something, and that one does not.
using ss::kDamageYes;

// shell_mapparse.cpp defines it; baseentity.h declared it (with the third
// argument defaulted to NULL, which the one call below now passes).
IGameEntity *CreateEntityByNameEither( const char *className, IGameEntity **ppOutBaseEntity,
                                       IServerEntity **ppOutServerEntity );

// The shim's own list of shells, defined in shell_standalone_base.cpp. Declared
// rather than included for the same reason shell_standalone.cpp declares it:
// there is no header that owns it yet, and inventing one here would put it in
// front of a file step 1e is rewriting.
std::vector< CStandaloneEntity * > &StandaloneEntities();

IGameEntity *SourceSharp_GameEntityFromHandleEntity( IHandleEntity *pHandleEntity )
{
	return SourceSharp_ShellFromHandleEntity( pHandleEntity );
}

//-----------------------------------------------------------------------------
// AN ENTITY'S OWN HANDLE -- THE ONE GUARDED CROSSING, AND THE ONLY ONE.
//
// Unarmed a CBaseEntity is an IHandleEntity and answers GetRefEHandle() itself.
// Armed it is an IGameEntity, which is not one -- igameentity.h refuses that
// base deliberately and names GetRefEHandle by name as a thing it will not
// duplicate. GameEntity_RefHandle (game_entity_relays.h) is the relay that crosses to the class
// that does answer it; it is the same ShellOf() cast every other relay makes,
// and it is spelled once, there.
//
// TWO SPELLINGS, ONE CROSSING. The packed uint32 is what C# is handed; the
// CBaseHandle is what native callers that want the entry and the serial
// separately take. Both are the same handle, and putting the guard HERE rather
// than at each of the six call sites is what lets those sites read identically
// armed and unarmed -- which is the difference between six arms a reader has to
// keep in step and one.
//-----------------------------------------------------------------------------
CBaseHandle SourceSharp_RefHandleFromEntity( IGameEntity *pEntity )
{
	if ( !pEntity )
		return CBaseHandle();

	return GameEntity_RefHandle( pEntity );
}

uint32_t SourceSharp_HandleFromEntity( IGameEntity *pEntity )
{
	if ( !pEntity )
		return INVALID_EHANDLE_INDEX;

	return (uint32_t)SourceSharp_RefHandleFromEntity( pEntity ).ToInt();
}

//-----------------------------------------------------------------------------
// Engine API surface handed to C#.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Both kinds of entity, from here on.
//
// A handle names an entity, not a C++ class. Half the entities in this mod are
// no longer CBaseEntity, so every call below tries that first — which is what
// it always did, at no extra cost for the entities that still are — and then
// asks the parallel hierarchy.
//
// Only the calls an entity outside the SDK can actually ANSWER are extended.
// The rest keep resolving to NULL and returning their neutral value, which is
// the honest answer: a logic_timer has no velocity, and inventing one would be
// worse than saying nothing.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityIsValid( uint32_t hEntity )
{
	return SourceSharp_ResolveEntity( hEntity ).IsValid() ? 1 : 0;
}

//-----------------------------------------------------------------------------
// Whether a handle names a player.
//
// Both hierarchies answer it, and both answer it the same way: IsPlayer is a
// virtual on CBaseEntity and on IGameEntity, and neither needs to know which
// kind the other is.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityIsPlayer( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );


	return ( ent.pStandalone && ent.pStandalone->IsPlayer() ) ? 1 : 0;
}

int32_t SourceSharp_EntityGetIndex( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );


	// The world is entity 0 BY DEFINITION -- CWorld::RequiredEdictIndex -- and
	// it is on neither arm below (plan_cworld_trace.md T3). Without this the
	// world enumerated with index -1, the sentinel that means "the client has
	// never heard of this", which is the opposite of true for worldspawn.
	if ( ent.pWorld )
		return ent.EntityIndex();

	// -1 for a server-only entity, which has no edict and therefore no index.
	// That is not a failure: an index is the number the CLIENT knows an entity
	// by, and one the client never hears about does not have one.
	return ent.pStandalone ? ent.pStandalone->EntIndex() : -1;
}

void SourceSharp_EntityGetClassName( uint32_t hEntity, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = '\0';

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	// THE PROJECTION, NOT A THIRD SPELLING OF THE ARMS. This read the two arms
	// itself -- `if ( ent.pBase ) ... else if ( ent.pStandalone ) ...` -- which
	// is exactly what shell_resolve.h's header says the design exists to stop:
	// "a projection cannot disagree with the resolve it projects, which is the
	// property the four spellings never had".
	//
	// IT DISAGREED. SourceSharpEntity_t::Classname() gained a world arm and
	// this did not, so every enumeration reported worldspawn with an EMPTY
	// classname while the projection beside it knew the answer -- and
	// `./debug-api ents "*"` counted the empty string as a sixty-first class,
	// which reads as the count gate passing.
	ss::StrCopy( pOut, ent.Classname(), nOutSize );
}

int32_t SourceSharp_EntityGetHealth( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );


	return ent.pStandalone ? ent.pStandalone->GetHealth() : 0;
}

void SourceSharp_EntitySetHealth( uint32_t hEntity, int32_t nHealth )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	// THE GUARD COVERS THE SDK ARM ONLY, WHICH IS THIS FILE'S RULE EVERYWHERE
	// ELSE (SourceSharp_EntityIsPlayer and SourceSharp_EntityGetIndex a few
	// screens up are the shape). It used to cover BOTH arms here, so armed --
	// where ent.pStandalone is the only arm that can ever be taken -- this
	// function was a silent no-op.
	//
	// WHAT IT COST, measured on a live armed map load: SsPlayer.OnSpawn's
	// `Health = SpawnHealth` wrote nothing, so CStandalonePlayer::GetHealth()
	// stayed 0, so MvIsAlive() (GetHealth() > 0 && !deadflag) was false, so the
	// player spawned DEAD -- `debug-api payload` on the player read Health 0 and
	// Alive false on a fresh spawn. A dead player does not walk, cannot be
	// given a weapon, cannot push a crate, ride a lift or open a door, which is
	// 45 of the live gate's checks descending from this one line.

	if ( ent.pStandalone )
		ent.pStandalone->SetHealth( nHealth );
}

void SourceSharp_EntityGetOrigin( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
		return;

	const Vector *pOrigin = &ent.Origin();

	pOutXYZ[0] = pOrigin->x;
	pOutXYZ[1] = pOrigin->y;
	pOutXYZ[2] = pOrigin->z;
}

void SourceSharp_EntitySetOrigin( uint32_t hEntity, const float *pXYZ )
{
	if ( !pXYZ )
		return;

	const Vector vecOrigin( pXYZ[0], pXYZ[1], pXYZ[2] );

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	// THE GUARD COVERS THE SDK ARM ONLY, WHICH IS THIS FILE'S RULE EVERYWHERE
	// ELSE (SourceSharp_EntityIsPlayer and SourceSharp_EntityGetIndex a few
	// screens up are the shape). It used to cover BOTH arms here, so armed --
	// where ent.pStandalone is the only arm that can ever be taken -- this
	// function was a silent no-op.
	//
	// Armed this was the whole of what managed code could do to place an
	// entity: SourceSharp_EntityGetOrigin two functions up answers off the
	// resolve with no guard at all, so a read came back correct and the
	// matching write did nothing.

	if ( ent.pStandalone )
		ent.pStandalone->SetAbsOrigin( vecOrigin );
}

//-----------------------------------------------------------------------------
// Hands C# a pointer to the entity's data blob.
//
// This is the one place SourceSharp deliberately shares memory rather than
// copying: C# reads and writes these bytes in place. Copying here would be
// fatal at entity-state scale (plan.md Part 3).
//
// A READ NO LONGER MARKS THE EDICT CHANGED (plan_nettable_generic.md Part 9,
// Step 5). It used to, deliberately: C# was handed a pointer and wrote through
// it, nothing native saw the write, and the alternative to marking here was a
// second interop call after every property set. That trade is gone — a bound
// state announces its own writes through the control block in entity_slots.h
// and native drains them once per frame — so this call is a fetch and nothing
// else.
//
// It matters more than it reads. SV_PackEntity skips SendTable_Encode entirely
// for an entity whose edict is unchanged, so marking on read put everything
// anybody merely LOOKED at into a full encode and a full delta compare: the dev
// inspector's /entity/<index>/payload route kept its subject in every one of
// them for as long as somebody watched it, which is the exact cost the payload
// design cannot afford at 512 props.
//
// A write that does NOT go through a bound state — EntityData.TryWrite, which
// takes a handle and a field layout and has no state object anywhere —
// announces itself through SourceSharp_EntityMarkStateChanged instead.
//
// The pointer's lifetime is the entity's. A caller that keeps it must hold a
// control-block slot and its serial with it, and SourceSharp_EntityBindBlob is
// what hands out all four together; everything reaching the blob through here
// must re-fetch.
//-----------------------------------------------------------------------------
void *SourceSharp_EntityGetDataBlob( uint32_t hEntity, int32_t *pOutSize )
{
	if ( pOutSize )
		*pOutSize = 0;

	// The parallel hierarchy first, because a handle there resolves to nothing
	// on the CBaseEntity side and the check below would answer "no storage" for
	// an entity that has some.
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( CStandaloneEntity *pStandalone = ent.pStandalone )
	{
		// THE ENTITY'S OWN LENGTH (Part 18). C# writes through this pointer for
		// the life of the entity and bounds its writes by this number, so a
		// length belonging to some other class's tier is a managed write past
		// the end of the allocation with nothing between here and the heap.
		if ( pOutSize )
			*pOutSize = pStandalone->BlobBytes();

		return pStandalone->DataBlob();
	}

	// THE WORLD CARRIES A BLOB, AND IT IS ON NEITHER OF THE TWO HIERARCHIES
	// ABOVE (plan_cworld_trace.md T3).
	//
	// This used to read `ent.pBase != NULL && ent.pBase == (CBaseEntity *)
	// GetWorldEntity()` -- an identity compare, which was right, made through
	// an arm that has stopped carrying the world. pBase is NULL for worldspawn
	// now, so the test could never fire.
	//
	// AND IT DID NOT, AND THE SYMPTOM WAS ONE THIS FILE'S OWN HISTORY ALREADY
	// NAMES. With the world's blob unreachable, every worldspawn key was
	// refused with "entity 0 ('worldspawn') offers 0", the world networked 76
	// zero words, and both sides agreed about nothing -- word for word the
	// failure world_adapter.cpp records the singleton assignment being moved to
	// fix. Same symptom, different cause, found the same way: by a live run,
	// not by reading.
	//
	// So it asks the arm that carries the world. AnyEntity_t::IsWorld is the
	// same pointer identity against the same singleton, reached through the
	// resolution that already happened one line up rather than by a second
	// GetWorldEntity() and a cast.
	if ( ent.IsWorld() )
	{
		CWorld *pWorld = GetWorldEntity();

		// Zero when the managed side never declared, which is the same fact as
		// "no blob" and is refused by the caller the same way.
		if ( pWorld->BlobBytes() > 0 )
		{
			if ( pOutSize )
				*pOutSize = pWorld->BlobBytes();

			return pWorld->DataBlob();
		}
	}

	// NULL makes the managed read/write fail closed rather than aliasing some
	// other entity's bytes.
	return NULL;
}

//-----------------------------------------------------------------------------
// Whether the generic payload props on this entity's SendTable read the same
// bytes its typed props do (plan_nettable_generic.md Part 14 Step 3).
//
// The resolve lives here because this is where the handle resolver is; the check
// itself lives in shell_nettable.cpp, because that is where the built table is
// and reading the payload back through the props the ENGINE will use is the
// whole point of the check.
//
// -1 for anything that carries no payload, which is most entities and is a real
// answer rather than a failure. Deliberately does NOT mark the edict changed the
// way EntityGetDataBlob does: this is a diagnostic, and one that put its subject
// into every delta compare would change the thing it is measuring.
//
// One shell answers it now; the fork on which C++ class the handle resolved to
// went with the deleted one. The ABI slot did not move and did not need to.
//-----------------------------------------------------------------------------
bool SourceSharp_StandalonePayloadAgrees( CStandaloneEntity *pEntity,
                                          int *pOutWords, int *pOutFirstBadWord );

int32_t SourceSharp_EntityPayloadAgreement( uint32_t hEntity, int32_t *pOutWords,
                                            int32_t *pOutFirstBadWord )
{
	if ( pOutWords )
		*pOutWords = 0;
	if ( pOutFirstBadWord )
		*pOutFirstBadWord = -1;

	int nWords = 0;
	int nFirstBad = -1;
	bool bAgrees = false;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( !ent.pStandalone )
		return -1;

	bAgrees = SourceSharp_StandalonePayloadAgrees( ent.pStandalone, &nWords, &nFirstBad );

	if ( pOutWords )
		*pOutWords = nWords;
	if ( pOutFirstBadWord )
		*pOutFirstBadWord = nFirstBad;

	// No payload props on its table is not a disagreement. Saying 0 there would
	// read as "the two paths differ" in every watch loop and in the UI, which is
	// the one answer that must mean something.
	if ( nWords <= 0 )
		return -1;

	return bAgrees ? 1 : 0;
}

//-----------------------------------------------------------------------------
// Gives a player an item by class name, as the stock loadout code does.
//
// Returns whether the item was actually created: a typo in a class name would
// otherwise fail silently and look like a rules bug rather than a name bug.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Team number, or -1 when the handle is not a player.
//
// -1 rather than 0 because 0 is TEAM_UNASSIGNED, a real team a player can
// legitimately be on — conflating the two would make "not a player" and
// "unassigned" indistinguishable to a rule.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// World items.
//
// The engine-shaped half of "C# defines a pickup": create, model, collision,
// remove. What the thing is and what touching it does stays in managed code.
//-----------------------------------------------------------------------------
uint32_t SourceSharp_CreateEntity( const char *pClassName )
{
	if ( !pClassName || !pClassName[0] )
		return INVALID_EHANDLE_INDEX;

	// CreateEntityByNameEither, NOT CreateEntityByName, and that is the whole of
	// the difference: the second returns a CBaseEntity, and an entity on the
	// parallel hierarchy is not one. It answered NULL and DESTROYED what the
	// factory had just made — so every class that moved off CBaseEntity became
	// uncreatable from C#, and said so in the words of a class that was never
	// registered at all. ss_bolt and ss_rocket were registered perfectly well;
	// they were made and thrown away between two lines.
	//
	// One factory call, and the parallel hierarchy is what it answers with when
	// the class is on it — which, asked from C#, it usually is.
	IGameEntity *pEnt = NULL;
	IGameEntity *pGameEntity = CreateEntityByNameEither( pClassName, &pEnt, NULL );

	if ( pGameEntity )
		return SourceSharp_ShellOf( pGameEntity )->ManagedHandle();

	// A CBaseEntity, which is still some of them.
	if ( pEnt )
		return SourceSharp_HandleFromEntity( pEnt );

	Warning( "[SourceSharp] could not create '%s' — no such entity class, or it is "
		"neither a CBaseEntity nor an IGameEntity\n", pClassName );
	return INVALID_EHANDLE_INDEX;
}

//-----------------------------------------------------------------------------
// Spawns what CreateEntity made.
//
// Separate from creation because the gap between them is where keyvalues and a
// position go: Source hands an entity its keys before it spawns, and spawning
// is what reads them.
//
// DispatchSpawn rather than Spawn() directly, because it is the whole sequence —
// precache, spawn, activate — and skipping it leaves an entity that exists
// without ever having been told to.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySpawn( uint32_t hEntity )
{
	// The parallel hierarchy first, and Spawn() rather than DispatchSpawn:
	// DispatchSpawn takes a CBaseEntity, and the shell's own Spawn is already
	// the whole sequence — it precaches, it applies what the keys set, and it
	// calls the managed OnSpawn last. This is the same call the map loader
	// makes for these, through IGameEntity::Spawn.
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->Spawn();
		return;
	}

	// THE SDK ARM. DispatchSpawn takes a CBaseEntity and lives in util.cpp,
	// which is a removal-set row; armed, ent.pBase is NULL for every handle
	// (GetBaseEntity() answers only for the SDK's hierarchy) so the standalone
	// arm above is the whole of this function. Not a capability removed: the
	// shell's own Spawn() IS the whole precache/spawn/activate sequence, which
	// is what the comment above says and what the map loader already uses.
}

//-----------------------------------------------------------------------------
// Gives an entity a model.
//
// The standalone shell is the only one C# dresses now. The runtime-networked
// shell used to take one too — for the pickups, which rode on it as transport —
// but that shell networks no model index, so the model was a server-side fact
// no client could draw. The pickups moved to the standalone path (SsPickup)
// and the netentity kept only what the round-trip fixture uses, which does not
// include wearing a model.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySetModel( uint32_t hEntity, const char *pModel )
{
	if ( !pModel || !pModel[0] )
		return;

	// Applied rather than recorded: the shell has no Spawn of its own left to
	// run afterwards, because it is the managed OnSpawn that is calling this
	// and the native one has already been.
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetModelName( pModel );
		return;
	}

	// The diagnostic for the wrong KIND of entity. Armed there is no SDK entity
	// left to be wrong about, so the warning goes with the arm it describes.
}

void SourceSharp_EntityMakePickup( uint32_t hEntity, float flSizeX, float flSizeY, float flSizeZ )
{
	// Standalone only. There is no touch function to name: the engine reports
	// the overlap through MarkEntitiesAsTouching and the shell relays it, so
	// being in the trigger list IS being a pickup. The CBaseEntity arm — solid
	// flags plus a SetTouch on the netentity shell — served the pickups when
	// they rode on that shell, and they no longer do; see SsPickup.
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetCollisionBounds( Vector( -flSizeX, -flSizeY, 0 ),
		                                     Vector(  flSizeX,  flSizeY, flSizeZ ) );

		ent.pStandalone->SetSolidity( SOLID_BBOX, FSOLID_TRIGGER | FSOLID_NOT_SOLID );
		return;
	}

}

//-----------------------------------------------------------------------------
// Entity iteration, in Source's own idiom.
//
// Handle-based rather than pointer-based so managed code cannot hold a stale
// pointer between calls — the iteration can span frames, and an entity removed
// in between must fail to resolve rather than dangle.
//-----------------------------------------------------------------------------
uint32_t SourceSharp_FindEntityByClassname( uint32_t hStartAfter, const char *pClassName )
{
	if ( !pClassName || !pClassName[0] )
		return INVALID_EHANDLE_INDEX;

	// Whichever hierarchy the caller is currently walking. A handle that names
	// a standalone entity means the CBaseEntity half is already exhausted, so
	// this continues where it left off rather than starting over.
	const SourceSharpEntity_t start = SourceSharp_ResolveEntity( hStartAfter );

	// THE WORLD FIRST, AND WITHOUT IT THE MAP IS ONE ENTITY SHORT.
	//
	// worldspawn is on NEITHER hierarchy (plan_cworld_trace.md T3), so it was
	// missed by both halves of this walk and every enumeration in the game
	// silently lost it -- `./debug-api ents "*"` read 472 entities in 60 classes
	// on dm_lockdown where it had read 473 in 61, and "worldspawn" was simply
	// not in the answer.
	//
	// FIRST, because a resumable walk needs the singleton at a fixed point in
	// the order and the front is the only one that does not depend on what the
	// rest returns. Resuming FROM the world's own handle leaves `start` empty,
	// so the walk below starts at the beginning -- which is what it already
	// does for a handle that no longer resolves.
	//
	// EntityNames_Match rather than FStrEq, because it is the rule the rest of
	// this walk uses: "*" is the everything-wildcard the dev API and the net
	// traffic profiler both pass, and a plain string compare would exclude the
	// world from exactly the query that means "all of them".
	if ( !start.IsValid() && EntityNames_Match( pClassName, "worldspawn" ) )
	{
		if ( CWorld *pWorld = GetWorldEntity() )
			return (uint32_t)pWorld->GetRefEHandle().ToInt();
	}

	//-------------------------------------------------------------------------
	// THE SDK HALF OF THIS WALK IS GONE, and that is plan_entity.md 1d rather
	// than an oversight. It read
	//
	//     <the global entity list>.FindEntityByClassname( start.pBase, pClassName )
	//
	// which asks CGlobalEntityList to walk its CBaseEntity active list. 3.3
	// says the shim keeps no entity list and asks no list a question: "iterate,
	// find by name, find by class" belong to the managed directory, and 3.16's
	// definition of done removes the SDK hierarchy this half searched. So the
	// question is asked once, of the entities this mod actually has.
	//
	// WHAT IT COSTS UNTIL PHASE 4, said plainly rather than left to be found: a
	// classname query no longer reaches an entity that is still a CBaseEntity.
	// On a loaded map that set is empty -- shell_restoreentities.cpp records
	// stock's own save walk yielding 0 of dm_lockdown's 473 -- but it is NOT
	// empty at runtime, because gib.cpp, physics_prop_ragdoll.cpp and
	// EntityDissolve.cpp still LINK_ENTITY_TO_CLASS and still make one when
	// somebody dies. Those classes leave in Phase 4 and this half leaves with
	// them; until then a `find by classname` for "prop_ragdoll" answers
	// nothing.
	//-------------------------------------------------------------------------
	CStandaloneEntity *pFound =
		StandaloneEntities_FindByClassname( start.pStandalone, pClassName );

	return pFound ? (uint32_t)pFound->GetRefEHandle().ToInt() : INVALID_EHANDLE_INDEX;
}

void SourceSharp_EntityRemove( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	// THE SDK ARM FIRST, and armed it is unreachable: UTIL_Remove takes a
	// CBaseEntity and is defined in util.cpp, a removal-set row, and ent.pBase
	// is NULL for every handle once there is no SDK hierarchy. The parallel
	// arm is CStandaloneEntity::Remove and is the whole of this function armed.

	if ( ent.pStandalone )
		ent.pStandalone->Remove();
}

//-----------------------------------------------------------------------------
// Hitscan attack primitives.
//
// Enough for C# to implement shooting without a weapon entity: trace where the
// player is looking, and apply damage. Range, damage and rate of fire are the
// caller's business — a real CBaseCombatWeapon would drag in viewmodels, ammo
// pools and prediction, none of which are needed to let players kill each other.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// What a player is holding, and where their shots come from.
//
// Both exist for the same reason: a weapon that fires more than one pellet has
// to aim each one separately, and it has to know which weapon it is before it
// knows how many. The client-side accessors are no use — what a shot does is
// decided on the server, and the server cannot ask a client what it is holding.
//-----------------------------------------------------------------------------
void SourceSharp_GetPlayerActiveWeapon( uint32_t hPlayer, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	// Weapons are managed (Phase 10, the Arsenal) and the CBasePlayer inventory
	// these read from retired with CSourceSharpPlayer (13c). The slots stay in
	// the ABI table; the honest answer through them is "no weapon here".
	pOut[0] = '\0';
}

//-----------------------------------------------------------------------------
// THE AMMUNITION A SHOT SPENDS IS GONE FROM HERE (lane 3c-entityshell).
//
// GetPlayerWeaponAmmo, SetPlayerWeaponClip and TakePlayerAmmo emptied out with
// the CBasePlayer inventory in 13c and answered zero afterwards. What decides
// them now is that NOTHING CALLS THEM: not through their Engine.cs wrappers and
// not through their IEnginePlayers facade names (GetWeaponAmmo, SetWeaponClip,
// TakeAmmo), which is the reading that matters -- managed code never names a
// slot, it names the facade method that forwards to one, and a grep for the
// slot name sees only the wrapper layer. The only callers were ArsenalPlayers'
// own pass-throughs, which nothing called in turn.
//
// The bodies went here; the SLOTS went at ABI 249, in the pass that removed
// every marked field of both tables at once. They were left standing until then
// because removing one from the middle of either renumbers every slot after it
// and AbiParityTests pins names in order, so a lane doing it alone would corrupt
// every lane still editing those files.
//
// THIS IS NOT THE DISPOSITION OF THE OTHER TWENTY-FIVE stubs in this file.
// Twenty of those have a live managed rule calling them and receiving nothing,
// and a caller that wants an answer is a requirement rather than a finished
// job. docs/entity-shell.md section 2 lists each with the rule it breaks, and
// sourcesharp/test/entity_shell_inert_slots_test.cpp fails if that set changes
// in either direction.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Being alive, and not.
//
// The cycle itself — how long you wait, what you watch, what ends the wait — is
// a game rule and lives in C#. These are the three things it cannot do itself.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PlayerIsAlive( uint32_t hPlayer )
{
	// The standalone player answers from its own state (Phase 9a).
	if ( CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer ) )
		return pStandalone->MvIsAlive() ? 1 : 0;

	return 0;
}

void SourceSharp_PlayerSetObserverMode( uint32_t hPlayer, int32_t nMode )
{
	if ( CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer ) )
		pStandalone->SetObserver( nMode != SOURCESHARP_OBS_NONE, nMode );
}

int32_t SourceSharp_PlayerGetObserverMode( uint32_t hPlayer )
{
	if ( CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer ) )
		return pStandalone->MvGetObserverMode();

	return SOURCESHARP_OBS_NONE;
}

//-----------------------------------------------------------------------------
// Putting a dead player back in the world.
//
// Through the engine's own respawn rather than by setting health and moving
// them: the spawn point choice, the loadout hook and the spawn event all hang
// off it, and doing it by hand would skip every one of them.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerRespawn( uint32_t hPlayer )
{
	if ( CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer ) )
	{
		// Spawn IS the respawn on the hierarchy: the same spawn-point ask,
		// the same managed OnSpawn resetting the game state.
		pStandalone->SetObserver( false, SOURCESHARP_OBS_NONE );
		pStandalone->Spawn();
	}
}

//-----------------------------------------------------------------------------
// Throwing a weapon into the world.
//
// WHETHER a dead player drops anything is a rule about how a deathmatch feels,
// and it is C#'s. This is the part that is not: unhooking the weapon from its
// owner, giving it a body, and letting it be picked up again.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PlayerDropActiveWeapon( uint32_t hPlayer, const float *pVelocityXYZ )
{
	// Retired with the CBasePlayer inventory (13c): dropping is the managed
	// Arsenal's decision and mechanics now. The ABI slot answers "nothing".
	return 0;
}

//-----------------------------------------------------------------------------
// Putting a weapon away and taking it back out.
//
// The two halves are one call each because what is indivisible is the
// bookkeeping, not the choice. Holstering means four things that have to happen
// together — remember what was out, let the weapon run its own cleanup, empty
// the hands, and blank the viewmodels — and a caller that got three of them
// leaves a gun hanging in mid-air or has nothing left to give back.
//
// WHY a player is empty-handed is a decision and is not here.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PlayerHolsterActiveWeapon( uint32_t hPlayer )
{
	// Retired with the CBasePlayer inventory (13c) — the Arsenal holsters.
	return 0;
}

int32_t SourceSharp_PlayerDeployLastWeapon( uint32_t hPlayer )
{
	// Retired with the CBasePlayer inventory (13c) — the Arsenal deploys.
	return 0;
}

//-----------------------------------------------------------------------------
// Emptying a player out.
//
// The mirror of GivePlayerItem. Distinct from PlayerDropActiveWeapon in a way a
// player can see: that one gives a weapon a body in the world for somebody else
// to collect, while this destroys everything carried.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerRemoveAllItems( uint32_t hPlayer, int32_t bStripSuit )
{
	// Retired with the CBasePlayer inventory (13c) — the Arsenal empties out.
}

//-----------------------------------------------------------------------------
// How fast one player's world runs, and which buttons of theirs are ignored.
//
// Both are per-player engine state with no other way in from managed code. The
// lagged movement value in particular cannot be a Movement rule: movement is
// predicted, and only the engine can network a per-player factor so that the
// client's copy of the movement loop scales by the same number. See
// PlayerSetLaggedMovement in sourcesharp.h.
//-----------------------------------------------------------------------------
// NOTE (13c): the per-player movement modifiers below acted on CBasePlayer
// state and were ALREADY inert for the standalone player before the flag
// flipped — the standalone loop reads its own state (MvGetLaggedMovementValue
// is a constant 1.0). Wiring them into the standalone player is a Movement
// port that happens when a managed rule actually asks for it; until then the
// ABI slots stay and answer by doing nothing, exactly as they observably did.
void SourceSharp_PlayerSetLaggedMovement( uint32_t hPlayer, float flValue )
{
}

void SourceSharp_PlayerSetButtonsDisabled( uint32_t hPlayer, int32_t nButtonMask,
                                           int32_t bDisabled )
{
}

//-----------------------------------------------------------------------------
// Buttons held down FOR a player, whether they are pressing them or not.
//
// The mirror of SourceSharp_PlayerSetButtonsDisabled and shaped the same way for
// the same reason: only the named bits move, either way, so a caller gives back
// exactly what it took and does not release something another rule is holding
// down. Which buttons, and when, is the decision and is not here.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerForceButtons( uint32_t hPlayer, int32_t nButtonMask, int32_t bForced )
{
}

//-----------------------------------------------------------------------------
// Whether the engine may move a player without being asked.
//
// The step up onto a stair, the slide along a wall, the ladder mount: all of
// them move the player somewhere they did not steer, and a few situations want
// that suppressed rather than fought.
//
// An engine call and not a Movement rule, for the reason PlayerSetLaggedMovement
// spells out at length: movement is predicted on both sides, and this bool is
// networked so that both sides run the loop the same way. Deciding WHEN to
// suppress it stays in C#.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerSetAllowAutoMovement( uint32_t hPlayer, int32_t bAllow )
{
	// The standalone player's m_Local is reachable, but a server-only write
	// would mispredict — the client's replay never hears it. Wiring this means
	// putting the bit on the player's declared state, when a rule first asks.
}

//-----------------------------------------------------------------------------
// Whether a player wades rather than walks.
//
// Stock sets this together with PFLAG_VPHYSICS_MOTIONCONTROLLER
// (triggers.cpp:4689); that flag is managed state now (SsPlayer.PhysicsFlags,
// read by PlayerShadowRecovery), so the caller sets it beside this call.
//
// STOCK BUG, reproduced rather than fixed: m_bSlowMovement is a CNetworkVar that
// playerlocaldata.cpp never adds to the send table, and its datadesc line is
// commented out -- while gamemovement.cpp reads it on BOTH sides. So the client
// never learns it is set and predicts full speed, in stock exactly as here.
// Setting it from managed code therefore behaves identically to stock, including
// the misprediction; fixing it would mean a send table change, which is a
// different decision from this call.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerSetSlowMovement( uint32_t hPlayer, int32_t bSlow )
{
	// Read into the movement block: no jump in a stasis field (gamemovement.cpp:2401).
	if ( CStandalonePlayer *pPlayer = SourceSharp_ResolveStandalonePlayer( hPlayer ) )
		pPlayer->MvLocal().m_bSlowMovement = ( bSlow != 0 );
}

//-----------------------------------------------------------------------------
// Keeps a player near a point, sluggishly rather than by a wall.
//
// One call for the five fields ActivateMovementConstraint writes, because they
// are one setting and not five: a centre with no radius constrains nothing, a
// radius with no centre constrains to the world origin, and a caller able to set
// them one at a time can only be wrong in between. A radius of 0 deactivates,
// which is how the engine itself already spells "not constrained".
//
// The constraint ENTITY is deliberately left NULL. Stock stores one only so that
// a player can be released when it dies, and C# holding a handle it can act on
// is a better answer than native watching an entity on C#'s behalf -- that would
// be native deciding when the constraint ends.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerSetMovementConstraint( uint32_t hPlayer, const float *pCenterXYZ,
                                             float flRadius, float flWidth,
                                             float flSpeedFactor )
{
	// CBasePlayer-only state the shared loop read from m_Local; already inert
	// for the standalone player before 13c. Wire when a managed rule asks.
}

//-----------------------------------------------------------------------------
// Whether this body, when it dies, becomes a ragdoll the SERVER simulates.
//
// The flag is m_bForceServerRagdoll on CBaseCombatCharacter, and
// CBaseCombatCharacter::BecomeRagdoll is the only thing that reads it. So the
// question a caller can ask -- "is this thing a combat character?" -- is one
// only the native side can answer, and it is asked here rather than exposed:
// MyCombatCharacterPointer() returns NULL for everything that is not one, and
// that includes every entity on the standalone hierarchy, which is not a
// CBaseEntity at all and so never reaches this line.
//
// Silently ignoring a non-combat-character is deliberate. A trigger volume is
// swept by whatever walks into it and almost none of that has a ragdoll; a
// warning here would be one per prop per crossing.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySetForceServerRagdoll( uint32_t hEntity, int32_t bForce )
{
	// 13c: with basecombatcharacter.cpp out of the build nothing left in it
	// derives from CBaseCombatCharacter, so MyCombatCharacterPointer() answers
	// NULL for every entity and this slot has nothing to write to. It stays
	// for the ABI, answering by doing nothing — exactly what it observably did.
}

// SourceSharp_EntityBecomePhysicsTrigger (BecomeTrigger / RemoveTrigger on
// the entity's object) is managed's since plan_physics_bones_native_zero (the
// last thin forwards, 2026-10-12): EntityPhysicsMotion.BecomeTrigger, through
// PhysicsObjectVtbl. Its slot left with it.

void SourceSharp_GetPlayerShootOrigin( uint32_t hPlayer, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	// EyePosition is what the stock weapon code fires from — using the entity
	// origin instead puts every shot at the player's feet.
	if ( CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer ) )
	{
		const Vector vecEye = pStandalone->EyePosition();

		pOutXYZ[0] = vecEye.x;
		pOutXYZ[1] = vecEye.y;
		pOutXYZ[2] = vecEye.z;
		return;
	}

}

uint32_t SourceSharp_TracePlayerAim( uint32_t hPlayer, float flRange, float *pOutHitXYZ )
{
	// The standalone player is the only player (13c). Its eyes and view angles
	// live on its own state — EyePosition through the loop-maintained view
	// offset, angles in the engine-facing CPlayerState.
	CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer );
	if ( !pStandalone )
		return INVALID_EHANDLE_INDEX;

	const Vector vecStart = pStandalone->EyePosition();

	Vector vecForward;
	AngleVectors( pStandalone->PlayerStatePtr()->v_angle, &vecForward );

	const Vector vecEnd = vecStart + vecForward * flRange;

	// MASK_SHOT and skipping the shooter, exactly as the stock weapon code
	// traces — anything else would let a player shoot themselves, or let shots
	// pass through cover that stops real bullets. A standalone entity is
	// already an IHandleEntity, so the filter takes it directly.
	trace_t tr;
	ss::UTIL_TraceLine( vecStart, vecEnd, MASK_SHOT, pStandalone, COLLISION_GROUP_NONE, &tr );

	if ( pOutHitXYZ )
	{
		pOutHitXYZ[0] = tr.endpos.x;
		pOutHitXYZ[1] = tr.endpos.y;
		pOutHitXYZ[2] = tr.endpos.z;
	}

	// The SUBJECT of this trace is a player and always will be. What it HIT is
	// any entity at all, and reporting a managed brush as the world made
	// `debug-api aim` -- the tool every other measurement here is checked with --
	// name the wrong thing for half the entities in the mod.
	return SourceSharp_HandleFromAnyEntity( tr.m_pEnt );
}

void SourceSharp_ApplyDamage( uint32_t hVictim, uint32_t hAttacker, float flDamage, int32_t nDamageType )
{
	// The parallel hierarchy, which has no VPhysics to push and no death
	// sequence of its own: the shell relays the damage, managed code answers how
	// much lands, and the shell applies it. Everything the comment below says
	// about inflictors applies to CBaseEntity victims only — nothing on this
	// side reads one.
	const SourceSharpEntity_t victim = SourceSharp_ResolveEntity( hVictim );
	IGameEntity *pAttacker = SourceSharp_ResolveEntity( hAttacker ).GameEntity();

	if ( victim.pStandalone )
	{
		ss::CTakeDamageInfo standaloneInfo( pAttacker, pAttacker, flDamage, nDamageType );

		victim.pStandalone->TakeDamage( standaloneInfo );
		return;
	}

	// The WHOLE SDK tail, because the standalone arm above it returns. Everything
	// in here is about a CBaseEntity victim -- the function's own comment says so
	// -- and WorldSpaceCenter / TakeDamage are neither of them one of
	// IGameEntity's six. The parallel arm is complete on its own: it builds its
	// own CTakeDamageInfo and calls the standalone TakeDamage.
}

//-----------------------------------------------------------------------------
// Damage with the force and position managed worked out: stock's
// CTakeDamageInfo after CalculateBulletDamageForce (takedamageinfo.cpp:332),
// so the victim's OnTakeDamage can push its physics object
// (plan_physics_managed A30). Carry-out only: the info is built and delivered.
//
// The attacker is resolved on both hierarchies (SourceSharpEntity_t::GameEntity).
//-----------------------------------------------------------------------------
void SourceSharp_ApplyDamageForce( uint32_t hVictim, uint32_t hAttacker, float flDamage, int32_t nDamageType,
                                   const float *pForceXYZ, const float *pPositionXYZ )
{
	const SourceSharpEntity_t victim = SourceSharp_ResolveEntity( hVictim );
	if ( !victim.pStandalone )
		return;

	IGameEntity *pAttacker = SourceSharp_ResolveEntity( hAttacker ).GameEntity();

	ss::CTakeDamageInfo info( pAttacker, pAttacker, flDamage, nDamageType );
	if ( pForceXYZ )
		info.SetDamageForce( Vector( pForceXYZ[0], pForceXYZ[1], pForceXYZ[2] ) );
	if ( pPositionXYZ )
		info.SetDamagePosition( Vector( pPositionXYZ[0], pPositionXYZ[1], pPositionXYZ[2] ) );

	victim.pStandalone->TakeDamage( info );
}

int32_t SourceSharp_GetPlayerTeam( uint32_t hPlayer )
{
	// Unassigned rather than "not a player": the rules entities gate on a
	// non-negative answer, and a standalone player is a player.
	if ( CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer ) )
		return pStandalone->TeamNumber();

	return -1;
}

//-----------------------------------------------------------------------------
// Put a player on a team: the store of CBaseEntity::ChangeTeam
// (baseentity.cpp:4947 -- `m_iTeamNum = iTeamNum`), and nothing else.
//
// NATIVE MAKES NO DECISION. The number is not validated -- an out-of-range
// team lands like any other -- because which teams exist, which ones accept
// a joiner and what a team change costs (items, observer state, the
// player_team event) is managed policy: SourceSharp.Server's ITeams and its
// single-owner IJoinTeamPolicy. A non-player handle is simply nobody to
// move, matching the void arms of every other setter here.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerSetTeam( uint32_t hPlayer, int32_t nTeam )
{
	if ( CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer ) )
		pStandalone->ChangeTeam( nTeam );
}

//-----------------------------------------------------------------------------
// The engine's own name for a player, which is what its game events carry.
//
// Zero rather than -1 for a non-player, matching what the engine writes into an
// event's attacker field when the world did the killing. A caller comparing an
// event's userid against this therefore needs no special case for the world.
//-----------------------------------------------------------------------------
int32_t SourceSharp_GetPlayerUserId( uint32_t hPlayer )
{
	// GetPlayerUserId goes to the engine by edict, and a player mid-disconnect
	// still resolves as an entity while its edict is already unbound.
	//
	// NOTE (13c): before the flip this answered 0 for every standalone player —
	// it only knew the CBasePlayer list. Answering from the standalone edict is
	// strictly less wrong: the same engine call stock's GetUserID made.
	if ( CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer ) )
	{
		edict_t *pEdict = engine->PEntityOfEntIndex( pStandalone->EntIndex() );
		return pEdict ? engine->GetPlayerUserId( pEdict ) : 0;
	}

	return 0;
}

//-----------------------------------------------------------------------------
// Gives ammo. Returns what was actually added — the engine clamps to the carry
// limit, and a caller that wants to know whether the pickup was worth consuming
// needs the difference.
//-----------------------------------------------------------------------------
int32_t SourceSharp_GivePlayerAmmo( uint32_t hPlayer, int32_t nCount, const char *pAmmoName )
{
	// Retired with the CBasePlayer inventory (13c) — the Arsenal carries ammo.
	return 0;
}

int32_t SourceSharp_GivePlayerItem( uint32_t hPlayer, const char *pClassName )
{
	// Retired with the CBasePlayer inventory (13c) — the Arsenal grants items.
	return 0;
}

uint32_t SourceSharp_GetPlayerHandle( int32_t nPlayerIndex )
{
	if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromIndex( nPlayerIndex ) )
		return pStandalone->ManagedHandle();

	return INVALID_EHANDLE_INDEX;
}

//-----------------------------------------------------------------------------
// Player armour.
//
// The engine keeps it separately from health and nothing else in this API
// reaches it, so a battery had no honest way to do anything before this.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityGetArmor( uint32_t hEntity )
{
	// CBasePlayer's m_ArmorValue retired with it (13c); armour is a managed
	// number on the player's declared state when a rule wants one. Already 0
	// for the standalone player before the flip.
	return 0;
}

void SourceSharp_EntitySetArmor( uint32_t hEntity, int32_t nArmor )
{
}

//-----------------------------------------------------------------------------
// Shows or hides a pickup without destroying it.
//
// A map item that respawns has to keep its entity: a mapper may have wired
// outputs to it, and those connections were bound when the map loaded and cannot
// be rebuilt. So collecting one makes it invisible and untouchable, and its
// respawn makes it solid again.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySetPickupEnabled( uint32_t hEntity, int32_t bEnabled )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( CStandaloneEntity *pStandalone = ent.pStandalone )
	{
		pStandalone->SetVisible( bEnabled != 0 );

		// Out of the trigger list entirely rather than merely invisible: a
		// pickup that has been taken must not be collectable while it waits to
		// respawn, and leaving it in the list would collect it again on the
		// next tick the player is still standing there.
		pStandalone->SetSolidity( bEnabled ? SOLID_BBOX : SOLID_NONE,
		                          bEnabled ? ( FSOLID_TRIGGER | FSOLID_NOT_SOLID )
		                                   : FSOLID_NOT_SOLID );
		return;
	}

	// Standalone only, like MakePickup: nothing on the CBaseEntity side is a
	// pickup any more, and silently toggling EF_NODRAW on one would hide a
	// caller's mistake rather than serve anything.
}

//-----------------------------------------------------------------------------
// Whether a brush is drawn.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySetVisible( uint32_t hEntity, int32_t bVisible )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetVisible( bVisible != 0 );
		return;
	}

	// EF_NODRAW lives in m_fEffects, a CBaseEntity network var. The standalone
	// arm above returns; armed, only it is left.
}

//-----------------------------------------------------------------------------
// Whether a brush can be walked through, walked into, or neither.
//
// Three states rather than a flag, because a map brush is any of them and the
// difference is not "solid or not": a trigger is passable AND reports being
// entered, which is a third thing.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySetBrushSolidity( uint32_t hEntity, int32_t nSolidity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( CStandaloneEntity *pStandalone = ent.pStandalone )
	{
		// SOLID_BSP either way: what changes is the FLAGS, not what shape the
		// engine traces against. A trigger volume is the same brushes as a
		// wall, described as passable and reporting.
		switch ( nSolidity )
		{
		case 0: // passable, and says nothing about it
			pStandalone->SetSolidity( SOLID_BSP, FSOLID_NOT_SOLID );
			break;

		case 2: // passable, and reports being entered
			pStandalone->SetSolidity( SOLID_BSP, FSOLID_NOT_SOLID | FSOLID_TRIGGER );
			break;

		default: // solid
			pStandalone->SetSolidity( SOLID_BSP, 0 );
			break;
		}

		return;
	}

}

//=============================================================================//
// Movement.
//
// What a trigger volume does to whoever is standing in it: push them, throw
// them, teleport them, change how they fall or how well they grip. None of it
// was reachable from C# before.
//=============================================================================//

void SourceSharp_EntityGetAngles( uint32_t hEntity, float *pOutPYR )
{
	if ( !pOutPYR )
		return;

	pOutPYR[0] = pOutPYR[1] = pOutPYR[2] = 0.0f;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	const QAngle *pAngles = NULL;

	// THE GUARD COVERS THE SDK ARM ONLY, WHICH IS THIS FILE'S RULE EVERYWHERE
	// ELSE (SourceSharp_EntityIsPlayer and SourceSharp_EntityGetIndex a few
	// screens up are the shape). It used to cover BOTH arms here, so armed --
	// where ent.pStandalone is the only arm that can ever be taken -- this
	// function was a silent no-op.
	//
	// Armed this answered (0,0,0) for EVERY entity -- and zero is a valid angle,
	// so nothing downstream could tell it from an entity that really was
	// unrotated.
	if ( ent.pStandalone )
		pAngles = &ent.pStandalone->GetAbsAngles();

	if ( !pAngles )
		return;

	pOutPYR[0] = pAngles->x;
	pOutPYR[1] = pAngles->y;
	pOutPYR[2] = pAngles->z;
}

//-----------------------------------------------------------------------------
// How fast it is going, and telling it how fast to go: the shell's own member.
//
// WHICH HOME A VELOCITY LIVES IN IS MANAGED'S since plan_physics_bones_native_zero
// (the last thin forwards, 2026-10-12): EntityPhysicsMotion asks the entity's
// IPhysicsObject itself when there is one (through PhysicsObjectVtbl, the
// solver's spin converted between the object's axes and the world's) and
// crosses here only when there is not. So these keep the shell's own member
// and nothing else; a handle that names no shell is a no-op, and a getter
// answers zero for it.
//-----------------------------------------------------------------------------
void SourceSharp_EntityGetVelocity( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveEntity( hEntity ).pStandalone )
	{
		const Vector &vecVelocity = pStandalone->GetVelocity();
		pOutXYZ[0] = vecVelocity.x;
		pOutXYZ[1] = vecVelocity.y;
		pOutXYZ[2] = vecVelocity.z;
	}
}

void SourceSharp_EntitySetVelocity( uint32_t hEntity, const float *pXYZ )
{
	if ( !pXYZ )
		return;

	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveEntity( hEntity ).pStandalone )
		pStandalone->SetVelocity( Vector( pXYZ[0], pXYZ[1], pXYZ[2] ) );
}

// SourceSharp_EntityApplyVelocityImpulse is managed's since
// plan_physics_bones_native_zero (the last thin forwards, 2026-10-12), and its
// decision with it: the impulse was applied as ApplyForceCenter( impulse *
// mass ); EntityPhysicsMotion.ApplyVelocityImpulse is stock's
// ApplyAbsVelocityImpulse (a zero impulse skipped, CheckEntityVelocity, then
// AddVelocity). Its slot left with it.

//-----------------------------------------------------------------------------
// Moves something somewhere, optionally facing a new way.
//
// A single call rather than setting origin and angles separately, because the
// engine has to unlink and relink the entity as one operation — doing it in
// pieces leaves it briefly inside whatever it was moved out of.
//-----------------------------------------------------------------------------
void SourceSharp_EntityTeleport( uint32_t hEntity, const float *pOrigin,
                                 const float *pAngles, int32_t bZeroVelocity )
{
	if ( !pOrigin )
		return;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
		return;

	Vector vecOrigin( pOrigin[0], pOrigin[1], pOrigin[2] );

	QAngle angles;
	if ( pAngles )
		angles.Init( pAngles[0], pAngles[1], pAngles[2] );

	Vector vecZero( 0, 0, 0 );


	// The same operation spelled out, because there is no Teleport to call: the
	// unlink-and-relink CBaseEntity::Teleport does is what SetAbsOrigin does on
	// this side, and it is one call rather than an ordering to get wrong.
	//
	// Angles BEFORE the origin. SetAbsAngles only stores; SetAbsOrigin is what
	// rebuilds the transform, moves the entity in the partition and marks the
	// edict, so it has to be the last of the two or the client is told about the
	// old facing.
	if ( pAngles )
		ent.pStandalone->SetAbsAngles( angles );

	ent.pStandalone->SetAbsOrigin( vecOrigin );

	// Through SetVelocity rather than by clearing a member, because that is also
	// what ends an arc in flight -- a teleported projectile asked to stop must
	// stop, not carry on from its new position.
	if ( bZeroVelocity )
		ent.pStandalone->SetVelocity( vecZero );

	// A SIMULATED entity's object is moved by managed, after this returns:
	// EntityPhysicsMotion.TeleportObject puts it at the same pose as a
	// teleport, stops it when asked and wakes it (plan_physics_bones_native_zero,
	// the last thin forwards, 2026-10-12). Without that the next physics update
	// reads the old pose back onto the entity.
	//
	// A player's physics object is its movement shadow, and stock's
	// TeleportEntity moves it with them. That is managed's since
	// plan_physics_bones_native_zero 2.5: EngineSide.EntityTeleport hands the
	// teleport to PlayerShadowBodies.Teleport after this returns.
}

void SourceSharp_EntitySetGravity( uint32_t hEntity, float flGravity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetGravityScale( flGravity );
		return;
	}

}

void SourceSharp_EntitySetFriction( uint32_t hEntity, float flFriction )
{
	// SDK-only from the start: friction is m_flFriction on CBaseEntity and the
	// standalone hierarchy never had a parallel for it, so armed this function
	// does nothing -- which is what it already did for half the mod's entities.
}

//-----------------------------------------------------------------------------
// How many times a projectile may bounce before its flight ends.
//
// Native rather than managed because the surface normal only exists inside the
// step trace: C# is told what was hit, never which way it was facing, so a
// reflection cannot be computed on that side at all.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySetBounce( uint32_t hEntity, int32_t nBounces )
{
	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveStandalone( hEntity ) )
		pStandalone->SetBounces( nBounces );
}

//-----------------------------------------------------------------------------
// Who owns something, which for a projectile is who fired it.
//
// Source's own trace filters already know what to do with this — see
// PassServerEntityFilter, "don't clip against own missiles" — so an owner is
// the standard way of saying "not this one" rather than a rule invented here.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySetOwner( uint32_t hEntity, uint32_t hOwner )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetOwnerHandle( hOwner );
		return;
	}

}

//-----------------------------------------------------------------------------
// Finds an entity by the name a mapper gave it, which is how a map refers to
// one entity from another — a teleport naming its destination, say.
//-----------------------------------------------------------------------------
// Where something looks from, and which way.
//
// Not the same as origin and angles: a player's origin is at their feet and
// their body angles are not their view angles. Anything asking "can they see
// this" needs the eye, not the entity.
//-----------------------------------------------------------------------------
void SourceSharp_EntityGetEyePosition( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
		return;

	// Origin plus a view offset for a CBaseEntity, and the origin alone for
	// anything on the parallel hierarchy that is not a PLAYER — which has no
	// view offset because it has no eyes. That much is not a gap: EyePosition on
	// a stock non-player entity is its origin too, and logic_measure_movement
	// measuring a managed brush wants exactly that rather than the zeros it used
	// to get.
	//
	// The standalone player is the exception and was being answered as if it
	// were not one: it carries m_vecViewOffset, VEC_VIEW from spawn, and its
	// eyes are 64 units above its feet like everybody else's. Read as the origin
	// it answered z≈1.8 standing on the floor — the eyes of somebody lying down
	// — so every "can they see this" question this ABI serves was asked from
	// ankle height. GetEyeAngles below already knew about the standalone player
	// and SourceSharp_GetPlayerShootOrigin already calls EyePosition(); this was
	// the one of the three that did not.
	Vector vecEye;

	// The SDK arm is the HEAD of an else-if chain, so guarding it would leave a
	// dangling `else if`. The chain is re-spelled as early returns into vecEye
	// instead: same three answers, same order, and the head can now be removed
	// on its own. EyePosition is not one of IGameEntity's six.
	bool bHaveEye = false;

	if ( !bHaveEye )
	{
		if ( CStandalonePlayer *pStandalonePlayer =
		         SourceSharp_ResolveStandalonePlayer( hEntity ) )
		{
			vecEye = pStandalonePlayer->EyePosition();
		}
		else
		{
			vecEye = ent.Origin();
		}
	}

	pOutXYZ[0] = vecEye.x;
	pOutXYZ[1] = vecEye.y;
	pOutXYZ[2] = vecEye.z;
}

void SourceSharp_EntityGetEyeAngles( uint32_t hEntity, float *pOutPYR )
{
	if ( !pOutPYR )
		return;

	pOutPYR[0] = pOutPYR[1] = pOutPYR[2] = 0.0f;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
		return;

	// As above: a player's view angles are not their body angles, and for
	// everything else the two are the same thing. The standalone player keeps
	// its view angles in CPlayerState, same as stock — its body angles carry no
	// pitch at all, which aimed every hierarchy-fired shot along the horizon.
	QAngle angles;

	// Re-spelled for the same reason as EyePosition above: the SDK arm is the
	// head of an else-if chain and cannot be guarded where it stands.
	bool bHaveAngles = false;

	if ( !bHaveAngles )
	{
		if ( CStandalonePlayer *pStandalonePlayer =
		         SourceSharp_ResolveStandalonePlayer( hEntity ) )
		{
			angles = pStandalonePlayer->PlayerStatePtr()->v_angle;
		}
		else
		{
			angles = ent.pStandalone->GetAbsAngles();
		}
	}

	pOutPYR[0] = angles.x;
	pOutPYR[1] = angles.y;
	pOutPYR[2] = angles.z;
}

//-----------------------------------------------------------------------------
// The middle of something's bounding box.
//
// A brush entity's origin is usually at the map's world origin rather than
// anywhere near the brush, so anything positioning relative to a volume has to
// ask for its centre instead.
//-----------------------------------------------------------------------------
void SourceSharp_EntityGetWorldSpaceCenter( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
		return;

	// The midpoint of the collision box for both, which for a point entity is
	// its origin and for a brush is nowhere near it. This used to answer a
	// standalone entity's ORIGIN, with a note saying nothing on that hierarchy
	// had real bounds yet -- every brush in the mod does now, and a func_door
	// answering the world origin is a number that looks right and is not.
	const Vector vecCenter = ent.Center();

	pOutXYZ[0] = vecCenter.x;
	pOutXYZ[1] = vecCenter.y;
	pOutXYZ[2] = vecCenter.z;
}

//=============================================================================//
// Host.
//
// A way to run a console command, for the development API. (The command line
// is read in C# since plan_engineapi_vtables retired its two slots.)
//=============================================================================//

//-----------------------------------------------------------------------------
// Runs a console command as if it had been typed at the server console.
//
// ServerExecute follows immediately so the command runs NOW rather than being
// queued to the next frame. The development API needs the effect to have
// happened by the time it answers the request that asked for it.
//-----------------------------------------------------------------------------
void SourceSharp_ExecuteServerCommand( const char *pCommand )
{
	if ( !pCommand || !pCommand[0] )
		return;

	engine->ServerCommand( UTIL_VarArgs( "%s\n", pCommand ) );
	engine->ServerExecute();
}

//-----------------------------------------------------------------------------
// The same, aimed at ONE client's console rather than the server's.
//
// The newline is added here rather than asked of the caller because it is not
// part of the command: a client's buffer executes what it has up to the
// terminator, so a command sent without one sits there until the next one pushes
// it through. A caller that had to remember would forget once.
//
// "%s" as the format rather than the command itself, which matters more here
// than it looks -- a command carrying a percent sign would otherwise be read as
// a format specifier and print from the stack, and the text comes from map data.
//-----------------------------------------------------------------------------
void SourceSharp_SendClientCommand( uint32_t hPlayer, const char *pCommand )
{
	if ( !pCommand || !pCommand[0] )
		return;

	CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer );
	if ( !pStandalone )
		return;

	// A player mid-disconnect still resolves as an entity while its edict is
	// already unbound, and the engine dereferences that edict without checking.
	edict_t *pEdict = engine->PEntityOfEntIndex( pStandalone->EntIndex() );
	if ( !pEdict || !pEdict->GetUnknown() )
		return;

	engine->ClientCommand( pEdict, "%s\n", pCommand );
}

//-----------------------------------------------------------------------------
// Adds to a player's score.
//
// The engine keeps score on the player rather than as a field anything else
// reads, so it needs its own call. AllowNegative is the engine's own flag: some
// maps score by penalty and want it, and most do not and would rather a stray
// negative be ignored than take points away.
//-----------------------------------------------------------------------------
void SourceSharp_AddPlayerScore( uint32_t hPlayer, int32_t nPoints, int32_t bAllowNegative )
{
	// CBasePlayer's engine-side frag counter retired with it (13c). The score
	// a player sees is the managed game's number; already inert for the
	// standalone player before the flip.
}

//-----------------------------------------------------------------------------
// The four calls a managed point_template makes.
//
// point_template used to be the standing example of "map-parse machinery, so it
// stays native". That was never wrong about the machinery and was always wrong
// about the entity. What it DECIDES -- which names it claims, whether it eats
// them, whether the copies get unique names, and where each copy goes -- is C#
// now, in SourceSharp.Server's PointTemplate. What is below is the residue:
// keyvalue text, the parser that turns text back into an entity, and the
// ordered spawn. None of those is a decision and none of them can move.
//
// The seam is a TRANSFORM, which is the reason the residue is this small. A
// template is a list of (index, place, facing); the arithmetic that turns
// "where the source stood" into "where the copy goes" is SourceSharp.Abi's
// Transform, held to the mathlib it replaced by the golden table in
// sourcesharp/test/template_transform_cases.txt.
//-----------------------------------------------------------------------------
int32_t SourceSharp_TemplateCapture( const char *pszSourceName, int32_t bRemoveSources,
                                     int32_t *pOutIndices, float *pOutOriginsXYZ,
                                     float *pOutAnglesPYR, int32_t nMax )
{
	SOURCESHARP_TRACE();

	// int32_t through the boundary and int inside it. The same width on every
	// platform this builds for, and passed through rather than copied because
	// the count can be zero and a zero-length copy would still want a buffer.
	return MapEntity_TemplateCapture( pszSourceName, bRemoveSources != 0,
	                                  (int *)pOutIndices, pOutOriginsXYZ, pOutAnglesPYR,
	                                  nMax );
}

// SourceSharp_TemplateReconnectIO AND SourceSharp_TemplateSchedulePrecache
// STOOD HERE and are gone (plan_server_all.md Wave 1, docs/plan-audit.md D11).
// Both forwarded to game/server/TemplateEntities.cpp, which has left this
// build: the store, the entity-I/O rewriting over it and the deferred precache
// list are SourceSharp.Server's TemplateStore, so both are one managed object
// calling another and neither is a crossing.

//-----------------------------------------------------------------------------
// A template instance from parsed pairs (plan_server_novalve.md P5).
//
// CPointTemplate::CreateInstance with the deciding AND the parsing taken out.
// Managed (PointTemplate) chose which copy of each template, parsed it with
// MapEntityBlock, and resolved every transform; what crosses is, per entity,
// its classname (pClassNames: nCount NUL-terminated names end to end) and its
// pairs (pPackedKeys: one key\0value\0... pack per entity, end to end,
// pPackBytes[i] long -- MapSpawnCreate's payload, classname pair included).
// The loop is create-and-key, claim, place, spawn: an entity is placed and
// spawned on the spot and claimed out of the pending queue first, or it would
// sit unspawned until the next level load.
//
// Answers how many were CREATED, which can exceed what fits in pOutHandles; a
// caller with a smaller buffer can tell it missed some.
//-----------------------------------------------------------------------------
int32_t SourceSharp_TemplateSpawnKeyed( const char *pClassNames, const char *pPackedKeys,
                                        const int32_t *pPackBytes, const float *pOriginsXYZ,
                                        const float *pAnglesPYR, int32_t nCount,
                                        uint32_t *pOutHandles, int32_t nMax )
{
	SOURCESHARP_TRACE();

	if ( !pClassNames || !pPackBytes || !pOriginsXYZ || !pAnglesPYR || nCount <= 0 )
		return 0;

	std::vector< IGameEntity * > standalone;
	std::vector< const char * > keys, values;

	const char *pName = pClassNames;
	const char *pPack = pPackedKeys;

	for ( int32_t i = 0; i < nCount; i++ )
	{
		const char *pClassName = pName;
		pName += strlen( pName ) + 1;

		// The pairs, as pointers into the pack: a split on NUL, not a parse.
		keys.clear();
		values.clear();
		const char *pAt = pPack;
		const char *pEnd = pPack ? pPack + pPackBytes[ i ] : pPack;
		while ( pAt && pAt < pEnd )
		{
			const char *pKey = pAt;
			pAt += strlen( pAt ) + 1;
			if ( pAt >= pEnd )
				break;
			keys.push_back( pKey );
			values.push_back( pAt );
			pAt += strlen( pAt ) + 1;
		}
		if ( pPack )
			pPack = pEnd;

		IGameEntity *pEntity = MapEntity_CreateFromKeys( pClassName, keys.data(), values.data(),
		                                                 (int)keys.size() );
		if ( pEntity == NULL )
		{
			Msg( "Failed to initialize templated entity of class %s\n", pClassName );
			return 0;
		}

		const Vector vecOrigin( pOriginsXYZ[i * 3], pOriginsXYZ[i * 3 + 1], pOriginsXYZ[i * 3 + 2] );
		const QAngle angles( pAnglesPYR[i * 3], pAnglesPYR[i * 3 + 1], pAnglesPYR[i * 3 + 2] );

		MapEntity_ClaimPendingNonBaseEntity( pEntity );
		StandaloneEntity_PlaceAndSpawn( pEntity, &vecOrigin, &angles );
		standalone.push_back( pEntity );
	}

	int32_t nWritten = 0;
	int32_t nCreated = 0;

	for ( IGameEntity *pEntity : standalone )
	{
		nCreated++;
		if ( pOutHandles && nWritten < nMax )
			pOutHandles[nWritten++] = SourceSharp_ShellOf( pEntity )->ManagedHandle();
	}

	return nCreated;
}

//-----------------------------------------------------------------------------
// Lending a player's eyes to an entity.
//
// The five calls point_viewcontrol needs, none of which is about cameras. The
// camera is C# and decides WHEN each of these happens, from spawnflags a mapper
// set; what is here is only the reaching-into-CBasePlayer that C# cannot do.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Whose eyes the client renders from.
//
// engine->SetView takes two EDICTS, and an entity on the parallel hierarchy has
// one — which is the whole reason a camera can move to C# at all. What it cannot
// do is live in CBasePlayer::m_hViewEntity, a handle typed to CBaseEntity: so for a
// standalone the member is cleared and the engine is told directly.
//
// Clearing it is not a loss. m_hViewEntity is read in four places: SetupVisibility
// (which is handed the view entity by the engine, not by the member),
// sceneentity's caption attenuation, CBasePlayer::Precache's re-assert, and
// CTriggerCamera's "was somebody else already holding this view" — and that last
// one is now the managed camera, which tracks what it is holding itself.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerSetViewEntity( uint32_t hPlayer, uint32_t hView )
{
	// CBasePlayer::m_hViewEntity retired with its class (13c); the engine is
	// told directly, which the comment above explains costs nothing — the
	// managed camera tracks what it is holding itself.
	CStandalonePlayer *pPlayerEnt = SourceSharp_ResolveStandalonePlayer( hPlayer );
	if ( !pPlayerEnt )
		return;

	edict_t *pPlayerEdict = engine->PEntityOfEntIndex( pPlayerEnt->EntIndex() );
	if ( !pPlayerEdict )
		return;

	const SourceSharpEntity_t view = SourceSharp_ResolveEntity( hView );

	edict_t *pViewEdict = NULL;
	if ( view.pStandalone )
		pViewEdict = view.pStandalone->GetEdict();
	// edict() reaches through CBaseEntity's IServerEntity base, which the alias
	// removes -- IGameEntity has no base at all. The standalone arm above answers
	// with GetEdict(), and a view entity with no edict falls through to the
	// player's own, which the line below already spells as the answer.

	// Nothing at all is how the view is given back: the player's own edict.
	engine->SetView( pPlayerEdict, pViewEdict ? pViewEdict : pPlayerEdict );
}

//-----------------------------------------------------------------------------
// Whether a player may act at all. CBasePlayer::EnableControl, spelt as what it
// does rather than what it permits.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerSetFrozen( uint32_t hPlayer, int32_t bFrozen )
{
	// CBasePlayer::EnableControl retired with its class (13c). Freezing the
	// standalone player is a movement rule — managed code owns the loop's
	// parameters — so the native slot has nothing left to reach into. Already
	// inert for the standalone player before the flip.
}

//-----------------------------------------------------------------------------
// WHERE THE PLAYER IS LOOKING, SET BY THE SERVER AND ADOPTED BY THE CLIENT
// (plan_shim.md §5 3b, lane 3c-entityshell).
//
// The one crossing setang needed, and the reason the whole cheat-command set
// was still native. Not EntitySetAngles: an entity's angles are where its model
// points, and a player's VIEW is CPlayerState::v_angle, which is separate state
// the engine networks for prediction.
//
// fixangle IS THE POINT. The engine reads it out of the pointer
// IServerGameClients::GetPlayerState hands it, and only that read tells the
// client to ADOPT v_angle rather than keep predicting from its own. Set v_angle
// without it and the next usercmd's stale viewangles overwrite it before anyone
// sees the change -- the snap works for a frame and undoes itself, which reads
// as "setang is flaky" rather than as "setang does not work".
//
// The body is the spawn's own SnapEyeAngles, which is why it is two lines: the
// standalone player's state block IS the one the engine was handed.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerSnapEyeAngles( uint32_t hPlayer, const float *pPitchYawRoll )
{
	if ( !pPitchYawRoll )
		return;

	CStandalonePlayer *pPlayer = SourceSharp_ResolveStandalonePlayer( hPlayer );

	if ( !pPlayer )
		return;

	pPlayer->PlayerStatePtr()->v_angle =
		QAngle( pPitchYawRoll[0], pPitchYawRoll[1], pPitchYawRoll[2] );
	pPlayer->PlayerStatePtr()->fixangle = FIXANGLE_ABSOLUTE;
}

//-----------------------------------------------------------------------------
// THE PLAYER'S MOVE TYPE, WRITTEN FROM OUTSIDE A MOVEMENT FRAME.
//
// noclip's crossing. Managed movement can already change the move type --
// MoveProcessMovement takes a SourceSharpMoveState whose nMoveType
// shell_movement.cpp:1321 writes back after the call -- and that is the right
// path for a rule the movement loop decides. A console command is not that: it
// happens between frames and has to take effect whether or not the player is
// moving, and a request parked for the next movement frame would do nothing at
// all for a player standing still, which is exactly when noclip is typed.
//
// ANSWERS THE PREVIOUS MOVE TYPE so that a toggle is one call. Read-then-write
// across the boundary would be two, with a frame's worth of movement possible
// in between -- and the thing being toggled is the state that decides what that
// movement does.
//
// The SOURCESHARP_MOVETYPE_* numbers ARE the engine's MOVETYPE_* numbers
// (WALK 2, NOCLIP 8), which is why this casts rather than translates;
// standalone_movetype.h's StandaloneMoveType_Report passes a player's through
// untouched for the same reason.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PlayerSetMoveType( uint32_t hPlayer, int32_t nMoveType )
{
	CStandalonePlayer *pPlayer = SourceSharp_ResolveStandalonePlayer( hPlayer );

	if ( !pPlayer )
		return SOURCESHARP_MOVETYPE_NONE;

	const int32_t nWas = (int32_t)pPlayer->MvGetMoveType();

	pPlayer->MvSetMoveType( (MoveType_t)nMoveType );

	return nWas;
}

//-----------------------------------------------------------------------------
// The buttons a player is holding, as IN_* bits.
//
// The read that PlayerSetButtonsDisabled and PlayerForceButtons are the writes
// of. What wants it is anything waiting for the player to press ANY key, which
// is not a question about a particular one.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PlayerGetButtons( uint32_t hPlayer )
{
	// Managed code already hears the standalone player's buttons once per
	// command (OnPlayerRunCommand), which is the same number this would poll.
	// Answered 0 for the standalone player before the flip too.
	return 0;
}

//-----------------------------------------------------------------------------
// The weapon a player is holding, as a handle.
//
// SourceSharp_GetPlayerActiveWeapon answers the same question with a classname,
// which is what a rule about weapons wants. A name cannot be hidden, and hiding
// the viewmodel means EF_NODRAW on the weapon ENTITY.
//-----------------------------------------------------------------------------
uint32_t SourceSharp_PlayerGetActiveWeaponEntity( uint32_t hPlayer )
{
	// Retired with the CBasePlayer inventory (13c) — the Arsenal knows.
	return INVALID_EHANDLE_INDEX;
}

//-----------------------------------------------------------------------------
// One inventory slot, as an untyped handle (Phase 10).
//
// GetWeapon(i) reads m_hMyWeapons[i] through the specialized crossing, so a
// slot holding a parallel-hierarchy entity answers NULL here — empty rather
// than wrong — until the managed inventory takes over as the authority.
//-----------------------------------------------------------------------------
uint32_t SourceSharp_PlayerGetWeaponEntity( uint32_t hPlayer, int32_t nSlot )
{
	// Retired with the CBasePlayer inventory (13c) — the Arsenal is the
	// authority the Phase 10 comment above was waiting for.
	return INVALID_EHANDLE_INDEX;
}

//-----------------------------------------------------------------------------
// The player's view model, as an entity handle.
//-----------------------------------------------------------------------------
uint32_t SourceSharp_PlayerGetViewModelEntity( uint32_t hPlayer )
{
	// Retired with CBasePlayer (13c) — the viewmodel is a hierarchy entity
	// (ViewModelNetState) the managed side owns outright.
	return INVALID_EHANDLE_INDEX;
}

//-----------------------------------------------------------------------------
// Whether an entity may be hurt, answering what it was before.
//
// The previous value is RETURNED rather than remembered here, and that is the
// whole design of the slot: invulnerability nests badly, and only whoever took
// it away knows when it is done. Two things taking it in turn each give back
// what they found instead of a shared idea of what normal was.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntitySetTakeDamageMode( uint32_t hEntity, int32_t nMode )
{
	// m_takedamage is a CBaseEntity DATA MEMBER, not a method, so there is not
	// even a virtual to reach it by. SDK-only from the start; armed the function
	// answers DAMAGE_YES, which is what it already answered for every entity of
	// the parallel hierarchy.
	return kDamageYes;
}

//-----------------------------------------------------------------------------
// Whether anything can bump into this.
//
// Not SetBrushSolidity, which is a brush choosing what SHAPE it presents to a
// trace. This is one flag on something that already has a shape — a player being
// stepped through while a camera holds their view.
//
// It is also func_physbox's "notsolid" (shell_physicsobjects.cpp's
// SetSolidToWorld was this call inverted until plan_physics_bones_native_zero
// 2.4 retired it).
//
// The parallel arm goes through SetSolidity rather than writing the flag,
// because the flag decides which partition
// lists the entity is in, and the partition is what the engine reads. Set
// directly, something told to stop being solid stays in the solid list and goes
// on stopping traces — an invisible wall where a player was meant to be walked
// through.
//
// It read only pBase until Phase 6's gate asked it about a standalone, which is
// the silence this whole gate exists for: it returned having done nothing, and
// GetSolidFlags — which does answer both — reported the same flags before and
// after.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySetNotSolid( uint32_t hEntity, int32_t bNotSolid )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( CStandaloneEntity *pStandalone = ent.pStandalone )
	{
		const int nFlags = bNotSolid
			? ( pStandalone->GetSolidFlags() | FSOLID_NOT_SOLID )
			: ( pStandalone->GetSolidFlags() & ~FSOLID_NOT_SOLID );

		pStandalone->SetSolidity( pStandalone->GetSolid(), nFlags );
		return;
	}

}

//-----------------------------------------------------------------------------
// Whether this entity is sent to every client regardless of where they are.
//
// An entity on the parallel hierarchy asks to be PVS-checked, which is right for
// everything that is somewhere a player can walk to. It is wrong for one a
// client is meant to be LOOKING THROUGH from a room it is not in: the check
// answers "not visible", the entity is never sent, and the client has nothing to
// take a view from. Stock's CTriggerCamera::UpdateTransmitState is this same
// switch spelt as FL_EDICT_ALWAYS.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySetTransmitAlways( uint32_t hEntity, int32_t bAlways )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( CStandaloneEntity *pStandalone = ent.pStandalone )
	{
		edict_t *pEdict = pStandalone->GetEdict();
		if ( !pEdict )
			return;

		// CheckTransmit masks the state flags down to
		// DONTSEND|ALWAYS|PVSCHECK|FULLCHECK and decides from those alone, so
		// the old answer has to go before the new one is set — two of them at
		// once is not a state the engine has a branch for.
		pEdict->ClearTransmitState();
		pEdict->m_fStateFlags |= ( bAlways ? FL_EDICT_ALWAYS : FL_EDICT_PVSCHECK );
		return;
	}

}

//-----------------------------------------------------------------------------
// Stock's CBaseEntity::DetectInSkybox, reachable from managed code.
//
// It walks the sky camera list and asks the engine whether the entity's area is
// connected to any of them, which is not something C# can do for itself. What
// it does with the answer is raise EFL_IN_SKYBOX, and that flag has exactly one
// consequence: UpdateTransmitState sends the entity to everyone rather than
// PVS-checking it. Which is the whole reason an env_smokestack calls it — a 3D
// skybox is in nobody's PVS, so a stack standing in one would be culled for
// every player and drawn for none.
//
// Only two entities in the tree ever called it, and this is one of them.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityDetectInSkybox( uint32_t hEntity )
{
	// The comment above says only two entities in the tree ever called it and
	// both are CBaseEntity. Armed, 0 -- not in a skybox -- is the same answer it
	// already gave for anything the SDK could not resolve.
	return 0;
}

//-----------------------------------------------------------------------------
// Hands one of a map's own keys to an entity, after it has spawned.
//
// The general form on purpose. A keyvalue is already the single channel every
// entity in this build understands, and it is the only one that reaches BOTH
// kinds of storage: CBaseEntity::KeyValue walks the datadesc and writes the
// member the key is bound to, and CStandaloneEntity::KeyValue checks the few
// keys the shell claims and then offers it to the managed class, which lands in
// a [ScriptedVar] field. A slot per field would have had to know which of those
// two it was addressing, and a caller has no way to find out.
//
// ai_changetarget is what wanted it, and is the case that shows why generality
// was cheaper than a special case: it rewrites "target" on every entity with a
// matching name, and "target" is CBaseEntity::m_target on a stock entity and a
// managed field on one of ours. A slot that wrote m_target would have reached
// exactly the half of the world that used to be all of it.
//
// Both hierarchies are tried in the same order the rest of this file uses:
// standalone first, because a handle that resolves there is not a CBaseEntity
// and asking would answer NULL.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntitySetKeyValue( uint32_t hEntity, const char *pKey, const char *pValue )
{
	if ( !pKey || !pKey[0] )
		return 0;

	// An empty value is legal and means the empty string -- clearing a target
	// is a thing a map does. A NULL pointer is not, and would reach strcmp.
	if ( !pValue )
		pValue = "";

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
		return ent.pStandalone->KeyValue( pKey, pValue ) ? 1 : 0;

	return 0;
}

//-----------------------------------------------------------------------------
// Which collision group this entity belongs to.
//
// Not SetBrushSolidity and not SetNotSolid, both of which say what SHAPE the
// entity presents to a trace. This says what it presents that shape TO: the
// group is one half of a pair CGameRules::ShouldCollide is asked about, and it
// is the only way in Source to spell "solid to vehicles and to nothing else".
// The rule itself is already there for both hierarchies — util_shared.cpp's
// trace filter asks it of a CBaseEntity and of an IGameEntity alike.
//
// The names are SourceSharp's, not the engine's, and are translated in
// collision_group_names.h. That is deliberate: Collision_Group_t is thirty
// positional entries deep and mostly names things this mod has none of, so
// mirroring its numbers into C# would put a table there that a change to the
// engine's could silently renumber. The translation lives in a header of its
// own because it is now asked in both directions, and it is ONE table read two
// ways rather than two switches -- two switches is a group written as one thing
// and read back as another, with nothing to say so. The engine's numbers stay
// here, where const.h already is; collision_group_names_test.cpp pins the rules.
//-----------------------------------------------------------------------------
//
// The engine's numbers, which the header above deliberately does not know. One
// table, read both ways -- the first row is the neutral one both directions
// fall back to.
//
static const CollisionGroupName_t s_CollisionGroupNames[] =
{
	{ SS_COLLISION_GROUP_NONE,         COLLISION_GROUP_NONE },
	{ SS_COLLISION_GROUP_VEHICLE_CLIP, COLLISION_GROUP_VEHICLE_CLIP },
	{ SS_COLLISION_GROUP_WEAPON,       COLLISION_GROUP_WEAPON },
	{ SS_COLLISION_GROUP_DEBRIS,       COLLISION_GROUP_DEBRIS },
	{ SS_COLLISION_GROUP_INTERACTIVE,  COLLISION_GROUP_INTERACTIVE },
};

static const int s_nCollisionGroupNames =
	sizeof( s_CollisionGroupNames ) / sizeof( s_CollisionGroupNames[0] );

// SourceSharp_CollisionGroupFromEngine / ToEngine, the same table for
// shell_breakpieces.cpp, WENT WITH IT: break pieces are C#'s (BreakModels),
// which reads the group in C#'s own vocabulary.

void SourceSharp_EntitySetCollisionGroup( uint32_t hEntity, int32_t nGroup )
{
	const int nEngineGroup =
		CollisionGroup_ToEngine( nGroup, s_CollisionGroupNames, s_nCollisionGroupNames );

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetCollisionGroup( nEngineGroup );
		return;
	}

}

//-----------------------------------------------------------------------------
// The same question read back, in the same names.
//
// Many-to-one rather than the inverse of a table: a group this mod has no name
// for reads as None, which is what "it is not one of the ones we care about"
// honestly means. Both directions live in collision_group_names.h.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityGetCollisionGroup( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	int nEngineGroup = COLLISION_GROUP_NONE;

	if ( ent.pStandalone )
		nEngineGroup = ent.pStandalone->GetCollisionGroup();
	else if ( ent.pBase )
		nEngineGroup = ent.pBase->GetCollisionGroup();
	else
		return 0;

	return CollisionGroup_FromEngine( nEngineGroup, s_CollisionGroupNames, s_nCollisionGroupNames );
}

//-----------------------------------------------------------------------------
// What SHAPE this entity presents to a trace, as the engine's own FSOLID_ bits.
//
// Unlike the collision group above these are NOT renamed, and the difference is
// worth stating: FSOLID_ is a bitfield of eleven stable flags that every part of
// the SDK reads directly, not a positional enumeration that a new member
// renumbers. Mirroring it into C# costs one enum and buys the question a
// projectile has to ask -- "is this a volume I am supposed to fly through?" --
// which is FSOLID_TRIGGER|FSOLID_VOLUME_CONTENTS and nothing else.
//
// Off the collideable rather than off CBaseEntity, so both hierarchies answer:
// a trigger_multiple is a CBaseEntity here and a managed brush is not, and the
// projectile that has to tell them from a wall cannot know which it was handed.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityGetSolidFlags( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	IHandleEntity *pHandle = ent.ToHandleEntity();

	if ( !pHandle )
		return 0;

	ICollideable *pCollide = ( (IServerEntity *)pHandle )->GetCollideable();

	return pCollide ? (int32_t)pCollide->GetSolidFlags() : 0;
}

//-----------------------------------------------------------------------------
// The model this entity is using, as the name the map knows it by.
//
// Brush models come back as "*23" — the twenty-third submodel the compiler
// carved out of the level — and studio models as their path. Both are what
// SetEntityModel takes back, which is the point: an entity that borrows
// another's model reads it here and wears it there, and neither side has to
// know which kind it got.
//
// func_areaportalwindow is what wanted it, and the borrowing is not a trick it
// invented: a mapper points the window at a brush entity, and the window takes
// that brush's model AND its position and hides the original. Stock reads
// CBaseEntity::GetModelName directly, which now reaches exactly half of what a
// map can place — the other half being on the parallel hierarchy.
//-----------------------------------------------------------------------------
void SourceSharp_EntityGetModelName( uint32_t hEntity, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = '\0';

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		ss::StrCopy( pOut, ent.pStandalone->GetModelNameString(), nOutSize );
		return;
	}

}

//-----------------------------------------------------------------------------
// THE SIX MISSING DIRECTIONS, for the native half of a savegame.
//
// docs/managed-saverestore.md 7g: the native half of an entity is never written
// to a .sav, so SavedEntity carries a NATIVE STORE and a `load` is a `map` plus
// that overlay. The store carries only fields the managed side can both READ
// and WRITE, because that is what makes the field list and its apply order
// testable against the engine double rather than only in a game. Six fields
// were short exactly one direction; these are the six.
//
// AT THE END OF THE FILE, AND THAT IS DELIBERATE. Every one of them has a
// natural neighbour a few hundred lines up -- the gravity setter at
// EntitySetGravity, the solid-flag getter at EntityGetSolidFlags -- and putting
// them there would re-point thirty ledgered SS_CBASEENTITY_ALIAS guards in this
// file and every dossier keyed on their line numbers. Appended, nothing above
// moves. The pairing is said in the comments instead, where it costs nothing.
//
// FIVE OF THE SIX ARE DECLARED ONE-HIERARCHY -- CStandaloneEntity only -- and
// that is argued rather than economised. The consumer is
// ScriptedEntity.LoadNativeState, and every entity that carries a managed save
// record IS a CStandaloneEntity: the records come off the scripted registry,
// and RegisterScriptedEntity installs standalone classes. So the second
// hierarchy is not a case these five can meet through the section that wants
// them. What an SDK arm WOULD cost is five more alias-guard regions, in a tree
// whose whole alias lane exists to delete them -- five new
// guards, five ledger rows, four pinned population counts and two shard totals,
// to serve a caller that cannot arrive. A handle that resolves to a CBaseEntity
// is left alone by all five, which is the same answer they would give under the
// alias build anyway.
//
// The sixth, EntityGetSolidType, answers for BOTH -- because it can do so
// through ICollideable without a guard at all, exactly as EntityGetSolidFlags
// above it does, and a getter that is cheap to make total should be total.
//-----------------------------------------------------------------------------

// The pair to EntityGetBounds. Bounds as OFFSETS from the origin, in the
// entity's own space -- not a size, which could only ever be centred on the
// origin.
void SourceSharp_EntitySetBounds( uint32_t hEntity, const float *pMinsXYZ, const float *pMaxsXYZ )
{
	// Both or neither. A box built from one supplied corner and one garbage
	// pointer is a box, and the partition would take it.
	if ( !pMinsXYZ || !pMaxsXYZ )
		return;

	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveStandalone( hEntity ) )
	{
		pStandalone->SetCollisionBounds( Vector( pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2] ),
		                                 Vector( pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2] ) );
	}
}

// The half of solidity that had no getter: the SHAPE that EntityGetSolidFlags'
// bits qualify. Off the collideable, so both hierarchies answer, and 0 --
// SOLID_NONE -- for a handle that resolves to nothing.
//
// The engine's OWN number, untranslated in both directions. That is what lets
// it be saved: compare EntityGetCollisionGroup, which is many-to-one on purpose
// and therefore cannot be.
int32_t SourceSharp_EntityGetSolidType( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	IHandleEntity *pHandle = ent.ToHandleEntity();

	if ( !pHandle )
		return 0;

	ICollideable *pCollide = ( (IServerEntity *)pHandle )->GetCollideable();

	return pCollide ? (int32_t)pCollide->GetSolid() : 0;
}

// Both halves of solidity in ONE call, because CStandaloneEntity::SetSolidity
// writes the pair and then runs UpdatePartition once -- and UpdatePartition
// reads both to decide whether the entity has a partition handle at all. Set in
// pieces, it is briefly listed for a shape it does not have.
void SourceSharp_EntitySetSolidity( uint32_t hEntity, int32_t nSolidType, int32_t nSolidFlags )
{
	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveStandalone( hEntity ) )
		pStandalone->SetSolidity( (SolidType_t)nSolidType, nSolidFlags );
}

// The pair to EntityGetSpawnFlags. Wholesale rather than add/remove, because
// what wants it is a restore putting back a value it read.
void SourceSharp_EntitySetSpawnFlags( uint32_t hEntity, int32_t nSpawnFlags )
{
	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveStandalone( hEntity ) )
		pStandalone->SetSpawnFlags( nSpawnFlags );
}

// The pair to EntitySetGravity, which for this hierarchy is the gravity SCALE
// -- a multiple of the world's, so 1 is no change. The same number that went
// in.
float SourceSharp_EntityGetGravity( uint32_t hEntity )
{
	CStandaloneEntity *pStandalone = SourceSharp_ResolveStandalone( hEntity );

	return pStandalone ? pStandalone->GetGravityScale() : 0.0f;
}


//=============================================================================//
// THE THREE STOCK REMOVAL SPELLINGS, ARMED.
//
// All three are defined in game/server/util.cpp, which the armed server does not
// compile. Armed their arguments have collapsed -- `CBaseEntity *` IS
// `IGameEntity *` -- so these are not new functions, they are what the stock
// declarations became, and stock code still calls them: the map loader calls two
// of them and the physics bridge the third.
//
// THEY ARE NOT RE-HOMED, AND THAT IS THE WHOLE POINT. Every one of the three
// bodies in util.cpp is already wrapped in `#ifndef SS_CBASEENTITY_ALIAS`, so
// moving them verbatim would produce three functions whose armed arms are
// `if ( !oldObj ) return;` and nothing else -- a silent no-op wearing the name
// of a removal, which is exactly the shape plan_entity.md §7 forbids and which
// this file would have had no way to tell apart from a working one.
//
// So each says what it does instead, and the two that genuinely cannot act say
// so out loud.
//
// ARMED ONLY. Unarmed, util.cpp defines all three and this block is not
// compiled -- otherwise the first of them would call straight back into the
// function that calls it.
//=============================================================================//

//-----------------------------------------------------------------------------
// The general remove. BOUND, not refused: the behaviour is intact.
//
// The entity is asked for its own handle and the handle goes to the removal
// this file already exposes, which ends at the shell's own Remove() and from
// there at the managed directory's deferred-delete list -- the same list, and
// the same drain, every other removal on this hierarchy uses.
//
// DELIBERATELY NOT the standalone template relay, which has the matching
// signature and would compile. That relay is scoped by its own header to a
// template source consuming its copy, and widening it into the general remove
// would be inventing a relay rather than using one.
//-----------------------------------------------------------------------------
void UTIL_Remove( IGameEntity *oldObj )
{
	if ( !oldObj )
		return;

	SourceSharp_EntityRemove( (uint32_t)GameEntity_RefHandle( oldObj ).ToInt() );
}

//-----------------------------------------------------------------------------
// The NETWORKABLE overload. REFUSED, and it is a genuinely different function
// from the two above -- not the same one after the alias.
//
// Stock reached its delete list by casting the networkable to the stock network
// property. Armed that cast is unsound as well as unreachable: a shell IS its
// own networkable and is not that property, so the cast would be undefined
// behaviour on the only entity kind this build has. The delete list it wanted
// leaves with the stock entity list.
//
// Its two call sites are both downstream of a producer that is itself guarded
// out armed, so this is expected to be entered zero times -- and unlike the
// per-frame refusals elsewhere in this port, that makes the reach count a real
// gate here rather than a number to ignore.
//
// WHAT WOULD HAVE TO EXIST: a physics callback that can name an entity of this
// hierarchy through a networkable, which is the crossing the slot table exists
// to make unnecessary.
//-----------------------------------------------------------------------------
void UTIL_Remove( IServerNetworkable *oldObj )
{
	if ( !oldObj )
		return;

	static CPhysicsBridgeRefusal s_RefuseNetworkableRemove(
		"removing an entity through its networkable face",
		"stock reached its delete list by casting the networkable to the stock network "
		"property; armed a shell IS its own networkable and is not that property, so "
		"the cast is unsound and not merely unreachable, and the delete list it wanted "
		"leaves the build with the stock entity list. Both call sites sit downstream of "
		"a producer that is guarded out armed, so zero reaches is what this predicts "
		"and a non-zero count here is a real finding. An entity of this hierarchy is "
		"removed through its handle, never through a networkable." );

	if ( s_RefuseNetworkableRemove.Reached() )
	{
		Warning( SOURCESHARP_PHYSICS_REFUSAL_FMT,
		         s_RefuseNetworkableRemove.What(), s_RefuseNetworkableRemove.Refusal() );
	}
}


//-----------------------------------------------------------------------------
// Is this world-space box inside this player's PVS?
//
// The engine's own read, exactly the trio stock's check-client machinery
// builds (triggers.cpp:1615, 1711): the leaf cluster under the player's eyes,
// the PVS of that cluster, and the engine's box-over-pvs test over it. Who
// asks — and what a yes or a no is worth — is managed code's decision; this
// only answers.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PlayerBoxInPvs( uint32_t hPlayer, const float *pMins, const float *pMaxs )
{
	if ( !pMins || !pMaxs )
		return 0;

	// The standalone player is the only player (13c), and it is the subject
	// of the read: a handle that is not one has no eyes, so nothing sits in
	// anyone's view of it.
	CStandalonePlayer *pStandalone = SourceSharp_ResolveStandalonePlayer( hPlayer );
	if ( !pStandalone )
		return 0;

	// Stock queries from the player's eyes, not their feet — EyePosition is
	// what every other per-player engine read here uses (GetPlayerShootOrigin
	// above, TracePlayerAim below).
	const Vector vecEye = pStandalone->EyePosition();

	const int nCluster = engine->GetClusterForOrigin( vecEye );

	// Stock's callers size the buffer MAX_MAP_CLUSTERS/8 for a landmark's
	// PVS; the leaf-sized buffer is what CheckBoxInPVS is documented to eat
	// and what the check-client machinery in triggers.cpp holds. GetPVSForCluster
	// answers the bytes it needed; passing the full buffer is stock's form.
	unsigned char pvs[MAX_MAP_LEAFS / 8];
	const int nPvs = engine->GetPVSForCluster( nCluster, sizeof( pvs ), pvs );
	if ( nPvs <= 0 )
		return 0;

	return engine->CheckBoxInPVS( Vector( pMins[0], pMins[1], pMins[2] ),
	                              Vector( pMaxs[0], pMaxs[1], pMaxs[2] ),
	                              pvs, nPvs ) ? 1 : 0;
}
