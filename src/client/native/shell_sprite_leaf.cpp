//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// The leaf sprite renderer's executor and the engine's sprite model.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"
#include "model_types.h"

#include "enginesprite.h"

#include "materialsystem/imaterialvar.h"

#include "viewrender.h"

#include "particles/particles.h"

#include "Sprite.h"

#include "shell_effect_ledger.h"
#include "shell_beam_render.h"

#include "imanaged_areas_client.h"
#include "shell_standalone_client.h"
#include "ss_bitblock.h"

#include <vector>

ConVar r_drawsprites("r_drawsprites", "1", FCVAR_CHEAT);

// Managed decides whether a sprite draws, its blend, colour, axes and quad; this reads the
// sprite model and renderer into a float request and builds the mesh from the float plan.
// A draw native starts crosses into managed once for the plan; a draw managed starts
// plans between FxSpriteDrawBegin and FxSpriteDrawEnd with no crossing back. With no
// managed half nothing draws.

enum
{
	SS_SPRITE_IN_DRAW_SPRITES = 0,
	SS_SPRITE_IN_WIREFRAME_MODE,
	SS_SPRITE_IN_ORIENTATION,
	SS_SPRITE_IN_LEFT,
	SS_SPRITE_IN_RIGHT,
	SS_SPRITE_IN_UP,
	SS_SPRITE_IN_DOWN,
	SS_SPRITE_IN_WIDTH,
	SS_SPRITE_IN_HEIGHT,
	SS_SPRITE_IN_ORIGIN,
	SS_SPRITE_IN_EFFECT_ORIGIN = SS_SPRITE_IN_ORIGIN + 3,
	SS_SPRITE_IN_ANGLES = SS_SPRITE_IN_EFFECT_ORIGIN + 3,
	SS_SPRITE_IN_RENDER_MODE = SS_SPRITE_IN_ANGLES + 3,
	SS_SPRITE_IN_RENDER_FX,
	SS_SPRITE_IN_ALPHA,
	SS_SPRITE_IN_R,
	SS_SPRITE_IN_G,
	SS_SPRITE_IN_B,
	SS_SPRITE_IN_SCALE,
	SS_SPRITE_IN_WORLD_SPACE_SCALE,
	SS_SPRITE_IN_HDR_COLOR_SCALE,
	SS_SPRITE_IN_BLEND,
	SS_SPRITE_IN_VIEW_FORWARD,
	SS_SPRITE_IN_VIEW_RIGHT = SS_SPRITE_IN_VIEW_FORWARD + 3,
	SS_SPRITE_IN_VIEW_UP = SS_SPRITE_IN_VIEW_RIGHT + 3,
	SS_SPRITE_IN_GLOW_PROXY_SIZE = SS_SPRITE_IN_VIEW_UP + 3,
	// The renderer's pixel visibility handle, its int bits in the float.
	SS_SPRITE_IN_QUERY_HANDLE,
	SS_SPRITE_IN_FLOATS,
};

COMPILE_TIME_ASSERT(SS_SPRITE_IN_FLOATS == 39);

enum
{
	SS_SPRITE_OUT_DRAWN = 0,
	SS_SPRITE_OUT_SET_BLEND,
	SS_SPRITE_OUT_BLEND,
	SS_SPRITE_OUT_QUAD,
	SS_SPRITE_OUT_WIREFRAME,
	SS_SPRITE_OUT_RGBA,
	SS_SPRITE_OUT_CORNERS = SS_SPRITE_OUT_RGBA + 4,
	SS_SPRITE_OUT_HDR_COLOR_SCALE = SS_SPRITE_OUT_CORNERS + 12,
	// The handle as the query left it, stored back on the renderer.
	SS_SPRITE_OUT_QUERY_HANDLE,
	SS_SPRITE_OUT_FLOATS,
};

COMPILE_TIME_ASSERT(SS_SPRITE_OUT_FLOATS == 23);

static float HandleBits(pixelvis_handle_t h)
{
	float f;
	memcpy(&f, &h, sizeof(f));
	return f;
}

static pixelvis_handle_t HandleOf(float f)
{
	pixelvis_handle_t h;
	memcpy(&h, &f, sizeof(h));
	return h;
}

COMPILE_TIME_ASSERT(sizeof(pixelvis_handle_t) == sizeof(float));

