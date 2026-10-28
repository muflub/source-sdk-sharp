//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE FLASHLIGHT ORACLE: stock's client flashlight -- CFlashlightEffect's
// projected texture (UpdateLightNew: the swing, the hull traces and their
// filter, the pull-back and its drag, the basis, the state handed to the
// shadow manager), the old dynamic-light flashlight (UpdateLightOld), the
// turn-on and turn-off arms and the r_newflashlight change callback -- run
// offline from the PRISTINE game/client/flashlighteffect.cpp over a scripted
// world, and written down as the golden
// src/test/client/Fixtures/flashlight-stock.tsv. Each frame is driven the way
// stock's C_BasePlayer::UpdateFlashlight (c_baseplayer.cpp) drives it: with
// EF_DIMLIGHT on, make the effect and turn it on if there is none, then
// UpdateLight( EyePosition(), the eye vectors, FLASHLIGHT_DISTANCE ); with it
// off, delete the effect.
//
// WHY OFFLINE. In this mod no server code sets EF_DIMLIGHT, so the game never
// reaches the subject; and a live recording sees one map's worth of walls.
// Here every arm is driven on purpose: open space, a wall close enough to pull
// back from, a wall behind as well, a floor under the eye, each kind of thing
// the trace filter is asked about, a grate and a window, the ladder, the
// locked position, the trace visualiser, every ConVar the state reads, the old
// flashlight and the switch between the two, and a DX6 card.
//
// THE SUBJECT IS INCLUDED, NOT LINKED: its ConVars and its filter class are
// file-static, so the harness includes the file into this translation unit
// and drives its ConVars by name. flashlight_stub/flashlight_harness.h says
// what is stock and what is stand-in.
//
// BOTH SIDES ARE PINNED TO THE GOLDEN. This test asserts that stock produces
// exactly it; SourceSharp.Client.Tests' FlashlightOracleTests asserts the
// managed port does. Regenerating the golden to make this test green deletes
// the check: the golden changes only when the scenario does, and then on
// purpose (touch flashlight_golden.regen beside this file, run, delete the
// marker, and say why in the commit).
//
//=============================================================================

#include <stdarg.h>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <cmath>

#include "gtest/gtest.h"
#include "../../native/ss_ci_skip.h"

#include "flashlight_harness.h"

#include "mathlib_test_env.h"

//-----------------------------------------------------------------------------
// The log.
//-----------------------------------------------------------------------------
static std::string g_Log;

void FlashlightLog( const char *pFmt, ... )
{
	char szLine[2048];
	va_list ap;
	va_start( ap, pFmt );
	vsnprintf( szLine, sizeof( szLine ), pFmt, ap );
	va_end( ap );
	g_Log += szLine;
	g_Log += '\n';
}

unsigned int FlashlightBits( float f )
{
	unsigned int u;
	memcpy( &u, &f, sizeof( u ) );
	return u;
}

#define B( f ) FlashlightBits( f )
#define V3( v ) B( ( v ).x ), B( ( v ).y ), B( ( v ).z )
#define V3FMT "%08x\t%08x\t%08x"

//-----------------------------------------------------------------------------
// The console: ConVar::SetValue tells g_pCVar's global change callbacks.
//-----------------------------------------------------------------------------
#include "icvar.h"

class CFlashlightCvar final : public ICvar
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

static CFlashlightCvar g_FlashlightCvar;

//-----------------------------------------------------------------------------
// The globals and the stand-ins the subject reaches.
//-----------------------------------------------------------------------------
static FlashlightGlobals_t s_Globals;
FlashlightGlobals_t *gpGlobals = &s_Globals;

static IFlashlightShadowMgr s_ShadowMgr;
IFlashlightShadowMgr *g_pClientShadowMgr = &s_ShadowMgr;
static IFlashlightEfx s_Efx;
IFlashlightEfx *effects = &s_Efx;
static IFlashlightRender s_Render;
IFlashlightRender *render = &s_Render;
static IFlashlightEngine s_Engine;
IFlashlightEngine *engine = &s_Engine;
static IFlashlightDebugOverlay s_Overlay;
IFlashlightDebugOverlay *debugoverlay = &s_Overlay;
static IFlashlightHardwareConfig s_HardwareConfig;
IFlashlightHardwareConfig *g_pMaterialSystemHardwareConfig = &s_HardwareConfig;
static IFlashlightClientTools s_ClientTools;
IFlashlightClientTools *clienttools = &s_ClientTools;

// clientshadowmgr.cpp's two, which the subject declares extern: the
// defaults it gives them on this platform.
ConVar r_flashlightdepthtexture( "r_flashlightdepthtexture", "1" );
ConVar r_flashlightdepthres( "r_flashlightdepthres", "1024" );

// The scenario's answers.
static int s_nDXLevel = 95;
static bool s_bBorderColor = true;
static int s_nNextShadow = 0;

int IFlashlightEngine::GetDXSupportLevel()
{
	return s_nDXLevel;
}

bool IFlashlightHardwareConfig::SupportsBorderColor()
{
	return s_bBorderColor;
}

