//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's standalone shells as one object, a member of the client DLL object: the
// live shells, the viewmodel-pass ones and the local player's, the frame's bone and draw
// batches, the per-frame block arrays managed reads, and the two client systems rows that
// walk them. It is the IShimStandaloneClient area; the slots whose bodies live in other
// files forward to them. Each shell is handed it at creation.

#pragma once

#include <stdint.h>

#include "utlvector.h"

#include "bound_callback.h"
#include "said_once_per_id.h"
#include "generic_nettable.h"
#include "generic_nettable.h"
#include "shell_clientsystem.h"
#include "ishim_areas_client.h"
#include "ishim_areas.h"
#include "shell_managed_areas_client.h"

class CClientEngineInterfaces;
class C_StandaloneEntity;
class CClientEntityList;
class CBeamsClient;
class CSpritesClient;
class CShadowsClient;
class CClientRenderablesList;
class ClientClass;
class RecvProp;
class RecvTable;
class IClientEntity;
class IClientNetworkable;
class IClientRenderable;
class Vector;
class QAngle;
struct SourceSharpBoneBatch_t;

class CStandaloneClients final : public IShimStandaloneClient, public IShimEntities
{
public:
	CStandaloneClients(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
					   CClientEntityList& entityList, CBeamsClient& beams, CSpritesClient& sprites,
					   CShadowsClient& shadows);

	CStandaloneClients(const CStandaloneClients&) = delete;
	CStandaloneClients& operator=(const CStandaloneClients&) = delete;

	// NULL outside a managed load.
	IManagedStandaloneClient* Managed() const { return m_Areas.m_pStandaloneClient; }

	IManagedBonesClient* Bones() const { return m_Areas.m_pBonesClient; }

	// The slot table each shell binds itself into.
	CClientEntityList& EntityList() const { return m_EntityList; }

	// The beams a removed entity's are killed through.
	// Slots of other areas that only ask for an entity (their files define them).
	int32_t GetLocalPlayerHealth();
	int32_t GetLocalPlayerOrigin(float* pOutXYZ);
	int32_t EntitiesUpdateVisibilityAll();
	void ClientImpactLedger(int32_t nFlecks, int32_t nDust);
	void RegisterTiers();
	int32_t NetHierarchyDeclare(int32_t nBlobBytes);
	bool TierIsInUse(int iTier);
	int32_t NetTierInfo(int32_t nShell, int32_t iTier, const char** ppOutTable, int32_t* pOutBytes, int32_t* pOutWords,
						int32_t* pOutProps, int32_t* pOutLive);
	int32_t NetPublishType(const char* pClassName, int32_t nShell, uint32_t nSchemaHash, int32_t nLayoutBytes,
						   uint32_t* pOutTypeId);
	int32_t ClientShotTrace(const float* pStartXYZ, const float* pEndXYZ, int32_t nEntity, float* pOutFloats,
							int32_t nOutFloats, int32_t* pOutInts, int32_t nOutInts);
	void ClientDecalShoot(int32_t nDecalIndex, int32_t nEntity, const float* pPositionXYZ, const float* pSAxisXYZ,
						  int32_t nFlags);
	void ClientPlayerLogoDecal(const float* pPositionXYZ, int32_t nPlayer, int32_t nEntity, const char* pszMaterial,
							   const char* pszTexture);
	int32_t ClientEntityModelType(int32_t nEntity);
	void ClientDecalStudioModel(int32_t nEntity, const float* pRayStartXYZ, const float* pRayDeltaXYZ,
								int32_t nDecalIndex);
	void ClientDecalBrushModel(int32_t nEntity, const float* pDecalCenterXYZ, int32_t nDecalIndex);
	int32_t EntityIsValid(uint32_t hEntity) override;
	void EntityGetOrigin(uint32_t hEntity, float* pOutXYZ) override;
	void EntityGetAngles(uint32_t hEntity, float* pOutPYR) override;

	CBeamsClient& Beams() const { return m_Beams; }

