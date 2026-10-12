//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the server's world question, and the trace accessors
// SS_REASON e  a synchronous mid-step answer
//          that ask it ==============================================//
//
// THE WHOLE OF game/server/gametrace_dll.cpp, MOVED. plan_entity.md step 4a.
//
// WHY THIS IS A MOVE AND NOT A COPY, and why the move is the deletion.
//
// `gametrace_dll.cpp` was one of the seven stock TUs of 4a's cluster, and it
// was the only one that had already stopped being stock: two of its five
// functions — `SourceSharp_IsTheWorld` and `SourceSharp_WorldFromEntityHandle`
// — were written BY this port, and the three that remain are
// `CGameTrace`'s own accessors. There is no entity declared here, no
// LINK_ENTITY_TO_CLASS, no datadesc and no send table; what kept the file in
// `server_csharp.vpc` was its address, not its content.
//
// So 4a does not port it, does not stub it and does not leave it compiled
// behind a guard. It moves the five definitions to the shim, where the two
// SourceSharp_ ones already belonged, and the vpc line goes. The stock file
// stays on disk for `server_base.vpc` — the non-C# build still compiles it —
// which is the same treatment every earlier phase gave a file it took out of
// the C# vpc.
//
// WHAT MAY NOT MOVE WITH THEM: the three `CGameTrace` methods are declared
// non-inline in `public/gametrace.h`, which is engine-shared. Every DLL that
// links a `trace_t` needs exactly one definition of each, and the client's is
// `game/client/gametrace_client.cpp` (untouched by this step — that side is
// 6c's). §6's "trace filters, partition enumerators, recipient filters" is the
// entry that keeps them native: a trace is filled by the engine, mid-query,
// and its accessors are read from inside engine callbacks.
//
// WHAT THE ARMED ARMS ANSWER is unchanged, word for word, from the stock file;
// the two `#ifndef SS_CBASEENTITY_ALIAS` sites moved with their functions and
// cpp_alias_t6_test.cpp carries the row at this address instead of the old one,
// with the shard total unchanged. That is the same bookkeeping A4, A9 and C5
// did for every guard they moved, and for the same reason: an armed-site count
// is not an invariant, but where the guards ARE is.
//
//=============================================================================//

#include "gametrace.h"
#include "ss_world.h"
#include "world_subject.h"


//-----------------------------------------------------------------------------
// THE SERVER'S THIRD ARM OF THE PARTITION. See world_subject.h for why it is an
// identity compare against a singleton and not a question asked of the object.
//
// LIFTED OUT OF DidHitWorld() RATHER THAN COPIED BESIDE IT. The comparison was
// already here and was already the right one; what it lacked was a name and a
// caller that is not a trace. Shared code -- movement_ground.h, and every
// resolver built on util_shared.h -- has an IHandleEntity and no trace_t, so it
// could not reach this fact at all, and the two-armed partition it could reach
// answers NULL for the world the day CWorld un-derives.
//
// THE CAST IS UPWARD AND ALONG A SINGLE-INHERITANCE CHAIN, which is what makes
// it a conversion the compiler performs rather than a reinterpret this file
// asserts: CWorld -> CBaseEntity -> IServerEntity -> IServerUnknown ->
// IHandleEntity today, and CWorld -> IServerEntity -> ... after T3. Both are
// static_cast-able and neither needs this file to change.
//-----------------------------------------------------------------------------
bool SourceSharp_IsTheWorld( const IHandleEntity *pHandleEntity )
{
	// UNGUARDED, AND IT USED TO BE `return false` ARMED. That arm read the
	// alias onto a class the alias does not touch: it argued "armed, CBaseEntity
	// is IGameEntity, which is NOT on the IHandleEntity chain, so there is no
	// conversion to make". CWorld IS NOT A CBaseEntity ON EITHER SIDE OF THE
	// FLIP -- plan_cworld_trace.md T3 un-derived it, and the prose directly
	// above this function already says so ("CWorld -> IServerEntity -> ... after
	// T3. Both are static_cast-able and neither needs this file to change").
	// The cast below is CWorld -> IServerEntity -> IServerUnknown ->
	// IHandleEntity, single inheritance the whole way, and SS_CBASEENTITY_ALIAS
	// is not on that chain. shell_entityslots.cpp performs the identical cast on
	// a CWorld * in an ARMED translation unit today.
	//
	// WHAT THE `return false` COST, measured on a live armed build rather than
	// argued: SourceSharp_WorldFromEntityHandle is a projection of this bool, so
	// it answered NULL for the world always; SourceSharp_ResolveEntity therefore
	// resolved the world's own handle to nothing; and
	// SourceSharp_FindEntityByClassname, whose world clause fires on
	// `!start.IsValid()`, returned the world FOREVER -- an infinite loop in
	// every find-by-classname that can reach it, `debug-api ents "*"` included.
	// CGameTrace::DidHitWorld() answered false for the world in the same build,
	// and DidHitNonWorldEntity() answered true for it.
	//
	// IT COULD NOT HAVE BEEN SEEN BEFORE. Armed, no world was ever constructed
	// (the map parse was guarded out -- see shell_level.cpp), so this arm
	// ran against a level that had nothing to be the world and `false` was
	// indistinguishable from the truth. That is the "plausible constant" shape:
	// the value was ASSIGNED here, never READ off the object.
	//
	// THE RULE IS world_subject.h's AND NOT SPELLED HERE. What this function
	// contributes is the one thing that is the server's -- WHICH POINTER the
	// world is -- and the NULL handling, which is the part with the bug in it,
	// is checked by world_subject_test.cpp against the same inline the client
	// half calls.
	return SourceSharp_IsTheWorldHandle(
		pHandleEntity, static_cast< const IHandleEntity * >( GetWorldEntity() ) );
}

