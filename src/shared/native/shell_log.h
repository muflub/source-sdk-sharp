//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CShellLog: this DLL's SourceSharp logging -- the verbosity, whether tier0 is up to take
// Msg(), the trace, stub and entity-factory lines, and the not-shipped ledger -- as one
// object, a member of the side's root (user 2026-10-27: "Move logging to log class ... keep
// get verbosity"). The free calls the trace macros make from any file, static initialisers
// included (shell_diagnostics.h), forward to the DLL's log, and answer its defaults before
// the root is built.

#pragma once

#include "shell_notshipped.h"

#ifdef CLIENT_DLL
#include "tier1/convar.h"
#else
#include "managed_convar_ref.h"
#endif

class CShellLog
{
public:
	CShellLog();
	~CShellLog();

	CShellLog(const CShellLog&) = delete;
	CShellLog& operator=(const CShellLog&) = delete;

	// 0 = errors only, 1 = startup tracing, 2 = verbose (per-call). 1 until tier0 is up.
	int Verbosity() const;

	// Once the engine is up, after which Msg() is safe.
	void MarkReady() { m_bReady = true; }

	// To Msg() once tier0 is up, to stdout before.
	void Print(const char* pFmt, ...);

	void Trace(const char* pWhat, const char* pDetail);
	void Stub(const char* pWhat, bool* pbAlreadyLogged);
	void EntityCreate(const char* pClassName);
	void EntityCreateFailed(const char* pClassName);
	void EntityDestroy(const char* pClassName);

	CShellNotShipped& NotShipped() { return m_NotShipped; }

	void PrintNotShipped() const;

private:
	bool m_bReady = false;

	// Named per side, because Source rejects one ConVar registered from both DLLs: the
	// client's is its own ConVar, the server's is declared in managed code.
#ifdef CLIENT_DLL
	ConVar m_Verbose;
#else
	CManagedConVarRef m_Verbose;
#endif

	CShellNotShipped m_NotShipped;
};

// The DLL's log's Print, or stdout before the log exists.
void SourceSharp_DiagPrint(const char* pFmt, ...);
