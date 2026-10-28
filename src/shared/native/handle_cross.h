//========= SourceSharp — crossing a handle to a typed entity, safely ========//
//
// THE RULE, IN ONE PLACE: an IHandleEntity looked up from the handle table may
// belong to EITHER hierarchy — the SDK's CBaseEntity tree or the parallel
// standalone one — and the only safe route to a typed pointer runs through two
// virtual calls:
//
//     handle table  ->  IHandleEntity *
//                       static_cast to I{Server,Client}Unknown   (structural:
//                            everything either hierarchy puts in the entity
//                            list is one)
//                       ->  GetBaseEntity()                      (the partition:
//                            the SDK's tree answers with itself, the parallel
//                            hierarchy answers NULL — shell_standalone.h:180
//                            and its client twin)
//                       ->  the typed virtual                    (MyCombatWeaponPointer
//                            and friends: NULL from anything that is not one)
//
// What this replaces is the (T*) cast in CHandle<T>::Get() — a reinterpret
// that assumes whatever the table returned is a T. For a standalone entity the
// address even LOOKS right, because both hierarchies put IHandleEntity at the
// front of the object; what is wrong is every vtable slot past the shared
// interface, so the pointer works until the first virtual call and then
// dispatches into a different class's function. shell_standalone_hierarchy.cpp
// scoped a real capability down to avoid exactly this, and Phase 13 step 1 of
// plan_baseentity.md is the general fix. Phase 10 needs it EARLY for one type:
// the weapon handles (m_hMyWeapons, m_hActiveWeapon, the viewmodel's
// m_hWeapon) are where a parallel-hierarchy entity first has to sit in a
// CBaseEntity-typed slot.
//
// TWO CROSSINGS LIVE HERE, and they differ only in where they stop.
// SourceSharp_SDKEntityFromUnknown walks the partition and answers the SDK's
// tree, which both sides have emptied, so it answers NULL. SourceSharp_
// CrossHandleToGameEntity walks the OTHER arm -- the entity face -- and is what
// the server's variant_t::Entity uses. The client, on stock headers since
// 2026-10-25, asks GetBaseEntity() exactly as the server does, so one
// partition serves both sides.
//
// Engine-free on purpose, like bone_cache_key.h: the rule is templates and two
// virtual calls, so it can be compiled by the native test binary against mock
// hierarchies where the failure is constructible — including one whose
// subobject addresses actually differ, so the cast and the crossing disagree
// and the test can fail.
//
//=============================================================================//
// =========================================================================
// THIS FILE IS NOT DEAD, AND plan_shim.md's PHASE 3 RULING TABLE SAYS IT IS.
//
// The row reads DELETE -- "dead since the no-base-entity merge, 327 lines plus
// 6 gtests", 1 live server consumer, 0 live client. Re-measured by lane 3f at
// the Phase 3 branch point (`tools/shim-header-census`, over the objects the
// ninja GRAPH names on each link line):
//
//     handle_cross.h in the include closure    102 server / 169 client TUs
//     SourceSharp_SDKEntityFromUnknown         103 server / 129 client
//     SourceSharp_CrossHandleToGameEntity      102 server /   0 client
//     SourceSharp_SDKEntityFromNetworkable       1 server /   0 client
//
// The ruling was taken with an instrument that asks which OBJECTS import a
// header's symbols. A header has no object and everything here is an inline
// template, so that question's honest answer is zero and it means nothing --
// the tool that was used says so itself and refuses. The consumers arrive
// transitively, through `entitylist.h` on the server and `c_baseentity.h` on
// the client, which is why the count looked like one file.
//
// handle_cross_test.cpp's HandleCrossConsumers section pins all of this in
// assertions, at the call sites, so the next reader of that table does not have
// to take this comment's word for it.
// =========================================================================
#pragma once

