//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// A vgui panel without vgui_controls. The paint and hit traversals follow stock's
// Panel::PaintTraverse and Panel::IsWithinTraverse.

#include "vgui/IVGui.h"

#include "vgui/IScheme.h"
#include "tier1/KeyValues.h"
#include "vgui_controls/Controls.h"

#include "shell_rawpanel_client.h"

CSourceSharpRawPanel::CSourceSharpRawPanel( const char *pszName )
	: m_pszName( pszName )
	, m_hCursor( vgui::dc_arrow )
	, m_bPaint( true )
	, m_nScreenWide( -1 )
	, m_nScreenTall( -1 )
	, m_bFillScreen( false )
{
	m_hPanel = vgui::ivgui()->AllocPanel();
	vgui::ipanel()->Init( m_hPanel, this );
}

CSourceSharpRawPanel::~CSourceSharpRawPanel()
{
	// Out of the tree first, the children let go (none is ours to delete),
	// then vgui2's half freed.
	vgui::ipanel()->SetParent( m_hPanel, 0 );
	while ( vgui::ipanel()->GetChildCount( m_hPanel ) )
		vgui::ipanel()->SetParent( vgui::ipanel()->GetChild( m_hPanel, 0 ), 0 );
	vgui::ivgui()->FreePanel( m_hPanel );
}

void CSourceSharpRawPanel::SetParent( vgui::VPANEL parent )
{
	vgui::ipanel()->SetParent( m_hPanel, parent );
}

void CSourceSharpRawPanel::SetVisible( bool bVisible )
{
	vgui::ipanel()->SetVisible( m_hPanel, bVisible );
}

bool CSourceSharpRawPanel::IsVisible()
{
	return vgui::ipanel()->IsVisible( m_hPanel );
}

void CSourceSharpRawPanel::SetZPos( int z )
{
	vgui::ipanel()->SetZPos( m_hPanel, z );
}

void CSourceSharpRawPanel::SetMouseInputEnabled( bool b )
{
	vgui::ipanel()->SetMouseInputEnabled( m_hPanel, b );
}

void CSourceSharpRawPanel::SetKeyBoardInputEnabled( bool b )
{
	vgui::ipanel()->SetKeyBoardInputEnabled( m_hPanel, b );
}

bool CSourceSharpRawPanel::IsMouseInputEnabled()
{
	return vgui::ipanel()->IsMouseInputEnabled( m_hPanel );
}

void CSourceSharpRawPanel::GetSize( int &wide, int &tall )
{
	vgui::ipanel()->GetSize( m_hPanel, wide, tall );
}

void CSourceSharpRawPanel::SetBounds( int x, int y, int wide, int tall )
{
	vgui::ipanel()->SetPos( m_hPanel, x, y );
	vgui::ipanel()->SetSize( m_hPanel, wide, tall );
}

void CSourceSharpRawPanel::MakePopup( bool bShowTaskbarIcon, bool bDisabled )
{
	vgui::surface()->CreatePopup( m_hPanel, false, bShowTaskbarIcon, bDisabled, true, true );
}

// The panel covers the screen, re-sized when the screen changes.
void CSourceSharpRawPanel::FillScreen()
{
	m_bFillScreen = true;
	m_nScreenWide = m_nScreenTall = -1;
	ScreenSizeNow();
}

void CSourceSharpRawPanel::Think()
{
	if ( m_bFillScreen )
		ScreenSizeNow();
	OnThink();
}

// A screen size seen for the first time: a screen-filling panel takes it, the children are
// told, then the panel's hook. Reached from vgui2's message or the next think, whichever
// comes first.
void CSourceSharpRawPanel::ScreenSizeNow()
{
	int nWide, nTall;
	vgui::surface()->GetScreenSize( nWide, nTall );
	if ( nWide == m_nScreenWide && nTall == m_nScreenTall )
		return;

	const int nOldWide = m_nScreenWide, nOldTall = m_nScreenTall;
	m_nScreenWide = nWide;
	m_nScreenTall = nTall;
	if ( m_bFillScreen )
		SetBounds( 0, 0, nWide, nTall );
	if ( nOldWide < 0 )
		return;

	for ( int i = 0; i < vgui::ipanel()->GetChildCount( m_hPanel ); i++ )
	{
		vgui::ivgui()->PostMessage( vgui::ipanel()->GetChild( m_hPanel, i ),
									new KeyValues( "OnScreenSizeChanged", "oldwide", nOldWide, "oldtall", nOldTall ),
									m_hPanel );
	}
	OnScreenSizeChanged( nOldWide, nOldTall );
}

