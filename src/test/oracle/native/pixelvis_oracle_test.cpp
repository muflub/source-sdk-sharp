//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE PIXEL VISIBILITY ORACLE (C7, c_pixel_visibility.cpp): stock's
// CPixelVisibilitySystem -- the per-caller query sets and their serial
// handles, a query per set per view, the fade over the pixel counts (the
// partial and the plain arms, a failed or unready query, the 0.95 snap), the
// proxies drawn at each view's end (the counting pass and the visible pass,
// the screen-space and minimum-pixel sizes, a proxy behind the eye), unused
// sets and queries freed at the scene's end, the level's start and end, the
// sight-line fallback and its trace filter, the views shifted, and the
// ConVars -- run offline from the PRISTINE game/client/c_pixel_visibility.cpp
// over a scripted world, and written down as the golden
// src/test/client/Fixtures/pixelvis-stock.tsv.
//
// THE SUBJECT IS INCLUDED, NOT LINKED: its system, its ConVars and its filter
// class are file statics. pixelvis_stub/pixelvis_harness.h says what is stock
// and what is stand-in; the material system, its render context, the dynamic
// mesh and the materials are vtable doubles below, every slot a trap but the
// ones the subject calls.
//
// THE GOLDEN CARRIES ITS STIMULUS: each step is an `S` row (a frame, a view, a
// ConVar, a caller's question, a view's end, the scene's end, a level change),
// followed by every engine call the subject made and every answer it gave, so
// the managed replay (PixelVisibilityOracleTests) drives the same steps and
// holds every row. Regenerating the golden to make this test green deletes the
// check: it changes only when the script does (touch pixelvis_golden.regen
// beside this file, run, delete the marker, and say why in the commit).
//
//=============================================================================

#include <stdarg.h>
#include <cmath>
#include <fstream>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

#include "gtest/gtest.h"
#include "../../native/ss_ci_skip.h"

#include "pixelvis_harness.h"
#include "vtable_hook.h"
#include "view_frustum_transform.h"

#include "mathlib_test_env.h"

//-----------------------------------------------------------------------------
// The log.
//-----------------------------------------------------------------------------
static std::string g_Log;

static void Append( const char *pFmt, va_list ap )
{
	char szLine[ 4096 ];
	vsnprintf( szLine, sizeof( szLine ), pFmt, ap );
	g_Log += szLine;
	g_Log += '\n';
}

void PixelVisLog( const char *pFmt, ... )
{
	va_list ap;
	va_start( ap, pFmt );
	Append( pFmt, ap );
	va_end( ap );
}

// A console line as one row, its newline dropped.
static void ConsoleRow( const char *pszKind, const char *pFmt, va_list ap )
{
	char sz[ 2048 ];
	vsnprintf( sz, sizeof( sz ), pFmt, ap );
	size_t n = strlen( sz );
	if ( n && sz[ n - 1 ] == '\n' )
		sz[ n - 1 ] = 0;
	PixelVisLog( "%s\t%s", pszKind, sz );
}

void PixelVisDevMsg( int level, const char *pFmt, ... )
{
	va_list ap;
	va_start( ap, pFmt );
	char szKind[ 16 ];
	snprintf( szKind, sizeof( szKind ), "D%d", level );
	ConsoleRow( szKind, pFmt, ap );
	va_end( ap );
}

void PixelVisMsg( const char *pFmt, ... )
{
	va_list ap;
	va_start( ap, pFmt );
	ConsoleRow( "M", pFmt, ap );
	va_end( ap );
}

static unsigned int Bits( float f )
{
	unsigned int u;
	memcpy( &u, &f, sizeof( u ) );
	return u;
}

#define V3( v ) Bits( ( v ).x ), Bits( ( v ).y ), Bits( ( v ).z )
#define V3FMT "%08x\t%08x\t%08x"

//-----------------------------------------------------------------------------
// The console: ConVar::SetValue tells g_pCVar's global change callbacks.
//-----------------------------------------------------------------------------
#include "icvar.h"

class CPixelVisCvar final : public ICvar
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
static CPixelVisCvar g_PixelVisCvar;

//-----------------------------------------------------------------------------
// The globals, the engine and the view.
//-----------------------------------------------------------------------------
static PixelVisGlobals_t s_Globals;
PixelVisGlobals_t *gpGlobals = &s_Globals;

static int s_nDXLevel = 95;
int IPixelVisEngine::GetDXSupportLevel() { return s_nDXLevel; }
static IPixelVisEngine s_Engine;
IPixelVisEngine *engine = &s_Engine;

// glow_overlay.cpp's, which the subject declares extern.
ConVar building_cubemaps( "building_cubemaps", "0" );

struct View_t
{
	int id;
	Vector origin, forward, right, up;
	float m[ 16 ];	// world to screen, row-major
};
static View_t s_View;

int CurrentViewID() { return s_View.id; }
const Vector &CurrentViewOrigin() { return s_View.origin; }
const Vector &CurrentViewForward() { return s_View.forward; }
const Vector &CurrentViewRight() { return s_View.right; }
const Vector &CurrentViewUp() { return s_View.up; }

