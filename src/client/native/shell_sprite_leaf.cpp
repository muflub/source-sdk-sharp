//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the leaf sprite renderer ===========================//
// SS_REASON g  leaf math over mdl/bsp data
//
// plan_entity.md step 7a, and section 6's list of what legitimately stays
// native: *"leaf renderers: brush, studio, SPRITE, beam, rope, world text,
// cracked glass"*. This is that renderer, moved out of game/client/c_sprite.cpp
// so the stock TU could leave client_csharp.vpc.
//
// WHAT MOVED HERE: the r_drawsprites ConVar, DrawSpriteModel (the quad
// itself), StandardGlowBlend and its pixel-visibility query, SpriteAspect, and
// C_SpriteRenderer -- GlowBlend, GetSpriteAxes and DrawSprite.
//
// AND THEN ON (lane c2-fx): every decision among them -- the glow blend and
// fade, the axes, the opaque rule, the quad's corners -- is SpriteRenderer.cs.
// What is left is the executor below the banner further down: it reads the
// sprite model and the renderer for the plan and builds the quad it returns.
//
// WHAT DID NOT MOVE. C_Sprite's two members -- its GlowBlend override and its
// GetToolRecordingState -- are still in game/client/c_sprite.cpp inside
// `#ifndef SOURCESHARP`, because that is the STOCK build's arm and stock still
// needs them. The stock file therefore stays ON DISK, uncompiled, exactly as
// c_te_explosion.cpp (step 7b) and game/server/effects.cpp do. Deleting it
// would be "a compile gate selects for deletion" taken literally.
//
// WHO WANTS WHAT IS HERE, measured with nm over the debug client at the tip
// this moved from -- three objects, and not one of them wants an entity:
//
//   shell_sprite_render.o    C_SpriteRenderer::DrawSprite, StandardGlowBlend,
//                            and the C_SpriteRenderer vtable it derives from
//   c_te_legacytempents.o    DrawSprite and C_SpriteRenderer::GlowBlend, for
//                            C_LocalTempEntity
//   glow_overlay.o           the r_drawsprites ConVar
//
// (The fourth referrer the old banner named, entity_client_tools.o, left with
// step 7g.)
//
//=============================================================================//

#include "cbase.h"
#include "c_sprite.h"
#include "model_types.h"
#include "iviewrender.h"
#include "view.h"
#include "enginesprite.h"
#include "engine/ivmodelinfo.h"
#include "util_shared.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/imaterialsystem.h"
#include "tier1/KeyValues.h"
#include "view_shared.h"
#include "viewrender.h"
#include "shell_effect_ledger.h"
#include "c_pixel_visibility.h"
#include "shell_beam_render.h"         // SourceSharp_PlanSprite
#include "sourcesharp.h"
#include "managed_runtime_client.h"
#include "imanaged_areas_client.h"   // IManagedFxClient::SpritePlan
#include "shell_standalone_client.h"  // SourceSharp_StandaloneDrawBatchBlend
#include "tier1/utlmap.h"
#include "ss_bitblock.h"                // the sprite material block

#include <vector>

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

ConVar	r_drawsprites( "r_drawsprites", "1", FCVAR_CHEAT );

//=============================================================================//
// THE SPRITE RENDERER IS AN EXECUTOR (lane c2-fx, plan_client_novalve.md §2
// cluster 4). Whether a sprite draws, the blend and colour it draws with, its
// axes and the four corners of its quad are SpriteRenderer's decisions
// (src/client/managed/Effects/SpriteRenderer.cs), recorded off this file
// before they moved (sprite-stock.tsv.gz) and replayed bit for bit by
// ClientSpriteRendererTests. What stays is what the managed half cannot do:
// read the sprite model and the renderer, answer its two glow questions on
// this renderer's query handle, and build the mesh.
//
// TWO PATHS TO ONE PLAN. The request is floats (SS_SPRITE_IN_*), the plan is
// floats (SS_SPRITE_OUT_*), and the glow answers are asked from inside the plan
// through IShimFxClient's FxSpriteGlowFraction / FxGlowSightDistance, which
// read the query handle left in s_pPlanQuery.
//   * A draw native starts (a tempent, C_SpriteRenderer) crosses into managed
//     once for the plan: IManagedFxClient::SpritePlan (SourceSharp_PlanSprite).
//   * A draw MANAGED starts (a managed sprite, from inside its owner's
//     ClientStandaloneDraw) does not cross back: FxSpriteDrawBegin hands it the
//     request, it plans in managed, and FxSpriteDrawEnd draws the plan. The
//     render budget (live gate `render`) is why; ClientSpriteRendererTests
//     holds the two paths to the same plan, bit for bit.
//
// NO MANAGED HALF, NO SPRITE. Outside a load there is nothing to decide with,
// and this file must not decide in its place.
//=============================================================================//

