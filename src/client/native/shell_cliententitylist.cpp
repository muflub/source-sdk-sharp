//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — IClientEntityList over the slot table ==============//
// SS_REASON b  an interface resolved by name at load
//
// plan_entity.md Phase 5a. THE INTERFACE THE ENGINE HOLDS AS
// VClientEntityList003 IS DEFINED HERE, and it answers out of §3.3's slot table
// rather than out of CClientEntityList.
//
// §3.3, on the client half of the table: "On the client it *is* the
// IClientEntityList implementation the engine resolves by name: the nine
// methods in public/icliententitylist.h are index and handle lookups plus
// counts, and the counts are maintained by bind and unbind." This file is that
// sentence. Nine methods, an array read each, and no list.
//
//=============================================================================
// THE ONE THING IN THIS FILE THAT CAN CRASH THE ENGINE
//=============================================================================
//
// GetClientUnknownFromHandle MAY NOT ANSWER NULL. Not "should not" — the
// engine's trace calls GetCollideable() on the answer without a test.
//
// That is ClipRayToEntity's path — a ray traced against ONE named entity that
// is not a static prop — and the ICollideable it produces is dereferenced
// without a test too (the trace reads GetCollisionModel() straight off it).
// So there is no null-object trick
// available here: an object answering a NULL collideable moves the same crash
// four frames later. The only correct behaviour is to ANSWER, and the arms
// below are ordered so that everything reachable answers.
//
// 0e recorded ZERO engine calls to this method over three map loads. That is
// not evidence: the engine's PARTITION path, which is the busy one, uses the
// IHandleEntity itself and never asks
// this interface, so a run that traces nothing but static props and partition
// enumerations records nothing here and proves nothing about the ray that does
// come this way.
//
// WHAT ANSWERING MEANS WHILE THE PORT IS HALF DONE. Two kinds of object can be
// in the client's spatial partition today: a SourceSharp shell (in the slot
// table) and a stock client entity (in CClientEntityList and not in the slot
// table). The engine can trace against either. So every resolver here tries the
// table FIRST — that is the gate's "answer from the table" — and falls back to
// the port seam in cliententitylist.h for the second kind.
//
// THE FALLBACK USED TO BE COUNTED, per method, and cs_cl_slot_table printed the
// counts: the reading that would settle whether the seam could go. Phase 1c
// deleted the counters, because the seam had already been bound and the
// question is already answered -- SourceSharp_LegacyListUnknownFromIndex and
// ...FromHandle are `return NULL` at the foot of this file, so the arm that did
// the counting is unreachable and every counter was pinned at zero. A counter
// that cannot move is not a measurement, and a reader taking a pinned zero for
// "the seam was never asked" is worse than no counter at all.
//
//=============================================================================
// WHAT THE OTHER EIGHT MAY ANSWER, FROM 0e'S MEASUREMENTS
//=============================================================================
//
// docs/engine-boundary.md §5, one live load, counts split by calling module:
//
//   GetClientNetworkable            818,199 calls, 673,905 from engine.so.
//                                   The engine tolerates a NULL answer.
//                                   Busiest method on either interface -- hence
//                                   the note on cost at ClientUnknownFromIndex below.
//   GetClientEntity                 62,989, of which 738 answered NULL, and
//                                   the engine tolerated every NULL. Where the
//                                   trace takes it, it lands in m_pEnt, which
//                                   is a store and not a dereference.
//   GetClientNetworkableFromHandle  no engine caller; 158 calls, all from
//   GetClientEntityFromHandle       client.so. §8: "Phase 5a chooses their
//                                   contract rather than inheriting it."
//   NumberOfEntities                no engine caller, zero live calls in
//                                   three runs and four stages.
//   GetHighestEntityIndex           1,184 calls, ALL from engine.so, used as a
//                                   loop bound over the engine's own tables.
//                                   A number that is too small truncates that
//                                   walk, so this one is maintained rather than
//                                   guessed — see m_nHighestUsed.
//   SetMaxEntities                  once per level load.
//   GetMaxEntities                  zero live calls, but the engine's entity
//                                   debug listings, cl_showents (FCVAR_CHEAT)
//                                   and the cl_entityreport overlay, use it as
//                                   a loop bound. It must be a correct count
//                                   and it has no hot path.
//
// THE 0e PROBE ARMS ARE ON THIS OBJECT NOW, moved here from cliententitylist.cpp
// rather than deleted. Six rows of that document still read "unknown" and name
// the live experiment that would settle them, and the counters are the
// instrument those experiments use; leaving them on a class the engine no
// longer calls would have made every row read zero. The report command keeps
// its name (ss_cl_boundary_probe_report), so the document's recipe still works
// unchanged.
//
//=============================================================================
// WHY THERE IS A SECOND ALLOWLIST ENTRY
//=============================================================================
//
// tools/entity-gates-allowlist had exactly one line, and its header said the
// definition of done allows exactly one. That was true while only the server
// had a slot table. §3.3 puts one table on EACH side; the resolver is per side
// because the shell type is per side, so the client's is a second permitted
// downcast, and the honest thing is a second entry rather than a shared
// resolver that would have to cast to a type neither side can name.
//
// It is excusable on exactly the same terms as the server's, and those terms
// are a test, not a promise: CSourceSharpSlotTable::Resolve compares the serial
// BEFORE this file ever sees a pointer, and client_slot_table_test.cpp's
// SerialReuse cases go red if that comparison is removed. If those tests are
// weakened the entry stops being excusable.
//
//===========================================================================//

