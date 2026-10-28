//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE PRECIPITATION ORACLE: stock's client weather -- CClient_Precipitation's
// rain, snow and ash, and CSnowFallManager's snowfall -- run offline from the
// PRISTINE game/client/c_effects.cpp, seeded, over a scripted world, and
// written down as the golden src/test/client/Fixtures/precipitation-stock.tsv.
//
// WHY OFFLINE. In this build every emission path of those classes is dead:
// ComputeEmissionArea, EmitParticles, CreateAshParticle and
// CSnowFallManager::CreateSnowFall each start by asking
// C_BasePlayer::GetLocalPlayer(), which answers NULL in this mod (the viewer
// is a C_StandalonePlayer). A live recording can only record nothing. Under
// the "port dead stock arms" rule they are ported, not deleted, so the
// behaviour to port is taken from stock itself, run where it is alive: here,
// with a stand-in player, world and particle manager (precipitation_stub/,
// whose precip_harness.h says what is stock and what is stand-in).
//
// THE SUBJECT IS INCLUDED, NOT LINKED. c_effects.cpp declares its classes in
// the .cpp, so the harness includes the file into this translation unit to be
// able to make a CClient_Precipitation and read its particle list.
// `private`/`protected` are defined to `public` around the include for the
// same reason; that changes access and nothing else.
//
// BOTH SIDES ARE PINNED TO THE GOLDEN. This test asserts that stock produces
// exactly it; SourceSharp.Tests' PrecipitationOracleTests asserts the managed
// port does. Regenerating the golden to make this test green deletes the
// check: the golden changes only when the scenario does, and then on purpose
// (touch precipitation_golden.regen beside this file, run, delete the marker,
// and say why in the commit).
//
//=============================================================================

#include <stdarg.h>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <cmath>
#include "valve_sdk.h"

#include "gtest/gtest.h"
#include "../../native/ss_ci_skip.h"

#include "precip_harness.h"

#include "mathlib_test_env.h"

//-----------------------------------------------------------------------------
// The log.
//-----------------------------------------------------------------------------
static std::string g_Log;
static bool g_bLogTracers = false;

void PrecipLog( const char *pFmt, ... )
{
	char line[1024];
	va_list ap;
	va_start( ap, pFmt );
	vsnprintf( line, sizeof( line ), pFmt, ap );
	va_end( ap );
	g_Log += line;
	g_Log += '\n';
}

//-----------------------------------------------------------------------------
// The console: ConVar::SetValue tells g_pCVar's global change callbacks, so the
// scenario's three convar settings need one there. Nothing else is asked of it.
//-----------------------------------------------------------------------------
#include "icvar.h"

class CPrecipCvar final : public ICvar
{
public:
	virtual bool Connect( CreateInterfaceFn ) { return true; }

	virtual void Disconnect() {}

	virtual void *QueryInterface( const char * ) { return NULL; }

	virtual InitReturnVal_t Init() { return INIT_OK; }

	virtual void Shutdown() {}

	virtual CVarDLLIdentifier_t AllocateDLLIdentifier() { return 1; }

	virtual void RegisterConCommand( ConCommandBase * ) {}

	virtual void UnregisterConCommand( ConCommandBase * ) {}

	virtual void UnregisterConCommands( CVarDLLIdentifier_t ) {}

	virtual const char *GetCommandLineValue( const char * ) { return NULL; }

	virtual ConCommandBase *FindCommandBase( const char * ) { return NULL; }

	virtual const ConCommandBase *FindCommandBase( const char * ) const { return NULL; }

	virtual ConVar *FindVar( const char * ) { return NULL; }

	virtual const ConVar *FindVar( const char * ) const { return NULL; }

	virtual ConCommand *FindCommand( const char * ) { return NULL; }

	virtual const ConCommand *FindCommand( const char * ) const { return NULL; }

	virtual ConCommandBase *GetCommands( void ) { return NULL; }

	virtual const ConCommandBase *GetCommands( void ) const { return NULL; }

	virtual void InstallGlobalChangeCallback( FnChangeCallback_t ) {}

	virtual void RemoveGlobalChangeCallback( FnChangeCallback_t ) {}

	virtual void CallGlobalChangeCallbacks( ConVar *, const char *, float ) {}

	virtual void InstallConsoleDisplayFunc( IConsoleDisplayFunc * ) {}

	virtual void RemoveConsoleDisplayFunc( IConsoleDisplayFunc * ) {}

	virtual void ConsoleColorPrintf( const Color &, const char *, ... ) const {}

	virtual void ConsolePrintf( const char *, ... ) const {}

	virtual void ConsoleDPrintf( const char *, ... ) const {}

	virtual void RevertFlaggedConVars( int ) {}

	virtual void InstallCVarQuery( ICvarQuery * ) {}

	virtual void PublishToVXConsole() {}

	virtual bool IsMaterialThreadSetAllowed() const { return true; }

	virtual void QueueMaterialThreadSetValue( ConVar *, const char * ) {}

	virtual void QueueMaterialThreadSetValue( ConVar *, int ) {}

	virtual void QueueMaterialThreadSetValue( ConVar *, float ) {}

	virtual bool HasQueuedMaterialThreadConVarSets() const { return false; }

	virtual int ProcessQueuedMaterialThreadConVarSets() { return 0; }

	virtual ICVarIteratorInternal *FactoryInternalIterator( void ) { return NULL; }
};

static CPrecipCvar g_PrecipCvar;

//-----------------------------------------------------------------------------
// The two random streams, seeded, every draw written down. 'e' is the client's
// `random` (stock's random->); 'g' is vstdlib's free functions (RandomFloat,
// RandomInt, and mathlib's RandomVector through them).
//-----------------------------------------------------------------------------
class CPrecipRandom : public IUniformRandomStream
{
public:
	CPrecipRandom( char cTag, int nSeed )
		: m_cTag( cTag )
	{
		m_Stream.SetSeed( nSeed );
	}

	virtual void SetSeed( int iSeed ) { m_Stream.SetSeed( iSeed ); }

	virtual float RandomFloat( float flMinVal = 0.0f, float flMaxVal = 1.0f )
	{
		const float v = m_Stream.RandomFloat( flMinVal, flMaxVal );
		PrecipLog( "R\t%c\tf\t%.9g\t%.9g\t%.9g", m_cTag, flMinVal, flMaxVal, v );
		return v;
	}

	virtual int RandomInt( int iMinVal, int iMaxVal )
	{
		const int v = m_Stream.RandomInt( iMinVal, iMaxVal );
		PrecipLog( "R\t%c\ti\t%d\t%d\t%d", m_cTag, iMinVal, iMaxVal, v );
		return v;
	}

