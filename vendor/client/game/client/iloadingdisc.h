//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <vgui/VGUI.h>

namespace vgui
{
class Panel;
}

abstract_class ILoadingDisc
{
public:
	virtual void Create( vgui::VPANEL parent ) = 0;
	virtual void Destroy() = 0;

	// loading disc
	virtual void SetLoadingVisible( bool bVisible ) = 0;

	// paused disc
	virtual void SetPausedVisible( bool bVisible, float flExpireTime = -1.f ) = 0;
};

extern ILoadingDisc *loadingdisc;
