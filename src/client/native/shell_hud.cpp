//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The UI's VGUI root panels and draw primitives. The HUD root does no layout and
// holds no state: once a frame it hands the managed UI the screen size, and the
// UI draws through IShimUiClient.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "hudelement.h"
#include "vgui_controls/Controls.h"
#include "vgui/ISurface.h"
#include "vgui/ILocalize.h"
#include "vgui/Cursor.h"
#include "VGuiMatSurface/IMatSystemSurface.h"
#include "tier3/tier3.h"
#include "client_textmessage.h"
#include "engine/ishadowmgr.h"
#include "mathlib/vector2d.h"
#include "tier0/commonmacros.h"
#include "tier0/platform.h"
#include "tier1/ilocalize.h"
#include "tier1/strtools.h"
#include "vgui/KeyCode.h"
#include "vgui/MouseCode.h"
#include "vgui/VGUI.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "cdll_util.h"
#include "eiface.h"
#include "hud_element_helper.h"
#include "shareddefs.h"
#include "shell_standalone_client.h"
#include "shell_rawpanel_client.h"
#include "sourcesharp_entrypoints.h"
#include "shell_diagnostics.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"

using namespace vgui;

// IShimUiClient's draw primitives, one vgui::ISurface call each. Colours arrive
// packed 0xRRGGBBAA; text arrives as UTF-16 and is widened to wchar_t. Draws are
// refused outside the root panel's paint window, since a draw from elsewhere
// would corrupt VGUI's state; measuring, fonts and textures work at any time.

namespace
{
	// Open only while a root panel's Paint calls IManagedUiClient::Paint; holds the
	// clip that panel's traversal set, restored when managed code turns its clip
	// off and when the window closes.
	struct UiPaintWindow
	{
		bool bOpen;
		bool bPanelClipDisabled;
		int  nPanelClip[ 4 ];
		CSourceSharpRawPanel *pMenuRoot;   // the menu layer's root panel, once made
	};
	UiPaintWindow g_UiPaint;

	// One draw's text, widened; longer runs are cut.
	const int kMaxText = 512;

	// One polygon's vertices; the toolkit's corners use well under this.
	const int kMaxVertices = 256;

	void SetColor( uint32_t nRgba )
	{
		surface()->DrawSetColor( ( nRgba >> 24 ) & 0xFF, ( nRgba >> 16 ) & 0xFF, ( nRgba >> 8 ) & 0xFF, nRgba & 0xFF );
	}

	int Widen( const uint16_t *pText, int32_t nLength, wchar_t *pOut, int nOut )
	{
		if ( !pText || nLength <= 0 )
			return 0;

		const int n = nLength < nOut - 1 ? nLength : nOut - 1;
		for ( int i = 0; i < n; i++ )
			pOut[ i ] = (wchar_t)pText[ i ];
		pOut[ n ] = 0;
		return n;
	}
}

static void ShellUi_BeginPaint()
{
	g_UiPaint.bOpen = true;
	if ( g_pMatSystemSurface )
		g_pMatSystemSurface->GetClippingRect( g_UiPaint.nPanelClip[ 0 ], g_UiPaint.nPanelClip[ 1 ], g_UiPaint.nPanelClip[ 2 ],
		                                      g_UiPaint.nPanelClip[ 3 ], g_UiPaint.bPanelClipDisabled );
}

static void ShellUi_EndPaint()
{
	SourceSharp_UiSetClip( 0, 0, 0, 0, 0 );
	g_UiPaint.bOpen = false;
}

void SourceSharp_UiDrawFilledRect( int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba )
{
	if ( !g_UiPaint.bOpen || x1 <= x0 || y1 <= y0 )
		return;

	SetColor( nRgba );
	surface()->DrawFilledRect( x0, y0, x1, y1 );
}

void SourceSharp_UiDrawFilledRectFade( int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba,
                                       int32_t nAlpha0, int32_t nAlpha1, int32_t bHorizontal )
{
	if ( !g_UiPaint.bOpen || x1 <= x0 || y1 <= y0 )
		return;

	SetColor( nRgba );
	surface()->DrawFilledRectFade( x0, y0, x1, y1, (unsigned int)nAlpha0, (unsigned int)nAlpha1, bHorizontal != 0 );
}

