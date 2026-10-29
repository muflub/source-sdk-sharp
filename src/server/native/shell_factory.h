// The interfaces server.so answers by name. One table rather than EXPOSE_*
// macros, so a name answered twice is visible rather than decided by static
// initialisation order. Each row is defined in the file that owns the object.

#pragma once

// One interface this DLL answers: its version name and the object that answers it.
struct SourceSharpExposed
{
	const char* m_pszVersion;
	void* (*m_pfnGet)();
};

extern const SourceSharpExposed kExposeServerGameDll;
extern const SourceSharpExposed kExposeServerGameEnts;
extern const SourceSharpExposed kExposeServerGameClients;
extern const SourceSharpExposed kExposeServerGameClients004;
extern const SourceSharpExposed kExposeServerGameClients003;
extern const SourceSharpExposed kExposeServerGameTags;
extern const SourceSharpExposed kExposeServerDllSharedAppSystems;

// The table CreateInterface reads, in its order, for diagnostics and tests.
int SourceSharp_ExposedCount();
const SourceSharpExposed& SourceSharp_ExposedAt(int nIndex);
