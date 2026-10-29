//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The draw executor for the managed particle sims, and the client's side of the new-style
// particle library. One CManagedParticleEffect per (owner, channel) is registered with the
// particle manager; its Update, SimulateParticles and RenderParticles each ask managed one
// question. The manager reorders its particles, so they pair with managed's by id, never
// by position.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "viewrender.h"
#include "clienteffectprecachesystem.h"

#include "view_scene.h"
#include "iclientmode.h"

#include "util_shared.h"

#include "shell_particle_effect.h"
#include "shell_standalone_client.h"
#include "shell_clientsystem.h"
#include "shell_effect_ledger.h"
#include "imanaged_areas_client.h"

#include "shell_particle_mgr.h"
#include "managed_convar_ref.h"
#include "shell_new_particles.h"
#include "particle_follow_mask.h"
#include "shell_effect_registry.h"
#include "shell_particle_systems.h"
#include "shell_clientdll_refusals.h"
#include "shell_cliententitylist.h"
#include "ss_bitblock.h"

// The view model FOV squash CLocalSpaceEmitter applied (FLE_VIEWMODEL).
extern void FormatViewModelAttachment(Vector& vOrigin, bool bInverse);

using ss::BitBlockBits;
using ss::BitBlockSetBits;
using ss::kBitBlockHeaderBytes;

// Managed's convars, read by name.
static CManagedConVarRef r_DrawParticles("r_drawparticles");
static CManagedConVarRef cl_particle_show_bbox("cl_particle_show_bbox");
static CManagedConVarRef cl_particle_show_bbox_cost("cl_particle_show_bbox_cost");
static CManagedConVarRef cl_particleeffect_aabb_buffer("cl_particleeffect_aabb_buffer");

namespace
{

// The heatwave's quad, with the normal managed computed.
void RenderParticle_ColorSizeNormal(ParticleDraw* pDraw, const Vector& pos, const Vector& color, float alpha,
									float size, const Vector& vNorm)
{
	if (alpha < 0.001f)
		return;

	CMeshBuilder* pBuilder = pDraw->GetMeshBuilder();
	if (!pBuilder)
		return;

	unsigned char ubColor[4];
	ubColor[0] = (unsigned char)RoundFloatToInt(color.x * 254.9f);
	ubColor[1] = (unsigned char)RoundFloatToInt(color.y * 254.9f);
	ubColor[2] = (unsigned char)RoundFloatToInt(color.z * 254.9f);
	ubColor[3] = (unsigned char)RoundFloatToInt(alpha * 254.9f);

	pBuilder->Position3f(pos.x - size, pos.y - size, pos.z);
	pBuilder->Color4ubv(ubColor);
	pBuilder->Normal3fv(vNorm.Base());
	pBuilder->TexCoord2f(0, 0, 1.0f);
	pBuilder->AdvanceVertex();

	pBuilder->Position3f(pos.x - size, pos.y + size, pos.z);
	pBuilder->Color4ubv(ubColor);
	pBuilder->Normal3fv(vNorm.Base());
	pBuilder->TexCoord2f(0, 0, 0);
	pBuilder->AdvanceVertex();

	pBuilder->Position3f(pos.x + size, pos.y + size, pos.z);
	pBuilder->Color4ubv(ubColor);
	pBuilder->Normal3fv(vNorm.Base());
	pBuilder->TexCoord2f(0, 1.0f, 0);
	pBuilder->AdvanceVertex();

	pBuilder->Position3f(pos.x + size, pos.y - size, pos.z);
	pBuilder->Color4ubv(ubColor);
	pBuilder->Normal3fv(vNorm.Base());
	pBuilder->TexCoord2f(0, 1.0f, 1.0f);
	pBuilder->AdvanceVertex();
}

// Kind 4, a tracer's quad: four view-space corners from managed, wound start-minus,
// start-plus, end-plus, end-minus with the texture along the line.
void RenderTracerQuad(ParticleDraw* pDraw, const SourceSharpParticleQuad_t& q)
{
	CMeshBuilder* pBuilder = pDraw->GetMeshBuilder();
	if (!pBuilder)
		return;

	const float color[4] = {q.ColorXYZ[0], q.ColorXYZ[1], q.ColorXYZ[2], q.Alpha};
	const float* corners[4] = {q.PositionXYZ, q.NormalXYZ, q.Corner3XYZ, q.Corner2XYZ};
	const float u[4] = {0.0f, 1.0f, 1.0f, 0.0f};
	const float v[4] = {0.0f, 0.0f, 1.0f, 1.0f};
	for (int i = 0; i < 4; i++)
	{
		pBuilder->Position3f(corners[i][0], corners[i][1], corners[i][2]);
		pBuilder->TexCoord2f(0, u[i], v[i]);
		pBuilder->Color4fv(color);
		pBuilder->AdvanceVertex();
	}
}

// Kind 5, a 3D particle's quad: managed hands the world-space corner, face normal and
// edges; the corners are walked as stock's and taken into the effect's view. Nothing is
// drawn below alpha 0.001.
void RenderAnglesQuad(ParticleDraw* pDraw, const SourceSharpParticleQuad_t& q, const VMatrix& mv)
{
	if (q.Alpha < 0.001f)
		return;
	CMeshBuilder* pBuilder = pDraw->GetMeshBuilder();
	if (!pBuilder)
		return;

	unsigned char ubColor[4];
	ubColor[0] = (unsigned char)RoundFloatToInt(q.ColorXYZ[0] * 254.9f);
	ubColor[1] = (unsigned char)RoundFloatToInt(q.ColorXYZ[1] * 254.9f);
	ubColor[2] = (unsigned char)RoundFloatToInt(q.ColorXYZ[2] * 254.9f);
	ubColor[3] = (unsigned char)RoundFloatToInt(q.Alpha * 254.9f);

	const Vector vWidth(q.Corner2XYZ[0], q.Corner2XYZ[1], q.Corner2XYZ[2]);
	const Vector vHeight(q.Corner3XYZ[0], q.Corner3XYZ[1], q.Corner3XYZ[2]);
	Vector vNorm;
	Vector3DMultiply(mv, Vector(q.NormalXYZ[0], q.NormalXYZ[1], q.NormalXYZ[2]), vNorm);

	Vector corners[4];
	corners[0] = Vector(q.PositionXYZ[0], q.PositionXYZ[1], q.PositionXYZ[2]);
	corners[1] = corners[0] + vWidth;
	corners[2] = corners[1] + vHeight;
	corners[3] = corners[2] - vWidth;
	const CParticleSubTexture* pSub = pDraw->m_pSubTexture;
	const float u[4] = {pSub->m_tCoordMins[0], pSub->m_tCoordMins[0], pSub->m_tCoordMaxs[0], pSub->m_tCoordMaxs[0]};
	const float v[4] = {pSub->m_tCoordMaxs[1], pSub->m_tCoordMins[1], pSub->m_tCoordMins[1], pSub->m_tCoordMaxs[1]};
	for (int i = 0; i < 4; i++)
	{
		Vector vView;
		TransformParticle(mv, corners[i], vView);
		pBuilder->Position3f(vView.x, vView.y, vView.z);
		pBuilder->Color4ubv(ubColor);
		pBuilder->Normal3f(vNorm.x, vNorm.y, vNorm.z);
		pBuilder->TexCoord2f(0, u[i], v[i]);
		pBuilder->AdvanceVertex();
	}
}

// Which view the particle manager is drawing, as a key over its model-view matrix and
// CurrentViewID(). Managed re-reads the view only when the key changes.
int32_t ParticlesViewKey()
{
	const VMatrix& mv = ShellParticleDraw_ModelView();
	const unsigned char* p = (const unsigned char*)&mv;
	uint32_t h = 2166136261u;
	for (size_t i = 0; i < sizeof(VMatrix); i++)
		h = (h ^ p[i]) * 16777619u;
	h = (h ^ (uint32_t)CurrentViewID()) * 16777619u;
	return (int32_t)h;
}

} // namespace

class CManagedParticleEffect : public CShellParticleEffect
{
public:
	CManagedParticleEffect(uint32_t hOwner, int32_t nChannel)
		: m_hOwner(hOwner)
		, m_nChannel(nChannel)
		, m_hMaterial(INVALID_MATERIAL_HANDLE)
		, m_bLit(false)
		, m_nSpaceAttachment(0)
		, m_nSpaceFlags(0)
		, m_flCullRadius(0.0f)
		, m_nFlags(kFlagsConstructed)
		, m_nActive(0)
	{
		SetIdentityMatrix(m_LocalSpace);
		SetIdentityMatrix(m_matRenderable);
		// CParticleEffectBinding's initial box, until managed commits one.
		m_Min.Init(-50, -50, -50);
		m_Max.Init(50, 50, 50);
	}

