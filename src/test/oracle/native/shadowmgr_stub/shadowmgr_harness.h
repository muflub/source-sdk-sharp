//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// The world the PRISTINE game/client/clientshadowmgr.cpp is compiled into by
// shadowmgr_oracle_test.cpp. That file is the client shadow manager: which
// shadows exist and of what kind, when each is re-projected and how (the blob
// and render-to-texture bases, the flashlight's frustum), which receivers each
// lands on, which render-to-texture shadows are redrawn into the shadow atlas
// and at what size (the visible list, the r_shadowmaxrendered budget, the LOD
// fallback, CTextureAllocator's LRU), the flashlight depth textures, the
// debug overlays and the two material proxies (Shadow, ShadowModel). In the
// game it sits on the engine's shadow manager, the static props, the model
// and brush shadow draws, the material system's render context and render
// targets, the BSP query, the client leaf system and the renderables; here
// each of those is a stand-in that answers from a scripted world and writes
// down what the subject asked of it.
//
// What is stand-in and what is not:
//
//   * The SUBJECT -- every function of clientshadowmgr.cpp, and the headers it
//     was written against (iclientshadowmgr.h, igamesystem.h, and the
//     pristine iclientunknown.h, iclientrenderable.h and
//     toolframework/itoolentity.h, whose farm copies differ) -- is upstream's
//     text, compiled unedited from the clone at valve-sdk.lock.
//   * tier0 (the command line, the spew, the asserts), tier1 (CUtlLinkedList,
//     CUtlRBTree, CUtlMultiList, CUtlVector, the ConVars and commands,
//     InterfaceReg), mathlib (the vector and matrix functions, R_CullBox,
//     CalculateAABBFromProjectionMatrix, GeneratePerspectiveFrustum) and the
//     pristine public/collisionutils.cpp (ComputeSeparatingPlane,
//     IsBoxIntersectingSphere) are the real code the test links; IShadowMgr,
//     ShadowInfo_t, FlashlightState_t, CViewSetup, IClientRenderable,
//     IMaterialProxy, ISpatialQuery and Ray_t are the real public headers.
//   * Everything declared below is a stand-in: the engine's shadow manager
//     (a full IShadowMgr that keeps what it was told), the static prop
//     manager, the model and brush shadow draws, the material system and its
//     render context, the render targets and materials (opaque tokens with a
//     name), the dynamic mesh, the engine (CullBox, the ambient light, the
//     BSP query over leaves cut along x), the view, the debug overlay, the
//     client leaf system (it keeps each shadow's leaves, calls the subject
//     back for the receivers registered in them, and enumerates them), the
//     entity list and the scripted renderables, C_BaseEntity (never handed
//     out: the arms that take one fire 0 times, as the fork's deletions made
//     them), the MDL cache lock, VPROF, PIX and the thread pool (the subject's
//     threaded arm is behind a constant `false`).
//
//=============================================================================

#pragma once

#include <stdio.h>
#include <float.h>
#include <string.h>
#include <stdlib.h>

#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier0/icommandline.h"
#include "tier1/convar.h"
#include "tier1/utlvector.h"
#include "tier1/utllinkedlist.h"
#include "tier1/utlrbtree.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "mathlib/vmatrix.h"
// The renderable interfaces are the PRISTINE public headers (copied beside
// the subject, ahead of the farm's): the farm's answer GetBaseEntity() with
// SourceSharp's IGameEntity, and the subject asks it for a C_BaseEntity.
#include "basehandle.h"
#include "iclientunknown.h"
#include "iclientrenderable.h"
#include "engine/ishadowmgr.h"
#include "engine/ivmodelrender.h"
#include "engine/ivmodelinfo.h"
#include "ivrenderview.h"
#include "bsptreedata.h"
#include "cmodel.h"
#include "model_types.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/imesh.h"
#include "materialsystem/itexture.h"
#include "view_shared.h"

