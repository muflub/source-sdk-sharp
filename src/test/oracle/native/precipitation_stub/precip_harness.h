//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// The world the PRISTINE game/client/c_effects.cpp is compiled into by
// precipitation_oracle_test.cpp. That file is the whole client weather system:
// rain and snow tracers, ash, the snowfall manager, and (beside them) the wind,
// the embers and the quadratic beam. In the game it sits on top of the client
// DLL's entity system, the particle manager, the material system and the
// engine's traces; here each of those is a stand-in that answers from a small
// scripted world and writes down what the subject asked of it.
//
// What is stand-in and what is not:
//
//   * The SUBJECT -- every function of c_effects.cpp -- is upstream's text,
//     compiled unedited from the clone at valve-sdk.lock.
//   * mathlib, tier0, tier1 (ConVar, CUtlLinkedList, CSmartPtr) and vstdlib
//     (the random streams) are the real libraries the test links.
//   * public/collisionutils.cpp (IsBoxIntersectingSphere, IntersectRayWithBox)
//     is upstream's, compiled beside the subject.
//   * game/client/timedevent.h and game/shared/precipitation_shared.h are
//     upstream's.
//   * Everything declared below is a stand-in. The entity, the player, the
//     traces, the particle manager's emitter and the renderer are the
//     harness's; CSimpleEmitter's five base behaviours (the velocity wind
//     push, the roll, the alpha and scale lerps, SimulateParticles' order) are
//     stated here as particles_simple.cpp states them, because the real one
//     needs the whole particle manager. The managed SimpleEmitter carries the
//     same five and is held to the real manager by the live ember recording.
//
//=============================================================================

#pragma once

#include <stdio.h>
#include <float.h>
#include <string.h>
#include <stdlib.h>
#include <algorithm>
#include <random>

// mathlib's RandomVector is Vector::Random, which draws the C library's rand()
// and divides by VALVE_RAND_MAX (0x7fff). Every rand() the subject reaches is
// routed through the harness, which logs it and answers from the generator the
// scenario names (glibc's own, or the MSVC CRT's; see RunScenario).
int ss_precip_rand();
#define rand() ss_precip_rand()

#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier0/fasttimer.h"
#include "tier1/convar.h"
#include "tier1/utlvector.h"
#include "tier1/utllinkedlist.h"
#include "tier1/smartptr.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "mathlib/vector2d.h"
#include "mathlib/vmatrix.h"
#include "vstdlib/random.h"
#include "bspflags.h"
#include "const.h"
#include "trace.h"
#include "collisionutils.h"
#include "worldsize.h"
#include "timedevent.h"

#ifndef VPROF_BUDGET
#define VPROF_BUDGET( name, group )
#endif

//-----------------------------------------------------------------------------
// The harness's log. Every stand-in that the subject reaches writes here.
//-----------------------------------------------------------------------------
void PrecipLog( const char *pFmt, ... ) FMTFUNCTION( 1, 2 );

//-----------------------------------------------------------------------------
// Globals the client DLL provides.
//-----------------------------------------------------------------------------
struct PrecipGlobals_t
{
	float curtime;
	float frametime;
	int framecount;
};

extern PrecipGlobals_t *gpGlobals;

// stdlib's random() would collide with the client's `random`; the subject's
// every `random->` is renamed with it.
#define random ss_precip_random
extern IUniformRandomStream *random;

enum DataUpdateType_t
{
	DATA_UPDATE_CREATED = 0,
	DATA_UPDATE_DATATABLE_CHANGED
};

enum RenderGroup_t
{
	RENDER_GROUP_OPAQUE_ENTITY = 0,
	RENDER_GROUP_TRANSLUCENT_ENTITY,
	RENDER_GROUP_OTHER
};

enum
{
	VIEW_MAIN = 0,
	VIEW_MONITOR = 2
};

enum
{
	DF_RENDER_REFLECTION = 0x1,
	DF_RENDER_REFRACTION = 0x2
};

#define CLIENT_THINK_ALWAYS -1293.0f
#define CLIENT_THINK_NEVER -1.0f
#define TEXTURE_GROUP_CLIENT_EFFECTS "ClientEffect textures"

// The recv-table and client-class declarations compile to nothing that
// matters: an int array per table, so the macro bodies stay well formed.
#define DECLARE_CLIENTCLASS()
#define DECLARE_CLASS( className, baseClassName ) \
	typedef baseClassName BaseClass;              \
	typedef className ThisClass;
