//========= SourceSharp =======================================================
//
// THE SERVER'S GAME-SYSTEM BASE -- plan_gamedll.md Phase 7a.
//
// Phase 7 wrote kServerSystems[] (shell_serversystems.cpp): one row per system,
// in the order it runs, walked forwards for init and backwards for shutdown.
// Phase 7a takes away the thing that made a system a system BEFORE that table
// existed -- stock's CAutoGameSystem, whose CONSTRUCTOR appends the object to
// an anonymous file-scope list in igamesystem.cpp that IGameSystem::Add builds
// and IGameSystem::InitAllSystems walks.
//
// WHY THAT LIST HAD TO GO, in one sentence: a leftover registrant is SILENT. A
// CAutoGameSystem whose InitAllSystems is never called does not warn, does not
// fail to link and does not run -- it simply never initialises, and whatever it
// was for stops working somewhere else, at a distance, later.
//
//=============================================================================
// WHAT THIS CLASS IS, AND WHAT IT DELIBERATELY IS NOT
//
// It is CBaseGameSystem's SHAPE with CAutoGameSystem's BEHAVIOUR removed:
//
//   * NO CONSTRUCTOR SIDE EFFECT. Constructing one of these appends to nothing,
//     registers with nothing and is visible to nothing. The ONLY way a system
//     runs on the server is by being named in kServerSystems[]. That is the
//     property this whole phase exists to buy, and it is a property of this
//     class rather than of anybody's discipline.
//   * NO STATIC ANYTHING. igamesystem.cpp carried s_GameSystems,
//     s_GameSystemsPerFrame, s_pMapName, s_pRunCommandPlayer and
//     s_pRunCommandUserCmd. This header has no state at file scope at all,
//     which is this project's standing rule for SourceSharp C++.
//   * NO Add / Remove / RemoveAll / *AllSystems. There is no list to add to.
//
// It is NOT a rename of CBaseGameSystem past Phase 7a's symbol-count gate. The
// gate counts CAutoGameSystem and CBaseGameSystem symbols in server.so, and the
// stronger property underneath it is that igamesystem.cpp LEAVES THE SERVER'S
// LINK LINE (server_csharp.vpc): IGameSystem::~IGameSystem and
// CAutoGameSystem::CAutoGameSystem are defined there and nowhere else, so a
// server class that goes back to a stock base is an UNDEFINED REFERENCE rather
// than a silent non-registration. The count confirms the absence; the missing
// translation unit is what enforces it.
//
//=============================================================================
// THE STAGES ARE THE ONES SOMETHING CALLS (plan_server_native_v2 T4e)
//
// kServerSystems[] and its walks are gone: the order is managed's
// (ServerSystems.cs) and each native system's stage is one IShimLevel step in
// shell_level.cpp. Two systems are left -- CGameStringPool and CPhysicsHook --
// and the stages below are the ones those steps call:
//
//   Init                     IShimLevel::PhysicsInit
//   LevelInitPreEntity       IShimLevel::PhysicsLevelInitPreEntity
//   LevelInitPostEntity      IShimLevel::PhysicsLevelInitPostEntity
//   LevelShutdownPreEntity   IShimLevel::PhysicsLevelShutdownPreEntity
//   LevelShutdownPostEntity  IShimLevel::PhysicsLevelShutdownPostEntity,
//                            IShimLevel::StringPoolLevelShutdownPostEntity
//
// PostInit, Shutdown, LevelShutdownPreClearSteamAPIContext and the per-frame
// subclass went with the walks: neither system overrode them, so a stage
// nothing could call is not kept to look like a live callback.
//
//=============================================================================

#pragma once

#include <cstddef>


//-----------------------------------------------------------------------------
// The base the two native server systems derive from. Every stage is a no-op,
// so a system overrides the ones it has.
//-----------------------------------------------------------------------------
class CServerSystem
{
public:
	CServerSystem( char const *pszName = NULL ) : m_pszName( pszName ) {}
	virtual ~CServerSystem() {}

	virtual char const *Name() { return m_pszName ? m_pszName : "unnamed"; }

	// Return false to abort the server's start-up: managed's ServerSystems.Init
	// stops on the refusal, for stock's reason -- a system that could not init
	// is a subsystem something later dereferences without knowing it failed.
	virtual bool Init() { return true; }

	virtual void LevelInitPreEntity() {}
	virtual void LevelInitPostEntity() {}
	virtual void LevelShutdownPreEntity() {}
	virtual void LevelShutdownPostEntity() {}

private:
	char const *m_pszName;
};
