//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the legacy temp entity pool, from C# ===============//
// SS_REASON a  a vtable the engine calls through
//
// plan_tmp.md Part 5, C-Phase 4. docs/te-legacy-pool-survey.md is the map of
// the 3,869 lines this came out of and is worth reading before this file.
//
// WHAT MOVED. c_te_legacytempents.cpp's CTempEnts is a pool of 500
// C_LocalTempEntity — gibs, shell casings, sprites and debris that bounce,
// animate, make noise and expire. Its BOOKKEEPING is now
// SourceSharp.Client/TempEnts/ClientTempEntPool.cs: the active list, the free
// list, the priority classes, the low-priority eviction, the lifetime and
// expiry, and the per-frame update loop. This file is the seam.
//
// WHAT DID NOT, AND WHY NOT ANY OF IT IS AN OVERSIGHT:
//
//   the object          A C_LocalTempEntity is already the DRAW SHELL — it was
//                       de-entified onto CShellRenderable and
//                       src/sourcesharp/test/tempent_verdict_test.cpp records
//                       that verdict. Its thirty members are the drawable's
//                       state, and a managed mirror of them would go stale
//                       within one frame of a bounce: native Frame() writes
//                       die and flags every tick.
//   Frame()             the physics. Its collision arm needs UTIL_TraceLine and
//                       a whole trace_t, and the ABI's TraceLine is wired
//                       #ifndef CLIENT_DLL — this client has no trace at all.
//                       That is the same wall docs/te-ledger.md records for
//                       CTEProjectedDecal, and it is what holds ledger rows C4
//                       and C5 open. It is NOT the pool being native.
//   PlaySound           called from inside Frame's collision arm, so it moves
//                       with that arm and not before it.
//   the spawn helpers   seventeen of them; the survey tables the order.
//
// THE SLOT TABLE, AND WHY A NUMBER RATHER THAN A POINTER. The managed pool
// addresses a pooled tempent by SLOT, and the rows below are what a slot means.
// A managed pool holding a C_LocalTempEntity * would be one managed reload away
// from a dangling one — the pool survives a reload and the objects do not — and
// the failure would be a wild write into freed memory rather than a dropped
// effect. A slot that has been emptied simply answers false to everything, and
// the managed pool drops it on its next pass, which is also how a reservation
// that never got an object cleans itself up.
//
// THE POOL IS ENTERED FROM THE LEGACY SHELL, AND FROM C#. fx_shelleject.cpp
// and c_baseanimating.cpp eject brass, fx.cpp and c_baseanimating.cpp flash
// muzzles, cdll_client_int.cpp drives the frame and view.cpp the level — and
// since the tempents cutover those four are CTempEnts methods in
// shell_legacy_tempents_client.cpp, which is why the frame/level drivers and
// Find live on this side. Nothing reserves from here any more: every object
// on a row is one the managed pool asked for and filled through
// TempEntPoolCreate, and nothing on this side asks C# for a slot.
//
//=============================================================================//

#include "cbase.h"

// plan_entity.md Phase 5a: an entity index or handle resolves through §3.3's
// slot table, not through cl_entitylist. shell_cliententitylist.h says which of
// the five resolvers answers which question.
#include "shell_cliententitylist.h"
#include "tempent.h"
#include "c_te_legacytempents.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"   // plan_client_vtables CE6: IManagedTempEntsClient
#include "view.h"
#include "movevars_shared.h"
#include "env_wind_shared.h"
#include "fx.h"                     // g_Mat_*
#include "iefx.h"
#include "dlight.h"
#include "c_recipientfilter.h"
#include "engine/IEngineSound.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "gamesound.h"
#include "model_types.h"
#include "studio.h"
#include "datacache/imdlcache.h"
#include <limits>

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// Forward-declared because Clear's sweep below uses it, and it is
// one of the six ABI slots defined at the bottom of this file.
void SourceSharp_TempEntPoolDestroy( int32_t nSlot );

//-----------------------------------------------------------------------------
// THE SLOT TABLE.
//
// One row per slot, NULL when the slot is empty. Sized to the pool's own
// capacity so that the number lives in exactly one place: CTempEnts declares
// MAX_TEMP_ENTITIES and SourceSharp_TempEntPoolCapacity answers it, rather than
// C# restating 500 beside a C++ enum — which is the stand-in this tree's memory
// says drifts silently.
//-----------------------------------------------------------------------------
static C_LocalTempEntity *g_pTempEntSlots[ CTempEnts::MAX_TEMP_ENTITIES ];

//-----------------------------------------------------------------------------
// The pool instance, as the free functions below reach it.
//
// `tempents` is the ITempEnts * every native caller already uses and it points
// at the one file-static CTempEnts in c_te_legacytempents.cpp. The downcast is
// safe by construction — CTempEnts is the only implementation linked into this
// build, ITempEnts is a non-virtual base of it, and HL1_CLIENT_DLL (which is
// the one build that supplies a different one) is not defined here.
//-----------------------------------------------------------------------------
static inline CTempEnts *ThePool()
{
	return static_cast< CTempEnts * >( tempents );
}

static inline bool ValidSlot( int nSlot )
{
	return nSlot >= 0 && nSlot < CTempEnts::MAX_TEMP_ENTITIES;
}

//-----------------------------------------------------------------------------
// The two members of CTempEnts the slot table cannot reach from outside,
// because m_TempEntsPool and AddVisibleTempEntity are private. Declared in
// c_te_legacytempents.h under #ifdef SOURCESHARP; defined here rather than
// there so that the stock file's whole diff stays #ifndef SOURCESHARP wrappers
// around bodies that did not change.
//-----------------------------------------------------------------------------
void CTempEnts::PoolFreeObject( C_LocalTempEntity *pTemp )
{
	m_TempEntsPool.Free( pTemp );
}

int CTempEnts::PoolAddVisible( C_LocalTempEntity *pTemp )
{
	return AddVisibleTempEntity( pTemp );
}

//=============================================================================//
// NATIVE -> MANAGED. What c_te_legacytempents.cpp calls.
//=============================================================================//

//-----------------------------------------------------------------------------
// One frame of the pool. CTempEnts::Update's body.
//-----------------------------------------------------------------------------
void SourceSharpTePool_Update( float flFrameTime )
{
	if ( IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient() )
		pTempEnts->OnTempEntPoolUpdate( flFrameTime );
}

//-----------------------------------------------------------------------------
// Free every live tempent. CTempEnts::Clear's body.
//
// THE SWEEP AFTERWARDS IS NOT BELT AND BRACES. Clear() runs at level shutdown
// and from view.cpp's Init and Shutdown, which are places the managed side may
// legitimately be down — during a reload, or after ClientShutdown. A slot left
// bound then would hold a C_LocalTempEntity the memory pool believes is live
// across a level change, so this side finishes the job with the same teardown
// the managed path uses.
//-----------------------------------------------------------------------------
void SourceSharpTePool_Clear( void )
{
	if ( IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient() )
		pTempEnts->OnTempEntPoolClear();

	for ( int i = 0; i < CTempEnts::MAX_TEMP_ENTITIES; i++ )
	{
		if ( g_pTempEntSlots[ i ] != NULL )
			SourceSharp_TempEntPoolDestroy( i );
	}
}