	// The space managed put this executor in: an entity attachment's, the view model's FOV
	// squash, drawn before the view model, or a singleton drawn at DrawParticleSingletons'
	// place (16 for the 3D sky's call, 8 for the world's).
	enum
	{
		kSpaceLocal = 1,
		kSpaceViewModel = 2,
		kSpaceBeforeViewModel = 4,
		kSpaceSingleton = 8,
		kSpaceSingletonSky = 16
	};

	// The CParticleEffectBinding FLAGS_ this renderable reads from managed's commit.
	enum
	{
		kFlagCameraSpace = 1 << 7,
		kFlagDrawThruLeafSystem = 1 << 8,
		kFlagsConstructed = (1 << 3) | (1 << 11) | (1 << 2) | (1 << 4) | (1 << 7) | (1 << 8) | (1 << 10),
	};

	// Which DrawParticleSingletons call draws this executor, or -1 for none.
	int SingletonPass() const
	{
		return (m_nSpaceFlags & kSpaceSingleton) ? ((m_nSpaceFlags & kSpaceSingletonSky) ? 1 : 0) : -1;
	}

	void DrawAsSingleton() { DrawModel(1); }

	// Managed holds the binding's half of the space; this keeps where it draws.
	void SetSpace(uint32_t hEntity, int32_t nAttachment, int32_t nFlags, SourceSharpParticleFollow_t* pFollow)
	{
		m_hSpace = CBaseHandle((int)(hEntity & ENT_ENTRY_MASK), (int)(hEntity >> NUM_SERIAL_NUM_SHIFT_BITS));
		m_nSpaceAttachment = nAttachment;
		m_nSpaceFlags = nFlags;
		if (pFollow)
			memset(pFollow, 0, sizeof(*pFollow));
		if (nFlags & kSpaceLocal)
			FollowSpace(pFollow);
	}

	// CLocalSpaceEmitter::SetupTransformMatrix: the attachment's matrix is the particles'
	// space, its origin the sort origin. The reading goes to managed.
	void FollowSpace(SourceSharpParticleFollow_t* pOut)
	{
		IClientRenderable* pRenderable = SourceSharp_ClientRenderableFromHandle(m_hSpace);
		if (!pRenderable)
			return;
		matrix3x4_t mat;
		SetIdentityMatrix(mat);
		pRenderable->GetAttachment(m_nSpaceAttachment, mat);
		Vector origin;
		MatrixGetColumn(mat, 3, origin);
		m_LocalSpace = mat;
		SetAbsOrigin(origin);
		if (!pOut)
			return;
		pOut->Followed = 1;
		V_memcpy(pOut->Matrix, mat.Base(), sizeof(pOut->Matrix));
		VectorCopy(origin.Base(), pOut->OriginXYZ);
		IClientUnknown* pUnk = pRenderable->GetIClientUnknown();
		ICollideable* pCollide = pUnk ? pUnk->GetCollideable() : NULL;
		if (pCollide)
		{
			Vector vecSize;
			VectorSubtract(pCollide->OBBMaxs(), pCollide->OBBMins(), vecSize);
			const float scale = vecSize.Length() * 0.5f;
			const Vector vScale(scale, scale, scale);
			pOut->HasBox = 1;
			VectorCopy((origin - vScale).Base(), pOut->MinsXYZ);
			VectorCopy((origin + vScale).Base(), pOut->MaxsXYZ);
		}
	}

	~CManagedParticleEffect()
	{
		if (m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE)
			ClientLeafSystem()->RemoveRenderable(m_hRenderHandle);
	}

	// Its material and cull radius, and into the leaf system.
	bool Start(const char* pszMaterial, float flCullRadius)
	{
		m_hMaterial = ShellParticleMaterial_Find(pszMaterial);
		if (!m_hMaterial)
			return false;
		m_flCullRadius = flCullRadius;
		ClientLeafSystem()->CreateRenderableHandle(this, false);
		return true;
	}

	// The leaf system is told when the cull radius changed.
	void Configure(float flCullRadius)
	{
		if (m_flCullRadius == flCullRadius)
			return;
		m_flCullRadius = flCullRadius;
		if (m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE)
			ClientLeafSystem()->RenderableChanged(m_hRenderHandle);
	}

	// Base colour 0..1, then the ambient and directional lights (position, colour,
	// intensity). The directional light goes to the particle manager before the effect draws,
	// for materials that light on the GPU.
	void SetLights(const float* pLights)
	{
		m_bLit = true;
		m_Directional.m_vPos.Init(pLights[10], pLights[11], pLights[12]);
		m_Directional.m_vColor.Init(pLights[13], pLights[14], pLights[15]);
		m_Directional.m_flIntensity = pLights[16];
	}

	// The effect's material, for a managed question about one of its vars.
	IMaterial* Material() { return ShellParticleMaterial_Material(m_hMaterial); }

	uint32_t Owner() const { return m_hOwner; }

	int32_t Channel() const { return m_nChannel; }

	int32_t SpaceFlags() const { return m_nSpaceFlags; }

	int32_t ActiveParticles() const { return m_nActive; }

	// This frame's read for managed: its place, its material's frame-buffer needs, and the follow.
	void FillFrameIn(SourceSharpParticleFrameIn_t& in)
	{
		memset(&in, 0, sizeof(in));
		in.Owner = m_hOwner;
		in.Channel = m_nChannel;
		VectorCopy(GetAbsOrigin().Base(), in.OriginXYZ);
		in.AnglesPYR[0] = GetAbsAngles().x;
		in.AnglesPYR[1] = GetAbsAngles().y;
		in.AnglesPYR[2] = GetAbsAngles().z;
		IMaterial* pPage = PageMaterial();
		if (pPage)
			in.MaterialFlags =
				(pPage->NeedsPowerOfTwoFrameBufferTexture() ? 1 : 0) | (pPage->NeedsFullFrameBufferTexture() ? 2 : 0);
		if (m_nSpaceFlags & kSpaceLocal)
		{
			SourceSharpParticleFollow_t follow;
			memset(&follow, 0, sizeof(follow));
			FollowSpace(&follow);
			in.Followed = follow.Followed;
			V_memcpy(in.Matrix, follow.Matrix, sizeof(in.Matrix));
			VectorCopy(follow.OriginXYZ, in.FollowOriginXYZ);
			in.HasBox = follow.HasBox;
			VectorCopy(follow.MinsXYZ, in.MinsXYZ);
			VectorCopy(follow.MaxsXYZ, in.MaxsXYZ);
		}
	}

	// Managed's binding after its update: what this answers the leaf system with.
	void Commit(const SourceSharpParticleCommit_t& c)
	{
		m_Min.Init(c.MinXYZ[0], c.MinXYZ[1], c.MinXYZ[2]);
		m_Max.Init(c.MaxXYZ[0], c.MaxXYZ[1], c.MaxXYZ[2]);
		m_nFlags = c.Flags;
		m_nActive = c.Active;
		if (c.Changed && m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE)
			ClientLeafSystem()->RenderableChanged(m_hRenderHandle);
	}

public:
	virtual const Vector& GetRenderOrigin() OVERRIDE { return GetAbsOrigin(); }

	virtual const QAngle& GetRenderAngles() OVERRIDE { return vec3_angle; }

	virtual const matrix3x4_t& RenderableToWorldTransform() OVERRIDE
	{
		SetIdentityMatrix(m_matRenderable);
		PositionMatrix(GetRenderOrigin(), m_matRenderable);
		return m_matRenderable;
	}

	// The world box about the sort origin, grown by the cull radius.
	virtual void GetRenderBounds(Vector& mins, Vector& maxs) OVERRIDE
	{
		const Vector& vSortOrigin = GetAbsOrigin();
		mins = m_Min - vSortOrigin;
		mins.x -= m_flCullRadius;
		mins.y -= m_flCullRadius;
		mins.z -= m_flCullRadius;
		maxs = m_Max - vSortOrigin;
		maxs.x += m_flCullRadius;
		maxs.y += m_flCullRadius;
		maxs.z += m_flCullRadius;
	}

	virtual bool ShouldDraw() OVERRIDE { return (m_nFlags & kFlagDrawThruLeafSystem) != 0; }

	virtual bool IsTransparent() OVERRIDE { return true; }

	virtual int DrawModel(int flags) OVERRIDE;

private:
	IMaterial* PageMaterial()
	{
		if (!m_hMaterial)
			return NULL;
		ShellParticleMaterial_Material(m_hMaterial);
		return m_hMaterial->m_pGroup ? m_hMaterial->m_pGroup->m_pPageMaterial : NULL;
	}

