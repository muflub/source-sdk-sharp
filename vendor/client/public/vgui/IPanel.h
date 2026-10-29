//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <vgui/VGUI.h>
#include "tier1/interface.h"
#include "tier1/utlvector.h"

#ifdef SendMessage
#undef SendMessage
#endif

class KeyValues;

namespace vgui
{

class IClientPanel;

//!! must be removed
class Panel;

// Purpose: interface from Client panels -> vgui panels
class IPanel : public IBaseInterface
{
public:
	virtual void Init( VPANEL vguiPanel, IClientPanel *panel ) = 0;

	// methods
	virtual void SetPos( VPANEL vguiPanel, int x, int y ) = 0;
	virtual void unused_GetPos() = 0;
	virtual void SetSize( VPANEL vguiPanel, int wide, int tall ) = 0;
	virtual void GetSize( VPANEL vguiPanel, int &wide, int &tall ) = 0;
	virtual void unused_SetMinimumSize() = 0;
	virtual void unused_GetMinimumSize() = 0;
	virtual void SetZPos( VPANEL vguiPanel, int z ) = 0;
	virtual void unused_GetZPos() = 0;

	virtual void unused_GetAbsPos() = 0;
	virtual void GetClipRect( VPANEL vguiPanel, int &x0, int &y0, int &x1, int &y1 ) = 0;
	virtual void unused_SetInset() = 0;
	virtual void GetInset( VPANEL vguiPanel, int &left, int &top, int &right, int &bottom ) = 0;

	virtual void SetVisible( VPANEL vguiPanel, bool state ) = 0;
	virtual bool IsVisible( VPANEL vguiPanel ) = 0;
	virtual void SetParent( VPANEL vguiPanel, VPANEL newParent ) = 0;
	virtual int GetChildCount( VPANEL vguiPanel ) = 0;
	virtual VPANEL GetChild( VPANEL vguiPanel, int index ) = 0;
	virtual CUtlVector<VPANEL> &GetChildren( VPANEL vguiPanel ) = 0;
	virtual VPANEL GetParent( VPANEL vguiPanel ) = 0;
	virtual void unused_MoveToFront() = 0;
	virtual void unused_MoveToBack() = 0;
	virtual void unused_HasParent() = 0;
	virtual bool IsPopup( VPANEL vguiPanel ) = 0;
	virtual void unused_SetPopup() = 0;
	virtual void unused_IsFullyVisible() = 0;

	// gets the scheme this panel uses
	virtual HScheme GetScheme( VPANEL vguiPanel ) = 0;
	// gets whether or not this panel should scale with screen resolution
	virtual void unused_IsProportional() = 0;
	// returns true if auto-deletion flag is set
	virtual void unused_IsAutoDeleteSet() = 0;
	// deletes the Panel * associated with the vpanel
	virtual void unused_DeletePanel() = 0;

	// input interest
	virtual void SetKeyBoardInputEnabled( VPANEL vguiPanel, bool state ) = 0;
	virtual void SetMouseInputEnabled( VPANEL vguiPanel, bool state ) = 0;
	virtual void unused_IsKeyBoardInputEnabled() = 0;
	virtual bool IsMouseInputEnabled( VPANEL vguiPanel ) = 0;

	// calculates the panels current position within the hierarchy
	virtual void unused_Solve() = 0;

	// gets names of the object (for debugging purposes)
	virtual const char *GetName( VPANEL vguiPanel ) = 0;
	virtual const char *GetClassName( VPANEL vguiPanel ) = 0;

	// delivers a message to the panel
	virtual void unused_SendMessage() = 0;

	// these pass through to the IClientPanel
	virtual void Think( VPANEL vguiPanel ) = 0;
	virtual void unused_PerformApplySchemeSettings() = 0;
	virtual void PaintTraverse( VPANEL vguiPanel, bool forceRepaint, bool allowForce = true ) = 0;
	virtual void Repaint( VPANEL vguiPanel ) = 0;
	virtual VPANEL IsWithinTraverse( VPANEL vguiPanel, int x, int y, bool traversePopups ) = 0;
	virtual void unused_OnChildAdded() = 0;
	virtual void unused_OnSizeChanged() = 0;

	virtual void unused_InternalFocusChanged() = 0;
	virtual void unused_RequestInfo() = 0;
	virtual void unused_RequestFocus() = 0;
	virtual void unused_RequestFocusPrev() = 0;
	virtual void unused_RequestFocusNext() = 0;
	virtual void unused_GetCurrentKeyFocus() = 0;
	virtual void unused_GetTabPosition() = 0;

	// used by ISurface to store platform-specific data
	virtual void unused_Plat() = 0;
	virtual void unused_SetPlat() = 0;

	// returns a pointer to the vgui controls baseclass Panel *
	// destinationModule needs to be passed in to verify that the returned Panel * is from the same module
	// it must be from the same module since Panel * vtbl may be different in each module
	virtual Panel *GetPanel( VPANEL vguiPanel, const char *destinationModule ) = 0;

	virtual void unused_IsEnabled() = 0;
	virtual void unused_SetEnabled() = 0;

	// Used by the drag/drop manager to always draw on top
	virtual void unused_IsTopmostPopup() = 0;
	virtual void unused_SetTopmostPopup() = 0;

	// sibling pins
	virtual void unused_SetSiblingPin() = 0;
};

#define VGUI_PANEL_INTERFACE_VERSION "VGUI_Panel009"

} // namespace vgui
