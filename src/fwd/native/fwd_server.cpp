//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — libss_fwd_server: the server's engine and vphysics calls ==//
// SS_REASON b  an interface resolved by name at load
//
// plan_engine_forwarders.md. One forwarder library per side (ruled
// 2026-10-18): this is the server's, linked by server.so alone, serving the
// tables the server's managed code can reach -- the engine's interfaces and
// vphysics' objects both sides call, and any only the server calls. The
// forwarders and their tables are generated (fwd_server.g.inc, by
// tools/engine-forwarders, with the interface headers they need); this file
// answers the library's one export.
//
// The library holds no state and connects to nothing: every forwarder is
// handed the object it calls through, and the tables are constant data. So it
// needs the SDK headers to compile and no SDK library to link.
//=============================================================================//
#include "ss_fwd_sdk.h"
#include "ss_fwd.h"

// The interface headers the forwarders call through are the generated file's
// own includes.
#include "fwd_server.g.inc"

SS_FWD_SERVER_API const SSForwarderTable* SSFwdServer_Table(const char* pszName)
{
	return SSFwd_Find(s_ForwarderTables, pszName);
}
