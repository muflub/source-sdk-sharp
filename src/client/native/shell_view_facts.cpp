//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the view pass's facts, answered ====================//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// The definitions behind view_facts.h. plan_entity.md step 7c.
//
// This file is the ONLY place the view/input TUs' de-entitying lands a native
// body, and each one is here for a reason view_facts.h states: an ask that a
// stock TU used to put to `C_BasePlayer::GetLocalPlayer()`, answered now from
// the engine block or from the managed player's published pose. Nothing here
// DECIDES anything — the two that would be decisions (whether the viewer's own
// body draws, and where the third-person camera ends up) are already
// shell_client.cpp's, and this file does not have a second opinion about
// either.
//
//=============================================================================//

#include "cbase.h"

#include "view_facts.h"
#include "view_subject.h"
#include "shell_standalone_client.h"
#include "shell_diagnostics.h"
#include "shell_cliententitylist.h"
#include "engine_block.h"

// WL_Eyes, so the value view_subject.h quotes can be checked against the enum
// it was quoted from. A restated constant that nothing compares is a second
// declaration nobody would notice going wrong.
#include "imovehelper.h"

COMPILE_TIME_ASSERT( ViewSubject::WATER_LEVEL_EYES == WL_Eyes );

// For the bone-access gate forwarded at the foot of this file.
#include "c_baseanimating.h"

// SourceSharpFogParams, which managed code pushes the viewer's fog through,
// and fogparams_t, which is what it is unpacked into. The second is the
// engine's own struct and lives with the rest of the player's networked view
// state.
#include "sourcesharp.h"

// fogparams_t, which the push above is unpacked into. THROUGH
// c_env_fog_controller.h AND NOT playernet_vars.h DIRECTLY: playernet_vars.h's
// fogplayerparams_t holds a CNetworkHandle( CFogController, m_hCtrl ) whose
// constructor instantiates CNetworkHandleBase<C_FogController>::Set, and that
// needs the class to be COMPLETE. c_env_fog_controller.h defines it (outside
// its own `#ifndef SOURCESHARP`, which covers only the network table and the
// constructor body) and then includes playernet_vars.h, so this order is the
// one every other client TU reaching fogparams_t already gets transitively.
#include "c_env_fog_controller.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
bool SourceSharp_HasLocalViewer()
{
	return SourceSharp_LocalPlayerShell() != NULL;
}

//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// The engine's view entity, when it is not this client's own player.
//
// `render->GetViewEntity()` is the index the engine is rendering FROM;
// `engine->GetLocalPlayer()` is this client's own player index, so the two being
// equal is "looking out of my own head" and there is nothing to override.
//
// GetClientEntity RATHER THAN GetEnt, and the difference is the whole of what
// lets a C# entity hold a view: GetEnt asks a slot for its C_BaseEntity, which
// an entity on the parallel hierarchy has none of. IClientEntity is what both
// hierarchies implement and it declares exactly the two things wanted here, so
// for a stock entity this resolves to the same object and the same two values.
//-----------------------------------------------------------------------------
bool SourceSharp_ViewEntityPose( Vector *pOrigin, QAngle *pAngles )
{
	const int nViewEntity = render->GetViewEntity();

	if ( !ViewSubject::ViewEntityOverridesTheEye( nViewEntity, engine->GetLocalPlayer() ) )
		return false;

	// p7-exit: the SLOT TABLE, not the stock list. The same question and, while
	// the seam exists, the same answer -- every shell is in both. What it buys
	// is clause 2 and a call site that survives the seam's removal.
	IClientEntity *pViewEntity = SourceSharp_ClientEntityFromIndex( nViewEntity );

	if ( !pViewEntity )
		return false;

	if ( pOrigin )
		*pOrigin = pViewEntity->GetAbsOrigin();

	if ( pAngles )
		*pAngles = pViewEntity->GetAbsAngles();

	return true;
}

