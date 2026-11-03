//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// CTempEnts (a member of the client DLL object), and
// C_LocalTempEntity's renderable members; managed code decides everything they
// carry out. ITempEnts slots nothing in this mod calls warn once if called.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "tempent.h"
#include "c_te_legacytempents.h"
#include "clienteffectprecachesystem.h"

#include "model_types.h"

#include "particlemgr.h"

#include "imanaged_areas_client.h"
#include "shell_managed_areas_client.h"
#include "shell_sprites_client.h"
#include "client_entity_list.h"
#include "shell_view_cameras.h"
#include "shell_client_engine_interfaces.h"

// The pool's seam (shell_te_pool_client.cpp).

static void PutVector(float* p, const Vector& v)
{
	p[0] = v.x;
	p[1] = v.y;
	p[2] = v.z;
}

C_LocalTempEntity::C_LocalTempEntity()
{
#ifdef _DEBUG
	tentOffset.Init();
	m_vecTempEntVelocity.Init();
	m_vecTempEntAngVelocity.Init();
	m_vecNormal.Init();
#endif
	m_vecTempEntAcceleration.Init();
	m_pfnDrawHelper = 0;

	// The block arrives zeroed; these are the values zero is wrong for.
	// m_nFXComputeFrame: zero is a real frame number.
	index = -1;
	m_pModel = NULL;
	m_nRenderMode = kRenderNormal;
	m_nRenderFX = kRenderFxNone;
	m_clrRender.r = m_clrRender.g = m_clrRender.b = m_clrRender.a = 255;
	m_nRenderFXBlend = 255;
	m_nFXComputeFrame = -1;
}

// Resets place and model for recycled memory; the managed spawn record writes
// every other field straight after.
void C_LocalTempEntity::Prepare(const model_t* pmodel, float time)
{
	index = -1;
	SetAbsOrigin(vec3_origin);
	SetAbsAngles(vec3_angle);
	SetModelPointer(pmodel);
}

void C_LocalTempEntity::SetVelocity(const Vector& vecVelocity)
{
	m_vecTempEntVelocity = vecVelocity;
}

void C_LocalTempEntity::SetAcceleration(const Vector& vecVelocity)
{
	m_vecTempEntAcceleration = vecVelocity;
}

IHandleEntity* C_LocalTempEntity::TempEntOwnerHandle() const
{
	return m_pPool->EntityList().UnknownFromHandle(m_hOwnerEntity);
}

IManagedFxClient* C_LocalTempEntity::ManagedFx() const
{
	return m_pAreas->m_pFxClient;
}

// Kept for the vtable; the managed pool owns lifetime and frame.
bool C_LocalTempEntity::IsActive()
{
	return true;
}

bool C_LocalTempEntity::Frame(float frametime, int framenumber)
{
	return true;
}

// The managed pool removes a slot from its breakmodel group when it frees it.
void C_LocalTempEntity::OnRemoveTempEntity() {}

bool C_LocalTempEntity::IsTransparent()
{
	IManagedFxClient* pFx = ManagedFx();
	const int bTranslucent = Pool().Engines().m_pModelInfo->IsTranslucent(GetModel()) ? 1 : 0;
	return pFx ? pFx->TempEntIsTransparent(bTranslucent, m_nRenderMode) != 0 : bTranslucent != 0;
}

void C_LocalTempEntity::GetRenderBounds(Vector& theMins, Vector& theMaxs)
{
	if (!GetModel())
	{
		theMins = vec3_origin;
		theMaxs = vec3_origin;
		return;
	}

	Pool().Engines().m_pModelInfo->GetModelRenderBounds(GetModel(), theMins, theMaxs);
}

void C_LocalTempEntity::GetColorModulation(float* color)
{
	color[0] = m_clrRender.r / 255.0f;
	color[1] = m_clrRender.g / 255.0f;
	color[2] = m_clrRender.b / 255.0f;
}

