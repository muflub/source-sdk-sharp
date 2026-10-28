//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The three factories the engine hands server.so at DLLInit, kept on the DLL tier's
// engine interfaces (server_engine_interfaces.h).

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

} // namespace ss