// Two textures, by name; the pointer the reference answers is the slot.
static const char *s_TextureNames[8];
static int s_nTextures = 0;
static char s_TextureSlots[8];

void CTextureReference::Init( char const *pTexture, const char *pTextureGroupName, bool bComplain )
{
	FlashlightLog( "X\t%s\t%s\t%d", pTexture, pTextureGroupName, bComplain ? 1 : 0 );
	int i = 0;
	for ( ; i < s_nTextures; i++ )
	{
		if ( !strcmp( s_TextureNames[i], pTexture ) )
			break;
	}
	if ( i == s_nTextures )
		s_TextureNames[s_nTextures++] = pTexture;
	m_pTexture = (ITexture *)&s_TextureSlots[i];
}

const char *FlashlightTextureName( const ITexture *pTexture )
{
	for ( int i = 0; i < s_nTextures; i++ )
	{
		if ( pTexture == (const ITexture *)&s_TextureSlots[i] )
			return s_TextureNames[i];
	}
	return pTexture ? "<unknown>" : "<null>";
}

static void LogState( const char *pszTag, int nHandle, const FlashlightState_t &s )
{
	FlashlightLog( "%s\t%d\t" V3FMT "\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x"
				   "\t%08x\t%08x\t%08x\t%08x\t%s\t%d\t%d\t%d\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%d",
				   pszTag, nHandle, V3( s.m_vecLightOrigin ), B( s.m_quatOrientation.x ), B( s.m_quatOrientation.y ),
				   B( s.m_quatOrientation.z ), B( s.m_quatOrientation.w ), B( s.m_NearZ ), B( s.m_FarZ ),
				   B( s.m_fHorizontalFOVDegrees ), B( s.m_fVerticalFOVDegrees ), B( s.m_fQuadraticAtten ),
				   B( s.m_fLinearAtten ), B( s.m_fConstantAtten ), B( s.m_Color[0] ), B( s.m_Color[1] ),
				   B( s.m_Color[2] ), B( s.m_Color[3] ), FlashlightTextureName( s.m_pSpotlightTexture ),
				   s.m_nSpotlightTextureFrame, s.m_bEnableShadows ? 1 : 0, s.m_bDrawShadowFrustum ? 1 : 0,
				   B( s.m_flShadowMapResolution ), B( s.m_flShadowFilterSize ), B( s.m_flShadowSlopeScaleDepthBias ),
				   B( s.m_flShadowDepthBias ), B( s.m_flShadowJitterSeed ), B( s.m_flShadowAtten ),
				   s.m_nShadowQuality );
}

ClientShadowHandle_t IFlashlightShadowMgr::CreateFlashlight( const FlashlightState_t &lightState )
{
	const int h = s_nNextShadow++;
	LogState( "C", h, lightState );
	return (ClientShadowHandle_t)h;
}

void IFlashlightShadowMgr::UpdateFlashlightState( ClientShadowHandle_t shadowHandle,
												  const FlashlightState_t &lightState )
{
	LogState( "W", shadowHandle, lightState );
}

void IFlashlightShadowMgr::UpdateProjectedTexture( ClientShadowHandle_t h, bool force )
{
	FlashlightLog( "P\t%d\t%d", h, force ? 1 : 0 );
}

void IFlashlightShadowMgr::DestroyFlashlight( ClientShadowHandle_t shadowHandle )
{
	FlashlightLog( "D\t%d", shadowHandle );
}

// The dynamic-light pool: CL_AllocDlight answers the light already holding
// the key, cleared, or else the first expired one, cleared and keyed.
static dlight_t s_Dlights[4];

dlight_t *IFlashlightEfx::CL_AllocDlight( int key )
{
	FlashlightLog( "A\t%d", key );
	for ( int i = 0; i < 4; i++ )
	{
		if ( s_Dlights[i].key == key )
		{
			memset( (void *)&s_Dlights[i], 0, sizeof( s_Dlights[i] ) );
			s_Dlights[i].key = key;
			return &s_Dlights[i];
		}
	}
	for ( int i = 0; i < 4; i++ )
	{
		if ( s_Dlights[i].die < gpGlobals->curtime )
		{
			memset( (void *)&s_Dlights[i], 0, sizeof( s_Dlights[i] ) );
			s_Dlights[i].key = key;
			return &s_Dlights[i];
		}
	}
	memset( (void *)&s_Dlights[0], 0, sizeof( s_Dlights[0] ) );
	s_Dlights[0].key = key;
	return &s_Dlights[0];
}

static void LogDlight( const char *pszTag, const dlight_t &d )
{
	FlashlightLog( "%s\t%d\t%d\t" V3FMT "\t%08x\t%d\t%d\t%d\t%d\t%08x\t%08x\t%08x\t%d\t" V3FMT "\t%08x\t%08x", pszTag,
				   d.key, d.flags, V3( d.origin ), B( d.radius ), d.color.r, d.color.g, d.color.b, d.color.exponent,
				   B( d.die ), B( d.decay ), B( d.minlight ), d.style, V3( d.m_Direction ), B( d.m_InnerAngle ),
				   B( d.m_OuterAngle ) );
}