RenderGroup_t C_LocalTempEntity::GetRenderGroup()
{
	IManagedFxClient* pFx = ManagedFx();
	if (!pFx)
		return RENDER_GROUP_OPAQUE_ENTITY;

	// The blend this frame, bypassing GetFxBlend's frame protection as stock does:
	// this is asked from the leaf-system add, before any blend is computed.
	int nBlend = 255;
	if (m_nRenderMode != kRenderNone)
	{
		const int nTempComputeFrame = m_nFXComputeFrame;
		m_nFXComputeFrame = Pool().Engines().m_pGlobals->framecount;
		nBlend = GetFxBlend();
		m_nFXComputeFrame = nTempComputeFrame;
	}

	const int bTranslucent = Pool().Engines().m_pModelInfo->IsTranslucent(GetModel()) ? 1 : 0;
	const int bTwoPass = Pool().Engines().m_pModelInfo->IsTranslucentTwoPass(GetModel()) ? 1 : 0;
	return (RenderGroup_t)pFx->TempEntRenderGroup(m_nRenderMode, nBlend, bTranslucent, bTwoPass);
}

void C_LocalTempEntity::ComputeFxBlend()
{
	// Don't recompute if we've already computed this frame
	if (m_nFXComputeFrame == Pool().Engines().m_pGlobals->framecount)
		return;

	IManagedFxClient* pFx = ManagedFx();
	if (!pFx)
		return;

	float in[14];
	in[0] = (float)m_nRenderFX;
	in[1] = (float)m_clrRender.a;
	in[2] = (float)m_nRenderMode;
	in[3] = (float)index;
	in[4] = Pool().Engines().m_pGlobals->curtime;
	PutVector(in + 5, GetAbsOrigin());
	PutVector(in + 8, Pool().Cameras().CurrentOrigin());
	PutVector(in + 11, Pool().Cameras().CurrentForward());

	int32_t out[2] = {255, m_clrRender.a};
	pFx->TempEntBlend(in, out);

	SetRenderColorA((byte)out[1]);
	m_nRenderFXBlend = out[0];
	m_nFXComputeFrame = Pool().Engines().m_pGlobals->framecount;

	// The render group follows the blend, as stock's GetRenderHandle() update did.
	if (IsInLeafSystem())
		Pool().Engines().m_pLeafSystem->SetRenderGroup(RenderHandle(), GetRenderGroup());
}

int C_LocalTempEntity::DrawStudioModel(int modelFlags)
{
	if (!GetModel() || Pool().Engines().m_pModelInfo->GetModelType(GetModel()) != mod_studio)
		return 0;

	MDLCACHE_CRITICAL_SECTION_(Pool().Engines().m_pMdlCache);
	if (!Pool().Engines().m_pModelInfo->GetStudiomodel(GetModel()))
		return 0;

	if (m_pfnDrawHelper)
		return (*m_pfnDrawHelper)(this, modelFlags);

	return Pool().Engines().m_pModelRender->DrawModel(modelFlags, this, MODEL_INSTANCE_INVALID, index, GetModel(),
													  GetAbsOrigin(), GetAbsAngles(), m_nSkin, m_nBody, m_nHitboxSet);
}