#include "cbase.h"

#include <typeinfo>

#include "icliententitylist.h"
#include "iclientunknown.h"
#include "icliententity.h"
#include "iclientnetworkable.h"

// For ClientClass -- the seam census below reads a class's m_pNetworkName at
// print time, having compared the class by POINTER on the hot path. The MEMBER
// and not GetName(), which is not const and so cannot be called on the const
// row the census holds; making the row non-const to reach an accessor that
// returns the member would be the wrong way round.
#include "client_class.h"

// The port seam, and the ONLY thing this file takes from the stock list. See
// the block at the bottom of that header for what the six functions are and why
// they are free functions.
#include "cliententitylist.h"

#include "slot_table.h"
#include "client_entity_counts.h"
#include "shell_cliententitylist.h"
#include "shell_standalone_client.h"

#include "handle_cross.h"          // SourceSharp_ClientShellFromTraceHit
#include "engine/IStaticPropMgr.h"

// plan_entity.md Phase 0 step 0e -- the engine-boundary probe, carried forward
// from cliententitylist.cpp onto the object the engine now calls.
#include "shell_boundary_probe.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//=============================================================================
// THE TABLE
//=============================================================================

//-----------------------------------------------------------------------------
// The client's slot table. One per side.
//
// A FUNCTION-LOCAL STATIC INSIDE ITS ACCESSOR, which is the shape plan.md §1a
// leaves for "one object per side": no file-scope object, no static-init order
// to be wrong about, and the lifetime stated by the code that hands it out.
// shell_entityslots.cpp:467 is the server's and says the same thing at length.
//
// NO LEVEL-SHUTDOWN RESET, and that is deliberate rather than missing. The
// server calls SourceSharp_ResetEntitySlots from its game system; over here
// every binding is removed by the shell's own destructor, and the client's
// level shutdown destroys every shell (CClientEntityList::Release calls
// Release() on each, which deletes it). So the table drains through the same
// path a single entity's death takes, and a Reset() would only ever paper over
// a shell that had leaked -- which cs_cl_entitylist is there to SHOW, by
// reading `bound` non-zero between maps, rather than to hide.
//-----------------------------------------------------------------------------
CSourceSharpSlotTable &SourceSharp_ClientSlotTable( void )
{
	static CSourceSharpSlotTable s_SlotTable;
	return s_SlotTable;
}

//-----------------------------------------------------------------------------
// BIND AT THE SERVER'S HANDLE. There is no other bind arm on this side; see
// slot_table.h's client block for why a Bind() that chose its own serial would
// be a bug rather than a convenience.
//-----------------------------------------------------------------------------
bool SourceSharp_ClientSlotBindAt( const CBaseHandle &handle, C_StandaloneEntity *pShell )
{
	return SourceSharp_ClientSlotTable().BindAt( handle, pShell );
}

void SourceSharp_ClientSlotUnbind( int nEntry )
{
	SourceSharp_ClientSlotTable().Unbind( nEntry );
}

//-----------------------------------------------------------------------------
// THE CLIENT'S RESOLVER, AND ITS ONE DOWNCAST.
//
// The second entry in tools/entity-gates-allowlist, by file and line. Sound for
// the reason the server's is: SourceSharp_ClientSlotBindAt above is the only
// route into this table and it takes a C_StandaloneEntity *, so static_cast
// recovers the type that was erased rather than a type somebody hoped for.
//
// The serial comparison is inside CSourceSharpSlotTable::Resolve, not here, so
// that there is no arrangement of this line that can cast a stale pointer: by
// the time this function has anything to cast, the answer is already known to
// be the live occupant or NULL.
//-----------------------------------------------------------------------------
C_StandaloneEntity *SourceSharp_ClientShellFromHandle( const CBaseHandle &handle )
{
	return static_cast< C_StandaloneEntity * >( SourceSharp_ClientSlotTable().Resolve( handle ) );
}

//-----------------------------------------------------------------------------
// By entry index -- the engine's route, which carries no serial.
//
// Through the handle resolver rather than casting again, for the reason the
// server's twin gives: the allowlist excuses SITES, and a second static_cast
// here would be a third entry excusing nothing new. HandleOf answers the live
// handle for a bound entry and an invalid one otherwise, so the serial compare
// is satisfied trivially and the answer is identical.
//-----------------------------------------------------------------------------
C_StandaloneEntity *SourceSharp_ClientShellFromIndex( int nEntry )
{
	return SourceSharp_ClientShellFromHandle( SourceSharp_ClientSlotTable().HandleOf( nEntry ) );
}

//-----------------------------------------------------------------------------
// IHandleEntity to shell, with NO CAST: GetRefEHandle() is a virtual on the
// engine interface, so the object answers with its own handle whatever it
// really is, and an IHandleEntity that is not one of ours names an entry the
// table does not hold or holds for somebody else.
//-----------------------------------------------------------------------------
C_StandaloneEntity *SourceSharp_ClientShellFromHandleEntity( IHandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return NULL;

	return SourceSharp_ClientShellFromHandle( pHandleEntity->GetRefEHandle() );
}

