//========= Copyright Valve Corporation, All rights reserved. ============//
// The copyright to the contents herein is the property of Valve, L.L.C.
// The contents may be used and/or copied only with the written permission of
// Valve, L.L.C., or in accordance with the terms and conditions stipulated in
// the agreement/contract under which the contents have been supplied.
// Purpose: Defines a group of app systems that all have the same lifetime
// that need to be connected/initialized, etc. in a well-defined order

#pragma once

#include "tier1/utldict.h"

// forward declarations
class IAppSystem;
class CSysModule;
class IBaseInterface;
class IFileSystem;

// Handle to a DLL
typedef int AppModule_t;

enum
{
	APP_MODULE_INVALID = (AppModule_t)~0
};

// NOTE: The following methods must be implemented in your application
// although they can be empty implementations if you like...
abstract_class IAppSystemGroup
{
public:
	// An installed application creation function, you should tell the group
	// the DLLs and the singleton interfaces you want to instantiate.
	// Return false if there's any problems and the app will abort
	virtual bool Create( ) = 0;

	// Allow the application to do some work after AppSystems are connected but 
	// they are all Initialized.
	// Return false if there's any problems and the app will abort
	virtual bool PreInit() = 0;

	// Main loop implemented by the application
	virtual int Main( ) = 0;

	// Allow the application to do some work after all AppSystems are shut down
	virtual void PostShutdown() = 0;

	// Call an installed application destroy function, occurring after all modules
	// are unloaded
	virtual void Destroy() = 0;
};

// Specifies a module + interface name for initialization
struct AppSystemInfo_t
{
	const char *m_pModuleName;
	const char *m_pInterfaceName;
};

// This class represents a group of app systems that all have the same lifetime
// that need to be connected/initialized, etc. in a well-defined order
class CAppSystemGroup : public IAppSystemGroup
{
public:
	// Used to determine where we exited out from the system
	enum AppSystemGroupStage_t
	{
		CREATION = 0,
		CONNECTION,
		PREINITIALIZATION,
		INITIALIZATION,
		SHUTDOWN,
		POSTSHUTDOWN,
		DISCONNECTION,
		DESTRUCTION,

		NONE,	// This means no error
	};

public:
	// constructor
	CAppSystemGroup( CAppSystemGroup *pParentAppSystem = NULL );

	// Runs the app system group.
	// First, modules are loaded, next they are connected, followed by initialization
	// Then Main() is run
	// Then modules are shut down, disconnected, and unloaded
	int Run( );

	// Use this version in cases where you can't control the main loop and
	// expect to be ticked
	virtual int Startup();
	virtual void Shutdown();

protected:
	// These methods are meant to be called by derived classes of CAppSystemGroup

private:

	// Loads a module the standard way
	virtual CSysModule *LoadModuleDLL( const char *pDLLName );

	struct Module_t
	{
		CSysModule *m_pModule;
		CreateInterfaceFn m_Factory;
		char *m_pModuleName;
	};

	CUtlVector<Module_t> m_Modules;
	CUtlVector<IAppSystem*> m_Systems;
	CUtlDict<int, unsigned short> m_SystemDict;
	CAppSystemGroup *m_pParentAppSystem;
	AppSystemGroupStage_t m_nErrorStage;

	friend class CSteamAppSystemGroup;
};

// This class represents a group of app systems that are loaded through steam
class CSteamAppSystemGroup : public CAppSystemGroup
{
public:
	CSteamAppSystemGroup( IFileSystem *pFileSystem = NULL, CAppSystemGroup *pParentAppSystem = NULL );

protected:

private:
	virtual CSysModule *LoadModuleDLL( const char *pDLLName );

	IFileSystem *m_pFileSystem;
	char m_pGameInfoPath[ MAX_PATH ];
};

// Special helper for game info directory suggestion

class CFSSteamSetupInfo;	// Forward declaration
