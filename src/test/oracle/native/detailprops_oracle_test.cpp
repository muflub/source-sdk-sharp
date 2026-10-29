//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE DETAIL-PROP ORACLE: stock's client detail props -- the detail lump's
// parse (UnserializeModelDict, UnserializeDetailSprites, ScanForCounts,
// UnserializeModels, UnserializeModelLighting, the fast-sprite build-out), the
// per-view fade and screen alignment (BuildDetailObjectRenderLists,
// EnumerateLeaf), the back-to-front sorts (SortSpritesBackToFront,
// BuildOutSortedSprites) and every sprite quad the four translucent draws
// write -- run offline from the PRISTINE game/client/detailobjectsystem.cpp
// over seeded lumps and a scripted world, and written down as the golden
// src/test/client/Fixtures/detailprops-stock.tsv.
//
// WHY OFFLINE. The maps this repo ships carry no detail props (dm_lockdown's
// lump is its three empty counts), and a live recording sees one camera's
// worth of one map. Here every arm is driven on purpose: the old and new lump
// versions, the fast and the old sprites, the model props, the shape types the
// build does not draw, light styles, the multiplier's random copies, both
// fades, the wireframe and fullbright switches and a mesh small enough to
// split every batch. (The live A/B on a map with detail props is the other
// half; see the eviction commit.)
//
// THE SUBJECT IS INCLUDED, NOT LINKED: it declares its classes in the .cpp, so
// the harness includes the file into this translation unit and reaches it only
// through DetailObjectSystem() and the renderables GetDetailModel answers.
// detailprops_stub/detail_harness.h says what is stock and what is stand-in.
//
// BOTH SIDES ARE PINNED TO THE GOLDEN. This test asserts that stock produces
// exactly it; SourceSharp.Client.Tests' DetailPropsOracleTests asserts the
// managed port does. Regenerating the golden to make this test green deletes
// the check: the golden changes only when the scenario does, and then on
// purpose (touch detailprops_golden.regen beside this file, run, delete the
// marker, and say why in the commit).
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

#include "detail_harness.h"

#include "mathlib_test_env.h"

//-----------------------------------------------------------------------------
// The log.
//-----------------------------------------------------------------------------
static std::string g_Log;

// Any length: a lump's row is its bytes in hex.
void DetailLog( const char *pFmt, ... )
{
	va_list ap;
	va_start( ap, pFmt );
	va_list again;
	va_copy( again, ap );
	const int n = vsnprintf( NULL, 0, pFmt, ap );
	va_end( ap );
	std::string line( n > 0 ? n : 0, '\0' );
	if ( n > 0 )
		vsnprintf( &line[0], n + 1, pFmt, again );
	va_end( again );
	g_Log += line;
	g_Log += '\n';
}

#define B( f ) DetailBits( f )

//-----------------------------------------------------------------------------
// tier0's spew, so Warning() and any Assert the subject trips are lines too.
//-----------------------------------------------------------------------------
static SpewRetval_t DetailSpew( SpewType_t type, const char *pMsg )
{
	std::string text( pMsg ? pMsg : "" );
	for ( char &c : text )
		if ( c == '\n' || c == '\t' )
			c = ' ';
	while ( !text.empty() && text.back() == ' ' )
		text.pop_back();
	DetailLog( "W\t%d\t%s", (int)type, text.c_str() );
	return SPEW_CONTINUE;
}

//-----------------------------------------------------------------------------
// The console: ConVar::SetValue tells g_pCVar's global change callbacks.
//-----------------------------------------------------------------------------
#include "icvar.h"

class CDetailCvar final : public ICvar
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

static CDetailCvar g_DetailCvar;

// The convar the subject declares `extern`: viewrender's, in the game.
ConVar r_DrawDetailProps( "r_DrawDetailProps", "1", FCVAR_NONE, "0=Off, 1=Normal, 2=Wireframe" );

//-----------------------------------------------------------------------------
// THE SUBJECT.
//-----------------------------------------------------------------------------
#include "detailobjectsystem.cpp"

//-----------------------------------------------------------------------------
// The world the stand-ins answer from, set per scenario.
//-----------------------------------------------------------------------------
namespace
{

struct World_t
{
	// The game lumps, by id; a lump with no entry is not in the map.
	std::vector<std::pair<int, std::pair<int, std::vector<unsigned char>>>> lumps;
	bool bHdr = false;
	int nMatW = 256, nMatH = 256;
	int nMaxVerts = 32768, nMaxIndices = 32768;
	bool bWireframe = false;
	bool bClientModeDraws = true;
	const char *pszWorldMaterial = NULL;
	Vector vecCurrentViewOrigin;
	int nBuildWorldListsNumber = 0;
	// The leaves' centres, for the BSP query.
	std::vector<Vector> leafCenters;
	// The leaf system's tables.
	std::vector<int> leafFirst, leafCount, leafFrame;
	std::vector<CClientLeafSubSystemData *> leafData;
	int nObjectsEnd = 0;
	// The models the engine has loaded, in load order.
	std::vector<model_t *> models;
};

World_t g_World;

unsigned int g_nMsvcRand = 1;

} // namespace

int ss_detail_rand()
{
	g_nMsvcRand = g_nMsvcRand * 214013u + 2531011u;
	const int v = (int)( ( g_nMsvcRand >> 16 ) & 0x7fff );
	DetailLog( "R\t%d", v );
	return v;
}

//-----------------------------------------------------------------------------
// The engine.
//-----------------------------------------------------------------------------
static const std::vector<unsigned char> *FindLump( int lumpId, int *pnVersion )
{
	for ( auto &entry : g_World.lumps )
	{
		if ( entry.first == lumpId )
		{
			if ( pnVersion )
				*pnVersion = entry.second.first;
			return &entry.second.second;
		}
	}
	return NULL;
}

int CDetailEngine::GameLumpVersion( int lumpId ) const
{
	int nVersion = 0;
	FindLump( lumpId, &nVersion );
	return nVersion;
}

