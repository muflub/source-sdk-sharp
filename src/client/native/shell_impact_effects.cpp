//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the leaf impact effects ============================//
// SS_REASON g  leaf math over mdl/bsp data
//
// plan_entity.md step 7a. game/client/c_impact_effects.cpp was 1,473 lines of
// which 1,391 compiled, and every one of them is a LEAF EFFECT in section 6's
// sense: a burst of particles, a decal, a sound at a point. There is no entity
// class here, no data table, and no decision about what the game does -- the
// decisions are the effect DISPATCH (c_te_effect_dispatch.cpp, still a guard by
// lane CE's ruling) and the managed backends that call these by name.
//
// So this moves rather than being ported or deleted, and the stock TU leaves
// client_csharp.vpc.
//
// WHAT DID NOT COME WITH IT: the C_TEGaussExplosion temp entity and
// TE_GaussExplosion, both inside `#ifndef SOURCESHARP` in the stock file, which
// is the STOCK build's arm. game/client/c_impact_effects.cpp therefore stays ON
// DISK, uncompiled -- the same treatment as c_te_explosion.cpp (step 7b) and
// c_sprite.cpp beside it. FX_GaussExplosion, which the managed
// ClientGaussExplosion backend and weapon_physcannon.cpp both called by name,
// was here until lane c5-particles made it C# (SparkEffects.GaussExplosion).
//
// THE C_BaseEntity REACHES. TWO ARE THE ALIAS LANE'S AND PREDATE THIS FILE:
// GetColorForSurface and FX_AntlionImpact each ask TraceEntity for the entity a
// trace hit, and each is behind `#ifndef SS_CBASEENTITY_ALIAS` with that lane's
// reasoning written beside it. Step 7a's move armed neither.
//
// TWO MORE ARRIVED WITH plan_entity.md §6's fx_impact.cpp ROW -- GetImpactDecal's
// DamageDecal ask and PlayImpactSound's `pEntity != tr.m_pEnt` identity test --
// and those two are DIFFERENT in one way worth knowing before editing either:
// game/client/fx_impact.cpp was in neither vpc, so its six guards were dead
// text, and these are the first of them ever to be compiled. They were moved
// exactly as written rather than flattened to their armed arm.
//
// cpp_alias_shards_b_test.cpp's per-file ledger follows the path: this file has
// a row on it now, reading 4, and fx_impact.cpp's went 6 -> 4.
//
// WHO WANTS WHAT IS HERE -- eleven objects at the tip this moved from, and the
// list is why "delete it" was never an option: shell_te_backends_client.o,
// fx_impact.o, c_te_legacytempents.o, fx_sparks.o, fx_explosion.o, fx.o,
// shell_trail_sim.o, shell_steamjet_sim.o, fx_blood.o,
// clienteffectprecachesystem.o and shell_dust_render.o (for g_Mat_DustPuff).
//
//=============================================================================//

#include "cbase.h"
#include "sharedInterface.h"
#include "fx.h"
#include "fx_sparks.h"
#include "clienteffectprecachesystem.h"
#include "decals.h"
#include "engine/IEngineSound.h"
#include "c_te_particlesystem.h"
#include "engine/ivmodelinfo.h"
#include "c_impact_effects.h"
#include "engine/IStaticPropMgr.h"
#include "c_te_effect_dispatch.h"
// The stock impact chain this file registers a callback into AND, since §6's
// LEAVE row landed, DEFINES: ParseImpactData, PerformCustomEffects,
// GetImpactDecal and PlayImpactSound are at the foot of this file. The HEADER
// keeps the declarations -- five stock mod TUs outside this build include it --
// and game/client/fx_impact.cpp is in client_base.vpc only, with everything
// left in it inside `#ifndef SOURCESHARP`, which is the stock build's arm.
#include "fx_impact.h"
// physprops. ParseImpactData turns the server's surfaceprop into a game
// material with it and PlayImpactSound turns the same index into a sound; it
// arrived with those two bodies and nothing here wanted it before.
#include "physics_shared.h"
// g_pEffects->Sparks, for PerformCustomEffects' CHAR_TEX_COMPUTER arm.
#include "IEffects.h"
// DispatchParticleEffect, for its CHAR_TEX_WARPSHIELD arm.
#include "particle_parse.h"
// The new-style effect's shell (C7 particles phase 3), for the whole
// cl_new_impact_effects 1 path -- the particle-system table and the three
// control points set on it.
#include "shell_new_particles.h"
// The PMaterial table the g_Mat_* handles come from (C7 particles phase 4).
#include "shell_particle_mgr.h"
// The typed refusal and the one spelling of its print. FX_AffectRagdolls is a
// §7 refusal in this build and the banner above its body says why.
#include "physics_bridge_refusal.h"
#include "shell_effect_ledger.h"
// SourceSharp_ClientEntityFromIndex: the entity lookup that answers for BOTH
// hierarchies. See the block above SourceSharp_ImpactCallback for why the
// C_BaseEntity spelling could not.
#include "shell_cliententitylist.h"
#include "world_subject.h"
#include "impact_effects.h"
#include "sourcesharp.h"
#include "managed_runtime_client.h"
#include "imanaged_areas_client.h"   // plan_client_vtables CE6: IManagedTempEntsClient

// The lifted sound statics. They used to be reached as CBaseEntity::Foo()
// through one-line forwarders in game/shared/baseentity_sound_forwarders.cpp;
// that quarantine is retired and the calls below name GameSound_* directly.
#include "gamesound.h"

#include "managed_convar_ref.h"
#include "shell_particle_systems.h"   // c5-particles: the one-shots are watched

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//Precahce the effects
CLIENTEFFECT_REGISTER_BEGIN( PrecacheEffectImpacts )
CLIENTEFFECT_MATERIAL( "effects/fleck_cement1" )
CLIENTEFFECT_MATERIAL( "effects/fleck_cement2" )
CLIENTEFFECT_MATERIAL( "effects/fleck_antlion1" )
CLIENTEFFECT_MATERIAL( "effects/fleck_antlion2" )
CLIENTEFFECT_MATERIAL( "effects/fleck_wood1" )
CLIENTEFFECT_MATERIAL( "effects/fleck_wood2" )
CLIENTEFFECT_MATERIAL( "effects/blood" )
CLIENTEFFECT_MATERIAL( "effects/blood2" )
CLIENTEFFECT_MATERIAL( "sprites/bloodspray" )
CLIENTEFFECT_MATERIAL( "particle/particle_noisesphere" )
CLIENTEFFECT_REGISTER_END()

// Cached handles to commonly used materials
PMaterialHandle g_Mat_Fleck_Wood[2] = { NULL, NULL };
PMaterialHandle g_Mat_Fleck_Cement[2] = { NULL, NULL };
PMaterialHandle g_Mat_Fleck_Antlion[2] = { NULL, NULL };
PMaterialHandle g_Mat_Fleck_Glass[2] = { NULL, NULL };
PMaterialHandle g_Mat_Fleck_Tile[2] = { NULL, NULL };
PMaterialHandle g_Mat_DustPuff[2] = { NULL, NULL };
PMaterialHandle g_Mat_BloodPuff[2] = { NULL, NULL };
PMaterialHandle g_Mat_SMG_Muzzleflash[4] = { NULL, NULL, NULL, NULL };
PMaterialHandle g_Mat_Combine_Muzzleflash[3] = { NULL, NULL, NULL };

