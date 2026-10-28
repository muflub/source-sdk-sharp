//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// The world the PRISTINE game/client/detailobjectsystem.cpp is compiled into
// by detailprops_oracle_test.cpp. That file is the whole client detail-prop
// system: the detail lump's parse, the per-view fades, the back-to-front
// sprite sorts and the sprite quads. In the game it sits on the engine (the
// game lumps, the models, the BSP tree, the lighting), the client leaf system
// and the material system; here each of those is a stand-in that answers from
// a scripted world and writes down what the subject asked of it.
//
// What is stand-in and what is not:
//
//   * The SUBJECT -- every function of detailobjectsystem.cpp -- is upstream's
//     text, copied unedited from the clone at valve-sdk.lock.
//   * mathlib (AngleVectors, VectorAngles, AngleMatrix, the SSE FourVectors,
//     TexLightToLinear's power2_n), tier0 (the aligned heap, Warning, Assert),
//     tier1 (CUtlBuffer, CUtlMap, CUtlVector, ConVar) and tier1/heapsort.h are
//     the real libraries the test links or includes.
//   * Everything declared below is a stand-in: the renderable interfaces (only
//     the members the subject declares are spelled, so CDetailModel's
//     virtuals override them), the engine, model info and model render, the
//     leaf system, the BSP query, the view, the client mode, the material
//     system and a CMeshBuilder that records every vertex. None of them makes a
//     decision of the subject's; each answers what the scenario scripted.
//
//=============================================================================

#pragma once

#include <stdio.h>
#include <float.h>
#include <string.h>
#include <stdlib.h>
#include <stdint.h>
// Before rand() is renamed below: the subject includes <algorithm> after this
// header, and its std::rand must stay the C library's.
#include <cstdlib>
#include <algorithm>

// mathlib's RandomVector is Vector::Random, which draws the C library's
// rand() and divides by VALVE_RAND_MAX (0x7fff). The subject reaches it for
// cl_detail_multiplier's extra copies; the harness answers from MSVC's
// generator (the range RandomVector was written for) and logs each draw.
int ss_detail_rand();
#define rand() ss_detail_rand()

#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier0/memalloc.h"
#include "tier1/convar.h"
#include "tier1/utlvector.h"
#include "tier1/utlmap.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "mathlib/vector2d.h"
#include "mathlib/ssemath.h"
#include "mathlib/compressed_vector.h"
#include "bsptreedata.h"
#include "datamap.h"

#define DECLARE_CLASS_NOBASE( className ) typedef className ThisClass;

// ispatialpartition.h's, for the subject's EnumElement declaration.
enum IterationRetval_t
{
	ITERATION_CONTINUE = 0,
	ITERATION_STOP
};

#ifndef VPROF_BUDGET
#define VPROF_BUDGET( name, group )
#endif
#ifndef VPROF
#define VPROF( name )
#endif
#ifndef MEM_ALLOC_CREDIT
#define MEM_ALLOC_CREDIT()
#endif

//-----------------------------------------------------------------------------
// The harness's log. Every stand-in the subject reaches writes here.
//-----------------------------------------------------------------------------
void DetailLog( const char *pFmt, ... ) FMTFUNCTION( 1, 2 );

// A float as its IEEE bits, the golden's spelling.
inline unsigned int DetailBits( float f )
{
	unsigned int u;
	memcpy( &u, &f, sizeof( u ) );
	return u;
}

typedef unsigned short LeafIndex_t;
typedef unsigned short ClientShadowHandle_t;
typedef unsigned short ClientRenderHandle_t;
typedef unsigned short ModelInstanceHandle_t;

enum
{
	CLIENTSHADOW_INVALID_HANDLE = 0xffff
};

enum
{
	MODEL_INSTANCE_INVALID = 0xffff
};

enum ShadowType_t
{
	SHADOWS_NONE = 0,
	SHADOWS_SIMPLE
};

#define TEXTURE_GROUP_OTHER "Other textures"

//-----------------------------------------------------------------------------
// The models. Each is the harness's: a name, and the answers model info gives
// for it (the scenario sets them).
//-----------------------------------------------------------------------------
struct model_t
{
	char szName[128];
	int nId;
	int nType; // mod_studio etc. (model_types.h)
	bool bVertexLit;
	bool bTranslucent;
	bool bProxy;
	Vector vecMins, vecMaxs;
};

struct studiohdr_t
{
	int numbones;
};

