//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
// SS_REASON b  an interface resolved by name at load
//
// IServerGameEnts -- THE EDICT<->ENTITY MAP AND THE TRANSMIT WALK.
//
// plan_gamedll_after.md Phase A9 step 1. Carried verbatim out of
// game/server/gameinterface.cpp:2281-2685, which this phase deletes. Six
// virtuals, one EXPOSE_, and the class body that was declared inline beside
// them; nothing else in that file named CServerGameEnts, so the whole subject
// is this file.
//
// THE ONE THING THAT IS NOT A VERBATIM CARRY. Stock kept the debug edict base
// in a file-scope global, `edict_t *g_pDebugEdictBase = 0;`
// (gameinterface.cpp:258), 2,000 lines away from the only method that touches
// it. tools/statics-audit gates that shape out of src/sourcesharp and the
// owner rule behind it says state goes in a class as fields, so it is a member
// of CServerGameEnts here. Nothing outside the class ever read it -- the whole
// tree held exactly two references, the definition and the one assignment
// below -- so making it a field costs no caller anything.
//
//===========================================================================//

#include "ss_util.h"   // UTIL_ClearTrace, and engine / eiface.h / edict.h / gametrace.h
#include "server_class.h"
#include "ispatialpartition.h"
#include "datacache/imdlcache.h"
#include "toolframework/iserverenginetools.h"
#include "shell_steporder.h"

// plan_entity.md Phase 0 step 0e -- the engine-boundary probe. Every virtual
// below is armed with a counter that records its caller's return address, so
// docs/engine-boundary.md can say which of these the engine actually calls and
// what it does with the answer.
#include "shell_boundary_probe.h"

// SourceSharp_SkyboxAreaForClient -- CheckTransmit's recipient question, asked
// of C#. plan_server.md Phase 8b, plan-audit C4.
//
// THE STANDALONE PLAYER'S HEADER LEFT WITH IT. This file included
// shell_standalone_player.h for one reason: CheckTransmit's recipient may be a
// player whose edict resolves to no CBaseEntity, and Phase 9a reached for
// CStandalonePlayer::FromEdict right here. That reach is now a step in
// shell_transmitsteps.cpp, chosen by C#, so the interface file no longer needs
// to know the second hierarchy exists at all -- which is the shape of the
// change, not a side effect of it.
#include "shell_server.h"
#include "imanaged_areas.h"   // IManagedClients::ServerTransmitAlwaysForClient

// Toucher_t and its two builders, for MarkEntitiesAsTouching. INCLUDED
// DIRECTLY now: it used to arrive through shell_standalone_player.h, which
// left with the transmit disjunction above. A header that arrives by
// accident is a header nobody knows this file needs.
#include "toucher.h"
#include "shell_factory.h"

// gpGlobals is defined in shell_engineboundary.cpp, mdlcache in tier_connect.cpp;
// cbase.h declared both before P5.
extern CGlobalVars *gpGlobals;
extern IMDLCache *mdlcache;

class CServerGameEnts : public IServerGameEnts
{
public:
	virtual void			SetDebugEdictBase(edict_t *base);
	virtual void			MarkEntitiesAsTouching( edict_t *e1, edict_t *e2 );
	virtual void			FreeContainingEntity( edict_t * ); 
	virtual edict_t*		BaseEntityToEdict( IGameEntity *pEnt );
	virtual IGameEntity*	EdictToBaseEntity( edict_t *pEdict );
	virtual void			CheckTransmit( CCheckTransmitInfo *pInfo, const unsigned short *pEdictIndices, int nEdicts );

private:
	// Stock's `edict_t *g_pDebugEdictBase` (gameinterface.cpp:258), as a field.
	// See the file header: write-only in the whole tree, and a file-scope
	// global is the shape tools/statics-audit exists to keep out of here.
	edict_t					*m_pDebugEdictBase = NULL;

