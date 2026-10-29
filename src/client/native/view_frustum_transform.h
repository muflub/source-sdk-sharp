//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// A point through a row-major 4x4 world-to-surface matrix, divided by w. Answers 1, with
// x and y pushed far off screen, when the point is behind the eye; 0 otherwise.

#pragma once

inline int ViewFrustumTransform( const float *m, const float *point, float *screen )
{
	float x = m[0] * point[0] + m[1] * point[1] + m[2] * point[2] + m[3];
	float y = m[4] * point[0] + m[5] * point[1] + m[6] * point[2] + m[7];
	const float w = m[12] * point[0] + m[13] * point[1] + m[14] * point[2] + m[15];
	screen[2] = 0.0f;

	if ( w < 0.001f )
	{
		screen[0] = x * 100000;
		screen[1] = y * 100000;
		return 1;
	}

	const float invw = 1.0f / w;
	screen[0] = x * invw;
	screen[1] = y * invw;
	return 0;
}
