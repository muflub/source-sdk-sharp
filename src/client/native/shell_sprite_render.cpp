//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — drawing the managed sprites ========================//
// SS_REASON g  leaf math over mdl/bsp data
//
// env_sprite, env_sprite_oriented and env_glow, drawn for entities on the
// parallel hierarchy. The sixth of these shells, and the FIRST that does not
// keep a stock client entity alive.
//
// WHY IT HAS TO EXIST AT ALL, which is the finding the whole particle and
// sprite family turns on. Stock's env_sprite draws because the server networks
// a CSprite and the engine builds the C_Sprite paired with DT_Sprite. A managed
// entity is never on the wire as that class: every one of them arrives as one
// of the six tier tables in generic_nettable.h, so DT_Sprite is simply not sent
// any more and the client class is never instantiated. Move the classname
// without something here and the entity spawns, networks, answers every query
// correctly through debug-api payload -- and draws nothing at all.
//
// WHY THIS ONE BUILDS NO C_Sprite, where the steam jet, the embers, the six
// trails, the smokestack and the world text all build their stock class and
// feed it. Those five have a client half that SIMULATES: a puff spawner, an
// ember drifting, a trail's point history, a font page of glyph quads. Keeping
// the object is the only way to keep the simulation.
//
// A sprite simulates nothing. C_SpriteRenderer::DrawSprite is a pure function
// of the values passed to it -- bind the material, work out three axes, build
// one quad -- and every one of those values is a networked field the managed
// half already holds decoded. What the OBJECT would add is exactly two things:
//
//   * m_flStartScale/m_flDestScale and their brightness twins, which interpolate
//     a SetScale( scale, duration ) ramp. No map can start one: "SetScale" is a
//     DEFINE_INPUT straight onto m_flSpriteScale with no duration, and the
//     two-argument setters are called only from C++ helpers (FadeAndDie,
//     AnimateAndDie) that no compiled caller in this build reaches. Both
//     m_flScaleTime and m_flBrightnessTime are zero for every sprite a map can
//     place, and GetRenderScale/GetRenderBrightness then return the networked
//     value unchanged.
//
//   * m_queryHandle, the pixel-visibility query behind the glow modes' distance
//     fade. That one is real, and it is the only thing this file keeps: a query
//     handle has to be the SAME handle frame after frame or the occlusion test
//     never settles, so there is one per managed sprite in the table below.
//
// So the renderer is kept and the entity is not, which is the smaller half.
//
// THE PLACE IS NOT SWEPT PER FRAME HERE, and that is the one way this shell is
// simpler than every other. The other five draw from a system's PreRender and
// have to chase their owner's origin, because their stock object holds its own
// copy of it. This draws from inside the owner's own DrawModel, so the origin
// it is handed is the owner's interpolated origin for the frame being drawn --
// there is nothing to follow.
//
//=============================================================================//

#include <stdio.h>
#include <stdlib.h>
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "Sprite.h"
#include "view.h"
#include "engine/ivmodelinfo.h"
#include "engine/IClientLeafSystem.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "tier0/dbg.h"
#include "tier0/platform.h"
#include "tier0/wchartypes.h"
#include "tier1/utlvector.h"
#include "cdll_client_int.h"
#include "const.h"
#include "globalvars_base.h"
#include "model_types.h"
#include "networkvar.h"
#include "shell_standalone_client.h"
#include "shell_renderable.h"
#include "shell_beam_render.h"
#include "shell_particle_systems.h"

#include "shell_effect_registry.h"
#include "imanaged_areas_client.h"   // IManagedFxClient: the sprite plan and the trail

// memdbgon must be the last include file in a .cpp file!!!