// view_scene.cpp's ScreenTransform through the view's world-to-screen matrix
// (SourceSharp's ViewFrustumTransform, held to FrustumTransform by
// view_frustum_transform_test).
int ScreenTransform( const Vector &point, Vector &screen )
{
	return ViewFrustumTransform( s_View.m, &point.x, &screen.x );
}

//-----------------------------------------------------------------------------
// The client effect precache registrations, logged when the script starts.
//-----------------------------------------------------------------------------
static std::vector< std::string > &Precaches()
{
	static std::vector< std::string > s_Rows;
	return s_Rows;
}

PixelVisPrecache_t::PixelVisPrecache_t( const char *pszName, const char *const *ppszMaterials, int nMaterials )
{
	std::string row = std::string( "P\t" ) + pszName;
	for ( int i = 0; i < nMaterials; i++ )
		row += std::string( "\t" ) + ppszMaterials[ i ];
	Precaches().push_back( row );
}

//-----------------------------------------------------------------------------
// The entities a trace asks the filter about, and the traces.
//-----------------------------------------------------------------------------
class CPixelVisEntity : public IClientUnknown, public ICollideable
{
public:
	CPixelVisEntity( const char *pszName, SolidType_t solid ) : m_pszName( pszName ), m_Solid( solid ) {}
	virtual void SetRefEHandle( const CBaseHandle & ) {}
	virtual const CBaseHandle &GetRefEHandle() const { return m_Handle; }
	virtual ICollideable *GetCollideable() { return this; }
	virtual SolidType_t GetSolid() const { return m_Solid; }

	const char *m_pszName;
	SolidType_t m_Solid;
	CBaseHandle m_Handle;
};

static CPixelVisEntity s_Prop( "prop", SOLID_VPHYSICS );
static CPixelVisEntity s_Brush( "brush", SOLID_BSP );
static CPixelVisEntity s_Player( "player", SOLID_BBOX );
static CPixelVisEntity s_Trigger( "trigger", SOLID_NONE );
static CPixelVisEntity *s_TraceEntities[] = { &s_Prop, &s_Brush, &s_Player, &s_Trigger };

static const char *EntityName( IHandleEntity *p )
{
	for ( CPixelVisEntity *e : s_TraceEntities )
	{
		if ( static_cast< IHandleEntity * >( e ) == p )
			return e->m_pszName;
	}
	return "?";
}

CTraceFilterSimple::CTraceFilterSimple( const IHandleEntity *passentity, int collisionGroup )
	: m_pPassEnt( passentity ), m_collisionGroup( collisionGroup )
{
}

bool CTraceFilterSimple::ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask )
{
	PixelVisLog( "FB\t%s\t%d\t%d", EntityName( pHandleEntity ), m_pPassEnt ? 1 : 0, m_collisionGroup );
	return true;
}

// The scenario's world: which entities lie on the line, and the world's own fraction.
static int s_nTraceEntities = 0;	// how many of s_TraceEntities the line crosses
static float s_flTraceWorld = 1.0f;

void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask, ITraceFilter *pFilter,
                     trace_t *ptr )
{
	PixelVisLog( "T\t" V3FMT "\t" V3FMT "\t%08x", V3( vecAbsStart ), V3( vecAbsEnd ), mask );
	memset( (void *)ptr, 0, sizeof( *ptr ) );
	ptr->startpos = vecAbsStart;
	ptr->fraction = s_flTraceWorld;
	for ( int i = 0; i < s_nTraceEntities; i++ )
	{
		IHandleEntity *pEnt = static_cast< IClientUnknown * >( s_TraceEntities[ i ] );
		const bool bHit = pFilter->ShouldHitEntity( pEnt, mask );
		PixelVisLog( "F\t%s\t%d\t%d", s_TraceEntities[ i ]->m_pszName, (int)s_TraceEntities[ i ]->m_Solid, bHit ? 1 : 0 );
		if ( bHit && ptr->fraction > 0.25f )
			ptr->fraction = 0.25f;
	}
	ptr->endpos = vecAbsStart + ( vecAbsEnd - vecAbsStart ) * ptr->fraction;
	PixelVisLog( "TR\t%08x", Bits( ptr->fraction ) );
}

