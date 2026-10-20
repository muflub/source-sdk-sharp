//========= SourceSharp — the client shadow manager's native table, carried out //
//
// C7 cluster 12 S2 (docs/plans/c7_shadow_manager.md). The shadow manager is
// managed's; what it does to the engine and asks of it crosses as a buffer of
// records that SSFwd_Shadows_Run (fwd_shadows.cpp) carries out in order. Each
// record kind must reach the one engine method it names with the arguments
// the record carries, in the record's order; each question must write its
// answer words after the answers before it; and the run's bounds (a record
// past the buffer, a short record, an answer past the answer buffer) must end
// or count exactly as fwd_shadows.h documents.
//
// THE FAKES ARE VTABLE ARRAYS, as fwd_leaf_system_test's: each recorder sits
// at the slot the compiler says its method occupies (VTableHook::MemberOf),
// the rest null, so a call to anything else faults rather than passes. The
// environment's client.so functions are recorders too.
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_fwd_sdk.h"
#include "fwd_shadows.h"
#include "vtable_hook.h"

#include "cdll_int.h"
#include "globalvars_base.h"
#include "engine/ishadowmgr.h"
#include "engine/IStaticPropMgr.h"
#include "engine/ivmodelinfo.h"
#include "engine/ivmodelrender.h"
#include "engine/ivdebugoverlay.h"
#include "ivrenderview.h"
#include "iclientrenderable.h"
#include "iclientunknown.h"
#include "iclientnetworkable.h"
#include "bsptreedata.h"
#include "cmodel.h"
#include "view_shared.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/itexture.h"
#include "mathlib/vmatrix.h"
#include "sourcesharp.h"

#include <stdarg.h>
#include <stdio.h>
#include <string.h>

#ifdef _WIN32
extern "C" __declspec( dllimport ) void *__stdcall VirtualAlloc( void *pAddress, size_t nSize, unsigned long nType, unsigned long nProtect );
#else
#include <sys/mman.h>
#endif

#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// A fake object: word 0 is the vtable, recorders at the compiler's slots.
//-----------------------------------------------------------------------------
struct FakeTable
{
	void *slots[ 512 ];

	FakeTable() { memset( slots, 0, sizeof( slots ) ); }

	template < typename Fn >
	void Put( Fn pmf, void *pRecorder )
	{
		const int slot = VTableHook::MemberOf( pmf ).slot;
		EXPECT_GE( slot, 0 );
		EXPECT_LT( slot, 512 );
		if ( slot >= 0 && slot < 512 )
			slots[ slot ] = pRecorder;
	}
};

struct FakeObject
{
	void **vtable;
};

std::vector< std::string > g_calls;

void Call( const std::string &s ) { g_calls.push_back( s ); }

// Calls whose name starts with a prefix ("sm." the shadow manager, "ctx." the
// render context, "env." client.so's functions, ...).
std::vector< std::string > CallsOf( const char *pszPrefix )
{
	std::vector< std::string > out;
	for ( const std::string &s : g_calls )
		if ( s.compare( 0, strlen( pszPrefix ), pszPrefix ) == 0 )
			out.push_back( s );
	return out;
}

std::string Fl( float f )
{
	char sz[ 64 ];
	snprintf( sz, sizeof( sz ), "%g", (double)f );
	return sz;
}

std::string Db( double d )
{
	char sz[ 64 ];
	snprintf( sz, sizeof( sz ), "%.17g", d );
	return sz;
}

std::string Vec( const Vector &v ) { return "(" + Fl( v.x ) + " " + Fl( v.y ) + " " + Fl( v.z ) + ")"; }
std::string Ang( const QAngle &v ) { return "(" + Fl( v.x ) + " " + Fl( v.y ) + " " + Fl( v.z ) + ")"; }

std::string Mat( const VMatrix &m )
{
	std::string s = "[";
	for ( int r = 0; r < 4; r++ )
		for ( int c = 0; c < 4; c++ )
			s += ( r || c ? " " : "" ) + Fl( m.m[ r ][ c ] );
	return s + "]";
}

std::string Hex( const void *p )
{
	char sz[ 32 ];
	snprintf( sz, sizeof( sz ), "0x%llx", (unsigned long long)(uintptr_t)p );
	return sz;
}

std::string LeafList( int n, const int *p )
{
	std::string s = std::to_string( n ) + ":";
	for ( int i = 0; i < n; i++ )
		s += ( i ? "," : "" ) + std::to_string( p[ i ] );
	return s;
}

//-----------------------------------------------------------------------------
// Materials and textures: client.so's references, by id.
//-----------------------------------------------------------------------------
struct FakeMaterial
{
	void **vtable;
	int id;
};

struct FakeTexture
{
	void **vtable;
	int id;
	int w, h;
};

std::string MatName( const IMaterial *p ) { return p ? "M" + std::to_string( reinterpret_cast< const FakeMaterial * >( p )->id ) : "null"; }
std::string TexName( const ITexture *p ) { return p ? "T" + std::to_string( reinterpret_cast< const FakeTexture * >( p )->id ) : "null"; }

void Rec_ColorModulate( void *self, float r, float g, float b )
{
	Call( "mat.ColorModulate " + MatName( static_cast< IMaterial * >( self ) ) + " " + Fl( r ) + " " + Fl( g ) + " " + Fl( b ) );
}
int Rec_GetActualWidth( const void *self ) { return static_cast< const FakeTexture * >( self )->w; }
int Rec_GetActualHeight( const void *self ) { return static_cast< const FakeTexture * >( self )->h; }

struct MaterialTable : FakeTable
{
	MaterialTable() { Put( &IMaterial::ColorModulate, (void *)&Rec_ColorModulate ); }
};
struct TextureTable : FakeTable
{
	TextureTable()
	{
		Put( &ITexture::GetActualWidth, (void *)&Rec_GetActualWidth );
		Put( &ITexture::GetActualHeight, (void *)&Rec_GetActualHeight );
	}
};
MaterialTable g_materialTable;
TextureTable g_textureTable;

// Ids 1..7 name a reference; 0 names none.
FakeMaterial g_materials[ 8 ];
FakeTexture g_textures[ 8 ];

struct InitRefs
{
	InitRefs()
	{
		for ( int i = 0; i < 8; i++ )
		{
			g_materials[ i ] = { g_materialTable.slots, i };
			g_textures[ i ] = { g_textureTable.slots, i, 64 * i, 32 * i + 1 };
		}
	}
} g_initRefs;

IMaterial *MaterialOf( int id ) { return id > 0 && id < 8 ? reinterpret_cast< IMaterial * >( &g_materials[ id ] ) : nullptr; }
ITexture *TextureOf( int id ) { return id > 0 && id < 8 ? reinterpret_cast< ITexture * >( &g_textures[ id ] ) : nullptr; }

//-----------------------------------------------------------------------------
// Renderables, their unknowns and networkables.
//-----------------------------------------------------------------------------
struct FakeNetworkable
{
	void **vtable;
	int entindex;
};

struct FakeUnknown
{
	void **vtable;
	FakeNetworkable *net;
};

struct FakeRenderable
{
	void **vtable;
	int id;
	Vector origin;
	QAngle angles;
	matrix3x4_t toWorld;
	const model_t *model;
	int body, skin;
	Vector absMins, absMaxs;
	Vector shadowMins, shadowMaxs;
	int receiveMask;
	bool answersDistance;
	float distance;
	bool answersDirection;
	Vector direction;
	FakeRenderable *parent, *child, *peer;
	ModelInstanceHandle_t instance;
	FakeUnknown *unknown;
};

FakeRenderable *R( void *self ) { return static_cast< FakeRenderable * >( self ); }
const FakeRenderable *R( const void *self ) { return static_cast< const FakeRenderable * >( self ); }

std::string RName( const IClientRenderable *p ) { return p ? "R" + std::to_string( reinterpret_cast< const FakeRenderable * >( p )->id ) : "null"; }

int Rec_EntIndex( const void *self ) { return static_cast< const FakeNetworkable * >( self )->entindex; }
IClientNetworkable *Rec_GetClientNetworkable( void *self )
{
	return reinterpret_cast< IClientNetworkable * >( static_cast< FakeUnknown * >( self )->net );
}

IClientUnknown *Rec_GetIClientUnknown( void *self ) { return reinterpret_cast< IClientUnknown * >( R( self )->unknown ); }
const Vector &Rec_GetRenderOrigin( void *self ) { return R( self )->origin; }
const QAngle &Rec_GetRenderAngles( void *self ) { return R( self )->angles; }
const matrix3x4_t &Rec_RenderableToWorldTransform( void *self ) { return R( self )->toWorld; }
const model_t *Rec_GetModel( const void *self ) { return R( self )->model; }
int Rec_GetBody( void *self ) { return R( self )->body; }
int Rec_GetSkin( void *self ) { return R( self )->skin; }
void Rec_GetRenderBoundsWorldspace( void *self, Vector &mins, Vector &maxs )
{
	mins = R( self )->absMins;
	maxs = R( self )->absMaxs;
}
void Rec_GetShadowRenderBounds( void *self, Vector &mins, Vector &maxs, ShadowType_t type )
{
	Call( "r.GetShadowRenderBounds " + std::to_string( R( self )->id ) + " " + std::to_string( (int)type ) );
	mins = R( self )->shadowMins;
	maxs = R( self )->shadowMaxs;
}
bool Rec_ShouldReceiveProjectedTextures( void *self, int flags )
{
	Call( "r.ShouldReceiveProjectedTextures " + std::to_string( R( self )->id ) + " " + std::to_string( flags ) );
	return ( R( self )->receiveMask & flags ) != 0;
}
bool Rec_GetShadowCastDistance( const void *self, float *pDist, ShadowType_t type )
{
	Call( "r.GetShadowCastDistance " + std::to_string( R( self )->id ) + " " + Fl( *pDist ) + " " + std::to_string( (int)type ) );
	if ( !R( self )->answersDistance )
		return false;
	*pDist = R( self )->distance;
	return true;
}
bool Rec_GetShadowCastDirection( const void *self, Vector *pDir, ShadowType_t type )
{
	Call( "r.GetShadowCastDirection " + std::to_string( R( self )->id ) + " " + Vec( *pDir ) + " " + std::to_string( (int)type ) );
	if ( !R( self )->answersDirection )
		return false;
	*pDir = R( self )->direction;
	return true;
}
IClientRenderable *Rec_GetShadowParent( void *self ) { return reinterpret_cast< IClientRenderable * >( R( self )->parent ); }
IClientRenderable *Rec_FirstShadowChild( void *self ) { return reinterpret_cast< IClientRenderable * >( R( self )->child ); }
IClientRenderable *Rec_NextShadowPeer( void *self ) { return reinterpret_cast< IClientRenderable * >( R( self )->peer ); }
void Rec_CreateModelInstance( void *self ) { Call( "r.CreateModelInstance " + std::to_string( R( self )->id ) ); }
ModelInstanceHandle_t Rec_GetModelInstance( void *self ) { return R( self )->instance; }

struct RenderableTable : FakeTable
{
	RenderableTable()
	{
		Put( &IClientRenderable::GetIClientUnknown, (void *)&Rec_GetIClientUnknown );
		Put( &IClientRenderable::GetRenderOrigin, (void *)&Rec_GetRenderOrigin );
		Put( &IClientRenderable::GetRenderAngles, (void *)&Rec_GetRenderAngles );
		Put( &IClientRenderable::RenderableToWorldTransform, (void *)&Rec_RenderableToWorldTransform );
		Put( &IClientRenderable::GetModel, (void *)&Rec_GetModel );
		Put( &IClientRenderable::GetBody, (void *)&Rec_GetBody );
		Put( &IClientRenderable::GetSkin, (void *)&Rec_GetSkin );
		Put( &IClientRenderable::GetRenderBoundsWorldspace, (void *)&Rec_GetRenderBoundsWorldspace );
		Put( &IClientRenderable::GetShadowRenderBounds, (void *)&Rec_GetShadowRenderBounds );
		Put( &IClientRenderable::ShouldReceiveProjectedTextures, (void *)&Rec_ShouldReceiveProjectedTextures );
		Put( &IClientRenderable::GetShadowCastDistance, (void *)&Rec_GetShadowCastDistance );
		Put( &IClientRenderable::GetShadowCastDirection, (void *)&Rec_GetShadowCastDirection );
		Put( &IClientRenderable::GetShadowParent, (void *)&Rec_GetShadowParent );
		Put( &IClientRenderable::FirstShadowChild, (void *)&Rec_FirstShadowChild );
		Put( &IClientRenderable::NextShadowPeer, (void *)&Rec_NextShadowPeer );
		Put( &IClientRenderable::CreateModelInstance, (void *)&Rec_CreateModelInstance );
		Put( &IClientRenderable::GetModelInstance, (void *)&Rec_GetModelInstance );
	}
};
struct UnknownTable : FakeTable
{
	UnknownTable() { Put( &IClientUnknown::GetClientNetworkable, (void *)&Rec_GetClientNetworkable ); }
};
struct NetworkableTable : FakeTable
{
	NetworkableTable() { Put( &IClientNetworkable::entindex, (void *)&Rec_EntIndex ); }
};
RenderableTable g_renderableTable;
UnknownTable g_unknownTable;
NetworkableTable g_networkableTable;

FakeRenderable Make( int id )
{
	FakeRenderable r;
	memset( (void *)&r, 0, sizeof( r ) );
	r.vtable = g_renderableTable.slots;
	r.id = id;
	r.origin.Init( id, 2.5f, -3 );
	r.angles.Init( 10, 20 + id, 30 );
	for ( int i = 0; i < 3; i++ )
		for ( int j = 0; j < 4; j++ )
			r.toWorld[ i ][ j ] = id * 100.0f + i * 4 + j + 0.25f;
	r.model = reinterpret_cast< const model_t * >( (uintptr_t)0x0000ABCD00001230ull + id );
	r.body = 40 + id;
	r.skin = 50 + id;
	r.absMins.Init( -1, -2, -3 );
	r.absMaxs.Init( 4, 5, 6 + id );
	r.shadowMins.Init( -7, -8, -9 );
	r.shadowMaxs.Init( 7, 8, 9 + id );
	r.instance = (ModelInstanceHandle_t)( 600 + id );
	return r;
}

IClientRenderable *AsRenderable( FakeRenderable &r ) { return reinterpret_cast< IClientRenderable * >( &r ); }

//-----------------------------------------------------------------------------
// The shadow manager and the static prop manager.
//-----------------------------------------------------------------------------
ShadowInfo_t g_info;
Frustum_t g_frustum;
FlashlightState_t g_flash;

