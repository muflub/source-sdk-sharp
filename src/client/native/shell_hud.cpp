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
#include "materialsystem/imaterial.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "cdll_util.h"
#include "eiface.h"
#include "shell_hud_ui_client.h"
#include "shell_client_mode.h"
#include "shareddefs.h"
#include "shell_standalone_client.h"
#include "shell_rawpanel_client.h"
#include "sourcesharp_entrypoints.h"
#include "shell_diagnostics.h"
#include "sourcesharp.h"
#include "view_scene.h"
#include "ienginevgui.h"
#include "shell_gameui.h"
#include "imanaged_areas_client.h"
#include "shell_standalone_clients.h"
#include "shell_client_engine_interfaces.h"

// IShimUiClient's draw primitives, one vgui::ISurface call each. Colours arrive
// packed 0xRRGGBBAA; text arrives as UTF-16 and is widened to wchar_t. Draws are
// refused outside the root panel's paint window, since a draw from elsewhere
// would corrupt VGUI's state; measuring, fonts and textures work at any time.

namespace
{
// One draw's text, widened; longer runs are cut.
const int kMaxText = 512;

// One polygon's vertices; the toolkit's corners use well under this.
const int kMaxVertices = 256;

int Widen(const uint16_t* pText, int32_t nLength, wchar_t* pOut, int nOut)
{
	if (!pText || nLength <= 0)
		return 0;

	const int n = nLength < nOut - 1 ? nLength : nOut - 1;
	for (int i = 0; i < n; i++)
		pOut[i] = (wchar_t)pText[i];
	pOut[n] = 0;
	return n;
}
}

void CHudUiClient::SetColor(uint32_t nRgba)
{
	m_Engines.m_pVGuiSurface->DrawSetColor((nRgba >> 24) & 0xFF, (nRgba >> 16) & 0xFF, (nRgba >> 8) & 0xFF,
										   nRgba & 0xFF);
}

void CHudUiClient::BeginPaint()
{
	m_bPaintOpen = true;
	if (m_Engines.m_pMatSystemSurface)
		m_Engines.m_pMatSystemSurface->GetClippingRect(m_nPanelClip[0], m_nPanelClip[1], m_nPanelClip[2],
													   m_nPanelClip[3], m_bPanelClipDisabled);
}

void CHudUiClient::EndPaint()
{
	UiSetClip(0, 0, 0, 0, 0);
	m_bPaintOpen = false;
}

void CHudUiClient::UiDrawFilledRect(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba)
{
	if (!m_bPaintOpen || x1 <= x0 || y1 <= y0)
		return;

	SetColor(nRgba);
	m_Engines.m_pVGuiSurface->DrawFilledRect(x0, y0, x1, y1);
}

void CHudUiClient::UiDrawFilledRectFade(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba, int32_t nAlpha0,
										int32_t nAlpha1, int32_t bHorizontal)
{
	if (!m_bPaintOpen || x1 <= x0 || y1 <= y0)
		return;

	SetColor(nRgba);
	m_Engines.m_pVGuiSurface->DrawFilledRectFade(x0, y0, x1, y1, (unsigned int)nAlpha0, (unsigned int)nAlpha1,
												 bHorizontal != 0);
}

void CHudUiClient::UiDrawOutlinedRect(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba)
{
	if (!m_bPaintOpen || x1 <= x0 || y1 <= y0)
		return;

	SetColor(nRgba);
	m_Engines.m_pVGuiSurface->DrawOutlinedRect(x0, y0, x1, y1);
}

void CHudUiClient::UiDrawLine(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba)
{
	if (!m_bPaintOpen)
		return;

	SetColor(nRgba);
	m_Engines.m_pVGuiSurface->DrawLine(x0, y0, x1, y1);
}

// Texture ids bound to an engine material (UiMaterialTexture below): a draw of
// one runs the material's own shader, and a Refract shader needs the frame
// buffer copy fresh — stock refreshes it at the top of CHudScope::Paint
// (tf_hud_scope.cpp:398) before its quadrant draws. The refresh is guarded by
// the frame count (view_scene.h), so the four quadrants of one frame pay once.
void CHudUiClient::RefreshRefractFor(int32_t nTexture)
{
	for (int i = 0; i < m_nMaterialTextures; i++)
	{
		if (m_MaterialTextures[i] == nTexture)
		{
			UpdateRefractTexture(m_Engines);
			return;
		}
	}
}