#ifndef VPROF_BUDGET
#define VPROF_BUDGET( name, group )
#endif
#ifndef VPROF
#define VPROF( name )
#endif
#ifndef VPROF_BUDGETGROUP_SHADOW_DEPTH_TEXTURING
#define VPROF_BUDGETGROUP_SHADOW_DEPTH_TEXTURING "Flashlight Shadows"
#endif
#ifndef VPROF_BUDGETGROUP_SHADOW_RENDERING
#define VPROF_BUDGETGROUP_SHADOW_RENDERING "Shadow Rendering"
#endif
#undef PIXEVENT
#define PIXEVENT( context, name )
#undef MDLCACHE_CRITICAL_SECTION
#define MDLCACHE_CRITICAL_SECTION()

//-----------------------------------------------------------------------------
// The harness's log. Every stand-in the subject reaches writes here.
//-----------------------------------------------------------------------------
void ShadowLog( const char *pFmt, ... ) FMTFUNCTION( 1, 2 );

// A float as its IEEE bits, the golden's spelling of every float.
unsigned int ShadowBits( float f );

//-----------------------------------------------------------------------------
// Globals the client DLL provides.
//-----------------------------------------------------------------------------
struct ShadowGlobals_t
{
	int framecount;
	float curtime;
};

extern ShadowGlobals_t *gpGlobals;

// icliententityinternal.h's handle; cliententitylist.h's invalid one.
typedef CBaseHandle ClientEntityHandle_t;
#define INVALID_CLIENTENTITY_HANDLE CBaseHandle( INVALID_EHANDLE )

//-----------------------------------------------------------------------------
// C_BaseEntity, as the subject names it: its arms (the rendering clip plane,
// the static-prop receiver's NPC/player test, the debug overlay's entindex,
// the threaded bone setups, the flashlight target) are compiled and never
// taken, because nothing here answers one -- every renderable's
// GetBaseEntity() is NULL, as the fork's deletions made it. Each member
// counts a touch, and the oracle holds the count to 0.
//-----------------------------------------------------------------------------
extern int g_nBaseEntityTouches;

class C_BaseAnimating;

class C_BaseEntity : public CDefaultClientRenderable
{
public:
	C_BaseEntity()
	{
		m_bEnableRenderingClipPlane = false;
		m_fRenderingClipPlane[0] = m_fRenderingClipPlane[1] = m_fRenderingClipPlane[2] = m_fRenderingClipPlane[3] =
			0.0f;
	}

	virtual ~C_BaseEntity() {}

	// IHandleEntity
	virtual void SetRefEHandle( const CBaseHandle &handle ) { m_RefEHandle = handle; }

	virtual const CBaseHandle &GetRefEHandle() const { return m_RefEHandle; }

	// IClientUnknown
	virtual ICollideable *GetCollideable()
	{
		++g_nBaseEntityTouches;
		return NULL;
	}

	virtual IClientNetworkable *GetClientNetworkable()
	{
		++g_nBaseEntityTouches;
		return NULL;
	}

	virtual IClientRenderable *GetClientRenderable()
	{
		++g_nBaseEntityTouches;
		return this;
	}

	virtual IClientEntity *GetIClientEntity()
	{
		++g_nBaseEntityTouches;
		return NULL;
	}

	virtual C_BaseEntity *GetBaseEntity()
	{
		++g_nBaseEntityTouches;
		return this;
	}

	virtual IClientThinkable *GetClientThinkable()
	{
		++g_nBaseEntityTouches;
		return NULL;
	}

	// IClientRenderable
	virtual const Vector &GetRenderOrigin( void )
	{
		++g_nBaseEntityTouches;
		return m_vecOrigin;
	}

	virtual const QAngle &GetRenderAngles( void )
	{
		++g_nBaseEntityTouches;
		return m_angAngles;
	}

	virtual const matrix3x4_t &RenderableToWorldTransform()
	{
		++g_nBaseEntityTouches;
		return m_Transform;
	}

	virtual bool ShouldDraw( void )
	{
		++g_nBaseEntityTouches;
		return true;
	}

	virtual bool IsTransparent( void )
	{
		++g_nBaseEntityTouches;
		return false;
	}

	virtual void GetRenderBounds( Vector &mins, Vector &maxs )
	{
		++g_nBaseEntityTouches;
		mins = maxs = m_vecOrigin;
	}

