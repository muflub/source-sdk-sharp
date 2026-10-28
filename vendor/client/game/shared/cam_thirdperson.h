//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// step c8-pch-residue: this header opened c_baseplayer.h (baseplayer.h on the
// server) and names nothing from either -- CThirdPersonManager is Vector,
// QAngle and floats throughout.  It was residue, and together with usercmd.h's
// imovehelper.h it was the second of the two edges by which in_mouse.cpp
// reached the entity layer.  Every .cpp that includes this one and does use the
// player either sees it through the precompiled header or asks by name -- all
// twelve includers were rebuilt and every one of them still compiles, and only
// three objects changed at all (in_camera, in_mouse, in_steamcontroller), each
// by losing two file-local rodata constants and no global symbol.

//-------------------------------------------------- Constants

#define CAM_HULL_OFFSET		14.0    // the size of the bounding hull used for collision checking

class CThirdPersonManager
{
public:

	CThirdPersonManager();
	
	// PositionCamera was here. It swept a hull from the player entity's eye to
	// the wanted camera spot and wrote m_flTargetFraction / m_flUpFraction from
	// what it found. Step 7c removed it: its subject was the local player
	// ENTITY, both callers handed it a NULL, and the sweep it did is now the
	// shim's -- shell_client.cpp's third-person pass, over the rule in
	// sourcesharp/game/shared/thirdperson_camera.h. See the .cpp.

	void	Init( void );

private:

	// What is the current camera offset from the view origin?
	Vector		m_vecCameraOffset;
	// Distances from the center
	Vector		m_vecDesiredCameraOffset;

	Vector m_vecCameraOrigin;

	bool	m_bUseCameraOffsets;

	QAngle	m_ViewAngles;

	float	m_flFraction;
	float	m_flUpFraction;

	float	m_flTargetFraction;
	float	m_flTargetUpFraction;

	bool	m_bOverrideThirdPerson;

	bool	m_bForced;

	float	m_flUpOffset;

	float	m_flLerpTime;
	float	m_flUpLerpTime;
};

extern CThirdPersonManager g_ThirdPersonManager;
