//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The view_scene.cpp names the rest of client.so still imports.
// SS_REASON c  static-init registration

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"

#include "materialsystem/imaterialvar.h"

#include "materialsystem/itexture.h"

#include "client_entity.h"

#include "shell_render_targets.h"
#include "view_frustum_transform.h"
#include "shell_client_engine_interfaces.h"
#include "shell_convars_client.h"

// The depth buffer copied into the full-frame depth texture, marked valid,
// and r_depthoverlay's debug quad over it. Nothing without ps_2_b.
void UpdateFullScreenDepthTexture(const CClientEngineInterfaces& engines)
{
	if (!engines.m_pHardwareConfig->SupportsPixelShaders_2_b())
		return;

	ITexture* pDepthTex = engines.m_pRenderTargets->Target(SS_RT_FullFrameDepth);
	CMatRenderContextPtr pRenderContext(engines.m_pMaterials);
	pRenderContext->CopyRenderTargetToTextureEx(pDepthTex, 0, NULL, NULL);
	pRenderContext->SetFullScreenDepthTextureValidityFlag(true);

	if (!engines.m_pConVars->m_r_depthoverlay.GetBool())
		return;

	IMaterial* pMaterial = engines.m_pMaterials->FindMaterial("debug/showz", TEXTURE_GROUP_OTHER, true);
	pMaterial->IncrementReferenceCount();
	IMaterialVar* pBaseTexture = pMaterial->FindVar("$basetexture", NULL, false);
	IMaterialVar* pDepthInAlpha = pMaterial->FindVar("$ALPHADEPTH", NULL, false);
	pDepthInAlpha->SetIntValue(1);
	pBaseTexture->SetTextureValue(pDepthTex);
	pRenderContext->OverrideDepthEnable(true, false);
	pRenderContext->DrawScreenSpaceQuad(pMaterial);
	pRenderContext->OverrideDepthEnable(false, true);
	pMaterial->DecrementReferenceCount();
}
