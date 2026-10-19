//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's entity resolvers ======================//
//
// plan_entity.md Phase 5a. What the CLIENT SHIM asks when it has an entity
// index or a handle and needs something the engine understands back.
//
// WHY THIS FILE EXISTS AT ALL, said once here rather than at each call site.
// Before 5a the client shim asked `cl_entitylist` directly — 34 code sites
// across six TUs, each one naming CClientEntityList, the class clause 2 of
// plan_entity.md's definition of done says may not survive. Every one of those
// questions is one of five, and every one of the five is an index or a handle
// lookup, so they collapse to the declarations below. The shim now names no
// entity list, and the port's remaining coupling to the stock one is five
// functions in shell_cliententitylist.cpp and nothing else.
//
// THESE ARE NOT AN ENTITY LIST AND MAY NOT BECOME ONE. Every function here
// takes an index or a handle the CALLER ALREADY HOLDS and answers about that
// one entity. None of them produces an index, a count of live entities, a
// first/next pair or a pointer into any storage — that is the same line
// slot_table.h draws and for the same reason. "Which entities are there" is the
// managed EntityDirectory's question (§3.5); if you are about to add a
// SourceSharp_ClientEntityAt( int nth ) here, it is that question wearing an
// index's clothes.
//
// WHY NOT JUST slot_table.h. The slot table answers with the concrete SHELL,
// which is what the shim wants when it means "one of ours". These answer with
// the ENGINE INTERFACE, which is what the shim wants when it means "whatever is
// at this index" — a beam endpoint, a trace's pass entity, a tempent's owner.
// While stock client entities still exist those two sets differ, and a caller
// that used the shell resolver for the second question would silently stop
// seeing half the world. So both exist, they are named for the difference, and
// shell_cliententitylist.cpp is the one place that knows there are two sets.
//
//===========================================================================//

#ifndef SOURCESHARP_SHELL_CLIENTENTITYLIST_H
#define SOURCESHARP_SHELL_CLIENTENTITYLIST_H
#ifdef _WIN32
#pragma once
#endif

#include "basehandle.h"

class IClientEntity;
class IClientNetworkable;
class IClientUnknown;
class IClientRenderable;
class IClientThinkable;

// GUARDED, like every other forward declaration of this class in the tree: when
// step 4 arms SS_CBASEENTITY_ALIAS the name becomes an alias for something else
// and a bare `class C_BaseEntity;` here is a hard error in a file whose author
// never heard of the switch. alias_mechanics_test.cpp is what says so.
#include "igameentity.h"

//-----------------------------------------------------------------------------
// BY INDEX. The engine's own currency: an entity index out of a user message,
// a networked field, a usercmd or a tempent spawn.
//
// The index is not validated against anything but the table's own bounds — an
// index naming no entity answers NULL, which is every stock caller's guard
// (`if ( IClientEntity *pClient = ... )`) written once.
//-----------------------------------------------------------------------------
IClientUnknown     *SourceSharp_ClientUnknownFromIndex( int nEntry );
IClientNetworkable *SourceSharp_ClientNetworkableFromIndex( int nEntry );
IClientEntity      *SourceSharp_ClientEntityFromIndex( int nEntry );

//-----------------------------------------------------------------------------
// BY HANDLE. The serial is checked, so a handle to an entity that has died
// answers NULL rather than whatever took its entry.
//-----------------------------------------------------------------------------
IClientUnknown     *SourceSharp_ClientUnknownFromHandle( const CBaseHandle &hEnt );
IClientEntity      *SourceSharp_ClientEntityFromHandle( const CBaseHandle &hEnt );

// The networkable face by HANDLE, the exact twin of the by-index one above.
// Added by lane `f-list` for CEffectData::entindex(), which held a handle and
// had to spell `ClientEntityList().GetClientNetworkableFromHandle` for want of
// this -- and so carried the stock list's symbol into a TU that stays compiled.
// Both hierarchies implement this face, so it answers for either.
IClientNetworkable *SourceSharp_ClientNetworkableFromHandle( const CBaseHandle &hEnt );