void SourceSharp_UiDrawOutlinedRect( int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba )
{
	if ( !g_UiPaint.bOpen || x1 <= x0 || y1 <= y0 )
		return;

	SetColor( nRgba );
	surface()->DrawOutlinedRect( x0, y0, x1, y1 );
}

void SourceSharp_UiDrawLine( int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba )
{
	if ( !g_UiPaint.bOpen )
		return;

	SetColor( nRgba );
	surface()->DrawLine( x0, y0, x1, y1 );
}

void SourceSharp_UiDrawTexturedRect( int32_t nTexture, int32_t x0, int32_t y0, int32_t x1, int32_t y1,
                                     float s0, float t0, float s1, float t1, uint32_t nRgba )
{
	if ( !g_UiPaint.bOpen || x1 <= x0 || y1 <= y0 || !surface()->IsTextureIDValid( nTexture ) )
		return;

	SetColor( nRgba );
	surface()->DrawSetTexture( nTexture );
	surface()->DrawTexturedSubRect( x0, y0, x1, y1, s0, t0, s1, t1 );
}

void SourceSharp_UiDrawPolygon( int32_t nTexture, const float *pXyuv, int32_t nVertices, uint32_t nRgba )
{
	if ( !g_UiPaint.bOpen || !pXyuv || nVertices < 3 || nVertices > kMaxVertices
	     || !surface()->IsTextureIDValid( nTexture ) )
		return;

	Vertex_t verts[ kMaxVertices ];
	for ( int i = 0; i < nVertices; i++ )
		verts[ i ].Init( Vector2D( pXyuv[ i * 4 + 0 ], pXyuv[ i * 4 + 1 ] ),
		                 Vector2D( pXyuv[ i * 4 + 2 ], pXyuv[ i * 4 + 3 ] ) );

	SetColor( nRgba );
	surface()->DrawSetTexture( nTexture );
	surface()->DrawTexturedPolygon( nVertices, verts );
}

void SourceSharp_UiDrawText( int32_t nFont, int32_t x, int32_t y, const uint16_t *pText, int32_t nLength,
                             uint32_t nRgba )
{
	if ( !g_UiPaint.bOpen || nFont == INVALID_FONT )
		return;

	wchar_t wszText[ kMaxText ];
	const int n = Widen( pText, nLength, wszText, kMaxText );
	if ( n <= 0 )
		return;

	surface()->DrawSetTextFont( (HFont)nFont );
	surface()->DrawSetTextColor( ( nRgba >> 24 ) & 0xFF, ( nRgba >> 16 ) & 0xFF, ( nRgba >> 8 ) & 0xFF, nRgba & 0xFF );
	surface()->DrawSetTextPos( x, y );
	surface()->DrawPrintText( wszText, n );
}

void SourceSharp_UiMeasureText( int32_t nFont, const uint16_t *pText, int32_t nLength, int32_t *pOutWidth,
                                int32_t *pOutHeight )
{
	if ( pOutWidth )  *pOutWidth = 0;
	if ( pOutHeight ) *pOutHeight = 0;
	if ( nFont == INVALID_FONT )
		return;

	wchar_t wszText[ kMaxText ];
	if ( Widen( pText, nLength, wszText, kMaxText ) <= 0 )
		return;

	int nWide = 0, nTall = 0;
	surface()->GetTextSize( (HFont)nFont, wszText, nWide, nTall );
	if ( pOutWidth )  *pOutWidth = nWide;
	if ( pOutHeight ) *pOutHeight = nTall;
}

void SourceSharp_UiSetClip( int32_t bEnabled, int32_t x0, int32_t y0, int32_t x1, int32_t y1 )
{
	if ( !g_UiPaint.bOpen || !g_pMatSystemSurface )
		return;

	if ( bEnabled )
	{
		g_pMatSystemSurface->DisableClipping( false );
		g_pMatSystemSurface->SetClippingRect( x0, y0, x1, y1 );
		return;
	}

	g_pMatSystemSurface->SetClippingRect( g_UiPaint.nPanelClip[ 0 ], g_UiPaint.nPanelClip[ 1 ], g_UiPaint.nPanelClip[ 2 ], g_UiPaint.nPanelClip[ 3 ] );
	g_pMatSystemSurface->DisableClipping( g_UiPaint.bPanelClipDisabled );
}