// The request (SpriteRenderer.cs, ClientSpriteHost.In*).
enum
{
	SS_SPRITE_IN_DRAW_SPRITES = 0, SS_SPRITE_IN_WIREFRAME_MODE, SS_SPRITE_IN_ORIENTATION,
	SS_SPRITE_IN_LEFT, SS_SPRITE_IN_RIGHT, SS_SPRITE_IN_UP, SS_SPRITE_IN_DOWN,
	SS_SPRITE_IN_WIDTH, SS_SPRITE_IN_HEIGHT,
	SS_SPRITE_IN_ORIGIN, SS_SPRITE_IN_EFFECT_ORIGIN = SS_SPRITE_IN_ORIGIN + 3, SS_SPRITE_IN_ANGLES = SS_SPRITE_IN_EFFECT_ORIGIN + 3,
	SS_SPRITE_IN_RENDER_MODE = SS_SPRITE_IN_ANGLES + 3, SS_SPRITE_IN_RENDER_FX, SS_SPRITE_IN_ALPHA,
	SS_SPRITE_IN_R, SS_SPRITE_IN_G, SS_SPRITE_IN_B, SS_SPRITE_IN_SCALE, SS_SPRITE_IN_WORLD_SPACE_SCALE,
	SS_SPRITE_IN_HDR_COLOR_SCALE, SS_SPRITE_IN_BLEND,
	SS_SPRITE_IN_VIEW_FORWARD, SS_SPRITE_IN_VIEW_RIGHT = SS_SPRITE_IN_VIEW_FORWARD + 3, SS_SPRITE_IN_VIEW_UP = SS_SPRITE_IN_VIEW_RIGHT + 3,
	SS_SPRITE_IN_GLOW_PROXY_SIZE = SS_SPRITE_IN_VIEW_UP + 3,
	// The renderer's pixel visibility handle (an int, its bits in the float),
	// which managed's pixel visibility asks with (C7).
	SS_SPRITE_IN_QUERY_HANDLE,
	SS_SPRITE_IN_FLOATS,
};
COMPILE_TIME_ASSERT( SS_SPRITE_IN_FLOATS == 39 );

// The plan.
enum
{
	SS_SPRITE_OUT_DRAWN = 0, SS_SPRITE_OUT_SET_BLEND, SS_SPRITE_OUT_BLEND, SS_SPRITE_OUT_QUAD, SS_SPRITE_OUT_WIREFRAME,
	SS_SPRITE_OUT_RGBA, SS_SPRITE_OUT_CORNERS = SS_SPRITE_OUT_RGBA + 4, SS_SPRITE_OUT_HDR_COLOR_SCALE = SS_SPRITE_OUT_CORNERS + 12,
	// The handle as the question left it, stored back on the renderer.
	SS_SPRITE_OUT_QUERY_HANDLE,
	SS_SPRITE_OUT_FLOATS,
};
COMPILE_TIME_ASSERT( SS_SPRITE_OUT_FLOATS == 23 );

static float HandleBits( pixelvis_handle_t h )
{
	float f;
	memcpy( &f, &h, sizeof( f ) );
	return f;
}

static pixelvis_handle_t HandleOf( float f )
{
	pixelvis_handle_t h;
	memcpy( &h, &f, sizeof( h ) );
	return h;
}

COMPILE_TIME_ASSERT( sizeof( pixelvis_handle_t ) == sizeof( float ) );

// The renderer whose sprite is being planned: its query handle, for the glow
// question asked from inside the plan. Sprites are drawn one at a time on the
// render thread, so one slot is enough; it is NULL outside a plan.
static pixelvis_handle_t *s_pPlanQuery = NULL;

