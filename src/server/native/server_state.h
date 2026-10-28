//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the game DLL's two heap tiers =====================//
//
// plan_globals.md §2 ("one root, two tiers") and plan_server_native_v2 T6. The
// server keeps ONE static object, the game DLL (`s_gameShellDll`,
// shell_gamedll.cpp). Native state that is not one of D6's three FLOOR shapes
// hangs off it in one of two heap objects:
//
//   m_pDll    CServerDllState     new in DLLInit,   delete in DLLShutdown
//   m_pLevel  CServerLevelState   new in LevelInit, delete in LevelShutdown
//
// WHY HEAP AND NOT BY-VALUE MEMBERS OF THE ROOT. A by-value member of a static
// is constructed at static init, before the engine, the allocator hooks and the
// managed runtime exist, and it is never reset. `new` in DLLInit puts the
// construction where the root already sequences everything else, and
// `delete m_pLevel` in LevelShutdown makes the per-map reset STRUCTURAL: a
// level-tier field cannot leak into the next map, because the object it lived
// in no longer exists. A reload bug of the "somebody forgot an = 0" kind
// cannot be written against a level-tier member.
//
// WHAT IS NOT HERE. The registration list heads static initialisers build, the
// ConVar/ConCommand objects the engine holds, and the C callbacks with no
// user-data argument stay as the FLOOR rows they are (user ruling D6,
// 2026-10-19). Everything else in docs/globals-ledger.tsv ruled MEMBER names
// its owner and tier, and moves in here owner by owner.
//
// A member's own header brings what it needs; the tier mechanics and each
// member's reset are gtests (server_state_test.cpp).
//
//=============================================================================//

#pragma once

#include "entity_slots.h"   // CSourceSharpEntitySlots, the block C# writes through
#include "slot_table.h"     // CSourceSharpSlotTable, handle -> shell
#include "generic_nettable.h"   // CSourceSharpTypeTable, the published net types

#include <stdint.h>
#include <set>
#include <string>

class SendProp;
class SendTable;
class ServerClass;

//-----------------------------------------------------------------------------
// TEMPENTS (shell_tempents.cpp): the temp entity tables managed's plan built.
//
// One built table. The props, the table and the class are leaked on purpose: a
// SendTable is read by the engine for the life of the process, and there is no
// point at which they could be freed and nothing would notice.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_TE_MAX_TABLES = 64 };

struct SourceSharpTeBuilt_t
{
	SendProp    *pProps;
	int          nProps;
	SendTable   *pTable;
	ServerClass *pClass;      // NULL for a table registered without one
	const char  *pClassName;  // interned: what a managed reload asks for again
	uint32_t     nShapeHash;
};

class CServerTempEnts
{
public:
	CServerTempEnts() = default;
	CServerTempEnts( const CServerTempEnts & ) = delete;
	CServerTempEnts &operator=( const CServerTempEnts & ) = delete;

	SourceSharpTeBuilt_t m_Built[ SOURCESHARP_TE_MAX_TABLES ] = {};
	int m_nBuilt = 0;
	bool m_bClosed = false;

	// The names the engine keeps by pointer for the table's lifetime: prop
	// names, table names and class names. A std::set node never moves, so
	// c_str() is stable for the life of the set; the same text twice is the
	// same pointer.
	std::set< std::string > m_Names;

	// TRANSIT: the encode buffer one PlaybackTempEntity is written into.
	unsigned char m_Scratch[ 512 ] = {};
};

//-----------------------------------------------------------------------------
// One heap tier: an owning pointer whose object exists between Begin and End.
//
// Begin ALWAYS builds a new object. A tier whose End was missed (a LevelInit
// with no LevelShutdown before it) has its stale object destroyed first, so a
// tier is never reused across a Begin -- that is the reset, and it is the whole
// point of the type.
//
// End clears the pointer BEFORE it deletes, so code the destructor reaches
// sees "no tier" rather than an object half torn down.
//
// The root is a static and outlives DLLShutdown; this type's own destructor
// therefore does NOT delete. Destroying a tier is DLLShutdown's job, in the
// order the root states, never the C runtime's at process exit.
//-----------------------------------------------------------------------------
template < typename T >
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

//-----------------------------------------------------------------------------
// The DLL tier: DLLInit to DLLShutdown. Its owners are the ledger's MEMBER rows
// with tier DLL (EntitySlots, NetTables, TempEnts, Players, ...).
//-----------------------------------------------------------------------------
class CServerDllState
{
public:
	CServerDllState() = default;
	CServerDllState( const CServerDllState & ) = delete;
	CServerDllState &operator=( const CServerDllState & ) = delete;

	//-------------------------------------------------------------------------
	// ENTITYSLOTS (shell_entityslots.cpp). Were g_EntitySlots (164 KB) and
	// SourceSharp_SlotTable()'s s_SlotTable (82 KB), file-scope statics.
	//
	// DLL tier, not level: managed holds pointers into m_EntitySlots for the
	// life of the side (they must outlive a cs_sv_reload, which keeps the DLL),
	// and the table's serials stay monotone across maps so map N+1 never
	// reissues a handle map N handed out. DLLShutdown unloads the managed side
	// before it ends this tier.
	//-------------------------------------------------------------------------
	CSourceSharpEntitySlots m_EntitySlots;
	CSourceSharpSlotTable m_SlotTable;

	//-------------------------------------------------------------------------
	// NETTYPES (generic_payload.cpp). Was SourceSharp_NetTypes()'s s_Types
	// (43 KB), a function-local static. The managed types published into it
	// at registration name the tier tables every shell's ServerClass is built
	// from, so it lives as long as those: the DLL.
	//-------------------------------------------------------------------------
	CSourceSharpTypeTable m_NetTypes;

	//-------------------------------------------------------------------------
	// TEMPENTS (shell_tempents.cpp). Were g_TeBuilt, g_nTeBuilt, g_bTeClosed,
	// g_TeNames and g_TeScratch, file-scope statics. Built once, at managed
	// registration; a managed reload asks for the same tables again by class
	// name and gets the existing ones back, so they live as long as the DLL.
	//-------------------------------------------------------------------------
	CServerTempEnts m_TempEnts;
};

//-----------------------------------------------------------------------------
// The level tier: LevelInit to LevelShutdown. Its owners are the ledger's
// MEMBER rows with tier LEVEL (the physics environment, string tables, the
// world's per-map latches, ...).
//-----------------------------------------------------------------------------
class CServerLevelState
{
public:
	CServerLevelState() = default;
	CServerLevelState( const CServerLevelState & ) = delete;
	CServerLevelState &operator=( const CServerLevelState & ) = delete;
};

// The game DLL's tiers, NULL outside their windows. Defined beside the root in
// shell_gamedll.cpp.
CServerDllState *ShellGameDll_Dll();
CServerLevelState *ShellGameDll_Level();