//-----------------------------------------------------------------------------
// CTempEnts::FindTempEntByID, over the slot table.
//
// The slot table holds exactly what the managed active list holds — a slot is
// bound when the managed create fills it and emptied when it is destroyed — so this walk and
// a managed one would answer the same thing. It stays here because both fields
// it matches on are the native object's and the answer is a pointer that could
// not cross the ABI in any case.
//-----------------------------------------------------------------------------
C_LocalTempEntity *SourceSharpTePool_Find( int nID, int nSubID )
{
	// HACK HACK: We're using skin and hitsounds as a hacky way to store an ID
	// and sub-ID for later identification -- stock's comment, stock's hack.
	for ( int i = 0; i < CTempEnts::MAX_TEMP_ENTITIES; i++ )
	{
		C_LocalTempEntity *p = g_pTempEntSlots[ i ];

		if ( p && p->m_nSkin == nID && p->hitSound == nSubID )
			return p;
	}

	return NULL;
}

//=============================================================================//
// MANAGED -> NATIVE. The six ABI slots, wired in sourcesharp_gamesystem.cpp.
//=============================================================================//

int32_t SourceSharp_TempEntPoolCapacity( void )
{
	return (int32_t)CTempEnts::MAX_TEMP_ENTITIES;
}

//-----------------------------------------------------------------------------
// Give one back. Stock CTempEnts::TempEntFree's body, in its order, minus the
// list removal that is now the managed pool's.
//-----------------------------------------------------------------------------
void SourceSharp_TempEntPoolDestroy( int32_t nSlot )
{
	if ( !ValidSlot( nSlot ) )
		return;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[ nSlot ];

	if ( !pTemp )
		return;

	// EMPTIED FIRST. OnRemoveTempEntity walks the break-model helper list and
	// PoolFreeObject hands the memory back; either could, in principle, reach
	// something that asks this table what is live, and the answer has to be
	// "not this one" by then.
	g_pTempEntSlots[ nSlot ] = NULL;

	pTemp->RemoveFromLeafSystem();
	pTemp->OnRemoveTempEntity();

	ThePool()->PoolFreeObject( pTemp );
}

//-----------------------------------------------------------------------------
// The lifetime view, read and written. See SourceSharpTempEntLife_t in
// sourcesharp.h for why it is a view and not a second copy of the state.
//-----------------------------------------------------------------------------
int32_t SourceSharp_TempEntPoolReadLife( int32_t nSlot, SourceSharpTempEntLife_t *pOutLife )
{
	if ( !ValidSlot( nSlot ) || !pOutLife )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[ nSlot ];

	if ( !pTemp )
		return 0;

	pOutLife->Die        = pTemp->die;
	pOutLife->Flags       = pTemp->flags;
	pOutLife->FadeSpeed  = pTemp->fadeSpeed;
	pOutLife->RenderAmt   = pTemp->tempent_renderamt;
	pOutLife->Priority    = pTemp->priority;
	pOutLife->ClientIndex = pTemp->clientIndex;
	pOutLife->RenderMode  = pTemp->m_nRenderMode;
	pOutLife->Alpha       = pTemp->m_clrRender.a;

	return 1;
}

int32_t SourceSharp_TempEntPoolWriteLife( int32_t nSlot, const SourceSharpTempEntLife_t *pLife )
{
	if ( !ValidSlot( nSlot ) || !pLife )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[ nSlot ];

	if ( !pTemp )
		return 0;

	pTemp->die               = pLife->Die;
	pTemp->flags             = pLife->Flags;
	pTemp->fadeSpeed         = pLife->FadeSpeed;
	pTemp->tempent_renderamt = pLife->RenderAmt;
	pTemp->priority          = pLife->Priority;
	pTemp->clientIndex       = (short)pLife->ClientIndex;

	// THROUGH THE SETTERS, not over the members. SetRenderMode is what stock's
	// IsActive calls when a FTENT_FADEOUT tempent flips kRenderNormal to
	// kRenderTransTexture, and SetRenderColorA is what ramps the alpha; both
	// are the shapes tempent.h publishes and a plain assignment here would be
	// this file's own second spelling of them.
	pTemp->SetRenderMode( (RenderMode_t)pLife->RenderMode );
	pTemp->SetRenderColorA( (byte)pLife->Alpha );

	return 1;
}

//-----------------------------------------------------------------------------
// One physics step. C_LocalTempEntity::Frame, whose `false` means the tempent
// asked to die -- an animating sprite that ran off its last frame.
//-----------------------------------------------------------------------------
int32_t SourceSharp_TempEntPoolSimulate( int32_t nSlot, float flFrameTime, int32_t nFrameNumber )
{
	if ( !ValidSlot( nSlot ) )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[ nSlot ];

	if ( !pTemp )
		return 0;

	return pTemp->Frame( flFrameTime, nFrameNumber ) ? 1 : 0;
}

//-----------------------------------------------------------------------------
// The transform C# owns (behaviour ledger row C4).
//
// Read once when the managed pool takes a tempent over, written once per frame
// after it has integrated. Native holds no opinion about where a C#-simulated
// tempent is; it renders what it is told.
//-----------------------------------------------------------------------------
static void VecOut( float *pOut, const Vector &v )
{
	pOut[ 0 ] = v.x; pOut[ 1 ] = v.y; pOut[ 2 ] = v.z;
}

static void AngOut( float *pOut, const QAngle &a )
{
	pOut[ 0 ] = a.x; pOut[ 1 ] = a.y; pOut[ 2 ] = a.z;
}

int32_t SourceSharp_TempEntPoolReadTransform( int32_t nSlot,
                                              SourceSharpTempEntTransform_t *pOut )
{
	if ( !ValidSlot( nSlot ) || !pOut )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[ nSlot ];

	if ( !pTemp )
		return 0;

	VecOut( pOut->OriginXYZ,       pTemp->GetLocalOrigin() );
	AngOut( pOut->AnglesPYR,       pTemp->GetLocalAngles() );
	VecOut( pOut->VelocityXYZ,     pTemp->GetVelocity() );
	VecOut( pOut->AccelerationXYZ, pTemp->GetAcceleration() );
	AngOut( pOut->AngVelocityPYR,  pTemp->m_vecTempEntAngVelocity );

	pOut->flSpriteScale = pTemp->m_flSpriteScale;
	pOut->flFrame       = pTemp->m_flFrame;
	pOut->flSineX       = pTemp->x;
	pOut->flSineY       = pTemp->y;

	return 1;
}

