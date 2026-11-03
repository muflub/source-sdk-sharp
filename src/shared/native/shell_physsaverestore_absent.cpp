//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The server's physics save/restore: nothing saves, so forgetting models is a no-op.

#include "ss_physsaverestore.h"

void ss::PhysSaveRestore_ForgetAllModels() {}