void CHudUiClient::UiDrawTexturedRect(int32_t nTexture, int32_t x0, int32_t y0, int32_t x1, int32_t y1, float s0,
									  float t0, float s1, float t1, uint32_t nRgba)
{
	if (!m_bPaintOpen || x1 <= x0 || y1 <= y0 || !m_Engines.m_pVGuiSurface->IsTextureIDValid(nTexture))
		return;

	RefreshRefractFor(nTexture);
	SetColor(nRgba);
	m_Engines.m_pVGuiSurface->DrawSetTexture(nTexture);
	m_Engines.m_pVGuiSurface->DrawTexturedSubRect(x0, y0, x1, y1, s0, t0, s1, t1);
}

void CHudUiClient::UiDrawPolygon(int32_t nTexture, const float* pXyuv, int32_t nVertices, uint32_t nRgba)
{
	if (!m_bPaintOpen || !pXyuv || nVertices < 3 || nVertices > kMaxVertices ||
		!m_Engines.m_pVGuiSurface->IsTextureIDValid(nTexture))
		return;

	RefreshRefractFor(nTexture);
	vgui::Vertex_t verts[kMaxVertices];
	for (int i = 0; i < nVertices; i++)
		verts[i].Init(Vector2D(pXyuv[i * 4 + 0], pXyuv[i * 4 + 1]), Vector2D(pXyuv[i * 4 + 2], pXyuv[i * 4 + 3]));

	SetColor(nRgba);
	m_Engines.m_pVGuiSurface->DrawSetTexture(nTexture);
	m_Engines.m_pVGuiSurface->DrawTexturedPolygon(nVertices, verts);
}

// One textured quad from (x, y, u, v) vertices: a rotated or additive leaf.
// bAdditive adds the pixels to what is behind (the damage ring's glow) by the
// material flag of the texture id's own backing material — every RGBA upload
// gets one UnlitGeneric of its own (__vgui_texture_N), so the flag touches
// only that handle. The turn is one-way and remembered: a refresh is paid
// once, on the draw that first asks, and a handle is drawn one way at a time.
void CHudUiClient::UiDrawTexturedQuad(int32_t nTexture, const float* pXyuv, int32_t nVertices, uint32_t nRgba,
									  int32_t bAdditive)
{
	if (!m_bPaintOpen || !pXyuv || nVertices < 3 || nVertices > kMaxVertices ||
		!m_Engines.m_pVGuiSurface->IsTextureIDValid(nTexture))
		return;

	RefreshRefractFor(nTexture);
	vgui::Vertex_t verts[kMaxVertices];
	for (int i = 0; i < nVertices; i++)
		verts[i].Init(Vector2D(pXyuv[i * 4 + 0], pXyuv[i * 4 + 1]), Vector2D(pXyuv[i * 4 + 2], pXyuv[i * 4 + 3]));

	if (bAdditive && m_Engines.m_pMatSystemSurface)
	{
		bool first = true;
		for (int i = 0; i < m_nAdditive; i++)
		{
			if (m_Additive[i] == nTexture)
			{
				first = false;
				break;
			}
		}
		if (first && m_nAdditive < kMaxTextures)
		{
			if (IMaterial* pMaterial = m_Engines.m_pMatSystemSurface->DrawGetTextureMaterial(nTexture))
			{
				pMaterial->SetMaterialVarFlag(MATERIAL_VAR_ADDITIVE, true);
				pMaterial->Refresh();
				m_Additive[m_nAdditive++] = nTexture;
			}
		}
	}

	SetColor(nRgba);
	m_Engines.m_pVGuiSurface->DrawSetTexture(nTexture);
	m_Engines.m_pVGuiSurface->DrawTexturedPolygon(nVertices, verts);
}

// A texture handle bound to the named engine material: stock's
// DrawSetTextureFile (CHudScope::Init, tf_hud_scope.cpp:317-320). The vgui
// surface resolves the .vmt and the draw slots run the material's own shader —
// a Refract quadrants' pixels are the refracted frame buffer, not a picture to
// upload. A name that loads nothing (an error material) answers 0, and the
// screen draws its fallback.
int32_t CHudUiClient::UiMaterialTexture(const char* pName)
{
	if (!pName || !pName[0])
		return 0;

	int id = m_Engines.m_pVGuiSurface->CreateNewTextureID(true);
	m_Engines.m_pVGuiSurface->DrawSetTextureFile(id, pName, true, false);
	if (m_Engines.m_pMatSystemSurface && IsErrorMaterial(m_Engines.m_pMatSystemSurface->DrawGetTextureMaterial(id)))
		return 0;

	if (m_nMaterialTextures < (int)(sizeof(m_MaterialTextures) / sizeof(m_MaterialTextures[0])))
		m_MaterialTextures[m_nMaterialTextures++] = id;
	return id;
}