int32_t SourceSharp_UiTextureCreate()
{
	return surface()->CreateNewTextureID( true );
}

void SourceSharp_UiTextureUpload( int32_t nTexture, const uint8_t *pRgba, int32_t nWide, int32_t nTall )
{
	if ( !pRgba || nWide <= 0 || nTall <= 0 )
		return;

	surface()->DrawSetTextureRGBA( nTexture, pRgba, nWide, nTall, 1, true );
}

int32_t SourceSharp_UiFontAddFile( const char *pPath )
{
	if ( !pPath || !pPath[ 0 ] )
		return 0;

	return surface()->AddCustomFontFile( pPath, pPath ) ? 1 : 0;
}

int32_t SourceSharp_UiFontCreate( const char *pFamily, int32_t nTall, int32_t nWeight, int32_t nFlags )
{
	if ( !pFamily || !pFamily[ 0 ] || nTall <= 0 )
		return INVALID_FONT;

	HFont hFont = surface()->CreateFont();
	if ( hFont == INVALID_FONT )
		return INVALID_FONT;

	if ( !surface()->SetFontGlyphSet( hFont, pFamily, nTall, nWeight, 0, 0, nFlags ) )
		return INVALID_FONT;

	return (int32_t)hFont;
}

void SourceSharp_UiFontMetrics( int32_t nFont, int32_t *pOutTall, int32_t *pOutAscent )
{
	if ( pOutTall )   *pOutTall = 0;
	if ( pOutAscent ) *pOutAscent = 0;
	if ( nFont == INVALID_FONT )
		return;

	if ( pOutTall )   *pOutTall = surface()->GetFontTall( (HFont)nFont );
	if ( pOutAscent ) *pOutAscent = surface()->GetFontAscent( (HFont)nFont, L'M' );
}

int32_t SourceSharp_UiFontAdvances( int32_t nFont, int32_t nFirst, int32_t nCount, int32_t *pOutAdvances )
{
	if ( nFont == INVALID_FONT || !pOutAdvances || nCount <= 0 )
		return 0;

	for ( int i = 0; i < nCount; i++ )
	{
		int a = 0, b = 0, c = 0;
		surface()->GetCharABCwide( (HFont)nFont, nFirst + i, a, b, c );
		pOutAdvances[ i ] = a + b + c;
	}
	return nCount;
}

void SourceSharp_UiPlaySound( const char *pPath )
{
	if ( pPath && pPath[ 0 ] )
		surface()->PlaySound( pPath );
}

// The engine's entity index for this client, 0 when there is no local player.
int32_t SourceSharp_GetLocalPlayerIndex()
{
	const int nIndex = engine->GetLocalPlayer();
	return nIndex > 0 ? nIndex : 0;
}

int32_t SourceSharp_GetLocalPlayerHealth()
{
	// The health the managed player publishes each frame; -1 when there is no
	// local player, so a HUD can tell "no player" from "dead".
	if ( C_StandaloneEntity *pShell = SourceSharp_LocalPlayerShell() )
		return pShell->PredictedHealth();

	return -1;
}

// player_info_t as managed code reads it.
COMPILE_TIME_ASSERT( sizeof( player_info_t ) == 132 );
COMPILE_TIME_ASSERT( offsetof( player_info_t, userID ) == 32 );
COMPILE_TIME_ASSERT( offsetof( player_info_t, guid ) == 36 );
COMPILE_TIME_ASSERT( offsetof( player_info_t, fakeplayer ) == 108 );

// The UI's menu layer: a full-screen popup that paints layer 1 and forwards
// input to IManagedUiClient::Input. It takes the mouse and keyboard only when
// managed code asks; otherwise it is transparent to input.
vgui::VPANEL SourceSharp_ClientViewportPanel();

class CSourceSharpMenuRoot : public CSourceSharpRawPanel
{
public:
	// Managed code's UiInputKind values, in its order.
	enum { MouseMove, MouseDown, MouseUp, Wheel, Key, Char, FocusLost };