//-----------------------------------------------------------------------------
// THE RENDER-MODE BOUNDS CHECK, ONCE. It was written out twice — at DrawSprite
// and at SpriteTrailUpdate — and the two copies had already drifted apart in
// their message while agreeing on the rule.
//
// WHY THE RULE EXISTS: CEngineSprite::GetMaterial keeps one material per render
// mode in an array of kRenderModeCount and subscripts it with NO CHECK AT ALL
// (enginesprite.h:59). A managed number reaching a C-array subscript is a
// boundary, and the boundary checks. REFUSED rather than clamped: a mode this
// side does not recognise is a caller bug, and drawing it as something else
// would hide it. ss_plasma took the game down this way.
//
// THE WARN-ONCE FLAG IS THE CALLER'S, passed in rather than made static here,
// and that is the whole reason this takes three arguments instead of one. A
// single static shared by both sites would mean the second caller's first bad
// mode is silent whenever the first caller has already warned — two different
// managed enums, one warning, and the second one diagnosed by nothing.
//-----------------------------------------------------------------------------
static bool SpriteRenderModeRefused( int32_t nRenderMode, const char *pszCaller, bool &bWarned )
{
	if ( nRenderMode >= 0 && nRenderMode < kRenderModeCount )
		return false;

	if ( !bWarned )
	{
		bWarned = true;
		Warning( "[SourceSharp] %s: render mode %d is outside RenderMode_t "
		         "(0..%d). Nothing drawn. Check the managed RenderMode enum against "
		         "public/const.h.\n", pszCaller, nRenderMode, kRenderModeCount - 1 );
	}

	return true;
}

//-----------------------------------------------------------------------------
// One managed sprite's renderer: a C_SpriteRenderer for its pixel-visibility
// query handle, which is all of it this file still needs. The glow's proxy
// size is the map's "GlowProxySize" (2 or 4 on every one of dm_lockdown's
// twenty-three), handed to the planner with the draw; stock's CSprite
// overrode GlowBlend for the same reason, and the blend is the planner's now
// (SpriteRenderer.cs).
//-----------------------------------------------------------------------------
class CSourceSharpSpriteRenderer : public C_SpriteRenderer
{
public:
	int Draw( IClientEntity *pEntity, const model_t *pModel, const Vector &vecOrigin, const QAngle &angAngles,
	          float flFrame, int nRenderMode, int nRenderFx, int a, int r, int g, int b,
	          float flScale, bool bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize )
	{
		return SourceSharp_PlanSprite( &m_queryHandle, pEntity, pModel, vecOrigin, angAngles, flFrame,
		                               /*attachedto=*/NULL, /*attachmentindex=*/0, nRenderMode, nRenderFx, a, r, g, b,
		                               flScale, bWorldSpaceScale, flHDRColorScale, flGlowProxySize );
	}

	// The direct path's first half: the request for a plan made in managed.
	int Begin( IClientEntity *pEntity, const model_t *pModel, const Vector &vecOrigin, const QAngle &angAngles,
	           float flFrame, int nRenderMode, int nRenderFx, int a, int r, int g, int b,
	           float flScale, bool bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize, float *pIn )
	{
		return SourceSharp_SpriteDrawBegin( &m_queryHandle, pEntity, pModel, vecOrigin, angAngles, flFrame,
		                                    nRenderMode, nRenderFx, a, r, g, b, flScale, bWorldSpaceScale,
		                                    flHDRColorScale, flGlowProxySize, pIn );
	}
};

//-----------------------------------------------------------------------------
// Which renderer belongs to which managed entity.
//
// Keyed by handle rather than held on the entity, for the reason every one of
// these files is a file: C_StandaloneEntity is the shell for EVERY managed
// class and has no business carrying a sprite's occlusion query.
//-----------------------------------------------------------------------------
struct SourceSharpSprite_t
{
	uint32_t                   hOwner;
	CSourceSharpSpriteRenderer renderer;
};

static CUtlVector< SourceSharpSprite_t * > g_ManagedSprites;