#define IMPLEMENT_CLIENTCLASS_DT( className, tableName, serverName ) static int ss_recv_##tableName[] = { 0,
#define BEGIN_RECV_TABLE_NOBASE( className, tableName ) static int ss_recv_##tableName[] = { 0,
#define END_RECV_TABLE() \
	}                    \
	;
#define RecvPropInt( ... ) 0
#define RecvPropFloat( ... ) 0
#define RecvPropVector( ... ) 0
#define RecvPropDataTable( ... ) 0

//-----------------------------------------------------------------------------
// The entity, as far as c_effects.cpp asks it anything.
//-----------------------------------------------------------------------------
struct model_t;
class IClientRenderable;
class C_BaseEntity;

struct PrecipRenderColor_t
{
	unsigned char r, g, b, a;

	const PrecipRenderColor_t *operator->() const { return this; }
};

class C_BaseEntity
{
public:
	C_BaseEntity();
	virtual ~C_BaseEntity();

	virtual void Precache() {}

	virtual void OnDataChanged( DataUpdateType_t updateType ) {}

	virtual void ClientThink() {}

	virtual bool ShouldDraw() { return true; }

	virtual void AddEntity() {}

	virtual int DrawModel( int ) { return 0; }

	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) {}

	virtual bool IsTransparent() { return false; }

	virtual void Release() { delete this; }

	virtual void SpawnClientEntity() {}

	void SetNextClientThink( float flTime );
	bool InitializeAsClientEntity( const char *pszModelName, RenderGroup_t renderGroup );

	int entindex() const { return m_nIndex; }

	IClientRenderable *GetClientRenderable() { return (IClientRenderable *)this; }

	const Vector &WorldAlignMins() const { return m_vecMins; }

	const Vector &WorldAlignMaxs() const { return m_vecMaxs; }

	const Vector &GetAbsOrigin() const { return m_vecOrigin; }

	const QAngle &GetAbsAngles() const { return m_angAngles; }

	const Vector &GetRenderOrigin() const { return m_vecOrigin; }

	const model_t *GetModel() const { return NULL; }

	int GetRenderMode() const { return 0; }

	color32 GetRenderColor() const
	{
		color32 c = { m_clrRender.r, m_clrRender.g, m_clrRender.b, m_clrRender.a };
		return c;
	}

	void UpdateVisibility() {}

	PrecipRenderColor_t m_clrRender;

	// The harness sets these.
	Vector m_vecMins, m_vecMaxs, m_vecOrigin;
	QAngle m_angAngles;
	int m_nIndex;
	bool m_bThinks;
};

class C_BasePlayer : public C_BaseEntity
{
public:
	static C_BasePlayer *GetLocalPlayer();

	Vector EyePosition() const { return m_vecEye; }

	void GetVectors( Vector *pForward, Vector *pRight, Vector *pUp ) const;

	const Vector &GetAbsVelocity() const { return m_vecVelocity; }

	Vector WorldSpaceCenter() const { return m_vecCenter; }

	// Stock's types: GetFOV is a float, GetDefaultFOV an int (c_baseplayer.h),
	// and CreateSnowFall's zoom scale divides one by the other.
	float GetFOV() const { return m_flFOV; }

	int GetDefaultFOV() const { return m_nDefaultFOV; }

	Vector m_vecEye, m_vecVelocity, m_vecCenter;
	float m_flFOV;
	int m_nDefaultFOV;
};

template <class T>
class CHandle
{
public:
	CHandle()
		: m_p( NULL )
	{
	}

	CHandle &operator=( T *p )
	{
		m_p = p;
		return *this;
	}

	operator T *() const { return m_p; }

	T *operator->() const { return m_p; }

private:
	T *m_p;
};

// The world entity's bounds (r_RainHack's arm).
class C_World
{
public:
	Vector m_WorldMins, m_WorldMaxs;
};

C_World *GetClientWorldEntity();

class CAutoGameSystemPerFrame
{
public:
	CAutoGameSystemPerFrame( const char *pszName ) {}

	virtual ~CAutoGameSystemPerFrame() {}

	virtual void LevelInitPostEntity() {}

	virtual void LevelShutdownPreEntity() {}

	virtual void Update( float frametime ) {}
};

//-----------------------------------------------------------------------------
// Traces.
//-----------------------------------------------------------------------------
struct csurface_t
{
	const char *name;
	short surfaceProps;
	unsigned short flags;
};

class CGameTrace : public CBaseTrace
{
public:
	csurface_t surface;
	int hitgroup;
	short physicsbone;
	C_BaseEntity *m_pEnt;
	int hitbox;
};

