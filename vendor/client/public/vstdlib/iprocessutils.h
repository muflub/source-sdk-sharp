//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
//===========================================================================//

#ifndef IPROCESSUTILS_H
#define IPROCESSUTILS_H

#ifdef _WIN32
#pragma once
#endif


#include "appframework/IAppSystem.h"


//-----------------------------------------------------------------------------
// Handle to a process
//-----------------------------------------------------------------------------
typedef int ProcessHandle_t;
enum
{
	PROCESS_HANDLE_INVALID = 0,
};


//-----------------------------------------------------------------------------
// Interface version
//-----------------------------------------------------------------------------
#define PROCESS_UTILS_INTERFACE_VERSION "VProcessUtils001"


//-----------------------------------------------------------------------------
// Interface for makefiles to build differently depending on where they are run from
//-----------------------------------------------------------------------------
abstract_class IProcessUtils : public IAppSystem
{
public:
	// Starts, stops a process
	virtual ProcessHandle_t StartProcess( const char *pCommandLine, bool bConnectStdPipes ) = 0;
	virtual ProcessHandle_t StartProcess( int argc, const char **argv, bool bConnectStdPipes ) = 0;
	virtual void unused_CloseProcess() = 0;
	virtual void unused_AbortProcess() = 0;

	// Returns true if a process is complete
	virtual void unused_IsProcessComplete() = 0;

	// Waits until a process is complete
	virtual void unused_WaitUntilProcessCompletes() = 0;

	// Methods used to write input into a process
	virtual void unused_SendProcessInput() = 0;

	// Methods used to read	output back from a process
	virtual void unused_GetProcessOutputSize() = 0;
	virtual void unused_GetProcessOutput() = 0;
	
	// Returns the exit code for the process. Doesn't work unless the process is complete
	virtual void unused_GetProcessExitCode() = 0;
};


#endif // IPROCESSUTILS_H
