//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the control block, and draining what C# wrote ========//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// plan_nettable_generic.md Part 9, the engine-facing half of entity_slots.h.
//
// WHAT THIS REPLACES. Every managed property set used to call
// EntityGetDataBlob, which resolved a handle, walked the standalone registry,
// did up to two dynamic_casts and marked the edict changed. Writing five fields
// on one entity cost five of those. And because C# writes through a bare
// pointer, native could not tell a read from a write, so a READ marked the edict
// too — deliberately, since the alternative was a second interop call after
// every set.
//
// WHAT IT COSTS NOW. One call at Bind, which hands back the blob pointer, the
// slot and its serial. After that a set is a compare against the serial and a
// store into the dirty word, and this file turns the whole frame's stores into
// one NetworkStateChanged per entity that actually changed. A read costs the
// compare and nothing else — no call, and nothing marked.
//
// THAT LAST SENTENCE IS THE POINT OF THE STEP, not a tidy-up. The engine
// skips SendTable_Encode entirely for an entity whose edict is unchanged, so
// an entity nothing touched costs nothing at all. Marking on read gave that
// up for anything anybody merely
// looked at — including the dev inspector, which put every entity it was
// watching into every delta compare. With the generic payload's 512-prop tier
// that is 512 proxy calls and 512 bit-compares per read.
//
//=============================================================================//

// (tools/entity-gates-allowlist names the resolver's cast by line: keep it.)
#include "basehandle.h"
#include "entity_slots.h"
#include "slot_table.h"
#include "standalone_netserial.h"
#include "shell_standalone.h"
#include "shell_resolve.h"
#include "world_subject.h"
#include "ss_world.h"
#include "sourcesharp.h"
#include "native_command_body.h"


// The block sizes itself without const.h so that the gtest can build it with no
// engine at all; this is where the two numbers are made to agree. A block
// SHORTER than the entity list would silently refuse the tail of it.
COMPILE_TIME_ASSERT( SOURCESHARP_MAX_ENTITY_SLOTS >= NUM_ENT_ENTRIES );

void *SourceSharp_EntityGetDataBlob( uint32_t hEntity, int32_t *pOutSize );

//-----------------------------------------------------------------------------
// One block for the process. Static rather than allocated: it is 128KB, it is
// live from the first entity to the last, and a pointer C# holds into it must
// stay valid for the life of the side — an array that could be reallocated is
// exactly the hazard this file exists to remove.
//-----------------------------------------------------------------------------
static CSourceSharpEntitySlots g_EntitySlots;

//-----------------------------------------------------------------------------
// The entry index a packed handle names, or -1.
//
// CBaseHandle::GetEntryIndex masks with ENT_ENTRY_MASK, which is SIXTEEN bits
// wide while the entity list is thirteen — a comment in basehandle.h explains
// that as bug-submarining that a lot of code depends on. So the mask alone can
// name a slot that does not exist, and this checks rather than trusting it.
//-----------------------------------------------------------------------------
static int SourceSharp_SlotEntryOf( uint32_t hEntity )
{
	if ( hEntity == (uint32_t)INVALID_EHANDLE_INDEX )
		return -1;

	const CBaseHandle handle = CBaseHandle::UnsafeFromIndex( (int)hEntity );
	const int nEntry = handle.GetEntryIndex();

	return CSourceSharpEntitySlots::IsEntry( nEntry ) ? nEntry : -1;
}

