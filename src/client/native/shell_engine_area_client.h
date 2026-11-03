// IShimEngine on the client: the globals, for managed to read.

#pragma once

#include "ishim_areas.h"
#include "shell_client_engine_interfaces.h"

class CEngineAreaClient final : public IShimEngine
{
public:
	explicit CEngineAreaClient(const CClientEngineInterfaces& engines)
		: m_Engines(engines)
	{
	}

	void* GetGlobalVarsPtr() override { return m_Engines.m_pGlobals; }

private:
	const CClientEngineInterfaces& m_Engines;
};
