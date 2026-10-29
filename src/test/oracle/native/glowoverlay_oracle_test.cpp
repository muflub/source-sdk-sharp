//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE GLOW OVERLAY ORACLE (C7, glow_overlay.cpp and warp_overlay.cpp): stock's
// glow overlays -- the active list (Activate adds an overlay once, Update's
// false deletes it, the level's end deletes them all), each overlay's
// obstruction (the pixel visibility question for a positional and an in-sky
// overlay, the trace and its fade when the card cannot answer, building
// cubemaps, the cache flags), the sprites' size and colour by the angle to the
// view, the basis and the quad each is drawn as, the frustum cull, the default
// material and its $hdrcolorscale, the wireframe pass, r_drawsprites, the
// clipping switch around the draws (off for a sky overlay), the warp overlay's
// own draw, and the sky overlays' obstruction -- run offline from the PRISTINE
// game/client/glow_overlay.cpp and warp_overlay.cpp over a scripted world, and
// written down as the golden src/test/client/Fixtures/glowoverlay-stock.tsv.
//
// THE SUBJECTS ARE INCLUDED, NOT LINKED: the overlay list and the ConVars are
// file statics. glowoverlay_stub/glowoverlay_harness.h says what is stock and
// what is stand-in; the material system, its render context, the dynamic mesh,
// the materials and their vars are vtable doubles below, every slot a trap
// but the ones the subjects call. The overlays are the subjects' classes, each
// given a name (a subclass whose destructor writes a row); one warp overlay's
// Update answers false on a scripted frame, which is the script's stimulus.
//
// THE GOLDEN CARRIES ITS STIMULUS: each step is an `S` row, followed by every
// call the subjects made and every answer they were given, so the managed
// replay (GlowOverlayOracleTests) drives the same steps, answers from the rows
// and holds every row. Regenerating the golden to make this test green deletes
// the check: it changes only when the script does (touch glowoverlay_golden.regen
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

#include "glowoverlay_harness.h"
#include "c_pixel_visibility.h"
#include "vtable_hook.h"

#include "mathlib_test_env.h"

//-----------------------------------------------------------------------------
// The log.
//-----------------------------------------------------------------------------
static std::string g_Log;

static void Append( const char *pFmt, va_list ap )
{
	char szLine[4096];
	vsnprintf( szLine, sizeof( szLine ), pFmt, ap );
	g_Log += szLine;
	g_Log += '\n';
}

void GlowLog( const char *pFmt, ... )
{
	va_list ap;
	va_start( ap, pFmt );
	Append( pFmt, ap );
	va_end( ap );
}

static unsigned int Bits( float f )
{
	unsigned int u;
	memcpy( &u, &f, sizeof( u ) );
	return u;
}

#define V3FMT "%08x\t%08x\t%08x"
#define V3( v ) Bits( ( v ).x ), Bits( ( v ).y ), Bits( ( v ).z )

//-----------------------------------------------------------------------------
// The console: ConVar::SetValue tells g_pCVar's global change callbacks.
//-----------------------------------------------------------------------------
#include "icvar.h"

class CGlowCvar final : public ICvar
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

static CGlowCvar g_GlowCvar;

//-----------------------------------------------------------------------------
// The globals, the view, the frustum, the wireframe question.
//-----------------------------------------------------------------------------
static GlowGlobals_t s_Globals;
GlowGlobals_t *gpGlobals = &s_Globals;

struct View_t
{
	Vector origin, forward;
	CViewSetup setup;
	VPlane frustum[5];
};

static View_t s_View;
static bool s_bWireframe = false;

const Vector &CurrentViewOrigin()
{
	return s_View.origin;
}

const Vector &CurrentViewForward()
{
	return s_View.forward;
}

const CViewSetup *CGlowViewRender::GetViewSetup() const
{
	return &s_View.setup;
}

VPlane *CGlowViewRender::GetFrustum()
{
	return s_View.frustum;
}

static CGlowViewRender s_ViewRender;
CGlowViewRender *view = &s_ViewRender;

// view.cpp's (shell_viewrender_client.cpp's in this build): wholly outside any plane.
bool R_CullSphere( VPlane const *pPlanes, int nPlanes, Vector const *pCenter, float radius )
{
	for ( int i = 0; i < nPlanes; i++ )
	{
		if ( pPlanes[i].DistTo( *pCenter ) < -radius )
			return true;
	}
	return false;
}