int CDetailEngine::GameLumpSize( int lumpId ) const
{
	const std::vector<unsigned char> *pLump = FindLump( lumpId, NULL );
	return pLump ? (int)pLump->size() : 0;
}

bool CDetailEngine::LoadGameLump( int lumpId, void *pBuffer, int size )
{
	const std::vector<unsigned char> *pLump = FindLump( lumpId, NULL );
	DetailLog( "GL\t%08x\t%d\t%d", (unsigned)lumpId, size, pLump ? 1 : 0 );
	if ( !pLump || size != (int)pLump->size() )
		return false;
	if ( size )
		memcpy( pBuffer, pLump->data(), size );
	return true;
}

// The model answers are the name's: "vlit" is vertex lit, "glass" translucent,
// "proxy" has a material proxy, "brush" is a brush model.
const model_t *CDetailEngine::LoadModel( const char *pName, bool bProp )
{
	for ( model_t *pModel : g_World.models )
	{
		if ( !strcmp( pModel->szName, pName ) )
		{
			DetailLog( "LM\t%s\t%d\t%d", pName, bProp ? 1 : 0, pModel->nId );
			return pModel;
		}
	}
	model_t *pModel = new model_t;
	V_strncpy( pModel->szName, pName, sizeof( pModel->szName ) );
	pModel->nId = (int)g_World.models.size() + 1;
	pModel->nType = strstr( pName, "brush" ) ? mod_brush : mod_studio;
	pModel->bVertexLit = strstr( pName, "vlit" ) != NULL;
	pModel->bTranslucent = strstr( pName, "glass" ) != NULL;
	pModel->bProxy = strstr( pName, "proxy" ) != NULL;
	pModel->vecMins.Init( -8, -8, 0 );
	pModel->vecMaxs.Init( 8, 8, 24 );
	g_World.models.push_back( pModel );
	DetailLog( "LM\t%s\t%d\t%d", pName, bProp ? 1 : 0, pModel->nId );
	return pModel;
}

// LinearToTexture's shape: a square root, quantised to bytes.
void CDetailEngine::LinearToGamma( float *linear, float *gamma )
{
	for ( int i = 0; i < 3; i++ )
	{
		float x = linear[i];
		if ( x < 0.0f )
			x = 0.0f;
		int n = (int)( sqrtf( x ) * 255.0f );
		if ( n > 255 )
			n = 255;
		gamma[i] = (float)n / 255.0f;
	}
}

float CDetailEngine::LightStyleValue( int style )
{
	return (float)( style % 5 ) * 0.25f;
}

// A dynamic light per 7-unit cell of x, and none on most of the world.
void CDetailEngine::ComputeDynamicLighting( const Vector &pt, const Vector *pNormal, Vector &color )
{
	const int cell = (int)fabsf( pt.x ) % 7;
	color.x = (float)cell * 0.03125f;
	color.y = (float)( (int)fabsf( pt.y ) % 3 ) * 0.0625f;
	color.z = cell == 0 ? 0.0f : 0.015625f;
}

namespace
{
// The BSP tree: every leaf whose centre is within the sphere plus 212 units,
// in leaf order.
class CDetailQuery : public ISpatialQuery
{
public:
	virtual int LeafCount() const { return (int)g_World.leafCenters.size(); }

	virtual bool EnumerateLeavesAtPoint( Vector const &, ISpatialLeafEnumerator *, intp ) { return true; }

	virtual bool EnumerateLeavesInBox( Vector const &, Vector const &, ISpatialLeafEnumerator *, intp ) { return true; }

	virtual bool EnumerateLeavesInSphere( Vector const &center, float radius, ISpatialLeafEnumerator *pEnum,
										  intp context )
	{
		std::string leaves;
		for ( int i = 0; i < (int)g_World.leafCenters.size(); i++ )
		{
			if ( ( g_World.leafCenters[i] - center ).Length() > radius + 212.0f )
				continue;
			leaves += "\t" + std::to_string( i );
		}
		DetailLog( "Q\t%08x\t%08x\t%08x\t%08x%s", B( center.x ), B( center.y ), B( center.z ), B( radius ),
				   leaves.c_str() );
		for ( int i = 0; i < (int)g_World.leafCenters.size(); i++ )
		{
			if ( ( g_World.leafCenters[i] - center ).Length() > radius + 212.0f )
				continue;
			if ( !pEnum->EnumerateLeaf( i, context ) )
				return false;
		}
		return true;
	}

	virtual bool EnumerateLeavesAlongRay( Ray_t const &, ISpatialLeafEnumerator *, intp ) { return true; }
};

CDetailQuery g_Query;
} // namespace

ISpatialQuery *CDetailEngine::GetBSPTreeQuery()
{
	return &g_Query;
}

static CDetailEngine g_Engine;
CDetailEngine *engine = &g_Engine;

int CDetailModelInfo::GetModelType( const model_t *model ) const
{
	return model ? model->nType : mod_bad;
}

void CDetailModelInfo::GetModelRenderBounds( const model_t *model, Vector &mins, Vector &maxs ) const
{
	mins = model->vecMins;
	maxs = model->vecMaxs;
}

bool CDetailModelInfo::ModelHasMaterialProxy( const model_t *model ) const
{
	return model && model->bProxy;
}

bool CDetailModelInfo::IsTranslucent( model_t const *model ) const
{
	return model && model->bTranslucent;
}

void CDetailModelInfo::RecomputeTranslucency( const model_t *model, int nSkin, int nBody, void *pClientRenderable,
											  float )
{
	DetailLog( "RT\t%d\t%d\t%d\t%d", model ? model->nId : 0, nSkin, nBody, pClientRenderable ? 1 : 0 );
}

bool CDetailModelInfo::IsModelVertexLit( const model_t *model ) const
{
	const bool b = model && model->bVertexLit;
	DetailLog( "VL\t%d\t%d", model ? model->nId : 0, b ? 1 : 0 );
	return b;
}

static studiohdr_t g_StudioHdr = { 3 };

studiohdr_t *CDetailModelInfo::GetStudiomodel( const model_t * )
{
	return &g_StudioHdr;
}

