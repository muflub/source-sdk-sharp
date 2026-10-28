//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — a vgui panel without vgui_controls =================//
//
// plan_client_novalve C3 step 4. vgui2 (the engine's vgui) holds panels as
// VPANELs and calls back into the module that made each one through
// vgui::IClientPanel. vgui_controls' Panel is one implementation of that
// interface, with a scheme, a build mode, message maps, focus navigation and a
// vtable of about two hundred methods; the client's own panels (the root, the
// HUD and the menu layer, the viewport) use none of it. This is the interface
// implemented directly: a panel that paints itself and its children as stock's
// Panel::PaintTraverse did, finds the panel under the cursor as stock's
// IsWithinTraverse did, and hands the input messages vgui2 sends it to virtual
// hooks by name. Everything else answers as a plain, scheme-less panel.
//=============================================================================//
#pragma once

#include "vgui/VGUI.h"
#include "vgui/IClientPanel.h"
#include "vgui/MouseCode.h"
#include "vgui/KeyCode.h"
#include "vgui/Cursor.h"
#include "tier0/platform.h"
#include "vgui/ISurface.h"

class KeyValues;

class CSourceSharpRawPanel : public vgui::IClientPanel
{
public:
	explicit CSourceSharpRawPanel( const char *pszName );
	virtual ~CSourceSharpRawPanel();

	// ---- the panel's own hooks ----
	virtual void Paint() {}
	virtual void OnThink() {}
	virtual void OnScreenSizeChanged( int nOldWide, int nOldTall ) {}
	virtual void OnCursorMoved( int x, int y ) {}
	virtual void OnMousePressed( vgui::MouseCode code ) {}
	virtual void OnMouseDoublePressed( vgui::MouseCode code ) {}
	virtual void OnMouseReleased( vgui::MouseCode code ) {}
	virtual void OnMouseWheeled( int delta ) {}
	virtual void OnKeyCodePressed( vgui::KeyCode code ) {}
	virtual void OnKeyTyped( wchar_t unichar ) {}
	virtual void OnKillFocus() {}

	// ---- what the client asks of it, onto vgui2's IPanel ----
	void SetParent( vgui::VPANEL parent );
	void SetVisible( bool bVisible );
	bool IsVisible();
	void SetBounds( int x, int y, int wide, int tall );
	void GetSize( int &wide, int &tall );
	void SetZPos( int z );
	void SetMouseInputEnabled( bool bEnabled );
	void SetKeyBoardInputEnabled( bool bEnabled );
	bool IsMouseInputEnabled();
	void SetPaintEnabled( bool bEnabled ) { m_bPaint = bEnabled; }
	void SetCursor( vgui::HCursor cursor ) { m_hCursor = cursor; }
	void MakePopup( bool bShowTaskbarIcon, bool bDisabled );
	void FillScreen();

	// ---- vgui::IClientPanel ----
	virtual vgui::VPANEL GetVPanel() OVERRIDE { return m_hPanel; }
	virtual void Think() OVERRIDE;
	virtual void PerformApplySchemeSettings() OVERRIDE {}
	virtual void PaintTraverse( bool forceRepaint, bool allowForce ) OVERRIDE;
	virtual void Repaint() OVERRIDE;
	virtual vgui::VPANEL IsWithinTraverse( int x, int y, bool traversePopups ) OVERRIDE;
	virtual void GetInset( int &top, int &left, int &right, int &bottom ) OVERRIDE;
	virtual void GetClipRect( int &x0, int &y0, int &x1, int &y1 ) OVERRIDE;
	virtual void OnChildAdded( vgui::VPANEL child ) OVERRIDE {}
	virtual void OnSizeChanged( int newWide, int newTall ) OVERRIDE {}
	virtual void InternalFocusChanged( bool lost ) OVERRIDE {}
	virtual bool RequestInfo( KeyValues *outputData ) OVERRIDE { return false; }
	virtual void RequestFocus( int direction ) OVERRIDE {}
	virtual bool RequestFocusPrev( vgui::VPANEL existingPanel ) OVERRIDE { return false; }
	virtual bool RequestFocusNext( vgui::VPANEL existingPanel ) OVERRIDE { return false; }
	virtual void OnMessage( const KeyValues *params, vgui::VPANEL ifromPanel ) OVERRIDE;
	virtual vgui::VPANEL GetCurrentKeyFocus() OVERRIDE { return 0; }
	virtual int GetTabPosition() OVERRIDE { return 0; }
	virtual const char *GetName() OVERRIDE { return m_pszName; }
	virtual const char *GetClassName() OVERRIDE { return "CSourceSharpRawPanel"; }
	virtual vgui::HScheme GetScheme() OVERRIDE;
	virtual bool IsProportional() OVERRIDE { return false; }
	virtual bool IsAutoDeleteSet() OVERRIDE { return false; }
	virtual void DeletePanel() OVERRIDE { delete this; }
	virtual void *QueryInterface( vgui::EInterfaceID id ) OVERRIDE { return NULL; }
	virtual vgui::Panel *GetPanel() OVERRIDE { return NULL; }
	virtual const char *GetModuleName() OVERRIDE { return "client"; }
	virtual void OnTick() OVERRIDE {}

private:
	bool IsWithin( int x, int y );
	void ScreenSizeNow();

	vgui::VPANEL m_hPanel;
	const char *m_pszName;
	vgui::HCursor m_hCursor;
	bool m_bPaint;
	int m_nScreenWide;
	int m_nScreenTall;
	bool m_bFillScreen;
};