	explicit CSourceSharpMenuRoot( vgui::VPANEL parent )
		: CSourceSharpRawPanel( "SourceSharpMenuRoot" )
	{
		SetParent( parent );
		// A popup: the surface counts a popup's input towards whether the cursor
		// shows, and a plain child would never show it.
		MakePopup( false, false );
		SetMouseInputEnabled( false );
		SetKeyBoardInputEnabled( false );
		SetVisible( true );
		FillScreen();
		g_UiPaint.pMenuRoot = this;
	}

	virtual ~CSourceSharpMenuRoot()
	{
		if ( g_UiPaint.pMenuRoot == this )
			g_UiPaint.pMenuRoot = NULL;
	}

	virtual void Paint() OVERRIDE
	{
		IManagedUiClient *pUi = ManagedUiClient();
		if ( !pUi )
			return;

		int nWide, nTall;
		GetSize( nWide, nTall );

		ShellUi_BeginPaint();
		pUi->Paint( 1, nWide, nTall );
		ShellUi_EndPaint();
	}

	virtual void OnCursorMoved( int x, int y ) OVERRIDE { Forward( MouseMove, x, y, 0, 0, 0.0f ); }
	virtual void OnMousePressed( vgui::MouseCode code ) OVERRIDE { ForwardAtCursor( MouseDown, code, 0.0f ); }
	virtual void OnMouseDoublePressed( vgui::MouseCode code ) OVERRIDE { ForwardAtCursor( MouseDown, code, 0.0f ); }
	virtual void OnMouseReleased( vgui::MouseCode code ) OVERRIDE { ForwardAtCursor( MouseUp, code, 0.0f ); }
	virtual void OnMouseWheeled( int delta ) OVERRIDE { ForwardAtCursor( Wheel, 0, (float)delta ); }
	virtual void OnKeyCodePressed( vgui::KeyCode code ) OVERRIDE { ForwardAtCursor( Key, code, 0.0f ); }
	virtual void OnKeyTyped( wchar_t unichar ) OVERRIDE { ForwardAtCursor( Char, 0, 0.0f, (int32_t)unichar ); }
	virtual void OnKillFocus() OVERRIDE { ForwardAtCursor( FocusLost, 0, 0.0f ); }

private:
	void ForwardAtCursor( int nKind, int nCode, float flDelta, int32_t nChar = 0 )
	{
		// Screen coordinates are the root's own: it covers the screen from 0,0.
		int x = 0, y = 0;
		surface()->SurfaceGetCursorPos( x, y );
		Forward( nKind, x, y, nCode, nChar, flDelta );
	}

	static void Forward( int nKind, int x, int y, int nCode, int32_t nChar, float flDelta )
	{
		if ( IManagedUiClient *pUi = ManagedUiClient() )
			pUi->Input( nKind, x, y, nCode, nChar, flDelta );
	}
};

void SourceSharp_UiSetMenuInput( int32_t bMouse, int32_t bKeyboard )
{
	CSourceSharpRawPanel *pRoot = g_UiPaint.pMenuRoot;
	if ( !pRoot )
		return;

	pRoot->SetMouseInputEnabled( bMouse != 0 );
	pRoot->SetKeyBoardInputEnabled( bKeyboard != 0 );
	// To the front, so vgui2's input finds it first for the keyboard.
	if ( bMouse || bKeyboard )
		surface()->MovePopupToFront( pRoot->GetVPanel() );
}

void SourceSharp_UiSetCursor( int32_t nShape )
{
	// Managed code's UiCursor values, in its order, as vgui cursors.
	static const vgui::CursorCode s_Cursors[] =
	{
		vgui::dc_arrow, vgui::dc_sizeall, vgui::dc_sizewe, vgui::dc_sizens, vgui::dc_sizenwse, vgui::dc_sizenesw,
	};

	if ( g_UiPaint.pMenuRoot && nShape >= 0 && nShape < (int32_t)ARRAYSIZE( s_Cursors ) )
		g_UiPaint.pMenuRoot->SetCursor( (vgui::HCursor)s_Cursors[ nShape ] );
}

