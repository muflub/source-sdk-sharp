// The owner of a network-var chain (DECLARE_NETWORKVAR_CHAIN), held without
// naming an entity class: the type is captured at IMPLEMENT_NETWORKVAR_CHAIN's
// `= this` as a function that makes the NetworkStateChanged call.
#pragma once

// Default-constructs to no owner, and notifying it then does nothing (stock's
// holder dereferenced NULL).
class CChainStateNotify
{
public:
	CChainStateNotify() : m_pOwner( 0 ), m_pfnNotify( 0 ) {}

	// Spelled as an assignment so IMPLEMENT_NETWORKVAR_CHAIN's `= this` still compiles.
	template < class T >
	CChainStateNotify &operator=( T *pOwner )
	{
		m_pOwner    = pOwner;
		m_pfnNotify = &Notify< T >;
		return *this;
	}

	bool IsBound() const { return m_pOwner != 0; }

	// Does nothing between the struct's constructor and the containing entity's.
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