//-----------------------------------------------------------------------------
// The material system doubles.
//-----------------------------------------------------------------------------
namespace
{
const int kSlots = 512;

// Each slot's own trap, so a call the double does not answer names its slot.
template < int N >
void TrapAt()
{
	ADD_FAILURE() << "slot " << N << ", which the double does not answer, was called";
}

template < int... N >
void FillTraps( void **pTable, std::integer_sequence< int, N... > )
{
	void *traps[] = { (void *)&TrapAt< N >... };
	for ( int i = 0; i < (int)sizeof...( N ); i++ )
		pTable[ i ] = traps[ i ];
}

struct Fake_t
{
	void **pVtbl;
	void *pTable[ kSlots ];
	Fake_t() : pVtbl( pTable )
	{
		FillTraps( pTable, std::make_integer_sequence< int, kSlots >() );
	}
	template < typename Fn, typename Impl >
	void Set( Fn pmf, Impl impl )
	{
		const VTableHook::Member m = VTableHook::MemberOf( pmf );
		ASSERT_EQ( m.adjust, 0 );
		ASSERT_GE( m.slot, 0 );
		ASSERT_LT( m.slot, kSlots );
		pTable[ m.slot ] = (void *)impl;
	}
};

struct FakeMaterial_t : Fake_t
{
	char szName[ 128 ];
	int nId;
};

Fake_t g_System, g_Context, g_Mesh;
FakeMaterial_t g_Materials[ 8 ];
int g_nMaterials = 0;

// The query objects: a handle is its id (1, 2, ...), never dereferenced.
int g_nNextQuery = 1;
bool g_bQueryCreateFails = false;

const char *MaterialNameOf( const void *p )
{
	return p ? static_cast< const FakeMaterial_t * >( p )->szName : "(null)";
}

int QueryId( OcclusionQueryObjectHandle_t h ) { return (int)(intptr_t)h; }

void IncRef( void *self ) { PixelVisLog( "MI\t%s", MaterialNameOf( self ) ); }
void DecRef( void *self ) { PixelVisLog( "MD\t%s", MaterialNameOf( self ) ); }
const char *GetName( void *self ) { return MaterialNameOf( self ); }

IMaterial *FindMaterial( void *, const char *pName, const char *pGroup, bool bComplain, const char * )
{
	PixelVisLog( "MF\t%s\t%s\t%d", pName, pGroup, bComplain ? 1 : 0 );
	for ( int i = 0; i < g_nMaterials; i++ )
	{
		if ( !strcmp( g_Materials[ i ].szName, pName ) )
			return (IMaterial *)&g_Materials[ i ];
	}
	FakeMaterial_t &m = g_Materials[ g_nMaterials ];
	m.nId = g_nMaterials++;
	V_strncpy( m.szName, pName, sizeof( m.szName ) );
	m.Set( &IMaterial::IncrementReferenceCount, &IncRef );
	m.Set( &IMaterial::DecrementReferenceCount, &DecRef );
	m.Set( &IMaterial::GetName, &GetName );
	return (IMaterial *)&m;
}

IMatRenderContext *GetRenderContext( void * ) { return (IMatRenderContext *)&g_Context; }
int Release( void * ) { return 0; }
void BeginRender( void * ) {}
void EndRender( void * ) {}

OcclusionQueryObjectHandle_t CreateQuery( void * )
{
	if ( g_bQueryCreateFails )
	{
		PixelVisLog( "QC\t0" );
		return INVALID_OCCLUSION_QUERY_OBJECT_HANDLE;
	}
	const int id = g_nNextQuery++;
	PixelVisLog( "QC\t%d", id );
	return (OcclusionQueryObjectHandle_t)(intptr_t)id;
}

void DestroyQuery( void *, OcclusionQueryObjectHandle_t h ) { PixelVisLog( "QD\t%d", QueryId( h ) ); }
void ResetQuery( void *, OcclusionQueryObjectHandle_t h ) { PixelVisLog( "QR\t%d", QueryId( h ) ); }

// The pixel counts: a fixed rule over the query's id and the frame, so every
// arm comes round. Odd ids are a set's visible query, even its counting one.
int PixelsRendered( void *, OcclusionQueryObjectHandle_t h )
{
	const int id = QueryId( h );
	const int k = id & 1 ? ( id * 31 + gpGlobals->framecount * 17 ) % 7 : ( id * 13 + gpGlobals->framecount * 5 ) % 6;
	static const int kVisible[ 7 ] = { -1, 0, 10, 32, 61, 64, 64 };
	static const int kCount[ 6 ] = { -1, 0, 64, 64, 64, 64 };
	const int n = id & 1 ? kVisible[ k ] : kCount[ k ];
	PixelVisLog( "QP\t%d\t%d", id, n );
	return n;
}

// The diameter of a sphere at its distance from the eye.
float PixelDiameter( void *, const Vector &origin, float flRadius )
{
	const float dist = MAX( ( origin - s_View.origin ).Length(), 1.0f );
	const float d = flRadius * 400.0f / dist;
	PixelVisLog( "PD\t" V3FMT "\t%08x\t%08x", V3( origin ), Bits( flRadius ), Bits( d ) );
	return d;
}

void BeginQuery( void *, OcclusionQueryObjectHandle_t h ) { PixelVisLog( "BQ\t%d", QueryId( h ) ); }
void EndQuery( void *, OcclusionQueryObjectHandle_t h ) { PixelVisLog( "EQ\t%d", QueryId( h ) ); }
void Bind( void *, IMaterial *pMaterial, void * ) { PixelVisLog( "B\t%s", MaterialNameOf( pMaterial ) ); }
void DepthRange( void *, float flNear, float flFar ) { PixelVisLog( "DR\t%08x\t%08x", Bits( flNear ), Bits( flFar ) ); }

IMesh *GetDynamicMesh( void *, bool bBuffered, IMesh *pVertexOverride, IMesh *pIndexOverride, IMaterial *pAutoBind )
{
	PixelVisLog( "GM\t%d\t%d\t%d\t%s", bBuffered ? 1 : 0, pVertexOverride ? 1 : 0, pIndexOverride ? 1 : 0, MaterialNameOf( pAutoBind ) );
	return (IMesh *)&g_Mesh;
}

// The mesh: one interleaved vertex array, as beamsegdraw_oracle_test's.
struct Vertex_t
{
	float pos[3];
	unsigned char color[4];
	float tc0[2];
	float tc1[2];
	float tangentS[3];
	float tangentT[3];
	float normal[3];
	unsigned char specular[4];
};
Vertex_t g_Verts[ 64 ];
unsigned short g_Indices[ 256 ];
float g_Dummy[ 64 ];
int g_nPrimitive = -1;
int g_nUnlocked = 0;

void SetPrimitiveType( void *, MaterialPrimitiveType_t type ) { g_nPrimitive = type; }
void LockMesh( void *, int nVerts, int nIndices, MeshDesc_t &desc )
{
	EXPECT_LE( nVerts, 64 );
	memset( &desc, 0, sizeof( desc ) );
	memset( g_Verts, 0, sizeof( g_Verts ) );
	const int stride = sizeof( Vertex_t );
	desc.m_VertexSize_Position = stride;
	desc.m_VertexSize_Normal = stride;
	desc.m_VertexSize_Color = stride;
	desc.m_VertexSize_Specular = stride;
	desc.m_VertexSize_TexCoord[0] = stride;
	desc.m_VertexSize_TexCoord[1] = stride;
	desc.m_VertexSize_TangentS = stride;
	desc.m_VertexSize_TangentT = stride;
	desc.m_ActualVertexSize = stride;
	desc.m_CompressionType = VERTEX_COMPRESSION_NONE;
	desc.m_pPosition = g_Verts[0].pos;
	desc.m_pNormal = g_Verts[0].normal;
	desc.m_pColor = g_Verts[0].color;
	desc.m_pSpecular = g_Verts[0].specular;
	desc.m_pTexCoord[0] = g_Verts[0].tc0;
	desc.m_pTexCoord[1] = g_Verts[0].tc1;
	for ( int i = 2; i < VERTEX_MAX_TEXTURE_COORDINATES; i++ )
		desc.m_pTexCoord[i] = g_Dummy;
	desc.m_pTangentS = g_Verts[0].tangentS;
	desc.m_pTangentT = g_Verts[0].tangentT;
	desc.m_pBoneWeight = g_Dummy;
	desc.m_pBoneMatrixIndex = (unsigned char *)g_Dummy;
	desc.m_pWrinkle = g_Dummy;
	desc.m_pUserData = g_Dummy;
	desc.m_pIndices = g_Indices;
	desc.m_nIndexSize = 1;
}
void UnlockMesh( void *, int nVerts, int, MeshDesc_t & ) { g_nUnlocked = nVerts; }
void ValidateData( void *, int, int, const MeshDesc_t & ) {}
int VertexCount( const void * ) { return g_nUnlocked; }
VertexFormat_t GetVertexFormat( const void * ) { return 0; }
void DrawMesh( void *, int, int )
{
	std::string row = "MESH\t" + std::to_string( g_nPrimitive ) + "\t" + std::to_string( g_nUnlocked );
	char sz[ 64 ];
	for ( int i = 0; i < g_nUnlocked && i < 64; i++ )
	{
		snprintf( sz, sizeof( sz ), "\t%08x,%08x,%08x", Bits( g_Verts[ i ].pos[0] ), Bits( g_Verts[ i ].pos[1] ), Bits( g_Verts[ i ].pos[2] ) );
		row += sz;
	}
	PixelVisLog( "%s", row.c_str() );
}

void BuildDoubles()
{
	g_System.Set( &IMaterialSystem::FindMaterial, &FindMaterial );
	g_System.Set( &IMaterialSystem::GetRenderContext, &GetRenderContext );
	g_Context.Set( &IMatRenderContext::Release, &Release );
	g_Context.Set( &IMatRenderContext::BeginRender, &BeginRender );
	g_Context.Set( &IMatRenderContext::EndRender, &EndRender );
	g_Context.Set( &IMatRenderContext::CreateOcclusionQueryObject, &CreateQuery );
	g_Context.Set( &IMatRenderContext::DestroyOcclusionQueryObject, &DestroyQuery );
	g_Context.Set( &IMatRenderContext::ResetOcclusionQueryObject, &ResetQuery );
	g_Context.Set( &IMatRenderContext::OcclusionQuery_GetNumPixelsRendered, &PixelsRendered );
	g_Context.Set( &IMatRenderContext::ComputePixelDiameterOfSphere, &PixelDiameter );
	g_Context.Set( &IMatRenderContext::BeginOcclusionQueryDrawing, &BeginQuery );
	g_Context.Set( &IMatRenderContext::EndOcclusionQueryDrawing, &EndQuery );
	g_Context.Set( &IMatRenderContext::Bind, &Bind );
	g_Context.Set( &IMatRenderContext::DepthRange, &DepthRange );
	g_Context.Set( &IMatRenderContext::GetDynamicMesh, &GetDynamicMesh );
	g_Mesh.Set( &IMesh::SetPrimitiveType, &SetPrimitiveType );
	g_Mesh.Set( &IMesh::LockMesh, &LockMesh );
	g_Mesh.Set( &IMesh::UnlockMesh, &UnlockMesh );
	g_Mesh.Set( &IMesh::VertexCount, &VertexCount );
	g_Mesh.Set( &IMesh::GetVertexFormat, &GetVertexFormat );
	g_Mesh.Set( static_cast< void ( IMesh::* )( int, int, const MeshDesc_t & ) >( &IMesh::ValidateData ), &ValidateData );
	g_Mesh.Set( static_cast< void ( IMesh::* )( int, int ) >( &IMesh::Draw ), &DrawMesh );
}
} // namespace

