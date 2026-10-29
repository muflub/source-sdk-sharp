//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration

#include "engine/iserverplugin.h"

#include "tier1/convar.h"
#include "shell_effect_ledger.h"
#include "particles_lib_client.h"
#include "ss_particles/iparticlelib.h"

SourceSharpEffectLedger_t g_SourceSharpEffects = {};

// Does not call the fog functions, which would count the ask; cs_cl_fog reports fog.
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
	Msg( "  batch in place      %8d\n", l.nDrawBatchInPlace );
	Msg( "  singleton draws     %8d\n", l.nSingletonDraws );
	SSParticleLibCounters_t particles;
	ParticleLib_Counters( &particles );
	Msg( "  particle beam strips (stubbed)       %8d\n", particles.nBeamStrips );
	Msg( "  particle wireframe boxes (stubbed)   %8d\n", particles.nWireframeBoxes );
	Msg( "  particle wireframe spheres (stubbed) %8d\n", particles.nWireframeSpheres );
	Msg( "  world fog           shipped - see cs_cl_fog\n" );
}
