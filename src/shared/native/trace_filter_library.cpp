//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the shared trace filter family =====================//
// SS_REASON e  a synchronous mid-step answer
//
// plan_entity.md Phase 4, step 4e (Utilities). "The trace ABI facts" half of
// the step's gate, and the file step 7f needs before it can take
// game/shared/util_shared.cpp out of the client's vpc.
//
// WHAT THIS IS. Every trace in the game asks one of these classes "may this ray
// hit that handle", and util_shared.h's own comment calls the resolver they
// share the busiest crossing in the game: eleven sites read its answer and
// compare it against NULL to decide whether a trace hit something the SDK knows
// about. Both sides run this code, so a divergence here is a divergence in what
// the client and the server each believe a shot hit.
//
// WHY IT IS THE SHIM'S RATHER THAN STOCK'S. Not because of where it happened to
// sit: because it is already the port's own code. The rule the filters apply to
// an entity outside the SDK hierarchy was written by this port; the resolver
// they consult is the parallel hierarchy's; the value type the server arm
// resolves through is the port's own. Stock's file was its address, not its
// author. Every declaration stays in util_shared.h, so not one caller on either
// side changed — what moved is which object answers the link.
//
// IT DECIDES WHAT A TRACE HITS, WHICH SOUNDS LIKE POLICY. It is not, on §6's
// test: the decision each filter makes is fixed by the collision group, the
// contents mask and the pass entity that its CALLER chose, and choosing those
// is where the policy is. What is here is the arithmetic of applying them,
// which is the same thing the engine would be doing if it did not let the game
// supply an ITraceFilter.
//
// r_visualizetraces IS DECLARED HERE. It is the ConVar the UTIL_TraceLine /
// UTIL_TraceHull / UTIL_TraceRay inlines in util_shared.h read, and those
// inlines also construct the first class below — so every translation unit that
// traces at all referenced two symbols in stock's object. Moving one and not
// the other would have left the edge exactly where it was.
//
//=============================================================================//

#ifdef CLIENT_DLL
#include "cbase.h"
#include "util_shared.h"
#include "physics_shared.h"
#else
// THE SERVER HALF ON THE R5 SET (plan_server_novalve.md P5): cbase.h,
// util_shared.h and physics_shared.h went; what it used of them is below.
#include "tier1/convar.h"
#include "engine/ivmodelinfo.h"
#include "ss_entityhandle.h"      // ss::GameEntityFromEntityHandle
extern IVModelInfo *modelinfo;
#endif
#ifdef CLIENT_DLL
#include "gamerules.h"
#else
#include "igamerules.h"
#endif
#include "model_types.h"

// The shape clause both hierarchies read. See util_shared.h.
#include "standard_filter_shape.h"

#ifndef CLIENT_DLL
	// SOURCESHARP: the dual value type the filters below resolve through.
	// Server only, exactly as anyentity.h says: the client half of the parallel
	// hierarchy does not join the spatial partition, so there is nothing over
	// there for a filter to find and the client arms stay as they were.
	#include "anyentity.h"

#endif


//-----------------------------------------------------------------------------
// The trace debug-draw switch the UTIL_Trace* inlines in util_shared.h read.
//
// ON THE SERVER IT IS THE ENGINE'S, plan_server_novalve.md P2.3b. engine.so
// declares r_visualizetraces (beside R_LightVec in its string table) and
// registers it before either game DLL loads, so a declaration here was a
// duplicate. The server target defines the name as a macro, which turns
// util_shared.h's `extern ConVar r_visualizetraces;` into a declaration of the
// function below and each `r_visualizetraces.GetBool()` into a read of the
// engine's ConVar through it. Until the engine's is found (a unit test, or a
// read before the engine is connected) the answer is tier1's empty convar,
// which reads 0: tracing is not visualised.
//
// The client still declares its own, as stock did.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
ConVar r_visualizetraces( "r_visualizetraces", "0", FCVAR_CHEAT );
#else
ConVar *SourceSharp_EngineVisualizeTraces()
{
	static ConVar *s_pResolved = NULL;

	if ( s_pResolved )
		return s_pResolved;

	// Built empty and then Init()ed, the pair managed_convar_ref.h already
	// uses, rather than through the by-name constructor: one tier1 import fewer.
	ConVarRef ref( (IConVar *)NULL );
	ref.Init( "r_visualizetraces", true );
	ConVar *pConVar = static_cast< ConVar * >( ref.GetLinkedConVar() );

	if ( ref.IsValid() )
		s_pResolved = pConVar;

	return pConVar;
}
#endif

