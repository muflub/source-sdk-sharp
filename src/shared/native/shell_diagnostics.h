//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Shell diagnostics. Verbosity is the cl_/sv_sourcesharp_verbose ConVar:
//
//   0   errors only
//   1   the default: each STUB once, every TRACE every time
//   2   every STUB call, and the TRACE_HOT sites as well
#pragma once

int SourceSharp_GetVerbosity();

void SourceSharp_LogStub(const char* pWhat, bool* pbAlreadyLogged);
void SourceSharp_Trace(const char* pWhat, const char* pDetail);
void SourceSharp_DumpExportedInterfaces();
void SourceSharp_MarkDiagnosticsReady();

// Warns once per name and counts every reach of a capability this build does not
// ship; cs_cl_notshipped / cs_sv_notshipped print the ledger. Both strings are
// stored, not copied, so they must outlive the call.
class CShellNotShipped;
void SourceSharp_NotShipped(const char* pCapability, const char* pReason);

// Client only. Always false: the HLTV camera is not shipped. Records the refusal
// when engine->IsHLTV() says it mattered.
bool SourceSharp_HltvCameraAvailable();

void SourceSharp_LogEntityCreateFailed(const char* pClassName);
void SourceSharp_LogEntityDestroy(const char* pClassName);

// Logs every ServerClass / ClientClass this DLL registered, with its datatable.
void SourceSharp_DumpNetworkClasses();

void SourceSharp_LogEntityCreate(const char* pClassName);

// Marks a virtual the engine calls but SourceSharp does not implement.
#define SOURCESHARP_STUB()                             \
	do                                                 \
	{                                                  \
		static bool s_bLogged = false;                 \
		SourceSharp_LogStub(__FUNCTION__, &s_bLogged); \
	} while (0)

// Traces every call, so the console shows the order the engine drives them in.
#define SOURCESHARP_TRACE() SourceSharp_Trace(__FUNCTION__, NULL)
#define SOURCESHARP_TRACE_MSG(detail) SourceSharp_Trace(__FUNCTION__, detail)

// For anything the engine calls every tick or frame: traces only at verbosity 2.
#define SOURCESHARP_TRACE_HOT()                    \
	do                                             \
	{                                              \
		if (SourceSharp_GetVerbosity() > 1)        \
			SourceSharp_Trace(__FUNCTION__, NULL); \
	} while (0)

#define SOURCESHARP_STUB_NAMED(name)           \
	do                                         \
	{                                          \
		static bool s_bLogged = false;         \
		SourceSharp_LogStub(name, &s_bLogged); \
	} while (0)
