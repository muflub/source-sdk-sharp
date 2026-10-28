//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#include "interface.h"
#include "vgui/VGUI.h"
#include "eiface.h"

// Forward declarations.
namespace vgui
{
	class Panel;
};

enum VGuiPanel_t
{
	PANEL_ROOT = 0,
	PANEL_GAMEUIDLL,
	PANEL_CLIENTDLL,
	PANEL_TOOLS,
	PANEL_INGAMESCREENS,
	PANEL_GAMEDLL,
	PANEL_CLIENTDLL_TOOLS,
	PANEL_SIZING,
};

// In-game panels are cropped to the current engine viewport size
enum PaintMode_t
{
	PAINT_UIPANELS		= (1<<0),
	PAINT_INGAMEPANELS  = (1<<1),
	PAINT_CURSOR		= (1<<2), // software cursor, if appropriate
};

abstract_class IEngineVGui
{
public:
	virtual					~IEngineVGui( void ) { }

	virtual vgui::VPANEL	GetPanel( VGuiPanel_t type ) = 0;

	virtual bool			IsGameUIVisible() = 0;

	virtual void unused_GetWorkshopMap() = 0;
};

#define VENGINE_VGUI_VERSION		"VEngineVGui002"

extern IEngineVGui *enginevgui;