static CSourceSharpSpriteRenderer *RendererFor( uint32_t hOwner )
{
	for ( int i = 0; i < g_ManagedSprites.Count(); i++ )
	{
		if ( g_ManagedSprites[i]->hOwner == hOwner )
			return &g_ManagedSprites[i]->renderer;
	}

	SourceSharpSprite_t *pEntry = new SourceSharpSprite_t;
	pEntry->hOwner = hOwner;

	g_ManagedSprites.AddToTail( pEntry );

	return &pEntry->renderer;
}

//-----------------------------------------------------------------------------
// One sprite, this frame.
//
// Everything here is stock CSprite::DrawModel with its members replaced by
// arguments, in the same order and with the same arithmetic:
//
//   * the model type check, which stock asserts on and this declines quietly --
//     a map naming a .mdl where a .spr belongs is a mapping mistake, and an
//     assert in a release build is a silent return anyway.
//   * the world-space scale division, which is stock's and needs the sprite's
//     own size. modelinfo answers it without enginesprite.h.
//   * the draw, through the same C_SpriteRenderer::DrawSprite the stock entity
//     calls, with the entity itself as the material's binding proxy.
//
// The attachment arguments are passed NULL and 0. m_hAttachedToEntity is only
// ever set by CSprite::SetAttachment, which is called from C++ callers -- every
// one of them in hl2/, none of them compiled here -- and never from a map key
// or an input. See report.md and docs/divergences.md.
//-----------------------------------------------------------------------------
static int32_t SourceSharpDrawSpriteCore( uint32_t hOwner, const Vector &vecOffset,
                                          int32_t nModelIndex,
                                          float flFrame, int32_t nRenderMode, int32_t nRenderFx,
                                          int32_t r, int32_t g, int32_t b, int32_t a,
                                          float flScale, int32_t bWorldSpaceScale,
                                          float flHDRColorScale, float flGlowProxySize,
                                          float *pBeginIn = NULL )
{
	if ( nModelIndex <= 0 )
		return 0;

	C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( hOwner );

	if ( !pOwner )
		return 0;

	const model_t *pModel = modelinfo->GetModel( nModelIndex );

	if ( !pModel || modelinfo->GetModelType( pModel ) != mod_sprite )
		return 0;

	// THE RENDER MODE IS AN INDEX, and it arrives from C#. The rule and the
	// reason for it are at SpriteRenderModeRefused; what is here is the record
	// of the crash that put it there.
	//
	// ss_plasma shipped with kRenderTransAddFrameBlend written as 13 in the
	// managed enum where the real value is 7, and the first plasma flame ever
	// drawn took the game down inside CEngineSprite::GetMaterial(
	// nRenderMode=13 ). Ten seconds of running, and nothing between C# and the
	// crash that could say what was wrong.
	static bool s_bWarnedDrawSprite = false;
	if ( SpriteRenderModeRefused( nRenderMode, "DrawSprite", s_bWarnedDrawSprite ) )
		return 0;

	// From the ENTITY, not from the caller.
	//
	// Two reasons and either is sufficient. This is the INTERPOLATED place for
	// the frame being drawn -- the hierarchy advances it in PreRender -- and
	// what the managed half holds is the last snapshot. And the managed half
	// cannot answer at all on this side: EntityGetOrigin is wired inside the
	// gamesystem's `#ifndef CLIENT_DLL`, so the client's pointer is null and
	// Engine.cs returns a silent zero vector. Measured, before the argument was
	// removed: every sprite in dm_lockdown drew at the world origin.
	// The offset is added in WORLD axes rather than the owner's, which is what
	// stock's C_Plasma does: the ring of flames is laid out around the vertical
	// and each one always points up, whatever the entity's angles say. Zero for
	// every caller but the plasma flame.
	const Vector vecOrigin = pOwner->GetAbsOrigin() + vecOffset;
	const QAngle angAngles = pOwner->GetAbsAngles();

	// The world-space scale's division by the sprite's size is the planner's
	// (SpriteRenderer.Plan), handed the flag and the scale as the entity has it.
	CSourceSharpSpriteRenderer *pRenderer = RendererFor( hOwner );

	// The direct path (SourceSharp_FxSpriteDrawBegin): the request only.
	if ( pBeginIn )
		return pRenderer->Begin( pOwner, pModel, vecOrigin, angAngles, flFrame, nRenderMode, nRenderFx, a, r, g, b,
		                         flScale, bWorldSpaceScale != 0, flHDRColorScale, flGlowProxySize, pBeginIn );

	return pRenderer->Draw( pOwner, pModel, vecOrigin, angAngles, flFrame, nRenderMode, nRenderFx, a, r, g, b,
	                        flScale, bWorldSpaceScale != 0, flHDRColorScale, flGlowProxySize );
}