void CHudUiClient::UiDrawText(int32_t nFont, int32_t x, int32_t y, const uint16_t* pText, int32_t nLength,
							  uint32_t nRgba)
{
	if (!m_bPaintOpen || nFont == vgui::INVALID_FONT)
		return;

	wchar_t wszText[kMaxText];
	const int n = Widen(pText, nLength, wszText, kMaxText);
	if (n <= 0)
		return;

	m_Engines.m_pVGuiSurface->DrawSetTextFont((vgui::HFont)nFont);
	m_Engines.m_pVGuiSurface->DrawSetTextColor((nRgba >> 24) & 0xFF, (nRgba >> 16) & 0xFF, (nRgba >> 8) & 0xFF,
											   nRgba & 0xFF);
	m_Engines.m_pVGuiSurface->DrawSetTextPos(x, y);
	m_Engines.m_pVGuiSurface->DrawPrintText(wszText, n);
}

void CHudUiClient::UiMeasureText(int32_t nFont, const uint16_t* pText, int32_t nLength, int32_t* pOutWidth,
								 int32_t* pOutHeight)
{
	if (pOutWidth)
		*pOutWidth = 0;
	if (pOutHeight)
		*pOutHeight = 0;
	if (nFont == vgui::INVALID_FONT)
		return;

	wchar_t wszText[kMaxText];
	if (Widen(pText, nLength, wszText, kMaxText) <= 0)
		return;

	int nWide = 0, nTall = 0;
	m_Engines.m_pVGuiSurface->GetTextSize((vgui::HFont)nFont, wszText, nWide, nTall);
	if (pOutWidth)
		*pOutWidth = nWide;
	if (pOutHeight)
		*pOutHeight = nTall;
}

void CHudUiClient::UiSetClip(int32_t bEnabled, int32_t x0, int32_t y0, int32_t x1, int32_t y1)
{
	if (!m_bPaintOpen || !m_Engines.m_pMatSystemSurface)
		return;

	if (bEnabled)
	{
		m_Engines.m_pMatSystemSurface->DisableClipping(false);
		m_Engines.m_pMatSystemSurface->SetClippingRect(x0, y0, x1, y1);
		return;
	}

	m_Engines.m_pMatSystemSurface->SetClippingRect(m_nPanelClip[0], m_nPanelClip[1], m_nPanelClip[2], m_nPanelClip[3]);
	m_Engines.m_pMatSystemSurface->DisableClipping(m_bPanelClipDisabled);
}

int32_t CHudUiClient::UiTextureCreate()
{
	return m_Engines.m_pVGuiSurface->CreateNewTextureID(true);
}

void CHudUiClient::UiTextureUpload(int32_t nTexture, const uint8_t* pRgba, int32_t nWide, int32_t nTall)
{
	if (!pRgba || nWide <= 0 || nTall <= 0)
		return;

	m_Engines.m_pVGuiSurface->DrawSetTextureRGBA(nTexture, pRgba, nWide, nTall, 1, true);
}

int32_t CHudUiClient::UiFontAddFile(const char* pPath)
{
	if (!pPath || !pPath[0])
		return 0;

	return m_Engines.m_pVGuiSurface->AddCustomFontFile(NULL, pPath) ? 1 : 0;
}

int32_t CHudUiClient::UiFontCreate(const char* pFamily, int32_t nTall, int32_t nWeight, int32_t nFlags)
{
	if (!pFamily || !pFamily[0] || nTall <= 0)
		return vgui::INVALID_FONT;

	vgui::HFont hFont = m_Engines.m_pVGuiSurface->CreateFont();
	if (hFont == vgui::INVALID_FONT)
		return vgui::INVALID_FONT;

	if (!m_Engines.m_pVGuiSurface->SetFontGlyphSet(hFont, pFamily, nTall, nWeight, 0, 0, nFlags))
		return vgui::INVALID_FONT;

	return (int32_t)hFont;
}