	virtual float RandomFloatExp( float flMinVal = 0.0f, float flMaxVal = 1.0f, float flExponent = 1.0f )
	{
		const float v = m_Stream.RandomFloatExp( flMinVal, flMaxVal, flExponent );
		PrecipLog( "R\t%c\tx\t%.9g\t%.9g\t%.9g\t%.9g", m_cTag, flMinVal, flMaxVal, flExponent, v );
		return v;
	}

private:
	CUniformRandomStream m_Stream;
	char m_cTag;
};

// The C library's rand(). GLIBC: the real one, srand( 1 ), values 0..2^31-1,
// which is what the game's client.so draws on Linux. MSVC: the Windows CRT's
// generator (x = x * 214013 + 2531011; ( x >> 16 ) & 0x7fff), seed 1, values
// 0..0x7fff, which is what VALVE_RAND_MAX assumes.
enum PrecipRand_t
{
	PRECIP_RAND_GLIBC,
	PRECIP_RAND_MSVC
};

static PrecipRand_t g_eRand = PRECIP_RAND_GLIBC;
static unsigned int g_nMsvcRand = 1;

int ss_precip_rand()
{
	int v;
	if ( g_eRand == PRECIP_RAND_GLIBC )
	{
		v = (rand)();
	}
	else
	{
		g_nMsvcRand = g_nMsvcRand * 214013u + 2531011u;
		v = (int)( ( g_nMsvcRand >> 16 ) & 0x7fff );
	}
	PrecipLog( "R\tc\tr\t%d", v );
	return v;
}

static CPrecipRandom g_EngineRandom( 'e', 0x5eed );
static CPrecipRandom g_GlobalRandom( 'g', 0x5eee );
IUniformRandomStream *random = &g_EngineRandom;

//-----------------------------------------------------------------------------
// The world.
//-----------------------------------------------------------------------------
static PrecipGlobals_t g_Globals = { 0.0f, 0.0f, 0 };
PrecipGlobals_t *gpGlobals = &g_Globals;

static IPrecipEngineTrace g_EngineTrace;
IPrecipEngineTrace *enginetrace = &g_EngineTrace;
static IPrecipMaterialSystem g_Materials;
IPrecipMaterialSystem *materials = &g_Materials;
static IPrecipViewRender g_ViewRender;
IPrecipViewRender *view = &g_ViewRender;
static IPrecipEngine g_Engine;
IPrecipEngine *engine = &g_Engine;
static IPrecipModelInfo g_ModelInfo;
IPrecipModelInfo *modelinfo = &g_ModelInfo;
static IPrecipDebugOverlay g_DebugOverlay;
IPrecipDebugOverlay *debugoverlay = &g_DebugOverlay;
static IPrecipLeafSystem g_LeafSystem;
IPrecipLeafSystem *g_pClientLeafSystem = &g_LeafSystem;
static CPrecipParticleMgr g_ParticleMgr;

CPrecipParticleMgr *ParticleMgr()
{
	return &g_ParticleMgr;
}

static C_World g_World;

C_World *GetClientWorldEntity()
{
	return &g_World;
}

// The scripted world, in one place:
//   solid below z = 0, and a pillar x 300..400, y -100..100, all z;
//   water whose surface is z = 5 where y > 0 (rain splashes);
//   over the emission traces (MASK_SOLID_BRUSHONLY, upwards): a sky ceiling
//     at z = 500 where y > 400, a plain ceiling at z = 450 where x > 600;
//   a wall at x = 1500 for the ash's forward trace (MASK_SHOT_HULL).
static bool InPillar( const Vector &p )
{
	return p.x >= 300 && p.x <= 400 && p.y >= -100 && p.y <= 100;
}

int IPrecipEngineTrace::GetPointContents( const Vector &p )
{
	return ( p.z < 0 || InPillar( p ) ) ? CONTENTS_SOLID : 0;
}

static void ClearTrace( const Vector &start, const Vector &end, trace_t *ptr )
{
	memset( (void *)ptr, 0, sizeof( *ptr ) );
	ptr->startpos = start;
	ptr->endpos = end;
	ptr->fraction = 1.0f;
}

// A plane z = flZ crossed going from start to end; true and the hit filled.
static bool CrossZ( const Vector &start, const Vector &end, float flZ, trace_t *ptr )
{
	if ( ( start.z - flZ ) * ( end.z - flZ ) > 0 || start.z == end.z )
		return false;
	const float t = ( start.z - flZ ) / ( start.z - end.z );
	if ( t < 0 || t >= ptr->fraction )
		return false;
	ptr->fraction = t;
	ptr->endpos = start + ( end - start ) * t;
	return true;
}

void UTIL_TraceLine( const Vector &start, const Vector &end, unsigned int mask, const C_BaseEntity *ignore,
					 int collisionGroup, trace_t *ptr )
{
	ClearTrace( start, end, ptr );

	if ( mask == MASK_WATER )
	{
		trace_t probe;
		ClearTrace( start, end, &probe );
		if ( CrossZ( start, end, 5.0f, &probe ) && probe.endpos.y > 0 )
			*ptr = probe;
	}
	else if ( mask == MASK_SOLID_BRUSHONLY )
	{
		trace_t probe;
		ClearTrace( start, end, &probe );
		if ( CrossZ( start, end, 450.0f, &probe ) && probe.endpos.x > 600 )
		{
			*ptr = probe;
			ptr->surface.flags = 0;
		}
		ClearTrace( start, end, &probe );
		probe.fraction = ptr->fraction;
		if ( CrossZ( start, end, 500.0f, &probe ) && probe.endpos.y > 400 )
		{
			*ptr = probe;
			ptr->surface.flags = SURF_SKY;
		}
	}
	else
	{
		// The ash's forward trace: a wall at x = 1500.
		if ( ( start.x - 1500 ) * ( end.x - 1500 ) <= 0 && start.x != end.x )
		{
			const float t = ( 1500 - start.x ) / ( end.x - start.x );
			ptr->fraction = t;
			ptr->endpos = start + ( end - start ) * t;
		}
	}

	PrecipLog( "L\t%u\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%d", mask, start.x, start.y, start.z, end.x, end.y,
			   end.z, ptr->fraction, (int)ptr->surface.flags );
}

