//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//===== SourceSharp — the server utility mechanism that names no entity ======//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// plan_entity.md Phase 4 exit, lane p4-util. `game/server/util.cpp` is one of
// SCC 11's 31 translation units, and these families left it for the shim.
//
// WHAT MADE EACH ONE MOVABLE, since 4e measured that ten of util.cpp's
// nineteen cross-edges name a `CBaseEntity`, a `CBasePlayer` or the stock
// entity factory in their SIGNATURE and so cannot: every family below reaches
// the ENGINE and the C runtime and nothing else. None of them takes, returns,
// stores or dereferences an entity of either hierarchy.
//
// `UTIL_IsCommandIssuedByServerAdmin` is DELIBERATELY NOT HERE, though it fits
// every sentence above. Lane `phase4-exit` has already relocated it to
// `sourcesharp/game/server/shell_commandclient.cpp`, which is the better home:
// that file already defines `UTIL_GetCommandClientIndex`, the one symbol the
// function's body needs. Two lanes moving one body to two addresses is a
// duplicate definition, so this lane left stock's copy exactly where it is and
// says so rather than racing for it.
//
// TWO OF THEM MOVED WHOLE RATHER THAN AS THE ONE SYMBOL THE SHIM NAMED,
// because the part the shim names is not the part that owns the state:
//
//   * `g_NullSurface` is stock's file static and `UTIL_SetTrace` reads it too.
//     `UTIL_SetTrace` could NOT come: it calls `CBaseEntity::Instance`, which
//     is not a cross-edge today, so moving it would ADD one. So the object is
//     defined here with external linkage and util.cpp declares it `extern` —
//     one object, not two. A second file static of the same name would be
//     neither a compile error nor a link error; it would be a second surface,
//     and the two would drift.
//
//   * The CHECK-CLIENT SUBSYSTEM came as a unit: `CCheckClient` and its
//     `g_CheckClient`, the accessor `shell_serversystems.cpp` registers it by,
//     `UTIL_GetNewCheckClient`, `UTIL_GetCurrentCheckClient`,
//     `UTIL_SetClientVisibilityPVS`, `sv_strict_notarget`,
//     `UTIL_FindClientInPVSGuts` and `UTIL_FindClientInPVS`. Every one of the
//     last six reads `g_CheckClient`'s fields directly, and three of them are
//     file statics no other TU can name. The shim named exactly two of the
//     eight; taking those two would have exported the state instead.
//
// `GetContainingEntity` inside that subsystem is a weak inline emitted per TU
// (`nm` reads it `W` in nine objects), not a cross-edge, and the
// `#ifndef SS_CBASEENTITY_ALIAS` arms it sits inside came across verbatim —
// each one is stock's behaviour today, not a future-proofing.
//
// Every declaration stays in `game/server/util.h`, so not one caller changed.
// This TU is SERVER-ONLY, because util.cpp is: `client_csharp.vpc` has no row
// for either, and `ServerUtilLibrary.TheClientVpcDoesNotCompileIt` asserts that
// direction rather than assuming it.
//
//=============================================================================//

#include <stdarg.h>
#include "ss_util.h"   // the util.h helpers, and engine / enginetrace / CTraceFilter
#include "ss_shareddefs.h"   // ss::kLanguageGerman, ss::BLOOD_COLOR_RED (lane R)
// CServerSystem, the base CCheckClient derives from and the type its accessor
// returns. util.cpp includes this first for the same reason.
#include "shell_serversystem.h"
// EntityPrecache_IsAllowed(), which UTIL_PrecacheDecal asks before warning.
// Header-inline (4e moved it here from baseentity_shared.cpp), so no edge.
#include "entity_shared_statics.h"
#include "bspfile.h"
#include "mathlib/mathlib.h"
#include "vstdlib/random.h"
// ss::Effects(), for UTIL_Smoke (lane SP's ss_effects.h).
#include "ss_effects.h"
// The emitter block below: recipient filters (CPVSFilter/CPASFilter), the temp
// entity forwarders, the world subject the impact arm asks first, and the world
// adapter that answers for it.
#include "ss_recipient_filter.h"
#include "shell_tempents.h"
#include "ss_world.h"
#include "world_subject.h"
#include "collisionutils.h"