IMaterialSystem *materials = (IMaterialSystem *)&g_System;

//-----------------------------------------------------------------------------
// The subject.
//-----------------------------------------------------------------------------
#include "c_pixel_visibility.cpp"

//-----------------------------------------------------------------------------
// The script.
//-----------------------------------------------------------------------------
namespace
{

// A view looking along yaw (degrees, about z) from origin, its world-to-screen
// a 90-degree perspective with a 16:9 aspect: row 0 is right / tan, row 1 up /
// (tan / aspect), row 3 the depth, each with the eye's translation.
void SetView( int id, const Vector &origin, float yaw )
{
	s_View.id = id;
	s_View.origin = origin;
	const float r = DEG2RAD( yaw );
	s_View.forward.Init( cosf( r ), sinf( r ), 0.0f );
	s_View.right.Init( sinf( r ), -cosf( r ), 0.0f );
	s_View.up.Init( 0.0f, 0.0f, 1.0f );
	const float t = 1.0f;
	const float a = 16.0f / 9.0f;
	float *m = s_View.m;
	memset( m, 0, sizeof( s_View.m ) );
	m[0] = s_View.right.x / t; m[1] = s_View.right.y / t; m[2] = s_View.right.z / t;
	m[3] = -DotProduct( s_View.right, origin ) / t;
	m[4] = s_View.up.x * a / t; m[5] = s_View.up.y * a / t; m[6] = s_View.up.z * a / t;
	m[7] = -DotProduct( s_View.up, origin ) * a / t;
	m[12] = s_View.forward.x; m[13] = s_View.forward.y; m[14] = s_View.forward.z;
	m[15] = -DotProduct( s_View.forward, origin );
	std::string row = "S\tview\t" + std::to_string( id );
	char sz[ 32 ];
	const Vector *vecs[] = { &s_View.origin, &s_View.forward, &s_View.right, &s_View.up };
	for ( const Vector *v : vecs )
	{
		snprintf( sz, sizeof( sz ), "\t" V3FMT, V3( *v ) );
		row += sz;
	}
	for ( int i = 0; i < 16; i++ )
	{
		snprintf( sz, sizeof( sz ), "\t%08x", Bits( m[ i ] ) );
		row += sz;
	}
	PixelVisLog( "%s", row.c_str() );
}

void Frame( int n, float frametime )
{
	gpGlobals->framecount = n;
	gpGlobals->frametime = frametime;
	gpGlobals->curtime += frametime;
	PixelVisLog( "S\tframe\t%d\t%08x", n, Bits( frametime ) );
}

void SetCvar( ConVar &var, const char *pszValue )
{
	PixelVisLog( "S\tcvar\t%s\t%s", var.GetName(), pszValue );
	var.SetValue( pszValue );
}

// A caller: its query handle, kept between frames as an owner keeps it.
struct Caller_t
{
	const char *pszName;
	pixelvis_handle_t hQuery;
};

Caller_t s_Callers[] = {
	{ "ahead", 0 },
	{ "screen", 0 },
	{ "behind", 0 },
	{ "far", 0 },
	{ "brief", 0 },
	{ "borrower", 0 },
	{ "edge", 0 },
};

Caller_t &CallerNamed( const char *psz )
{
	for ( Caller_t &c : s_Callers )
	{
		if ( !strcmp( c.pszName, psz ) )
			return c;
	}
	ADD_FAILURE() << "no caller " << psz;
	return s_Callers[ 0 ];
}

// PixelVisibility_FractionVisible with the caller's handle (fadeTime and the
// screen-space flag are params' own fields).
void Ask( const char *pszCaller, const Vector &pos, float size, float aspect, float fade, bool bScreen )
{
	Caller_t &c = CallerNamed( pszCaller );
	PixelVisLog( "S\tfv\t%s\t" V3FMT "\t%08x\t%08x\t%08x\t%d\t%d", c.pszName, V3( pos ), Bits( size ), Bits( aspect ),
	             Bits( fade ), bScreen ? 1 : 0, c.hQuery );
	pixelvis_queryparams_t params;
	params.Init( pos, size, aspect, fade );
	params.bSizeInScreenspace = bScreen;
	const float f = PixelVisibility_FractionVisible( params, &c.hQuery );
	PixelVisLog( "R\t%08x\t%d", Bits( f ), c.hQuery );
}

// PixelVisibility_FractionVisible with no handle: the sight line, 1 or 0.
void AskNoHandle( const Vector &pos )
{
	PixelVisLog( "S\tfvnull\t" V3FMT, V3( pos ) );
	pixelvis_queryparams_t params;
	params.Init( pos );
	const float f = PixelVisibility_FractionVisible( params, NULL );
	PixelVisLog( "R\t%08x", Bits( f ) );
}

void EndView()
{
	PixelVisLog( "S\tendview" );
	PixelVisibility_EndCurrentView();
}

void EndScene()
{
	PixelVisLog( "S\tendscene" );
	PixelVisibility_EndScene();
}

void LevelInit( int dx, bool bQueryFails )
{
	PixelVisLog( "S\tinit\t%d\t%d", dx, bQueryFails ? 1 : 0 );
	s_nDXLevel = dx;
	g_bQueryCreateFails = bQueryFails;
	g_PixelVisibilitySystem.LevelInitPreEntity();
	g_bQueryCreateFails = false;
}

void LevelShutdown()
{
	PixelVisLog( "S\tshutdown" );
	g_PixelVisibilitySystem.LevelShutdownPostEntity();
}

void Available()
{
	PixelVisLog( "S\tavail" );
	PixelVisLog( "R\t%d", PixelVisibility_IsAvailable() ? 1 : 0 );
}

void Sight( const Vector &pos, bool bTrace )
{
	PixelVisLog( "S\tsight\t" V3FMT "\t%d", V3( pos ), bTrace ? 1 : 0 );
	PixelVisLog( "R\t%08x", Bits( GlowSightDistance( pos, bTrace ) ) );
}

void Shift( int src, int dst )
{
	PixelVisLog( "S\tshift\t%d\t%d", src, dst );
	PixelVisibility_ShiftVisibilityViews( src, dst );
}

void World( int nEntities, float flWorld )
{
	PixelVisLog( "S\tworld\t%d\t%08x", nEntities, Bits( flWorld ) );
	s_nTraceEntities = nEntities;
	s_flTraceWorld = flWorld;
}

void Debug()
{
	PixelVisLog( "S\tdebug" );
	pixelvis_debug( CCommand() );
}

// The positions: the eye stands at the origin looking along +x.
const Vector kAhead( 200, 10, 5 );
const Vector kScreen( 400, -40, 30 );
const Vector kBehind( -300, 0, 0 );
const Vector kFar( 5000, 300, 100 );
const Vector kBrief( 150, -20, -10 );
const Vector kEdge( 120, 150, 0 );
const Vector kEye( 0, 0, 0 );

// The callers of one view in one frame.
void AskAll( int frame )
{
	Ask( "ahead", kAhead, 2.0f, 1.0f, 0.0625f, false );
	Ask( "screen", kScreen, 3.0f, 2.0f, 0.2f, true );
	Ask( "behind", kBehind, 2.0f, 1.0f, 0.0f, false );
	Ask( "far", kFar, 1.0f, 1.0f, 0.0625f, false );
	if ( frame <= 3 || frame >= 9 )
		Ask( "brief", kBrief, 2.0f, 1.0f, 0.0625f, false );
	Ask( "edge", kEdge, 8.0f, 0.5f, 0.0625f, false );
}

std::string RunScript()
{
	static std::string s_Run;
	static bool s_bRan = false;
	if ( s_bRan )
		return s_Run;
	s_bRan = true;

	g_pCVar = &g_PixelVisCvar;
	BuildDoubles();
	g_Log.clear();
	for ( const std::string &row : Precaches() )
		PixelVisLog( "%s", row.c_str() );

	//-------------------------------------------------------------------------
	// A level with queries: twelve frames, two views each.
	//-------------------------------------------------------------------------
	LevelInit( 95, false );
	Available();
	World( 0, 1.0f );
	Sight( kAhead, false );
	Sight( kAhead, true );
	Sight( Vector( 2, 1, 0 ), true );	// within four units: the line is not pulled back

	// No set yet: the view's end has nothing to draw.
	Frame( 1, 1.0f / 60.0f );
	SetView( 0, kEye, 0.0f );
	EndView();

	for ( int frame = 2; frame <= 14; frame++ )
	{
		Frame( frame, frame == 6 ? 0.05f : 1.0f / 60.0f );
		if ( frame == 7 )
			SetCvar( r_pixelvisibility_partial, "0" );
		if ( frame == 10 )
			SetCvar( r_pixelvisibility_partial, "1" );
		if ( frame == 8 )
			SetCvar( r_drawpixelvisibility, "1" );
		if ( frame == 9 )
			SetCvar( r_drawpixelvisibility, "0" );
		if ( frame == 11 )
			SetCvar( r_pixelvisibility_spew, "1" );
		if ( frame == 12 )
			SetCvar( r_pixelvisibility_spew, "0" );
		if ( frame == 13 )
			SetCvar( building_cubemaps, "1" );
		if ( frame == 14 )
			SetCvar( building_cubemaps, "0" );

		// The main view.
		SetView( 0, kEye, 0.0f );
		AskAll( frame );
		if ( frame == 5 )
		{
			// Another caller handed a live set's handle asks with it, and its
			// own question lands on that set.
			CallerNamed( "borrower" ).hQuery = CallerNamed( "ahead" ).hQuery;
			Ask( "borrower", kAhead + Vector( 0, 0, 1 ), 2.0f, 1.0f, 0.0625f, false );
		}
		if ( frame == 11 )
		{
			// A handle whose set was freed and handed on: its serial no longer
			// matches, so the caller gets a new set.
			CallerNamed( "borrower" ).hQuery = 0x00050001;
			Ask( "borrower", kAhead, 2.0f, 1.0f, 0.0625f, false );
		}
		World( frame % 2 ? 4 : 1, frame % 3 ? 1.0f : 0.5f );
		AskNoHandle( kAhead );
		EndView();

		// A second view: the 3D skybox's, turned away.
		SetView( 1, Vector( 10, 0, 0 ), frame > 8 ? 90.0f : 10.0f );
		Ask( "ahead", kAhead, 2.0f, 1.0f, 0.0625f, false );
		if ( frame % 2 )
			Ask( "screen", kScreen, 3.0f, 2.0f, 0.2f, true );
		EndView();

		// A reflection view asked once, so a shift has a destination to replace.
		if ( frame == 11 )
		{
			SetView( 3, kEye, 0.0f );
			Ask( "ahead", kAhead, 2.0f, 1.0f, 0.0625f, false );
			EndView();
		}

		// No view: nothing is visible.
		if ( frame == 4 )
		{
			s_View.id = -1;
			PixelVisLog( "S\tnoview" );
			Ask( "ahead", kAhead, 2.0f, 1.0f, 0.0625f, false );
			EndView();
		}

		// Pixel visibility switched off: a real view's end draws nothing.
		if ( frame == 12 )
		{
			SetCvar( r_dopixelvisibility, "0" );
			Available();
			SetView( 0, kEye, 0.0f );
			EndView();
			SetCvar( r_dopixelvisibility, "1" );
			Available();
		}

		if ( frame == 12 )
			Shift( 0, 3 );
		if ( frame == 13 )
			Shift( 1, 5 );
		EndScene();
	}

	Debug();
	LevelShutdown();

	//-------------------------------------------------------------------------
	// A level on a DX7 card: no queries, every question a sight line.
	//-------------------------------------------------------------------------
	LevelInit( 70, false );
	Available();
	for ( int frame = 15; frame <= 17; frame++ )
	{
		Frame( frame, 1.0f / 60.0f );
		SetView( 0, kEye, 0.0f );
		World( frame - 14, frame == 16 ? 0.5f : 1.0f );
		Ask( "ahead", kAhead, 2.0f, 1.0f, 0.0625f, false );
		EndView();
		EndScene();
	}
	LevelShutdown();

	//-------------------------------------------------------------------------
	// A level whose query cannot be made, and one with the switch off.
	//-------------------------------------------------------------------------
	LevelInit( 95, true );
	Available();
	LevelShutdown();
	SetCvar( r_dopixelvisibility, "0" );
	LevelInit( 95, false );
	Available();
	LevelShutdown();
	SetCvar( r_dopixelvisibility, "1" );

	//-------------------------------------------------------------------------
	// A level again: the lists are new, and an old handle names nothing.
	//-------------------------------------------------------------------------
	LevelInit( 95, false );
	Frame( 18, 1.0f / 60.0f );
	SetView( 0, kEye, 0.0f );
	Ask( "ahead", kAhead, 2.0f, 1.0f, 0.0625f, false );
	EndView();
	EndScene();
	Frame( 19, 1.0f / 60.0f );
	SetView( 0, kEye, 0.0f );
	Ask( "ahead", kAhead, 2.0f, 1.0f, 0.0625f, false );
	EndView();
	EndScene();
	Debug();
	LevelShutdown();

	s_Run = g_Log;
	return s_Run;
}

std::string RepoFile( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

std::string GoldenPath() { return RepoFile( "src/test/client/Fixtures/pixelvis-stock.tsv" ); }
std::string MarkerPath() { return RepoFile( "src/test/oracle/native/pixelvis_golden.regen" ); }

int Count( const std::string &run, const char *pszNeedle )
{
	int n = 0;
	for ( size_t at = run.find( pszNeedle ); at != std::string::npos; at = run.find( pszNeedle, at + 1 ) )
		n++;
	return n;
}

} // namespace

TEST( PixelVisOracle, StockProducesTheGolden )
{
	SS_SKIP_ON_CI( "a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit" );
	const std::string run = RunScript();

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
		ASSERT_TRUE( ha && hb && la == lb ) << "line " << n << ": stock now writes\n  " << ( ha ? la : "<end>" )
			<< "\nand the golden has\n  " << ( hb ? lb : "<end>" )
			<< "\nDo NOT regenerate the golden to make this pass; see the file header.";
	}
}