void Rec_DestroyShadow( void *, ShadowHandle_t h ) { Call( "sm.DestroyShadow " + std::to_string( h ) ); }
void Rec_SetShadowMaterial( void *, ShadowHandle_t h, IMaterial *a, IMaterial *b, void *proxy )
{
	Call( "sm.SetShadowMaterial " + std::to_string( h ) + " " + MatName( a ) + " " + MatName( b ) + " " + Hex( proxy ) );
}
void Rec_ProjectShadow( void *, ShadowHandle_t h, const Vector &origin, const Vector &dir, const VMatrix &m, const Vector2D &size, int n,
                        const int *pLeaves, float maxHeight, float falloffOffset, float falloffAmount, const Vector &caster )
{
	Call( "sm.ProjectShadow " + std::to_string( h ) + " " + Vec( origin ) + " " + Vec( dir ) + " " + Mat( m ) + " (" + Fl( size.x ) + " " +
	      Fl( size.y ) + ") " + LeafList( n, pLeaves ) + " " + Fl( maxHeight ) + " " + Fl( falloffOffset ) + " " + Fl( falloffAmount ) + " " +
	      Vec( caster ) );
}
void Rec_ProjectFlashlight( void *, ShadowHandle_t h, const VMatrix &m, int n, const int *pLeaves )
{
	Call( "sm.ProjectFlashlight " + std::to_string( h ) + " " + Mat( m ) + " " + LeafList( n, pLeaves ) );
}
const ShadowInfo_t &Rec_GetInfo( void *, ShadowHandle_t h )
{
	Call( "sm.GetInfo " + std::to_string( h ) );
	return g_info;
}
const Frustum_t &Rec_GetFlashlightFrustum( void *, ShadowHandle_t h )
{
	Call( "sm.GetFlashlightFrustum " + std::to_string( h ) );
	return g_frustum;
}
void Rec_AddShadowToBrushModel( void *, ShadowHandle_t h, model_t *pModel, const Vector &origin, const QAngle &angles )
{
	Call( "sm.AddShadowToBrushModel " + std::to_string( h ) + " " + Hex( pModel ) + " " + Vec( origin ) + " " + Ang( angles ) );
}
void Rec_RemoveAllShadowsFromBrushModel( void *, model_t *pModel ) { Call( "sm.RemoveAllShadowsFromBrushModel " + Hex( pModel ) ); }
void Rec_SetShadowTexCoord( void *, ShadowHandle_t h, float x, float y, float w, float hh )
{
	Call( "sm.SetShadowTexCoord " + std::to_string( h ) + " " + Fl( x ) + " " + Fl( y ) + " " + Fl( w ) + " " + Fl( hh ) );
}
void Rec_AddShadowToModel( void *, ShadowHandle_t h, ModelInstanceHandle_t instance )
{
	Call( "sm.AddShadowToModel " + std::to_string( h ) + " " + std::to_string( instance ) );
}
void Rec_RemoveAllShadowsFromModel( void *, ModelInstanceHandle_t instance ) { Call( "sm.RemoveAllShadowsFromModel " + std::to_string( instance ) ); }
void Rec_ClearExtraClipPlanes( void *, ShadowHandle_t h ) { Call( "sm.ClearExtraClipPlanes " + std::to_string( h ) ); }
void Rec_AddExtraClipPlane( void *, ShadowHandle_t h, const Vector &normal, float dist )
{
	Call( "sm.AddExtraClipPlane " + std::to_string( h ) + " " + Vec( normal ) + " " + Fl( dist ) );
}
void Rec_EnableShadow( void *, ShadowHandle_t h, bool bEnable ) { Call( "sm.EnableShadow " + std::to_string( h ) + " " + std::to_string( bEnable ) ); }
void Rec_SetFalloffBias( void *, ShadowHandle_t h, unsigned char bias ) { Call( "sm.SetFalloffBias " + std::to_string( h ) + " " + std::to_string( bias ) ); }
void Rec_UpdateFlashlightState( void *, ShadowHandle_t h, const FlashlightState_t &state )
{
	Call( "sm.UpdateFlashlightState " + std::to_string( h ) );
	g_flash = state;
}
void Rec_AddFlashlightRenderable( void *, ShadowHandle_t h, IClientRenderable *p )
{
	Call( "sm.AddFlashlightRenderable " + std::to_string( h ) + " " + RName( p ) );
}
ShadowHandle_t Rec_CreateShadowEx( void *, IMaterial *a, IMaterial *b, void *proxy, int flags )
{
	Call( "sm.CreateShadowEx " + MatName( a ) + " " + MatName( b ) + " " + Hex( proxy ) + " " + std::to_string( flags ) );
	return 321;
}
void Rec_SetFlashlightDepthTexture( void *, ShadowHandle_t h, ITexture *pTexture, unsigned char stencil )
{
	Call( "sm.SetFlashlightDepthTexture " + std::to_string( h ) + " " + TexName( pTexture ) + " " + std::to_string( stencil ) );
}
void Rec_SetFlashlightRenderState( void *, ShadowHandle_t h ) { Call( "sm.SetFlashlightRenderState " + std::to_string( h ) ); }

struct ShadowMgrTable : FakeTable
{
	ShadowMgrTable()
	{
		Put( &IShadowMgr::DestroyShadow, (void *)&Rec_DestroyShadow );
		Put( &IShadowMgr::SetShadowMaterial, (void *)&Rec_SetShadowMaterial );
		Put( &IShadowMgr::ProjectShadow, (void *)&Rec_ProjectShadow );
		Put( &IShadowMgr::ProjectFlashlight, (void *)&Rec_ProjectFlashlight );
		Put( &IShadowMgr::GetInfo, (void *)&Rec_GetInfo );
		Put( &IShadowMgr::GetFlashlightFrustum, (void *)&Rec_GetFlashlightFrustum );
		Put( &IShadowMgr::AddShadowToBrushModel, (void *)&Rec_AddShadowToBrushModel );
		Put( &IShadowMgr::RemoveAllShadowsFromBrushModel, (void *)&Rec_RemoveAllShadowsFromBrushModel );
		Put( &IShadowMgr::SetShadowTexCoord, (void *)&Rec_SetShadowTexCoord );
		Put( &IShadowMgr::AddShadowToModel, (void *)&Rec_AddShadowToModel );
		Put( &IShadowMgr::RemoveAllShadowsFromModel, (void *)&Rec_RemoveAllShadowsFromModel );
		Put( &IShadowMgr::ClearExtraClipPlanes, (void *)&Rec_ClearExtraClipPlanes );
		Put( &IShadowMgr::AddExtraClipPlane, (void *)&Rec_AddExtraClipPlane );
		Put( &IShadowMgr::EnableShadow, (void *)&Rec_EnableShadow );
		Put( &IShadowMgr::SetFalloffBias, (void *)&Rec_SetFalloffBias );
		Put( &IShadowMgr::UpdateFlashlightState, (void *)&Rec_UpdateFlashlightState );
		Put( &IShadowMgr::AddFlashlightRenderable, (void *)&Rec_AddFlashlightRenderable );
		Put( &IShadowMgr::CreateShadowEx, (void *)&Rec_CreateShadowEx );
		Put( &IShadowMgr::SetFlashlightDepthTexture, (void *)&Rec_SetFlashlightDepthTexture );
		Put( &IShadowMgr::SetFlashlightRenderState, (void *)&Rec_SetFlashlightRenderState );
	}
};

void Rec_AddShadowToStaticProp( void *, unsigned short h, IClientRenderable *p )
{
	Call( "sp.AddShadowToStaticProp " + std::to_string( h ) + " " + RName( p ) );
}
void Rec_RemoveAllShadowsFromStaticProp( void *, IClientRenderable *p ) { Call( "sp.RemoveAllShadowsFromStaticProp " + RName( p ) ); }

struct StaticPropTable : FakeTable
{
	StaticPropTable()
	{
		Put( &IStaticPropMgrClient::AddShadowToStaticProp, (void *)&Rec_AddShadowToStaticProp );
		Put( &IStaticPropMgrClient::RemoveAllShadowsFromStaticProp, (void *)&Rec_RemoveAllShadowsFromStaticProp );
	}
};

//-----------------------------------------------------------------------------
// The material system, its render context and hardware config.
//-----------------------------------------------------------------------------
int g_nGetContext = 0;
int g_nRelease = 0;
bool g_bDepthTextures = true;
int g_nDepthFormat = 13;
int g_nNullFormat = 42;
int g_nDxLevel = 95;
MaterialFogMode_t g_fogMode = MATERIAL_FOG_LINEAR_BELOW_FOG_Z;
MaterialHeightClipMode_t g_heightClipMode = MATERIAL_HEIGHTCLIPMODE_RENDER_BELOW_HEIGHT;

int Rec_Release( void * )
{
	++g_nRelease;
	Call( "ctx.Release" );
	return 0;
}
void Rec_FogMode( void *, MaterialFogMode_t mode ) { Call( "ctx.FogMode " + std::to_string( (int)mode ) ); }
void Rec_SetHeightClipMode( void *, MaterialHeightClipMode_t mode ) { Call( "ctx.SetHeightClipMode " + std::to_string( (int)mode ) ); }
void Rec_ClearColor4ub( void *, unsigned char r, unsigned char g, unsigned char b, unsigned char a )
{
	Call( "ctx.ClearColor4ub " + std::to_string( r ) + " " + std::to_string( g ) + " " + std::to_string( b ) + " " + std::to_string( a ) );
}
void Rec_ClearColor3ub( void *, unsigned char r, unsigned char g, unsigned char b )
{
	Call( "ctx.ClearColor3ub " + std::to_string( r ) + " " + std::to_string( g ) + " " + std::to_string( b ) );
}
void Rec_MatrixMode( void *, MaterialMatrixMode_t mode ) { Call( "ctx.MatrixMode " + std::to_string( (int)mode ) ); }
void Rec_PushMatrix( void * ) { Call( "ctx.PushMatrix" ); }
void Rec_PopMatrix( void * ) { Call( "ctx.PopMatrix" ); }
void Rec_LoadIdentity( void * ) { Call( "ctx.LoadIdentity" ); }
void Rec_LoadMatrix( void *, const VMatrix &m ) { Call( "ctx.LoadMatrix " + Mat( m ) ); }
void Rec_Scale( void *, float x, float y, float z ) { Call( "ctx.Scale " + Fl( x ) + " " + Fl( y ) + " " + Fl( z ) ); }
void Rec_Ortho( void *, double l, double t, double r, double b, double zn, double zf )
{
	Call( "ctx.Ortho " + Db( l ) + " " + Db( t ) + " " + Db( r ) + " " + Db( b ) + " " + Db( zn ) + " " + Db( zf ) );
}
void Rec_PushRenderTargetAndViewport( void *, ITexture *p ) { Call( "ctx.PushRenderTargetAndViewport " + TexName( p ) ); }
void Rec_PopRenderTargetAndViewport( void * ) { Call( "ctx.PopRenderTargetAndViewport" ); }
void Rec_ClearBuffers( void *, bool c, bool d, bool s )
{
	Call( "ctx.ClearBuffers " + std::to_string( c ) + " " + std::to_string( d ) + " " + std::to_string( s ) );
}
void Rec_Viewport( void *, int x, int y, int w, int h )
{
	Call( "ctx.Viewport " + std::to_string( x ) + " " + std::to_string( y ) + " " + std::to_string( w ) + " " + std::to_string( h ) );
}
void Rec_SetShadowDepthBiasFactors( void *, float slope, float bias ) { Call( "ctx.SetShadowDepthBiasFactors " + Fl( slope ) + " " + Fl( bias ) ); }
float Rec_ComputePixelDiameterOfSphere( void *, const Vector &center, float radius )
{
	Call( "ctx.ComputePixelDiameterOfSphere " + Vec( center ) + " " + Fl( radius ) );
	return radius * 2.0f + center.x;
}
MaterialFogMode_t Rec_GetFogMode( void * )
{
	Call( "ctx.GetFogMode" );
	return g_fogMode;
}
MaterialHeightClipMode_t Rec_GetHeightClipMode( void * )
{
	Call( "ctx.GetHeightClipMode" );
	return g_heightClipMode;
}

struct ContextTable : FakeTable
{
	ContextTable()
	{
		Put( &IMatRenderContext::Release, (void *)&Rec_Release );
		Put( &IMatRenderContext::FogMode, (void *)&Rec_FogMode );
		Put( &IMatRenderContext::SetHeightClipMode, (void *)&Rec_SetHeightClipMode );
		Put( &IMatRenderContext::ClearColor4ub, (void *)&Rec_ClearColor4ub );
		Put( &IMatRenderContext::ClearColor3ub, (void *)&Rec_ClearColor3ub );
		Put( &IMatRenderContext::MatrixMode, (void *)&Rec_MatrixMode );
		Put( &IMatRenderContext::PushMatrix, (void *)&Rec_PushMatrix );
		Put( &IMatRenderContext::PopMatrix, (void *)&Rec_PopMatrix );
		Put( &IMatRenderContext::LoadIdentity, (void *)&Rec_LoadIdentity );
		Put( static_cast< void ( IMatRenderContext::* )( const VMatrix & ) >( &IMatRenderContext::LoadMatrix ), (void *)&Rec_LoadMatrix );
		Put( &IMatRenderContext::Scale, (void *)&Rec_Scale );
		Put( &IMatRenderContext::Ortho, (void *)&Rec_Ortho );
		Put( static_cast< void ( IMatRenderContext::* )( ITexture * ) >( &IMatRenderContext::PushRenderTargetAndViewport ),
		     (void *)&Rec_PushRenderTargetAndViewport );
		Put( &IMatRenderContext::PopRenderTargetAndViewport, (void *)&Rec_PopRenderTargetAndViewport );
		Put( &IMatRenderContext::ClearBuffers, (void *)&Rec_ClearBuffers );
		Put( &IMatRenderContext::Viewport, (void *)&Rec_Viewport );
		Put( &IMatRenderContext::SetShadowDepthBiasFactors, (void *)&Rec_SetShadowDepthBiasFactors );
		Put( &IMatRenderContext::ComputePixelDiameterOfSphere, (void *)&Rec_ComputePixelDiameterOfSphere );
		Put( &IMatRenderContext::GetFogMode, (void *)&Rec_GetFogMode );
		Put( &IMatRenderContext::GetHeightClipMode, (void *)&Rec_GetHeightClipMode );
	}
};
ContextTable g_contextTable;
FakeObject g_context = { g_contextTable.slots };

IMatRenderContext *Rec_GetRenderContext( void * )
{
	++g_nGetContext;
	Call( "ms.GetRenderContext" );
	return reinterpret_cast< IMatRenderContext * >( &g_context );
}
bool Rec_SupportsShadowDepthTextures( void * ) { return g_bDepthTextures; }
ImageFormat Rec_GetShadowDepthTextureFormat( void * ) { return (ImageFormat)g_nDepthFormat; }
ImageFormat Rec_GetNullTextureFormat( void * ) { return (ImageFormat)g_nNullFormat; }
void Rec_BeginRenderTargetAllocation( void * ) { Call( "ms.BeginRenderTargetAllocation" ); }
void Rec_EndRenderTargetAllocation( void * ) { Call( "ms.EndRenderTargetAllocation" ); }
void Rec_UncacheUnusedMaterials( void *, bool bRecompute ) { Call( "ms.UncacheUnusedMaterials " + std::to_string( bRecompute ) ); }

struct MaterialSystemTable : FakeTable
{
	MaterialSystemTable()
	{
		Put( &IMaterialSystem::GetRenderContext, (void *)&Rec_GetRenderContext );
		Put( &IMaterialSystem::SupportsShadowDepthTextures, (void *)&Rec_SupportsShadowDepthTextures );
		Put( &IMaterialSystem::GetShadowDepthTextureFormat, (void *)&Rec_GetShadowDepthTextureFormat );
		Put( &IMaterialSystem::GetNullTextureFormat, (void *)&Rec_GetNullTextureFormat );
		Put( &IMaterialSystem::BeginRenderTargetAllocation, (void *)&Rec_BeginRenderTargetAllocation );
		Put( &IMaterialSystem::EndRenderTargetAllocation, (void *)&Rec_EndRenderTargetAllocation );
		Put( &IMaterialSystem::UncacheUnusedMaterials, (void *)&Rec_UncacheUnusedMaterials );
	}
};