#include "managed_convar_ref.h"
#include "ss_string.h"

extern CGlobalVars *gpGlobals;   // shell_engineboundary.cpp


//------------------------------------------------------------------------------
// DBG_AssertFunction
//------------------------------------------------------------------------------
#ifdef	DEBUG
#endif	// DEBUG

//------------------------------------------------------------------------------
// UTIL_PrecacheDecal
//------------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
int UTIL_PrecacheDecal( const char *name, bool preload )
{
	// If this is out of order, make sure to warn.
	if ( !EntityPrecache_IsAllowed() )
	{
		if ( !engine->IsDecalPrecached( name ) )
		{
			Assert( !"UTIL_PrecacheDecal:  too late" );

			Warning( "Late precache of %s\n", name );
		}
	}

	return engine->PrecacheDecal( name, preload );
}

//------------------------------------------------------------------------------
// g_NullSurface
//------------------------------------------------------------------------------
// So we always return a valid surface
// EXTERNAL linkage, unlike stock's: util.cpp's UTIL_SetTrace still reads it
// and could not travel (it calls CBaseEntity::Instance, which is not a
// cross-edge today, so moving it would ADD one). One object, declared extern
// over there — never a second copy.
csurface_t	g_NullSurface = { "**empty**", 0 };

//------------------------------------------------------------------------------
// UTIL_ClearTrace
//------------------------------------------------------------------------------
void UTIL_ClearTrace( trace_t &trace )
{
	memset( &trace, 0, sizeof(trace));
	trace.fraction = 1.f;
	trace.fractionleftsolid = 0;
	trace.surface = g_NullSurface;
}

//------------------------------------------------------------------------------
// UTIL_SnapDirectionToAxis
//------------------------------------------------------------------------------
// snaps a vector to the nearest axis vector (if within epsilon)
void UTIL_SnapDirectionToAxis( Vector &direction, float epsilon )
{
	float proj = 1 - epsilon;
	for ( int i = 0; i < 3; i ++ )
	{
		if ( fabs(direction[i]) > proj )
		{
			// snap to axis unit vector
			if ( direction[i] < 0 )
				direction[i] = -1.0f;
			else
				direction[i] = 1.0f;
			direction[(i+1)%3] = 0;
			direction[(i+2)%3] = 0;
			return;
		}
	}
}

//------------------------------------------------------------------------------
// UTIL_VarArgs
//------------------------------------------------------------------------------
const char *UTIL_VarArgs( const char *format, ... )
{
	va_list		argptr;
	static char		string[1024];
	
	va_start (argptr, format);
	ss::FormatV(string, sizeof(string), format,argptr);
	va_end (argptr);

	return string;	
}

//------------------------------------------------------------------------------
// the check-client subsystem
//------------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Purpose: Helper for UTIL_FindClientInPVS
// Input  : check - last checked client
// Output : static int UTIL_GetNewCheckClient
//-----------------------------------------------------------------------------
// FIXME:  include bspfile.h here?
class CCheckClient : public CServerSystem
{
public:
	CCheckClient( char const *name ) : CServerSystem( name )
	{
	}

	void LevelInitPreEntity()
	{
		m_checkCluster = -1;
		m_lastcheck = 1;
		m_lastchecktime = -1;
		m_bClientPVSIsExpanded = false;
	}

	byte	m_checkPVS[MAX_MAP_LEAFS/8];
	byte	m_checkVisibilityPVS[MAX_MAP_LEAFS/8];
	int		m_checkCluster;
	int		m_lastcheck;
	float	m_lastchecktime;
	bool	m_bClientPVSIsExpanded;
};

CCheckClient g_CheckClient( "CCheckClient" );

//-----------------------------------------------------------------------------
// plan_gamedll.md Part 3a / Phase 7 -- one accessor, so the shell's named list
// `kServerSystems[]` (sourcesharp/game/server/shell_serversystems.cpp) can name
// this system instead of relying on the CAutoGameSystem constructor's append,
// which is link order.
//
// The object above has INTERNAL LINKAGE, so this function is the only way to
// reach it from another translation unit. Nothing else changes: it still
// registers itself, it is simply no longer the registration that decides when
// it runs.
//-----------------------------------------------------------------------------
CServerSystem *CheckClientGameSystem()
{
	return &g_CheckClient;
}



