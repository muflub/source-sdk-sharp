// Binds a sprite model's material for a frame and render mode (stock's
// Draw_SetSpriteTexture, with the wireframe choice passed in).

#pragma once

class CEngineSprite;
struct model_t;

CEngineSprite *SourceSharp_BindSpriteModel( const model_t *pModel, int nFrame, int nRenderMode, bool bWireframe );

// Plans and draws one sprite as C_SpriteRenderer::DrawSprite does.
class IClientEntity;
class QAngle;
class Vector;
typedef int pixelvis_handle_t;
int SourceSharp_PlanSprite( pixelvis_handle_t *pQuery, IClientEntity *entity, const model_t *model,
	const Vector &origin, const QAngle &angles, float frame, IClientEntity *attachedto, int attachmentindex,
	int rendermode, int renderfx, int alpha, int r, int g, int b, float scale, bool bWorldSpaceScale,
	float flHDRColorScale, float flGlowProxySize );

// Writes the request into pIn (SS_SPRITE_IN_FLOATS floats) and arms the glow
// query; managed code makes the plan and hands it to SourceSharp_FxSpriteDrawEnd.
int SourceSharp_SpriteDrawBegin( pixelvis_handle_t *pQuery, IClientEntity *entity, const model_t *model,
	const Vector &origin, const QAngle &angles, float frame, int rendermode, int renderfx, int alpha,
	int r, int g, int b, float scale, bool bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize,
	float *pIn );

// Carries out the plan stored for pEntity by SourceSharp_FxSpriteDrawStore and
// answers what DrawModel returns; -1 when the plan was made for another blend
// (it is dropped), -2 when nothing is stored. Clear drops every stored plan.
int SourceSharp_SpriteExecuteStored( IClientEntity *pEntity, float flBlendNow );
void SourceSharp_SpriteStoredClear();