// Against the entity's own box: a point trace answers startsolid inside it, a
// swept hull answers the box grown by the hull (upstream's IntersectRayWithBox).
void UTIL_TraceModel( const Vector &start, const Vector &end, const Vector &hullMin, const Vector &hullMax,
					  C_BaseEntity *pEnt, int collisionGroup, trace_t *ptr )
{
	ClearTrace( start, end, ptr );
	const Vector boxMin = pEnt->WorldAlignMins() - hullMax;
	const Vector boxMax = pEnt->WorldAlignMaxs() - hullMin;

	CBaseTrace tr;
	memset( (void *)&tr, 0, sizeof( tr ) );
	if ( IntersectRayWithBox( start, end - start, boxMin, boxMax, 0.0f, &tr ) )
	{
		ptr->fraction = tr.fraction;
		ptr->startsolid = tr.startsolid;
		ptr->endpos = start + ( end - start ) * tr.fraction;
		ptr->m_pEnt = pEnt;
	}

	PrecipLog( "M\t%d\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%d\t%.9g\t%d", pEnt->entindex(), start.x, start.y, start.z,
			   end.x, end.y, end.z, ptr->m_pEnt ? 1 : 0, ptr->fraction, ptr->startsolid ? 1 : 0 );
}

bool IPrecipLeafSystem::IsRenderableInPVS( IClientRenderable *pRenderable )
{
	return true;
}

static Vector g_vWind;

void GetWindspeedAtTime( float flTime, Vector &vecVelocity )
{
	vecVelocity = g_vWind;
}

int ScreenWidth()
{
	return 1280;
}

static VMatrix g_WorldToView;
static Vector g_vViewOrigin, g_vViewForward;

const VMatrix &CurrentWorldToViewMatrix()
{
	return g_WorldToView;
}

const Vector &CurrentViewForward()
{
	return g_vViewForward;
}

const Vector &CurrentViewOrigin()
{
	return g_vViewOrigin;
}

int CurrentViewID()
{
	return VIEW_MAIN;
}

void IPrecipRenderContext::GetMatrix( MaterialMatrixMode_t mode, VMatrix *pMatrix )
{
	pMatrix->Identity();
}

static IMesh g_Mesh;

IMesh *IPrecipRenderContext::GetDynamicMesh()
{
	return &g_Mesh;
}

static IPrecipRenderContext g_RenderContext;

IPrecipRenderContext *CMatRenderContextPtr::operator->()
{
	return &g_RenderContext;
}

static IMaterial g_RainMaterial = { "particle/rain" };
static IMaterial g_SnowMaterial = { "particle/snow" };

IMaterial *IPrecipMaterialSystem::FindMaterial( const char *pName, const char *pGroup )
{
	return !strcmp( pName, "particle/rain" ) ? &g_RainMaterial : &g_SnowMaterial;
}

void Tracer_Draw( CMeshBuilder *pMeshBuilder, Vector &start, Vector &delta, float width, float *color, float startV,
				  float endV )
{
	if ( g_bLogTracers )
		PrecipLog( "T\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g", start.x, start.y, start.z, delta.x, delta.y,
				   delta.z, width, color[3] );
}

void FX_WaterRipple( const Vector &origin, float scale, Vector *pColor, float flLifetime, float flAlpha )
{
	PrecipLog( "W\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g", origin.x, origin.y, origin.z, scale, flLifetime, flAlpha );
}

void Draw_SetSpriteTexture( const model_t *, int, int ) {}

void DrawBeamQuadratic( const Vector &, const Vector &, const Vector &, float, const Vector &, float ) {}

//-----------------------------------------------------------------------------
// Entities and the player.
//-----------------------------------------------------------------------------
static std::vector<C_BaseEntity *> g_Thinkers;
static int g_nNextIndex = 1;

C_BaseEntity::C_BaseEntity()
	: m_nIndex( g_nNextIndex++ )
	, m_bThinks( false )
{
	memset( &m_clrRender, 255, sizeof( m_clrRender ) );
	m_vecMins.Init();
	m_vecMaxs.Init();
	m_vecOrigin.Init();
	m_angAngles.Init();
}

C_BaseEntity::~C_BaseEntity()
{
	for ( size_t i = 0; i < g_Thinkers.size(); i++ )
		if ( g_Thinkers[i] == this )
		{
			g_Thinkers.erase( g_Thinkers.begin() + i );
			break;
		}
}

void C_BaseEntity::SetNextClientThink( float flTime )
{
	if ( flTime == CLIENT_THINK_ALWAYS && !m_bThinks )
	{
		m_bThinks = true;
		g_Thinkers.push_back( this );
	}
}

bool C_BaseEntity::InitializeAsClientEntity( const char *pszModelName, RenderGroup_t renderGroup )
{
	SpawnClientEntity();
	return true;
}

static C_BasePlayer g_Player;
static bool g_bHavePlayer = true;

C_BasePlayer *C_BasePlayer::GetLocalPlayer()
{
	return g_bHavePlayer ? &g_Player : NULL;
}

static QAngle g_angEye;

void C_BasePlayer::GetVectors( Vector *pForward, Vector *pRight, Vector *pUp ) const
{
	AngleVectors( g_angEye, pForward, pRight, pUp );
}

//-----------------------------------------------------------------------------
// The simple emitter's stand-in: particles_simple.cpp's base behaviours.
//-----------------------------------------------------------------------------
static std::vector<CSimpleEmitter *> g_Emitters;
static int g_nNextEmitter = 1;
static int g_nNextParticle = 1;

CSimpleEmitter::CSimpleEmitter( const char *pDebugName )
	: m_pszDebugName( pDebugName )
	, m_nEmitter( g_nNextEmitter++ )
	, m_Binding( &m_Particles )
	, m_nRefs( 0 )
{
	m_vSortOrigin.Init();
	g_Emitters.push_back( this );
}

CSimpleEmitter::~CSimpleEmitter()
{
	for ( size_t i = 0; i < g_Emitters.size(); i++ )
		if ( g_Emitters[i] == this )
		{
			g_Emitters.erase( g_Emitters.begin() + i );
			break;
		}
	for ( int i = 0; i < m_Particles.Count(); i++ )
		free( m_Particles[i] );
}

float CSimpleEmitter::UpdateAlpha( const SimpleParticle *pParticle )
{
	return ( pParticle->m_uchStartAlpha / 255.0f ) +
		( (float)( pParticle->m_uchEndAlpha / 255.0f ) - (float)( pParticle->m_uchStartAlpha / 255.0f ) ) *
		( pParticle->m_flLifetime / pParticle->m_flDieTime );
}

float CSimpleEmitter::UpdateScale( const SimpleParticle *pParticle )
{
	return (float)pParticle->m_uchStartSize +
		( (float)pParticle->m_uchEndSize - (float)pParticle->m_uchStartSize ) *
		( pParticle->m_flLifetime / pParticle->m_flDieTime );
}

float CSimpleEmitter::UpdateRoll( SimpleParticle *pParticle, float timeDelta )
{
	pParticle->m_flRoll += pParticle->m_flRollDelta * timeDelta;
	return pParticle->m_flRoll;
}