static int UTIL_GetNewCheckClient( int check )
{
	int		i;
	edict_t	*ent;

// cycle to the next one

	if (check < 1)
		check = 1;
	if (check > gpGlobals->maxClients)
		check = gpGlobals->maxClients;

	if (check == gpGlobals->maxClients)
		i = 1;
	else
		i = check + 1;

	for ( ;  ; i++)
	{
		if ( i > gpGlobals->maxClients )
		{
			i = 1;
		}

		ent = engine->PEntityOfEntIndex( i );
		if ( !ent )
			continue;

		// Looped but didn't find anything else
		if ( i == check )
			break;	

		if ( !ent->GetUnknown() )
			continue;

		// EVERY CANDIDATE IS SKIPPED, and was before this line changed.
		// GetContainingEntity ANSWERED A CONSTANT NULL in this build: it is
	// SourceSharp_SDKEntityFromUnknown< IServerEntity >, which bottoms out in
	// IServerUnknown::GetBaseEntity(), and every implementation of that in
	// server.so returns NULL (the close read all three out of the binary). The
	// function is gone; the NULL it answered is written down instead.
		// GetFlags never crosses (THE TWELVE) either, so the "is this a client"
		// test that used to follow had nothing left to ask.
		continue;
	}

	if ( i != check )
	{
		memset( g_CheckClient.m_checkVisibilityPVS, 0, sizeof(g_CheckClient.m_checkVisibilityPVS) );
		g_CheckClient.m_bClientPVSIsExpanded = false;
	}

	if ( ent )
	{
		// igameentity.h answers EyePosition by name: "the SDK sites that remain
		// resolve a player EDICT through GetContainingEntity, which answers NULL here
		// -- so they are already inert for this hierarchy rather than waiting on
		// anything". This is one of those sites, verbatim. Armed the block is dead by
		// its own `!pce` return, and falling out of it reaches the same `return i`.
	}
	
	return i;
}


//-----------------------------------------------------------------------------
// Gets the current check client....
//-----------------------------------------------------------------------------
static edict_t *UTIL_GetCurrentCheckClient()
{
	edict_t	*ent;

	// find a new check if on a new frame
	float delta = gpGlobals->curtime - g_CheckClient.m_lastchecktime;
	if ( delta >= 0.1 || delta < 0 )
	{
		g_CheckClient.m_lastcheck = UTIL_GetNewCheckClient( g_CheckClient.m_lastcheck );
		g_CheckClient.m_lastchecktime = gpGlobals->curtime;
	}

	// return check if it might be visible	
	ent = engine->PEntityOfEntIndex( g_CheckClient.m_lastcheck );

	// Allow dead clients -- JAY
	// Our monsters know the difference, and this function gates alot of behavior
	// It's annoying to die and see monsters stop thinking because you're no longer
	// "in" their PVS
	if ( !ent || ent->IsFree() || !ent->GetUnknown())
	{
		return NULL;
	}

	return ent;
}

void UTIL_SetClientVisibilityPVS( edict_t *pClient, const unsigned char *pvs, int pvssize )
{
	if ( pClient == UTIL_GetCurrentCheckClient() )
	{
		Assert( pvssize <= sizeof(g_CheckClient.m_checkVisibilityPVS) );

		g_CheckClient.m_bClientPVSIsExpanded = false;

		unsigned *pFrom = (unsigned *)pvs;
		unsigned *pMask = (unsigned *)g_CheckClient.m_checkPVS;
		unsigned *pTo = (unsigned *)g_CheckClient.m_checkVisibilityPVS;

		int limit = pvssize / 4;
		int i;

		for ( i = 0; i < limit; i++ )
		{
			pTo[i] = pFrom[i] & ~pMask[i];

			if ( pFrom[i] )
			{
				g_CheckClient.m_bClientPVSIsExpanded = true;
			}
		}

		int remainder = pvssize % 4;
		for ( i = 0; i < remainder; i++ )
		{
			((unsigned char *)&pTo[limit])[i] = ((unsigned char *)&pFrom[limit])[i] & !((unsigned char *)&pMask[limit])[i];

			if ( ((unsigned char *)&pFrom[limit])[i] != 0)
			{
				g_CheckClient.m_bClientPVSIsExpanded = true;
			}
		}
	}
}