void CHudUiClient::UiFontMetrics(int32_t nFont, int32_t* pOutTall, int32_t* pOutAscent)
{
	if (pOutTall)
		*pOutTall = 0;
	if (pOutAscent)
		*pOutAscent = 0;
	if (nFont == vgui::INVALID_FONT)
		return;

	if (pOutTall)
		*pOutTall = m_Engines.m_pVGuiSurface->GetFontTall((vgui::HFont)nFont);
	if (pOutAscent)
		*pOutAscent = m_Engines.m_pVGuiSurface->GetFontAscent((vgui::HFont)nFont, L'M');
}

int32_t CHudUiClient::UiFontAdvances(int32_t nFont, int32_t nFirst, int32_t nCount, int32_t* pOutAdvances)
{
	if (nFont == vgui::INVALID_FONT || !pOutAdvances || nCount <= 0)
		return 0;

	for (int i = 0; i < nCount; i++)
	{
		int a = 0, b = 0, c = 0;
		m_Engines.m_pVGuiSurface->GetCharABCwide((vgui::HFont)nFont, nFirst + i, a, b, c);
		pOutAdvances[i] = a + b + c;
	}
	return nCount;
}

void CHudUiClient::UiPlaySound(const char* pPath)
{
	if (pPath && pPath[0])
		m_Engines.m_pVGuiSurface->PlaySound(pPath);
}

// The engine's entity index for this client, 0 when there is no local player.
int32_t CHudUiClient::GetLocalPlayerIndex()
{
	const int nIndex = m_Engines.m_pEngine->GetLocalPlayer();
	return nIndex > 0 ? nIndex : 0;
}

int32_t CStandaloneClients::GetLocalPlayerHealth()
{
	// The health the managed player publishes each frame; -1 when there is no
	// local player, so a HUD can tell "no player" from "dead".
	if (C_StandaloneEntity* pShell = LocalPlayer())
		return pShell->PredictedHealth();

	return -1;
}

// player_info_t as managed code reads it.
COMPILE_TIME_ASSERT(sizeof(player_info_t) == 132);
COMPILE_TIME_ASSERT(offsetof(player_info_t, userID) == 32);
COMPILE_TIME_ASSERT(offsetof(player_info_t, guid) == 36);
COMPILE_TIME_ASSERT(offsetof(player_info_t, fakeplayer) == 108);

// The UI's menu layer: a full-screen popup under the client viewport that
// paints layer 1 and forwards input to IManagedUiClient::Input. It takes the
// mouse and keyboard only when managed code asks; otherwise it is transparent
// to input. The GameUI is managed (shell_gameui.cpp) and draws in this layer,
// so no other module's dialogs share the screen with it.

class CSourceSharpMenuRoot : public CSourceSharpRawPanel
{
public:
	// Managed code's UiInputKind values, in its order.
	enum
	{
		MouseMove,
		MouseDown,
		MouseUp,
		Wheel,
		Key,
		Char,
		FocusLost
	};

	CSourceSharpMenuRoot(CHudUiClient& ui, const CClientEngineInterfaces& engines, vgui::VPANEL parent)
		: CSourceSharpRawPanel(engines, "SourceSharpMenuRoot")
		, m_Ui(ui)
	{
		SetParent(parent);
		// A popup: the surface counts a popup's input towards whether the cursor
		// shows, and a plain child would never show it.
		MakePopup(false, false);
		SetMouseInputEnabled(false);
		SetKeyBoardInputEnabled(false);
		SetVisible(true);
		FillScreen();
		m_Ui.SetMenuRoot(this);
	}

	virtual ~CSourceSharpMenuRoot()
	{
		if (m_Ui.MenuRoot() == this)
			m_Ui.SetMenuRoot(NULL);
	}

	virtual void Paint() OVERRIDE
	{
		IManagedUiClient* pUi = m_Ui.Managed();
		if (!pUi)
			return;

		int nWide, nTall;
		GetSize(nWide, nTall);

		m_Ui.BeginPaint();
		pUi->Paint(1, nWide, nTall);
		m_Ui.EndPaint();
	}

	virtual void OnCursorMoved(int x, int y) OVERRIDE { Forward(MouseMove, x, y, 0, 0, 0.0f); }

	virtual void OnMousePressed(vgui::MouseCode code) OVERRIDE { ForwardAtCursor(MouseDown, code, 0.0f); }

	virtual void OnMouseDoublePressed(vgui::MouseCode code) OVERRIDE { ForwardAtCursor(MouseDown, code, 0.0f); }

