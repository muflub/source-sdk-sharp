//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the standalone player's local block, as server.so owns it
//
// plan_server_novalve.md P5. CPlayerLocalData (game/server/playerlocaldata.h)
// and the three records embedded in it (game/shared/playernet_vars.h) are
// server-only: the engine never reads them by layout. The client receives
// them through DT_Local, whose send table shell_playerlocaldata.cpp builds from
// THESE members' offsets, and decodes them by property name into its own
// C_PlayerLocalData. Per the plan's naming rule they are therefore ss:: types,
// built on public/ headers alone, never a second body under the Valve name.
//
// Members, types and order are the stock ones, so the send table, the save
// datadesc and every reader (shell_movement.cpp's movement state, the area
// bits in shell_gameclients.cpp, the managed skybox in StandaloneClientData_Update)
// keep their meaning. Two departures:
//   * fogplayerparams_t::m_hCtrl is a networked CBaseHandle rather than a
//     CHandle<CFogController>: same layout and wire value (SendPropEHandle
//     reads a CBaseHandle), and server.so has no CFogController.
//   * CPlayerLocalData::UpdateAreaBits(CBasePlayer *) is not declared: its one
//     caller went in P5 wave 8b.
//=============================================================================//
#pragma once

#include "const.h"          // MAX_AREA_STATE_BYTES, MAX_AREA_PORTAL_STATE_BYTES
#include "basehandle.h"
#include "datamap.h"
#include "networkvar.h"
#include "mathlib/vector.h"
#include "dt_send.h"         // EXTERN_SEND_TABLE

class CSkyCamera;

namespace ss
{

#define SS_NUM_AUDIO_LOCAL_SOUNDS 8

// World and 3D-skybox fog.
struct fogparams_t
{
	DECLARE_CLASS_NOBASE( fogparams_t );
	DECLARE_EMBEDDED_NETWORKVAR();
	DECLARE_SIMPLE_DATADESC();

	CNetworkVector( dirPrimary );               // the direction the primary colour faces
	CNetworkColor32( colorPrimary );            // blended toward colorSecondary away from dirPrimary
	CNetworkColor32( colorSecondary );
	CNetworkColor32( colorPrimaryLerpTo );      // the colours a lerp is heading for
	CNetworkColor32( colorSecondaryLerpTo );
	// Range, each beside the value a lerp is heading for. (Stock declares the
	// lerp targets after farz/maxdensity; nothing reads this record by layout:
	// DT_Local and the save datadesc address members by name.)
	CNetworkVar( float, start );                // distance the fog begins at
	CNetworkVar( float, end );                  // distance it reaches maxdensity at
	CNetworkVar( float, startLerpTo );
	CNetworkVar( float, endLerpTo );
	CNetworkVar( float, farz );                 // far clip under this fog, -1 for none
	CNetworkVar( float, maxdensity );           // 0..1

	CNetworkVar( float, lerptime );             // when the lerp completes
	CNetworkVar( float, duration );             // how long it takes
	CNetworkVar( bool, enable );
	CNetworkVar( bool, blend );                 // blend the two colours by direction
	CNetworkVar( bool, radial );                // distance from the eye rather than depth
};

// The fog controller a player is under, and the fade between two fogs.
struct fogplayerparams_t
{
	DECLARE_CLASS_NOBASE( fogplayerparams_t );
	DECLARE_EMBEDDED_NETWORKVAR();
	DECLARE_SIMPLE_DATADESC();

	CNetworkVar( CBaseHandle, m_hCtrl );
	float m_flTransitionTime = -1.0f;

	color32 m_OldColor = { 0, 0, 0, 0 };
	float m_flOldStart = 0.0f;
	float m_flOldEnd = 0.0f;

