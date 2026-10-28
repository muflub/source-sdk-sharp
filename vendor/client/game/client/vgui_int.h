//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#include "interface.h"

#include <vgui/VGUI.h>

namespace vgui
{
class Panel;
}

bool VGui_Startup( CreateInterfaceFn appSystemFactory );
void VGui_Shutdown();
void VGui_CreateGlobalPanels();
vgui::VPANEL VGui_GetClientDLLRootPanel();
void VGUI_CreateClientDLLRootPanel();
void VGUI_DestroyClientDLLRootPanel();
void VGui_PreRender();
void VGui_PostRender();