//-----------------------------------------------------------------------------
// THE OTHER TWO ENGINE FACES OF THE SAME OBJECT, by handle.
//
// Added by step p7-exit for the STOCK client TUs that stay compiled -- the beam
// list, the tempent list, the think list, the particle systems -- which asked
// CClientEntityList for exactly these two and for nothing else. They are the
// same shape as the two above: one handle in, one interface out, no iteration
// and no count, which is the line the banner at the top of this file draws.
//
// They answer for a managed entity and for a stock one alike, as the two above
// do -- while the seam exists BOTH sets are in the stock list too, so this is
// not a widening. It is the table asked first, and a call site that does not
// have to be found again when cliententitylist.cpp goes.
//-----------------------------------------------------------------------------
IClientRenderable  *SourceSharp_ClientRenderableFromHandle( const CBaseHandle &hEnt );
IClientThinkable   *SourceSharp_ClientThinkableFromHandle( const CBaseHandle &hEnt );

//-----------------------------------------------------------------------------
// INDEX TO HANDLE. The inverse of the above, for a caller that must hold on to
// what it found: an index is only good until its entry is reused.
//
// Stock's CClientEntityList::EntIndexToHandle, without the entity list.
//-----------------------------------------------------------------------------
CBaseHandle         SourceSharp_ClientHandleFromIndex( int nEntry );

//-----------------------------------------------------------------------------
// BIND AND UNBIND — the two calls C_StandaloneEntity's constructor and
// destructor make, and the only two places a client shell enters or leaves the
// slot table.
//
// ONE CALL AND NOT TWO on purpose. Binding the slot table and registering with
// the stock list while it still exists have to happen together and in that
// order, and a shell that did one without the other is invisible to half the
// client — the failure mode is PostDataUpdate never running, an entity that
// exists and holds nothing, and nothing saying why. Behind one name they cannot
// come apart, and when the stock half goes it goes from one body.
//
// bind takes the CONCRETE C_StandaloneEntity *, never an interface pointer:
// slot_table.h stores what it is given and the resolver static_casts it back,
// so a base-typed argument would compile and produce a pointer wrong by the
// base's offset. The parameter type is what makes that a compile error.
//-----------------------------------------------------------------------------
class C_StandaloneEntity;

void SourceSharp_ClientEntityBind( C_StandaloneEntity *pShell, int nEntNum, int nSerialNum );
void SourceSharp_ClientEntityUnbind( C_StandaloneEntity *pShell );

//-----------------------------------------------------------------------------
// THE WORLD'S ARM -- worldspawn is not in the slot table, and this is how it is
// found without a list.
//
// slot_table.h states the rule for the server and it is the same rule here: the
// table binds SHELLS -- one type in, that type out, which is what makes its
// single static_cast sound -- and the world is not one. On this side it is a
// C_World, a process-lifetime singleton the factory owns, bound to entity 0 for
// the life of a map by ClientWorldFactory's create call. Putting it in the
// table would put a second, unrelated type behind the same void * and the
// resolver's cast would answer about whichever object happened to be there.
//
// So the world's binding is the world itself: one singleton, one handle it
// already stores for IHandleEntity's sake, and these compare the two. That is
// the server's SourceSharp_WorldFromHandle (world_subject.h), on the client, in
// the shape §3.3 asks for -- the same arm, not a second design.
//
// WHY THE CLIENT NEEDED ONE AND DID NOT HAVE ONE. Lane `entlist-client`
// measured the seam over a full two-pass live gate: of 5.07M questions the slot
// table could not answer, 2.17M were `CWorld` at entry 0, asked BY INDEX --
// every engine GetClientNetworkable( 0 ) missing the table and falling through
// to what is left of CClientEntityList. The world was in the stock list and
// nowhere else, so the stock list was the only thing that could answer for it.
// These three are what answer for it now.
//
// THE SERIAL IS COMPARED, not just the index: C_World is reused for every map
// and takes a fresh serial from each create call, so a handle to the PREVIOUS
// map's world does not resolve to this one's. That is the reload bug the
// server's world_release_test.cpp stands over, on this side.
//
// AN UNBOUND WORLD ANSWERS NULL AND AN INVALID HANDLE -- "there is no level
// loaded" and "you asked about something that is not the world" are the same
// answer here, and both are the absence world_subject.h keeps distinct from
// "you hit the world". Nothing here dereferences an unbound singleton.
//
// DEFINED IN world_adapter_client.cpp, beside the object that owns the handle,
// exactly as the server's twin is defined in world_adapter.cpp. Declared here
// because this header is what the resolvers ask.
//-----------------------------------------------------------------------------
IClientUnknown *SourceSharp_ClientWorldFromIndex( int nEntry );
IClientUnknown *SourceSharp_ClientWorldFromHandle( const CBaseHandle &hEnt );

