//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the vphysics callbacks, built natively (libss_fwd_<side>) ===//
// SS_REASON b  an interface resolved by name at load
//
// plan_forwarders_everywhere.md phase 5. vphysics calls the handlers managed
// gives it -- IPhysicsObjectEvent, IPhysicsCollisionEvent,
// IPhysicsCollisionSolver, IPhysicsConstraintEvent, IMotionEvent and the key
// parser's IVPhysicsKeyHandler -- through their own C++ vtables. So the library
// builds each object here, deriving the Valve interface, and its overrides call
// one managed-built object whose table managed wrote (imanaged_physics_callbacks.h,
// generated from docs/managed-interfaces.tsv), in the order that file gives on
// every ABI. Engine types cross as void *: managed reads them through the
// forwarders, never here.
//
// THE OBJECT LIVES IN THE MANAGED BLOCK. Managed's block for each handler
// reserves kManagedPhysicsCallbackWords words after word 0, which the
// registry zeroes once and never writes again, a reload included; Construct
// builds the object there. So vphysics' pointer stays good across a managed
// reload, which patches the block in place, and the object is freed with the
// block, when native has let go of the side. Each class is a vptr and one
// pointer, trivially destructible: nothing runs before the free.
//
// THE LIBRARY ALLOCATES NOTHING and links no runtime (NO_RUNTIME). Built without
// RTTI (src/fwd/native/CMakeLists.txt), as fwd_steam.cpp is: with it, each
// class's vtable would point at a type_info whose own vtable is the runtime's.
//=============================================================================//
#include "ss_fwd_sdk.h"
#include "fwd_physics.h"

#include <new>

#include "vphysics_interface.h"
#include "vcollide_parse.h"
#include "imanaged_physics_callbacks.h"

namespace
{
	class SSPhysicsObjectEvent final : public IPhysicsObjectEvent
	{
	public:
		explicit SSPhysicsObjectEvent( IManagedPhysicsObjectEvent *pTarget ) : m_pTarget( pTarget ) {}

		void ObjectWake( IPhysicsObject *pObject ) override { m_pTarget->ObjectWake( pObject ); }
		void ObjectSleep( IPhysicsObject *pObject ) override { m_pTarget->ObjectSleep( pObject ); }

	private:
		IManagedPhysicsObjectEvent *m_pTarget;
	};

	class SSPhysicsCollisionEvent final : public IPhysicsCollisionEvent
	{
	public:
		explicit SSPhysicsCollisionEvent( IManagedPhysicsCollisionEvent *pTarget ) : m_pTarget( pTarget ) {}

		void PreCollision( vcollisionevent_t *pEvent ) override { m_pTarget->PreCollision( pEvent ); }
		void PostCollision( vcollisionevent_t *pEvent ) override { m_pTarget->PostCollision( pEvent ); }
		void Friction( IPhysicsObject *pObject, float energy, int surfaceProps, int surfacePropsHit, IPhysicsCollisionData *pData ) override
			{ m_pTarget->Friction( pObject, energy, surfaceProps, surfacePropsHit, pData ); }
		void StartTouch( IPhysicsObject *pObject1, IPhysicsObject *pObject2, IPhysicsCollisionData *pTouchData ) override
			{ m_pTarget->StartTouch( pObject1, pObject2, pTouchData ); }
		void EndTouch( IPhysicsObject *pObject1, IPhysicsObject *pObject2, IPhysicsCollisionData *pTouchData ) override
			{ m_pTarget->EndTouch( pObject1, pObject2, pTouchData ); }
		void FluidStartTouch( IPhysicsObject *pObject, IPhysicsFluidController *pFluid ) override { m_pTarget->FluidStartTouch( pObject, pFluid ); }
		void FluidEndTouch( IPhysicsObject *pObject, IPhysicsFluidController *pFluid ) override { m_pTarget->FluidEndTouch( pObject, pFluid ); }
		void PostSimulationFrame() override { m_pTarget->PostSimulationFrame(); }
		void ObjectEnterTrigger( IPhysicsObject *pTrigger, IPhysicsObject *pObject ) override { m_pTarget->ObjectEnterTrigger( pTrigger, pObject ); }
		void ObjectLeaveTrigger( IPhysicsObject *pTrigger, IPhysicsObject *pObject ) override { m_pTarget->ObjectLeaveTrigger( pTrigger, pObject ); }