int Rec_GetDXSupportLevel( const void * ) { return g_nDxLevel; }
struct HardwareTable : FakeTable
{
	HardwareTable() { Put( &IMaterialSystemHardwareConfig::GetDXSupportLevel, (void *)&Rec_GetDXSupportLevel ); }
};

//-----------------------------------------------------------------------------
// Model render, render view, engine, BSP query, model info, debug overlay.
//-----------------------------------------------------------------------------
matrix3x4_t g_bones[ 4 ];
bool g_bSetupAnswersNoBones = false;

matrix3x4_t *Rec_DrawModelShadowSetup( void *, IClientRenderable *p, int body, int skin, DrawModelInfo_t *pInfo, matrix3x4_t *pCustom )
{
	Call( "mr.DrawModelShadowSetup " + RName( p ) + " " + std::to_string( body ) + " " + std::to_string( skin ) + " " + Hex( pCustom ) );
	pInfo->m_Body = body;
	pInfo->m_Skin = skin;
	pInfo->m_Lod = 77;
	return g_bSetupAnswersNoBones ? nullptr : g_bones;
}
void Rec_DrawModelShadow( void *, IClientRenderable *p, const DrawModelInfo_t &info, matrix3x4_t *pBones )
{
	Call( "mr.DrawModelShadow " + RName( p ) + " " + std::to_string( info.m_Body ) + " " + std::to_string( info.m_Skin ) + " " +
	      std::to_string( info.m_Lod ) + " " + ( pBones == g_bones ? "bones" : Hex( pBones ) ) );
}
struct ModelRenderTable : FakeTable
{
	ModelRenderTable()
	{
		Put( &IVModelRender::DrawModelShadowSetup, (void *)&Rec_DrawModelShadowSetup );
		Put( &IVModelRender::DrawModelShadow, (void *)&Rec_DrawModelShadow );
	}
};

void Rec_DrawBrushModelShadow( void *, IClientRenderable *p ) { Call( "rv.DrawBrushModelShadow " + RName( p ) ); }
struct RenderViewTable : FakeTable
{
	RenderViewTable() { Put( &IVRenderView::DrawBrushModelShadow, (void *)&Rec_DrawBrushModelShadow ); }
};

// The BSP query: a box touches the leaves from its low x to its high x, in
// whole tens; a ray touches the three leaves named by its start's x, y, z.
bool Rec_EnumerateLeavesInBox( void *, Vector const &mins, Vector const &maxs, ISpatialLeafEnumerator *pEnum, intp context )
{
	Call( "q.EnumerateLeavesInBox " + Vec( mins ) + " " + Vec( maxs ) + " " + std::to_string( (long long)context ) );
	for ( int leaf = (int)( mins.x / 10 ); leaf <= (int)( maxs.x / 10 ); leaf++ )
		if ( !pEnum->EnumerateLeaf( leaf, context ) )
			return false;
	return true;
}
bool Rec_EnumerateLeavesAlongRay( void *, Ray_t const &ray, ISpatialLeafEnumerator *pEnum, intp context )
{
	Call( "q.EnumerateLeavesAlongRay " + Vec( ray.m_Start ) + " " + Vec( ray.m_Delta ) + " " + Vec( ray.m_StartOffset ) + " " +
	      Vec( ray.m_Extents ) + " " + std::to_string( ray.m_IsRay ) + " " + std::to_string( ray.m_IsSwept ) + " " +
	      std::to_string( (long long)context ) );
	pEnum->EnumerateLeaf( (int)ray.m_Start.x, context );
	pEnum->EnumerateLeaf( (int)ray.m_Start.y, context );
	pEnum->EnumerateLeaf( (int)ray.m_Start.z, context );
	return true;
}
struct QueryTable : FakeTable
{
	QueryTable()
	{
		Put( &ISpatialQuery::EnumerateLeavesInBox, (void *)&Rec_EnumerateLeavesInBox );
		Put( &ISpatialQuery::EnumerateLeavesAlongRay, (void *)&Rec_EnumerateLeavesAlongRay );
	}
};
QueryTable g_queryTable;
FakeObject g_query = { g_queryTable.slots };

bool Rec_CullBox( void *, const Vector &mins, const Vector &maxs )
{
	Call( "eng.CullBox " + Vec( mins ) + " " + Vec( maxs ) );
	return mins.x >= 100;
}
void Rec_GetAmbientLightColor( void *, Vector &color )
{
	Call( "eng.GetAmbientLightColor" );
	color.Init( 0.125f, 0.25f, 0.5f );
}
ISpatialQuery *Rec_GetBSPTreeQuery( void * ) { return reinterpret_cast< ISpatialQuery * >( &g_query ); }
struct EngineTable : FakeTable
{
	EngineTable()
	{
		Put( &IVEngineClient::CullBox, (void *)&Rec_CullBox );
		Put( &IVEngineClient::GetAmbientLightColor, (void *)&Rec_GetAmbientLightColor );
		Put( &IVEngineClient::GetBSPTreeQuery, (void *)&Rec_GetBSPTreeQuery );
	}
};

int Rec_GetModelType( const void *, const model_t *model )
{
	Call( "mi.GetModelType " + Hex( model ) );
	return (int)( (uintptr_t)model & 0xF );
}
struct ModelInfoTable : FakeTable
{
	ModelInfoTable() { Put( &IVModelInfoClient::GetModelType, (void *)&Rec_GetModelType ); }
};

void Rec_AddLineOverlay( void *, const Vector &origin, const Vector &dest, int r, int g, int b, bool noDepthTest, float duration )
{
	Call( "ov.AddLineOverlay " + Vec( origin ) + " " + Vec( dest ) + " " + std::to_string( r ) + " " + std::to_string( g ) + " " +
	      std::to_string( b ) + " " + std::to_string( noDepthTest ) + " " + Fl( duration ) );
}
void Rec_AddTextOverlay( void *, const Vector &origin, float duration, const char *pszFormat, ... )
{
	char sz[ 512 ];
	va_list ap;
	va_start( ap, pszFormat );
	vsnprintf( sz, sizeof( sz ), pszFormat, ap );
	va_end( ap );
	Call( "ov.AddTextOverlay " + Vec( origin ) + " " + Fl( duration ) + " [" + pszFormat + "] " + sz );
}
struct OverlayTable : FakeTable
{
	OverlayTable()
	{
		Put( &IVDebugOverlay::AddLineOverlay, (void *)&Rec_AddLineOverlay );
		Put( static_cast< void ( IVDebugOverlay::* )( const Vector &, float, const char *, ... ) >( &IVDebugOverlay::AddTextOverlay ),
		     (void *)&Rec_AddTextOverlay );
	}
};

ShadowMgrTable g_shadowMgrTable;
StaticPropTable g_staticPropTable;
MaterialSystemTable g_materialSystemTable;
HardwareTable g_hardwareTable;
ModelRenderTable g_modelRenderTable;
RenderViewTable g_renderViewTable;
EngineTable g_engineTable;
ModelInfoTable g_modelInfoTable;
OverlayTable g_overlayTable;

FakeObject g_shadowMgrObject = { g_shadowMgrTable.slots };
FakeObject g_staticPropObject = { g_staticPropTable.slots };
FakeObject g_materialSystemObject = { g_materialSystemTable.slots };
FakeObject g_hardwareObject = { g_hardwareTable.slots };
FakeObject g_modelRenderObject = { g_modelRenderTable.slots };
FakeObject g_renderViewObject = { g_renderViewTable.slots };
FakeObject g_engineObject = { g_engineTable.slots };
FakeObject g_modelInfoObject = { g_modelInfoTable.slots };
FakeObject g_overlayObject = { g_overlayTable.slots };

IShadowMgr *g_pShadowMgr = reinterpret_cast< IShadowMgr * >( &g_shadowMgrObject );
IStaticPropMgrClient *g_pStaticProps = reinterpret_cast< IStaticPropMgrClient * >( &g_staticPropObject );
IMaterialSystem *g_pMaterials = reinterpret_cast< IMaterialSystem * >( &g_materialSystemObject );
IMaterialSystemHardwareConfig *g_pHardware = reinterpret_cast< IMaterialSystemHardwareConfig * >( &g_hardwareObject );
IVModelRender *g_pModelRender = reinterpret_cast< IVModelRender * >( &g_modelRenderObject );
IVRenderView *g_pRenderView = reinterpret_cast< IVRenderView * >( &g_renderViewObject );
IVEngineClient *g_pEngine = reinterpret_cast< IVEngineClient * >( &g_engineObject );
IVModelInfoClient *g_pModelInfo = reinterpret_cast< IVModelInfoClient * >( &g_modelInfoObject );
IVDebugOverlay *g_pOverlay = reinterpret_cast< IVDebugOverlay * >( &g_overlayObject );
CGlobalVarsBase g_globals( false );
CGlobalVarsBase *g_pGlobals = &g_globals;

//-----------------------------------------------------------------------------
// client.so's functions, as recorders.
//-----------------------------------------------------------------------------
int g_nToolsMode = 0;
FakeRenderable g_entityRenderable = Make( 70 );
const uint32_t kKnownEntity = 0x00230011u;

int32_t Env_InitMaterial( const char *pszName, const char *pszGroup )
{
	Call( std::string( "env.InitMaterial " ) + ( pszName ? pszName : "(null)" ) + "|" + ( pszGroup ? pszGroup : "(null)" ) );
	return 17;
}
void Env_ShutdownMaterial( int32_t id ) { Call( "env.ShutdownMaterial " + std::to_string( id ) ); }
IMaterial *Env_Material( int32_t id ) { return MaterialOf( id ); }
int32_t Env_InitRenderTarget( const char *pszName, int32_t w, int32_t h, int32_t sizeMode, int32_t format, int32_t depth, int32_t hdr )
{
	Call( std::string( "env.InitRenderTarget " ) + ( pszName ? pszName : "(null)" ) + " " + std::to_string( w ) + " " + std::to_string( h ) + " " +
	      std::to_string( sizeMode ) + " " + std::to_string( format ) + " " + std::to_string( depth ) + " " + std::to_string( hdr ) );
	return 23;
}
void Env_ShutdownTexture( int32_t id ) { Call( "env.ShutdownTexture " + std::to_string( id ) ); }
ITexture *Env_Texture( int32_t id ) { return TextureOf( id ); }
ITexture *Env_FlashlightTexture( int32_t index )
{
	Call( "env.FlashlightTexture " + std::to_string( index ) );
	return TextureOf( index );
}
void Env_AddRestoreFunc() { Call( "env.AddRestoreFunc" ); }
void Env_RemoveRestoreFunc() { Call( "env.RemoveRestoreFunc" ); }
int32_t Env_ToolsMode() { return g_nToolsMode; }
IClientRenderable *Env_RenderableOf( uint32_t hEntity )
{
	Call( "env.RenderableOf " + std::to_string( hEntity ) );
	return hEntity == kKnownEntity ? AsRenderable( g_entityRenderable ) : nullptr;
}
void Env_UpdateShadowDepthTexture( ITexture *pRenderTarget, ITexture *pDepth, const CViewSetup &v )
{
	Call( "env.UpdateShadowDepthTexture " + TexName( pRenderTarget ) + " " + TexName( pDepth ) + " " + std::to_string( v.x ) + " " +
	      std::to_string( v.y ) + " " + std::to_string( v.width ) + " " + std::to_string( v.height ) + " " + Fl( v.fov ) + " " +
	      Fl( v.fovViewmodel ) + " " + Vec( v.origin ) + " " + Ang( v.angles ) + " " + Fl( v.zNear ) + " " + Fl( v.zFar ) + " " +
	      Fl( v.zNearViewmodel ) + " " + Fl( v.zFarViewmodel ) + " " + Fl( v.m_flAspectRatio ) + " " + std::to_string( v.m_bOrtho ) + " " +
	      std::to_string( v.m_bDoBloomAndToneMapping ) );
}
void Env_DrawTextureQuad( IMaterial *pMaterial, float w, float h ) { Call( "env.DrawTextureQuad " + MatName( pMaterial ) + " " + Fl( w ) + " " + Fl( h ) ); }

SSShadowEnv FullEnv()
{
	SSShadowEnv env;
	memset( &env, 0, sizeof( env ) );
	env.ppShadowMgr = &g_pShadowMgr;
	env.ppStaticPropMgr = &g_pStaticProps;
	env.ppMaterials = &g_pMaterials;
	env.ppHardwareConfig = &g_pHardware;
	env.ppModelRender = &g_pModelRender;
	env.ppRender = &g_pRenderView;
	env.ppEngine = &g_pEngine;
	env.ppModelInfo = &g_pModelInfo;
	env.ppDebugOverlay = &g_pOverlay;
	env.ppGlobals = &g_pGlobals;
	env.pfnInitMaterial = Env_InitMaterial;
	env.pfnShutdownMaterial = Env_ShutdownMaterial;
	env.pfnMaterial = Env_Material;
	env.pfnInitRenderTarget = Env_InitRenderTarget;
	env.pfnShutdownTexture = Env_ShutdownTexture;
	env.pfnTexture = Env_Texture;
	env.pfnFlashlightTexture = Env_FlashlightTexture;
	env.pfnAddRestoreFunc = Env_AddRestoreFunc;
	env.pfnRemoveRestoreFunc = Env_RemoveRestoreFunc;
	env.pfnToolsMode = Env_ToolsMode;
	env.pfnRenderableOf = Env_RenderableOf;
	env.pfnUpdateShadowDepthTexture = Env_UpdateShadowDepthTexture;
	env.pfnDrawTextureQuad = Env_DrawTextureQuad;
	return env;
}

//-----------------------------------------------------------------------------
// The records, encoded as fwd_shadows.h documents them.
//-----------------------------------------------------------------------------
int32_t Bits( float f )
{
	int32_t i;
	memcpy( &i, &f, sizeof( i ) );
	return i;
}

float FloatOf( int32_t w )
{
	float f;
	memcpy( &f, &w, sizeof( f ) );
	return f;
}

struct Ops
{
	std::vector< int32_t > w;
	size_t rec = 0;
	bool open = false;