// Where the direct path's plan stores the query handle back. Sprites draw one at a time on
// the render thread, so one slot is enough; NULL outside a plan.
static pixelvis_handle_t* s_pPlanQuery = NULL;

// The blend the last request was gathered under.
static float s_flDirectBlend = 0.0f;

static void PutVector(float* p, const Vector& v)
{
	p[0] = v.x;
	p[1] = v.y;
	p[2] = v.z;
}

static unsigned int s_nHDRColorScaleCache = 0;

// What the draw needs that the plan does not carry.
struct SpritePending_t
{
	CEngineSprite* pSprite;
	const model_t* pModel;
	IClientEntity* pEntity;
	float flFrame;
	int nRenderMode;
};

// False when there is nothing to plan (no sprite model, no managed half).
static bool GatherSprite(IClientEntity* entity, const model_t* model, const Vector& origin, const QAngle& angles,
						 float frame, IClientEntity* attachedto, int attachmentindex, int rendermode, int renderfx,
						 int alpha, int r, int g, int b, float scale, bool bWorldSpaceScale, float flHDRColorScale,
						 float flGlowProxySize, float* in, SpritePending_t& pending)
{
	if (!model || modelinfo->GetModelType(model) != mod_sprite)
		return false;

	CEngineSprite* psprite = (CEngineSprite*)modelinfo->GetModelExtraData(model);
	if (!psprite)
		return false;

	Vector effect_origin;
	VectorCopy(origin, effect_origin);

	if (attachedto)
	{
		QAngle temp;
		attachedto->GetAttachment(attachmentindex, effect_origin, temp);
	}

	in[SS_SPRITE_IN_DRAW_SPRITES] = (float)r_drawsprites.GetInt();
	in[SS_SPRITE_IN_WIREFRAME_MODE] = ShouldDrawInWireFrameMode() ? 1.0f : 0.0f;
	in[SS_SPRITE_IN_ORIENTATION] = (float)psprite->GetOrientation();
	in[SS_SPRITE_IN_LEFT] = psprite->GetLeft();
	in[SS_SPRITE_IN_RIGHT] = psprite->GetRight();
	in[SS_SPRITE_IN_UP] = psprite->GetUp();
	in[SS_SPRITE_IN_DOWN] = psprite->GetDown();
	in[SS_SPRITE_IN_WIDTH] = (float)modelinfo->GetModelSpriteWidth(model);
	in[SS_SPRITE_IN_HEIGHT] = (float)modelinfo->GetModelSpriteHeight(model);
	PutVector(in + SS_SPRITE_IN_ORIGIN, origin);
	PutVector(in + SS_SPRITE_IN_EFFECT_ORIGIN, effect_origin);
	in[SS_SPRITE_IN_ANGLES + 0] = angles.x;
	in[SS_SPRITE_IN_ANGLES + 1] = angles.y;
	in[SS_SPRITE_IN_ANGLES + 2] = angles.z;
	in[SS_SPRITE_IN_RENDER_MODE] = (float)rendermode;
	in[SS_SPRITE_IN_RENDER_FX] = (float)renderfx;
	in[SS_SPRITE_IN_ALPHA] = (float)alpha;
	in[SS_SPRITE_IN_R] = (float)r;
	in[SS_SPRITE_IN_G] = (float)g;
	in[SS_SPRITE_IN_B] = (float)b;
	in[SS_SPRITE_IN_SCALE] = scale;
	in[SS_SPRITE_IN_WORLD_SPACE_SCALE] = bWorldSpaceScale ? 1.0f : 0.0f;
	in[SS_SPRITE_IN_HDR_COLOR_SCALE] = flHDRColorScale;
	// The render's blend, or while managed prepares a view's draw batch, the one that
	// entity's DrawModel will find.
	in[SS_SPRITE_IN_BLEND] = SourceSharp_StandaloneDrawBatchBlend(entity, render->GetBlend());
	s_flDirectBlend = in[SS_SPRITE_IN_BLEND];
	PutVector(in + SS_SPRITE_IN_VIEW_FORWARD, CurrentViewForward());
	PutVector(in + SS_SPRITE_IN_VIEW_RIGHT, CurrentViewRight());
	PutVector(in + SS_SPRITE_IN_VIEW_UP, CurrentViewUp());
	in[SS_SPRITE_IN_GLOW_PROXY_SIZE] = flGlowProxySize;

	pending.pSprite = psprite;
	pending.pModel = model;
	pending.pEntity = entity;
	pending.flFrame = frame;
	pending.nRenderMode = rendermode;
	return true;
}

