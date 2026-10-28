//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The managed UI decides the loading and paused plaques; this object only
// answers vgui_int's calls.

#include "iloadingdisc.h"
#include "tier0/platform.h"
#include "vgui/VGUI.h"

class CSourceSharpLoadingDisc : public ILoadingDisc
{
public:
	virtual void Create( vgui::VPANEL parent ) OVERRIDE {}
	virtual void Destroy() OVERRIDE {}

	virtual void SetLoadingVisible( bool bVisible ) OVERRIDE {}
	virtual void SetPausedVisible( bool bVisible, float flExpireTime ) OVERRIDE {}
};

static CSourceSharpLoadingDisc g_LoadingDisc;
ILoadingDisc *loadingdisc = &g_LoadingDisc;
