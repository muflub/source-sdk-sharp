//========= SourceSharp ======================================================//
//
// Putting a member function into typedescription_t::inputFunc without a
// C-style cast doing two jobs at once.
//
// WHAT THE SLOT IS
//
// datamap.h declares one slot, `inputfunc_t inputFunc`, and two unrelated
// families of macro write to it.
//
//   DEFINE_INPUTFUNC fills it with something that IS an inputfunc_t and IS
//   called through it: CBaseEntity::AcceptInput walks the datamap, and inside
//   `if ( dataDesc[i].flags & FTYPEDESC_INPUT )` does `(this->*pfnInput)(data)`.
//   That macro static_casts, which checks both the class and the signature, and
//   it is left exactly as it was.
//
//   DEFINE_FUNCTION / _RAW / THINKFUNC / ENTITYFUNC / USEFUNC fill it with a
//   Think, Touch, Use or Blocked handler whose signature is nothing like
//   inputfunc_t's, and it is NEVER called through. Those entries are flagged
//   FTYPEDESC_FUNCTIONTABLE, never FTYPEDESC_INPUT, so AcceptInput's arm cannot
//   reach them. The only code that reads them is saverestore.cpp's
//   UTIL_FunctionToName / UTIL_FunctionFromName, and all either does is compare
//   the slot's FIRST WORD (EXTRACT_INPUTFUNC_FUNCTIONPTR) to turn a saved
//   function name back into a pointer. The slot is storage there, not a
//   callable, and the signature always had to be punned.
//
// WHY ONE CAST WAS THE WRONG NUMBER
//
// The old spelling was `(inputfunc_t)((func_type)(&classNameTypedef::function))`
// and it did two different things in one breath:
//
//   the CLASS conversion   D::*  ->  CBaseEntity::*     -- must be CHECKED
//   the SIGNATURE pun      (void)  ->  (inputdata_t &)  -- must be REINTERPRETED
//
// A C-style cast picks whichever it can: static_cast while the class
// relationship holds, and reinterpret_cast the moment it stops holding. It
// does not say which one it picked. So the day CBaseEntity stops being the base
// of classNameTypedef, every one of these sites keeps compiling and quietly
// starts storing a pointer-to-member of an unrelated class -- which is not a
// wrong address so much as a value whose meaning is gone: the Itanium
// representation is { pointer-or-vtable-offset, this-adjustment } and the
// adjustment stored is the one for a subobject that no longer exists. On MSVC
// it is worse than meaningless, because the representation is not even the same
// SIZE for a class the compiler was not told is singly-inherited, and the cast
// truncates.
//
// That day is Phase 13 step 4, when CBaseEntity becomes IGameEntity. This
// splits the two jobs so that the class half cannot degrade quietly: a static
// probe conversion that is the very conversion being punned, and then an
// explicit reinterpret_cast between two members of the SAME class, which is the
// part that was always fine.
//
// WHAT IS DELIBERATELY NOT CHECKED HERE
//
// `&D::f` where f was declared in a base B yields a `void (B::*)(...)`, not a
// `void (D::*)(...)`, and the reinterpret below keeps B's zero adjustment. That
// is only wrong if B is a SECONDARY base of D at a non-zero offset, and nothing
// in this tree is: DECLARE_CLASS gives every entity exactly one BaseClass, and
// datamap.h's `class SINGLE_INHERITANCE CBaseEntity;` plus baseentity.h's
// ENTITYFUNCPTR_SIZE assertions are already built on that invariant. Step 4
// does not change it -- IGameEntity becomes the single root, not a second one.
//
//=============================================================================//
#pragma once

//-----------------------------------------------------------------------------
// Store a member function of OWNER in a SLOT typed as a member of ROOT.
//
//   ROOT   the class the slot's type names        (CBaseEntity)
//   SLOT   the slot's type                        (inputfunc_t)
//   OWNER  the class whose datadesc this is       (classNameTypedef)
//   FUNC   deduced from the argument              (BASEPTR, USEPTR, ...)
//
// Nothing here survives optimisation: the probe is dead, the assert is
// compile-time, and the reinterpret_cast is a relabelling. What survives is the
// diagnostic.
//-----------------------------------------------------------------------------
template < typename ROOT, typename SLOT, typename OWNER, typename FUNC >
inline SLOT EntityFuncSlot( FUNC pfn )
{
	typedef void (ROOT::*RootProbe_t)( void );
	typedef void (OWNER::*OwnerProbe_t)( void );

	// THE CLASS HALF, checked. This is not a stand-in for the conversion the
	// old cast was making -- it IS that conversion, on a null member pointer,
	// so it fails for exactly the cases the pun is meaningless for: OWNER not
	// derived from ROOT, an ambiguous ROOT, an inaccessible ROOT, and a virtual
	// ROOT (where a member pointer cannot be converted at all).
	OwnerProbe_t pOwnerProbe = 0;
	RootProbe_t pRootProbe = static_cast< RootProbe_t >( pOwnerProbe );
	(void)pRootProbe;

	// And the two have to be the same shape before one can stand in for the
	// other. On this build both are 16 bytes; on a compiler that sizes member
	// pointers by inheritance model they need not be, and a C-style cast would
	// truncate rather than complain.
	static_assert( sizeof( FUNC ) == sizeof( SLOT ),
		"member function pointer does not fit the datadesc slot" );

	// THE SIGNATURE HALF, punned -- deliberately, and only between two members
	// of the same class. Read back by name, never called.
	return reinterpret_cast< SLOT >( pfn );
}