void IFlashlightRender::TouchLight( dlight_t *light )
{
	LogDlight( "L", *light );
}

void IFlashlightDebugOverlay::AddBoxOverlay( const Vector &origin, const Vector &mins, const Vector &max,
											 QAngle const &orientation, int r, int g, int b, int a, float duration )
{
	FlashlightLog( "BX\t" V3FMT "\t" V3FMT "\t" V3FMT "\t" V3FMT "\t%d\t%d\t%d\t%d\t%08x", V3( origin ), V3( mins ),
				   V3( max ), V3( orientation ), r, g, b, a, B( duration ) );
}

void IFlashlightDebugOverlay::AddLineOverlay( const Vector &origin, const Vector &dest, int r, int g, int b,
											  bool noDepthTest, float duration )
{
	FlashlightLog( "LN\t" V3FMT "\t" V3FMT "\t%d\t%d\t%d\t%d\t%08x", V3( origin ), V3( dest ), r, g, b,
				   noDepthTest ? 1 : 0, B( duration ) );
}

//-----------------------------------------------------------------------------
// The scripted world: the local player, the things the filter is asked
// about, and boxes.
//-----------------------------------------------------------------------------
static C_BasePlayer s_LocalPlayer( "localplayer", COLLISION_GROUP_PLAYER );
static C_BasePlayer s_OtherPlayer( "otherplayer", COLLISION_GROUP_PLAYER );
static C_BaseViewModel s_ViewModel( "viewmodel", COLLISION_GROUP_NONE );
static C_BaseEntity s_Debris( "debris", COLLISION_GROUP_DEBRIS );
static C_BaseEntity s_InteractiveDebris( "interactivedebris", COLLISION_GROUP_INTERACTIVE_DEBRIS );
static C_BaseEntity s_Prop( "prop", COLLISION_GROUP_NONE );
static C_BaseEntity s_Pushaway( "pushaway", COLLISION_GROUP_PUSHAWAY );
static CFlashlightNonEntity s_StaticProp;

C_BasePlayer *C_BasePlayer::GetLocalPlayer()
{
	return &s_LocalPlayer;
}

static const char *HandleName( IHandleEntity *p )
{
	if ( p == &s_StaticProp )
		return "staticprop";
	C_BaseEntity *pEnt = static_cast<C_BaseEntity *>( p );
	return pEnt ? pEnt->m_pszName : "world";
}

C_BaseEntity *EntityFromEntityHandle( IHandleEntity *pHandleEntity )
{
	if ( !pHandleEntity || pHandleEntity == &s_StaticProp )
		return NULL;
	return static_cast<C_BaseEntity *>( pHandleEntity );
}

struct FlashlightBox_t
{
	Vector mins, maxs;
	int contents;
	IHandleEntity *pEnt; // NULL: the world
};

static std::vector<FlashlightBox_t> s_World;

static void ClearWorld()
{
	s_World.clear();
	FlashlightLog( "WC" );
}

static void AddBox( const Vector &mins, const Vector &maxs, int contents, IHandleEntity *pEnt )
{
	FlashlightBox_t b = { mins, maxs, contents, pEnt };
	s_World.push_back( b );
	FlashlightLog( "WB\t" V3FMT "\t" V3FMT "\t%08x\t%s", V3( mins ), V3( maxs ), contents,
				   pEnt ? HandleName( pEnt ) : "world" );
}