//-----------------------------------------------------------------------------
// What a trace hit, as a shell: handle_cross.h's crossing over this table.
// The static-prop question is util_shared.h GameEntityFromEntityHandle's own
// first ask, kept so a prop's handle never reaches the table.
//-----------------------------------------------------------------------------
C_StandaloneEntity *SourceSharp_ClientShellFromTraceHit( IHandleEntity *pHandleEntity )
{
	return SourceSharp_CrossHandleToClientShell< C_StandaloneEntity >( pHandleEntity,
		[]( IHandleEntity *pHit ) { return SourceSharp_ClientShellFromHandleEntity( pHit ); },
		[]( IHandleEntity *pHit ) { return staticpropmgr && staticpropmgr->IsStaticProp( pHit ); } );
}

//=============================================================================
// THE NINE METHODS
//=============================================================================

//-----------------------------------------------------------------------------
// THE TWO PRIMITIVES EVERY METHOD BELOW IS BUILT OUT OF.
//
// Table first, seam second, in that order and never the other way round: the
// table is the port's answer and the seam is what is left of the thing it
// replaces, so a reader of a call site can tell which world an answer came from
// by reading two lines rather than by reading the counters.
//
// The extra cost against stock, recorded per plan_entity.md's "costs are
// recorded NOW" rule: stock's GetClientNetworkable was a single array read out
// of m_EntityCacheInfo, cached precisely because 0e measured 818,199 calls in
// forty seconds. This is an array read (ResolveByIndex), a serial compare, and
// ONE VIRTUAL CALL (GetClientNetworkable() on the shell, which returns `this`
// adjusted by the base offset). The virtual is not removable by hand -- the
// adjustment is the compiler's -- and at 818k/40s it is ~20k virtual calls per
// second. Listed here for the performance pass, not defended.
//-----------------------------------------------------------------------------
static IClientUnknown *ClientUnknownFromIndex( int nEntry )
{
	C_StandaloneEntity *pShell = SourceSharp_ClientShellFromIndex( nEntry );

	if ( pShell )
		return pShell->GetIClientUnknown();

	// THE WORLD, WHICH IS NOT IN THE TABLE AND NEVER WILL BE.
	// shell_cliententitylist.h says why it is its own arm; the measurement that
	// put it here is that 2.17M of the seam's 5.07M answers over a two-pass
	// live gate were this one object, asked by index.
	if ( IClientUnknown *pWorld = SourceSharp_ClientWorldFromIndex( nEntry ) )
		return pWorld;

	return SourceSharp_LegacyListUnknownFromIndex( nEntry );
}

static IClientUnknown *ClientUnknownFromHandle( const CBaseHandle &hEnt )
{
	C_StandaloneEntity *pShell = SourceSharp_ClientShellFromHandle( hEnt );

	if ( pShell )
		return pShell->GetIClientUnknown();

	// The same arm, asked of a handle: the serial is compared inside it, so a
	// handle to the PREVIOUS map's world does not resolve to this one's.
	if ( IClientUnknown *pWorld = SourceSharp_ClientWorldFromHandle( hEnt ) )
		return pWorld;

	return SourceSharp_LegacyListUnknownFromHandle( hEnt );
}

//-----------------------------------------------------------------------------
// THE CLIENT'S EntityIsValid: whether this side's list holds the entity a
// handle names, serial and all.
//
// Stock's meaning on this side is CHandle::Get() != NULL, which is
// ClientEntityList().LookupEntity(): the entry's occupant, and only if its
// serial matches the handle's. ClientUnknownFromHandle above is that lookup
// over this port's arms -- the slot table compares the serial inside Resolve,
// the world arm compares it too -- so a handle to an entity that has gone, or
// to the last occupant of a reused entry, answers 0. The server's twin,
// SourceSharp_EntityIsValid, asks its own table the same question.
//
// It was SERVER-ONLY, so on the client every ask answered false, and the one
// client caller is a restore: SavedHandles re-checks each handle cs_cl_reload
// carries across, so every restored handle field came back None (lane
// side-audit's walk, 2026-10-02).
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientEntityIsValid( uint32_t hEntity )
{
	// The invalid handle names nothing, and splitting it would hand Init a
	// serial out of range.
	if ( hEntity == INVALID_EHANDLE_INDEX )
		return 0;

	const CBaseHandle handle( (int)( hEntity & ENT_ENTRY_MASK ),
	                          (int)( hEntity >> NUM_SERIAL_NUM_SHIFT_BITS ) );

	return ClientUnknownFromHandle( handle ) ? 1 : 0;
}