// FOUR KNOBS, DECLARED IN MANAGED CODE -- SourceSharp.Client/ConVars.cs holds
// these two plus r_drawflecks and cl_new_impact_effects, with the same names,
// the same defaults and the same flags. None of the four is transmitted and
// none is resolved by the engine by name: they are tuning, which is precisely
// what plan_entity.md §6 says may not be declared in C++. The reads below are
// unchanged -- a CManagedConVarRef read is a load through a pointer into the
// engine's own ConVar, and no managed boundary is crossed per impact.
static CManagedConVarRef fx_drawimpactdebris( "fx_drawimpactdebris" );
static CManagedConVarRef fx_drawimpactdust( "fx_drawimpactdust" );

void FX_CacheMaterialHandles( void )
{
	g_Mat_Fleck_Wood[0] = ShellParticleMaterial_Find( "effects/fleck_wood1" );
	g_Mat_Fleck_Wood[1] = ShellParticleMaterial_Find( "effects/fleck_wood2" );

	g_Mat_Fleck_Cement[0] = ShellParticleMaterial_Find( "effects/fleck_cement1");
	g_Mat_Fleck_Cement[1] = ShellParticleMaterial_Find( "effects/fleck_cement2" );

	g_Mat_Fleck_Antlion[0] = ShellParticleMaterial_Find( "effects/fleck_antlion1" );
	g_Mat_Fleck_Antlion[1] = ShellParticleMaterial_Find( "effects/fleck_antlion2" );

	g_Mat_Fleck_Glass[0] = ShellParticleMaterial_Find( "effects/fleck_glass1" );
	g_Mat_Fleck_Glass[1] = ShellParticleMaterial_Find( "effects/fleck_glass2" );

	g_Mat_Fleck_Tile[0] = ShellParticleMaterial_Find( "effects/fleck_tile1" );
	g_Mat_Fleck_Tile[1] = ShellParticleMaterial_Find( "effects/fleck_tile2" );

	g_Mat_DustPuff[0] = ShellParticleMaterial_Find( "particle/particle_smokegrenade" );
	g_Mat_DustPuff[1] = ShellParticleMaterial_Find( "particle/particle_noisesphere" );

	g_Mat_BloodPuff[0] = ShellParticleMaterial_Find( "effects/blood" );
	g_Mat_BloodPuff[1] = ShellParticleMaterial_Find( "effects/blood2" );
	
	// The two mod guards stock wraps these in -- `#ifndef TF_CLIENT_DLL` around
	// the SMG flashes and `#ifndef CSTRIKE_DLL` around the Combine ones -- are
	// gone rather than carried: this is neither of those games and never will
	// be, so both arms this build takes are the only arms, and a dead mod guard
	// in the shim is what tools/dead-conditionals exists to refuse.
	g_Mat_SMG_Muzzleflash[0] = ShellParticleMaterial_Find( "effects/muzzleflash1" );
	g_Mat_SMG_Muzzleflash[1] = ShellParticleMaterial_Find( "effects/muzzleflash2" );
	g_Mat_SMG_Muzzleflash[2] = ShellParticleMaterial_Find( "effects/muzzleflash3" );
	g_Mat_SMG_Muzzleflash[3] = ShellParticleMaterial_Find( "effects/muzzleflash4" );
	g_Mat_Combine_Muzzleflash[0] = ShellParticleMaterial_Find( "effects/combinemuzzle1" );
	g_Mat_Combine_Muzzleflash[1] = ShellParticleMaterial_Find( "effects/combinemuzzle2" );
	g_Mat_Combine_Muzzleflash[2] = ShellParticleMaterial_Find( "effects/strider_muzzle" );
}


//=============================================================================//
// WHERE THE NUMBERS COME FROM NOW. ImpactEffects.cs's ImpactEffectPolicy builds
// each effect's block and the effect itself is C#'s (lane c5-particles: the
// flecks FleckSystems, the dust, blood and bug blood ImpactSprays); what is left
// here is each entry point's engine reads and its one crossing.
//=============================================================================//

//-----------------------------------------------------------------------------
// Purpose: Returns the color given trace information
// Input  : *trace - trace to get results for
//			*color - return color, gamma corrected (0.0f to 1.0f)
//-----------------------------------------------------------------------------
void GetColorForSurface( trace_t *trace, Vector *color )
{
	Vector	baseColor, diffuseColor;
	Vector	end = trace->startpos + ( ( Vector )trace->endpos - ( Vector )trace->startpos ) * 1.1f;
	
	if ( SourceSharp_IsTheWorld( trace->m_pEnt ) )
	{
		if ( trace->hitbox == 0 )
		{
			// If we hit the world, then ask the world for the fleck color
			engine->TraceLineMaterialAndLighting( trace->startpos, end, diffuseColor, baseColor );
		}
		else
		{
			// In this case we hit a static prop.
			staticpropmgr->GetStaticPropMaterialColorAndLighting( trace, trace->hitbox - 1, diffuseColor, baseColor );
		}
	}
	else
	{
		// In this case, we hit an entity. Find out the model associated with it
		// THE ARM THAT ALREADY RAN. This read `TraceEntity( *trace )`, which is
		// a constant NULL in this build, so the `!pEnt` arm below WAS the whole
		// branch -- it prints "Couldn't find surface" and returns white, which
		// is this function's own stated fallback. The SDK arm that followed
		// wanted GetCollideable reached THROUGH the entity, and IGameEntity has
		// no base at all -- not even IHandleEntity -- so there is no face to
		// step onto and nothing to re-point it to.
		{
			Msg("Couldn't find surface in GetColorForSurface()\n");
			color->x = 255;
			color->y = 255;
			color->z = 255;
			return;
		}

		// GetCollideable is reached THROUGH the entity, and armed C_BaseEntity is
		// IGameEntity, which has no base at all -- not even IHandleEntity -- so there
		// is no face to step onto. TraceEntity above is a NULL-subject answer as well,
		// so the `!pEnt` arm already handles every entity armed: it prints
		// "Couldn't find surface" and returns white, which is this function's own
		// stated fallback rather than one invented here.
	}

	//Get final light value
	color->x = pow( diffuseColor[0], 1.0f/2.2f ) * baseColor[0];
	color->y = pow( diffuseColor[1], 1.0f/2.2f ) * baseColor[1];
	color->z = pow( diffuseColor[2], 1.0f/2.2f ) * baseColor[2];
}


//-----------------------------------------------------------------------------
// This does the actual debris flecks
//-----------------------------------------------------------------------------
// The five FLECK_* constants that used to be here are in
// SourceSharp.Client/TempEnts/ImpactEffects.cs, where FX_DebrisFlecks and
// FX_AntlionImpact each name their own -- which is also how the two stopped
// silently sharing a gravity and a dampening that only one of them chose.


