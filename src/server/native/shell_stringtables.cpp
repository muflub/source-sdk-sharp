//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// Creates the seven stock network string tables. Native, because native code
// adds to two of them; mod-declared tables are managed and created after these.

#include "networkstringtabledefs.h"
#include "tier0/dbg.h"

#include "server_state.h"
#include "server_engine.h"

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

// The DLL tier, which holds the two tables from DLLInit to DLLShutdown.
static CServerDllState &Dll()
{
	CServerDllState *pDll = ShellGameDll_Dll();
	AssertMsg( pDll, "a string table was asked for outside DLLInit..DLLShutdown" );
	return *pDll;
}

void PrecacheMaterial( const char *pMaterialName )
{
	Assert( pMaterialName && pMaterialName[0] );
	Dll().m_pMaterials->AddString( true, pMaterialName );
}

void PrecacheParticleSystem( const char *pParticleSystemName )
{
	Assert( pParticleSystemName && pParticleSystemName[0] );
	Dll().m_pParticleEffectNames->AddString( true, pParticleSystemName );
}

// A failed lookup answers 0, which SourceSharp_CreateNetworkStringTables makes
// the error particle system.
int GetParticleSystemIndex( const char *pParticleSystemName )
{
	if ( pParticleSystemName )
	{
		int nIndex = Dll().m_pParticleEffectNames->FindStringIndex( pParticleSystemName );
		if ( nIndex != INVALID_STRING_INDEX )
			return nIndex;

		DevWarning( "Server: Missing precache for particle system \"%s\"!\n", pParticleSystemName );
	}

	// This is the invalid string index
	return 0;
}

// One of the engine's tables. Only two are kept: the rest are created for the
// client and never read here.
static INetworkStringTable *CreateTable( const char *pName, int nMaxEntries )
{
	INetworkStringTable *pTable = ServerStringTables()->CreateStringTable( pName, nMaxEntries );
	AssertMsg( pTable, "the engine refused a stock string table" );
	return pTable;
}

// Stock's CServerGameDLL::CreateNetworkStringTables, without the econ/TF arms. Answers the
// rules' table, which the world writes its rules classname into.
INetworkStringTable *SourceSharp_CreateNetworkStringTables()
{
	CServerDllState &dll = Dll();

	dll.m_pParticleEffectNames = CreateTable( "ParticleEffectNames", kMaxParticleSystemStrings );
	CreateTable( "EffectDispatch", kMaxEffectDispatchStrings );
	CreateTable( "VguiScreen", kMaxVguiScreenStrings );
	dll.m_pMaterials = CreateTable( "Materials", kMaxMaterialStrings );
	CreateTable( "InfoPanel", kMaxInfoPanelStrings );
	CreateTable( "Scenes", kMaxChoreoScenesStrings );
	CreateTable( "ServerMapCycle", 128 );

	// These must be the first strings in their tables: every failed lookup
	// answers index 0, which must be the error material and particle system.
	PrecacheMaterial( "debug/debugempty" );

	PrecacheParticleSystem( "error" );
	Assert( GetParticleSystemIndex( "error" ) == 0 );

	// The rules' table, whose client half tells the client to build a rules object.
	return CreateNetworkStringTables_GameRules( ServerStringTables() );
}