//-----------------------------------------------------------------------------
// FROM THE BLOCK, not from a second copy.
//
// `EngineBlockHead::Effects` is where an entity's EF_* bits are for both sides
// (engine_block_generated.h), written by managed code and read by whoever
// needs them. Stock's C_BaseEntity::IsEffectActive reads the same word off
// m_fEffects; there is one of it here and this is it.
//-----------------------------------------------------------------------------
bool SourceSharp_ViewerEffectActive( int nEffectBits )
{
	C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell();

	if ( !pPlayer )
		return false;

	return ViewSubject::EffectActive( pPlayer->Block().Head.Effects, nEffectBits );
}

//-----------------------------------------------------------------------------
// The sound listener is underwater when the viewer's eyes are.
//
// Stock: `GetWaterLevel() >= WL_Eyes`, off C_BasePlayer::AudioStateIsUnderwater
// (c_baseplayer.cpp:515-525). WL_Eyes is 3 and EngineBlockHead::WaterLevel is
// the same scale, so this is that comparison against the one copy of the value.
//
// vecMainViewOrigin is stock's observer arm's input — a point-contents test for
// a spectator who has no body to measure — and is unused here for the reason
// view_facts.h gives.
//-----------------------------------------------------------------------------
bool SourceSharp_ViewerAudioIsUnderwater( const Vector &vecMainViewOrigin )
{
	NOTE_UNUSED( vecMainViewOrigin );

	C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell();

	if ( !pPlayer )
		return false;

	return ViewSubject::AudioIsUnderwater( pPlayer->Block().Head.WaterLevel );
}

//-----------------------------------------------------------------------------
// THE VIEWER'S WORLD FOG (plan_entity.md step 8e-render).
//
// IT ANSWERED AN UNCONDITIONAL NULL UNTIL THIS STEP, and the reason was the
// WIRE rather than this file. In stock, fog reaches a client two ways at once:
// CFogController networks its own m_fog through DT_FogController, and the
// player carries a handle to it in CPlayerLocalData::m_PlayerFog. Here
// fogcontroller.cpp is entirely `#ifndef SOURCESHARP` and env_fog_controller is
// EnvFogController.cs, so server.so declares NO DT_FogController -- there was no
// sender for either half, the numbers sat on the server, and
// CRendering3dView::EnableWorldFog passed this NULL into GetFogEnable, which
// answered false on every frame since the flip. Step 7c measured it and made it
// a printed not-shipped row rather than a working feature, and said so.
//
// The channel now: the master controller's RESOLVED values (the server runs the
// lerp) ride in PlayerNetState, the managed client pushes the local player's
// copy through EngineApi.SetViewerFogParams, and this hands back a pointer to
// that copy.
//
// STILL NULL WHEN NOTHING HAS BEEN PUSHED, and that is the point rather than an
// unfinished edge. A map with no env_fog_controller, and a client that has not
// yet been given its player, both get the NULL stock itself produces for a
// player with no fog controller. A zeroed fogparams_t would read as "fog
// enabled = false, start 0, end 0" -- a REAL answer meaning "no fog" -- and
// then "this map has no fog" and "nobody asked" would look identical again,
// which is exactly what 7c refused to let happen.
//
// THE COUNTERS ARE GONE, PHASE 1c, AND THE NULL IS WHY THEY CAN BE. An effect
// that renders nothing also does not crash, so "the fog reaches the client"
// cannot be checked by running the game and looking at it -- and the four
// counters cs_cl_fog_report printed were the runtime way to check it. What
// makes the check possible AT ALL, though, is the NULL above: it is the thing
// that keeps "this map has no fog" and "nobody pushed any" distinguishable,
// and a counter could only report a distinction the NULL already makes. See
// view_facts.h for the full argument and for what read them.
//-----------------------------------------------------------------------------
static fogparams_t g_ViewerFog;
static bool        g_bHaveViewerFog = false;


