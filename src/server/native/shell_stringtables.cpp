//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// Creates the seven stock network string tables. Native, because native code
// dereferences the g_pStringTable* globals; mod-declared tables are managed and
// created after these.

#include "networkstringtabledefs.h"
#include "tier0/dbg.h"

extern INetworkStringTableContainer *networkstringtable;

// Stock's globals, defined in shell_engineboundary.cpp.
extern INetworkStringTable *g_pStringTableParticleEffectNames;
extern INetworkStringTable *g_pStringTableEffectDispatch;
extern INetworkStringTable *g_pStringTableVguiScreen;
extern INetworkStringTable *g_pStringTableMaterials;
extern INetworkStringTable *g_pStringTableInfoPanel;
extern INetworkStringTable *g_pStringTableClientSideChoreoScenes;
extern INetworkStringTable *g_pStringTableServerMapCycle;

// The game rules' own table (shell_gamerules.cpp).
void CreateNetworkStringTables_GameRules();

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

void PrecacheMaterial( const char *pMaterialName )
{
	Assert( pMaterialName && pMaterialName[0] );
	g_pStringTableMaterials->AddString( true, pMaterialName );
}

void PrecacheParticleSystem( const char *pParticleSystemName )
{
	Assert( pParticleSystemName && pParticleSystemName[0] );
	g_pStringTableParticleEffectNames->AddString( true, pParticleSystemName );
}

// A failed lookup answers 0, which SourceSharp_CreateNetworkStringTables makes
// the error particle system.
int GetParticleSystemIndex( const char *pParticleSystemName )
{
	if ( pParticleSystemName )
	{
		int nIndex = g_pStringTableParticleEffectNames->FindStringIndex( pParticleSystemName );
		if ( nIndex != INVALID_STRING_INDEX )
			return nIndex;

		DevWarning( "Server: Missing precache for particle system \"%s\"!\n", pParticleSystemName );
	}

	// This is the invalid string index
	return 0;
}

// Stock's CServerGameDLL::CreateNetworkStringTables, without the econ/TF arms.
void SourceSharp_CreateNetworkStringTables()
{
	g_pStringTableParticleEffectNames =
		networkstringtable->CreateStringTable( "ParticleEffectNames", kMaxParticleSystemStrings );
	g_pStringTableEffectDispatch = networkstringtable->CreateStringTable( "EffectDispatch", kMaxEffectDispatchStrings );
	g_pStringTableVguiScreen = networkstringtable->CreateStringTable( "VguiScreen", kMaxVguiScreenStrings );
	g_pStringTableMaterials = networkstringtable->CreateStringTable( "Materials", kMaxMaterialStrings );
	g_pStringTableInfoPanel = networkstringtable->CreateStringTable( "InfoPanel", kMaxInfoPanelStrings );
	g_pStringTableClientSideChoreoScenes = networkstringtable->CreateStringTable( "Scenes", kMaxChoreoScenesStrings );
	g_pStringTableServerMapCycle = networkstringtable->CreateStringTable( "ServerMapCycle", 128 );

	Assert( g_pStringTableParticleEffectNames && g_pStringTableEffectDispatch && g_pStringTableVguiScreen &&
			g_pStringTableMaterials && g_pStringTableInfoPanel && g_pStringTableClientSideChoreoScenes &&
			g_pStringTableServerMapCycle );

	// These must be the first strings in their tables: every failed lookup
	// answers index 0, which must be the error material and particle system.
	PrecacheMaterial( "debug/debugempty" );

	PrecacheParticleSystem( "error" );
	Assert( GetParticleSystemIndex( "error" ) == 0 );

	// The rules' table, whose client half tells the client to build a rules object.
	CreateNetworkStringTables_GameRules();
}