	// STOCK'S `ConVar sv_force_transmit_ents` (gameinterface.cpp:232) IS GONE,
	// and the argument that kept it is the argument that removes it --
	// plan_shim.md §5 3a, item 8.
	//
	// It was kept because "a ConVar nothing reads is a ConVar whose setting
	// silently does nothing, which is a worse state than an honest one". True,
	// and the read that was kept to avoid that state was `(void)`-discarded on
	// the line after it: `const bool bWouldForceTransmit = bInPVS ||
	// m_svForceTransmitEnts.GetBool(); (void)bWouldForceTransmit;`. So the
	// convar was ALREADY in the state the comment called worse -- a cheat that
	// registers, accepts a value, and changes nothing -- and the read was
	// satisfying a gtest rather than a caller.
	//
	// NOT MOVED TO MANAGED EITHER, which is the other half of the ruling.
	// Declaring it in ConVars.cs would put the same silent no-op in a new file;
	// what it used to control is decided in shell_transmitsteps.cpp and in C#
	// now. It is FCVAR_DEVELOPMENTONLY, so no shipped config can be relying on
	// it, and a repo-wide grep for the literal name found it in this file and
	// its gtest and nowhere else -- not in tools/, docs/, cfg/, the managed
	// tree or any .vpc. If a managed transmit path ever wants the knob back it
	// is one [ConVar] attribute, declared beside the code that reads it.
};

namespace
{
	CServerGameEnts s_ServerGameEnts;
}
// Answered by the factory (shell_factory.cpp), which asks for it by this row.
static void *SourceSharp_GetServerGameEnts() { return (IServerGameEnts *)&s_ServerGameEnts; }
const SourceSharpExposed kExposeServerGameEnts = { INTERFACEVERSION_SERVERGAMEENTS, SourceSharp_GetServerGameEnts };

void CServerGameEnts::SetDebugEdictBase(edict_t *base)
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Ents_SetDebugEdictBase );

	m_pDebugEdictBase = base;
}

//-----------------------------------------------------------------------------
// Purpose: Marks entities as touching
// Input  : *e1 - 
//			*e2 - 
//-----------------------------------------------------------------------------
void CServerGameEnts::MarkEntitiesAsTouching( edict_t *e1, edict_t *e2 )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Ents_MarkEntitiesAsTouching );

	// GetContainingEntity ANSWERED A CONSTANT NULL in this build: it is
	// SourceSharp_SDKEntityFromUnknown< IServerEntity >, which bottoms out in
	// IServerUnknown::GetBaseEntity(), and every implementation of that in
	// server.so returns NULL (the close read all three out of the binary). The
	// function is gone; the NULL it answered is written down instead.
	IGameEntity *entity = NULL;
	IGameEntity *entityTouched = NULL;
	if ( entity && entityTouched )
	{
		// HACKHACK: UNDONE: Pass in the trace here??!?!?
		trace_t tr;
		UTIL_ClearTrace( tr );
		// Phase 13 (e) retype: GetAbsOrigin() == GetCollideable()->GetCollisionOrigin()
		// (collisionproperty.cpp's GetCollisionOrigin returns m_pOuter->GetAbsOrigin()
		// unconditionally, parented entities included).
	// GetCollideable and PhysicsMarkEntitiesAsTouching are both CBaseEntity's.
	// The `return` goes with them, so armed control falls into the block below --
	// which is already the arm for "at least one of the two is not a
	// CBaseEntity", and armed that is both of them.
	}

	// At least one of the two is not a CBaseEntity.
	//
	// The engine has already done the hard part: SolidMoved enumerated the
	// trigger list in the spatial partition, found the overlap, and reported
	// this pair. Everything below that point assumed both sides were
	// CBaseEntity and did nothing at all when either was not — so an entity
	// outside the SDK could sit in the trigger list, be found by the engine
	// every tick, and never be told.
	//
	// BOTH kinds are resolved and BOTH are told, because a touch is symmetric
	// and PhysicsMarkEntitiesAsTouching above has always treated it so: each of
	// the two gets a link naming the other. Two entities outside the SDK
	// touching each other used to be dropped entirely here, on the grounds that
	// the touch was delivered as a CBaseEntity and there was none to deliver —
	// a managed prop rolling into a managed trigger reported nothing at all.
	// Toucher_t is what that argument was missing.
	//
	// Only the standalone sides are told from here. An SDK entity's side of a
	// mixed pair is reported by the standalone one's own link bookkeeping,
	// which is the only place that can hold the link at all — touchlink_t is an
	// handle typed to CBaseEntity and cannot name the other hierarchy. Telling it here as well
	// would report the same arrival twice.
	const Toucher_t toucher1 = entity ? ToucherOf( entity ) : ToucherFromEntityHandle( e1->GetUnknown() );
	const Toucher_t toucher2 = entityTouched ? ToucherOf( entityTouched ) : ToucherFromEntityHandle( e2->GetUnknown() );

	if ( toucher1.pGame && toucher2.IsValid() )
		toucher1.pGame->Touch( toucher2 );

	if ( toucher2.pGame && toucher1.IsValid() )
		toucher2.pGame->Touch( toucher1 );
}

