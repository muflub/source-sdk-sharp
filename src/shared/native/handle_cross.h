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
// THREE CROSSINGS LIVE HERE, and they differ only in where they stop.
// SourceSharp_CrossHandleToWeapon walks the partition and then a typed virtual,
// for the SDK's tree. SourceSharp_CrossHandleToGameEntity walks the OTHER arm
// of the same partition -- GetGameEntity() -- and is what CHandle<IGameEntity>
// uses, so that Phase 13's typedef never turns a handle into a cast.
// SourceSharp_CrossHandleToClientGameEntity is that same walk on the client,
// where the boundary is reached through GetIClientEntity() rather than a cast
// because the client's handle table holds things that are not entities.
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
//     SourceSharp_CrossHandleToClientGameEntity  0 server / 129 client
//     SourceSharp_SDKEntityFromNetworkable       1 server /   0 client
//     SourceSharp_CrossHandleToWeapon            0 server /   0 client
//
// The ruling was taken with an instrument that asks which OBJECTS import a
// header's symbols. A header has no object and everything here is an inline
// template, so that question's honest answer is zero and it means nothing --
// the tool that was used says so itself and refuses. The consumers arrive
// transitively, through `entitylist.h` on the server and `c_baseentity.h` on
// the client, which is why the count looked like one file.
//
// The last row is the one real absence and it is deliberate: the weapon
// crossing is the mechanism waiting for Phase 13's body for
// CHandle<CBaseCombatWeapon>::Get(), which both baseentity.h and
// c_baseentity.h declare and do not define for exactly that reason.
//
// handle_cross_test.cpp's HandleCrossConsumers section pins all of this in
// assertions, at the call sites, so the next reader of that table does not have
// to take this comment's word for it.
// =========================================================================
#ifndef SOURCESHARP_HANDLE_CROSS_H
#define SOURCESHARP_HANDLE_CROSS_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// "IS THIS OBJECT OUTSIDE THE SDK'S TREE?" -- one question, and only one side
// of the build can ask it.
//
// The server asks IServerUnknown::GetBaseEntity(), its one accessor since
// plan_server_native_v2 T1.3: a standalone answers its entity face, the world
// answers NULL, and no SDK entity is left to answer anything. (Before T1.3 the
// server also had the SDK's tree, whose GetBaseEntity() answered itself, so it
// asked the patch's GetGameEntity() instead; the client still does, below.)
//
// THE CLIENT ASKS THE SAME QUESTION, ONE VIRTUAL LATER (Phase 13's revised step
// 1, landed). IClientEntity::GetGameEntity() now exists and answers exactly as
// IServerEntity::GetGameEntity() does. What differs is how you REACH it, and
// the difference is forced rather than stylistic:
//
//   the server   everything in the entity list is an IServerEntity, and
//                IHandleEntity -> IServerUnknown -> IServerEntity is one
//                single-inheritance chain, so a static_cast adjusts nothing
//                and cannot be wrong.
//
//   the client   the handle table holds IClientUnknowns that are NOT entities:
//                CDefaultClientRenderable, CDetailModel, C_BaseTempEntity, and
//                the engine's own static props -- which is what
//                IStaticPropMgrClient::IsStaticProp( IHandleEntity * ) exists
//                for. Casting one of those down to IClientEntity would be a lie
//                about its layout and the call would land past the end of its
//                primary vtable, which is the exact hazard this file exists to
//                stop, arriving through this file.
//
// So the client asks GetIClientEntity() first. That is a virtual the object
// performs for ITSELF, it lives on IClientUnknown where every table entry has
// it, and all four non-entity implementors answer NULL. No cast and no
// assumption: two questions, both answered by the object.
//
// TAGS rather than defaults, so that choosing one is written at the call site
// and a reader cannot mistake the server's arm for the client's.
//-----------------------------------------------------------------------------
struct SourceSharpClientPartition {};

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