	CSpritesClient& Sprites() const { return m_Sprites; }

	CShadowsClient& Shadows() const { return m_Shadows; }

	// The live shells, in creation order but for removals.
	void Add(C_StandaloneEntity* pShell) { m_Shells.AddToTail(pShell); }

	void Remove(C_StandaloneEntity* pShell);

	int Count() const { return m_Shells.Count(); }

	C_StandaloneEntity* At(int nIndex) const;

	// The shell a packed handle's entry names (the serial is not compared), or NULL.
	C_StandaloneEntity* FromHandle(uint32_t hEntity) const;

	CSaidOncePerId& SaidUnknownType() { return m_SaidUnknownType; }

	CSourceSharpTypeTable& NetTypes() { return m_NetTypes; }

	C_StandaloneEntity* LocalPlayer() const { return m_pLocalPlayer; }

	void SetLocalPlayer(C_StandaloneEntity* pShell) { m_pLocalPlayer = pShell; }

	// Whether the camera shows the local player's body: the client mode's third-person
	// camera (a camera pulled onto the eye by a wall is first person whatever mode was
	// typed), or a second camera that shows the viewer, decides; the player's model and
	// viewmodel draw by it.
	const CClientEngineInterfaces& Engines() const { return m_Engines; }

	bool ThirdPersonSeesOwner() const { return m_bThirdPersonSeesOwner; }

	// Whether this side recognises the type and layout an update was written with; false
	// means the bytes must not be decoded. The decision is managed's. *ppOutClassName gets
	// the classname the type id resolves to.
	bool SchemaAccept(unsigned int nTypeId, unsigned int nSchemaHash, int nEntIndex, const char** ppOutClassName);

	// Shim slots: the decals and lighting the temp entity backends ask of the engine.
	int32_t ClientDecalIndexFromName(const char* pName);
	void ClientDecalStaticProp(const float* pStartXYZ, const float* pEndXYZ, int32_t nStaticPropIndex,
							   int32_t nDecalIndex);
	void ClientWorldLighting(const float* pStartXYZ, const float* pEndXYZ, float* pOut6);
	void ClientStaticPropLighting(const float* pStartXYZ, const float* pEndXYZ, const float* pNormalXYZ,
								  int32_t nStaticProp, float* pOut6);

	// Shim slots over a handle.
	int32_t EntityGetIndex(uint32_t hEntity) override;
	void StandaloneSetModelScale(uint32_t hEntity, float flScale);
	void EntityGetModelName(uint32_t hEntity, char* pOut, int32_t nOutSize);

	void SetThirdPersonSeesOwner(bool bSees) { m_bThirdPersonSeesOwner = bSees; }

	// The resting answer's setter, with the edge the leaf system needs: the leaf
	// membership of a world entity is decided once, never re-asked, so a flip of
	// the flag must re-ask every shell or the local player's carried weapon keeps
	// the leaf state of the camera it was created under (stock re-runs
	// C_BaseCombatWeapon::ShouldDraw, third person and all, every frame).
	void SetRestingThirdPersonSeen(bool bSees);

	// Every viewmodel-pass entity rides the eye: the viewmodel and its bone-merged attachments.
	void SetViewModelPass(C_StandaloneEntity* pShell, bool bEnable);
	void ViewModelToEye(const Vector& vecEye, const QAngle& angEye);

	// Releases every shell; the count released.
	int LevelShutdown();

	// The frame's bone batch from managed, fetched once per frame; NULL with none.
	const SourceSharpBoneBatch_t* FrameBoneBatch(IManagedBonesClient* pBones);

	// The leaf system's list for a view: its custom-draw entities are drawn from one batch.
	void DrawBatchCapture(const CClientRenderablesList& list);
	float DrawBatchBlend(IClientEntity* pEntity, float flFallback) const;

	// A custom-draw entity's batch entry, asking managed once per batch; -1 when it has none,
	// kDrawBatchInPlace when managed drew it in place.
	static const int32_t kDrawBatchInPlace = -2;
	bool DrawBatchResult(const C_StandaloneEntity* pEntity, int32_t* pOutResult);

