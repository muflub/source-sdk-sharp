//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// Free functions client.so still names whose stock files are not built.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "view.h"
#include "view_shared.h"
#include "iviewrender.h"

class C_BasePlayer;
struct cplane_t;

// No reflective glass exists on the client.
bool IsReflectiveGlassInView(const CViewSetup& view, cplane_t& plane)
{
	return false;
}