//-----------------------------------------------------------------------------
// "IS THIS OBJECT OUTSIDE THE SDK'S TREE?" -- one question, and only one side
// of the build can ask it.
//
// IServerUnknown::GetBaseEntity() on the server and IClientUnknown::
// GetBaseEntity() on the client: a standalone answers its entity face, the
// world and every client object answer NULL, and no SDK entity is left to
// answer anything. Everything in either handle table is an I*Unknown, and
// IHandleEntity -> I*Unknown is one single-inheritance step, so the cast below
// adjusts nothing and cannot be wrong.
//
// TPartition is IServerEntity on the server and IClientUnknown on the client.
//-----------------------------------------------------------------------------
template< class TPartition >
struct SourceSharp_Partition
{
	template< class TObject >
	static auto GameEntityOf( TObject *pObject )
		-> decltype( static_cast< TPartition * >( pObject )->GetBaseEntity() )
	{
		// Structural: the interfaces this is used with are one
		// single-inheritance chain, so the cast adjusts nothing. The address
		// change, if any, happens inside the object on the virtual call.
		return static_cast< TPartition * >( pObject )->GetBaseEntity();
	}

	template< class TObject >
	static bool IsOutsideTheSdk( TObject *pObject )
	{
		return GameEntityOf( pObject ) != 0;
	}
};

//-----------------------------------------------------------------------------
// THE PARTITION, ASKED THE WAY THAT SURVIVES STEP 5.
//
// Every "is this entity in the SDK's tree?" question in the game is spelled as
// `GetBaseEntity() == NULL` -- 48 server sites in 20 files and 38 client ones,
// enumerated in docs/typedef-dryrun/nullflip_verbose.txt. That spelling stops
// working on the day Phase 13 step 5 makes GetBaseEntity() answer with the
// entity's IGameEntity view instead of NULL: every one of those NULL checks
// starts PASSING, and the SDK arm behind it runs on a pointer whose vtable
// belongs to a different class. The dry run armed exactly that, with a canary
// on GetBaseEntity() -- `ss_step5_sim`, retired at plan_entity.md step 4g -- and
// the process died in seconds, three times, at three sites nobody had listed;
// one of which was not a cast at all, just an `if`.
//
// So the question is asked the OTHER way round here: GetGameEntity() != NULL.
// That arm of the partition is stable across step 5 -- a standalone entity
// answers with itself before and after, an SDK entity answers NULL before and
// after -- because it is the arm whose meaning step 5 does not change.
//
// WHY THIS IS A RESOLVER AND NOT 86 CALL-SITE EDITS. The dry run's finding was
// that the enumerated list was incomplete: three live crash sites were on
// nobody's list, and two more (ClientSetupVisibility, MarkEntitiesAsTouching)
// were found only by reading. A fix applied at the five functions that every
// one of those sites resolves through is a fix that cannot miss a site,
// including the sites nobody has found yet. Callers keep the NULL they have
// always got for an entity outside the SDK, and keep getting it after step 5.
//
// TPartition is IServerEntity on the server and IClientUnknown on the client:
// the same question on both sides. See sdk_player_cast_test.cpp, whose client
// ratchet became a ban when the client could first ask it.
//-----------------------------------------------------------------------------
template< class TPartition, class TUnknown >
inline auto SourceSharp_SDKEntityFromUnknown( TUnknown *pUnknown ) -> decltype( pUnknown->GetBaseEntity() )
{
	if ( !pUnknown )
		return 0;

	if ( SourceSharp_Partition< TPartition >::IsOutsideTheSdk( pUnknown ) )
		return 0;

	return pUnknown->GetBaseEntity();
}