//-----------------------------------------------------------------------------
// FX_DebrisFlecks IS C#'S (lane c5-particles): the flecks (FleckSystems,
// fleck-stock.tsv.gz) and the dust trail, puffs and capper behind them
// (ImpactSprays, sprays-stock.tsv.gz), from ImpactEffectPolicy's block. This
// hands it the impact once: the trace's end and normal, the surface colour
// (GetColorForSurface, an engine read), the material, the scale, the fleck
// suppression and the particle system's live throttle. The count it started
// feeds step 7a's ledger as each fleck and dust particle did.
//-----------------------------------------------------------------------------
void FX_DebrisFlecks( const Vector& origin, trace_t *tr, char materialType, int iScale, bool bNoFlecks )
{

	if ( !fx_drawimpactdebris.GetBool() )
		return;

	IManagedParticlesClient *pArea = ManagedParticlesClient();
	if ( !pArea )
		return;

	Vector	color;
	GetColorForSurface( tr, &color );

	const int32_t nMade = pArea->ImpactDebris( tr->endpos.Base(), tr->plane.normal.Base(), color.Base(), (int32_t)materialType,
	                                           iScale, bNoFlecks ? 1 : 0, g_pParticleSystemMgr->ParticleThrottleScaling() );
	if ( nMade > 0 )
		g_SourceSharpEffects.nImpactFlecks += nMade;
}

//-----------------------------------------------------------------------------
// GLASS left with lane c5-particles: FX_GlassImpact and its GlassImpact client
// effect are C#'s ShardEffects (the shards on Simple3D, the dust on simple
// sheets), reading ImpactEffects.cs's Glass block as this body did.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : &pos - 
//			*tr - 
//-----------------------------------------------------------------------------
// FX_AntlionImpact moved to C# with lane c5-particles (ShardEffects.AntlionImpact),
// behind stock's own gate: it reads the hit entity's bounding radius and
// returns without one, and this ABI carries no entity bounds, so it is not live
// (docs/todo.md). Its arm below therefore draws nothing.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// FX_BugBlood and FX_Blood are C#'s (lane c5-particles: ImpactSprays.BugBlood,
// which nothing in this mod calls, and ImpactSprays.Blood, the blood sprite
// temp entity's puff), replayed against sprays-stock.tsv.gz.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Purpose: Dust impact
// Input  : &origin - position
//			&tr - trace information
//-----------------------------------------------------------------------------
void FX_DustImpact( const Vector &origin, trace_t *tr, int iScale )
{
	if ( !fx_drawimpactdust.GetBool() )
		return;

	// STOCK'S XBOX ARM IS DELETED, NOT CARRIED, for the reason
	// FX_DebrisFlecks gives above: `_XBOX` is on no command line this tree
	// builds, so it never drew and its constants were never policy this file
	// exercised.
	FX_DustImpact( origin, tr, (float)iScale );
}

void FX_DustImpact( const Vector &origin, trace_t *tr, float flScale )
{

	// C#'s (lane c5-particles): ImpactSprays.Dust on CDustParticle's rules
	// (sprays-stock.tsv.gz). The surface colour and the throttle are the
	// engine's and cross with the impact.
	IManagedParticlesClient *pArea = ManagedParticlesClient();
	if ( !pArea )
		return;

	Vector	color;
	GetColorForSurface( tr, &color );

	const int32_t nMade = pArea->ImpactDust( origin.Base(), tr->plane.normal.Base(), color.Base(), flScale,
	                                         g_pParticleSystemMgr->ParticleThrottleScaling() );
	if ( nMade > 0 )
		g_SourceSharpEffects.nImpactDust += nMade;
}

//-----------------------------------------------------------------------------
// FX_GaussExplosion left with lane c5-particles: the ring of sparks and the
// electric spark it ends with are C# (SparkEffects.GaussExplosion), over the
// same ImpactEffects.cs Gauss block, and ClientTeGaussExplosion calls it.
//-----------------------------------------------------------------------------

//=============================================================================//
// plan_entity.md §6's LEAVE ROW FOR game/client/fx_impact.cpp, LANDED HERE:
// FOUR BODIES RE-HOMED AND ONE REFUSED.
//
// §6 names the row and names this file as where it goes -- GetImpactDecal
// "chooses a decal from a surface material and a damage type", PlayImpactSound
// "chooses a sound", FX_AffectRagdolls "applies an impulse to every ragdoll
// along the shot ray by damage type". ParseImpactData and PerformCustomEffects
// came with them because SourceSharp_ImpactCallback at the foot of this file is
// the only caller any of the five has in this build, and a chain does not port
// one link at a time.
//
// THE DECLARATIONS DID NOT MOVE. They stay in game/client/fx_impact.h, which
// hl2/fx_hl2_impacts.cpp, hl2/c_npc_combinegunship.cpp, tf/tf_fx_impacts.cpp,
// sdk/sdk_fx_impacts.cpp and sdk/sdk_fx_weaponfx.cpp still include for the
// stock builds this tree also ships. Header keeps the declaration, shim keeps
// the body: that is game/shared/physicslink.h's split, and it is what makes
// this a RE-HOME rather than a rewrite.
//
// WHAT WAS ACTUALLY BROKEN. Five undefined symbols on the client link line,
// every one of them wanted by SourceSharp_ImpactCallback and by no other
// object. game/client/fx_impact.cpp is in client_base.vpc only -- its $File row
// left client_csharp.vpc with the rest of the removal set -- so the definitions
// had no translation unit in this build at all.
//
// THE ALIAS GUARDS CAME ACROSS UNCHANGED, AND THEY COMPILE HERE FOR THE FIRST
// TIME. fx_impact.cpp carried six `#ifndef SS_CBASEENTITY_ALIAS` regions while
// being in NEITHER vpc, so all six were dead text, and "it already has the
// guard" was never "it already builds armed". TWO of the six came with these
// bodies -- GetImpactDecal's DamageDecal ask and PlayImpactSound's tr.m_pEnt
// identity test -- and they are preserved as written rather than flattened to
// their armed arm: the SDK arm is what the stock build still compiles, and the
// guard is where the reasoning for each answer is written down.
// cpp_alias_shards_b_test.cpp counts them per file, so the move reads as a move
// there: fx_impact.cpp 6 -> 4, and this file's row carries those two on top of
// the two it already had.
//
// WHAT STAYED BEHIND, and why it is not a hole: Impact(), CRagdollEnumerator
// and RagdollImpactCallback. None of the three is in the undefined set that
// brought the other five here -- that set is the positive control on the claim
// -- and Impact()'s own work is done inline in SourceSharp_ImpactCallback, in
// an order that function's banner explains and for reasons it gives. The four
// remaining alias guards stayed with them.
//=============================================================================//

// BOTH ConVars CAME WITH PerformCustomEffects, AND THAT IS A LIFETIME RATHER
// THAN TIDINESS. A ConVar registers itself at DLL load from wherever its
// definition is compiled, and nothing else in this build defines either name.
// Leaving them behind in an uncompiled file would not have failed the link: it
// would have shipped a client on which `r_drawflecks` and
// `cl_new_impact_effects` simply do not exist -- no console entry, no way to
// turn the flecks off -- while the code that reads them read a default nobody
// could change. `cl_new_impact_effects` is below, with the table it selects.
//
// WHAT THAT ARGUMENT SETTLES IS WHERE THE NAME LIVES, NOT WHICH LANGUAGE
// DECLARES IT. Both are managed declarations now, and the reason the paragraph
// above still stands is that a managed [ConVar] constructs a REAL native ConVar
// in the engine's registry: the console entry exists, the flag is honoured, and
// the reads here are by-name refs into that same object.
//
// `r_drawmodeldecals`, the third ConVar that file named, did NOT come: it is an
// `extern` there rather than a definition, it belongs to Impact()'s decal
// branch, and this build already binds that name through
// shell_te_backends_client.cpp's own extern to shell_entitystatics.cpp.
static CManagedConVarRef r_drawflecks( "r_drawflecks" );

