//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client leaf system's storage, a member of the client DLL object. The leaf system
// itself is managed, behind libss_fwd_client's forwarder object; this keeps its storage, the
// environment it reaches client.so through, and the client systems row. The global Valve code
// reads it by points at it from this object's construction: the engine asks the factory for
// it by name before the DLL's Init.

#pragma once

#include "bound_callback.h"
#include "clientleafsystem.h"
#include "fwd_leaf_system.h"
#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"

class CClientEngineInterfaces;
class CShellDetailProps;

class CLeafSystemClient
{
public:
	CLeafSystemClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
					  CShellDetailProps& detailProps);

	CLeafSystemClient(const CLeafSystemClient&) = delete;
	CLeafSystemClient& operator=(const CLeafSystemClient&) = delete;

	// The forwarder object, or NULL when its library has no table.
	IClientLeafSystem* System() const { return m_pSystem; }

	CClientSystem& SystemRow() { return m_SystemRow; }

private:
	// The row, which forwards the three stages stock had bodies for.
	class CSystemRow final : public CClientSystemPerFrame
	{
	public:
		explicit CSystemRow(CLeafSystemClient& leaf)
			: m_Leaf(leaf)
		{
		}

		char const* Name() override { return "CClientLeafSystem"; }

		void LevelInitPreEntity() override;
		void LevelShutdownPostEntity() override;
		void PreRender() override;

	private:
		CLeafSystemClient& m_Leaf;
	};

	IDetailObjectSystem* DetailSystem();

	typedef CBoundCallback<IDetailObjectSystem*()> DetailSystem_t;

	CShellDetailProps& m_DetailProps;

	// The environment's detail system callback takes no context: a thunk bound to this object.
	DetailSystem_t m_DetailSystem{this,
								  &DetailSystem_t::Trampoline<CLeafSystemClient, &CLeafSystemClient::DetailSystem>};

	SSLeafSystemEnv m_Env = {};
	alignas(16) unsigned char m_Storage[kSSLeafSystemBytes];
	IClientLeafSystem* m_pSystem = nullptr;

	CSystemRow m_SystemRow{*this};
};