//-----------------------------------------------------------------------------
// The renderable interfaces, as far as CDetailModel declares them. Each
// member's signature is the SDK's, so the subject's virtuals override these.
//-----------------------------------------------------------------------------
class ICollideable;
class IClientNetworkable;
class IClientEntity;
class IClientThinkable;
class IPVSNotify;
class C_BaseEntity;
class CBaseHandle;
class IClientRenderable;

class IHandleEntity
{
public:
	virtual ~IHandleEntity() {}

	virtual void SetRefEHandle( const CBaseHandle &handle ) = 0;
	virtual const CBaseHandle &GetRefEHandle() const = 0;
};

class IClientUnknown : public IHandleEntity
{
public:
	virtual IClientUnknown *GetIClientUnknown() = 0;
	virtual ICollideable *GetCollideable() = 0;
	virtual IClientNetworkable *GetClientNetworkable() = 0;
	virtual IClientRenderable *GetClientRenderable() = 0;
	virtual IClientEntity *GetIClientEntity() = 0;
	virtual C_BaseEntity *GetBaseEntity() = 0;
	virtual IClientThinkable *GetClientThinkable() = 0;
};

class IClientRenderable
{
public:
	virtual IClientUnknown *GetIClientUnknown() = 0;
	virtual const Vector &GetRenderOrigin() = 0;
	virtual const QAngle &GetRenderAngles() = 0;
	virtual bool ShouldDraw() = 0;
	virtual bool IsTransparent() = 0;
	virtual const model_t *GetModel() const = 0;
	virtual int DrawModel( int flags ) = 0;
	virtual int GetFxBlend() = 0;
	virtual void GetColorModulation( float *color ) = 0;
	virtual bool SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones, int boneMask, float currentTime ) = 0;
	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) = 0;
	virtual void GetRenderBoundsWorldspace( Vector &mins, Vector &maxs ) = 0;
	virtual bool GetAttachment( int number, Vector &origin, QAngle &angles ) = 0;
	virtual const matrix3x4_t &RenderableToWorldTransform() = 0;
};

// clientleafsystem's helper: the model's own bounds about its origin.
void DefaultRenderBoundsWorldspace( IClientRenderable *pRenderable, Vector &absMins, Vector &absMaxs );

//-----------------------------------------------------------------------------
// The engine, model info and model render.
//-----------------------------------------------------------------------------
class CDetailEngine
{
public:
	int GameLumpVersion( int lumpId ) const;
	int GameLumpSize( int lumpId ) const;
	bool LoadGameLump( int lumpId, void *pBuffer, int size );
	const model_t *LoadModel( const char *pName, bool bProp = false );
	void LinearToGamma( float *linear, float *gamma );
	float LightStyleValue( int style );
	void ComputeDynamicLighting( const Vector &pt, const Vector *pNormal, Vector &color );
	ISpatialQuery *GetBSPTreeQuery();
};

extern CDetailEngine *engine;

class CDetailModelInfo
{
public:
	int GetModelType( const model_t *model ) const;
	void GetModelRenderBounds( const model_t *model, Vector &mins, Vector &maxs ) const;
	bool ModelHasMaterialProxy( const model_t *model ) const;
	bool IsTranslucent( model_t const *model ) const;
	void RecomputeTranslucency( const model_t *model, int nSkin, int nBody, void *pClientRenderable,
								float fInstanceAlphaModulate = 1.0f );
	bool IsModelVertexLit( const model_t *model ) const;
	studiohdr_t *GetStudiomodel( const model_t *mod );
};

extern CDetailModelInfo *modelinfo;

class CDetailModelRender
{
public:
	int DrawModel( int flags, IClientRenderable *pRenderable, ModelInstanceHandle_t instance, int entity_index,
				   const model_t *model, Vector const &origin, QAngle const &angles, int skin, int body, int hitboxset,
				   const matrix3x4_t *modelToWorld = NULL, const matrix3x4_t *pLightingOffset = NULL );
};

extern CDetailModelRender *modelrender;

//-----------------------------------------------------------------------------
// The client leaf system.
//-----------------------------------------------------------------------------
#define CLSUBSYSTEM_DETAILOBJECTS 0

class CClientLeafSubSystemData
{
public:
	virtual ~CClientLeafSubSystemData( void ) {}
};

class CDetailLeafSystem
{
public:
	void SetSubSystemDataInLeaf( int leaf, int nSubSystemIdx, CClientLeafSubSystemData *pData );
	CClientLeafSubSystemData *GetSubSystemDataInLeaf( int leaf, int nSubSystemIdx );
	void SetDetailObjectsInLeaf( int leaf, int firstDetailObject, int detailObjectCount );
	void GetDetailObjectsInLeaf( int leaf, int &firstDetailObject, int &detailObjectCount );
	void DrawDetailObjectsInLeaf( int leaf, int frameNumber, int &firstDetailObject, int &detailObjectCount );
};