//-----------------------------------------------------------------------------
// One sprite at its owner's own origin -- the whole sprite family, and every
// caller but one.
//-----------------------------------------------------------------------------
int32_t SourceSharp_DrawSprite( uint32_t hOwner, int32_t nModelIndex,
                                float flFrame, int32_t nRenderMode, int32_t nRenderFx,
                                int32_t r, int32_t g, int32_t b, int32_t a,
                                float flScale, int32_t bWorldSpaceScale,
                                float flHDRColorScale, float flGlowProxySize )
{
	return SourceSharpDrawSpriteCore( hOwner, vec3_origin, nModelIndex,
	                                  flFrame, nRenderMode, nRenderFx,
	                                  r, g, b, a, flScale, bWorldSpaceScale,
	                                  flHDRColorScale, flGlowProxySize );
}

//-----------------------------------------------------------------------------
// One sprite a little away from its owner, which is how ONE entity draws the
// SEVEN sprites a plasma flame is made of: six in a ring whose radius follows
// the flame's scale, and a glow beneath them that rises as it grows.
//
// Stock gave each of those seven its own C_Sprite, held as members of C_Plasma
// and pushed into the leaf system by hand. A managed entity cannot hold a
// C_Sprite -- it is a C_BaseEntity and the parallel hierarchy has none -- and
// would not want to: seven client entities per fire, to draw seven quads that
// depend on nothing but this frame's scale.
//-----------------------------------------------------------------------------
int32_t SourceSharp_DrawSpriteOffset( uint32_t hOwner, const float *pOffsetXYZ,
                                      int32_t nModelIndex,
                                      float flFrame, int32_t nRenderMode, int32_t nRenderFx,
                                      int32_t r, int32_t g, int32_t b, int32_t a,
                                      float flScale, int32_t bWorldSpaceScale,
                                      float flHDRColorScale, float flGlowProxySize )
{
	const Vector vecOffset = pOffsetXYZ
	                       ? Vector( pOffsetXYZ[0], pOffsetXYZ[1], pOffsetXYZ[2] )
	                       : vec3_origin;

	return SourceSharpDrawSpriteCore( hOwner, vecOffset, nModelIndex,
	                                  flFrame, nRenderMode, nRenderFx,
	                                  r, g, b, a, flScale, bWorldSpaceScale,
	                                  flHDRColorScale, flGlowProxySize );
}

//-----------------------------------------------------------------------------
// A managed sprite's draw, planned in managed: IShimFxClient's FxSpriteDrawBegin
// fills the request (SS_SPRITE_IN_*) for the sprite at its owner's place plus
// the offset, the managed planner plans it, and FxSpriteDrawEnd
// (shell_sprite_leaf.cpp) draws the plan. Called from inside the owner's own
// draw, so the plan costs no crossing back into managed.
//-----------------------------------------------------------------------------
int32_t SourceSharp_FxSpriteDrawBegin( uint32_t hOwner, const float *pOffsetXYZ, int32_t nModelIndex,
                                       float flFrame, int32_t nRenderMode, int32_t nRenderFx,
                                       int32_t r, int32_t g, int32_t b, int32_t a,
                                       float flScale, int32_t bWorldSpaceScale,
                                       float flHDRColorScale, float flGlowProxySize, float *pIn )
{
	if ( !pIn )
		return 0;

	const Vector vecOffset = pOffsetXYZ
	                       ? Vector( pOffsetXYZ[0], pOffsetXYZ[1], pOffsetXYZ[2] )
	                       : vec3_origin;

	return SourceSharpDrawSpriteCore( hOwner, vecOffset, nModelIndex,
	                                  flFrame, nRenderMode, nRenderFx,
	                                  r, g, b, a, flScale, bWorldSpaceScale,
	                                  flHDRColorScale, flGlowProxySize, pIn );
}

