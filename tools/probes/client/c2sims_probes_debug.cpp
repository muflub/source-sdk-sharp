//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — lane c2-sims's oracle probes (debug builds only) ====//
//
// DEBUG BUILDS ONLY, AND OPT-IN, as shell_probes_debug.cpp beside it: compiled
// into client.so only when farm/debug/build is configured with
// -DSS_CLIENT_PROBES=ON. Each probe records the STOCK code a lane c2-sims port
// replaces (plan_client_novalve.md §5.1 step 1) into a fixture under
// $SOURCESHARP_C2SIMS_FIXTURE_DIR; nothing runs unless that is set.
//
//   cs_cl_probe_decals   decals.cpp's CDecalEmitterSystem, through the
//                        `decalsystem` pointer every caller uses: the script
//                        it read, the precache index of every entry, five
//                        weighted picks per group with the random draws each
//                        one took (the client's `random` stream is wrapped for
//                        the duration), the name of every index, and the
//                        translation of five names over all 256 materials.
//                        Writes decals-stock.tsv.
//
//=============================================================================//

#include "cbase.h"
#include "decals.h"
#include "iefx.h"
#include "filesystem.h"
#include "KeyValues.h"
#include "vstdlib/random.h"
#include "sharedInterface.h"

#include "tier0/memdbgon.h"

namespace
{

FILE *OpenFixture( const char *pName )
{
	const char *pDir = getenv( "SOURCESHARP_C2SIMS_FIXTURE_DIR" );
	if ( !pDir || !*pDir )
	{
		Msg( "[c2-sims probe] SOURCESHARP_C2SIMS_FIXTURE_DIR is not set; %s not written\n", pName );
		return NULL;
	}
	char path[ 1024 ];
	Q_snprintf( path, sizeof( path ), "%s/%s", pDir, pName );
	FILE *f = fopen( path, "w" );
	if ( !f )
		Msg( "[c2-sims probe] cannot open %s\n", path );
	return f;
}

//-----------------------------------------------------------------------------
// The client's `random`, forwarded, with every RandomFloat logged into a line
// buffer while armed. Only RandomFloat is drawn by the decal pick; the others
// are counted so a draw the fixture does not explain is visible.
//-----------------------------------------------------------------------------
class CRecordingRandom : public IUniformRandomStream
{
public:
	explicit CRecordingRandom( IUniformRandomStream *pInner ) : m_pInner( pInner ) { Reset(); }

	void Reset() { m_szDraws[ 0 ] = '\0'; m_nDraws = 0; m_nOther = 0; }

	virtual void SetSeed( int iSeed ) { ++m_nOther; m_pInner->SetSeed( iSeed ); }
	virtual float RandomFloat( float flMin, float flMax )
	{
		float v = m_pInner->RandomFloat( flMin, flMax );
		size_t n = strlen( m_szDraws );
		Q_snprintf( m_szDraws + n, sizeof( m_szDraws ) - n, "%s%.9g,%.9g,%.9g", n ? ";" : "", flMin, flMax, v );
		++m_nDraws;
		return v;
	}
	virtual int RandomInt( int iMin, int iMax ) { ++m_nOther; return m_pInner->RandomInt( iMin, iMax ); }
	virtual float RandomFloatExp( float flMin, float flMax, float flExp ) { ++m_nOther; return m_pInner->RandomFloatExp( flMin, flMax, flExp ); }

	IUniformRandomStream *m_pInner;
	char m_szDraws[ 4096 ];
	int m_nDraws;
	int m_nOther;
};

const char *kDecalScript = "scripts/decals_subrect.txt";

} // namespace