	private:
		IManagedPhysicsCollisionEvent *m_pTarget;
	};

	class SSPhysicsCollisionSolver final : public IPhysicsCollisionSolver
	{
	public:
		explicit SSPhysicsCollisionSolver( IManagedPhysicsCollisionSolver *pTarget ) : m_pTarget( pTarget ) {}

		int ShouldCollide( IPhysicsObject *pObj0, IPhysicsObject *pObj1, void *pGameData0, void *pGameData1 ) override
			{ return m_pTarget->ShouldCollide( pObj0, pObj1, pGameData0, pGameData1 ); }
		int ShouldSolvePenetration( IPhysicsObject *pObj0, IPhysicsObject *pObj1, void *pGameData0, void *pGameData1, float dt ) override
			{ return m_pTarget->ShouldSolvePenetration( pObj0, pObj1, pGameData0, pGameData1, dt ); }
		bool ShouldFreezeObject( IPhysicsObject *pObject ) override { return m_pTarget->ShouldFreezeObject( pObject ); }
		int AdditionalCollisionChecksThisTick( int currentChecksDone ) override
			{ return m_pTarget->AdditionalCollisionChecksThisTick( currentChecksDone ); }
		bool ShouldFreezeContacts( IPhysicsObject **pObjectList, int objectCount ) override
			{ return m_pTarget->ShouldFreezeContacts( pObjectList, objectCount ); }

	private:
		IManagedPhysicsCollisionSolver *m_pTarget;
	};

	class SSPhysicsConstraintEvent final : public IPhysicsConstraintEvent
	{
	public:
		explicit SSPhysicsConstraintEvent( IManagedPhysicsConstraintEvent *pTarget ) : m_pTarget( pTarget ) {}

		void ConstraintBroken( IPhysicsConstraint *pConstraint ) override { m_pTarget->ConstraintBroken( pConstraint ); }

	private:
		IManagedPhysicsConstraintEvent *m_pTarget;
	};

	class SSMotionEvent final : public IMotionEvent
	{
	public:
		explicit SSMotionEvent( IManagedMotionEvent *pTarget ) : m_pTarget( pTarget ) {}

		// The answer is one of simresult_e's values, which managed returns as
		// the int vphysics' enum is.
		simresult_e Simulate( IPhysicsMotionController *pController, IPhysicsObject *pObject, float deltaTime,
		                      Vector &linear, AngularImpulse &angular ) override
		{
			return static_cast< simresult_e >( m_pTarget->Simulate( pController, pObject, deltaTime, &linear, &angular ) );
		}

	private:
		IManagedMotionEvent *m_pTarget;
	};

	class SSVPhysicsKeyHandler final : public IVPhysicsKeyHandler
	{
	public:
		explicit SSVPhysicsKeyHandler( IManagedVPhysicsKeyHandler *pTarget ) : m_pTarget( pTarget ) {}

		void ParseKeyValue( void *pData, const char *pKey, const char *pValue ) override { m_pTarget->ParseKeyValue( pData, pKey, pValue ); }
		void SetDefaults( void *pData ) override { m_pTarget->SetDefaults( pData ); }

	private:
		IManagedVPhysicsKeyHandler *m_pTarget;
	};