CDetailLeafSystem *ClientLeafSystem();

//-----------------------------------------------------------------------------
// The view, the client mode, the map's controller and world.
//-----------------------------------------------------------------------------
class CDetailView
{
public:
	int BuildWorldListsNumber() const;
};

extern CDetailView *view;

const Vector &CurrentViewOrigin();
bool ShouldDrawInWireFrameMode();

class CDetailClientMode
{
public:
	bool ShouldDrawDetailObjects();
};

extern CDetailClientMode *g_pClientMode;

class C_EnvDetailController
{
public:
	float m_flFadeStartDist;
	float m_flFadeEndDist;
};

C_EnvDetailController *GetDetailController();

// The local player, whose FOV scales both fades in stock. This build has no
// native local player (its one GetLocalPlayer answers NULL), so both factors
// are 1 -- the fork deleted the two sites for that reason, and the harness
// answers what the game does.
class C_BasePlayer
{
public:
	static C_BasePlayer *GetLocalPlayer() { return NULL; }

	float GetFOVDistanceAdjustFactor() { return 1.0f; }
};

class C_World
{
public:
	const char *GetDetailSpriteMaterial() const;
};

C_World *GetClientWorldEntity();

//-----------------------------------------------------------------------------
// The material system.
//-----------------------------------------------------------------------------
enum MaterialMatrixMode_t
{
	MATERIAL_VIEW = 0,
	MATERIAL_PROJECTION,
	MATERIAL_MODEL = 10
};

enum MaterialPrimitiveType_t
{
	MATERIAL_POINTS = 0,
	MATERIAL_LINES,
	MATERIAL_TRIANGLES,
	MATERIAL_TRIANGLE_STRIP,
	MATERIAL_LINE_STRIP,
	MATERIAL_LINE_LOOP,
	MATERIAL_POLYGON,
	MATERIAL_QUADS
};

enum HDRType_t
{
	HDR_TYPE_NONE = 0,
	HDR_TYPE_INTEGER,
	HDR_TYPE_FLOAT
};

class IMaterial
{
public:
	const char *m_pszName;
	int GetMappingWidth();
	int GetMappingHeight();
};

class CMaterialReference
{
public:
	CMaterialReference()
		: m_pMaterial( NULL )
	{
	}

	void Init( const char *pMaterialName, const char *pTextureGroupName, bool bComplain = true );
	void Shutdown();

	operator IMaterial *() { return m_pMaterial; }

	IMaterial *m_pMaterial;
};

void PrecacheMaterial( const char *pMaterialName );

class IMesh
{
public:
	void Draw( int firstIndex = -1, int numIndices = 0 );
};

class IMatRenderContext
{
public:
	void MatrixMode( MaterialMatrixMode_t mode );
	void PushMatrix();
	void LoadIdentity();
	void PopMatrix();
	IMesh *GetDynamicMesh( bool buffered = true, IMesh *pVertexOverride = 0, IMesh *pIndexOverride = 0,
						   IMaterial *pAutoBind = 0 );
	void GetMaxToRender( IMesh *pMesh, bool bMaxUntilFlush, int *pMaxVerts, int *pMaxIndices );
};

class IMaterialSystem
{
public:
	IMatRenderContext *GetRenderContext();
};

extern IMaterialSystem *materials;

class CMatRenderContextPtr
{
public:
	CMatRenderContextPtr( IMaterialSystem *pFrom )
		: m_pContext( pFrom->GetRenderContext() )
	{
	}

	IMatRenderContext *operator->() const { return m_pContext; }

private:
	IMatRenderContext *m_pContext;
};

class CMeshBuilder
{
public:
	void Begin( IMesh *pMesh, MaterialPrimitiveType_t type, int numPrimitives );
	void End( bool bSpewData = false, bool bDraw = false );
	void Position3f( float x, float y, float z );
	void Position3fv( const float *v );
	void Color4ubv( unsigned char const *rgba );
	void TexCoord2f( int stage, float s, float t );
	void TexCoord2fv( int stage, const float *st );
	void AdvanceVertex();

private:
	float m_Pos[3];
	unsigned char m_Color[4];
	float m_Tex[2];
	int m_nVerts;
};

class CDetailHardwareConfig
{
public:
	HDRType_t GetHDRType() const;
};

extern CDetailHardwareConfig *g_pMaterialSystemHardwareConfig;