int C_LocalTempEntity::DrawModel(int modelFlags)
{
	if (!GetModel() || GetRenderMode() == kRenderNone)
		return 0;

	// The occlusion fade (FTENT_BEOCCLUDED): managed code answers the alpha.
	if (this->flags & FTENT_BEOCCLUDED)
	{
		if (IManagedFxClient* pFx = ManagedFx())
		{
			float in[9];
			PutVector(in, m_vecNormal);
			PutVector(in + 3, GetAbsOrigin());
			PutVector(in + 6, Pool().Cameras().MainOrigin());
			SetRenderColorA((byte)pFx->TempEntOccludedAlpha(flags, m_clrRender.a, tempent_renderamt, in));
		}
	}

	switch (Pool().Engines().m_pModelInfo->GetModelType(GetModel()))
	{
		case mod_sprite:
			// NULL entity: a tempent has no IClientEntity for the material proxies, and
			// they treat NULL as "not an entity".
			return m_pPool->Sprites().PlanSprite(&m_queryHandle, NULL, GetModel(), GetAbsOrigin(), GetAbsAngles(),
												 m_flFrame, NULL, m_nSkin, GetRenderMode(), m_nRenderFX, m_clrRender.a,
												 m_clrRender.r, m_clrRender.g, m_clrRender.b, m_flSpriteScale,
												 /*bWorldSpaceScale=*/false,
												 /*flHDRColorScale=*/1.0f, PIXELVIS_DEFAULT_PROXY_SIZE);
		case mod_studio:
			return DrawStudioModel(modelFlags);
		default:
			return 0;
	}
}

// Every slot is the render transform: no other shell renderable has a studio model to
// pose (only C_LocalTempEntity answers GetModel), so none asks managed code.
bool CShellRenderable::SetupBones(matrix3x4_t* pBoneToWorldOut, int nMaxBones, int boneMask, float currentTime)
{
	if (!pBoneToWorldOut || nMaxBones <= 0)
		return true;

	const matrix3x4_t& matRender = RenderableToWorldTransform();
	for (int i = 0; i < nMaxBones; i++)
		MatrixCopy(matRender, pBoneToWorldOut[i]);

	return true;
}

// Managed code builds stock's tempent pose (sequence 0, cycle 0, zero pose parameters)
// and writes the render transform into every slot the build did not; with no managed
// side, or a non-studio model, every slot is the render transform.
bool C_LocalTempEntity::SetupBones(matrix3x4_t* pBoneToWorldOut, int nMaxBones, int boneMask, float currentTime)
{
	if (!pBoneToWorldOut || nMaxBones <= 0)
		return true;

	const matrix3x4_t& matRender = RenderableToWorldTransform();
	const model_t* pModel = GetModel();
	IManagedBonesClient* pBones = m_pAreas->m_pBonesClient;

	if (pBones && pModel && Pool().Engines().m_pModelInfo->GetModelType(pModel) == mod_studio)
	{
		const Vector& vecOrigin = GetRenderOrigin();
		const QAngle& angAngles = GetRenderAngles();
		const float flPlace[6] = {vecOrigin.x, vecOrigin.y, vecOrigin.z, angAngles.x, angAngles.y, angAngles.z};

		if (pBones->PoseModel(
				Pool().Engines().m_pModelInfo->GetModelIndex(Pool().Engines().m_pModelInfo->GetModelName(pModel)),
				flPlace, matRender.Base(), pBoneToWorldOut[0].Base(), nMaxBones, boneMask, currentTime,
				Pool().Engines().m_pGlobals->framecount))
			return true;
	}

	for (int i = 0; i < nMaxBones; i++)
		MatrixCopy(matRender, pBoneToWorldOut[i]);

	return true;
}

// The breakmodel draw, lit from the group head's origin as managed code answers it.
static int BreakModelDrawHelper(C_LocalTempEntity* entity, int flags)
{
	ModelRenderInfo_t sInfo;
	sInfo.flags = flags;
	sInfo.pRenderable = entity;
	sInfo.instance = MODEL_INSTANCE_INVALID;
	sInfo.entity_index = entity->index;
	sInfo.pModel = entity->GetModel();
	sInfo.origin = entity->GetRenderOrigin();
	sInfo.angles = entity->GetRenderAngles();
	sInfo.skin = entity->m_nSkin;
	sInfo.body = entity->m_nBody;
	sInfo.hitboxset = entity->m_nHitboxSet;

	if (IManagedFxClient* pFx = entity->ManagedFx())
	{
		const int nHead = pFx->TempEntLightingOrigin(entity->Pool().PoolSlotOf(entity));
		if (C_LocalTempEntity* pHead = entity->Pool().PoolAtSlot(nHead))
			sInfo.pLightingOrigin = &pHead->GetAbsOrigin();
	}

	return entity->Pool().Engines().m_pModelRender->DrawModelEx(sInfo);
}