// UTIL_ClientPVSIsExpanded and the box overload of UTIL_FindClientInPVS were
// here; deleted. Every caller of both was a file this build has never
// compiled -- ai_basenpc.cpp, npc_antliongrub.cpp and triggers.cpp -- and the
// box overload was one more ask on the pointer that is becoming IGameEntity.
// UTIL_SetClientVisibilityPVS still writes m_bClientPVSIsExpanded and
// m_checkVisibilityPVS below; the fields keep the visibility bookkeeping
// coherent for the edict-flavoured UTIL_FindClientInPVS, which keeps its one
// live caller (ragdoll_shared.cpp's fade check).

//-----------------------------------------------------------------------------
// Purpose: Returns a client (or object that has a client enemy) that would be a valid target.
//  If there are more than one valid options, they are cycled each frame
//  If (self.origin + self.viewofs) is not in the PVS of the current target, it is not returned at all.
// Input  : *pEdict - 
// Output : edict_t*
//-----------------------------------------------------------------------------
static CManagedConVarRef sv_strict_notarget( "sv_strict_notarget" );

//-----------------------------------------------------------------------------
// Purpose: Returns a client that could see the entity directly
//-----------------------------------------------------------------------------

//------------------------------------------------------------------------------
// UTIL_Smoke — moved for CYCLE MEMBERSHIP, not for the census.
//
// It was never a shim->stock cross-edge; it was game/shared/util_shared.cpp's
// ONE remaining sole-definer debt to game/server/util.cpp, and while that edge
// stood the two were locked together inside SCC 11. g_sModelIndexSmoke is
// combatweapon.cpp's and is reached here by the same extern declaration
// util.cpp used.
//------------------------------------------------------------------------------

extern short g_sModelIndexSmoke;

void UTIL_Smoke( const Vector &origin, const float scale, const float framerate )
{
	ss::Effects()->Smoke( origin, g_sModelIndexSmoke, scale, framerate );
}

//------------------------------------------------------------------------------
// THE THREE TEMP-ENTITY EMITTERS, and the two helpers they could not leave
// behind. Out of game/server/util.cpp.
//
// 4e listed these three as "temp-entity emitters; see the note under
// util_shared.cpp" and did not move them, because at that point moving one
// CONVERTED an edge instead of removing one. What changed is the two helpers:
// UTIL_BubbleTrail's water surface and UTIL_ImpactTrace's validity check are
// both in util.cpp, and taking the emitters WITHOUT them just moves the edge
// next door. Taking all five, plus UTIL_Bubbles which shares the bubble model
// index, leaves nothing next door to land on.
//
// EVERY REMAINING NAME IN THESE BODIES IS ALREADY THE SHIM'S, checked with nm
// over the object table rather than assumed:
//     UTIL_ShouldShowBlood        util_shared_library.o   (7f)
//     TE_BloodStream, TE_Bubbles  shell_te_fwd_d.o
//     SourceSharpTe_SuppressFilter shell_tempents.o
//     SourceSharp_IsTheWorld      world_subject_dll.o
//     GetWorldEntity, CWorld::ImpactTrace  world_adapter.o
//     TraceEntity                 a weak inline, emitted in ten objects
// and CBaseEntity::ImpactTrace is a VIRTUAL CALL, which emits no symbol at all.
//
// THE ONE COST, named rather than hidden: `g_Language` is defined in
// game/shared/gamerules.cpp, so UTIL_BloodStream's German-blood arm becomes a
// shim->stock edge into a file that had none. That arm is stock's behaviour and
// is LIVE, so it was not deleted -- deleting an unreachable-looking arm is the
// shape of all six recorded silent losses. gamerules.cpp is not in SCC 11, so
// the edge leaves the atomic batch even though it does not leave the census.
//------------------------------------------------------------------------------

