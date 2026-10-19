//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// The world the PRISTINE game/client/clientleafsystem.cpp is compiled into by
// leafsystem_oracle_test.cpp. That file is the client leaf system: the
// registry of renderables and the BSP leaves they sit in, the per-view
// render list built over it (BuildRenderablesList / CollateRenderablesInLeaf),
// the translucent render leaf and back-to-front sort, the view models' split
// and the shadows projected into leaves. In the game it sits on the engine's
// BSP query and frustum, occlusion and area-portal tests, the model info, the
// detail object system and the shadow manager; here each of those is a
// stand-in that answers from a scripted world and writes down what the
// subject asked of it.
//
// What is stand-in and what is not:
//
//   * The SUBJECT -- every function of clientleafsystem.cpp, and its header
//     clientleafsystem.h, and igamesystem.h that header derives from -- is
//     upstream's text, compiled unedited from the clone at valve-sdk.lock.
//   * tier0 (CTSList, ThreadInMainThread, the asserts), tier1 (CUtlLinkedList,
//     CBidirectionalSet, the fixed-size allocator, the ConVars) and mathlib
//     are the real libraries the test links; IClientRenderable,
//     IClientUnknown, the render groups, WorldListInfo_t, ISpatialQuery and
//     the shadow flags are the real public headers.
//   * Everything declared below is a stand-in: the engine (leaf count, leaf
//     areas, the BSP query, CullBox, DoesBoxTouchAreaFrustum, IsOccluded,
//     Con_NPrintf), the renderer's AreAnyLeavesVisible, the model info's
//     model types, the MDL cache lock, the shadow manager, the detail object
//     system, C_BaseEntity, the client's globals, VPROF and the thread pool
//     (the subject's threaded arms are behind constant `false`s).
//
//=============================================================================

#ifndef SS_LEAFSYSTEM_HARNESS_H
#define SS_LEAFSYSTEM_HARNESS_H

#include <stdio.h>
#include <float.h>
#include <string.h>
#include <stdlib.h>

#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier0/threadtools.h"
#include "tier0/tslist.h"
#include "tier1/convar.h"
#include "tier1/utlvector.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
// The renderable interfaces are the PRISTINE public headers (copied beside
// the subject, ahead of the farm's): the farm's answer GetBaseEntity() with
// SourceSharp's IGameEntity, and the subject asks it for a C_BaseEntity.
#include "basehandle.h"
#include "iclientunknown.h"
#include "iclientrenderable.h"
#include "ivrenderview.h"
#include "engine/ishadowmgr.h"
#include "bsptreedata.h"

#ifndef VPROF_BUDGET
#define VPROF_BUDGET( name, group )
#endif
#ifndef VPROF
#define VPROF( name )
#endif
#ifndef VPROF_BUDGETGROUP_SHADOW_DEPTH_TEXTURING
#define VPROF_BUDGETGROUP_SHADOW_DEPTH_TEXTURING "Flashlight Shadows"
#endif

//-----------------------------------------------------------------------------
// The harness's log. Every stand-in the subject reaches writes here.
//-----------------------------------------------------------------------------
void LeafLog( const char *pFmt, ... ) FMTFUNCTION( 1, 2 );

// A float as its IEEE bits, the golden's spelling of every float.
unsigned int LeafBits( float f );

//-----------------------------------------------------------------------------
// Globals the client DLL provides.
//-----------------------------------------------------------------------------
struct LeafGlobals_t
{
	int framecount;
	float curtime;
};
extern LeafGlobals_t *gpGlobals;

// viewrender.h's view ids, the ones the subject and the scenario name.
enum view_id_t { VIEW_ILLEGAL = -2, VIEW_MAIN = 0, VIEW_MONITOR = 2, VIEW_REFLECTION = 3, VIEW_SHADOW_DEPTH_TEXTURE = 7 };

// iclientshadowmgr.h's receiver kinds.
enum ShadowReceiver_t
{
	SHADOW_RECEIVER_BRUSH_MODEL = 0,
	SHADOW_RECEIVER_STATIC_PROP,
	SHADOW_RECEIVER_STUDIO_MODEL,
};

//-----------------------------------------------------------------------------
// The scripted renderable. It is its own C_BaseEntity, as a client entity is
// (the pristine iclientunknown.h answers GetBaseEntity() with one), and never
// follows another: the subject's bone-merge arms ask and are told no. Every
// answer is a member the scenario sets.
//-----------------------------------------------------------------------------
class C_BaseEntity : public CDefaultClientRenderable
{
public:
	explicit C_BaseEntity( int nId ) : m_nId( nId )
	{
		m_vecAbsMins.Init(); m_vecAbsMaxs.Init(); m_vecMins.Init(); m_vecMaxs.Init(); m_vecOrigin.Init();
		m_angAngles.Init();
	}
	virtual ~C_BaseEntity() {}

