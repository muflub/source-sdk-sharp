//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "iclientnetworkable.h"

#include "cdll_int.h"
#include "eiface.h"

class IVModelRender;
class IVEngineClient;
class IVModelRender;
class IVEfx;
class IVRenderView;
class IVDebugOverlay;
class IMaterialSystem;
class IMaterialSystemStub;
class IMDLCache;
class IVModelInfoClient;
class IEngineVGui;
class ISpatialPartition;
class IBaseClientDLL;
class ISpatialPartition;
class IFileSystem;
class IStaticPropMgrClient;
class IShadowMgr;
class IEngineSound;
class IMatSystemSurface;
class IMaterialSystemHardwareConfig;
class ISharedGameRules;
class IEngineTrace;
class IGameEventManager2;
class IPhysicsGameTrace;
class CGlobalVarsBase;
class C_BaseAnimating;
class IColorCorrectionSystem;
class IInputSystem;
class CSteamAPIContext;
class IClientReplayContext;
class CSteamID;

// HPE_BEGIN
// [dwenger] Necessary for stats display

// HPE_END

extern IVModelRender *modelrender;
extern IVEngineClient *engine;
extern IVModelRender *modelrender;
extern IVEfx *effects;
extern IVRenderView *render;
extern IVDebugOverlay *debugoverlay;
extern IMaterialSystem *materials;
extern IMaterialSystemStub *materials_stub;
extern IMaterialSystemHardwareConfig *g_pMaterialSystemHardwareConfig;
extern IMDLCache *mdlcache;
extern IVModelInfoClient *modelinfo;
extern IEngineVGui *enginevgui;
extern ISpatialPartition *partition;
extern IBaseClientDLL *clientdll;
extern IFileSystem *filesystem;
extern IStaticPropMgrClient *staticpropmgr;
extern IShadowMgr *shadowmgr;
extern IEngineSound *enginesound;
extern IMatSystemSurface *g_pMatSystemSurface;
extern IEngineTrace *enginetrace;
extern IGameEventManager2 *gameeventmanager;
extern CGlobalVarsBase *gpGlobals;
extern IUploadGameStats *gamestatsuploader;
extern IClientReplayContext *g_pClientReplayContext;

// HPE_BEGIN
// [dwenger] Necessary for stats display

// HPE_END

// Set to true between LevelInit and LevelShutdown.
extern bool g_bLevelInitialized;
extern bool g_bTextMode;

// Precaches a material
void PrecacheMaterial( const char *pMaterialName );

// Precache-related methods for particle systems
void PrecacheParticleSystem( const char *pParticleSystemName );
int GetParticleSystemIndex( const char *pParticleSystemName );
const char *GetParticleSystemNameFromIndex( int nIndex );