// The blend the last request was gathered under (the batched path keeps it with its plan).
static float s_flDirectBlend = 0.0f;

static void PutVector( float *p, const Vector &v ) { p[0] = v.x; p[1] = v.y; p[2] = v.z; }

static unsigned int s_nHDRColorScaleCache = 0;

// One sprite between its request and its plan: what the draw needs that the
// plan does not carry.
struct SpritePending_t
{
	CEngineSprite   *pSprite;
	const model_t   *pModel;
	IClientEntity   *pEntity;
	float            flFrame;
	int              nRenderMode;
};

// Read the sprite model and the renderer into the request. False when there is
// nothing to plan (no sprite model, no managed half).
static bool GatherSprite( IClientEntity *entity, const model_t *model,
	const Vector &origin, const QAngle &angles, float frame, IClientEntity *attachedto, int attachmentindex,
	int rendermode, int renderfx, int alpha, int r, int g, int b, float scale, bool bWorldSpaceScale,
	float flHDRColorScale, float flGlowProxySize, float *in, SpritePending_t &pending )
{
	// Nothing to read without a sprite model.
	if ( !model || modelinfo->GetModelType( model ) != mod_sprite )
		return false;

	CEngineSprite *psprite = (CEngineSprite *)modelinfo->GetModelExtraData( model );
	if ( !psprite )
		return false;

	Vector effect_origin;
	VectorCopy( origin, effect_origin );

	// Use attachment point
	if ( attachedto )
	{
		QAngle temp;
		attachedto->GetAttachment( attachmentindex, effect_origin, temp );
	}

	in[ SS_SPRITE_IN_DRAW_SPRITES ]      = (float)r_drawsprites.GetInt();
	in[ SS_SPRITE_IN_WIREFRAME_MODE ]    = ShouldDrawInWireFrameMode() ? 1.0f : 0.0f;
	in[ SS_SPRITE_IN_ORIENTATION ]       = (float)psprite->GetOrientation();
	in[ SS_SPRITE_IN_LEFT ]              = psprite->GetLeft();
	in[ SS_SPRITE_IN_RIGHT ]             = psprite->GetRight();
	in[ SS_SPRITE_IN_UP ]                = psprite->GetUp();
	in[ SS_SPRITE_IN_DOWN ]              = psprite->GetDown();
	in[ SS_SPRITE_IN_WIDTH ]             = (float)modelinfo->GetModelSpriteWidth( model );
	in[ SS_SPRITE_IN_HEIGHT ]            = (float)modelinfo->GetModelSpriteHeight( model );
	PutVector( in + SS_SPRITE_IN_ORIGIN, origin );
	PutVector( in + SS_SPRITE_IN_EFFECT_ORIGIN, effect_origin );
	in[ SS_SPRITE_IN_ANGLES + 0 ]        = angles.x;
	in[ SS_SPRITE_IN_ANGLES + 1 ]        = angles.y;
	in[ SS_SPRITE_IN_ANGLES + 2 ]        = angles.z;
	in[ SS_SPRITE_IN_RENDER_MODE ]       = (float)rendermode;
	in[ SS_SPRITE_IN_RENDER_FX ]         = (float)renderfx;
	in[ SS_SPRITE_IN_ALPHA ]             = (float)alpha;
	in[ SS_SPRITE_IN_R ]                 = (float)r;
	in[ SS_SPRITE_IN_G ]                 = (float)g;
	in[ SS_SPRITE_IN_B ]                 = (float)b;
	in[ SS_SPRITE_IN_SCALE ]             = scale;
	in[ SS_SPRITE_IN_WORLD_SPACE_SCALE ] = bWorldSpaceScale ? 1.0f : 0.0f;
	in[ SS_SPRITE_IN_HDR_COLOR_SCALE ]   = flHDRColorScale;
	// The blend the sprite's DrawModel draws under: the render's own, or, while
	// managed prepares a view's draw batch, the one that entity's DrawModel will
	// find (shell_standalone_client.cpp).
	in[ SS_SPRITE_IN_BLEND ]             = SourceSharp_StandaloneDrawBatchBlend( entity, render->GetBlend() );
	s_flDirectBlend                      = in[ SS_SPRITE_IN_BLEND ];
	PutVector( in + SS_SPRITE_IN_VIEW_FORWARD, CurrentViewForward() );
	PutVector( in + SS_SPRITE_IN_VIEW_RIGHT, CurrentViewRight() );
	PutVector( in + SS_SPRITE_IN_VIEW_UP, CurrentViewUp() );
	in[ SS_SPRITE_IN_GLOW_PROXY_SIZE ]   = flGlowProxySize;

	pending.pSprite     = psprite;
	pending.pModel      = model;
	pending.pEntity     = entity;
	pending.flFrame     = frame;
	pending.nRenderMode = rendermode;
	return true;
}

