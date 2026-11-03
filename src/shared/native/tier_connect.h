// The engine-factory lookups the tier libraries' Connect functions make.

#pragma once

#include "tier1/interface.h"

#ifdef CLIENT_DLL
class CClientEngineInterfaces;
void SourceSharp_ConnectTier2(CClientEngineInterfaces& engines, CreateInterfaceFn engineFactory);
void SourceSharp_DisconnectTier2(CClientEngineInterfaces& engines);
void SourceSharp_ConnectTier3(CClientEngineInterfaces& engines, CreateInterfaceFn engineFactory);
void SourceSharp_DisconnectTier3(CClientEngineInterfaces& engines);
#endif