//=============================================================================//
// THE TRAIL, which is the one member of the family that SIMULATES.
//
// Everything above this line exists because a sprite simulates nothing. A trail
// does: it lays a point down into a ring of 256 and ages them out, and builds a
// ribbon from whatever is still alive. That state has to live somewhere between
// frames.
//
// IT USED TO LIVE ON A STOCK C_SpriteTrail, AND THAT WAS THE RIGHT ANSWER ONCE.
// Commit d9f7f80a refused to de-entify this one on the ground that "the line is
// not 'is there a C_Foo' but 'does the client half SIMULATE'" -- and the
// simulation is real, so keeping the object stock wrote was the cheapest way to
// keep it. That refusal was a refusal of a STEP-3 EVICTION: the question it
// answered was "can SpriteTrail.o leave the link line", and it could not,
// because this file was asking for it.
//
// IT IS OVERTURNED HERE, on three grounds and with the cost measured:
//
//   1. "KEEP DERIVING" IS NOT ON THE MENU AT STEP 6. Once CBaseEntity is a
//      typedef for IGameEntity -- six pure virtuals and no members --
//      `class CSpriteTrail : public CSprite : public CBaseEntity` does not
//      compile at all. Every member access in it is an error and no retype
//      makes one legal. So the refusal was never a DISPOSITION; it said when
//      the object could leave, not what the class becomes.
//
//   2. THE C++ GOES DOWN, NOT UP, WHICH IS THE CRITERION. SpriteTrail.cpp is
//      708 lines and SpriteTrail.h is 167; both leave the build entirely. What
//      arrives here is the ring, the ribbon and the bounds -- and not the
//      skybox conversion, not the attachment, not the datadesc, not the
//      prediction data, not the network table, not SpriteTrailCreate,
//      ShouldTransmit or the whole server half. A deletion that needed more C++
//      than it removed would be a refusal; this one removes about four times
//      what it adds.
//
//   3. THE SIMULATION DID NOT NEED THE ENTITY. What the entity supplied was a
//      place, a ClientThink and a row in the leaf system so DrawModel got
//      called. CShellRenderable supplies the first and the third -- it is the
//      base built for exactly this when C_PointWorldText was de-entified -- and
//      the PreRender sweep at the bottom of this file was already running for
//      the trails anyway, to follow their owners. The think is one more line in
//      a loop that existed.
//
// AND THE ARMING TRAP GOES WITH IT, which is worth more than the line count.
// The old code had to call SetNextClientThink( CLIENT_THINK_ALWAYS ) by hand
// with a comment explaining that stock arms it from OnDataChanged on
// DATA_UPDATE_CREATED -- "exactly the call a client-created entity never gets".
// A sweep either runs or it does not; there is nothing left to forget to arm.
//
// WHAT IS DELIBERATELY NOT CARRIED, each measured dead rather than dropped:
//
//   THE SKYBOX PAIR. m_vecSkyboxOrigin / m_flSkyboxScale are written by
//   CSpriteTrail::SetSkybox alone, which has no caller in this build and no
//   keyvalue behind it, so both sides carried a constant 1.0f and vec3_origin.
//   docs/divergences.md already records that the managed half does not send
//   them. Every setter below therefore loses a `/= m_flSkyboxScale` that was
//   dividing by one, and ConvertSkybox loses its only trigger.
//
//   THE ATTACHMENT. GetRenderOrigin followed m_hAttachedToEntity, which only
//   CSprite::SetAttachment writes -- and every caller of that is under hl2/,
//   none compiled here. The origin is the owner's, as it is for the plain
//   sprite above.
//
//   SCREEN_SPACE_TRAILS. `#define SCREEN_SPACE_TRAILS 0` at SpriteTrail.cpp:36.
//   Both arms are in the source and one of them has never been compiled.
//
//   SCALE, HDR COLOUR SCALE AND RENDERFX. Still ARGUMENTS below, because the
//   ABI is not this lane's to change -- and inert, because they were inert
//   before: CSpriteTrail overrides DrawModel and its ribbon reads none of the
//   three. SetScale wrote m_flSpriteScale, which only CSprite::DrawModel reads.
//=============================================================================//

