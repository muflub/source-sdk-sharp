//========= SourceSharp — the acquisition entry point, declared ===============//
//
// Three declarations and nothing else. The bodies are in
// shell_vtable_interfaces.cpp; this header exists so that the fill in
// sourcesharp_gamesystem.cpp and the gtest that walks the reason set can both
// see the same signatures rather than each restating them.
//
// WHY THE REASON SET IS REACHABLE FROM A TEST AT ALL. The contract says the
// four failure literals are ASCII and carry no format specifier, and that is a
// claim about a SET rather than about a sample. A test that spot-checked one
// literal would pass while a fifth arrived carrying "%s"; walking the enum
// cannot. The enum itself lives in sourcesharp.h, beside the factory type,
// because both sides of the boundary read it.
//
//=============================================================================//

#ifndef SHELL_VTABLE_INTERFACES_H
#define SHELL_VTABLE_INTERFACES_H
#ifdef _WIN32
#pragma once
#endif

#include "sourcesharp.h"

//-----------------------------------------------------------------------------
// The one acquisition entry point. See sourcesharp.h's AcquireInterface field
// for the contract; this is the function that fills it.
//-----------------------------------------------------------------------------
void *SourceSharp_AcquireInterface( SourceSharpFactoryType eType, const char *pszVersion,
                                    char *pError, int32_t nErrorBytes );

//-----------------------------------------------------------------------------
// The shim layer's own CreateInterfaceFn -- the SOURCESHARP_FACTORY_INTERNAL
// arm. Defined per side in shell_internalfactory.cpp; answers NULL for a name
// this side does not serve. No count comes back: managed calls what it gets
// through a forwarder table, whose header holds its own
// (plan_forwarders_everywhere.md).
//-----------------------------------------------------------------------------
void *SourceSharp_InternalFactory( const char *pszName );

//-----------------------------------------------------------------------------
// One reason's literal. Never NULL, and never anything but ASCII with no '%'.
//-----------------------------------------------------------------------------
const char *SourceSharp_AcquireReason( SourceSharpAcquireReason eReason );

//-----------------------------------------------------------------------------
// Copy a reason into the caller's buffer, TRUNCATING rather than refusing.
//
// A NULL or zero-length buffer is legal and writes nothing: a diagnostic path
// may not want a reason at all. Out of line so that every failure arm of the
// acquire is one line, and so that the truncation has exactly one
// implementation for a test to drive.
//-----------------------------------------------------------------------------
void SourceSharp_SayAcquireReason( char *pOut, int32_t nBytes,
                                   SourceSharpAcquireReason eReason );

#endif // SHELL_VTABLE_INTERFACES_H
