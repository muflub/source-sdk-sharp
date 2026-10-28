//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The three factories the engine hands server.so at DLLInit, stored once and handed back
// later. Defined in shell_globals.cpp.

#pragma once

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