	color32 m_NewColor = { 0, 0, 0, 0 };
	float m_flNewStart = 0.0f;
	float m_flNewEnd = 0.0f;
};

// The 3D skybox camera and its fog.
struct sky3dparams_t
{
	DECLARE_CLASS_NOBASE( sky3dparams_t );
	DECLARE_EMBEDDED_NETWORKVAR();
	DECLARE_SIMPLE_DATADESC();

	CNetworkVar( int, scale );
	CNetworkVector( origin );
	CNetworkVar( int, area );

	CNetworkVarEmbedded( fogparams_t, fog );
};

// The soundscape a player hears.
struct audioparams_t
{
	DECLARE_CLASS_NOBASE( audioparams_t );
	DECLARE_EMBEDDED_NETWORKVAR();
	DECLARE_SIMPLE_DATADESC();

	CNetworkArray( Vector, localSound, SS_NUM_AUDIO_LOCAL_SOUNDS )
	CNetworkVar( int, soundscapeIndex );   // into soundscape.txt
	CNetworkVar( int, localBits );         // bits 0..3: which localSound entries are used
	CNetworkVar( int, entIndex );          // the entity that set the soundscape
};

// Sent only to the player it belongs to (DT_Local).
class CPlayerLocalData
{
public:
	DECLARE_SIMPLE_DATADESC();
	DECLARE_CLASS_NOBASE( CPlayerLocalData );
	DECLARE_EMBEDDED_NETWORKVAR();

	CPlayerLocalData();

	// What the client sees: visibility, HUD and view.
	CNetworkArray( unsigned char, m_chAreaBits, MAX_AREA_STATE_BYTES );               // areas potentially visible
	CNetworkArray( unsigned char, m_chAreaPortalBits, MAX_AREA_PORTAL_STATE_BYTES );  // open area portals
	CNetworkVar( int, m_iHideHUD );        // HUD sections to hide
	CNetworkVar( float, m_flFOVRate );     // rate the FOV changes at

	// The movement loop's ducking, jumping and falling state, networked so the
	// client predicts it.
	CNetworkVar( bool, m_bDucked );        // fully ducked
	CNetworkVar( bool, m_bDucking );       // part way down
	CNetworkVar( bool, m_bInDuckJump );
	CNetworkVar( float, m_flDucktime );    // time left until fully ducked
	CNetworkVar( float, m_flDuckJumpTime );
	CNetworkVar( float, m_flJumpTime );    // when to auto-unduck after a crouch jump
	CNetworkVar( float, m_flFallVelocity );   // speed at the moment the ground was hit
	CNetworkVar( float, m_flStepSize );      // the highest step the player walks up
	CNetworkVar( bool, m_bAllowAutoMovement );
	CNetworkVar( bool, m_bSlowMovement );

	// Server-side only: the loop's scratch between frames, and view overrides.
	int m_nStepside;                       // which foot the next step sound uses
	int m_nOldButtons;
	float m_flOldForwardMove;
	Vector m_vecOverViewpoint;             // a viewpoint overriding the player's
	CSkyCamera *m_pOldSkyCamera;

	// Stock declares these interleaved with the networked members above, in
	// another order; nothing reads this class by layout (DT_Local and the save
	// datadesc address members by name, and the engine never sees it).
	CNetworkQAngle( m_vecPunchAngle );     // auto-decaying view angle adjustment
	CNetworkQAngle( m_vecPunchAngleVel );
	CNetworkVar( bool, m_bDrawViewmodel );

	CNetworkVar( bool, m_bWearingSuit );
	CNetworkVar( bool, m_bPoisoned );
	CNetworkVar( bool, m_bForceLocalPlayerDraw );

	CNetworkVarEmbedded( sky3dparams_t, m_skybox3d );
	CNetworkVarEmbedded( fogplayerparams_t, m_PlayerFog );
	fogparams_t m_fog;
	CNetworkVarEmbedded( audioparams_t, m_audio );


	CNetworkString( m_szScriptOverlayMaterial, MAX_PATH );
};

EXTERN_SEND_TABLE( DT_Local );

} // namespace ss
