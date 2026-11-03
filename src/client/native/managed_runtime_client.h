// The client's runtime adapter. Engine-free so a test can compile it against swapped runtimes.

#pragma once

#include "sourcesharp_gamesystem.h"

#include "imanaged_runtime_client.h"

// This load's managed-built runtime, held by the client DLL object.
IManagedRuntimeClient* ManagedRuntimeClient();

// The partner of LoadManagedRuntime (sourcesharp_gamesystem.cpp, shared by both DLLs).
void SourceSharp_UnloadManagedSide();

// One forward per method: the slot is read on every call because a reload replaces the
// runtime, so nothing may cache it.
class CManagedRuntimeClient : public IClientRuntimeAdapter
{
public:
	// The holders' slot itself, so the object a reload replaced is read.
	explicit CManagedRuntimeClient(IManagedRuntimeClient* const& runtime)
		: m_Runtime(runtime)
	{
	}

	// The client's release of the refcounted CLR; the managed Shutdown slot runs inside it.
	void OnSystemStop() override { SourceSharp_UnloadManagedSide(); }

	void OnLevelInit(const char* pMapName) override { m_Runtime->LevelInit(pMapName); }

	void OnLevelShutdown() override { m_Runtime->LevelShutdown(); }

	void OnFrame(float flFrameTime) override { m_Runtime->Frame(flFrameTime); }

private:
	IManagedRuntimeClient* const& m_Runtime;
};
