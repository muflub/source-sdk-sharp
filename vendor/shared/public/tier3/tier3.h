//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: A higher level link library for general use in the game and tools.

#pragma once

#include "tier2/tier2.h"

// Forward declarations
class IStudioRender;
class IMatSystemSurface;
class IMDLCache;
class IPhysicsCollision;
class ISoundEmitterSystemBase;

namespace vgui
{
class ISurface;
class IVGui;
class IInput;
class IPanel;
class ILocalize;
class ISchemeManager;
class ISystem;
}

extern IStudioRender* studiorender;
extern IMatSystemSurface* g_pMatSystemSurface;
extern vgui::ISurface* g_pVGuiSurface;
extern vgui::IInput* g_pVGuiInput;
extern vgui::IVGui* g_pVGui;
extern vgui::IPanel* g_pVGuiPanel;
extern vgui::ILocalize* g_pVGuiLocalize;
extern vgui::ISchemeManager* g_pVGuiSchemeManager;
extern vgui::ISystem* g_pVGuiSystem;
extern IMDLCache* mdlcache;

// Call this to connect to/disconnect from all tier 3 libraries.
// It's up to the caller to check the globals it cares about to see if ones are missing
void ConnectTier3Libraries(CreateInterfaceFn* pFactoryList, int nFactoryCount);
void DisconnectTier3Libraries();

// Helper empty implementation of an IAppSystem for tier2 libraries
template <class IInterface, int ConVarFlag = 0>
class CTier3AppSystem : public CTier2AppSystem<IInterface, ConVarFlag>
{
	typedef CTier2AppSystem<IInterface, ConVarFlag> BaseClass;

public:
	CTier3AppSystem(bool bIsPrimaryAppSystem = true)
		: BaseClass(bIsPrimaryAppSystem)
	{
	}

	virtual bool Connect(CreateInterfaceFn factory)
	{
		if (!BaseClass::Connect(factory))
			return false;

		if (BaseClass::IsPrimaryAppSystem())
		{
			ConnectTier3Libraries(&factory, 1);
		}
		return true;
	}

	virtual void Disconnect()
	{
		if (BaseClass::IsPrimaryAppSystem())
		{
			DisconnectTier3Libraries();
		}
		BaseClass::Disconnect();
	}
};