int32_t SourceSharp_TempEntPoolWriteTransform( int32_t nSlot,
                                               const SourceSharpTempEntTransform_t *pIn )
{
	if ( !ValidSlot( nSlot ) || !pIn )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[ nSlot ];

	if ( !pTemp )
		return 0;

	// SetLocal rather than SetAbs, because that is what Frame itself calls: a
	// tempent has no parent so the two agree, and matching stock's call keeps
	// it that way if one ever does.
	pTemp->SetLocalOrigin( Vector( pIn->OriginXYZ[ 0 ], pIn->OriginXYZ[ 1 ], pIn->OriginXYZ[ 2 ] ) );
	pTemp->SetLocalAngles( QAngle( pIn->AnglesPYR[ 0 ], pIn->AnglesPYR[ 1 ], pIn->AnglesPYR[ 2 ] ) );

	// Through the accessors stock already exports, rather than the fields.
	pTemp->SetVelocity( Vector( pIn->VelocityXYZ[ 0 ], pIn->VelocityXYZ[ 1 ], pIn->VelocityXYZ[ 2 ] ) );
	pTemp->SetAcceleration( Vector( pIn->AccelerationXYZ[ 0 ], pIn->AccelerationXYZ[ 1 ], pIn->AccelerationXYZ[ 2 ] ) );
	pTemp->m_vecTempEntAngVelocity = QAngle( pIn->AngVelocityPYR[ 0 ], pIn->AngVelocityPYR[ 1 ], pIn->AngVelocityPYR[ 2 ] );

	pTemp->m_flSpriteScale = pIn->flSpriteScale;
	pTemp->m_flFrame       = pIn->flFrame;
	pTemp->x               = pIn->flSineX;
	pTemp->y               = pIn->flSineY;

	return 1;
}

//-----------------------------------------------------------------------------
// The write-once state C# reads ONCE when it takes a tempent over.
//
// See SourceSharpTempEntStatics_t for why this is a third struct rather than a
// bigger transform, and why it is asked of the OBJECT rather than remembered
// from the managed spawn record: this build still has three NATIVE spawn
// routes -- c_stickybolt.cpp's SpawnTempModel, the pool's own
// cs_cl_te_pool_attach -> AttachTentToPlayer, and sdk_fx_weaponfx.cpp's
// EjectBrass_9mm/12Gauge -> CSEjectBrass -- and none of them writes one.
//-----------------------------------------------------------------------------
int32_t SourceSharp_TempEntPoolReadStatics( int32_t nSlot,
                                            SourceSharpTempEntStatics_t *pOut )
{
	if ( !ValidSlot( nSlot ) || !pOut )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[ nSlot ];

	if ( !pTemp )
		return 0;

	pOut->flBounceFactor    = pTemp->bounceFactor;
	pOut->nHitSound         = pTemp->hitSound;
	pOut->flFrameMax        = pTemp->m_flFrameMax;
	pOut->flFrameRate       = pTemp->m_flFrameRate;

	VecOut( pOut->TentOffsetXYZ, pTemp->tentOffset );

	pOut->nOwnerEntityIndex = pTemp->TempEntOwnerIndex();
	pOut->nFlickerFrame     = pTemp->m_nFlickerFrame;

	// Row C5's one bit. The NAME deliberately does not cross; see the struct.
	pOut->bHasImpactEffect  = ( pTemp->m_pszImpactEffect != NULL ) ? 1 : 0;

	return 1;
}

//-----------------------------------------------------------------------------
// GetWindspeedAtTime -- what FTENT_WINDBLOWN accelerates towards.
//
// Answers the zero vector through the same out parameter when there is no wind
// entity, which is what env_wind_shared.cpp itself does; there is no "no wind"
// return value to distinguish, because a still map and a map with no env_wind
// blow a tempent the same distance.
//-----------------------------------------------------------------------------
void SourceSharp_ClientWindSpeed( float flTime, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	Vector vecWind;
	vecWind.Init();

	GetWindspeedAtTime( flTime, vecWind );

	VecOut( pOutXYZ, vecWind );
}

//-----------------------------------------------------------------------------
// A CLIENT entity's origin, by INDEX -- what FTENT_PLYRATTACHMENT stores.
//
// Answers 0 and writes nothing when the index names no entity, which is stock's
// own behaviour: its branch is `if ( IClientEntity *pClient = ... )` and simply
// does not move the tempent.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientEntityOrigin( int32_t nEntityIndex, float *pOutXYZ )
{
	if ( !pOutXYZ || nEntityIndex < 0 )
		return 0;

	IClientEntity *pClient = SourceSharp_ClientEntityFromIndex( nEntityIndex );

	if ( !pClient )
		return 0;

	const Vector &vecOrigin = pClient->GetAbsOrigin();

	pOutXYZ[ 0 ] = vecOrigin.x;
	pOutXYZ[ 1 ] = vecOrigin.y;
	pOutXYZ[ 2 ] = vecOrigin.z;

	return 1;
}

//-----------------------------------------------------------------------------
// A CLIENT entity's MODEL BOUNDS and its ANGLES, by INDEX. Behaviour ledger
// row C6, and this is the whole of what closing it needed from native.
//
// STOCK'S TWO READS, FROM ONE RESOLVE. CTempEnts::FizzEffect asks
//
//     modelinfo->GetModelBounds( pent->GetModel(), mins, maxs );
//     SinCos( pent->GetLocalAngles()[1]*M_PI/180, &yspeed, &xspeed );
//
// and nothing else about the entity. The bounds are the volume the bubbles are
// spread across; the yaw is the direction the "current" drags them.
//
// GetModelBounds AND NOT GetModelRenderBounds, and not the collideable's box.
// The three are different rectangles: GetModelBounds is what the MODEL occupies
// in its own frame -- for a brush entity the slab the mapper drew, which for a
// brush model is world-space already, which is why stock can hand the result
// straight to TempEntAlloc as a world origin. GetModelRenderBounds is the
// renderer's culling box, and the collideable's is, on C_StandaloneEntity, a
// symmetric +-16 cube fixed in the constructor and never networked. Either
// substitution draws a plausible column of bubbles in the wrong place, and
// nothing anywhere would say so.
//
// GetClientEntity AND NOT GetEnt, and that is the fix rather than a detail.
// c_te_fizz.cpp resolved with stock's cl_entitylist->GetEnt, which answers a
// C_BaseEntity * -- NULL for every entity on the parallel hierarchy -- so every
// fizz in this build arrived and was dropped. GetClientEntity answers an
// IClientEntity for BOTH hierarchies, and GetModel and GetRenderAngles are
// IClientRenderable members that both implement, so there is no branch here and
// no hierarchy this arm can stop answering for.
//
// Answers 0 and writes nothing for an index that names no entity or an entity
// with no model -- stock's `pEnt != NULL` and its `!pent->GetModel()`, which
// are two guards in two functions and one answer here.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientEntityModelBounds( int32_t nEntityIndex,
                                             float *pOutMinsXYZ, float *pOutMaxsXYZ,
                                             float *pOutAnglesPYR )
{
	if ( nEntityIndex < 0 )
		return 0;

	IClientEntity *pClient = SourceSharp_ClientEntityFromIndex( nEntityIndex );

	if ( !pClient )
		return 0;

	const model_t *pModel = pClient->GetModel();

	if ( !pModel )
		return 0;

	if ( pOutMinsXYZ || pOutMaxsXYZ )
	{
		Vector vecMins, vecMaxs;

		modelinfo->GetModelBounds( pModel, vecMins, vecMaxs );

		if ( pOutMinsXYZ )
		{
			pOutMinsXYZ[ 0 ] = vecMins.x;
			pOutMinsXYZ[ 1 ] = vecMins.y;
			pOutMinsXYZ[ 2 ] = vecMins.z;
		}

		if ( pOutMaxsXYZ )
		{
			pOutMaxsXYZ[ 0 ] = vecMaxs.x;
			pOutMaxsXYZ[ 1 ] = vecMaxs.y;
			pOutMaxsXYZ[ 2 ] = vecMaxs.z;
		}
	}

	if ( pOutAnglesPYR )
	{
		// GetRenderAngles is m_angRotation on C_StandaloneEntity, which is
		// stock's GetLocalAngles() exactly; on an SDK entity it is the abs
		// angles, which are the local ones for anything unparented -- and a
		// fizzing volume is unparented.
		const QAngle &ang = pClient->GetRenderAngles();

		pOutAnglesPYR[ 0 ] = ang.x;
		pOutAnglesPYR[ 1 ] = ang.y;
		pOutAnglesPYR[ 2 ] = ang.z;
	}

	return 1;
}