	void RenderStart(VMatrix& tempModel, VMatrix& tempView);
	void RenderEnd(VMatrix& tempModel, VMatrix& tempView);
	void DrawQuads(IMaterial* pMaterial, float flFrameTime, const SourceSharpParticleQuad_t* pQuads, int nQuads,
				   float* pKeys);

	uint32_t m_hOwner;
	int32_t m_nChannel;
	PMaterialHandle m_hMaterial;
	bool m_bLit;
	CParticleLightInfo m_Directional;
	CBaseHandle m_hSpace;
	int32_t m_nSpaceAttachment;
	int32_t m_nSpaceFlags;
	float m_flCullRadius;
	int32_t m_nFlags;
	int32_t m_nActive;
	Vector m_Min;
	Vector m_Max;
	matrix3x4_t m_LocalSpace;
	matrix3x4_t m_matRenderable;
	CUtlVector<uint8_t> m_DrawBuffer;
	CUtlVector<float> m_Keys;
	CUtlVector<uint8_t> m_KeyBuffer;
};

// The particles draw in camera space, so model and view go to identity for the draw (the
// view kept for the user clip planes), and the lit sim hands over its light.
void CManagedParticleEffect::RenderStart(VMatrix& tempModel, VMatrix& tempView)
{
	if (m_nFlags & kFlagCameraSpace)
	{
		CMatRenderContextPtr pRenderContext(materials);
		pRenderContext->GetMatrix(MATERIAL_VIEW, &tempView);
		pRenderContext->GetMatrix(MATERIAL_MODEL, &tempModel);
		pRenderContext->EnableUserClipTransformOverride(true);
		pRenderContext->UserClipTransform(tempView);
		pRenderContext->MatrixMode(MATERIAL_MODEL);
		pRenderContext->LoadIdentity();
		pRenderContext->MatrixMode(MATERIAL_VIEW);
		pRenderContext->LoadIdentity();
	}
	if (m_bLit)
		ShellParticleDraw_SetLight(m_Directional);
}

void CManagedParticleEffect::RenderEnd(VMatrix& tempModel, VMatrix& tempView)
{
	if (m_nFlags & kFlagCameraSpace)
	{
		CMatRenderContextPtr pRenderContext(materials);
		pRenderContext->EnableUserClipTransformOverride(false);
		pRenderContext->MatrixMode(MATERIAL_MODEL);
		pRenderContext->LoadMatrix(tempModel);
		pRenderContext->MatrixMode(MATERIAL_VIEW);
		pRenderContext->LoadMatrix(tempView);
	}
}

// One material's draw: the particle manager bound as proxy argument, quads in managed's
// order, flushed every NUM_PARTICLES_PER_BATCH, each quad's sort key written back.
void CManagedParticleEffect::DrawQuads(IMaterial* pMaterial, float flFrameTime, const SourceSharpParticleQuad_t* pQuads,
									   int nQuads, float* pKeys)
{
	CMatRenderContextPtr pRenderContext(materials);
	pRenderContext->Bind(pMaterial, ShellParticleDraw_BindArg());
	IMesh* pMesh = pRenderContext->GetDynamicMesh(true);
	CMeshBuilder builder;
	builder.Begin(pMesh, MATERIAL_QUADS, NUM_PARTICLES_PER_BATCH * 4);
	ParticleDraw particleDraw;
	particleDraw.Init(&builder, pMaterial, flFrameTime);
	particleDraw.m_pSubTexture = m_hMaterial;
	ParticleDraw* pDraw = &particleDraw;
	const VMatrix& mv = ShellParticleDraw_ModelView();

	int nInBatch = 0;
	for (int i = 0; i < nQuads; i++)
	{
		const SourceSharpParticleQuad_t* pQuad = &pQuads[i];
		float flKey = 0.0f;
		if (pQuad->Kind == 5)
		{
			RenderAnglesQuad(pDraw, *pQuad, mv);
			flKey = pQuad->SortKey;
		}
		else if (pQuad->Kind == 6)
		{
			// The dust motes: already in view space, alpha and size from depth decided by managed.
			RenderParticle_Color255Size(
				pDraw, Vector(pQuad->PositionXYZ[0], pQuad->PositionXYZ[1], pQuad->PositionXYZ[2]),
				Vector(pQuad->ColorXYZ[0], pQuad->ColorXYZ[1], pQuad->ColorXYZ[2]), pQuad->Alpha, pQuad->Size);
			g_SourceSharpEffects.nDustMotes++;
			flKey = pQuad->SortKey;
		}
		else if (pQuad->Kind == 4)
		{
			if (pQuad->Size > 0.0f)
				RenderTracerQuad(pDraw, *pQuad);
			flKey = pQuad->SortKey;
		}
		else if (pQuad->Kind >= 0)
		{
			Vector tPos;
			Vector vPos(pQuad->PositionXYZ[0], pQuad->PositionXYZ[1], pQuad->PositionXYZ[2]);
			if (m_nSpaceFlags & kSpaceLocal)
			{
				// Local to world, then the view model's squash.
				Vector vWorld;
				VectorTransform(vPos, m_LocalSpace, vWorld);
				if (m_nSpaceFlags & kSpaceViewModel)
					FormatViewModelAttachment(vWorld, false);
				vPos = vWorld;
			}
			TransformParticle(mv, vPos, tPos);
			const Vector vColor(pQuad->ColorXYZ[0], pQuad->ColorXYZ[1], pQuad->ColorXYZ[2]);
			const float flAlpha = pQuad->FadeFar > 0.0f
				? pQuad->Alpha * GetAlphaDistanceFade(tPos, pQuad->FadeNear, pQuad->FadeFar)
				: pQuad->Alpha;

			if (pQuad->Kind == 2)
				RenderParticle_Color255SizeNormalAngle(pDraw, tPos, vColor, flAlpha, pQuad->Size, vec3_origin,
													   pQuad->Roll);
			else if (pQuad->Kind == 3)
				RenderParticle_ColorSize(pDraw, tPos, vColor, flAlpha, pQuad->Size);
			else if (pQuad->Kind == 1)
				RenderParticle_ColorSizeNormal(pDraw, tPos, vColor, flAlpha, pQuad->Size,
											   Vector(pQuad->NormalXYZ[0], pQuad->NormalXYZ[1], pQuad->NormalXYZ[2]));
			else
				RenderParticle_ColorSizeAngle(pDraw, tPos, vColor, flAlpha, pQuad->Size, pQuad->Roll);

			flKey = pQuad->FixedSort == 1 ? pQuad->SortKey : pQuad->FixedSort == 2 ? (float)(int)tPos.z : tPos.z;
		}
		pKeys[i] = flKey;

		if (++nInBatch >= NUM_PARTICLES_PER_BATCH)
		{
			builder.End(false, true);
			builder.Begin(pMesh, MATERIAL_QUADS, NUM_PARTICLES_PER_BATCH * 4);
			nInBatch = 0;
		}
	}
	builder.End(false, true);
}

