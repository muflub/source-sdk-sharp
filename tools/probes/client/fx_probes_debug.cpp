//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// tools/probes/client/fx_probes_debug.cpp -- lane c2-fx's oracle probes
// (plan_client_novalve.md §5.1 step 1: record from the code being REPLACED).
//
// Compiled only into a debug client.so configured with -DSS_CLIENT_PROBES=ON,
// beside shell_probes_debug.cpp (tools/probes/c0-session says how to run).
// Every line goes to SOURCESHARP_PROBE_LOG, tagged "fx".
//
// THE EFFECT DISPATCH TABLE (c_te_effect_dispatch.cpp):
//   cs_cl_probe_fxreg            walk CClientEffectRegistration::s_pHead in
//                                stock's own order and log one line per
//                                registration ("fxreg <i> <name>"); swap each
//                                callback for a trampoline that logs which
//                                registration ran ("fxrun <i>") before calling
//                                the original.
//   cs_cl_probe_fxlookup <names> ask stock's DispatchEffectToCallback which
//                                registration each name resolves to, without
//                                running it: the trampolines are put in dry
//                                mode for each call. Logs
//                                "fxlookup <i or -1> <name>".

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "c_te_effect_dispatch.h"
#include "fx_probe_log.h"
#include "IEffects.h"
#include "ipredictionsystem.h"
#include "sharedInterface.h"
#include "shell_cliententitylist.h"

#include <fcntl.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "tier0/memdbgon.h"

namespace
{

int FxProbeFd()
{
	static int s_fd = -2;
	if ( s_fd == -2 )
	{
		const char *pPath = getenv( "SOURCESHARP_PROBE_LOG" );
		s_fd = ( pPath && *pPath ) ? open( pPath, O_WRONLY | O_CREAT | O_APPEND | O_CLOEXEC, 0644 ) : -1;
	}
	return s_fd;
}

} // namespace

// Not static: the probe copies of fork TUs log through it (fx_probe_log.h).
void FxProbeLog( const char *pFmt, ... )
{
	int fd = FxProbeFd();
	if ( fd < 0 )
		return;
	timespec ts;
	clock_gettime( CLOCK_MONOTONIC, &ts );
	char buf[2048];
	int n =
		snprintf( buf, sizeof( buf ), "%lld %d fx ", (long long)ts.tv_sec * 1000000000LL + ts.tv_nsec, (int)getpid() );
	va_list ap;
	va_start( ap, pFmt );
	int m = vsnprintf( buf + n, sizeof( buf ) - n - 1, pFmt, ap );
	va_end( ap );
	if ( m < 0 )
		m = 0;
	n += ( m < (int)sizeof( buf ) - n - 1 ) ? m : (int)sizeof( buf ) - n - 2;
	buf[n++] = '\n';
	ssize_t w = write( fd, buf, n );
	(void)w;
}

namespace
{

//-----------------------------------------------------------------------------
// The dispatch table. One trampoline per registration, by template index, so a
// trampoline knows which registration it stands for without any state.
//-----------------------------------------------------------------------------
const int kMaxRegs = 128;
ClientEffectCallback g_pOrig[kMaxRegs];
int g_nRegs = 0;
bool g_bDry = false;
int g_nHit = -1;

template <int N>
void FxTrampoline( const CEffectData &data )
{
	g_nHit = N;
	if ( g_bDry )
		return;
	FxProbeLog( "fxrun %d", N );
	g_pOrig[N]( data );
}

template <int N>
struct TrampolineTable
{
	static void Fill( ClientEffectCallback *pOut )
	{
		TrampolineTable<N - 1>::Fill( pOut );
		pOut[N - 1] = &FxTrampoline<N - 1>;
	}
};

template <>
struct TrampolineTable<0>
{
	static void Fill( ClientEffectCallback * ) {}
};

ClientEffectCallback g_pTramp[kMaxRegs];

} // namespace

