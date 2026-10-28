//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The entity-factory interfaces: what hooks a map's classname to the code that
// builds it. Members are in util.h's order, which is their vtable layout.

#pragma once

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