//-----------------------------------------------------------------------------
// THE FREE FUNCTION IT FORWARDED TO IS COLLAPSED IN HERE. plan_shim.md Phase 2c.
//
// ::FreeContainingEntity lived in game/server/mapentities.cpp and this was its
// only importer. Its whole body was:
//
//     if ( ed )
//     {
//         IGameEntity *ent = NULL;          // GetContainingEntity, written down
//         if ( ent ) { ed->SetEdict( NULL, false ); PhysicsLink_RemoveTouchedList( ent );
//                      PhysicsLink_RemoveGroundList( ent ); UTIL_RemoveImmediate( ent ); }
//     }
//
// -- a branch on a local NULL, and the NULL was itself a measurement written
// into the code by an earlier lane: GetContainingEntity is
// SourceSharp_SDKEntityFromUnknown< IServerEntity >, which bottoms out in
// IServerUnknown::GetBaseEntity(), and EVERY implementation of that in server.so
// returns NULL. This lane re-took that measurement from the other end over two
// dm_lockdown loads in one process: 781 entities created, GetBaseEntity()
// non-NULL 0 times.
//
// So the function was one translation unit's worth of indirection onto nothing,
// and the engine's entry point is the honest place for the nothing to be said.
// It is NOT an empty body: the engine calls this to release an edict's entity
// and there is no entity to release, and that sentence is here rather than in
// a file that no longer exists.
//-----------------------------------------------------------------------------
void CServerGameEnts::FreeContainingEntity( edict_t *e )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Ents_FreeContainingEntity );

	// Nothing to free. An edict on this build never has a CBaseEntity behind
	// it, so the touch-list, ground-list and removal the SDK did here have no
	// subject. The edict itself is the engine's and is not ours to clear.
	(void)e;
}

edict_t* CServerGameEnts::BaseEntityToEdict( IGameEntity *pEnt )
{
	// edict() reaches through CBaseEntity's IServerEntity base, which the alias
	// removes. NULL is the answer this function already gives for an entity it
	// cannot turn into an edict.
	//
	// 0e: ONE RETURN, so the probe sees every answer. Two returns armed
	// separately would count the same call twice the day someone adds a third
	// arm above them, and a "calls" number that can double is not evidence.
	edict_t *pResult = NULL;


	SS_BOUNDARY_HIT_PTR( kBoundaryProbe_Ents_BaseEntityToEdict, pResult );
	return pResult;
}

IGameEntity* CServerGameEnts::EdictToBaseEntity( edict_t *pEdict )
{
	// 0e: one return, for the reason BaseEntityToEdict gives above. The
	// null/non-null split is the whole point of arming this one -- a method
	// that already answers NULL on every call is a method Phase 1 can make
	// answer NULL unconditionally.
	// GetContainingEntity ANSWERED A CONSTANT NULL in this build: it is
	// SourceSharp_SDKEntityFromUnknown< IServerEntity >, which bottoms out in
	// IServerUnknown::GetBaseEntity(), and every implementation of that in
	// server.so returns NULL (the close read all three out of the binary). The
	// function is gone; the NULL it answered is written down instead.
	IGameEntity *pResult = NULL;

	SS_BOUNDARY_HIT_PTR( kBoundaryProbe_Ents_EdictToBaseEntity, pResult );
	return pResult;
}



