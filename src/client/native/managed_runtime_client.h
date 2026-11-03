// The client's runtime adapter. Engine-free so a test can compile it against swapped runtimes.

#pragma once

#include "sourcesharp_gamesystem.h"

#include "imanaged_runtime_client.h"

// One forward per method: the slot is read on every call because a reload replaces the
// runtime, so nothing may cache it.
class CManagedRuntimeClient : public IClientRuntimeAdapter
{
public:
	// The holders' slot itself, so the object a reload replaced is read.
	CManagedRuntimeClient(IManagedRuntimeClient* const& runtime, IManagedRuntimeEnvironment& env)
		: m_Runtime(runtime)
		, m_Env(env)
	{
	}

	// The client's release of the refcounted CLR; the managed Shutdown slot runs inside it.
	void OnSystemStop() override { m_Env.UnloadManagedSide(); }

	void OnLevelInit(const char* pMapName) override { m_Runtime->LevelInit(pMapName); }

	void OnLevelShutdown() override { m_Runtime->LevelShutdown(); }

	void OnFrame(float flFrameTime) override { m_Runtime->Frame(flFrameTime); }

private:
	IManagedRuntimeClient* const& m_Runtime;
	IManagedRuntimeEnvironment& m_Env;
};