// Carry out a plan: the blend, then the quad.
static int ExecuteSprite(const SpritePending_t& pending, const float* out)
{
	if (out[SS_SPRITE_OUT_SET_BLEND] != 0.0f)
		render->SetBlend(out[SS_SPRITE_OUT_BLEND]);

	if (out[SS_SPRITE_OUT_QUAD] != 0.0f)
	{
		IMaterial* material = pending.pSprite->GetMaterial((RenderMode_t)pending.nRenderMode, pending.flFrame);
		if (material)
		{
			CMatRenderContextPtr pRenderContext(materials);
			if (out[SS_SPRITE_OUT_WIREFRAME] != 0.0f)
			{
				IMaterial* pMaterial = materials->FindMaterial("debug/debugspritewireframe", TEXTURE_GROUP_OTHER);
				pRenderContext->Bind(pMaterial, NULL);
			}
			else
			{
				pRenderContext->Bind(material, (IClientRenderable*)pending.pEntity);
			}

			IMaterialVar* pHDRColorScaleVar = material->FindVarFast("$HDRCOLORSCALE", &s_nHDRColorScaleCache);
			if (pHDRColorScaleVar)
			{
				const float flHDR = out[SS_SPRITE_OUT_HDR_COLOR_SCALE];
				pHDRColorScaleVar->SetVecValue(flHDR, flHDR, flHDR);
			}

			unsigned char color[4];
			for (int i = 0; i < 4; ++i)
				color[i] = (unsigned char)out[SS_SPRITE_OUT_RGBA + i];

			float flMinU, flMinV, flMaxU, flMaxV;
			pending.pSprite->GetTexCoordRange(&flMinU, &flMinV, &flMaxU, &flMaxV);
			const float uv[4][2] = {{flMinU, flMaxV}, {flMinU, flMinV}, {flMaxU, flMinV}, {flMaxU, flMaxV}};

			IMesh* pMesh = pRenderContext->GetDynamicMesh();
			CMeshBuilder meshBuilder;
			meshBuilder.Begin(pMesh, MATERIAL_QUADS, 1);
			for (int i = 0; i < 4; ++i)
			{
				meshBuilder.Color4ubv(color);
				meshBuilder.TexCoord2f(0, uv[i][0], uv[i][1]);
				meshBuilder.Position3fv(out + SS_SPRITE_OUT_CORNERS + i * 3);
				meshBuilder.AdvanceVertex();
			}
			meshBuilder.End();
			pMesh->Draw();

			g_SourceSharpEffects.nSpriteQuads++;
		}
	}

	return out[SS_SPRITE_OUT_DRAWN] != 0.0f ? 1 : 0;
}

// A draw native starts: asks the managed planner once.
int SourceSharp_PlanSprite(pixelvis_handle_t* pQuery, IClientEntity* entity, const model_t* model, const Vector& origin,
						   const QAngle& angles, float frame, IClientEntity* attachedto, int attachmentindex,
						   int rendermode, int renderfx, int alpha, int r, int g, int b, float scale,
						   bool bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize)
{
	IManagedFxClient* pFx = ManagedFxClient();
	if (!pFx)
		return 0;

	float in[SS_SPRITE_IN_FLOATS];
	SpritePending_t pending;
	if (!GatherSprite(entity, model, origin, angles, frame, attachedto, attachmentindex, rendermode, renderfx, alpha, r,
					  g, b, scale, bWorldSpaceScale, flHDRColorScale, flGlowProxySize, in, pending))
		return 0;
	in[SS_SPRITE_IN_QUERY_HANDLE] = HandleBits(pQuery ? *pQuery : 0);

	float out[SS_SPRITE_OUT_FLOATS];
	const int32_t nPlanned = pFx->SpritePlan(in, SS_SPRITE_IN_FLOATS, out, SS_SPRITE_OUT_FLOATS);

	if (nPlanned != SS_SPRITE_OUT_FLOATS)
		return 0;
	if (pQuery)
		*pQuery = HandleOf(out[SS_SPRITE_OUT_QUERY_HANDLE]);

	return ExecuteSprite(pending, out);
}