static CDetailModelInfo g_ModelInfo;
CDetailModelInfo *modelinfo = &g_ModelInfo;

int CDetailModelRender::DrawModel( int flags, IClientRenderable *pRenderable, ModelInstanceHandle_t instance,
								   int entity_index, const model_t *model, Vector const &origin, QAngle const &angles,
								   int skin, int body, int hitboxset, const matrix3x4_t *, const matrix3x4_t * )
{
	DetailLog( "DM\t%d\t%d\t%d\t%d\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%d\t%d\t%d", flags, (int)instance, entity_index,
			   model ? model->nId : 0, B( origin.x ), B( origin.y ), B( origin.z ), B( angles.x ), B( angles.y ),
			   B( angles.z ), skin, body, hitboxset );
	return 1;
}

static CDetailModelRender g_ModelRender;
CDetailModelRender *modelrender = &g_ModelRender;

void DefaultRenderBoundsWorldspace( IClientRenderable *pRenderable, Vector &absMins, Vector &absMaxs )
{
	Vector mins, maxs;
	pRenderable->GetRenderBounds( mins, maxs );
	absMins = pRenderable->GetRenderOrigin() + mins;
	absMaxs = pRenderable->GetRenderOrigin() + maxs;
}

//-----------------------------------------------------------------------------
// The leaf system.
//-----------------------------------------------------------------------------
namespace
{
class CDetailLeafData : public CClientLeafSubSystemData
{
};
}

void CDetailLeafSystem::SetSubSystemDataInLeaf( int leaf, int nSubSystemIdx, CClientLeafSubSystemData *pData )
{
	DetailLog( "LS\t%d\t%d\t%d", leaf, nSubSystemIdx, pData ? 1 : 0 );
	if ( g_World.leafData[leaf] )
		delete g_World.leafData[leaf];
	g_World.leafData[leaf] = pData;
}

CClientLeafSubSystemData *CDetailLeafSystem::GetSubSystemDataInLeaf( int leaf, int nSubSystemIdx )
{
	return g_World.leafData[leaf];
}

void CDetailLeafSystem::SetDetailObjectsInLeaf( int leaf, int firstDetailObject, int detailObjectCount )
{
	DetailLog( "LD\t%d\t%d\t%d", leaf, firstDetailObject, detailObjectCount );
	g_World.leafFirst[leaf] = firstDetailObject;
	g_World.leafCount[leaf] = detailObjectCount;
	g_World.nObjectsEnd = MAX( g_World.nObjectsEnd, firstDetailObject + detailObjectCount );
}

void CDetailLeafSystem::GetDetailObjectsInLeaf( int leaf, int &firstDetailObject, int &detailObjectCount )
{
	firstDetailObject = g_World.leafFirst[leaf];
	detailObjectCount = g_World.leafCount[leaf];
}

void CDetailLeafSystem::DrawDetailObjectsInLeaf( int leaf, int frameNumber, int &firstDetailObject,
												 int &detailObjectCount )
{
	DetailLog( "LK\t%d\t%d", leaf, frameNumber );
	g_World.leafFrame[leaf] = frameNumber;
	firstDetailObject = g_World.leafFirst[leaf];
	detailObjectCount = g_World.leafCount[leaf];
}

static CDetailLeafSystem g_LeafSystem;

CDetailLeafSystem *ClientLeafSystem()
{
	return &g_LeafSystem;
}

//-----------------------------------------------------------------------------
// The view, the client mode, the map's controller and world.
//-----------------------------------------------------------------------------
int CDetailView::BuildWorldListsNumber() const
{
	return g_World.nBuildWorldListsNumber;
}

static CDetailView g_View;
CDetailView *view = &g_View;

const Vector &CurrentViewOrigin()
{
	return g_World.vecCurrentViewOrigin;
}

bool ShouldDrawInWireFrameMode()
{
	return g_World.bWireframe;
}

bool CDetailClientMode::ShouldDrawDetailObjects()
{
	return g_World.bClientModeDraws;
}

static CDetailClientMode g_ClientMode;
CDetailClientMode *g_pClientMode = &g_ClientMode;

// NULL, as this build's GetDetailController answered (shell_client_orphans.cpp,
// until C7 cluster 10): the managed env_detail_controller clamps the convars
// itself.
C_EnvDetailController *GetDetailController()
{
	return NULL;
}

const char *C_World::GetDetailSpriteMaterial() const
{
	return g_World.pszWorldMaterial;
}

static C_World g_ClientWorld;

C_World *GetClientWorldEntity()
{
	return &g_ClientWorld;
}

//-----------------------------------------------------------------------------
// The material system.
//-----------------------------------------------------------------------------
int IMaterial::GetMappingWidth()
{
	return g_World.nMatW;
}

int IMaterial::GetMappingHeight()
{
	return g_World.nMatH;
}

namespace
{
std::vector<IMaterial *> g_Materials;
}

void CMaterialReference::Init( const char *pMaterialName, const char *pTextureGroupName, bool )
{
	DetailLog( "MI\t%s\t%s", pMaterialName, pTextureGroupName );
	for ( IMaterial *pMat : g_Materials )
	{
		if ( !strcmp( pMat->m_pszName, pMaterialName ) )
		{
			m_pMaterial = pMat;
			return;
		}
	}
	IMaterial *pMat = new IMaterial;
	pMat->m_pszName = strdup( pMaterialName );
	g_Materials.push_back( pMat );
	m_pMaterial = pMat;
}

void CMaterialReference::Shutdown()
{
	DetailLog( "MS\t%s", m_pMaterial ? m_pMaterial->m_pszName : "-" );
	m_pMaterial = NULL;
}

void PrecacheMaterial( const char *pMaterialName )
{
	DetailLog( "PM\t%s", pMaterialName );
}

void IMesh::Draw( int, int )
{
	DetailLog( "D" );
}

static IMesh g_Mesh;

void IMatRenderContext::MatrixMode( MaterialMatrixMode_t mode )
{
	DetailLog( "XM\t%d", (int)mode );
}

void IMatRenderContext::PushMatrix()
{
	DetailLog( "XP" );
}

