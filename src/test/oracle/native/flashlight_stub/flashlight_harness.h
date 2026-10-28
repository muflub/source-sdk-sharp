//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// The world the PRISTINE game/client/flashlighteffect.cpp is compiled into by
// flashlight_oracle_test.cpp. That file is the player's flashlight: the
// projected texture's pose, pull-back and attenuation (UpdateLightNew), the
// old dynamic-light flashlight (UpdateLightOld), the trace filter both trace
// with, and its ConVars. In the game it sits on the client shadow manager,
// the engine's dynamic-light pool, the traces and the material system; here
// each of those is a stand-in that answers from a small scripted world and
// writes down what the subject asked of it.
//
// What is stand-in and what is not:
//
//   * The SUBJECT -- every function of flashlighteffect.cpp, and its header
//     flashlighteffect.h -- is upstream's text, compiled unedited from the
//     clone at valve-sdk.lock.
//   * mathlib (VectorNormalize's rsqrt, BasisToQuaternion), tier0 and tier1
//     (the ConVars and their change callback) are the real libraries the test
//     links; FlashlightState_t, dlight_t, ITraceFilter / CTraceFilter and the
//     COLLISION_GROUP_ and MASK_ values are the real public headers.
//   * Everything declared below is a stand-in: the entities the trace filter
//     is asked about, the traces (a swept box against a list of boxes), the
//     shadow manager, the dynamic-light pool, the renderer, the debug
//     overlay, the engine's DX level, the hardware config and the texture
//     reference. Each logs what it was asked and answers from the scenario.
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
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "bspflags.h"
#include "const.h"
#include "trace.h"
#include "ihandleentity.h"
#include "basehandle.h"
#include "engine/IEngineTrace.h"
#include "dlight.h"
#include "materialsystem/imaterialsystem.h"

#ifndef VPROF_BUDGET
#define VPROF_BUDGET( name, group )
#endif
#ifndef VPROF_BUDGETGROUP_SHADOW_DEPTH_TEXTURING
#define VPROF_BUDGETGROUP_SHADOW_DEPTH_TEXTURING "Flashlight Shadows"
#endif

//-----------------------------------------------------------------------------
// The harness's log. Every stand-in the subject reaches writes here.
//-----------------------------------------------------------------------------
void FlashlightLog( const char *pFmt, ... ) FMTFUNCTION( 1, 2 );

// A float as its IEEE bits, the golden's spelling of every float.
unsigned int FlashlightBits( float f );

//-----------------------------------------------------------------------------
// Globals the client DLL provides.
//-----------------------------------------------------------------------------
struct FlashlightGlobals_t
{
	float curtime;
};
extern FlashlightGlobals_t *gpGlobals;

//-----------------------------------------------------------------------------
// The entities the trace filter is asked about. The filter dynamic_casts to
// the view model and the player and asks the collision group, so these are
// the three things an entity here has.
//-----------------------------------------------------------------------------
class C_BaseEntity : public IHandleEntity
{
public:
	C_BaseEntity( const char *pszName, int nCollisionGroup )
		: m_pszName( pszName ), m_nCollisionGroup( nCollisionGroup ) {}
	virtual ~C_BaseEntity() {}

	virtual void SetRefEHandle( const CBaseHandle &handle ) { m_Handle = handle; }
	virtual const CBaseHandle &GetRefEHandle() const { return m_Handle; }

	int GetCollisionGroup() const { return m_nCollisionGroup; }

	const char *m_pszName;
	int m_nCollisionGroup;
	CBaseHandle m_Handle;
};

class C_BaseViewModel : public C_BaseEntity
{
public:
	C_BaseViewModel( const char *pszName, int nCollisionGroup ) : C_BaseEntity( pszName, nCollisionGroup ) {}
};

class C_BasePlayer : public C_BaseEntity
{
public:
	C_BasePlayer( const char *pszName, int nCollisionGroup ) : C_BaseEntity( pszName, nCollisionGroup ), m_nMoveType( MOVETYPE_WALK ) {}

	// The scenario's local player; never NULL here, as stock's never is.
	static C_BasePlayer *GetLocalPlayer();

	MoveType_t GetMoveType() const { return (MoveType_t)m_nMoveType; }

	int m_nMoveType;
};

