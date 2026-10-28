//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the HUD, drawn from C# ==============================//
// SS_REASON a  a vtable the engine calls through
//
// A single VGUI panel that does no layout and holds no state: the HUD's root
// panel (plan_client_ui §8 U2). Once a frame it hands the UI the screen size
// and the UI draws its HUD layer through IShimUiClient (shell_ui.cpp). What is
// on it, and where, is decided there.
//
// The split is the same one used everywhere else in this mod — the shell owns
// what genuinely needs the engine (a VGUI surface, a font, the paint pass) and
// the behaviour lives in C#. The stock HL2MP equivalents are three separate
// CHudElement subclasses totalling roughly 950 lines of C++; none of that
// structure is needed once the drawing is immediate-mode.
//
//=============================================================================//

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
#include "imanaged_areas_client.h"   // IManagedUiClient (plan_client_ui U2)

// memdbgon must be the last include file in a .cpp file!!!

using namespace vgui;

//=============================================================================
// THE UI'S DRAW PRIMITIVES (plan_client_ui §8 U2): IShimUiClient's bodies, one
// vgui::ISurface call each. VguiBackend (src/client/managed/Ui) turns a render
// list into these; what to draw, where, in which font and colour is decided
// there. Colours arrive packed 0xRRGGBBAA; text arrives as UTF-16, which is how
// C# holds it, and is widened here to the engine's wchar_t. The draw slots
// refuse outside the paint window the root panel below opens, as the old HUD's
// did: a draw from a C# timer would otherwise corrupt VGUI's state. Measuring,
// fonts and textures are not draws and work at any time.
//=============================================================================

namespace
{
	// The UI's paint window: open only while a root panel's Paint calls
	// IManagedUiClient::Paint, and the clip that panel's traversal set,
	// restored when managed turns its own clip off and when the window closes.
	// One zero-initialised POD: no static initialiser.
	struct UiPaintWindow
	{
		bool bOpen;
		bool bPanelClipDisabled;
		int  nPanelClip[ 4 ];
		CSourceSharpRawPanel *pMenuRoot;   // the menu layer's root panel, once made
	};
	UiPaintWindow g_UiPaint;