//-----------------------------------------------------------------------------
// From the handle table's IHandleEntity to the game-entity boundary, or NULL.
//
// The SAME rule as above, one virtual shorter, and the one Phase 13 needs:
// IGameEntity is NOT in the IHandleEntity -> IServerUnknown -> IServerEntity
// single-inheritance chain. It is a separate base, so an entity's IGameEntity
// view and its IHandleEntity view are DIFFERENT ADDRESSES for the same object
// -- on CStandaloneEntity the interface is the fourth base, so the two differ
// by a measurable number of bytes. A (T*) cast from the handle table therefore
// does not merely risk a wrong vtable here: it lands on the wrong part of the
// object, and every call through it dispatches out of whatever happens to sit
// at that offset.
//
// GetBaseEntity() is the compiler-adjusted conversion, performed by the object
// itself, and it is also the partition: the world answers NULL and a
// standalone answers with itself. Both facts come from one virtual
// call, which is why there is nothing to cast.
//
// TServerEntity is IServerEntity. The server's form only: the client resolves
// a trace hit to its shell (SourceSharp_CrossHandleToClientShell, below).
//-----------------------------------------------------------------------------
template< class TGameEntity, class TServerEntity, class THandleEntity >
inline TGameEntity *SourceSharp_CrossHandleToGameEntity( THandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return 0;

	// Structural, and the only cast in the crossing: everything in the entity
	// list is an IServerEntity, and the three interfaces are one chain, so this
	// adjusts nothing and cannot be wrong. The address change happens on the
	// NEXT line, inside the object, where the compiler knows the layout.
	TServerEntity *pServerEntity = static_cast< TServerEntity * >( pHandleEntity );

	return pServerEntity->GetBaseEntity();
}


//-----------------------------------------------------------------------------
// The same partition, reached from the NETWORKABLE face instead.
//
// IServerNetworkable declares its own GetBaseEntity() ("Only used by game
// code") and CStandaloneEntity's single override answers both it and
// IServerUnknown's. So a pointer resolved through the networkable is exactly as
// exposed to step 5 as one resolved through the unknown -- UTIL_Remove,
// CreateEntityByName and the tools interface all take this route.
//
// It needs its own function because the cast in the one above WOULD BE WRONG
// here. IServerNetworkable and IServerEntity are SEPARATE BASES of
// CStandaloneEntity, not one chain, so static_cast between them is a cross-cast
// the compiler cannot perform and would silently misadjust if it could.
// GetEntityHandle() is the conversion the object performs for itself, and it
// lands on the IHandleEntity view -- which IS in the chain, so the cast after
// it adjusts nothing, exactly as in the crossings above.
//-----------------------------------------------------------------------------
template< class TPartition, class TNetworkable >
inline auto SourceSharp_SDKEntityFromNetworkable( TNetworkable *pNetworkable ) -> decltype( pNetworkable->GetBaseEntity() )
{
	if ( !pNetworkable )
		return 0;

	if ( SourceSharp_Partition< TPartition >::IsOutsideTheSdk( pNetworkable->GetEntityHandle() ) )
		return 0;

	return pNetworkable->GetBaseEntity();
}

//-----------------------------------------------------------------------------
// From a trace's IHandleEntity to the CLIENT SHELL, or NULL, asking no
// game-entity face (C_StandaloneEntity is no IGameEntity, 2026-10-18). A static
// prop is never looked up; the slot table answers the shell bound at the
// object's own handle; and that shell's IHandleEntity view must BE the object
// hit, so an entry held by something else answers NULL, not a stranger.
// Engine-free: handle_cross_test.cpp's ClientShellCross drives it with mocks.
//-----------------------------------------------------------------------------
template< class TShell, class THandleEntity, class TResolve, class TIsStaticProp >
inline TShell *SourceSharp_CrossHandleToClientShell( THandleEntity *pHandleEntity,
                                                     TResolve resolve,
                                                     TIsStaticProp isStaticProp )
{
	if ( !pHandleEntity )
		return 0;

	if ( isStaticProp( pHandleEntity ) )
		return 0;

	TShell *pShell = resolve( pHandleEntity );
	if ( !pShell )
		return 0;

	// An upcast, adjusted by the compiler with the whole layout in view.
	if ( static_cast< THandleEntity * >( pShell ) != pHandleEntity )
		return 0;

	return pShell;
}