// Managed decides whether it draws, the frame time, the frame-buffer updates and debug
// passes, and lists the quads; this draws them and hands back each sort key.
int CManagedParticleEffect::DrawModel(int flags)
{
	IManagedParticlesClient* pArea = ManagedParticlesClient();
	if (!pArea)
		return 0;

	// Set before managed is asked, because its view read is keyed by it.
	if (m_nFlags & kFlagCameraSpace)
	{
		CMatRenderContextPtr pRenderContext(materials);
		pRenderContext->GetMatrix(MATERIAL_VIEW, &ShellParticleDraw_ModelView());
	}
	else
	{
		ShellParticleDraw_ModelView().Identity();
	}

	SourceSharpParticleDrawIn_t in;
	memset(&in, 0, sizeof(in));
	in.Owner = m_hOwner;
	in.Channel = m_nChannel;
	in.Flags = flags;
	in.ClientModeDraws = g_pClientMode && g_pClientMode->ShouldDrawParticles() ? 1 : 0;
	in.DrawParticles = r_DrawParticles.GetInt() != 0 ? 1 : 0;
	in.Wireframe = ShouldDrawInWireFrameMode() ? 1 : 0;
	in.ShowBBox = cl_particle_show_bbox.GetBool() ? 1 : 0;
	in.ShowBBoxCost = cl_particle_show_bbox_cost.GetInt();
	in.ViewKey = ParticlesViewKey();
	in.FrameTime = gpGlobals->frametime;

	const int nHeader = kBitBlockHeaderBytes + (int)sizeof(SourceSharpParticleDrawOut_t);
	if (m_DrawBuffer.Count() < nHeader + 64 * (int)sizeof(SourceSharpParticleQuad_t))
		m_DrawBuffer.SetCount(nHeader + 64 * (int)sizeof(SourceSharpParticleQuad_t));
	int nResult = pArea->DrawBegin(&in, m_DrawBuffer.Base(), m_DrawBuffer.Count());
	if (nResult > m_DrawBuffer.Count())
	{
		// Managed answered the size it needs before deciding anything; ask again.
		m_DrawBuffer.SetCount(nResult);
		nResult = pArea->DrawBegin(&in, m_DrawBuffer.Base(), m_DrawBuffer.Count());
	}
	if (nResult < nHeader || BitBlockBits(m_DrawBuffer.Base()) / 8 + kBitBlockHeaderBytes > m_DrawBuffer.Count())
		return 0;

	const SourceSharpParticleDrawOut_t* pOut =
		(const SourceSharpParticleDrawOut_t*)(m_DrawBuffer.Base() + kBitBlockHeaderBytes);
	if (pOut->Result == 0)
		return 0;
	if (pOut->Result == 1)
		return 1;

	const int nPayload = BitBlockBits(m_DrawBuffer.Base()) / 8;
	const int nQuads = (nPayload - (int)sizeof(SourceSharpParticleDrawOut_t)) / (int)sizeof(SourceSharpParticleQuad_t);
	const SourceSharpParticleQuad_t* pQuads = (const SourceSharpParticleQuad_t*)(pOut + 1);

	VMatrix mTempModel, mTempView;
	RenderStart(mTempModel, mTempView);

	IMaterial* pPage = PageMaterial();
	if (pOut->RefractUpdate)
		UpdateRefractTexture();
	if (pOut->ScreenUpdate)
		UpdateScreenEffectTexture();

	m_Keys.SetCount(MAX(nQuads, 1));
	m_KeyBuffer.SetCount(kBitBlockHeaderBytes + MAX(nQuads, 1) * (int)sizeof(float));
	const int nPasses = pOut->Wireframe ? 2 : 1;
	const float flFrameTime = pOut->FrameTime;
	const bool bBox = pOut->BBox != 0;
	const int r = pOut->BBoxR, g = pOut->BBoxG, b = pOut->BBoxB, nActive = pOut->Active;
	const Vector vMin(pOut->MinXYZ[0], pOut->MinXYZ[1], pOut->MinXYZ[2]);
	const Vector vMax(pOut->MaxXYZ[0], pOut->MaxXYZ[1], pOut->MaxXYZ[2]);
	for (int nPass = 0; nPass < nPasses; nPass++)
	{
		IMaterial* pMaterial =
			nPass == 0 ? pPage : materials->FindMaterial("debug/debugparticlewireframe", TEXTURE_GROUP_OTHER);
		// The wireframe pass draws the same quads again.
		DrawQuads(pMaterial, flFrameTime, pQuads, nQuads, m_Keys.Base());

		// The sort keys back in one length-prefixed buffer.
		BitBlockSetBits(m_KeyBuffer.Base(), nQuads * (int)sizeof(float) * 8);
		V_memcpy(m_KeyBuffer.Base() + kBitBlockHeaderBytes, m_Keys.Base(), nQuads * sizeof(float));
		pArea->DrawEnd(m_hOwner, m_nChannel, m_KeyBuffer.Base(), m_KeyBuffer.Count());
	}

	if (bBox && debugoverlay)
	{
		const Vector center = (vMin + vMax) / 2;
		debugoverlay->AddBoxOverlay(center, vMin - center, vMax - center, QAngle(0, 0, 0), r, g, b, 16, 0);
		debugoverlay->AddTextOverlayRGB(center, 0, 0, r, g, b, 64, "managed-%d:(%d)", m_nChannel, nActive);
	}

	RenderEnd(mTempModel, mTempView);
	return 1;
}

// The live executors, one per (owner, channel).
static CUtlVector<CManagedParticleEffect*> g_ManagedParticles;

static int ManagedParticles_Find(uint32_t hOwner, int32_t nChannel)
{
	for (int i = 0; i < g_ManagedParticles.Count(); i++)
	{
		if (g_ManagedParticles[i]->Owner() == hOwner && g_ManagedParticles[i]->Channel() == nChannel)
			return i;
	}
	return -1;
}

// An executor released by any route is forgotten through this callback.
static void ManagedParticles_Forget(CShellParticleEffect* pEffect)
{
	for (int i = g_ManagedParticles.Count() - 1; i >= 0; i--)
	{
		if (g_ManagedParticles[i] == pEffect)
			g_ManagedParticles.Remove(i);
	}
}

int32_t SourceSharp_ParticlesCreate(uint32_t hOwner, int32_t nChannel, const char* pszMaterial, float flCullRadius,
									int32_t bAlwaysSimulate)
{
	C_StandaloneEntity* pOwner = SourceSharp_StandaloneFromHandle(hOwner);
	if (!pOwner || !pszMaterial || ManagedParticles_Find(hOwner, nChannel) >= 0)
		return 0;

	CManagedParticleEffect* pEffect = new CManagedParticleEffect(hOwner, nChannel);
	pEffect->SetAbsOrigin(pOwner->GetAbsOrigin());
	pEffect->SetAbsAngles(pOwner->GetAbsAngles());
	if (!pEffect->Start(pszMaterial, flCullRadius))
	{
		pEffect->Release();
		return 0;
	}

	pEffect->SetGoneCallback(ManagedParticles_Forget);
	g_ManagedParticles.AddToTail(pEffect);
	return 1;
}

// An executor for an ownerless effect, placed once at a world point and keyed by the
// no-owner handle and the managed id. Managed releases it when the effect finishes.
int32_t SourceSharp_ParticlesCreateAt(int32_t nId, const char* pszMaterial, const float* pOriginXYZ, float flCullRadius,
									  int32_t bAlwaysSimulate)
{
	if (!pszMaterial || !pOriginXYZ || ManagedParticles_Find(SOURCESHARP_PARTICLE_NO_OWNER, nId) >= 0)
		return 0;

	CManagedParticleEffect* pEffect = new CManagedParticleEffect(SOURCESHARP_PARTICLE_NO_OWNER, nId);
	pEffect->SetAbsOrigin(Vector(pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2]));
	if (!pEffect->Start(pszMaterial, flCullRadius))
	{
		pEffect->Release();
		return 0;
	}

	pEffect->SetGoneCallback(ManagedParticles_Forget);
	g_ManagedParticles.AddToTail(pEffect);
	return 1;
}

// Called at its place in the translucent passes; draws the executors managed put in the
// singleton space.
void DrawParticleSingletons(bool bInSkybox)
{
	const int nPass = bInSkybox ? 1 : 0;
	for (int i = 0; i < g_ManagedParticles.Count(); i++)
	{
		if (g_ManagedParticles[i]->SingletonPass() != nPass)
			continue;
		g_ManagedParticles[i]->DrawAsSingleton();
		g_SourceSharpEffects.nSingletonDraws++;
	}
}

// 1 when the executor was found.
int32_t SourceSharp_ParticlesSetSpace(uint32_t hOwner, int32_t nChannel, uint32_t hEntity, int32_t nAttachment,
									  int32_t nFlags, SourceSharpParticleFollow_t* pFollow)
{
	const int i = ManagedParticles_Find(hOwner, nChannel);
	if (i < 0)
		return 0;
	g_ManagedParticles[i]->SetSpace(hEntity, nAttachment, nFlags, pFollow);
	return 1;
}

// Each live executor's frame read goes to managed in one length-prefixed buffer, and each
// binding's state comes back in another.

void ShellParticles_Frame(float flFrameTime)
{
	IManagedParticlesClient* pArea = ManagedParticlesClient();
	if (!pArea)
		return;

	// On the stack for the usual executor counts; the heap past it.
	CUtlVectorFixedGrowable<uint8_t, 16384> in;
	CUtlVectorFixedGrowable<uint8_t, 4096> out;
	const int nExecutors = g_ManagedParticles.Count();
	const int nIn =
		(int)sizeof(SourceSharpParticleFrameHeader_t) + nExecutors * (int)sizeof(SourceSharpParticleFrameIn_t);
	in.SetCount(kBitBlockHeaderBytes + nIn);
	BitBlockSetBits(in.Base(), nIn * 8);
	SourceSharpParticleFrameHeader_t* pHeader = (SourceSharpParticleFrameHeader_t*)(in.Base() + kBitBlockHeaderBytes);
	pHeader->Dt = flFrameTime;
	pHeader->CurTime = gpGlobals->curtime;
	pHeader->FrameTime = gpGlobals->frametime;
	pHeader->AabbBuffer = cl_particleeffect_aabb_buffer.GetFloat();
	VectorCopy(MainViewOrigin().Base(), pHeader->ViewOriginXYZ);
	SourceSharpParticleFrameIn_t* pIn = (SourceSharpParticleFrameIn_t*)(pHeader + 1);
	for (int i = 0; i < nExecutors; i++)
		g_ManagedParticles[i]->FillFrameIn(pIn[i]);

	const int nOut = nExecutors * (int)sizeof(SourceSharpParticleCommit_t);
	out.SetCount(kBitBlockHeaderBytes + MAX(nOut, 4));
	BitBlockSetBits(out.Base(), 0);
	if (pArea->Frame(in.Base(), in.Count(), out.Base(), out.Count()) <= 0)
		return;

	const int nBytes = MIN(BitBlockBits(out.Base()) / 8, out.Count() - kBitBlockHeaderBytes);
	const SourceSharpParticleCommit_t* pCommits =
		(const SourceSharpParticleCommit_t*)(out.Base() + kBitBlockHeaderBytes);
	for (int c = 0; c < nBytes / (int)sizeof(SourceSharpParticleCommit_t); c++)
	{
		const int i = ManagedParticles_Find(pCommits[c].Owner, pCommits[c].Channel);
		if (i >= 0)
			g_ManagedParticles[i]->Commit(pCommits[c]);
	}
}

