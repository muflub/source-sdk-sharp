//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Finding the standalone hierarchy's entities by classname. Registration is
// CServerEntityLifecycle (shell_entity_lifecycle.h); op routing is shell_dispatch.cpp.
#pragma once

#include "shell_standalone.h"

// The next entity of a classname after pStartAfter. NULL both when there is none and
// when pStartAfter has been removed.
CStandaloneEntity *StandaloneEntities_FindByClassname( CStandaloneEntity *pStartAfter, const char *pClassName );