//-----------------------------------------------------------------------------
// CTempEnts::AddVisibleTempEntity: back into the leaf system for this frame.
//-----------------------------------------------------------------------------
int32_t SourceSharp_TempEntPoolAddVisible( int32_t nSlot )
{
	if ( !ValidSlot( nSlot ) )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[ nSlot ];

	if ( !pTemp )
		return 0;

	return ThePool()->PoolAddVisible( pTemp ) ? 1 : 0;
}

//=============================================================================//
// THE INSTRUMENT GATE 3 NEEDED, and the finding that made it necessary.
//
// NOTHING IN THIS BUILD ALLOCATES FROM THE POOL. Measured on a live join, not
// assumed: the file-level callers are all still compiled, but every one of them
// is unreachable in this configuration --
//
//   fx_shelleject.cpp     its three DECLARE_CLIENT_EFFECT callbacks are only
//                         reached by DispatchEffect("ShellEject"), and nothing
//                         dispatches those names. The client's animation events
//                         are managed now, and ClientAnimationEffects.cs says so
//                         in its own words: "EjectBrass lands here too, and that
//                         is the honest state of it: there is no brass temp
//                         entity on this ABI at all".
//   c_baseanimating.cpp   AE_EJECTBRASS, same path, same answer.
//   c_te_breakmodel.cpp   still compiled, but NO MANAGED SENDER exists --
//                         ITempEnts.BreakModel has no caller in
//                         SourceSharp.Server, so TE_BreakModel is never on the
//                         wire.
//   c_te_fizz.cpp         reached, and dropped at the entity lookup: behaviour
//                         ledger row C6.
//
// So gate 3 for this phase cannot be "fire a weapon and watch the shells": the
// shells were already gone before this lane touched anything. Firing four
// func_breakables, exploding the player and emptying an SMG all left the pool
// at zero allocations, on the build BEFORE and AFTER, which proves nothing
// regressed and equally proves nothing about the pool.
//
// CTempEnts::EjectBrass -- the exact entry point fx_shelleject.cpp uses, not a
// private one -- so what it exercises is the real chain end to end: the managed
// reservation and spawn record (ClientTempEntDecisions.EjectBrass), the managed
// create that fills the slot, Prepare, the leaf system, and then every frame
// the managed expiry, the native Frame() with its gravity and its bounce, the
// visibility pass, the FTENT_FADEOUT alpha ramp and the free.
//
// A DIAGNOSTIC AND NOT A SANDBOX STATION. It makes no temp entity, registers no
// class and knows nothing about any effect; it is the pool's own instrument,
// beside the pool's own report (cs_cl_te_pool). Cheat-flagged, like every other
// command in this tree that puts something in the world.
//=============================================================================//
static void TempEntPoolShell_f( const CCommand &args )
{
	const int nCount = ( args.ArgC() > 1 ) ? atoi( args[ 1 ] ) : 1;
	const int nType  = ( args.ArgC() > 2 ) ? atoi( args[ 2 ] ) : 0;

	if ( nCount <= 0 || nType < 0 || nType > 2 )
	{
		Msg( "cs_cl_te_pool_shell [count] [type 0=pistol 1=rifle 2=shotgun]\n" );
		return;
	}

	// The view rather than the player's eyes, and deliberately: no client
	// entity here is a C_BasePlayer (behaviour ledger row C5's cause), so
	// GetLocalPlayer() is not available to ask. MainViewOrigin is what the
	// renderer is drawing from, which is where a shell casing should appear.
	const Vector vecOrigin = MainViewOrigin();
	const QAngle angView   = MainViewAngles();

	for ( int i = 0; i < nCount; i++ )
	{
		tempents->EjectBrass( vecOrigin, angView, angView, nType );
	}

	Msg( "[SourceSharp] cs_cl_te_pool_shell: asked the pool for %d shell(s) of type %d "
	     "at (%.0f %.0f %.0f)\n", nCount, nType, vecOrigin.x, vecOrigin.y, vecOrigin.z );
}

static ConCommand cs_cl_te_pool_shell( "cs_cl_te_pool_shell", TempEntPoolShell_f,
	"Eject brass through CTempEnts::EjectBrass -- the legacy temp entity pool's own entry "
	"point, for verifying the pool end to end. cs_cl_te_pool_shell [count] [type 0|1|2]",
	FCVAR_CHEAT );