void ShellParticles_FrameCode()
{
	if (IManagedParticlesClient* pArea = ManagedParticlesClient())
		pArea->FrameCode();
}

void ShellParticles_PostRender()
{
	if (IManagedParticlesClient* pArea = ManagedParticlesClient())
		pArea->PostRender();
}

void SourceSharp_ParticlesConfigure(uint32_t hOwner, int32_t nChannel, float flCullRadius)
{
	const int i = ManagedParticles_Find(hOwner, nChannel);
	if (i >= 0)
		g_ManagedParticles[i]->Configure(flCullRadius);
}

void SourceSharp_ParticlesSetLights(uint32_t hOwner, int32_t nChannel, const float* pLights, int32_t nFloats)
{
	const int i = ManagedParticles_Find(hOwner, nChannel);
	if (i >= 0 && pLights && nFloats >= 17)
		g_ManagedParticles[i]->SetLights(pLights);
}

// 1 and the value when the var exists, else 0.
int32_t SourceSharp_ParticlesMaterialVarInt(uint32_t hOwner, int32_t nChannel, const char* pszVar, int32_t* pValue)
{
	const int i = ManagedParticles_Find(hOwner, nChannel);
	IMaterial* pMaterial = i >= 0 ? g_ManagedParticles[i]->Material() : NULL;
	if (!pMaterial || !pszVar || !pValue)
		return 0;
	bool bFound = false;
	IMaterialVar* pVar = pMaterial->FindVar(pszVar, &bFound, false);
	if (!bFound || !pVar)
		return 0;
	*pValue = pVar->GetIntValue();
	return 1;
}

// A world trace that also answers the hit plane, which the smoke volume's wall fade reads.
// Out: fraction, normal xyz, dist. Returns the count written, or 0.
int32_t SourceSharp_ParticlesTraceLine(const float* pStartXYZ, const float* pEndXYZ, int32_t nMask, float* pOut,
									   int32_t nOut)
{
	if (!pStartXYZ || !pEndXYZ || !pOut || nOut < 5)
		return 0;
	trace_t tr;
	UTIL_TraceLine(Vector(pStartXYZ[0], pStartXYZ[1], pStartXYZ[2]), Vector(pEndXYZ[0], pEndXYZ[1], pEndXYZ[2]),
				   (unsigned int)nMask, NULL, COLLISION_GROUP_NONE, &tr);
	pOut[0] = tr.fraction;
	pOut[1] = tr.plane.normal.x;
	pOut[2] = tr.plane.normal.y;
	pOut[3] = tr.plane.normal.z;
	pOut[4] = tr.plane.dist;
	return 5;
}

void SourceSharp_ParticlesDestroy(uint32_t hOwner, int32_t nChannel)
{
	const int i = ManagedParticles_Find(hOwner, nChannel);
	if (i >= 0)
		g_ManagedParticles[i]->Release();
}

// Each effect sits at its owner every frame: a physics_cannister drags its steam jet.
static void ManagedParticles_Follow()
{
	for (int i = g_ManagedParticles.Count() - 1; i >= 0; i--)
	{
		CManagedParticleEffect* pEffect = g_ManagedParticles[i];

		if (pEffect->Owner() == SOURCESHARP_PARTICLE_NO_OWNER)
			continue;

		C_StandaloneEntity* pOwner = SourceSharp_StandaloneFromHandle(pEffect->Owner());
		if (!pOwner)
		{
			pEffect->Release();
			continue;
		}
		pEffect->SetAbsOrigin(pOwner->GetAbsOrigin());
		pEffect->SetAbsAngles(pOwner->GetAbsAngles());
	}
}

// Every live executor, where it draws this frame and how many particles it simulates.
CON_COMMAND(cs_cl_effect_kinds,
			"every live managed particle executor (trails, steam jets, smoke stacks, smoke volumes): "
			"where it is drawn this frame and how many particles its binding is simulating")
{
	for (int i = 0; i < g_ManagedParticles.Count(); i++)
	{
		CManagedParticleEffect* pEffect = g_ManagedParticles[i];
		const Vector vOrigin = pEffect->GetAbsOrigin();
		Msg("[SourceSharp] effect kind: managed-%d at (%.1f %.1f %.1f) particles %d owner %u space %d\n",
			pEffect->Channel(), vOrigin.x, vOrigin.y, vOrigin.z, pEffect->ActiveParticles(), pEffect->Owner(),
			pEffect->SpaceFlags());
	}

	Msg("[SourceSharp] effect kinds: %d executor(s)\n", g_ManagedParticles.Count());
}

class CSourceSharpManagedParticlesSystem : public CClientSystemPerFrame
{
public:
	CSourceSharpManagedParticlesSystem()
		: CClientSystemPerFrame("CSourceSharpManagedParticlesSystem")
	{
	}

	virtual void PreRender() OVERRIDE { ManagedParticles_Follow(); }

	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		while (g_ManagedParticles.Count())
		{
			const int nBefore = g_ManagedParticles.Count();
			g_ManagedParticles[0]->Release();
			if (g_ManagedParticles.Count() >= nBefore)
				g_ManagedParticles.Remove(0);
		}
	}
};

static CSourceSharpManagedParticlesSystem g_ManagedParticlesSystem;

CClientSystem* ClientSystem_ManagedParticles()
{
	return &g_ManagedParticlesSystem;
}

// The new-style particle systems the client starts, owned by the library. An owned system
// (started by a managed entity) is held and kept at its owner by the sweep below; a
// one-shot (an impact) is only watched and the manager removes it. Both are watched by a
// weak reference so a stopped system stays listed until deleted.
//
// A managed entity has no ParticleProp(), so its systems are created ownerless and their
// control points are driven from the owner's origin.

struct SourceSharpParticleSystem_t
{
	uint32_t hOwner;
	CSmartPtr<CShellNewParticleEffect> pEffect;

	// Which control points the sweep keeps at the owner, as a bitmask. An unfed control point
	// is the world origin.
	int32_t nFollowMask;
};

static CUtlVector<SourceSharpParticleSystem_t> g_ParticleSystems;

// Heap entries: a CUtlReference links itself into its object's list and must not move.
struct WatchedParticleSystem_t
{
	CUtlReference<CShellNewParticleEffect> ref;
	uint32_t hOwner;
	int32_t nKind;
	float flStarted;
};

static CUtlVector<WatchedParticleSystem_t*> g_WatchedParticles;

// The entries whose effect the manager has deleted.
static void ReapWatchedParticleSystems()
{
	for (int i = g_WatchedParticles.Count() - 1; i >= 0; i--)
	{
		if (g_WatchedParticles[i]->ref() == NULL)
		{
			delete g_WatchedParticles[i];
			g_WatchedParticles.Remove(i);
		}
	}
}

// Reaps first, so the table is bounded by the live effects.
static void WatchParticleSystem(CShellNewParticleEffect* pEffect, uint32_t hOwner, int32_t nKind)
{
	ReapWatchedParticleSystems();

	WatchedParticleSystem_t* pWatch = new WatchedParticleSystem_t;
	pWatch->ref.Set(pEffect);
	pWatch->hOwner = hOwner;
	pWatch->nKind = nKind;
	pWatch->flStarted = gpGlobals->curtime;
	g_WatchedParticles.AddToTail(pWatch);
}

// A refused name is not watched: Start refuses it to managed.
static CSmartPtr<CShellNewParticleEffect> CreateWatched(uint32_t hOwner, const char* pszName)
{
	CSmartPtr<CShellNewParticleEffect> pEffect = CShellNewParticleEffect::Create(pszName, "SourceSharpFlame");
	if (pEffect.IsValid() && pEffect->IsValid())
		WatchParticleSystem(pEffect.GetObject(), hOwner, SOURCESHARP_PARTICLE_SYSTEM_OWNED);
	return pEffect;
}

