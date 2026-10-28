//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the third arm of the partition =====================//
//
// THE PARTITION HAS TWO ARMS AND THE WORLD IS ON NEITHER.
//
// `handle_cross.h` answers "which hierarchy holds this?" with one virtual call:
// `GetGameEntity() != NULL` means the parallel hierarchy, NULL means the SDK's
// tree. Two arms, and every resolver in the game is built on them —
// `EntityFromEntityHandle`, `GameEntityFromEntityHandle`, `TraceEntity`,
// `AnyEntityOf`, `MovementGroundFromTrace`.
//
// worldspawn is about to be on a THIRD hierarchy (plan_cworld_trace.md T3,
// the owner's instruction: *"CWorld should not inherit CStandaloneEntity
// either — it should be its own standalone class"*), and a two-armed partition
// answers NULL for it in both arms. NULL is what every one of those resolvers
// spells "nothing there". `util_shared.h` says so itself, in the comment on
// `TraceEntity`:
//
//     "NULL is the SAFE answer for an entity outside the SDK — it reads as
//      HIT THE WORLD, which is what such an entity looked like before it
//      could be hit at all."
//
// That reading cannot survive the world BEING the entity outside the SDK.
// "NULL means you hit the world" and "the world resolves to NULL" are both
// true the moment CWorld un-derives, and the code cannot tell them apart.
// T1 counted the population: **44 server call sites** reach that resolver, and
// not one of them names `trace_t::m_pEnt`, so no census keyed on the field can
// see them.
//
//=============================================================================//
//
// WHY THIS IS AN IDENTITY COMPARE AND NOT A VIRTUAL.
//
// The obvious third arm is a third question on the interface — an
// `IsTheWorld()` beside `GetGameEntity()`. It is the wrong shape here for two
// reasons and both are measured facts rather than preferences:
//
//   * IT WOULD BE A CHANGE TO AN ENGINE INTERFACE. `IServerUnknown` and
//     `IHandleEntity` are `public/`, above the cliff, and shared with the
//     client's `IClientUnknown`. The partition's two existing arms cost that
//     once, deliberately, because "which hierarchy" genuinely is a question
//     only the object can answer.
//
//   * THE WORLD IS A SINGLETON AND THEREFORE DOES NOT NEED ASKING. There is
//     exactly one worldspawn per level, `GetWorldEntity()` is a read of one
//     file-scope pointer in `world_adapter.cpp`, and the client's half is
//     `ClientEntityList().GetBaseEntity( 0 )` — the comparison
//     `CGameTrace::DidHitWorld()` has always made. Identity against a
//     singleton is EXACT: it cannot answer "maybe", it cannot dispatch through
//     a wrong vtable (it dereferences nothing), and it is correct for all
//     three hierarchies at once because a pointer compare has no type in it.
//
// So this is one call, not a base class and not an interface method — which is
// the same reason `AnyEntity_t` resolves rather than casting, one level out.
//
//-----------------------------------------------------------------------------
//
// THE ARM IS LIVE TODAY, AND THAT IS THE POINT OF ITS ORDERING.
//
// Every caller asks THIS FIRST and only then asks the two-armed partition. It
// would have been easier to ask it LAST — as a fallback for when both existing
// arms answer NULL — and that version would be dead code until the day CWorld
// un-derives, which is the day it would first be discovered wrong.
//
// Asked first, it is exercised by every world hit in every frame of every live
// run from the moment it lands, and it returns THE SAME ANSWER the old route
// returned: today the world is still a CBaseEntity, so the SDK arm would have
// found it too. Same answer, different route, verified live — and then T3
// changes what the old route says without changing what this one says.
//
// ONE DEFINITION PER DLL, and each sits in the file that already made this
// exact comparison: `world_subject_dll.cpp` (step 4a moved it out of the
// stock `gametrace_dll.cpp`) and `gametrace_client.cpp`, beside
// `CGameTrace::DidHitWorld()`, which is now expressed in terms of it.
//
// THE CLIENT'S ARM RETURNED THE SAME ANSWER AS THE SDK ROUTE UNTIL STEP 5e AND
// DOES NOT ANY MORE. gametrace_client.cpp's own header used to say the client's
// answer "will not change at T3", because C_World was staying a C_BaseEntity;
// 5e took it off that hierarchy too, so on this side as well the SDK route now
// answers NULL for the world and this arm is the only one that does not.
//
// Two spellings of "is it the world" in one DLL is how the two sides drift.
//
//=============================================================================//
#pragma once

class IHandleEntity;

