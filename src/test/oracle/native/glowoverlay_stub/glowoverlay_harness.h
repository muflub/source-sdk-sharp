//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// The world the PRISTINE game/client/glow_overlay.cpp and warp_overlay.cpp are
// compiled into by glowoverlay_oracle_test.cpp. Those files are the client's
// glow overlays: the list of active overlays, each overlay's obstruction (a
// pixel visibility question, or a trace when the card cannot answer), its
// sprites' size and colour by the angle to the view, the quad each sprite is
// drawn as, the warp overlay's own draw, and the sky overlays' obstruction.
// In the game they sit on the pixel visibility, the view, the material
// system and the traces; here each is a stand-in that answers from a scripted
// world and writes down what the subject asked of it.
//
// What is stand-in and what is not:
//
//   * The SUBJECT -- every function of glow_overlay.cpp, warp_overlay.cpp and
//     glow_overlay.h -- is upstream's text, compiled unedited from the clone at
//     valve-sdk.lock, with c_pixel_visibility.h and sun_shared.h beside it.
//   * tier0, tier1 (the ConVars, CUtlLinkedList) and mathlib (VectorNormalize,
//     SinCos, RemapValClamped) are the real libraries; the material system's
//     interfaces, CMeshBuilder, IMaterialVar, VPlane and the CONTENTS_ /
//     SURF_ values are the real public headers.
//   * The material system, its render context, the dynamic mesh, materials
//     and material vars are vtable doubles (glowoverlay_oracle_test.cpp).
//   * Everything declared below is a stand-in: the globals, the current view,
//     the view render's setup and frustum, R_CullSphere (shell_viewrender_
//     client.cpp's, which is view.cpp's), the wireframe question, the pixel
//     visibility (its answers are the script's), the trace, VPROF and the
//     client effect precache macros (the list is a row).
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
#include "tier1/utllinkedlist.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "mathlib/vplane.h"
#include "bspflags.h"
#include "const.h"
#include "worldsize.h"
#include "trace.h"
#include "ihandleentity.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imesh.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"

//-----------------------------------------------------------------------------
// The log.
//-----------------------------------------------------------------------------
void GlowLog( const char *pFmt, ... ) FMTFUNCTION( 1, 2 );

//-----------------------------------------------------------------------------
// The client's globals.
//-----------------------------------------------------------------------------
struct GlowGlobals_t
{
	int framecount;
	float frametime;
	float curtime;
};

extern GlowGlobals_t *gpGlobals;

//-----------------------------------------------------------------------------
// The game-system base glow_overlay.cpp's system derives from.
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
// The current view (view.h), the view render's setup and frustum
// (iviewrender.h, view_shared.h), the frustum cull and the wireframe question.
//-----------------------------------------------------------------------------
const Vector &CurrentViewOrigin();
const Vector &CurrentViewForward();

class CViewSetup
{
public:
	float zFar;
};

class CGlowViewRender
{
public:
	const CViewSetup *GetViewSetup() const;
	VPlane *GetFrustum();
};

extern CGlowViewRender *view;

bool R_CullSphere( VPlane const *pPlanes, int nPlanes, Vector const *pCenter, float radius );
bool ShouldDrawInWireFrameMode();

//-----------------------------------------------------------------------------
// Traces: the scripted world's answer to a line.
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
	IHandleEntity *m_pEnt;
	int hitbox;
};

typedef CGameTrace trace_t;

void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask, const IHandleEntity *ignore,
					 int collisionGroup, trace_t *ptr );

//-----------------------------------------------------------------------------
// VPROF (tier0/vprof.h is shadowed by glowoverlay_stub/tier0/vprof.h).
//-----------------------------------------------------------------------------
#ifndef VPROF
#define VPROF( name )
#endif

//-----------------------------------------------------------------------------
// clienteffectprecachesystem.h's registration: the list is a row of the log.
//-----------------------------------------------------------------------------
struct GlowPrecache_t
{
	GlowPrecache_t( const char *pszName, const char *const *ppszMaterials, int nMaterials );
};

#define CLIENTEFFECT_REGISTER_BEGIN( className )             \
	namespace className                                      \
	{                                                        \
	static const char *const s_pszRegisterName = #className; \
	static const char *const s_pszRegisterMaterials[] = {
#define CLIENTEFFECT_MATERIAL( materialName ) materialName,
#define CLIENTEFFECT_REGISTER_END()                                              \
	}                                                                            \
	;                                                                            \
	static GlowPrecache_t s_Register( s_pszRegisterName, s_pszRegisterMaterials, \
									  ARRAYSIZE( s_pszRegisterMaterials ) );     \
	}
