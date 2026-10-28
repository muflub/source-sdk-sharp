//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
// The view pass's questions about the local player (view_facts.h), answered from
// the engine block or the managed player's published pose. Nothing here decides.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"

#include "view_subject.h"
#include "shell_standalone_client.h"
#include "shell_diagnostics.h"
#include "shell_cliententitylist.h"

// Checks the value view_subject.h restates against the enum it came from.

COMPILE_TIME_ASSERT( ViewSubject::WATER_LEVEL_EYES == WL_Eyes );

#include "sourcesharp.h"

bool SourceSharp_HasLocalViewer()
{
	return SourceSharp_LocalPlayerShell() != NULL;
}

bool SourceSharp_ViewerAbsPosition( Vector *pOrigin, QAngle *pAngles )
{
	C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell();

	if ( !pPlayer )
		return false;

	if ( pOrigin )
		*pOrigin = pPlayer->GetAbsOrigin();

	if ( pAngles )
		*pAngles = pPlayer->GetAbsAngles();

	return true;
}

// The engine's view entity, when it is not this client's own player.
// GetClientEntity rather than GetEnt: a managed entity has no C_BaseEntity, and
// IClientEntity is what both hierarchies implement.
bool SourceSharp_ViewEntityPose( Vector *pOrigin, QAngle *pAngles )
{
	const int nViewEntity = render->GetViewEntity();

	if ( !ViewSubject::ViewEntityOverridesTheEye( nViewEntity, engine->GetLocalPlayer() ) )
		return false;

	IClientEntity *pViewEntity = SourceSharp_ClientEntityFromIndex( nViewEntity );

	if ( !pViewEntity )
		return false;

	if ( pOrigin )
		*pOrigin = pViewEntity->GetAbsOrigin();

	if ( pAngles )
		*pAngles = pViewEntity->GetAbsAngles();

	return true;
}

// Read from the engine block, the one copy of an entity's EF_* bits.
bool SourceSharp_ViewerEffectActive( int nEffectBits )
{
	C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell();

	if ( !pPlayer )
		return false;

	return ViewSubject::EffectActive( pPlayer->Block().Head.Effects, nEffectBits );
}

// The sound listener is underwater when the viewer's eyes are (stock:
// GetWaterLevel() >= WL_Eyes). vecMainViewOrigin, stock's spectator input, is unused.
bool SourceSharp_ViewerAudioIsUnderwater( const Vector &vecMainViewOrigin )
{
	NOTE_UNUSED( vecMainViewOrigin );

	C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell();

	if ( !pPlayer )
		return false;

	return ViewSubject::AudioIsUnderwater( pPlayer->Block().Head.WaterLevel );
}

// The viewer's world fog, as managed code pushes it. NULL until something has
// been pushed (no fog controller, or no player yet), as stock answers for a
// player with no fog controller; a zeroed struct would read as a real "no fog".
static fogparams_t g_ViewerFog;
static bool g_bHaveViewerFog = false;

// CNetworkColor32 wants its four components set, not an address written through.
template <typename T>
static void SourceSharp_UnpackViewerColor32( uint32_t packed, T &out )
{
	out.SetR( (unsigned char)( packed & 0xFF ) );
	out.SetG( (unsigned char)( ( packed >> 8 ) & 0xFF ) );
	out.SetB( (unsigned char)( ( packed >> 16 ) & 0xFF ) );
	out.SetA( (unsigned char)( ( packed >> 24 ) & 0xFF ) );
}

void SourceSharp_SetViewerFogParams( const SourceSharpFogParams *pParams )
{
	if ( !pParams )
	{
		// Cleared, which is not "fog off".
		g_bHaveViewerFog = false;
		return;
	}

	g_ViewerFog.enable = pParams->Enable != 0;
	g_ViewerFog.blend = pParams->Blend != 0;
	g_ViewerFog.radial = pParams->Radial != 0;

	g_ViewerFog.dirPrimary = Vector( pParams->DirPrimaryXYZ[0], pParams->DirPrimaryXYZ[1], pParams->DirPrimaryXYZ[2] );

	SourceSharp_UnpackViewerColor32( pParams->ColorPrimary, g_ViewerFog.colorPrimary );
	SourceSharp_UnpackViewerColor32( pParams->ColorSecondary, g_ViewerFog.colorSecondary );

	// The lerp targets are the current values, not zero: GetFogColor blends towards
	// them whenever lerptime is in the future. The server already ran the lerp.
	g_ViewerFog.colorPrimaryLerpTo = g_ViewerFog.colorPrimary;
	g_ViewerFog.colorSecondaryLerpTo = g_ViewerFog.colorSecondary;
	g_ViewerFog.startLerpTo = pParams->Start;
	g_ViewerFog.endLerpTo = pParams->End;
	g_ViewerFog.lerptime = 0.0f;
	g_ViewerFog.duration = 0.0f;

	g_ViewerFog.start = pParams->Start;
	g_ViewerFog.end = pParams->End;
	g_ViewerFog.farz = pParams->FarZ;
	g_ViewerFog.maxdensity = pParams->MaxDensity;

	g_bHaveViewerFog = true;
}