//-----------------------------------------------------------------------------
// THE RULE, with the singleton passed in — so that it can be checked.
//
// The two per-DLL definitions below differ in ONE thing: where they get the
// world's pointer from (`GetWorldEntity()`, `ClientEntityList().GetBaseEntity(
// 0 )`). Everything else about the question is this line, and this line has a
// bug in it that is not obvious and would be invisible in a running game until
// the wrong moment:
//
//     return pHandleEntity == pWorld;          // WRONG
//
// **BOTH SIDES ARE NULL DURING LEVEL LOAD.** `g_WorldEntity` is set by CWorld's
// constructor and is NULL before it; the client's singleton outlives every map
// and so answers its own pointer instead through `IsBoundToTheLevel()`, which
// is false until the level's create call has bound entity 0. A trace taken in
// that window — and traces ARE taken in it, world_adapter.cpp's own header
// records colour correction doing exactly this during LevelInit — carries
// `m_pEnt == NULL`, and `NULL == NULL` is TRUE. So the naive spelling reports
// "you hit the world" for every trace that hit NOTHING, for the whole of level
// load, and reports it most confidently when there is no world at all.
//
// That is this header's own defect arriving from the other direction: it exists
// to stop "hit the world" and "hit nothing" reading alike, and the one-line
// version of it makes them read alike.
//
// Which is why the rule is here, engine-free, taking both pointers, rather than
// being two copies of one comparison in two DLLs: world_subject_test.cpp can
// hold two addresses and a NULL and pin all four cases, and neither DLL can
// drift from what it pins.
//-----------------------------------------------------------------------------
inline bool SourceSharp_IsTheWorldHandle( const IHandleEntity *pHandleEntity,
                                          const IHandleEntity *pWorldHandle )
{
	// NOTHING is not the world, even when there is no world.
	//
	// EITHER HALF ALONE WOULD BE SUFFICIENT, measured, and that is written down
	// because it is not what the line looks like. With only `!pHandleEntity`,
	// the case (entity, NULL) reaches the comparison and `entity == NULL` is
	// already false; with only `!pWorldHandle`, (NULL, world) reaches it and
	// `NULL == world` is already false. The ONLY case that needs a guard at all
	// is (NULL, NULL), and each half catches it on its own.
	//
	// So neither half is individually falsifiable. world_subject_mutate.py
	// found that by removing each and getting a green suite, and it now removes
	// each on every run and REQUIRES green -- so the day this stops being a
	// redundancy, that harness says so instead of this comment quietly becoming
	// false. Both halves are kept because they state two different facts, and a
	// reader given only one would have to re-derive the other.
	if ( !pHandleEntity || !pWorldHandle )
		return false;

	return pHandleEntity == pWorldHandle;
}

//-----------------------------------------------------------------------------
// Whether this handle IS the world.
//
// NULL in answers false: "nothing" is not the world, and keeping those two
// distinguishable is the whole of what this header is for.
//
// Defined in sourcesharp/game/server/world_subject_dll.cpp on the server and
// game/client/gametrace_client.cpp on the
// client — each of them the rule above, over that DLL's own singleton.
// Declared here rather than in either so that shared code — util_shared.h,
// movement_ground.h — can ask without naming a per-DLL type.
//-----------------------------------------------------------------------------
bool SourceSharp_IsTheWorld( const IHandleEntity *pHandleEntity );

#ifndef CLIENT_DLL
class CWorld;
class CBaseHandle;

//-----------------------------------------------------------------------------
// The same question, answered as the object rather than as a bool.
//
// The typed form of the arm above, and it exists so that AnyEntity_t can hold
// a THIRD POINTER of which exactly one is set -- which is the shape that type
// already has for its other two, and the shape PhysicsOwner_t and Toucher_t
// have for theirs. A bool beside two pointers would be a fourth spelling of
// "which kind is this", and four spellings of one question is what
// shell_resolve.h exists to have stopped.
//
// SERVER ONLY, for the same reason anyentity.h is server only: AnyEntity_t is
// the SERVER's three-armed resolve and there is no client twin of it to hold a
// third pointer. Step 5e took C_World off C_BaseEntity too -- so the client's
// world is now on the same third hierarchy this header describes -- but nothing
// on that side asks the question AS AN OBJECT, only as the bool above, and a
// typed arm with no caller would be a second spelling of a question nobody is
// asking. The bool IS on both sides, because shared code calls it.
//
// Returns NULL for anything that is not the world, including NULL.
//-----------------------------------------------------------------------------
CWorld *SourceSharp_WorldFromEntityHandle( IHandleEntity *pHandleEntity );

//-----------------------------------------------------------------------------
// The same question again, asked of a HANDLE rather than of a pointer.
//
// The world is the one entity the slot table does not hold. §3.3's table binds
// SHELLS -- one type in, that type out, which is what makes its single
// static_cast sound -- and the world is not one: it is a CWorld, at a fixed
// index, for the life of the map. Putting it in the table would put a second,
// unrelated type behind the same void * and the resolver's cast would be
// answering about whichever object happened to be there.
//
// So the world's binding is the world itself: one singleton, one handle it
// already stores for IHandleEntity's sake, and this compares the two. That is
// the whole of it -- no table, no list, no cast -- and it is the arm
// SourceSharp_EntityFromHandle asks between the slot table and what is left of
// the SDK's list.
//
// THE SERIAL IS COMPARED, not just the index. CBaseHandle::operator== is the
// packed compare, so a handle to the PREVIOUS map's world does not resolve to
// this one's: CWorld::AttachToEdictZero takes a new serial every time it runs.
//
// Returns NULL for anything that is not the world, including an invalid handle.
//-----------------------------------------------------------------------------
CWorld *SourceSharp_WorldFromHandle( const CBaseHandle &handle );
#endif // !CLIENT_DLL
