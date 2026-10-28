//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
//===========================================================================//

#ifndef TIER0_ICOMMANDLINE_H
#define TIER0_ICOMMANDLINE_H
#ifdef _WIN32
#pragma once
#endif

#include "tier0/platform.h"


//-----------------------------------------------------------------------------
// Purpose: Interface to engine command line
//-----------------------------------------------------------------------------
abstract_class ICommandLine
{
public:
	virtual void		CreateCmdLine( const char *commandline ) = 0;
	virtual void		CreateCmdLine( int argc, char **argv ) = 0;
	virtual void unused_GetCmdLine() = 0;

	// Check whether a particular parameter exists
	virtual	const char	*CheckParm( const char *psz, const char **ppszValue = 0 ) const = 0;
	virtual void unused_RemoveParm() = 0;
	virtual void unused_AppendParm() = 0;

	// Returns the argument after the one specified, or the default if not found
	virtual const char	*ParmValue( const char *psz, const char *pDefaultVal = 0 ) const = 0;
	virtual int			ParmValue( const char *psz, int nDefaultVal ) const = 0;
	virtual float		ParmValue( const char *psz, float flDefaultVal ) const = 0;

	// Gets at particular parameters
	virtual void unused_ParmCount() = 0;
	virtual int			FindParm( const char *psz ) const = 0;	// Returns 0 if not found.
	virtual void unused_GetParm() = 0;
	
	// copies the string passwed
	virtual void unused_SetParm() = 0;

	virtual void unused_ParmValueByIndex() = 0;

	// A bool return of whether param exists, useful for just checking if param that is just a flag is set
	virtual void unused_HasParm() = 0;

	virtual void unused_GetParms() = 0;

	// Newer call with flag to control paramfile behavior
	virtual void		CreateCmdLine1( const char *commandline, bool bParseParamFiles ) = 0;
	virtual void		CreateCmdLine1( int argc, char **argv, bool bParseParamFiles ) = 0;
};

//-----------------------------------------------------------------------------
// Gets a singleton to the commandline interface
// NOTE: The #define trickery here is necessary for backwards compat:
// this interface used to lie in the vstdlib library.
//-----------------------------------------------------------------------------
PLATFORM_INTERFACE ICommandLine *CommandLine_Tier0();

#if !defined( VSTDLIB_BACKWARD_COMPAT )
#define CommandLine CommandLine_Tier0
#endif

#endif // TIER0_ICOMMANDLINE_H