// The handle the world is bound to, or an invalid handle when no level is
// loaded. The two counts IClientEntityList maintains need the ENTRY and not the
// object -- "is there a world" and "at which index" -- and this is the one ask
// that answers both without a second accessor.
CBaseHandle     SourceSharp_ClientWorldHandle( void );

//-----------------------------------------------------------------------------
// RELEASES THE WORLD AT LEVEL SHUTDOWN. True if there was a bound world to
// release, false if no level was loaded -- a positive answer either way, which
// is what the caller asserts on.
//
// The world's per-map UNBINDING is done today by CClientEntityList::Release()
// walking the stock list, because C_World::Init binds the singleton into it
// through SourceSharp_LegacyListBind. That file is a removal-set row, so armed
// nothing unbinds the world -- and a world that keeps the previous map's handle
// is the reload bug the SERIAL in the handle exists to make impossible.
// Defined in world_adapter_client.cpp beside the object that owns the handle.
//-----------------------------------------------------------------------------
bool            SourceSharp_ClientWorldLevelShutdown( void );

//-----------------------------------------------------------------------------
// AND THE FOUR RULES THE ARM IS MADE OF, HERE AND ENGINE-FREE, so that they can
// be checked without a game.
//
// This is world_subject.h's arrangement and not a new one: the RULE lives in a
// header that takes nothing from Valve but a handle, the STORAGE lives with the
// object, and a gtest can hold two handles and pin every case. The three
// functions above are the storage half; these four are what they decide with,
// and shell_cliententitylist.cpp's two counts use them directly.
//
// EVERY ONE OF THEM TESTS IsValid() FIRST, and in three of the four that guard
// is the only thing between a correct answer and a confidently wrong one:
//
//   * `CBaseHandle::GetEntryIndex()` ON AN INVALID HANDLE RETURNS
//     NUM_ENT_ENTRIES-1 -- 8191, a real entry -- deliberately
//     (basehandle.h:145-153 explains the code that depends on it). So an
//     unguarded index compare says "that is the world" to every caller asking
//     about entry 8191 while no level is loaded.
//   * the handle compare needs it only for (invalid, invalid): an unbound world
//     and a caller asking about nothing would otherwise match, which is
//     world_subject.h's own defect arriving from the other direction.
//   * the highest-entity-index answer is a LOOP BOUND the engine walks, so an
//     unbound world contributing 8191 would send it over eight thousand entries
//     that do not exist.
//
// The count's guard is the plain one: no level, no world, nothing to add.
//-----------------------------------------------------------------------------
inline bool SourceSharp_ClientWorldAnswersForIndex( const CBaseHandle &hWorld, int nEntry )
{
	return hWorld.IsValid() && hWorld.GetEntryIndex() == nEntry;
}

inline bool SourceSharp_ClientWorldAnswersForHandle( const CBaseHandle &hWorld,
                                                     const CBaseHandle &hAsked )
{
	// operator== is the PACKED compare -- entry AND serial -- which is what
	// makes the previous map's world handle fail to resolve to this map's.
	return hWorld.IsValid() && hWorld == hAsked;
}

inline int SourceSharp_ClientHighestWithWorld( const CBaseHandle &hWorld, int nTableHighest )
{
	if ( !hWorld.IsValid() )
		return nTableHighest;

	const int nWorld = hWorld.GetEntryIndex();

	return nWorld > nTableHighest ? nWorld : nTableHighest;
}

inline int SourceSharp_ClientCountWithWorld( const CBaseHandle &hWorld, int nTableCount )
{
	return hWorld.IsValid() ? nTableCount + 1 : nTableCount;
}

