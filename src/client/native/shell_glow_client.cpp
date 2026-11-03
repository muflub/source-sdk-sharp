//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// Carries out the managed glow overlays (GlowOverlaySystem): material lookup by
// name and group (no reference taken), the GlowDrawOverlays plan op (hands the
// view to IManagedGlowClient::DrawOverlays and runs the commands it answers),
// and the GlowUpdateSkyOverlays op.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "view.h"
#include "iviewrender.h"
#include "view_shared.h"
#include "materialsystem/imesh.h"

#include "materialsystem/imaterialvar.h"
#include "clienteffectprecachesystem.h"

#include "client_entity.h"
#include "imanaged_areas_client.h"

#include "shell_glow_client.h"
#include "shell_effects_list_client.h"
#include "shell_view_cameras.h"
#include "shell_client_engine_interfaces.h"
#include "shell_convars_client.h"

namespace
{

float FloatOf(int32_t nBits)
{
	float f;
	memcpy(&f, &nBits, sizeof(f));
	return f;
}

} // namespace

void CGlowOverlaysClient::Execute(const int32_t* pCmds, int nWords)
{
	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	bool bSaved = false;
	int i = 0;
	while (i < nWords)
	{
		const int32_t nKind = pCmds[i];
		if (nKind == SS_GLOW_CMD_CLIP && i + 2 <= nWords)
		{
			const int32_t nMode = pCmds[i + 1];
			if (nMode == 0)
				bSaved = pRenderContext->EnableClipping(true);
			else
				pRenderContext->EnableClipping(nMode == 1 ? false : bSaved);
			i += 2;
		}
		else if (nKind == SS_GLOW_CMD_HDRSCALE && i + 3 <= nWords)
		{
			if (IMaterial* pMaterial = m_Effects.MaterialOf(pCmds[i + 1]))
			{
				if (IMaterialVar* pVar = pMaterial->FindVarFast("$hdrcolorscale", &m_nHDRColorScaleCache))
					pVar->SetFloatValue(FloatOf(pCmds[i + 2]));
			}
			i += 3;
		}
		else if (nKind == SS_GLOW_CMD_QUAD && i + SS_GLOW_QUAD_WORDS <= nWords)
		{
			IMaterial* pMaterial = m_Effects.MaterialOf(pCmds[i + 1]);
			IMesh* pMesh = pRenderContext->GetDynamicMesh(false, 0, 0, pMaterial);
			CMeshBuilder builder;
			builder.Begin(pMesh, MATERIAL_QUADS, 1);
			for (int v = 0; v < 4; v++)
			{
				const int32_t* p = pCmds + i + 2 + v * 8;
				const float pos[3] = {FloatOf(p[0]), FloatOf(p[1]), FloatOf(p[2])};
				builder.Position3fv(pos);
				builder.Color4f(FloatOf(p[3]), FloatOf(p[4]), FloatOf(p[5]), 1);
				builder.TexCoord2f(0, FloatOf(p[6]), FloatOf(p[7]));
				builder.AdvanceVertex();
			}
			builder.End(false, true);
			i += SS_GLOW_QUAD_WORDS;
		}
		else if (nKind == SS_GLOW_CMD_WIREQUAD && i + SS_GLOW_WIREQUAD_WORDS <= nWords)
		{
			IMaterial* pMaterial = m_Effects.MaterialOf(pCmds[i + 1]);
			pRenderContext->Bind(pMaterial);
			IMesh* pMesh = pRenderContext->GetDynamicMesh(false, 0, 0, pMaterial);
			CMeshBuilder builder;
			builder.Begin(pMesh, MATERIAL_QUADS, 1);
			for (int v = 0; v < 4; v++)
			{
				const int32_t* p = pCmds + i + 2 + v * 3;
				const float pos[3] = {FloatOf(p[0]), FloatOf(p[1]), FloatOf(p[2])};
				builder.Position3fv(pos);
				builder.Color3f(1.0f, 0.0f, 0.0f);
				builder.AdvanceVertex();
			}
			builder.End(false, true);
			i += SS_GLOW_WIREQUAD_WORDS;
		}
		else
		{
			Warning("[SourceSharp] glow overlays: command %d at word %d of %d is not one; the rest is dropped\n", nKind,
					i, nWords);
			return;
		}
	}
}

int32_t CGlowOverlaysClient::GlowMaterialFind(const char* pszName, const char* pszGroup)
{
	if (!pszName || !pszGroup || !m_Engines.m_pMaterials)
		return 0;
	return m_Effects.MaterialId(m_Engines.m_pMaterials->FindMaterial(pszName, pszGroup));
}

void CGlowOverlaysClient::DrawOverlays(bool bCacheFullSceneState)
{
	IManagedGlowClient* pManaged = m_Areas.m_pGlowClient;
	if (!pManaged || !m_Engines.m_pViewRender)
		return;

	float in[SS_GLOW_VIEW_FLOATS];
	const Vector& origin = m_Cameras.CurrentOrigin();
	const Vector& forward = m_Cameras.CurrentForward();
	in[0] = origin.x;
	in[1] = origin.y;
	in[2] = origin.z;
	in[3] = forward.x;
	in[4] = forward.y;
	in[5] = forward.z;
	in[6] = m_Engines.m_pViewRender->GetViewSetup()->zFar;
	const VPlane* pFrustum = m_Engines.m_pViewRender->GetFrustum();
	for (int p = 0; p < 5; p++)
	{
		in[7 + p * 4] = pFrustum[p].m_Normal.x;
		in[8 + p * 4] = pFrustum[p].m_Normal.y;
		in[9 + p * 4] = pFrustum[p].m_Normal.z;
		in[10 + p * 4] = pFrustum[p].m_Dist;
	}
	in[27] = m_Engines.m_pConVars->ShouldDrawInWireFrameMode() ? 1.0f : 0.0f;
	in[28] = (float)m_DrawSprites.GetInt();

	if (m_Commands.Count() < 2048)
		m_Commands.SetCount(2048);
	int32_t nWords = pManaged->DrawOverlays(in, SS_GLOW_VIEW_FLOATS, bCacheFullSceneState ? 1 : 0, m_Commands.Base(),
											m_Commands.Count());
	if (nWords > m_Commands.Count())
	{
		m_Commands.SetCount(nWords);
		nWords = pManaged->Commands(m_Commands.Base(), m_Commands.Count());
	}
	if (nWords > 0 && nWords <= m_Commands.Count())
		Execute(m_Commands.Base(), nWords);
}

void CGlowOverlaysClient::UpdateSkyOverlays(float flZFar, bool bCacheFullSceneState)
{
	if (IManagedGlowClient* pManaged = m_Areas.m_pGlowClient)
	{
		const Vector& origin = m_Cameras.CurrentOrigin();
		const float in[3] = {origin.x, origin.y, origin.z};
		pManaged->UpdateSkyOverlays(in, flZFar, bCacheFullSceneState ? 1 : 0);
	}
}
