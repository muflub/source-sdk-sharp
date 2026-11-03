//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The UI's native half as one object, a member of the client DLL object: the paint window
// managed draws through, the textures its materials need refreshed or made additive, the HUD
// element and the menu layer's root panel. Its IShimUiClient slots are the draw primitives,
// one vgui::ISurface call each; the area's other slots are free answers.

#pragma once

#include <stdint.h>

#include "shell_managed_areas_client.h"

class CSourceSharpClientMode;
class CSourceSharpRawPanel;

class CHudUiClient
{
public:
	CHudUiClient(const CManagedAreasClient& areas, CSourceSharpClientMode& clientMode)
		: m_Areas(areas)
		, m_ClientMode(clientMode)
	{
	}

	CHudUiClient(const CHudUiClient&) = delete;
	CHudUiClient& operator=(const CHudUiClient&) = delete;

	// NULL before the first load and after the last unload.
	IManagedUiClient* Managed() const { return m_Areas.m_pUiClient; }

	// The one HUD element; before gHUD.Init.
	void CreateHud();

	// Open only while a root panel's Paint calls IManagedUiClient::Paint.
	void BeginPaint();
	void EndPaint();

	CSourceSharpRawPanel* MenuRoot() const { return m_pMenuRoot; }

	void SetMenuRoot(CSourceSharpRawPanel* pRoot) { m_pMenuRoot = pRoot; }

	void MenuRootFollowGameUI();

	// IShimUiClient.
	void UiDrawFilledRect(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba);
	void UiDrawFilledRectFade(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba, int32_t nAlpha0,
							  int32_t nAlpha1, int32_t bHorizontal);
	void UiDrawOutlinedRect(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba);
	void UiDrawLine(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba);
	void UiDrawTexturedRect(int32_t nTexture, int32_t x0, int32_t y0, int32_t x1, int32_t y1, float s0, float t0,
							float s1, float t1, uint32_t nRgba);
	void UiDrawPolygon(int32_t nTexture, const float* pXyuv, int32_t nVertices, uint32_t nRgba);
	void UiDrawText(int32_t nFont, int32_t x, int32_t y, const uint16_t* pText, int32_t nLength, uint32_t nRgba);
	void UiMeasureText(int32_t nFont, const uint16_t* pText, int32_t nLength, int32_t* pOutWidth, int32_t* pOutHeight);
	void UiSetClip(int32_t bEnabled, int32_t x0, int32_t y0, int32_t x1, int32_t y1);
	int32_t UiTextureCreate();
	void UiTextureUpload(int32_t nTexture, const uint8_t* pRgba, int32_t nWide, int32_t nTall);
	int32_t UiFontAddFile(const char* pPath);
	int32_t UiFontCreate(const char* pFamily, int32_t nTall, int32_t nWeight, int32_t nFlags);
	void UiFontMetrics(int32_t nFont, int32_t* pOutTall, int32_t* pOutAscent);
	int32_t UiFontAdvances(int32_t nFont, int32_t nFirst, int32_t nCount, int32_t* pOutAdvances);
	void UiPlaySound(const char* pPath);
	void UiSetMenuInput(int32_t bMouse, int32_t bKeyboard);
	void UiSetCursor(int32_t nShape);
	void UiDrawTexturedQuad(int32_t nTexture, const float* pXyuv, int32_t nVertices, uint32_t nRgba, int32_t bAdditive);
	int32_t UiMaterialTexture(const char* pName);

private:
	void RefreshRefractFor(int32_t nTexture);

	const CManagedAreasClient& m_Areas;
	CSourceSharpClientMode& m_ClientMode;

	// The paint window, and the clip the open panel's traversal set, restored when managed
	// code turns its clip off and when the window closes.
	bool m_bPaintOpen = false;
	bool m_bPanelClipDisabled = false;
	int m_nPanelClip[4] = {};

	// The menu layer's root panel, once made.
	CSourceSharpRawPanel* m_pMenuRoot = nullptr;

	static const int kMaxTextures = 64;

	int m_MaterialTextures[kMaxTextures] = {};
	int m_nMaterialTextures = 0;

	int m_Additive[kMaxTextures] = {};
	int m_nAdditive = 0;
};