// A swept box against the boxes, as the engine's box trace answers it: the
// nearest entry (backed off by DIST_EPSILON, never below zero), a start
// inside a box marks startsolid, and a box the whole sweep stays inside
// marks allsolid with fraction 0.
static void TraceBoxes( const char *pszKind, const Vector &start, const Vector &end, const Vector &hmin,
						const Vector &hmax, unsigned int mask, ITraceFilter *pFilter, trace_t *tr )
{
	memset( (void *)tr, 0, sizeof( *tr ) );
	tr->startpos = start;

	const double kDistEpsilon = 0.03125;
	double delta[3] = { (double)end.x - start.x, (double)end.y - start.y, (double)end.z - start.z };
	const double len = sqrt( delta[0] * delta[0] + delta[1] * delta[1] + delta[2] * delta[2] );

	double best = 1.0;
	int bestAxis = -1;
	double bestSign = 0.0;
	IHandleEntity *pBest = NULL;
	bool bStartSolid = false, bAllSolid = false;

	for ( size_t k = 0; k < s_World.size(); k++ )
	{
		const FlashlightBox_t &b = s_World[k];
		if ( !( (unsigned int)b.contents & mask ) )
			continue;
		if ( b.pEnt )
		{
			const bool bHit = pFilter->ShouldHitEntity( b.pEnt, (int)mask );
			FlashlightLog( "F\t%s\t%08x\t%d", HandleName( b.pEnt ), mask, bHit ? 1 : 0 );
			if ( !bHit )
				continue;
		}

		double lo[3] = { (double)b.mins.x - hmax.x, (double)b.mins.y - hmax.y, (double)b.mins.z - hmax.z };
		double hi[3] = { (double)b.maxs.x - hmin.x, (double)b.maxs.y - hmin.y, (double)b.maxs.z - hmin.z };
		const double s[3] = { start.x, start.y, start.z };

		double tEnter = -1e30, tExit = 1e30;
		int axis = -1;
		double sign = 0.0;
		bool bMiss = false;
		for ( int i = 0; i < 3; i++ )
		{
			if ( fabs( delta[i] ) < 1e-12 )
			{
				if ( s[i] < lo[i] || s[i] > hi[i] )
					bMiss = true;
				continue;
			}
			double t1 = ( lo[i] - s[i] ) / delta[i];
			double t2 = ( hi[i] - s[i] ) / delta[i];
			double n = -1.0;
			if ( t1 > t2 )
			{
				double t = t1;
				t1 = t2;
				t2 = t;
				n = 1.0;
			}
			if ( t1 > tEnter )
			{
				tEnter = t1;
				axis = i;
				sign = n;
			}
			if ( t2 < tExit )
				tExit = t2;
		}
		if ( bMiss || tEnter > tExit || tExit < 0.0 || tEnter > 1.0 )
			continue;

		if ( tEnter < 0.0 )
		{
			bStartSolid = true;
			if ( tExit > 1.0 )
			{
				bAllSolid = true;
				best = 0.0;
				pBest = b.pEnt;
				bestAxis = -1;
			}
			continue;
		}

		double f = len > 0.0 ? ( tEnter * len - kDistEpsilon ) / len : 0.0;
		if ( f < 0.0 )
			f = 0.0;
		if ( f < best )
		{
			best = f;
			bestAxis = axis;
			bestSign = sign;
			pBest = b.pEnt;
		}
	}

	tr->fraction = (float)best;
	tr->startsolid = bStartSolid;
	tr->allsolid = bAllSolid;
	tr->endpos.x = (float)( start.x + delta[0] * best );
	tr->endpos.y = (float)( start.y + delta[1] * best );
	tr->endpos.z = (float)( start.z + delta[2] * best );
	if ( bestAxis >= 0 )
		tr->plane.normal[bestAxis] = (float)bestSign;
	tr->m_pEnt = pBest;

	FlashlightLog( "T\t%s\t" V3FMT "\t" V3FMT "\t" V3FMT "\t" V3FMT "\t%08x\t%08x\t" V3FMT "\t%d\t%d\t%s", pszKind,
				   V3( start ), V3( end ), V3( hmin ), V3( hmax ), mask, B( tr->fraction ), V3( tr->endpos ),
				   tr->startsolid ? 1 : 0, tr->allsolid ? 1 : 0,
				   ( tr->fraction < 1.0f || tr->allsolid ) ? HandleName( pBest ) : "-" );
}

void UTIL_TraceHull( const Vector &vecAbsStart, const Vector &vecAbsEnd, const Vector &hullMin, const Vector &hullMax,
					 unsigned int mask, ITraceFilter *pFilter, trace_t *ptr )
{
	TraceBoxes( "hull", vecAbsStart, vecAbsEnd, hullMin, hullMax, mask, pFilter, ptr );
}

void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask, ITraceFilter *pFilter,
					 trace_t *ptr )
{
	TraceBoxes( "line", vecAbsStart, vecAbsEnd, vec3_origin, vec3_origin, mask, pFilter, ptr );
}

//-----------------------------------------------------------------------------
// THE SUBJECT.
//-----------------------------------------------------------------------------
#include "flashlighteffect.cpp"