template<>
struct SourceSharp_Partition< SourceSharpClientPartition >
{
	// The client's two questions. NULL from the first is not "I cannot tell" --
	// it is "I am not an entity at all", which is a definite answer and means
	// the caller is holding something on NEITHER hierarchy.
	template< class TObject >
	static auto GameEntityOf( TObject *pObject )
		-> decltype( pObject->GetIClientEntity()->GetGameEntity() )
	{
		if ( !pObject )
			return 0;

		auto pEntity = pObject->GetIClientEntity();
		if ( !pEntity )
			return 0;

		return pEntity->GetGameEntity();
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
// TPartition is IServerEntity on the server and SourceSharpClientPartition on
// the client. Both arms are live now: Phase 13's revised step 1 added
// IClientEntity::GetGameEntity(), so the client's resolvers ask the same
// question the server's do rather than being counted and left. See
// sdk_player_cast_test.cpp, whose client ratchet became a ban on that commit.
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
// From the handle table's IHandleEntity to a combat weapon, or NULL.
//
// TUnknown is IServerUnknown on the server and IClientUnknown on the client;
// THandleEntity is deduced. NULL in means NULL out, a parallel-hierarchy
// entity comes out NULL at the partition, and an SDK entity that is not a
// weapon comes out NULL at the typed virtual. The one thing that can never
// come out is a pointer whose vtable belongs to a different class than its
// type says.
//
// THIS FUNCTION IS ITSELF ON THE STEP-5 LIST -- handle_cross.h:70 is the last
// entry in nullflip_verbose.txt's server column, and it earned its place: it
// used to read GetBaseEntity() and NULL-check the answer, so at step 5 a
// managed weapon in a CHandle<CBaseCombatWeapon> slot would have come back
// non-NULL and MyCombatWeaponPointer() would have dispatched through the wrong
// subobject. Which is the exact bug the crossing was written to prevent,
// arriving through the crossing.
//-----------------------------------------------------------------------------
template< class TWeapon, class TPartition, class TUnknown, class THandleEntity >
inline TWeapon *SourceSharp_CrossHandleToWeapon( THandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return 0;

	TUnknown *pUnknown = static_cast< TUnknown * >( pHandleEntity );

	// The partition. This is the step the (T*) cast skips, and skipping it is
	// the wrong-vtable bug.
	decltype( pUnknown->GetBaseEntity() ) pBase =
		SourceSharp_SDKEntityFromUnknown< TPartition >( pUnknown );
	if ( !pBase )
		return 0;

	return pBase->MyCombatWeaponPointer();
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
// TServerEntity is IServerEntity. THE SERVER'S FORM ONLY -- the client's is
// below, and it is a separate function rather than a template parameter because
// the cast on the next line WOULD BE WRONG over there.
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
// The same crossing on the CLIENT, and it needs its own function for the same
// reason SourceSharp_SDKEntityFromNetworkable does: the cast above would be
// wrong here.
//
// The client's handle table is WIDER THAN ITS ENTITY INTERFACE. Everything in
// it is an IClientUnknown -- that much the SDK already assumes, in
// EntityFromEntityHandle's client arm -- but not everything in it is an
// IClientEntity. CDefaultClientRenderable, CDetailModel and C_BaseTempEntity
// implement IClientUnknown and stop there, and the engine's static props are in
// the same table without being game objects at all. static_cast-ing one of
// those to IClientEntity compiles, adjusts by zero (IClientUnknown is its
// primary base), and then calls GetGameEntity() at an index that is PAST THE END
// of that object's primary vtable -- reading whatever follows it as a function
// pointer. That is not a smaller version of the bug this file prevents; it is
// the same bug.
//
// GetIClientEntity() is the conversion the object performs for itself. Every
// IClientUnknown has it, at an index no SourceSharp change moves, and every one
// of the four non-entity implementors answers NULL. So the crossing is two
// virtual calls and one structural cast, exactly like the server's -- the extra
// virtual is what buys the safety the server gets from its chain being narrow.
//
// TUnknown is IClientUnknown; TGameEntity is IGameEntity, deduced nowhere and
// written at the call site so a reader sees which boundary is meant.
//-----------------------------------------------------------------------------
template< class TGameEntity, class TUnknown, class THandleEntity >
inline TGameEntity *SourceSharp_CrossHandleToClientGameEntity( THandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return 0;

	// Structural, and the only cast: IHandleEntity -> IClientUnknown is one
	// single-inheritance step, and everything in the client's handle table is an
	// IClientUnknown.
	TUnknown *pUnknown = static_cast< TUnknown * >( pHandleEntity );

	return SourceSharp_Partition< SourceSharpClientPartition >::GameEntityOf( pUnknown );
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

#endif // SOURCESHARP_HANDLE_CROSS_H
