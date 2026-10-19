//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the tool framework, off =============================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// SFM IS DEAD (user ruling 2026-10-19): Valve's tool framework and the Source
// Filmmaker recording hooks are deleted, not ported. toolframework_client.cpp
// and entity_client_tools.cpp left client.so with that ruling.
//
// These are the names the Valve TUs still compiled into client.so link
// against, and each answers "tools are off":
//
//   clientshadowmgr.cpp    ToolsEnabled, ToolFramework_RecordMaterialParams
//
// ToolsEnabled() is false, so the caller skips its tool arms and the material
// recording is never reached. Nothing here decides anything.
//
// flashlighteffect.cpp's two (clienttools, never in recording mode, and
// ToolFramework_PostToolMessage) went with that file (C7 cluster 12): the
// managed flashlight posts no tool messages.
//
// VCLIENTTOOLS001 IS NOT EXPORTED. Only a tool asks for it, and no binary of
// the install names it (`strings -a` over Source SDK Base 2013 Multiplayer's
// and the dedicated server's bin/linux64/*.so, 2026-10-19).
//
// Exit: clientshadowmgr.cpp's eviction.
//=============================================================================//

class IMaterial;

#include "toolframework_client.h"

bool ToolsEnabled()
{
	return false;
}

void ToolFramework_RecordMaterialParams( IMaterial * )
{
}