bool ShouldDrawInWireFrameMode()
{
	return s_bWireframe;
}

// view.cpp's (shell_sprite_leaf.cpp's in this build), which glow_overlay.cpp declares extern.
ConVar r_drawsprites( "r_drawsprites", "1", FCVAR_CHEAT );

//-----------------------------------------------------------------------------
// The pixel visibility: the script's answers, every question a row.
//-----------------------------------------------------------------------------
static bool s_bPixelVisAvailable = true;
static int s_nNextHandle = 1;
static int s_nQuestions = 0;

bool PixelVisibility_IsAvailable()
{
	GlowLog( "PA\t%d", s_bPixelVisAvailable ? 1 : 0 );
	return s_bPixelVisAvailable;
}

float PixelVisibility_FractionVisible( const pixelvis_queryparams_t &params, pixelvis_handle_t *queryHandle )
{
	static const float kAnswers[] = { 1.0f, 0.5f, 0.0f, 0.25f, 0.75f, 1.0f, 0.125f };
	const int hIn = queryHandle ? *queryHandle : -1;
	if ( queryHandle && *queryHandle == 0 )
		*queryHandle = s_nNextHandle++;
	const float f = kAnswers[s_nQuestions++ % ( sizeof( kAnswers ) / sizeof( kAnswers[0] ) )];
	GlowLog( "PV\t" V3FMT "\t%08x\t%08x\t%08x\t%d\t%d\t%08x\t%d", V3( params.position ), Bits( params.proxySize ),
			 Bits( params.proxyAspect ), Bits( params.fadeTime ), params.bSizeInScreenspace ? 1 : 0, hIn, Bits( f ),
			 queryHandle ? *queryHandle : -1 );
	return f;
}

//-----------------------------------------------------------------------------
// The trace: the scripted world's fraction and surface flags.
//-----------------------------------------------------------------------------
static float s_flTraceFraction = 1.0f;
static int s_nTraceFlags = 0;

void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask, const IHandleEntity *ignore,
					 int collisionGroup, trace_t *ptr )
{
	memset( (void *)ptr, 0, sizeof( *ptr ) );
	ptr->startpos = vecAbsStart;
	ptr->fraction = s_flTraceFraction;
	ptr->endpos = vecAbsStart + ( vecAbsEnd - vecAbsStart ) * ptr->fraction;
	ptr->surface.flags = (unsigned short)s_nTraceFlags;
	GlowLog( "T\t" V3FMT "\t" V3FMT "\t%08x\t%d\t%d\t%08x\t%d", V3( vecAbsStart ), V3( vecAbsEnd ), mask,
			 ignore ? 1 : 0, collisionGroup, Bits( ptr->fraction ), s_nTraceFlags );
}

//-----------------------------------------------------------------------------
// The client effect precache registrations, logged when the script starts.
//-----------------------------------------------------------------------------
static std::vector<std::string> &Precaches()
{
	static std::vector<std::string> s_Rows;
	return s_Rows;
}

GlowPrecache_t::GlowPrecache_t( const char *pszName, const char *const *ppszMaterials, int nMaterials )
{
	std::string row = std::string( "P\t" ) + pszName;
	for ( int i = 0; i < nMaterials; i++ )
		row += std::string( "\t" ) + ppszMaterials[i];
	Precaches().push_back( row );
}

//-----------------------------------------------------------------------------
// The material system doubles.
//-----------------------------------------------------------------------------
namespace
{
const int kSlots = 512;

template <int N>
void TrapAt()
{
	ADD_FAILURE() << "slot " << N << ", which the double does not answer, was called";
}

template <int... N>
void FillTraps( void **pTable, std::integer_sequence<int, N...> )
{
	void *traps[] = { (void *)&TrapAt<N>... };
	for ( int i = 0; i < (int)sizeof...( N ); i++ )
		pTable[i] = traps[i];
}

struct Fake_t
{
	void **pVtbl;
	void *pTable[kSlots];

	Fake_t()
		: pVtbl( pTable )
	{
		FillTraps( pTable, std::make_integer_sequence<int, kSlots>() );
	}