// One packed RGBA word into the engine's colour type. The mirror of
// shell_worldquery.cpp's SourceSharp_UnpackColor32 on the server side, and
// spelled out for the same reason: CNetworkColor32 has no address to write
// through -- it wants the four components set, and it wants to know they
// changed.
template < typename T >
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
		// CLEARED, WHICH IS NOT "FOG OFF". See the header: the difference is
		// the whole reason this returns a pointer rather than a struct.
		g_bHaveViewerFog = false;
		return;
	}

	g_ViewerFog.enable = pParams->Enable != 0;
	g_ViewerFog.blend  = pParams->Blend != 0;
	g_ViewerFog.radial = pParams->Radial != 0;

	g_ViewerFog.dirPrimary = Vector( pParams->DirPrimaryXYZ[0],
	                                 pParams->DirPrimaryXYZ[1],
	                                 pParams->DirPrimaryXYZ[2] );

	SourceSharp_UnpackViewerColor32( pParams->ColorPrimary,   g_ViewerFog.colorPrimary );
	SourceSharp_UnpackViewerColor32( pParams->ColorSecondary, g_ViewerFog.colorSecondary );

	// THE LERP TARGETS ARE THE CURRENT VALUES, not zero. viewrender.cpp's
	// GetFogColor blends colorPrimary towards colorPrimaryLerpTo whenever
	// lerptime is in the future; leaving the targets zeroed while lerptime is
	// zero happens to be safe today, but a black target beside a live colour is
	// one changed comparison away from a fog that fades to black on its own.
	// The server ran the lerp and published the resolved numbers, so the
	// client's transition is already over: saying so is what these four lines
	// do.
	g_ViewerFog.colorPrimaryLerpTo   = g_ViewerFog.colorPrimary;
	g_ViewerFog.colorSecondaryLerpTo = g_ViewerFog.colorSecondary;
	g_ViewerFog.startLerpTo          = pParams->Start;
	g_ViewerFog.endLerpTo            = pParams->End;
	g_ViewerFog.lerptime             = 0.0f;
	g_ViewerFog.duration             = 0.0f;

	g_ViewerFog.start      = pParams->Start;
	g_ViewerFog.end        = pParams->End;
	g_ViewerFog.farz       = pParams->FarZ;
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


//-----------------------------------------------------------------------------
// THE 3D SKYBOX, AND THE TWO-PART FAULT IT TOOK TO CLOSE IT.
//
// THE LEDGER ROW IS GONE FROM HERE. It was step 7c's, and the whole of its
// argument was that a screenshot cannot tell "this map has no sky camera" from
// "nobody asked". Both halves of the fault behind it are fixed:
//
//   1. REACHABILITY (lane phase4-exit). UpdateAllClientData walked
//      UTIL_PlayerByIndex, which cannot answer on this build, so
//      ClientData_Update never ran and SourceSharp_FillSkyboxParams -- its only
//      caller -- was never reached. StandaloneClientData_Update fixed that and
//      the SERVER has been filling MvLocal().m_skybox3d ever since.
//   2. THE WIRE (this lane). CStandalonePlayer does not network
//      CPlayerLocalData, it networks PlayerNetState, so what the server filled
//      went nowhere. Measured live on ss_sandbox before the fix: 12,270
//      consecutive frames in which the engine said a 3D skybox WAS visible from
//      the eye and this function answered NULL every one of them.
//
// What answers now: the sky camera's resolved block rides in PlayerNetState,
// the managed client pushes the LOCAL player's copy through
// EngineApi.SetViewerSkyboxParams, and this hands back a pointer to that copy.
// Exactly the world fog's shape, one wire over.
//
// STILL NULL WHEN THERE IS NOTHING TO SAY, and the three states are distinct:
//
//   NULL          no sky camera has been heard from -- a map without one, or a
//                 client whose player has not arrived. Stock's CSkyboxView gets
//                 the same NULL for a player with no sky camera and every
//                 caller in viewrender.cpp already handles it.
//   area 255      a camera that IS there and sits in the engine's "nowhere":
//                 outside the sealed world, or a map compiled without vis.
//                 PreRender3dSkyboxWorld refuses it one line after the NULL.
//   a zeroed      NEITHER, and that is why one is never returned. area 0 is a
//   struct        REAL visleaf area, so it would send CSkyboxView at a camera
//                 that is not there.
//
// The NULL arm kept a count instead of a ledger row until Phase 1c took the
// readout that was the only way to see it; view_facts.h says what went and
// why. The refusal itself is unchanged and is what this comment is about.
//-----------------------------------------------------------------------------
static sky3dparams_t g_ViewerSkybox;
static bool          g_bHaveViewerSkybox = false;