//-----------------------------------------------------------------------------
// THE HIGH-PRIORITY, PLAYER-ATTACHED HALF, which the shells above cannot reach.
//
// CTempEnts::EjectBrass allocates at TENTPRIORITY_LOW and sets none of the
// flags the pool's lifetime rules turn on. AttachTentToPlayer is the other
// shape entirely and it is the one CTEKillPlayerAttachments exists for:
// TempEntAllocHigh (so the eviction rule is reachable), FTENT_PLYRATTACHMENT
// (so KillAttachedTents has something to find) and FTENT_PERSIST (so the
// not-visible-so-drop policy has to spare it).
//
// It has no caller in this build -- its callers are HL1's -- so without this it
// could not be exercised at all, and the one temp entity this lane unblocked
// would have a live receiving path and nothing to act on.
//
// THE MODEL IS ASKED FOR BY NAME, not invented: sprites/bubble.vmt is precached
// by the managed server's temp entity precache (SourceSharp.Server/TempEnts.cs)
// and is a sprite, which is what AttachTentToPlayer's animating branch wants.
// A zero index is said out loud rather than passed on -- TempEntAlloc would
// refuse it and the refusal would look like the pool being broken.
//-----------------------------------------------------------------------------
static void TempEntPoolAttach_f( const CCommand &args )
{
	const int nCount  = ( args.ArgC() > 1 ) ? atoi( args[ 1 ] ) : 1;
	const int nPlayer = ( args.ArgC() > 2 ) ? atoi( args[ 2 ] ) : 1;

	if ( nCount <= 0 )
	{
		Msg( "cs_cl_te_pool_attach [count] [player]\n" );
		return;
	}

	const int nModelIndex = modelinfo->GetModelIndex( "sprites/bubble.vmt" );

	if ( nModelIndex <= 0 )
	{
		Warning( "cs_cl_te_pool_attach: sprites/bubble.vmt is not precached on this "
		         "client, so there is no model to attach\n" );
		return;
	}

	const model_t *pModel = modelinfo->GetModel( nModelIndex );

	if ( !pModel )
	{
		Warning( "cs_cl_te_pool_attach: model index %d does not resolve\n", nModelIndex );
		return;
	}

	for ( int i = 0; i < nCount; i++ )
	{
		tempents->AttachTentToPlayer( nPlayer, nModelIndex, 40.0f, 30.0f );
	}

	Msg( "[SourceSharp] cs_cl_te_pool_attach: asked the pool for %d high-priority "
	     "attachment(s) on player %d, model %d\n", nCount, nPlayer, nModelIndex );
}

static ConCommand cs_cl_te_pool_attach( "cs_cl_te_pool_attach", TempEntPoolAttach_f,
	"Attach high-priority, FTENT_PLYRATTACHMENT tempents to a player through "
	"CTempEnts::AttachTentToPlayer -- what CTEKillPlayerAttachments kills. "
	"cs_cl_te_pool_attach [count] [player]",
	FCVAR_CHEAT );

//=============================================================================//
// cs_cl_te_live -- WHERE THE LIVE TEMPENTS ACTUALLY ARE.
//
// LANE `c8-tempents-beams`. INSTRUMENTATION, AND THEREFORE DEBT: its removal
// owner is whoever retires the cs_cl_te_pool_* family, and it goes with them.
//
// WHY A COUNTER IS NOT ENOUGH, which is the whole reason this exists beside
// cs_cl_te_pool's four numbers. The gate clause is "tempents spawn, ATTACH TO A
// MOVING OWNER, and expire", and `allocated` climbs identically whether the
// attachment follows the player or sits at the world origin -- `8f-movetypes`
// is the precedent: a broken roaming observer kept its loop counter growing
// exactly like a working build. Tracking is a GEOMETRY question and only a
// per-tempent position can answer it.
//
// AND IT PRINTS THE OWNER, which is the other half of what this lane changed.
// TempEntOwnerIndex() is bit extraction off the stored handle and
// TempEntOwnerHandle() is the seam's resolve of it -- the two answers the
// collision trace's ignore-subject is made of. A tempent whose owner index is
// -1 has no owner to ignore; one whose index is real and whose handle does NOT
// resolve has a handle pointing at nothing, which is a different fault and
// would otherwise look the same from a counter.
//=============================================================================//
//-----------------------------------------------------------------------------
// cs_cl_te_live bones: do the studio tempents hand the engine a pose?
//
// A studio tempent (a breakmodel gib, a shell casing) is drawn by
// modelrender, and the engine's studio path asks the renderable for its
// bone-to-world matrices through IClientRenderable::SetupBones before it
// transforms a vertex. Stock's C_LocalTempEntity was a C_BaseAnimating and
// answered with its pose; ours is a CShellRenderable, and
// CDefaultClientRenderable::SetupBones answered TRUE AND WROTE NOTHING -- the
// gibs were skinned with the engine's stale bone buffer (screen-sized
// translucent triangles converging on the view). This asks every live studio
// tempent what the engine asks, into a NaN-filled buffer, and counts what came
// back untouched: 11 of 11 model bone matrices before the fix, 0 after.
//-----------------------------------------------------------------------------
static bool MatrixIsUnwritten( const matrix3x4_t &m )
{
	// The sentinel is a NaN in every cell; a written matrix has none.
	for ( int r = 0; r < 3; r++ )
		for ( int c = 0; c < 4; c++ )
			if ( m[r][c] == m[r][c] )
				return false;
	return true;
}

static void TempEntPoolBones()
{
	static matrix3x4_t s_Bones[ MAXSTUDIOBONES ];

	int nTempEnts = 0, nModelBones = 0, nUnwrittenModel = 0, nUnwrittenAny = 0;

	for ( int nSlot = 0; nSlot < CTempEnts::MAX_TEMP_ENTITIES; nSlot++ )
	{
		C_LocalTempEntity *pTemp = g_pTempEntSlots[ nSlot ];
		const model_t *pModel = pTemp ? pTemp->GetModel() : NULL;

		if ( !pModel || modelinfo->GetModelType( pModel ) != mod_studio )
			continue;

		MDLCACHE_CRITICAL_SECTION();
		studiohdr_t *pHdr = modelinfo->GetStudiomodel( pModel );
		if ( !pHdr )
			continue;

		const float flNaN = std::numeric_limits< float >::quiet_NaN();
		for ( int i = 0; i < MAXSTUDIOBONES; i++ )
			for ( int r = 0; r < 3; r++ )
				for ( int c = 0; c < 4; c++ )
					s_Bones[i][r][c] = flNaN;

		if ( !pTemp->SetupBones( s_Bones, MAXSTUDIOBONES, BONE_USED_BY_ANYTHING, gpGlobals->curtime ) )
			continue;

		nTempEnts++;
		nModelBones += pHdr->numbones;

		for ( int i = 0; i < MAXSTUDIOBONES; i++ )
		{
			if ( !MatrixIsUnwritten( s_Bones[i] ) )
				continue;
			nUnwrittenAny++;
			if ( i < pHdr->numbones )
				nUnwrittenModel++;
		}
	}

	Msg( "[SourceSharp] cs_cl_te_live bones: %d studio tempent(s), %d model bone(s), "
	     "%d model bone matrices unwritten, %d of %d slots unwritten\n",
	     nTempEnts, nModelBones, nUnwrittenModel, nUnwrittenAny, nTempEnts * MAXSTUDIOBONES );
}

