//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — what client code knows of the client entity =======//
// Stock game/client/c_baseentity.h is C_BaseEntity, which client.so does not
// build: the client's entity is the shared face (igameentity.h), named
// C_BaseEntity here because the stock headers name it that and only
// forward-declare it (igameentity_fwd.h). This is the rest of what those
// headers and the client's code expected beside it: the handle typedef, the
// command backup count, and the routes from an engine object to the face.
//=============================================================================//
#pragma once

// What stock c_baseentity.h put in front of the client's headers, which do
// not include it themselves, in its order; less the entity's own members.
#include "mathlib/vector.h"
#include "tier1/convar.h"
#include "tier0/threadtools.h"
#include "igameentity_fwd.h"
#include "shared_classnames.h"
#include "icliententityinternal.h"
#include "engine/ivmodelinfo.h"
#include "engine/ivmodelrender.h"
#include "client_class.h"
#include "ehandle.h"

// The client's version of EHANDLE (stock c_baseentity.h); iclientshadowmgr.h
// names it.
typedef CHandle< C_BaseEntity > EHANDLE;

#include "iclientshadowmgr.h"
#include "iclientunknown.h"
#include "iclientrenderable.h"
#include "client_thinklist.h"
#include "soundflags.h"
#include "shareddefs.h"
#include "networkvar.h"
#include "interpolatedvar.h"
#include "isaverestore.h"
#include "cdll_client_int.h"
#include "handle_cross.h"
#include "entity_debug_globals.h"
#include "entity_abs_query_state.h"
#include "c_baseentity_statics.h"

// playernet_vars.h's fog parameters hold a handle to it, and it needs them
// and the face it derives from.
#include "igameentity.h"
#include "playernet_vars.h"
#include "c_env_fog_controller.h"

// Two of stock c_baseentity.h's forward declarations, which the client's
// headers count on.
struct solid_t;
class C_RecipientFilter;

// How many user commands the engine keeps (stock c_baseentity.h).
#define MULTIPLAYER_BACKUP 90

// The spectator mode a kill cam shows, 0 for none (shell_player_orphans.cpp).
extern int g_nKillCamMode;
