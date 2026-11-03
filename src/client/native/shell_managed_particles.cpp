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
#include "shell_particle_systems_client.h"
#include "shell_particle_query_client.h"
#include "shell_managed_particles.h"
#include "shell_standalone_clients.h"
#include "shell_clientdll_refusals.h"
#include "shell_cliententitylist.h"
#include "ss_bitblock.h"
#include "client_entity_list.h"
#include "shell_view_cameras.h"
#include "shell_client_engine_interfaces.h"

// The view model FOV squash CLocalSpaceEmitter applied (FLE_VIEWMODEL).

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

// Converts between viewmodel-FOV space and world space; stock's arithmetic.
// Live for particles attached to the managed viewmodel, so it must not stub.
void FormatViewModelAttachment(const CViewCameras& cameras, Vector& vOrigin, bool bInverse)
{
	// Presumably, SetUpView has been called so we know our FOV and render origin.
	const CViewSetup* pViewSetup = view->GetPlayerViewSetup();

	float worldx = tan(pViewSetup->fov * M_PI / 360.0);
	float viewx = tan(pViewSetup->fovViewmodel * M_PI / 360.0);

	// aspect ratio cancels out, so only need one factor
	// the difference between the screen coordinates of the 2 systems is the ratio
	// of the coefficients of the projection matrices (tan (fov/2) is that coefficient)
	// NOTE: viewx was coming in as 0 when folks set their viewmodel_fov to 0 and show their weapon.
	float factorX = viewx ? (worldx / viewx) : 0.0f;
	float factorY = factorX;

	// Get the coordinates in the viewer's space.
	Vector tmp = vOrigin - pViewSetup->origin;
	Vector vTransformed(cameras.MainRight().Dot(tmp), cameras.MainUp().Dot(tmp), cameras.MainForward().Dot(tmp));

	// Now squash X and Y.
	if (bInverse)
	{
		if (factorX != 0 && factorY != 0)
		{
			vTransformed.x /= factorX;
			vTransformed.y /= factorY;
		}
		else
		{
			vTransformed.x = 0.0f;
			vTransformed.y = 0.0f;
		}
	}
	else
	{
		vTransformed.x *= factorX;
		vTransformed.y *= factorY;
	}

	// Transform back to world space.
	Vector vOut = (cameras.MainRight() * vTransformed.x) + (cameras.MainUp() * vTransformed.y) +
		(cameras.MainForward() * vTransformed.z);
	vOrigin = pViewSetup->origin + vOut;
}

// Which view the particle manager is drawing, as a key over its model-view matrix and
// CurrentViewID(). Managed re-reads the view only when the key changes.
int32_t ParticlesViewKey(const CViewCameras& cameras)
{
	const VMatrix& mv = ShellParticleDraw_ModelView();
	const unsigned char* p = (const unsigned char*)&mv;
	uint32_t h = 2166136261u;
	for (size_t i = 0; i < sizeof(VMatrix); i++)
		h = (h ^ p[i]) * 16777619u;
	h = (h ^ (uint32_t)cameras.CurrentID()) * 16777619u;
	return (int32_t)h;
}

} // namespace

class CManagedParticleEffect : public CShellParticleEffect
{
public:
	CManagedParticleEffect(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
						   const CViewCameras& cameras, CClientEntityList& entityList, uint32_t hOwner,
						   int32_t nChannel)
		: m_Areas(areas)
		, m_Engines(engines)
		, m_Cameras(cameras)
		, m_EntityList(entityList)
		, m_hOwner(hOwner)
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
		IClientRenderable* pRenderable = m_EntityList.RenderableFromHandle(m_hSpace);
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
	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	const CViewCameras& m_Cameras;
	CClientEntityList& m_EntityList;

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
		CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
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
		CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
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
	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
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
					FormatViewModelAttachment(m_Cameras, vWorld, false);
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
	IManagedParticlesClient* pArea = m_Areas.m_pParticlesClient;
	if (!pArea)
		return 0;

