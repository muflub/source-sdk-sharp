//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The engine-free rules behind the view's facts.
#pragma once

namespace ViewSubject
{

// imovehelper.h's WL_Eyes, restated so this header stays engine-free; shell_view_facts.cpp
// asserts it against the enum.
const int WATER_LEVEL_EYES = 3;

// The listener is underwater at eye level. >= because WL_Eyes is the top of the scale.
inline bool AudioIsUnderwater(int nWaterLevel)
{
	return nWaterLevel >= WATER_LEVEL_EYES;
}

// True when any of the bits are set, as C_BaseEntity::IsEffectActive.
inline bool EffectActive(int nEffects, int nEffectMask)
{
	return (nEffects & nEffectMask) != 0;
}

// Whether the frame's view comes from the engine's view entity rather than the viewer's
// eyes. Index 0 (the world) and negatives (no view entity) are refused as well as the
// local player's own index.
inline bool ViewEntityOverridesTheEye(int nViewEntity, int nLocalPlayer)
{
	if (nViewEntity <= 0)
		return false;

	return nViewEntity != nLocalPlayer;
}

} // namespace ViewSubject
