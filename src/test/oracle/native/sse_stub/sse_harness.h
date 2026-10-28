//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// The stand-in client world the PRISTINE game/client/ScreenSpaceEffects.cpp
// is compiled into for screenspaceeffects_oracle_test.
//
// STOCK: the subject itself (included whole by the test), its own header,
// tier1's KeyValues, strtools' Q_stricmp, tier0's CommandLine and the
// material headers.
//
// STAND-IN: cbase.h, cdll_client_int.h, view_scene.h and viewrender.h are this
// header (sse_stub/); Warning is the log's (the subject's one warning, "Could
// not find screen space effect %s", becomes a row); UpdateScreenEffectTexture
// and GetFullFrameFrameBufferTexture, which only CExampleEffect::Render's
// enabled arm reaches, log that they were reached.
//
//=============================================================================

#pragma once

#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "tier1/KeyValues.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "materialsystem/itexture.h"

// The log every stand-in and recording effect writes to.
void SseLog( const char *pFmt, ... );

// The subject's Warning, recorded.
void SseWarning( const char *pFmt, ... );
#define Warning SseWarning

// view_scene.h's and rendertexture.h's, as CExampleEffect::Render's enabled
// arm would reach them.
void UpdateScreenEffectTexture( int textureIndex, int x, int y, int w, int h, bool bDestFullScreen = false,
                                Rect_t *pActualRect = NULL );
ITexture *GetFullFrameFrameBufferTexture( int textureIndex );
