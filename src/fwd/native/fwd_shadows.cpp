//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client shadow manager's calls, carried out ======//
// SS_REASON a  a vtable the engine calls through
//
// C7 cluster 12 S2 (docs/plans/c7_shadow_manager.md). The shadow manager is
// managed's: which shadows exist, how and when each is projected, what it
// lands on, the render-to-texture pass and its atlas, the flashlights and
// their depth textures are SourceSharp.Client's Render/Shadows. What it does
// to the engine, and what it asks the engine and the renderables, arrives
// here as records (fwd_shadows.h), one buffer a crossing, and SSFwd_Shadows_Run
// carries them out in the order they were made: the engine's IShadowMgr and
// static prop manager, the material system's references (client.so's,
// through the environment), the render context of the render-to-texture
// pass, the model and brush shadow draws, the flashlight depth views, the
// renderables' shadow surface.
//
// IT DECIDES NOTHING. Every `if` below is a record's bounds, a null guard or
// the switch over its kind; the arguments are the manager's.
//
// THE LIBRARY HAS NO RUNTIME (NO_RUNTIME): no memcpy, no memset, no heap. The
// answers are written word by word into the caller's buffer, and the leaves
// a query enumerates are written there as the engine names them.
//=============================================================================//
#include "ss_fwd_sdk.h"
#include "fwd_shadows.h"

#include <stddef.h>

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
#include "materialsystem/itexture.h"
#include "mathlib/vmatrix.h"
#include "sourcesharp.h"

namespace
{
	union Word
	{
		int32_t i;
		float f;
	};

	float F( int32_t w )
	{
		Word u;
		u.i = w;
		return u.f;
	}

	int32_t Bits( float f )
	{
		Word u;
		u.f = f;
		return u.i;
	}

	// One record's payload, read in order; a read past its end answers 0
	// and marks the record short, which ends the run.
	struct Payload
	{
		const int32_t *p;
		int32_t n;
		int32_t at;
		bool bShort;

		int32_t I()
		{
			if ( at >= n )
			{
				bShort = true;
				return 0;
			}
			return p[ at++ ];
		}

		float Fl() { return F( I() ); }

		void *Ptr()
		{
			const uint32_t lo = (uint32_t)I();
			const uint32_t hi = (uint32_t)I();
			return reinterpret_cast< void * >( (uintptr_t)( ( (uint64_t)hi << 32 ) | lo ) );
		}

		// NULL when the record was too short to hold the whole pointer: a
		// truncated pointer is never followed.
		IClientRenderable *Renderable()
		{
			void *pv = Ptr();
			return bShort ? NULL : static_cast< IClientRenderable * >( pv );
		}

		Vector Vec()
		{
			const float x = Fl();
			const float y = Fl();
			const float z = Fl();
			return Vector( x, y, z );
		}

		QAngle Ang()
		{
			const float x = Fl();
			const float y = Fl();
			const float z = Fl();
			return QAngle( x, y, z );
		}

		double D()
		{
			union
			{
				int32_t w[ 2 ];
				double d;
			} u;
			u.w[ 0 ] = I();
			u.w[ 1 ] = I();
			return u.d;
		}

		void Matrix( VMatrix &m )
		{
			for ( int r = 0; r < 4; ++r )
				for ( int c = 0; c < 4; ++c )
					m.m[ r ][ c ] = Fl();
		}

		// A string: its byte length, then its bytes padded to a word; NULL
		// when the record is too short to hold it or it carries no NUL.
		const char *Str()
		{
			const int32_t nBytes = I();
			const int32_t nWords = ( nBytes + 4 ) / 4;
			if ( bShort || nBytes < 0 || nWords > n - at )
			{
				bShort = true;
				return NULL;
			}
			const char *psz = reinterpret_cast< const char * >( p + at );
			at += nWords;
			if ( psz[ nBytes ] != 0 )
			{
				bShort = true;
				return NULL;
			}
			return psz;
		}

		// The rest of the payload: a count, then that many words.
		const int *Leaves( int &nCount )
		{
			nCount = I();
			if ( bShort || nCount < 0 || nCount > n - at )
			{
				bShort = true;
				nCount = 0;
				return NULL;
			}
			const int *pLeaves = reinterpret_cast< const int * >( p + at );
			at += nCount;
			return pLeaves;
		}
	};

	// The answers, after one another; past the buffer only counted.
	struct Answers
	{
		int32_t *p;
		int32_t nMax;
		int32_t n;