void CSimpleEmitter::UpdateVelocity( SimpleParticle *pParticle, float timeDelta )
{
	if ( pParticle->m_iFlags & SIMPLE_PARTICLE_FLAG_WINDBLOWN )
	{
		Vector vecWind;
		GetWindspeedAtTime( gpGlobals->curtime, vecWind );
		for ( int i = 0; i < 2; i++ )
		{
			if ( pParticle->m_vecVelocity[i] < vecWind[i] )
			{
				pParticle->m_vecVelocity[i] += ( timeDelta * 50 );
				if ( pParticle->m_vecVelocity[i] > vecWind[i] )
					pParticle->m_vecVelocity[i] = vecWind[i];
			}
			else if ( pParticle->m_vecVelocity[i] > vecWind[i] )
			{
				pParticle->m_vecVelocity[i] -= ( timeDelta * 50 );
				if ( pParticle->m_vecVelocity[i] < vecWind[i] )
					pParticle->m_vecVelocity[i] = vecWind[i];
			}
		}
	}
}

Vector CSimpleEmitter::UpdateColor( const SimpleParticle *pParticle )
{
	return Vector( pParticle->m_uchColor[0] / 255.0f, pParticle->m_uchColor[1] / 255.0f,
				   pParticle->m_uchColor[2] / 255.0f );
}

void CSimpleEmitter::SimulateParticles( CParticleSimulateIterator *pIterator )
{
	float timeDelta = pIterator->GetTimeDelta();
	SimpleParticle *pParticle = (SimpleParticle *)pIterator->GetFirst();
	while ( pParticle )
	{
		UpdateVelocity( pParticle, timeDelta );
		pParticle->m_Pos += pParticle->m_vecVelocity * timeDelta;
		pParticle->m_flLifetime += timeDelta;
		UpdateRoll( pParticle, timeDelta );
		if ( pParticle->m_flLifetime >= pParticle->m_flDieTime )
			pIterator->RemoveParticle( pParticle );
		pParticle = (SimpleParticle *)pIterator->GetNext();
	}
}

// The manager's memory is not constructed; zeroed here, and the fields the
// subject leaves unset (the ash's end alpha) are therefore 0 in the golden.
Particle *CSimpleEmitter::AddParticle( unsigned int particleSize, PMaterialHandle material, const Vector &origin )
{
	SimpleParticle *p = (SimpleParticle *)calloc( 1, sizeof( SimpleParticle ) );
	p->m_Pos = origin;
	p->m_pSubTexture = (void *)material;
	p->m_nHarnessId = g_nNextParticle++;
	p->m_bHarnessLogged = false;
	m_Particles.AddToHead( p );
	return p;
}

void CParticleSimulateIterator::RemoveParticle( Particle *pParticle )
{
	SimpleParticle *p = ( *m_pList )[m_iCur];
	PrecipLog( "X\t%d", p->m_nHarnessId );
	m_pList->Remove( m_iCur );
	free( p );
	--m_iCur;
}

void CPrecipBinding::SetBBox( const Vector &bbMin, const Vector &bbMax, bool bDisableAutoUpdate )
{
	PrecipLog( "B\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g", bbMin.x, bbMin.y, bbMin.z, bbMax.x, bbMax.y, bbMax.z );
}

//-----------------------------------------------------------------------------
// THE SUBJECT.
//-----------------------------------------------------------------------------
#define private public
#define protected public
#include "c_effects.cpp"
#undef private
#undef protected

