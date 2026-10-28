//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The texture in a render-target slot (SS_RT_*), or NULL for a slot nobody declared.

#pragma once

#include "render_target_store.h"

ITexture *ShellRenderTarget( int nSlot );