	// Set before managed is asked, because its view read is keyed by it.
	if (m_nFlags & kFlagCameraSpace)
	{
		CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
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
	in.ViewKey = ParticlesViewKey(m_Cameras);
	in.FrameTime = m_Engines.m_pGlobals->frametime;

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
		IMaterial* pMaterial = nPass == 0
			? pPage
			: m_Engines.m_pMaterials->FindMaterial("debug/debugparticlewireframe", TEXTURE_GROUP_OTHER);
		// The wireframe pass draws the same quads again.
		DrawQuads(pMaterial, flFrameTime, pQuads, nQuads, m_Keys.Base());

		// The sort keys back in one length-prefixed buffer.
		BitBlockSetBits(m_KeyBuffer.Base(), nQuads * (int)sizeof(float) * 8);
		V_memcpy(m_KeyBuffer.Base() + kBitBlockHeaderBytes, m_Keys.Base(), nQuads * sizeof(float));
		pArea->DrawEnd(m_hOwner, m_nChannel, m_KeyBuffer.Base(), m_KeyBuffer.Count());
	}

	if (bBox && m_Engines.m_pDebugOverlay)
	{
		const Vector center = (vMin + vMax) / 2;
		m_Engines.m_pDebugOverlay->AddBoxOverlay(center, vMin - center, vMax - center, QAngle(0, 0, 0), r, g, b, 16, 0);
		m_Engines.m_pDebugOverlay->AddTextOverlayRGB(center, 0, 0, r, g, b, 64, "managed-%d:(%d)", m_nChannel, nActive);
	}

	RenderEnd(mTempModel, mTempView);
	return 1;
}

CManagedParticlesClient::~CManagedParticlesClient()
{
	for (int i = 0; i < m_Effects.Count(); i++)
		m_Effects[i]->SetGoneCallback(NULL);
}

int CManagedParticlesClient::Find(uint32_t hOwner, int32_t nChannel) const
{
	for (int i = 0; i < m_Effects.Count(); i++)
	{
		if (m_Effects[i]->Owner() == hOwner && m_Effects[i]->Channel() == nChannel)
			return i;
	}
	return -1;
}

void CManagedParticlesClient::Forget(CShellParticleEffect* pEffect)
{
	for (int i = m_Effects.Count() - 1; i >= 0; i--)
	{
		if (m_Effects[i] == pEffect)
			m_Effects.Remove(i);
	}
}

int32_t CManagedParticlesClient::ParticlesCreate(uint32_t hOwner, int32_t nChannel, const char* pszMaterial,
												 float flCullRadius, int32_t bAlwaysSimulate)
{
	C_StandaloneEntity* pOwner = m_Standalone.FromHandle(hOwner);
	if (!pOwner || !pszMaterial || Find(hOwner, nChannel) >= 0 || !m_ForgetHook.Get())
		return 0;

	CManagedParticleEffect* pEffect =
		new CManagedParticleEffect(m_Areas, m_Engines, m_Cameras, m_Standalone.EntityList(), hOwner, nChannel);
	pEffect->SetAbsOrigin(pOwner->GetAbsOrigin());
	pEffect->SetAbsAngles(pOwner->GetAbsAngles());
	if (!pEffect->Start(pszMaterial, flCullRadius))
	{
		pEffect->Release();
		return 0;
	}

	pEffect->SetGoneCallback(m_ForgetHook.Get());
	m_Effects.AddToTail(pEffect);
	return 1;
}

// An executor for an ownerless effect, placed once at a world point and keyed by the
// no-owner handle and the managed id. Managed releases it when the effect finishes.
int32_t CManagedParticlesClient::ParticlesCreateAt(int32_t nId, const char* pszMaterial, const float* pOriginXYZ,
												   float flCullRadius, int32_t bAlwaysSimulate)
{
	if (!pszMaterial || !pOriginXYZ || Find(SOURCESHARP_PARTICLE_NO_OWNER, nId) >= 0 || !m_ForgetHook.Get())
		return 0;

	CManagedParticleEffect* pEffect = new CManagedParticleEffect(
		m_Areas, m_Engines, m_Cameras, m_Standalone.EntityList(), SOURCESHARP_PARTICLE_NO_OWNER, nId);
	pEffect->SetAbsOrigin(Vector(pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2]));
	if (!pEffect->Start(pszMaterial, flCullRadius))
	{
		pEffect->Release();
		return 0;
	}

	pEffect->SetGoneCallback(m_ForgetHook.Get());
	m_Effects.AddToTail(pEffect);
	return 1;
}

