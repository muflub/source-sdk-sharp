//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <vgui/VGUI.h>
#include <vgui/IPanel.h>

#include "vgui/MouseCode.h"
#include "vgui/KeyCode.h"
#include "tier3/tier3.h"

namespace vgui
{

class IPanel;
class IInput;
class ISchemeManager;
class ISurface;
class ISystem;
class IVGui;

// Backward compat interfaces, use the interfaces grabbed in tier3
// set of accessor functions to vgui interfaces
// the appropriate header file for each is listed above the item

// #include <vgui/IInput.h>
inline vgui::IInput *input()
{
	return g_pVGuiInput;
}

// #include <vgui/IScheme.h>
inline vgui::ISchemeManager *scheme()
{
	return g_pVGuiSchemeManager;
}

// #include <vgui/ISurface.h>
inline vgui::ISurface *surface()
{
	return g_pVGuiSurface;
}

// #include <vgui/ISystem.h>
inline vgui::ISystem *system()
{
	return g_pVGuiSystem;
}

// #include <vgui/IVGui.h>
inline vgui::IVGui *ivgui()
{
	return g_pVGui;
}

// #include <vgui/IPanel.h>
inline vgui::IPanel *ipanel()
{
	return g_pVGuiPanel;
}

class AnimationController;
class BuildModeDialog;
class Button;
class Label;
class Menu;
class Panel;
class BaseTooltip;

// vgui controls helper classes
class BuildGroup;
class IBorder;
class IImage;
class Image;
class TextImage;

} // namespace vgui

// hotkeys disabled until we work out exactly how we want to do them

//#define VGUI_DRAW_HOTKEYS_ENABLED