	virtual const model_t *GetModel() const
	{
		++g_nBaseEntityTouches;
		return NULL;
	}

	// C_BaseEntity, as the subject asks it.
	int GetFlags()
	{
		++g_nBaseEntityTouches;
		return 0;
	}

	bool IsNPC()
	{
		++g_nBaseEntityTouches;
		return false;
	}

	C_BaseAnimating *GetBaseAnimating()
	{
		++g_nBaseEntityTouches;
		return NULL;
	}

	C_BaseEntity *FirstMoveChild()
	{
		++g_nBaseEntityTouches;
		return NULL;
	}

	C_BaseEntity *NextMovePeer()
	{
		++g_nBaseEntityTouches;
		return NULL;
	}

	int entindex()
	{
		++g_nBaseEntityTouches;
		return 0;
	}

	bool m_bEnableRenderingClipPlane;
	float m_fRenderingClipPlane[4];
	Vector m_vecOrigin;
	QAngle m_angAngles;
	matrix3x4_t m_Transform;
	CBaseHandle m_RefEHandle;
};

class C_BaseAnimating : public C_BaseEntity
{
};

// ehandle.h's EHANDLE, as the subject uses its flashlight target: never set
// to an entity here (the fork deleted the target's tails), so it is NULL.
class CShadowEHandle
{
public:
	CShadowEHandle()
		: m_pEntity( NULL )
	{
	}

	C_BaseEntity *Get() const { return m_pEntity; }

	operator C_BaseEntity *() const { return m_pEntity; }

	C_BaseEntity *operator->() const
	{
		++g_nBaseEntityTouches;
		return m_pEntity;
	}

	bool operator!() const { return m_pEntity == NULL; }

	C_BaseEntity *m_pEntity;
};

typedef CShadowEHandle EHANDLE;

// The flags' type the subject packs into its own shadow flags.
#include "toolframework/itoolentity.h"

//-----------------------------------------------------------------------------
// The scripted renderable. Every question the subject asks one is a Q row
// with its answer; MarkShadowDirty and CreateModelInstance are E rows. Its
// answers are members the scenario sets (and the T row it is described by
// carries, so the C# replay rebuilds it).
//-----------------------------------------------------------------------------
class CShadowThing : public CDefaultClientRenderable
{
public:
	explicit CShadowThing( int nId );

	virtual ~CShadowThing() {}

	// IHandleEntity
	virtual void SetRefEHandle( const CBaseHandle &handle ) { m_RefEHandle = handle; }

	virtual const CBaseHandle &GetRefEHandle() const { return m_RefEHandle; }

	// IClientUnknown: the fork's renderables answer no C_BaseEntity.
	virtual ICollideable *GetCollideable() { return NULL; }

	virtual IClientNetworkable *GetClientNetworkable() { return NULL; }

	virtual IClientRenderable *GetClientRenderable() { return this; }

	virtual IClientEntity *GetIClientEntity() { return NULL; }

	virtual C_BaseEntity *GetBaseEntity();

	virtual IClientThinkable *GetClientThinkable() { return NULL; }

	// IClientRenderable
	virtual IClientUnknown *GetIClientUnknown() { return this; }

	virtual const Vector &GetRenderOrigin( void );
	virtual const QAngle &GetRenderAngles( void );
	virtual const matrix3x4_t &RenderableToWorldTransform();

	virtual bool ShouldDraw( void ) { return true; }

	virtual bool IsTransparent( void ) { return false; }

	virtual ClientShadowHandle_t GetShadowHandle() const;
	virtual const model_t *GetModel() const;
	virtual int GetBody();
	virtual int GetSkin();

	virtual void GetRenderBounds( Vector &mins, Vector &maxs )
	{
		mins = m_vecMins;
		maxs = m_vecMaxs;
	}

