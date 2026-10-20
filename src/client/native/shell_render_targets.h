//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- the render targets client.so holds ================//
//
// The texture in one of shell_render_targets_client.cpp's slots (SS_RT_*,
// render_target_store.h), as managed's RenderTargetCatalog declared it; NULL
// for a slot nobody declared.
//=============================================================================//

#ifndef SHELL_RENDER_TARGETS_H
#define SHELL_RENDER_TARGETS_H

#include "render_target_store.h"

ITexture *ShellRenderTarget( int nSlot );

#endif // SHELL_RENDER_TARGETS_H