	// IHandleEntity
	virtual void SetRefEHandle( const CBaseHandle &handle ) { m_RefEHandle = handle; }
	virtual const CBaseHandle &GetRefEHandle() const { return m_RefEHandle; }

	// IClientUnknown
	virtual C_BaseEntity *GetBaseEntity() { return this; }

	// IClientRenderable
	virtual const Vector &GetRenderOrigin( void ) { return m_vecOrigin; }
	virtual const QAngle &GetRenderAngles( void ) { return m_angAngles; }
	virtual const matrix3x4_t &RenderableToWorldTransform() { return m_Transform; }
	virtual bool ShouldDraw( void ) { return true; }
	virtual bool IsTransparent( void ) { return m_bTransparent; }
	virtual bool IsTwoPass( void ) { return m_bTwoPass; }
	virtual const model_t *GetModel() const { return (const model_t *)(intp)( m_nModelType + 1 ); }
	virtual void ComputeFxBlend();
	virtual int GetFxBlend( void );
	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) { mins = m_vecMins; maxs = m_vecMaxs; }
	virtual void GetRenderBoundsWorldspace( Vector &mins, Vector &maxs );
	virtual bool ShouldReceiveProjectedTextures( int flags ) { return ( m_nReceiveFlags & flags ) != 0; }

	// C_BaseEntity, as the subject's bounds helpers ask it.
	bool IsFollowingEntity() { return false; }
	C_BaseEntity *GetFollowedEntity() { return NULL; }
	C_BaseEntity *GetMoveParent() { return NULL; }
	const Vector &GetLocalOrigin() const { return m_vecOrigin; }

	int     m_nId;
	int     m_nModelType = 0;         // mod_bad, mod_brush, mod_sprite, mod_studio
	bool    m_bTransparent = false;
	bool    m_bTwoPass = false;
	int     m_nFxBlend = 255;
	int     m_nBlendTo = -1;          // ComputeFxBlend sets the blend to this when >= 0
	int     m_nGroupOnBlend = -1;     // and the render group (SetRenderGroup) when >= 0
	int     m_nReceiveFlags = 0;      // the projected-texture kinds it takes
	Vector  m_vecAbsMins, m_vecAbsMaxs, m_vecMins, m_vecMaxs, m_vecOrigin;
	QAngle  m_angAngles;
	matrix3x4_t m_Transform;
	CBaseHandle m_RefEHandle;
};

//-----------------------------------------------------------------------------
// The engine: the leaves, their areas and the BSP query over them, and the
// three per-box answers, each read from the scenario's script.
//-----------------------------------------------------------------------------
class LeafEngine
{
public:
	int LevelLeafCount();
	int GetLeavesArea( int *pLeaves, int nLeaves );
	ISpatialQuery *GetBSPTreeQuery();
	bool DoesBoxTouchAreaFrustum( const Vector &mins, const Vector &maxs, int iArea );
	bool CullBox( const Vector &mins, const Vector &maxs );
	bool IsOccluded( const Vector &vecAbsMins, const Vector &vecAbsMaxs );
	void Con_NPrintf( int pos, const char *fmt, ... );
};
extern LeafEngine *engine;

// ivrenderview.h declares `render` an IVRenderView; the subject asks it one
// question, so the name is the stand-in's from here on.
class LeafRender
{
public:
	bool AreAnyLeavesVisible( int *pLeafList, int nLeaves );
};
#define render g_pLeafRender
extern LeafRender *render;

class LeafModelInfo
{
public:
	int GetModelType( const model_t *pModel ) const { return pModel ? (int)( (intp)pModel - 1 ) : 0; }
};
extern LeafModelInfo *modelinfo;

class LeafMdlCache
{
public:
	void BeginLock() {}
	void EndLock() {}
};
extern LeafMdlCache *mdlcache;

class LeafShadowMgr
{
public:
	void AddShadowToReceiver( ClientShadowHandle_t handle, IClientRenderable *pRenderable, ShadowReceiver_t type );
	void RemoveAllShadowsFromReceiver( IClientRenderable *pRenderable, ShadowReceiver_t type );
};
extern LeafShadowMgr *g_pClientShadowMgr;

class LeafDetailSystem
{
public:
	IClientRenderable *GetDetailModel( int idx );
};
LeafDetailSystem *DetailObjectSystem();

// vstdlib/jobthread.h's ParallelProcess: the subject names it on its threaded
// arms, which are compiled and never taken (`bThreaded = false`).
template < typename... Args > inline void ParallelProcess( const char *, Args &&... ) {}

#endif // SS_LEAFSYSTEM_HARNESS_H