	virtual void GetRenderBoundsWorldspace( Vector &mins, Vector &maxs );
	virtual void GetShadowRenderBounds( Vector &mins, Vector &maxs, ShadowType_t shadowType );
	virtual bool ShouldReceiveProjectedTextures( int flags );
	virtual bool GetShadowCastDistance( float *pDist, ShadowType_t shadowType ) const;
	virtual bool GetShadowCastDirection( Vector *pDirection, ShadowType_t shadowType ) const;
	virtual bool IsShadowDirty();
	virtual void MarkShadowDirty( bool bDirty );
	virtual IClientRenderable *GetShadowParent();
	virtual IClientRenderable *FirstShadowChild();
	virtual IClientRenderable *NextShadowPeer();
	virtual void CreateModelInstance();
	virtual ModelInstanceHandle_t GetModelInstance();

	int m_nId;
	bool m_bHasModel = true;
	int m_nModelType = mod_studio; // mod_bad, mod_brush, mod_sprite, mod_studio
	Vector m_vecOrigin, m_vecMins, m_vecMaxs, m_vecRttMins, m_vecRttMaxs, m_vecAbsMins, m_vecAbsMaxs;
	QAngle m_angAngles;
	matrix3x4_t m_Transform;
	bool m_bCastDistance = false; // overrides GetShadowCastDistance
	float m_flCastDistance = 0.0f;
	bool m_bCastDirection = false; // overrides GetShadowCastDirection
	Vector m_vecCastDirection;
	int m_nParent = -1, m_nFirstChild = -1, m_nNextPeer = -1; // the shadow hierarchy, by id
	int m_nReceiveFlags = 0; // the projected-texture kinds it takes
	ClientShadowHandle_t m_hShadow = CLIENTSHADOW_INVALID_HANDLE; // what GetShadowHandle answers
	bool m_bShadowDirty = false;
	ModelInstanceHandle_t m_hInstance = MODEL_INSTANCE_INVALID;
	int m_nBody = 0, m_nSkin = 0;
	bool m_bBonesOk = true; // DrawModelShadowSetup answers a bone array
	bool m_bDirtyOnOrigin = false; // GetRenderOrigin re-dirties the shadow, as an aiment's does
	bool m_bGone = false; // the entity list no longer answers it
	CBaseHandle m_RefEHandle;
};

// The thing's entity handle: entry m_nId, serial 1.
ClientEntityHandle_t ShadowEntityOf( int nId );

//-----------------------------------------------------------------------------
// The entity list: a handle answers its thing until the thing is gone.
//-----------------------------------------------------------------------------
class CShadowEntityList
{
public:
	IClientRenderable *GetClientRenderableFromHandle( ClientEntityHandle_t h );
	C_BaseEntity *GetBaseEntityFromHandle( ClientEntityHandle_t h );
};

CShadowEntityList &ClientEntityList();

//-----------------------------------------------------------------------------
// The client leaf system's shadow half (clientleafsystem.h).
//-----------------------------------------------------------------------------
typedef unsigned short ClientLeafShadowHandle_t;

enum
{
	CLIENT_LEAF_SHADOW_INVALID_HANDLE = (ClientLeafShadowHandle_t)~0
};

abstract_class IClientLeafShadowEnum
{
public:
	virtual void EnumShadow( ClientShadowHandle_t userId ) = 0;
};

class CShadowLeafSystem
{
public:
	ClientLeafShadowHandle_t AddShadow( ClientShadowHandle_t userId, unsigned short flags );
	void RemoveShadow( ClientLeafShadowHandle_t h );
	void ProjectShadow( ClientLeafShadowHandle_t handle, int nLeafCount, const int *pLeafList );
	void ProjectFlashlight( ClientLeafShadowHandle_t handle, int nLeafCount, const int *pLeafList );
	void EnumerateShadowsInLeaves( int leafCount, LeafIndex_t *pLeaves, IClientLeafShadowEnum *pEnum );
};

CShadowLeafSystem *ClientLeafSystem();

//-----------------------------------------------------------------------------
// Render targets and materials: opaque tokens with a name, so the log says
// which one the subject handed the engine. The subject never calls through
// an ITexture or IMaterial it made; the references are these.
//-----------------------------------------------------------------------------
struct ShadowTexture_t
{
	char m_szName[64];
	int m_nWidth, m_nHeight;

	int GetActualWidth() const { return m_nWidth; }