CON_COMMAND( cs_cl_probe_decals, "c2-sims probe (debug): record decals.cpp's answers into $SOURCESHARP_C2SIMS_FIXTURE_DIR/decals-stock.tsv" )
{
	if ( !decalsystem || !effects || !random )
	{
		Msg( "[c2-sims probe] decals: decalsystem=%p effects=%p random=%p\n", (void *)decalsystem, (void *)effects, (void *)random );
		return;
	}
	FILE *f = OpenFixture( "decals-stock.tsv" );
	if ( !f )
		return;

	fprintf( f, "# c2-sims decals oracle: stock decals.cpp (CDecalEmitterSystem) through `decalsystem`.\n" );
	fprintf( f, "# S <group> <entry> <weight>      the script, as stock's KeyValues read it\n" );
	fprintf( f, "# M <material key> <group>        its TranslationData section\n" );
	fprintf( f, "# P <entry> <precache index>      effects->Draw_DecalIndexFromName, as stock's LevelInitPreEntity\n" );
	fprintf( f, "# I <group> <answer> <draws>      GetDecalIndexForName; draws are min,max,value;... ('-' for none)\n" );
	fprintf( f, "# N <index> <name>                GetDecalNameForIndex ('-' for \"\")\n" );
	fprintf( f, "# T <name> <material> <answer>    TranslateDecalForGameMaterial ('=' when it answered its input, '-' for \"\")\n" );

	// The script, read as stock reads it (decals.cpp LoadDecalsFromScript).
	CUtlVector< CUtlString > groups;
	CUtlVector< CUtlString > entries;
	KeyValues *kv = new KeyValues( kDecalScript );
	if ( kv->LoadFromFile( filesystem, kDecalScript ) )
	{
		for ( KeyValues *p = kv; p; p = p->GetNextKey() )
		{
			if ( !p->GetFirstSubKey() )
				continue;
			if ( !Q_stricmp( p->GetName(), "TranslationData" ) )
			{
				for ( KeyValues *sub = p->GetFirstSubKey(); sub; sub = sub->GetNextKey() )
					fprintf( f, "M\t%s\t%s\n", sub->GetName(), *sub->GetString() ? sub->GetString() : "-" );
				continue;
			}
			groups.AddToTail( CUtlString( p->GetName() ) );
			for ( KeyValues *sub = p->GetFirstSubKey(); sub; sub = sub->GetNextKey() )
			{
				fprintf( f, "S\t%s\t%s\t%.9g\n", p->GetName(), sub->GetName(), sub->GetFloat() );
				entries.AddToTail( CUtlString( sub->GetName() ) );
			}
		}
	}
	kv->deleteThis();

	int nMaxIndex = -1;
	for ( int i = 0; i < entries.Count(); ++i )
	{
		int idx = effects->Draw_DecalIndexFromName( (char *)entries[ i ].Get() );
		fprintf( f, "P\t%s\t%d\n", entries[ i ].Get(), idx );
		if ( idx > nMaxIndex )
			nMaxIndex = idx;
	}

	// The picks, with the draws each took. Unknown and empty names too.
	CRecordingRandom rec( random );
	IUniformRandomStream *pSaved = random;
	random = &rec;
	groups.AddToTail( CUtlString( "NoSuchDecalGroup" ) );
	groups.AddToTail( CUtlString( "" ) );
	int nPicks = 0, nOther = 0;
	for ( int g = 0; g < groups.Count(); ++g )
	{
		for ( int k = 0; k < 5; ++k )
		{
			rec.Reset();
			int answer = decalsystem->GetDecalIndexForName( groups[ g ].Get() );
			fprintf( f, "I\t%s\t%d\t%s\n", groups[ g ].Get(), answer, rec.m_nDraws ? rec.m_szDraws : "-" );
			nOther += rec.m_nOther;
			++nPicks;
		}
	}
	random = pSaved;

	for ( int i = -2; i <= nMaxIndex + 2; ++i )
	{
		const char *pName = decalsystem->GetDecalNameForIndex( i );
		fprintf( f, "N\t%d\t%s\n", i, ( pName && *pName ) ? pName : "-" );
	}

	static const char *kNames[] = { "Impact.Concrete", "impact.concrete", "Impact.Metal", "Scorch", "NoSuchDecalGroup" };
	for ( int n = 0; n < (int)ARRAYSIZE( kNames ); ++n )
	{
		for ( int m = 0; m < 256; ++m )
		{
			const char *pAnswer = decalsystem->TranslateDecalForGameMaterial( kNames[ n ], (unsigned char)m );
			const char *pOut = ( pAnswer == kNames[ n ] ) ? "=" : ( ( pAnswer && *pAnswer ) ? pAnswer : "-" );
			fprintf( f, "T\t%s\t%d\t%s\n", kNames[ n ], m, pOut );
		}
	}
	fclose( f );
	Msg( "[c2-sims probe] decals: %d groups, %d entries, %d picks, %d non-float draws, max index %d\n",
		groups.Count() - 2, entries.Count(), nPicks, nOther, nMaxIndex );
}

//=============================================================================//
// THE SIM RECORDER: what tools/probes/c2sims-*-probe.diff patches into one of
// OUR sim files calls, so a stock simulation can be replayed in C#.
//
//   cs_cl_probe_sim <name> <frames>   opens <name>-stock.tsv and records the
//                                     next <frames> frames the patched sim
//                                     reports, then closes it
//
// While a patched sim's Update runs it arms the draw recorder: every draw
// through the client's `random` and through vstdlib's installed stream (the
// free RandomInt that client.so's rand() answers from, randoverride.cpp, which
// is what FRand calls) is written as a row, in order, so the managed replay is
// handed the same numbers in the same order. The recorder is installed as the
// free functions' stream and as `random` for the recording's duration, and
// draws from its own CUniformRandomStream seeded 0x5eed: a fixed seed.
//=============================================================================//

