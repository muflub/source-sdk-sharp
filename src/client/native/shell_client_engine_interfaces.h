//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The engine interfaces the client acquires in Init, held by the client DLL object and handed
// to what calls them; valid from Init to Shutdown. As the server's CServerEngineInterfaces.

#pragma once

class CClientSystem;
class CConVarsClient;
class CRenderTargetsClient;
class CParticleMgrClient;
struct SourceSharpEffectLedger_t;
class CWorldClient;
class CGlobalVarsBase;
class CParticleSystemMgr;
class IClientLeafSystem;
class IClientMode;
class IDecalEmitterSystem;
class IEffects;
class ITempEnts;
class IInput;
class IEngineSound;
class IEngineTrace;
class IEngineVGui;
class IFileSystem;
class IGameEventManager2;
class IMDLCache;
class ICvar;
class IMaterialSystem;
class IMaterialSystemHardwareConfig;
class IMaterialSystemStub;
class INetworkStringTable;
class INetworkStringTableContainer;
class IPhysicsCollision;
class IPhysicsSurfaceProps;
class ISoundEmitterSystemBase;
class ISpatialPartition;
class IShadowMgr;
class IStaticPropMgrClient;
class IVDebugOverlay;
class IVEfx;
class IVEngineClient;
class IVModelInfoClient;
class IVModelRender;
class IVRenderView;
class IViewPort;
class IViewRender;
class IMatSystemSurface;

namespace vgui
{
class IInput;
class ILocalize;
class IPanel;
class ISchemeManager;
class ISurface;
class ISystem;
class IVGui;
}

class CClientEngineInterfaces
{
public:
	IVEngineClient* m_pEngine = nullptr;
	IVRenderView* m_pRender = nullptr;
	IVModelInfoClient* m_pModelInfo = nullptr;
	IVModelRender* m_pModelRender = nullptr;
	IVEfx* m_pEffects = nullptr;
	IEngineTrace* m_pTrace = nullptr;
	IEngineSound* m_pSound = nullptr;
	IEngineVGui* m_pVGui = nullptr;
	IVDebugOverlay* m_pDebugOverlay = nullptr;
	IFileSystem* m_pFileSystem = nullptr;
	INetworkStringTableContainer* m_pStringTables = nullptr;
	IGameEventManager2* m_pEvents = nullptr;
	IStaticPropMgrClient* m_pStaticProps = nullptr;
	IShadowMgr* m_pShadows = nullptr;
	ISpatialPartition* m_pPartition = nullptr;
	ISoundEmitterSystemBase* m_pSoundEmitter = nullptr;
	IPhysicsCollision* m_pPhysCollision = nullptr;
	IPhysicsSurfaceProps* m_pPhysProps = nullptr;
	IMDLCache* m_pMdlCache = nullptr;
	IMaterialSystem* m_pMaterials = nullptr;
	IMaterialSystemHardwareConfig* m_pHardwareConfig = nullptr;
	ICvar* m_pCvar = nullptr;
	CParticleSystemMgr* m_pParticleMgr = nullptr;
	vgui::IVGui* m_pVGuiCore = nullptr;
	vgui::IInput* m_pVGuiInput = nullptr;
	vgui::IPanel* m_pVGuiPanel = nullptr;
	vgui::ISurface* m_pVGuiSurface = nullptr;
	vgui::ISchemeManager* m_pVGuiSchemes = nullptr;
	vgui::ISystem* m_pVGuiSystem = nullptr;
	vgui::ILocalize* m_pVGuiLocalize = nullptr;
	IMatSystemSurface* m_pMatSystemSurface = nullptr;

	// The client's own interfaces, which stock reached through globals too.
	IClientMode* m_pClientMode = nullptr;
	IInput* m_pInput = nullptr;
	IClientLeafSystem* m_pLeafSystem = nullptr;
	IEffects* m_pGameEffects = nullptr;
	ITempEnts* m_pTempEnts = nullptr;
	IDecalEmitterSystem* m_pDecalSystem = nullptr;
	IMaterialSystemStub* m_pMaterialsStub = nullptr;
	CConVarsClient* m_pConVars = nullptr;
	IViewRender* m_pViewRender = nullptr;
	IViewPort* m_pViewPort = nullptr;
	CWorldClient* m_pWorld = nullptr;
	CRenderTargetsClient* m_pRenderTargets = nullptr;
	CClientSystem* m_pDirtyPartition = nullptr;
	CParticleMgrClient* m_pParticles = nullptr;
	SourceSharpEffectLedger_t* m_pEffectLedger = nullptr;

	// The string tables readers ask by index; NULL between maps.
	INetworkStringTable* m_pEffectDispatchTable = nullptr;
	INetworkStringTable* m_pParticleEffectNames = nullptr;

	bool m_bTextMode = false;

	// Engine-owned memory changing every frame; the DLL object's placeholder until Init.
	CGlobalVarsBase* m_pGlobals = nullptr;
};
