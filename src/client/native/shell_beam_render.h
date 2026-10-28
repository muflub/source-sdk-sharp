//========= SourceSharp — the beam executor's one native export ==============//
//
// Binding a sprite model's material for a frame and render mode, which the
// beam executor does for every managed beam and the sprite trail's native
// draw does for itself (shell_sprite_render.cpp). Stock's
// Draw_SetSpriteTexture, with the wireframe choice handed in rather than read.
//=============================================================================//

#pragma once

class CEngineSprite;
struct model_t;

CEngineSprite *SourceSharp_BindSpriteModel( const model_t *pModel, int nFrame, int nRenderMode, bool bWireframe );

// One sprite through the managed planner (shell_sprite_leaf.cpp): what
// C_SpriteRenderer::DrawSprite does, with the query handle, the world-space
// flag and the glow proxy size a managed sprite brings of its own.
class IClientEntity;
class QAngle;
class Vector;
typedef int pixelvis_handle_t;
int SourceSharp_PlanSprite( pixelvis_handle_t *pQuery, IClientEntity *entity, const model_t *model,
	const Vector &origin, const QAngle &angles, float frame, IClientEntity *attachedto, int attachmentindex,
	int rendermode, int renderfx, int alpha, int r, int g, int b, float scale, bool bWorldSpaceScale,
	float flHDRColorScale, float flGlowProxySize );

// The direct path's first half (shell_sprite_leaf.cpp): the request into pIn
// (SS_SPRITE_IN_FLOATS of them) and the glow query armed; the plan is made in
// managed and handed to SourceSharp_FxSpriteDrawEnd.
int SourceSharp_SpriteDrawBegin( pixelvis_handle_t *pQuery, IClientEntity *entity, const model_t *model,
	const Vector &origin, const QAngle &angles, float frame, int rendermode, int renderfx, int alpha,
	int r, int g, int b, float scale, bool bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize,
	float *pIn );

// The batched path (C7 item 7, shell_standalone_client.cpp's draw batch): a
// managed sprite's plan made ahead of its draw and stored by
// SourceSharp_FxSpriteDrawStore against its entity, carried out at its
// DrawModel. ExecuteStored answers what DrawModel returns, -1 when the stored
// plan was made for another blend than the one in force (it is dropped), and
// -2 when nothing is stored for the entity. Clear drops every stored plan.
int SourceSharp_SpriteExecuteStored( IClientEntity *pEntity, float flBlendNow );
void SourceSharp_SpriteStoredClear();