	// The two client systems rows: the tiers' registration, and the per-frame policy and pose.
	CClientSystem& TierSystem() { return m_TierSystem; }

	CClientSystem& InterpSystem() { return m_InterpSystem; }

	// IShimStandaloneClient.
	void ActivateOccluder(int32_t nOccluderIndex, int32_t bActive) override;
	void** ClientInterpBlocks(int32_t* pOutCount, float* pOutInterpAmount) override;
	void** ClientPolicyBlocks(int32_t* pOutCount) override;
	void DynamicLightRelease(int32_t nKey) override;
	void DynamicLightUpdate(const SourceSharpDynamicLight* pLight) override;
	int32_t StandaloneIsDormant(uint32_t hEntity) override;
	void StandaloneSetBlending(uint32_t hEntity, int32_t bTranslucent, int32_t bIgnoreZ, int32_t nAlpha) override;
	void StandaloneSetCustomDraw(uint32_t hEntity, const float* pMinsXYZ, const float* pMaxsXYZ) override;
	void StandaloneSetFade(uint32_t hEntity, int32_t nStyle, float flStartDist, float flEndDist,
						   float flMinAlpha) override;
	void StandaloneSetOverlayModel(uint32_t hEntity, int32_t nModelIndex) override;
	void StandaloneSetViewModel(uint32_t hEntity, int32_t bEnable) override;
	int32_t StandaloneShadowCast(uint32_t hEntity, int32_t* pOutChoice) override;
	int32_t ClientRecvProp(int32_t iIndex, char* pOutClass, int32_t nClassBytes, char* pOutTable, int32_t nTableBytes,
						   char* pOutProp, int32_t nPropBytes, int32_t* pOutType, int32_t* pOutFlags,
						   int32_t* pOutOffset, int32_t* pOutElements) override;
	int32_t ClientSchemaRefusal(int32_t iIndex, uint32_t* pOutTypeId, uint32_t* pOutExpected, uint32_t* pOutSeen,
								int32_t* pOutCount, char* pOutClass, int32_t nClassBytes,
								int32_t* pOutRefusedUpdates) override;
	void* StandaloneEngineBlock(uint32_t hEntity, int32_t* pOutBytes) override;
	void StandaloneStoreSequence(uint32_t hEntity, int32_t nSequence, float flPlaybackRate, float flAnimTime,
								 float flDuration, int32_t bLooping) override;
	void StandaloneSetRagdoll(uint32_t hEntity, int32_t bRagdoll) override;
	void EntitySetSkin(uint32_t hEntity, int32_t nSkin) override;
	void RegisterAmmoType(const char* pName, int32_t nDamageType, int32_t nMaxCarry, int32_t nTracerType,
						  float flDamageForce, float flDamage) override;
	int32_t StandaloneSurroundingBounds(const uint32_t* pHandles, int32_t nCount, float* pOutMinsMaxs) override;
	int32_t SurfaceMaterialFacts(uint32_t hEntity, char* pOutMaterial, int32_t nMaterialBytes, char* pOutCrack,
								 int32_t nCrackBytes, int32_t* pOutHasCrack) override;
	void SurfaceSetMaterials(uint32_t hEntity, const char* pNames, int32_t nBytes, int32_t nFlags) override;
	void SurfaceSetPlan(uint32_t hEntity, const int32_t* pWords, int32_t nWords) override;
	void ClientInterpAdmitted(const uint32_t* pHandles, int32_t nCount) override;
	void EntitySetModelOverride(uint32_t hEntity, const char* pModel) override;

private:
	// Registers the tiers' ClientClasses and hands each its create function.
	class CTierSystem final : public CClientSystem
	{
	public:
		explicit CTierSystem(CStandaloneClients& clients)
			: CClientSystem("CSourceSharpStandaloneClientSystem")
			, m_Clients(clients)
		{
		}

