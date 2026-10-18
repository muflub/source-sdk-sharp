//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — libss_fwd_client: the client's engine, vphysics and Steam calls ==//
// SS_REASON b  an interface resolved by name at load
//
// plan_engine_forwarders.md. One forwarder library per side (ruled
// 2026-10-18): this is the client's, linked by client.so alone, serving the
// tables the client's managed code can reach -- the engine's interfaces and
// vphysics' objects both sides call, and those only the client calls (the
// engine client, the input system, IPhysics, and the Steam callback, whose
// functions fwd_steam.cpp writes). The forwarders and their tables are
// generated (fwd_client.g.inc, by tools/engine-forwarders, with the headers
// they need); this file answers the library's one export.
//
// The library holds no state and connects to nothing: every forwarder is
// handed the object it calls through, and the tables are constant data. So it
// needs the SDK headers to compile and no SDK library to link.
//=============================================================================//
#include "ss_fwd_sdk.h"
#include "ss_fwd.h"

// The interface headers the forwarders call through, and fwd_steam.h for the
// Steam callback's functions, are the generated file's own includes.
#include "fwd_client.g.inc"

SS_FWD_CLIENT_API const SSForwarderTable *SSFwdClient_Table( const char *pszName )
{
	return SSFwd_Find( s_ForwarderTables, pszName );
}
