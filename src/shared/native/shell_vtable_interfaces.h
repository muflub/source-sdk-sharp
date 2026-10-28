#pragma once

#include "sourcesharp.h"

// Fills sourcesharp.h's AcquireInterface field; see there for the contract.
void *SourceSharp_AcquireInterface( SourceSharpFactoryType eType, const char *pszVersion, char *pError,
									int32_t nErrorBytes );

// The shim layer's CreateInterfaceFn (SOURCESHARP_FACTORY_INTERNAL), defined per side.
// Returns NULL for a name this side does not serve.
void *SourceSharp_InternalFactory( const char *pszName );

// Never NULL; always ASCII with no '%'.
const char *SourceSharp_AcquireReason( SourceSharpAcquireReason eReason );

// Copies a reason into pOut, truncating. A NULL or zero-length buffer writes nothing.
void SourceSharp_SayAcquireReason( char *pOut, int32_t nBytes, SourceSharpAcquireReason eReason );
