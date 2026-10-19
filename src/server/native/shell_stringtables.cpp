//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================
// SS_REASON b  an interface resolved by name at load
//
// Part 8 of plan_gamedll.md, natively in the shell.
//
// THE SEVEN TABLES ARE NOT OPTIONAL. A server that creates none has no
// materials, no effect-dispatch names, no VGUI screens, no info panel, no
// scenes and no map cycle -- and nothing says so: every AddString goes to a
// null table, which is a crash, and every lookup that would have failed
// silently answers zero instead.
//
// WHY THESE SEVEN ARE NATIVE, stated here because a reader will ask, and the
// answer CHANGED at plan_gamedll_after.md Phase A8 in one half and not the
// other.
//
// Phase 6 wrote: "a managed route needs a CreateStringTable / AddString /
// GetString ABI on both sides -- roughly 150 lines -- and buys nothing yet,
// because no C# code has ever asked to create a table." Part 13 made that ABI
// conditional on somebody asking. **Phase A8 met the condition by decision and
// built it** -- see shell_stringtables.h and the six StringTable* slots at the
// tail of SourceSharpEngineApi.
//
// THE SEVEN BELOW DID NOT MOVE, AND MUST NOT. 141 native references to the
// g_pStringTable* globals across this tree read them -- shell_te_fwd_s4.cpp on
// the temp-entity path (and, until 2026-09-29, the message of the day on
// g_pStringTableInfoPanel) -- and
// every one is a dereference that would have to keep being answered whatever
// created the table. The new ABI is ADDITIVE: it is where MOD-DECLARED tables
// go, which is what the IManagedRuntimeServer::CreateNetworkStringTables slot
// was reserved for, and the shell still calls that slot AFTER these seven
// exist. plan_gamedll.md Part 8 and Part 13.
//
// NO FILE-SCOPE STATE. Everything assigned below is a pre-existing STOCK global
// (gameinterface.cpp:237-250, all outside CServerGameDLL's class body, so all
// of them survive Phase 7a's deletion of that class). This file adds none of
// its own.
//
//=============================================================================

#include "networkstringtabledefs.h"
#include "tier0/dbg.h"


// The engine's string table container (connected in shell_engineboundary.cpp).
extern INetworkStringTableContainer *networkstringtable;

// Stock's, all seven of them, defined in shell_engineboundary.cpp (they were
// gameinterface.cpp:237-243, outside the class). Declared here, the one file
// that creates them; nothing in game/ is included for them.
extern INetworkStringTable *g_pStringTableParticleEffectNames;
extern INetworkStringTable *g_pStringTableEffectDispatch;
extern INetworkStringTable *g_pStringTableVguiScreen;
extern INetworkStringTable *g_pStringTableMaterials;
extern INetworkStringTable *g_pStringTableInfoPanel;
extern INetworkStringTable *g_pStringTableClientSideChoreoScenes;
extern INetworkStringTable *g_pStringTableServerMapCycle;

// The eighth table's creator, the game rules' own (shell_gamerules.cpp).
void CreateNetworkStringTables_GameRules();

// Each table's capacity, as stock sized them (networkstringtable_gamedll.h's
// MAX_*_STRINGS, effect_dispatch_data.h's MAX_EFFECT_DISPATCH_STRINGS). The
// engine sends the capacity with the table, so the client needs no copy.
enum
{
	kMaxVguiScreenStrings      = 1 << 8,
	kMaxMaterialStrings        = 1 << 10,
	kMaxChoreoScenesStrings    = 1 << 13,
	kMaxParticleSystemStrings  = 1 << 13,
	kMaxEffectDispatchStrings  = 1 << 10,
	kMaxInfoPanelStrings       = 128,
};

//-----------------------------------------------------------------------------
// THE SIX STRING-TABLE ACCESSORS, moved here by plan_gamedll_after.md Phase A4.
//
// They were free functions in gameinterface.cpp (:2509-2588) and every one of
// them is a call on g_pStringTableMaterials or g_pStringTableParticleEffectNames
// -- the two tables THIS file creates, twelve lines below. Nothing in them is
// about a game interface; they were in that file because that is where the
// globals are declared, and the globals are read here.
//
// plan_gamedll_after.md A4 sent them to precache_model.cpp. That is the wrong
// file: precache_model.cpp is engine->PrecacheModel and mdlcache, and touches
// no string table at all. They came here instead.
//
// THE BODIES ARE CARRIED ACROSS UNCHANGED, byte for byte. In particular BOTH
// miss paths of GetMaterialIndex still answer 0 and GetParticleSystemIndex
// still answers 0, because index 0 is what SourceSharp_CreateNetworkStringTables
// below is arranging to be the error material and the error particle system.
// stringtable_index0_test.cpp reads those bodies out of THIS file now.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Precaches a vgui screen overlay material
//-----------------------------------------------------------------------------
void PrecacheMaterial( const char *pMaterialName )
{
	Assert( pMaterialName && pMaterialName[0] );
	g_pStringTableMaterials->AddString( true, pMaterialName );
}


