//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// Free functions client.so still names whose stock files are not built.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "view.h"
#include "view_shared.h"
#include "iviewrender.h"

class C_BasePlayer;
struct cplane_t;

// No reflective glass exists on the client.
bool IsReflectiveGlassInView(const CViewSetup& view, cplane_t& plane)
{
	return false;
}

// Converts between viewmodel-FOV space and world space; stock's arithmetic.
// Live for particles attached to the managed viewmodel, so it must not stub.
void FormatViewModelAttachment(Vector& vOrigin, bool bInverse)
{
	// Presumably, SetUpView has been called so we know our FOV and render origin.
	const CViewSetup* pViewSetup = view->GetPlayerViewSetup();

	float worldx = tan(pViewSetup->fov * M_PI / 360.0);
	float viewx = tan(pViewSetup->fovViewmodel * M_PI / 360.0);

	// aspect ratio cancels out, so only need one factor
	// the difference between the screen coordinates of the 2 systems is the ratio
	// of the coefficients of the projection matrices (tan (fov/2) is that coefficient)
	// NOTE: viewx was coming in as 0 when folks set their viewmodel_fov to 0 and show their weapon.
	float factorX = viewx ? (worldx / viewx) : 0.0f;
	float factorY = factorX;

	// Get the coordinates in the viewer's space.
	Vector tmp = vOrigin - pViewSetup->origin;
	Vector vTransformed(MainViewRight().Dot(tmp), MainViewUp().Dot(tmp), MainViewForward().Dot(tmp));

	// Now squash X and Y.
	if (bInverse)
	{
		if (factorX != 0 && factorY != 0)
		{
			vTransformed.x /= factorX;
			vTransformed.y /= factorY;
		}
		else
		{
			vTransformed.x = 0.0f;
			vTransformed.y = 0.0f;
		}
	}
	else
	{
		vTransformed.x *= factorX;
		vTransformed.y *= factorY;
	}

	// Transform back to world space.
	Vector vOut =
		(MainViewRight() * vTransformed.x) + (MainViewUp() * vTransformed.y) + (MainViewForward() * vTransformed.z);
	vOrigin = pViewSetup->origin + vOut;
}