static void TempEntPoolLive_f( const CCommand &args )
{
	if ( args.ArgC() > 1 && !V_stricmp( args.Arg( 1 ), "bones" ) )
	{
		TempEntPoolBones();
		return;
	}

	int nLive = 0;

	Msg( "[SourceSharp] cs_cl_te_live: slot  origin                    "
	     "client  owner  ownerOK  flags     die-in\n" );

	for ( int i = 0; i < CTempEnts::MAX_TEMP_ENTITIES; i++ )
	{
		C_LocalTempEntity *pTemp = g_pTempEntSlots[ i ];

		if ( !pTemp )
			continue;

		nLive++;

		const Vector &vecOrigin = pTemp->GetAbsOrigin();

		Msg( "[SourceSharp]   %4d  (%8.1f %8.1f %8.1f)  %6d  %5d  %7s  %08x  %6.2f\n",
		     i, vecOrigin.x, vecOrigin.y, vecOrigin.z,
		     (int)pTemp->clientIndex,
		     pTemp->TempEntOwnerIndex(),
		     pTemp->TempEntOwnerHandle() ? "yes" : "no",
		     (unsigned int)pTemp->flags,
		     pTemp->die - gpGlobals->curtime );
	}

	Msg( "[SourceSharp] cs_cl_te_live: %d live of %d\n",
	     nLive, (int)CTempEnts::MAX_TEMP_ENTITIES );

	// WHAT AN OWNER INDEX WOULD RESOLVE TO, asked of the two lookups by name.
	//
	// THIS IS THE DEFECT AND THE FIX SIDE BY SIDE. The spawn used to store the
	// SDK-only arm's answer, which is NULL for every managed entity, so the
	// stored handle was INVALID and the collision trace ignored nothing. It now
	// stores the seam's both-hierarchies handle. On a live client with managed
	// entities the two answers DIFFER, and that difference is the capability.
	if ( args.ArgC() > 1 )
	{
		const int nEntity = atoi( args[ 1 ] );

		IGameEntity      *pSdk    = SourceSharp_ClientSdkEntityFromIndex( nEntity );
		const CBaseHandle  hSeam   = SourceSharp_ClientHandleFromIndex( nEntity );
		IClientUnknown    *pSeamed = SourceSharp_ClientUnknownFromHandle( hSeam );

		Msg( "[SourceSharp] cs_cl_te_live: owner lookup for entity %d\n"
		     "  SDK-only arm (what the spawn USED to store) : %s\n"
		     "  seam handle  (what the spawn stores now)    : %s, entry %d\n"
		     "  seam handle resolves to an IClientUnknown   : %s\n",
		     nEntity,
		     pSdk ? "a C_BaseEntity" : "NULL -- no owner, nothing ignored",
		     hSeam.IsValid() ? "VALID" : "INVALID",
		     hSeam.IsValid() ? hSeam.GetEntryIndex() : -1,
		     pSeamed ? "yes" : "no" );
	}
}

static ConCommand cs_cl_te_live( "cs_cl_te_live", TempEntPoolLive_f,
	"Every live tempent's POSITION, its attachment client, its owner index and whether "
	"that owner still resolves -- the geometry a pool counter cannot show. "
	"cs_cl_te_live [entity], where the entity prints what an owner index resolves to "
	"through each of the two lookups. cs_cl_te_live bones counts the bone matrices the "
	"live studio tempents leave unwritten when asked as the engine's draw asks (0 is right).",
	FCVAR_CHEAT );

//=============================================================================//
// C-PHASE 5 -- WHAT A MANAGED SPAWN HELPER NEEDS.
//
// The six slots above are the managed pool's hands on a tempent NATIVE made.
// These four are the other direction of the same pool: c_te_legacytempents.cpp
// has seventeen spawn helpers that allocate a tempent and then write fifteen to
// twenty of its fields, and those helpers -- not the pool -- are what ten
// networked temp entity classes were still waiting on.
// docs/te-legacy-pool-survey.md section 6 settled all four before this lane
// wrote a line of them.
//
// WHY NOT ONE SLOT PER HELPER. Seventeen slots that each set the same twenty
// fields in a different order is seventeen places for a field to be forgotten,
// and each one would have to be tested against the stock body it came from.
// Two slots -- construct, then write the whole record -- put the seventeen
// bodies in C#, where they are testable without a game running, and leave
// exactly one native path for a field to travel down.
//=============================================================================//

// Defined in c_te_legacytempents.cpp, where g_BreakableHelper and
// BreakModelDrawHelper are file-static. See SourceSharpTempEntSpawn_t::BreakHelper.
void SourceSharpTePool_JoinBreakGroup( C_LocalTempEntity *pTemp, bool bSlave );

//-----------------------------------------------------------------------------
// The private no-argument allocator, reachable from outside this class.
//
// A member for the reason PoolFreeObject and PoolAddVisible are: CTempEnts
// declares TempEntAlloc() private, and a friend declaration in a stock header
// would name a SourceSharp translation unit that the stock client does not
// compile.
//-----------------------------------------------------------------------------
C_LocalTempEntity *CTempEnts::PoolCreateObject()
{
	return TempEntAlloc();
}

//-----------------------------------------------------------------------------
// Construct into a slot the managed pool has already reserved.
//
// Stock CTempEnts::TempEntAlloc( org, model ), from the memory-pool allocation
// down, with the list add left out because the list is C#'s and has already
// happened -- that is what the slot IS.
//
// THE ORDER IS STOCK'S AND MATTERS. Prepare() runs before the origin is set,
// because Prepare itself does SetAbsOrigin( vec3_origin ), and it runs before
// AddToLeafSystem because the leaf system is told where the renderable is.
// Getting them the other way round is a gib that draws at the world origin for
// one frame, which on a dedicated listen server looks like a flicker and on a
// dark map looks like nothing at all.
//
// THE -tools RECORDING BLOCK IS NOT CARRIED, and stock's own comment is why:
// it is `#ifdef _DEBUG` around a Msg and a COMMENTED-OUT
// AddNonNetworkableEntity, whose comment says recording tempents as entities
// crashes. There is no behaviour there to lose.
//-----------------------------------------------------------------------------
int32_t SourceSharpTePool_CreateWithModel( int32_t nSlot, const float *pOriginXYZ,
                                           const model_t *pModel, int32_t nPriority );

int32_t SourceSharp_TempEntPoolCreate( int32_t nSlot, const float *pOriginXYZ,
                                       int32_t nModelIndex, int32_t nPriority )
{
	const model_t *pModel = ( nModelIndex != 0 ) ? modelinfo->GetModel( nModelIndex ) : NULL;
	return SourceSharpTePool_CreateWithModel( nSlot, pOriginXYZ, pModel, nPriority );
}

