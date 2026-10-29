#pragma once

#include "sourcesharp.h"

typedef void *( *CreateInterfaceFn )( const char *pName, int *pReturnCode );

// The engine's factories as this side stored them at DLL init: the app-system factory
// (which also serves the filesystem on the client), the physics factory and the
// filesystem factory.
struct SourceSharpFactories_t
{
	CreateInterfaceFn pAppSystem;
	CreateInterfaceFn pPhysics;
	CreateInterfaceFn pFileSystem;
};

// Fills sourcesharp.h's AcquireInterface field; see there for the contract. pFactories is
// NULL, or has no app-system factory, until this side has stored them.
void *SourceSharp_AcquireInterface( SourceSharpFactoryType eType, const char *pszVersion, char *pError,
									int32_t nErrorBytes, const SourceSharpFactories_t *pFactories );

// The shim layer's CreateInterfaceFn (SOURCESHARP_FACTORY_INTERNAL), defined per side.
// Returns NULL for a name this side does not serve.
void *SourceSharp_InternalFactory( const char *pszName );

// Never NULL; always ASCII with no '%'.
const char *SourceSharp_AcquireReason( SourceSharpAcquireReason eReason );

// Copies a reason into pOut, truncating. A NULL or zero-length buffer writes nothing.
void SourceSharp_SayAcquireReason( char *pOut, int32_t nBytes, SourceSharpAcquireReason eReason );