// g_Language IS DECLARED IN C# (SourceSharp.Server.GLanguageConVar),
// plan_server_novalve.md P2.3b; this reads it by name.
static CManagedConVarRef s_Language( "g_Language" );

extern short g_sModelIndexBubbles;
extern short g_sModelIndexBloodDrop;
extern short g_sModelIndexBloodSpray;

void UTIL_BloodStream( const Vector &origin, const Vector &direction, int color, int amount )
{
	if ( !UTIL_ShouldShowBlood( color ) )
		return;

	if ( s_Language.GetInt() == ss::kLanguageGerman && color == ss::BLOOD_COLOR_RED )
		color = 0;

	ss::CPVSFilter filter( origin );

	if ( SourceSharpTe_SuppressFilter( filter ) )
		return;

	TE_BloodStream( filter, 0.0, &origin, &direction, 247, 63, 14, 255, MIN( amount, 255 ) );
}				

//------------------------------------------------------------------------------
// Purpose : Creates both an decal and any associated impact effects (such
//			 as flecks) for the given iDamageType and the trace's end position
// Input   :
// Output  :
//------------------------------------------------------------------------------
void UTIL_ImpactTrace( trace_t *pTrace, int iDamageType, const char *pCustomImpactName )
{
	// TraceEntity is named in the shard brief's NULL-subject inventory: it
	// answers NULL for anything outside the SDK, so armed this function is
	// already inert for every entity rather than waiting on anything. Its own
	// `!pEntity` return is that inertness written down.
	//
	// UTIL_IsValidEntity takes an IServerEntity, and armed a CBaseEntity * no
	// longer converts to one -- IGameEntity has no base at all. ImpactTrace is
	// CBaseEntity's own.
	// THE WORLD FIRST, AND WITHOUT IT THERE ARE NO BULLET HOLES ON WALLS.
	//
	// This is the site the T1 census called the second most expensive: almost
	// every impact effect in a game lands on level geometry, and every one of
	// them reaches this function. With CWorld off the CBaseEntity hierarchy
	// (plan_cworld_trace.md T3) `TraceEntity` answers NULL for the world, the
	// `!pEntity` return above fires, and decals and flecks stop appearing on
	// walls -- silently, and only on walls, which is the failure mode that
	// reads as a renderer problem.
	//
	// The world answers for itself. CWorld::ImpactTrace is
	// CBaseEntity::ImpactTrace's body with the entity index answered by
	// definition rather than looked up.
	if ( SourceSharp_IsTheWorld( pTrace->m_pEnt ) )
	{
		if ( ( pTrace->surface.flags & SURF_SKY ) || pTrace->fraction == 1.0 )
			return;

		if ( CWorld *pWorld = GetWorldEntity() )
			pWorld->ImpactTrace( pTrace, iDamageType, pCustomImpactName );

		return;
	}

}

//-----------------------------------------------------------------------------
// Like UTIL_WaterLevel, but *way* less expensive.
// I didn't replace UTIL_WaterLevel everywhere to avoid breaking anything.
//-----------------------------------------------------------------------------
class CWaterTraceFilter : public CTraceFilter
{
public:
	bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask )
	{
		// SOURCESHARP: this asked EntityFromEntityHandle for a CBaseEntity and
		// answered "not water" for anything that was not one -- which is a
		// static prop, correctly, and ALSO every entity on the parallel
		// hierarchy, which is not correct and is most of this build.
		//
		// The question is a COLLIDEABLE's, not an entity's:
		// CBaseEntity::GetSolidFlags() is `m_Collision.GetSolidFlags()` and
		// m_Collision IS the ICollideable. enginetrace->GetCollideable resolves
		// the same field for whatever is in the spatial partition, both
		// hierarchies and static props alike, so the static-prop answer is
		// unchanged -- a prop carries no FSOLID_VOLUME_CONTENTS -- and a
		// managed water volume stops being invisible here.
		ICollideable *pCollide = enginetrace->GetCollideable( pHandleEntity );

		if ( !pCollide )
			return false;

		// Only impact water stuff...
		if ( pCollide->GetSolidFlags() & FSOLID_VOLUME_CONTENTS )
			return true;

		return false;
	}
};