// A thing a trace can hit that is no entity (a static prop): the filter's
// NULL arm. The handle is its own object, and EntityFromEntityHandle answers
// NULL for it.
class CFlashlightNonEntity : public IHandleEntity
{
public:
	virtual void SetRefEHandle( const CBaseHandle &handle ) {}
	virtual const CBaseHandle &GetRefEHandle() const { return m_Handle; }
	CBaseHandle m_Handle;
};

C_BaseEntity *EntityFromEntityHandle( IHandleEntity *pHandleEntity );

//-----------------------------------------------------------------------------
// Traces: a swept box against the scenario's boxes.
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

void UTIL_TraceHull( const Vector &vecAbsStart, const Vector &vecAbsEnd, const Vector &hullMin,
                     const Vector &hullMax, unsigned int mask, ITraceFilter *pFilter, trace_t *ptr );
void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask,
                     ITraceFilter *pFilter, trace_t *ptr );

//-----------------------------------------------------------------------------
// The client shadow manager's four flashlight calls (iclientshadowmgr.h).
//-----------------------------------------------------------------------------
typedef unsigned short ClientShadowHandle_t;
enum
{
	CLIENTSHADOW_INVALID_HANDLE = (ClientShadowHandle_t)~0
};

class IFlashlightShadowMgr
{
public:
	ClientShadowHandle_t CreateFlashlight( const FlashlightState_t &lightState );
	void UpdateFlashlightState( ClientShadowHandle_t shadowHandle, const FlashlightState_t &lightState );
	void UpdateProjectedTexture( ClientShadowHandle_t h, bool force );
	void DestroyFlashlight( ClientShadowHandle_t shadowHandle );
};
extern IFlashlightShadowMgr *g_pClientShadowMgr;

//-----------------------------------------------------------------------------
// The engine's dynamic-light pool (iefx.h), the renderer (ivrenderview.h),
// the engine (cdll_int.h), the debug overlay (ivdebugoverlay.h) and the
// hardware config: only what the subject calls.
//-----------------------------------------------------------------------------
class IFlashlightEfx
{
public:
	dlight_t *CL_AllocDlight( int key );
};
extern IFlashlightEfx *effects;

class IFlashlightRender
{
public:
	void TouchLight( dlight_t *light );
};
extern IFlashlightRender *render;

class IFlashlightEngine
{
public:
	int GetDXSupportLevel();
};
extern IFlashlightEngine *engine;

class IFlashlightDebugOverlay
{
public:
	void AddBoxOverlay( const Vector &origin, const Vector &mins, const Vector &max, QAngle const &orientation,
	                    int r, int g, int b, int a, float duration );
	void AddLineOverlay( const Vector &origin, const Vector &dest, int r, int g, int b, bool noDepthTest,
	                     float duration );
};
extern IFlashlightDebugOverlay *debugoverlay;

class IFlashlightHardwareConfig
{
public:
	bool SupportsBorderColor();
};
extern IFlashlightHardwareConfig *g_pMaterialSystemHardwareConfig;

// The material system's texture reference (MaterialSystemUtil.h): what the
// subject asks it is Init and the cast to ITexture *. The pointer it answers
// names the texture in the log.
class CTextureReference
{
public:
	CTextureReference() : m_pTexture( NULL ) {}
	void Init( char const *pTexture, const char *pTextureGroupName, bool bComplain = true );
	operator ITexture *() { return m_pTexture; }
	ITexture *m_pTexture;
};
const char *FlashlightTextureName( const ITexture *pTexture );

//-----------------------------------------------------------------------------
// The tool framework's recording mode (toolframework_client.h), which the
// harness never enters: the subject's KeyValues messages are SFM's.
//-----------------------------------------------------------------------------
class IFlashlightClientTools
{
public:
	bool IsInRecordingMode() { return false; }
};
extern IFlashlightClientTools *clienttools;

class KeyValues
{
public:
	KeyValues( const char *pszName ) {}
	void SetFloat( const char *pszKey, float flValue ) {}
	void SetInt( const char *pszKey, int nValue ) {}
	void SetPtr( const char *pszKey, void *pValue ) {}
	void deleteThis() { delete this; }
};
#define HTOOLHANDLE_INVALID 0
inline void ToolFramework_PostToolMessage( int hEntity, KeyValues *msg ) {}
