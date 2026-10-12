//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — baseentity.cpp's statics, out of the class =========//
//
// CBaseEntity members that needed no `this`, lifted out of the class scope when
// plan_baseentity.md Phase 13 retired CBaseEntity. The server's counterpart to
// game/client/c_baseentity_statics.h, and named for it.
//
// NONE IS LEFT. The last, GameSound_EmitSentenceByIndex (EmitSentenceByIndex,
// handing a sentence to enginesound by entity index), went in the server gc
// cull (2026-10-01): its one caller, SENTENCEG_PlaySequentialSz, had no caller,
// and --print-gc-sections dropped both. The header stays for its includers.
//
// THE OTHER FOUR WENT AS DEAD CODE (P5 lane E). EntityCreate_Spawned and
// EntityCreate_NoSpawn (CBaseEntity::Create / CreateNoSpawn) had no caller in
// any compiled TU, server.so carried neither (--gc-sections dropped both), and
// EntityCreate_Spawned called DispatchSpawn, which nothing defines.
// EntityUse_ShouldToggle and EntityPredict_CreateByName were declared here and
// defined nowhere. With ShouldToggle went the only USE_TYPE, and so the
// shareddefs.h include. With the two creators went the only IGameEntity in
// this header, and so igameentity.h.
//
// SERVER ONLY.
//
//=============================================================================//
#ifndef BASEENTITY_STATICS_H
#define BASEENTITY_STATICS_H
#ifdef _WIN32
#pragma once
#endif

#include "mathlib/vector.h"
#include "soundflags.h"

class IRecipientFilter;

#endif // BASEENTITY_STATICS_H