int32_t SourceSharpTePool_CreateWithModel( int32_t nSlot, const float *pOriginXYZ,
                                           const model_t *pModel, int32_t nPriority )
{
	if ( !ValidSlot( nSlot ) || !pOriginXYZ )
		return 0;

	// An occupied slot means the managed free list handed out one it had not
	// taken back. Refused rather than overwritten: the caller still holds the
	// slot, and a second object on the same row would leak the first — no
	// other path would ever find it to tear it down.
	if ( g_pTempEntSlots[ nSlot ] != NULL )
	{
		Warning( "[SourceSharp] TempEntPoolCreate: slot %d is already occupied; "
		         "the managed pool handed out a slot it had not freed\n", nSlot );
		return 0;
	}

	// stock's `if ( !model )`, moved to the index that stands for it. A managed
	// helper checks its own model index first (every stock helper opens with
	// exactly that test), so reaching here with a bad one is a helper that
	// did not, and it is worth one line rather than a silent nothing.
	if ( !pModel )
	{
		DevWarning( 1, "Can't create temporary entity with NULL model!\n" );
		return 0;
	}

	C_LocalTempEntity *pTemp = ThePool()->PoolCreateObject();

	if ( !pTemp )
	{
		// The memory pool refused where the managed list did not. The slot is
		// LEFT EMPTY rather than bound to anything, which is the state the
		// managed pool already drops on its next pass -- the same recovery a
		// reservation that never got an object takes.
		DevWarning( 1, "Overflow %d temporary ents!\n", (int)CTempEnts::MAX_TEMP_ENTITIES );
		return 0;
	}

	g_pTempEntSlots[ nSlot ] = pTemp;

	pTemp->Prepare( pModel, gpGlobals->curtime );

	pTemp->priority = nPriority;
	pTemp->SetAbsOrigin( Vector( pOriginXYZ[ 0 ], pOriginXYZ[ 1 ], pOriginXYZ[ 2 ] ) );

	pTemp->m_RenderGroup = RENDER_GROUP_OTHER;
	pTemp->AddToLeafSystem( pTemp->m_RenderGroup );

	return 1;
}

//-----------------------------------------------------------------------------
// A whole spawn record into the object.
//
// EVERY FIELD, UNCONDITIONALLY, and SourceSharpTempEntSpawn_t says why: the
// object is in stock's post-Prepare state when this runs, C# builds the record
// from a managed mirror of Prepare, and a partial write would let a helper
// inherit whatever the previous tenant of a recycled slot left in a field it
// did not think about.
//
// THROUGH THE SETTERS where tempent.h publishes one, for the reason
// TempEntPoolWriteLife gives: SetRenderMode, SetRenderColor and SetAbsAngles
// are the shapes that class publishes, and a plain assignment here would be
// this file's own second spelling of them.
//-----------------------------------------------------------------------------
int32_t SourceSharp_TempEntPoolWriteSpawn( int32_t nSlot, const SourceSharpTempEntSpawn_t *pSpawn )
{
	if ( !ValidSlot( nSlot ) || !pSpawn )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[ nSlot ];

	if ( !pTemp )
		return 0;

	pTemp->flags             = pSpawn->Flags;
	pTemp->die               = pSpawn->Die;

	pTemp->m_flSpriteScale   = pSpawn->SpriteScale;
	pTemp->m_flFrame         = pSpawn->Frame;
	pTemp->m_flFrameMax      = pSpawn->FrameMax;
	pTemp->m_flFrameRate     = pSpawn->FrameRate;

	pTemp->m_nBody           = pSpawn->Body;
	pTemp->m_nSkin           = pSpawn->Skin;

	pTemp->SetRenderMode( (RenderMode_t)pSpawn->RenderMode );
	pTemp->m_nRenderFX       = pSpawn->RenderFx;
	pTemp->tempent_renderamt = pSpawn->RenderAmt;
	pTemp->fadeSpeed         = pSpawn->FadeSpeed;
	pTemp->SetRenderColor( (byte)pSpawn->ColorR, (byte)pSpawn->ColorG,
	                       (byte)pSpawn->ColorB, (byte)pSpawn->ColorA );

	pTemp->SetVelocity( Vector( pSpawn->VelocityXYZ[ 0 ], pSpawn->VelocityXYZ[ 1 ], pSpawn->VelocityXYZ[ 2 ] ) );
	pTemp->SetAcceleration( Vector( pSpawn->AccelerationXYZ[ 0 ], pSpawn->AccelerationXYZ[ 1 ], pSpawn->AccelerationXYZ[ 2 ] ) );
	pTemp->m_vecTempEntAngVelocity = QAngle( pSpawn->AngularVelocityPYR[ 0 ],
	                                         pSpawn->AngularVelocityPYR[ 1 ],
	                                         pSpawn->AngularVelocityPYR[ 2 ] );
	pTemp->SetAbsAngles( QAngle( pSpawn->AnglesPYR[ 0 ], pSpawn->AnglesPYR[ 1 ], pSpawn->AnglesPYR[ 2 ] ) );
	pTemp->bounceFactor      = pSpawn->BounceFactor;

	pTemp->x                 = pSpawn->SineX;
	pTemp->y                 = pSpawn->SineY;

	pTemp->m_vecNormal       = Vector( pSpawn->NormalXYZ[ 0 ], pSpawn->NormalXYZ[ 1 ], pSpawn->NormalXYZ[ 2 ] );

	pTemp->hitSound          = pSpawn->HitSound;
	pTemp->clientIndex       = (short)pSpawn->ClientIndex;
	pTemp->tentOffset        = Vector( pSpawn->TentOffsetXYZ[ 0 ], pSpawn->TentOffsetXYZ[ 1 ], pSpawn->TentOffsetXYZ[ 2 ] );

	// The two that are not a field write.

	if ( pSpawn->BreakHelper != 0 )
		SourceSharpTePool_JoinBreakGroup( pTemp, pSpawn->BreakHelper == 2 );

	// THE OWNER, AND IT IS NO LONGER AN SDK-ONLY QUESTION. Lane
	// `c8-tempents-beams`.
	//
	// The owner handle is what Frame()'s collision arm hands its trace filter
	// as the entity to IGNORE (c_te_legacytempents.cpp:958). An INDEX arrives,
	// never a handle -- reading a client handle back through CBaseHandle::Get()
	// is behaviour ledger row C8, and it killed a live client -- so the
	// direction is index-to-handle and stays that way.
	//
	// WHAT CHANGED IS WHICH LOOKUP DOES IT. This used to call
	// SourceSharp_ClientSdkEntityFromIndex, whose own header says it "answers
	// NULL for every managed entity - and that is the answer, not a gap",
	// and hand the result to SetTempEntOwner( C_BaseEntity * ). In a build
	// whose players and props are ALL managed that resolved to NULL every
	// time, so no tempent has ever had an owner to ignore and every
	// FTENT_COLLIDEALL tempent could collide with whoever spawned it on its
	// first frame. SourceSharp_ClientHandleFromIndex is the seam's
	// BOTH-hierarchies answer -- the slot table first, the legacy list second
	// -- and SetTempEntOwnerHandle stores it without naming an entity type at
	// all, which is also what lets this call survive the flip.
	//
	// A NEGATIVE INDEX IS PASSED ON AS "NO OWNER" RATHER THAN SKIPPED: stock's
	// ClientProjectile calls SetTempEntOwner( pOwner ) with whatever it was
	// given, NULL included, and the INVALID handle it stores is what tells the
	// collision arm there is nothing to ignore. A default-constructed
	// CBaseHandle is that invalid handle (basehandle.h:87-90) and NOT a handle
	// to entry 0.
	if ( pSpawn->OwnerEntityIndex >= 0 )
		pTemp->SetTempEntOwnerHandle( SourceSharp_ClientHandleFromIndex( pSpawn->OwnerEntityIndex ) );
	else
		pTemp->SetTempEntOwnerHandle( CBaseHandle() );

	return 1;
}