		void I( int32_t w )
		{
			if ( n < nMax )
				p[ n ] = w;
			++n;
		}

		void Fl( float f ) { I( Bits( f ) ); }

		void Ptr( const void *pv )
		{
			const uint64_t v = (uint64_t)(uintptr_t)pv;
			I( (int32_t)(uint32_t)( v & 0xFFFFFFFFu ) );
			I( (int32_t)(uint32_t)( v >> 32 ) );
		}

		void Vec( const Vector &v )
		{
			Fl( v.x );
			Fl( v.y );
			Fl( v.z );
		}

		// A struct's words as they lie: ShadowInfo_t and Frustum_t, which the
		// managed side reads through their byte-for-byte mirrors.
		void Words( const void *pv, size_t nBytes )
		{
			const unsigned char *pb = static_cast< const unsigned char * >( pv );
			for ( size_t i = 0; i < nBytes; i += 4 )
			{
				int32_t w = 0;
				for ( size_t k = 0; k < 4 && i + k < nBytes; ++k )
					w |= (int32_t)( (uint32_t)pb[ i + k ] << ( 8 * k ) );
				I( w );
			}
		}
	};

	// The engine's BSP query calls this back per leaf; the leaves go after the
	// count the caller reserved.
	class CLeafCollector final : public ISpatialLeafEnumerator
	{
	public:
		explicit CLeafCollector( Answers &out ) : m_Out( out ), m_nCount( 0 ) {}

		bool EnumerateLeaf( int leaf, intp ) override
		{
			m_Out.I( leaf );
			++m_nCount;
			return true;
		}

		Answers &m_Out;
		int32_t m_nCount;
	};

	// The render context, asked for once per run that needs it.
	struct Context
	{
		IMaterialSystem *pMaterials;
		IMatRenderContext *pContext;

		IMatRenderContext *Get()
		{
			if ( !pContext && pMaterials )
				pContext = pMaterials->GetRenderContext();
			return pContext;
		}
	};

	void StateFromWire( const SSShadowEnv &env, const SourceSharpFlashlightState &w, FlashlightState_t &s )
	{
		s.m_vecLightOrigin.Init( w.OriginXYZ[ 0 ], w.OriginXYZ[ 1 ], w.OriginXYZ[ 2 ] );
		s.m_quatOrientation.Init( w.Orientation[ 0 ], w.Orientation[ 1 ], w.Orientation[ 2 ], w.Orientation[ 3 ] );
		s.m_NearZ = w.NearZ;
		s.m_FarZ = w.FarZ;
		s.m_fHorizontalFOVDegrees = w.HorizontalFovDegrees;
		s.m_fVerticalFOVDegrees = w.VerticalFovDegrees;
		s.m_fQuadraticAtten = w.QuadraticAtten;
		s.m_fLinearAtten = w.LinearAtten;
		s.m_fConstantAtten = w.ConstantAtten;
		for ( int i = 0; i < 4; i++ )
			s.m_Color[ i ] = w.Color[ i ];
		s.m_pSpotlightTexture = env.pfnFlashlightTexture ? env.pfnFlashlightTexture( w.Texture ) : NULL;
		s.m_nSpotlightTextureFrame = w.TextureFrame;
		s.m_bEnableShadows = w.EnableShadows != 0;
		s.m_flShadowMapResolution = w.ShadowMapResolution;
		s.m_flShadowAtten = w.ShadowAtten;
		s.m_flShadowSlopeScaleDepthBias = w.ShadowSlopeScaleDepthBias;
		s.m_flShadowDepthBias = w.ShadowDepthBias;
	}

	enum { FLASHLIGHT_STATE_WORDS = sizeof( SourceSharpFlashlightState ) / 4 };
	static_assert( sizeof( SourceSharpFlashlightState ) == 25 * 4, "the flashlight state is 25 words" );

	IMaterial *Material( const SSShadowEnv &env, int32_t id ) { return env.pfnMaterial ? env.pfnMaterial( id ) : NULL; }

	ITexture *Texture( const SSShadowEnv &env, int32_t id ) { return env.pfnTexture ? env.pfnTexture( id ) : NULL; }