// THE SOUND-ROUTE HOOK, MOVED WITH THE FUNCTION THAT READS IT AND THE SETTER
// THAT WRITES IT -- the three are one lifetime and splitting them is silent
// both ways. PlayImpactSound below tests this before it emits, so a definition
// left in an uncompiled file is a dangling read; SetImpactSoundRoute at the
// foot of this block is the only thing that ever assigns it, so a setter left
// behind is a hook that stops existing without anything failing.
//
// It is NULL on every frame of this build and the `else` arm always runs: the
// two callers of the setter are game/shared/tf/tf_fx_shared.cpp and
// game/shared/sdk/sdk_fx_shared.cpp, batching a shotgun's ten identical impact
// sounds into one, and neither mod is compiled here.
ImpactSoundRouteFn g_pImpactSoundRouteFn = NULL;


// cl_new_impact_effects selects between the two tables managed code holds, and
// it is READ HERE rather than there so that the read stays what it always was:
// a load through a pointer into the engine's ConVar, on the native side of the
// boundary, handed up as one of the six inputs.
static CManagedConVarRef cl_new_impact_effects( "cl_new_impact_effects" );

//=============================================================================
// THE CHOICE ABOVE THE EFFECT, ASKED ONCE PER IMPACT (plan_shim.md §6, lane
// 4b-effects).
//
// What used to be here: GetImpactDecal's decal name, PerformCustomEffects'
// `iMaterial == CHAR_TEX_*` chain, PerformNewCustomEffects' twenty-six-row
// surface-to-particle-system table, and the fleck suppression both of the last
// two computed for themselves. Four choices about what a surface IS, written as
// literals. They are in SourceSharp.Client/TempEnts/ImpactChoice.cs now, and
// this is the seam.
//
// WHAT DID NOT MOVE, because the question is about the TRACE rather than about
// the surface: the SURF_SKY|SURF_NODRAW|SURF_HINT|SURF_SKIP early-out below,
// the reflect vector metal jitters, the control points a new particle effect
// takes and the angles a dispatched one takes. Each is arithmetic over the
// trace's own plane normal, done where the trace_t is.
//
// THE REFUSAL IS STOCK'S FALL-THROUGH. Every material with no arm in stock's
// chain already drew nothing, so a managed half that is absent, older than this
// slot, or refusing produces exactly what such a surface produced before -- no
// custom effect -- rather than an effect this side made up. Said once by name,
// like ImpactParams above.
//=============================================================================
struct ImpactChoice_t
{
	int  nAction;
	int  nScale;
	bool bNoFlecks;
	char szParticle[ SS_IMPACT_NAME_MAX ];
	char szDecal[ SS_IMPACT_NAME_MAX ];
};

static bool SourceSharp_ImpactChoice( int nMaterial, int nScale, int nFlags,
                                      int nDamageType, ImpactChoice_t *pOut )
{
	static bool s_bSaid = false;

	pOut->nAction = SS_IMPACT_ACT_NOTHING;
	pOut->nScale = nScale;
	pOut->bNoFlecks = false;
	pOut->szParticle[0] = '\0';
	pOut->szDecal[0] = '\0';

	const int32_t nIn[ SS_CHOICE_IN_COUNT ] =
	{
		nMaterial,
		nScale,
		nFlags,
		r_drawflecks.GetInt(),
		cl_new_impact_effects.GetInt(),
		nDamageType,
	};

	int32_t nAnswer[ SS_CHOICE_OUT_COUNT ] = { 0, 0, 0 };

	IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient();

	const int32_t nWrote = pTempEnts
		? pTempEnts->OnImpactChoice( nIn, SS_CHOICE_IN_COUNT,
		                             nAnswer, SS_CHOICE_OUT_COUNT,
		                             pOut->szParticle, SS_IMPACT_NAME_MAX,
		                             pOut->szDecal, SS_IMPACT_NAME_MAX )
		: -1;

	if ( nWrote < SS_CHOICE_OUT_COUNT )
	{
		if ( !s_bSaid )
		{
			s_bSaid = true;
			Warning( "[SourceSharp] impacts have no managed choice "
			         "(OnImpactChoice answered %d). No custom effect and no decal "
			         "is drawn for any of them.\n", (int)nWrote );
		}
		return false;
	}

	if ( nAnswer[ SS_CHOICE_OUT_ACTION ] < 0 ||
	     nAnswer[ SS_CHOICE_OUT_ACTION ] >= SS_IMPACT_ACT_COUNT )
	{
		if ( !s_bSaid )
		{
			s_bSaid = true;
			Warning( "[SourceSharp] impact choice answered action %d, which this "
			         "build has no arm for. Nothing is drawn.\n",
			         (int)nAnswer[ SS_CHOICE_OUT_ACTION ] );
		}
		return false;
	}

	pOut->nAction = nAnswer[ SS_CHOICE_OUT_ACTION ];
	pOut->nScale = nAnswer[ SS_CHOICE_OUT_SCALE ];
	pOut->bNoFlecks = nAnswer[ SS_CHOICE_OUT_NOFLECKS ] != 0;

	// The writer NUL-terminates and refuses a name that does not fit rather
	// than truncating one. This is the belt: a buffer it never touched is
	// already empty from the head of this function, and one it filled ends
	// where it said it did.
	pOut->szParticle[ SS_IMPACT_NAME_MAX - 1 ] = '\0';
	pOut->szDecal[ SS_IMPACT_NAME_MAX - 1 ] = '\0';

	return true;
}


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------
char const *GetImpactDecal( IGameEntity *pEntity, int iMaterial, int iDamageType )
{
	// "Impact.Concrete" WAS A LITERAL HERE AND IS A MANAGED CONSTANT NOW
	// (ImpactChoicePolicy.DefaultDecal). The argument for the value has not
	// changed and is worth keeping where a reader of this function will look
	// for it: DamageDecal is a virtual on C_BaseEntity that a handful of SDK
	// classes override to name their own material -- glass, flesh, antlion.
	// Armed there is no such table to consult, and the name is not a
	// placeholder for the missing answer: it is the answer, for a subject this
	// side has no decal opinion about. The material translation below still
	// runs, so the surfaceprop the ray actually hit is what picks the final
	// decal -- which is where a managed entity's answer belongs.
	//
	// pEntity IS STILL IGNORED, and it was before: the parameter is the hook
	// for that virtual, and stock's own caller in this file passes NULL.
	ImpactChoice_t choice;

	if ( !SourceSharp_ImpactChoice( iMaterial, 1, 0, iDamageType, &choice ) ||
	     choice.szDecal[0] == '\0' )
		return NULL;

	// See if we need to offset the decal for material type
	return decalsystem->TranslateDecalForGameMaterial( choice.szDecal, iMaterial );
}

//-----------------------------------------------------------------------------
// Purpose: Perform custom effects based on the Decal index
//-----------------------------------------------------------------------------
static void SetImpactControlPoint( CShellNewParticleEffect *pEffect, int nPoint, const Vector &vecImpactPoint, const Vector &vecForward, IGameEntity *pEntity )
{
	Vector vecImpactY, vecImpactZ;
	VectorVectors( vecForward, vecImpactY, vecImpactZ );
	vecImpactY *= -1.0f;

	pEffect->SetControlPoint( nPoint, vecImpactPoint );
	pEffect->SetControlPointOrientation( nPoint, vecForward, vecImpactY, vecImpactZ );
	pEffect->SetControlPointEntity( nPoint, pEntity );
}

