// Forward declaration of the entity face. On stock-header sides IGameEntity is an
// alias of CBaseEntity or C_BaseEntity, so a bare `class IGameEntity;` would clash.
#pragma once

// The class's name on a side built on stock headers. Unset elsewhere.
#if defined(SS_STOCK_PUBLIC_HEADERS) && defined(GAME_DLL)
#define SS_GAME_ENTITY CBaseEntity
#elif defined(SS_STOCK_PUBLIC_HEADERS) && defined(CLIENT_DLL)
#define SS_GAME_ENTITY C_BaseEntity
#endif

#ifdef SS_GAME_ENTITY
class SS_GAME_ENTITY;
using IGameEntity = SS_GAME_ENTITY;
#else
class IGameEntity;
#endif
