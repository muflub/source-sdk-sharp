//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration

// Debug builds only.

#include <stdlib.h>
#include "tier1/convar.h"
#include "mathlib/vector.h"
#include "irecipientfilter.h"
#include "ss_recipient_filter.h"
#include "ss_effect_dispatch.h"
#include "shell_tempents.h"
#include "networkstringtabledefs.h"

extern INetworkStringTable *g_pStringTableEffectDispatch;
#include "native_command_body.h"
#include "ss_string.h"

// The effect names this build's own callers dispatch.
static const char *const s_pLiveEffects[] =
{
	"Impact",
	"watersplash",
	"bloodspray",
	"GlassImpact",
};

// Names no client effect declares, so the client reports each one back rather
// than playing it.
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

	// -1 is "no surface property", the value SendProxy_ShortAddOne exists for.
	data.m_nSurfaceProp = -1;

	ss::CBroadcastRecipientFilter filter;

	TE_DispatchEffect( filter, 0.0f, data.m_vOrigin, pName, data );

	// A lookup: TE_DispatchEffect already added the name.
	Msg( "cs_sv_te_dispatch: '%s' -> string table index %d at (%g %g %g)\n", pName,
		g_pStringTableEffectDispatch ? g_pStringTableEffectDispatch->FindStringIndex( pName ) : -1, at.x, at.y, at.z );
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

	// Interleaved, so a string table mapping off by one prints a live name for a probe.
	int iSite = 0;

	for ( int i = 0; i < ARRAYSIZE( s_pLiveEffects ); i++ )
	{
		if ( bLive )
			FireOneDispatch( s_pLiveEffects[i], at + Vector( 0, iSite++ * 48.0f, 0 ) );

		if ( bProbe && i < ARRAYSIZE( s_pProbeEffects ) )
			FireOneDispatch( s_pProbeEffects[i], at + Vector( 0, iSite++ * 48.0f, 0 ) );
	}
}