	Ops &K( int32_t kind )
	{
		Close();
		rec = w.size();
		w.push_back( kind );
		w.push_back( 0 );
		open = true;
		return *this;
	}
	Ops &I( int32_t v )
	{
		w.push_back( v );
		return *this;
	}
	Ops &F( float f ) { return I( Bits( f ) ); }
	Ops &P( const void *p )
	{
		const uint64_t v = (uint64_t)(uintptr_t)p;
		I( (int32_t)(uint32_t)( v & 0xFFFFFFFFu ) );
		return I( (int32_t)(uint32_t)( v >> 32 ) );
	}
	Ops &V( float x, float y, float z ) { return F( x ).F( y ).F( z ); }
	// A matrix: row by row, element (r, c) = base + 4r + c.
	Ops &M( float base )
	{
		for ( int i = 0; i < 16; i++ )
			F( base + i );
		return *this;
	}
	Ops &D( double d )
	{
		int32_t two[ 2 ];
		memcpy( two, &d, sizeof( two ) );
		return I( two[ 0 ] ).I( two[ 1 ] );
	}
	// A string: its byte length, then its bytes with a NUL, padded to a word.
	Ops &S( const char *psz )
	{
		const int32_t n = (int32_t)strlen( psz );
		I( n );
		const int32_t nWords = ( n + 4 ) / 4;
		std::vector< char > bytes( (size_t)nWords * 4, 0 );
		memcpy( bytes.data(), psz, (size_t)n );
		for ( int32_t i = 0; i < nWords; i++ )
		{
			int32_t word;
			memcpy( &word, bytes.data() + 4 * i, sizeof( word ) );
			I( word );
		}
		return *this;
	}
	// Leaves: a count, then the leaves.
	Ops &L( const std::vector< int > &leaves )
	{
		I( (int32_t)leaves.size() );
		for ( int leaf : leaves )
			I( leaf );
		return *this;
	}
	void Close()
	{
		if ( open )
			w[ rec + 1 ] = (int32_t)( w.size() - rec - 2 );
		open = false;
	}
	const std::vector< int32_t > &Done()
	{
		Close();
		return w;
	}
};

const int32_t kSentinel = 0x5EEDF00D;

std::string MatString( float base )
{
	VMatrix m;
	for ( int r = 0; r < 4; r++ )
		for ( int c = 0; c < 4; c++ )
			m.m[ r ][ c ] = base + 4 * r + c;
	return Mat( m );
}

// Words of a struct as they lie, the way the answer writes them.
std::vector< int32_t > WordsOf( const void *pv, size_t nBytes )
{
	std::vector< int32_t > out( ( nBytes + 3 ) / 4, 0 );
	memcpy( out.data(), pv, nBytes );
	return out;
}

class FwdShadows : public ::testing::Test
{
protected:
	SSShadowEnv env = FullEnv();
	std::vector< int32_t > answers;

	void SetUp() override
	{
		g_calls.clear();
		g_nGetContext = 0;
		g_nRelease = 0;
		g_bSetupAnswersNoBones = false;
		g_nToolsMode = 0;
		answers.assign( 512, kSentinel );
	}

	int32_t Run( Ops &ops, int32_t nAnswerWords = 512 )
	{
		const std::vector< int32_t > &w = ops.Done();
		return SSFwd_Shadows_Run( &env, w.data(), (int32_t)w.size(), answers.data(), nAnswerWords );
	}

	// One call record's run: it answers nothing.
	std::vector< std::string > RunCall( Ops &ops )
	{
		EXPECT_EQ( 0, Run( ops ) );
		EXPECT_EQ( kSentinel, answers[ 0 ] ) << "a call record wrote an answer";
		return g_calls;
	}

	float A( int i ) const { return FloatOf( answers[ (size_t)i ] ); }

	uintptr_t PtrAt( int i ) const { return (uintptr_t)( (uint64_t)(uint32_t)answers[ (size_t)i ] | ( (uint64_t)(uint32_t)answers[ (size_t)i + 1 ] << 32 ) ); }
};

typedef std::vector< std::string > Calls;

// A pointer with both halves set, so a swapped pair reads as a different pointer.
const void *const kProxy = reinterpret_cast< const void * >( (uintptr_t)0x0000123487654321ull );
model_t *const kModel = reinterpret_cast< model_t * >( (uintptr_t)0x00007FFE0000BEE0ull );

} // namespace

//=============================================================================
// Calls: each reaches the one engine method its kind names.
//=============================================================================

TEST_F( FwdShadows, DestroyShadow )
{
	Ops o;
	o.K( SS_SHADOW_DestroyShadow ).I( 7 );
	EXPECT_EQ( Calls{ "sm.DestroyShadow 7" }, RunCall( o ) );
}

TEST_F( FwdShadows, SetShadowMaterial )
{
	Ops o;
	o.K( SS_SHADOW_SetShadowMaterial ).I( 7 ).I( 1 ).I( 2 ).P( kProxy );
	o.K( SS_SHADOW_SetShadowMaterial ).I( 8 ).I( 0 ).I( 3 ).P( nullptr );
	EXPECT_EQ( ( Calls{ "sm.SetShadowMaterial 7 M1 M2 0x123487654321", "sm.SetShadowMaterial 8 null M3 0x0" } ), RunCall( o ) );
}

TEST_F( FwdShadows, ProjectShadow )
{
	Ops o;
	o.K( SS_SHADOW_ProjectShadow ).I( 9 ).V( 1, 2, 3 ).V( 0, 0, -1 ).M( 0.5f ).F( 16 ).F( 24 ).F( 300 ).F( 10 ).F( 0.75f ).V( 4, 5, 6 ).L( { 3, 1, 4 } );
	EXPECT_EQ( Calls{ "sm.ProjectShadow 9 (1 2 3) (0 0 -1) " + MatString( 0.5f ) + " (16 24) 3:3,1,4 300 10 0.75 (4 5 6)" }, RunCall( o ) );
}

TEST_F( FwdShadows, ProjectFlashlight )
{
	Ops o;
	o.K( SS_SHADOW_ProjectFlashlight ).I( 4 ).M( -2 ).L( { 8, 9 } );
	o.K( SS_SHADOW_ProjectFlashlight ).I( 5 ).M( 1 ).L( {} );
	EXPECT_EQ( ( Calls{ "sm.ProjectFlashlight 4 " + MatString( -2 ) + " 2:8,9", "sm.ProjectFlashlight 5 " + MatString( 1 ) + " 0:" } ),
	           RunCall( o ) );
}

TEST_F( FwdShadows, AddShadowToBrushModel )
{
	Ops o;
	o.K( SS_SHADOW_AddShadowToBrushModel ).I( 3 ).P( kModel ).V( 1, 2, 3 ).V( 10, 20, 30 );
	o.K( SS_SHADOW_AddShadowToBrushModel ).I( 4 ).P( nullptr ).V( 1, 2, 3 ).V( 10, 20, 30 );   // no model: no call
	EXPECT_EQ( Calls{ "sm.AddShadowToBrushModel 3 0x7ffe0000bee0 (1 2 3) (10 20 30)" }, RunCall( o ) );
}

TEST_F( FwdShadows, RemoveAllShadowsFromBrushModel )
{
	Ops o;
	o.K( SS_SHADOW_RemoveAllShadowsFromBrushModel ).P( kModel );
	o.K( SS_SHADOW_RemoveAllShadowsFromBrushModel ).P( nullptr );
	EXPECT_EQ( Calls{ "sm.RemoveAllShadowsFromBrushModel 0x7ffe0000bee0" }, RunCall( o ) );
}

TEST_F( FwdShadows, SetShadowTexCoord )
{
	Ops o;
	o.K( SS_SHADOW_SetShadowTexCoord ).I( 2 ).F( 0.125f ).F( 0.25f ).F( 0.5f ).F( 0.0625f );
	EXPECT_EQ( Calls{ "sm.SetShadowTexCoord 2 0.125 0.25 0.5 0.0625" }, RunCall( o ) );
}

TEST_F( FwdShadows, AddShadowToModel )
{
	Ops o;
	o.K( SS_SHADOW_AddShadowToModel ).I( 2 ).I( 4000 );
	EXPECT_EQ( Calls{ "sm.AddShadowToModel 2 4000" }, RunCall( o ) );
}

TEST_F( FwdShadows, RemoveAllShadowsFromModel )
{
	Ops o;
	o.K( SS_SHADOW_RemoveAllShadowsFromModel ).I( 4001 );
	EXPECT_EQ( Calls{ "sm.RemoveAllShadowsFromModel 4001" }, RunCall( o ) );
}

TEST_F( FwdShadows, ClearExtraClipPlanes )
{
	Ops o;
	o.K( SS_SHADOW_ClearExtraClipPlanes ).I( 11 );
	EXPECT_EQ( Calls{ "sm.ClearExtraClipPlanes 11" }, RunCall( o ) );
}

TEST_F( FwdShadows, AddExtraClipPlane )
{
	Ops o;
	o.K( SS_SHADOW_AddExtraClipPlane ).I( 11 ).V( 0, 1, 0 ).F( -64.5f );
	EXPECT_EQ( Calls{ "sm.AddExtraClipPlane 11 (0 1 0) -64.5" }, RunCall( o ) );
}

TEST_F( FwdShadows, EnableShadow )
{
	Ops o;
	o.K( SS_SHADOW_EnableShadow ).I( 6 ).I( 1 );
	o.K( SS_SHADOW_EnableShadow ).I( 6 ).I( 0 );
	o.K( SS_SHADOW_EnableShadow ).I( 6 ).I( 2 );   // any non-zero word is true
	EXPECT_EQ( ( Calls{ "sm.EnableShadow 6 1", "sm.EnableShadow 6 0", "sm.EnableShadow 6 1" } ), RunCall( o ) );
}

TEST_F( FwdShadows, SetFalloffBias )
{
	Ops o;
	o.K( SS_SHADOW_SetFalloffBias ).I( 6 ).I( 200 );
	EXPECT_EQ( Calls{ "sm.SetFalloffBias 6 200" }, RunCall( o ) );
}

TEST_F( FwdShadows, UpdateFlashlightState )
{
	SourceSharpFlashlightState w;
	memset( &w, 0, sizeof( w ) );
	w.OriginXYZ[ 0 ] = 1;
	w.OriginXYZ[ 1 ] = 2;
	w.OriginXYZ[ 2 ] = 3;
	w.Orientation[ 0 ] = 0.5f;
	w.Orientation[ 1 ] = -0.5f;
	w.Orientation[ 2 ] = 0.25f;
	w.Orientation[ 3 ] = 0.75f;
	w.NearZ = 4;
	w.FarZ = 750;
	w.HorizontalFovDegrees = 45;
	w.VerticalFovDegrees = 30;
	w.QuadraticAtten = 0.1f;
	w.LinearAtten = 0.2f;
	w.ConstantAtten = 0.3f;
	w.Color[ 0 ] = 1;
	w.Color[ 1 ] = 0.5f;
	w.Color[ 2 ] = 0.25f;
	w.Color[ 3 ] = 0.125f;
	w.Texture = 5;
	w.TextureFrame = 2;
	w.EnableShadows = 1;
	w.ShadowMapResolution = 2048;
	w.ShadowAtten = 0.35f;
	w.ShadowSlopeScaleDepthBias = 4;
	w.ShadowDepthBias = 0.0002f;
	ASSERT_EQ( 25u * 4u, sizeof( w ) );
	int32_t words[ 25 ];
	memcpy( words, &w, sizeof( words ) );

	Ops o;
	o.K( SS_SHADOW_UpdateFlashlightState ).I( 12 );
	for ( int32_t word : words )
		o.I( word );
	EXPECT_EQ( ( Calls{ "env.FlashlightTexture 5", "sm.UpdateFlashlightState 12" } ), RunCall( o ) );

	EXPECT_EQ( Vector( 1, 2, 3 ), g_flash.m_vecLightOrigin );
	EXPECT_EQ( 0.5f, g_flash.m_quatOrientation.x );
	EXPECT_EQ( -0.5f, g_flash.m_quatOrientation.y );
	EXPECT_EQ( 0.25f, g_flash.m_quatOrientation.z );
	EXPECT_EQ( 0.75f, g_flash.m_quatOrientation.w );
	EXPECT_EQ( 4.0f, g_flash.m_NearZ );
	EXPECT_EQ( 750.0f, g_flash.m_FarZ );
	EXPECT_EQ( 45.0f, g_flash.m_fHorizontalFOVDegrees );
	EXPECT_EQ( 30.0f, g_flash.m_fVerticalFOVDegrees );
	EXPECT_EQ( 0.1f, g_flash.m_fQuadraticAtten );
	EXPECT_EQ( 0.2f, g_flash.m_fLinearAtten );
	EXPECT_EQ( 0.3f, g_flash.m_fConstantAtten );
	EXPECT_EQ( 1.0f, g_flash.m_Color[ 0 ] );
	EXPECT_EQ( 0.125f, g_flash.m_Color[ 3 ] );
	EXPECT_EQ( TextureOf( 5 ), g_flash.m_pSpotlightTexture );
	EXPECT_EQ( 2, g_flash.m_nSpotlightTextureFrame );
	EXPECT_TRUE( g_flash.m_bEnableShadows );
	EXPECT_EQ( 2048.0f, g_flash.m_flShadowMapResolution );
	EXPECT_EQ( 0.35f, g_flash.m_flShadowAtten );
	EXPECT_EQ( 4.0f, g_flash.m_flShadowSlopeScaleDepthBias );
	EXPECT_EQ( 0.0002f, g_flash.m_flShadowDepthBias );
	// A field the wire does not carry keeps FlashlightState_t's constructor's value.
	EXPECT_EQ( 3.0f, g_flash.m_flShadowFilterSize );
}

TEST_F( FwdShadows, AddFlashlightRenderable )
{
	FakeRenderable r = Make( 3 );
	Ops o;
	o.K( SS_SHADOW_AddFlashlightRenderable ).I( 12 ).P( &r );
	o.K( SS_SHADOW_AddFlashlightRenderable ).I( 12 ).P( nullptr );   // no renderable: no call
	EXPECT_EQ( Calls{ "sm.AddFlashlightRenderable 12 R3" }, RunCall( o ) );
}

TEST_F( FwdShadows, SetFlashlightDepthTexture )
{
	Ops o;
	o.K( SS_SHADOW_SetFlashlightDepthTexture ).I( 12 ).I( 3 ).I( 5 );
	o.K( SS_SHADOW_SetFlashlightDepthTexture ).I( 12 ).I( 0 ).I( 0 );   // no texture: the engine is told so
	EXPECT_EQ( ( Calls{ "sm.SetFlashlightDepthTexture 12 T3 5", "sm.SetFlashlightDepthTexture 12 null 0" } ), RunCall( o ) );
}

TEST_F( FwdShadows, SetFlashlightRenderState )
{
	Ops o;
	o.K( SS_SHADOW_SetFlashlightRenderState ).I( 12 );
	EXPECT_EQ( Calls{ "sm.SetFlashlightRenderState 12" }, RunCall( o ) );
}

TEST_F( FwdShadows, AddShadowToStaticProp )
{
	FakeRenderable r = Make( 4 );
	Ops o;
	o.K( SS_SHADOW_AddShadowToStaticProp ).I( 33 ).P( &r );
	o.K( SS_SHADOW_AddShadowToStaticProp ).I( 33 ).P( nullptr );
	EXPECT_EQ( Calls{ "sp.AddShadowToStaticProp 33 R4" }, RunCall( o ) );
}

TEST_F( FwdShadows, RemoveAllShadowsFromStaticProp )
{
	FakeRenderable r = Make( 4 );
	Ops o;
	o.K( SS_SHADOW_RemoveAllShadowsFromStaticProp ).P( &r );
	o.K( SS_SHADOW_RemoveAllShadowsFromStaticProp ).P( nullptr );
	EXPECT_EQ( Calls{ "sp.RemoveAllShadowsFromStaticProp R4" }, RunCall( o ) );
}

TEST_F( FwdShadows, ShutdownMaterial )
{
	Ops o;
	o.K( SS_SHADOW_ShutdownMaterial ).I( 6 );
	EXPECT_EQ( Calls{ "env.ShutdownMaterial 6" }, RunCall( o ) );
}