//-----------------------------------------------------------------------------
// The engine's IClientEntityList.
//
// Nine methods, in icliententitylist.h's declaration order because that order
// IS the vtable the engine holds -- 0e verified it against
// `vtable for CClientEntityList` at 0x8e8760 in the shipped client.so, and
// reordering these declarations would silently re-point every engine call.
//
// IT HOLDS NO ENTITY AND CANNOT BE WALKED. Its only state is three counters and
// a maximum; every answer comes from the slot table, which slot_table.h keeps
// un-iterable by construction. clause 2 of plan_entity.md's definition of done
// is that no native entity list survives, and "the class the engine calls
// IClientEntityList" is exactly where one would come back.
//-----------------------------------------------------------------------------
class CSourceSharpClientEntityList : public IClientEntityList
{
public:
	CSourceSharpClientEntityList( void )
		: m_Counts( SourceSharp_ClientSlotTable() ), m_bWarnedUnresolved( false )
	{
	}

	//-------------------------------------------------------------------------
	// +0x00. The busiest method on either interface; the engine tolerates a
	// NULL answer.
	//-------------------------------------------------------------------------
	virtual IClientNetworkable *GetClientNetworkable( int entnum ) OVERRIDE
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_ClientList_GetClientNetworkable );

		IClientUnknown *pUnknown = ClientUnknownFromIndex( entnum );
		return pUnknown ? pUnknown->GetClientNetworkable() : NULL;
	}

	//-------------------------------------------------------------------------
	// +0x08. No engine caller at all (0e: every one of the 158 live callers
	// was client.so). NULL for an unresolved handle is
	// therefore this port's choice and not an inherited constraint -- and it is
	// the same answer stock gave, so nothing in the client DLL changes shape.
	//-------------------------------------------------------------------------
	virtual IClientNetworkable *GetClientNetworkableFromHandle( CBaseHandle hEnt ) OVERRIDE
	{
		IClientUnknown *pUnknown = ClientUnknownFromHandle( hEnt );

		IClientNetworkable *pResult = pUnknown ? pUnknown->GetClientNetworkable() : NULL;
		SS_BOUNDARY_HIT_PTR( kBoundaryProbe_ClientList_GetClientNetworkableFromHandle, pResult );
		return pResult;
	}

	//-------------------------------------------------------------------------
	// +0x10. THE ONE THAT MAY NOT ANSWER NULL. See the file header: the engine
	// dereferences this for a virtual call with no test, in its own trace.
	//
	// Both arms are tried, and a miss is reported ONCE PER SESSION rather than
	// silently: if this ever answers NULL to the engine the next thing that
	// happens is a null dereference inside the engine's trace, and a console line
	// naming the handle is the difference between a diagnosable crash and a
	// stack in a module we have no symbols for. It is rate-limited to one
	// because the caller is a trace -- a per-frame path that would otherwise
	// fill the console faster than it could be read.
	//-------------------------------------------------------------------------
	virtual IClientUnknown *GetClientUnknownFromHandle( CBaseHandle hEnt ) OVERRIDE
	{
		IClientUnknown *pResult = ClientUnknownFromHandle( hEnt );

		if ( !pResult && hEnt.IsValid() && !m_bWarnedUnresolved )
		{
			m_bWarnedUnresolved = true;
			Warning( "[SourceSharp] IClientEntityList::GetClientUnknownFromHandle: "
			         "entry %d serial %d resolves to NOTHING. The engine's trace "
			         "dereferences this without a test -- if "
			         "the caller was engine.so the next thing that happens is a "
			         "crash inside the engine. Said once per session.\n",
			         hEnt.GetEntryIndex(), hEnt.GetSerialNumber() );
		}

		SS_BOUNDARY_HIT_PTR( kBoundaryProbe_ClientList_GetClientUnknownFromHandle, pResult );
		return pResult;
	}

	//-------------------------------------------------------------------------
	// +0x18. The engine tolerates a NULL answer; where the client trace takes
	// it, it lands in m_pEnt, which is a store and not a dereference.
	//-------------------------------------------------------------------------
	virtual IClientEntity *GetClientEntity( int entnum ) OVERRIDE
	{
		IClientUnknown *pUnknown = ClientUnknownFromIndex( entnum );

		// One return, so the probe's null/non-null split counts every answer --
		// 0e's shape, kept because §5.1's open question (which caller the 738
		// NULLs went to) is still open.
		IClientEntity *pResult = pUnknown ? pUnknown->GetIClientEntity() : NULL;
		SS_BOUNDARY_HIT_PTR( kBoundaryProbe_ClientList_GetClientEntity, pResult );
		return pResult;
	}

	//-------------------------------------------------------------------------
	// +0x20. No engine caller; one live caller, in client.so.
	//-------------------------------------------------------------------------
	virtual IClientEntity *GetClientEntityFromHandle( CBaseHandle hEnt ) OVERRIDE
	{
		IClientUnknown *pUnknown = ClientUnknownFromHandle( hEnt );

		IClientEntity *pResult = pUnknown ? pUnknown->GetIClientEntity() : NULL;
		SS_BOUNDARY_HIT_PTR( kBoundaryProbe_ClientList_GetClientEntityFromHandle, pResult );
		return pResult;
	}

	//-------------------------------------------------------------------------
	// +0x28. HOW MANY, NEVER WHICH.
	//
	// Maintained by Bind and Unbind below, which is §3.3's "the counts are
	// maintained by bind and unbind" -- not derived by counting occupied
	// entries, because deriving it would need a walk and the walk is the thing
	// clause 2 forbids.
	//
	// The larger of the two worlds' answers. They OVERLAP rather than partition
	// -- while the seam exists every shell is in both -- so adding them would
	// double-count, and while stock client entities exist the stock number is a
	// superset of this one. When Phase 7 empties the stock list its answer goes
	// to zero and the table's stands alone, with no edit here.
	//-------------------------------------------------------------------------
	virtual int NumberOfEntities( bool bIncludeNonNetworkable ) OVERRIDE
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_ClientList_NumberOfEntities );

		// THE WORLD IS ONE ENTITY AND IT IS NOT IN THE TABLE, so the count
		// the table maintains is one short of the truth for the whole of every
		// map. It is a networked entry (entry 0), so it counts under both
		// arguments -- there is no arm of this question it is absent from.
		const int nSlot = SourceSharp_ClientCountWithWorld(
			SourceSharp_ClientWorldHandle(),
			m_Counts.NumberOfEntities( bIncludeNonNetworkable ) );

		return nSlot;
	}

	//-------------------------------------------------------------------------
	// +0x30. 1,184 calls, ALL of them from engine.so, used as a LOOP BOUND over
	// the engine's own tables -- so an answer that is too small truncates the
	// engine's walk and entities stop being drawn, with nothing reporting it.
	//
	// slot_table.h deliberately does NOT carry this: it is the one scalar that
	// would turn ResolveByIndex into a walk for every shim TU that can reach
	// the table. It lives here, on the interface that needs it, maintained by
	// this class's own Bind and Unbind.
	//-------------------------------------------------------------------------
	virtual int GetHighestEntityIndex( void ) OVERRIDE
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_ClientList_GetHighestEntityIndex );

		// AND THE WORLD RAISES IT, from -1 to 0, on a map where nothing else is
		// bound yet. This answer is a LOOP BOUND for the engine's own walk
		// (1,184 calls, all from engine.so), so an answer that is too small
		// truncates it and entities stop being drawn with nothing reporting it
		// -- and the world is exactly the entry that would be left out, because
		// CClientEntityCounts only knows what the table holds.
		const int nHighest =
			SourceSharp_ClientHighestWithWorld( SourceSharp_ClientWorldHandle(),
			                                    m_Counts.HighestEntityIndex() );

		return nHighest;
	}

	//-------------------------------------------------------------------------
	// +0x38 and +0x40. Stored and handed back, which is all stock did.
	// SetMaxEntities is called once per level load; GetMaxEntities is reached
	// only by cl_showents (FCVAR_CHEAT) and the cl_entityreport overlay, both
	// as loop bounds -- a correct count
	// with no hot path.
	//-------------------------------------------------------------------------
	virtual void SetMaxEntities( int maxents ) OVERRIDE
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_ClientList_SetMaxEntities );

		m_Counts.SetMaxEntities( maxents );
	}

	virtual int GetMaxEntities( void ) OVERRIDE
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_ClientList_GetMaxEntities );

		return m_Counts.MaxEntities();
	}

	//-------------------------------------------------------------------------
	// THE COUNTS, in client_entity_counts.h rather than inline here.
	//
	// That header is ENGINE-FREE, so client_slot_table_test.cpp drives the real
	// class this object delegates to rather than a stand-in written to look like
	// it. Three of the four scalars have an edge worth a test -- see the header
	// -- and none of them is checkable inside a class that needs cbase.h.
	//
	// OnBound is called only after the table has ACCEPTED a bind, and OnUnbound
	// only after it has released one: both orders are load-bearing and both are
	// stated at their call sites in SourceSharp_ClientEntityBind/Unbind.
	//-------------------------------------------------------------------------
	void OnBound( int nEntry )   { m_Counts.OnBound( nEntry ); }
	void OnUnbound( int nEntry ) { m_Counts.OnUnbound( nEntry ); }

