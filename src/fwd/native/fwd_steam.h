//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the Steam callback's functions, as the table names them ==//
//
// plan_engine_forwarders.md section 6. fwd_steam.cpp writes these and the
// client library's generated table (fwd_client.g.inc, tools/engine-forwarders'
// HAND row SteamCallback) lays them out, in this order. They are the
// library's own: its version script exports none of them.
//=============================================================================//
#ifndef FWD_STEAM_H
#define FWD_STEAM_H
#pragma once

class CCallbackBase;
class IManagedSteamCallback;

// How many bytes Construct needs: what managed allocates for one callback.
int SSFwd_SteamCallback_SizeOf();

// Builds the callback in pMemory (SizeOf bytes, pointer-aligned) over its
// managed side, and answers the CCallbackBase * Steam registers. NULL, and
// nothing built, without memory or a target.
CCallbackBase *SSFwd_SteamCallback_Construct( void *pMemory, IManagedSteamCallback *pTarget, int nPayloadBytes );

// Whether Steam holds the callback: unregister it before freeing it only then.
bool SSFwd_SteamCallback_IsRegistered( CCallbackBase *pCallback );

// The id Steam filed the callback under when it registered it; 0 before.
int SSFwd_SteamCallback_CallbackId( CCallbackBase *pCallback );

#endif // FWD_STEAM_H
