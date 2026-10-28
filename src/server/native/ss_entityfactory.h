//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The entity-factory interfaces, server.so's own (plan_server_novalve.md P5,
// lane E): what hooks a map's classname to the code that builds it.
//
// util.h declared them as IEntityFactory / IEntityFactoryDictionary /
// EntityFactoryDictionary(). Nothing outside server.so names them: no engine
// binary carries any of those strings (grep over both SDK installs' .so files
// finds them only in the stock game server.so, while VEngineServer0 is found in
// engine.so and engine_srv.so), so the engine never asks for a dictionary by
// name. They are therefore ss::, not a second body under the Valve name.
//
// The members are util.h's, in util.h's order. That order is the vtable layout
// shell_standalone_class.cpp's dictionary and the map's factories implement,
// and nothing outside server.so calls through it.
//=============================================================================//

#pragma once

#include <stddef.h>

class IServerNetworkable;

namespace ss
{

// Builds and destroys one classname's entities.
class IEntityFactory
{
public:
	virtual IServerNetworkable *Create( const char *pClassName ) = 0;
	virtual void Destroy( IServerNetworkable *pNetworkable ) = 0;
	virtual size_t GetEntitySize() = 0;
};

// Every classname's factory, by name. shell_standalone_class.cpp defines the
// one instance.
class IEntityFactoryDictionary
{
public:
	virtual void InstallFactory( IEntityFactory *pFactory, const char *pClassName ) = 0;
	virtual IServerNetworkable *Create( const char *pClassName ) = 0;
	virtual void Destroy( const char *pClassName, IServerNetworkable *pNetworkable ) = 0;
	virtual IEntityFactory *FindFactory( const char *pClassName ) = 0;
	virtual const char *GetCannonicalName( const char *pClassName ) = 0;
};

IEntityFactoryDictionary *EntityFactoryDictionary();

} // namespace ss
