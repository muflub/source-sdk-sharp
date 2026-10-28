//========= SourceSharp — an ICvar that records what is printed to it ========//
//
// For the tier0 spew tests: every ConsolePrintf / ConsoleColorPrintf /
// ConsoleDPrintf lands in m_Printed, kind and text. Everything else is inert.
//
//=============================================================================//

#pragma once

#include "icvar.h"
#include "Color.h"

#include <stdarg.h>
#include <stdio.h>
#include <string>
#include <vector>

namespace
{
struct Printed
{
	std::string kind;   // "plain", "color", "dev"
	std::string text;
};

class CRecordingCvar final : public ICvar
{
public:
	mutable std::vector< Printed > m_Printed;
	// The install/remove pair the console-display forwarder is gated on
	// (console_display_forward_test.cpp): what was installed, and what later
	// came off again.
	std::vector<IConsoleDisplayFunc *> m_Installed;
	std::vector<IConsoleDisplayFunc *> m_Removed;

	virtual bool Connect( CreateInterfaceFn ) { return true; }
	virtual void Disconnect() {}
	virtual void *QueryInterface( const char * ) { return NULL; }
	virtual InitReturnVal_t Init() { return INIT_OK; }
	virtual void Shutdown() {}
	virtual CVarDLLIdentifier_t AllocateDLLIdentifier() { return 7; }
	virtual void RegisterConCommand( ConCommandBase * ) {}
	virtual void UnregisterConCommand( ConCommandBase * ) {}
	virtual void UnregisterConCommands( CVarDLLIdentifier_t ) {}
	virtual const char *GetCommandLineValue( const char * ) { return NULL; }
	virtual ConCommandBase *FindCommandBase( const char * ) { return NULL; }
	virtual const ConCommandBase *FindCommandBase( const char * ) const { return NULL; }
	virtual ConVar *FindVar( const char * ) { return NULL; }
	virtual const ConVar *FindVar( const char * ) const { return NULL; }
	virtual ConCommand *FindCommand( const char * ) { return NULL; }
	virtual const ConCommand *FindCommand( const char * ) const { return NULL; }
	virtual ConCommandBase *GetCommands( void ) { return NULL; }
	virtual const ConCommandBase *GetCommands( void ) const { return NULL; }
	virtual void InstallGlobalChangeCallback( FnChangeCallback_t ) {}
	virtual void RemoveGlobalChangeCallback( FnChangeCallback_t ) {}
	virtual void CallGlobalChangeCallbacks( ConVar *, const char *, float ) {}
	virtual void InstallConsoleDisplayFunc( IConsoleDisplayFunc *pfnFunc ) { m_Installed.push_back( pfnFunc ); }
	virtual void RemoveConsoleDisplayFunc( IConsoleDisplayFunc *pfnFunc ) { m_Removed.push_back( pfnFunc ); }
	virtual void ConsoleColorPrintf( const Color &, const char *pFormat, ... ) const
	{
		va_list args;
		va_start( args, pFormat );
		Record( "color", pFormat, args );
		va_end( args );
	}
	virtual void ConsolePrintf( const char *pFormat, ... ) const
	{
		va_list args;
		va_start( args, pFormat );
		Record( "plain", pFormat, args );
		va_end( args );
	}
	virtual void ConsoleDPrintf( const char *pFormat, ... ) const
	{
		va_list args;
		va_start( args, pFormat );
		Record( "dev", pFormat, args );
		va_end( args );
	}
	virtual void RevertFlaggedConVars( int ) {}
	virtual void InstallCVarQuery( ICvarQuery * ) {}
	virtual void PublishToVXConsole() {}
	virtual bool IsMaterialThreadSetAllowed() const { return false; }
	virtual void QueueMaterialThreadSetValue( ConVar *, const char * ) {}
	virtual void QueueMaterialThreadSetValue( ConVar *, int ) {}
	virtual void QueueMaterialThreadSetValue( ConVar *, float ) {}
	virtual bool HasQueuedMaterialThreadConVarSets() const { return false; }
	virtual int ProcessQueuedMaterialThreadConVarSets() { return 0; }
	virtual ICVarIteratorInternal *FactoryInternalIterator( void ) { return NULL; }

private:
	void Record( const char *kind, const char *pFormat, va_list args ) const
	{
		char szText[ 8192 ];
		vsnprintf( szText, sizeof( szText ), pFormat, args );
		m_Printed.push_back( Printed{ kind, szText } );
	}
};

}
