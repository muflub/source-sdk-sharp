//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The command words IManagedGlowClient::DrawOverlays answers with (GlowCommand
// in C# must match), the view it is handed, and the two plan ops' bodies.

#pragma once

enum
{
	SS_GLOW_CMD_CLIP = 1, // mode: 0 on (its answer kept), 1 off, 2 the kept answer
	SS_GLOW_CMD_HDRSCALE = 2, // material, $hdrcolorscale (float bits)
	SS_GLOW_CMD_QUAD = 3, // material, 4 x (position, colour, s, t) (float bits)
	SS_GLOW_CMD_WIREQUAD = 4, // material, 4 x position (float bits)
	SS_GLOW_QUAD_WORDS = 2 + 4 * 8,
	SS_GLOW_WIREQUAD_WORDS = 2 + 4 * 3,
	SS_GLOW_VIEW_FLOATS = 29, // origin, forward, zFar, 5 x (plane normal, dist), wireframe, r_drawsprites
};

void ShellGlowDrawOverlays(bool bCacheFullSceneState);
void ShellGlowUpdateSkyOverlays(float flZFar, bool bCacheFullSceneState);