//-----------------------------------------------------------------------------
// A managed env_spritetrail's ribbon, as a renderable: its owner, whether it is
// switched on, and the box the managed ring last measured. The ring, the
// ribbon and the bounds are SpriteTrail.cs (lane c2-fx), recorded off this
// file before they moved (spritetrail-stock.tsv.gz) and replayed bit for bit
// by ClientSpriteTrailTests. Ticked by the PreRender sweep below and drawn by
// the leaf system, each a call into IManagedFxClient with the curtime and the
// interpolated place it follows; the draw itself is the beam executor's,
// called from the managed side.
//-----------------------------------------------------------------------------
class CSourceSharpSpriteTrail : public CShellRenderable
{
public:
	DECLARE_CLASS_GAMEROOT( CSourceSharpSpriteTrail, CShellRenderable );

	// The block arrived zeroed (SOURCESHARP_ZEROING_ALLOCATOR), so only what a
	// zero is wrong for is set.
	CSourceSharpSpriteTrail()
	{
		m_bVisible = true;
	}

	// IClientRenderable. CDefaultClientRenderable leaves ShouldDraw and
	// IsTransparent pure; the other two are stock CSpriteTrail's.
	virtual bool ShouldDraw() OVERRIDE { return m_bVisible; }
	virtual bool IsTransparent() OVERRIDE { return true; }
	virtual int DrawModel( int flags ) OVERRIDE
	{

		IManagedFxClient *pFx = ManagedFxClient();
		if ( !pFx )
			return 0;

		float origin[3];
		PutOrigin( origin );
		return pFx->SpriteTrailDraw( m_hOwner, gpGlobals->curtime, origin ) > 0 ? 1 : 0;
	}
	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) OVERRIDE
	{
		mins = m_vecRenderMins;
		maxs = m_vecRenderMaxs;
	}

	// What ClientThink was: the managed ring lays a point and re-measures.
	void Tick()
	{
		IManagedFxClient *pFx = ManagedFxClient();
		if ( !pFx )
			return;

		float origin[3], box[6];
		PutOrigin( origin );
		if ( pFx->SpriteTrailTick( m_hOwner, gpGlobals->curtime, origin, box ) )
		{
			m_vecRenderMins.Init( box[0], box[1], box[2] );
			m_vecRenderMaxs.Init( box[3], box[4], box[5] );
		}
	}

	void SourceSharpUpdate( uint32_t hOwner, bool bVisible )
	{
		m_hOwner = hOwner;
		m_bVisible = bVisible;
	}

private:
	void PutOrigin( float *p )
	{
		const Vector &o = GetAbsOrigin();
		p[0] = o.x; p[1] = o.y; p[2] = o.z;
	}

	uint32_t	m_hOwner;
	Vector		m_vecRenderMins;
	Vector		m_vecRenderMaxs;
	bool		m_bVisible;
};

//-----------------------------------------------------------------------------
// Which ribbon belongs to which managed entity.
//-----------------------------------------------------------------------------
struct SourceSharpSpriteTrail_t
{
	uint32_t                 hOwner;
	CSourceSharpSpriteTrail *pTrail;
};

