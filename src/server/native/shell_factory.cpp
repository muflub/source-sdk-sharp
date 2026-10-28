//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load

// CreateInterface: answers each interface name the engine asks this DLL for from
// the kExposed table. Names match exactly, as stock's strcmp did; anything else,
// and a NULL name, answers NULL with IFACE_FAILED.

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