//-----------------------------------------------------------------------------
// The scenario.
//-----------------------------------------------------------------------------
namespace
{

const int kFrames = 240;
const int kAshFrames = 300;
const int kSnapshotEvery = 60;

CClient_Precipitation *MakeVolume( PrecipitationType_t type, int nAlpha, const Vector &mins, const Vector &maxs )
{
	CClient_Precipitation *p = new CClient_Precipitation;
	p->m_nPrecipType = type;
	p->m_clrRender.a = (unsigned char)nAlpha;
	p->m_vecMins = mins;
	p->m_vecMaxs = maxs;
	PrecipLog( "E\t%d\t%d\t%d\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g", p->entindex(), (int)type, nAlpha, mins.x, mins.y,
			   mins.z, maxs.x, maxs.y, maxs.z );
	static_cast<C_BaseEntity *>( p )->OnDataChanged( DATA_UPDATE_CREATED );
	return p;
}

// The frame's player and view, all of it in the F row.
void PlacePlayer( int f, float dt )
{
	const float x = 100.0f + 4.0f * ( f < 180 ? f : 180 );
	g_Player.m_vecOrigin.Init( x, 50.0f, 0.0f );
	g_Player.m_vecEye.Init( x, 50.0f, 64.0f );
	g_Player.m_vecCenter.Init( x, 50.0f, 36.0f );
	g_Player.m_vecVelocity.Init( f < 180 ? 240.0f : 0.0f, 0.0f, 0.0f );
	g_angEye.Init( 0.0f, f < 200 ? 0.0f : 30.0f, 0.0f );
	g_Player.m_nDefaultFOV = 90;
	g_Player.m_flFOV = ( f >= 180 && f < 220 ) ? 45.0f : 90.0f;
	if ( f == 150 )
		g_vWind.Init( -20.0f, 15.0f, 0.0f );

	Vector fwd, right, up;
	AngleVectors( g_angEye, &fwd, &right, &up );
	g_vViewOrigin = g_Player.m_vecEye;
	g_vViewForward = fwd;
	// World to view: x right, y up, z backwards (Source's view space).
	g_WorldToView.Init( right.x, right.y, right.z, -DotProduct( right, g_vViewOrigin ), up.x, up.y, up.z,
						-DotProduct( up, g_vViewOrigin ), -fwd.x, -fwd.y, -fwd.z, DotProduct( fwd, g_vViewOrigin ), 0,
						0, 0, 1 );

	PrecipLog( "F\t%d\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%d\t%.9g\t%.9g\t%.9g", f,
			   gpGlobals->curtime, dt, g_Player.m_vecOrigin.x, g_Player.m_vecOrigin.y, g_Player.m_vecOrigin.z,
			   g_Player.m_vecVelocity.x, g_Player.m_vecVelocity.y, g_Player.m_vecVelocity.z, g_angEye.y,
			   g_Player.m_flFOV, g_Player.m_nDefaultFOV, g_vWind.x, g_vWind.y, g_vWind.z );
}

void LogNewPrecipParticles( CClient_Precipitation *p )
{
	for ( int i = p->m_Particles.Head(); i != p->m_Particles.InvalidIndex(); i = p->m_Particles.Next( i ) )
	{
		CPrecipitationParticle &q = p->m_Particles[i];
		if ( q.m_SpawnTime != gpGlobals->curtime )
			continue;
		PrecipLog( "C\t%d\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g", p->entindex(), q.m_Pos.x, q.m_Pos.y,
				   q.m_Pos.z, q.m_Velocity.x, q.m_Velocity.y, q.m_Velocity.z, q.m_Mass, q.m_Ramp );
	}
}

void SnapshotPrecip( CClient_Precipitation *p )
{
	PrecipLog( "Q\t%d\t%d\t%.9g\t%.9g\t%.9g\t%.9g", p->entindex(), p->m_Particles.Count(), p->m_Remainder,
			   p->m_Lifetime, p->s_WindVector.x, p->s_WindVector.y );
	for ( int i = p->m_Particles.Head(); i != p->m_Particles.InvalidIndex(); i = p->m_Particles.Next( i ) )
	{
		CPrecipitationParticle &q = p->m_Particles[i];
		PrecipLog( "S\t%d\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g", p->entindex(), q.m_Pos.x, q.m_Pos.y, q.m_Pos.z,
				   q.m_Velocity.x, q.m_Velocity.y, q.m_Velocity.z, q.m_Ramp );
	}
}

void LogNewSimpleParticles()
{
	for ( size_t e = 0; e < g_Emitters.size(); e++ )
	{
		CSimpleEmitter *pEmitter = g_Emitters[e];
		for ( int i = pEmitter->m_Particles.Count() - 1; i >= 0; i-- )
		{
			SimpleParticle *q = pEmitter->m_Particles[i];
			if ( q->m_bHarnessLogged )
				continue;
			q->m_bHarnessLogged = true;
			PrecipLog(
				"N\t%d\t%d\t%s\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%.9g",
				pEmitter->m_nEmitter, q->m_nHarnessId, (const char *)q->m_pSubTexture, q->m_Pos.x, q->m_Pos.y,
				q->m_Pos.z, q->m_vecVelocity.x, q->m_vecVelocity.y, q->m_vecVelocity.z, q->m_flRoll, q->m_flDieTime,
				q->m_flLifetime, q->m_uchColor[0], q->m_uchColor[1], q->m_uchColor[2], q->m_uchStartAlpha,
				q->m_uchEndAlpha, q->m_uchStartSize, q->m_uchEndSize, q->m_iFlags, q->m_flRollDelta );
		}
	}
}

void SnapshotSimpleParticles()
{
	for ( size_t e = 0; e < g_Emitters.size(); e++ )
	{
		CSimpleEmitter *pEmitter = g_Emitters[e];
		if ( !pEmitter->m_Particles.Count() )
			continue;
		PrecipLog( "G\t%d\t%s\t%d\t%.9g\t%.9g\t%.9g", pEmitter->m_nEmitter, pEmitter->m_pszDebugName,
				   pEmitter->m_Particles.Count(), pEmitter->m_vSortOrigin.x, pEmitter->m_vSortOrigin.y,
				   pEmitter->m_vSortOrigin.z );
		for ( int i = 0; i < pEmitter->m_Particles.Count(); i++ )
		{
			SimpleParticle *q = pEmitter->m_Particles[i];
			PrecipLog( "H\t%d\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g", q->m_nHarnessId, q->m_Pos.x,
					   q->m_Pos.y, q->m_Pos.z, q->m_vecVelocity.x, q->m_vecVelocity.y, q->m_vecVelocity.z, q->m_flRoll,
					   q->m_flLifetime, pEmitter->UpdateAlpha( q ) );
		}
	}
}

// One run of frames over the volumes given.
void RunFrames( const char *pszPhase, int nFrames, CClient_Precipitation *pRain, CClient_Precipitation *pSnow )
{
	PrecipLog( "P\t%s\t%d", pszPhase, nFrames );
	for ( int f = 0; f < nFrames; f++ )
	{
		// Mostly 60 Hz; a long frame (the emission clamp) and a zero one (no wind recompute).
		const float dt = ( f == 90 ) ? 0.125f : ( f == 91 ) ? 0.0f : ( 1.0f / 60.0f );
		gpGlobals->curtime += dt;
		gpGlobals->frametime = dt;
		gpGlobals->framecount = f;
		PlacePlayer( f, dt );

		// Client thinks, in registration order (the snowfall manager after the
		// first snowfall volume, which made it).
		std::vector<C_BaseEntity *> thinkers = g_Thinkers;
		for ( size_t i = 0; i < thinkers.size(); i++ )
			thinkers[i]->ClientThink();

		if ( pRain )
			LogNewPrecipParticles( pRain );
		if ( pSnow )
			LogNewPrecipParticles( pSnow );
		LogNewSimpleParticles();

		// The particle manager's simulation of the simple emitters.
		for ( size_t e = 0; e < g_Emitters.size(); e++ )
		{
			CParticleSimulateIterator it( &g_Emitters[e]->m_Particles, dt );
			g_Emitters[e]->SimulateParticles( &it );
		}

		// The render, every frame; its tracers written down on snapshot frames.
		const bool bSnapshot = ( f % kSnapshotEvery ) == kSnapshotEvery - 1;
		g_bLogTracers = bSnapshot;
		DrawPrecipitation();
		g_bLogTracers = false;

		if ( bSnapshot )
		{
			if ( pRain )
				SnapshotPrecip( pRain );
			if ( pSnow )
				SnapshotPrecip( pSnow );
			SnapshotSimpleParticles();
		}
	}
}

std::string RunScenario()
{
	g_Log.clear();
	g_nNextIndex = 2; // 1 is the player
	g_nNextEmitter = 1;
	g_nNextParticle = 1;
	g_Emitters.clear(); // the snowfall manager's per-volume emitters leak, as in stock
	InstallUniformRandomStream( &g_GlobalRandom );
	g_EngineRandom.SetSeed( 0x5eed );
	g_GlobalRandom.SetSeed( 0x5eee );
	srand( 1 );
	g_nMsvcRand = 1;

	g_pCVar = &g_PrecipCvar;
	cl_winddir.SetValue( 225 );
	cl_windspeed.SetValue( 60 );
	r_RainRadius.SetValue( 900 );
	r_SnowParticles.SetValue( 40 );
	g_vWind.Init( 30.0f, -10.0f, 0.0f );
	g_World.m_WorldMins.Init( -4096, -4096, -4096 );
	g_World.m_WorldMaxs.Init( 4096, 4096, 4096 );

	PrecipLog(
		"# c2-sims precipitation oracle: stock c_effects.cpp, run offline (src/test/oracle/native/precipitation_oracle_test.cpp)" );

	// PHASE A, glibc's rand(): every volume kind at once.
	g_eRand = PRECIP_RAND_GLIBC;
	gpGlobals->curtime = 10.0f;
	gpGlobals->frametime = 0.0f;
	PlacePlayer( -1, 0.0f );

	CClient_Precipitation *pRain =
		MakeVolume( PRECIPITATION_TYPE_RAIN, 60, Vector( -1200, -1200, 0 ), Vector( 1200, 1200, 600 ) );
	CClient_Precipitation *pSnow =
		MakeVolume( PRECIPITATION_TYPE_SNOW, 30, Vector( -800, -800, 0 ), Vector( 800, 800, 500 ) );
	CClient_Precipitation *pAsh =
		MakeVolume( PRECIPITATION_TYPE_ASH, 255, Vector( -2000, -2000, 0 ), Vector( 3000, 2000, 800 ) );
	CClient_Precipitation *pFallA =
		MakeVolume( PRECIPITATION_TYPE_SNOWFALL, 255, Vector( -500, -500, 0 ), Vector( 500, 500, 400 ) );
	CClient_Precipitation *pFallB =
		MakeVolume( PRECIPITATION_TYPE_SNOWFALL, 255, Vector( 2000, -500, 0 ), Vector( 3000, 500, 400 ) );

	RunFrames( "glibc", kFrames, pRain, pSnow );

	// The ash volume and both snowfall volumes go; the first deletion takes the
	// snowfall manager with it (~CClient_Precipitation calls SnowFallManagerDestroy).
	delete pFallB;
	delete pFallA;
	delete pAsh;
	delete pSnow;
	delete pRain;
	PrecipLog( "D" );

	// PHASE B, the MSVC CRT's rand(): ash alone. On Linux (phase A) every
	// RandomVector component is up to ~65,000x too large and the ash never
	// lands inside its volume; with VALVE_RAND_MAX's range it does.
	g_eRand = PRECIP_RAND_MSVC;
	pAsh = MakeVolume( PRECIPITATION_TYPE_ASH, 255, Vector( -2000, -2000, 0 ), Vector( 3000, 2000, 800 ) );
	RunFrames( "msvc", kAshFrames, NULL, NULL );
	delete pAsh;

	InstallUniformRandomStream( NULL );
	return g_Log;
}

std::string GoldenPath()
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/src/test/client/Fixtures/precipitation-stock.tsv";
}

