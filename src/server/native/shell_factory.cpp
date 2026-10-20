//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the interface factory, as one function ============//
// SS_REASON b  an interface resolved by name at load
//
// CreateInterface is the one symbol the engine looks up in server.so, and every
// interface it (or a server plugin) asks this DLL for is answered here, by
// comparing the name with each row of kExposed. What stock did with a linked
// list of InterfaceReg nodes, built during static initialisation by the
// EXPOSE_*_INTERFACE macros, is this table: see shell_factory.h for why. A
// name matches exactly (case and length), as stock's strcmp did; anything
// else, and a NULL name, answers NULL with IFACE_FAILED.
//
// server_factory_test pins the table and the answers.
//
//=============================================================================//

#include "tier1/interface.h"
#include "shell_factory.h"

#include <string_view>

namespace
{

const SourceSharpExposed *const kExposed[] =
{
	&kExposeServerGameDll,
	&kExposeServerGameEnts,
	&kExposeServerGameClients,
	&kExposeServerGameClients004,
	&kExposeServerGameClients003,
	&kExposeServerGameTags,
	&kExposeServerDllSharedAppSystems,
};

constexpr int kExposedCount = (int)( sizeof( kExposed ) / sizeof( kExposed[0] ) );

}

void *CreateInterface( const char *pName, int *pReturnCode )
{
	void *pAnswer = nullptr;
	if ( pName )
	{
		const std::string_view name( pName );
		for ( const SourceSharpExposed *pRow : kExposed )
		{
			if ( name == pRow->m_pszVersion )
			{
				pAnswer = pRow->m_pfnGet();
				break;
			}
		}
	}

	if ( pReturnCode )
		*pReturnCode = pAnswer ? IFACE_OK : IFACE_FAILED;
	return pAnswer;
}

int SourceSharp_ExposedCount()
{
	return kExposedCount;
}

const SourceSharpExposed &SourceSharp_ExposedAt( int nIndex )
{
	return *kExposed[nIndex];
}
