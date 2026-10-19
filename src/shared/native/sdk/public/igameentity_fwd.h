//========= SourceSharp ======================================================//
//
// The entity face, declared but not defined: igameentity.h's name for it on
// this side. On the server built on stock public headers that name is
// CBaseEntity, and IGameEntity an alias of it (plan_server_native_v2 D1);
// anywhere else it is the class IGameEntity. A bare `class IGameEntity;`
// cannot sit beside the alias, so a header that only names the type includes
// this instead.
//
//=============================================================================//
#ifndef IGAMEENTITY_FWD_H
#define IGAMEENTITY_FWD_H

#if defined( GAME_DLL ) && defined( SS_STOCK_PUBLIC_HEADERS )
class CBaseEntity;
using IGameEntity = CBaseEntity;
#else
class IGameEntity;
#endif

#endif // IGAMEENTITY_FWD_H