private:
	CClientEntityCounts m_Counts;
	bool                m_bWarnedUnresolved;
};

//-----------------------------------------------------------------------------
// The one instance, in the accessor that hands it out -- the same shape as the
// table above and for the same reason.
//-----------------------------------------------------------------------------
static CSourceSharpClientEntityList &SourceSharpClientEntityList( void )
{
	static CSourceSharpClientEntityList s_List;
	return s_List;
}

//-----------------------------------------------------------------------------
// VClientEntityList003, registered for the engine.
//
// NOT EXPOSE_SINGLE_INTERFACE_GLOBALVAR, because that macro needs a file-scope
// object and plan.md §1a says there are none. InterfaceReg is the engine's
// registration mechanism and is unavoidable; what it registers here is a
// factory that hands out the function-local static above, so the object is
// constructed on the first CreateInterface rather than during static init.
//
// cliententitylist.cpp's registration of the same version string was DELETED
// when this one landed, not left beside it. Two InterfaceRegs for one name
// resolve by link order, and the two answers are "a working game" and "a client
// that sees no entities at all".
//-----------------------------------------------------------------------------
static void *CreateSourceSharpClientEntityList( void )
{
	return static_cast< IClientEntityList * >( &SourceSharpClientEntityList() );
}

static InterfaceReg g_CreateSourceSharpClientEntityList(
	CreateSourceSharpClientEntityList, VCLIENTENTITYLIST_INTERFACE_VERSION );