CON_COMMAND( cs_cl_probe_fxreg, "c2-fx probe (debug): log the client effect registrations and trace each callback" )
{
	if ( FxProbeFd() < 0 )
		return;
	if ( g_nRegs > 0 )
	{
		FxProbeLog( "fxreg-again %d", g_nRegs );
		return;
	}
	TrampolineTable<kMaxRegs>::Fill( g_pTramp );
	int i = 0;
	for ( CClientEffectRegistration *pReg = CClientEffectRegistration::s_pHead; pReg; pReg = pReg->m_pNext, ++i )
	{
		FxProbeLog( "fxreg %d %s", i, pReg->m_pEffectName );
		if ( i < kMaxRegs )
		{
			g_pOrig[i] = pReg->m_pFunction;
			pReg->m_pFunction = g_pTramp[i];
		}
	}
	g_nRegs = i;
	FxProbeLog( "fxreg-count %d traced=%d", i, i < kMaxRegs ? i : kMaxRegs );
}

CON_COMMAND( cs_cl_probe_fxlookup,
			 "c2-fx probe (debug): which registration stock's walk picks for each name, without running it" )
{
	if ( FxProbeFd() < 0 || g_nRegs == 0 )
		return;
	for ( int i = 1; i < args.ArgC(); ++i )
	{
		const char *pName = args[i];
		CEffectData data;
		g_bDry = true;
		g_nHit = -1;
		DispatchEffectToCallback( pName, data );
		g_bDry = false;
		FxProbeLog( "fxlookup %d %s", g_nHit, pName );
	}
}

//-----------------------------------------------------------------------------
// IEffects (EffectsClient.cpp, probe copy): the two arms a live session does
// not reach on its own. The probe copy logs every call, the suppression
// decision and the FX_* call it made.
//   cs_cl_probe_fxsuppress <0|1>        IPredictionSystem::SuppressEvents, as a
//                                       re-prediction does (CanPredict() false)
//   cs_cl_probe_fxsuppresssound <0|1>   IEffects::SuppressEffectsSounds
//-----------------------------------------------------------------------------
CON_COMMAND( cs_cl_probe_fxsuppress, "c2-fx probe (debug): IPredictionSystem::SuppressEvents( arg )" )
{
	if ( FxProbeFd() < 0 || args.ArgC() < 2 )
		return;
	const bool b = atoi( args.Arg( 1 ) ) != 0;
	IPredictionSystem::SuppressEvents( b );
	FxProbeLog( "ie-arm suppress-events %d", b ? 1 : 0 );
}

CON_COMMAND( cs_cl_probe_fxsuppresssound, "c2-fx probe (debug): g_pEffects->SuppressEffectsSounds( arg )" )
{
	if ( FxProbeFd() < 0 || args.ArgC() < 2 || !g_pEffects )
		return;
	g_pEffects->SuppressEffectsSounds( atoi( args.Arg( 1 ) ) != 0 );
}

// IEffects::MuzzleFlash for each type, at a fixed place: the switch's other arms
// (gunship, strider, an unknown type) are not sent by anything in dm_lockdown.
CON_COMMAND( cs_cl_probe_fxmuzzle, "c2-fx probe (debug): g_pEffects->MuzzleFlash( (0 0 64), (0 90 0), 1.5, type )" )
{
	if ( FxProbeFd() < 0 || args.ArgC() < 2 || !g_pEffects )
		return;
	g_pEffects->MuzzleFlash( Vector( 0, 0, 64 ), QAngle( 0, 90, 0 ), 1.5f, atoi( args.Arg( 1 ) ) );
}

//-----------------------------------------------------------------------------
// The client-side effect list (clientsideeffects*.cpp, fx_line/quad/staticline/
// discreetline.cpp, fx_tracer.cpp, probe copies): logged random draws, and a
// battery of effects in front of the camera, so their draws are recorded frame
// by frame with the mesh they build (fx_probe_mesh.h).
//-----------------------------------------------------------------------------
float FxProbeRandomFloat( float flLow, float flHigh )
{
	const float f = random->RandomFloat( flLow, flHigh );
	FxProbeLog( "fxr f %.9g %.9g %.9g", flLow, flHigh, f );
	return f;
}