	virtual void OnMouseReleased(vgui::MouseCode code) OVERRIDE { ForwardAtCursor(MouseUp, code, 0.0f); }

	virtual void OnMouseWheeled(int delta) OVERRIDE { ForwardAtCursor(Wheel, 0, (float)delta); }

	virtual void OnKeyCodePressed(vgui::KeyCode code) OVERRIDE { ForwardAtCursor(Key, code, 0.0f); }

	virtual void OnKeyTyped(wchar_t unichar) OVERRIDE { ForwardAtCursor(Char, 0, 0.0f, (int32_t)unichar); }

	virtual void OnKillFocus() OVERRIDE { ForwardAtCursor(FocusLost, 0, 0.0f); }

private:
	void ForwardAtCursor(int nKind, int nCode, float flDelta, int32_t nChar = 0)
	{
		// Screen coordinates are the root's own: it covers the screen from 0,0.
		int x = 0, y = 0;
		m_Engines.m_pVGuiSurface->SurfaceGetCursorPos(x, y);
		Forward(nKind, x, y, nCode, nChar, flDelta);
	}

	void Forward(int nKind, int x, int y, int nCode, int32_t nChar, float flDelta)
	{
		if (IManagedUiClient* pUi = m_Ui.Managed())
			pUi->Input(nKind, x, y, nCode, nChar, flDelta);
	}

	CHudUiClient& m_Ui;
};

void CHudUiClient::UiSetMenuInput(int32_t bMouse, int32_t bKeyboard)
{
	if (CSourceSharpRawPanel* pRoot = m_pMenuRoot)
	{
		pRoot->SetMouseInputEnabled(bMouse != 0);
		pRoot->SetKeyBoardInputEnabled(bKeyboard != 0);
		// To the front, so vgui2's input finds it first for the keyboard.
		if (bMouse || bKeyboard)
			m_Engines.m_pVGuiSurface->MovePopupToFront(pRoot->GetVPanel());
	}
}

void CHudUiClient::UiSetCursor(int32_t nShape)
{
	// Managed code's UiCursor values, in its order, as vgui cursors.
	static const vgui::CursorCode s_Cursors[] = {
		vgui::dc_arrow, vgui::dc_sizeall, vgui::dc_sizewe, vgui::dc_sizens, vgui::dc_sizenwse, vgui::dc_sizenesw,
	};

	if (nShape < 0 || nShape >= (int32_t)ARRAYSIZE(s_Cursors))
		return;
	if (m_pMenuRoot)
		m_pMenuRoot->SetCursor((vgui::HCursor)s_Cursors[nShape]);
}

// While the engine's GameUI is up it hides the client root, so the menu root
// follows the GameUI root and returns under the client viewport when the
// GameUI hides. Once a frame (CShellClientDLL::HudUpdate); a no-op unless the
// home changed.
void CHudUiClient::MenuRootFollowGameUI()
{
	CSourceSharpRawPanel* pRoot = m_pMenuRoot;
	if (!pRoot || !m_Engines.m_pVGui)
		return;

	const vgui::VPANEL home = m_Engines.m_pVGui->IsGameUIVisible() ? m_Engines.m_pVGui->GetPanel(PANEL_GAMEUIDLL)
																   : m_ClientMode.GetViewportVPanel();
	if (!home || m_Engines.m_pVGuiPanel->GetParent(pRoot->GetVPanel()) == home)
		return;

	m_Engines.m_pVGuiPanel->SetParent(pRoot->GetVPanel(), home);
	m_Engines.m_pVGuiPanel->SetVisible(pRoot->GetVPanel(), true);
	m_Engines.m_pVGuiSurface->MovePopupToFront(pRoot->GetVPanel());
}

class CSourceSharpHud : public CHudElement, public CSourceSharpRawPanel
{
public:
	CSourceSharpHud(CHudUiClient& ui, const CClientEngineInterfaces& engines, CHud& hud, const char* pElementName,
					vgui::VPANEL parent)
		: CHudElement(engines, hud, pElementName)
		, CSourceSharpRawPanel(engines, "SourceSharpHud")
		, m_Ui(ui)
	{
		SOURCESHARP_TRACE();

		// The viewport is a raw vgui2 panel (shell_viewport_client.h): parented by VPANEL.
		SetParent(parent);

		// Hidden only when the player is dead; HIDEHUD_MISCSTATUS would hide it for
		// unrelated reasons.
		SetHiddenBits(HIDEHUD_PLAYERDEAD);
		SetVisible(true);

		// The canvas covers the screen.
		FillScreen();

		// The menu layer's panel, made once with the HUD, under the viewport.
		if (!m_Ui.MenuRoot())
			new CSourceSharpMenuRoot(m_Ui, engines, parent);
	}