void CServerGameEnts::CheckTransmit( CCheckTransmitInfo *pInfo, const unsigned short *pEdictIndices, int nEdicts )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Ents_CheckTransmit );

	// NOTE: for speed's sake, this assumes that all networkables are CBaseEntities and that the edict list
	// is consecutive in memory. If either of these things change, then this routine needs to change, but
	// ideally we won't be calling any virtual from this routine. This speedy routine was added as an
	// optimization which would be nice to keep.
	edict_t *pBaseEdict = engine->PEntityOfEntIndex( 0 );

	// get recipient player's skybox:
	// GetContainingEntity ANSWERED A CONSTANT NULL in this build: it is
	// SourceSharp_SDKEntityFromUnknown< IServerEntity >, which bottoms out in
	// IServerUnknown::GetBaseEntity(), and every implementation of that in
	// server.so returns NULL (the close read all three out of the binary). The
	// function is gone; the NULL it answered is written down instead.
	// Read only by the Assert below, so a release build sees it unused.
	[[maybe_unused]] IGameEntity *pRecipientEntity = NULL;

	// WHICH HIERARCHY ANSWERS FOR THIS CLIENT -- plan_server.md Phase 8b,
	// docs/plan-audit.md C4. THE DISJUNCTION THAT USED TO BE HERE IS NOW C#.
	//
	// A standalone recipient resolves to no CBaseEntity, and the guard below
	// transmitted NOTHING to that client -- the whole world invisible, with an
	// assert in debug and silence in release (Phase 9a). The fix was an
	// `#ifdef SOURCESHARP` block right here that reached for
	// CStandalonePlayer::FromEdict when the SDK lookup came back NULL, and then
	// a ternary further down choosing between two skybox areas.
	//
	// That is a DECISION -- which hierarchy answers, and in which order -- and
	// plan_gamedll.md Part 4's rule is that a step is never one. So the two
	// reads are now steps in shell_transmitsteps.cpp that each ask ONE
	// hierarchy and decide nothing, and the order they are asked in is one
	// method of ServerEntities in C#. The behaviour is stock's, unchanged; the
	// place it is written down is not. plan_server.md Part 6's sentence about
	// MarkEntitiesAsTouching applies word for word to this.
	//
	// nRecipientIndex REPRODUCES THE FALLBACK ABOVE. `pInfo->m_pClientEnt ? … :
	// pBaseEdict` is edict 0 when the engine names no client, and index 0 is
	// what that is; a managed side asked about "no client" would have to invent
	// the same fallback and could invent a different one.
	//
	// AND GetContainingEntity RUNS TWICE PER CLIENT PER FRAME NOW -- once above
	// and once inside the SDK step. That is deliberate and it is the price of
	// the step deciding nothing. Passing pRecipientEntity into the step would
	// save one pointer deref a frame and would hand the step the answer to the
	// question it exists to be asked separately, which is the whole shape of
	// this change put back.
	const int nRecipientIndex = pInfo->m_pClientEnt
	                                ? (int)( pInfo->m_pClientEnt - pBaseEdict )
	                                : 0;

	int skyBoxArea = 0;
	const bool bHaveRecipient =
		SourceSharp_SkyboxAreaForClient( nRecipientIndex, &skyBoxArea ) != 0;

	// SOURCESHARP: STATIC-NS's neighbour -- an ordinary ask, but inside an
	// Assert, so -DNDEBUG hides it from every release measurement. Found by
	// compiling this TU armed with -D_DEBUG, which no instrument in this port
	// does. Armed the SDK half of the disjunction cannot be true, and the
	// standalone half is the whole assertion.
	//
	// STILL READS pRecipientEntity, and it must: the assertion is about the SDK
	// entity's TYPE, which no skybox area can stand in for. The managed answer
	// says a recipient exists; this says an SDK one that exists is a player.
	Assert( bHaveRecipient && !pRecipientEntity );
	if ( !bHaveRecipient )
		return;

	// GATE 5's TRANSMIT SET -- plan_server.md Gate 5, docs/plan-audit.md C2.
	//
	// ARMED HERE AND NOT AT THE TOP OF THE FUNCTION, because the two early
	// returns above it are recipients this function DECLINES to answer for. A
	// capture begun before them would spend its one shot on a frame that
	// transmits nothing and hand the gate an empty set that looks exactly like
	// a client who can see nothing -- which is the finding Gate 5 exists to
	// report, so producing it as an artefact of the instrument is the worst
	// available failure.
	//
	// The set itself is COPIED AT THE END from pInfo->m_pTransmitEdict rather
	// than marked at each of the eleven places that set a bit. That is not
	// brevity: SetTransmit() sets bits this function never touches (an entity's
	// dependents), so an instrument that marked only the sites visible here
	// would record a SUBSET and call it the transmit set. The bitvec at the end
	// of the walk is the answer the engine gets, which is the noun Gate 5 names.
	//
	// nRecipientIndex is computed above, where the skybox question needed it.
	const bool bCaptureTransmit = ShellGameDll_TransmitCaptureBegin( nRecipientIndex );

	// THE INPUT, RECORDED BESIDE THE OUTPUT. skyBoxArea is what plan-audit C4
	// moved into C#, and a mutation proved the SET alone cannot see a wrong
	// one: perturbed by +1 on dm_lockdown the transmit set is byte-identical,
	// because no entity there sits in either area. See shell_steporder.h.
	if ( bCaptureTransmit )
		ShellGameDll_TransmitCaptureSkyboxArea( skyBoxArea );

	MDLCACHE_CRITICAL_SECTION();

	// STOCK CBasePlayer::ShouldTransmit's FIRST ARM, decided in C#: the
	// recipient's own player is sent whatever its PVS says ("allow me to
	// introduce myself to, err, myself"), ahead of the EF_NODRAW / observer
	// rule. Without it an observer watching in-eye from across the map is
	// culled from its own snapshot and stops receiving its observer fields
	// (live gate spectate, 2026-10-11). Marked before the walk, which skips
	// an edict already marked.
	if ( IManagedClients *pApi = ManagedClients() )
	{
		const int iAlways = pApi->ServerTransmitAlwaysForClient( nRecipientIndex );
		if ( iAlways > 0 && iAlways < engine->GetEntityCount() && !pBaseEdict[iAlways].IsFree() )
			pInfo->m_pTransmitEdict->Set( iAlways );
	}

	// THE HLTV / REPLAY RECIPIENT IS GONE FROM THIS FUNCTION -- plan_shim.md
	// step 5c, and this is the one deletion in the step that deserves reading
	// twice, so the whole argument is here rather than in the plan.
	//
	// WHAT STOOD HERE. A `CBasePlayer *pRecipientPlayer = NULL;`, two locals
	//
	//     const bool bIsHLTV   = pRecipientPlayer && pRecipientPlayer->IsHLTV();
	//     const bool bIsReplay = pRecipientPlayer && pRecipientPlayer->IsReplay();
	//
	// an Assert on `!pRecipientEntity || bIsHLTV == (m_pTransmitAlways != NULL)
	// || bIsReplay == ...`, and two `if ( bIsHLTV || bIsReplay )` arms further
	// down: one that also set `pInfo->m_pTransmitAlways`, one whose body was a
	// comment saying HLTV is not culled against the PVS.
	//
	// IT WAS ALREADY UNREACHABLE, AND THAT IS THE POINT. pRecipientPlayer was
	// a hardcoded NULL -- it has been since the entity port replaced
	// GetContainingEntity -- so both locals were unconditionally false and both
	// arms were dead code BEFORE this step touched the file. pRecipientEntity
	// is likewise a hardcoded NULL twenty lines above, so the Assert's first
	// disjunct was always true and it asserted nothing.
	//
	// SO WHAT HAPPENS TO A SOURCETV CLIENT? The engine will create one if it is
	// told to -- `tv_enable 1` gets as far as `maxplayers set to 5 (extra slot
	// was added for SourceTV)` and `ClientPutInServer (server): SourceTV` even
	// on a mod with no director module, where `tv_status` then reports SourceTV
	// not active. Such a client reaches this function like any other recipient,
	// and it gets ORDINARY PVS-CULLED TRANSMIT with an empty m_pTransmitAlways.
	// That is what it already got: deleting unreachable code cannot change what
	// a client receives, and nothing here is a new refusal.
	//
	// The refusal is made elsewhere and deliberately: `-nohltv` and
	// `+tv_enable 0` on every launch line this tree owns (Makefile RUN_ARGS,
	// tools/live-gate), so the client is not created in the first place. If
	// that enforcement is ever removed, what comes back is a SourceTV client
	// that sees the world through the PVS instead of seeing all of it -- a
	// degraded broadcast, not a crash, and not something this function should
	// grow a branch to pretend otherwise about.

	for ( int i=0; i < nEdicts; i++ )
	{
		int iEdict = pEdictIndices[i];

		edict_t *pEdict = &pBaseEdict[iEdict];
		Assert( pEdict == engine->PEntityOfEntIndex( iEdict ) );
		int nFlags = pEdict->m_fStateFlags & (FL_EDICT_DONTSEND|FL_EDICT_ALWAYS|FL_EDICT_PVSCHECK|FL_EDICT_FULLCHECK);

		// entity needs no transmit
		if ( nFlags & FL_EDICT_DONTSEND )
			continue;
		
		// entity is already marked for sending
		if ( pInfo->m_pTransmitEdict->Get( iEdict ) )
			continue;
		
		if ( nFlags & FL_EDICT_ALWAYS )
		{
			// mark entity for sending
			//
			// STOCK ALSO SET pInfo->m_pTransmitAlways HERE, under
			// `if ( bIsHLTV || bIsReplay )`. Dropped at 5c; see the block
			// above the loop for why that arm never ran in this build.
			//
			// STOCK THEN WALKED THE NETWORK PARENTS, marking each, and that walk
			// is gone with CServerNetworkProperty (P5 lane E). It cast the
			// edict's networkable to a CServerNetworkProperty and asked
			// GetNetworkParent(), whose body here was `return NULL;`, so the walk
			// marked this one edict and stopped on every path. server.so never
			// constructs a CServerNetworkProperty (no CBaseEntity owns one), so
			// the cast named an object of another type anyway: a shell's or the
			// world's own networkable.
			pInfo->m_pTransmitEdict->Set( iEdict );
			continue;
		}

		// Decided on visibility alone, because that is all an entity here has
		// to offer: no ShouldTransmit to ask, no hierarchy to walk, and no
		// network property to read an area number from.
		//
		// STOCK ASKED THE EDICT FOR ITS CBaseEntity FIRST, and the arm for one
		// that answered is gone (P5 lane E). It was
		// SourceSharp_SDKEntityFromUnknown< IServerEntity >, which ends in
		// IServerUnknown::GetBaseEntity(), and server.so's three
		// implementations (CStandaloneEntity, CWorld, CWorldNetworkable) all
		// return NULL. So this arm was the only one that ever ran, and what
		// followed it -- the CServerNetworkProperty area and PVS reads and the
		// parent chase -- never ran. Its own FL_EDICT_ALWAYS test was already
		// answered by the arm above, which always continues. (Its ShouldTransmit
		// and SetTransmit calls went earlier, at the close, with the
		// SS_CBASEENTITY_ALIAS regions that guarded them.)
		if ( !( nFlags & FL_EDICT_PVSCHECK ) )
			continue;

		ICollideable *pCollide = pEdict->GetCollideable();
		if ( !pCollide )
			continue;

		Vector vecMins, vecMaxs;
		pCollide->WorldSpaceSurroundingBounds( &vecMins, &vecMaxs );

		if ( engine->CheckBoxInPVS( vecMins, vecMaxs, pInfo->m_PVS, pInfo->m_nPVSSize ) )
			pInfo->m_pTransmitEdict->Set( iEdict );
	}

	// GATE 5, the other end. See the arming comment above the walk.
	if ( bCaptureTransmit )
	{
		COMPILE_TIME_ASSERT( CShellTransmitSet::kMaxEdicts == MAX_EDICTS );

		for ( int i = 0; i < MAX_EDICTS; i++ )
		{
			if ( pInfo->m_pTransmitEdict->Get( i ) )
				ShellGameDll_TransmitCaptureMark( i );
		}
	}

//	Msg("A:%i, N:%i, F: %i, P: %i\n", always, dontSend, fullCheck, PVS );
}

// ss_boundary_probe_report and ss_boundary_probe_reset, the engine-boundary
// probe's readout, are diagnostics: they live in shell_gameents_debug.cpp, which
// only a debug build compiles.