//=============================================================================
// WHAT THE SHIM ASKS -- shell_cliententitylist.h's declarations
//=============================================================================

IClientEntity *SourceSharp_ClientEntityFromIndex( int nEntry )
{
	IClientUnknown *pUnknown = ClientUnknownFromIndex( nEntry );
	return pUnknown ? pUnknown->GetIClientEntity() : NULL;
}

IClientUnknown *SourceSharp_ClientUnknownFromHandle( const CBaseHandle &hEnt )
{
	return ClientUnknownFromHandle( hEnt );
}

//-----------------------------------------------------------------------------
// THE UNION RESOLVE, THE CLIENT'S HALF -- the twin of shell_entityslots.cpp's
// SourceSharp_EntityFromHandle, same name for the same contract in the other
// DLL. Lane `f-list`.
//
// EVERY EHANDLE ON THIS SIDE COMES THROUGH HERE. game/shared/ehandle.h's
// CBaseHandle::Get() is what every handle resolve on either DLL goes through,
// and the client arm of it used to be the STOCK LIST alone. The three arms
// below are a SUPERSET of that: the slot table, the world at entry 0 -- which
// the stock list has never held -- and the stock list itself.
//
// IT IS A WIDENING AND NOT A MOVE, which is why it is safe while the seam is
// still here. The third arm is the one that leaves with the client flip.
//-----------------------------------------------------------------------------
IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle )
{
	if ( !handle.IsValid() )
		return NULL;

	return ClientUnknownFromHandle( handle );
}

IClientNetworkable *SourceSharp_ClientNetworkableFromHandle( const CBaseHandle &hEnt )
{
	IClientUnknown *pUnknown = ClientUnknownFromHandle( hEnt );
	return pUnknown ? pUnknown->GetClientNetworkable() : NULL;
}

IClientEntity *SourceSharp_ClientEntityFromHandle( const CBaseHandle &hEnt )
{
	IClientUnknown *pUnknown = ClientUnknownFromHandle( hEnt );
	return pUnknown ? pUnknown->GetIClientEntity() : NULL;
}

//-----------------------------------------------------------------------------
// The renderable, by handle -- step p7-exit.
//
// Built out of the SAME ClientUnknownFromHandle primitive as the two above, so
// there is one place that knows there are two sets of client entities and it is
// still that function. Stock's CClientEntityList::GetClientRenderableFromHandle
// is `GetClientUnknownFromHandle` followed by the matching virtual
// (cliententitylist.h), which is exactly this.
//-----------------------------------------------------------------------------
IClientRenderable *SourceSharp_ClientRenderableFromHandle( const CBaseHandle &hEnt )
{
	IClientUnknown *pUnknown = ClientUnknownFromHandle( hEnt );
	return pUnknown ? pUnknown->GetClientRenderable() : NULL;
}

//-----------------------------------------------------------------------------
// Index to handle. Stock asked the OBJECT for its handle rather than the table
// for its serial (EntIndexToHandle: `pUnk ? pUnk->GetRefEHandle() : INVALID`),
// and this keeps that: HandleOf answers from the table for a shell, and the
// seam's object answers for a stock entity, so both kinds get the handle their
// own occupant really holds.
//-----------------------------------------------------------------------------
CBaseHandle SourceSharp_ClientHandleFromIndex( int nEntry )
{
	const CBaseHandle hSlot = SourceSharp_ClientSlotTable().HandleOf( nEntry );

	if ( hSlot.IsValid() )
		return hSlot;

	// The world's own handle, from the world, for the same reason the two
	// resolvers ask it: entry 0 is not in the table and the stock list is the
	// only other thing that has ever known where it is.
	const CBaseHandle hWorld = SourceSharp_ClientWorldHandle();

	if ( SourceSharp_ClientWorldAnswersForIndex( hWorld, nEntry ) )
		return hWorld;

	IClientUnknown *pLegacy = SourceSharp_LegacyListUnknownFromIndex( nEntry );

	return pLegacy ? pLegacy->GetRefEHandle() : CBaseHandle();
}

//=============================================================================
// BIND AND UNBIND
//=============================================================================

//-----------------------------------------------------------------------------
// A shell enters the world.
//
// ORDER IS LOAD-BEARING. The stock list goes first because AddNetworkableEntity
// calls SetRefEHandle on the shell -- that is how m_RefEHandle comes to hold the
// handle -- and it is the handle the table then adopts. Binding the table from
// the arguments first and registering afterwards would work today and would
// stop working the day the stock half goes, because the assignment of
// m_RefEHandle would go with it. So the handle this function binds is read back
// from the shell, and the seam's removal is a deletion of two lines rather than
// a rearrangement.
//
// A REFUSED BIND IS SAID OUT LOUD. slot_table.h refuses an entry that is
// already bound rather than overwriting it (stock asserts the same thing, and
// an assert compiles out), so a refusal means two live shells believe they own
// one entry -- and the SECOND one is the one that will resolve to nothing, in a
// build where every resolve answering NULL is a silent no-op. There is no
// recovery to attempt here; there is a fact to report.
//-----------------------------------------------------------------------------
void SourceSharp_ClientEntityBind( C_StandaloneEntity *pShell, int nEntNum, int nSerialNum )
{
	if ( !pShell )
		return;

	// Into the client's stock entity list, which is what makes the engine
	// finish the job. Creating the object through the ClientClass is only half
	// of it: the engine looks the entity up to unpack into it, so without this
	// it is constructed and then never given any of the server's data. The
	// symptom of missing it is that PostDataUpdate never runs -- the object
	// exists, holds nothing, and nothing says why.
	const CBaseHandle hEnt =
		SourceSharp_LegacyListBind( pShell->GetIClientUnknown(), nEntNum, nSerialNum );

	if ( !SourceSharp_ClientSlotBindAt( hEnt, pShell ) )
	{
		Warning( "[SourceSharp] client slot table REFUSED the bind for entity %d "
		         "(serial %d): the entry is already bound, outside the table, or the "
		         "handle is invalid. This entity will resolve to nothing.\n",
		         nEntNum, nSerialNum );
		return;
	}

	SourceSharpClientEntityList().OnBound( hEnt.GetEntryIndex() );
}