void CManagedParticlesClient::DrawSingletons(bool bInSkybox)
{
	const int nPass = bInSkybox ? 1 : 0;
	for (int i = 0; i < m_Effects.Count(); i++)
	{
		if (m_Effects[i]->SingletonPass() != nPass)
			continue;
		m_Effects[i]->DrawAsSingleton();
		g_SourceSharpEffects.nSingletonDraws++;
	}
}

// 1 when the executor was found.
int32_t CManagedParticlesClient::ParticlesSetSpace(uint32_t hOwner, int32_t nChannel, uint32_t hEntity,
												   int32_t nAttachment, int32_t nFlags,
												   SourceSharpParticleFollow_t* pFollow)
{
	const int i = Find(hOwner, nChannel);
	if (i < 0)
		return 0;
	m_Effects[i]->SetSpace(hEntity, nAttachment, nFlags, pFollow);
	return 1;
}

// Each live executor's frame read goes to managed in one length-prefixed buffer, and each
// binding's state comes back in another.

void CManagedParticlesClient::Frame(float flFrameTime)
{
	IManagedParticlesClient* pArea = Managed();
	if (!pArea)
		return;

	// On the stack for the usual executor counts; the heap past it.
	CUtlVectorFixedGrowable<uint8_t, 16384> in;
	CUtlVectorFixedGrowable<uint8_t, 4096> out;
	const int nExecutors = m_Effects.Count();
	const int nIn =
		(int)sizeof(SourceSharpParticleFrameHeader_t) + nExecutors * (int)sizeof(SourceSharpParticleFrameIn_t);
	in.SetCount(kBitBlockHeaderBytes + nIn);
	BitBlockSetBits(in.Base(), nIn * 8);
	SourceSharpParticleFrameHeader_t* pHeader = (SourceSharpParticleFrameHeader_t*)(in.Base() + kBitBlockHeaderBytes);
	pHeader->Dt = flFrameTime;
	pHeader->CurTime = m_Engines.m_pGlobals->curtime;
	pHeader->FrameTime = m_Engines.m_pGlobals->frametime;
	pHeader->AabbBuffer = cl_particleeffect_aabb_buffer.GetFloat();
	VectorCopy(m_Cameras.MainOrigin().Base(), pHeader->ViewOriginXYZ);
	SourceSharpParticleFrameIn_t* pIn = (SourceSharpParticleFrameIn_t*)(pHeader + 1);
	for (int i = 0; i < nExecutors; i++)
		m_Effects[i]->FillFrameIn(pIn[i]);

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
		const int i = Find(pCommits[c].Owner, pCommits[c].Channel);
		if (i >= 0)
			m_Effects[i]->Commit(pCommits[c]);
	}
}

void CManagedParticlesClient::FrameCode()
{
	if (IManagedParticlesClient* pArea = Managed())
		pArea->FrameCode();
}

void CManagedParticlesClient::PostRender()
{
	if (IManagedParticlesClient* pArea = Managed())
		pArea->PostRender();
}

void CManagedParticlesClient::ParticlesConfigure(uint32_t hOwner, int32_t nChannel, float flCullRadius)
{
	const int i = Find(hOwner, nChannel);
	if (i >= 0)
		m_Effects[i]->Configure(flCullRadius);
}

void CManagedParticlesClient::ParticlesSetLights(uint32_t hOwner, int32_t nChannel, const float* pLights,
												 int32_t nFloats)
{
	const int i = Find(hOwner, nChannel);
	if (i >= 0 && pLights && nFloats >= 17)
		m_Effects[i]->SetLights(pLights);
}

