//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The standalone player's local block: CPlayerLocalData and its embedded records, as
// server.so's own ss:: types. Members, types and order are stock's. m_hCtrl is a plain
// networked CBaseHandle, since server.so has no CFogController.
#pragma once

#include "const.h"
#include "basehandle.h"
#include "datamap.h"
#include "networkvar.h"
#include "mathlib/vector.h"
#include "dt_send.h"

class CSkyCamera;

namespace ss
{

#define SS_NUM_AUDIO_LOCAL_SOUNDS 8

// World and 3D-skybox fog.
struct fogparams_t
{
	DECLARE_CLASS_NOBASE(fogparams_t);
	DECLARE_EMBEDDED_NETWORKVAR();
	DECLARE_SIMPLE_DATADESC();

	CNetworkVector(dirPrimary); // the direction the primary colour faces
	CNetworkColor32(colorPrimary); // blended toward colorSecondary away from dirPrimary
	CNetworkColor32(colorSecondary);
	CNetworkColor32(colorPrimaryLerpTo); // the colours a lerp is heading for
	CNetworkColor32(colorSecondaryLerpTo);
	// Range, each beside its lerp target. Members are addressed by name, never by layout.
	CNetworkVar(float, start); // distance the fog begins at
	CNetworkVar(float, end); // distance it reaches maxdensity at
	CNetworkVar(float, startLerpTo);
	CNetworkVar(float, endLerpTo);
	CNetworkVar(float, farz); // far clip under this fog, -1 for none
	CNetworkVar(float, maxdensity); // 0..1

	CNetworkVar(float, lerptime); // when the lerp completes
	CNetworkVar(float, duration); // how long it takes
	CNetworkVar(bool, enable);
	CNetworkVar(bool, blend); // blend the two colours by direction
	CNetworkVar(bool, radial); // distance from the eye rather than depth
};

// The fog controller a player is under, and the fade between two fogs.
struct fogplayerparams_t
{
	DECLARE_CLASS_NOBASE(fogplayerparams_t);
	DECLARE_EMBEDDED_NETWORKVAR();
	DECLARE_SIMPLE_DATADESC();

	CNetworkVar(CBaseHandle, m_hCtrl);
	float m_flTransitionTime = -1.0f;

	color32 m_OldColor = {0, 0, 0, 0};
	float m_flOldStart = 0.0f;
	float m_flOldEnd = 0.0f;

	color32 m_NewColor = {0, 0, 0, 0};
	float m_flNewStart = 0.0f;
	float m_flNewEnd = 0.0f;
};

// The 3D skybox camera and its fog.
struct sky3dparams_t
{
	DECLARE_CLASS_NOBASE(sky3dparams_t);
	DECLARE_EMBEDDED_NETWORKVAR();
	DECLARE_SIMPLE_DATADESC();

	CNetworkVar(int, scale);
	CNetworkVector(origin);
	CNetworkVar(int, area);

	CNetworkVarEmbedded(fogparams_t, fog);
};

// The soundscape a player hears.
struct audioparams_t
{
	DECLARE_CLASS_NOBASE(audioparams_t);
	DECLARE_EMBEDDED_NETWORKVAR();
	DECLARE_SIMPLE_DATADESC();

	CNetworkArray(Vector, localSound, SS_NUM_AUDIO_LOCAL_SOUNDS)
		CNetworkVar(int, soundscapeIndex); // into soundscape.txt
	CNetworkVar(int, localBits); // bits 0..3: which localSound entries are used
	CNetworkVar(int, entIndex); // the entity that set the soundscape
};

// Sent only to the player it belongs to (DT_Local).
class CPlayerLocalData
{
public:
	DECLARE_SIMPLE_DATADESC();
	DECLARE_CLASS_NOBASE(CPlayerLocalData);
	DECLARE_EMBEDDED_NETWORKVAR();

	CPlayerLocalData();

	// What the client sees: visibility, HUD and view.
	CNetworkArray(unsigned char, m_chAreaBits, MAX_AREA_STATE_BYTES); // areas potentially visible
	CNetworkArray(unsigned char, m_chAreaPortalBits, MAX_AREA_PORTAL_STATE_BYTES); // open area portals
	CNetworkVar(int, m_iHideHUD); // HUD sections to hide
	CNetworkVar(float, m_flFOVRate); // rate the FOV changes at

	// The movement loop's ducking, jumping and falling state, networked so the
	// client predicts it.
	CNetworkVar(bool, m_bDucked); // fully ducked
	CNetworkVar(bool, m_bDucking); // part way down
	CNetworkVar(bool, m_bInDuckJump);
	CNetworkVar(float, m_flDucktime); // time left until fully ducked
	CNetworkVar(float, m_flDuckJumpTime);
	CNetworkVar(float, m_flJumpTime); // when to auto-unduck after a crouch jump
	CNetworkVar(float, m_flFallVelocity); // speed at the moment the ground was hit
	CNetworkVar(float, m_flStepSize); // the highest step the player walks up
	CNetworkVar(bool, m_bAllowAutoMovement);
	CNetworkVar(bool, m_bSlowMovement);

	// Server-side only: the loop's scratch between frames, and view overrides.
	int m_nStepside; // which foot the next step sound uses
	int m_nOldButtons;
	float m_flOldForwardMove;
	Vector m_vecOverViewpoint; // a viewpoint overriding the player's
	CSkyCamera* m_pOldSkyCamera;

	// Stock interleaves these with the members above; nothing reads this class by layout.
	CNetworkQAngle(m_vecPunchAngle); // auto-decaying view angle adjustment
	CNetworkQAngle(m_vecPunchAngleVel);
	CNetworkVar(bool, m_bDrawViewmodel);

	CNetworkVar(bool, m_bWearingSuit);
	CNetworkVar(bool, m_bPoisoned);
	CNetworkVar(bool, m_bForceLocalPlayerDraw);

	CNetworkVarEmbedded(sky3dparams_t, m_skybox3d);
	CNetworkVarEmbedded(fogplayerparams_t, m_PlayerFog);
	fogparams_t m_fog;
	CNetworkVarEmbedded(audioparams_t, m_audio);

	CNetworkString(m_szScriptOverlayMaterial, MAX_PATH);
};

EXTERN_SEND_TABLE(DT_Local);

} // namespace ss
