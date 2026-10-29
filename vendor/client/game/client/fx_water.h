//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "fx.h"

extern void FX_WaterRipple(const Vector& origin, float scale, Vector* pColor, float flLifetime = 1.5,
						   float flAlpha = 1);
extern void FX_GunshotSplash(const Vector& origin, const Vector& normal, float scale);
extern void FX_GunshotSlimeSplash(const Vector& origin, const Vector& normal, float scale);

// Purpose: Retrieve and alter lighting for splashes
// Input  : position - point to check
//			*color - tint of the lighting at this point
//			*luminosity - adjusted luminosity at this point
inline void FX_GetSplashLighting(Vector position, Vector* color, float* luminosity)
{
	// Compute our lighting at our position
	Vector totalColor = engine->GetLightForPoint(position, true);

	// Get our lighting information
	UTIL_GetNormalizedColorTintAndLuminosity(totalColor, color, luminosity);

	// Fake a specular highlight (too dim otherwise)
	if (luminosity != NULL)
	{
		*luminosity = MIN(1.0f, (*luminosity) * 4.0f);

		// Clamp so that we never go completely translucent
		if (*luminosity < 0.25f)
		{
			*luminosity = 0.25f;
		}
	}

	// Only take a quarter of the tint, mostly we want to be white
	if (color != NULL)
	{
		(*color) = ((*color) * 0.25f) + Vector(0.75f, 0.75f, 0.75f);
	}
}