//-----------------------------------------------------------------------------
// The scenario.
//-----------------------------------------------------------------------------
namespace
{

// The local player's index: stock passes the player's entity index to the
// effect, which keys its dynamic light.
const int kPlayerIndex = 1;

// FLASHLIGHT_DISTANCE (c_baseplayer.cpp): what UpdateFlashlight passes.
const int kFlashlightDistance = 1000;

CFlashlightEffect *g_pFlashlight = NULL;
int g_nFrame = 0;

ConVar *FindFlashlightConVar( const char *pszName )
{
	ConVar *const kAll[] = {
		&r_newflashlight,
		&r_swingflashlight,
		&r_flashlightlockposition,
		&r_flashlightfov,
		&r_flashlightoffsetx,
		&r_flashlightoffsety,
		&r_flashlightoffsetz,
		&r_flashlightnear,
		&r_flashlightfar,
		&r_flashlightconstant,
		&r_flashlightlinear,
		&r_flashlightquadratic,
		&r_flashlightvisualizetrace,
		&r_flashlightambient,
		&r_flashlightshadowatten,
		&r_flashlightladderdist,
		&mat_slopescaledepthbias_shadowmap,
		&mat_depthbias_shadowmap,
		&r_flashlightdepthtexture,
		&r_flashlightdepthres,
	};
	for ( size_t i = 0; i < sizeof( kAll ) / sizeof( kAll[0] ); i++ )
	{
		if ( !strcmp( kAll[i]->GetName(), pszName ) )
			return kAll[i];
	}
	return NULL;
}

void LogConVar( ConVar *pVar )
{
	FlashlightLog( "CV\t%s\t%s", pVar->GetName(), pVar->GetString() );
}

void Set( const char *pszName, const char *pszValue )
{
	ConVar *pVar = FindFlashlightConVar( pszName );
	ASSERT_TRUE( pVar != NULL ) << pszName;
	FlashlightLog( "V\t%s\t%s", pszName, pszValue );
	pVar->SetValue( pszValue );
	LogConVar( pVar );
}

void RevertAll()
{
	const char *const kNames[] = {
		"r_newflashlight",
		"r_swingflashlight",
		"r_flashlightlockposition",
		"r_flashlightfov",
		"r_flashlightoffsetx",
		"r_flashlightoffsety",
		"r_flashlightoffsetz",
		"r_flashlightnear",
		"r_flashlightfar",
		"r_flashlightconstant",
		"r_flashlightlinear",
		"r_flashlightquadratic",
		"r_flashlightvisualizetrace",
		"r_flashlightambient",
		"r_flashlightshadowatten",
		"r_flashlightladderdist",
		"mat_slopescaledepthbias_shadowmap",
		"mat_depthbias_shadowmap",
		"r_flashlightdepthtexture",
		"r_flashlightdepthres",
	};
	for ( size_t i = 0; i < sizeof( kNames ) / sizeof( kNames[0] ); i++ )
	{
		ConVar *pVar = FindFlashlightConVar( kNames[i] );
		pVar->Revert();
		LogConVar( pVar );
	}
}

// The dynamic lights still keyed, after a frame.
void LogPool()
{
	for ( int i = 0; i < 4; i++ )
	{
		if ( s_Dlights[i].key != 0 )
			FlashlightLog( "Q\t%d\t%d\t%08x", i, s_Dlights[i].key, B( s_Dlights[i].die ) );
	}
}

// One client frame, as C_BasePlayer::Simulate runs C_BasePlayer::Flashlight
// for the local player: stock's UpdateFlashlight, restated.
void Frame( float flTime, bool bDimLight, const Vector &eye, const QAngle &angles, int nMoveType = MOVETYPE_WALK )
{
	gpGlobals->curtime = flTime;
	s_LocalPlayer.m_nMoveType = nMoveType;

	Vector vecForward, vecRight, vecUp;
	AngleVectors( angles, &vecForward, &vecRight, &vecUp );

	FlashlightLog( "U\t%d\t%08x\t%d\t%d\t" V3FMT "\t" V3FMT "\t" V3FMT "\t" V3FMT, g_nFrame++, B( flTime ),
				   bDimLight ? 1 : 0, nMoveType, V3( eye ), V3( vecForward ), V3( vecRight ), V3( vecUp ) );

	if ( bDimLight )
	{
		if ( !g_pFlashlight )
		{
			g_pFlashlight = new CFlashlightEffect( kPlayerIndex );
			FlashlightLog( "NEW" );
			g_pFlashlight->TurnOn();
		}
		g_pFlashlight->UpdateLight( eye, vecForward, vecRight, vecUp, kFlashlightDistance );
	}
	else if ( g_pFlashlight )
	{
		FlashlightLog( "DEL" );
		delete g_pFlashlight;
		g_pFlashlight = NULL;
	}
	LogPool();
}

void TurnOff()
{
	FlashlightLog( "OFF" );
	g_pFlashlight->TurnOff();
	LogPool();
}

void TurnOn()
{
	FlashlightLog( "ON" );
	g_pFlashlight->TurnOn();
}

void Begin( const char *pszName, int nDXLevel, bool bBorder )
{
	FlashlightLog( "S\t%s\t%d\t%d", pszName, nDXLevel, bBorder ? 1 : 0 );
	s_nDXLevel = nDXLevel;
	s_bBorderColor = bBorder;
	memset( (void *)s_Dlights, 0, sizeof( s_Dlights ) );
	gpGlobals->curtime = 0.0f;
	RevertAll();
	ClearWorld();
}

const Vector kEye( 0.0f, 0.0f, 64.0f );

// The world a stock map gives a flashlight that is lit in a corridor: far
// walls, out of reach of r_flashlightfar and the hull traces.
void FarWalls()
{
	AddBox( Vector( 2000, -2000, -100 ), Vector( 2100, 2000, 400 ), CONTENTS_SOLID, NULL );
	AddBox( Vector( -2100, -2000, -100 ), Vector( -2000, 2000, 400 ), CONTENTS_SOLID, NULL );
}

void ScenarioNew()
{
	Begin( "new", 95, true );
	FarWalls();
	float t = 1.0f;
	const float dt = 0.015f;

	// Open space, level, then looking down (the swing) and up.
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Frame( t += dt, true, kEye, QAngle( 0, 30, 0 ) );
	Frame( t += dt, true, kEye, QAngle( 45, 30, 0 ) );
	Frame( t += dt, true, kEye, QAngle( 89, 10, 0 ) );
	Frame( t += dt, true, kEye, QAngle( -60, 200, 0 ) );

	// A wall in front: pull back, frame after frame (the drag).
	ClearWorld();
	FarWalls();
	AddBox( Vector( 100, -500, -100 ), Vector( 120, 500, 400 ), CONTENTS_SOLID, NULL );
	for ( int i = 0; i < 6; i++ )
		Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );

	// Closer, and a wall behind as well: the back trace limits the pull-back.
	ClearWorld();
	FarWalls();
	AddBox( Vector( 40, -500, -100 ), Vector( 60, 500, 400 ), CONTENTS_SOLID, NULL );
	AddBox( Vector( -40, -500, -100 ), Vector( -20, 500, 400 ), CONTENTS_SOLID, NULL );
	for ( int i = 0; i < 4; i++ )
		Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );

	// Out into the open again: the pull-back drags to zero.
	ClearWorld();
	FarWalls();
	for ( int i = 0; i < 3; i++ )
		Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );

	// A floor under the eye, inside the downward offset: the origin hull
	// trace hits and the light starts at the eye.
	AddBox( Vector( -500, -500, 40 ), Vector( 500, 500, 50 ), CONTENTS_SOLID, NULL );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Frame( t += dt, true, kEye, QAngle( 20, 90, 0 ) );

	// A grate under the eye: MASK_SOLID has CONTENTS_GRATE, the direction
	// trace's mask does not.
	ClearWorld();
	FarWalls();
	AddBox( Vector( -500, -500, 40 ), Vector( 500, 500, 50 ), CONTENTS_GRATE, NULL );
	AddBox( Vector( 70, -500, -100 ), Vector( 80, 500, 400 ), CONTENTS_GRATE, NULL );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );

	// A window in front: the direction trace's mask has CONTENTS_WINDOW.
	ClearWorld();
	FarWalls();
	AddBox( Vector( 70, -500, -100 ), Vector( 72, 500, 400 ), CONTENTS_WINDOW, NULL );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );

	// Each thing the trace filter is asked about, in front at 50 and under
	// the eye: the player, another player, the view model, the two debris
	// groups (all skipped), then a prop, a pushaway prop and a static prop
	// (hit).
	IHandleEntity *const kAsked[] = { &s_LocalPlayer,       &s_OtherPlayer, &s_ViewModel, &s_Debris,
									  &s_InteractiveDebris, &s_Prop,        &s_Pushaway,  &s_StaticProp };
	for ( size_t i = 0; i < sizeof( kAsked ) / sizeof( kAsked[0] ); i++ )
	{
		ClearWorld();
		FarWalls();
		AddBox( Vector( 50, -16, 0 ), Vector( 66, 16, 72 ), CONTENTS_SOLID | CONTENTS_MONSTER, kAsked[i] );
		AddBox( Vector( -16, -16, 30 ), Vector( 16, 16, 50 ), CONTENTS_SOLID | CONTENTS_MONSTER, kAsked[i] );
		Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	}

	// The ladder: no origin trace, a fixed pull-back and no back trace.
	ClearWorld();
	FarWalls();
	AddBox( Vector( 40, -500, -100 ), Vector( 60, 500, 400 ), CONTENTS_SOLID, NULL );
	AddBox( Vector( -500, -500, 40 ), Vector( 500, 500, 50 ), CONTENTS_SOLID, NULL );
	for ( int i = 0; i < 3; i++ )
		Frame( t += dt, true, kEye, QAngle( -10, 0, 0 ), MOVETYPE_LADDER );
	ClearWorld();
	FarWalls();
	for ( int i = 0; i < 2; i++ )
		Frame( t += dt, true, kEye, QAngle( -10, 0, 0 ), MOVETYPE_LADDER );

	// The locked position: no state update, the projection still runs.
	Set( "r_flashlightlockposition", "1" );
	Frame( t += dt, true, kEye + Vector( 30, 0, 0 ), QAngle( 0, 45, 0 ) );
	Frame( t += dt, true, kEye + Vector( 60, 0, 0 ), QAngle( 0, 90, 0 ) );
	Set( "r_flashlightlockposition", "0" );

	// The trace visualiser.
	AddBox( Vector( 100, -500, -100 ), Vector( 120, 500, 400 ), CONTENTS_SOLID, NULL );
	Set( "r_flashlightvisualizetrace", "1" );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Set( "r_flashlightvisualizetrace", "0" );

	// Every ConVar the state reads.
	Set( "r_flashlightfov", "60" );
	Set( "r_flashlightlinear", "50.5" );
	Set( "r_flashlightquadratic", "2" );
	Set( "r_flashlightconstant", "0.75" );
	Set( "r_flashlightambient", "0.25" );
	Set( "r_flashlightnear", "6" );
	Set( "r_flashlightfar", "500" );
	Set( "r_flashlightshadowatten", "0.5" );
	Set( "r_flashlightoffsety", "-10" );
	Set( "r_flashlightoffsetx", "99" );
	Set( "r_flashlightoffsetz", "99" );
	Set( "mat_slopescaledepthbias_shadowmap", "4" );
	Set( "mat_depthbias_shadowmap", "0.001" );
	Set( "r_flashlightdepthres", "2048" );
	Set( "r_flashlightdepthtexture", "0" );
	Frame( t += dt, true, kEye, QAngle( 10, 0, 0 ) );
	Set( "r_swingflashlight", "0" );
	Set( "r_flashlightladderdist", "25" );
	Frame( t += dt, true, kEye, QAngle( 45, 0, 0 ) );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ), MOVETYPE_LADDER );
	Set( "r_flashlightdepthtexture", "1" );
	Set( "r_swingflashlight", "1" );
	Frame( t += dt, true, kEye, QAngle( 45, 0, 0 ) );

	// EF_DIMLIGHT goes: the effect is deleted (the light off), then comes
	// back as a new effect with a new handle.
	Frame( t += dt, false, kEye, QAngle( 0, 0, 0 ) );
	Frame( t += dt, false, kEye, QAngle( 0, 0, 0 ) );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );

	// The old flashlight: a dynamic light, a line trace through MASK_ALL,
	// and the new light put out. Near (full brightness), then far (the
	// falloff), then the move back.
	Set( "r_newflashlight", "0" );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	AddBox( Vector( 300, -500, -100 ), Vector( 320, 500, 400 ), CONTENTS_SOLID, NULL );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	ClearWorld();
	FarWalls();
	AddBox( Vector( 800, -500, -100 ), Vector( 820, 500, 400 ), CONTENTS_SOLID, NULL );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Frame( t += dt, true, kEye, QAngle( -5, 3, 0 ) );
	ClearWorld();
	FarWalls();
	AddBox( Vector( 650, -500, -100 ), Vector( 660, 500, 400 ), CONTENTS_SOLID, NULL );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	// Either side of the falloff's 500-unit knee: inside it the light is at
	// full brightness, just past it already dimmed.
	ClearWorld();
	FarWalls();
	AddBox( Vector( 450, -500, -100 ), Vector( 460, 500, 400 ), CONTENTS_SOLID, NULL );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	ClearWorld();
	FarWalls();
	AddBox( Vector( 510, -500, -100 ), Vector( 520, 500, 400 ), CONTENTS_SOLID, NULL );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	// Things only MASK_ALL stops on: the filter is asked as for the hull.
	ClearWorld();
	FarWalls();
	AddBox( Vector( 400, -16, 0 ), Vector( 416, 16, 72 ), CONTENTS_DEBRIS, &s_Debris );
	AddBox( Vector( 500, -16, 0 ), Vector( 516, 16, 72 ), CONTENTS_WATER, NULL );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Set( "r_newflashlight", "1" );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Set( "r_newflashlight", "0" );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );

	// TurnOff and TurnOn, which nothing in the stock driver calls but the
	// class offers: off puts the light out and UpdateLight does nothing.
	TurnOff();
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	TurnOff();
	TurnOn();
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Set( "r_newflashlight", "1" );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	TurnOff();
	TurnOn();
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );

	// Off for good: deleted with the new light lit.
	Frame( t += dt, false, kEye, QAngle( 0, 0, 0 ) );
}

