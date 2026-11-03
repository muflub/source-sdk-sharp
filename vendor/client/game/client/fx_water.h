//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "fx.h"
#include "imanaged_areas_client.h"

// Purpose: Retrieve and alter lighting for splashes
// Input  : position - point to check
//			*color - tint of the lighting at this point
//			*luminosity - adjusted luminosity at this point
inline void FX_GetSplashLighting(IVEngineClient* pEngine, IManagedParticlesClient* pArea, Vector position,
								 Vector* color, float* luminosity)
{
	// Compute our lighting at our position
	Vector totalColor = pEngine->GetLightForPoint(position, true);

	// Get our lighting information. With no managed side, the tint is black and the
	// luminosity 0, as for a black light.
	Vector vTint(0, 0, 0);
	float flLuminosity = 0.0f;
	if (pArea)
		pArea->SplashTint(totalColor.Base(), vTint.Base(), &flLuminosity);
	if (color != NULL)
		*color = vTint;
	if (luminosity != NULL)
		*luminosity = flLuminosity;

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
