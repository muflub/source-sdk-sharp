//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "predictable_entity.h"

class CBasePlayer;

#define CSprite C_Sprite
#define CSpriteOriented C_SpriteOriented
#include "c_pixel_visibility.h"
class CEngineSprite;

class C_SpriteRenderer
{
public:
	// Purpose: Sprite orientations
	// WARNING!  Change these in common/MaterialSystem/Sprite.cpp if you change them here!
	typedef enum
	{
		SPR_VP_PARALLEL_UPRIGHT		= 0,
		SPR_FACING_UPRIGHT			= 1,
		SPR_VP_PARALLEL				= 2,
		SPR_ORIENTED				= 3,
		SPR_VP_PARALLEL_ORIENTED	= 4
	} SPRITETYPE;
	
	// Sprites can alter blending amount
	virtual float					GlowBlend( CEngineSprite *psprite, const Vector& entorigin, int rendermode, int renderfx, int alpha, float *scale );

	// Draws tempent as a sprite
	int								DrawSprite( 
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
										float flHDRColorScale = 1.0f
										);

protected:
	pixelvis_handle_t	m_queryHandle;
	float				m_flGlowProxySize;
	float				m_flHDRColorScale;
};

//========= SourceSharp ======================================================//
// THE ENTITY IS OUT OF THIS BUILD; C_SpriteRenderer ABOVE IS NOT.
// C_Sprite and C_SpriteOriented are two of the nine compiled ClientClasses that
// pair on a ServerClass name NOTHING in this build ever sends: they pair on
// CSprite and CSpriteOriented, and env_sprite (dm_lockdown x23),
// env_sprite_oriented and env_glow are all served by C#, arriving on a
// generic_nettable.h size tier rather than under an SDK class name. The client's
// own env_sprite / env_sprite_oriented registrations were reachable only through
// C_Sprite::SpriteCreatePredictable, which has no caller in this build, and the
// SERVER's CSprite::SpriteCreate resolved to one dead site in CGibShooter, which
// left with game/server/effects.cpp.
// What was left holding it was our own shell_sprite_render.cpp, through
// C_SpriteTrail: the trail derived from CSprite and the shell built one per
// managed env_spritetrail. The trail's ribbon moved onto CShellRenderable and
// SpriteTrail.cpp left the build, so Sprite.o's referrer count went to zero and
// game/shared/Sprite.cpp is `-$File` out of both projects.
// C_SpriteRenderer, ABOVE THIS BANNER, STAYS AND IS THE POINT. It is not an
// entity and never was -- three static-ish helpers and a pixel-visibility query
// handle -- and it is what actually draws a sprite. Four compiled objects want
// it: shell_sprite_render.o derives its own renderer from it,
// c_te_legacytempents.o has C_LocalTempEntity derive from it,
// entity_client_tools.o calls GetSpriteAxes, and spritemodel.o names its
// SPRITETYPE enum. Its definitions live in game/client/c_sprite.cpp, which stays
// in the build for exactly that reason.
// THE FILE STAYS ON DISK. client_base.vpc and server_base.vpc still list
// Sprite.cpp for the stock projects, and ~40 hl2/, hl2mp/, tf/ and episodic/
// sources this build does not compile include this header for CSprite.