int FxProbeRandomInt( int iLow, int iHigh )
{
	const int i = random->RandomInt( iLow, iHigh );
	FxProbeLog( "fxr i %d %d %d", iLow, iHigh, i );
	return i;
}

#include "view.h"
#include "fx_line.h"
#include "fx_quad.h"
#include "clientsideeffects.h"
#include "shareddefs.h"

void FX_TracerSound( const Vector &start, const Vector &end, int iTracerType );

namespace
{

Vector Ahead( float flForward, float flRight, float flUp )
{
	Vector f, r, u;
	AngleVectors( MainViewAngles(), &f, &r, &u );
	return MainViewOrigin() + f * flForward + r * flRight + u * flUp;
}

} // namespace

CON_COMMAND( cs_cl_probe_fxbattery,
			 "c2-fx probe (debug): add a fixed battery of client-side effects in front of the camera" )
{
	if ( FxProbeFd() < 0 )
		return;
	const int nSet = args.ArgC() > 1 ? atoi( args.Arg( 1 ) ) : 0;
	FxProbeLog( "fxb-begin %d view=%.9g %.9g %.9g ang=%.9g %.9g %.9g", nSet, MainViewOrigin().x, MainViewOrigin().y,
				MainViewOrigin().z, MainViewAngles().x, MainViewAngles().y, MainViewAngles().z );

	// Lines: still, moving, alpha and scale ramps, two materials.
	const char *pLineMats[] = { "effects/spark", "effects/gunshiptracer" };
	for ( int i = 0; i < 4; ++i )
	{
		FXLineData_t data;
		data.m_vecStart = Ahead( 200.0f, -60.0f + 40.0f * i, 20.0f );
		data.m_vecEnd = Ahead( 260.0f + 10.0f * i, -40.0f + 40.0f * i, -10.0f );
		data.m_vecStartVelocity = ( i & 1 ) ? Vector( 0, 0, 40.0f * i ) : vec3_origin;
		data.m_vecEndVelocity = ( i & 2 ) ? Vector( 30.0f, -20.0f, 0 ) : vec3_origin;
		data.m_flStartAlpha = 1.0f - 0.2f * i;
		data.m_flEndAlpha = 0.1f * i;
		data.m_flStartScale = 1.0f + i;
		data.m_flEndScale = 4.0f - i;
		data.m_flDieTime = 0.4f + 0.3f * i + 0.1f * nSet;
		data.m_flLifeTime = 0.0f;
		data.m_pMaterial = materials->FindMaterial( pLineMats[i & 1], TEXTURE_GROUP_CLIENT_EFFECTS );
		FX_AddLine( data );
	}

	// Quads: every flag combination, yaw spin, a zero-alpha one (no draw).
	const char *pQuadMats[] = { "effects/splashwake1", "effects/fire_cloud1", "effects/blood_core" };
	for ( int i = 0; i < 8; ++i )
	{
		const Vector n = ( i % 3 == 0 ) ? Vector( 0, 0, 1 )
			: ( i % 3 == 1 )            ? Vector( 0.6f, 0.0f, 0.8f )
										: Vector( -0.48f, 0.6f, 0.64f );
		const float flAlpha0 = ( i == 7 ) ? 0.0f : 1.0f;
		const float flAlpha1 = ( i == 7 ) ? 0.0f : 0.05f * i;
		FX_AddQuad( Ahead( 240.0f, -90.0f + 26.0f * i, 30.0f ), n, 8.0f + 4.0f * i, 40.0f - 3.0f * i, 0.3f + 0.05f * i,
					flAlpha0, flAlpha1, 0.4f + 0.05f * i, 30.0f * i, ( i & 1 ) ? 90.0f : -45.0f,
					Vector( 1.0f, 0.8f - 0.1f * ( i % 4 ), 0.5f ), 0.5f + 0.2f * i + 0.1f * nSet, pQuadMats[i % 3],
					(unsigned int)i );
	}

	// Static lines: each flip.
	for ( int i = 0; i < 4; ++i )
		FX_AddStaticLine( Ahead( 180.0f, -30.0f + 20.0f * i, -30.0f ), Ahead( 220.0f, -10.0f + 20.0f * i, 10.0f ),
						  2.0f + i, 0.3f + 0.2f * i, "effects/spark", (unsigned char)i );

	// Discreet lines: clipped and not.
	for ( int i = 0; i < 4; ++i )
	{
		Vector dir = Ahead( 300.0f, 20.0f * i, -10.0f * i ) - Ahead( 100.0f, -40.0f, 20.0f );
		VectorNormalize( dir );
		FX_AddDiscreetLine( Ahead( 100.0f, -40.0f, 20.0f ), dir, 1500.0f + 500.0f * i, 64.0f + 16.0f * i,
							( i & 1 ) ? 200.0f : 0.0f, 0.75f + 0.05f * i, 0.4f + 0.1f * i, "effects/spark" );
	}

	// Tracers and the player's own (FX_Tracer, FX_PlayerTracer) were here; they
	// left client.so with clientsideeffects_test.cpp (13df3d85's successor), and
	// effectlist-stock.tsv.gz holds what they recorded.
	FxProbeLog( "fxb-end %d", nSet );
}

