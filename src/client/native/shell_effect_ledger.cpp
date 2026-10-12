//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the effect draw ledger, and the command that reads it =//
// SS_REASON c  static-init registration
//
// See shell_effect_ledger.h for what a counter here means and why it exists.
// This file holds the one definition of the struct and the console command
// step 7a's gate reads.
//
//=============================================================================//

#include "cbase.h"
#include "shell_effect_ledger.h"
#include "particles_lib_client.h"
#include "ss_particles/iparticlelib.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

SourceSharpEffectLedger_t g_SourceSharpEffects = {};

//-----------------------------------------------------------------------------
// THE FOG ROW USED TO READ "not shipped". IT IS A COUNT NOW (step 8e-render).
//
// Step 7c measured, and step 7a re-measured, that world fog did not render on
// this client, and both were right: env_fog_controller ITSELF was managed and
// held the right numbers (SourceSharp.Server EnvFogController), and the CHANNEL
// from it to a viewer did not exist -- fogcontroller.cpp is entirely
// `#ifndef SOURCESHARP`, so server.so declared no DT_FogController and nothing
// sent the values anywhere. 8e-render built that channel: the master
// controller's resolved values ride in PlayerNetState, the managed client
// pushes the local player's copy, and SourceSharp_ViewerFogParams hands back a
// pointer to it.
//
// SO THE ROW POINTS AT A DIFFERENT COMMAND, and the reason it points at one at
// all is unchanged: this command still does NOT call the fog functions, because
// calling them would add an ASK to their counters every time the gate ran --
// a diagnostic that changes the thing it measures. `cs_cl_fog` is where the
// pushes and the answered-NULL count live, and tools/8e-fog-gate reads them.
//
// The 3D skybox is still a not-shipped row and is NOT this step's: lane
// phase4-exit traced it to UpdateAllClientData never running, which is a
// different fault from the fog's.
//-----------------------------------------------------------------------------
CON_COMMAND( cs_cl_effects_report,
	"SourceSharp: how many primitives each of step 7a's leaf effect renderers has emitted." )
{
	const SourceSharpEffectLedger_t &l = g_SourceSharpEffects;

	Msg( "[SourceSharp/Client] effect draws (7a):\n" );
	Msg( "  sprite quads        %8d\n", l.nSpriteQuads );
	Msg( "  rope draws          %8d\n", l.nRopeDraws );
	Msg( "  dust motes          %8d\n", l.nDustMotes );
	Msg( "  impact flecks       %8d\n", l.nImpactFlecks );
	Msg( "  impact dust         %8d\n", l.nImpactDust );
	Msg( "  dissolve draws      %8d\n", l.nDissolveDraws );
	Msg( "  draw batches        %8d\n", l.nDrawBatches );
	Msg( "  batched sprites     %8d\n", l.nDrawBatchedSprites );
	Msg( "  batch fallbacks     %8d\n", l.nDrawBatchFallbacks );
	Msg( "  singleton draws     %8d\n", l.nSingletonDraws );
	// particles.a's tier2 stand-ins count inside libss_particles.so
	// (particles_lib/particles_tier2_stubs.cpp, plan_particles_so.md).
	SSParticleLibCounters_t particles;
	ParticleLib_Counters( &particles );
	Msg( "  particle beam strips (stubbed)       %8d\n", particles.nBeamStrips );
	Msg( "  particle wireframe boxes (stubbed)   %8d\n", particles.nWireframeBoxes );
	Msg( "  particle wireframe spheres (stubbed) %8d\n", particles.nWireframeSpheres );
	Msg( "  world fog           shipped - see cs_cl_fog\n" );
}
