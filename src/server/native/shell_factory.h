//========= SourceSharp — what server.so answers the engine by name =========//
//
// server.so's interface factory is ONE function, CreateInterface
// (shell_factory.cpp), comparing the name asked for against a fixed table.
// There are no EXPOSE_*_INTERFACE macros and no InterfaceReg nodes: the list of
// what this DLL answers is in one place, and a name answered twice is visible
// in that list rather than decided by static-initialisation order.
//
// Each row is defined in the file that owns the object, where the object's
// full class is visible, so the cast to the interface type is made exactly
// where the macro made it. The version names stay the SDK's macros for the
// same reason: they are spelled once, by the header that declares the
// interface.
//
//=============================================================================//

#ifndef SHELL_FACTORY_H
#define SHELL_FACTORY_H
#ifdef _WIN32
#pragma once
#endif

// One interface this DLL answers: the version name the engine (or a plugin)
// asks for, and the object that answers it.
struct SourceSharpExposed
{
	const char *m_pszVersion;
	void *( *m_pfnGet )();
};

extern const SourceSharpExposed kExposeServerGameDll;
extern const SourceSharpExposed kExposeServerGameEnts;
extern const SourceSharpExposed kExposeServerGameClients;
extern const SourceSharpExposed kExposeServerGameClients004;
extern const SourceSharpExposed kExposeServerGameClients003;
extern const SourceSharpExposed kExposeServerGameTags;
extern const SourceSharpExposed kExposeServerDllSharedAppSystems;

// The table CreateInterface reads, in its order: for the diagnostics dump and
// the tests, which must see what the factory sees.
int SourceSharp_ExposedCount();
const SourceSharpExposed &SourceSharp_ExposedAt( int nIndex );

#endif // SHELL_FACTORY_H