	// Every object is a vptr and one pointer: the block's reserved words hold it.
	static_assert( sizeof( SSPhysicsObjectEvent ) <= kManagedPhysicsCallbackWords * sizeof( void * ), "fits the block's reserved words" );
	static_assert( sizeof( SSPhysicsCollisionEvent ) <= kManagedPhysicsCallbackWords * sizeof( void * ), "fits the block's reserved words" );
	static_assert( sizeof( SSPhysicsCollisionSolver ) <= kManagedPhysicsCallbackWords * sizeof( void * ), "fits the block's reserved words" );
	static_assert( sizeof( SSPhysicsConstraintEvent ) <= kManagedPhysicsCallbackWords * sizeof( void * ), "fits the block's reserved words" );
	static_assert( sizeof( SSMotionEvent ) <= kManagedPhysicsCallbackWords * sizeof( void * ), "fits the block's reserved words" );
	static_assert( sizeof( SSVPhysicsKeyHandler ) <= kManagedPhysicsCallbackWords * sizeof( void * ), "fits the block's reserved words" );

	template < typename TObject, typename TInterface, typename TTarget >
	void *Build( void *pMemory, TTarget *pTarget )
	{
		if ( !pMemory || !pTarget )
			return nullptr;

		return static_cast< TInterface * >( new ( pMemory ) TObject( pTarget ) );
	}
}

int SSFwd_PhysicsCallbacks_SizeOf()
{
	return int( kManagedPhysicsCallbackWords * sizeof( void * ) );
}

void *SSFwd_PhysicsCallbacks_ConstructObjectEvent( void *pMemory, IManagedPhysicsObjectEvent *pTarget )
	{ return Build< SSPhysicsObjectEvent, IPhysicsObjectEvent >( pMemory, pTarget ); }
void *SSFwd_PhysicsCallbacks_ConstructCollisionEvent( void *pMemory, IManagedPhysicsCollisionEvent *pTarget )
	{ return Build< SSPhysicsCollisionEvent, IPhysicsCollisionEvent >( pMemory, pTarget ); }
void *SSFwd_PhysicsCallbacks_ConstructCollisionSolver( void *pMemory, IManagedPhysicsCollisionSolver *pTarget )
	{ return Build< SSPhysicsCollisionSolver, IPhysicsCollisionSolver >( pMemory, pTarget ); }
void *SSFwd_PhysicsCallbacks_ConstructConstraintEvent( void *pMemory, IManagedPhysicsConstraintEvent *pTarget )
	{ return Build< SSPhysicsConstraintEvent, IPhysicsConstraintEvent >( pMemory, pTarget ); }
void *SSFwd_PhysicsCallbacks_ConstructMotionEvent( void *pMemory, IManagedMotionEvent *pTarget )
	{ return Build< SSMotionEvent, IMotionEvent >( pMemory, pTarget ); }
void *SSFwd_PhysicsCallbacks_ConstructKeyHandler( void *pMemory, IManagedVPhysicsKeyHandler *pTarget )
	{ return Build< SSVPhysicsKeyHandler, IVPhysicsKeyHandler >( pMemory, pTarget ); }

// Each class's base vtable, which its constructor installs before the class's
// own replaces it, holds the C++ runtime's handler for a pure virtual call in
// its slots (CCallbackBase's in fwd_steam.cpp, the vphysics interfaces' here).
// That handler is the runtime's, and neither library links one (NO_RUNTIME),
// so it defines its own: trap, as the runtime's does. Never called: no object
// is used while its base is being built. A test that compiles this source into
// an executable with a runtime leaves the runtime's in place.
#if defined( SS_FWD_SERVER_BUILD ) || defined( SS_FWD_CLIENT_BUILD )
#if defined( _MSC_VER )
#if defined( SS_NO_CRT )
extern "C" int __cdecl _purecall( void )
{
	__debugbreak();
	return 0;
}
#endif
#else
extern "C" __attribute__(( visibility( "hidden" ) )) void __cxa_pure_virtual()
{
	__builtin_trap();
}
#endif
#endif