//-----------------------------------------------------------------------------
// One managed choice, carried out.
//
// The two `*Particle` arms are what used to be PerformNewCustomEffects' body
// and PerformCustomEffects' warp-shield branch. They are one function here
// because the difference between them is which particle system creates the
// effect, and everything after that -- the reflect, the impact point, the
// control points -- was already identical.
//-----------------------------------------------------------------------------
static void DispatchNewParticleImpact( const char *pImpactName, const Vector &vecOrigin,
                                       trace_t &tr, const Vector &shotDir, int iScale )
{
	CSmartPtr<CShellNewParticleEffect> pEffect = CShellNewParticleEffect::Create( pImpactName );
	if ( !pEffect->IsValid() )
		return;

	// Watched for /client/{slot}/particles; the manager still owns its life.
	ShellParticles_WatchOneShot( pEffect.GetObject() );

	Vector	vecReflect;
	float	flDot = DotProduct( shotDir, tr.plane.normal );
	VectorMA( shotDir, -2.0f * flDot, tr.plane.normal, vecReflect );

	Vector vecShotBackward;
	VectorMultiply( shotDir, -1.0f, vecShotBackward );

	Vector vecImpactPoint = ( tr.fraction != 1.0f ) ? tr.endpos : vecOrigin;
	Assert( VectorsAreEqual( vecOrigin, tr.endpos, 1e-1 ) );

	SetImpactControlPoint( pEffect.GetObject(), 0, vecImpactPoint, tr.plane.normal, TraceGameEntity( tr ) );
	SetImpactControlPoint( pEffect.GetObject(), 1, vecImpactPoint, vecReflect,		TraceGameEntity( tr ) );
	SetImpactControlPoint( pEffect.GetObject(), 2, vecImpactPoint, vecShotBackward,	TraceGameEntity( tr ) );
	pEffect->SetControlPoint( 3, Vector( iScale, iScale, iScale ) );
	if ( pEffect->DefinitionReadsControlPoint( 4 ) )
	{
		Vector vecColor;
		GetColorForSurface( &tr, &vecColor );
		pEffect->SetControlPoint( 4, vecColor );
	}
}

//-----------------------------------------------------------------------------
// ASKING AND DOING ARE TWO FUNCTIONS, so that one impact is one crossing.
//
// SourceSharp_ImpactCallback needs the decal name AND the effect family, both
// from the same six inputs. Split any other way it would ask twice -- once
// through GetImpactDecal and once through PerformCustomEffects -- for one
// decision made from one set of numbers. It asks once and hands the answer
// here.
//
// PerformCustomEffects keeps its stock signature and is the asking half, for
// the callers fx_impact.h declares it for. Nothing on this build's link line is
// one of them today -- this file's own callback is the only live caller of any
// of the three -- and it is kept rather than folded away because the header is
// stock's and an evicted translation unit coming back would expect it.
//-----------------------------------------------------------------------------
static void ApplyImpactChoice( const ImpactChoice_t &choice, const Vector &vecOrigin,
                               trace_t &tr, const Vector &shotDir, int iMaterial )
{
	// Throw out the effect if any of these are true.
	//
	// STAYS HERE, and it is not an oversight that it is the one test managed
	// code was not given: it is a fact about the surface the ENGINE traced,
	// arriving on the trace_t this function already holds. Crossing the trace
	// to ask a question about the trace would be a crossing that buys nothing.
	if ( tr.surface.flags & (SURF_SKY|SURF_NODRAW|SURF_HINT|SURF_SKIP) )
		return;

	switch ( choice.nAction )
	{
	case SS_IMPACT_ACT_NOTHING:
		// Every material with no arm in stock's chain -- and, with the new
		// effects on, every material with no row in its table.
		break;

	case SS_IMPACT_ACT_DEBRIS_FLECKS:
		// Cement, tile and wood. The emitter reads the material itself, which
		// is why one arm serves all three.
		FX_DebrisFlecks( vecOrigin, &tr, iMaterial, choice.nScale, choice.bNoFlecks );
		break;

	case SS_IMPACT_ACT_DUST:
		FX_DustImpact( vecOrigin, &tr, choice.nScale );
		break;

	case SS_IMPACT_ACT_ANTLION:
		// FX_AntlionImpact (C#, ShardEffects.AntlionImpact) returns before it
		// draws when the hit entity's bounds are unknown, and they always are
		// on this ABI: nothing to carry out.
		break;

	case SS_IMPACT_ACT_METAL_SPARK:
		// The reflect, its jitter and FX_MetalSpark are C# (SparkEffects,
		// lane c5-particles): one crossing with the trace's plane normal.
		if ( IManagedParticlesClient *pArea = ManagedParticlesClient() )
			pArea->ImpactMetalSpark( vecOrigin.Base(), shotDir.Base(), tr.plane.normal.Base(), choice.nScale,
			                         g_pParticleSystemMgr->ParticleThrottleScaling() );
		break;

	case SS_IMPACT_ACT_SPARKS:
		{
			Vector	offset = vecOrigin + ( tr.plane.normal * 1.0f );

			g_pEffects->Sparks( offset );
		}
		break;

	case SS_IMPACT_ACT_PARTICLE:
		{
			QAngle vecAngles;
			VectorAngles( -shotDir, vecAngles );
			DispatchParticleEffect( choice.szParticle, vecOrigin, vecAngles );
		}
		break;

	case SS_IMPACT_ACT_NEW_PARTICLE:
		DispatchNewParticleImpact( choice.szParticle, vecOrigin, tr, shotDir, choice.nScale );
		break;
	}
}

void PerformCustomEffects( const Vector &vecOrigin, trace_t &tr, const Vector &shotDir, int iMaterial, int iScale, int nFlags )
{
	ImpactChoice_t choice;

	// The damage type is 0 here because this entry point has never carried one:
	// stock's signature takes the material, the scale and the flags, and the
	// damage type only ever reached the decal half.
	if ( !SourceSharp_ImpactChoice( iMaterial, iScale, nFlags, 0, &choice ) )
		return;

	ApplyImpactChoice( choice, vecOrigin, tr, shotDir, iMaterial );
}

//-----------------------------------------------------------------------------
// Purpose: Play a sound for an impact. If tr contains a valid hit, use that. 
//			If not, use the passed in origin & surface.
//-----------------------------------------------------------------------------
void PlayImpactSound( IGameEntity *pEntity, trace_t &tr, Vector &vecServerOrigin, int nServerSurfaceProp )
{
	surfacedata_t *pdata;
	Vector vecOrigin;

	// If the client-side trace hit a different entity than the server, or
	// the server didn't specify a surfaceprop, then use the client-side trace 
	// material if it's valid.
	// The identity half of this test needs pEntity and tr.m_pEnt to share a base.
	// Armed they do not -- tr.m_pEnt is an IHandleEntity and IGameEntity has no
	// such base by design -- so what is left is the half that is answerable:
	// the server named no surfaceprop.
	if ( tr.DidHit() && nServerSurfaceProp == 0 )
	{
		nServerSurfaceProp = tr.surface.surfaceProps;
	}
	pdata = physprops->GetSurfaceData( nServerSurfaceProp );
	if ( tr.fraction < 1.0 )
	{
		vecOrigin = tr.endpos;
	}
	else
	{
		vecOrigin = vecServerOrigin;
	}

	// Now play the esound
	if ( pdata->sounds.bulletImpact )
	{
		const char *pbulletImpactSoundName = physprops->GetString( pdata->sounds.bulletImpact );
		
		if ( g_pImpactSoundRouteFn )
		{
			g_pImpactSoundRouteFn( pbulletImpactSoundName, vecOrigin );
		}
		else
		{
			CLocalPlayerFilter filter;
			GameSound_Emit( filter, NULL, pbulletImpactSoundName, pdata->soundhandles.bulletImpact, &vecOrigin );
		}

		return;
	}

#ifdef _DEBUG
	Msg("***ERROR: PlayImpactSound() on a surface with 0 bulletImpactCount!\n");
#endif //_DEBUG
}