	// One record; false when it was short (the run ends).
	bool Execute( const SSShadowEnv &env, int32_t kind, Payload &in, Answers &out, Context &ctx )
	{
		IShadowMgr *pShadowMgr = env.ppShadowMgr ? *env.ppShadowMgr : NULL;
		IStaticPropMgrClient *pStaticProps = env.ppStaticPropMgr ? *env.ppStaticPropMgr : NULL;
		IMaterialSystem *pMaterials = env.ppMaterials ? *env.ppMaterials : NULL;
		IVEngineClient *pEngine = env.ppEngine ? *env.ppEngine : NULL;
		IVModelInfoClient *pModelInfo = env.ppModelInfo ? *env.ppModelInfo : NULL;

		switch ( kind )
		{
		//---------------------------------------------------------------------
		// IShadowMgr and the static prop manager.
		//---------------------------------------------------------------------
		case SS_SHADOW_DestroyShadow:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->DestroyShadow( h );
			break;
		}
		case SS_SHADOW_SetShadowMaterial:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			IMaterial *pMaterial = Material( env, in.I() );
			IMaterial *pModelMaterial = Material( env, in.I() );
			void *pProxy = in.Ptr();
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->SetShadowMaterial( h, pMaterial, pModelMaterial, pProxy );
			break;
		}
		case SS_SHADOW_ProjectShadow:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			const Vector origin = in.Vec();
			const Vector dir = in.Vec();
			VMatrix worldToShadow;
			in.Matrix( worldToShadow );
			const float sx = in.Fl();
			const float sy = in.Fl();
			const float maxHeight = in.Fl();
			const float falloffOffset = in.Fl();
			const float falloffAmount = in.Fl();
			const Vector caster = in.Vec();
			int nLeaves;
			const int *pLeaves = in.Leaves( nLeaves );
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->ProjectShadow( h, origin, dir, worldToShadow, Vector2D( sx, sy ), nLeaves, pLeaves, maxHeight,
				                           falloffOffset, falloffAmount, caster );
			break;
		}
		case SS_SHADOW_ProjectFlashlight:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			VMatrix worldToShadow;
			in.Matrix( worldToShadow );
			int nLeaves;
			const int *pLeaves = in.Leaves( nLeaves );
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->ProjectFlashlight( h, worldToShadow, nLeaves, pLeaves );
			break;
		}
		case SS_SHADOW_AddShadowToBrushModel:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			model_t *pModel = static_cast< model_t * >( in.Ptr() );
			const Vector origin = in.Vec();
			const QAngle angles = in.Ang();
			if ( !in.bShort && pShadowMgr && pModel )
				pShadowMgr->AddShadowToBrushModel( h, pModel, origin, angles );
			break;
		}
		case SS_SHADOW_RemoveAllShadowsFromBrushModel:
		{
			model_t *pModel = static_cast< model_t * >( in.Ptr() );
			if ( !in.bShort && pShadowMgr && pModel )
				pShadowMgr->RemoveAllShadowsFromBrushModel( pModel );
			break;
		}
		case SS_SHADOW_SetShadowTexCoord:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			const float x = in.Fl();
			const float y = in.Fl();
			const float w = in.Fl();
			const float hh = in.Fl();
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->SetShadowTexCoord( h, x, y, w, hh );
			break;
		}
		case SS_SHADOW_AddShadowToModel:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			const ModelInstanceHandle_t instance = (ModelInstanceHandle_t)in.I();
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->AddShadowToModel( h, instance );
			break;
		}
		case SS_SHADOW_RemoveAllShadowsFromModel:
		{
			const ModelInstanceHandle_t instance = (ModelInstanceHandle_t)in.I();
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->RemoveAllShadowsFromModel( instance );
			break;
		}
		case SS_SHADOW_ClearExtraClipPlanes:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->ClearExtraClipPlanes( h );
			break;
		}
		case SS_SHADOW_AddExtraClipPlane:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			const Vector normal = in.Vec();
			const float dist = in.Fl();
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->AddExtraClipPlane( h, normal, dist );
			break;
		}
		case SS_SHADOW_EnableShadow:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			const bool bEnable = in.I() != 0;
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->EnableShadow( h, bEnable );
			break;
		}
		case SS_SHADOW_SetFalloffBias:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			const unsigned char bias = (unsigned char)in.I();
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->SetFalloffBias( h, bias );
			break;
		}
		case SS_SHADOW_UpdateFlashlightState:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			if ( in.n - in.at < FLASHLIGHT_STATE_WORDS )
			{
				in.bShort = true;
				break;
			}
			const SourceSharpFlashlightState *pWire = reinterpret_cast< const SourceSharpFlashlightState * >( in.p + in.at );
			in.at += FLASHLIGHT_STATE_WORDS;
			FlashlightState_t state;
			StateFromWire( env, *pWire, state );
			if ( pShadowMgr )
				pShadowMgr->UpdateFlashlightState( h, state );
			break;
		}
		case SS_SHADOW_AddFlashlightRenderable:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			IClientRenderable *pRenderable = in.Renderable();
			if ( !in.bShort && pShadowMgr && pRenderable )
				pShadowMgr->AddFlashlightRenderable( h, pRenderable );
			break;
		}
		case SS_SHADOW_SetFlashlightDepthTexture:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			ITexture *pTexture = Texture( env, in.I() );
			const unsigned char stencil = (unsigned char)in.I();
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->SetFlashlightDepthTexture( h, pTexture, stencil );
			break;
		}
		case SS_SHADOW_SetFlashlightRenderState:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			if ( !in.bShort && pShadowMgr )
				pShadowMgr->SetFlashlightRenderState( h );
			break;
		}
		case SS_SHADOW_AddShadowToStaticProp:
		{
			const unsigned short h = (unsigned short)in.I();
			IClientRenderable *pRenderable = in.Renderable();
			if ( !in.bShort && pStaticProps && pRenderable )
				pStaticProps->AddShadowToStaticProp( h, pRenderable );
			break;
		}
		case SS_SHADOW_RemoveAllShadowsFromStaticProp:
		{
			IClientRenderable *pRenderable = in.Renderable();
			if ( !in.bShort && pStaticProps && pRenderable )
				pStaticProps->RemoveAllShadowsFromStaticProp( pRenderable );
			break;
		}

		//---------------------------------------------------------------------
		// The material system.
		//---------------------------------------------------------------------
		case SS_SHADOW_ShutdownMaterial:
		{
			const int32_t id = in.I();
			if ( !in.bShort && env.pfnShutdownMaterial )
				env.pfnShutdownMaterial( id );
			break;
		}
		case SS_SHADOW_ColorModulate:
		{
			IMaterial *pMaterial = Material( env, in.I() );
			const float r = in.Fl();
			const float g = in.Fl();
			const float b = in.Fl();
			if ( !in.bShort && pMaterial )
				pMaterial->ColorModulate( r, g, b );
			break;
		}
		case SS_SHADOW_ShutdownTexture:
		{
			const int32_t id = in.I();
			if ( !in.bShort && env.pfnShutdownTexture )
				env.pfnShutdownTexture( id );
			break;
		}
		case SS_SHADOW_BeginRenderTargetAllocation:
			if ( pMaterials )
				pMaterials->BeginRenderTargetAllocation();
			break;
		case SS_SHADOW_EndRenderTargetAllocation:
			if ( pMaterials )
				pMaterials->EndRenderTargetAllocation();
			break;
		case SS_SHADOW_UncacheUnusedMaterials:
			if ( pMaterials )
				pMaterials->UncacheUnusedMaterials();
			break;
		case SS_SHADOW_AddRestoreFunc:
			if ( env.pfnAddRestoreFunc )
				env.pfnAddRestoreFunc();
			break;
		case SS_SHADOW_RemoveRestoreFunc:
			if ( env.pfnRemoveRestoreFunc )
				env.pfnRemoveRestoreFunc();
			break;

		//---------------------------------------------------------------------
		// The render context.
		//---------------------------------------------------------------------
		case SS_SHADOW_FogMode:
		{
			const int32_t mode = in.I();
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->FogMode( (MaterialFogMode_t)mode );
			break;
		}
		case SS_SHADOW_SetHeightClipMode:
		{
			const int32_t mode = in.I();
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->SetHeightClipMode( (MaterialHeightClipMode_t)mode );
			break;
		}
		case SS_SHADOW_ClearColor4ub:
		{
			const unsigned char r = (unsigned char)in.I();
			const unsigned char g = (unsigned char)in.I();
			const unsigned char b = (unsigned char)in.I();
			const unsigned char a = (unsigned char)in.I();
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->ClearColor4ub( r, g, b, a );
			break;
		}
		case SS_SHADOW_ClearColor3ub:
		{
			const unsigned char r = (unsigned char)in.I();
			const unsigned char g = (unsigned char)in.I();
			const unsigned char b = (unsigned char)in.I();
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->ClearColor3ub( r, g, b );
			break;
		}
		case SS_SHADOW_MatrixMode:
		{
			const int32_t mode = in.I();
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->MatrixMode( (MaterialMatrixMode_t)mode );
			break;
		}
		case SS_SHADOW_PushMatrix:
			if ( ctx.Get() )
				ctx.Get()->PushMatrix();
			break;
		case SS_SHADOW_PopMatrix:
			if ( ctx.Get() )
				ctx.Get()->PopMatrix();
			break;
		case SS_SHADOW_LoadIdentity:
			if ( ctx.Get() )
				ctx.Get()->LoadIdentity();
			break;
		case SS_SHADOW_LoadMatrix:
		{
			VMatrix m;
			in.Matrix( m );
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->LoadMatrix( m );
			break;
		}
		case SS_SHADOW_Scale:
		{
			const float x = in.Fl();
			const float y = in.Fl();
			const float z = in.Fl();
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->Scale( x, y, z );
			break;
		}
		case SS_SHADOW_Ortho:
		{
			const double l = in.D();
			const double t = in.D();
			const double r = in.D();
			const double b = in.D();
			const double zn = in.D();
			const double zf = in.D();
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->Ortho( l, t, r, b, zn, zf );
			break;
		}
		case SS_SHADOW_PushRenderTargetAndViewport:
		{
			ITexture *pTexture = Texture( env, in.I() );
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->PushRenderTargetAndViewport( pTexture );
			break;
		}
		case SS_SHADOW_PopRenderTargetAndViewport:
			if ( ctx.Get() )
				ctx.Get()->PopRenderTargetAndViewport();
			break;
		case SS_SHADOW_ClearBuffers:
		{
			const bool bColor = in.I() != 0;
			const bool bDepth = in.I() != 0;
			const bool bStencil = in.I() != 0;
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->ClearBuffers( bColor, bDepth, bStencil );
			break;
		}
		case SS_SHADOW_Viewport:
		{
			const int32_t x = in.I();
			const int32_t y = in.I();
			const int32_t w = in.I();
			const int32_t h = in.I();
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->Viewport( x, y, w, h );
			break;
		}
		case SS_SHADOW_SetShadowDepthBiasFactors:
		{
			const float slope = in.Fl();
			const float bias = in.Fl();
			if ( !in.bShort && ctx.Get() )
				ctx.Get()->SetShadowDepthBiasFactors( slope, bias );
			break;
		}
		case SS_SHADOW_DrawTextureQuad:
		{
			IMaterial *pMaterial = Material( env, in.I() );
			const float w = in.Fl();
			const float h = in.Fl();
			if ( !in.bShort && env.pfnDrawTextureQuad )
				env.pfnDrawTextureQuad( pMaterial, w, h );
			break;
		}
		case SS_SHADOW_DrawBrushModelShadow:
		{
			IClientRenderable *pRenderable = in.Renderable();
			IVRenderView *pRender = env.ppRender ? *env.ppRender : NULL;
			if ( !in.bShort && pRender && pRenderable )
				pRender->DrawBrushModelShadow( pRenderable );
			break;
		}
		case SS_SHADOW_UpdateShadowDepthTexture:
		{
			ITexture *pRenderTarget = Texture( env, in.I() );
			ITexture *pDepth = Texture( env, in.I() );
			CViewSetup view;
			view.x = in.I();
			view.y = in.I();
			view.width = in.I();
			view.height = in.I();
			view.fov = in.Fl();
			view.fovViewmodel = in.Fl();
			view.origin = in.Vec();
			view.angles = in.Ang();
			view.zNear = in.Fl();
			view.zFar = in.Fl();
			view.zNearViewmodel = in.Fl();
			view.zFarViewmodel = in.Fl();
			view.m_flAspectRatio = in.Fl();
			view.m_bOrtho = in.I() != 0;
			view.m_bDoBloomAndToneMapping = in.I() != 0;
			if ( !in.bShort && env.pfnUpdateShadowDepthTexture )
				env.pfnUpdateShadowDepthTexture( pRenderTarget, pDepth, view );
			break;
		}
		case SS_SHADOW_CreateModelInstance:
		{
			IClientRenderable *pRenderable = in.Renderable();
			if ( !in.bShort && pRenderable )
				pRenderable->CreateModelInstance();
			break;
		}
		case SS_SHADOW_AddLineOverlay:
		{
			const Vector origin = in.Vec();
			const Vector dest = in.Vec();
			const int32_t r = in.I();
			const int32_t g = in.I();
			const int32_t b = in.I();
			const bool bNoDepthTest = in.I() != 0;
			const float duration = in.Fl();
			IVDebugOverlay *pOverlay = env.ppDebugOverlay ? *env.ppDebugOverlay : NULL;
			if ( !in.bShort && pOverlay )
				pOverlay->AddLineOverlay( origin, dest, r, g, b, bNoDepthTest, duration );
			break;
		}
		case SS_SHADOW_AddTextOverlay:
		{
			const Vector origin = in.Vec();
			const float duration = in.Fl();
			const char *pszText = in.Str();
			IVDebugOverlay *pOverlay = env.ppDebugOverlay ? *env.ppDebugOverlay : NULL;
			if ( !in.bShort && pOverlay && pszText )
				pOverlay->AddTextOverlay( origin, duration, "%s", pszText );
			break;
		}

		//---------------------------------------------------------------------
		// Questions.
		//---------------------------------------------------------------------
		case SS_SHADOW_CreateShadowEx:
		{
			IMaterial *pMaterial = Material( env, in.I() );
			IMaterial *pModelMaterial = Material( env, in.I() );
			void *pProxy = in.Ptr();
			const int32_t flags = in.I();
			const ShadowHandle_t h = ( !in.bShort && pShadowMgr ) ? pShadowMgr->CreateShadowEx( pMaterial, pModelMaterial, pProxy, flags )
			                                                     : (ShadowHandle_t)SHADOW_HANDLE_INVALID;
			out.I( h );
			break;
		}
		case SS_SHADOW_GetInfo:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			if ( in.bShort || !pShadowMgr )
			{
				for ( size_t i = 0; i < ( sizeof( ShadowInfo_t ) + 3 ) / 4; ++i )
					out.I( 0 );
				break;
			}
			const ShadowInfo_t &info = pShadowMgr->GetInfo( h );
			out.Words( &info, sizeof( ShadowInfo_t ) );
			break;
		}
		case SS_SHADOW_GetFlashlightFrustum:
		{
			const ShadowHandle_t h = (ShadowHandle_t)in.I();
			if ( in.bShort || !pShadowMgr )
			{
				for ( size_t i = 0; i < ( sizeof( Frustum_t ) + 3 ) / 4; ++i )
					out.I( 0 );
				break;
			}
			const Frustum_t &frustum = pShadowMgr->GetFlashlightFrustum( h );
			out.Words( &frustum, sizeof( Frustum_t ) );
			break;
		}
		case SS_SHADOW_InitMaterial:
		{
			const char *pszName = in.Str();
			const char *pszGroup = in.Str();
			out.I( ( !in.bShort && env.pfnInitMaterial ) ? env.pfnInitMaterial( pszName, pszGroup ) : 0 );
			break;
		}
		case SS_SHADOW_InitRenderTarget:
		{
			const char *pszName = in.Str();
			const int32_t w = in.I();
			const int32_t h = in.I();
			const int32_t sizeMode = in.I();
			const int32_t format = in.I();
			const int32_t depth = in.I();
			const int32_t hdr = in.I();
			out.I( ( !in.bShort && env.pfnInitRenderTarget ) ? env.pfnInitRenderTarget( pszName, w, h, sizeMode, format, depth, hdr ) : 0 );
			break;
		}
		case SS_SHADOW_TextureSize:
		{
			ITexture *pTexture = Texture( env, in.I() );
			out.I( pTexture ? pTexture->GetActualWidth() : 0 );
			out.I( pTexture ? pTexture->GetActualHeight() : 0 );
			break;
		}
		case SS_SHADOW_Hardware:
		{
			IMaterialSystemHardwareConfig *pHardware = env.ppHardwareConfig ? *env.ppHardwareConfig : NULL;
			out.I( pMaterials && pMaterials->SupportsShadowDepthTextures() ? 1 : 0 );
			out.I( pMaterials ? (int32_t)pMaterials->GetShadowDepthTextureFormat() : 0 );
			out.I( pMaterials ? (int32_t)pMaterials->GetNullTextureFormat() : 0 );
			out.I( pHardware ? pHardware->GetDXSupportLevel() : 0 );
			out.I( env.pfnToolsMode ? env.pfnToolsMode() : 0 );
			break;
		}
		case SS_SHADOW_ComputePixelDiameterOfSphere:
		{
			const Vector center = in.Vec();
			const float radius = in.Fl();
			out.Fl( ( !in.bShort && ctx.Get() ) ? ctx.Get()->ComputePixelDiameterOfSphere( center, radius ) : 0.0f );
			break;
		}
		case SS_SHADOW_GetFogMode:
			out.I( ctx.Get() ? (int32_t)ctx.Get()->GetFogMode() : 0 );
			break;
		case SS_SHADOW_GetHeightClipMode:
			out.I( ctx.Get() ? (int32_t)ctx.Get()->GetHeightClipMode() : 0 );
			break;
		case SS_SHADOW_DrawModelShadow:
		{
			IClientRenderable *pRenderable = in.Renderable();
			const int32_t body = in.I();
			const int32_t skin = in.I();
			IVModelRender *pModelRender = env.ppModelRender ? *env.ppModelRender : NULL;
			int32_t drew = 0;
			if ( !in.bShort && pModelRender && pRenderable )
			{
				DrawModelInfo_t info;
				matrix3x4_t *pBoneToWorld = pModelRender->DrawModelShadowSetup( pRenderable, body, skin, &info );
				if ( pBoneToWorld )
				{
					pModelRender->DrawModelShadow( pRenderable, info, pBoneToWorld );
					drew = 1;
				}
			}
			out.I( drew );
			break;
		}
		case SS_SHADOW_CullBox:
		{
			const Vector mins = in.Vec();
			const Vector maxs = in.Vec();
			out.I( ( !in.bShort && pEngine && pEngine->CullBox( mins, maxs ) ) ? 1 : 0 );
			break;
		}
		case SS_SHADOW_AmbientLightColor:
		{
			Vector color( 0, 0, 0 );
			if ( pEngine )
				pEngine->GetAmbientLightColor( color );
			out.Vec( color );
			break;
		}
		case SS_SHADOW_LeavesAlongRay:
		case SS_SHADOW_LeavesInBox:
		{
			const Vector a = in.Vec();
			const Vector b = in.Vec();
			const Vector extents = kind == SS_SHADOW_LeavesAlongRay ? in.Vec() : Vector( 0, 0, 0 );
			ISpatialQuery *pQuery = ( !in.bShort && pEngine ) ? pEngine->GetBSPTreeQuery() : NULL;
			const int32_t at = out.n;
			out.I( 0 );
			CLeafCollector collector( out );
			if ( pQuery && kind == SS_SHADOW_LeavesAlongRay )
			{
				Ray_t ray;
				VectorCopy( a, ray.m_Start );
				VectorCopy( b, ray.m_Delta );
				ray.m_StartOffset.Init( 0, 0, 0 );
				ray.m_Extents = extents;
				ray.m_IsRay = false;
				ray.m_IsSwept = true;
				pQuery->EnumerateLeavesAlongRay( ray, &collector, 0 );
			}
			else if ( pQuery )
			{
				pQuery->EnumerateLeavesInBox( a, b, &collector, 0 );
			}
			if ( at < out.nMax )
				out.p[ at ] = collector.m_nCount;
			break;
		}
		case SS_SHADOW_FrameCount:
		{
			CGlobalVarsBase *pGlobals = env.ppGlobals ? *env.ppGlobals : NULL;
			out.I( pGlobals ? pGlobals->framecount : 0 );
			break;
		}
		case SS_SHADOW_RenderableOf:
		{
			const uint32_t hEntity = (uint32_t)in.I();
			out.Ptr( ( !in.bShort && env.pfnRenderableOf ) ? env.pfnRenderableOf( hEntity ) : NULL );
			break;
		}
		case SS_SHADOW_RenderOrigin:
		{
			IClientRenderable *p = in.Renderable();
			out.Vec( p ? p->GetRenderOrigin() : Vector( 0, 0, 0 ) );
			break;
		}
		case SS_SHADOW_RenderAngles:
		{
			IClientRenderable *p = in.Renderable();
			const QAngle angles = p ? p->GetRenderAngles() : QAngle( 0, 0, 0 );
			out.Fl( angles.x );
			out.Fl( angles.y );
			out.Fl( angles.z );
			break;
		}
		case SS_SHADOW_RenderableToWorld:
		{
			IClientRenderable *p = in.Renderable();
			for ( int r = 0; r < 3; ++r )
				for ( int c = 0; c < 4; ++c )
					out.Fl( p ? p->RenderableToWorldTransform()[ r ][ c ] : 0.0f );
			break;
		}
		case SS_SHADOW_Model:
		{
			IClientRenderable *p = in.Renderable();
			out.Ptr( p ? p->GetModel() : NULL );
			break;
		}
		case SS_SHADOW_ModelType:
		{
			const model_t *pModel = static_cast< const model_t * >( in.Ptr() );
			out.I( ( !in.bShort && pModelInfo ) ? pModelInfo->GetModelType( pModel ) : 0 );
			break;
		}
		case SS_SHADOW_BodySkin:
		{
			IClientRenderable *p = in.Renderable();
			out.I( p ? p->GetBody() : 0 );
			out.I( p ? p->GetSkin() : 0 );
			break;
		}
		case SS_SHADOW_RenderBoundsWorldspace:
		{
			IClientRenderable *p = in.Renderable();
			Vector mins( 0, 0, 0 ), maxs( 0, 0, 0 );
			if ( p )
				p->GetRenderBoundsWorldspace( mins, maxs );
			out.Vec( mins );
			out.Vec( maxs );
			break;
		}
		case SS_SHADOW_ShadowRenderBounds:
		{
			IClientRenderable *p = in.Renderable();
			const ShadowType_t type = (ShadowType_t)in.I();
			Vector mins( 0, 0, 0 ), maxs( 0, 0, 0 );
			if ( p && !in.bShort )
				p->GetShadowRenderBounds( mins, maxs, type );
			out.Vec( mins );
			out.Vec( maxs );
			break;
		}
		case SS_SHADOW_ShouldReceiveProjectedTextures:
		{
			IClientRenderable *p = in.Renderable();
			const int32_t flags = in.I();
			out.I( ( p && !in.bShort && p->ShouldReceiveProjectedTextures( flags ) ) ? 1 : 0 );
			break;
		}
		case SS_SHADOW_ShadowCastDistance:
		{
			IClientRenderable *p = in.Renderable();
			const ShadowType_t type = (ShadowType_t)in.I();
			float dist = in.Fl();
			const bool bAnswered = p && !in.bShort && p->GetShadowCastDistance( &dist, type );
			out.I( bAnswered ? 1 : 0 );
			out.Fl( dist );
			break;
		}
		case SS_SHADOW_ShadowCastDirection:
		{
			IClientRenderable *p = in.Renderable();
			const ShadowType_t type = (ShadowType_t)in.I();
			Vector dir = in.Vec();
			const bool bAnswered = p && !in.bShort && p->GetShadowCastDirection( &dir, type );
			out.I( bAnswered ? 1 : 0 );
			out.Vec( dir );
			break;
		}
		case SS_SHADOW_ShadowParent:
		{
			IClientRenderable *p = in.Renderable();
			out.Ptr( p ? p->GetShadowParent() : NULL );
			break;
		}
		case SS_SHADOW_FirstShadowChild:
		{
			IClientRenderable *p = in.Renderable();
			out.Ptr( p ? p->FirstShadowChild() : NULL );
			break;
		}
		case SS_SHADOW_NextShadowPeer:
		{
			IClientRenderable *p = in.Renderable();
			out.Ptr( p ? p->NextShadowPeer() : NULL );
			break;
		}
		case SS_SHADOW_ModelInstance:
		{
			IClientRenderable *p = in.Renderable();
			out.I( p ? (int32_t)p->GetModelInstance() : (int32_t)MODEL_INSTANCE_INVALID );
			break;
		}
		case SS_SHADOW_NetworkableEntIndex:
		{
			IClientRenderable *p = in.Renderable();
			IClientUnknown *pUnknown = p ? p->GetIClientUnknown() : NULL;
			IClientNetworkable *pNet = pUnknown ? pUnknown->GetClientNetworkable() : NULL;
			out.I( pNet ? pNet->entindex() : -1 );
			break;
		}

		default:
			break;
		}
		return !in.bShort;
	}
}

int32_t SSFwd_Shadows_Run( const SSShadowEnv *pEnv, const int32_t *pOps, int32_t nWords, int32_t *pAnswers, int32_t nAnswerWords )
{
	if ( !pEnv || !pOps || nWords <= 0 )
		return 0;

	Answers out;
	out.p = pAnswers;
	out.nMax = pAnswers ? nAnswerWords : 0;
	out.n = 0;

	Context ctx;
	ctx.pMaterials = pEnv->ppMaterials ? *pEnv->ppMaterials : NULL;
	ctx.pContext = NULL;

	for ( int32_t at = 0; at + 2 <= nWords; )
	{
		const int32_t kind = pOps[ at ];
		const int32_t nPayload = pOps[ at + 1 ];
		if ( nPayload < 0 || nPayload > nWords - at - 2 )
			break;
		Payload in;
		in.p = pOps + at + 2;
		in.n = nPayload;
		in.at = 0;
		in.bShort = false;
		at += 2 + nPayload;
		if ( !Execute( *pEnv, kind, in, out, ctx ) )
			break;
	}

	if ( ctx.pContext )
		ctx.pContext->Release();

	return out.n <= out.nMax ? out.n : -out.n;
}
