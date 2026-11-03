//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The temp entity tables managed registers, as one object, a member of the client DLL object:
// each table's RecvProps and RecvTable and, when asked, its ClientClass and the one shell
// that class creates. Built parts are never freed: the engine reads them for the life of the
// process. Its IShimTempEntBackendsClient slot is the build.

#pragma once

#include <stdint.h>

#include <set>
#include <string>

#include "client_class.h"
#include "dt_recv.h"

#include "bound_callback.h"
#include "shell_managed_areas_client.h"

class CSourceSharpTempEnt;
class IClientNetworkable;

class CTempEntTablesClient
{
public:
	explicit CTempEntTablesClient(const CManagedAreasClient& areas)
		: m_Areas(areas)
	{
	}

	CTempEntTablesClient(const CTempEntTablesClient&) = delete;
	CTempEntTablesClient& operator=(const CTempEntTablesClient&) = delete;

	// cs_cl_te_dump: every registered temp entity, its props as the engine holds them.
	void Dump(const char* pWanted) const;

	// IShimTempEntBackendsClient.
	int32_t TempEntBuildClient(const void* pBlock, int32_t nBlockBytes, int32_t nBlobBytes,
							   SourceSharpTeRecvBuilt_t* pOut);

	// Once managed has pushed its tables.
	void TempEntClientCloseRegistration();

private:
	// The managed registry refuses past the same bound (TePropPlanner.MaxClasses).
	enum
	{
		kMaxTables = 64
	};

	struct Built_t
	{
		typedef CBoundCallback<IClientNetworkable*()> CreateEvent_t;

		RecvProp* pProps = nullptr;
		int nProps = 0;
		RecvTable* pTable = nullptr;
		ClientClass* pClass = nullptr; // NULL for a table registered without one
		CSourceSharpTempEnt* pShell = nullptr; // NULL likewise: no class, nothing to create
		const char* pClassName = nullptr; // interned: what a managed reload asks for again
		uint32_t nShapeHash = 0; // managed's fingerprint, handed back on a re-registration

		// CreateEventFn carries no context, so the class's is a thunk bound to its row.
		IClientNetworkable* CreateEvent();
		CreateEvent_t Create{this, &CreateEvent_t::Trampoline<Built_t, &Built_t::CreateEvent>};
	};

	const char* InternName(const char* pName) { return m_Names.emplace(pName).first->c_str(); }

	const CManagedAreasClient& m_Areas;

	Built_t* m_pBuilt[kMaxTables] = {};
	int m_nBuilt = 0;
	bool m_bClosed = false;

	// Names the engine keeps by pointer. A std::set node never moves, so c_str() stays valid.
	std::set<std::string> m_Names;
};

// cs_cl_te_dump has no context. Defined in shell_clientdll.cpp.
CTempEntTablesClient& ShellClientDll_TempEntTables();
