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

// The server-browser game description; a built-in string when the managed
// runtime is unavailable.
const char *SourceSharp_GetGameDescription();

void SourceSharp_OnClientPutInServer( edict_t *pEntity, const char *pPlayerName );
void SourceSharp_OnClientActive( edict_t *pEntity, bool bLoadGame );

// The 3D skybox a managed sky_camera pushed, for ClientData_Update when no
// CSkyCamera exists. False when nothing was pushed.
namespace ss
{
struct sky3dparams_t;
}

bool SourceSharp_FillSkyboxParams( ss::sky3dparams_t &out );

// Whether a ServerClass was declared by C# rather than registered by the SDK.
class ServerClass;
bool SourceSharp_IsManagedServerClass( const ServerClass *pClass );

// The seven shared network string tables, created before managed's are.
void SourceSharp_CreateNetworkStringTables();

// Acquires every engine interface this binary reaches by global. False on the
// first miss, as stock's DLLInit.
typedef void *( *CreateInterfaceFn )( const char *pName, int *pReturnCode );
bool SourceSharp_AcquireEngineInterfaces( CreateInterfaceFn appSystemFactory, CreateInterfaceFn physicsFactory,
										  CreateInterfaceFn fileSystemFactory );

int32_t SourceSharp_EntitiesInvalidateMdlCacheAll();

// The managed pre-entity level init, reached from the world's precache.
void SourceSharp_ManagedLevelInitPreEntity( const char *pszMapName );

// For CheckTransmit: 1 with *pOutArea set to the client's 3D-skybox area, else 0.
int SourceSharp_SkyboxAreaForClient( int iClientEdict, int *pOutArea );

// 1 with the client's ear position in pOutXYZ, else 0.
int SourceSharp_EarPositionForClient( int iClientEdict, float *pOutXYZ );

// The area portal walk for ClientSetupVisibility. Sets the client's bits in
// pPortalBits (zeroed by the caller) and returns -1, or a portal number too large
// for the bits. -1 with nothing written when managed is not loaded.
int SourceSharp_AreaPortalVisibility( const float *pOriginXYZ, float flFovDistanceAdjustFactor,
									  unsigned char *pPortalBits, int nPortalBytes );