// StopEmission rather than a release, so particles already in the air burn out.
static void ReleaseParticleSystemAt(int nIndex)
{
	CShellNewParticleEffect* pEffect = g_ParticleSystems[nIndex].pEffect.GetObject();

	if (pEffect)
		pEffect->StopEmission(false, false, true);

	g_ParticleSystems.Remove(nIndex);
}

// One system per entity: starting again replaces the running one.
int32_t SourceSharp_ParticleSystemStart(uint32_t hOwner, const char* pszName)
{
	if (!pszName || !pszName[0])
		return 0;

	C_StandaloneEntity* pOwner = SourceSharp_StandaloneFromHandle(hOwner);

	if (!pOwner)
		return 0;

	int nExisting = ShellEffects_Find(g_ParticleSystems, hOwner);

	if (nExisting >= 0)
		ReleaseParticleSystemAt(nExisting);

	CSmartPtr<CShellNewParticleEffect> pEffect = CreateWatched(hOwner, pszName);

	// A name no .pcf defines yields an effect that draws nothing; refuse it so managed knows.
	if (!pEffect.IsValid() || !pEffect->IsValid())
		return 0;

	pEffect->SetControlPoint(0, pOwner->GetAbsOrigin());
	pEffect->SetControlPointOrientation(0, Vector(1, 0, 0), Vector(0, 1, 0), Vector(0, 0, 1));
	pEffect->SetSortOrigin(pOwner->GetAbsOrigin());

	SourceSharpParticleSystem_t entry;
	entry.hOwner = hOwner;
	entry.pEffect = pEffect;
	entry.nFollowMask = kSourceSharpParticleFollowDefault;
	g_ParticleSystems.AddToTail(entry);

	return 1;
}

// Which control points of a running system stay at the owner. A separate call so Start's
// ABI row is unchanged. Operators read an unwritten control point as the origin, and
// burning_character's initializer reads control point 1. Returns 0 if the entity has no
// system, which is a caller bug.
int32_t SourceSharp_ParticleSystemFollowPoints(uint32_t hOwner, int32_t nPointMask)
{
	int nIndex = ShellEffects_Find(g_ParticleSystems, hOwner);

	if (nIndex < 0)
		return 0;

	// Point 0 is always fed: it is where the system is placed.
	g_ParticleSystems[nIndex].nFollowMask = SourceSharpParticleFollow_Normalise(nPointMask);

	return 1;
}

void SourceSharp_ParticleSystemStop(uint32_t hOwner)
{
	int nIndex = ShellEffects_Find(g_ParticleSystems, hOwner);

	if (nIndex >= 0)
		ReleaseParticleSystemAt(nIndex);
}

// A one-shot an impact started: watched, not held.
void ShellParticles_WatchOneShot(CShellNewParticleEffect* pEffect)
{
	if (pEffect)
		WatchParticleSystem(pEffect, SOURCESHARP_PARTICLE_NO_OWNER, SOURCESHARP_PARTICLE_SYSTEM_ONE_SHOT);
}

// Every managed particle system moved to its entity: a move alone raises no data change.
// Backwards, since entries are removed mid-walk.
void ShellParticles_PreRender()
{
	for (int i = g_ParticleSystems.Count() - 1; i >= 0; i--)
	{
		CShellNewParticleEffect* pEffect = g_ParticleSystems[i].pEffect.GetObject();

		if (!pEffect)
		{
			g_ParticleSystems.Remove(i);
			continue;
		}

		C_StandaloneEntity* pOwner = SourceSharp_StandaloneFromHandle(g_ParticleSystems[i].hOwner);

		// The entity is gone; let the system finish rather than emit at its last position.
		if (!pOwner)
		{
			ReleaseParticleSystemAt(i);
			continue;
		}

		// Every requested point gets the owner's position, as stock's C_EntityFlame binds both.
		const Vector vecOrigin = pOwner->GetAbsOrigin();
		const int32_t nMask = g_ParticleSystems[i].nFollowMask;

		for (int nPoint = 0; nPoint <= kSourceSharpParticleMaxFollowPoint; nPoint++)
		{
			if (SourceSharpParticleFollow_Wants(nMask, nPoint))
				pEffect->SetControlPoint(nPoint, vecOrigin);
		}
		pEffect->SetSortOrigin(vecOrigin); // the leaf system culls by it
	}
}

// Stopped rather than forgotten: the particle manager holds a reference to each.
void ShellParticles_LevelShutdown()
{
	for (int i = g_ParticleSystems.Count() - 1; i >= 0; i--)
		if (CShellNewParticleEffect* pEffect = g_ParticleSystems[i].pEffect.GetObject())
			pEffect->StopEmission(false, true, true);

	g_ParticleSystems.RemoveAll();
}

// The dev API's read: one watched instance by index; returns the total.
static int CountParticles(CParticleCollection* pCollection)
{
	if (!pCollection)
		return 0;

	int nTotal = pCollection->m_nActiveParticles;
	for (CParticleCollection* pChild = pCollection->m_Children.m_pHead; pChild; pChild = pChild->m_pNext)
		nTotal += CountParticles(pChild);
	return nTotal;
}

int32_t SourceSharp_ParticleSystemsRead(int32_t nIndex, SourceSharpParticleSystemInfo_t* pOut, char* pszName,
										int32_t nNameMax)
{
	ReapWatchedParticleSystems();

	const int32_t nCount = g_WatchedParticles.Count();
	if (nIndex < 0 || nIndex >= nCount || !pOut)
		return nCount;

	const WatchedParticleSystem_t* pWatch = g_WatchedParticles[nIndex];
	CShellNewParticleEffect* pEffect = pWatch->ref();

	memset(pOut, 0, sizeof(*pOut));
	pOut->Kind = pWatch->nKind;
	pOut->Owner = pWatch->hOwner;
	pOut->Age = gpGlobals->curtime - pWatch->flStarted;

	const int nOwned = ShellEffects_Find(g_ParticleSystems, pWatch->hOwner);
	const bool bHeld = nOwned >= 0 && g_ParticleSystems[nOwned].pEffect.GetObject() == pEffect;
	pOut->Stopped = (pWatch->nKind == SOURCESHARP_PARTICLE_SYSTEM_OWNED && !bHeld) ? 1 : 0;
	pOut->FollowMask = bHeld ? g_ParticleSystems[nOwned].nFollowMask : 0;

	if (pEffect)
	{
		pOut->Particles = CountParticles(pEffect->Collection());
		const Vector& cp0 = pEffect->Collection()->m_ControlPoints[0].m_Position;
		pOut->ControlPoint0XYZ[0] = cp0.x;
		pOut->ControlPoint0XYZ[1] = cp0.y;
		pOut->ControlPoint0XYZ[2] = cp0.z;
		const Vector& cp1 = pEffect->Collection()->m_ControlPoints[1].m_Position;
		pOut->ControlPoint1XYZ[0] = cp1.x;
		pOut->ControlPoint1XYZ[1] = cp1.y;
		pOut->ControlPoint1XYZ[2] = cp1.z;
		const Vector& vSort = pEffect->GetSortOrigin();
		pOut->SortOriginXYZ[0] = vSort.x;
		pOut->SortOriginXYZ[1] = vSort.y;
		pOut->SortOriginXYZ[2] = vSort.z;
	}

	if (pszName && nNameMax > 0)
		Q_strncpy(pszName, pEffect ? pEffect->GetEffectName() : "", nNameMax);

	return nCount;
}

// The IParticleSystemQuery particles.a calls back into, with each answer from managed.
// The six methods no definition calls warn and count as refusals. Calls with no managed
// side (the collision-group reads during Init, before the runtime exists) answer the
// interface's default and are counted.

class CShellParticleSystemQuery : public CBaseAppSystem<IParticleSystemQuery>
{
public:
	enum
	{
		kCollisionGroup,
		kLighting,
		kTrace,
		kHitBoxPoints,
		kHitBoxInfo,
		kMethods
	};

	virtual void GetLightingAtPoint(const Vector& vecOrigin, Color& tint) OVERRIDE
	{
		IManagedParticlesClient* pArea = ManagedParticlesClient();
		uint8_t rgba[4];
		// The library simulates on several threads, and the engine's light lookup is not reentrant.
		AUTO_LOCK(m_LightMutex);
		if (pArea && pArea->QueryLighting(vecOrigin.Base(), rgba) == 1)
			tint.SetColor(rgba[0], rgba[1], rgba[2], rgba[3]);
		else
			Unanswered_Add(kLighting);
	}

