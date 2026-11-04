//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The placing shadow's box: the collision hull widened by BBLOAT, stock's
// C_ObjectSentrygun::GetShadowRenderBounds placing arm (c_obj_sentrygun.cpp:
// 588-597). The z is untouched -- bbBloat is (10, 10, 0) -- because the floor
// shadow's height comes from the cast, not the box.

#pragma once

constexpr float StandaloneShadowHull_BloatXY = 10.0f;

inline void StandaloneShadowHull_Bounds(const float obbMinsXYZ[3], const float obbMaxsXYZ[3], float minsXYZ[3],
										float maxsXYZ[3])
{
	minsXYZ[0] = obbMinsXYZ[0] - StandaloneShadowHull_BloatXY;
	minsXYZ[1] = obbMinsXYZ[1] - StandaloneShadowHull_BloatXY;
	minsXYZ[2] = obbMinsXYZ[2];
	maxsXYZ[0] = obbMaxsXYZ[0] + StandaloneShadowHull_BloatXY;
	maxsXYZ[1] = obbMaxsXYZ[1] + StandaloneShadowHull_BloatXY;
	maxsXYZ[2] = obbMaxsXYZ[2];
}