// NO FindTrail OF ITS OWN. ShellEffects_Find is the same loop with the same
// FIRST-match rule and one test that drives it
// (sourcesharp/test/shell_effect_registry_test.cpp); this file had written it
// out three times, once here, once for the particle systems (now in
// shell_managed_particles.cpp) and once for the renderers above. Two of the
// three are the helper now. The third — RendererFor — is not, and the reason
// is worth reading before anyone "finishes" the job: its list holds POINTERS
// to heap rows, because a CSourceSharpSpriteRenderer is handed out by address
// and a CUtlVector of values moves its elements when it grows. The helper
// takes a list of rows.
static CUtlVector< SourceSharpSpriteTrail_t > g_ManagedTrails;

//-----------------------------------------------------------------------------
// "It has gone", from the one place that always knows.
//
// A CHandle used to answer this for free: it reads as null the moment the engine
// destroys the entity behind it, so a stale row announced itself. A raw pointer
// does not, so the destructor says so instead -- the arrangement
// shell_renderable.h and shell_effect_registry.h both describe.
//-----------------------------------------------------------------------------
static void ManagedSpriteTrail_Forget( CShellRenderable *pGone )
{
	for ( int i = g_ManagedTrails.Count() - 1; i >= 0; i-- )
	{
		if ( g_ManagedTrails[i].pTrail == pGone )
		{
			g_ManagedTrails.Remove( i );
			return;
		}
	}
}

//-----------------------------------------------------------------------------
// Makes, or updates, the trail belonging to a managed entity.
//-----------------------------------------------------------------------------
void SourceSharp_SpriteTrailUpdate( uint32_t hOwner, int32_t nRenderMode, int32_t bVisible )
{
	C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( hOwner );

	if ( !pOwner )
		return;

	// THE RENDER MODE IS AN INDEX AND IT ARRIVES FROM C#, and it reaches the
	// same bare C-array subscript the sprite above does. Its own warn-once
	// flag, so that a bad mode here is reported whether or not DrawSprite has
	// already seen one: they come from two different managed enums.
	static bool s_bWarnedTrail = false;
	if ( SpriteRenderModeRefused( nRenderMode, "SpriteTrailUpdate", s_bWarnedTrail ) )
		return;

	int iEntry = ShellEffects_Find( g_ManagedTrails, hOwner );

	if ( iEntry < 0 )
	{
		CSourceSharpSpriteTrail *pNew = new CSourceSharpSpriteTrail;

		if ( !pNew )
			return;

		pNew->SetAbsOrigin( pOwner->GetAbsOrigin() );
		pNew->SetAbsAngles( pOwner->GetAbsAngles() );

		// What InitializeAsClientEntity was doing on this path, and the ONLY
		// thing it was doing: putting the object where the client leaf system
		// can find it, which is what makes DrawModel get called. No entity
		// index, no row in the client entity list, no model to bind -- the
		// ribbon resolves its sprite from the model index every frame.
		pNew->AddToLeafSystem( RENDER_GROUP_TRANSLUCENT_ENTITY );
		pNew->SetGoneCallback( ManagedSpriteTrail_Forget );

		SourceSharpSpriteTrail_t entry;
		entry.hOwner = hOwner;
		entry.pTrail = pNew;

		iEntry = g_ManagedTrails.AddToTail( entry );
	}

	g_ManagedTrails[ iEntry ].pTrail->SourceSharpUpdate( hOwner, bVisible != 0 );
}

//-----------------------------------------------------------------------------
// The trail goes when its managed entity does.
//-----------------------------------------------------------------------------
void SourceSharp_SpriteTrailDestroy( uint32_t hOwner )
{
	const int iEntry = ShellEffects_Find( g_ManagedTrails, hOwner );

	if ( iEntry < 0 )
		return;

	// Release() removes the row itself, through ManagedSpriteTrail_Forget.
	g_ManagedTrails[ iEntry ].pTrail->Release();
}