void SetImpactSoundRoute( ImpactSoundRouteFn fn )
{
	g_pImpactSoundRouteFn = fn;
}

//-----------------------------------------------------------------------------
// Purpose: Pull the impact data out
// Input  : &data - 
//			*vecOrigin - 
//			*vecAngles - 
//			*iMaterial - 
//			*iDamageType - 
//			*iHitbox - 
//			*iEntIndex - 
//-----------------------------------------------------------------------------
IGameEntity *ParseImpactData( const CEffectData &data, Vector *vecOrigin, Vector *vecStart, 
	Vector *vecShotDir, short &nSurfaceProp, int &iMaterial, int &iDamageType, int &iHitbox )
{
	IGameEntity *pEntity = data.GetEntity( );
	*vecOrigin = data.m_vOrigin;
	*vecStart = data.m_vStart;
	nSurfaceProp = data.m_nSurfaceProp;
	iDamageType = data.m_nDamageType;
	iHitbox = data.m_nHitBox;

	*vecShotDir = (*vecOrigin - *vecStart);
	VectorNormalize( *vecShotDir );

	// Get the material from the surfaceprop
	surfacedata_t *psurfaceData = physprops->GetSurfaceData( data.m_nSurfaceProp );
	iMaterial = psurfaceData->game.material;

	return pEntity;
}

//=============================================================================//
// FX_AffectRagdolls IS A DECLARED, PRINTED REFUSAL (§7). IT IS THE FIFTH NAME
// ON THAT ROW AND THE ONLY ONE THAT DID NOT COME ACROSS AS A BODY.
//
// Stock's is a ray walk: take the shot ray, ask the client spatial partition
// for every PARTITION_CLIENT_RESPONSIVE_EDICTS element along it, and for each
// one resolve an entity, take its physics object, clip the ray to it and call
// ImpactTrace so the bullet shoves the corpse.
//
// WHAT WAS MEASURED, THREE WAYS, BEFORE THE WORD "REFUSED" WAS WRITTEN:
//
//   * NO SUBJECT. Neither C_ClientRagdoll nor C_ServerRagdoll has a definition
//     in any of the 252 armed client objects. The control on that instrument is
//     that the same read DOES find a neighbouring class -- CRagdoll:: in
//     ragdoll.o -- so what is being read here is an absence and not a broken
//     query.
//
//     THE CONTROL USED TO NAME TWO CLASSES and now names one:
//     CRagdollLRURetirement:: was the second, and it was deleted by
//     plan_shim.md 4a item 3 (lane 3a). A control that names a class which no
//     longer exists would fail for the same reason as the absence it is meant
//     to distinguish from, which is exactly the failure a control exists to
//     rule out -- so it is re-pointed rather than left reading true by luck.
//
//   * NO FEEDER, AND NOW NO LIST EITHER. The retirement LRU stock consults on
//     its first line was filled only by MoveToTopOfLRU, which had no armed
//     caller at all; 4a item 3 then deleted the class, so CountRagdolls and
//     stock's early-out -- "don't do this when lots of ragdolls are
//     simulating" -- have no subject rather than a permanently empty one.
//
//   * NOTHING TO ENUMERATE. The parallel hierarchy never joins the client
//     spatial partition (shell_standalone_client.h:11), so the ray walk
//     enumerates an EMPTY SET on this client; and CRagdollEnumerator::EnumElement,
//     which stayed behind, is already elided by its own alias guard to
//     `(void)pHandleEntity; return ITERATION_CONTINUE;`.
//
// SO A PLAIN RE-HOME WOULD COMPILE, LINK, WALK NOTHING AND ALWAYS ANSWER FALSE,
// and that is §7's forbidden shape exactly. physics_bridge_refusal.h states the
// rule this obeys: "A refused path written as `(void)pEntity;` and a path that
// simply found nothing to do are byte-identical from outside."
//
// THE REACH COUNT WILL NOT BE ZERO, AND THAT IS THE HONEST READING RATHER THAN
// A DEFEATED ARGUMENT. Every other refusal in this lane predicts
// ReachCount() == 0 and treats a non-zero count as a wrong argument. This one
// is the exception and says so in its own reason string, because the impact
// callback genuinely fires on every bullet: the number is a bullet counter.
// What is vacuous here is the WALK, not the call, and ReachCount() is still the
// only instrument that will ever see this path -- which is why it is the typed
// CPhysicsBridgeRefusal and not a hand-rolled `static bool bSaidIt`.
//
// WHAT WOULD HAVE TO EXIST TO CHANGE THE ANSWER: an inbound ROUTE, not a
// subject. The subject is already managed and already wired --
// StandaloneRagdollState and StandaloneRagdollApplyForceCenter, sourcesharp.h
// :8784 and :8793, bound at sourcesharp_gamesystem.cpp:2471-2474 -- but there
// is no managed callback a native client effect can ask, and the ABI slot that
// would carry one is an integrator's decision rather than this file's. The
// SPHERE twin of this row is already closed and shows the shape the answer
// takes: SourceSharp.Client/TempEnts/ClientTeExplosion.cs:203 AffectRagdolls,
// behaviour ledger row C1. THIS IS THAT ROW FOR A RAY.
//
// AND ONE THING DIED WITH THE FILE, RECORDED HERE BECAUSE NOTHING ELSE WOULD
// SAY IT. game/client/fx_impact.cpp also carried
// DECLARE_CLIENT_EFFECT( "RagdollImpact", RagdollImpactCallback ), and its only
// dispatcher -- game/shared/baseentity_shared.cpp:2001 -- is out of this build
// as well. The two leave together, so today that is symmetric and not a hole.
// It becomes one the moment managed code dispatches "RagdollImpact", because
// the name would reach a client registry that has no such entry: the same
// shape, and the same silence, as the "Impact" row this file was written to
// close.
//=============================================================================//
bool FX_AffectRagdolls( Vector vecOrigin, Vector vecStart, int iDamageType )
{
	static CPhysicsBridgeRefusal s_RefuseRagdollRay(
		"the bullet's push on a ragdoll along the shot ray (FX_AffectRagdolls)",
		"the ray walk has no subject on this client. Neither C_ClientRagdoll nor "
		"C_ServerRagdoll is defined in any of the 252 armed client objects, "
		"the retirement LRU it would consult no longer exists at all -- its only "
		"writer MoveToTopOfLRU had no armed caller, so plan_shim.md 4a item 3 "
		"deleted the class -- and the parallel hierarchy never joins the client spatial partition "
		"(shell_standalone_client.h:11), so PARTITION_CLIENT_RESPONSIVE_EDICTS "
		"enumerates nothing. Re-homing the body would link and always answer false, "
		"which plan_entity.md section 7 forbids. READ THE COUNT CAREFULLY: unlike "
		"every other refusal in this lane, this one predicts a NON-ZERO ReachCount, "
		"because the impact callback fires on every bullet -- the number is a bullet "
		"counter and not a defeated argument, and what is vacuous is the walk rather "
		"than the call. The successor exists and the missing half is the ROUTE: "
		"StandaloneRagdollState and StandaloneRagdollApplyForceCenter are already "
		"bound (sourcesharp_gamesystem.cpp:2471-2474), but no managed callback lets "
		"a native client effect ask, and the sphere twin of this row is already "
		"closed in SourceSharp.Client/TempEnts/ClientTeExplosion.cs AffectRagdolls, "
		"behaviour ledger row C1. This is that row for a ray." );

	if ( s_RefuseRagdollRay.Reached() )
	{
		Warning( SOURCESHARP_PHYSICS_REFUSAL_FMT,
		         s_RefuseRagdollRay.What(), s_RefuseRagdollRay.Refusal() );
	}

	// The three arguments are read by nothing above on purpose. There is no
	// walk to start, so there is no shot ray to build out of vecStart and
	// vecOrigin and no damage type to hand an ImpactTrace that will not happen.
	// The answer is stock's own for a sweep that hit nothing.
	return false;
}

