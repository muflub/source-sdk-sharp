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

// THE DEFINES MUST MATCH THE ENGINE'S, and for cdll_int.h they are
// load-bearing: two of IVEngineClient013's virtuals are declared inside `#if`
// blocks, and each one shifts every slot below it. The forwarders below call
// those virtuals, so a different answer here calls the wrong method through
// every slot past the block. The shipped engine was built with USE_SDL in and
// NO_VOICE out on Linux and macOS; Valve's Windows engine renders through
// DirectX and has no SDL, so there USE_SDL must be OUT (source_video_base.vpc
// sets SDL only with GL). Checked before the headers, as command-line defines.
#if defined( _WIN32 )
#if defined( USE_SDL )
#error \
	"SourceSharp: the shipped Windows engine is built without USE_SDL and this translation unit has it. Every IVEngineClient013 slot from 127 down would be called one too high."
#endif
#elif !defined( USE_SDL )
#error \
	"SourceSharp: the shipped engine is built with USE_SDL and this translation unit is not. Every IVEngineClient013 slot from 127 down would be called one too low."
#endif
#if defined( NO_VOICE )
#error \
	"SourceSharp: the shipped engine is built with voice IN (NO_VOICE undefined) and this translation unit has NO_VOICE. Every IVEngineClient013 slot from 53 down would be called one too high."
#endif

// The interface headers the forwarders call through, and fwd_steam.h for the
// Steam callback's functions, are the generated file's own includes.
#include "fwd_client.g.inc"

SS_FWD_CLIENT_API const SSForwarderTable *SSFwdClient_Table( const char *pszName )
{
	return SSFwd_Find( s_ForwarderTables, pszName );
}