// 1 and the value when the var exists, else 0.
int32_t CManagedParticlesClient::ParticlesMaterialVarInt(uint32_t hOwner, int32_t nChannel, const char* pszVar,
														 int32_t* pValue)
{
	const int i = Find(hOwner, nChannel);
	IMaterial* pMaterial = i >= 0 ? m_Effects[i]->Material() : NULL;
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

void CManagedParticlesClient::ParticlesDestroy(uint32_t hOwner, int32_t nChannel)
{
	const int i = Find(hOwner, nChannel);
	if (i >= 0)
		m_Effects[i]->Release();
}

// Each effect sits at its owner every frame: a physics_cannister drags its steam jet.
void CManagedParticlesClient::Follow()
{
	for (int i = m_Effects.Count() - 1; i >= 0; i--)
	{
		CManagedParticleEffect* pEffect = m_Effects[i];

		if (pEffect->Owner() == SOURCESHARP_PARTICLE_NO_OWNER)
			continue;

		C_StandaloneEntity* pOwner = m_Standalone.FromHandle(pEffect->Owner());
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
void CManagedParticlesClient::PrintKinds() const
{
	for (int i = 0; i < m_Effects.Count(); i++)
	{
		CManagedParticleEffect* pEffect = m_Effects[i];
		const Vector vOrigin = pEffect->GetAbsOrigin();
		Msg("[SourceSharp] effect kind: managed-%d at (%.1f %.1f %.1f) particles %d owner %u space %d\n",
			pEffect->Channel(), vOrigin.x, vOrigin.y, vOrigin.z, pEffect->ActiveParticles(), pEffect->Owner(),
			pEffect->SpaceFlags());
	}

	Msg("[SourceSharp] effect kinds: %d executor(s)\n", m_Effects.Count());
}

void CManagedParticlesClient::CSystemRow::PreRender()
{
	m_Particles.Follow();
}

void CManagedParticlesClient::CSystemRow::LevelShutdownPostEntity()
{
	m_Particles.ReleaseAll();
}

void CManagedParticlesClient::ReleaseAll()
{
	while (m_Effects.Count())
	{
		const int nBefore = m_Effects.Count();
		m_Effects[0]->Release();
		if (m_Effects.Count() >= nBefore)
			m_Effects.Remove(0);
	}
}

// CParticleSystemsClient (shell_particle_systems_client.h).

CParticleSystemsClient::CParticleSystemsClient(const CClientEngineInterfaces& engines, CStandaloneClients& standalone,
											   CNewParticlesClient& newParticles)
	: m_Engines(engines)
	, m_Standalone(standalone)
	, m_NewParticles(newParticles)
{
}

CParticleSystemsClient::~CParticleSystemsClient()
{
	m_Watched.PurgeAndDeleteElements();
}

// The entries whose effect the manager has deleted.
void CParticleSystemsClient::Reap()
{
	for (int i = m_Watched.Count() - 1; i >= 0; i--)
	{
		if (m_Watched[i]->ref() == NULL)
		{
			delete m_Watched[i];
			m_Watched.Remove(i);
		}
	}
}

// Reaps first, so the table is bounded by the live effects.
void CParticleSystemsClient::Watch(CShellNewParticleEffect* pEffect, uint32_t hOwner, int32_t nKind)
{
	Reap();

	Watched_t* pWatch = new Watched_t;
	pWatch->ref.Set(pEffect);
	pWatch->hOwner = hOwner;
	pWatch->nKind = nKind;
	pWatch->flStarted = m_Engines.m_pGlobals->curtime;
	m_Watched.AddToTail(pWatch);
}

// A refused name is not watched: Start refuses it to managed.
CSmartPtr<CShellNewParticleEffect> CParticleSystemsClient::CreateWatched(uint32_t hOwner, const char* pszName)
{
	CSmartPtr<CShellNewParticleEffect> pEffect = m_NewParticles.Create(pszName, "SourceSharpFlame");
	if (pEffect.IsValid() && pEffect->IsValid())
		Watch(pEffect.GetObject(), hOwner, SOURCESHARP_PARTICLE_SYSTEM_OWNED);
	return pEffect;
}

// StopEmission rather than a release, so particles already in the air burn out.
void CParticleSystemsClient::ReleaseAt(Systems_t& systems, int nIndex)
{
	CShellNewParticleEffect* pEffect = systems[nIndex].pEffect.GetObject();

	if (pEffect)
		pEffect->StopEmission(false, false, true);

	systems.Remove(nIndex);
}

// One system per entity in a slot: starting again replaces the running one.
int32_t CParticleSystemsClient::StartAttached(Systems_t& systems, uint32_t hOwner, const char* pszName)
{
	if (!pszName || !pszName[0])
		return 0;

	C_StandaloneEntity* pOwner = m_Standalone.FromHandle(hOwner);

	if (!pOwner)
		return 0;

	int nExisting = ShellEffects_Find(systems, hOwner);

	if (nExisting >= 0)
		ReleaseAt(systems, nExisting);

	CSmartPtr<CShellNewParticleEffect> pEffect = CreateWatched(hOwner, pszName);

	// A name no .pcf defines yields an effect that draws nothing; refuse it so managed knows.
	if (!pEffect.IsValid() || !pEffect->IsValid())
		return 0;

	pEffect->SetControlPoint(0, pOwner->GetAbsOrigin());
	pEffect->SetControlPointOrientation(0, Vector(1, 0, 0), Vector(0, 1, 0), Vector(0, 0, 1));
	pEffect->SetSortOrigin(pOwner->GetAbsOrigin());

	System_t entry;
	entry.hOwner = hOwner;
	entry.pEffect = pEffect;
	entry.nFollowMask = kSourceSharpParticleFollowDefault;
	memset(entry.hFollowEntity, 0, sizeof(entry.hFollowEntity));
	memset(entry.nFollowAttachment, 0, sizeof(entry.nFollowAttachment));
	memset(&entry.vecFollowOffset, 0, sizeof(entry.vecFollowOffset));
	systems.AddToTail(entry);

	return 1;
}

void CParticleSystemsClient::StopAttached(Systems_t& systems, uint32_t hOwner)
{
	int nIndex = ShellEffects_Find(systems, hOwner);

	if (nIndex >= 0)
		ReleaseAt(systems, nIndex);
}

int32_t CParticleSystemsClient::ParticleSystemStart(uint32_t hOwner, const char* pszName)
{
	return StartAttached(m_Slots[0], hOwner, pszName);
}

// Which control points of a running system stay at the owner. A separate call so Start's
// ABI row is unchanged. Operators read an unwritten control point as the origin, and
// burning_character's initializer reads control point 1. Returns 0 if the entity has no
// system, which is a caller bug.
int32_t CParticleSystemsClient::ParticleSystemFollowPoints(uint32_t hOwner, int32_t nPointMask)
{
	int nIndex = ShellEffects_Find(m_Slots[0], hOwner);

	if (nIndex < 0)
		return 0;

	// Point 0 is always fed: it is where the system is placed.
	m_Slots[0][nIndex].nFollowMask = SourceSharpParticleFollow_Normalise(nPointMask);

	return 1;
}

// One control point of a running system follows an entity's attachment: the medigun beam's
// muzzle end rides its weapon, the far end its patient. A zero entity handle hands the
// point back to the owner feed. Returns 0 if the entity has no system, which is a caller
// bug.
int32_t CParticleSystemsClient::ParticleSystemFollowEntity(uint32_t hOwner, int32_t nPoint, uint32_t hEntity,
														   int32_t nAttachment, const float* pOffsetXYZ)
{
	return ParticleSystemFollowEntitySlot(hOwner, nPoint, hEntity, nAttachment, 0, pOffsetXYZ);
}

// The slot-addressed follow: the blast jump's smoke wears a foot per slot, beside the
// slot-0 point the medigun beam's ends follow. Every slot's sweep honours the entry, so
// only the setter had to learn the slot.
int32_t CParticleSystemsClient::ParticleSystemFollowEntitySlot(uint32_t hOwner, int32_t nPoint, uint32_t hEntity,
															   int32_t nAttachment, int32_t nSlot,
															   const float* pOffsetXYZ)
{
	Systems_t* pSystems = Slot(nSlot);

	if (!pSystems)
		return 0;

	int nIndex = ShellEffects_Find(*pSystems, hOwner);

	if (nIndex < 0)
		return 0;

	if (nPoint < 0 || nPoint > kSourceSharpParticleMaxFollowPoint)
		return 0;

	System_t& entry = (*pSystems)[nIndex];

	if (hEntity == 0)
	{
		entry.hFollowEntity[nPoint] = 0;
		entry.nFollowMask |= (1 << nPoint);
		return 1;
	}

	entry.hFollowEntity[nPoint] = hEntity;
	entry.nFollowAttachment[nPoint] = nAttachment;

	if (pOffsetXYZ)
		entry.vecFollowOffset[nPoint] = Vector(pOffsetXYZ[0], pOffsetXYZ[1], pOffsetXYZ[2]);
	else
		entry.vecFollowOffset[nPoint] = Vector(0, 0, 0);

	// A followed point is not fed the owner's origin.
	entry.nFollowMask &= ~(1 << nPoint);

	return 1;
}

void CParticleSystemsClient::ParticleSystemStop(uint32_t hOwner)
{
	StopAttached(m_Slots[0], hOwner);
}

// A second slot beside the first: stock's crit glow runs beside the trail.
int32_t CParticleSystemsClient::ParticleSystemStartExtra(uint32_t hOwner, const char* pszName)
{
	return StartAttached(m_Slots[1], hOwner, pszName);
}

void CParticleSystemsClient::ParticleSystemStopExtra(uint32_t hOwner)
{
	StopAttached(m_Slots[1], hOwner);
}

// A slot-addressed start, for a caller that counts its own systems: the drips
// wear slots 2 and 3, beside the burn's and the glow's. Starting again in a
// slot replaces that slot's system, as the named rows do; the other slots are
// untouched. An unknown slot refuses.
int32_t CParticleSystemsClient::ParticleSystemStartSlot(uint32_t hOwner, const char* pszName, int32_t nSlot)
{
	Systems_t* pSystems = Slot(nSlot);

	return pSystems ? StartAttached(*pSystems, hOwner, pszName) : 0;
}

void CParticleSystemsClient::ParticleSystemStopSlot(uint32_t hOwner, int32_t nSlot)
{
	if (Systems_t* pSystems = Slot(nSlot))
		StopAttached(*pSystems, hOwner);
}

// A one-shot an impact started: watched, not held.
void CParticleSystemsClient::WatchOneShot(CShellNewParticleEffect* pEffect)
{
	if (pEffect)
		Watch(pEffect, SOURCESHARP_PARTICLE_NO_OWNER, SOURCESHARP_PARTICLE_SYSTEM_ONE_SHOT);
}

// Every managed particle system moved to its entity: a move alone raises no data change.
// Backwards, since entries are removed mid-walk.
void CParticleSystemsClient::Sweep(Systems_t& systems)
{
	for (int i = systems.Count() - 1; i >= 0; i--)
	{
		CShellNewParticleEffect* pEffect = systems[i].pEffect.GetObject();

		if (!pEffect)
		{
			systems.Remove(i);
			continue;
		}

		C_StandaloneEntity* pOwner = m_Standalone.FromHandle(systems[i].hOwner);

		// The entity is gone; let the system finish rather than emit at its last position.
		if (!pOwner)
		{
			ReleaseAt(systems, i);
			continue;
		}

		// Every requested point gets the owner's position, as stock's C_EntityFlame binds both.
		const Vector vecOrigin = pOwner->GetAbsOrigin();
		const int32_t nMask = systems[i].nFollowMask;

		for (int nPoint = 0; nPoint <= kSourceSharpParticleMaxFollowPoint; nPoint++)
		{
			if (SourceSharpParticleFollow_Wants(nMask, nPoint))
				pEffect->SetControlPoint(nPoint, vecOrigin);
		}

		// A point an entity's attachment owns overrides the owner feed. A followed entity
		// that has gone leaves its last point; the system's owner stops the emission.
		for (int nPoint = 0; nPoint <= kSourceSharpParticleMaxFollowPoint; nPoint++)
		{
			if (!systems[i].hFollowEntity[nPoint])
				continue;

			IClientRenderable* pRenderable = m_Standalone.EntityList().RenderableFromHandle(
				CBaseHandle((int)(systems[i].hFollowEntity[nPoint] & ENT_ENTRY_MASK),
							(int)(systems[i].hFollowEntity[nPoint] >> NUM_SERIAL_NUM_SHIFT_BITS)));

			if (!pRenderable)
				continue;

			Vector vecAt;
			matrix3x4_t mat;
			if (pRenderable->GetAttachment(systems[i].nFollowAttachment[nPoint], mat))
			{
				MatrixGetColumn(mat, 3, vecAt);
			}
			else
			{
				// The entity has no such attachment (it is 0, or it has no model yet):
				// stock's C_BaseAnimating::GetAttachment leaves the caller's matrix
				// alone rather than failing, so fall back to the entity's origin.
				vecAt = pRenderable->GetRenderOrigin();
			}
			vecAt += systems[i].vecFollowOffset[nPoint];
			pEffect->SetControlPoint(nPoint, vecAt);
		}
		pEffect->SetSortOrigin(vecOrigin); // the leaf system culls by it
	}
}

void CParticleSystemsClient::PreRender()
{
	for (int nSlot = 0; nSlot < kSlots; nSlot++)
		Sweep(m_Slots[nSlot]);
}

// Stopped rather than forgotten: the particle manager holds a reference to each.
void CParticleSystemsClient::LevelShutdown()
{
	for (int nSlot = 0; nSlot < kSlots; nSlot++)
	{
		Systems_t& systems = m_Slots[nSlot];

		for (int i = systems.Count() - 1; i >= 0; i--)
			if (CShellNewParticleEffect* pEffect = systems[i].pEffect.GetObject())
				pEffect->StopEmission(false, true, true);

		systems.RemoveAll();
	}
}

void CParticleSystemsClient::CSystemRow::PreRender()
{
	m_Systems.PreRender();
}

void CParticleSystemsClient::CSystemRow::LevelShutdownPostEntity()
{
	m_Systems.LevelShutdown();
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

int32_t CParticleSystemsClient::ParticleSystemsRead(int32_t nIndex, SourceSharpParticleSystemInfo_t* pOut,
													char* pszName, int32_t nNameMax)
{
	Reap();

	const int32_t nCount = m_Watched.Count();
	if (nIndex < 0 || nIndex >= nCount || !pOut)
		return nCount;

	const Watched_t* pWatch = m_Watched[nIndex];
	CShellNewParticleEffect* pEffect = pWatch->ref();

	memset(pOut, 0, sizeof(*pOut));
	pOut->Kind = pWatch->nKind;
	pOut->Owner = pWatch->hOwner;
	pOut->Age = m_Engines.m_pGlobals->curtime - pWatch->flStarted;

	const int nOwned = ShellEffects_Find(m_Slots[0], pWatch->hOwner);
	const bool bHeld = nOwned >= 0 && m_Slots[0][nOwned].pEffect.GetObject() == pEffect;
	pOut->Stopped = (pWatch->nKind == SOURCESHARP_PARTICLE_SYSTEM_OWNED && !bHeld) ? 1 : 0;
	pOut->FollowMask = bHeld ? m_Slots[0][nOwned].nFollowMask : 0;

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

// CParticleSystemQueryClient (shell_particle_query_client.h).

void CParticleSystemQueryClient::GetLightingAtPoint(const Vector& vecOrigin, Color& tint)
{
	IManagedParticlesClient* pArea = Managed();
	uint8_t rgba[4];
	// The library simulates on several threads, and the engine's light lookup is not reentrant.
	AUTO_LOCK(m_LightMutex);
	if (pArea && pArea->QueryLighting(vecOrigin.Base(), rgba) == 1)
		tint.SetColor(rgba[0], rgba[1], rgba[2], rgba[3]);
	else
		Unanswered_Add(kLighting);
}

void CParticleSystemQueryClient::TraceLine(const Vector& vecAbsStart, const Vector& vecAbsEnd, unsigned int mask,
										   const IHandleEntity* ignore, int collisionGroup, CBaseTrace* ptr)
{
	IManagedParticlesClient* pArea = Managed();
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

int CParticleSystemQueryClient::GetCollisionGroupFromName(const char* pszCollisionGroupName)
{
	IManagedParticlesClient* pArea = Managed();
	const int32_t nGroup = (pArea && pszCollisionGroupName) ? pArea->QueryCollisionGroup(pszCollisionGroupName) : -1;
	if (nGroup >= 0)
		return nGroup;
	Unanswered_Add(kCollisionGroup);
	return COLLISION_GROUP_NONE;
}

void CParticleSystemQueryClient::GetRandomPointsOnControllingObjectHitBox(
	CParticleCollection* pParticles, int nControlPointNumber, int nNumPtsOut, float flBBoxScale,
	int nNumTrysToGetAPointInsideTheModel, Vector* pPntsOut, Vector vecDirectionalBias, Vector* pHitBoxRelativeCoordOut,
	int* pHitBoxIndexOut)
{
	const CParticleControlPoint& point = pParticles->m_ControlPoints[nControlPointNumber];
	IManagedParticlesClient* pArea = Managed();
	const int32_t nAnswer = pArea
		? pArea->QueryHitBoxPoints(nControlPointNumber, point.m_Position.Base(), point.m_pObject ? 1 : 0, nNumPtsOut,
								   pPntsOut ? pPntsOut->Base() : NULL, pHitBoxIndexOut,
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

int CParticleSystemQueryClient::GetControllingObjectHitBoxInfo(CParticleCollection* pParticles, int nControlPointNumber,
															   int nBufSize, ModelHitBoxInfo_t* pHitBoxOutputBuffer)
{
	IManagedParticlesClient* pArea = Managed();
	const int32_t nAnswer = pArea
		? pArea->QueryHitBoxInfo(nControlPointNumber,
								 pParticles->m_ControlPoints[nControlPointNumber].m_pObject ? 1 : 0, nBufSize)
		: -1;
	if (nAnswer >= 0 && nAnswer <= nBufSize)
		return nAnswer;
	Unanswered_Add(kHitBoxInfo);
	return 0;
}

bool CParticleSystemQueryClient::MovePointInsideControllingObject(CParticleCollection*, void*, Vector*)
{
	ShellClientDll_RefuseExternal("IParticleSystemQuery::MovePointInsideControllingObject");
	return true;
}

bool CParticleSystemQueryClient::IsPointInControllingObjectHitBox(CParticleCollection*, int, Vector, bool)
{
	ShellClientDll_RefuseExternal("IParticleSystemQuery::IsPointInControllingObjectHitBox");
	return true;
}

Vector CParticleSystemQueryClient::GetLocalPlayerPos()
{
	ShellClientDll_RefuseExternal("IParticleSystemQuery::GetLocalPlayerPos");
	return vec3_origin;
}

void CParticleSystemQueryClient::GetLocalPlayerEyeVectors(Vector* pForward, Vector* pRight, Vector* pUp)
{
	ShellClientDll_RefuseExternal("IParticleSystemQuery::GetLocalPlayerEyeVectors");
	*pForward = vec3_origin;
	*pRight = vec3_origin;
	*pUp = vec3_origin;
}

float CParticleSystemQueryClient::GetPixelVisibility(int*, const Vector&, float)
{
	ShellClientDll_RefuseExternal("IParticleSystemQuery::GetPixelVisibility");
	return 0.0f;
}

void CParticleSystemQueryClient::SetUpLightingEnvironment(const Vector&)
{
	ShellClientDll_RefuseExternal("IParticleSystemQuery::SetUpLightingEnvironment");
}

int32_t CParticleSystemQueryClient::ParticleQueryUnanswered(int32_t* pOut, int32_t nMax)
{
	int32_t n = 0;
	for (; pOut && n < nMax && n < kMethods; n++)
		pOut[n] = m_nUnanswered[n];
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
int32_t CParticleSystemsClient::ParticleSystemOneShot(const char* pszName, const float* pSortOriginXYZ,
													  const float* pPoints, int32_t nPoints, const float* pOrientation0)
{
	if (!pszName || !pSortOriginXYZ || (nPoints > 0 && !pPoints) || !pOrientation0)
		return 0;
	CSmartPtr<CShellNewParticleEffect> pEffect = m_NewParticles.Create(pszName);
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
	WatchOneShot(pEffect.GetObject());
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
void CManagedParticlesClient::ParticlesView(float* pModelView12, int32_t* pViewId, float* pOriginForward6)
{
	if (pModelView12)
	{
		const VMatrix& mv = ShellParticleDraw_ModelView();
		for (int r = 0; r < 3; r++)
			for (int c = 0; c < 4; c++)
				pModelView12[r * 4 + c] = mv[r][c];
	}
	if (pViewId)
		*pViewId = (int32_t)m_Cameras.CurrentID();
	if (pOriginForward6)
	{
		const Vector& vo = m_Cameras.CurrentOrigin();
		const Vector& vf = m_Cameras.CurrentForward();
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