// Carry out a plan: the blend, then the quad.
static int ExecuteSprite( const SpritePending_t &pending, const float *out )
{
	if ( out[ SS_SPRITE_OUT_SET_BLEND ] != 0.0f )
		render->SetBlend( out[ SS_SPRITE_OUT_BLEND ] );

	if ( out[ SS_SPRITE_OUT_QUAD ] != 0.0f )
	{
		IMaterial *material = pending.pSprite->GetMaterial( (RenderMode_t)pending.nRenderMode, pending.flFrame );
		if ( material )
		{
			CMatRenderContextPtr pRenderContext( materials );
			if ( out[ SS_SPRITE_OUT_WIREFRAME ] != 0.0f )
			{
				IMaterial *pMaterial = materials->FindMaterial( "debug/debugspritewireframe", TEXTURE_GROUP_OTHER );
				pRenderContext->Bind( pMaterial, NULL );
			}
			else
			{
				pRenderContext->Bind( material, (IClientRenderable*)pending.pEntity );
			}

			IMaterialVar *pHDRColorScaleVar = material->FindVarFast( "$HDRCOLORSCALE", &s_nHDRColorScaleCache );
			if( pHDRColorScaleVar )
			{
				const float flHDR = out[ SS_SPRITE_OUT_HDR_COLOR_SCALE ];
				pHDRColorScaleVar->SetVecValue( flHDR, flHDR, flHDR );
			}

			unsigned char color[4];
			for ( int i = 0; i < 4; ++i )
				color[i] = (unsigned char)out[ SS_SPRITE_OUT_RGBA + i ];

			float flMinU, flMinV, flMaxU, flMaxV;
			pending.pSprite->GetTexCoordRange( &flMinU, &flMinV, &flMaxU, &flMaxV );
			const float uv[4][2] = { { flMinU, flMaxV }, { flMinU, flMinV }, { flMaxU, flMinV }, { flMaxU, flMaxV } };

			IMesh* pMesh = pRenderContext->GetDynamicMesh();
			CMeshBuilder meshBuilder;
			meshBuilder.Begin( pMesh, MATERIAL_QUADS, 1 );
			for ( int i = 0; i < 4; ++i )
			{
				meshBuilder.Color4ubv( color );
				meshBuilder.TexCoord2f( 0, uv[i][0], uv[i][1] );
				meshBuilder.Position3fv( out + SS_SPRITE_OUT_CORNERS + i * 3 );
				meshBuilder.AdvanceVertex();
			}
			meshBuilder.End();
			pMesh->Draw();

			// Step 7a's gate: counted AFTER the quad has been built and drawn.
			g_SourceSharpEffects.nSpriteQuads++;
		}
	}

	return out[ SS_SPRITE_OUT_DRAWN ] != 0.0f ? 1 : 0;
}

