//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// Creates the seven stock network string tables, and the first entry of two of them.
// Mod-declared tables are managed and created after these.

#include "networkstringtabledefs.h"
#include "tier0/dbg.h"

// The game rules' own table (shell_gamerules.cpp).
#include "shell_gamerules.h"

// Stock's capacities. The engine sends the capacity with the table.
enum
{
	kMaxVguiScreenStrings = 1 << 8,
	kMaxMaterialStrings = 1 << 10,
	kMaxChoreoScenesStrings = 1 << 13,
	kMaxParticleSystemStrings = 1 << 13,
	kMaxEffectDispatchStrings = 1 << 10,
	kMaxInfoPanelStrings = 128,
};

// One of the engine's tables. Only two are kept: the rest are created for the
// client and never read here.
static INetworkStringTable* CreateTable(INetworkStringTableContainer* pTables, const char* pName, int nMaxEntries)
{
	INetworkStringTable* pTable = pTables->CreateStringTable(pName, nMaxEntries);
	AssertMsg(pTable, "the engine refused a stock string table");
	return pTable;
}

// Stock's CServerGameDLL::CreateNetworkStringTables, without the econ/TF arms. Answers the
// rules' table, which the world writes its rules classname into.
INetworkStringTable* SourceSharp_CreateNetworkStringTables(INetworkStringTableContainer* pTables)
{
	INetworkStringTable* pParticleEffectNames = CreateTable(pTables, "ParticleEffectNames", kMaxParticleSystemStrings);
	CreateTable(pTables, "EffectDispatch", kMaxEffectDispatchStrings);
	CreateTable(pTables, "VguiScreen", kMaxVguiScreenStrings);
	INetworkStringTable* pMaterials = CreateTable(pTables, "Materials", kMaxMaterialStrings);
	CreateTable(pTables, "InfoPanel", kMaxInfoPanelStrings);
	CreateTable(pTables, "Scenes", kMaxChoreoScenesStrings);
	CreateTable(pTables, "ServerMapCycle", 128);

	// These must be the first strings in their tables: every failed lookup
	// answers index 0, which must be the error material and particle system.
	pMaterials->AddString(true, "debug/debugempty");

	pParticleEffectNames->AddString(true, "error");
	Assert(pParticleEffectNames->FindStringIndex("error") == 0);

	// The rules' table, whose client half tells the client to build a rules object.
	return CreateNetworkStringTables_GameRules(pTables);
}