// A draw managed starts plans between these two calls. Begin reads the request and keeps
// the query handle; End carries the plan out, or drops the draw when handed NULL.
static SpritePending_t s_DirectPending;
static bool s_bDirectPending = false;

int SourceSharp_SpriteDrawBegin(pixelvis_handle_t* pQuery, IClientEntity* entity, const model_t* model,
								const Vector& origin, const QAngle& angles, float frame, int rendermode, int renderfx,
								int alpha, int r, int g, int b, float scale, bool bWorldSpaceScale,
								float flHDRColorScale, float flGlowProxySize, float* pIn)
{
	s_bDirectPending = false;
	s_pPlanQuery = NULL;

	if (!pIn ||
		!GatherSprite(entity, model, origin, angles, frame, NULL, 0, rendermode, renderfx, alpha, r, g, b, scale,
					  bWorldSpaceScale, flHDRColorScale, flGlowProxySize, pIn, s_DirectPending))
		return 0;
	pIn[SS_SPRITE_IN_QUERY_HANDLE] = HandleBits(pQuery ? *pQuery : 0);

	s_bDirectPending = true;
	s_pPlanQuery = pQuery;
	return SS_SPRITE_IN_FLOATS;
}

// The direct path's handle, as the plan left it, back on the renderer.
static void StorePlanQuery(const float* pOut)
{
	if (s_pPlanQuery && pOut)
		*s_pPlanQuery = HandleOf(pOut[SS_SPRITE_OUT_QUERY_HANDLE]);
}

int32_t SourceSharp_FxSpriteDrawEnd(const float* pOut)
{
	const bool bPending = s_bDirectPending;
	StorePlanQuery(pOut);
	s_bDirectPending = false;
	s_pPlanQuery = NULL;

	if (!bPending || !pOut)
		return 0;

	return ExecuteSprite(s_DirectPending, pOut);
}

// The batched path: a plan kept against its entity with its blend, carried out when the
// entity's DrawModel comes round.
struct SpriteStored_t
{
	SpritePending_t pending;
	float out[SS_SPRITE_OUT_FLOATS];
	float flBlend;
};

static CUtlMap<IClientEntity*, SpriteStored_t> s_StoredSprites(DefLessFunc(IClientEntity*));

int32_t SourceSharp_FxSpriteDrawStore(const float* pOut)
{
	const bool bPending = s_bDirectPending;
	StorePlanQuery(pOut);
	s_bDirectPending = false;
	s_pPlanQuery = NULL;

	if (!bPending || !pOut)
		return 0;

	SpriteStored_t stored;
	stored.pending = s_DirectPending;
	memcpy(stored.out, pOut, sizeof(stored.out));
	stored.flBlend = s_flDirectBlend;
	s_StoredSprites.InsertOrReplace(s_DirectPending.pEntity, stored);
	return 1;
}

int SourceSharp_SpriteExecuteStored(IClientEntity* pEntity, float flBlendNow)
{
	const unsigned short i = s_StoredSprites.Find(pEntity);
	if (i == s_StoredSprites.InvalidIndex())
		return -2;
	const SpriteStored_t stored = s_StoredSprites[i];
	s_StoredSprites.RemoveAt(i);
	if (stored.flBlend != flBlendNow)
		return -1;

	return ExecuteSprite(stored.pending, stored.out);
}

void SourceSharp_SpriteStoredClear()
{
	s_StoredSprites.RemoveAll();
}

// The stock proxy size, and a scale already in multiples of the sprite's size.
int C_SpriteRenderer::DrawSprite(IClientEntity* entity, const model_t* model, const Vector& origin,
								 const QAngle& angles, float frame, IClientEntity* attachedto, int attachmentindex,
								 int rendermode, int renderfx, int alpha, int r, int g, int b, float scale,
								 float flHDRColorScale)
{
	return SourceSharp_PlanSprite(&m_queryHandle, entity, model, origin, angles, frame, attachedto, attachmentindex,
								  rendermode, renderfx, alpha, r, g, b, scale, /*bWorldSpaceScale=*/false,
								  flHDRColorScale, PIXELVIS_DEFAULT_PROXY_SIZE);
}

// Virtual in Sprite.h, so C_LocalTempEntity's vtable names it. The glow blend is decided
// inside the managed plan.
float C_SpriteRenderer::GlowBlend(CEngineSprite*, const Vector&, int, int, int, float*)
{
	return 0.0f;
}