//-----------------------------------------------------------------------------
// A shell leaves.
//
// BY THE SHELL'S OWN HANDLE, read before either half is torn down. Unbind takes
// an ENTRY and not a handle on purpose (slot_table.h says why), and the entry
// is only nameable while the shell still holds its handle.
//
// The table is unbound BEFORE the stock list, the mirror image of Bind: the
// stock list's RemoveEntity calls SetRefEHandle( INVALID_EHANDLE ) on the way
// through, so after it the entry is no longer nameable from the shell either.
//-----------------------------------------------------------------------------
void SourceSharp_ClientEntityUnbind( C_StandaloneEntity *pShell )
{
	if ( !pShell )
		return;

	const CBaseHandle hEnt = pShell->GetRefEHandle();

	if ( hEnt.IsValid() )
	{
		const int nEntry = hEnt.GetEntryIndex();

		// Only if THIS shell is what is bound there. An entity whose bind was
		// refused above never owned the entry, and unbinding on the way out
		// would take the binding away from the shell that did.
		if ( SourceSharp_ClientShellFromHandle( hEnt ) == pShell )
		{
			SourceSharp_ClientSlotUnbind( nEntry );
			SourceSharpClientEntityList().OnUnbound( nEntry );
		}
	}

	SourceSharp_LegacyListUnbind( hEnt );
}

//=============================================================================
// THE READOUTS
//=============================================================================

//-----------------------------------------------------------------------------
// What the client's slot table is holding.
//
// A DELTA IS THE MEASUREMENT. `bound` should return to zero between maps -- a
// non-zero reading at the main menu is a leaked shell, which is the thing the
// absence of a Reset() makes visible rather than hides. `refused` moving is a
// bind that did not happen.
//
// IT USED TO PRINT THE PORT SEAM'S ANSWERS TOO -- four fallback counters and a
// per-class census of what the stock list answered for. Phase 1c deleted them:
// the arms that fed them are inside `if ( pLegacy )` and `if ( nLegacy > ... )`
// on a seam whose resolvers are `return NULL` and whose counts are `return -1`
// and `return 0`, so every one of the five was pinned at its initial value and
// no live run could move it. The finding they were built to produce is already
// taken and written down -- 2,270,361 by index and 2,876,089 by handle, before
// the seam was bound -- and a counter that cannot move is not evidence of
// anything.
//
// It prints no list. There is nothing here that could produce one; slot_table.h
// carries the five must-not-compile legs that keep it that way.
//-----------------------------------------------------------------------------
CON_COMMAND( cs_cl_slot_table, "SourceSharp: the client entity slot table (plan_entity.md 3.3)" )
{
	const CSourceSharpSlotTable &table = SourceSharp_ClientSlotTable();

	Msg( "[SourceSharp] client slot table: %d entries (%d edict, %d engine-invisible)\n",
	     CSourceSharpSlotTable::EntryCount(), MAX_EDICTS,
	     CSourceSharpSlotTable::EntryCount() - MAX_EDICTS );
	Msg( "  bound %d  binds %d  unbinds %d  refused %d\n",
	     table.BoundCount(), table.Binds(), table.Unbinds(), table.Refused() );
	Msg( "  highest networked entry %d  max entities %d\n",
	     SourceSharpClientEntityList().GetHighestEntityIndex(),
	     SourceSharpClientEntityList().GetMaxEntities() );
}

//-----------------------------------------------------------------------------
// THE CLIENT'S PROBE READOUT -- plan_entity.md Phase 0 step 0e, moved here from
// cliententitylist.cpp with the arms it prints.
//
// A SECOND COMMAND, not a second half of the server's. server.so and client.so
// are separate shared objects: the probe header's function-local instance is
// one per DLL, so the server's report cannot see these counters and this one
// cannot see the server's. Printing "(client)" in the header line is what keeps
// a reader from taking one report for both.
//
// The whole table is printed on both sides, server rows included. A row that
// reads calls=0 here means "this DLL was never asked", which is a fact, not a
// gap -- and hiding the rows would leave a reader unable to tell the two apart.
//-----------------------------------------------------------------------------
CON_COMMAND( ss_cl_boundary_probe_report,
	"ss_cl_boundary_probe_report -- engine-boundary call counts inside client.so" )
{
	Msg( "[SourceSharp] engine-boundary probe (client): map '%s'\n",
	     engine->GetLevelName() ? engine->GetLevelName() : "(none)" );

	for ( int id = 0; id < kBoundaryProbeCount; id++ )
	{
		char szLine[ 2048 ];
		BoundaryProbes().FormatLine( id, szLine, sizeof( szLine ) );
		Msg( "  %s\n", szLine );
	}
}