//-----------------------------------------------------------------------------
//
// Shared client/server trace filter code
//
//-----------------------------------------------------------------------------
bool PassServerEntityFilter( const IHandleEntity *pTouch, const IHandleEntity *pPass ) 
{
	if ( !pPass )
		return true;

	if ( pTouch == pPass )
		return false;

#if !defined( CLIENT_DLL )
	// SOURCESHARP: OWNERSHIP COMPARED AS HANDLES, so it survives the crossing.
	//
	// This resolved both sides to CBaseEntity and gave up when either answered
	// NULL — which is every entity on the parallel hierarchy — so a managed
	// projectile was never filtered against the thing that fired it and a
	// managed shooter was never filtered against its own projectile. Both
	// clipped on the frame they were created.
	//
	// The comparison itself never needed an entity: GetOwnerEntity returns a
	// CBaseEntity that is immediately compared as a pointer, and the parallel
	// hierarchy stores its owner as a handle already
	// (CStandaloneEntity::SetOwnerHandle), which is why AnyEntity_t answers this
	// one as an IHandleEntity — the base both sides share and the type the two
	// arguments already have.
	AnyEntity_t touch = AnyEntityOf( const_cast< IHandleEntity * >( pTouch ) );
	AnyEntity_t pass  = AnyEntityOf( const_cast< IHandleEntity * >( pPass ) );
	if ( !touch.IsValid() || !pass.IsValid() )
		return true;

	// don't clip against own missiles
	if ( touch.GetOwnerHandle() == pPass )
		return false;

	// don't clip against owner
	if ( pass.GetOwnerHandle() == pTouch )
		return false;

	return true;
#else
	// THE CLIENT'S HALF OF THE SAME QUESTION, AND IT HAS NO AnyEntity_t.
	//
	// anyentity.h is server only -- it says so in its own header, for the same
	// reason toucher.h does -- so the arm above cannot simply be widened to
	// cover this one. What is left here is the stock route, and armed it
	// answers `true` for everything: EntityFromEntityHandle walks the partition
	// before it asks GetBaseEntity(), so the only object it answers non-NULL for
	// is an SDK C_BaseEntity and armed there is no such class. That is exactly
	// what this build already does on the client for a managed projectile and
	// its owner -- the bug the server arm fixed, still open on the client -- so
	// the arm changes nothing and is not standing in for anything.
	//
	// GetOwnerEntity has a landed answer for when the client does get one:
	// StandaloneEntity_OwnerHandle, the relay that hands back the owner as the
	// IHandleEntity both hierarchies already are, which is what the server arm
	// above compares.

	return true;
#endif
}


