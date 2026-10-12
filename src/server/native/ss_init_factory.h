//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the factories the engine handed server.so ==========//
//
// plan_server_novalve.md P5. The engine passes the game DLL three
// CreateInterfaceFn pointers at DLLInit; server.so stores them once
// (shell_engineboundary.cpp) and hands them back to whatever needs one later
// (the physics hook, the interface acquirer). Nothing outside server.so sees
// the list, so per the plan's naming rule it is server.so's own type, in
// namespace ss, on public/ headers alone. game/server/init_factory.h is no
// longer included by any server TU, and its factorylist_t /
// FactoryList_Store / FactoryList_Retrieve are no longer defined in server.so.
// (The client stores its own, differently shaped list: client_factorylist.h.)
//
// shell_globals.cpp defines the two functions and holds the stored copy.
//=============================================================================//

#ifndef SOURCESHARP_SS_INIT_FACTORY_H
#define SOURCESHARP_SS_INIT_FACTORY_H
#ifdef _WIN32
#pragma once
#endif

#include "interface.h"

namespace ss
{

// The three factories, as the engine passed them to DLLInit.
struct factorylist_t
{
	CreateInterfaceFn engineFactory;
	CreateInterfaceFn physicsFactory;
	CreateInterfaceFn fileSystemFactory;
};

// Stores a copy; a later Store replaces it.
void FactoryList_Store( const factorylist_t &sourceData );

// Copies the stored list out (all NULL before the first Store).
void FactoryList_Retrieve( factorylist_t &destData );

} // namespace ss

#endif // SOURCESHARP_SS_INIT_FACTORY_H