void SourceSharpTePool_JoinBreakGroup(C_LocalTempEntity* pTemp, bool bSlave)
{
	if (pTemp)
		pTemp->SetDrawHelper(BreakModelDrawHelper);
}

CTempEnts::CTempEnts(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
					 const CViewCameras& cameras, CSpritesClient& sprites, CClientEntityList& entityList)
	: m_TempEntsPool((MAX_TEMP_ENTITIES / 20), CUtlMemoryPool::GROW_SLOW)
	, m_Areas(areas)
	, m_Engines(engines)
	, m_Cameras(cameras)
	, m_Sprites(sprites)
	, m_EntityList(entityList)
{
}

CTempEnts::~CTempEnts()
{
	m_TempEntsPool.Clear();
	m_TempEnts.RemoveAll();
}

void CTempEnts::Init()
{
	Clear();
}

void CTempEnts::Shutdown()
{
	LevelShutdown();
}

void CTempEnts::LevelInit()
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->TempEntsLevelInit();
}

void CTempEnts::LevelShutdown()
{
	Clear();
}

void CTempEnts::Update()
{
	if (!m_Engines.m_pEngine->IsInGame())
		return;

	PoolUpdate(m_Engines.m_pGlobals->frametime);
}

void CTempEnts::Clear()
{
	PoolClear();
}

C_LocalTempEntity* CTempEnts::FindTempEntByID(int nID, int nSubID)
{
	return PoolFind(nID, nSubID);
}

C_LocalTempEntity* CTempEnts::TempEntAlloc()
{
	return m_TempEntsPool.AllocZero();
}

int CTempEnts::AddVisibleTempEntity(C_LocalTempEntity* pEntity)
{
	if (!pEntity->GetModel())
		return 0;

	pEntity->index = -1;
	if (pEntity->m_RenderGroup == RENDER_GROUP_OTHER)
		pEntity->AddToLeafSystem(m_Engines.m_pLeafSystem);
	else
		pEntity->AddToLeafSystem(m_Engines.m_pLeafSystem, pEntity->m_RenderGroup);
	return 1;
}

void CTempEnts::MuzzleFlash(int type, ClientEntityHandle_t hEntity, int attachmentIndex, bool firstPerson)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->TempEntMuzzleFlash(type, (uint32_t)hEntity.ToInt(), attachmentIndex, firstPerson ? 1 : 0, 0);
}

void CTempEnts::MuzzleFlash(const Vector& pos1, const QAngle& angles, int type, ClientEntityHandle_t hEntity,
							bool firstPerson)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->TempEntMuzzleFlash(type, (uint32_t)hEntity.ToInt(), 1, firstPerson ? 1 : 0, 1);
}

void CTempEnts::EjectBrass(const Vector& pos1, const QAngle& angles, const QAngle& gunAngles, int type)
{
	IManagedFxClient* pFx = m_Areas.m_pFxClient;
	if (!pFx)
		return;
	float in[9];
	PutVector(in, pos1);
	in[3] = angles.x;
	in[4] = angles.y;
	in[5] = angles.z;
	in[6] = gunAngles.x;
	in[7] = gunAngles.y;
	in[8] = gunAngles.z;
	pFx->TempEntEjectBrass(in, type);
}

void CTempEnts::AttachTentToPlayer(int client, int modelIndex, float zoffset, float life)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->TempEntAttach(client, modelIndex, zoffset, life, m_Engines.m_pGlobals->maxClients);
}