	virtual void TraceLine(const Vector& vecAbsStart, const Vector& vecAbsEnd, unsigned int mask,
						   const IHandleEntity* ignore, int collisionGroup, CBaseTrace* ptr) OVERRIDE
	{
		IManagedParticlesClient* pArea = ManagedParticlesClient();
		const int32_t nAnswer =
			pArea ? pArea->QueryTrace(vecAbsStart.Base(), vecAbsEnd.Base(), mask, collisionGroup, ignore ? 1 : 0) : -1;
		if (nAnswer == 1)
		{
			trace_t tempTrace;
			UTIL_TraceLine(vecAbsStart, vecAbsEnd, mask, ignore, collisionGroup, &tempTrace);
			memcpy(ptr, &tempTrace, sizeof(CBaseTrace));
			return;
		}
		if (nAnswer < 0)
			Unanswered_Add(kTrace);
		ptr->startsolid = 0;
		ptr->fraction = 1.0;
	}

	virtual int GetCollisionGroupFromName(const char* pszCollisionGroupName) OVERRIDE
	{
		IManagedParticlesClient* pArea = ManagedParticlesClient();
		const int32_t nGroup =
			(pArea && pszCollisionGroupName) ? pArea->QueryCollisionGroup(pszCollisionGroupName) : -1;
		if (nGroup >= 0)
			return nGroup;
		Unanswered_Add(kCollisionGroup);
		return COLLISION_GROUP_NONE;
	}

	virtual void GetRandomPointsOnControllingObjectHitBox(CParticleCollection* pParticles, int nControlPointNumber,
														  int nNumPtsOut, float flBBoxScale,
														  int nNumTrysToGetAPointInsideTheModel, Vector* pPntsOut,
														  Vector vecDirectionalBias, Vector* pHitBoxRelativeCoordOut,
														  int* pHitBoxIndexOut) OVERRIDE
	{
		const CParticleControlPoint& point = pParticles->m_ControlPoints[nControlPointNumber];
		IManagedParticlesClient* pArea = ManagedParticlesClient();
		const int32_t nAnswer = pArea
			? pArea->QueryHitBoxPoints(nControlPointNumber, point.m_Position.Base(), point.m_pObject ? 1 : 0,
									   nNumPtsOut, pPntsOut ? pPntsOut->Base() : NULL, pHitBoxIndexOut,
									   pHitBoxRelativeCoordOut ? pHitBoxRelativeCoordOut->Base() : NULL)
			: -1;
		if (nAnswer == nNumPtsOut)
			return;

		// Stock's fallback: every point at the control point.
		Unanswered_Add(kHitBoxPoints);
		for (int i = 0; i < nNumPtsOut; i++)
		{
			pPntsOut[i] = point.m_Position;
			if (pHitBoxIndexOut)
				pHitBoxIndexOut[i] = 0;
			if (pHitBoxRelativeCoordOut)
				pHitBoxRelativeCoordOut[i].Init();
		}
	}

	virtual int GetControllingObjectHitBoxInfo(CParticleCollection* pParticles, int nControlPointNumber, int nBufSize,
											   ModelHitBoxInfo_t* pHitBoxOutputBuffer) OVERRIDE
	{
		IManagedParticlesClient* pArea = ManagedParticlesClient();
		const int32_t nAnswer = pArea
			? pArea->QueryHitBoxInfo(nControlPointNumber,
									 pParticles->m_ControlPoints[nControlPointNumber].m_pObject ? 1 : 0, nBufSize)
			: -1;
		if (nAnswer >= 0 && nAnswer <= nBufSize)
			return nAnswer;
		Unanswered_Add(kHitBoxInfo);
		return 0;
	}

	// Never called by any definition: each warns and counts as a refusal.
	virtual bool MovePointInsideControllingObject(CParticleCollection*, void*, Vector*) OVERRIDE
	{
		ShellClientDll_RefuseExternal("IParticleSystemQuery::MovePointInsideControllingObject");
		return true;
	}

	virtual bool IsPointInControllingObjectHitBox(CParticleCollection*, int, Vector, bool) OVERRIDE
	{
		ShellClientDll_RefuseExternal("IParticleSystemQuery::IsPointInControllingObjectHitBox");
		return true;
	}

	virtual Vector GetLocalPlayerPos() OVERRIDE
	{
		ShellClientDll_RefuseExternal("IParticleSystemQuery::GetLocalPlayerPos");
		return vec3_origin;
	}

	virtual void GetLocalPlayerEyeVectors(Vector* pForward, Vector* pRight, Vector* pUp) OVERRIDE
	{
		ShellClientDll_RefuseExternal("IParticleSystemQuery::GetLocalPlayerEyeVectors");
		*pForward = vec3_origin;
		*pRight = vec3_origin;
		*pUp = vec3_origin;
	}

	virtual float GetPixelVisibility(int*, const Vector&, float) OVERRIDE
	{
		ShellClientDll_RefuseExternal("IParticleSystemQuery::GetPixelVisibility");
		return 0.0f;
	}

	virtual void SetUpLightingEnvironment(const Vector&) OVERRIDE
	{
		ShellClientDll_RefuseExternal("IParticleSystemQuery::SetUpLightingEnvironment");
	}

	int32_t Unanswered(int nMethod) const { return (nMethod >= 0 && nMethod < kMethods) ? m_nUnanswered[nMethod] : 0; }

private:
	// The library's worker threads call these concurrently.
	void Unanswered_Add(int nMethod) { ThreadInterlockedIncrement(&m_nUnanswered[nMethod]); }

	CThreadFastMutex m_LightMutex;
	volatile int32 m_nUnanswered[kMethods];
};

static CShellParticleSystemQuery s_ShellParticleQuery;

// The global CParticleMgr::Init hands particles.a, also registered by name.
IParticleSystemQuery* g_pParticleSystemQuery = &s_ShellParticleQuery;
EXPOSE_SINGLE_INTERFACE_GLOBALVAR(CShellParticleSystemQuery, IParticleSystemQuery,
								  PARTICLE_SYSTEM_QUERY_INTERFACE_VERSION, s_ShellParticleQuery);

// The unanswered calls per method, in CShellParticleSystemQuery's order. Returns the count.
int32_t SourceSharp_ParticleQueryUnanswered(int32_t* pOut, int32_t nMax)
{
	int32_t n = 0;
	for (; pOut && n < nMax && n < CShellParticleSystemQuery::kMethods; n++)
		pOut[n] = s_ShellParticleQuery.Unanswered(n);
	return n;
}

// The ParticleEffect client effect's carry-out: managed chooses the name, points and
// orientation; this creates the system as a watched one-shot.
const char* GetParticleSystemNameFromIndex(int nIndex);

int32_t SourceSharp_ParticleSystemNameFromIndex(int32_t nIndex, char* pszOut, int32_t nOutMax)
{
	const char* pszName = nIndex >= 0 ? GetParticleSystemNameFromIndex(nIndex) : NULL;
	if (!pszName || !pszOut || nOutMax <= 0)
		return -1;
	Q_strncpy(pszOut, pszName, nOutMax);
	return Q_strlen(pszOut);
}

// pPoints: nPoints of (point, x, y, z). pOrientation0: control point 0's forward, right
// and up. 1 when a definition answered the name.
int32_t SourceSharp_ParticleSystemOneShot(const char* pszName, const float* pSortOriginXYZ, const float* pPoints,
										  int32_t nPoints, const float* pOrientation0)
{
	if (!pszName || !pSortOriginXYZ || (nPoints > 0 && !pPoints) || !pOrientation0)
		return 0;
	CSmartPtr<CShellNewParticleEffect> pEffect = CShellNewParticleEffect::Create(pszName);
	if (!pEffect.IsValid() || !pEffect->IsValid())
		return 0;
	pEffect->SetSortOrigin(Vector(pSortOriginXYZ[0], pSortOriginXYZ[1], pSortOriginXYZ[2]));
	for (int i = 0; i < nPoints && i < 2; i++)
		pEffect->SetControlPoint((int)pPoints[i * 4],
								 Vector(pPoints[i * 4 + 1], pPoints[i * 4 + 2], pPoints[i * 4 + 3]));
	pEffect->SetControlPointOrientation(0, Vector(pOrientation0[0], pOrientation0[1], pOrientation0[2]),
										Vector(pOrientation0[3], pOrientation0[4], pOrientation0[5]),
										Vector(pOrientation0[6], pOrientation0[7], pOrientation0[8]));
	for (int i = 2; i < nPoints; i++)
		pEffect->SetControlPoint((int)pPoints[i * 4],
								 Vector(pPoints[i * 4 + 1], pPoints[i * 4 + 2], pPoints[i * 4 + 3]));
	ShellParticles_WatchOneShot(pEffect.GetObject());
	return 1;
}

