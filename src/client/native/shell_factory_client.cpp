//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON b  an interface resolved by name at load
// CreateInterface walks the InterfaceReg chain the EXPOSE_*_INTERFACE macros
// build during static initialisation; names match exactly.

#include "client_factorylist.h"

InterfaceReg *InterfaceReg::s_pInterfaceRegs = NULL;

InterfaceReg::InterfaceReg( InstantiateInterfaceFn fn, const char *pName )
	: m_pName( pName )
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