	// One draw's text, widened. Longer runs are cut here: the toolkit elides
	// long before this.
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

int32_t SourceSharp_UiTextureCreate( void )
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

//-----------------------------------------------------------------------------
// The local player's weapons.
//
// Read live from the player rather than cached: the HUD is drawn every frame
// from whatever the client currently believes, and a cache would only add a way
// for the display to disagree with the game.
//-----------------------------------------------------------------------------
// THE FIVE LOCAL-WEAPON SLOTS ARE GONE, AND THIS IS WHAT MADE THEM DELETABLE.
//
// They read the C_BasePlayer inventory, which retired with the class, and 13c
// left them answering "no weapons here" -- 0, -1, -1, -1 and an empty string --
// on the grounds that this is what they had observably answered since the
// standalone player became the local player.
//
// WHAT THE RULINGS TABLE GOT WRONG, AND WHAT THE ADDENDUM PREDICTED. C3 called
// all five DEAD because the only things naming them were Engine.cs and
// EngineBinding.cs wrappers "with no caller". Managed does not name the slot,
// it names the FACADE: SourceSharp.Client/Hud.cs:213-246 reads
// Screen.LocalWeaponCount, .LocalActiveWeapon, .LocalWeaponName(i, buf),
// .LocalWeaponClip and .LocalWeaponReserve every frame the HUD paints. Five
// LIVE managed callers receiving nothing, which is a defect ledger and not a
// retirement list.
//
// SO IT WAS FIXED AT THE MANAGED END AND THEN RETIRED. Hud.cs had two feeds: the
// Arsenal (the standalone player's networked weapon state, decoded managed-side)
// and this one as a fallback for when the Arsenal has no active weapon. The
// fallback returned immediately on `count <= 0` and so drew nothing, every
// frame, for every player. Lane 4c-render deleted the fallback -- the Arsenal is
// the feed -- and retired the five slots with it. The five ABI fields stood
// marked in sourcesharp.h and Abi.cs, with their assignments nulled, until the
// serialised pass at ABI 249 removed them from both tables along with their
// Engine.cs wrappers and the five IEngineHud facade members those sat behind.
// They were left standing that long because removing a field from the middle of
// either table renumbers every slot after it and AbiParityTests pins names in
// order, so a lane doing it alone corrupts every lane still editing those files.
//
// WHAT WOULD CLOSE IT PROPERLY: the server networking the inventory behind the
// active weapon. Hud.cs says so at the Arsenal feed -- "a list invented from one
// entity would be a lie with a scrollbar" -- and that list would arrive managed,
// not through five native slots reading a class that no longer exists.

//-----------------------------------------------------------------------------
// Local player health, or -1 when there is no local player — which is the case
// in the menu and between levels, and is why this is not simply 0: a HUD needs
// to tell "no player" from "dead".
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// WHO the viewer is: the entity index the engine gave this client, 0 when
// there is no local player. The engine's answer directly, because it is the
// same fact whichever hierarchy the player entity lives on — and it is how
// managed code decides "is this entity mine" for the viewmodel and the HUD.
//-----------------------------------------------------------------------------
int32_t SourceSharp_GetLocalPlayerIndex( void )
{
	const int nIndex = engine->GetLocalPlayer();
	return nIndex > 0 ? nIndex : 0;
}

int32_t SourceSharp_GetLocalPlayerHealth( void )
{
	// The standalone local player's health, PUBLISHED BY THE MANAGED PLAYER
	// once a frame rather than read out of the blob at a quoted offset.
	//
	// IT WAS READ AT A QUOTED OFFSET FOR ONE BUILD AND THE HUD PRINTED
	// 1544355841. standalone_player_state.h restated Health's placement as a
	// #define "for the native readers that cannot ask C#", said the number was
	// pinned from the other side by PlayerNetStateTests.TheLayoutIsThePinnedOne
	// -- and no such test existed. So it was an unpinned second declaration of a
	// layout that has grown a great deal since it was written, which is exactly
	// the drift the port removes everywhere else. The header is deleted; this
	// reads the one copy the managed player writes.
	//
	// -1 before the first prediction frame, which is what this answered before
	// a player existed anyway.
	if ( C_StandaloneEntity *pShell = SourceSharp_LocalPlayerShell() )
		return pShell->PredictedHealth();

	return -1;
}

//-----------------------------------------------------------------------------
// player_info_t as EngineClient.cs's PlayerInfoT reads it (plan_client_ui U3).
COMPILE_TIME_ASSERT( sizeof( player_info_t ) == 132 );
COMPILE_TIME_ASSERT( offsetof( player_info_t, userID ) == 32 );
COMPILE_TIME_ASSERT( offsetof( player_info_t, guid ) == 36 );
COMPILE_TIME_ASSERT( offsetof( player_info_t, fakeplayer ) == 108 );

//-----------------------------------------------------------------------------
// THE MENU ROOT (plan_client_ui §8 U3): the UI's menu layer. A full-screen
// popup that paints layer 1 and forwards its input to IManagedUiClient::Input,
// one call per event. It takes the mouse and the keyboard only when managed
// code asks (UiSetMenuInput: a screen that wants them is open); otherwise it
// is drawn and transparent to input, and the game keeps its mouse.
//-----------------------------------------------------------------------------
vgui::VPANEL SourceSharp_ClientViewportPanel();   // shell_client.cpp

class CSourceSharpMenuRoot : public CSourceSharpRawPanel
{
public:
	// The managed input kinds (UiInputKind), in its order.
	enum { MouseMove, MouseDown, MouseUp, Wheel, Key, Char, FocusLost };

