//========= SourceSharp — who owns a console variable's name ================//
//
// A managed declaration never aliases a convar the engine defines (the user's
// rule, 2026-09-29). When C# declares a name the engine's table already holds,
// the engine would link our carrier to it as a child: a second declaration of
// the engine's variable, with its own copy of the default and the help text.
// SourceSharp_EngineRegisterConVar refuses that instead, and the managed
// registry refuses to start (ConVarRegistry.RegisterAll).
//
// OWNERSHIP IS READ OFF THE FLAGS. Each game DLL's registration ORs in its own
// identifier -- FCVAR_GAMEDLL for server.so, FCVAR_CLIENTDLL for client.so --
// and nothing else sets either. A variable carrying neither was declared by
// the engine or one of its modules (material system, vgui, ...).
//
// A client declaration whose name the SERVER already registered is not
// refused: that is a replicated pair, the client's twin linked to the
// server's, exactly as two native declarations are.
//
// convar_ownership_test pins both answers.
//
//=============================================================================//

#ifndef CONVAR_OWNERSHIP_H
#define CONVAR_OWNERSHIP_H
#ifdef _WIN32
#pragma once
#endif

#include "tier1/iconvar.h"

// What SourceSharp_EngineRegisterConVar answers for a refused engine-owned
// name. Every other refusal is -1; both are below zero, which is how the
// managed side has always read "refused".
#define SOURCESHARP_CONVAR_ENGINE_OWNED ( -2 )

// True when an existing variable with these flags belongs to neither game DLL.
inline bool SourceSharp_ConVarOwnedByEngine( int nExistingFlags )
{
	return ( nExistingFlags & ( FCVAR_GAMEDLL | FCVAR_CLIENTDLL ) ) == 0;
}

#endif // CONVAR_OWNERSHIP_H
