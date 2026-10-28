//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#define ROTATION_SPEED				0.1

		// Take N seconds to expand to SMOKESPHERE_MAX_RADIUS.

void InitSmokeFogOverlay();
void TermSmokeFogOverlay();
void DrawSmokeFogOverlay();

// Set these before calling DrawSmokeFogOverlay.
extern float	g_SmokeFogOverlayAlpha;
extern Vector	g_SmokeFogOverlayColor;
