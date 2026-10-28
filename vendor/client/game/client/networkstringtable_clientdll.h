//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "networkstringtabledefs.h"

extern INetworkStringTableContainer *networkstringtable;

// String tables used by the client DLL	
// (see InstallStringTableCallback for where they're initialized)
extern INetworkStringTable *g_StringTableVguiScreen;
extern INetworkStringTable *g_StringTableEffectDispatch;
extern INetworkStringTable *g_pStringTableInfoPanel;
extern INetworkStringTable *g_pStringTableClientSideChoreoScenes;
extern INetworkStringTable *g_pStringTableServerMapCycle;