	explicit CSourceSharpMenuRoot( vgui::VPANEL parent )
		: CSourceSharpRawPanel( "SourceSharpMenuRoot" )
	{
		SetParent( parent );
		// A popup, as Panel::MakePopup( false, false ) made one: the surface
		// counts a popup's input towards whether the cursor shows, and a plain
		// child would never show it.
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

	virtual void Paint( void ) OVERRIDE
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
	// The managed cursor shapes (UiCursor), in its order, as vgui's cursors.
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

		// Hidden only when the player is dead. NOT HIDEHUD_MISCSTATUS, which
		// means "trains, pickup history, death notices" and would hide a health
		// readout for unrelated reasons.
		SetHiddenBits( HIDEHUD_PLAYERDEAD );
		SetVisible( true );

		// The canvas covers the screen. (It was sized in PerformLayout when it
		// was a vgui_controls Panel, because HudLayout.res overwrote bounds set
		// earlier; nothing lays this panel out from a script.)
		FillScreen();

		// The menu layer's root, beside the HUD's, owned by the viewport as
		// its parent: made once, with the HUD.
		if ( !g_UiPaint.pMenuRoot )
			new CSourceSharpMenuRoot( parent );
	}

	virtual void Paint( void ) OVERRIDE
	{
		// The UI's HUD layer (plan_client_ui §8 U2): layer 0 is the HUD, drawn
		// through IShimUiClient inside the UI's own paint window. The UI area is
		// managed-built: NULL before the first load and after the last unload,
		// which is when this is skipped.
		IManagedUiClient *pUi = ManagedUiClient();
		if ( !pUi )
			return;

		int nWide, nTall;
		GetSize( nWide, nTall );

		ShellUi_BeginPaint();
		pUi->Paint( 0, nWide, nTall );
		ShellUi_EndPaint();
	}

	// A video-mode change, forwarded so the UI drops what it sized for the old
	// screen (fonts are rasterised per pixel height) the same frame.
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

//-----------------------------------------------------------------------------
// WEAPON SWITCHING IS MANAGED (lane M1-B, 2026-10-25). The stock binds' nine
// commands (invnext, invprev, lastinv, slot1..slot6) stood here as native
// CON_COMMANDs forwarding a fixed line to the server's Arsenal; they are
// SourceSharp.Client's WeaponSelection commands now, sending the same lines
// through ServerCmd. weapon_selection.cpp, which registered the crashing stock
// versions, stays out of the build.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// titles.txt, which is what env_message names.
//
// A HudText user message carries a NAME, never a sentence. The words live in
// titles.txt along with where on screen they go, in what colour and for how
// long — and the engine already parses that file into client_textmessage_t for
// its own use. So this asks for the parsed entry rather than reimplementing a
// parser managed code would then have to keep in step with the engine's.
//
// Copied out rather than pointed at. The engine reallocates that buffer on a
// level change, so a managed side holding the pointer would be reading freed
// text at exactly the moment a map wanted to say something.
//-----------------------------------------------------------------------------
int32_t SourceSharp_HudTextMessageGet( const char *pName, SourceSharpTextMessage *pOut )
{
	if ( !pName || !pName[0] || !pOut )
		return 0;

	// Exactly the name handed over: stock's leading-'#' rule
	// (CHudMessage::MessageAdd) is HudMessages.TitleName's (lane M1-B).
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

//-----------------------------------------------------------------------------
// Which key a player has bound to a command.
//
// Hint text is written with %+use% tokens in it, because the sentence has to
// name a key and only the client knows which one. Nothing is decided here: an
// unbound command answers with an empty string, and what to draw for that is
// the managed side's call.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// A "#Token" through the localisation tables.
//
// Writes the token back unchanged when there is no entry, so a caller can always
// draw what it gets rather than having to decide what an absent translation
// looks like on screen.
//-----------------------------------------------------------------------------
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