void SourceSharp_SetViewerSkyboxParams( const SourceSharpSkyboxParams *pParams )
{
	if ( !pParams )
	{
		// CLEARED, WHICH IS NOT AREA 255. See the block above.
		g_bHaveViewerSkybox = false;
		return;
	}

	g_ViewerSkybox.origin = Vector( pParams->OriginXYZ[0], pParams->OriginXYZ[1],
	                                pParams->OriginXYZ[2] );
	g_ViewerSkybox.scale  = pParams->Scale;
	g_ViewerSkybox.area   = pParams->Area;

	g_ViewerSkybox.fog.enable = pParams->FogEnable != 0;
	g_ViewerSkybox.fog.blend  = pParams->FogBlend != 0;
	g_ViewerSkybox.fog.radial = pParams->FogRadial != 0;

	g_ViewerSkybox.fog.dirPrimary = Vector( pParams->FogDirPrimaryXYZ[0],
	                                        pParams->FogDirPrimaryXYZ[1],
	                                        pParams->FogDirPrimaryXYZ[2] );

	SourceSharp_UnpackViewerColor32( pParams->FogColorPrimary,   g_ViewerSkybox.fog.colorPrimary );
	SourceSharp_UnpackViewerColor32( pParams->FogColorSecondary, g_ViewerSkybox.fog.colorSecondary );

	// THE LERP TARGETS ARE THE CURRENT VALUES, not zero -- the same four lines
	// SourceSharp_SetViewerFogParams carries above and for the identical
	// reason. viewrender.cpp's GetFogColor blends colorPrimary towards
	// colorPrimaryLerpTo whenever lerptime is in the future, and a black target
	// beside a live colour is one changed comparison away from a skybox fog
	// that fades to black on its own. sky_camera runs no transition at all, so
	// saying "the transition is over" is simply true here.
	g_ViewerSkybox.fog.colorPrimaryLerpTo   = g_ViewerSkybox.fog.colorPrimary;
	g_ViewerSkybox.fog.colorSecondaryLerpTo = g_ViewerSkybox.fog.colorSecondary;
	g_ViewerSkybox.fog.startLerpTo          = pParams->FogStart;
	g_ViewerSkybox.fog.endLerpTo            = pParams->FogEnd;
	g_ViewerSkybox.fog.lerptime             = 0.0f;
	g_ViewerSkybox.fog.duration             = 0.0f;

	g_ViewerSkybox.fog.start      = pParams->FogStart;
	g_ViewerSkybox.fog.end        = pParams->FogEnd;
	g_ViewerSkybox.fog.maxdensity = pParams->FogMaxDensity;

	// farz IS NOT SENT AND IS NOT THE SKYBOX'S TO SET. Stock's sky_camera
	// parses no fogfarz key -- CSkyCamera::Spawn fills m_skyboxData.fog from
	// six keys and farz is not one of them -- and CSkyboxView sets zFar to
	// MAX_TRACE_LENGTH itself. Left at its constructed value rather than
	// invented, so a reader comparing this against SkyCamera.cs finds the same
	// field list on both sides.

	g_bHaveViewerSkybox = true;
}

sky3dparams_t *SourceSharp_ViewerSkyboxParams()
{
	if ( !g_bHaveViewerSkybox )
		return NULL;

	return &g_ViewerSkybox;
}

