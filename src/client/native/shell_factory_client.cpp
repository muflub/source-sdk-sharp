//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client's interface factory =====================//
// SS_REASON b  an interface resolved by name at load
//
// CreateInterface is the symbol the engine looks up in client.so, and every
// interface it asks this DLL for is a row the EXPOSE_*_INTERFACE macros made:
// each constructs an InterfaceReg during static initialisation, and the
// rows chain through InterfaceReg::s_pInterfaceRegs, newest first. The macros
// are in fork TUs too (clientleafsystem.cpp, clientshadowmgr.cpp, ...), so
// unlike the server's table (shell_factory.cpp) the client keeps the chain.
// These are tier1 interface.cpp's three definitions client.so needs, so
// interface.o leaves the client's link (plan_client_novalve C9). A name
// matches exactly, as strcmp; an unmatched one answers NULL, IFACE_FAILED.
// shell_factory_client_test holds the answers.
//
// client_factorylist.cpp's pair is here too (C9): shell_clientdll.cpp stores
// the engine's factories at Init and the tool framework and the vtable
// interfaces read them back. One stored copy, as stock's file static.
//=============================================================================//

#include "tier1/interface.h"
#include "client_factorylist.h"

#include <string.h>

InterfaceReg *InterfaceReg::s_pInterfaceRegs = NULL;

InterfaceReg::InterfaceReg( InstantiateInterfaceFn fn, const char *pName ) : m_pName( pName )
{
	m_CreateFn = fn;
	m_pNext = s_pInterfaceRegs;
	s_pInterfaceRegs = this;
}

void *CreateInterface( const char *pName, int *pReturnCode )
{
	for ( InterfaceReg *pReg = InterfaceReg::s_pInterfaceRegs; pReg; pReg = pReg->m_pNext )
	{
		if ( strcmp( pReg->m_pName, pName ) != 0 )
			continue;
		if ( pReturnCode )
			*pReturnCode = IFACE_OK;
		return pReg->m_CreateFn();
	}
	if ( pReturnCode )
		*pReturnCode = IFACE_FAILED;
	return NULL;
}

static factorylist_t s_Factories;

void FactoryList_Store( const factorylist_t &sourceData )
{
	s_Factories = sourceData;
}

void FactoryList_Retrieve( factorylist_t &destData )
{
	destData = s_Factories;
}
