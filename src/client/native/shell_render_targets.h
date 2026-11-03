//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The render-target store: managed code declares each slot's texture, group and
// release order; the references are found on a slot's first use and dropped by
// the release function the material system calls on a mode change or device loss.

#pragma once

#include "render_target_store.h"
#include "bound_callback.h"

class CClientEngineInterfaces;

class CRenderTargetsClient final
{
public:
	explicit CRenderTargetsClient(const CClientEngineInterfaces& engines);

	CRenderTargetsClient(const CRenderTargetsClient&) = delete;
	CRenderTargetsClient& operator=(const CRenderTargetsClient&) = delete;

	int32_t RenderTargetDeclare(int32_t nSlot, const char* pszName, const char* pszGroup, int32_t bComplain,
								int32_t nCreationFlags, int32_t nReleaseOrder);

	// The texture in a slot (SS_RT_*), or NULL for a slot nobody declared.
	ITexture* Target(int nSlot);

	// An index past the four frame buffers is no texture (stock indexed past its array).
	ITexture* FullFrameFrameBuffer(int textureIndex);

private:
	typedef CBoundCallback<void()> ReleaseFn_t;

	void Release();

	const CClientEngineInterfaces& m_Engines;
	CRenderTargetStore m_Store;
	ReleaseFn_t m_ReleaseFn;
};
