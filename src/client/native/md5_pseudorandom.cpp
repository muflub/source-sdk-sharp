//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — a user command's random seed, from C# ==============//
// SS_REASON e  a synchronous mid-step answer
//
// stock's CInput::CreateMove (game/client/in_main.cpp) seeds every new user
// command with MD5_PseudoRandom( command_number ), and client prediction draws
// its shared random numbers from that seed -- so it has to be exactly the value
// the server's decode computes. That is C#'s UserCmdCodec.Md5PseudoRandom, and
// this definition asks it through IManagedDiagnosticsClient (CE9a), so tier1's
// checksum_md5.cpp is out of the build (cmake/sdk/tier1.cmake).
//
// Declared by tier1/checksum_md5.h, whose other functions no code in either
// DLL calls. A missing area is a managed side that did not load, which the
// client does not survive anyway: Error() here names it rather than seeding
// every command with 0 and letting prediction disagree with the server.
//
// md5_pseudorandom_test pins the forwarding and the fatal arm's condition.
//
//=============================================================================//

#include "checksum_md5.h"
#include "imanaged_areas_client.h"
#include "tier0/dbg.h"

unsigned int MD5_PseudoRandom( unsigned int nSeed )
{
	IManagedDiagnosticsClient *pDiagnostics = ManagedDiagnosticsClient();
	if ( !pDiagnostics )
	{
		Error( "[SourceSharp] MD5_PseudoRandom: the managed client did not provide "
		       "IManagedDiagnosticsClient, so a user command cannot be seeded as the "
		       "server seeds it.\n" );
	}

	return pDiagnostics->Md5PseudoRandom( nSeed );
}
