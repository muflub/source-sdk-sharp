//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — one resolver for a managed entity handle ===========//
//
// A handle names an ENTITY, not a C++ class. There used to be four spellings of
// "turn a handle into an entity" — ResolveEntityHandle, ResolveHandle,
// ResolveStandalone, ResolveEntity — and every ABI function that took a handle
// chose among them, usually by trying one and falling back to another. Two
// lookups per call, and a caller that picked the wrong half got NULL, which
// reads as "no such entity" rather than as an error. That silence cost real
// behaviour four separate times before this header existed.
//
// Now there is ONE resolve — SourceSharp_ResolveEntity — doing ONE entity-list
// lookup, and everything else in this header is a typed projection of its
// result. A projection cannot disagree with the resolve it projects, which is
// the property the four spellings never had.
//
// The lookup is the same one the trace filter and the event queue use:
// CBaseHandle -> LookupEntity (serial revalidated) -> IServerUnknown, then two
// virtual asks that partition the world exactly — GetGameEntity() answers only
// for the parallel hierarchy and GetBaseEntity() answers only for the SDK's,
// so at most one arm of the result is ever set.
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_RESOLVE_H
#define SOURCESHARP_SHELL_RESOLVE_H
#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>
#include "mathlib/vector.h"

#include "igameentity.h"
class CBaseAnimating;
class CBasePlayer;
class CStandaloneEntity;
class CWorld;
class IGameEntity;
class IHandleEntity;
class IPhysicsObject;
struct edict_t;

// Declared rather than included: mathlib/vector.h above does NOT define it —
// it lives in mathlib/mathlib.h — and a reference return needs no definition.
// Pulling mathlib.h in here would put it above every shell that includes this.
struct matrix3x4_t;

//-----------------------------------------------------------------------------
// A handle resolved to whichever hierarchy holds it.
//
// Every [ScriptedEntity] in this mod is on the parallel hierarchy, so an ABI
// call that resolves only a CBaseEntity gets NULL for all of them, returns
// early, and says so nowhere. That silence has cost real behaviour four
// separate times: a game_zone_player that counted nobody, a door whose noise1
// never played, `debug-api place` that could not place, and a train that put its
// origin on each corner rather than its middle.
//
// So the asking is one call and the answering is one place. Only the questions
// BOTH hierarchies can honestly answer are methods here; a call that needs a
// CBaseAnimating, a CBaseCombatCharacter or a CTakeDamageInfo reads pBase
// directly, because there is nothing on the other side to ask and inventing an
// answer is worse than the neutral one.
//
// It carries out; it does not decide. Which entity, and what to do with it, is
// still the caller's — and above the caller, C#'s.
//-----------------------------------------------------------------------------
struct SourceSharpEntity_t
{
	IGameEntity       *pBase;
	CStandaloneEntity *pStandalone;

	//-------------------------------------------------------------------------
	// worldspawn, which is on NEITHER of those (plan_cworld_trace.md T3).
	//
	// The third arm, here for the same reason it is on AnyEntity_t and on
	// PhysicsOwner_t: the two asks this resolver makes -- GetGameEntity() and
	// GetBaseEntity() -- both answer NULL for the world, and this header's own
	// contract is that "NULL in both arms means the handle did not resolve AT
	// ALL". Without a third arm the world resolves as gone.
	//
	// IT WAS FOUND BY A LIVE RUN. SourceSharp_EntityGetDataBlob asks this
	// resolver where the world's managed state lives; with the world reading as
	// gone, every worldspawn key was refused with "entity 0 ('worldspawn')
	// offers 0" and the world networked 76 zero words -- the same sentence,
	// word for word, that world_adapter.cpp records the singleton assignment
	// being moved to fix. Same symptom, different cause, and no offline gate
	// saw either of them.
	//-------------------------------------------------------------------------
	CWorld            *pWorld;

	bool IsValid() const { return pBase != NULL || pStandalone != NULL || pWorld != NULL; }

	// Whether this handle names the world.
	bool IsWorld() const { return pWorld != NULL; }
	// The entity as an IGameEntity, on either hierarchy: pBase, or the
	// standalone entity's own GetGameEntity(). NULL when it did not resolve.
	// What a CTakeDamageInfo's attacker and inflictor take: reading pBase alone
	// named no managed attacker at all (plan_physics_managed A40).
	IGameEntity *GameEntity() const;