#include <stdarg.h>

namespace
{
FILE *g_pSimFile = NULL;
int g_nSimFramesLeft = 0;
int g_nSimFrame = 0;
bool g_bSimArmed = false;
IUniformRandomStream *g_pSimSavedRandom = NULL;

class CSimRecordingRandom : public IUniformRandomStream
{
public:
	IUniformRandomStream *m_pInner = NULL;
	virtual void SetSeed( int iSeed ) { m_pInner->SetSeed( iSeed ); }
	virtual float RandomFloat( float flMin, float flMax )
	{
		float v = m_pInner->RandomFloat( flMin, flMax );
		if ( g_bSimArmed && g_pSimFile )
			fprintf( g_pSimFile, "RF\t%.9g\t%.9g\t%.9g\n", flMin, flMax, v );
		return v;
	}
	virtual int RandomInt( int iMin, int iMax )
	{
		int v = m_pInner->RandomInt( iMin, iMax );
		if ( g_bSimArmed && g_pSimFile )
			fprintf( g_pSimFile, "RI\t%d\t%d\t%d\n", iMin, iMax, v );
		return v;
	}
	virtual float RandomFloatExp( float flMin, float flMax, float flExp )
	{
		float v = m_pInner->RandomFloatExp( flMin, flMax, flExp );
		if ( g_bSimArmed && g_pSimFile )
			fprintf( g_pSimFile, "RX\t%.9g\t%.9g\t%.9g\t%.9g\n", flMin, flMax, flExp, v );
		return v;
	}
};
CSimRecordingRandom g_SimRandom;

// The stream every draw comes from while recording, seeded fixed: the client's
// `random` may itself forward to vstdlib's free functions (whose stream this
// recorder is installed as), so forwarding to it would recurse.
CUniformRandomStream g_SimStream;
} // namespace

bool C2SimsProbe_On() { return g_pSimFile != NULL; }
int C2SimsProbe_Frame() { return g_nSimFrame; }
void C2SimsProbe_Arm( bool bArm ) { g_bSimArmed = bArm && g_pSimFile; }

void C2SimsProbe_Row( const char *pFmt, ... ) __attribute__(( format( printf, 1, 2 ) ));
void C2SimsProbe_Row( const char *pFmt, ... )
{
	if ( !g_pSimFile )
		return;
	va_list ap;
	va_start( ap, pFmt );
	vfprintf( g_pSimFile, pFmt, ap );
	va_end( ap );
	fputc( '\n', g_pSimFile );
}

// Called once per frame by the patched sim, at the top of its Update: closes
// the file once <frames> whole frames are in it (so the last frame recorded is
// complete), else counts this frame in. False: not recording.
bool C2SimsProbe_BeginFrame()
{
	if ( !g_pSimFile )
		return false;
	if ( g_nSimFramesLeft <= 0 )
	{
		fclose( g_pSimFile );
		g_pSimFile = NULL;
		g_bSimArmed = false;
		if ( g_pSimSavedRandom )
		{
			random = g_pSimSavedRandom;
			g_pSimSavedRandom = NULL;
			InstallUniformRandomStream( NULL );
		}
		Msg( "[c2-sims probe] sim: recorded %d frame(s)\n", g_nSimFrame );
		return false;
	}
	--g_nSimFramesLeft;
	++g_nSimFrame;
	return true;
}

CON_COMMAND( cs_cl_probe_sim, "c2-sims probe (debug): record a patched sim into $SOURCESHARP_C2SIMS_FIXTURE_DIR/<name>-stock.tsv. <name> <frames>" )
{
	if ( args.ArgC() < 3 || g_pSimFile )
	{
		Msg( "[c2-sims probe] usage: cs_cl_probe_sim <name> <frames> (one recording at a time)\n" );
		return;
	}
	char name[ 128 ];
	Q_snprintf( name, sizeof( name ), "%s-stock.tsv", args.Arg( 1 ) );
	g_pSimFile = OpenFixture( name );
	if ( !g_pSimFile )
		return;
	g_nSimFramesLeft = atoi( args.Arg( 2 ) );
	g_nSimFrame = 0;
	if ( random && !g_pSimSavedRandom )
	{
		g_pSimSavedRandom = random;
		g_SimStream.SetSeed( 0x5eed );
		g_SimRandom.m_pInner = &g_SimStream;
		random = &g_SimRandom;
		InstallUniformRandomStream( &g_SimRandom );
	}
	fprintf( g_pSimFile, "# c2-sims %s oracle, recorded by a patched sim (tools/probes/c2sims-%s-probe.diff)\n", args.Arg( 1 ), args.Arg( 1 ) );
	Msg( "[c2-sims probe] sim: recording %s for %d frame(s)\n", args.Arg( 1 ), g_nSimFramesLeft );
}

