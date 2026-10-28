//========= SourceSharp — engine_utlvector_layout_oracle_test's stand-in =====//
//
// The pristine public/tier1/utlblockmemory.h does not compile under clang (it
// binds non-const references to bit-fields in a non-dependent swap). The
// pristine utlvector.h includes it only for CUtlBlockVector, which the layout
// oracle never instantiates, so a declaration is all it needs. Every other
// header the oracle reads is the clone's own.
//
//=============================================================================//

#pragma once
#define UTLBLOCKMEMORY_H

template <class T, class I>
class CUtlBlockMemory;
