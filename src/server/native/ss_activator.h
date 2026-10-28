//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The entity I/O activator on either hierarchy. The SDK half reads NULL for an
// activator on the other hierarchy, as before. The event queue stores the other
// half as a CBaseHandle beside its EHANDLE, since an EHANDLE naming the other
// kind would hand its reader a pointer of the wrong type.
#pragma once




// Instantiated only at call sites that have ehandle.h.
template < class T > class CHandle;




// Reached from inside baseentity.h's guard, where CBaseEntity is incomplete, so
// everything inline here works without it.
struct Activator_t
{
	// The SDK entity; NULL for the other kind.
	IGameEntity *pBase;

	// The entity outside the SDK. NULL for a CBaseEntity.
	IGameEntity *pGame;

	// The game half's identity; IGameEntity is not an IHandleEntity. Set with pGame.
	IHandleEntity *pGameHandle;

	Activator_t() : pBase( NULL ), pGame( NULL ), pGameHandle( NULL ) {}

	// Implicit, so existing FireOutput calls passing a CBaseEntity * keep compiling.
	Activator_t( IGameEntity *pEntity )
		: pBase( pEntity ), pGame( NULL ), pGameHandle( NULL ) {}

	// For activators held in a handle; a template so the type completes at the call.
	template < class T >
	Activator_t( const CHandle< T > &handle )
		: pBase( handle.Get() ), pGame( NULL ), pGameHandle( NULL ) {}

	Activator_t( IGameEntity *pEntity, IHandleEntity *pHandle )
		: pBase( NULL ), pGame( pEntity ), pGameHandle( pHandle ) {}

	bool IsValid() const { return pBase != NULL || pGame != NULL; }

	// Empty for an SDK activator or none.
	CBaseHandle GetGameRefEHandle() const
	{
		return pGameHandle ? pGameHandle->GetRefEHandle() : CBaseHandle();
	}
};

// Rebuilt from the handle at delivery; an entity removed meanwhile resolves to nothing.
Activator_t ActivatorFromGameHandle( const CBaseHandle &handle );

// The two halves an event stored, back together.
Activator_t ActivatorOf( IGameEntity *pBase, const CBaseHandle &hGame );