//=============================================================================//
// THE SOUND ENVELOPE RECORDER (sound cluster 17): stock's client
// CSoundEnvelopeController (game/shared/soundenvelope.cpp) driven by a fixed
// script on one patch, its volume and pitch read back once a frame.
//
//   cs_cl_probe_sndenv <frames>   opens soundenvelope-stock.tsv, creates a
//                                 patch on the local player and plays the
//                                 script; C2SimsProbe_Tick (patched into a
//                                 PreRender by tools/probes/c2sims-sndenv-probe.diff)
//                                 issues the timed commands and logs a row
//
// Rows: C <curtime> <command> <args...>  each controller call, when it was made
//       W <curtime> <frametime> <volume> <pitch>   once a frame, after the
//                                 controller's own SystemUpdate (client Update)
//=============================================================================//

#include "soundenvelope.h"
#include "c_recipientfilter.h"

namespace
{
FILE *g_pEnvFile = NULL;
int g_nEnvFrames = 0;
int g_nEnvFrame = 0;
CSoundPatch *g_pEnvPatch = NULL;

void EnvRow( const char *pFmt, ... ) __attribute__(( format( printf, 1, 2 ) ));
void EnvRow( const char *pFmt, ... )
{
	va_list ap;
	va_start( ap, pFmt );
	vfprintf( g_pEnvFile, pFmt, ap );
	va_end( ap );
	fputc( '\n', g_pEnvFile );
}
}

namespace { void FrictionTick(); }

void C2SimsProbe_Tick()
{
	FrictionTick();
	if ( !g_pEnvFile || !g_pEnvPatch )
		return;
	CSoundEnvelopeController &c = CSoundEnvelopeController::GetController();
	const float t = gpGlobals->curtime;
	const int f = ++g_nEnvFrame;

	// The timed half of the script.
	if ( f == 40 )
	{
		EnvRow( "C\t%.9g\tvolume\t0.4\t0.25", t );
		c.SoundChangeVolume( g_pEnvPatch, 0.4f, 0.25f );
	}
	if ( f == 60 )
	{
		envelopePoint_t pts[3] = { { 140, 140, 0.2f, 0.2f }, { 60, 60, 0.3f, 0.3f }, { 110, 110, 0.15f, 0.15f } };
		const float total = c.SoundPlayEnvelope( g_pEnvPatch, SOUNDCTRL_CHANGE_PITCH, pts, 3 );
		EnvRow( "C\t%.9g\tenvelope\t%.9g", t, total );
	}
	if ( f == 110 )
	{
		EnvRow( "C\t%.9g\tcommand\t0.1\t0\t0.2\t0.9", t );
		c.CommandAdd( g_pEnvPatch, 0.1f, SOUNDCTRL_CHANGE_VOLUME, 0.2f, 0.9f );
	}
	if ( f == 140 )
	{
		EnvRow( "C\t%.9g\tplay\t0.5\t95", t );
		c.Play( g_pEnvPatch, 0.5f, 95.0f );
	}
	if ( f == 160 )
	{
		EnvRow( "C\t%.9g\tfadeout\t0.4", t );
		c.SoundFadeOut( g_pEnvPatch, 0.4f, false );
	}

	EnvRow( "W\t%.9g\t%.9g\t%.9g\t%.9g", t, gpGlobals->frametime, c.SoundGetVolume( g_pEnvPatch ), c.SoundGetPitch( g_pEnvPatch ) );

	if ( f >= g_nEnvFrames )
	{
		c.SoundDestroy( g_pEnvPatch );
		g_pEnvPatch = NULL;
		fclose( g_pEnvFile );
		g_pEnvFile = NULL;
		Msg( "[c2-sims probe] sndenv: recorded %d frame(s)\n", f );
	}
}