void ScenarioDX6()
{
	// A card below DX7 and no border colour: the constructor forces the old
	// flashlight, and the change callback refuses to turn the new one on.
	Begin( "dx6", 60, false );
	FarWalls();
	float t = 1.0f;
	const float dt = 0.015f;
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Frame( t += dt, true, kEye, QAngle( 10, 20, 0 ) );
	Set( "r_newflashlight", "1" );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Frame( t += dt, false, kEye, QAngle( 0, 0, 0 ) );

	// The texture with no border colour, on a DX9 card.
	s_nDXLevel = 95;
	FlashlightLog( "S\tdx9-noborder\t95\t0" );
	Set( "r_newflashlight", "1" );
	Frame( t += dt, true, kEye, QAngle( 0, 0, 0 ) );
	Frame( t += dt, false, kEye, QAngle( 0, 0, 0 ) );
}

std::string RunScenario()
{
	g_Log.clear();
	g_pCVar = &g_FlashlightCvar;
	g_nFrame = 0;
	s_nNextShadow = 0;
	FlashlightLog( "# flashlight-stock.tsv: written by src/test/oracle/native/flashlight_oracle_test.cpp" );
	FlashlightLog(
		"# from the pristine game/client/flashlighteffect.cpp; README-flashlight-stock.md says how to read it." );
	ScenarioNew();
	ScenarioDX6();
	FlashlightLog( "END" );
	if ( g_pFlashlight )
	{
		delete g_pFlashlight;
		g_pFlashlight = NULL;
	}
	return g_Log;
}

std::string RepoFile( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

std::string GoldenPath()
{
	return RepoFile( "src/test/client/Fixtures/flashlight-stock.tsv" );
}

std::string MarkerPath()
{
	return RepoFile( "src/test/oracle/native/flashlight_golden.regen" );
}

} // namespace

TEST( FlashlightOracle, StockProducesTheGolden )
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

	ASSERT_GT( golden.size(), 20000u ) << "the golden is empty or truncated";

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