std::string MarkerPath()
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/src/test/oracle/native/precipitation_golden.regen";
}

//-----------------------------------------------------------------------------
// THE SNOWFALL RAY-BOX TOLERANCE.
//
// CSnowFallManager::FindSnowVolumes gives each volume two chances: the sphere
// test (IsBoxIntersectingSphere about the emit origin), and, for a volume the
// sphere missed, a line-of-sight ray (eye position, forward * r_SnowRayLength
// with z zeroed) through IntersectRayWithBox with a flaring tolerance of
// 0.325. That tolerance is what makes a volume whose face sits just BEYOND the
// ray's end still count, and with it its flakes (m_bRayParticles, so the ray
// arms of the emitters run). The golden's scenario never reaches it: both its
// snowfall volumes sit inside r_SnowOutsideRadius, so every volume is picked by
// the sphere and the else-branch never runs. These cases drive that branch:
// boxes far enough from the center that the sphere rejects them, whose x-max
// face sits so far along the ray that hit or miss hinges on the 0.325 alone.
//
// The geometry (verified against the upstream float arithmetic, and against
// the compiled IntersectRayWithBox through the mutation runs): eye (0,0,64),
// yaw 10 so forward*8192 = (8067.54492, 1422.52588, 0). Every box spans
// x from 2000 (crossed by the ray at t = 0.2478) out to a chosen x-max, and
// the ray's y/z corridor so only two faces decide:
//   hit    x-max 8052.47265625  -- t1 0.9979959 < t2 0.9980915 at 0.325: HIT,
//            but t1 0.9982244 > t2 0.9981318 at tolerance 0: the flare alone
//            makes it.
//   brin   x-max 8051.77197265625 -- hit at 0.31, miss at 0.30.
//   brout  x-max 8051.6318359375  -- miss at 0.325 (t1 == t2 exactly), hit at
//            0.34.
//   miss   x-max 8050.41748046875 -- miss through 0.59, hit only at 0.61:
//            nearly double the tolerance.
// The x-max literals are the exact float grid beside the analytic solution,
// so the classification is not a coin flip: the deciding margins are ~0.01 in
// t against the tolerance's 0.015 steps.
//-----------------------------------------------------------------------------
namespace
{

const float kRayYaw = 10.0f;
const Vector kEye( 0.0f, 0.0f, 64.0f );

Vector SnowfallForward()
{
	QAngle ang( 0.0f, kRayYaw, 0.0f );
	Vector fwd;
	AngleVectors( ang, &fwd, NULL, NULL );
	fwd.z = 0.0f;
	return fwd;
}

// One fresh snowfall world: no leftovers from any other case (or from the
// golden's run, whose convars and streams it reset), then the volumes given.
// Deleting a volume takes the manager with it, as the golden's deletions do.
void BeginSnowfallRun()
{
	g_Log.clear();
	g_nNextIndex = 2;
	g_nNextEmitter = 1;
	g_nNextParticle = 1;
	g_Emitters.clear();
	g_eRand = PRECIP_RAND_GLIBC;
	g_nMsvcRand = 1;
	InstallUniformRandomStream( &g_GlobalRandom );
	g_EngineRandom.SetSeed( 0x5eed );
	g_GlobalRandom.SetSeed( 0x5eee );
	srand( 1 );
	g_pCVar = &g_PrecipCvar;
	gpGlobals->curtime = 0.0f;
	gpGlobals->frametime = 0.0f;
	gpGlobals->framecount = 0;
	g_vWind.Init( 0.0f, 0.0f, 0.0f );
	g_bHavePlayer = true;
	g_angEye.Init( 0.0f, 0.0f, 0.0f );
	g_Player.m_vecOrigin.Init();
	g_Player.m_vecEye = kEye;
	g_Player.m_vecCenter = kEye;
	g_Player.m_vecVelocity.Init();
	g_Player.m_flFOV = 90.0f;
	g_Player.m_nDefaultFOV = 90;
}

// The manager as FindSnowVolumes itself sees it: the volume far enough away
// that IsBoxIntersectingSphere (center, 1024 = r_SnowOutsideRadius's default)
// rejects it, so only the tolerant ray can make it active. Returns the volume
// for the caller to delete.
CClient_Precipitation *RayArmVolume( const Vector &mins, const Vector &maxs )
{
	return MakeVolume( PRECIPITATION_TYPE_SNOWFALL, 255, mins, maxs );
}

void RunFindSnowVolumes( const Vector &mins, const Vector &maxs, int *pnActive, bool *pbRay )
{
	CClient_Precipitation *pVol = RayArmVolume( mins, maxs );
	Vector center = kEye; // the sphere is centred on the EMIT origin; the eye
	// is 64 units back and both are > 3000 from the box.
	Vector eye = kEye;
	Vector fwd = SnowfallForward();
	s_pSnowFallMgr->FindSnowVolumes( center, 1024.0f, eye, fwd );
	*pnActive = s_pSnowFallMgr->m_nActiveSnowCount;
	*pbRay = s_pSnowFallMgr->m_bRayParticles;
	delete pVol;
}

const Vector kFarMins( 2000.0f, 1420.0f, -40.0f );
const Vector kFarMaxsBase( 2000.0f, 2000.0f, 668.0f ); // x replaced per case

Vector FarMaxs( float flXMax )
{
	Vector m = kFarMaxsBase;
	m.x = flXMax;
	return m;
}

// A live run: the manager thinks 120 frames at 60 Hz over one far volume, the
// way ClientThink drives it. Returns the log; the caller deletes the volume.
std::string RunLiveSnowfall( const Vector &mins, const Vector &maxs, int *pnArea )
{
	BeginSnowfallRun();
	CClient_Precipitation *pVol = RayArmVolume( mins, maxs );
	g_angEye.Init( 0.0f, kRayYaw, 0.0f );
	for ( int f = 0; f < 120; f++ )
	{
		const float dt = 1.0f / 60.0f;
		gpGlobals->curtime += dt;
		gpGlobals->frametime = dt;
		gpGlobals->framecount = f;
		s_pSnowFallMgr->ClientThink();
	}
	*pnArea = s_pSnowFallMgr->m_iSnowFallArea;
	delete pVol;
	return g_Log;
}

// The 5th argument of a `NAME ( ... )` call, as a float; NAN when the text has
// no such call or its 5th argument is not a numeric literal (the callers all
// assert on the value, so a missing call is a failure, never a silent pass).
// Arguments are split at the call's own depth, so a boxed subscript like
// m_aSnow[iSnow] cannot fake a comma. Stock's call is 6-argument (it ends in
// an out-trace); the managed port's returns the fraction, so 5.
static float FifthArgOfCall( const std::string &text, const size_t nParen )
{
	std::vector<std::string> args;
	int depth = 0;
	std::string arg;
	for ( size_t i = nParen + 1; i < text.size(); i++ )
	{
		const char ch = text[i];
		if ( ch == '(' || ch == '[' )
			++depth;
		else if ( ch == ')' || ch == ']' )
		{
			if ( depth == 0 )
			{
				args.push_back( arg );
				break;
			}
			--depth;
		}
		if ( depth == 0 && ch == ',' )
		{
			args.push_back( arg );
			arg.clear();
			continue;
		}
		if ( depth == 0 )
			arg += ch;
	}
	if ( args.size() < 5 )
		return NAN;
	// Trim, and drop a C++/C# float suffix.
	std::string tok = args[4];
	size_t b = tok.find_first_not_of( " \t\n\r" );
	size_t e = tok.find_last_not_of( " \t\n\r" );
	if ( b == std::string::npos )
		return NAN;
	tok = tok.substr( b, e - b + 1 );
	if ( !tok.empty() && ( tok[tok.size() - 1] == 'f' || tok[tok.size() - 1] == 'F' ) )
		tok.erase( tok.size() - 1 );
	const char *psz = tok.c_str();
	char *pen = NULL;
	const float v = strtof( psz, &pen );
	return ( pen && pen != psz && *pen == '\0' ) ? v : NAN;
}

// The first call of `pszFn` after `nFrom` whose 5th argument is a number:
// declarations of the function (whose 5th argument is a parameter, not a
// literal) are stepped over, calls with a variable tolerance too.
float CallTolerance( const std::string &text, const size_t nFrom, const char *pszFn )
{
	for ( size_t n = text.find( pszFn, nFrom ); n != std::string::npos; n = text.find( pszFn, n + strlen( pszFn ) ) )
	{
		size_t p = n + strlen( pszFn );
		while ( p < text.size() && ( text[p] == ' ' || text[p] == '\t' ) )
			++p;
		if ( p < text.size() && text[p] == '(' )
		{
			const float v = FifthArgOfCall( text, p );
			if ( v == v ) // NAN compares unequal to itself
				return v;
		}
	}
	return NAN;
}

std::string ReadRepoFile( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	const std::string path = std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
	std::ifstream f( path.c_str(), std::ios::binary );
	if ( !f.is_open() )
	{
		ADD_FAILURE() << path << " is unreadable: a managed-side pin must exist.";
		return std::string();
	}
	return std::string( ( std::istreambuf_iterator<char>( f ) ), std::istreambuf_iterator<char>() );
}

} // namespace

