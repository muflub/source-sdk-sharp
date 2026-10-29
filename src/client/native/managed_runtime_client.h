// The client's runtime adapter. Engine-free so a test can compile it against swapped runtimes.

#pragma once

#include "sourcesharp_gamesystem.h"

#include "imanaged_runtime_client.h"

// This load's managed-built runtime, held by the client DLL object.
IManagedRuntimeClient* ManagedRuntimeClient();

// The partner of LoadManagedRuntime (sourcesharp_gamesystem.cpp, shared by both DLLs).
void SourceSharp_UnloadManagedSide();

// One forward per method and no members: ManagedRuntimeClient() is asked on every call
// because a reload replaces the runtime, so nothing may cache it.
class CManagedRuntimeClient : public IClientRuntimeAdapter
{
public:
	// The client's release of the refcounted CLR; the managed Shutdown slot runs inside it.
	void OnSystemStop() override { SourceSharp_UnloadManagedSide(); }

	void OnLevelInit(const char* pMapName) override { ManagedRuntimeClient()->LevelInit(pMapName); }

	void OnLevelShutdown() override { ManagedRuntimeClient()->LevelShutdown(); }

	void OnFrame(float flFrameTime) override { ManagedRuntimeClient()->Frame(flFrameTime); }
};