//=============================================================================//
// THE NAME "Impact", CLAIMED. BEHAVIOUR LEDGER ROW C7 CLOSES HERE.
//
// WHAT WAS WRONG. A bullet in this build dispatched no impact effect at all --
// no decal, no flecks, no dust, no impact sound -- and it did it in SILENCE,
// which is the one shape plan_entity.md §7 forbids outright: "a behaviour that
// is refused is a ContractBaseline entry with a reason, never a silent no-op".
// Lane `live-batch` measured it with the whole firing chain asserted first: a
// crate at the aim point, `debug-api aim` naming prop_physics, carried rounds
// 90 -> 63, sprite quads +2115 -- and both effect ledgers flat, temp entities
// 58 -> 58 and impact particles 0 -> 0.
//
// IT HAD TWO ENDS MISSING, NOT ONE, AND NEITHER WAS A REGRESSION.
//
//   THE SERVER never asked. Arsenal.FireBullets traced, damaged and bled, and
//     never called Effects.ImpactEffect. `git log -S'ImpactEffect' --follow` on
//     Arsenal.cs names no commit at all across the sixteen that have touched
//     it: the call was never there to lose.
//
//   THE CLIENT could not have answered if it had. DECLARE_CLIENT_EFFECT(
//     "Impact", ImpactCallback ) is written in hl2/fx_hl2_impacts.cpp:115, and
//     client_csharp.vpc compiles ZERO files from hl2\ -- so the one caller that
//     did dispatch (FuncBreakableSurf.cs:235, a pane of glass) reached
//     DispatchEffectToCallback, walked a registry with no such entry and
//     printed "effect 'Impact' not found on client" at developer verbosity.
//     c_te_effect_dispatch.cpp's own header block records this as ledger row
//     C7, open, and says the row "may be closed only by making the capability
//     real -- never by substituting a name that resolves".
//
// SO IT IS MADE REAL, HERE, AND THE NAME IS UNCHANGED. Nothing about what the
// server sends moves: it still dispatches "Impact", spelt exactly as
// SourceSharp_ImpactEffect spells it, and TempEntClientCeTests'
// TheImpactDispatchNameIsCarriedThroughUnchangedAndRowC7StaysOpen still holds
// on every value it asserts. What changed is that something now claims the name.
//
// WHY THIS FILE AND NOT hl2/fx_hl2_impacts.cpp. plan_entity.md §6's LEAVE table
// rules on fx_impact.cpp by name -- GetImpactDecal, PlayImpactSound and
// FX_AffectRagdolls are game semantics and game state, so they are managed
// policy -- and names this file as its successor, "which already carries
// FX_DebrisFlecks, FX_GlassImpact and GetColorForSurface". Bringing a stock hl2
// TU into client_csharp.vpc to get one twenty-line function would drag
// ImpactJeepCallback, ImpactGaussCallback, the airboat and the helicopter in
// with it, none of which has a vehicle in this build to fire it.
//
// C++ IS KEPT TO WHAT THE ENGINE NEEDS. This is the registration and the
// ORDER; every decision inside it is already elsewhere. Which effect a material
// gets is PerformCustomEffects in fx_impact.cpp, itself on §6's port list; the
// NUMBERS each effect draws with are SourceSharp.Client/TempEnts/
// ImpactEffects.cs, reached through OnImpactEffectParams and counted on
// CrossingOp.ClientImpactEffectParams; and the surfaceprop that picks both is
// chosen server-side in managed C#, by Arsenal.FireBullets.
//=============================================================================//

//-----------------------------------------------------------------------------
// THE ENTITY IS RESOLVED WIDELY HERE, AND THAT IS THE SECOND HALF OF THE BUG.
//
// MEASURED, not reasoned to. With the callback registered and the server
// dispatching, a burst into dm_lockdown's floor printed, per round:
//
//     impact ent=(nil) surfprop=13 material=84(T) dmg=2 hitbox=0
//
// -- the surfaceprop and the material crossing correctly, and the ENTITY NULL.
// Not for a managed prop: for WORLDSPAWN. `CEffectData::GetEntity()` is
// `ClientEntityList().GetBaseEntityFromHandle`, which filters to
// `GetGameEntity() != NULL` and so answers NULL for everything on the parallel
// hierarchy -- which on this client is everything, the world included. Stock's
// ImpactCallback reads that NULL as "the thing that was hit has been
// destroyed", plays a sound and returns. So a faithful copy of stock's callback
// draws NOTHING here, for every shot, and looks exactly like the defect it was
// written to fix.
//
// `CEffectData::entindex()` is the spelling that answers: it goes through
// `GetClientNetworkableFromHandle`, which BOTH hierarchies implement, and its
// own comment in effect_dispatch_data.cpp records being retyped for this exact
// reason -- "NULL there meant there IS an entity and it is not an SDK one".
// The index it answers is the currency every decal slot in this ABI already
// speaks.
//-----------------------------------------------------------------------------

// Declared where sourcesharp_gamesystem.cpp declares them -- at the use site,
// because these are ABI slot implementations and there is no header that
// gathers them. They are this client's own, they take an entity INDEX, and
// every one of them resolves through SourceSharp_ClientEntityFromIndex, so each
// reaches the parallel hierarchy where the C_BaseEntity spelling cannot.
//
// NOT `extern "C"`. They are defined as ordinary C++ in
// shell_te_backends_client.cpp and the linker wants the mangled names; an
// `extern "C"` block here compiles perfectly and then fails at link with four
// undefined references, which is how this was found.
void    SourceSharp_ClientDecalShoot( int32_t nDecalIndex, int32_t nEntity,
                                      const float *pPositionXYZ, const float *pSAxisXYZ,
                                      int32_t nFlags );
void    SourceSharp_ClientDecalStaticProp( const float *pStartXYZ, const float *pEndXYZ,
                                           int32_t nStaticPropIndex, int32_t nDecalIndex );
void    SourceSharp_ClientDecalStudioModel( int32_t nEntity,
                                            const float *pRayStartXYZ, const float *pRayEndXYZ,
                                            int32_t nDecalIndex );
int32_t SourceSharp_ClientEntityModelType( int32_t nEntity );

