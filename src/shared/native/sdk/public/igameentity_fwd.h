//========= SourceSharp ======================================================//
//
// The entity face, declared but not defined: igameentity.h's name for it on
// this side. On a side built on stock headers (vendor/) that name is the one
// those headers use, CBaseEntity on the server and C_BaseEntity on the
// client, and IGameEntity an alias of it (plan_server_native_v2 D1, the
// client the same way); anywhere else it is the class IGameEntity. A bare `class IGameEntity;`
// cannot sit beside the alias, so a header that only names the type includes
// this instead.
//
//=============================================================================//
#pragma once

// SS_GAME_ENTITY: the class's name on a side built on stock headers, where
// those headers name the game's entity and only forward-declare it --
// CBaseEntity on the server, C_BaseEntity on the client. Unset elsewhere.
#if defined( SS_STOCK_PUBLIC_HEADERS ) && defined( GAME_DLL )
#define SS_GAME_ENTITY CBaseEntity
#elif defined( SS_STOCK_PUBLIC_HEADERS ) && defined( CLIENT_DLL )
#define SS_GAME_ENTITY C_BaseEntity
#endif

#ifdef SS_GAME_ENTITY
class SS_GAME_ENTITY;
using IGameEntity = SS_GAME_ENTITY;
#else
class IGameEntity;
#endif
