//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — one Steam callback's managed side =================//
//
// plan_engine_forwarders.md section 6. Steam calls a registered callback
// through CCallbackBase's three virtuals, and the order of its two Run
// overloads is the compiler's: MSVC reverses them. So managed no longer builds
// a CCallbackBase. libss_fwd_steam builds one (fwd_steam.cpp), and its
// virtuals call this: one method, no overload, so its slots are the same on
// every ABI and managed knows nothing about the C++ one.
//
// IMPLEMENTED BY MANAGED (tools/vtable-layout's ManagedSteamCallbackVtbl):
// managed writes the table, one block per registered callback. Release is
// slot 0 and Validate slot 1, as every managed-built object's; native calls
// neither. Managed releases the block itself, after Steam has let go of the
// callback that holds it (SteamCallbacks.Dispose). No virtual destructor:
// nothing native deletes it.
//=============================================================================//
#ifndef IMANAGED_STEAM_CALLBACK_H
#define IMANAGED_STEAM_CALLBACK_H
#pragma once

#include "imanaged_core.h"

abstract_class IManagedSteamCallback : public IManagedObject
{
public:
	// A callback or a call result arrived: the payload Steam hands over,
	// GetCallbackSizeBytes of it. Both of CCallbackBase's Run overloads come
	// here, as stock's CCallback runs its member function for either.
	virtual void Run( void *pvParam ) = 0;
};

#endif // IMANAGED_STEAM_CALLBACK_H