void CSourceSharpRawPanel::PaintTraverse( bool repaint, bool allowForce )
{
	if ( !IsVisible() )
		return;

	int nClip[4];
	vgui::ipanel()->GetClipRect( m_hPanel, nClip[0], nClip[1], nClip[2], nClip[3] );
	const bool bPaintSelf = repaint && m_bPaint && nClip[2] > nClip[0] && nClip[3] > nClip[1];
	if ( bPaintSelf )
	{
		vgui::surface()->PushMakeCurrent( m_hPanel, true );
		Paint();
		vgui::surface()->PopMakeCurrent( m_hPanel );
	}

	// The children, in order: a child the surface will not paint now is
	// invalidated and still traversed, painting nothing.
	CUtlVector<vgui::VPANEL> &children = vgui::ipanel()->GetChildren( m_hPanel );
	for ( int i = 0; i < children.Count(); i++ )
	{
		const vgui::VPANEL child = children[i];
		if ( !vgui::ipanel()->IsVisible( child ) )
			continue;
		if ( vgui::surface()->ShouldPaintChildPanel( child ) )
		{
			vgui::ipanel()->PaintTraverse( child, repaint, allowForce );
		}
		else
		{
			vgui::surface()->Invalidate( child );
			vgui::ipanel()->PaintTraverse( child, false, false );
		}
	}
}

void CSourceSharpRawPanel::Repaint()
{
	vgui::surface()->Invalidate( m_hPanel );
}

bool CSourceSharpRawPanel::IsWithin( int x, int y )
{
	int x0, y0, x1, y1;
	vgui::ipanel()->GetClipRect( m_hPanel, x0, y0, x1, y1 );
	return x >= x0 && y >= y0 && x < x1 && y < y1;
}

// The panel under (x, y): popups among the children first, then the other children, last
// drawn first, then this one. None when this panel is hidden or takes no mouse input.
vgui::VPANEL CSourceSharpRawPanel::IsWithinTraverse( int x, int y, bool traversePopups )
{
	if ( !IsVisible() || !IsMouseInputEnabled() )
		return 0;

	CUtlVector<vgui::VPANEL> &children = vgui::ipanel()->GetChildren( m_hPanel );
	if ( traversePopups )
	{
		for ( int i = children.Count() - 1; i >= 0; i-- )
		{
			if ( !vgui::ipanel()->IsPopup( children[i] ) )
				continue;
			if ( vgui::VPANEL hit = vgui::ipanel()->IsWithinTraverse( children[i], x, y, true ) )
				return hit;
		}
	}
	else if ( !IsWithin( x, y ) )
	{
		return 0;
	}

	for ( int i = children.Count() - 1; i >= 0; i-- )
	{
		if ( vgui::ipanel()->IsPopup( children[i] ) )
			continue;
		if ( vgui::VPANEL hit = vgui::ipanel()->IsWithinTraverse( children[i], x, y, traversePopups ) )
			return hit;
	}

	return IsWithin( x, y ) ? m_hPanel : 0;
}

void CSourceSharpRawPanel::GetInset( int &top, int &left, int &right, int &bottom )
{
	top = left = right = bottom = 0;
}

void CSourceSharpRawPanel::GetClipRect( int &x0, int &y0, int &x1, int &y1 )
{
	vgui::ipanel()->GetClipRect( m_hPanel, x0, y0, x1, y1 );
}

vgui::HScheme CSourceSharpRawPanel::GetScheme()
{
	return vgui::scheme()->GetDefaultScheme();
}

// vgui2's input messages, by the names its input sends.
void CSourceSharpRawPanel::OnMessage( const KeyValues *params, vgui::VPANEL ifromPanel )
{
	const char *pszName = params->GetName();
	KeyValues *p = const_cast<KeyValues *>( params );

	if ( !V_strcmp( pszName, "CursorMoved" ) )
	{
		if ( !IsMouseInputEnabled() )
			return;
		vgui::surface()->SetCursor( m_hCursor );
		OnCursorMoved( p->GetInt( "xpos" ), p->GetInt( "ypos" ) );
	}
	else if ( !V_strcmp( pszName, "MousePressed" ) )
		OnMousePressed( (vgui::MouseCode)p->GetInt( "code" ) );
	else if ( !V_strcmp( pszName, "MouseDoublePressed" ) )
		OnMouseDoublePressed( (vgui::MouseCode)p->GetInt( "code" ) );
	else if ( !V_strcmp( pszName, "MouseReleased" ) )
		OnMouseReleased( (vgui::MouseCode)p->GetInt( "code" ) );
	else if ( !V_strcmp( pszName, "MouseWheeled" ) )
		OnMouseWheeled( p->GetInt( "delta" ) );
	else if ( !V_strcmp( pszName, "KeyCodePressed" ) )
		OnKeyCodePressed( (vgui::KeyCode)p->GetInt( "code" ) );
	else if ( !V_strcmp( pszName, "KeyTyped" ) )
		OnKeyTyped( (wchar_t)p->GetInt( "unichar" ) );
	else if ( !V_strcmp( pszName, "KillFocus" ) )
		OnKillFocus();
	else if ( !V_strcmp( pszName, "OnScreenSizeChanged" ) )
		ScreenSizeNow();
}