TEST_F( FwdShadows, ColorModulate )
{
	Ops o;
	o.K( SS_SHADOW_ColorModulate ).I( 4 ).F( 0.25f ).F( 0.5f ).F( 0.75f );
	o.K( SS_SHADOW_ColorModulate ).I( 0 ).F( 1 ).F( 1 ).F( 1 );   // no material: no call
	EXPECT_EQ( Calls{ "mat.ColorModulate M4 0.25 0.5 0.75" }, RunCall( o ) );
}

TEST_F( FwdShadows, ShutdownTexture )
{
	Ops o;
	o.K( SS_SHADOW_ShutdownTexture ).I( 7 );
	EXPECT_EQ( Calls{ "env.ShutdownTexture 7" }, RunCall( o ) );
}

TEST_F( FwdShadows, BeginRenderTargetAllocation )
{
	Ops o;
	o.K( SS_SHADOW_BeginRenderTargetAllocation );
	EXPECT_EQ( Calls{ "ms.BeginRenderTargetAllocation" }, RunCall( o ) );
}

TEST_F( FwdShadows, EndRenderTargetAllocation )
{
	Ops o;
	o.K( SS_SHADOW_EndRenderTargetAllocation );
	EXPECT_EQ( Calls{ "ms.EndRenderTargetAllocation" }, RunCall( o ) );
}

TEST_F( FwdShadows, UncacheUnusedMaterials )
{
	Ops o;
	o.K( SS_SHADOW_UncacheUnusedMaterials );
	EXPECT_EQ( Calls{ "ms.UncacheUnusedMaterials 0" }, RunCall( o ) );
}

TEST_F( FwdShadows, AddRestoreFunc )
{
	Ops o;
	o.K( SS_SHADOW_AddRestoreFunc );
	EXPECT_EQ( Calls{ "env.AddRestoreFunc" }, RunCall( o ) );
}

TEST_F( FwdShadows, RemoveRestoreFunc )
{
	Ops o;
	o.K( SS_SHADOW_RemoveRestoreFunc );
	EXPECT_EQ( Calls{ "env.RemoveRestoreFunc" }, RunCall( o ) );
}

