//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's game system row for the managed frame: holds the runtime adapter and
// forwards level and frame events to it. A member of the client DLL object; the server
// has none.

#pragma once

#include "shell_clientsystem.h"

class CManagedSide;
class IClientRuntimeAdapter;

class CSourceSharpSystem final : public CClientSystemPerFrame
{
public:
	explicit CSourceSharpSystem(CManagedSide& managedSide)
		: CClientSystemPerFrame("SourceSharp")
		, m_ManagedSide(managedSide)
	{
	}

	// Set from CShellClientDLL::Init after LoadManagedRuntime; this row does not load.
	void SetRuntime(IClientRuntimeAdapter* pRuntime) { m_pRuntime = pRuntime; }

	bool Init() override;
	void Shutdown() override;
	void LevelInitPostEntity() override;
	void LevelShutdownPostEntity() override;
	void Update(float frametime) override;

private:
	CManagedSide& m_ManagedSide;
	IClientRuntimeAdapter* m_pRuntime = nullptr;
};