	virtual void Paint() OVERRIDE
	{
		// Layer 0 is the HUD. The UI area is NULL before the first load and after the
		// last unload.
		IManagedUiClient* pUi = m_Ui.Managed();
		if (!pUi)
			return;

		int nWide, nTall;
		GetSize(nWide, nTall);

		m_Ui.BeginPaint();
		pUi->Paint(0, nWide, nTall);
		m_Ui.EndPaint();
	}

	// Forwarded so the UI drops what it sized for the old screen (fonts are
	// rasterised per pixel height) the same frame.
	virtual void OnScreenSizeChanged(int nOldWide, int nOldTall) OVERRIDE
	{
		int nWide, nTall;
		m_Engines.m_pVGuiSurface->GetScreenSize(nWide, nTall);
		if (IManagedUiClient* pUi = m_Ui.Managed())
			pUi->ScreenSizeChanged(nOldWide, nOldTall, nWide, nTall);
	}

	virtual const char* GetName() OVERRIDE { return CHudElement::GetName(); }

private:
	CHudUiClient& m_Ui;
};

// The one HUD element, which creates the managed HUD host; gHUD.Init initialises it.
void CHudUiClient::CreateHud()
{
	new CSourceSharpHud(*this, m_Engines, m_Hud, "CSourceSharpHud", m_ClientMode.GetViewportVPanel());
}

// A titles.txt entry by name (what env_message and HudText name), as the engine
// parsed it. Copied out: the engine reallocates that buffer on a level change.
int32_t CHudUiClient::HudTextMessageGet(const char* pName, SourceSharpTextMessage* pOut)
{
	if (!pName || !pName[0] || !pOut)
		return 0;

	// Exactly the name handed over; managed code applies stock's leading-'#' rule.
	client_textmessage_t* pMessage = m_Engines.m_pEngine->TextMessageGet(pName);

	if (!pMessage || !pMessage->pMessage)
		return 0;

	pOut->nEffect = pMessage->effect;
	pOut->r1 = pMessage->r1;
	pOut->g1 = pMessage->g1;
	pOut->b1 = pMessage->b1;
	pOut->a1 = pMessage->a1;
	pOut->r2 = pMessage->r2;
	pOut->g2 = pMessage->g2;
	pOut->b2 = pMessage->b2;
	pOut->a2 = pMessage->a2;
	pOut->flX = pMessage->x;
	pOut->flY = pMessage->y;
	pOut->flFadeIn = pMessage->fadein;
	pOut->flFadeOut = pMessage->fadeout;
	pOut->flHoldTime = pMessage->holdtime;
	pOut->flFxTime = pMessage->fxtime;

	V_strncpy(pOut->szMessage, pMessage->pMessage, sizeof(pOut->szMessage));
	V_strncpy(pOut->szClearMessage, pMessage->pClearMessage ? pMessage->pClearMessage : "",
			  sizeof(pOut->szClearMessage));

	return 1;
}

// The key bound to a command, for hint text's %+use% tokens; empty when unbound.
void CHudUiClient::HudLookupBinding(const char* pBinding, char* pOut, int32_t nOutSize)
{
	if (!pOut || nOutSize <= 0)
		return;

	pOut[0] = 0;

	if (!pBinding || !pBinding[0])
		return;

	const char* pKey = m_Engines.m_pEngine->Key_LookupBinding(pBinding);
	if (pKey)
		V_strncpy(pOut, pKey, nOutSize);
}

// A "#Token" through the localisation tables; the token unchanged when there is
// no entry.
void CHudUiClient::HudLocalise(const char* pToken, char* pOut, int32_t nOutSize)
{
	if (!pOut || nOutSize <= 0)
		return;

	pOut[0] = 0;

	if (!pToken || !pToken[0])
		return;

	wchar_t* pLocalized = m_Engines.m_pVGuiLocalize->Find(pToken);

	if (pLocalized)
		m_Engines.m_pVGuiLocalize->ConvertUnicodeToANSI(pLocalized, pOut, nOutSize);
	else
		V_strncpy(pOut, pToken, nOutSize);
}