// Each arm the script exists to reach, counted in the run.
TEST( PixelVisOracle, TheScriptReachesEveryArm )
{
	const std::string run = RunScript();
	struct Arm_t { const char *pszNeedle; const char *pszWhat; };
	static const Arm_t kArms[] = {
		{ "P\tPrecacheOcclusionProxy\tengine/occlusionproxy\tengine/occlusionproxy_countdraw", "the precache list" },
		{ "MF\tengine/occlusionproxy\tClientEffect textures\t1\nMI\tengine/occlusionproxy", "the proxy material referenced at the level's start" },
		{ "QC\t1\nQD\t1\n", "the level start's query probe was made and destroyed" },
		{ "QC\t0\n", "a query that could not be made" },
		{ "\nB\tengine/occlusionproxy\nDR\t00000000\t3c23d70a\n", "the counting pass's depth range" },
		{ "\nB\tengine/occlusionproxy_countdraw\n", "the proxies drawn visibly (r_drawpixelvisibility)" },
		{ "\nGM\t0\t0\t0\tengine/occlusionproxy\n", "a proxy's dynamic mesh" },
		{ "\nMESH\t2\t12\t", "a proxy's four triangles" },
		{ "\nQR\t", "a reused query was reset" },
		{ "\nD1\tPixels visible: ", "the partial arm's spew" },
		{ "\nD1\tDraw Proxy: ", "the proxy's spew" },
		{ "\nM\tPixel vis system using ", "pixelvis_debug" },
		{ "\nT\t", "a sight line was traced" },
		{ "\nF\tprop\t6\t", "the filter was asked about a vphysics prop" },
		{ "\nF\tbrush\t1\t", "the filter was asked about a brush" },
		{ "\nF\tplayer\t2\t0", "the filter turned a bounding box away" },
		{ "\nF\ttrigger\t0\t0", "the filter turned a non-solid away" },
		{ "\nFB\tprop\t", "the simple filter's own rule was asked" },
		{ "\nS\tshift\t0\t3\n", "a view's queries shifted onto a view that had its own" },
	};
	for ( const Arm_t &arm : kArms )
		EXPECT_NE( run.find( arm.pszNeedle ), std::string::npos ) << arm.pszWhat << ": no";

	// Counts that say the arms were taken more than once, and by every route.
	EXPECT_GE( Count( run, "\nQP\t" ), 40 ) << "pixel counts read";
	EXPECT_GE( Count( run, "\t-1\n" ), 3 ) << "unready pixel counts";
	EXPECT_GE( Count( run, "\nBQ\t" ), 40 ) << "proxies drawn";
	EXPECT_GE( Count( run, "\nPD\t" ), 40 ) << "sphere diameters asked";
	EXPECT_GE( Count( run, "\nQD\t" ), 10 ) << "query objects destroyed at a level's end";
	EXPECT_GE( Count( run, "\nR\t00000000\t" ), 3 ) << "questions answered 0";
	EXPECT_GE( Count( run, "\nR\t3f800000\t" ), 1 ) << "a question answered 1";
}