typedef CGameTrace trace_t;

class IPrecipEngineTrace
{
public:
	int GetPointContents( const Vector &vecAbsPosition );
};

extern IPrecipEngineTrace *enginetrace;

void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask, const C_BaseEntity *ignore,
					 int collisionGroup, trace_t *ptr );
void UTIL_TraceModel( const Vector &vecStart, const Vector &vecEnd, const Vector &hullMin, const Vector &hullMax,
					  C_BaseEntity *pentModel, int collisionGroup, trace_t *ptr );

//-----------------------------------------------------------------------------
// Materials and the renderer.
//-----------------------------------------------------------------------------
class IMaterial
{
public:
	const char *m_pszName;
};

#define INVALID_MATERIAL_HANDLE ( (IMaterial *)NULL )

enum MaterialMatrixMode_t
{
	MATERIAL_VIEW = 0,
	MATERIAL_PROJECTION,
	MATERIAL_MODEL
};

enum MaterialPrimitiveType_t
{
	MATERIAL_QUADS = 0
};

class IMesh
{
};

class CMeshBuilder
{
public:
	void Begin( IMesh *pMesh, MaterialPrimitiveType_t type, int nCount ) {}

	void End( bool bSpewData = false, bool bDraw = false ) {}
};

class IPrecipRenderContext
{
public:
	void GetMatrix( MaterialMatrixMode_t mode, VMatrix *pMatrix );

	void MatrixMode( MaterialMatrixMode_t mode ) {}

	void LoadIdentity() {}

	void LoadMatrix( const VMatrix &m ) {}

	void EnableUserClipTransformOverride( bool b ) {}

	void UserClipTransform( const VMatrix &m ) {}

	void Bind( IMaterial *pMaterial ) {}

	IMesh *GetDynamicMesh();
};

class IPrecipMaterialSystem
{
public:
	IMaterial *FindMaterial( const char *pName, const char *pGroup );
};

extern IPrecipMaterialSystem *materials;

class CMatRenderContextPtr
{
public:
	CMatRenderContextPtr( IPrecipMaterialSystem * ) {}

	IPrecipRenderContext *operator->();
};

const VMatrix &CurrentWorldToViewMatrix();
const Vector &CurrentViewForward();
const Vector &CurrentViewOrigin();
int CurrentViewID();

class IPrecipViewRender
{
public:
	int GetDrawFlags() { return 0; }
};

extern IPrecipViewRender *view;

int ScreenWidth();

void Tracer_Draw( CMeshBuilder *pMeshBuilder, Vector &start, Vector &delta, float width, float *color,
				  float startV = 0.0, float endV = 1.0 );
void FX_WaterRipple( const Vector &origin, float scale, Vector *pColor, float flLifetime = 1.5, float flAlpha = 1 );

class IPrecipEngine
{
public:
	void Con_NPrintf( int pos, const char *fmt, ... ) {}
};

extern IPrecipEngine *engine;

class IPrecipModelInfo
{
public:
	void GetModelBounds( const model_t *model, Vector &mins, Vector &maxs )
	{
		mins.Init();
		maxs.Init();
	}
};

extern IPrecipModelInfo *modelinfo;

class IPrecipDebugOverlay
{
public:
	void AddBoxOverlay( const Vector &origin, const Vector &mins, const Vector &max, QAngle const &orientation, int r,
						int g, int b, int a, float duration )
	{
	}
};

extern IPrecipDebugOverlay *debugoverlay;

class IPrecipLeafSystem
{
public:
	bool IsRenderableInPVS( IClientRenderable *pRenderable );
};

extern IPrecipLeafSystem *g_pClientLeafSystem;

void Draw_SetSpriteTexture( const model_t *pSpriteModel, int frame, int rendermode );
void DrawBeamQuadratic( const Vector &start, const Vector &control, const Vector &end, float width, const Vector &color,
						float scrollOffset );

// The client effect precache list compiles to a list of names.
#define CLIENTEFFECT_REGISTER_BEGIN( className ) static const char *ss_fx_##className[] = {
#define CLIENTEFFECT_MATERIAL( materialName ) materialName,
#define CLIENTEFFECT_REGISTER_END() \
	NULL                            \
	}                               \
	;

//-----------------------------------------------------------------------------
// The wind (env_wind_shared.h).
//-----------------------------------------------------------------------------
void GetWindspeedAtTime( float flTime, Vector &vecVelocity );

