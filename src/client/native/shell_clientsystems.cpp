//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// CClientSystems: the client's game systems as one named table. The order is
// stock's observed runtime order: the systems CHLClient::Init added by hand
// first, then the auto-registered ones in reverse construction order. Not rows:
// CToolFrameworkClient, CBasePresence, CBaseGameStats_Driver and
// CRagdollLRURetirement left with their files.

#include "detailobjectsystem.h"
#include "clienteffectprecachesystem.h"

#include "cdll_client_int.h"

#include "shell_clientsystems.h"

// Stock's IGameSystem keys its vtable on this destructor. client.so keeps no
// system list of its own, so the body is empty.
IGameSystem::~IGameSystem() {}

// The accessors. Declared here because this table is their only other reader.
CClientSystem* GameStringSystem();

CClientSystem* ClientSystem_StandaloneClient();
CClientSystem* ClientSystem_DirtySpatialPartition();
CClientSystem* ClientSystem_BulletWhizTimer();
CClientSystem* ClientSystem_Sprite();
CClientSystem* ClientSystem_WorldText();
CClientSystem* ClientSystem_Rope();
CClientSystem* ClientSystem_ManagedParticles();
CClientSystem* ClientSystem_StandaloneInterp();

// Stock's accessors answer a more specific type; the table's adapters keep its
// column uniform.
class IClientLeafSystem;

IClientLeafSystem* ClientLeafSystem();
IDetailObjectSystem* DetailObjectSystem();
CClientEffectPrecacheSystem* ClientEffectPrecacheSystem();

// The leaf system is libss_fwd_client's object, built without RTTI, so its row
// is an adapter the per-frame walks can dynamic_cast.
CClientSystem* ClientLeafSystemGameSystem();

// The shadow manager's row is shell_shadows_client.cpp's adapter over the managed manager.
CClientSystem* ClientShadowMgrGameSystem();

// The map's base name, or "" when no level is loaded.
const char* SourceSharp_ClientMapName(char* pszOut, int nOutBytes)
{
	if (pszOut == NULL || nOutBytes <= 0)
		return "";

	pszOut[0] = 0;

	const char* pszLevel = engine ? engine->GetLevelName() : NULL;
	if (pszLevel == NULL || pszLevel[0] == 0)
		return pszOut;

	V_FileBase(pszLevel, pszOut, nOutBytes);
	return pszOut;
}

CClientSystems::CClientSystems(const CManagedAreasClient& areas, CClientSystem& managedFrame, CClientSystem& physics,
							   CClientSystem& decals)
	: m_ManagedFrame(managedFrame)
	, m_Physics(physics)
	, m_Decals(decals)
	, m_ViewportRow(areas, "CViewportClientSystem")
	, m_GlowOverlayRow(areas, "CGlowOverlaySystem")
	, m_PixelVisibilityRow(areas, "CPixelVisibilitySystem")
	, m_SoundEmitterRow(areas)
	, m_SoundscapeRow(areas)
{
}

CClientSystems::~CClientSystems() {}