// Each arm the scenario exists to reach, found in the run: a check that the
// golden is not silently a recording of one path.
TEST( FlashlightOracle, TheScenarioReachesEveryArm )
{
	const std::string run = RunScenario();

	struct Arm_t
	{
		const char *pszNeedle;
		const char *pszWhat;
	};

	static const Arm_t kArms[] = {
		{ "\nC\t0\t", "the first projected texture was created" },
		{ "\nW\t0\t", "the projected texture's state was updated" },
		{ "\nP\t0\t1", "the projected texture was projected" },
		{ "\nD\t0", "the projected texture was destroyed" },
		{ "\nC\t1\t", "a second effect made a new handle" },
		{ "\nF\tlocalplayer\t", "the filter was asked about the local player" },
		{ "\nF\tviewmodel\t", "the filter was asked about the view model" },
		{ "\nF\tdebris\t", "the filter was asked about debris" },
		{ "\nF\tinteractivedebris\t", "the filter was asked about interactive debris" },
		{ "\nF\tprop\t", "the filter was asked about a prop" },
		{ "\tstaticprop\n", "a trace stopped on a static prop" },
		{ "\nT\tline\t", "the old flashlight traced a line" },
		{ "\nA\t1", "a dynamic light was allocated for the player" },
		{ "\nL\t1\t", "the dynamic light was touched" },
		{ "\nBX\t", "the trace visualiser drew a box" },
		{ "\nLN\t", "the trace visualiser drew a line" },
		{ "\nX\teffects/flashlight_border\t", "the border texture was used" },
		{ "\nX\teffects/flashlight001\t", "the no-border texture was used" },
		{ "\nOFF", "TurnOff ran" },
		{ "\nDEL", "an effect was deleted" },
	};
	for ( size_t i = 0; i < sizeof( kArms ) / sizeof( kArms[0] ); i++ )
		EXPECT_NE( run.find( kArms[i].pszNeedle ), std::string::npos ) << kArms[i].pszWhat << ": no";

	// The DX6 card's refusal: the callback put r_newflashlight back to 0.
	EXPECT_NE( run.find( "V\tr_newflashlight\t1\nCV\tr_newflashlight\t0" ), std::string::npos )
		<< "the change callback did not refuse the new flashlight on a DX6 card";
}

// THE FILTER, HELD TO STOCK'S CLASS. SourceSharp's SKIP_PLAYER_AND_VIEW_MODEL
// trace filter asks flashlight_trace_filter.h's predicate of a client shell;
// here that predicate and the pristine CTraceFilterSkipPlayerAndViewModel are
// asked about a plain entity, a player and a view model in every shared
// collision group, and must agree on each.
#include "../../../client/native/flashlight_trace_filter.h"

TEST( FlashlightOracle, OurFilterAnswersAsStocksClassDoes )
{
	CTraceFilterSkipPlayerAndViewModel stock;
	int asked = 0, skipped = 0;
	for ( int group = 0; group < LAST_SHARED_COLLISION_GROUP; group++ )
	{
		C_BaseEntity plain( "plain", group );
		C_BasePlayer player( "player", group );
		C_BaseViewModel viewmodel( "viewmodel", group );

		struct Case_t
		{
			IHandleEntity *p;
			bool bPlayer;
			bool bViewModel;
		};

		const Case_t kCases[] = { { &plain, false, false }, { &player, true, false }, { &viewmodel, false, true } };
		for ( const Case_t &c : kCases )
		{
			const bool bStock = stock.ShouldHitEntity( c.p, MASK_SOLID );
			const bool bOurs = SkipPlayerAndViewModelHits( c.bPlayer, c.bViewModel, group );
			EXPECT_EQ( bStock, bOurs ) << "group " << group << " player " << c.bPlayer << " viewmodel " << c.bViewModel;
			asked++;
			skipped += bStock ? 0 : 1;
		}
	}
	// Every player and view model, and the plain entity in the two debris groups.
	EXPECT_EQ( 3 * LAST_SHARED_COLLISION_GROUP, asked );
	EXPECT_EQ( 2 * LAST_SHARED_COLLISION_GROUP + 2, skipped );

	// And the thing that is no entity, which stock hits.
	EXPECT_TRUE( stock.ShouldHitEntity( &s_StaticProp, MASK_SOLID ) );
}

// The pull-back arm is reached, with and without the back trace limiting it:
// a near plane pushed out past r_flashlightnear's default.
TEST( FlashlightOracle, ThePullBackMovesTheNearPlane )
{
	const std::string run = RunScenario();
	std::istringstream in( run );
	std::string line;
	int pushed = 0;
	while ( std::getline( in, line ) )
	{
		if ( line.compare( 0, 2, "W\t" ) && line.compare( 0, 2, "C\t" ) )
			continue;
		std::vector<std::string> cols;
		std::istringstream fields( line );
		std::string col;
		while ( std::getline( fields, col, '\t' ) )
			cols.push_back( col );
		ASSERT_GT( cols.size(), 10u );
		unsigned int bits = (unsigned int)strtoul( cols[9].c_str(), NULL, 16 );
		float flNear;
		memcpy( &flNear, &bits, sizeof( flNear ) );
		if ( flNear > 4.5f && flNear < 100.0f )
			pushed++;
	}
	EXPECT_GT( pushed, 5 ) << "the scenario never pulled the light back";
}