// The engine's sprite model. The engine allocates it (enginesprite.h layout) and has the
// client fill it: one procedural material per render mode from the sprite's .vmt, its
// size, frame count, orientation and the quad's extents.

static unsigned int s_nSpriteFrameCache;
static unsigned int s_nSpriteOrientationCache;
static unsigned int s_nSpriteOriginCache;

// kRenderNone and kRenderEnvironmental draw nothing and get no material.
static bool SpriteModeHasMaterial(int nMode)
{
	return nMode != kRenderNone && nMode != kRenderEnvironmental;
}

// Managed resolves the sprite's material name and loads its .vmt, answering both in one
// block: the name NUL-terminated, then the .vmt's root in preorder, each key a type byte,
// its name, its value by type and a uint32 child count. Native rebuilds the KeyValues.
enum
{
	kSpriteBlockBytes = 64 * 1024,
	kSpriteMaxDepth = 128
};

namespace
{
struct SpriteBlockReader
{
	const uint8_t* m_p;
	const uint8_t* m_pEnd;
	bool m_bOk;

	const char* CString()
	{
		const uint8_t* pNul = m_p;
		while (pNul < m_pEnd && *pNul)
			pNul++;
		if (pNul >= m_pEnd)
		{
			m_bOk = false;
			return "";
		}
		const char* psz = (const char*)m_p;
		m_p = pNul + 1;
		return psz;
	}

	bool Bytes(void* pOut, int n)
	{
		if (m_pEnd - m_p < n)
			return m_bOk = false;
		memcpy(pOut, m_p, n);
		m_p += n;
		return true;
	}
};

// One key and its children, or NULL when the block is malformed.
KeyValues* ReadSpriteKey(SpriteBlockReader& r, int nDepth)
{
	uint8_t nType = 0;
	if (nDepth > kSpriteMaxDepth || !r.Bytes(&nType, 1))
		return NULL;
	const char* pszName = r.CString();
	if (!r.m_bOk)
		return NULL;

	KeyValues* kv = new KeyValues(pszName);
	switch (nType)
	{
		case KeyValues::TYPE_STRING:
			kv->SetStringValue(r.CString());
			break;
		case KeyValues::TYPE_INT:
		{
			int32_t n = 0;
			if (r.Bytes(&n, 4))
				kv->SetInt(NULL, n);
			break;
		}
		case KeyValues::TYPE_FLOAT:
		{
			float fl = 0;
			if (r.Bytes(&fl, 4))
				kv->SetFloat(NULL, fl);
			break;
		}
		case KeyValues::TYPE_UINT64:
		{
			uint64_t u = 0;
			if (r.Bytes(&u, 8))
				kv->SetUint64(NULL, u);
			break;
		}
		case KeyValues::TYPE_NONE:
			break;
		default:
			r.m_bOk = false;
			break;
	}

	uint32_t nChildren = 0;
	r.Bytes(&nChildren, 4);
	for (uint32_t i = 0; r.m_bOk && i < nChildren; i++)
	{
		KeyValues* pChild = ReadSpriteKey(r, nDepth + 1);
		if (pChild)
			kv->AddSubKey(pChild);
	}
	if (!r.m_bOk)
	{
		kv->deleteThis();
		return NULL;
	}
	return kv;
}
} // namespace