TEST( PrecipitationOracle, SnowfallRayArmActivatesOnlyThroughTheRayBoxTolerance )
{
	BeginSnowfallRun();
	int nActive = -1;
	bool bRay = true;
	RunFindSnowVolumes( kFarMins, FarMaxs( 8052.47265625f ), &nActive, &bRay );
	EXPECT_EQ( nActive, 1 ) << "the box the 0.325 flare reaches was not made active";
	EXPECT_TRUE( bRay ) << "a ray hit must set m_bRayParticles";
}

TEST( PrecipitationOracle, SnowfallRayArmMissesABoxTheToleranceCannotReach )
{
	// Its face needs ~double the flare (hit only from 0.61), so stock's 0.325
	// must leave it inactive and the ray arms asleep.
	BeginSnowfallRun();
	int nActive = -1;
	bool bRay = true;
	RunFindSnowVolumes( kFarMins, FarMaxs( 8050.41748046875f ), &nActive, &bRay );
	EXPECT_EQ( nActive, 0 ) << "a box beyond the flare was made active";
	EXPECT_FALSE( bRay ) << "a miss must leave m_bRayParticles clear";
}

TEST( PrecipitationOracle, SnowfallRayBoxToleranceIsBracketedByItsHitAndMiss )
{
	// Two far boxes in one manager: the nearer face is inside the flare's
	// reach (would also hit at 0.31, so a shrunken tolerance turns it off),
	// the further one outside it (hits from 0.34, so a grown tolerance turns
	// it on). Only the 0.325 sits between them.
	BeginSnowfallRun();
	CClient_Precipitation *pIn = RayArmVolume( kFarMins, FarMaxs( 8051.77197265625f ) );
	CClient_Precipitation *pOut = RayArmVolume( kFarMins, FarMaxs( 8051.6318359375f ) );
	Vector center = kEye;
	Vector eye = kEye;
	Vector fwd = SnowfallForward();
	s_pSnowFallMgr->FindSnowVolumes( center, 1024.0f, eye, fwd );
	EXPECT_EQ( s_pSnowFallMgr->m_nActiveSnowCount, 1 ) << "exactly the flared box is active";
	if ( s_pSnowFallMgr->m_nActiveSnowCount == 1 )
		EXPECT_EQ( s_pSnowFallMgr->m_aActiveSnow[0], 0 ) << "the active one is the nearer box";
	EXPECT_TRUE( s_pSnowFallMgr->m_bRayParticles );
	delete pOut;
	delete pIn;
}