	template <typename Fn, typename Impl>
	void Set( Fn pmf, Impl impl )
	{
		const VTableHook::Member m = VTableHook::MemberOf( pmf );
		ASSERT_EQ( m.adjust, 0 );
		ASSERT_GE( m.slot, 0 );
		ASSERT_LT( m.slot, kSlots );
		pTable[m.slot] = (void *)impl;
	}
};

struct FakeVar_t : Fake_t
{
	char szMaterial[128];
};

struct FakeMaterial_t : Fake_t
{
	char szName[128];
	FakeVar_t var;
	bool bHasHdrScale;
};

Fake_t g_System, g_Context, g_Mesh;
FakeMaterial_t g_Materials[8];
int g_nMaterials = 0;
bool g_bClipping = true;

const char *MaterialNameOf( const void *p )
{
	return p ? static_cast<const FakeMaterial_t *>( p )->szName : "(null)";
}

// $hdrcolorscale: the default sprite material and sun/overlay carry it; a material named *_noscale does not.
IMaterialVar *FindVarFast( void *self, char const *pVarName, unsigned int * )
{
	FakeMaterial_t *m = static_cast<FakeMaterial_t *>( self );
	GlowLog( "FV\t%s\t%s\t%d", m->szName, pVarName, m->bHasHdrScale ? 1 : 0 );
	return m->bHasHdrScale ? (IMaterialVar *)&m->var : NULL;
}

void SetFloatValue( void *self, float val )
{
	GlowLog( "HS\t%s\t%08x", static_cast<FakeVar_t *>( self )->szMaterial, Bits( val ) );
}

const char *GetName( void *self )
{
	return MaterialNameOf( self );
}

IMaterial *FindMaterial( void *, const char *pName, const char *pGroup, bool bComplain, const char * )
{
	GlowLog( "MF\t%s\t%s\t%d", pName, pGroup, bComplain ? 1 : 0 );
	for ( int i = 0; i < g_nMaterials; i++ )
	{
		if ( !strcmp( g_Materials[i].szName, pName ) )
			return (IMaterial *)&g_Materials[i];
	}
	FakeMaterial_t &m = g_Materials[g_nMaterials++];
	V_strncpy( m.szName, pName, sizeof( m.szName ) );
	V_strncpy( m.var.szMaterial, pName, sizeof( m.var.szMaterial ) );
	m.bHasHdrScale = strstr( pName, "_noscale" ) == NULL;
	m.Set( &IMaterial::FindVarFast, &FindVarFast );
	m.Set( &IMaterial::GetName, &GetName );
	m.var.Set( static_cast<void ( IMaterialVar::* )( float )>( &IMaterialVar::SetFloatValue ), &SetFloatValue );
	return (IMaterial *)&m;
}

IMatRenderContext *GetRenderContext( void * )
{
	return (IMatRenderContext *)&g_Context;
}

int Release( void * )
{
	return 0;
}

void BeginRender( void * ) {}

void EndRender( void * ) {}

bool EnableClipping( void *, bool bEnable )
{
	const bool bOld = g_bClipping;
	g_bClipping = bEnable;
	GlowLog( "EC\t%d\t%d", bEnable ? 1 : 0, bOld ? 1 : 0 );
	return bOld;
}

void Bind( void *, IMaterial *pMaterial, void * )
{
	GlowLog( "B\t%s", MaterialNameOf( pMaterial ) );
}

IMesh *GetDynamicMesh( void *, bool bBuffered, IMesh *pVertexOverride, IMesh *pIndexOverride, IMaterial *pAutoBind )
{
	GlowLog( "GM\t%d\t%d\t%d\t%s", bBuffered ? 1 : 0, pVertexOverride ? 1 : 0, pIndexOverride ? 1 : 0,
			 MaterialNameOf( pAutoBind ) );
	return (IMesh *)&g_Mesh;
}

// The mesh: one interleaved vertex array.
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

Vertex_t g_Verts[64];
unsigned short g_Indices[256];
float g_Dummy[64];
int g_nPrimitive = -1;
int g_nUnlocked = 0;

void SetPrimitiveType( void *, MaterialPrimitiveType_t type )
{
	g_nPrimitive = type;
}

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

void UnlockMesh( void *, int nVerts, int, MeshDesc_t & )
{
	g_nUnlocked = nVerts;
}

void ValidateData( void *, int, int, const MeshDesc_t & ) {}

int VertexCount( const void * )
{
	return g_nUnlocked;
}

VertexFormat_t GetVertexFormat( const void * )
{
	return 0;
}

void DrawMesh( void *, int, int )
{
	std::string row = "MESH\t" + std::to_string( g_nPrimitive ) + "\t" + std::to_string( g_nUnlocked );
	char sz[128];
	for ( int i = 0; i < g_nUnlocked && i < 64; i++ )
	{
		const Vertex_t &v = g_Verts[i];
		snprintf( sz, sizeof( sz ), "\t%08x,%08x,%08x/%02x%02x%02x%02x/%08x,%08x", Bits( v.pos[0] ), Bits( v.pos[1] ),
				  Bits( v.pos[2] ), v.color[0], v.color[1], v.color[2], v.color[3], Bits( v.tc0[0] ),
				  Bits( v.tc0[1] ) );
		row += sz;
	}
	GlowLog( "%s", row.c_str() );
}

void BuildDoubles()
{
	g_System.Set( &IMaterialSystem::FindMaterial, &FindMaterial );
	g_System.Set( &IMaterialSystem::GetRenderContext, &GetRenderContext );
	g_Context.Set( &IMatRenderContext::Release, &Release );
	g_Context.Set( &IMatRenderContext::BeginRender, &BeginRender );
	g_Context.Set( &IMatRenderContext::EndRender, &EndRender );
	g_Context.Set( &IMatRenderContext::EnableClipping, &EnableClipping );
	g_Context.Set( &IMatRenderContext::Bind, &Bind );
	g_Context.Set( &IMatRenderContext::GetDynamicMesh, &GetDynamicMesh );
	g_Mesh.Set( &IMesh::SetPrimitiveType, &SetPrimitiveType );
	g_Mesh.Set( &IMesh::LockMesh, &LockMesh );
	g_Mesh.Set( &IMesh::UnlockMesh, &UnlockMesh );
	g_Mesh.Set( &IMesh::VertexCount, &VertexCount );
	g_Mesh.Set( &IMesh::GetVertexFormat, &GetVertexFormat );
	g_Mesh.Set( static_cast<void ( IMesh::* )( int, int, const MeshDesc_t & )>( &IMesh::ValidateData ), &ValidateData );
	g_Mesh.Set( static_cast<void ( IMesh::* )( int, int )>( &IMesh::Draw ), &DrawMesh );
}
} // namespace