//-----------------------------------------------------------------------------
// Precaches a vgui screen overlay material
//-----------------------------------------------------------------------------
void PrecacheParticleSystem( const char *pParticleSystemName )
{
	Assert( pParticleSystemName && pParticleSystemName[0] );
	g_pStringTableParticleEffectNames->AddString( true, pParticleSystemName );
}


//-----------------------------------------------------------------------------
// Converts a previously precached material into an index
//-----------------------------------------------------------------------------
int GetParticleSystemIndex( const char *pParticleSystemName )
{
	if ( pParticleSystemName )
	{
		int nIndex = g_pStringTableParticleEffectNames->FindStringIndex( pParticleSystemName );
		if (nIndex != INVALID_STRING_INDEX )
			return nIndex;

		DevWarning("Server: Missing precache for particle system \"%s\"!\n", pParticleSystemName );
	}

	// This is the invalid string index
	return 0;
}

//-----------------------------------------------------------------------------
// CServerGameDLL::CreateNetworkStringTables (gameinterface.cpp:1577-1621),
// transliterated. The econ/TF arms are dropped: TF_DLL is not this build.
//
// The two Asserts stock writes after the precaches are kept as Asserts AND are
// pinned by a gtest, because an index-0 that is something else is the failure
// this whole function exists to prevent and it is completely silent.
//-----------------------------------------------------------------------------
void SourceSharp_CreateNetworkStringTables( void )
{
	g_pStringTableParticleEffectNames     = networkstringtable->CreateStringTable( "ParticleEffectNames", kMaxParticleSystemStrings );
	g_pStringTableEffectDispatch          = networkstringtable->CreateStringTable( "EffectDispatch", kMaxEffectDispatchStrings );
	g_pStringTableVguiScreen              = networkstringtable->CreateStringTable( "VguiScreen", kMaxVguiScreenStrings );
	g_pStringTableMaterials               = networkstringtable->CreateStringTable( "Materials", kMaxMaterialStrings );
	g_pStringTableInfoPanel               = networkstringtable->CreateStringTable( "InfoPanel", kMaxInfoPanelStrings );
	g_pStringTableClientSideChoreoScenes  = networkstringtable->CreateStringTable( "Scenes", kMaxChoreoScenesStrings );
	g_pStringTableServerMapCycle          = networkstringtable->CreateStringTable( "ServerMapCycle", 128 );

	Assert( g_pStringTableParticleEffectNames &&
			g_pStringTableEffectDispatch &&
			g_pStringTableVguiScreen &&
			g_pStringTableMaterials &&
			g_pStringTableInfoPanel &&
			g_pStringTableClientSideChoreoScenes &&
			g_pStringTableServerMapCycle );

	// FIRST INTO A TABLE THAT WAS JUST CREATED, WHICH IS THE WHOLE POINT.
	//
	// "debug/debugempty" must land at index 0 of Materials and "error" at
	// index 0 of ParticleEffectNames, because that is what every lookup that
	// FAILED answers -- GetMaterialIndex and GetParticleSystemIndex both fall
	// back to 0 when the name is not in the table (gameinterface.cpp:2480-2490,
	// :2523-2533). Put anything else there first and every failed lookup
	// silently resolves to a real material or a real particle system instead
	// of to the error one, everywhere, forever, with no diagnostic anywhere.
	//
	// So the ORDER of these two lines relative to the seven above and to each
	// other is load-bearing, and stringtable_index0_test.cpp is what says so
	// when somebody moves one.
	PrecacheMaterial( "debug/debugempty" );

	PrecacheParticleSystem( "error" );
	Assert( GetParticleSystemIndex( "error" ) == 0 );

	// The eighth table, owned by the game rules rather than by this list. On
	// this build it is SourceSharp's own one-entry table (shell_gamerules.cpp),
	// and the CLIENT's half of that pair is what tells the client to build a
	// rules object at all.
	CreateNetworkStringTables_GameRules();

}

//=============================================================================
// PHASE A8's mod-declared route (the six StringTable* slots and
// shell_stringtables.h's CShellStringTables) is managed since
// plan_server_native_v2 T3: ForwardedStringTables, over the container's and
// the tables' forwarders, with the same rules.
//=============================================================================