// The render context's calls: the context is asked for, the call made on it,
// and the context released at the end of the run.
TEST_F( FwdShadows, FogMode )
{
	Ops o;
	o.K( SS_SHADOW_FogMode ).I( MATERIAL_FOG_LINEAR );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.FogMode 1", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, SetHeightClipMode )
{
	Ops o;
	o.K( SS_SHADOW_SetHeightClipMode ).I( MATERIAL_HEIGHTCLIPMODE_RENDER_ABOVE_HEIGHT );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.SetHeightClipMode 1", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, ClearColor4ub )
{
	Ops o;
	o.K( SS_SHADOW_ClearColor4ub ).I( 255 ).I( 128 ).I( 1 ).I( 0 );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.ClearColor4ub 255 128 1 0", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, ClearColor3ub )
{
	Ops o;
	o.K( SS_SHADOW_ClearColor3ub ).I( 9 ).I( 99 ).I( 199 );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.ClearColor3ub 9 99 199", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, MatrixMode )
{
	Ops o;
	o.K( SS_SHADOW_MatrixMode ).I( MATERIAL_PROJECTION );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.MatrixMode " + std::to_string( (int)MATERIAL_PROJECTION ), "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, PushMatrix )
{
	Ops o;
	o.K( SS_SHADOW_PushMatrix );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.PushMatrix", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, PopMatrix )
{
	Ops o;
	o.K( SS_SHADOW_PopMatrix );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.PopMatrix", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, LoadIdentity )
{
	Ops o;
	o.K( SS_SHADOW_LoadIdentity );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.LoadIdentity", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, LoadMatrix )
{
	Ops o;
	o.K( SS_SHADOW_LoadMatrix ).M( 100 );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.LoadMatrix " + MatString( 100 ), "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, Scale )
{
	Ops o;
	o.K( SS_SHADOW_Scale ).F( 1 ).F( -1 ).F( 0.5f );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.Scale 1 -1 0.5", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, Ortho )
{
	// Doubles a float cannot hold, so a narrowing on the way reads as a different number.
	Ops o;
	o.K( SS_SHADOW_Ortho ).D( 0.1 ).D( -1.0000000001 ).D( 1e10 + 0.5 ).D( 3.141592653589793 ).D( -0.0 ).D( 2.0 / 3.0 );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.Ortho " + Db( 0.1 ) + " " + Db( -1.0000000001 ) + " " + Db( 1e10 + 0.5 ) + " " +
	                                             Db( 3.141592653589793 ) + " " + Db( -0.0 ) + " " + Db( 2.0 / 3.0 ),
	                    "ctx.Release" } ),
	           RunCall( o ) );
}

TEST_F( FwdShadows, PushRenderTargetAndViewport )
{
	Ops o;
	o.K( SS_SHADOW_PushRenderTargetAndViewport ).I( 2 );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.PushRenderTargetAndViewport T2", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, PopRenderTargetAndViewport )
{
	Ops o;
	o.K( SS_SHADOW_PopRenderTargetAndViewport );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.PopRenderTargetAndViewport", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, ClearBuffers )
{
	Ops o;
	o.K( SS_SHADOW_ClearBuffers ).I( 1 ).I( 0 ).I( 1 );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.ClearBuffers 1 0 1", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, Viewport )
{
	Ops o;
	o.K( SS_SHADOW_Viewport ).I( 16 ).I( 32 ).I( 128 ).I( 64 );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.Viewport 16 32 128 64", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, SetShadowDepthBiasFactors )
{
	Ops o;
	o.K( SS_SHADOW_SetShadowDepthBiasFactors ).F( 16 ).F( 0.0005f );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.SetShadowDepthBiasFactors 16 0.0005", "ctx.Release" } ), RunCall( o ) );
}

TEST_F( FwdShadows, DrawTextureQuad )
{
	// client.so's quad: no render context is fetched for it here.
	Ops o;
	o.K( SS_SHADOW_DrawTextureQuad ).I( 3 ).F( 512 ).F( 256 );
	EXPECT_EQ( Calls{ "env.DrawTextureQuad M3 512 256" }, RunCall( o ) );
	EXPECT_EQ( 0, g_nGetContext );
}

TEST_F( FwdShadows, DrawBrushModelShadow )
{
	FakeRenderable r = Make( 6 );
	Ops o;
	o.K( SS_SHADOW_DrawBrushModelShadow ).P( &r );
	o.K( SS_SHADOW_DrawBrushModelShadow ).P( nullptr );
	EXPECT_EQ( Calls{ "rv.DrawBrushModelShadow R6" }, RunCall( o ) );
}

TEST_F( FwdShadows, UpdateShadowDepthTexture )
{
	// ShadowDepthView: x, y, w, h, fov, fovViewmodel, origin (3), angles (3),
	// zNear, zFar, zNearViewmodel, zFarViewmodel, aspect, ortho, bloom.
	Ops o;
	o.K( SS_SHADOW_UpdateShadowDepthTexture ).I( 1 ).I( 2 );
	o.I( 0 ).I( 512 ).I( 1024 ).I( 1024 ).F( 90 ).F( 54 ).V( 1, 2, 3 ).V( 10, 20, 30 ).F( 4 ).F( 750 ).F( 1 ).F( 2 ).F( 1.5f ).I( 1 ).I( 0 );
	EXPECT_EQ( 2u + 2u + 19u, o.Done().size() ) << "the record is the kind, the count, two textures and the 19-word view";
	EXPECT_EQ( Calls{ "env.UpdateShadowDepthTexture T1 T2 0 512 1024 1024 90 54 (1 2 3) (10 20 30) 4 750 1 2 1.5 1 0" }, RunCall( o ) );
}

TEST_F( FwdShadows, CreateModelInstance )
{
	FakeRenderable r = Make( 8 );
	Ops o;
	o.K( SS_SHADOW_CreateModelInstance ).P( &r );
	o.K( SS_SHADOW_CreateModelInstance ).P( nullptr );
	EXPECT_EQ( Calls{ "r.CreateModelInstance 8" }, RunCall( o ) );
}

TEST_F( FwdShadows, AddLineOverlay )
{
	Ops o;
	o.K( SS_SHADOW_AddLineOverlay ).V( 1, 2, 3 ).V( 4, 5, 6 ).I( 255 ).I( 0 ).I( 7 ).I( 1 ).F( 0.01f );
	EXPECT_EQ( Calls{ "ov.AddLineOverlay (1 2 3) (4 5 6) 255 0 7 1 0.01" }, RunCall( o ) );
}

TEST_F( FwdShadows, AddTextOverlay )
{
	// The text goes through "%s", so a '%' in it is printed rather than read as
	// a format. The word after the origin is the float overload's duration (stock
	// passes 0, the managed binding its "line offset" as that float).
	Ops o;
	o.K( SS_SHADOW_AddTextOverlay ).V( 1, 2, 3 ).F( 0.5f ).S( "shadow 12 %d%s" );
	o.K( SS_SHADOW_AddTextOverlay ).V( 0, 0, 0 ).F( 0 ).S( "abc" );   // three bytes: one word with its NUL
	EXPECT_EQ( ( Calls{ "ov.AddTextOverlay (1 2 3) 0.5 [%s] shadow 12 %d%s", "ov.AddTextOverlay (0 0 0) 0 [%s] abc" } ), RunCall( o ) );
}

//=============================================================================
// Questions: each writes its answer after the answers before it.
//=============================================================================

TEST_F( FwdShadows, CreateShadowEx )
{
	Ops o;
	o.K( SS_SHADOW_CreateShadowEx ).I( 1 ).I( 2 ).P( kProxy ).I( 9 );
	EXPECT_EQ( 1, Run( o ) );
	EXPECT_EQ( 321, answers[ 0 ] );
	EXPECT_EQ( Calls{ "sm.CreateShadowEx M1 M2 0x123487654321 9" }, g_calls );
}

TEST_F( FwdShadows, GetInfo )
{
	unsigned char *pb = reinterpret_cast< unsigned char * >( &g_info );
	for ( size_t i = 0; i < sizeof( g_info ); i++ )
		pb[ i ] = (unsigned char)( i * 7 + 1 );
	const std::vector< int32_t > expected = WordsOf( &g_info, sizeof( g_info ) );
	Ops o;
	o.K( SS_SHADOW_GetInfo ).I( 5 );
	ASSERT_EQ( (int32_t)expected.size(), Run( o ) );
	for ( size_t i = 0; i < expected.size(); i++ )
		EXPECT_EQ( expected[ i ], answers[ i ] ) << i;
	EXPECT_EQ( kSentinel, answers[ expected.size() ] );
	EXPECT_EQ( Calls{ "sm.GetInfo 5" }, g_calls );
}

TEST_F( FwdShadows, GetFlashlightFrustum )
{
	unsigned char *pb = reinterpret_cast< unsigned char * >( &g_frustum );
	for ( size_t i = 0; i < sizeof( g_frustum ); i++ )
		pb[ i ] = (unsigned char)( i * 13 + 5 );
	const std::vector< int32_t > expected = WordsOf( &g_frustum, sizeof( g_frustum ) );
	EXPECT_EQ( 48u, expected.size() ) << "fwd_shadows.h: Frustum_t (48)";
	Ops o;
	o.K( SS_SHADOW_GetFlashlightFrustum ).I( 6 );
	ASSERT_EQ( (int32_t)expected.size(), Run( o ) );
	for ( size_t i = 0; i < expected.size(); i++ )
		EXPECT_EQ( expected[ i ], answers[ i ] ) << i;
	EXPECT_EQ( Calls{ "sm.GetFlashlightFrustum 6" }, g_calls );
}

TEST_F( FwdShadows, InitMaterial )
{
	Ops o;
	o.K( SS_SHADOW_InitMaterial ).S( "decals/rendershadow" ).S( "Other textures" );
	EXPECT_EQ( 1, Run( o ) );
	EXPECT_EQ( 17, answers[ 0 ] );
	EXPECT_EQ( Calls{ "env.InitMaterial decals/rendershadow|Other textures" }, g_calls );
}

TEST_F( FwdShadows, InitRenderTarget )
{
	Ops o;
	o.K( SS_SHADOW_InitRenderTarget ).S( "_rt_Shadows" ).I( 1024 ).I( 512 ).I( 3 ).I( 12 ).I( 2 ).I( 1 );
	EXPECT_EQ( 1, Run( o ) );
	EXPECT_EQ( 23, answers[ 0 ] );
	EXPECT_EQ( Calls{ "env.InitRenderTarget _rt_Shadows 1024 512 3 12 2 1" }, g_calls );
}

TEST_F( FwdShadows, TextureSize )
{
	Ops o;
	o.K( SS_SHADOW_TextureSize ).I( 3 );
	o.K( SS_SHADOW_TextureSize ).I( 0 );   // no texture: 0 by 0
	EXPECT_EQ( 4, Run( o ) );
	EXPECT_EQ( 192, answers[ 0 ] );
	EXPECT_EQ( 97, answers[ 1 ] );
	EXPECT_EQ( 0, answers[ 2 ] );
	EXPECT_EQ( 0, answers[ 3 ] );
}

TEST_F( FwdShadows, Hardware )
{
	g_bDepthTextures = true;
	g_nDepthFormat = 13;
	g_nNullFormat = 42;
	g_nDxLevel = 95;
	g_nToolsMode = 1;
	Ops o;
	o.K( SS_SHADOW_Hardware );
	EXPECT_EQ( 5, Run( o ) );
	EXPECT_EQ( 1, answers[ 0 ] );
	EXPECT_EQ( 13, answers[ 1 ] );
	EXPECT_EQ( 42, answers[ 2 ] );
	EXPECT_EQ( 95, answers[ 3 ] );
	EXPECT_EQ( 1, answers[ 4 ] );
	g_bDepthTextures = false;
	answers.assign( 512, kSentinel );
	Ops o2;
	o2.K( SS_SHADOW_Hardware );
	EXPECT_EQ( 5, Run( o2 ) );
	EXPECT_EQ( 0, answers[ 0 ] );
	g_bDepthTextures = true;
}

TEST_F( FwdShadows, ComputePixelDiameterOfSphere )
{
	Ops o;
	o.K( SS_SHADOW_ComputePixelDiameterOfSphere ).V( 3, 4, 5 ).F( 10 );
	EXPECT_EQ( 1, Run( o ) );
	EXPECT_EQ( 23.0f, A( 0 ) );
	EXPECT_EQ( ( Calls{ "ms.GetRenderContext", "ctx.ComputePixelDiameterOfSphere (3 4 5) 10", "ctx.Release" } ), g_calls );
}

TEST_F( FwdShadows, GetFogMode )
{
	g_fogMode = MATERIAL_FOG_LINEAR_BELOW_FOG_Z;
	Ops o;
	o.K( SS_SHADOW_GetFogMode );
	EXPECT_EQ( 1, Run( o ) );
	EXPECT_EQ( (int32_t)MATERIAL_FOG_LINEAR_BELOW_FOG_Z, answers[ 0 ] );
	EXPECT_EQ( 1, g_nRelease );
}

TEST_F( FwdShadows, GetHeightClipMode )
{
	g_heightClipMode = MATERIAL_HEIGHTCLIPMODE_RENDER_BELOW_HEIGHT;
	Ops o;
	o.K( SS_SHADOW_GetHeightClipMode );
	EXPECT_EQ( 1, Run( o ) );
	EXPECT_EQ( (int32_t)MATERIAL_HEIGHTCLIPMODE_RENDER_BELOW_HEIGHT, answers[ 0 ] );
}

TEST_F( FwdShadows, DrawModelShadow )
{
	// The setup's info and bones are what the draw is handed; no bones, no draw.
	FakeRenderable r = Make( 2 );
	Ops o;
	o.K( SS_SHADOW_DrawModelShadow ).P( &r ).I( 3 ).I( 1 );
	EXPECT_EQ( 1, Run( o ) );
	EXPECT_EQ( 1, answers[ 0 ] );
	EXPECT_EQ( ( Calls{ "mr.DrawModelShadowSetup R2 3 1 0x0", "mr.DrawModelShadow R2 3 1 77 bones" } ), g_calls );

	g_calls.clear();
	g_bSetupAnswersNoBones = true;
	Ops o2;
	o2.K( SS_SHADOW_DrawModelShadow ).P( &r ).I( 3 ).I( 1 );
	o2.K( SS_SHADOW_DrawModelShadow ).P( nullptr ).I( 3 ).I( 1 );
	EXPECT_EQ( 2, Run( o2 ) );
	EXPECT_EQ( 0, answers[ 0 ] );
	EXPECT_EQ( 0, answers[ 1 ] );
	EXPECT_EQ( Calls{ "mr.DrawModelShadowSetup R2 3 1 0x0" }, g_calls );
}

TEST_F( FwdShadows, CullBox )
{
	Ops o;
	o.K( SS_SHADOW_CullBox ).V( 1, 2, 3 ).V( 4, 5, 6 );
	o.K( SS_SHADOW_CullBox ).V( 100, 2, 3 ).V( 104, 5, 6 );
	EXPECT_EQ( 2, Run( o ) );
	EXPECT_EQ( 0, answers[ 0 ] );
	EXPECT_EQ( 1, answers[ 1 ] );
	EXPECT_EQ( ( Calls{ "eng.CullBox (1 2 3) (4 5 6)", "eng.CullBox (100 2 3) (104 5 6)" } ), g_calls );
}

TEST_F( FwdShadows, AmbientLightColor )
{
	Ops o;
	o.K( SS_SHADOW_AmbientLightColor );
	EXPECT_EQ( 3, Run( o ) );
	EXPECT_EQ( 0.125f, A( 0 ) );
	EXPECT_EQ( 0.25f, A( 1 ) );
	EXPECT_EQ( 0.5f, A( 2 ) );
}

TEST_F( FwdShadows, LeavesAlongRay )
{
	// A swept box: the start, the delta, no start offset, the extents.
	Ops o;
	o.K( SS_SHADOW_LeavesAlongRay ).V( 5, 6, 7 ).V( 0, 0, -100 ).V( 8, 8, 1 );
	EXPECT_EQ( 4, Run( o ) );
	EXPECT_EQ( 3, answers[ 0 ] );
	EXPECT_EQ( 5, answers[ 1 ] );
	EXPECT_EQ( 6, answers[ 2 ] );
	EXPECT_EQ( 7, answers[ 3 ] );
	EXPECT_EQ( Calls{ "q.EnumerateLeavesAlongRay (5 6 7) (0 0 -100) (0 0 0) (8 8 1) 0 1 0" }, g_calls );
}

TEST_F( FwdShadows, LeavesInBox )
{
	Ops o;
	o.K( SS_SHADOW_LeavesInBox ).V( 10, 0, 0 ).V( 35, 1, 1 );
	o.K( SS_SHADOW_FrameCount );   // after the leaves, not over them
	g_globals.framecount = 99;
	EXPECT_EQ( 5, Run( o ) );
	EXPECT_EQ( 3, answers[ 0 ] );
	EXPECT_EQ( 1, answers[ 1 ] );
	EXPECT_EQ( 2, answers[ 2 ] );
	EXPECT_EQ( 3, answers[ 3 ] );
	EXPECT_EQ( 99, answers[ 4 ] );
	EXPECT_EQ( Calls{ "q.EnumerateLeavesInBox (10 0 0) (35 1 1) 0" }, g_calls );
}

TEST_F( FwdShadows, FrameCount )
{
	g_globals.framecount = 4321;
	Ops o;
	o.K( SS_SHADOW_FrameCount );
	EXPECT_EQ( 1, Run( o ) );
	EXPECT_EQ( 4321, answers[ 0 ] );
}

TEST_F( FwdShadows, RenderableOf )
{
	Ops o;
	o.K( SS_SHADOW_RenderableOf ).I( (int32_t)kKnownEntity );
	o.K( SS_SHADOW_RenderableOf ).I( 5 );   // names nothing now
	EXPECT_EQ( 4, Run( o ) );
	EXPECT_EQ( (uintptr_t)&g_entityRenderable, PtrAt( 0 ) );
	EXPECT_EQ( 0u, PtrAt( 2 ) );
	EXPECT_EQ( ( Calls{ "env.RenderableOf " + std::to_string( kKnownEntity ), "env.RenderableOf 5" } ), g_calls );
}

TEST_F( FwdShadows, RenderOrigin )
{
	FakeRenderable r = Make( 9 );
	Ops o;
	o.K( SS_SHADOW_RenderOrigin ).P( &r );
	o.K( SS_SHADOW_RenderOrigin ).P( nullptr );
	EXPECT_EQ( 6, Run( o ) );
	EXPECT_EQ( 9.0f, A( 0 ) );
	EXPECT_EQ( 2.5f, A( 1 ) );
	EXPECT_EQ( -3.0f, A( 2 ) );
	EXPECT_EQ( 0, answers[ 3 ] );
	EXPECT_EQ( 0, answers[ 4 ] );
	EXPECT_EQ( 0, answers[ 5 ] );
}

TEST_F( FwdShadows, RenderAngles )
{
	FakeRenderable r = Make( 9 );
	Ops o;
	o.K( SS_SHADOW_RenderAngles ).P( &r );
	EXPECT_EQ( 3, Run( o ) );
	EXPECT_EQ( 10.0f, A( 0 ) );
	EXPECT_EQ( 29.0f, A( 1 ) );
	EXPECT_EQ( 30.0f, A( 2 ) );
}

TEST_F( FwdShadows, RenderableToWorld )
{
	FakeRenderable r = Make( 2 );
	Ops o;
	o.K( SS_SHADOW_RenderableToWorld ).P( &r );
	o.K( SS_SHADOW_RenderableToWorld ).P( nullptr );
	EXPECT_EQ( 24, Run( o ) );
	for ( int i = 0; i < 3; i++ )
		for ( int j = 0; j < 4; j++ )
			EXPECT_EQ( r.toWorld[ i ][ j ], A( 4 * i + j ) ) << i << "," << j << ": row by row";
	for ( int k = 12; k < 24; k++ )
		EXPECT_EQ( 0, answers[ (size_t)k ] ) << k;
}

TEST_F( FwdShadows, Model )
{
	FakeRenderable r = Make( 3 );
	Ops o;
	o.K( SS_SHADOW_Model ).P( &r );
	o.K( SS_SHADOW_Model ).P( nullptr );
	EXPECT_EQ( 4, Run( o ) );
	EXPECT_EQ( (uintptr_t)r.model, PtrAt( 0 ) );
	EXPECT_EQ( 0u, PtrAt( 2 ) );
}

TEST_F( FwdShadows, ModelType )
{
	Ops o;
	o.K( SS_SHADOW_ModelType ).P( kModel );
	EXPECT_EQ( 1, Run( o ) );
	EXPECT_EQ( 0, answers[ 0 ] ) << "kModel ends in 0";
	answers.assign( 512, kSentinel );
	g_calls.clear();
	Ops o2;
	o2.K( SS_SHADOW_ModelType ).P( reinterpret_cast< const void * >( (uintptr_t)0x0000100000000003ull ) );
	EXPECT_EQ( 1, Run( o2 ) );
	EXPECT_EQ( 3, answers[ 0 ] );
	EXPECT_EQ( Calls{ "mi.GetModelType 0x100000000003" }, g_calls ) << "both halves of the pointer reach the model info";
}

TEST_F( FwdShadows, BodySkin )
{
	FakeRenderable r = Make( 4 );
	Ops o;
	o.K( SS_SHADOW_BodySkin ).P( &r );
	o.K( SS_SHADOW_BodySkin ).P( nullptr );
	EXPECT_EQ( 4, Run( o ) );
	EXPECT_EQ( 44, answers[ 0 ] );
	EXPECT_EQ( 54, answers[ 1 ] );
	EXPECT_EQ( 0, answers[ 2 ] );
	EXPECT_EQ( 0, answers[ 3 ] );
}

TEST_F( FwdShadows, RenderBoundsWorldspace )
{
	FakeRenderable r = Make( 4 );
	Ops o;
	o.K( SS_SHADOW_RenderBoundsWorldspace ).P( &r );
	EXPECT_EQ( 6, Run( o ) );
	const float expected[ 6 ] = { -1, -2, -3, 4, 5, 10 };
	for ( int i = 0; i < 6; i++ )
		EXPECT_EQ( expected[ i ], A( i ) ) << i;
}

TEST_F( FwdShadows, ShadowRenderBounds )
{
	FakeRenderable r = Make( 1 );
	Ops o;
	o.K( SS_SHADOW_ShadowRenderBounds ).P( &r ).I( SHADOWS_RENDER_TO_TEXTURE_DYNAMIC );
	EXPECT_EQ( 6, Run( o ) );
	const float expected[ 6 ] = { -7, -8, -9, 7, 8, 10 };
	for ( int i = 0; i < 6; i++ )
		EXPECT_EQ( expected[ i ], A( i ) ) << i;
	EXPECT_EQ( Calls{ "r.GetShadowRenderBounds 1 3" }, g_calls );
}

TEST_F( FwdShadows, ShouldReceiveProjectedTextures )
{
	FakeRenderable r = Make( 1 );
	r.receiveMask = 2;
	Ops o;
	o.K( SS_SHADOW_ShouldReceiveProjectedTextures ).P( &r ).I( 1 );
	o.K( SS_SHADOW_ShouldReceiveProjectedTextures ).P( &r ).I( 2 | 4 );
	o.K( SS_SHADOW_ShouldReceiveProjectedTextures ).P( nullptr ).I( 2 );
	EXPECT_EQ( 3, Run( o ) );
	EXPECT_EQ( 0, answers[ 0 ] );
	EXPECT_EQ( 1, answers[ 1 ] );
	EXPECT_EQ( 0, answers[ 2 ] );
	EXPECT_EQ( ( Calls{ "r.ShouldReceiveProjectedTextures 1 1", "r.ShouldReceiveProjectedTextures 1 6" } ), g_calls );
}

TEST_F( FwdShadows, ShadowCastDistance )
{
	// The renderable is handed the manager's distance; it answers or leaves it.
	FakeRenderable yes = Make( 1 ), no = Make( 2 );
	yes.answersDistance = true;
	yes.distance = 42;
	Ops o;
	o.K( SS_SHADOW_ShadowCastDistance ).P( &yes ).I( SHADOWS_SIMPLE ).F( 7 );
	o.K( SS_SHADOW_ShadowCastDistance ).P( &no ).I( SHADOWS_RENDER_TO_TEXTURE ).F( 8 );
	o.K( SS_SHADOW_ShadowCastDistance ).P( nullptr ).I( SHADOWS_SIMPLE ).F( 9 );
	EXPECT_EQ( 6, Run( o ) );
	EXPECT_EQ( 1, answers[ 0 ] );
	EXPECT_EQ( 42.0f, A( 1 ) );
	EXPECT_EQ( 0, answers[ 2 ] );
	EXPECT_EQ( 8.0f, A( 3 ) );
	EXPECT_EQ( 0, answers[ 4 ] );
	EXPECT_EQ( 9.0f, A( 5 ) );
	EXPECT_EQ( ( Calls{ "r.GetShadowCastDistance 1 7 1", "r.GetShadowCastDistance 2 8 2" } ), g_calls );
}

TEST_F( FwdShadows, ShadowCastDirection )
{
	FakeRenderable yes = Make( 1 ), no = Make( 2 );
	yes.answersDirection = true;
	yes.direction.Init( 0, 0.5f, -1 );
	Ops o;
	o.K( SS_SHADOW_ShadowCastDirection ).P( &yes ).I( SHADOWS_SIMPLE ).V( 1, 2, 3 );
	o.K( SS_SHADOW_ShadowCastDirection ).P( &no ).I( SHADOWS_RENDER_TO_TEXTURE ).V( 4, 5, 6 );
	EXPECT_EQ( 8, Run( o ) );
	EXPECT_EQ( 1, answers[ 0 ] );
	EXPECT_EQ( 0.0f, A( 1 ) );
	EXPECT_EQ( 0.5f, A( 2 ) );
	EXPECT_EQ( -1.0f, A( 3 ) );
	EXPECT_EQ( 0, answers[ 4 ] );
	EXPECT_EQ( 4.0f, A( 5 ) );
	EXPECT_EQ( 5.0f, A( 6 ) );
	EXPECT_EQ( 6.0f, A( 7 ) );
	EXPECT_EQ( ( Calls{ "r.GetShadowCastDirection 1 (1 2 3) 1", "r.GetShadowCastDirection 2 (4 5 6) 2" } ), g_calls );
}

TEST_F( FwdShadows, ShadowParent )
{
	FakeRenderable a = Make( 1 ), b = Make( 2 );
	a.parent = &b;
	Ops o;
	o.K( SS_SHADOW_ShadowParent ).P( &a );
	o.K( SS_SHADOW_ShadowParent ).P( &b );
	EXPECT_EQ( 4, Run( o ) );
	EXPECT_EQ( (uintptr_t)&b, PtrAt( 0 ) );
	EXPECT_EQ( 0u, PtrAt( 2 ) );
}

TEST_F( FwdShadows, FirstShadowChild )
{
	FakeRenderable a = Make( 1 ), b = Make( 2 );
	a.child = &b;
	Ops o;
	o.K( SS_SHADOW_FirstShadowChild ).P( &a );
	o.K( SS_SHADOW_FirstShadowChild ).P( nullptr );
	EXPECT_EQ( 4, Run( o ) );
	EXPECT_EQ( (uintptr_t)&b, PtrAt( 0 ) );
	EXPECT_EQ( 0u, PtrAt( 2 ) );
}

TEST_F( FwdShadows, NextShadowPeer )
{
	FakeRenderable a = Make( 1 ), b = Make( 2 );
	a.peer = &b;
	Ops o;
	o.K( SS_SHADOW_NextShadowPeer ).P( &a );
	o.K( SS_SHADOW_NextShadowPeer ).P( &b );
	EXPECT_EQ( 4, Run( o ) );
	EXPECT_EQ( (uintptr_t)&b, PtrAt( 0 ) );
	EXPECT_EQ( 0u, PtrAt( 2 ) );
}

TEST_F( FwdShadows, ModelInstance )
{
	FakeRenderable r = Make( 5 );
	Ops o;
	o.K( SS_SHADOW_ModelInstance ).P( &r );
	o.K( SS_SHADOW_ModelInstance ).P( nullptr );
	EXPECT_EQ( 2, Run( o ) );
	EXPECT_EQ( 605, answers[ 0 ] );
	EXPECT_EQ( (int32_t)MODEL_INSTANCE_INVALID, answers[ 1 ] );
}

TEST_F( FwdShadows, NetworkableEntIndex )
{
	FakeNetworkable net = { g_networkableTable.slots, 9 };
	FakeUnknown withNet = { g_unknownTable.slots, &net };
	FakeUnknown withoutNet = { g_unknownTable.slots, nullptr };
	FakeRenderable a = Make( 1 ), b = Make( 2 ), c = Make( 3 );
	a.unknown = &withNet;
	b.unknown = &withoutNet;
	c.unknown = nullptr;
	Ops o;
	o.K( SS_SHADOW_NetworkableEntIndex ).P( &a );
	o.K( SS_SHADOW_NetworkableEntIndex ).P( &b );
	o.K( SS_SHADOW_NetworkableEntIndex ).P( &c );
	o.K( SS_SHADOW_NetworkableEntIndex ).P( nullptr );
	EXPECT_EQ( 4, Run( o ) );
	EXPECT_EQ( 9, answers[ 0 ] );
	EXPECT_EQ( -1, answers[ 1 ] );
	EXPECT_EQ( -1, answers[ 2 ] );
	EXPECT_EQ( -1, answers[ 3 ] );
}

//=============================================================================
// The run.
//=============================================================================

TEST_F( FwdShadows, RecordsAreCarriedOutInOrderAcrossKinds )
{
	FakeRenderable r = Make( 6 );
	g_globals.framecount = 12;
	Ops o;
	o.K( SS_SHADOW_DestroyShadow ).I( 1 );
	o.K( SS_SHADOW_CullBox ).V( 200, 0, 0 ).V( 201, 1, 1 );
	o.K( SS_SHADOW_FogMode ).I( 0 );
	o.K( SS_SHADOW_ShutdownMaterial ).I( 2 );
	o.K( SS_SHADOW_FrameCount );
	o.K( SS_SHADOW_DrawBrushModelShadow ).P( &r );
	o.K( SS_SHADOW_PushMatrix );
	o.K( SS_SHADOW_BodySkin ).P( &r );
	o.K( SS_SHADOW_DestroyShadow ).I( 2 );
	EXPECT_EQ( 4, Run( o ) );
	EXPECT_EQ( 1, answers[ 0 ] );
	EXPECT_EQ( 12, answers[ 1 ] );
	EXPECT_EQ( 46, answers[ 2 ] );
	EXPECT_EQ( 56, answers[ 3 ] );
	EXPECT_EQ( kSentinel, answers[ 4 ] );
	const Calls expected = {
		"sm.DestroyShadow 1",
		"eng.CullBox (200 0 0) (201 1 1)",
		"ms.GetRenderContext",
		"ctx.FogMode 0",
		"env.ShutdownMaterial 2",
		"rv.DrawBrushModelShadow R6",
		"ctx.PushMatrix",
		"sm.DestroyShadow 2",
		"ctx.Release",
	};
	EXPECT_EQ( expected, g_calls );
}

TEST_F( FwdShadows, ARecordWhosePayloadRunsPastTheBufferEndsTheRun )
{
	Ops o;
	o.K( SS_SHADOW_DestroyShadow ).I( 1 );
	o.K( SS_SHADOW_DestroyShadow ).I( 2 );
	o.K( SS_SHADOW_DestroyShadow ).I( 3 );
	const std::vector< int32_t > w = o.Done();

	// The whole buffer: all three.
	EXPECT_EQ( 0, SSFwd_Shadows_Run( &env, w.data(), (int32_t)w.size(), answers.data(), 512 ) );
	EXPECT_EQ( ( Calls{ "sm.DestroyShadow 1", "sm.DestroyShadow 2", "sm.DestroyShadow 3" } ), g_calls );

	// One word short: the third's payload runs past the end, and it does not run.
	g_calls.clear();
	EXPECT_EQ( 0, SSFwd_Shadows_Run( &env, w.data(), (int32_t)w.size() - 1, answers.data(), 512 ) );
	EXPECT_EQ( ( Calls{ "sm.DestroyShadow 1", "sm.DestroyShadow 2" } ), g_calls );

	// The second claims more payload than the buffer holds: it and everything
	// after it end the run.
	std::vector< int32_t > bad = w;
	bad[ 4 ] = 1000;
	g_calls.clear();
	EXPECT_EQ( 0, SSFwd_Shadows_Run( &env, bad.data(), (int32_t)bad.size(), answers.data(), 512 ) );
	EXPECT_EQ( Calls{ "sm.DestroyShadow 1" }, g_calls );

	// And a negative count is not a record.
	bad[ 4 ] = -1;
	g_calls.clear();
	EXPECT_EQ( 0, SSFwd_Shadows_Run( &env, bad.data(), (int32_t)bad.size(), answers.data(), 512 ) );
	EXPECT_EQ( Calls{ "sm.DestroyShadow 1" }, g_calls );
}

TEST_F( FwdShadows, AShortRecordEndsTheRun )
{
	// SetShadowTexCoord reads five words; this one carries two. It is not
	// carried out, and neither is anything after it.
	Ops o;
	o.K( SS_SHADOW_DestroyShadow ).I( 1 );
	o.K( SS_SHADOW_SetShadowTexCoord ).I( 7 ).F( 1 );
	o.K( SS_SHADOW_DestroyShadow ).I( 9 );
	o.K( SS_SHADOW_FrameCount );
	EXPECT_EQ( 0, Run( o ) );
	EXPECT_EQ( Calls{ "sm.DestroyShadow 1" }, g_calls );
	EXPECT_EQ( kSentinel, answers[ 0 ] );

	// A short question answers its zero, and nothing after it runs.
	g_calls.clear();
	g_globals.framecount = 5;
	Ops q;
	q.K( SS_SHADOW_CullBox ).V( 100, 0, 0 );   // mins only
	q.K( SS_SHADOW_FrameCount );
	EXPECT_EQ( 1, Run( q ) );
	EXPECT_EQ( 0, answers[ 0 ] );
	EXPECT_EQ( kSentinel, answers[ 1 ] ) << "the frame count after the short record was answered";
	EXPECT_TRUE( g_calls.empty() ) << g_calls.front();

	// A string whose length runs past its record, or that carries no NUL.
	g_calls.clear();
	Ops s;
	s.K( SS_SHADOW_InitMaterial ).I( 400 ).I( 0x41414141 );
	s.K( SS_SHADOW_DestroyShadow ).I( 1 );
	EXPECT_EQ( 1, Run( s ) );
	EXPECT_EQ( 0, answers[ 0 ] );
	Ops s2;
	s2.K( SS_SHADOW_InitMaterial ).I( 4 ).I( 0x41414141 ).S( "group" );   // four bytes and no NUL in their word
	s2.K( SS_SHADOW_DestroyShadow ).I( 1 );
	answers.assign( 512, kSentinel );
	EXPECT_EQ( 1, Run( s2 ) );
	EXPECT_EQ( 0, answers[ 0 ] );
	EXPECT_TRUE( g_calls.empty() ) << g_calls.front();

	// And a flashlight state shorter than its 25 words.
	Ops f;
	f.K( SS_SHADOW_UpdateFlashlightState ).I( 12 );
	for ( int i = 0; i < 24; i++ )
		f.I( 0 );
	f.K( SS_SHADOW_DestroyShadow ).I( 1 );
	EXPECT_EQ( 0, Run( f ) );
	EXPECT_TRUE( g_calls.empty() ) << g_calls.front();
}

TEST_F( FwdShadows, AnEmptyRecordOfAKindThatReadsAPayloadReachesNothing )
{
	// Every kind whose payload is not empty, sent with none: the record is
	// short, so nothing is called or asked and the run ends there.
	const int32_t kKinds[] = {
		SS_SHADOW_DestroyShadow, SS_SHADOW_SetShadowMaterial, SS_SHADOW_ProjectShadow, SS_SHADOW_ProjectFlashlight,
		SS_SHADOW_AddShadowToBrushModel, SS_SHADOW_RemoveAllShadowsFromBrushModel, SS_SHADOW_SetShadowTexCoord,
		SS_SHADOW_AddShadowToModel, SS_SHADOW_RemoveAllShadowsFromModel, SS_SHADOW_ClearExtraClipPlanes, SS_SHADOW_AddExtraClipPlane,
		SS_SHADOW_EnableShadow, SS_SHADOW_SetFalloffBias, SS_SHADOW_UpdateFlashlightState, SS_SHADOW_AddFlashlightRenderable,
		SS_SHADOW_SetFlashlightDepthTexture, SS_SHADOW_SetFlashlightRenderState, SS_SHADOW_AddShadowToStaticProp,
		SS_SHADOW_RemoveAllShadowsFromStaticProp, SS_SHADOW_ShutdownMaterial, SS_SHADOW_ColorModulate, SS_SHADOW_ShutdownTexture,
		SS_SHADOW_FogMode, SS_SHADOW_SetHeightClipMode, SS_SHADOW_ClearColor4ub, SS_SHADOW_ClearColor3ub, SS_SHADOW_MatrixMode,
		SS_SHADOW_LoadMatrix, SS_SHADOW_Scale, SS_SHADOW_Ortho, SS_SHADOW_PushRenderTargetAndViewport, SS_SHADOW_ClearBuffers,
		SS_SHADOW_Viewport, SS_SHADOW_SetShadowDepthBiasFactors, SS_SHADOW_DrawTextureQuad, SS_SHADOW_DrawBrushModelShadow,
		SS_SHADOW_UpdateShadowDepthTexture, SS_SHADOW_CreateModelInstance, SS_SHADOW_AddLineOverlay, SS_SHADOW_AddTextOverlay,
		SS_SHADOW_CreateShadowEx, SS_SHADOW_GetInfo, SS_SHADOW_GetFlashlightFrustum, SS_SHADOW_InitMaterial, SS_SHADOW_InitRenderTarget,
		SS_SHADOW_TextureSize, SS_SHADOW_ComputePixelDiameterOfSphere, SS_SHADOW_DrawModelShadow, SS_SHADOW_CullBox,
		SS_SHADOW_LeavesAlongRay, SS_SHADOW_LeavesInBox, SS_SHADOW_RenderableOf, SS_SHADOW_RenderOrigin, SS_SHADOW_RenderAngles,
		SS_SHADOW_RenderableToWorld, SS_SHADOW_Model, SS_SHADOW_ModelType, SS_SHADOW_BodySkin, SS_SHADOW_RenderBoundsWorldspace,
		SS_SHADOW_ShadowRenderBounds, SS_SHADOW_ShouldReceiveProjectedTextures, SS_SHADOW_ShadowCastDistance,
		SS_SHADOW_ShadowCastDirection, SS_SHADOW_ShadowParent, SS_SHADOW_FirstShadowChild, SS_SHADOW_NextShadowPeer,
		SS_SHADOW_ModelInstance, SS_SHADOW_NetworkableEntIndex,
	};
	g_globals.framecount = 77;
	for ( int32_t kind : kKinds )
	{
		g_calls.clear();
		answers.assign( 512, kSentinel );
		Ops o;
		o.K( kind );
		o.K( SS_SHADOW_FrameCount );
		const int32_t n = Run( o );
		EXPECT_TRUE( g_calls.empty() ) << "kind " << kind << " with no payload reached " << g_calls.front();
		for ( int32_t i = 0; i < n; i++ )
			EXPECT_NE( 77, answers[ (size_t)i ] ) << "kind " << kind << ": the run went on past the short record";
	}
}

namespace
{
// A renderable at an address whose high word is zero, so a record that
// carries only a pointer's low word names it: what a short record would read
// if it were carried out. Mapped once, at the first free address tried.
FakeRenderable *LowRenderable()
{
	static FakeRenderable *s_p = nullptr;
	if ( s_p )
		return s_p;
	const uintptr_t kTry[] = { 0x20000000u, 0x30000000u, 0x40000000u, 0x50000000u };
	for ( uintptr_t at : kTry )
	{
#ifdef _WIN32
		void *pv = VirtualAlloc( reinterpret_cast< void * >( at ), 4096, 0x1000 | 0x2000 /* MEM_COMMIT | MEM_RESERVE */, 0x04 /* PAGE_READWRITE */ );
		if ( pv != reinterpret_cast< void * >( at ) )
			continue;
#else
		// A hint, not MAP_FIXED: the kernel answers it only when the range is free.
		void *pv = mmap( reinterpret_cast< void * >( at ), 4096, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0 );
		if ( pv == MAP_FAILED )
			continue;
		if ( pv != reinterpret_cast< void * >( at ) )
		{
			munmap( pv, 4096 );
			continue;
		}
#endif
		FakeRenderable r = Make( 31 );
		memcpy( pv, (const void *)&r, sizeof( r ) );
		s_p = static_cast< FakeRenderable * >( pv );
		// Every pointer it answers is itself, and it is networked, so no
		// question about it answers what a question about none would.
		static FakeNetworkable s_net = { g_networkableTable.slots, 31 };
		static FakeUnknown s_unknown = { g_unknownTable.slots, &s_net };
		s_p->parent = s_p;
		s_p->child = s_p;
		s_p->peer = s_p;
		s_p->unknown = &s_unknown;
		break;
	}
	return s_p;
}
} // namespace

TEST_F( FwdShadows, AQuestionWithHalfAPointerIsNotAsked )
{
	// fwd_shadows.h: a record shorter than its kind reads ends the run there.
	// Each of these reads a renderable (or a model) and nothing else; carrying
	// only the pointer's low word, it is short, and must answer as a question
	// about no renderable does rather than ask the half pointer.
	FakeRenderable *pLow = LowRenderable();
	ASSERT_NE( nullptr, pLow ) << "no low address could be mapped for the fake";
	const uint64_t v = (uint64_t)(uintptr_t)pLow;
	ASSERT_EQ( 0u, v >> 32 );
	const int32_t lo = (int32_t)(uint32_t)v;

	// The positive control: the full pointer is the fake, and it answers.
	{
		Ops o;
		o.K( SS_SHADOW_BodySkin ).P( pLow );
		ASSERT_EQ( 2, Run( o ) );
		ASSERT_EQ( 71, answers[ 0 ] ) << "the low fake was not read";
	}

	const int32_t kKinds[] = {
		SS_SHADOW_RenderOrigin, SS_SHADOW_RenderAngles, SS_SHADOW_RenderableToWorld, SS_SHADOW_Model, SS_SHADOW_ModelType,
		SS_SHADOW_BodySkin, SS_SHADOW_RenderBoundsWorldspace, SS_SHADOW_ShadowParent, SS_SHADOW_FirstShadowChild,
		SS_SHADOW_NextShadowPeer, SS_SHADOW_ModelInstance, SS_SHADOW_NetworkableEntIndex, SS_SHADOW_ShadowRenderBounds,
		SS_SHADOW_ShouldReceiveProjectedTextures, SS_SHADOW_ShadowCastDistance, SS_SHADOW_ShadowCastDirection,
		SS_SHADOW_DrawModelShadow, SS_SHADOW_DrawBrushModelShadow, SS_SHADOW_CreateModelInstance, SS_SHADOW_RemoveAllShadowsFromStaticProp, SS_SHADOW_RemoveAllShadowsFromBrushModel,
	};
	g_globals.framecount = 77;
	for ( int32_t kind : kKinds )
	{
		// What the kind answers about no renderable, as a full record.
		Ops none;
		none.K( kind ).P( nullptr );
		answers.assign( 512, kSentinel );
		g_calls.clear();
		const int32_t nNone = Run( none );
		const std::vector< int32_t > expected( answers.begin(), answers.begin() + ( nNone > 0 ? nNone : 0 ) );

		Ops half;
		half.K( kind ).I( lo );
		half.K( SS_SHADOW_FrameCount );
		answers.assign( 512, kSentinel );
		g_calls.clear();
		const int32_t nHalf = Run( half );
		EXPECT_EQ( nNone, nHalf ) << "kind " << kind;
		for ( int32_t i = 0; i < nNone && i < nHalf; i++ )
			EXPECT_EQ( expected[ (size_t)i ], answers[ (size_t)i ] ) << "kind " << kind << " answered from the half pointer, word " << i;
		EXPECT_EQ( kSentinel, answers[ (size_t)( nNone > 0 ? nNone : 0 ) ] ) << "kind " << kind << ": the run went on";
		EXPECT_TRUE( g_calls.empty() ) << "kind " << kind << " with half a pointer reached " << g_calls.front();
	}
}

TEST_F( FwdShadows, AnAnswerLongerThanTheBufferWritesWhatFitsAndAsksForTheRest )
{
	// 0..300 in tens is 31 leaves: a count and 31 words, 32 in all.
	Ops o;
	o.K( SS_SHADOW_LeavesInBox ).V( 0, 0, 0 ).V( 300, 1, 1 );
	o.K( SS_SHADOW_DestroyShadow ).I( 4 );   // the run goes on: only the answers are counted
	EXPECT_EQ( -32, Run( o, 10 ) );
	EXPECT_EQ( 31, answers[ 0 ] ) << "the count is written where it fits";
	for ( int i = 1; i < 10; i++ )
		EXPECT_EQ( i - 1, answers[ (size_t)i ] ) << i;
	EXPECT_EQ( kSentinel, answers[ 10 ] ) << "nothing is written past the answer buffer";
	EXPECT_EQ( ( Calls{ "q.EnumerateLeavesInBox (0 0 0) (300 1 1) 0", "sm.DestroyShadow 4" } ), g_calls );

	// Room for exactly what it needs: the positive count.
	answers.assign( 512, kSentinel );
	EXPECT_EQ( 32, Run( o, 32 ) );
	EXPECT_EQ( 30, answers[ 31 ] );

	// No room at all, even for the count.
	answers.assign( 512, kSentinel );
	EXPECT_EQ( -32, Run( o, 0 ) );
	EXPECT_EQ( kSentinel, answers[ 0 ] );
	const std::vector< int32_t > &w = o.Done();
	EXPECT_EQ( -32, SSFwd_Shadows_Run( &env, w.data(), (int32_t)w.size(), nullptr, 100 ) ) << "no answer buffer: everything only counted";
}

TEST_F( FwdShadows, NoEnvironmentOrNoRecordsAnswersZero )
{
	Ops o;
	o.K( SS_SHADOW_DestroyShadow ).I( 1 );
	o.K( SS_SHADOW_FrameCount );
	const std::vector< int32_t > &w = o.Done();
	EXPECT_EQ( 0, SSFwd_Shadows_Run( nullptr, w.data(), (int32_t)w.size(), answers.data(), 512 ) );
	EXPECT_EQ( 0, SSFwd_Shadows_Run( &env, nullptr, (int32_t)w.size(), answers.data(), 512 ) );
	EXPECT_EQ( 0, SSFwd_Shadows_Run( &env, w.data(), 0, answers.data(), 512 ) );
	EXPECT_EQ( 0, SSFwd_Shadows_Run( &env, w.data(), -3, answers.data(), 512 ) );
	EXPECT_EQ( 0, SSFwd_Shadows_Run( &env, w.data(), 1, answers.data(), 512 ) ) << "one word is not a record";
	EXPECT_TRUE( g_calls.empty() ) << g_calls.front();
	EXPECT_EQ( kSentinel, answers[ 0 ] );
	// The positive control: the same buffer and environment run.
	EXPECT_EQ( 1, SSFwd_Shadows_Run( &env, w.data(), (int32_t)w.size(), answers.data(), 512 ) );
	EXPECT_EQ( Calls{ "sm.DestroyShadow 1" }, g_calls );
}

TEST_F( FwdShadows, TheRenderContextIsFetchedOncePerRunAndReleasedOnceAtTheEnd )
{
	Ops o;
	o.K( SS_SHADOW_FogMode ).I( 0 );
	o.K( SS_SHADOW_PushMatrix );
	o.K( SS_SHADOW_DestroyShadow ).I( 1 );
	o.K( SS_SHADOW_PopMatrix );
	o.K( SS_SHADOW_GetFogMode );
	EXPECT_EQ( 1, Run( o ) );
	EXPECT_EQ( 1, g_nGetContext );
	EXPECT_EQ( 1, g_nRelease );
	ASSERT_FALSE( g_calls.empty() );
	EXPECT_EQ( "ctx.Release", g_calls.back() ) << "released after the last record";

	// A second run asks again: the context does not outlive its run.
	Run( o );
	EXPECT_EQ( 2, g_nGetContext );
	EXPECT_EQ( 2, g_nRelease );

	// A run that needs none asks for none and releases none.
	g_nGetContext = 0;
	g_nRelease = 0;
	Ops n;
	n.K( SS_SHADOW_DestroyShadow ).I( 1 );
	n.K( SS_SHADOW_BeginRenderTargetAllocation );
	n.K( SS_SHADOW_DrawTextureQuad ).I( 1 ).F( 1 ).F( 1 );
	n.K( SS_SHADOW_FrameCount );
	EXPECT_EQ( 1, Run( n ) );
	EXPECT_EQ( 0, g_nGetContext );
	EXPECT_EQ( 0, g_nRelease );
}

TEST_F( FwdShadows, AMissingEnginePointerIsANoOpForACallAndAZeroAnswerForAQuestion )
{
	FakeRenderable r = Make( 1 );
	Ops o;
	// The calls that reach an engine interface.
	o.K( SS_SHADOW_DestroyShadow ).I( 1 );
	o.K( SS_SHADOW_SetShadowMaterial ).I( 1 ).I( 1 ).I( 2 ).P( kProxy );
	o.K( SS_SHADOW_ProjectShadow ).I( 1 ).V( 1, 2, 3 ).V( 0, 0, -1 ).M( 0 ).F( 1 ).F( 1 ).F( 1 ).F( 1 ).F( 1 ).V( 0, 0, 0 ).L( { 1 } );
	o.K( SS_SHADOW_ProjectFlashlight ).I( 1 ).M( 0 ).L( { 1 } );
	o.K( SS_SHADOW_AddShadowToBrushModel ).I( 1 ).P( kModel ).V( 0, 0, 0 ).V( 0, 0, 0 );
	o.K( SS_SHADOW_RemoveAllShadowsFromBrushModel ).P( kModel );
	o.K( SS_SHADOW_SetShadowTexCoord ).I( 1 ).F( 0 ).F( 0 ).F( 1 ).F( 1 );
	o.K( SS_SHADOW_AddShadowToModel ).I( 1 ).I( 2 );
	o.K( SS_SHADOW_RemoveAllShadowsFromModel ).I( 2 );
	o.K( SS_SHADOW_ClearExtraClipPlanes ).I( 1 );
	o.K( SS_SHADOW_AddExtraClipPlane ).I( 1 ).V( 0, 0, 1 ).F( 0 );
	o.K( SS_SHADOW_EnableShadow ).I( 1 ).I( 1 );
	o.K( SS_SHADOW_SetFalloffBias ).I( 1 ).I( 1 );
	o.K( SS_SHADOW_UpdateFlashlightState ).I( 1 );
	for ( int i = 0; i < 25; i++ )
		o.I( 0 );
	o.K( SS_SHADOW_AddFlashlightRenderable ).I( 1 ).P( &r );
	o.K( SS_SHADOW_SetFlashlightDepthTexture ).I( 1 ).I( 1 ).I( 1 );
	o.K( SS_SHADOW_SetFlashlightRenderState ).I( 1 );
	o.K( SS_SHADOW_AddShadowToStaticProp ).I( 1 ).P( &r );
	o.K( SS_SHADOW_RemoveAllShadowsFromStaticProp ).P( &r );
	o.K( SS_SHADOW_BeginRenderTargetAllocation );
	o.K( SS_SHADOW_EndRenderTargetAllocation );
	o.K( SS_SHADOW_UncacheUnusedMaterials );
	o.K( SS_SHADOW_FogMode ).I( 1 );
	o.K( SS_SHADOW_SetHeightClipMode ).I( 1 );
	o.K( SS_SHADOW_ClearColor4ub ).I( 1 ).I( 1 ).I( 1 ).I( 1 );
	o.K( SS_SHADOW_ClearColor3ub ).I( 1 ).I( 1 ).I( 1 );
	o.K( SS_SHADOW_MatrixMode ).I( 1 );
	o.K( SS_SHADOW_PushMatrix );
	o.K( SS_SHADOW_PopMatrix );
	o.K( SS_SHADOW_LoadIdentity );
	o.K( SS_SHADOW_LoadMatrix ).M( 0 );
	o.K( SS_SHADOW_Scale ).F( 1 ).F( 1 ).F( 1 );
	o.K( SS_SHADOW_Ortho ).D( 0 ).D( 0 ).D( 1 ).D( 1 ).D( 0 ).D( 1 );
	o.K( SS_SHADOW_PushRenderTargetAndViewport ).I( 1 );
	o.K( SS_SHADOW_PopRenderTargetAndViewport );
	o.K( SS_SHADOW_ClearBuffers ).I( 1 ).I( 1 ).I( 1 );
	o.K( SS_SHADOW_Viewport ).I( 0 ).I( 0 ).I( 1 ).I( 1 );
	o.K( SS_SHADOW_SetShadowDepthBiasFactors ).F( 1 ).F( 1 );
	o.K( SS_SHADOW_DrawBrushModelShadow ).P( &r );
	o.K( SS_SHADOW_AddLineOverlay ).V( 0, 0, 0 ).V( 1, 1, 1 ).I( 1 ).I( 1 ).I( 1 ).I( 1 ).F( 1 );
	o.K( SS_SHADOW_AddTextOverlay ).V( 0, 0, 0 ).F( 0 ).S( "x" );
	// The questions that ask an engine interface.
	o.K( SS_SHADOW_CreateShadowEx ).I( 1 ).I( 2 ).P( kProxy ).I( 0 );   // 1 word
	o.K( SS_SHADOW_GetInfo ).I( 1 );                                   // the struct's words
	o.K( SS_SHADOW_GetFlashlightFrustum ).I( 1 );                      // 48
	o.K( SS_SHADOW_Hardware );                                         // 5
	o.K( SS_SHADOW_ComputePixelDiameterOfSphere ).V( 0, 0, 0 ).F( 1 ); // 1
	o.K( SS_SHADOW_GetFogMode );                                       // 1
	o.K( SS_SHADOW_GetHeightClipMode );                                // 1
	o.K( SS_SHADOW_DrawModelShadow ).P( &r ).I( 0 ).I( 0 );            // 1
	o.K( SS_SHADOW_CullBox ).V( 100, 0, 0 ).V( 101, 1, 1 );            // 1
	o.K( SS_SHADOW_AmbientLightColor );                                // 3
	o.K( SS_SHADOW_LeavesAlongRay ).V( 1, 2, 3 ).V( 0, 0, 1 ).V( 1, 1, 1 ); // 1: the count
	o.K( SS_SHADOW_LeavesInBox ).V( 0, 0, 0 ).V( 30, 1, 1 );           // 1: the count
	o.K( SS_SHADOW_FrameCount );                                       // 1
	o.K( SS_SHADOW_ModelType ).P( reinterpret_cast< const void * >( (uintptr_t)7 ) ); // 1
	const int32_t nInfo = (int32_t)( ( sizeof( ShadowInfo_t ) + 3 ) / 4 );
	const int32_t nAnswers = 1 + nInfo + 48 + 5 + 1 + 1 + 1 + 1 + 1 + 3 + 1 + 1 + 1 + 1;

	IShadowMgr *pNoShadowMgr = nullptr;
	IStaticPropMgrClient *pNoStaticProps = nullptr;
	IMaterialSystem *pNoMaterials = nullptr;
	IMaterialSystemHardwareConfig *pNoHardware = nullptr;
	IVModelRender *pNoModelRender = nullptr;
	IVRenderView *pNoRender = nullptr;
	IVEngineClient *pNoEngine = nullptr;
	IVModelInfoClient *pNoModelInfo = nullptr;
	IVDebugOverlay *pNoOverlay = nullptr;
	CGlobalVarsBase *pNoGlobals = nullptr;

	const std::vector< int32_t > &w = o.Done();
	for ( int variant = 0; variant < 2; variant++ )
	{
		// 0: each pointer the environment hands is null; 1: the environment hands no pointer at all.
		SSShadowEnv bare = FullEnv();
		bare.ppShadowMgr = variant ? nullptr : &pNoShadowMgr;
		bare.ppStaticPropMgr = variant ? nullptr : &pNoStaticProps;
		bare.ppMaterials = variant ? nullptr : &pNoMaterials;
		bare.ppHardwareConfig = variant ? nullptr : &pNoHardware;
		bare.ppModelRender = variant ? nullptr : &pNoModelRender;
		bare.ppRender = variant ? nullptr : &pNoRender;
		bare.ppEngine = variant ? nullptr : &pNoEngine;
		bare.ppModelInfo = variant ? nullptr : &pNoModelInfo;
		bare.ppDebugOverlay = variant ? nullptr : &pNoOverlay;
		bare.ppGlobals = variant ? nullptr : &pNoGlobals;
		g_calls.clear();
		answers.assign( 512, kSentinel );
		EXPECT_EQ( nAnswers, SSFwd_Shadows_Run( &bare, w.data(), (int32_t)w.size(), answers.data(), 512 ) ) << variant;
		// Only client.so's own functions ran (the flashlight state's texture).
		for ( const std::string &s : g_calls )
			EXPECT_EQ( 0u, s.find( "env." ) ) << variant << ": " << s;
		EXPECT_EQ( (int32_t)SHADOW_HANDLE_INVALID, answers[ 0 ] ) << variant << ": no shadow manager, no handle";
		for ( int32_t i = 1; i < nAnswers; i++ )
			EXPECT_EQ( 0, answers[ (size_t)i ] ) << variant << ": answer word " << i;
		EXPECT_EQ( kSentinel, answers[ (size_t)nAnswers ] ) << variant;
	}

	// The positive control: the same records over the full environment reach
	// every engine interface (the arm above is not passing for want of a run).
	g_calls.clear();
	answers.assign( 512, kSentinel );
	g_bDepthTextures = true;
	EXPECT_LT( 0, SSFwd_Shadows_Run( &env, w.data(), (int32_t)w.size(), answers.data(), 512 ) );
	const char *const kReached[] = { "sm.", "sp.", "ms.", "ctx.", "mr.", "rv.", "eng.", "q.", "mi.", "ov." };
	for ( const char *pszPrefix : kReached )
		EXPECT_FALSE( CallsOf( pszPrefix ).empty() ) << pszPrefix;
}