IMaterialSystem *materials = (IMaterialSystem *)&g_System;

//-----------------------------------------------------------------------------
// The subjects.
//-----------------------------------------------------------------------------
#include "glow_overlay.cpp"
#include "warp_overlay.cpp"

//-----------------------------------------------------------------------------
// The script.
//-----------------------------------------------------------------------------
namespace
{

// The overlays, named: the subjects' classes, each with a destructor that says so.
class CNamedGlow : public CGlowOverlay
{
public:
	explicit CNamedGlow( const char *pszName )
		: m_pszName( pszName )
	{
	}

	virtual ~CNamedGlow() { GlowLog( "DEL\t%s", m_pszName ); }

	const char *m_pszName;
};

class CNamedWarp : public CWarpOverlay
{
public:
	explicit CNamedWarp( const char *pszName, int nLives )
		: m_pszName( pszName )
		, m_nLives( nLives )
	{
	}

	virtual ~CNamedWarp() { GlowLog( "DEL\t%s", m_pszName ); }

	// The script's stimulus: a warp overlay that lives a counted number of updates (-1: always).
	virtual bool Update()
	{
		if ( m_nLives < 0 )
			return true;
		return m_nLives-- > 0;
	}

	const char *m_pszName;
	int m_nLives;
};

struct Named_t
{
	const char *pszName;
	CGlowOverlay *p;
};

std::vector<Named_t> s_Overlays;

// Made zeroed, so the two cache flags the constructor leaves alone start false.
template <typename T, typename... A>
T *Make( A... args )
{
	void *mem = calloc( 1, sizeof( T ) );
	return new ( mem ) T( args... );
}

CGlowOverlay *Named( const char *psz )
{
	for ( const Named_t &n : s_Overlays )
	{
		if ( !strcmp( n.pszName, psz ) )
			return n.p;
	}
	ADD_FAILURE() << "no overlay " << psz;
	return NULL;
}

void Forget( const char *psz )
{
	for ( size_t i = 0; i < s_Overlays.size(); i++ )
	{
		if ( !strcmp( s_Overlays[i].pszName, psz ) )
		{
			s_Overlays.erase( s_Overlays.begin() + i );
			return;
		}
	}
}

// One overlay's description, as the replay builds it: kind, name, position or direction, the flags, the
// proxy radius, the HDR scale, and each sprite's colour, sizes and material (- for none, the default).
void LogOverlay( const char *pszKind, const char *pszName, int nLives, const CGlowOverlay *p,
				 const char *const *ppszMaterials )
{
	std::string row;
	char sz[256];
	snprintf( sz, sizeof( sz ), "S\toverlay\t%s\t%s\t%d\t" V3FMT "\t%d\t" V3FMT "\t%d\t%08x\t%08x\t%d", pszKind,
			  pszName, nLives, V3( p->m_vPos ), p->m_bDirectional ? 1 : 0, V3( p->m_vDirection ), p->m_bInSky ? 1 : 0,
			  Bits( p->m_flProxyRadius ), Bits( p->m_flHDRColorScale ), p->m_nSprites );
	row = sz;
	for ( int i = 0; i < p->m_nSprites; i++ )
	{
		snprintf( sz, sizeof( sz ), "\t" V3FMT "\t%08x\t%08x\t%s", V3( p->m_Sprites[i].m_vColor ),
				  Bits( p->m_Sprites[i].m_flHorzSize ), Bits( p->m_Sprites[i].m_flVertSize ),
				  ppszMaterials[i] ? ppszMaterials[i] : "-" );
		row += sz;
	}
	GlowLog( "%s", row.c_str() );
}

struct Sprite_t
{
	Vector color;
	float horz, vert;
	const char *pszMaterial;
};

void AddOverlay( const char *pszKind, const char *pszName, int nLives, const Vector &pos, bool bDirectional,
				 const Vector &dir, bool bInSky, float proxyRadius, float hdrScale,
				 std::initializer_list<Sprite_t> sprites )
{
	CGlowOverlay *p = !strcmp( pszKind, "warp" ) ? (CGlowOverlay *)Make<CNamedWarp>( pszName, nLives )
												 : (CGlowOverlay *)Make<CNamedGlow>( pszName );
	p->m_vPos = pos;
	p->m_bDirectional = bDirectional;
	p->m_vDirection = dir;
	p->m_bInSky = bInSky;
	p->m_flProxyRadius = proxyRadius;
	p->m_flHDRColorScale = hdrScale;
	const char *materials_[MAX_SUN_LAYERS] = {};
	int i = 0;
	for ( const Sprite_t &s : sprites )
	{
		p->m_Sprites[i].m_vColor = s.color;
		p->m_Sprites[i].m_flHorzSize = s.horz;
		p->m_Sprites[i].m_flVertSize = s.vert;
		materials_[i] = s.pszMaterial;
		i++;
	}
	p->m_nSprites = i;
	LogOverlay( pszKind, pszName, nLives, p, materials_ );
	// The creator's materials: found by the creator, before the overlay is drawn.
	for ( int k = 0; k < p->m_nSprites; k++ )
	{
		if ( materials_[k] )
			p->m_Sprites[k].m_pMaterial = materials->FindMaterial( materials_[k], TEXTURE_GROUP_CLIENT_EFFECTS );
	}
	p->Activate();
	s_Overlays.push_back( { pszName, p } );
}

// A view looking along yaw (degrees, about z) and pitch from origin; its frustum's five planes (the
// four sides of a 90-degree view and the near plane one unit out), inward-facing.
void SetView( const Vector &origin, float yaw, float pitch, float zFar )
{
	s_View.origin = origin;
	QAngle ang( pitch, yaw, 0 );
	Vector f, r, u;
	AngleVectors( ang, &f, &r, &u );
	s_View.forward = f;
	s_View.setup.zFar = zFar;
	Vector n[5] = { f + r, f - r, f - u, f + u, f };
	for ( int i = 0; i < 5; i++ )
	{
		if ( i < 4 )
			VectorNormalize( n[i] );
		const float d = DotProduct( n[i], origin ) + ( i == 4 ? 1.0f : 0.0f );
		s_View.frustum[i].Init( n[i], d );
	}
	std::string row = "S\tview";
	char sz[64];
	snprintf( sz, sizeof( sz ), "\t" V3FMT "\t" V3FMT "\t%08x", V3( origin ), V3( f ), Bits( zFar ) );
	row += sz;
	for ( int i = 0; i < 5; i++ )
	{
		snprintf( sz, sizeof( sz ), "\t" V3FMT "\t%08x", V3( s_View.frustum[i].m_Normal ),
				  Bits( s_View.frustum[i].m_Dist ) );
		row += sz;
	}
	GlowLog( "%s", row.c_str() );
}

void Frame( float frametime )
{
	gpGlobals->framecount++;
	gpGlobals->frametime = frametime;
	GlowLog( "S\tframe\t%08x", Bits( frametime ) );
}

void SetCvar( ConVar &var, const char *pszValue )
{
	GlowLog( "S\tcvar\t%s\t%s", var.GetName(), pszValue );
	var.SetValue( pszValue );
}

void Available( bool b )
{
	GlowLog( "S\tavailable\t%d", b ? 1 : 0 );
	s_bPixelVisAvailable = b;
}

void Wireframe( bool b )
{
	GlowLog( "S\twireframe\t%d", b ? 1 : 0 );
	s_bWireframe = b;
}

void World( float fraction, int flags )
{
	GlowLog( "S\tworld\t%08x\t%d", Bits( fraction ), flags );
	s_flTraceFraction = fraction;
	s_nTraceFlags = flags;
}

void Clipping( bool b )
{
	GlowLog( "S\tclipping\t%d", b ? 1 : 0 );
	g_bClipping = b;
}

void Draw( bool bCache )
{
	GlowLog( "S\tdraw\t%d", bCache ? 1 : 0 );
	CGlowOverlay::DrawOverlays( bCache );
	// An overlay whose Update answered false is gone.
	for ( size_t i = 0; i < s_Overlays.size(); )
	{
		bool bListed = false;
		for ( unsigned short k = g_GlowOverlaySystem.m_GlowOverlays.Head();
			  k != g_GlowOverlaySystem.m_GlowOverlays.InvalidIndex(); k = g_GlowOverlaySystem.m_GlowOverlays.Next( k ) )
			bListed |= g_GlowOverlaySystem.m_GlowOverlays[k] == s_Overlays[i].p;
		if ( bListed )
			i++;
		else
			s_Overlays.erase( s_Overlays.begin() + i );
	}
}

void Sky( float zFar, bool bCache )
{
	GlowLog( "S\tsky\t%08x\t%d", Bits( zFar ), bCache ? 1 : 0 );
	CGlowOverlay::UpdateSkyOverlays( zFar, bCache );
}

void Deactivate( const char *psz )
{
	GlowLog( "S\tdeactivate\t%s", psz );
	Named( psz )->Deactivate();
}

void Activate( const char *psz )
{
	GlowLog( "S\tactivate\t%s", psz );
	Named( psz )->Activate();
}

void Delete( const char *psz )
{
	GlowLog( "S\tdelete\t%s", psz );
	CGlowOverlay *p = Named( psz );
	Forget( psz );
	delete p;
}

void Shutdown()
{
	GlowLog( "S\tshutdown" );
	g_GlowOverlaySystem.LevelShutdownPostEntity();
	s_Overlays.clear();
}

const Vector kEye( 0, 0, 0 );

void AddTheOverlays()
{
	// A positional glow ahead: two sprites, the default material and one without $hdrcolorscale.
	AddOverlay( "glow", "ahead", -1, Vector( 300, 20, 10 ), false, Vector( 0, 0, 0 ), false, 2.0f, 2.0f,
				{ { Vector( 1, 0.8f, 0.6f ), 1.0f, 0.5f, NULL },
				  { Vector( 0.2f, 0.4f, 1 ), 0.5f, 0.5f, "sprites/glow_noscale" } } );
	// A warp overlay to one side, which lives for ever.
	AddOverlay( "warp", "flash", -1, Vector( 200, -40, 0 ), false, Vector( 0, 0, 0 ), false, 2.0f, 1.0f,
				{ { Vector( 1, 0.9f, 0.7f ), 0.05f, 0.025f, NULL } } );
	// The sun: directional and in the sky.
	AddOverlay(
		"glow", "sun", -1, Vector( 0, 0, 0 ), true, Vector( 0.8660254f, 0.2f, 0.4582576f ), true, 4.0f, 1.5f,
		{ { Vector( 1, 1, 0.9f ), 2.0f, 2.0f, "sun/overlay" }, { Vector( 0.5f, 0.5f, 0.4f ), 4.0f, 4.0f, NULL } } );
	// A warp overlay in the sky.
	AddOverlay( "warp", "skyflash", -1, Vector( 0, 0, 0 ), true, Vector( 0.9f, -0.3f, 0.3162278f ), true, 2.0f, 1.0f,
				{ { Vector( 0.6f, 0.6f, 0.6f ), 1.0f, 1.0f, NULL } } );
	// A warp overlay that lives three updates.
	AddOverlay( "warp", "brief", 3, Vector( 150, 10, -5 ), false, Vector( 0, 0, 0 ), false, 2.0f, 1.0f,
				{ { Vector( 1, 0.9f, 0.7f ), 0.01f, 0.005f, NULL } } );
	// A glow behind the eye, and one big glow off to the edge of the frustum.
	AddOverlay( "glow", "behind", -1, Vector( -300, 0, 0 ), false, Vector( 0, 0, 0 ), false, 2.0f, 1.0f,
				{ { Vector( 1, 1, 1 ), 1.0f, 1.0f, NULL } } );
	AddOverlay( "glow", "edge", -1, Vector( 200, 190, 0 ), false, Vector( 0, 0, 0 ), false, 2.0f, 1.0f,
				{ { Vector( 1, 1, 1 ), 0.2f, 0.2f, NULL } } );
	// A dim glow whose colour is all but zero.
	AddOverlay( "glow", "dim", -1, Vector( 250, 0, 0 ), false, Vector( 0, 0, 0 ), false, 2.0f, 1.0f,
				{ { Vector( 0.001f, 0.001f, 0.001f ), 1.0f, 1.0f, NULL } } );
}

std::string RunScript()
{
	static std::string s_Run;
	static bool s_bRan = false;
	if ( s_bRan )
		return s_Run;
	s_bRan = true;

	g_pCVar = &g_GlowCvar;
	BuildDoubles();
	g_Log.clear();
	for ( const std::string &row : Precaches() )
		GlowLog( "%s", row.c_str() );

	//-------------------------------------------------------------------------
	// A level with pixel visibility: twelve frames.
	//-------------------------------------------------------------------------
	Clipping( true );
	World( 1.0f, 0 );
	SetView( kEye, 0, 0, 8000.0f );
	AddTheOverlays();
	for ( int frame = 1; frame <= 12; frame++ )
	{
		Frame( frame == 6 ? 0.05f : 1.0f / 60.0f );
		if ( frame == 3 )
			Clipping( false );
		if ( frame == 4 )
			Clipping( true );
		if ( frame == 5 )
			Wireframe( true );
		if ( frame == 6 )
			Wireframe( false );
		if ( frame == 7 )
			SetCvar( r_drawsprites, "0" );
		if ( frame == 8 )
			SetCvar( r_drawsprites, "2" );
		if ( frame == 9 )
			SetCvar( r_drawsprites, "1" );
		if ( frame == 10 )
			Deactivate( "flash" );
		if ( frame == 11 )
			Activate( "flash" );
		// Turned toward the edge glow and the sun for a while.
		SetView( frame >= 8 ? Vector( 10, 0, 0 ) : kEye, frame >= 8 ? 25.0f : 0.0f, frame == 12 ? -20.0f : 0.0f,
				 8000.0f );
		Sky( 8000.0f, frame == 9 || frame == 10 );
		Draw( frame == 9 || frame == 10 );
	}

	//-------------------------------------------------------------------------
	// The same overlays with the card unable to answer: traces and the fade.
	//-------------------------------------------------------------------------
	Available( false );
	for ( int frame = 13; frame <= 22; frame++ )
	{
		Frame( frame == 16 ? 0.1f : 1.0f / 60.0f );
		SetView( frame >= 18 ? Vector( 10, 0, 0 ) : kEye, frame >= 18 ? 25.0f : 0.0f, 0.0f, 8000.0f );
		if ( frame == 14 )
			World( 0.5f, 0 );
		if ( frame == 15 )
			World( 0.5f, SURF_SKY );
		if ( frame == 16 )
			World( 1.0f, SURF_SKY );
		if ( frame == 17 )
			World( 1.0f, 0 );
		if ( frame == 18 )
			SetCvar( building_cubemaps, "1" );
		if ( frame == 19 )
			SetCvar( building_cubemaps, "0" );
		if ( frame == 20 )
			SetCvar( cl_sun_decay_rate, "0.2" );
		if ( frame == 21 )
			World( 0.25f, 0 );
		Sky( 8000.0f, frame == 22 );
		Draw( frame == 22 );
	}
	SetCvar( cl_sun_decay_rate, "0.05" );
	Available( true );

	// An overlay deleted by its owner, then the level's end deletes the rest.
	Delete( "dim" );
	Frame( 1.0f / 60.0f );
	Draw( false );
	Shutdown();

	//-------------------------------------------------------------------------
	// A level again: the list is new.
	//-------------------------------------------------------------------------
	World( 1.0f, 0 );
	SetView( kEye, 0, 0, 4000.0f );
	AddOverlay( "warp", "again", 2, Vector( 180, 0, 5 ), false, Vector( 0, 0, 0 ), false, 2.0f, 1.0f,
				{ { Vector( 1, 0.9f, 0.7f ), 0.05f, 0.025f, NULL } } );
	AddOverlay( "glow", "again2", -1, Vector( 260, -10, 0 ), false, Vector( 0, 0, 0 ), false, 2.0f, 1.0f,
				{ { Vector( 1, 1, 1 ), 1.0f, 1.0f, NULL } } );
	for ( int frame = 23; frame <= 26; frame++ )
	{
		Frame( 1.0f / 60.0f );
		Sky( 4000.0f, false );
		Draw( false );
	}
	Shutdown();

	s_Run = g_Log;
	return s_Run;
}

std::string RepoFile( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

std::string GoldenPath()
{
	return RepoFile( "src/test/client/Fixtures/glowoverlay-stock.tsv" );
}

std::string MarkerPath()
{
	return RepoFile( "src/test/oracle/native/glowoverlay_golden.regen" );
}

int Count( const std::string &run, const char *pszNeedle )
{
	int n = 0;
	for ( size_t at = run.find( pszNeedle ); at != std::string::npos; at = run.find( pszNeedle, at + 1 ) )
		n++;
	return n;
}

} // namespace

TEST( GlowOverlayOracle, StockProducesTheGolden )
{
	SS_SKIP_ON_CI(
		"a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit" );
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
		ASSERT_TRUE( ha && hb && la == lb )
			<< "line " << n << ": stock now writes\n  " << ( ha ? la : "<end>" ) << "\nand the golden has\n  "
			<< ( hb ? lb : "<end>" ) << "\nDo NOT regenerate the golden to make this pass; see the file header.";
	}
}