// THE CALLBACK PATH: a draw native starts (a tempent's, C_SpriteRenderer's)
// asks the managed planner once through IManagedFxClient::SpritePlan.
int SourceSharp_PlanSprite( pixelvis_handle_t *pQuery, IClientEntity *entity, const model_t *model,
	const Vector &origin, const QAngle &angles, float frame, IClientEntity *attachedto, int attachmentindex,
	int rendermode, int renderfx, int alpha, int r, int g, int b, float scale, bool bWorldSpaceScale,
	float flHDRColorScale, float flGlowProxySize )
{

	// Nothing to decide with without the managed half.
	IManagedFxClient *pFx = ManagedFxClient();
	if ( !pFx )
		return 0;

	float in[ SS_SPRITE_IN_FLOATS ];
	SpritePending_t pending;
	if ( !GatherSprite( entity, model, origin, angles, frame, attachedto, attachmentindex, rendermode, renderfx,
	                    alpha, r, g, b, scale, bWorldSpaceScale, flHDRColorScale, flGlowProxySize, in, pending ) )
		return 0;
	in[ SS_SPRITE_IN_QUERY_HANDLE ] = HandleBits( pQuery ? *pQuery : 0 );

	float out[ SS_SPRITE_OUT_FLOATS ];
	s_pPlanQuery = pQuery;
	const int32_t nPlanned = pFx->SpritePlan( in, SS_SPRITE_IN_FLOATS, out, SS_SPRITE_OUT_FLOATS );
	s_pPlanQuery = NULL;

	if ( nPlanned != SS_SPRITE_OUT_FLOATS )
		return 0;
	if ( pQuery )
		*pQuery = HandleOf( out[ SS_SPRITE_OUT_QUERY_HANDLE ] );

	return ExecuteSprite( pending, out );
}

// THE DIRECT PATH: a draw MANAGED starts (a managed sprite's, from inside its
// owner's ClientStandaloneDraw) plans in managed between these two calls, so
// the plan costs no crossing back. Begin reads the request and arms the glow
// query for the planner's questions; End carries the plan out (or, handed
// NULL, drops the pending draw). One sprite at a time, on the render thread.
static SpritePending_t s_DirectPending;
static bool s_bDirectPending = false;

int SourceSharp_SpriteDrawBegin( pixelvis_handle_t *pQuery, IClientEntity *entity, const model_t *model,
	const Vector &origin, const QAngle &angles, float frame, int rendermode, int renderfx, int alpha,
	int r, int g, int b, float scale, bool bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize,
	float *pIn )
{
	s_bDirectPending = false;
	s_pPlanQuery = NULL;

	if ( !pIn || !GatherSprite( entity, model, origin, angles, frame, NULL, 0, rendermode, renderfx, alpha,
	                            r, g, b, scale, bWorldSpaceScale, flHDRColorScale, flGlowProxySize, pIn,
	                            s_DirectPending ) )
		return 0;
	pIn[ SS_SPRITE_IN_QUERY_HANDLE ] = HandleBits( pQuery ? *pQuery : 0 );

	s_bDirectPending = true;
	s_pPlanQuery = pQuery;
	return SS_SPRITE_IN_FLOATS;
}

// The direct path's handle, as the plan left it, back on the renderer.
static void StorePlanQuery( const float *pOut )
{
	if ( s_pPlanQuery && pOut )
		*s_pPlanQuery = HandleOf( pOut[ SS_SPRITE_OUT_QUERY_HANDLE ] );
}

int32_t SourceSharp_FxSpriteDrawEnd( const float *pOut )
{

	const bool bPending = s_bDirectPending;
	StorePlanQuery( pOut );
	s_bDirectPending = false;
	s_pPlanQuery = NULL;

	if ( !bPending || !pOut )
		return 0;

	return ExecuteSprite( s_DirectPending, pOut );
}

// THE BATCHED PATH (C7 item 7): the direct path's plan kept instead of drawn,
// against its entity, with the blend it was planned under; DrawModel carries
// it out when its turn comes (shell_standalone_client.cpp's draw batch).
struct SpriteStored_t
{
	SpritePending_t pending;
	float           out[ SS_SPRITE_OUT_FLOATS ];
	float           flBlend;
};

static CUtlMap<IClientEntity *, SpriteStored_t> s_StoredSprites( DefLessFunc( IClientEntity * ) );

int32_t SourceSharp_FxSpriteDrawStore( const float *pOut )
{
	const bool bPending = s_bDirectPending;
	StorePlanQuery( pOut );
	s_bDirectPending = false;
	s_pPlanQuery = NULL;

	if ( !bPending || !pOut )
		return 0;

	SpriteStored_t stored;
	stored.pending = s_DirectPending;
	memcpy( stored.out, pOut, sizeof( stored.out ) );
	stored.flBlend = s_flDirectBlend;
	s_StoredSprites.InsertOrReplace( s_DirectPending.pEntity, stored );
	return 1;
}