// THE INDEX OF WHAT A TRACE HIT, read off the hit's own handle (C6: what
// CGameTrace::GetEntityIndex answered, without the trace member). The entry
// when this side's table resolves the handle (bResolves) and the entry is a
// networked one, where entindex() equals it; -1 otherwise, which is what a
// client-only entity's entindex() and a non-entity's missing networkable said.
inline int SourceSharp_ClientIndexOfHit( const CBaseHandle &hHit, bool bResolves )
{
	if ( !hHit.IsValid() || !bResolves )
		return -1;

	const int nEntry = hHit.GetEntryIndex();
	return nEntry < MAX_EDICTS ? nEntry : -1;
}

//=============================================================================
// THE PORT'S OWN CODE, MOVED OUT OF THE STOCK FILE THE FLIP DELETES
//=============================================================================
//
// Everything below this line was DEFINED in game/client/cliententitylist.cpp
// and declared either in game/client/cliententitylist.h or in
// game/client/c_baseentity.h. All three are stock files; the first is a vpc row
// the client flip removes. So eleven of the port's own definitions were sitting
// inside a translation unit whose deletion is somebody else's commit, and the
// lane that deleted it would have deleted them too -- silently, because a
// missing definition of a function nobody outside the shim calls reads as a
// link error in the shim rather than as a capability that was removed.
//
// Nothing about what any of them does changed in the move. Each is still the
// one call the shim used to make, written where the stock class is visible.
//
// THEY STILL NAME `cl_entitylist`, AND THAT IS WHAT THIS BLOCK IS FOR. The
// port's whole coupling to CClientEntityList is now six function bodies in one
// delimited region of one file the port owns, instead of a region of a file
// that leaves. tools/entity-gates row 2 counts those mentions; they did not
// become fewer by moving, they became findable and deletable in one edit.
//
// REMOVAL OWNER: lane 9 (`c8-flip`), the commit that takes
// game/client/cliententitylist.cpp out of client_csharp.vpc. This block and
// that vpc row go together; neither is correct without the other.
//=============================================================================

//-----------------------------------------------------------------------------
// FOUR CALLS INTO THE STOCK CLIENT ENTITY LIST, and that is the measurement.
//
// Free functions rather than methods, exactly as they were: a caller needs no
// declaration of CClientEntityList to make the call. If you are about to add a
// fifth, ask first whether the caller wanted the slot table (slot_table.h) or
// the resolvers above -- those two are where a question about ONE entity
// belongs, and nothing else has a reason to reach the stock list.
//
// IT WAS SIX UNTIL PHASE 1c. SourceSharp_LegacyListHighestEntityIndex (`return
// -1`) and SourceSharp_LegacyListNumberOfEntities (`return 0`) were each read
// in exactly one place -- an `if ( nLegacy > ours )` arm in
// shell_cliententitylist.cpp's own GetHighestEntityIndex and NumberOfEntities,
// whose only effect was to bump a fallback counter. Against a constant -1 and
// a constant 0 those comparisons can never be true, so the arms were dead code
// guarding a counter that could not move, and all three went together.
//-----------------------------------------------------------------------------
CBaseHandle     SourceSharp_LegacyListBind( IClientUnknown *pUnknown, int nEntNum, int nSerialNum );
void            SourceSharp_LegacyListUnbind( const CBaseHandle &hEnt );
IClientUnknown *SourceSharp_LegacyListUnknownFromIndex( int nEntry );
IClientUnknown *SourceSharp_LegacyListUnknownFromHandle( const CBaseHandle &hEnt );

//-----------------------------------------------------------------------------
// THE FOUR ASK COUNTERS AND THEIR RECORDER WENT IN PHASE 1c.
//
// c_baseentity.h's SDKEntityFromUnknown bumped two of them and called the
// recorder; shell_standalone_client.cpp's cs_cl_handle_cross_check printed all
// four and reset two. The readout went with the other client bring-up
// instruments, and it was the only reader -- nothing in the server DLL, in
// managed code or under tools/ ever named any of the four -- so the counters,
// the recorder and the arm in SDKEntityFromUnknown that fed them went with it.
//
// The SAFETY they watched is unchanged and is asserted structurally rather
// than counted: client_typed_handle_sweep_test.cpp drives the crossing with an
// IClientUnknown that is not an IClientEntity, which is the object the
// counters existed to notice.
//-----------------------------------------------------------------------------

#endif // SOURCESHARP_SHELL_CLIENTENTITYLIST_H