class CSourceSharpHud : public CHudElement, public CSourceSharpRawPanel
{
public:
	CSourceSharpHud( const char *pElementName )
		: CHudElement( pElementName ), CSourceSharpRawPanel( "SourceSharpHud" )
	{
		SOURCESHARP_TRACE();

		// The viewport is a raw vgui2 panel (shell_viewport_client.h): parented by VPANEL.
		const vgui::VPANEL parent = SourceSharp_ClientViewportPanel();
		SetParent( parent );

		// Hidden only when the player is dead; HIDEHUD_MISCSTATUS would hide it for
		// unrelated reasons.
		SetHiddenBits( HIDEHUD_PLAYERDEAD );
		SetVisible( true );

		// The canvas covers the screen.
		FillScreen();

		// The menu layer's root, parented to the viewport, made once with the HUD.
		if ( !g_UiPaint.pMenuRoot )
			new CSourceSharpMenuRoot( parent );
	}

	virtual void Paint() OVERRIDE
	{
		// Layer 0 is the HUD. The UI area is NULL before the first load and after the
		// last unload.
		IManagedUiClient *pUi = ManagedUiClient();
		if ( !pUi )
			return;

		int nWide, nTall;
		GetSize( nWide, nTall );

		ShellUi_BeginPaint();
		pUi->Paint( 0, nWide, nTall );
		ShellUi_EndPaint();
	}

	// Forwarded so the UI drops what it sized for the old screen (fonts are
	// rasterised per pixel height) the same frame.
	virtual void OnScreenSizeChanged( int nOldWide, int nOldTall ) OVERRIDE
	{
		int nWide, nTall;
		surface()->GetScreenSize( nWide, nTall );
		if ( IManagedUiClient *pUi = ManagedUiClient() )
			pUi->ScreenSizeChanged( nOldWide, nOldTall, nWide, nTall );
	}

	virtual const char *GetName() OVERRIDE { return CHudElement::GetName(); }
};

DECLARE_HUDELEMENT( CSourceSharpHud );

// A titles.txt entry by name (what env_message and HudText name), as the engine
// parsed it. Copied out: the engine reallocates that buffer on a level change.
int32_t SourceSharp_HudTextMessageGet( const char *pName, SourceSharpTextMessage *pOut )
{
	if ( !pName || !pName[0] || !pOut )
		return 0;

	// Exactly the name handed over; managed code applies stock's leading-'#' rule.
	client_textmessage_t *pMessage = TextMessageGet( pName );

	if ( !pMessage || !pMessage->pMessage )
		return 0;

	pOut->nEffect    = pMessage->effect;
	pOut->r1         = pMessage->r1;
	pOut->g1         = pMessage->g1;
	pOut->b1         = pMessage->b1;
	pOut->a1         = pMessage->a1;
	pOut->r2         = pMessage->r2;
	pOut->g2         = pMessage->g2;
	pOut->b2         = pMessage->b2;
	pOut->a2         = pMessage->a2;
	pOut->flX        = pMessage->x;
	pOut->flY        = pMessage->y;
	pOut->flFadeIn   = pMessage->fadein;
	pOut->flFadeOut  = pMessage->fadeout;
	pOut->flHoldTime = pMessage->holdtime;
	pOut->flFxTime   = pMessage->fxtime;

	V_strncpy( pOut->szMessage, pMessage->pMessage, sizeof( pOut->szMessage ) );
	V_strncpy( pOut->szClearMessage,
	           pMessage->pClearMessage ? pMessage->pClearMessage : "",
	           sizeof( pOut->szClearMessage ) );

	return 1;
}

// The key bound to a command, for hint text's %+use% tokens; empty when unbound.
void SourceSharp_HudLookupBinding( const char *pBinding, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = 0;

	if ( !pBinding || !pBinding[0] )
		return;

	const char *pKey = engine->Key_LookupBinding( pBinding );
	if ( pKey )
		V_strncpy( pOut, pKey, nOutSize );
}

// A "#Token" through the localisation tables; the token unchanged when there is
// no entry.
void SourceSharp_HudLocalise( const char *pToken, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = 0;

	if ( !pToken || !pToken[0] )
		return;

	wchar_t *pLocalized = g_pVGuiLocalize->Find( pToken );

	if ( pLocalized )
		g_pVGuiLocalize->ConvertUnicodeToANSI( pLocalized, pOut, nOutSize );
	else
		V_strncpy( pOut, pToken, nOutSize );
}
