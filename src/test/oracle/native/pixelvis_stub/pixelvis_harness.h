//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// The world the PRISTINE game/client/c_pixel_visibility.cpp is compiled into
// by pixelvis_oracle_test.cpp. That file is the client's pixel visibility:
// occlusion-query sets per caller and per view, their fade, the proxies drawn
// at each view's end, and the sight-line fallback. In the game it sits on the
// material system's render context, the engine, the view and the traces; here
// each is a stand-in that answers from a scripted world and writes down what
// the subject asked of it.
//
// What is stand-in and what is not:
//
//   * The SUBJECT -- every function of c_pixel_visibility.cpp and its header --
//     is upstream's text, compiled unedited from the clone at valve-sdk.lock.
//   * tier0, tier1 (the ConVars, their change callback, CUtlMultiList) and
//     mathlib (VectorNormalize, Approach) are the real libraries; the material
//     system's interfaces, CMeshBuilder, ITraceFilter / CTraceFilter and the
//     MASK_ / CONTENTS_ / SOLID_ values are the real public headers.
//   * The material system, its render context, its dynamic mesh and materials
//     are vtable doubles (pixelvis_oracle_test.cpp), every slot a trap but
//     the ones the subject calls.
//   * Everything declared below is a stand-in: the globals, the engine's DX
//     level, the game-system base, the current view, the world-to-screen
//     transform, the entities the trace filter is asked about, the traces,
//     CTraceFilterSimple's own rule (its glow subclass is the subject's), the
//     local player (NULL, as this build's accessor answers), Msg / DevMsg (the
//     log's), and the client effect precache macros (the list is a row).
//
//=============================================================================

#pragma once

#include <stdio.h>
#include <float.h>
#include <string.h>
#include <stdlib.h>

#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/convar.h"
#include "tier1/utlmultilist.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "bspflags.h"
#include "const.h"
#include "trace.h"
#include "ihandleentity.h"
#include "basehandle.h"
#include "engine/IEngineTrace.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imesh.h"
#include "materialsystem/imaterial.h"

#ifndef tmZone
#define tmZone( ... )
#endif

#ifndef DECLARE_CLASS
#define DECLARE_CLASS( className, baseClassName ) \
	typedef baseClassName BaseClass;              \
	typedef className ThisClass;
#endif

//-----------------------------------------------------------------------------
// The log, and the subject's console output into it.
//-----------------------------------------------------------------------------
void PixelVisLog( const char *pFmt, ... ) FMTFUNCTION( 1, 2 );
void PixelVisDevMsg( int level, const char *pFmt, ... ) FMTFUNCTION( 2, 3 );
void PixelVisMsg( const char *pFmt, ... ) FMTFUNCTION( 1, 2 );
#define DevMsg PixelVisDevMsg
#define Msg PixelVisMsg

//-----------------------------------------------------------------------------
// The client's globals and the engine's DX level.
//-----------------------------------------------------------------------------
struct PixelVisGlobals_t
{
	int framecount;
	float frametime;
	float curtime;
};

extern PixelVisGlobals_t *gpGlobals;

class IPixelVisEngine
{
public:
	int GetDXSupportLevel();
};

extern IPixelVisEngine *engine;

//-----------------------------------------------------------------------------
// The game-system base the subject derives from (its two stages).
//-----------------------------------------------------------------------------
class CAutoGameSystem
{
public:
	explicit CAutoGameSystem( const char *pszName ) {}

	virtual ~CAutoGameSystem() {}

	virtual void LevelInitPreEntity() {}

	virtual void LevelShutdownPostEntity() {}
};

//-----------------------------------------------------------------------------
// The current view (view.h) and VR (sourcevr: never).
//-----------------------------------------------------------------------------
int CurrentViewID();
const Vector &CurrentViewOrigin();
const Vector &CurrentViewForward();
const Vector &CurrentViewRight();
const Vector &CurrentViewUp();

inline bool UseVR()
{
	return false;
}

//-----------------------------------------------------------------------------
// The entities the glow trace filter is asked about: a collideable's solid type.
//-----------------------------------------------------------------------------
class ICollideable
{
public:
	virtual SolidType_t GetSolid() const = 0;
};

class IClientUnknown : public IHandleEntity
{
public:
	virtual ICollideable *GetCollideable() = 0;
};

// GlowSightDistance's zoom factor: this build's local-player accessor answers
// NULL (docs/absence-client.md row 6), so the arm is not taken.
class C_BasePlayer
{
public:
	static C_BasePlayer *GetLocalPlayer() { return NULL; }

	float GetFOVDistanceAdjustFactor() { return 1.0f; }
};

// util_shared.h's CTraceFilterSimple: its own rule is a stand-in that records
// it was asked and answers true; CTraceFilterGlow's solid-type rule over it is
// the subject's.
class CTraceFilterSimple : public CTraceFilter
{
public:
	CTraceFilterSimple( const IHandleEntity *passentity, int collisionGroup );
	virtual bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask );

	const IHandleEntity *m_pPassEnt;
	int m_collisionGroup;
};

//-----------------------------------------------------------------------------
// Traces: a line against the scenario's answer (and its entities, which the
// filter is asked about in order).
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
	bool DidHit() const { return fraction < 1.0f || allsolid || startsolid; }

	csurface_t surface;
	int hitgroup;
	short physicsbone;
	IHandleEntity *m_pEnt;
	int hitbox;
};

typedef CGameTrace trace_t;

void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask, ITraceFilter *pFilter,
					 trace_t *ptr );

//-----------------------------------------------------------------------------
// clienteffectprecachesystem.h's registration: the list is a row of the log.
//-----------------------------------------------------------------------------
struct PixelVisPrecache_t
{
	PixelVisPrecache_t( const char *pszName, const char *const *ppszMaterials, int nMaterials );
};

#define CLIENTEFFECT_REGISTER_BEGIN( className )             \
	namespace className                                      \
	{                                                        \
	static const char *const s_pszRegisterName = #className; \
	static const char *const s_pszRegisterMaterials[] = {
#define CLIENTEFFECT_MATERIAL( materialName ) materialName,
#define CLIENTEFFECT_REGISTER_END()                                                  \
	}                                                                                \
	;                                                                                \
	static PixelVisPrecache_t s_Register( s_pszRegisterName, s_pszRegisterMaterials, \
										  ARRAYSIZE( s_pszRegisterMaterials ) );     \
	}
