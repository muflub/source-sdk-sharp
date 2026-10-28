// The engine-factory lookups the tier libraries' Connect functions make.

#pragma once

#include "tier1/interface.h"

#ifdef CLIENT_DLL
void SourceSharp_ConnectTier1( CreateInterfaceFn engineFactory );
void SourceSharp_DisconnectTier1();
void SourceSharp_ConnectTier2( CreateInterfaceFn engineFactory );
void SourceSharp_DisconnectTier2();
void SourceSharp_ConnectTier3( CreateInterfaceFn engineFactory );
void SourceSharp_DisconnectTier3();
#endif