//-----------------------------------------------------------------------------
// A standard filter to be applied to just about everything.
//-----------------------------------------------------------------------------
#ifndef CLIENT_DLL
//-----------------------------------------------------------------------------
// The shape clause, for an entity that is NOT a CBaseEntity.
//
// EntityFromEntityHandle answers NULL for everything on the parallel hierarchy,
// so every one of them took the static-prop arm below and was waved through by
// every trace unconditionally — the opposite of what the rule says about a
// prop. It cost the blast's blocking rule outright: the CONTENTS_SOLID retrace
// in CGameRules::RadiusDamage asks whether the WORLD is also in the way and
// skips the victim when it is, and a managed prop waved through that retrace
// made it stop on the victim itself. Every managed victim behind anything took
// ZERO instead of a reduced share.
//
// The three facts come off the engine-facing faces, which is where this
// hierarchy answers geometry and is the same question stock asks of a
// CBaseEntity: the model index is IServerEntity's, the solid type
// ICollideable's. The RULE over them is StandardFilterShapePasses, read here
// and by the arm below it, so the two cannot drift.
//
// NARROW IN PRACTICE, deliberately: every mask carrying CONTENTS_MONSTER passes
// unchanged, and that is MASK_SOLID, MASK_SHOT, MASK_PLAYERSOLID and
// MASK_RADIUS_DAMAGE — bullets, movement, and the blast's own visibility
// traces. What changes is the world-only masks, which is exactly the set that
// was being answered wrongly.
//
// The two clauses BELOW are not ported and do not need to be. IsTransparent is
// a render mode and an entity with none is opaque, which is the arm that does
// not filter. MOVETYPE_PUSH is the SDK's moving-brush movetype and nothing here
// has it — a managed door integrates its own position — so a managed brush that
// moves is not skipped by a CONTENTS_MOVEABLE-less mask where an SDK one would
// be. A divergence, written down rather than papered over.
//-----------------------------------------------------------------------------
static bool StandaloneFilterRules( IHandleEntity *pHandleEntity, int fContentsMask )
{
	IServerEntity *pServerEntity = (IServerEntity *)pHandleEntity;

	ICollideable *pCollideable = pServerEntity->GetCollideable();
	SolidType_t solid = pCollideable ? pCollideable->GetSolid() : SOLID_NONE;

	return StandardFilterShapePasses(
		modelinfo->GetModelType( modelinfo->GetModel( pServerEntity->GetModelIndex() ) ) == mod_brush,
		solid == SOLID_BSP || solid == SOLID_VPHYSICS,
		( fContentsMask & CONTENTS_MONSTER ) != 0 );
}
#endif // !CLIENT_DLL

bool StandardFilterRules( IHandleEntity *pHandleEntity, int fContentsMask )
{
	// THE SDK ARM IS GONE BECAUSE IT WAS ALREADY UNREACHABLE. This read
	// `EntityFromEntityHandle( pHandleEntity )` and branched on the answer being
	// NULL -- and that function answered a constant NULL in this build, so the
	// NULL branch WAS the whole function. The four asks the deleted arm made are
	// each accounted for in the prose above: GetSolid and GetModelIndex are
	// ICollideable's and IServerEntity's, which is where StandaloneFilterRules
	// asks them; IsTransparent is a render mode and an entity with none is
	// opaque, the arm that does not filter; MOVETYPE_PUSH is the SDK's
	// moving-brush movetype and nothing on this hierarchy has one.
	//
	// THE CLIENT GAP IS UNCHANGED AND STILL OPEN. StandaloneFilterRules is
	// `#ifndef CLIENT_DLL`, so on the client this returns true for a managed
	// entity as though it were a static prop. That is what this build does
	// today; closing it needs a client StandaloneFilterRules, which is a
	// behaviour change and not this pass's.
#ifndef CLIENT_DLL
	// An entity on the parallel hierarchy is not a static prop and must not be
	// waved through as one. See StandaloneFilterRules.
	if ( ss::GameEntityFromEntityHandle( pHandleEntity ) )
		return StandaloneFilterRules( pHandleEntity, fContentsMask );
#endif

	// Static prop case.
	return true;
}


//-----------------------------------------------------------------------------
// Simple trace filter. One body, two classes: on the client it is
// util_shared.h's CTraceFilterSimple; on the server it is server.so's own
// ss::CTraceFilterSimple (ss_tracefilter.h; the engine sees a filter only
// through ITraceFilter's vtable), whose last legacy user, shell_movement.cpp,
// moved to the R5 set in P5. Until then ss::'s ShouldHitEntity built a
// ::CTraceFilterSimple and asked it.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
#define SS_SIMPLE_FILTER CTraceFilterSimple
#else
#include "ss_tracefilter.h"
#define SS_SIMPLE_FILTER ss::CTraceFilterSimple
#endif

