//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
#include "materialsystem/itexture.h"

#include "shell_render_targets.h"
#include "shell_client_engine_interfaces.h"

CRenderTargetsClient::CRenderTargetsClient(const CClientEngineInterfaces& engines)
	: m_Engines(engines)
	, m_ReleaseFn(this, &ReleaseFn_t::Trampoline<CRenderTargetsClient, &CRenderTargetsClient::Release>)
{
}

// The material system's release function: the declared references, dropped.
void CRenderTargetsClient::Release()
{
	m_Store.Release();
}

int32_t CRenderTargetsClient::RenderTargetDeclare(int32_t nSlot, const char* pszName, const char* pszGroup,
												  int32_t bComplain, int32_t nCreationFlags, int32_t nReleaseOrder)
{
	m_Store.SetReleaseFunc(m_ReleaseFn.Get());
	return m_Store.Declare(nSlot, pszName, pszGroup, bComplain, nCreationFlags, nReleaseOrder) ? 1 : 0;
}

ITexture* CRenderTargetsClient::FullFrameFrameBuffer(int textureIndex)
{
	if (textureIndex < 0 || textureIndex >= MAX_FB_TEXTURES)
		return NULL;
	return Target(SS_RT_FullFrameFrameBuffer0 + textureIndex);
}

ITexture* CRenderTargetsClient::Target(int nSlot)
{
	return m_Store.Get(nSlot, m_Engines.m_pMaterials);
}
