//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#include <vgui/VGUI.h>

namespace vgui
{
class Panel;
}

abstract_class IDebugOverlayPanel
{
public:
	virtual void Create(vgui::VPANEL parent) = 0;
	virtual void Destroy() = 0;
};

extern IDebugOverlayPanel* debugoverlaypanel;