//-----------------------------------------------------------------------------
// The camera mode changed.
//
// Stock's C_BasePlayer::ThirdPersonSwitch is UpdateVisibility() plus the same
// for every bone attachment. Nothing on the managed hierarchy has a bone
// attachment — GetNumBoneAttachments and GetBoneAttachment are C_BaseAnimating's
// and no managed class carries either — so the visibility refresh is the whole
// of the base body, and it is what moves the player's own model in and out of
// the leaf system on a `thirdperson` / `firstperson`.
//
// The shim's third-person pass calls UpdateVisibility every frame anyway
// (shell_client.cpp), so this is the EDGE being handled at the moment it
// happens rather than one frame later — which is the frame a player would see
// the inside of their own head in.
//-----------------------------------------------------------------------------
void SourceSharp_ViewerCameraModeChanged()
{
	if ( C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell() )
		pPlayer->UpdateVisibility();
}

//-----------------------------------------------------------------------------
// THE BONE-ACCESS GATE'S FORWARDERS STOOD HERE, AND THE DEBT THEY CARRIED IS
// PAID -- lane f-bone. view_facts.h's note said "when the gate moves off
// C_BaseAnimating [...] these three go with it", and it has: the gate's state
// and the bodies of SourceSharp_BoneAccessPush / Pop and the scoped
// CSourceSharpBoneAccessScope are now
// sourcesharp/game/client/shell_bone_leaf_library.cpp, declared in
// bone_leaf_library.h. C_BaseAnimating's statics forward to THEM now, which is
// the direction plan_entity.md §6's bone-leaf-library row asks for.
//
// Nothing here re-declares them: this file's callers reach them through
// view_facts.h, which includes that header.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE VIEWER'S VEHICLE, for the movement stick's response curve.
//
// view_facts.h carries the argument. The short of it: this build has no
// vehicles and the server shell is where that is settled, not here --
// CStandalonePlayer::MvIsInAVehicle and CShellPlayerInfo::IsInAVehicle are both
// a flat `return false`, so an in-vehicle arm on this client could not be
// reached by any route.
//
// *pOutCurve IS LEFT ALONE ON A FALSE, and that is not tidiness. in_joystick's
// caller seeds the curve with joy_response_move BEFORE it asks, so a body that
// helpfully zeroed the out-parameter on the way to answering "no vehicle" would
// hand the stick response curve 0 -- a REAL curve -- in place of the convar's.
// That is the fogparams_t mistake in miniature: a zeroed answer that reads as a
// real one.
//-----------------------------------------------------------------------------
bool SourceSharp_ViewerVehicleJoystickResponseCurve( int *pOutCurve )
{
	NOTE_UNUSED( pOutCurve );

	SourceSharp_NotShipped( "player vehicles (joystick response curve)",
		"the managed player is never in a vehicle -- MvIsInAVehicle and "
		"IPlayerInfo::IsInAVehicle are both a flat false -- so the movement "
		"stick always uses joy_response_move and never a vehicle's own curve" );

	return false;
}

//-----------------------------------------------------------------------------
// WHETHER THE VIEWER'S AIM IS BEING ASSISTED, for the stick dampening.
//
// view_facts.h carries the argument. The short of it: m_bAutoAimTarget is HL2
// local player data, neither end of its wire is compiled into this build, and
// HL2_CLIENT_DLL is not defined on this client -- so stock's own reader is not
// compiled either. The ask exists so that in_joystick.cpp stops SPELLING the
// entity types, which is what the census counts and what a later reader would
// take for a live entity ask.
//-----------------------------------------------------------------------------
bool SourceSharp_ViewerAimIsAssisted()
{
	SourceSharp_NotShipped( "HL2 autoaim assist (client)",
		"CHL2_PlayerLocalData::m_bAutoAimTarget is written on the server by "
		"hl2_player.cpp and networked through DT_HL2Local; neither file is in "
		"this build and the state is not in the managed player's networked "
		"declaration, so the joystick never dampens onto a target" );

	return false;
}