// AND ONE OF STOCK'S OWN, WHICH NO HEADER DECLARES. This block used to carry a
// forward declaration of FX_AffectRagdolls, because fx_impact.cpp defined it,
// only its own ImpactCallback ever called it, and the declaration therefore
// lived in that file's callers -- none of which this build has. It needs no
// declaration now: §6's LEAVE row landed above, the body is in THIS file, and
// what it is is a §7 REFUSAL that prints. The banner over that body is the
// entry, and it is still called exactly where stock calls it so that the
// refusal is reached and counted rather than argued about.

//-----------------------------------------------------------------------------
// One bullet's mark, on whatever it landed on.
//
// STOCK'S ImpactCallback (hl2/fx_hl2_impacts.cpp:86) IN ITS OWN ORDER, with
// each step spelt the way this hierarchy can answer. The order is load-bearing
// rather than incidental:
//
//   1. THE RAGDOLL SWEEP, which is a free function and needs no subject.
//
//   2. THE TRACE, which stock gets as a SIDE EFFECT of AddDecal and which has
//      to be taken explicitly here. Everything after it reads it:
//      PerformCustomEffects tests tr.surface.flags for SURF_SKY, SURF_NODRAW,
//      SURF_HINT and SURF_SKIP and returns before emitting a single particle,
//      it reflects a metal spark off tr.plane.normal, and PlayImpactSound reads
//      tr.endpos for where to put the noise. A callback that sparked first
//      would spark on the skybox.
//
//      ClipRayToEntity FIRST, which is stock's own IMPACT_NODECAL branch and
//      takes an IHandleEntity -- so it clips against the thing that was
//      actually hit, on either hierarchy. THAT ORDER MATTERS FOR GLASS: a
//      breakable surface stops being solid to MASK_SHOT the moment its first
//      pane goes, so a world trace would sail through the pane the shot just
//      hit and put the impact on the wall behind it. The world trace is the
//      FALLBACK, for an impact whose entity has gone or whose collideable will
//      not clip.
//
//   3. THE DECAL, through the three slots C-Phase 3 built for exactly this
//      hierarchy -- a static prop, a studio model, and everything else. The
//      switch is modelinfo->GetModelType, which is what C_BaseEntity::AddDecal
//      itself switches on; SourceSharp_ClientEntityModelType is that question
//      asked by index. It is written here rather than in C# because the caller
//      is fx_impact.cpp's chain, and plan_entity.md §6 already rules that this
//      chain -- GetImpactDecal, PlayImpactSound, FX_AffectRagdolls -- is
//      managed policy whose successor is THIS FILE. The switch moves when that
//      port does, and it is three lines in the meantime rather than a fourth
//      ABI slot that would hide it.
//
//   4. THE SOUND, WHETHER OR NOT ANYTHING ELSE HAPPENED, which is stock and is
//      not an oversight: a surface with no registered decal still makes a noise
//      when it is shot.
//
// GetImpactDecal IS PASSED NULL DELIBERATELY. Its entity argument exists to let
// a class override DamageDecal -- glass, flesh, antlion -- and that virtual is
// C_BaseEntity's, so there is no table to consult on this hierarchy. The
// function's own comment says so where it stands: "Impact.Concrete" is not a
// placeholder for a missing answer, it is that function's answer for a subject
// it has no decal opinion about, and TranslateDecalForGameMaterial then picks
// the final decal from the surfaceprop the SERVER traced. Which is where a
// managed entity's answer belongs.
//
// iScale 1 is stock's for this callback. ImpactJeep and ImpactGauss pass 2.
//-----------------------------------------------------------------------------
void SourceSharp_ImpactCallback( const CEffectData &data )
{

	trace_t tr;
	Vector vecOrigin, vecStart, vecShotDir;
	int iMaterial, iDamageType, iHitbox;
	short nSurfaceProp;

	// THE RETURN VALUE IS DISCARDED, and the block above says why: it is
	// GetEntity(), which is NULL for every entity on this client. What is
	// wanted from this call is the UNPACKING -- the two points, the shot
	// direction, the game material behind the surfaceprop, the damage type and
	// the hitbox -- and that is all of its body but the last line.
	ParseImpactData( data, &vecOrigin, &vecStart, &vecShotDir,
	                 nSurfaceProp, iMaterial, iDamageType, iHitbox );

	const int nEntity = data.entindex();
	IClientEntity *pEntity = SourceSharp_ClientEntityFromIndex( nEntity );

	memset( &tr, 0, sizeof( trace_t ) );
	tr.fraction = 1.0f;

	Vector shotDir = vecOrigin - vecStart;
	const float flLength = VectorNormalize( shotDir );
	Vector traceExt;
	VectorMA( vecStart, flLength + 8.0f, shotDir, traceExt );

	FX_AffectRagdolls( vecOrigin, vecStart, iDamageType );

	Ray_t ray;
	ray.Init( vecStart, traceExt );

	if ( pEntity )
		enginetrace->ClipRayToEntity( ray, MASK_SHOT, pEntity, &tr );

	if ( !tr.DidHit() )
		UTIL_TraceLine( vecStart, traceExt, MASK_SHOT, NULL, COLLISION_GROUP_NONE, &tr );

	// ONE CROSSING FOR ONE DECISION. The decal name and the effect family are
	// both answers to the same six inputs, so they are asked together -- see
	// ApplyImpactChoice's note. GetImpactDecal still exists and still asks for
	// itself; it is not on this path because this path needs the other half of
	// the same answer.
	//
	// A REFUSAL IS STOCK'S FALL-THROUGH, WHICH IS NOT NOTHING HERE. Managed
	// absent or refusing means no decal and no custom effect -- but the sound
	// below still plays, because a surface with no registered decal still makes
	// a noise when it is shot, and that is stock's order rather than an
	// accident of this one.
	ImpactChoice_t choice;
	const bool bChose = SourceSharp_ImpactChoice( iMaterial, 1, 0, iDamageType, &choice );

	const int nDecal = ( bChose && choice.szDecal[0] != '\0' )
		? decalsystem->GetDecalIndexForName(
		      decalsystem->TranslateDecalForGameMaterial( choice.szDecal, iMaterial ) )
		: -1;

	if ( nDecal != -1 )
	{
		// A non-zero hitbox on entity 0 is the engine saying "this was static
		// prop hitbox-1", which is the convention ClientDecalStaticProp takes.
		if ( nEntity == 0 && iHitbox != 0 )
		{
			SourceSharp_ClientDecalStaticProp( &vecStart.x, &traceExt.x, iHitbox - 1, nDecal );
		}
		else if ( SourceSharp_ClientEntityModelType( nEntity ) == SOURCESHARP_MODELTYPE_STUDIO )
		{
			SourceSharp_ClientDecalStudioModel( nEntity, &vecStart.x, &traceExt.x, nDecal );
		}
		else
		{
			// The world, a brush model, and the two answers that are neither:
			// a SPRITE takes no decal in stock either, and ClientDecalShoot
			// resolving nothing prints its own line at verbosity 1. Given the
			// POINT the trace found rather than the server's origin, because
			// this is the decal's centre and not the shot's end.
			SourceSharp_ClientDecalShoot( nDecal, nEntity, &tr.endpos.x, NULL, 0 );
		}
	}

	if ( bChose && tr.DidHit() )
	{
		ApplyImpactChoice( choice, vecOrigin, tr, vecShotDir, iMaterial );
	}

	PlayImpactSound( NULL, tr, vecOrigin, nSurfaceProp );
}

DECLARE_CLIENT_EFFECT( "Impact", SourceSharp_ImpactCallback );