CON_COMMAND( cs_cl_probe_sndenv, "c2-sims probe (debug): record the client sound envelope controller into $SOURCESHARP_C2SIMS_FIXTURE_DIR/soundenvelope-stock.tsv. <frames>" )
{
	if ( args.ArgC() < 2 || g_pEnvFile )
		return;
	g_pEnvFile = OpenFixture( "soundenvelope-stock.tsv" );
	if ( !g_pEnvFile )
		return;
	g_nEnvFrames = atoi( args.Arg( 1 ) );
	g_nEnvFrame = 0;
	fprintf( g_pEnvFile, "# c2-sims soundenvelope oracle: stock CSoundEnvelopeController on the client (tools/probes/c2sims-sndenv-probe.diff)\n" );
	CSoundEnvelopeController &c = CSoundEnvelopeController::GetController();
	CLocalPlayerFilter filter;
	const float t = gpGlobals->curtime;
	g_pEnvPatch = c.SoundCreate( filter, engine->GetLocalPlayer(), CHAN_STATIC, "ambient/wind/wind1.wav", ATTN_NORM );
	EnvRow( "C\t%.9g\tcreate", t );
	EnvRow( "C\t%.9g\tplay\t0\t100", t );
	c.Play( g_pEnvPatch, 0.0f, 100.0f );
	EnvRow( "C\t%.9g\tvolume\t1\t0.5", t );
	c.SoundChangeVolume( g_pEnvPatch, 1.0f, 0.5f );
	EnvRow( "C\t%.9g\tpitch\t150\t1", t );
	c.SoundChangePitch( g_pEnvPatch, 150.0f, 1.0f );
	EnvRow( "C\t%.9g\tcommand\t0.75\t1\t0.3\t80", t );
	c.CommandAdd( g_pEnvPatch, 0.75f, SOUNDCTRL_CHANGE_PITCH, 0.3f, 80.0f );
	Msg( "[c2-sims probe] sndenv: recording %d frame(s)\n", g_nEnvFrames );
}

//=============================================================================//
// THE FRICTION STIMULUS (sound cluster 17's live check): the client physics.cpp
// friction path, driven directly. No map gives this client a sliding client-side
// physics object on demand, so the probe calls PhysFrictionSound itself once a
// frame (through C2SimsProbe_Tick, tools/probes/c2sims-sndenv-probe.diff) for
// <frames> frames with a rising-and-falling volume, keyed on a probe-owned
// address as its "entity", on the world physics object. Then it stops calling,
// and physics.cpp's UpdateFrictionSounds shuts the patch down 0.1 s later --
// the whole create / play / change / destroy cycle through the managed
// controller. cs_cl_crossings before and after shows each op crossed.
//
//   cs_cl_probe_friction <frames> [sound]   default sound Metal_Box.ScrapeRough
//=============================================================================//

#include "gamesound.h"
#include "physics_shared.h"

namespace
{
int g_nFrictionFrames = 0;
int g_nFrictionFrame = 0;
int g_FrictionKey = 0;	// its address is the probe's "entity"
char g_szFrictionSound[ 128 ] = "Metal_Box.ScrapeRough";
HSOUNDSCRIPTHANDLE g_hFriction = SOUNDEMITTER_INVALID_HANDLE;

void FrictionTick()
{
	if ( g_nFrictionFrame >= g_nFrictionFrames )
		return;
	if ( !g_PhysWorldObject )
	{
		Msg( "[c2-sims probe] friction: no world physics object\n" );
		g_nFrictionFrames = 0;
		return;
	}
	const int f = g_nFrictionFrame++;
	const float flVolume = 0.3f + 0.6f * sinf( 3.14159265f * f / (float)g_nFrictionFrames );
	PhysFrictionSound( (IGameEntity *)&g_FrictionKey, g_PhysWorldObject, g_szFrictionSound, g_hFriction, flVolume );
	if ( g_nFrictionFrame == g_nFrictionFrames )
		Msg( "[c2-sims probe] friction: drove %d frame(s) of %s (handle %d)\n", g_nFrictionFrames, g_szFrictionSound, (int)g_hFriction );
}
}

CON_COMMAND( cs_cl_probe_friction, "c2-sims probe (debug): drive the client friction sound for <frames> frames. <frames> [sound]" )
{
	if ( args.ArgC() < 2 )
		return;
	g_nFrictionFrames = atoi( args.Arg( 1 ) );
	g_nFrictionFrame = 0;
	if ( args.ArgC() > 2 )
		Q_strncpy( g_szFrictionSound, args.Arg( 2 ), sizeof( g_szFrictionSound ) );
	CSoundParameters params;
	const bool bKnown = GameSound_GetParameters( g_szFrictionSound, g_hFriction, params, NULL );
	Msg( "[c2-sims probe] friction: %s is %s\n", g_szFrictionSound, bKnown ? "a known sound script" : "NOT a known sound script" );
}