SS_SIMPLE_FILTER::CTraceFilterSimple( const IHandleEntity *passedict, int collisionGroup,
									   ShouldHitFunc_t pExtraShouldHitFunc )
{
	m_pPassEnt = passedict;
	m_collisionGroup = collisionGroup;
	m_pExtraShouldHitCheckFunction = pExtraShouldHitFunc;
}

//-----------------------------------------------------------------------------
// The trace filter!
//-----------------------------------------------------------------------------
bool SS_SIMPLE_FILTER::ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask )
{
	if ( !StandardFilterRules( pHandleEntity, contentsMask ) )
		return false;

	if ( m_pPassEnt )
	{
		if ( !PassServerEntityFilter( pHandleEntity, m_pPassEnt ) )
		{
			return false;
		}
	}

	// Don't test if the game code tells us we should ignore this collision...
	//
	// ONE ARM, AND IT IS THE ONE THAT ALREADY RAN. This asked
	// EntityFromEntityHandle first and fell into the block below when the answer
	// was NULL -- which it always was. The SDK arm that followed could not be
	// reached, and deleting it changes nothing, including on the client where
	// the answer is still `false`.
	//
	// The engine traces against anything in the spatial partition, and this
	// filter was the only thing rejecting an entity outside the SDK. Measured
	// before this existed: the same ray missed through UTIL_TraceLine and hit
	// through enginetrace->TraceRay, which is what established that the entity
	// was in the world and the filter was the problem.
#ifndef CLIENT_DLL
	if ( IGameEntity *pGameEntity = ss::GameEntityFromEntityHandle( pHandleEntity ) )
	{
		if ( !pGameEntity->ShouldCollide( m_collisionGroup, contentsMask ) )
			return false;

		if ( !g_pGameRules->ShouldCollide( m_collisionGroup,
		                                   pGameEntity->GetCollisionGroup() ) )
			return false;

		if ( m_pExtraShouldHitCheckFunction &&
			!m_pExtraShouldHitCheckFunction( pHandleEntity, contentsMask ) )
			return false;

		return true;
	}
#endif

	return false;
}

//-----------------------------------------------------------------------------
// THE OTHER NINE FILTERS ARE GONE -- plan_shim.md §5 3f, lane 3f.
//
// util_shared.h declares ten filter classes and this file defined all ten.
// Nine of them had no caller anywhere on either link line, and they went:
// OnlyNPCsAndPlayer, NoNPCsOrPlayer, SkipTwoEntities, SimpleList, LOS,
// SkipClassname, SkipTwoClassnames, SimpleClassnameList and Chain.
//
// WHY THAT NEEDED TWO INSTRUMENTS AND NOT ONE. plan_shim.md §7 records the
// caveat as 3f's open question: a per-function census sees only OUT-OF-LINE
// symbols, so a filter constructed entirely inline from a header would read as
// unused when it is not. Settled before the cut, not after:
//
//   nm -C over the objects the GRAPH names on both link lines -- no object
//   except this one's own emits OR references `vtable for CTraceFilter*` for
//   any of the nine, defined (V) or undefined (U) alike. A class with a key
//   function is emitted once and referenced from everywhere else, so the U rows
//   are the ones that would have caught an inline constructor; there are none.
//
//   tools/shim-census who-imports agrees from the other side: the ONLY symbols
//   any live object imports from this one are CTraceFilterSimple's constructor,
//   its ShouldHitEntity, its typeinfo, and the ConVar r_visualizetraces.
//
// THE DECLARATIONS STAY IN util_shared.h, which is stock's header and not this
// port's to edit -- same shape as §4a item 8's two documented cases. Files this
// build does not compile (ai_basenpc.cpp, beam_shared.cpp, the nav mesh, the tf
// tree) still name these classes, and a lane porting one of them wants the
// declaration to still be there. It is a link error for a NEW caller rather
// than a compile error, which is the cost of that choice, stated rather than
// discovered.
//
// The managed side already re-decided the one that mattered: EntityMotion.cs
// documents SkipTwoEntities as the filter native used for a projectile's step
// trace, and why the ABI's one-entity TraceLine diverges from it.
//-----------------------------------------------------------------------------
