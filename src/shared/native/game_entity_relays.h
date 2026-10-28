//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Entity facts that shared files ask for, declared over IGameEntity and defined once per
// side beside its shell.
#pragma once

#include "basehandle.h"

#include "igameentity_fwd.h"

// The entity's own handle, by value.
CBaseHandle GameEntity_RefHandle( const IGameEntity *pEnt );

// The engine's index for the entity, or -1 (also for NULL), as entindex() answers.
int GameEntity_EntIndex( const IGameEntity *pEnt );

// The model index modelinfo takes; 0 for NULL or for an entity with no model.
int GameEntity_ModelIndex( const IGameEntity *pEnt );
