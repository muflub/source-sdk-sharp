//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// tier0's spew and assert entry points, defined in server.so, which links no libtier0.
// Hidden visibility keeps them inside the DLL. The spew slot is this DLL's own; its
// default hands lines to the engine's current tier0 spew function (which writes the
// console and console.log), else to ICvar, else holds them until one exists. Asserts
// and errors are handed on as warnings: what they do is decided here, not by tier0.

#include "tier0/dbg.h"
#include "icvar.h"
#include "Color.h"
#include "tier1/convar.h"

#include "fatal_report.h"
#include "loaded_symbol.h"
#include "tier0_spew_port.h"
#include "ss_platform.h"
#ifdef _MSC_VER
// SS_BIND_IMPORT at the end binds this module's calls to these dllimport-declared functions.
#pragma warning(disable : 4273)
#endif

#include <atomic>
#include <mutex>
#include <stdarg.h>
#include <stdio.h>
#include <string.h>
#include <string>

namespace
{
// Where an assert's text came from: _SpewInfo sets it, _SpewMessage reads it,
// on the same thread, as tier0's did.
thread_local SpewType_t t_Type = SPEW_MESSAGE;
thread_local const tchar* t_pFile = nullptr;
thread_local int t_nLine = 0;

std::mutex s_EarlyLock;
std::string s_Early; // lines spewed before the console existed

AssertFailedNotifyFunc_t s_pAssertNotify = nullptr;

// The engine's console, connected by the DLL root beside its CCVarRegistry.
ICvar* s_pConsole = nullptr;

void WriteToConsole(SpewType_t type, const char* pText)
{
	switch (type)
	{
		case SPEW_WARNING:
			s_pConsole->ConsoleColorPrintf(Color(255, 255, 0, 255), "%s", pText);
			break;
		case SPEW_ASSERT:
		case SPEW_ERROR:
			s_pConsole->ConsoleColorPrintf(Color(255, 64, 64, 255), "%s", pText);
			break;
		default:
			s_pConsole->ConsolePrintf("%s", pText);
			break;
	}
}

SpewRetval_t ConsoleSpew(SpewType_t type, const tchar* pText);

// tier0's GetSpewOutputFunc in the libtier0.so the engine loaded, or NULL.
// Kept once found: engine.so needs that libtier0, so it outlives server.so. A
// miss is not kept, so a libtier0 that arrives later is still found.
using GetSpewOutputFunc_t = SpewOutputFunc_t (*)();
std::atomic<GetSpewOutputFunc_t> s_pEngineGetSpew{nullptr};

SpewOutputFunc_t EngineSpew()
{
	GetSpewOutputFunc_t pGet = s_pEngineGetSpew.load(std::memory_order_acquire);
	if (!pGet)
	{
		pGet = (GetSpewOutputFunc_t)ss::FindLoadedSymbol("libtier0.so", "GetSpewOutputFunc");
		if (!pGet)
			return nullptr;
		s_pEngineGetSpew.store(pGet, std::memory_order_release);
	}

	// Read on every line: the engine may change its slot at any time.
	SpewOutputFunc_t pSpew = pGet();
	return pSpew == ConsoleSpew ? nullptr : pSpew;
}

SpewType_t HandedOnAs(SpewType_t type)
{
	switch (type)
	{
		case SPEW_ASSERT:
		case SPEW_ERROR:
		case SPEW_WARNING:
			return SPEW_WARNING;
		default:
			return SPEW_MESSAGE;
	}
}

// Set while a line is in the engine's slot. A slot that leads back here
// (someone put one of ours into tier0's) would recurse without end; the second
// pass writes to ICvar instead.
thread_local bool t_bInEngine = false;

void HandToEngine(SpewOutputFunc_t pEngine, SpewType_t type, const char* pText)
{
	t_bInEngine = true;
	pEngine(type, pText);
	t_bInEngine = false;
}

// The default spew function: the engine's tier0 slot, else the engine console,
// else held until there is one.
SpewRetval_t ConsoleSpew(SpewType_t type, const tchar* pText)
{
	if (!pText)
		return SPEW_CONTINUE;

	SpewOutputFunc_t pEngine = t_bInEngine ? nullptr : EngineSpew();
	if (!pEngine && !s_pConsole)
	{
		std::lock_guard<std::mutex> hold(s_EarlyLock);
		s_Early += pText;
		return SPEW_CONTINUE;
	}

	{
		std::lock_guard<std::mutex> hold(s_EarlyLock);
		if (!s_Early.empty())
		{
			if (pEngine)
				HandToEngine(pEngine, SPEW_MESSAGE, s_Early.c_str());
			else
				s_pConsole->ConsolePrintf("%s", s_Early.c_str());
			s_Early.clear();
		}
	}

	if (pEngine)
		HandToEngine(pEngine, HandedOnAs(type), pText);
	else
		WriteToConsole(type, pText);
	return SPEW_CONTINUE;
}

SpewOutputFunc_t s_pSpew = ConsoleSpew;

SpewRetval_t SpewV(SpewType_t type, const tchar* pFormat, va_list args)
{
	char szText[4096];
	vsnprintf(szText, sizeof(szText), pFormat, args);
	return s_pSpew(type, szText);
}

bool DeveloperAt(int level)
{
	if (!s_pConsole)
		return false;
	const ConVar* pDeveloper = s_pConsole->FindVar("developer");
	return pDeveloper && pDeveloper->GetInt() >= level;
}
}