void IMatRenderContext::LoadIdentity()
{
	DetailLog( "XI" );
}

void IMatRenderContext::PopMatrix()
{
	DetailLog( "XO" );
}

IMesh *IMatRenderContext::GetDynamicMesh( bool buffered, IMesh *, IMesh *, IMaterial *pAutoBind )
{
	DetailLog( "XG\t%d\t%s", buffered ? 1 : 0, pAutoBind ? pAutoBind->m_pszName : "-" );
	return &g_Mesh;
}

void IMatRenderContext::GetMaxToRender( IMesh *, bool, int *pMaxVerts, int *pMaxIndices )
{
	*pMaxVerts = g_World.nMaxVerts;
	*pMaxIndices = g_World.nMaxIndices;
}

static IMatRenderContext g_RenderContext;

IMatRenderContext *IMaterialSystem::GetRenderContext()
{
	return &g_RenderContext;
}

static IMaterialSystem g_MaterialSystem;
IMaterialSystem *materials = &g_MaterialSystem;

void CMeshBuilder::Begin( IMesh *, MaterialPrimitiveType_t type, int numPrimitives )
{
	DetailLog( "MB\t%d\t%d", (int)type, numPrimitives );
	m_nVerts = 0;
}

void CMeshBuilder::End( bool, bool )
{
	DetailLog( "ME\t%d", m_nVerts );
}

void CMeshBuilder::Position3f( float x, float y, float z )
{
	m_Pos[0] = x;
	m_Pos[1] = y;
	m_Pos[2] = z;
}

void CMeshBuilder::Position3fv( const float *v )
{
	Position3f( v[0], v[1], v[2] );
}

void CMeshBuilder::Color4ubv( unsigned char const *rgba )
{
	memcpy( m_Color, rgba, 4 );
}

void CMeshBuilder::TexCoord2f( int, float s, float t )
{
	m_Tex[0] = s;
	m_Tex[1] = t;
}

void CMeshBuilder::TexCoord2fv( int stage, const float *st )
{
	TexCoord2f( stage, st[0], st[1] );
}

void CMeshBuilder::AdvanceVertex()
{
	DetailLog( "V\t%08x\t%08x\t%08x\t%02x%02x%02x%02x\t%08x\t%08x", B( m_Pos[0] ), B( m_Pos[1] ), B( m_Pos[2] ),
			   m_Color[0], m_Color[1], m_Color[2], m_Color[3], B( m_Tex[0] ), B( m_Tex[1] ) );
	++m_nVerts;
}

HDRType_t CDetailHardwareConfig::GetHDRType() const
{
	return g_World.bHdr ? HDR_TYPE_INTEGER : HDR_TYPE_NONE;
}

static CDetailHardwareConfig g_HardwareConfig;
CDetailHardwareConfig *g_pMaterialSystemHardwareConfig = &g_HardwareConfig;

//-----------------------------------------------------------------------------
// THE SCENARIOS.
//-----------------------------------------------------------------------------
namespace
{

// xorshift32: the scenario's inputs, never the subject's draws.
struct Rng_t
{
	unsigned int s;

	unsigned int Next()
	{
		s ^= s << 13;
		s ^= s >> 17;
		s ^= s << 5;
		return s;
	}

	int Int( int lo, int hi ) { return lo + (int)( Next() % (unsigned)( hi - lo + 1 ) ); }

