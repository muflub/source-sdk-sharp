//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the explosion's glow overlay, in the shim ==========//
// SS_REASON a  a vtable the engine calls through
//
// plan_entity.md Phase 7 step 7b. Another MOVE rather than a port, and the
// argument is the one shell_te_particle_render.cpp makes: this is a leaf
// renderer, §6 lists leaf renderers as what legitimately stays native, and the
// body is stock's own with nothing changed.
//
// WHAT c_te_explosion.cpp HAD LEFT THAT THIS BUILD COMPILED. Two things, and
// only one of them was reachable:
//
//   CExplosionOverlay::Update   -- HERE. A CWarpOverlay subclass, seven lines
//                                  of colour and size ramp over a tenth of a
//                                  second. Two shim TUs emit a vtable for it
//                                  (shell_te_backends_client.cpp and the
//                                  RPGShotDown effect below both build one), so
//                                  without a definition of Update the link
//                                  fails on _ZTV17CExplosionOverlay.
//
//   CRagdollExplosionEnumerator -- NOT here, and not anywhere: it has no caller
//                                  in this build. The blast push on ragdolls is
//                                  managed (ClientTeExplosion.AffectRagdolls,
//                                  eleven [Fact]s in TempEntParticleClientTests),
//                                  and the only native caller left in the tree
//                                  is hl2/c_te_concussiveexplosion.cpp, which
//                                  client_csharp.vpc does not build. Its bodies
//                                  stay ON DISK in c_te_explosion.cpp, outside
//                                  that file's SOURCESHARP guard, because they
//                                  are the reference the managed [Fact] and
//                                  ragdoll_blast_seam_test.cpp both read the
//                                  falloff, the cut and the 32-unit drop out of.
//                                  Copying them here would be a SECOND copy of
//                                  arithmetic this port did not write, which is
//                                  what those two tests exist to prevent.
//
// SO THE OVERLAY IS THE WHOLE OF THIS FILE, BAR RPGShotDown (at the end). `gpGlobals->frametime` is the only
// thing it reads and `m_Sprites` is CWarpOverlay's own.
//
//=============================================================================//

#include "cbase.h"
#include "glow_overlay.h"
#include "fx_explosion.h"
#include "c_recipientfilter.h"
#include "engine/IEngineSound.h"
#include "c_te_effect_dispatch.h"
#include "client_sound.h"
#include "sourcesharp.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//
// CExplosionOverlay
//

// The ramp both overlays here run, stock's own (CExplosionOverlay::Update,
// fx.cpp's CImpactOverlay::Update), differing only in how fast the sprites grow.
static bool RampWarpOverlay( CWarpOverlay *pOverlay, float &flLifetime, const Vector *pBaseColors, float flGrowth )
{
	flLifetime += gpGlobals->frametime;

	const float flTotalLifetime = 0.1f;

	if ( flLifetime < flTotalLifetime )
	{
		float flColorScale = 1.0f - ( flLifetime / flTotalLifetime );

		for( int i=0; i < pOverlay->m_nSprites; i++ )
		{
			pOverlay->m_Sprites[i].m_vColor = pBaseColors[i] * flColorScale;

			pOverlay->m_Sprites[i].m_flHorzSize += flGrowth * gpGlobals->frametime;
			pOverlay->m_Sprites[i].m_flVertSize += flGrowth * gpGlobals->frametime;
		}

		return true;
	}

	return false;
}

bool CExplosionOverlay::Update( void )
{
	return RampWarpOverlay( this, m_flLifetime, m_vBaseColors, 16.0f );
}

//-----------------------------------------------------------------------------
// "RPGShotDown": stock's client effect, moved here from shell_trail_sim.cpp
// (c_smoke_trail.cpp) when the trails went to C# (lane c2-sims), because it is
// this overlay's other user and not a trail. Nothing in this build dispatches
// it (its sender is hl2's weapon_rpg); it is kept, not deleted, with the
// client effects lane c2-fx owns.
//-----------------------------------------------------------------------------
void RPGShotDownCallback( const CEffectData &data )
{
	CLocalPlayerFilter filter;
	ClientSound::Emit( filter, SOUND_FROM_WORLD, "Missile.ShotDown", &data.m_vOrigin );

	if ( CExplosionOverlay *pOverlay = new CExplosionOverlay )
	{
		pOverlay->m_flLifetime	= 0;
		pOverlay->m_vPos		= data.m_vOrigin;
		pOverlay->m_nSprites	= 1;
		
		pOverlay->m_vBaseColors[0].Init( 1.0f, 0.9f, 0.7f );

		pOverlay->m_Sprites[0].m_flHorzSize = 0.01f;
		pOverlay->m_Sprites[0].m_flVertSize = pOverlay->m_Sprites[0].m_flHorzSize*0.5f;

		pOverlay->Activate();
	}
}

DECLARE_CLIENT_EFFECT( "RPGShotDown", RPGShotDownCallback );

//-----------------------------------------------------------------------------
// fx.cpp's CImpactOverlay, the gunship impact's and command pointer's warp
// overlay (lane c5-particles), moved here with this file's other overlay when
// fx.cpp's routines went to C# (FxEffects.GunshipImpact). Named for its use so
// it does not share a vtable with the fork's copy while fx.cpp still builds.
// Its sprites grow a unit a second where CExplosionOverlay's grow sixteen.
//-----------------------------------------------------------------------------
class CGunshipImpactOverlay : public CWarpOverlay
{
public:
	virtual bool Update( void );

	float	m_flLifetime;
	Vector	m_vBaseColors[MAX_SUN_LAYERS];
};

bool CGunshipImpactOverlay::Update( void )
{
	return RampWarpOverlay( this, m_flLifetime, m_vBaseColors, 1.0f );
}

// FX_GunshipImpact's body past managed's VectorMA: one sprite, a hundredth of
// a unit, in the colour managed chose.
void SourceSharp_FxImpactOverlay( const float *pPositionXYZ, const float *pColorRGB )
{
	if ( CGunshipImpactOverlay *pOverlay = new CGunshipImpactOverlay )
	{
		pOverlay->m_flLifetime	= 0;
		pOverlay->m_vPos.Init( pPositionXYZ[0], pPositionXYZ[1], pPositionXYZ[2] );
		pOverlay->m_nSprites	= 1;

		pOverlay->m_vBaseColors[0].Init( pColorRGB[0], pColorRGB[1], pColorRGB[2] );

		pOverlay->m_Sprites[0].m_flHorzSize = 0.01f;
		pOverlay->m_Sprites[0].m_flVertSize = 0.01f;

		pOverlay->Activate();
	}
}