int SourceSharp_SpriteExecuteStored( IClientEntity *pEntity, float flBlendNow )
{
	const unsigned short i = s_StoredSprites.Find( pEntity );
	if ( i == s_StoredSprites.InvalidIndex() )
		return -2;
	const SpriteStored_t stored = s_StoredSprites[i];
	s_StoredSprites.RemoveAt( i );
	if ( stored.flBlend != flBlendNow )
		return -1;

	return ExecuteSprite( stored.pending, stored.out );
}

void SourceSharp_SpriteStoredClear()
{
	s_StoredSprites.RemoveAll();
}

// IShimFxClient: the two glow answers, for the sprite being planned.
float SourceSharp_FxSpriteGlowFraction( const float *pPositionXYZ, float flProxySize, float flProxyAspect )
{
	if ( !s_pPlanQuery || !pPositionXYZ )
		return 0.0f;

	pixelvis_queryparams_t params;
	params.Init( Vector( pPositionXYZ[0], pPositionXYZ[1], pPositionXYZ[2] ), flProxySize, flProxyAspect );
	return PixelVisibility_FractionVisible( params, s_pPlanQuery );
}

float SourceSharp_FxGlowSightDistance( const float *pPositionXYZ )
{
	if ( !pPositionXYZ )
		return 0.0f;

	return GlowSightDistance( Vector( pPositionXYZ[0], pPositionXYZ[1], pPositionXYZ[2] ), false );
}

// C_SpriteRenderer's two, for C_LocalTempEntity (c_te_legacytempents.cpp)
// and anything else on the stock base: the stock proxy size, a scale that is
// already a multiple of the sprite's size.
int C_SpriteRenderer::DrawSprite( 
	IClientEntity *entity,
	const model_t *model, 
	const Vector& origin, 
	const QAngle& angles,
	float frame,
	IClientEntity *attachedto,
	int attachmentindex,
	int rendermode,
	int renderfx,
	int alpha,
	int r, 
	int g, 
	int b,
	float scale,
	float flHDRColorScale
	)
{
	return SourceSharp_PlanSprite( &m_queryHandle, entity, model, origin, angles, frame, attachedto, attachmentindex,
		rendermode, renderfx, alpha, r, g, b, scale, /*bWorldSpaceScale=*/false, flHDRColorScale,
		PIXELVIS_DEFAULT_PROXY_SIZE );
}