//-----------------------------------------------------------------------------
// The typed form, for the third arm of AnyEntity_t.
//
// A PROJECTION OF THE BOOL ABOVE AND NOT A SECOND COMPARISON: it asks the same
// function every other caller asks and only then names the singleton, so there
// is no way for the two to disagree about the same pointer. The same rule
// shell_resolve.h states for its own projections.
//-----------------------------------------------------------------------------
CWorld *SourceSharp_WorldFromEntityHandle( IHandleEntity *pHandleEntity )
{
	return SourceSharp_IsTheWorld( pHandleEntity ) ? GetWorldEntity() : NULL;
}

//-----------------------------------------------------------------------------
// The same question of a HANDLE -- plan_entity.md §3.3, lane entlist-server.
//
// THE WORLD IS THE ONE ENTITY THE SLOT TABLE DOES NOT HOLD, and world_subject.h
// carries the argument: the table binds SHELLS, one type in and that type out,
// which is what makes its single static_cast sound. A CWorld behind the same
// void * would make the resolver answer about whichever object was there.
//
// So the world's binding is the world: the singleton, and the handle it already
// stores because IHandleEntity requires one. This compares the two.
//
// THE PACKED COMPARE, so the serial is checked. CWorld::AttachToEdictZero takes
// a NEW serial each time it runs, so a handle to the previous map's world is
// stale here -- which is the same rule the slot table's Unbind enforces for a
// shell and the same one CGlobalEntityList enforced for this object before the
// registration moved.
//
// The invalid handle is refused first. Every entity that is not bound anywhere
// carries INVALID_EHANDLE, and so does the world between AttachToEdictZero and
// the destructor's Term(); without this guard those two would match each other.
//-----------------------------------------------------------------------------
CWorld *SourceSharp_WorldFromHandle( const CBaseHandle &handle )
{
	if ( !handle.IsValid() )
		return NULL;

	CWorld *pWorld = GetWorldEntity();

	if ( !pWorld || pWorld->GetRefEHandle() != handle )
		return NULL;

	return pWorld;
}

bool CGameTrace::DidHitWorld() const
{
	// ONE COMPARISON, ONE PLACE. This used to spell the pointer compare itself;
	// it now asks the free function above, so a trace and a bare handle cannot
	// come to different conclusions about the same object.
	return SourceSharp_IsTheWorld( m_pEnt );
}


bool CGameTrace::DidHitNonWorldEntity() const
{
	return m_pEnt != NULL && !DidHitWorld();
}

