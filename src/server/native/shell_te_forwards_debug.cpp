//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_te_forwards.cpp's diagnostic console command ==//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
//=============================================================================//

#include <stdlib.h>                  // atof
#include "tier1/convar.h"            // CCommand
#include "mathlib/vector.h"
#include "irecipientfilter.h"
#include "ss_recipient_filter.h"
#include "ss_effect_dispatch.h"
#include "shell_tempents.h"
#include "sourcesharp_entrypoints.h"   // SourceSharp_EffectDispatchIndex
#include "native_command_body.h"
#include "ss_string.h"

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_s4.cpp
//=============================================================================//

// The four names this build's own callers dispatch, from shell_worldquery.cpp,
// shell_ai_orphans.cpp and shell_entity_orphans.cpp. TempEntEffectDataTests
// reads those three files and fails if one of them grows a name this list does
// not have, so gate 3 exercises the traffic the build produces rather than a
// list somebody chose once.
static const char *const s_pLiveEffects[] =
{
	"Impact",
	"watersplash",
	"bloodspray",
	"GlassImpact",
};

// Names with no DECLARE_CLIENT_EFFECT anywhere, so the client is guaranteed to
// print each one back. Deliberately ugly: a name that collided with a real
// effect would play it instead of reporting, which is the one outcome this
// cannot afford.
static const char *const s_pProbeEffects[] =
{
	"cs_s4_probe_0",
	"cs_s4_probe_1",
	"cs_s4_probe_2",
	"cs_s4_probe_3",
};

static void FireOneDispatch( const char *pName, const Vector &at )
{
	ss::CEffectData data;

	data.m_vOrigin = at;
	data.m_vStart  = at + Vector( 0, 0, 64 );
	data.m_vNormal = Vector( 0, 0, 1 );
	data.m_vAngles = QAngle( 0, 0, 0 );

	data.m_flScale     = 1.0f;
	data.m_flMagnitude = 2.0f;
	data.m_flRadius    = 16.0f;

	// -1 is "no surface property", and it is here on purpose: it is the value
	// SendProxy_ShortAddOne existed for, so a run of this command with the plus
	// one missing is a run with a real surface property in its place.
	data.m_nSurfaceProp = -1;

	ss::CBroadcastRecipientFilter filter;


	TE_DispatchEffect( filter, 0.0f, data.m_vOrigin, pName, data );

	// SourceSharp_EffectDispatchIndex is shell_te_forwards.cpp's EffectNameIndex,
	// exported -- the same resolution TE_DispatchEffect just made, not a second.
	Msg( "cs_sv_te_dispatch: '%s' -> string table index %d at (%g %g %g)\n",
		pName, SourceSharp_EffectDispatchIndex( pName ), at.x, at.y, at.z );
}

SS_NATIVE_COMMAND( cs_sv_te_dispatch )
{
	if ( args.ArgC() < 2 )
	{
		Msg( "usage: cs_sv_te_dispatch <name|live|probe|all> [x y z]\n" );
		return;
	}

	Vector at( 0, 0, 0 );

	if ( args.ArgC() >= 5 )
		at.Init( atof( args[2] ), atof( args[3] ), atof( args[4] ) );

	const bool bLive  = !ss::StrICmp( args[1], "live" )  || !ss::StrICmp( args[1], "all" );
	const bool bProbe = !ss::StrICmp( args[1], "probe" ) || !ss::StrICmp( args[1], "all" );

	if ( !bLive && !bProbe )
	{
		FireOneDispatch( args[1], at );
		return;
	}

	// INTERLEAVED, so the probes sit BETWEEN the live names in the string table
	// rather than after them. A mapping that shifted by one would then print a
	// live effect's name out of a probe, which is the specific failure this is
	// looking for; a run of probes added at the end could only ever shift into
	// another probe.
	int iSite = 0;

	for ( int i = 0; i < ARRAYSIZE( s_pLiveEffects ); i++ )
	{
		if ( bLive )
			FireOneDispatch( s_pLiveEffects[i], at + Vector( 0, iSite++ * 48.0f, 0 ) );

		if ( bProbe && i < ARRAYSIZE( s_pProbeEffects ) )
			FireOneDispatch( s_pProbeEffects[i], at + Vector( 0, iSite++ * 48.0f, 0 ) );
	}
}
