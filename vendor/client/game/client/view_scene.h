//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "shell_client_engine_interfaces.h"
#include "shell_convars_client.h"

#include "convar.h"
#include "iviewrender.h"
#include "view_shared.h"
#include "shell_render_targets.h"
#include "materialsystem/itexture.h"

// Transform a world point into normalized screen space (X and Y from -1 to 1).
// Returns 0 if the point is behind the viewer.

inline void UpdateRefractTexture(const CClientEngineInterfaces& engines, int x, int y, int w, int h,
								 bool bForceUpdate = false)
{
	if (!IsRetail() && !engines.m_pConVars->m_r_updaterefracttexture.GetBool())
		return;

	CMatRenderContextPtr pRenderContext(engines.m_pMaterials);
	ITexture* pTexture = engines.m_pRenderTargets->Target(SS_RT_PowerOfTwoFrameBuffer);
	if (IsPC() || bForceUpdate || (engines.m_pGlobals->framecount != engines.m_pConVars->m_nRefractUpdateFrame))
	{
		// forced or only once per frame
		Rect_t rect;
		rect.x = x;
		rect.y = y;
		rect.width = w;
		rect.height = h;
		pRenderContext->CopyRenderTargetToTextureEx(pTexture, 0, &rect, NULL);

		engines.m_pConVars->m_nRefractUpdateFrame = engines.m_pGlobals->framecount;
	}
	pRenderContext->SetFrameBufferCopyTexture(pTexture);
}

inline void UpdateRefractTexture(const CClientEngineInterfaces& engines, bool bForceUpdate = false)
{
	CMatRenderContextPtr pRenderContext(engines.m_pMaterials);

	int x, y, w, h;
	pRenderContext->GetViewport(x, y, w, h);
	UpdateRefractTexture(engines, x, y, w, h, bForceUpdate);
}

inline void UpdateScreenEffectTexture(const CClientEngineInterfaces& engines, int textureIndex, int x, int y, int w,
									  int h, bool bDestFullScreen = false, Rect_t* pActualRect = NULL)
{
	Rect_t srcRect;
	srcRect.x = x;
	srcRect.y = y;
	srcRect.width = w;
	srcRect.height = h;

	CMatRenderContextPtr pRenderContext(engines.m_pMaterials);
	ITexture* pTexture = engines.m_pRenderTargets->FullFrameFrameBuffer(textureIndex);
	int nSrcWidth, nSrcHeight;
	pRenderContext->GetRenderTargetDimensions(nSrcWidth, nSrcHeight);
	int nDestWidth = pTexture->GetActualWidth();
	int nDestHeight = pTexture->GetActualHeight();

	Rect_t destRect = srcRect;
	if (!bDestFullScreen && (nSrcWidth > nDestWidth || nSrcHeight > nDestHeight))
	{
		// the source and target sizes aren't necessarily the same (specifically in dx7 where
		// nonpow2 rendertargets aren't supported), so lets figure it out here.
		float scaleX = (float)nDestWidth / (float)nSrcWidth;
		float scaleY = (float)nDestHeight / (float)nSrcHeight;
		destRect.x = srcRect.x * scaleX;
		destRect.y = srcRect.y * scaleY;
		destRect.width = srcRect.width * scaleX;
		destRect.height = srcRect.height * scaleY;
		destRect.x = clamp(destRect.x, 0, nDestWidth);
		destRect.y = clamp(destRect.y, 0, nDestHeight);
		destRect.width = clamp(destRect.width, 0, nDestWidth - destRect.x);
		destRect.height = clamp(destRect.height, 0, nDestHeight - destRect.y);
	}

	pRenderContext->CopyRenderTargetToTextureEx(pTexture, 0, &srcRect, bDestFullScreen ? NULL : &destRect);
	pRenderContext->SetFrameBufferCopyTexture(pTexture, textureIndex);

	if (pActualRect)
	{
		pActualRect->x = destRect.x;
		pActualRect->y = destRect.y;
		pActualRect->width = destRect.width;
		pActualRect->height = destRect.height;
	}
}

// Draws the screen effect
inline void DrawScreenEffectMaterial(const CClientEngineInterfaces& engines, IMaterial* pMaterial, int x, int y, int w,
									 int h)
{
	Rect_t actualRect;
	UpdateScreenEffectTexture(engines, 0, x, y, w, h, false, &actualRect);
	ITexture* pTexture = engines.m_pRenderTargets->FullFrameFrameBuffer(0);

	CMatRenderContextPtr pRenderContext(engines.m_pMaterials);

	pRenderContext->DrawScreenSpaceRectangle(pMaterial, x, y, w, h, actualRect.x, actualRect.y,
											 actualRect.x + actualRect.width - 1, actualRect.y + actualRect.height - 1,
											 pTexture->GetActualWidth(), pTexture->GetActualHeight());
}

inline void UpdateScreenEffectTexture(const CClientEngineInterfaces& engines)
{
	const CViewSetup* pViewSetup = engines.m_pViewRender->GetViewSetup();
	UpdateScreenEffectTexture(engines, 0, pViewSetup->x, pViewSetup->y, pViewSetup->width, pViewSetup->height);
}

void UpdateFullScreenDepthTexture(const CClientEngineInterfaces& engines);