		bool Init() override;

	private:
		CStandaloneClients& m_Clients;
	};

	// A tier's ClientClass create function. The engine calls it with no context, so it is a
	// thunk bound to this object (bound_callback.h) that knows its tier.
	class CTierCreate
	{
	public:
		typedef CBoundCallback<IClientNetworkable*(int, int)> Fn_t;

		CTierCreate(CStandaloneClients& clients, int iTier)
			: m_Clients(clients)
			, m_iTier(iTier)
			, m_Fn(this, &Fn_t::Trampoline<CTierCreate, &CTierCreate::Create>)
		{
		}

		CTierCreate(const CTierCreate&) = delete;
		CTierCreate& operator=(const CTierCreate&) = delete;

		// NULL when the system refused the thunk's page.
		Fn_t::Fn Get() const { return m_Fn.Get(); }

	private:
		IClientNetworkable* Create(int nEntNum, int nSerialNum)
		{
			return m_Clients.CreateTier(m_iTier, nEntNum, nSerialNum);
		}

		CStandaloneClients& m_Clients;
		const int m_iTier;
		Fn_t m_Fn;
	};

	class CInterpSystem final : public CClientSystemPerFrame
	{
	public:
		explicit CInterpSystem(CStandaloneClients& clients)
			: CClientSystemPerFrame("CSourceSharpStandaloneInterpSystem")
			, m_Clients(clients)
		{
		}

		void PreRender() override;
		void LevelShutdownPostEntity() override;

	private:
		CStandaloneClients& m_Clients;
	};

	struct DrawBatchEntry_t
	{
		C_StandaloneEntity* pEntity;
		uint32_t hHandle;
		float flBlend;
		int32_t nResult;
	};

	// A new shell of the tier's class for the engine's entry and serial.
	IClientNetworkable* CreateTier(int iTier, int nEntNum, int nSerialNum);

	int FindInDrawBatch(const C_StandaloneEntity* pEntity) const;
	C_StandaloneEntity* FromRenderable(IClientRenderable* pRenderable) const;
	void AskDrawBatch();

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	CClientEntityList& m_EntityList;
	CBeamsClient& m_Beams;
	CSpritesClient& m_Sprites;
	CShadowsClient& m_Shadows;

	CUtlVector<C_StandaloneEntity*> m_Shells;
	CUtlVector<C_StandaloneEntity*> m_ViewModels;
	C_StandaloneEntity* m_pLocalPlayer = nullptr;
	bool m_bThirdPersonSeesOwner = false;

	int m_nBoneBatchFrame = -1;
	const SourceSharpBoneBatch_t* m_pBoneBatch = nullptr;

	CUtlVector<DrawBatchEntry_t> m_DrawBatch;
	bool m_bDrawBatchAsked = false;
	bool m_bDrawBatchPreparing = false;

	// Rebuilt each call; valid until the next. The policy array is unfiltered: the
	// viewmodel, which interpolation skips, needs deciding most.
	CUtlVector<void*> m_InterpBlocks;
	CUtlVector<void*> m_PolicyBlocks;

	CTierSystem m_TierSystem{*this};
	CInterpSystem m_InterpSystem{*this};

	// One per tier, so a missing one does not compile.
	// One ClientClass per tier; only the create function knows an entity's tier.
	RecvProp* m_pTierProps[SOURCESHARP_TIER_MAX] = {};
	RecvTable* m_pTierTables[SOURCESHARP_TIER_MAX] = {};
	ClientClass* m_pTierClasses[SOURCESHARP_TIER_MAX] = {};
	bool m_bTiersRegistered = false;
	CSaidOncePerId m_SaidUnknownType;
	CSourceSharpTypeTable m_NetTypes;

	CTierCreate m_TierCreate[SOURCESHARP_TIER_MAX] = {{*this, 0}, {*this, 1}, {*this, 2}, {*this, 3},
													  {*this, 4}, {*this, 5}, {*this, 6}};
};
