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
void SourceSharp_OnClientGamePrecache();

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
// PHASE 7 -- THE NAMED LIST. shell_serversystems.cpp holds kServerSystems[],
// one row per game system in the order it runs; every pass below walks those
// rows, and the shutdown passes walk the SAME rows backwards.
//
// This replaced shell_gamesystems.cpp, the Phase 6 crutch, which called
// IGameSystem::InitAllSystems() and so took its order from the linker.
//
// WHO CALLS WHAT, because the answer is not "the shell" for all of them:
//
//   Init / PostInit / Shutdown            CShellGameDll::DLLInit, DLLShutdown
//   LevelInitPreEntity                    world_adapter.cpp, from inside the
//                                         WORLD ENTITY's precache -- stock's
//                                         own placement, and the one pass the
//                                         shell does not drive
//   LevelInitPostEntity                   CShellGameDll::ServerActivate
//   LevelShutdown{PreClearSteam,Pre,Post} CShellGameDll::LevelShutdown
//   Frame{Pre,Post}Think, PreClientUpdate CShellGameDll::GameFrame /
//                                         PreClientUpdate
//-----------------------------------------------------------------------------
bool SourceSharp_ServerSystemsInit( void );
void SourceSharp_ServerSystemsPostInit( void );
void SourceSharp_ServerSystemsShutdown( void );

// The pre-entity pass. It does not carry the ServerSystems name because its ONE
// caller is world_adapter.cpp -- stock calls this pass from inside the WORLD
// ENTITY's precache and not from LevelInit, and the name is what makes that
// call site legible beside stock's world.cpp:622.
void SourceSharp_LevelInitPreEntityAllSystems( const char *pszMapName );

void SourceSharp_ServerSystemsLevelInitPostEntity( void );
void SourceSharp_ServerSystemsLevelShutdownPreClearSteamAPIContext( void );
void SourceSharp_ServerSystemsLevelShutdownPreEntity( void );
void SourceSharp_ServerSystemsLevelShutdownPostEntity( void );
void SourceSharp_ServerSystemsFramePreThink( void );
void SourceSharp_ServerSystemsFramePostThink( void );
void SourceSharp_ServerSystemsPreClientUpdate( void );

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
// Two things drive them: CSourceSharpSystem in delegate mode and the shell's
// kServerSystems[] row in shell mode. Two drivers are the design; two BODIES
// would be the bug, which is why these are declared rather than duplicated.
//-----------------------------------------------------------------------------
void SourceSharp_ManagedLevelInitPreEntity( const char *pszMapName );
void SourceSharp_ManagedLevelInit( const char *pszMapName );
void SourceSharp_ManagedLevelShutdownPreEntity( void );
void SourceSharp_ManagedLevelShutdown( void );
void SourceSharp_ManagedFramePreThink( void );
void SourceSharp_ManagedFrameThink( void );

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

#endif // SOURCESHARP_SHELL_SERVER_H