// MASK_SOLID_BRUSHONLY, as particle_collision.cpp traced. Out: fraction, endpos xyz,
// normal xyz, dist, allsolid, startsolid. Returns the count written.
int32_t SourceSharp_ParticlesWorldTrace(const float* pStartXYZ, const float* pEndXYZ, float* pOut, int32_t nOut)
{
	if (!pStartXYZ || !pEndXYZ || !pOut || nOut < 10)
		return 0;
	trace_t tr;
	UTIL_TraceLine(Vector(pStartXYZ[0], pStartXYZ[1], pStartXYZ[2]), Vector(pEndXYZ[0], pEndXYZ[1], pEndXYZ[2]),
				   MASK_SOLID_BRUSHONLY, NULL, COLLISION_GROUP_NONE, &tr);
	pOut[0] = tr.fraction;
	pOut[1] = tr.endpos.x;
	pOut[2] = tr.endpos.y;
	pOut[3] = tr.endpos.z;
	pOut[4] = tr.plane.normal.x;
	pOut[5] = tr.plane.normal.y;
	pOut[6] = tr.plane.normal.z;
	pOut[7] = tr.plane.dist;
	pOut[8] = tr.allsolid ? 1.0f : 0.0f;
	pOut[9] = tr.startsolid ? 1.0f : 0.0f;
	return 10;
}

// The model-view matrix's three rows, CurrentViewID(), and the view's origin and forward.
void SourceSharp_ParticlesView(float* pModelView12, int32_t* pViewId, float* pOriginForward6)
{
	if (pModelView12)
	{
		const VMatrix& mv = ShellParticleDraw_ModelView();
		for (int r = 0; r < 3; r++)
			for (int c = 0; c < 4; c++)
				pModelView12[r * 4 + c] = mv[r][c];
	}
	if (pViewId)
		*pViewId = (int32_t)CurrentViewID();
	if (pOriginForward6)
	{
		const Vector& vo = CurrentViewOrigin();
		const Vector& vf = CurrentViewForward();
		pOriginForward6[0] = vo.x;
		pOriginForward6[1] = vo.y;
		pOriginForward6[2] = vo.z;
		pOriginForward6[3] = vf.x;
		pOriginForward6[4] = vf.y;
		pOriginForward6[5] = vf.z;
	}
}

// A trace with managed's contents mask. Out: fraction, endpos xyz, normal xyz, startsolid,
// fractionleftsolid. Returns the count written.
int32_t SourceSharp_ParticlesTraceMask(const float* pStartXYZ, const float* pEndXYZ, uint32_t nMask, float* pOut,
									   int32_t nOut)
{
	if (!pStartXYZ || !pEndXYZ || !pOut || nOut < 9)
		return 0;
	trace_t tr;
	UTIL_TraceLine(Vector(pStartXYZ[0], pStartXYZ[1], pStartXYZ[2]), Vector(pEndXYZ[0], pEndXYZ[1], pEndXYZ[2]), nMask,
				   NULL, COLLISION_GROUP_NONE, &tr);
	pOut[0] = tr.fraction;
	pOut[1] = tr.endpos.x;
	pOut[2] = tr.endpos.y;
	pOut[3] = tr.endpos.z;
	pOut[4] = tr.plane.normal.x;
	pOut[5] = tr.plane.normal.y;
	pOut[6] = tr.plane.normal.z;
	pOut[7] = tr.startsolid ? 1.0f : 0.0f;
	pOut[8] = tr.fractionleftsolid;
	return 9;
}

// fx_water.cpp's entry points native code still calls; managed decides each splash or tint.
static void ManagedWaterSplash(int nKind, const Vector& origin, const Vector* pNormal, float flScale,
							   const Vector* pColor, float flLifetime, float flAlpha)
{
	if (IManagedParticlesClient* pArea = ManagedParticlesClient())
		pArea->WaterSplash(nKind, origin.Base(), pNormal ? pNormal->Base() : NULL, flScale,
						   pColor ? pColor->Base() : NULL, flLifetime, flAlpha);
}

void FX_GunshotSplash(const Vector& origin, const Vector& normal, float scale)
{
	ManagedWaterSplash(0, origin, &normal, scale, NULL, 0.0f, 0.0f);
}

void FX_GunshotSlimeSplash(const Vector& origin, const Vector& normal, float scale)
{
	ManagedWaterSplash(1, origin, &normal, scale, NULL, 0.0f, 0.0f);
}

void FX_WaterRipple(const Vector& origin, float scale, Vector* pColor, float flLifetime, float flAlpha)
{
	ManagedWaterSplash(2, origin, NULL, scale, pColor, flLifetime, flAlpha);
}

// With no managed side, the tint is black and the luminosity 0, as for a black light.
void UTIL_GetNormalizedColorTintAndLuminosity(const Vector& color, Vector* tint, float* luminosity)
{
	Vector vTint(0, 0, 0);
	float flLuminosity = 0.0f;
	if (IManagedParticlesClient* pArea = ManagedParticlesClient())
		pArea->SplashTint(color.Base(), vTint.Base(), &flLuminosity);
	if (tint)
		*tint = vTint;
	if (luminosity)
		*luminosity = flLuminosity;
}

// Materials the managed effects draw with, precached as the stock effect files did.
CLIENTEFFECT_REGISTER_BEGIN(PrecacheEffectBloodSpray)
CLIENTEFFECT_MATERIAL("effects/blood_core")
CLIENTEFFECT_MATERIAL("effects/blood_gore")
CLIENTEFFECT_MATERIAL("effects/blood_drop")
CLIENTEFFECT_MATERIAL("effects/blood_puff")
CLIENTEFFECT_REGISTER_END()

CLIENTEFFECT_REGISTER_BEGIN(PrecacheEffectExplosion)
CLIENTEFFECT_MATERIAL("effects/fire_cloud1")
CLIENTEFFECT_MATERIAL("effects/fire_cloud2")
CLIENTEFFECT_MATERIAL("effects/fire_embers1")
CLIENTEFFECT_MATERIAL("effects/fire_embers2")
CLIENTEFFECT_MATERIAL("effects/fire_embers3")
CLIENTEFFECT_MATERIAL("particle/particle_smokegrenade")
CLIENTEFFECT_MATERIAL("particle/particle_smokegrenade1")
CLIENTEFFECT_MATERIAL("effects/splash3")
CLIENTEFFECT_MATERIAL("effects/splashwake1")
CLIENTEFFECT_REGISTER_END()

CLIENTEFFECT_REGISTER_BEGIN(PrecacheMuzzleFlash)
CLIENTEFFECT_MATERIAL("effects/muzzleflash1")
CLIENTEFFECT_MATERIAL("effects/muzzleflash2")
CLIENTEFFECT_MATERIAL("effects/muzzleflash3")
CLIENTEFFECT_MATERIAL("effects/muzzleflash4")
CLIENTEFFECT_MATERIAL("effects/bluemuzzle")
CLIENTEFFECT_MATERIAL("effects/gunshipmuzzle")
CLIENTEFFECT_MATERIAL("effects/gunshiptracer")
CLIENTEFFECT_MATERIAL("effects/huntertracer")
CLIENTEFFECT_MATERIAL("sprites/physcannon_bluelight2")
CLIENTEFFECT_MATERIAL("effects/combinemuzzle1")
CLIENTEFFECT_MATERIAL("effects/combinemuzzle2")
CLIENTEFFECT_MATERIAL("effects/combinemuzzle2_nocull")
CLIENTEFFECT_REGISTER_END()

CLIENTEFFECT_REGISTER_BEGIN(PrecacheEffectSplash)
CLIENTEFFECT_MATERIAL("effects/splash1")
CLIENTEFFECT_MATERIAL("effects/splash2")
CLIENTEFFECT_MATERIAL("effects/splash4")
CLIENTEFFECT_MATERIAL("effects/slime1")
CLIENTEFFECT_REGISTER_END()

CLIENTEFFECT_REGISTER_BEGIN(PrecacheEffectSparks)
CLIENTEFFECT_MATERIAL("effects/combinemuzzle1_nocull")
CLIENTEFFECT_MATERIAL("effects/combinemuzzle2_nocull")
CLIENTEFFECT_MATERIAL("effects/energyball")
CLIENTEFFECT_MATERIAL("effects/energysplash")
CLIENTEFFECT_MATERIAL("effects/spark")
CLIENTEFFECT_MATERIAL("effects/yellowflare")
CLIENTEFFECT_MATERIAL("effects/yellowflare_noz")
CLIENTEFFECT_MATERIAL("sprites/blueflare1")
CLIENTEFFECT_MATERIAL("sprites/rico1")
CLIENTEFFECT_MATERIAL("sprites/rico1_noz")
CLIENTEFFECT_REGISTER_END()