void Tier0_ConnectConsole(ICvar* pConsole)
{
	s_pConsole = pConsole;
}

void SpewOutputFunc(SpewOutputFunc_t func)
{
	s_pSpew = func ? func : ConsoleSpew;
}

SpewOutputFunc_t GetSpewOutputFunc()
{
	return s_pSpew;
}

// The Assert macro calls _SpewInfo, _SpewMessage, then the notify function, and asks
// about a dialog only on SPEW_DEBUGGER, which nothing here answers.
void _SpewInfo(SpewType_t type, const tchar* pFile, int line)
{
	t_Type = type;
	t_pFile = pFile;
	t_nLine = line;
}

SpewRetval_t _SpewMessage(const tchar* pMsg, ...)
{
	char szText[4096];
	int n = 0;

	// tier0's assert line: "file (line) : message\n".
	if (t_Type == SPEW_ASSERT && t_pFile)
		n = snprintf(szText, sizeof(szText), "%s (%d) : ", t_pFile, t_nLine);

	if (n < 0 || n >= (int)sizeof(szText))
		n = 0;

	va_list args;
	va_start(args, pMsg);
	vsnprintf(szText + n, sizeof(szText) - n, pMsg, args);
	va_end(args);

	if (t_Type == SPEW_ASSERT)
	{
		const size_t len = strlen(szText);
		if (len + 1 < sizeof(szText) && (len == 0 || szText[len - 1] != '\n'))
		{
			szText[len] = '\n';
			szText[len + 1] = 0;
		}
	}

	const SpewType_t type = t_Type;
	t_Type = SPEW_MESSAGE;
	return s_pSpew(type, szText);
}

bool ShouldUseNewAssertDialog()
{
	return false;
}

bool DoNewAssertDialog(const tchar* pFile, int line, const tchar* pExpression)
{
	return false;
}

void SetAssertFailedNotifyFunc(AssertFailedNotifyFunc_t func)
{
	s_pAssertNotify = func;
}

void CallAssertFailedNotifyFunc(const char* pchFile, int nLine, const char* pchMessage)
{
	if (s_pAssertNotify)
		s_pAssertNotify(pchFile, nLine, pchMessage);
}

void Msg(const tchar* pMsg, ...)
{
	va_list args;
	va_start(args, pMsg);
	SpewV(SPEW_MESSAGE, pMsg, args);
	va_end(args);
}

void Warning(const tchar* pMsg, ...)
{
	va_list args;
	va_start(args, pMsg);
	SpewV(SPEW_WARNING, pMsg, args);
	va_end(args);
}

void Tier0_DevSpewV(int level, SpewType_t type, const tchar* pMsg, va_list args)
{
	if (DeveloperAt(level))
		SpewV(type, pMsg, args);
}

// C++-linkage overloads have mangled import names SS_BIND_IMPORT cannot bind; with no
// tier0.lib linked, the linker binds their imports to these (LNK4217).
void DevMsg(const tchar* pMsg, ...)
{
	if (!DeveloperAt(1))
		return;
	va_list args;
	va_start(args, pMsg);
	SpewV(SPEW_MESSAGE, pMsg, args);
	va_end(args);
}

void DevWarning(const tchar* pMsg, ...)
{
	if (!DeveloperAt(1))
		return;
	va_list args;
	va_start(args, pMsg);
	SpewV(SPEW_WARNING, pMsg, args);
	va_end(args);
}

// Said, then the process ends through the fatal report.
void Error(const tchar* pMsg, ...)
{
	char szText[4096];
	va_list args;
	va_start(args, pMsg);
	vsnprintf(szText, sizeof(szText), pMsg, args);
	va_end(args);

	s_pSpew(SPEW_ERROR, szText);
	SourceSharp_FatalReport("ERROR", szText);
}

SS_BIND_IMPORT(Msg)
SS_BIND_IMPORT(Warning)
SS_BIND_IMPORT(Error)
SS_BIND_IMPORT(_SpewInfo)
SS_BIND_IMPORT(_SpewMessage)
SS_BIND_IMPORT(SpewOutputFunc)
SS_BIND_IMPORT(GetSpewOutputFunc)
SS_BIND_IMPORT(ShouldUseNewAssertDialog)
SS_BIND_IMPORT(DoNewAssertDialog)
SS_BIND_IMPORT(SetAssertFailedNotifyFunc)
SS_BIND_IMPORT(CallAssertFailedNotifyFunc)
