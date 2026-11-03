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

extern IMaterialSystem* materials;
extern IMaterialSystemHardwareConfig* g_pMaterialSystemHardwareConfig;
extern IMDLCache* mdlcache;
extern IBaseClientDLL* clientdll;
extern IUploadGameStats* gamestatsuploader;
extern IClientReplayContext* g_pClientReplayContext;

// HPE_BEGIN
// [dwenger] Necessary for stats display

// HPE_END

// Precaches a material

// Precache-related methods for particle systems
