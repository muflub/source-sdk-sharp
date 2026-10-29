//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the Steam callback, built natively (libss_fwd_client) ====//
// SS_REASON b  an interface resolved by name at load
//
// plan_engine_forwarders.md section 6. SteamAPI_RegisterCallback takes a
// CCallbackBase *, and Steam calls its three virtuals: Run( void * ),
// Run( void *, bool, SteamAPICall_t ) and GetCallbackSizeBytes. The order of
// the two Run overloads is the compiler's (MSVC reverses them), so a table
// managed wrote in one order is wrong on the other ABI. The client's forwarder
// library builds the object instead, here, in a translation unit of its own: SSSteamCallback derives CCallbackBase, so its vtable is
// this compiler's, and its virtuals call one managed-built object,
// IManagedSteamCallback, whose single slot is the same everywhere.
//
// The callback's payload size lives here too, so GetCallbackSizeBytes is
// answered without a call into managed, and the registered flag Steam keeps in
// the object is read here (IsRegistered), so managed reads no CCallbackBase
// member by offset.
//
// THE LIBRARY ALLOCATES NOTHING. It links no runtime (NO_RUNTIME), so it has
// no heap: managed allocates SizeOf bytes, Construct builds the object in
// them, and managed frees them once Steam has let go (SteamCallbacks.Dispose).
// The class is trivially destructible (CCallbackBase declares no destructor),
// so there is nothing to run before the free.
//
// The functions are the table's slots (tools/engine-forwarders' HAND row
// SteamCallback): their names are the generator's, SSFwd_<table>_<slot>,
// declared in fwd_steam.h, and fwd_client.g.inc lays them out. This file is
// built without RTTI (src/fwd/native/CMakeLists.txt): with it, SSSteamCallback's
// vtable would point at a type_info whose own vtable is the C++ runtime's.
//=============================================================================//
#include "ss_fwd_sdk.h"

#include "steam/steam_api_common.h"
#include "imanaged_steam_callback.h"

namespace
{
class SSSteamCallback final : public CCallbackBase
{
public:
	SSSteamCallback( IManagedSteamCallback *pTarget, int nPayloadBytes )
		: m_pTarget( pTarget )
		, m_nPayloadBytes( nPayloadBytes )
	{
	}

	void Run( void *pvParam ) override { m_pTarget->Run( pvParam ); }

	// A call result: stock's CCallback runs the same member function, the
	// I/O failure and call handle unread.
	void Run( void *pvParam, bool, SteamAPICall_t ) override { m_pTarget->Run( pvParam ); }

	int GetCallbackSizeBytes() override { return m_nPayloadBytes; }

	// The flag SteamAPI_RegisterCallback sets and SteamAPI_Unregister-
	// Callback clears, which stock's CCallback destructor reads to decide
	// whether to unregister.
	bool IsRegistered() const { return ( m_nCallbackFlags & k_ECallbackFlagsRegistered ) != 0; }

private:
	IManagedSteamCallback *m_pTarget;
	int m_nPayloadBytes;
};
}

// How many bytes Construct needs: what managed allocates for one callback.
int SSFwd_SteamCallback_SizeOf()
{
	return int( sizeof( SSSteamCallback ) );
}

// Builds the callback in pMemory (SizeOf bytes, pointer-aligned) over its
// managed side, and answers the CCallbackBase * Steam registers. NULL, and
// nothing built, without memory or a target.
CCallbackBase *SSFwd_SteamCallback_Construct( void *pMemory, IManagedSteamCallback *pTarget, int nPayloadBytes )
{
	if ( !pMemory || !pTarget )
		return nullptr;

	return new ( pMemory ) SSSteamCallback( pTarget, nPayloadBytes );
}

// Whether Steam holds the callback: unregister it before freeing it only then.
bool SSFwd_SteamCallback_IsRegistered( CCallbackBase *pCallback )
{
	return pCallback && static_cast<SSSteamCallback *>( pCallback )->IsRegistered();
}