//-----------------------------------------------------------------------------
// Every trail, ticked and put back where its entity is.
//
// The sweep the plain sprite does not need. A sprite is drawn from inside its
// owner's DrawModel and is handed the owner's own interpolated origin; a trail
// is a SEPARATE renderable with its own place, and it reads that place once a
// frame to decide where to lay its next point. An entity that only MOVED
// delivers no data change at all -- its origin is a real prop outside the blob
// -- so a trail fed only on update would lay every point in the same spot.
//
// The Tick() is what the engine used to deliver as ClientThink, and the two are
// the same two calls in the same order.
//
// Backwards, so that removing an entry mid-walk cannot make the sweep skip its
// neighbour. Release() shortens the list itself, through the gone callback.
//-----------------------------------------------------------------------------
static void ManagedSpriteTrails_Follow()
{
	for ( int i = g_ManagedTrails.Count() - 1; i >= 0; i-- )
	{
		CSourceSharpSpriteTrail *pTrail = g_ManagedTrails[i].pTrail;

		C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( g_ManagedTrails[i].hOwner );

		if ( !pOwner )
		{
			pTrail->Release();
			continue;
		}

		pTrail->SetAbsOrigin( pOwner->GetAbsOrigin() );
		pTrail->SetAbsAngles( pOwner->GetAbsAngles() );
		pTrail->Tick();
	}
}

//-----------------------------------------------------------------------------
// THE FLAME'S PARTICLE SYSTEM sat here, behind a banner, until lane
// c5-particles gave it the particles shell of its own (shell_managed_particles.cpp).
// Its two hooks are still called from this system's PreRender and level
// shutdown below, in the place they always ran.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// PreRender, beside the hierarchy's own interpolation pass and for the same
// reason: the place a trail lays its point at this frame has to be the place its
// entity has been interpolated to this frame, not last frame's.
//
// The RENDERER table is dropped on a level change, because every handle in it
// names an entity the engine has already released. Nothing else releases an
// entry: a renderer is a couple of dozen bytes, and a sprite that stops drawing
// for a while is a sprite that will draw again -- an env_sprite is switched off
// far more often than it is removed.
//-----------------------------------------------------------------------------
#include "shell_clientsystem.h"	// plan_cworld_trace.md T4

class CSourceSharpSpriteSystem : public CClientSystemPerFrame
{
public:
	CSourceSharpSpriteSystem()
		: CClientSystemPerFrame( "CSourceSharpSpriteSystem" ) {}

	virtual void PreRender() OVERRIDE
	{
		ManagedSpriteTrails_Follow();
		ShellParticles_PreRender();
	}

	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		g_ManagedSprites.PurgeAndDeleteElements();

		// THE TRAILS ARE OURS NOW, and this line used to be RemoveAll(). While
		// they were client entities the engine tore them down with the rest of
		// the entity list and forgetting the row was the whole job; a
		// CShellRenderable is owned by whoever made it and nothing else will
		// ever delete one. Release() takes the row out itself, through
		// ManagedSpriteTrail_Forget, so the walk cannot hold an index.
		while ( g_ManagedTrails.Count() > 0 )
		{
			const int nBefore = g_ManagedTrails.Count();

			g_ManagedTrails[ 0 ].pTrail->Release();

			// Not defensive dressing: if the gone callback is ever dropped this
			// loop is an infinite one, and a hang at level change is a far worse
			// way to find out than a leak.
			if ( g_ManagedTrails.Count() >= nBefore )
			{
				AssertMsg( false, "CShellRenderable::Release did not remove its row" );
				g_ManagedTrails.RemoveAll();
				break;
			}
		}

		// The particle systems: stopped, not merely forgotten
		// (shell_managed_particles.cpp).
		ShellParticles_LevelShutdown();
	}
};

static CSourceSharpSpriteSystem g_SpriteSystem;

// plan_cworld_trace.md T4: kClientSystems[] is the only thing that reaches this.
CClientSystem *ClientSystem_Sprite() { return &g_SpriteSystem; }
