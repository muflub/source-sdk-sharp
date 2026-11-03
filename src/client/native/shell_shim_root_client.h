//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's shim root: a by-value member of the client DLL object, handed to managed at
// load. Acquire serves this side's shim interfaces and forwarder tables by name.

#pragma once

#include "ishim_root.h"
#include "shell_shim_console.h"
#include "shell_shim_areas.h"
#include "interface.h"

class CShimRootClient final : public IShimRoot
{
public:
	// NULL for a name this side does not serve. Defined in shell_internalfactory.cpp.
	void* Acquire(const char* pszName) override;

	// The engine's interfaces by factory kind; this side's own (INTERNAL) from Acquire.
	void* AcquireInterface(int32_t nType, const char* pszVersion, char* pError, int32_t nErrorBytes) override;

	// The root lives as long as the DLL; this does nothing.
	void Release() override {}

	// The native-side shim areas. What native holds from managed is the client DLL's m_Managed.
	CShimConsole m_Console;
	CShimAreasClient m_Areas;

	// The engine's factories, from Init to Shutdown. The client has no filesystem factory;
	// its app-system factory serves the filesystem.
	CreateInterfaceFn m_pAppSystemFactory = nullptr;
	CreateInterfaceFn m_pPhysicsFactory = nullptr;
};
