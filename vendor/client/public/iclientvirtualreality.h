//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Contains the IClientVirtualReality interface, which is implemented in
//			client.dll and called by engine.dll

#pragma once

// forward declarations

// important enumeration

// NOTE NOTE NOTE!!!!  If you up this, grep for "NEW_INTERFACE" to see if there is anything
// waiting to be enabled during an interface revision.

// The ISourceVirtualReality interface

abstract_class IClientVirtualReality : public IAppSystem
{
public:
	virtual ~IClientVirtualReality() {}

	// Placeholder for API revision
	virtual bool Connect( CreateInterfaceFn factory ) = 0;
	virtual void Disconnect() = 0;
	virtual void *QueryInterface( const char *pInterfaceName ) = 0;
	virtual InitReturnVal_t Init() = 0;
	virtual void Shutdown() = 0;

	// the interface

	// Draw the main menu in VR mode
	virtual void DrawMainMenu() = 0;
};
