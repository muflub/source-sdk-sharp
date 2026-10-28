//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The game DLL's two heap tiers, hanging off the one static root in shell_gamedll.cpp:
//
//   m_pDll    CServerDllState     new in DLLInit,   delete in DLLShutdown
//   m_pLevel  CServerLevelState   new in LevelInit, delete in LevelShutdown
//
// Heap rather than by-value members so construction happens after the engine is up,
// and so a level-tier field cannot leak into the next map.

#pragma once

#include "entity_slots.h"
#include "slot_table.h"
#include "generic_nettable.h"

#include <set>
#include <string>

class SendProp;
class SendTable;
class ServerClass;

// The temp entity tables built from the managed plan. Leaked on purpose: the engine
// reads a SendTable for the life of the process.
enum
{
	SOURCESHARP_TE_MAX_TABLES = 64
};

struct SourceSharpTeBuilt_t
{
	SendProp *pProps;
	int nProps;
	SendTable *pTable;
	ServerClass *pClass; // NULL for a table registered without one
	const char *pClassName; // interned: what a managed reload asks for again
	uint32_t nShapeHash;
};

class CServerTempEnts
{
public:
	CServerTempEnts() = default;
	CServerTempEnts( const CServerTempEnts & ) = delete;
	CServerTempEnts &operator=( const CServerTempEnts & ) = delete;

	SourceSharpTeBuilt_t m_Built[SOURCESHARP_TE_MAX_TABLES] = {};
	int m_nBuilt = 0;
	bool m_bClosed = false;

	// Names the engine keeps by pointer. A std::set node never moves, so c_str() is stable.
	std::set<std::string> m_Names;
};

// One heap tier: an owning pointer whose object exists between Begin and End. Begin
// destroys a stale object first, so a tier is never reused. End clears the pointer
// before deleting. The destructor does not delete: DLLShutdown ends the tiers.
template <typename T>
class CStateTier
{
public:
	CStateTier() = default;
	CStateTier( const CStateTier & ) = delete;
	CStateTier &operator=( const CStateTier & ) = delete;

	T *Get() const { return m_p; }

	T &Begin()
	{
		End();
		m_p = new T();
		return *m_p;
	}

	void End()
	{
		T *p = m_p;
		m_p = nullptr;
		delete p;
	}

private:
	T *m_p = nullptr;
};

// The DLL tier: DLLInit to DLLShutdown.
class CServerDllState
{
public:
	CServerDllState() = default;
	CServerDllState( const CServerDllState & ) = delete;
	CServerDllState &operator=( const CServerDllState & ) = delete;

	// DLL tier: managed holds pointers into m_EntitySlots across a reload, and serials stay
	// monotone across maps so no handle is reissued. Managed unloads before this tier ends.
	CSourceSharpEntitySlots m_EntitySlots;
	CSourceSharpSlotTable m_SlotTable;

	// The published managed types the tier ServerClasses are built from.
	CSourceSharpTypeTable m_NetTypes;

	// Built once at managed registration; a reload gets the existing tables back.
	CServerTempEnts m_TempEnts;
};

// The level tier: LevelInit to LevelShutdown.
class CServerLevelState
{
public:
	CServerLevelState() = default;
	CServerLevelState( const CServerLevelState & ) = delete;
	CServerLevelState &operator=( const CServerLevelState & ) = delete;
};

// The tiers, NULL outside their windows. Defined in shell_gamedll.cpp.
CServerDllState *ShellGameDll_Dll();
CServerLevelState *ShellGameDll_Level();