	int GetActualHeight() const { return m_nHeight; }
};

struct ShadowMaterial_t
{
	char m_szName[64];
	void ColorModulate( float r, float g, float b );
};

const char *ShadowTextureName( const ITexture *pTexture );
const char *ShadowMaterialName( const IMaterial *pMaterial );

class CShadowTextureReference
{
public:
	CShadowTextureReference()
		: m_pTexture( NULL )
	{
	}

	void InitRenderTarget( int w, int h, RenderTargetSizeMode_t sizeMode, ImageFormat fmt,
						   MaterialRenderTargetDepth_t depth, bool bHDR, const char *pStrOptionalName = NULL );
	void Shutdown( bool bDeleteIfUnReferenced = false );

	operator ITexture *() const { return (ITexture *)m_pTexture; }

	ShadowTexture_t *operator->() const { return m_pTexture; }

	bool IsValid() const { return m_pTexture != NULL; }

	ShadowTexture_t *m_pTexture;
};

class CShadowMaterialReference
{
public:
	CShadowMaterialReference()
		: m_pMaterial( NULL )
	{
	}

	void Init( const char *pMaterialName, const char *pTextureGroupName, bool bComplain = true );
	void Shutdown();

	operator IMaterial *() const { return (IMaterial *)m_pMaterial; }

	ShadowMaterial_t *operator->() const { return m_pMaterial; }

	ShadowMaterial_t *m_pMaterial;
};

#define CTextureReference CShadowTextureReference
#define CMaterialReference CShadowMaterialReference

//-----------------------------------------------------------------------------
// The material system, its render context and the dynamic mesh.
//-----------------------------------------------------------------------------
class CShadowMesh
{
public:
	void Draw( int firstIndex = -1, int numIndices = 0 );
};

class CShadowMeshBuilder
{
public:
	void Begin( CShadowMesh *pMesh, MaterialPrimitiveType_t type, int numPrimitives );
	void Position3f( float x, float y, float z );
	void TexCoord2f( int stage, float s, float t );
	void Color4ub( unsigned char r, unsigned char g, unsigned char b, unsigned char a );
	void AdvanceVertex();
	void End();
};

class CShadowRenderContext
{
public:
	float ComputePixelDiameterOfSphere( const Vector &vecAbsOrigin, float flRadius );
	MaterialFogMode_t GetFogMode();
	void FogMode( MaterialFogMode_t fogMode );
	void Bind( IMaterial *pMaterial, void *proxyData = 0 );
	CShadowMesh *GetDynamicMesh( bool buffered = true, void *pVertexOverride = 0, void *pIndexOverride = 0,
								 IMaterial *pAutoBind = 0 );
	void ClearColor4ub( unsigned char r, unsigned char g, unsigned char b, unsigned char a );
	void ClearColor3ub( unsigned char r, unsigned char g, unsigned char b );
	MaterialHeightClipMode_t GetHeightClipMode();
	void SetHeightClipMode( MaterialHeightClipMode_t nHeightClipMode );
	void MatrixMode( MaterialMatrixMode_t matrixMode );
	void PushMatrix();
	void PopMatrix();
	void LoadIdentity();
	void LoadMatrix( const VMatrix &matrix );
	void Scale( float x, float y, float z );
	void Ortho( double left, double top, double right, double bottom, double zNear, double zFar );
	void PushRenderTargetAndViewport( ITexture *pTexture );
	void PopRenderTargetAndViewport();
	void ClearBuffers( bool bClearColor, bool bClearDepth, bool bClearStencil = false );
	void Viewport( int x, int y, int width, int height );
	void CopyRenderTargetToTextureEx( ITexture *pTexture, int nRenderTargetID, Rect_t *pSrcRect,
									  Rect_t *pDstRect = NULL );
	void SetShadowDepthBiasFactors( float fSlopeScaleDepthBias, float fDepthBias );
};

class CShadowMaterials;

class CShadowRenderContextPtr
{
public:
	explicit CShadowRenderContextPtr( CShadowMaterials *pMaterials );
	CShadowRenderContext *operator->() const;
};

