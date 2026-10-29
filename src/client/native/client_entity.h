//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's entity is the shared face (igameentity.h), named C_BaseEntity as the
// stock headers expect; this supplies the rest of what c_baseentity.h gave them.
#pragma once

// Stock c_baseentity.h's includes, in its order; the client's headers rely on them.

#include "client_class.h"

// Must precede iclientshadowmgr.h, which names it.
typedef CHandle<C_BaseEntity> EHANDLE;

#include "iclientshadowmgr.h"

#include "networkvar.h"
#include "interpolatedvar.h"
#include "isaverestore.h"

#include "entity_abs_query_state.h"

// playernet_vars.h needs igameentity.h first.
#include "igameentity.h"
#include "playernet_vars.h"
#include "c_env_fog_controller.h"

struct solid_t;
class C_RecipientFilter;

// How many user commands the engine keeps (stock c_baseentity.h).
#define MULTIPLAYER_BACKUP 90

// The spectator mode a kill cam shows, 0 for none.
extern int g_nKillCamMode;