CON_COMMAND( ss_cl_boundary_probe_reset,
	"ss_cl_boundary_probe_reset -- zero client.so's engine-boundary counters" )
{
	BoundaryProbes().Reset();
	Msg( "[SourceSharp] engine-boundary probe (client): counters zeroed\n" );
}


//=============================================================================
// THE PORT SEAM -- THE PORT'S OWN CODE, OUT OF THE STOCK FILE THE FLIP DELETES
//=============================================================================
//
// ELEVEN DEFINITIONS, MOVED HERE VERBATIM from game/client/cliententitylist.cpp
// by lane `c8-listseam`. shell_cliententitylist.h carries the paragraph that
// says why, and names lane 9 (`c8-flip`) as this block's removal owner: it goes
// in the same commit that takes cliententitylist.cpp out of client_csharp.vpc.
//
// NOTHING HERE DECIDES ANYTHING. Each of the six is the one call the shim used
// to make, written where the stock class is still visible; the four counters
// and the recorder are a debug instrument c_baseentity.h's SDKEntityFromUnknown
// drives and cs_cl_handle_cross_check prints.
//
// IT IS AT THE FOOT OF THE FILE, after every method that answers out of the
// slot table, so that a reader meets the port's answer before it meets what is
// left of the thing it replaces -- the same order the two primitives near the
// top ask their arms in, and for the same reason.
//=============================================================================

//-----------------------------------------------------------------------------
// ARMED, THERE IS NO STOCK CLIENT ENTITY LIST AT ALL, and these six are the
// only bodies in the port that name it. game/client/cliententitylist.cpp is a
// removal-set row, so `cl_entitylist`, CBaseEntityList::AddNetworkableEntity
// and CBaseEntityList::RemoveEntity are three of the ten symbols the armed
// client link is owed -- measured, not predicted:
//
//     ./ar_run all .ar/fv-b0-link.log
//     shell_cliententitylist.o owes cl_entitylist (x8, one "more ... follow"),
//     AddNetworkableEntity and RemoveEntity; shell_clientdll.o owes
//     CClientEntityList::Release().
//
// THE ARMED ARMS ARE NOT STUBS AND THE DIFFERENCE MATTERS. A stub would answer
// the same question wrongly; each of these answers a question whose subject has
// gone. "What is at index n in the stock list" has no answer when there is no
// stock list, and NULL is that answer -- every caller already carries the guard
// because a missing entity has always been possible. The two COUNTS are the
// same: -1 and 0 are "the stock list contributes nothing", which is true, and
// shell_cliententitylist.cpp's own IClientEntityList adds the slot table's and
// the world's contribution on top of them (SourceSharp_ClientHighestWithWorld,
// SourceSharp_ClientCountWithWorld).
//
// THE BIND IS THE ONE THAT IS NOT AN ABSENCE, and it is the reason this block
// is written rather than deleted. AddNetworkableEntity did TWO things: it put
// a row in the list, and it called SetRefEHandle on the way through -- which is
// how m_RefEHandle comes to hold the handle at all, for a shell AND for the
// client world (world_adapter_client.cpp:480 reads it back rather than
// assigning it, and says so). The row has no subject any more; the handle
// assignment does, and it is kept here so the single place that issues a client
// handle stays single.
//-----------------------------------------------------------------------------
CBaseHandle SourceSharp_LegacyListBind( IClientUnknown *pUnknown, int nEntNum, int nSerialNum )
{
	// The handle the stock list would have assigned, from the same two numbers
	// it was given, written onto the same object by the same virtual. The entry
	// and the serial are the SERVER's -- nothing here chooses either -- which is
	// the property the stock arm's comment above exists to protect.
	if ( !pUnknown )
		return CBaseHandle();

	const CBaseHandle hEnt( nEntNum, nSerialNum );

	pUnknown->SetRefEHandle( hEnt );

	return hEnt;
}

void SourceSharp_LegacyListUnbind( const CBaseHandle &hEnt )
{
	// NOT A NO-OP BY OVERSIGHT. RemoveEntity's other effect was
	// SetRefEHandle( INVALID_EHANDLE ) on the way out, and both callers already
	// do their own: ~C_StandaloneEntity is about to free the object, and
	// C_World::Release Term()s m_RefEHandle on the line after this call --
	// which world_adapter_client.cpp's banner calls the reload bug and which
	// therefore may not depend on this function at all.
	(void)hEnt;
}

IClientUnknown *SourceSharp_LegacyListUnknownFromIndex( int nEntry )
{
	(void)nEntry;
	return NULL;
}

IClientUnknown *SourceSharp_LegacyListUnknownFromHandle( const CBaseHandle &hEnt )
{
	(void)hEnt;
	return NULL;
}

