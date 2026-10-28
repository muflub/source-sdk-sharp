//========= SourceSharp — the engine interfaces the tier libraries connected ==//
//
// plan_server_novalve.md P3: see tier_connect.cpp.
//
//=============================================================================//

#pragma once

#include "tier1/interface.h"

void SourceSharp_ConnectTier1( CreateInterfaceFn engineFactory );
void SourceSharp_DisconnectTier1();
#ifdef CLIENT_DLL
void SourceSharp_ConnectTier2( CreateInterfaceFn engineFactory );
void SourceSharp_DisconnectTier2();
#endif
void SourceSharp_ConnectTier3( CreateInterfaceFn engineFactory );
void SourceSharp_DisconnectTier3();