#define CMatRenderContextPtr CShadowRenderContextPtr
#define CMeshBuilder CShadowMeshBuilder
#define IMesh CShadowMesh

class CShadowMaterials
{
public:
	bool SupportsShadowDepthTextures();
	void AddRestoreFunc( MaterialBufferRestoreFunc_t func );
	void RemoveRestoreFunc( MaterialBufferRestoreFunc_t func );
	ImageFormat GetShadowDepthTextureFormat();
	ImageFormat GetNullTextureFormat();
	void BeginRenderTargetAllocation();
	void EndRenderTargetAllocation();
	void UncacheUnusedMaterials( bool bRecomputeStateSnapshots = false );
};

// The real headers declare `materials` an IMaterialSystem; the name is the
// stand-in's from here on.
#define materials g_pShadowMaterials
extern CShadowMaterials *materials;

class CShadowHardwareConfig
{
public:
	int GetDXSupportLevel();
};

extern CShadowHardwareConfig *g_pMaterialSystemHardwareConfig;

//-----------------------------------------------------------------------------
// The engine, the model info, the model and brush shadow draws, the static
// props, the view and the debug overlay: only what the subject calls.
//-----------------------------------------------------------------------------
class CShadowEngine
{
public:
	bool CullBox( const Vector &mins, const Vector &maxs );
	void GetAmbientLightColor( Vector &ambientColor );
	ISpatialQuery *GetBSPTreeQuery();
};

extern CShadowEngine *engine;

class CShadowModelInfo
{
public:
	int GetModelType( const model_t *pModel ) const;
};

// ivmodelinfo.h declares `modelinfo` an IVModelInfoClient; the name is the
// stand-in's from here on.
#define modelinfo g_pShadowModelInfo
extern CShadowModelInfo *modelinfo;

class CShadowModelRender
{
public:
	matrix3x4_t *DrawModelShadowSetup( IClientRenderable *pRenderable, int body, int skin, DrawModelInfo_t *pInfo );
	void DrawModelShadow( IClientRenderable *pRenderable, const DrawModelInfo_t &info, matrix3x4_t *pBoneToWorld );
};

extern CShadowModelRender *modelrender;

// ivrenderview.h declares `render` an IVRenderView; the subject asks it one
// thing, so the name is the stand-in's from here on.
class CShadowRenderView
{
public:
	void DrawBrushModelShadow( IClientRenderable *pRenderable );
};

#define render g_pShadowRenderView
extern CShadowRenderView *render;

class CShadowStaticPropMgr
{
public:
	void AddShadowToStaticProp( ShadowHandle_t shadowHandle, IClientRenderable *pRenderable );
	void RemoveAllShadowsFromStaticProp( IClientRenderable *pRenderable );
};

extern CShadowStaticPropMgr *staticpropmgr;

class CShadowView
{
public:
	void UpdateShadowDepthTexture( ITexture *pRenderTarget, ITexture *pDepthTexture, const CViewSetup &shadowView );
};

extern CShadowView *view;

class CShadowDebugOverlay
{
public:
	void AddLineOverlay( const Vector &origin, const Vector &dest, int r, int g, int b, bool noDepthTest,
						 float duration );
	void AddTextOverlay( const Vector &origin, int line_offset, const char *format, ... );
};

extern CShadowDebugOverlay *debugoverlay;

extern IShadowMgr *shadowmgr;

//-----------------------------------------------------------------------------
// The tool framework (toolframework_client.h): never recording.
//-----------------------------------------------------------------------------
inline bool ToolsEnabled()
{
	return false;
}

//-----------------------------------------------------------------------------
// The thread pool (vstdlib/jobthread.h): the subject's threaded arm is
// compiled and never taken (m_bThreaded is a constant false).
//-----------------------------------------------------------------------------
class CShadowThreadPool
{
public:
	int NumIdleThreads() { return 0; }
};

extern CShadowThreadPool *g_pThreadPool;

template <typename... Args>
inline void ParallelProcess( const char *, Args &&... )
{
}

// The pristine game-system interface and the shadow manager's own.
#include "igamesystem.h"
#include "iclientshadowmgr.h"