	// Where it is.
	const Vector &Origin() const;

	// The collision box, as offsets FROM that origin. Origin-relative because a
	// brush model's box is: a door is a slab somewhere in the map whose origin
	// is usually at the world's, so the box and the position are two separate
	// facts and folding them together loses one.
	void Bounds( Vector *pOutMins, Vector *pOutMaxs ) const;

	// The middle of that box, which is what anything positioning itself
	// relative to a VOLUME means by "where it is".
	Vector Center() const;

	// The entity index a sound attaches to, or SOUND_FROM_WORLD for an entity
	// the client has never heard of. A server-only entity has no index, and a
	// sound from a fixed point is the honest answer rather than no sound.
	int SoundSourceIndex() const;

	// The index the ENGINE stores something against — a decal, and anything
	// else engine-side that names an entity by number rather than by pointer.
	// 0 for an entity the client has never heard of, which is the world's
	// index and is what "attached to nothing in particular" has always meant
	// on that side.
	//
	// SEPARATE FROM SoundSourceIndex despite the two agreeing today, because
	// they are answers to different questions and only one of them has a
	// sentinel: SOUND_FROM_WORLD is a sound-engine constant that happens to be
	// 0, and SOUND_FROM_LOCAL_PLAYER is -1 in the same numbering. Folding them
	// together would make a future change to either silently change the other.
	int EntityIndex() const;

	// What the engine would draw for it, or 0 for an entity with no model.
	// Both hierarchies answer this through IServerEntity, which is where the
	// membership rule says it belongs.
	int ModelIndex() const;

	// The frame the entity's own model is expressed in — what turns a point in
	// the world into a point ON the entity, and back.
	//
	// NOT ICollideable::CollisionToWorldTransform for the SDK arm. That is a
	// DIFFERENT matrix on the branch CCollisionProperty takes for a
	// SOLID_NONE, a SOLID_BBOX or an FSOLID_FORCE_WORLD_ALIGNED entity: it
	// drops the rotation and keeps only the translation. The parallel
	// hierarchy has no such branch — CStandaloneEntity::SetAbsTransform builds
	// m_Transform with AngleMatrix from the origin AND the angles, every time,
	// and CollisionToWorldTransform returns exactly that — so on that arm the
	// two really are one matrix. Each arm therefore asks the thing that is
	// right for it, which is the whole reason this is a projection and not a
	// cast at the call site.
	//
	// AND IT COSTS ONE CONSUMER, measured rather than discovered later: the SDK
	// arm's EntityToWorldTransform() is an inline in baseentity.h that calls
	// CBaseEntity::CalcAbsolutePosition() when the transform is dirty, so
	// shell_standalone_base.o needs that one symbol and joins the release
	// server's consumer set (57 -> 58). The obvious way to avoid it is the
	// substitution above, and that is not a trade -- it is the bug, on every
	// SOLID_BBOX, SOLID_NONE and world-aligned entity. One call-bound need is
	// what a correct answer costs here, and it dies with the alias exactly as
	// every other member ask does.
	const matrix3x4_t &EntityToWorld() const;

	// What to call it in a diagnostic. Neither is the entity's identity — a
	// handle is — but a warning that cannot name the thing it is about is a
	// warning nobody can act on.
	const char *Classname() const;
	const char *Name() const;

	IPhysicsObject *Physics() const;

	// The SDK arm's skeleton, or NULL — for the animation calls, which only one
	// hierarchy answers this way (the other side has its own studio header, and
	// the callers ask for it separately).
	//
	// A PROJECTION rather than a cast at each site, for two reasons that both
	// cost something. It was five open-coded dynamic_cast< CBaseAnimating * >(
	// ent.pBase ), which is RTTI on a path the entity API takes for every
	// animation call when CBaseEntity::GetBaseAnimating() is a virtual the SDK
	// put there for exactly this question. And the obvious hand-substitution is
	// WRONG: dynamic_cast of a NULL pointer is NULL, a virtual call on one is
	// not, and ent.pBase is NULL for every entity on the parallel hierarchy —
	// which is most of them. Writing the guard once is what keeps the five
	// call sites from each having to remember it.
	CBaseAnimating *Animating() const;