// The slots nothing in this mod calls: each warns once.
// Once per name: nothing in this mod calls these.
void CTempEnts::SayNoCaller(const char* pszName)
{
	if (m_NoCallersSaid.Find(pszName) != m_NoCallersSaid.InvalidIndex())
		return;

	m_NoCallersSaid.AddToTail(pszName);
	DevWarning("[SourceSharp] CTempEnts::%s was called; nothing in this mod calls it, and the "
			   "managed temp entities spawn through TempEntSpawns. Nothing drawn.\n",
			   pszName);
}

void CTempEnts::BloodSprite(const Vector&, int, int, int, int, int, int, float)
{
	SayNoCaller("BloodSprite");
}

void CTempEnts::RicochetSprite(const Vector&, model_t*, float, float)
{
	SayNoCaller("RicochetSprite");
}

void CTempEnts::BreakModel(const Vector&, const QAngle&, const Vector&, const Vector&, float, float, int, int, char)
{
	SayNoCaller("BreakModel");
}

void CTempEnts::Bubbles(const Vector&, const Vector&, float, int, int, float)
{
	SayNoCaller("Bubbles");
}

void CTempEnts::BubbleTrail(const Vector&, const Vector&, float, int, int, float)
{
	SayNoCaller("BubbleTrail");
}

void CTempEnts::Sprite_Explode(C_LocalTempEntity*, float, int)
{
	SayNoCaller("Sprite_Explode");
}

void CTempEnts::FizzEffect(IClientRenderable*, int, int, int)
{
	SayNoCaller("FizzEffect");
}

C_LocalTempEntity* CTempEnts::DefaultSprite(const Vector&, int, float)
{
	SayNoCaller("DefaultSprite");
	return NULL;
}

void CTempEnts::Sprite_Smoke(C_LocalTempEntity*, float)
{
	SayNoCaller("Sprite_Smoke");
}

C_LocalTempEntity* CTempEnts::TempSprite(const Vector&, const Vector&, float, int, int, int, float, float, int,
										 const Vector&)
{
	SayNoCaller("TempSprite");
	return NULL;
}

void CTempEnts::KillAttachedTents(int)
{
	SayNoCaller("KillAttachedTents");
}

void CTempEnts::Sprite_Spray(const Vector&, const Vector&, int, int, int, int)
{
	SayNoCaller("Sprite_Spray");
}

void CTempEnts::PlaySound(C_LocalTempEntity*, float)
{
	SayNoCaller("PlaySound");
}

C_LocalTempEntity* CTempEnts::SpawnTempModel(const model_t*, const Vector&, const QAngle&, const Vector&, float, int)
{
	SayNoCaller("SpawnTempModel");
	return NULL;
}

void CTempEnts::Sprite_Trail(const Vector&, const Vector&, int, int, float, float, float, int, float)
{
	SayNoCaller("Sprite_Trail");
}

void CTempEnts::RocketFlare(const Vector&)
{
	SayNoCaller("RocketFlare");
}

void CTempEnts::HL1EjectBrass(const Vector&, const QAngle&, const Vector&, int)
{
	SayNoCaller("HL1EjectBrass");
}

void CTempEnts::CSEjectBrass(const Vector&, const QAngle&, int, int, CBasePlayer*)
{
	SayNoCaller("CSEjectBrass");
}

void CTempEnts::PhysicsProp(int, int, const Vector&, const QAngle&, const Vector&, int, int)
{
	SayNoCaller("PhysicsProp");
}

C_LocalTempEntity* CTempEnts::ClientProjectile(const Vector&, const Vector&, const Vector&, int, int, IGameEntity*,
											   const char*, const char*)
{
	SayNoCaller("ClientProjectile");
	return NULL;
}

C_PhysPropClientside* CTempEnts::PhysicsProp(const model_t*, int, const Vector&, const QAngle&, const Vector&, int, int,
											 int)
{
	SayNoCaller("PhysicsProp(model)");
	return NULL;
}