// THE FILTER, HELD TO STOCK'S CLASS. SourceSharp's GLOW trace filter asks
// glow_trace_filter.h's rule of the collideable a trace reaches, then hands the
// entity to CTraceFilterSimple as stock's class does; here that rule and the
// pristine CTraceFilterGlow are asked about an entity of every solid type, and
// must agree on each.
#include "../../../client/native/glow_trace_filter.h"

TEST( PixelVisOracle, OurFilterAnswersAsStocksClassDoes )
{
	CTraceFilterGlow stock( NULL, COLLISION_GROUP_NONE );
	int asked = 0, admitted = 0;
	for ( int solid = SOLID_NONE; solid < SOLID_LAST; solid++ )
	{
		CPixelVisEntity entity( "entity", (SolidType_t)solid );
		IHandleEntity *pEnt = static_cast< IClientUnknown * >( &entity );
		const bool bStock = stock.ShouldHitEntity( pEnt, MASK_OPAQUE | CONTENTS_MONSTER | CONTENTS_DEBRIS );
		// CTraceFilterSimple's stand-in answers true, so ours is the rule alone.
		const bool bOurs = GlowSightAdmits( solid );
		EXPECT_EQ( bStock, bOurs ) << "solid " << solid;
		asked++;
		admitted += bStock ? 1 : 0;
	}
	// Every solid type, and only VPHYSICS and BSP let through.
	EXPECT_EQ( (int)SOLID_LAST, asked );
	EXPECT_EQ( 2, admitted );
}