// Each arm the script exists to reach, counted in the run.
TEST( GlowOverlayOracle, TheScriptReachesEveryArm )
{
	const std::string run = RunScript();

	struct Arm_t
	{
		const char *pszNeedle;
		const char *pszWhat;
	};

	static const Arm_t kArms[] = {
		{ "P\tPrecacheEffectGlow\tsun/overlay\tsprites/light_glow02_add_noz", "the glow precache list" },
		{ "P\tPrecacheEffectWarp\tsun/overlay", "the warp precache list" },
		{ "\nMF\tsprites/light_glow02_add_noz\tClientEffect textures\t1\n", "the default material found at a draw" },
		{ "\nMF\tsun/overlay\tClientEffect textures\t1\n", "the warp overlay's material" },
		{ "\nMF\tdebug/debugwireframevertexcolor\tOther textures\t1\n", "the wireframe material" },
		{ "\nB\tdebug/debugwireframevertexcolor\n", "the wireframe pass bound" },
		{ "\nFV\tsprites/glow_noscale\t$hdrcolorscale\t0\n", "a material without $hdrcolorscale" },
		{ "\nHS\tsprites/light_glow02_add_noz\t", "$hdrcolorscale set" },
		{ "\nMESH\t2\t4\t", "a quad drawn (MATERIAL_QUADS is built as two triangles)" },
		{ "\nEC\t1\t", "clipping enabled around the draws" },
		{ "\nEC\t0\t", "clipping switched off for a sky overlay" },
		{ "\nPA\t0\n", "the card unable to answer" },
		{ "\nT\t", "a trace at an overlay" },
		{ "\nDEL\tbrief\n", "an overlay whose Update answered false, deleted at a draw" },
		{ "\nDEL\tdim\n", "an overlay deleted by its owner" },
		{ "S\tshutdown\nDEL\t", "the level's end deleted the rest" },
	};
	for ( const Arm_t &arm : kArms )
		EXPECT_NE( run.find( arm.pszNeedle ), std::string::npos ) << arm.pszWhat << ": no";

	EXPECT_GE( Count( run, "\nPV\t" ), 60 ) << "pixel visibility questions";
	EXPECT_GE( Count( run, "\t00000001\t" ), 1 ) << "a screen-space question";
	EXPECT_GE( Count( run, "\nMESH\t" ), 60 ) << "quads drawn";
	EXPECT_GE( Count( run, "\nT\t" ), 10 ) << "traces";
}
