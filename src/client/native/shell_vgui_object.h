//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The client's vgui: its startup and shutdown, and its root panel. A member of the client DLL
// object.

#pragma once

#include "tier1/interface.h"
#include "vgui/VGUI.h"

class CClientEngineInterfaces;
class CSourceSharpRawPanel;

class CVguiClient final
{
public:
	explicit CVguiClient(const CClientEngineInterfaces& engines);

	CVguiClient(const CVguiClient&) = delete;
	CVguiClient& operator=(const CVguiClient&) = delete;

	bool Startup(CreateInterfaceFn appSystemFactory);
	void Shutdown();

	// The engine parents all client VGUI under this; 0 outside Startup and Shutdown.
	vgui::VPANEL RootPanel() const;

private:
	const CClientEngineInterfaces& m_Engines;
	CSourceSharpRawPanel* m_pRootPanel = nullptr;
};