class CEnvWindShared
{
public:
	void Init( int iEntIndex, int iRandomSeed, float flTime, int iWindDir, float flInitialWindSpeed ) {}

	float WindThink( float flTime ) { return flTime + 1.0f; }

	int m_iMinWind, m_iMaxWind, m_iMinGust, m_iMaxGust;
	float m_flMinGustDelay, m_flMaxGustDelay;
	int m_iGustDirChange, m_iWindSeed, m_iInitialWindDir;
	float m_flInitialWindSpeed, m_flStartTime, m_flGustDuration;
};

//-----------------------------------------------------------------------------
// The particle manager's simple emitter (particles_simple.h).
//-----------------------------------------------------------------------------
typedef const char *PMaterialHandle;

class Particle
{
public:
	Particle *m_pPrev, *m_pNext;
	void *m_pSubTexture;
	Vector m_Pos;
};

enum SimpleParticleFlag_t
{
	SIMPLE_PARTICLE_FLAG_WINDBLOWN = 0x1,
	SIMPLE_PARTICLE_FLAG_NO_VEL_DECAY = 0x2
};

class SimpleParticle : public Particle
{
public:
	Vector m_vecVelocity;
	float m_flRoll;
	float m_flDieTime;
	float m_flLifetime;
	unsigned char m_uchColor[3];
	unsigned char m_uchStartAlpha;
	unsigned char m_uchEndAlpha;
	unsigned char m_uchStartSize;
	unsigned char m_uchEndSize;
	unsigned char m_iFlags;
	float m_flRollDelta;

	// The harness's, not stock's: which emitter added it, its id in the log,
	// and whether this frame's log has shown it yet.
	int m_nHarnessId;
	bool m_bHarnessLogged;
};

class CParticleSimulateIterator
{
public:
	CParticleSimulateIterator( CUtlVector<SimpleParticle *> *pList, float flTimeDelta )
		: m_pList( pList )
		, m_flTimeDelta( flTimeDelta )
		, m_iCur( -1 )
	{
	}

	float GetTimeDelta() const { return m_flTimeDelta; }

	Particle *GetFirst()
	{
		m_iCur = 0;
		return Cur();
	}

	Particle *GetNext()
	{
		++m_iCur;
		return Cur();
	}

	void RemoveParticle( Particle *pParticle );

private:
	Particle *Cur() { return m_iCur < m_pList->Count() ? ( *m_pList )[m_iCur] : NULL; }

	CUtlVector<SimpleParticle *> *m_pList;
	float m_flTimeDelta;
	int m_iCur;
};

class CPrecipBinding
{
public:
	CPrecipBinding( CUtlVector<SimpleParticle *> *pList )
		: m_pList( pList )
	{
	}

	int GetNumActiveParticles() { return m_pList->Count(); }

	void SetBBox( const Vector &bbMin, const Vector &bbMax, bool bDisableAutoUpdate = true );

private:
	CUtlVector<SimpleParticle *> *m_pList;
};

class CSimpleEmitter
{
public:
	CSimpleEmitter( const char *pDebugName );
	virtual ~CSimpleEmitter();

	// particles_simple.cpp's base behaviours, restated (see the file header).
	virtual float UpdateAlpha( const SimpleParticle *pParticle );
	virtual float UpdateScale( const SimpleParticle *pParticle );
	virtual float UpdateRoll( SimpleParticle *pParticle, float timeDelta );
	virtual void UpdateVelocity( SimpleParticle *pParticle, float timeDelta );
	virtual Vector UpdateColor( const SimpleParticle *pParticle );
	virtual void SimulateParticles( CParticleSimulateIterator *pIterator );

	void SetSortOrigin( const Vector &vSortOrigin ) { m_vSortOrigin = vSortOrigin; }

	Particle *AddParticle( unsigned int particleSize, PMaterialHandle material, const Vector &origin );

	PMaterialHandle GetPMaterial( const char *name ) { return name; }

	CPrecipBinding &GetBinding() { return m_Binding; }

	void AddRef() { ++m_nRefs; }

	void Release()
	{
		if ( --m_nRefs <= 0 )
			delete this;
	}

	// The harness's.
	const char *m_pszDebugName;
	int m_nEmitter;
	Vector m_vSortOrigin;
	CUtlVector<SimpleParticle *> m_Particles; // newest first, as the manager's lists are
	CPrecipBinding m_Binding;
	int m_nRefs;
};

class CPrecipParticleMgr
{
public:
	PMaterialHandle GetPMaterial( const char *name ) { return name; }
};

CPrecipParticleMgr *ParticleMgr();
