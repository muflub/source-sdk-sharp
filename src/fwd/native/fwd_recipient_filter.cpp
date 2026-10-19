//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the recipient filter, built natively (libss_fwd_<side>) ===//
// SS_REASON a  a vtable the engine calls through
//
// plan_server_native_v2 T4a. IEngineSound::EmitSound takes an IRecipientFilter
// & and calls it through its own C++ vtable, so the object it is handed is
// built here, deriving IRecipientFilter, and its four overrides call one
// managed-built object whose table managed wrote (IManagedRecipientFilter,
// generated into imanaged_physics_callbacks.h from docs/managed-interfaces.tsv).
// WHO HEARS A SOUND IS MANAGED'S DECISION: the SoundEmitter chooses the
// recipients and this class only answers the engine's questions about them.
// The vphysics callbacks (fwd_physics.cpp) are the precedent, and the object
// lives where theirs do: in its managed block's reserved words.
//
// THE DESTRUCTOR IS IRecipientFilter's, and it is virtual, so the class's
// vtable carries a deleting destructor that names an operator delete. The
// library links no runtime (NO_RUNTIME), and nothing ever deletes this object
// -- it is freed with its block -- so the class names its own, which does
// nothing, rather than import the runtime's.
//=============================================================================//
#include "ss_fwd_sdk.h"
#include "fwd_recipient_filter.h"

#include <new>
#include <stddef.h>

#include "irecipientfilter.h"
#include "imanaged_physics_callbacks.h"

namespace
{
	class SSRecipientFilter final : public IRecipientFilter
	{
	public:
		explicit SSRecipientFilter( IManagedRecipientFilter *pTarget ) : m_pTarget( pTarget ) {}

		bool IsReliable( void ) const override { return m_pTarget->IsReliable() != 0; }
		bool IsInitMessage( void ) const override { return m_pTarget->IsInitMessage() != 0; }
		int GetRecipientCount( void ) const override { return m_pTarget->GetRecipientCount(); }
		int GetRecipientIndex( int slot ) const override { return m_pTarget->GetRecipientIndex( slot ); }

		static void operator delete( void *, size_t ) {}

	private:
		IManagedRecipientFilter *m_pTarget;
	};

	// A vptr and one pointer: the block's reserved words hold it.
	static_assert( sizeof( SSRecipientFilter ) <= kManagedPhysicsCallbackWords * sizeof( void * ), "fits the block's reserved words" );
}

int SSFwd_RecipientFilter_SizeOf()
{
	return int( kManagedPhysicsCallbackWords * sizeof( void * ) );
}

void *SSFwd_RecipientFilter_Construct( void *pMemory, IManagedRecipientFilter *pTarget )
{
	if ( !pMemory || !pTarget )
		return nullptr;

	return static_cast< IRecipientFilter * >( new ( pMemory ) SSRecipientFilter( pTarget ) );
}
