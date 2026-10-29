//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

namespace vgui
{
class Panel;
}

abstract_class IFPSPanel
{
public:
	virtual void Create(vgui::VPANEL parent) = 0;
	virtual void Destroy() = 0;
};

extern IFPSPanel* fps;