TEST( PrecipitationOracle, SnowfallSphereArmActivatesWithoutTheRayAndNeverSetsRayParticles )
{
	// The control: a box the SPHERE catches (it straddles the eye, 200 from
	// it, inside the 1024). It must be active with m_bRayParticles clear --
	// if that flag were simply set by everything, case 1 would prove nothing.
	BeginSnowfallRun();
	int nActive = -1;
	bool bRay = true;
	RunFindSnowVolumes( Vector( -1000.0f, -300.0f, -40.0f ), Vector( -200.0f, 300.0f, 668.0f ), &nActive, &bRay );
	EXPECT_EQ( nActive, 1 ) << "the sphere arm did not activate";
	EXPECT_FALSE( bRay ) << "the sphere arm must never set m_bRayParticles";
}

TEST( PrecipitationOracle, LiveSnowfallRayArmRunsTheEmitterOnlyThroughTheTolerance )
{
	// The live path: ClientThink -> CreateSnowFall -> FindSnowVolumes ->
	// SNOWFALL_IN_ENTITY -> CreateOutsideVolumeSnowParticles, whose spawn
	// draws are the R rows. Miss phase first (no draws at all), then the
	// flared box on the same rig (draws appear): the arm only runs because
	// of the 0.325.
	int nArea = -1;
	const std::string miss = RunLiveSnowfall( kFarMins, FarMaxs( 8050.41748046875f ), &nArea );
	EXPECT_EQ( nArea, 0 ) << "SNOWFALL_NONE";
	EXPECT_EQ( miss.find( "\nR\tg\tf\t2000\t8050.41748" ), std::string::npos )
		<< "the unflared box's spawn range was drawn";

	const std::string hit = RunLiveSnowfall( kFarMins, FarMaxs( 8052.47265625f ), &nArea );
	EXPECT_EQ( nArea, 2 ) << "SNOWFALL_IN_ENTITY: the flared box must flip the area";
	EXPECT_NE( hit.find( "\nR\tg\tf\t2000\t8052.47266" ), std::string::npos )
		<< "the flared box's spawn draws never ran";
}

TEST( PrecipitationOracle, ManagedSnowfallUsesStocksRayBoxTolerance )
{
	// Value for value, both sides parsed from their own source: stock's
	// FindSnowVolumes ray, and the managed port's call in its FindSnowVolumes.
	// The parse contract: renaming FindSnowVolumes (either tree), the call, or
	// the file breaks this test with the ASSERT below naming the side — the
	// rename must then update these needles, not delete this test.
	const std::string stock = ReadValve( "src/game/client/c_effects.cpp" );
	const size_t fn = stock.find( "void CSnowFallManager::FindSnowVolumes" );
	const float flStock = CallTolerance( stock, fn, "IntersectRayWithBox" );
	ASSERT_EQ( flStock, 0.325f ) << "stock's snowfall ray tolerance is no longer 0.325;"
								 << " the golden's ray cases (and the port) must follow it on purpose";

	const std::string managed = ReadRepoFile( "src/client/managed/Effects/PrecipitationSim.cs" );
	ASSERT_FALSE( managed.empty() );
	// We are parsing the snowfall selector: the sphere arm and the ray flag
	// must both be in the same file, or the pin below pins nothing.
	EXPECT_NE( managed.find( "IsBoxIntersectingSphere(" ), std::string::npos );
	EXPECT_NE( managed.find( "_rayParticles = true" ), std::string::npos );
	const size_t sel = managed.find( "void FindSnowVolumes" );
	const float flManaged = CallTolerance( managed, sel, "IntersectRayWithBoxFraction" );
	EXPECT_EQ( flManaged, flStock ) << "the managed snowfall ray no longer uses stock's tolerance";
}
} // namespace

TEST( PrecipitationOracle, StockProducesTheGolden )
{
	SS_SKIP_ON_CI(
		"a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit" );
	const std::string run = RunScenario();

	if ( std::ifstream( MarkerPath() ).good() )
	{
		std::ofstream out( GoldenPath() );
		out << run;
	}

	std::ifstream in( GoldenPath() );
	ASSERT_TRUE( in.good() ) << GoldenPath() << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	const std::string golden = ss.str();

	ASSERT_GT( golden.size(), 100000u ) << "the golden is empty or truncated";

	std::istringstream a( run ), b( golden );
	std::string la, lb;
	int n = 0;
	while ( true )
	{
		const bool ha = (bool)std::getline( a, la );
		const bool hb = (bool)std::getline( b, lb );
		++n;
		if ( !ha && !hb )
			break;
		ASSERT_TRUE( ha && hb && la == lb )
			<< "line " << n << ": stock now writes\n  " << ( ha ? la : "<end>" ) << "\nand the golden has\n  "
			<< ( hb ? lb : "<end>" ) << "\nDo NOT regenerate the golden to make this pass; see the file header.";
	}
}

TEST( PrecipitationOracle, TheScenarioReachesEveryArm )
{
	const std::string run = RunScenario();

	struct Arm_t
	{
		const char *pszNeedle;
		const char *pszWhat;
	};

	static const Arm_t kArms[] = {
		{ "\nC\t2\t", "rain particles were emitted" },
		{ "\nC\t3\t", "snow particles were emitted" },
		{ "\tashtray\t", "the ash emitter holds particles" },
		{ "\tsnowfall\t", "the snowfall emitter holds particles" },
		{ "\nW\t", "a rain splash rippled" },
		{ "\nT\t", "a tracer was drawn" },
		{ "\nX\t", "a simple particle died" },
		{ "effects/ember_swirling001", "the ash's ember arm ran" },
	};
	for ( size_t i = 0; i < sizeof( kArms ) / sizeof( kArms[0] ); i++ )
		EXPECT_NE( run.find( kArms[i].pszNeedle ), std::string::npos ) << kArms[i].pszWhat << ": no";
}
