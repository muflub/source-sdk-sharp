//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Server-side hooks that route mod decisions into C#.
#pragma once

#include "igameentity.h"
struct edict_t;

// Whether a ServerClass was declared by C# rather than registered by the SDK.
class ServerClass;
class CServerTempEnts;
class CServerNetTables;
bool SourceSharp_IsManagedServerClass( const CServerNetTables &tables, const CServerTempEnts &tempEnts,
									   const ServerClass *pClass );

// The seven shared network string tables, created before managed's are.
class INetworkStringTable;
class INetworkStringTableContainer;
INetworkStringTable *SourceSharp_CreateNetworkStringTables( INetworkStringTableContainer *pTables );

// Acquires every engine interface this binary reaches by global. False on the
// first miss, as stock's DLLInit.
typedef void *( *CreateInterfaceFn )( const char *pName, int *pReturnCode );
class ICvar;
class CServerEngineInterfaces;
bool SourceSharp_AcquireEngineInterfaces( CServerEngineInterfaces &bound, CreateInterfaceFn appSystemFactory,
										  CreateInterfaceFn physicsFactory, CreateInterfaceFn fileSystemFactory,
										  ICvar *pCvar );

int32_t SourceSharp_EntitiesInvalidateMdlCacheAll();