//-----------------------------------------------------------------------------
// Marks an entity's networked state as moved, whichever hierarchy it is on.
//
// Both are asked, because half this mod's entities are not CBaseEntity and a
// drain that stopped at the SDK's list would announce some writes and swallow
// others — which is the failure shape the plan is written against, one layer
// down.
//-----------------------------------------------------------------------------
static bool SourceSharp_MarkStateChanged( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	// ARMED, ent.pBase IS ALWAYS NULL, so only the standalone arm can answer --
	// which is the whole point of this function keeping two.
	//
	// SourceSharp_ResolveEntity sets pBase from GetBaseEntity() and pStandalone
	// from GetGameEntity(), and those two partition the world exactly (see
	// shell_resolve.h). Armed there is no SDK hierarchy for the first to answer
	// for, so the arm below carries every entity.
	//
	// NetworkStateChanged is also the one ask in this shard that exists ONLY in
	// an armed survey: the TYPEDEF_SURVEY subclass carries two shims for it, and
	// the alias -- being a typedef -- has nowhere to hang them
	// (survey_arm_alias.py says so in its header). It is a real member of
	// CBaseEntity today and a real member of CStandaloneEntity, and it is not on
	// IGameEntity because the engine never asks an entity to mark itself dirty.

	if ( ent.pStandalone )
	{
		ent.pStandalone->NetworkStateChanged();
		return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// ABI: the blob, and the slot to check it against.
//
// The ONE crossing per bound state, replacing one per property access. It hands
// back four things because holding three of them without the fourth is not
// safe: the pointer alone cannot be revalidated, and a slot without its serial
// cannot say which binding it belongs to.
//
// pOutSlot receives a pointer to the slot ITSELF rather than an index into the
// block. Part 9 described a base and an index; the pointer is the same fact with
// one fewer thing to get wrong, since the block is a fixed static array and a
// slot's address is therefore stable for the life of the process. It also means
// C# never has to be told the block's base or its length, and cannot index past
// the end of something it was never handed.
//
// NULL, size 0 and serial 0 for anything that has no blob. The managed side
// turns that into a named exception at Bind rather than a state that would
// overrun on first use.
//-----------------------------------------------------------------------------
void *SourceSharp_EntityBindBlob( uint32_t hEntity, int32_t *pOutSize,
                                  SourceSharpEntitySlot_t **pOutSlot, uint32_t *pOutSerial )
{
	if ( pOutSize )
		*pOutSize = 0;
	if ( pOutSlot )
		*pOutSlot = NULL;
	if ( pOutSerial )
		*pOutSerial = SOURCESHARP_SLOT_SERIAL_NONE;

	const int nEntry = SourceSharp_SlotEntryOf( hEntity );

	if ( nEntry < 0 )
	{
		return NULL;
	}

	int32_t nSize = 0;
	void *pBlob = SourceSharp_EntityGetDataBlob( hEntity, &nSize );

	if ( !pBlob || nSize <= 0 )
	{
		return NULL;
	}

	uint32_t nSerial = SOURCESHARP_SLOT_SERIAL_NONE;

	if ( !g_EntitySlots.Open( nEntry, hEntity, &nSerial ) )
	{
		return NULL;
	}


	if ( pOutSize )
		*pOutSize = nSize;
	if ( pOutSlot )
		*pOutSlot = g_EntitySlots.Slots() + nEntry;
	if ( pOutSerial )
		*pOutSerial = nSerial;

	return pBlob;
}

//-----------------------------------------------------------------------------
// ABI: announce a write that did NOT go through a bound state.
//
// EntityData.TryWrite reaches an entity's blob by handle and field layout with
// no state object anywhere — the player's diagnostic fields and the
// runtime-networked entity's are written that way. Those writes used to be
// announced as a side effect of fetching the blob, and this step deletes that
// side effect, so they say so themselves.
//
// Deliberately NOT called by any read. That asymmetry is the entire behaviour
// change of this step, and it is only expressible because the write and the
// fetch are now separate calls.
//-----------------------------------------------------------------------------
void SourceSharp_EntityMarkStateChanged( uint32_t hEntity )
{
	SourceSharp_MarkStateChanged( hEntity );
}

//-----------------------------------------------------------------------------
// Closes an entity's slot. Called from the destructor of every shell that
// carries a blob.
//
// BEFORE the handle is torn down and before the memory goes, because after
// either of those the entry index is either unavailable or already reused. A
// slot left open past its entity is a pointer C# will happily write through
// into freed memory, with the serial still matching — the one failure this
// design must not have.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySlotClosed( uint32_t hEntity )
{
	const int nEntry = SourceSharp_SlotEntryOf( hEntity );

	if ( nEntry >= 0 )
		g_EntitySlots.Close( nEntry );
}

//-----------------------------------------------------------------------------
// Once per frame, in FrameUpdatePostEntityThink: turn the frame's stores into
// NetworkStateChanged calls, and sweep slots whose entity went without saying.
//
// The sweep is belt and braces over the destructor hooks, and it earns its place
// because the cost of a missed hook is silent corruption rather than a wrong
// value: a handle whose serial has rolled resolves to NULL, so an entity that
// died without closing its slot is found here and closed within one frame. What
// the sweep CANNOT do is close the window inside the frame it died in, which is
// exactly why the destructor hooks exist as well.
//
// Walks backwards, because Close swaps the tail entry into the freed position —
// forwards would skip whichever entry landed there.
//-----------------------------------------------------------------------------
int SourceSharp_DrainEntitySlots( void )
{

	int nMarked = 0;

	for ( int i = g_EntitySlots.ActiveCount() - 1; i >= 0; --i )
	{
		const int nEntry = g_EntitySlots.ActiveAt( i );
		const uint32_t hEntity = g_EntitySlots.HandleAt( nEntry );

		const bool bDirty = g_EntitySlots.TakeDirty( nEntry );

		if ( bDirty )
		{
			if ( SourceSharp_MarkStateChanged( hEntity ) )
			{
				++nMarked;
				continue;
			}

			// Dirty and gone. The write already landed in memory that is being
			// reused, so this is reported rather than counted: it means a shell
			// freed an entity without closing its slot.
			Warning( "[SourceSharp] entity slot %d was written and its entity is gone; "
			         "a shell is freeing without closing its slot\n", nEntry );
		}
		else if ( !SourceSharp_ResolveEntity( hEntity ).IsValid() )
		{
			g_EntitySlots.Close( nEntry );
			continue;
		}
	}

	return nMarked;
}

//-----------------------------------------------------------------------------
// SourceSharp_DrainEntitySlotsLate WAS HERE: the second drain, which
// CSourceSharpGameRules::EndGameFrame ran after the frame's last managed point
// so a write made there was announced in the frame it was made. Since
// plan_server_native_v2 T4e that point and its drain are managed's calls
// (ServerGameDll.GameFrame: the point, then IShimFrame::DrainEntitySlots), so
// both drains are the one function, SourceSharp_DrainEntitySlots, through the
// frame's slot. Draining twice stays safe: TakeDirty clears what it reports.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// A managed reload is about to happen: void every serial handed out so far.
// plan_nettable_generic.md Step 10; the argument is on Retire() in
// entity_slots.h.
//
// Called between the unload and the load, which is the only window with no
// managed code in it. Doing it before the unload would make the outgoing
// module's own Shutdown throw on any state it legitimately touched, and doing
// it after the load would risk voiding a state the incoming module had already
// bound.
//-----------------------------------------------------------------------------
int SourceSharp_RetireEntitySlots( void )
{
	return g_EntitySlots.Retire();
}

//-----------------------------------------------------------------------------
// Everything closed. Level shutdown: the entities are going, and a slot that
// outlived the map would be handed to whatever takes its entry next.
//-----------------------------------------------------------------------------
void SourceSharp_ResetEntitySlots( void )
{
	g_EntitySlots.Reset();

	// AND THE SLOT TABLE, which is the other table this file owns and has the
	// same reason to be cleared: a binding that outlived the map would be
	// handed to whatever takes its entry next. Cleared together rather than
	// through a second hook, because a level shutdown that cleared one and not
	// the other is exactly the state neither table can report.
	//
	// ClearBindings AND NOT Reset -- plan_entity.md §3.3, lane entlist-server,
	// and the difference is a crash. Reset rewinds every serial to zero;
	// ClearBindings unbinds, which BUMPS each one. The serials are the only
	// thing here that must survive a level change, because the EDICT's mirror
	// does: edict_t::m_NetworkSerialNumber is kept by the engine across a map
	// load, and SV_PackEntity reuses the previous occupant's PackedEntity and
	// ChangeFrameList whenever the serial it snapshotted still matches. A
	// rewound table hands map 2's entity at edict 5 the serial map 1's had, the
	// mirror does not move, and the new entity is packed as a continuation of
	// the dead one. CGlobalEntityList never met this: it drew its serials once
	// at static construction and only ever bumped them.
	SourceSharp_SlotTable().ClearBindings();
}

//=============================================================================
// THE SLOT TABLE -- plan_entity.md §3.3, step 1c.
//
// A SECOND TABLE IN THIS FILE, AND IT IS NOT THE ONE ABOVE. The block above is
// plan_nettable_generic.md's control block: { serial, dirty } per entity, one
// writer per word, existing so that a managed property write can announce
// itself without an interop call. What follows is plan_entity.md's slot table:
// { shell pointer, serial } per entry, existing so that an index or a handle
// out of the ENGINE can become the concrete shell without RTTI, a registry walk
// or a cast the compiler cannot check.
//
// They share a file because they share a lifetime -- both are opened when an
// entity is created, closed when it dies, and reset at level shutdown -- and
// keeping the two Close paths in one place is what stops one of them being
// forgotten. They share nothing else: different arrays, different serial
// spaces, different reasons. slot_table.h says why the second one may not be
// walked while the first one must be.
//
// WHY THE TABLE IS NOT A FILE-SCOPE GLOBAL. plan.md §1a: nothing in
// SourceSharp's C++ is a static or a global, and the exception in the block
// above (g_EntitySlots) predates the rule. The tree's answer to "one object per
// side, owned by the game system" is the shape
// shell_serversystems.cpp:236 uses for the managed system itself -- a
// function-local static inside the accessor that hands it out, so the object's
// lifetime is stated by the code that owns it rather than decided by the
// linker's static-init order. This is that shape. The game system drives its
// only lifecycle event: SourceSharp_ResetEntitySlots below is called from
// SourceSharp_ManagedLevelShutdown, and it now resets both tables.
//=============================================================================

//-----------------------------------------------------------------------------
// The server's slot table. One per side; the client's arrives with
// IClientEntityList in Phase 5a.
//-----------------------------------------------------------------------------
CSourceSharpSlotTable &SourceSharp_SlotTable( void )
{
	static CSourceSharpSlotTable s_SlotTable;
	return s_SlotTable;
}

//-----------------------------------------------------------------------------
// THE HANDLE A SHELL AT THIS ENTRY WILL CARRY, ISSUED WITHOUT BINDING.
//
// plan_entity.md §3.3: this is where CGlobalEntityList::AddEntityAtSlot's
// serial went on this side. Stock issues and binds in one call and calls
// SetRefEHandle from INSIDE it (entitylist_base.cpp:236), which is why the
// shim's `SetRefEHandle( <the entity list>->AddNetworkableEntity( ... ) )` called
// SetRefEHandle twice for every entity in the map. Here the shell's own
// SetRefEHandle is the single binder, so the two steps are two calls and the
// second one is the store.
//
// NO EDICT MIRROR HERE, deliberately: nothing has been assigned yet. The mirror
// belongs to the bind, and SourceSharp_SlotBindAt below does it -- which is the
// call SetRefEHandle makes with the handle this hands back.
//-----------------------------------------------------------------------------
CBaseHandle SourceSharp_SlotIssue( int nEntry )
{
	return SourceSharp_SlotTable().IssueHandle( nEntry );
}

//-----------------------------------------------------------------------------
// The same, for a handle assigned elsewhere -- the world's own, and the
// engine's forced serial on the client in Phase 5a.
//-----------------------------------------------------------------------------
bool SourceSharp_SlotBindAt( const CBaseHandle &handle, CStandaloneEntity *pShell,
                             edict_t *pEdict )
{
	if ( !SourceSharp_SlotTable().BindAt( handle, pShell ) )
		return false;

	StandaloneNetSerial_Mirror( pEdict, (unsigned long)handle.GetSerialNumber() );
	return true;
}

//-----------------------------------------------------------------------------
// UNBIND. Called from the shell's destructor, BEFORE the handle is torn down --
// after that the entry index is either unavailable or already reused.
//
// The entity's control-block slot is closed by SourceSharp_EntitySlotClosed
// above, on the same path and for the same reason. Two calls rather than one,
// because they take different keys: the control block is keyed by the packed
// handle and this is keyed by the entry, and an entity whose handle has already
// been invalidated can still unbind its entry.
//-----------------------------------------------------------------------------
void SourceSharp_SlotUnbind( int nEntry )
{
	SourceSharp_SlotTable().Unbind( nEntry );
}

//-----------------------------------------------------------------------------
// THE RESOLVER, AND THE ONE DOWNCAST plan_entity.md PERMITS ON AN ENTITY PATH.
//
// plan_entity.md §3.16: "the only permitted downcast on an entity path is the
// one line inside the slot-table resolver". §5's gate-3 row: it "is allowlisted
// by file and line and covered by the serial-reuse test". Both halves of that
// are real -- this file and this line are the single entry in
// tools/entity-gates-allowlist, and slot_table_test.cpp's SerialReuse cases are
// the cover. An allowlist entry is a HOLE in gate 3, so the test is the only
// thing left guarding this line: deleting the serial comparison inside
// Resolve() turns six of them red, which was verified by doing it.
//
// WHY IT IS SOUND. The stored pointer is a CStandaloneEntity * that went in
// through SourceSharp_SlotBind or SourceSharp_SlotBindAt above, which are the
// only routes into this side's table and both take a CStandaloneEntity *. So
// static_cast recovers the type that was erased, not a type somebody hoped for.
// It is not dynamic_cast because there is nothing to ask: the shell is the only
// thing bound here and (from Phase 4) is final.
//
// WHY THE SERIAL CHECK IS INSIDE RESOLVE AND NOT HERE. A cast that ran before
// the check would already have produced the pointer by the time the answer was
// known, and the version of this function that returned it anyway would look
// identical in a diff. Resolve() answers NULL or the live pointer; there is no
// arrangement of this line that casts a stale one.
//-----------------------------------------------------------------------------
CStandaloneEntity *SourceSharp_ShellFromHandle( const CBaseHandle &handle )
{
	return static_cast< CStandaloneEntity * >( SourceSharp_SlotTable().Resolve( handle ) );
}

//-----------------------------------------------------------------------------
// Resolve by entry index -- the engine's route, which carries no serial.
//
// ROUTED THROUGH THE HANDLE RESOLVER ABOVE RATHER THAN CASTING AGAIN, and that
// is the whole reason this is three lines instead of one: the allowlist permits
// exactly ONE cast site, so a second static_cast here would either fail gate 3
// or widen the allowlist to two entries, and §3.16 says the permitted number is
// one. HandleOf answers an invalid handle for an unbound entry and the live
// handle otherwise, so the serial compare inside Resolve is trivially satisfied
// and the answer is the same one a direct cast would have given.
//-----------------------------------------------------------------------------
CStandaloneEntity *SourceSharp_ShellFromIndex( int nEntry )
{
	return SourceSharp_ShellFromHandle( SourceSharp_SlotTable().HandleOf( nEntry ) );
}

//-----------------------------------------------------------------------------
// IHandleEntity to shell, which is §3.16's cast family 3 -- the eighteen shim
// sites that read a trace result or a partition walk.
//
// NO CAST AT ALL, and that is the point of the design rather than an accident:
// GetRefEHandle() is a VIRTUAL on the engine interface, so the object answers
// with its own handle whatever it really is, and the handle indexes the table.
// An IHandleEntity that is not one of ours answers a handle the table does not
// hold, or holds for somebody else, and the serial compare turns that into
// NULL. The stock spelling -- cast the IHandleEntity and hope -- cannot tell
// those two apart.
//-----------------------------------------------------------------------------
CStandaloneEntity *SourceSharp_ShellFromHandleEntity( IHandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return NULL;

	return SourceSharp_ShellFromHandle( pHandleEntity->GetRefEHandle() );
}

//-----------------------------------------------------------------------------
// THE UNION RESOLVE -- a handle to whatever holds it, as the face both
// hierarchies share. plan_entity.md §3.3, lane entlist-server.
//
// WHAT IT REPLACES. Five sites in four files spelled
// the entity list's LookupEntity( handle ), and each of them had to know that
// CGlobalEntityList was the UNION of the two hierarchies rather than the SDK's
// list -- SourceSharp_ResolveEntity's one lookup, the standalone's owner for a
// trace filter, the player's ground entity, and the two in
// takedamageinfo_library.cpp. That union was the whole reason the port could
// not put the entity list down: it was not asking the SDK a question, it was
// asking the list the port had been keeping its own entities in.
//
// THREE ARMS, IN THIS ORDER, AND ONLY THE LAST IS A LIST:
//
//   the slot table   every shell in this mod, serial checked by Resolve.
//   the world        one object at a fixed index, matched on its own handle.
//                    Not in the table and not a shell -- world_subject.h has
//                    the argument, which is that a second type behind the same
//                    void * would make the resolver's one static_cast answer
//                    about whichever object was there.
//   the SDK's list   what is left of CGlobalEntityList's population:
//                    physics_entity_solver, physics_npc_solver, the ragdolls,
//                    env_entity_dissolver, env_ragdoll_boogie and the four
//                    subs.cpp point entities. THE ONE PLACE THE SHIM STILL
//                    NAMES THE ENTITY LIST ON A RESOLUTION PATH, and it leaves
//                    with game/server/entitylist.cpp.
//
// THE THREE POPULATIONS ARE DISJOINT BY CONSTRUCTION, so the order is an order
// of cost and not a precedence: a shell binds its edict entry in the table and
// nowhere else, the world owns entry 0 and is in neither of the other two, and
// an SDK entity reaches the list through CBaseEntity::PostConstructor and the
// table through nothing. Two arms cannot both answer.
//
// UPCASTS ONLY. CStandaloneEntity -> IServerEntity -> IHandleEntity is a
// conversion the compiler performs along a single-inheritance chain, and so is
// CWorld's; the one downcast on this path is the one inside
// SourceSharp_ShellFromHandle, which is allowlisted and covered.
//-----------------------------------------------------------------------------
IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle )
{
	if ( !handle.IsValid() )
		return NULL;

	if ( CStandaloneEntity *pShell = SourceSharp_ShellFromHandle( handle ) )
		return static_cast< IHandleEntity * >( static_cast< IServerEntity * >( pShell ) );

	if ( CWorld *pWorld = SourceSharp_WorldFromHandle( handle ) )
		return static_cast< IHandleEntity * >( static_cast< IServerEntity * >( pWorld ) );

	// THE SDK HIERARCHY'S OWN LIST, and the only remaining reason this file
	// includes it. Nothing of this port's is in here any more -- and ARMED,
	// nothing at all is: no compiled server translation unit registers an entity
	// of that hierarchy, which is the residue the level steps already measured.
	// NULL is the TRUE answer armed, not a stub's: the two arms above are the
	// whole population, and they are disjoint from this one by construction, so
	// dropping it cannot shadow either.
	return NULL;
}

// cs_sv_slot_table, the slot-table readout, is a diagnostic: it lives in
// shell_entityslots_debug.cpp, which only a debug build compiles.