CON_COMMAND( cs_cl_probe_fxtracersound, "c2-fx probe (debug): FX_TracerSound for each tracer type, near and far" )
{
	if ( FxProbeFd() < 0 )
		return;
	const int types[] = { TRACER_TYPE_DEFAULT, TRACER_TYPE_GUNSHIP,     TRACER_TYPE_STRIDER,
						  TRACER_TYPE_GAUSS,   TRACER_TYPE_WATERBULLET, 0 };
	const float flPass = args.ArgC() > 1 ? atof( args.Arg( 1 ) ) : 10.0f;
	for ( int t = 0; t < 6; ++t )
	{
		const Vector s = Ahead( -300.0f, flPass, 0.0f );
		const Vector e = Ahead( 300.0f, flPass, -8.0f );
		FX_TracerSound( s, e, types[t] );
	}
}

CON_COMMAND( cs_cl_probe_fxtracercb,
			 "c2-fx probe (debug): dispatch Tracer, ParticleTracer and TracerSound with fixed data" )
{
	if ( FxProbeFd() < 0 )
		return;
	const char *names[] = { "Tracer", "ParticleTracer", "TracerSound" };
	for ( int n = 0; n < 3; ++n )
	{
		for ( int f = 0; f < 4; ++f )
		{
			CEffectData data;
			data.m_vStart = Ahead( 30.0f, -100.0f, 0.0f );
			data.m_vOrigin = Ahead( 800.0f, 100.0f, 20.0f );
			data.m_fFlags = f;
			data.m_flScale = ( f & 1 ) ? 0.0f : 6000.0f;
			data.m_nAttachmentIndex = 1;
			data.m_nHitBox = 0;
			data.m_hEntity = INVALID_EHANDLE;
			DispatchEffectToCallback( names[n], data );
		}
	}
}

// fx_shelleject.cpp (probe copy): the three shell-eject effects, dispatched
// for each kind of owner: the local player, the world, an empty slot, none.
CON_COMMAND( cs_cl_probe_fxshells,
			 "c2-fx probe (debug): dispatch ShellEject, RifleShellEject, ShotgunShellEject for several owners" )
{
	if ( FxProbeFd() < 0 )
		return;
	const char *names[] = { "ShellEject", "RifleShellEject", "ShotgunShellEject" };
	const int owners[] = { engine->GetLocalPlayer(), 0, 1000, -1 };
	for ( int n = 0; n < 3; ++n )
	{
		for ( int o = 0; o < 4; ++o )
		{
			CEffectData data;
			data.m_vOrigin = Ahead( 40.0f, 10.0f * n, -5.0f * o );
			data.m_vAngles = QAngle( 10.0f * o, 90.0f + 20.0f * n, 5.0f );
			data.m_hEntity = owners[o] >= 0 ? SourceSharp_ClientHandleFromIndex( owners[o] ) : INVALID_EHANDLE;
			DispatchEffectToCallback( names[n], data );
		}
	}
}