bool CEngineSprite::Init(const char* pName)
{
	m_VideoMaterial = NULL;
	m_width = m_height = m_numFrames = 1;
	for (int nMode = 0; nMode < kRenderModeCount; nMode++)
		m_material[nMode] = NULL;

	IManagedMaterialFilesClient* pFiles = ManagedMaterialFilesClient();
	if (!pFiles)
	{
		Warning("[SourceSharp] sprite %s: no managed load to read its material\n", pName);
		return false;
	}

	std::vector<uint8_t> block(ss::kBitBlockHeaderBytes + kSpriteBlockBytes);
	const int nBytes = pFiles->SpriteMaterial(pName, block.data(), (int32_t)block.size());
	if (nBytes == -2)
		Warning("[SourceSharp] sprite %s: its material file is over %d bytes as a block; not loaded\n", pName,
				(int)kSpriteBlockBytes);
	if (nBytes < 0)
		return false;

	const uint8_t* pPayload = block.data() + ss::kBitBlockHeaderBytes;
	SpriteBlockReader head = {pPayload, pPayload + nBytes, true};
	char szMaterial[MAX_PATH];
	Q_strncpy(szMaterial, head.CString(), sizeof(szMaterial));
	const uint8_t* pTree = head.m_p;
	if (!head.m_bOk)
		return false;

	for (int nMode = 0; nMode < kRenderModeCount; nMode++)
	{
		if (!SpriteModeHasMaterial(nMode))
			continue;

		SpriteBlockReader r = {pTree, pPayload + nBytes, true};
		KeyValues* pModeVmt = ReadSpriteKey(r, 0);
		if (!pModeVmt)
		{
			Warning("[SourceSharp] sprite %s: its material block is malformed\n", pName);
			return false;
		}
		pModeVmt->SetInt("$spriteRenderMode", nMode);

		char szProcedural[MAX_PATH];
		Q_snprintf(szProcedural, sizeof(szProcedural), "%s_rendermode_%d", szMaterial, nMode);
		m_material[nMode] =
			g_pMaterialSystem->FindProceduralMaterial(szProcedural, TEXTURE_GROUP_CLIENT_EFFECTS, pModeVmt);
		if (!m_material[nMode])
			return false;
		m_material[nMode]->IncrementReferenceCount();
	}

	IMaterial* pFirst = m_material[0];
	m_width = pFirst->GetMappingWidth();
	m_height = pFirst->GetMappingHeight();
	m_numFrames = pFirst->GetNumAnimationFrames();

	IMaterialVar* pOrientation = pFirst->FindVarFast("$spriteorientation", &s_nSpriteOrientationCache);
	m_orientation = pOrientation ? pOrientation->GetIntValue() : C_SpriteRenderer::SPR_VP_PARALLEL_UPRIGHT;

	// The quad's anchor as a fraction of its size: $spriteorigin when a vector, else the centre.
	float flAnchorX = 0.5f, flAnchorY = 0.5f;
	IMaterialVar* pOrigin = pFirst->FindVarFast("$spriteorigin", &s_nSpriteOriginCache);
	if (pOrigin && pOrigin->GetType() == MATERIAL_VAR_TYPE_VECTOR)
	{
		float rgflAnchor[3];
		pOrigin->GetVecValue(rgflAnchor, 3);
		flAnchorX = rgflAnchor[0];
		flAnchorY = rgflAnchor[1];
	}

	left = -m_width * flAnchorX;
	up = m_height * flAnchorY;
	right = m_width + left;
	down = up - m_height;
	return true;
}

void CEngineSprite::Shutdown()
{
	UnloadMaterial();
}

bool CEngineSprite::IsVideo()
{
	return m_VideoMaterial != NULL;
}

// Half a texel in from each edge, so the quad's border samples its own texels.
void CEngineSprite::GetTexCoordRange(float* pMinU, float* pMinV, float* pMaxU, float* pMaxV)
{
	*pMinU = 0.5f * (m_width != 0 ? 1.0f / m_width : 1.0f);
	*pMinV = 0.5f * (m_height != 0 ? 1.0f / m_height : 1.0f);
	*pMaxU = 1.0f - *pMinU;
	*pMaxV = 1.0f - *pMinV;
}

void CEngineSprite::SetFrame(RenderMode_t nRenderMode, int nFrame)
{
	IMaterial* pMaterial = m_material[nRenderMode];
	if (!pMaterial)
		return;
	if (IMaterialVar* pFrame = pMaterial->FindVarFast("$frame", &s_nSpriteFrameCache))
		pFrame->SetIntValue(nFrame);
}

IMaterial* CEngineSprite::GetMaterial(RenderMode_t nRenderMode, int nFrame)
{
	if (!SpriteModeHasMaterial(nRenderMode))
		return NULL;
	SetFrame(nRenderMode, nFrame);
	return m_material[nRenderMode];
}

int CEngineSprite::GetOrientation()
{
	return m_orientation;
}

void CEngineSprite::UnloadMaterial()
{
	for (int nMode = 0; nMode < kRenderModeCount; nMode++)
	{
		if (m_material[nMode])
		{
			m_material[nMode]->DecrementReferenceCount();
			m_material[nMode] = NULL;
		}
	}
}