bool CClientSystems::Init()
{
	m_DetailObjectRow = CGameSystemRow<IDetailObjectSystem>(DetailObjectSystem());
	m_EffectPrecacheRow = CGameSystemRow<CClientEffectPrecacheSystem>(ClientEffectPrecacheSystem());

	const Row_t rows[] = {
		// Stock's hand-added systems, in CHLClient::Init's order.
		{"CGameStringPool", GameStringSystem()},
		{"CSoundEmitterSystem", &m_SoundEmitterRow},

		// "Client Leaf System has to be initialized first, since DetailObjectSystem
		// uses it" (stock): it must precede DetailObjectSystem.
		{"CClientLeafSystem", ClientLeafSystemGameSystem()},
		{"CDetailObjectSystem", &m_DetailObjectRow},

		{"CViewportClientSystem", &m_ViewportRow},
		{"CClientEffectPrecacheSystem", &m_EffectPrecacheRow},
		{"CClientShadowMgr", ClientShadowMgrGameSystem()},

		{"C_SoundscapeSystem", &m_SoundscapeRow},

		// The auto-registered systems, in stock's runtime order.
		{"CSourceSharpStandaloneClientSystem", ClientSystem_StandaloneClient()},
		{"CGlowOverlaySystem", &m_GlowOverlayRow},
		{"CDecalEmitterSystem", &m_Decals},
		{"CDirtySpatialPartitionEntityList", ClientSystem_DirtySpatialPartition()},
		{"CBulletWhizTimer", ClientSystem_BulletWhizTimer()},
		{"CPixelVisibilitySystem", &m_PixelVisibilityRow},
		{"CSourceSharpSpriteSystem", ClientSystem_Sprite()},
		{"CSourceSharpWorldTextSystem", ClientSystem_WorldText()},
		{"CSourceSharpRopeSystem", ClientSystem_Rope()},
		{"CSourceSharpManagedParticlesSystem", ClientSystem_ManagedParticles()},
		{"CSourceSharpStandaloneInterpSystem", ClientSystem_StandaloneInterp()},

		// The managed frame. The CLR is already up from CHLClient::Init.
		{"SourceSharp", &m_ManagedFrame},

		{"CPhysicsSystem", &m_Physics},
	};
	COMPILE_TIME_ASSERT(ARRAYSIZE(rows) == kRowCount);

	for (int i = 0; i < kRowCount; i++)
		m_Rows[i] = rows[i];
	m_nRows = kRowCount;

	for (int i = 0; i < m_nRows; i++)
	{
		MDLCACHE_CRITICAL_SECTION();

		if (!m_Rows[i].m_pSystem->Init())
		{
			Warning("[SourceSharp] client system '%s' refused to init; the client "
					"cannot come up without it\n",
					m_Rows[i].m_pszName);
			return false;
		}
	}

	return true;
}

void CClientSystems::WalkForward(Stage_t fn)
{
	for (int i = 0; i < m_nRows; i++)
	{
		MDLCACHE_CRITICAL_SECTION();

		CClientSystem* pSys = m_Rows[i].m_pSystem;
		(pSys->*fn)();
	}
}

void CClientSystems::WalkBackward(Stage_t fn)
{
	for (int i = m_nRows; --i >= 0;)
	{
		MDLCACHE_CRITICAL_SECTION();

		CClientSystem* pSys = m_Rows[i].m_pSystem;
		(pSys->*fn)();
	}
}

// The per-frame walk: the same rows, filtered by dynamic_cast to the ones with
// a per-frame half, as stock's second list was.
void CClientSystems::WalkForwardPerFrame(FrameStage_t fn)
{
	for (int i = 0; i < m_nRows; i++)
	{
		CClientSystemPerFrame* pSys = dynamic_cast<CClientSystemPerFrame*>(m_Rows[i].m_pSystem);

		if (pSys == NULL)
			continue;

		MDLCACHE_CRITICAL_SECTION();
		(pSys->*fn)();
	}
}

void CClientSystems::PostInit()
{
	WalkForward(&CClientSystem::PostInit);
}

void CClientSystems::Shutdown()
{
	WalkBackward(&CClientSystem::Shutdown);
}

void CClientSystems::LevelInitPreEntity()
{
	WalkForward(&CClientSystem::LevelInitPreEntity);
}

void CClientSystems::LevelInitPostEntity()
{
	WalkForward(&CClientSystem::LevelInitPostEntity);
}

void CClientSystems::LevelShutdownPreEntity()
{
	WalkBackward(&CClientSystem::LevelShutdownPreEntity);
}

void CClientSystems::LevelShutdownPostEntity()
{
	WalkBackward(&CClientSystem::LevelShutdownPostEntity);
}

void CClientSystems::PreRender()
{
	WalkForwardPerFrame(&CClientSystemPerFrame::PreRender);
}

// Update takes an argument, so it does not go through WalkForwardPerFrame.
void CClientSystems::Update(float frametime)
{
	for (int i = 0; i < m_nRows; i++)
	{
		CClientSystemPerFrame* pSys = dynamic_cast<CClientSystemPerFrame*>(m_Rows[i].m_pSystem);

		if (pSys == NULL)
			continue;

		MDLCACHE_CRITICAL_SECTION();
		pSys->Update(frametime);
	}
}
