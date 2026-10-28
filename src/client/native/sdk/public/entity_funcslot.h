// Stores a member function in typedescription_t::inputFunc with the class
// conversion checked and only the signature reinterpreted; a C-style cast
// silently becomes reinterpret_cast when the class relationship breaks. Assumes
// single inheritance (SINGLE_INHERITANCE).
#pragma once

// Stores a member function of OWNER in a SLOT typed as a member of ROOT.
//   ROOT   the class the slot's type names        (CBaseEntity)
//   SLOT   the slot's type                        (inputfunc_t)
//   OWNER  the class whose datadesc this is       (classNameTypedef)
//   FUNC   deduced from the argument              (BASEPTR, USEPTR, ...)
template < typename ROOT, typename SLOT, typename OWNER, typename FUNC >
inline SLOT EntityFuncSlot( FUNC pfn )
{
	typedef void (ROOT::*RootProbe_t)( void );
	typedef void (OWNER::*OwnerProbe_t)( void );

	// The class half, checked: the same conversion on a null member pointer fails
	// when OWNER is not unambiguously, accessibly and non-virtually derived from ROOT.
	OwnerProbe_t pOwnerProbe = 0;
	RootProbe_t pRootProbe = static_cast< RootProbe_t >( pOwnerProbe );
	(void)pRootProbe;

	static_assert( sizeof( FUNC ) == sizeof( SLOT ),
		"member function pointer does not fit the datadesc slot" );

	// The signature half, punned between two members of the same class; never called.
	return reinterpret_cast< SLOT >( pfn );
}