//=============================================================================//
// THE MUZZLE FLASHES' ENGINE READS (lane c2-fx). Every muzzle-flash decision
// is MuzzleFlashes.cs's, and since lane c5-particles its emitters are managed
// too (PoolEmitters, on the managed executors). What is here is what a flash
// reads from the engine: an attachment, the entity index behind a handle, the
// elight, client.so's rand(), and whether the muzzle materials were cached.
//=============================================================================//
static CBaseHandle FxHandle( uint32_t hEntity )
{
	return CBaseHandle( (int)( hEntity & ENT_ENTRY_MASK ), (int)( hEntity >> NUM_SERIAL_NUM_SHIFT_BITS ) );
}

static void PutVectorAngles( float *pOut6, const Vector &v, const QAngle &a )
{
	pOut6[0] = v.x; pOut6[1] = v.y; pOut6[2] = v.z;
	pOut6[3] = a.x; pOut6[4] = a.y; pOut6[5] = a.z;
}

int32_t SourceSharp_FxRenderableAttachment( uint32_t hEntity, int32_t nAttachment, float *pOut6 )
{
	IClientRenderable *pRenderable = SourceSharp_ClientRenderableFromHandle( FxHandle( hEntity ) );
	if ( !pRenderable )
		return 0;
	Vector origin( 0, 0, 0 );
	QAngle angles( 0, 0, 0 );
	pRenderable->GetAttachment( nAttachment, origin, angles );
	PutVectorAngles( pOut6, origin, angles );
	return 1;
}

int32_t SourceSharp_FxHandleEntityIndex( uint32_t hEntity )
{
	IClientEntity *pEnt = SourceSharp_ClientEntityFromHandle( FxHandle( hEntity ) );
	return pEnt ? pEnt->entindex() : -1;
}

void SourceSharp_FxElight( int32_t nKey, const float *pOriginXYZ, int32_t r, int32_t g, int32_t b, int32_t nExponent,
                           float flRadius, float flDecay, float flDie )
{
	dlight_t *el = effects->CL_AllocElight( nKey );
	if ( !el )
		return;
	el->origin.Init( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );
	el->color.r = (byte)r;
	el->color.g = (byte)g;
	el->color.b = (byte)b;
	el->color.exponent = (signed char)nExponent;
	el->radius = flRadius;
	el->decay  = flDecay;
	el->die    = flDie;
}

void SourceSharp_FxVectorRandom( float flLo, float flHi, float *pOutXYZ )
{
	Vector v;
	v.Random( flLo, flHi );
	pOutXYZ[0] = v.x; pOutXYZ[1] = v.y; pOutXYZ[2] = v.z;
}

int32_t SourceSharp_FxMaterialsCached( void )
{
	return ( g_Mat_SMG_Muzzleflash[0] ? 1 : 0 ) | ( g_Mat_Combine_Muzzleflash[0] ? 2 : 0 );
}

// The tempents' bounce sounds: a script's parameters (volume, pitch, pitch
// low/high, level, channel; the wave it resolved), and the play.
int32_t SourceSharp_FxSoundParameters( const char *pScript, float *pOut6, char *pWave, int32_t nWaveBytes )
{
	CSoundParameters params;
	if ( !pScript || !GameSound_GetParameters( pScript, params, NULL ) )
		return 0;
	pOut6[0] = params.volume;
	pOut6[1] = (float)params.pitch;
	pOut6[2] = (float)params.pitchlow;
	pOut6[3] = (float)params.pitchhigh;
	pOut6[4] = (float)params.soundlevel;
	pOut6[5] = (float)params.channel;
	if ( pWave && nWaveBytes > 0 )
		Q_strncpy( pWave, params.soundname, nWaveBytes );
	return 1;
}

void SourceSharp_FxEmitWorldSound( const char *pWave, int32_t nChannel, float flVolume, int32_t nSoundLevel, int32_t nPitch,
                                   const float *pOriginXYZ )
{
	if ( !pWave || !pOriginXYZ )
		return;
	const Vector origin( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );
	CLocalPlayerFilter filter;
	EmitSound_t ep;
	ep.m_nChannel = nChannel;
	ep.m_pSoundName = pWave;
	ep.m_flVolume = flVolume;
	ep.m_SoundLevel = (soundlevel_t)nSoundLevel;
	ep.m_nPitch = nPitch;
	ep.m_pOrigin = &origin;
	GameSound_Emit( filter, SOUND_FROM_WORLD, ep );
}

// A tempent of a model the engine loads by name (the shell casings), made in a
// slot the managed pool reserved.
int32_t SourceSharp_FxTempEntCreateNamed( int32_t nSlot, const float *pOriginXYZ, const char *pModel, int32_t nPriority )
{
	const model_t *pLoaded = pModel ? engine->LoadModel( pModel ) : NULL;
	if ( !pLoaded )
		return 0;
	return SourceSharpTePool_CreateWithModel( nSlot, pOriginXYZ, pLoaded, nPriority );
}

void SourceSharp_FxLoadModel( const char *pModel )
{
	if ( pModel )
		engine->LoadModel( pModel );
}

// modelinfo->GetModelFrameCount of engine->LoadModel( name ): what a named
// tempent's frame count is (CTempEnts::RocketFlare's), or -1 for no model.
int32_t SourceSharp_FxModelFrameCount( const char *pModel )
{
	const model_t *pLoaded = pModel ? engine->LoadModel( pModel ) : NULL;
	return pLoaded ? modelinfo->GetModelFrameCount( pLoaded ) : -1;
}

// The slot a tempent is in, and the tempent in a slot (the breakmodel draw's
// lighting origin, asked of managed by slot).
int SourceSharpTePool_SlotOf( const C_LocalTempEntity *pTemp )
{
	for ( int i = 0; i < CTempEnts::MAX_TEMP_ENTITIES; i++ )
	{
		if ( pTemp && g_pTempEntSlots[ i ] == pTemp )
			return i;
	}
	return -1;
}

C_LocalTempEntity *SourceSharpTePool_AtSlot( int nSlot )
{
	return ValidSlot( nSlot ) ? g_pTempEntSlots[ nSlot ] : NULL;
}