fogparams_t *SourceSharp_ViewerFogParams()
{
	if ( !g_bHaveViewerFog )
	{
		return NULL;
	}

	return &g_ViewerFog;
}

// The viewer's 3D skybox, as managed code pushes it. NULL means no sky camera
// has been heard from, and callers handle it. Area 255 is a camera outside the
// sealed world. A zeroed struct is never returned: area 0 is a real area.
static sky3dparams_t g_ViewerSkybox;
static bool g_bHaveViewerSkybox = false;

void SourceSharp_SetViewerSkyboxParams( const SourceSharpSkyboxParams *pParams )
{
	if ( !pParams )
	{
		// Cleared, which is not area 255.
		g_bHaveViewerSkybox = false;
		return;
	}

	g_ViewerSkybox.origin = Vector( pParams->OriginXYZ[0], pParams->OriginXYZ[1], pParams->OriginXYZ[2] );
	g_ViewerSkybox.scale = pParams->Scale;
	g_ViewerSkybox.area = pParams->Area;

	g_ViewerSkybox.fog.enable = pParams->FogEnable != 0;
	g_ViewerSkybox.fog.blend = pParams->FogBlend != 0;
	g_ViewerSkybox.fog.radial = pParams->FogRadial != 0;

	g_ViewerSkybox.fog.dirPrimary =
		Vector( pParams->FogDirPrimaryXYZ[0], pParams->FogDirPrimaryXYZ[1], pParams->FogDirPrimaryXYZ[2] );

	SourceSharp_UnpackViewerColor32( pParams->FogColorPrimary, g_ViewerSkybox.fog.colorPrimary );
	SourceSharp_UnpackViewerColor32( pParams->FogColorSecondary, g_ViewerSkybox.fog.colorSecondary );

	// The lerp targets are the current values, as for the world fog above.
	g_ViewerSkybox.fog.colorPrimaryLerpTo = g_ViewerSkybox.fog.colorPrimary;
	g_ViewerSkybox.fog.colorSecondaryLerpTo = g_ViewerSkybox.fog.colorSecondary;
	g_ViewerSkybox.fog.startLerpTo = pParams->FogStart;
	g_ViewerSkybox.fog.endLerpTo = pParams->FogEnd;
	g_ViewerSkybox.fog.lerptime = 0.0f;
	g_ViewerSkybox.fog.duration = 0.0f;

	g_ViewerSkybox.fog.start = pParams->FogStart;
	g_ViewerSkybox.fog.end = pParams->FogEnd;
	g_ViewerSkybox.fog.maxdensity = pParams->FogMaxDensity;

	// farz is not sent: sky_camera has no such key and CSkyboxView sets zFar itself.

	g_bHaveViewerSkybox = true;
}

sky3dparams_t *SourceSharp_ViewerSkyboxParams()
{
	if ( !g_bHaveViewerSkybox )
		return NULL;

	return &g_ViewerSkybox;
}

// Refreshes visibility on the camera mode change's edge, so the player's own
// model leaves or joins the leaf system that frame rather than the next.
void SourceSharp_ViewerCameraModeChanged()
{
	if ( C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell() )
		pPlayer->UpdateVisibility();
}

// HL2 aim assist: neither end of m_bAutoAimTarget's wire is in this build.
bool SourceSharp_ViewerAimIsAssisted()
{
	SourceSharp_NotShipped( "HL2 autoaim assist (client)",
							"CHL2_PlayerLocalData::m_bAutoAimTarget is written on the server by "
							"hl2_player.cpp and networked through DT_HL2Local; neither file is in "
							"this build and the state is not in the managed player's networked "
							"declaration, so the joystick never dampens onto a target" );

	return false;
}
