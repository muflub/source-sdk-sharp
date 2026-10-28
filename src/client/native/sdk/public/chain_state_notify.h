//========= SourceSharp — who to tell when a chained network var moves =======//
//
// networkvar.h's DECLARE_NETWORKVAR_CHAIN lets a plain struct hold CNetworkVars
// and forward the change notification to the entity that contains it. The
// forwarding needs a pointer to that entity, and stock stores it as a
// CBaseEntity *:
//
//     class CAutoInitEntPtr { public: CBaseEntity *m_pEnt; };
//     void NetworkStateChanged() { __m_pChainEntity.m_pEnt->NetworkStateChanged(); }
//
// PHASE 13 TURNS THAT INTO A COMPILE ERROR. `using CBaseEntity = IGameEntity`
// makes m_pEnt an IGameEntity *, and IGameEntity has no NetworkStateChanged()
// — deliberately. It is the engine boundary and holds only what the engine
// reaches; NetworkStateChanged is asked by the game's own blob writer, which
// already holds concrete types, and by this macro. plan_baseentity.md names the
// two ways out: the method joins the interface, or the holder is retyped.
//
// THE HOLDER IS RETYPED, and the measurement is why. The chain macros have
// exactly one user in the SDK — smokestack.h and smokestack.cpp — and
// server_csharp.vpc evicts smokestack.cpp, so this build expands them ZERO
// times. "It has two unrelated callers" was the case for the method joining
// IGameEntity, and one of those two callers is no longer in the build. Putting
// a seventh method on a six-method boundary to serve nothing, and undoing the
// interface-emptying pass that got it to six, is not a trade worth making.
//
// SO THIS HOLDER NAMES NO ENTITY CLASS AT ALL. The type is only ever known at
// the assignment — IMPLEMENT_NETWORKVAR_CHAIN writes `this` from inside the
// owner — so the assignment is where it is captured, as a function that knows
// how to make the call. Everything after that is a void * and a function
// pointer, which no typedef can break.
//
// Header-only and engine-free, like bone_cache_key.h and handle_cross.h, so
// the native test binary can compile it: a notifier that silently forwards to
// nobody is exactly the failure this shape can have, and it is not one the
// build would report.
//
//=============================================================================//
#pragma once

//-----------------------------------------------------------------------------
// The owner of a network-var chain, whatever class that turns out to be.
//
// Default-constructs to "nobody", and calling it then does nothing — which is
// what the stock holder did too, except that it did it by dereferencing NULL.
//-----------------------------------------------------------------------------
class CChainStateNotify
{
public:
	CChainStateNotify() : m_pOwner( 0 ), m_pfnNotify( 0 ) {}

	// Binds it, and captures how to make the call. Spelled as an assignment so
	// IMPLEMENT_NETWORKVAR_CHAIN's `= this` still reads the way it always did.
	template < class T >
	CChainStateNotify &operator=( T *pOwner )
	{
		m_pOwner    = pOwner;
		m_pfnNotify = &Notify< T >;
		return *this;
	}

	bool IsBound() const { return m_pOwner != 0; }

	// Tells the owner. Nothing happens if there is no owner, which is the state
	// a struct is in between its constructor and the containing entity's.
	void NetworkStateChanged() const
	{
		if ( m_pOwner )
			m_pfnNotify( m_pOwner );
	}

private:
	template < class T >
	static void Notify( void *pOwner )
	{
		( ( T * )pOwner )->NetworkStateChanged();
	}

	void *m_pOwner;
	void ( *m_pfnNotify )( void * );
};
