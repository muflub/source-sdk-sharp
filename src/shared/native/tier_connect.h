//========= SourceSharp — the engine interfaces the tier libraries connected ==//
//
// plan_server_novalve.md P3: see tier_connect.cpp.
//
//=============================================================================//

#ifndef TIER_CONNECT_H
#define TIER_CONNECT_H
#ifdef _WIN32
#pragma once
#endif

#include "tier1/interface.h"

void SourceSharp_ConnectTier1( CreateInterfaceFn engineFactory );
void SourceSharp_DisconnectTier1();
#ifdef CLIENT_DLL
void SourceSharp_ConnectTier2( CreateInterfaceFn engineFactory );
void SourceSharp_DisconnectTier2();
#endif
void SourceSharp_ConnectTier3( CreateInterfaceFn engineFactory );
void SourceSharp_DisconnectTier3();

#endif // TIER_CONNECT_H
