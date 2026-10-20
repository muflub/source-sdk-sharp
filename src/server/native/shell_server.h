//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — server shell bridge =================================//
//
// Declarations for the server-side hooks that route mod decisions into C#.
// Implemented in sourcesharp_gamesystem.cpp, which owns the managed API table.
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_SERVER_H
#define SOURCESHARP_SHELL_SERVER_H
#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>

#include "igameentity.h"
struct edict_t;

// Returns the server-browser game description. Falls back to a built-in string
// when the managed runtime is unavailable.
const char *SourceSharp_GetGameDescription();

void SourceSharp_OnClientPutInServer( edict_t *pEntity, const char *pPlayerName );
void SourceSharp_OnClientActive( edict_t *pEntity, bool bLoadGame );

// The 3D skybox a managed sky_camera pushed, if there is one.
//
// The one place the SDK has to ask C# about this family, and it is here because
// the destination cannot move: the skybox lands in CPlayerLocalData::m_skybox3d,
// a networked embedded struct on the player, and stock reaches it by walking a
// linked list of CSkyCamera that a managed sky_camera is not in. Six lines in
// ClientData_Update read this instead, and only when that list is empty.
//
// False when nothing was pushed, which is an ordinary map with no sky camera.
namespace ss { struct sky3dparams_t; }
bool SourceSharp_FillSkyboxParams( ss::sky3dparams_t &out );

//-----------------------------------------------------------------------------
// Was this ServerClass registered because C# declared something, or is it a
// surviving SDK IMPLEMENT_SERVERCLASS? plan_gamedll.md Part 5's meter.
//
// EVERY ServerClass IS ON ONE LIST -- the tier classes and the temp-entity
// classes link themselves into g_pServerClassHead from their own constructors
// exactly as the SDK's do -- so the two populations cannot be told apart by
// walking the chain. They are told apart by IDENTITY against the two places
// SourceSharp constructs one, which is a fact neither side can drift on: a
// third site would have to answer here too or it would be counted as stock,
// and being counted as stock is the direction that reads as "work still to do"
// rather than as "work already done".
//
// The count this feeds is printed at DLLInit and is the plan's meter for the
// endgame: when the stock half reaches zero, g_pServerClassHead goes.
//-----------------------------------------------------------------------------
class ServerClass;
bool SourceSharp_IsManagedServerClass( const ServerClass *pClass );

//-----------------------------------------------------------------------------
// plan_gamedll.md Part 8. The seven shared network string tables, created in
// the shell's CreateNetworkStringTables BEFORE the managed slot is called --
// see shell_stringtables.cpp for why this half is native and what "index 0"
// buys.
//-----------------------------------------------------------------------------
void SourceSharp_CreateNetworkStringTables( void );

//-----------------------------------------------------------------------------
// Phase 6. Every engine interface the rest of this binary reaches by global,
// acquired from the factories DLLInit was handed. See
// shell_engineboundary.cpp: the shell acquired exactly two of them until this
// phase, which nothing noticed because nothing had loaded a map.
//
// False on the first miss, which is stock's behaviour and is what DLLInit's
// caller expects: a server missing one of these does not half-work.
//-----------------------------------------------------------------------------
typedef void *(*CreateInterfaceFn)( const char *pName, int *pReturnCode );
bool SourceSharp_AcquireEngineInterfaces( CreateInterfaceFn appSystemFactory,
                                          CreateInterfaceFn physicsFactory,
                                          CreateInterfaceFn fileSystemFactory );

//-----------------------------------------------------------------------------
// PHASE 7'S NAMED LIST IS MANAGED'S (plan_server_native_v2 T4e).
// shell_serversystems.cpp held kServerSystems[] and walked its rows forwards
// and backwards; the order is ServerSystems.cs now, and each native system's
// stage is one IShimLevel step (PhysicsInit, PhysicsLevelInitPreEntity, ...,
// StringPoolLevelShutdownPostEntity) carried out in shell_level.cpp.
//-----------------------------------------------------------------------------

// SourceSharp_HostIsDedicatedServer IS GONE -- plan_native_api.md Phase 4.
// IEngineHost.IsDedicatedServer calls IVEngineServer::IsDedicatedServer
// through its own vtable now.
int32_t SourceSharp_EntitiesInvalidateMdlCacheAll( void );

//-----------------------------------------------------------------------------
// plan_gamedll_after.md Phase A3 -- the two "C" systems' native halves.
//
// The read is a step because IFileSystem is the ENGINE's and the two game
// scripts this phase parses are on neither the mod's path nor any VPK; the
// parse is C#'s because a name table and a weighted pick have no engine tie.
// shell_files.cpp.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The managed side's four server event points, each with exactly ONE body --
// defined in sourcesharp/game/shared/sourcesharp_gamesystem.cpp beside the
// managed API table they read.
//
// One is left on the server: the pre-entity edge, which the WORLD entity's
// own precache reaches (world_adapter.cpp). The level init, level shutdown and
// frame edges are managed's own calls since plan_server_native_v2 T4e
// (ServerSystems.cs), so their bodies here went with kServerSystems[].
//-----------------------------------------------------------------------------
void SourceSharp_ManagedLevelInitPreEntity( const char *pszMapName );

//-----------------------------------------------------------------------------
// CHECKTRANSMIT'S RECIPIENT -- plan_server.md Phase 8b, plan-audit C4.
//
// Which hierarchy answers for this client, and what its 3D-skybox area is.
// 1 with *pOutArea written; 0 with *pOutArea untouched, and 0 is what
// CServerGameEnts::CheckTransmit returns early on.
//
// Declared beside the level and frame wrappers because it is the same kind of
// thing -- one native site asking C# a question the shell used to answer in
// C++ -- and defined in the same file, sourcesharp_gamesystem.cpp, so that
// there is exactly one body of it.
//-----------------------------------------------------------------------------
int SourceSharp_SkyboxAreaForClient( int iClientEdict, int *pOutArea );

//-----------------------------------------------------------------------------
// CLIENTEARPOSITION'S HIERARCHY -- plan_server.md Phase 7, plan-audit C5.
//
// Which hierarchy answers for this client's ears, and where they are. 1 with
// pOutXYZ written; 0 with pOutXYZ untouched, and 0 is what
// CServerGameClients::ClientEarPosition asserts on before writing vec3_origin.
//
// Same arrangement as SkyboxAreaForClient above and for the same reasons: one
// native site asking C# a question the shell used to answer in an #ifdef, with
// exactly one body of it, in sourcesharp_gamesystem.cpp.
//-----------------------------------------------------------------------------
int SourceSharp_EarPositionForClient( int iClientEdict, float *pOutXYZ );

//-----------------------------------------------------------------------------
// ClientSetupVisibility's area portal walk, asked of C# once per client per
// frame (plan_server_native_v2 T4d): the managed AreaPortals tell the engine
// every portal's state (SetAreaPortalStates, 512 at a time) and set the
// client's bits in pPortalBits, which the caller zeroed. -1, or the portal
// number too large for the bits, which the caller raises as an Error. -1 with
// nothing written when the managed side is not loaded: there are no portals
// then, because only managed entities make them.
//-----------------------------------------------------------------------------
int SourceSharp_AreaPortalVisibility( const float *pOriginXYZ, float flFovDistanceAdjustFactor,
                                      unsigned char *pPortalBits, int nPortalBytes );

#endif // SOURCESHARP_SHELL_SERVER_H
