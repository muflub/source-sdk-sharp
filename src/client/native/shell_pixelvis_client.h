//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Command words of IManagedPixelVisClient::EndView's reply; managed code mirrors these values.

#pragma once

enum
{
	SS_PIXELVIS_CMD_BIND = 1, // material
	SS_PIXELVIS_CMD_DEPTHRANGE = 2, // near, far (float bits)
	SS_PIXELVIS_CMD_PROXY = 3, // query id, material, 12 vertices x 3 (float bits)
	SS_PIXELVIS_PROXY_WORDS = 3 + 36,
	SS_PIXELVIS_VIEW_FLOATS = 29, // id, origin, forward, right, up, world-to-screen (row-major)
};

void ShellPixelVisEndView();
void ShellPixelVisEndScene();