	float Float( float lo, float hi ) { return lo + ( hi - lo ) * (float)( Next() & 0xffffff ) / (float)0xffffff; }
};

struct Scenario_t
{
	const char *pszName;
	unsigned int nSeed;
	int nVersion; // the detail lump's; 0: the map has no detail lump
	bool bHdr;
	int nLighting; // light style entries in each lighting lump (-1: neither lump)
	int nMatW, nMatH;
	int nMaxVerts, nMaxIndices;
	float flDist, flFade; // cl_detaildist / cl_detailfade at Init
	int nFast; // cl_fastdetailsprites
	int nMultiplier; // cl_detail_multiplier
	int nLeaves, nObjects, nDict, nSprites;
	const char *pszWorldMaterial;
	int nViews;
	float flUserDist; // cl_detaildist the player sets after the level loads (0: none)
};

const Scenario_t kScenarios[] = {
	// name          seed        ver hdr  lit  matW matH  verts  idx    dist   fade  fast mul lv  obj  dct spr  world material              views user
	{ "basic", 0x5eed0001, 4, true, 6, 512, 256, 32768, 32768, 1200, 400, 1, 1, 12, 260, 4, 6, NULL, 6, 0 },
	{ "ldr-square",
	  0x5eed0002,
	  4,
	  false,
	  4,
	  256,
	  256,
	  32768,
	  32768,
	  800,
	  900,
	  1,
	  1,
	  10,
	  180,
	  3,
	  5,
	  "detail/detailsprites_map",
	  5,
	  0 },
	{ "tiny-mesh", 0x5eed0003, 4, true, 3, 512, 128, 23, 40, 1500, 300, 1, 1, 4, 220, 2, 4, NULL, 5, 0 },
	{ "slow-only", 0x5eed0004, 4, true, 5, 128, 512, 32768, 32768, 1200, 400, 0, 1, 9, 200, 2, 6, NULL, 5, 0 },
	{ "multiplier", 0x5eed0005, 4, true, 2, 256, 256, 32768, 32768, 1200, 400, 1, 3, 6, 90, 2, 3, NULL, 4, 0 },
	{ "user-dist", 0x5eed0006, 4, true, 2, 256, 256, 32768, 32768, 1200, 400, 1, 1, 8, 160, 2, 4, NULL, 4, 500 },
	{ "close-fade", 0x5eed0007, 4, false, 0, 256, 256, 32768, 32768, 300, 50, 1, 1, 6, 150, 2, 4, NULL, 4, 0 },
	{ "old-format", 0x5eed0008, 3, true, 2, 256, 256, 32768, 32768, 1200, 400, 1, 1, 4, 40, 1, 2, NULL, 1, 0 },
	{ "no-lump", 0x5eed0009, 0, true, -1, 256, 256, 32768, 32768, 1200, 400, 1, 1, 4, 0, 0, 0, NULL, 1, 0 },
	{ "empty", 0x5eed000a, 4, true, 0, 256, 256, 32768, 32768, 1200, 400, 1, 1, 4, 0, 0, 0, NULL, 1, 0 },
	{ "dict-only-v5", 0x5eed000b, 5, true, 2, 256, 256, 32768, 32768, 1200, 400, 1, 1, 4, 30, 2, 2, NULL, 1, 0 },
};

void PutInt( std::vector<unsigned char> &out, int v )
{
	unsigned char b[4];
	memcpy( b, &v, 4 );
	out.insert( out.end(), b, b + 4 );
}

void PutFloat( std::vector<unsigned char> &out, float v )
{
	unsigned char b[4];
	memcpy( b, &v, 4 );
	out.insert( out.end(), b, b + 4 );
}

std::string Hex( const std::vector<unsigned char> &bytes )
{
	static const char kDigits[] = "0123456789abcdef";
	std::string s;
	s.reserve( bytes.size() * 2 );
	for ( unsigned char c : bytes )
	{
		s += kDigits[c >> 4];
		s += kDigits[c & 15];
	}
	return s;
}

Vector LeafCenter( int i )
{
	return Vector( (float)( i % 4 ) * 500.0f, (float)( i / 4 ) * 500.0f, (float)( i % 3 ) * 16.0f );
}

// The detail lump: the model dictionary, the sprite dictionary, the objects.
std::vector<unsigned char> BuildDetailLump( const Scenario_t &sc, Rng_t &rng, int nLightingEntries )
{
	std::vector<unsigned char> out;

	PutInt( out, sc.nDict );
	for ( int i = 0; i < sc.nDict; i++ )
	{
		char name[DETAIL_NAME_LENGTH] = {};
		static const char *kKinds[] = { "grass", "vlit_fern", "glass_shard", "proxy_reed", "brush_rock" };
		V_snprintf( name, sizeof( name ), "models/detail/%s%02d.mdl", kKinds[i % 5], i );
		out.insert( out.end(), name, name + DETAIL_NAME_LENGTH );
	}

	PutInt( out, sc.nSprites );
	for ( int i = 0; i < sc.nSprites; i++ )
	{
		const float halfW = rng.Float( 4.0f, 28.0f );
		PutFloat( out, -halfW ); // m_UL.x
		PutFloat( out, rng.Float( 8.0f, 44.0f ) ); // m_UL.y
		PutFloat( out, halfW + rng.Float( -2.0f, 2.0f ) ); // m_LR.x
		PutFloat( out, rng.Float( -4.0f, 4.0f ) ); // m_LR.y
		const float u0 = rng.Float( 0.0f, 0.75f ), v0 = rng.Float( 0.0f, 0.75f );
		PutFloat( out, u0 ); // m_TexUL
		PutFloat( out, v0 );
		PutFloat( out, u0 + rng.Float( 0.05f, 0.25f ) ); // m_TexLR
		PutFloat( out, v0 + rng.Float( 0.05f, 0.25f ) );
	}

	// The objects, sorted by leaf as vbsp writes them, then one leaf revisited.
	std::vector<int> leafOf;
	for ( int i = 0; i < sc.nObjects; i++ )
		leafOf.push_back( ( i * sc.nLeaves ) / MAX( sc.nObjects, 1 ) );
	if ( sc.nObjects > 8 && sc.nLeaves > 3 )
	{
		for ( int i = sc.nObjects - 4; i < sc.nObjects; i++ )
			leafOf[i] = 2;
	}

	PutInt( out, sc.nObjects );
	static const float kScales[] = { 0.333333f, 1e-6f, 70000.0f, 0.0f, 1.0f, 65519.0f };
	for ( int i = 0; i < sc.nObjects; i++ )
	{
		const int leaf = leafOf[i];
		const Vector c = LeafCenter( leaf );
		DetailObjectLump_t lump;
		memset( &lump, 0, sizeof( lump ) );
		lump.m_Origin.Init( c.x + rng.Float( -140, 140 ), c.y + rng.Float( -140, 140 ), c.z + rng.Float( -30, 30 ) );
		lump.m_Angles.Init( rng.Float( -20, 20 ), rng.Float( 0, 360 ), rng.Float( -10, 10 ) );
		lump.m_Leaf = (unsigned short)leaf;
		lump.m_Lighting.r = (byte)rng.Int( 0, 255 );
		lump.m_Lighting.g = (byte)rng.Int( 0, 255 );
		lump.m_Lighting.b = (byte)rng.Int( 0, 255 );
		lump.m_Lighting.exponent = (signed char)rng.Int( -6, 1 );
		lump.m_flScale = rng.Float( 0.3f, 2.5f );
		if ( rng.Int( 0, 19 ) == 0 )
			lump.m_flScale = kScales[rng.Int( 0, 5 )];

		const int roll = rng.Int( 0, 99 );
		lump.m_Type = DETAIL_PROP_TYPE_SPRITE;
		if ( roll < 40 )
		{
			lump.m_Orientation = 2; // the fast sprites
		}
		else if ( roll < 55 )
		{
			lump.m_Orientation = 0;
		}
		else if ( roll < 62 )
		{
			lump.m_Orientation = 1;
		}
		else if ( roll < 66 )
		{
			lump.m_Orientation = 2;
			lump.m_SwayAmount = (unsigned char)rng.Int( 1, 255 );
		}
		else if ( roll < 70 && nLightingEntries > 0 )
		{
			lump.m_Orientation = (unsigned char)rng.Int( 0, 2 );
			lump.m_LightStyleCount = (unsigned char)rng.Int( 1, MIN( 3, nLightingEntries ) );
			lump.m_LightStyles = (unsigned int)rng.Int( 0, nLightingEntries - lump.m_LightStyleCount );
		}
		else if ( roll < 80 && sc.nDict > 0 )
		{
			lump.m_Type = DETAIL_PROP_TYPE_MODEL;
			lump.m_Orientation = (unsigned char)rng.Int( 0, 3 );
			lump.m_ShapeAngle = (unsigned char)rng.Int( 0, 255 );
			if ( nLightingEntries > 0 && rng.Int( 0, 3 ) == 0 )
			{
				lump.m_LightStyleCount = 1;
				lump.m_LightStyles = (unsigned int)rng.Int( 0, nLightingEntries - 1 );
			}
		}
		else if ( roll < 84 )
		{
			lump.m_Type = DETAIL_PROP_TYPE_SHAPE_CROSS;
			lump.m_Orientation = (unsigned char)rng.Int( 0, 2 );
			lump.m_ShapeAngle = (unsigned char)rng.Int( 0, 90 );
			lump.m_ShapeSize = (unsigned char)rng.Int( 0, 255 );
		}
		else if ( roll < 87 )
		{
			lump.m_Type = DETAIL_PROP_TYPE_SHAPE_TRI;
			lump.m_Orientation = (unsigned char)rng.Int( 0, 2 );
			lump.m_ShapeSize = (unsigned char)rng.Int( 0, 255 );
		}
		else if ( roll < 89 )
		{
			lump.m_Type = (unsigned char)rng.Int( 4, 7 ); // not a type: skipped
			lump.m_Orientation = 2;
		}
		else if ( roll < 92 )
		{
			lump.m_Orientation = 6; // the bitfield keeps 2
		}
		else if ( roll < 95 )
		{
			lump.m_Orientation = 3; // no screen alignment at all
		}
		else
		{
			lump.m_Orientation = 2;
			lump.m_ShapeAngle = (unsigned char)rng.Int( 1, 255 ); // not fast either
		}

		if ( lump.m_Type == DETAIL_PROP_TYPE_MODEL )
			lump.m_DetailModel = (unsigned short)rng.Int( 0, sc.nDict - 1 );
		else
			lump.m_DetailModel = (unsigned short)rng.Int( 0, MAX( sc.nSprites - 1, 0 ) );

		const unsigned char *p = (const unsigned char *)&lump;
		out.insert( out.end(), p, p + sizeof( lump ) );
	}

	return out;
}

std::vector<unsigned char> BuildLightingLump( int nEntries, Rng_t &rng )
{
	std::vector<unsigned char> out;
	PutInt( out, nEntries );
	for ( int i = 0; i < nEntries; i++ )
	{
		DetailPropLightstylesLump_t entry;
		entry.m_Lighting.r = (byte)rng.Int( 0, 255 );
		entry.m_Lighting.g = (byte)rng.Int( 0, 255 );
		entry.m_Lighting.b = (byte)rng.Int( 0, 255 );
		entry.m_Lighting.exponent = (signed char)rng.Int( -4, 0 );
		entry.m_Style = (unsigned char)rng.Int( 0, 11 );
		const unsigned char *p = (const unsigned char *)&entry;
		out.insert( out.end(), p, p + sizeof( entry ) );
	}
	return out;
}

void Vec( const char *pszTag, const Vector &v )
{
	DetailLog( "%s\t%08x\t%08x\t%08x", pszTag, B( v.x ), B( v.y ), B( v.z ) );
}

// Every renderable GetDetailModel answers, as the leaf system and the
// renderer would read it. Its colour modulation is what the renderer reads
// when it gathers the view's lists, after the view's list build, so it is
// asked only where a build ran (bColor).
void DumpModels( IDetailObjectSystem *pSystem, bool bColor )
{
	for ( int i = 0; i < g_World.nObjectsEnd; i++ )
	{
		IClientRenderable *pRenderable = pSystem->GetDetailModel( i );
		if ( !pRenderable )
			continue;
		const Vector &o = pRenderable->GetRenderOrigin();
		const QAngle &a = pRenderable->GetRenderAngles();
		DetailLog( "M\t%d\t%d\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%d\t%d", i,
				   pRenderable->GetModel() ? pRenderable->GetModel()->nId : 0, B( o.x ), B( o.y ), B( o.z ), B( a.x ),
				   B( a.y ), B( a.z ), pRenderable->GetFxBlend(), pRenderable->IsTransparent() ? 1 : 0 );
		if ( bColor )
		{
			float color[3];
			pRenderable->GetColorModulation( color );
			DetailLog( "MC\t%d\t%08x\t%08x\t%08x", i, B( color[0] ), B( color[1] ), B( color[2] ) );
		}
		pRenderable->DrawModel( 1 );
	}
}

void ViewVectors( const QAngle &angles, Vector &fwd, Vector &right, Vector &up )
{
	AngleVectors( angles, &fwd, &right, &up );
}

void RunOne( const Scenario_t &sc )
{
	Rng_t rng = { sc.nSeed };
	g_World = World_t();
	g_World.bHdr = sc.bHdr;
	g_World.nMatW = sc.nMatW;
	g_World.nMatH = sc.nMatH;
	g_World.nMaxVerts = sc.nMaxVerts;
	g_World.nMaxIndices = sc.nMaxIndices;
	g_World.pszWorldMaterial = sc.pszWorldMaterial;
	const int nLeafSlots = 64;
	for ( int i = 0; i < nLeafSlots; i++ )
		g_World.leafCenters.push_back( LeafCenter( i ) );
	g_World.leafFirst.assign( nLeafSlots, 0 );
	g_World.leafCount.assign( nLeafSlots, 0 );
	g_World.leafFrame.assign( nLeafSlots, -1 );
	g_World.leafData.assign( nLeafSlots, NULL );
	g_nMsvcRand = sc.nSeed & 0xffff;

	const int nLighting = MAX( sc.nLighting, 0 );
	std::vector<unsigned char> detail = BuildDetailLump( sc, rng, nLighting );
	std::vector<unsigned char> ldr = BuildLightingLump( nLighting, rng );
	std::vector<unsigned char> hdr = BuildLightingLump( nLighting, rng );
	if ( sc.nVersion > 0 )
		g_World.lumps.push_back( { GAMELUMP_DETAIL_PROPS, { sc.nVersion, detail } } );
	if ( sc.nLighting >= 0 )
	{
		g_World.lumps.push_back( { GAMELUMP_DETAIL_PROP_LIGHTING, { GAMELUMP_DETAIL_PROP_LIGHTING_VERSION, ldr } } );
		g_World.lumps.push_back(
			{ GAMELUMP_DETAIL_PROP_LIGHTING_HDR, { GAMELUMP_DETAIL_PROP_LIGHTING_HDR_VERSION, hdr } } );
	}

	DetailLog( "S\t%s\t%u\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%08x\t%08x\t%d\t%d\t%s", sc.pszName, sc.nSeed, sc.nVersion,
			   sc.bHdr ? 1 : 0, sc.nLighting, sc.nMatW, sc.nMatH, sc.nMaxVerts, sc.nMaxIndices, B( sc.flDist ),
			   B( sc.flFade ), sc.nFast, sc.nMultiplier, sc.pszWorldMaterial ? sc.pszWorldMaterial : "-" );
	for ( auto &entry : g_World.lumps )
		DetailLog( "G\t%08x\t%d\t%s", (unsigned)entry.first, entry.second.first, Hex( entry.second.second ).c_str() );

	IDetailObjectSystem *pSystem = DetailObjectSystem();

	// What the player's settings were when the client started (stock's Init
	// reads them once), then the map's own.
	cl_detaildist.SetValue( sc.flDist );
	cl_detailfade.SetValue( sc.flFade );
	cl_fastdetailsprites.SetValue( sc.nFast );
	cl_detail_multiplier.SetValue( sc.nMultiplier );
	pSystem->Init();
	DetailLog( "C\tinit" );

	pSystem->LevelInitPreEntity();
	DetailLog( "C\tpost" );
	pSystem->LevelInitPostEntity();
	DetailLog( "CV\t%08x\t%08x", B( cl_detailfade.GetFloat() ), B( cl_detaildist.GetFloat() ) );
	DumpModels( pSystem, false );

	if ( sc.flUserDist > 0 )
	{
		cl_detaildist.SetValue( sc.flUserDist );
		DetailLog( "U\t%08x", B( sc.flUserDist ) );
	}

	for ( int v = 0; v < sc.nViews; v++ )
	{
		// The camera: over a leaf, or between two, looking anywhere.
		const int leaf = rng.Int( 0, MAX( sc.nLeaves - 1, 0 ) );
		const Vector c = LeafCenter( leaf );
		Vector origin( c.x + rng.Float( -250, 250 ), c.y + rng.Float( -250, 250 ), c.z + rng.Float( 10, 80 ) );
		if ( v == 3 )
			origin += Vector( 700, 0, 0 ); // one view far off, past most fades
		QAngle angles( rng.Float( -30, 30 ), rng.Float( 0, 360 ), 0 );
		Vector fwd, right, up;
		ViewVectors( angles, fwd, right, up );
		g_World.vecCurrentViewOrigin = origin;
		if ( v == 2 )
			g_World.vecCurrentViewOrigin = origin + Vector( 40, -30, 5 ); // screen alignment's own view

		// The switches this view runs under.
		r_DrawDetailProps.SetValue( v == 4 ? 2 : ( v == 5 ? 0 : 1 ) );
		g_World.bWireframe = ( v == 1 && sc.nViews > 2 );
		mat_fullbright.SetValue( v == 2 ? 1 : 0 );
		g_World.bClientModeDraws = !( v == 4 && sc.nViews > 5 );
		g_World.nBuildWorldListsNumber = 100 + v;

		DetailLog( "C\tview\t%d\t%d\t%d\t%d\t%d\t%d", v, r_DrawDetailProps.GetInt(), g_World.bWireframe ? 1 : 0,
				   mat_fullbright.GetInt(), g_World.bClientModeDraws ? 1 : 0, g_World.nBuildWorldListsNumber );
		Vec( "VO", origin );
		Vec( "VC", g_World.vecCurrentViewOrigin );
		Vec( "VF", fwd );
		Vec( "VR", right );
		Vec( "VU", up );

		pSystem->BuildDetailObjectRenderLists( origin );
		// The build's own early outs: the renderer gathers no detail model's
		// colour in a view that built none.
		const bool bBuilt = g_World.bClientModeDraws && r_DrawDetailProps.GetInt() != 0;
		DumpModels( pSystem, bBuilt );

		// The leaves the view draws: those the build marked, as the plan's
		// scene lists them, and one it did not.
		std::vector<LeafIndex_t> marked;
		for ( int i = 0; i < nLeafSlots; i++ )
		{
			if ( g_World.leafFrame[i] == g_World.nBuildWorldListsNumber )
				marked.push_back( (LeafIndex_t)i );
		}
		// Back to front, as the translucent pass visits them.
		std::sort( marked.begin(), marked.end(),
				   [&]( LeafIndex_t a, LeafIndex_t b )
				   {
					   return ( g_World.leafCenters[a] - origin ).LengthSqr() >
						   ( g_World.leafCenters[b] - origin ).LengthSqr();
				   } );
		if ( v == 0 )
			marked.push_back( (LeafIndex_t)( nLeafSlots - 1 ) );
		// A view whose build marked nothing (detail off, or the client mode
		// says no) still draws the leaves it is handed: the draws do not ask.
		if ( marked.empty() && sc.nLeaves >= 4 )
		{
			for ( int i = 3; i >= 0; i-- )
				marked.push_back( (LeafIndex_t)i );
		}

		std::string leafText;
		for ( LeafIndex_t l : marked )
			leafText += "\t" + std::to_string( l );
		DetailLog( "C\tbegin%s", leafText.c_str() );
		pSystem->BeginTranslucentDetailRendering();

		// The first half as one list, the next leaf with entities in it, then the rest.
		const int nHalf = (int)marked.size() / 2;
		std::vector<LeafIndex_t> first( marked.begin(), marked.begin() + nHalf );
		std::string firstText;
		for ( LeafIndex_t l : first )
			firstText += "\t" + std::to_string( l );
		DetailLog( "C\tlist%s", firstText.c_str() );
		pSystem->RenderTranslucentDetailObjects( origin, fwd, right, up, (int)first.size(), first.data() );

		if ( nHalf < (int)marked.size() )
		{
			const int nLeaf = marked[nHalf];
			// Entities in the leaf, back to front: each draw is the detail
			// farther than it, then the entity.
			for ( int e = 0; e < 3; e++ )
			{
				const float d = 400.0f - 150.0f * (float)e + rng.Float( -40, 40 );
				Vector closest = origin + fwd * d;
				DetailLog( "C\tinleaf\t%d\t%08x\t%08x\t%08x", nLeaf, B( closest.x ), B( closest.y ), B( closest.z ) );
				pSystem->RenderTranslucentDetailObjectsInLeaf( origin, fwd, right, up, nLeaf, &closest );
			}
			DetailLog( "C\tinleaf\t%d\t-", nLeaf );
			pSystem->RenderTranslucentDetailObjectsInLeaf( origin, fwd, right, up, nLeaf, NULL );
			DetailLog( "C\tinleaf\t%d\t-", nLeaf );
			pSystem->RenderTranslucentDetailObjectsInLeaf( origin, fwd, right, up, nLeaf, NULL );
		}

		std::vector<LeafIndex_t> rest;
		for ( int i = nHalf + 1; i < (int)marked.size(); i++ )
			rest.push_back( marked[i] );
		std::string restText;
		for ( LeafIndex_t l : rest )
			restText += "\t" + std::to_string( l );
		DetailLog( "C\tlist%s", restText.c_str() );
		pSystem->RenderTranslucentDetailObjects( origin, fwd, right, up, (int)rest.size(), rest.data() );

		DumpModels( pSystem, bBuilt );
	}

	DetailLog( "C\tshutdown" );
	pSystem->LevelShutdownPreEntity();
	pSystem->LevelShutdownPostEntity();
	for ( CClientLeafSubSystemData *&pData : g_World.leafData )
	{
		delete pData;
		pData = NULL;
	}
	DetailLog( "C\tend" );
}

// The CPU's rsqrtps on a few inputs: the fast sprites' VectorNormalizeFast
// rests on it, and two CPUs' estimates differ in the last bits. The managed
// replay compares those vertices exactly when its own estimate matches these.
void LogReciprocalSqrtEstimates()
{
	static const float kInputs[] = { 1.0f, 2.0f, 3.0f, 10.0f, 12345.678f, 0.0625f, 7.5e6f, 0.3f };
	for ( float f : kInputs )
	{
		fltx4 in = ReplicateX4( f );
		fltx4 out = ReciprocalSqrtEstSIMD( in );
		DetailLog( "RSQ\t%08x\t%08x", B( f ), B( SubFloat( out, 0 ) ) );
	}
}

std::string RunScenario()
{
	g_Log.clear();
	g_pCVar = &g_DetailCvar;
	SpewOutputFunc( DetailSpew );
	DetailLog( "# detailprops-stock: stock's detailobjectsystem.cpp over seeded lumps (README-detailprops-stock.md)" );
	LogReciprocalSqrtEstimates();
	for ( const Scenario_t &sc : kScenarios )
		RunOne( sc );
	SpewOutputFunc( NULL );
	return g_Log;
}

std::string RepoPath( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

std::string GoldenPath()
{
	return RepoPath( "src/test/client/Fixtures/detailprops-stock.tsv" );
}

std::string MarkerPath()
{
	return RepoPath( "src/test/oracle/native/detailprops_golden.regen" );
}

bool WriteText( const std::string &path, const std::string &text )
{
	std::ofstream out( path.c_str(), std::ios::binary );
	out << text;
	return out.good();
}

bool ReadText( const std::string &path, std::string *pOut )
{
	std::ifstream in( path.c_str(), std::ios::binary );
	if ( !in.good() )
		return false;
	std::stringstream ss;
	ss << in.rdbuf();
	*pOut = ss.str();
	return true;
}

} // namespace

TEST( DetailPropsOracle, StockProducesTheGolden )
{
	SS_SKIP_ON_CI(
		"a golden recorded bit for bit on the owner's machine; another CPU's rsqrtps differs in the last bit" );
	const std::string run = RunScenario();

	if ( std::ifstream( MarkerPath() ).good() )
		ASSERT_TRUE( WriteText( GoldenPath(), run ) ) << GoldenPath() << " could not be written";

	std::string golden;
	ASSERT_TRUE( ReadText( GoldenPath(), &golden ) ) << GoldenPath() << " is missing";
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

TEST( DetailPropsOracle, TheScenarioReachesEveryArm )
{
	const std::string run = RunScenario();

	struct Arm_t
	{
		const char *pszNeedle;
		const char *pszWhat;
	};

	static const Arm_t kArms[] = {
		{ "\nLS\t", "a leaf got fast sprites" },
		{ "\nLD\t", "a leaf got its detail objects" },
		{ "\nQ\t", "a view enumerated the BSP tree" },
		{ "\nRT\t", "a proxied detail model recomputed its translucency" },
		{ "\nVL\t1\t", "a model was asked whether it is vertex lit" },
		{ "\tmodels/error.mdl\t", "a vertex-lit model was replaced by the error model" },
		{ "\nPM\tdetail/detailsprites", "the sprite material was precached" },
		{ "\nMI\tdetail/detailsprites_map\t", "the map's own sprite material was used" },
		{ "\nXG\t1\tdebug/debugspritewireframe", "a batch drew wireframe" },
		{ "\nMB\t7\t5", "a batch was split by the tiny mesh" },
		{ "\nR\t", "the multiplier drew random copies" },
		{ "\nDM\t", "a model prop drew" },
		{ "\nV\t", "a sprite vertex was written" },
		{ "\nMC\t", "a model prop's colour modulation was read" },
		{ "old detail prop file format", "the old format was refused" },
	};
	for ( size_t i = 0; i < sizeof( kArms ) / sizeof( kArms[0] ); i++ )
		EXPECT_NE( run.find( kArms[i].pszNeedle ), std::string::npos ) << kArms[i].pszWhat << ": no";
}