	// What the engine's own collision code takes when it is told to skip
	// something. Both hierarchies are IHandleEntity already, so neither needs
	// converting and a trace can ignore either kind.
	IHandleEntity *ToHandleEntity() const;
};

//-----------------------------------------------------------------------------
// THE resolve. One entity-list lookup, serial revalidated, both arms answered.
//
// NULL in both arms means the handle did not resolve AT ALL — the entity is
// gone or the slot was reused. It no longer ever means "wrong hierarchy",
// because there is no second call left to fall through to.
//-----------------------------------------------------------------------------
SourceSharpEntity_t SourceSharp_ResolveEntity( uint32_t hEntity );

//-----------------------------------------------------------------------------
// plan_entity.md step 2e -- THE ENGINE-INVISIBLE RANGE'S SAFETY COUNTER.
//
// Called by the resolver above, from the one arm that can see it happen: an
// entry index in the engine-invisible half that the ENGINE'S OWN entity list
// does not resolve. Defined in shell_scripted.cpp, with the whole argument for
// what is counted and why it is counted natively rather than in managed code.
//-----------------------------------------------------------------------------
void    SourceSharp_NoteInvisibleRangeReach( void *pReturnAddress );
int32_t SourceSharp_InvisibleRangeReach( void );

// The parallel hierarchy's arm alone, for the calls only that side can answer
// (per-class tables, managed blobs, scripted state). A projection of
// SourceSharp_ResolveEntity, not a second resolver.
CStandaloneEntity *SourceSharp_ResolveStandalone( uint32_t hEntity );

//-----------------------------------------------------------------------------
// The other direction: the handle C# knows an entity by.
//-----------------------------------------------------------------------------

// From a CBaseEntity. INVALID_EHANDLE_INDEX for NULL.
uint32_t SourceSharp_HandleFromEntity( IGameEntity *pEntity );

// The SAME identity as a CBaseHandle, for the callers that want the two halves
// rather than the packed number -- a networked ehandle's entry and serial, and
// the restore list's entries.
//
// It exists because `pEntity->GetRefEHandle()` STOPS COMPILING at the alias:
// IGameEntity is not an IHandleEntity and deliberately never will be
// (igameentity.h, "duplicating them here would invite the two to disagree").
// Both spellings above are therefore ONE guarded definition in
// shell_entityapi.cpp rather than a guard at each of the six call sites, which
// is also why the call sites read the same armed and unarmed.
CBaseHandle SourceSharp_RefHandleFromEntity( IGameEntity *pEntity );

// From an entity of EITHER hierarchy. Both are IHandleEntity already, and the
// handle is the one number that never cared which side its owner lived on.
uint32_t SourceSharp_HandleFromAnyEntity( IHandleEntity *pHandleEntity );

//-----------------------------------------------------------------------------
// SourceSharp_ShellFromEdict, the edict-keyed resolver, is gone (server gc
// cull, 2026-10-01): nothing called it and --print-gc-sections dropped it.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// SECTION 3.16 FAMILY 3, FOR A HEADER THAT MUST NOT SEE THE SHELL.
//
// SourceSharp_ShellFromHandleEntity (slot_table.h) is the family's replacement
// and answers a CStandaloneEntity *. A caller that only wants the game-entity
// boundary -- toucher.h is the one that matters, and it is read by 26 server
// translation units -- would have to include shell_standalone.h to perform the
// derived-to-base conversion, which is a 3,000-line header arriving in 26 TUs
// to spell a conversion the compiler could do in one.
//
// So the conversion is performed ONCE, here, in a TU that already has the whole
// shell type. Same table, same virtual, same NULL for anything that is not one
// of ours; what differs is only which of the shell's faces comes back.
//
// This is NOT a second resolver in the sense 1c's handover forbids: it does not
// index the table itself and it casts nothing -- it calls
// SourceSharp_ShellFromHandleEntity and returns the answer through a base
// pointer the compiler adjusts.
//-----------------------------------------------------------------------------
IGameEntity *SourceSharp_GameEntityFromHandleEntity( IHandleEntity *pHandleEntity );

#endif // SOURCESHARP_SHELL_RESOLVE_H