// Sprite.h declares it virtual, so C_LocalTempEntity's vtable names it. Nothing
// calls it any more: the glow blend is SpriteRenderer's, asked inside the plan.
float C_SpriteRenderer::GlowBlend( CEngineSprite *, const Vector &, int, int, int, float * )
{
	return 0.0f;
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
// SourceSharp: C_Sprite's, and out of this build with the class. See the banner
// above CSprite::GlowBlend. Nothing replaces it: tool recording is SFM's capture
// path, a managed sprite is not an entity and has no GetToolRecordingState to
// override, and the tool framework never walks the parallel hierarchy.


//=============================================================================//
// THE ENGINE'S SPRITE MODEL (CEngineSprite), which spritemodel.cpp defined.
//
// The engine allocates one of these per sprite model (GetSpriteSize answers
// its size) and has the client fill it (InitSprite/ShutdownSprite): its
// layout is enginesprite.h's and the engine holds the memory, so it is native
// by binding. What it holds is the material system's: one procedural material
// per render mode, made from the sprite's .vmt with $spriteRenderMode set, its
// mapping size and frame count, the $spriteorientation it asks to be drawn
// with, and the quad's extents from $spriteorigin.
//
// spritemodel.cpp's HUD half (DrawFrame, DrawFrameOfSize, the scissor
// helpers) had no caller in this build and went with the file. So did the
// video arm (a .bik/.webm sprite played through IVideoServices): the client
// video system is deleted (user ruling 2026-10-04, plan_client_novalve), so a
// video name is looked up as a material like any other and refused with
// stock's warning when it is not one.
//=============================================================================//

static unsigned int s_nSpriteFrameCache;
static unsigned int s_nSpriteOrientationCache;
static unsigned int s_nSpriteOriginCache;

// kRenderNone and kRenderEnvironmental draw nothing and get no material.
static bool SpriteModeHasMaterial( int nMode )
{
	return nMode != kRenderNone && nMode != kRenderEnvironmental;
}

//-----------------------------------------------------------------------------
// The sprite's material file is read in C# (C9 KeyValues K4:
// IManagedMaterialFilesClient::SpriteMaterial, SpriteMaterialFiles and
// KeyValuesLoad): managed decides the material's name from the sprite's and
// loads its .vmt as tier1 would, and answers both in one BitBlock -- the name
// NUL-terminated, then the .vmt's first root in preorder, each key a type
// byte, its name, its value by type (a string NUL-terminated, int32, float32
// or uint64) and a uint32 child count. Native rebuilds that tree as the
// KeyValues the material system takes, once per render mode.
//-----------------------------------------------------------------------------
enum { kSpriteBlockBytes = 64 * 1024, kSpriteMaxDepth = 128 };

namespace
{
struct SpriteBlockReader
{
	const uint8_t *m_p;
	const uint8_t *m_pEnd;
	bool m_bOk;

	const char *CString()
	{
		const uint8_t *pNul = m_p;
		while ( pNul < m_pEnd && *pNul )
			pNul++;
		if ( pNul >= m_pEnd )
		{
			m_bOk = false;
			return "";
		}
		const char *psz = (const char *)m_p;
		m_p = pNul + 1;
		return psz;
	}

	bool Bytes( void *pOut, int n )
	{
		if ( m_pEnd - m_p < n )
			return m_bOk = false;
		memcpy( pOut, m_p, n );
		m_p += n;
		return true;
	}
};

// One key and its children, or NULL when the block is malformed.
KeyValues *ReadSpriteKey( SpriteBlockReader &r, int nDepth )
{
	uint8_t nType = 0;
	if ( nDepth > kSpriteMaxDepth || !r.Bytes( &nType, 1 ) )
		return NULL;
	const char *pszName = r.CString();
	if ( !r.m_bOk )
		return NULL;

	KeyValues *kv = new KeyValues( pszName );
	switch ( nType )
	{
	case KeyValues::TYPE_STRING: kv->SetStringValue( r.CString() ); break;
	case KeyValues::TYPE_INT: { int32_t n = 0; if ( r.Bytes( &n, 4 ) ) kv->SetInt( NULL, n ); break; }
	case KeyValues::TYPE_FLOAT: { float fl = 0; if ( r.Bytes( &fl, 4 ) ) kv->SetFloat( NULL, fl ); break; }
	case KeyValues::TYPE_UINT64: { uint64_t u = 0; if ( r.Bytes( &u, 8 ) ) kv->SetUint64( NULL, u ); break; }
	case KeyValues::TYPE_NONE: break;
	default: r.m_bOk = false; break;
	}

	uint32_t nChildren = 0;
	r.Bytes( &nChildren, 4 );
	for ( uint32_t i = 0; r.m_bOk && i < nChildren; i++ )
	{
		KeyValues *pChild = ReadSpriteKey( r, nDepth + 1 );
		if ( pChild )
			kv->AddSubKey( pChild );
	}
	if ( !r.m_bOk )
	{
		kv->deleteThis();
		return NULL;
	}
	return kv;
}
} // namespace

bool CEngineSprite::Init( const char *pName )
{
	m_VideoMaterial = NULL;
	m_width = m_height = m_numFrames = 1;
	for ( int nMode = 0; nMode < kRenderModeCount; nMode++ )
		m_material[nMode] = NULL;

	IManagedMaterialFilesClient *pFiles = ManagedMaterialFilesClient();
	if ( !pFiles )
	{
		Warning( "[SourceSharp] sprite %s: no managed load to read its material\n", pName );
		return false;
	}

	std::vector<uint8_t> block( ss::kBitBlockHeaderBytes + kSpriteBlockBytes );
	const int nBytes = pFiles->SpriteMaterial( pName, block.data(), (int32_t)block.size() );
	if ( nBytes == -2 )
		Warning( "[SourceSharp] sprite %s: its material file is over %d bytes as a block; not loaded\n", pName, (int)kSpriteBlockBytes );
	if ( nBytes < 0 )
		return false;

	const uint8_t *pPayload = block.data() + ss::kBitBlockHeaderBytes;
	SpriteBlockReader head = { pPayload, pPayload + nBytes, true };
	char szMaterial[MAX_PATH];
	Q_strncpy( szMaterial, head.CString(), sizeof( szMaterial ) );
	const uint8_t *pTree = head.m_p;
	if ( !head.m_bOk )
		return false;

	for ( int nMode = 0; nMode < kRenderModeCount; nMode++ )
	{
		if ( !SpriteModeHasMaterial( nMode ) )
			continue;

		SpriteBlockReader r = { pTree, pPayload + nBytes, true };
		KeyValues *pModeVmt = ReadSpriteKey( r, 0 );
		if ( !pModeVmt )
		{
			Warning( "[SourceSharp] sprite %s: its material block is malformed\n", pName );
			return false;
		}
		pModeVmt->SetInt( "$spriteRenderMode", nMode );

		char szProcedural[MAX_PATH];
		Q_snprintf( szProcedural, sizeof( szProcedural ), "%s_rendermode_%d", szMaterial, nMode );
		m_material[nMode] = g_pMaterialSystem->FindProceduralMaterial( szProcedural, TEXTURE_GROUP_CLIENT_EFFECTS,
		                                                               pModeVmt );
		if ( !m_material[nMode] )
			return false;
		m_material[nMode]->IncrementReferenceCount();
	}

	IMaterial *pFirst = m_material[0];
	m_width = pFirst->GetMappingWidth();
	m_height = pFirst->GetMappingHeight();
	m_numFrames = pFirst->GetNumAnimationFrames();

	IMaterialVar *pOrientation = pFirst->FindVarFast( "$spriteorientation", &s_nSpriteOrientationCache );
	m_orientation = pOrientation ? pOrientation->GetIntValue() : C_SpriteRenderer::SPR_VP_PARALLEL_UPRIGHT;

	// The quad's anchor as a fraction of its size: $spriteorigin when it is a
	// vector, the centre otherwise.
	float flAnchorX = 0.5f, flAnchorY = 0.5f;
	IMaterialVar *pOrigin = pFirst->FindVarFast( "$spriteorigin", &s_nSpriteOriginCache );
	if ( pOrigin && pOrigin->GetType() == MATERIAL_VAR_TYPE_VECTOR )
	{
		float rgflAnchor[3];
		pOrigin->GetVecValue( rgflAnchor, 3 );
		flAnchorX = rgflAnchor[0];
		flAnchorY = rgflAnchor[1];
	}

	left = -m_width * flAnchorX;
	up = m_height * flAnchorY;
	right = m_width + left;
	down = up - m_height;
	return true;
}

void CEngineSprite::Shutdown( void )
{
	UnloadMaterial();
}

bool CEngineSprite::IsVideo()
{
	return m_VideoMaterial != NULL;
}

// Half a texel in from each edge, so the quad's border samples its own texels.
void CEngineSprite::GetTexCoordRange( float *pMinU, float *pMinV, float *pMaxU, float *pMaxV )
{
	*pMinU = 0.5f * ( m_width != 0 ? 1.0f / m_width : 1.0f );
	*pMinV = 0.5f * ( m_height != 0 ? 1.0f / m_height : 1.0f );
	*pMaxU = 1.0f - *pMinU;
	*pMaxV = 1.0f - *pMinV;
}

void CEngineSprite::SetFrame( RenderMode_t nRenderMode, int nFrame )
{
	IMaterial *pMaterial = m_material[nRenderMode];
	if ( !pMaterial )
		return;
	if ( IMaterialVar *pFrame = pMaterial->FindVarFast( "$frame", &s_nSpriteFrameCache ) )
		pFrame->SetIntValue( nFrame );
}

IMaterial *CEngineSprite::GetMaterial( RenderMode_t nRenderMode, int nFrame )
{
	if ( !SpriteModeHasMaterial( nRenderMode ) )
		return NULL;
	SetFrame( nRenderMode, nFrame );
	return m_material[nRenderMode];
}

int CEngineSprite::GetOrientation( void )
{
	return m_orientation;
}

void CEngineSprite::UnloadMaterial( void )
{
	for ( int nMode = 0; nMode < kRenderModeCount; nMode++ )
	{
		if ( m_material[nMode] )
		{
			m_material[nMode]->DecrementReferenceCount();
			m_material[nMode] = NULL;
		}
	}
}
