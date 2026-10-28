//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "edict.h"

#include "tier1/KeyValues.h"

class CCommand;

// you will also want to listen for game events via IGameEventManager::AddListener()

typedef enum
{
	PLUGIN_CONTINUE = 0, // keep going
	PLUGIN_OVERRIDE, // run the game dll function but use our return value instead
	PLUGIN_STOP, // don't run the game dll function at all
} PLUGIN_RESULT;

typedef enum
{
	eQueryCvarValueStatus_ValueIntact = 0, // It got the value fine.
	eQueryCvarValueStatus_CvarNotFound = 1,
	eQueryCvarValueStatus_NotACvar = 2, // There's a ConCommand, but it's not a ConVar.
	eQueryCvarValueStatus_CvarProtected =
		3 // The cvar was marked with FCVAR_SERVER_CAN_NOT_QUERY, so the server is not allowed to have its value.
} EQueryCvarValueStatus;

typedef int QueryCvarCookie_t;
#define InvalidQueryCvarCookie -1

typedef enum
{
	DIALOG_MSG = 0, // just an on screen message
	DIALOG_MENU, // an options menu
	DIALOG_TEXT, // a richtext dialog
	DIALOG_ENTRY, // an entry box
	DIALOG_ASKCONNECT // Ask the client to connect to a specified IP address. Only the "time" and "title" keys are used.
} DIALOG_TYPE;
