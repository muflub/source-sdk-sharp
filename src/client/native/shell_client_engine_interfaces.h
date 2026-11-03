//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The engine interfaces the client acquires in Init, held by the client DLL object and handed
// to what calls them; valid from Init to Shutdown. As the server's CServerEngineInterfaces.

#pragma once

class CGlobalVarsBase;
class IEngineSound;
class IEngineTrace;
class IEngineVGui;
class IFileSystem;
class IGameEventManager2;
class IMDLCache;
class IMaterialSystem;
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

	// Engine-owned memory changing every frame; the DLL object's placeholder until Init.
	CGlobalVarsBase* m_pGlobals = nullptr;
};
