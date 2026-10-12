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
// THE STAGE LIST IS THE WALKER LIST, EXACTLY
//
// Every virtual below is dispatched by a named function in
// shell_serversystems.cpp, and every walk in that file dispatches a virtual
// from below. One-to-one, on purpose:
//
//   Name                                  the table's row label, cross-checked
//   Init / PostInit / Shutdown            SourceSharp_ServerSystems{Init,PostInit,Shutdown}
//   LevelInitPreEntity                    SourceSharp_LevelInitPreEntityAllSystems
//   LevelInitPostEntity                   SourceSharp_ServerSystemsLevelInitPostEntity
//   LevelShutdownPreClearSteamAPIContext  ...LevelShutdownPreClearSteamAPIContext
//   LevelShutdownPreEntity                ...LevelShutdownPreEntity
//   LevelShutdownPostEntity               ...LevelShutdownPostEntity
//   FrameUpdatePreEntityThink             SourceSharp_ServerSystemsFramePreThink
//   FrameUpdatePostEntityThink            SourceSharp_ServerSystemsFramePostThink
//   PreClientUpdate                       SourceSharp_ServerSystemsPreClientUpdate
//
// STOCK'S OnSave, OnRestore AND SafeRemoveIfDesired ARE ABSENT, and that is a
// decision rather than an omission. The first two are the save cluster's, which
// this build refuses in full (Phase 8: fifteen refusals, and the engine's own
// answer is "Can't save multiplayer games"). SafeRemoveIfDesired is stock's
// idiom for a system that unregisters itself mid-frame, and nothing here
// registers itself -- the table does. A system that still declares one of the
// three keeps a method nothing can reach, and after this header it READS that
// way instead of looking like a live callback.
//
//=============================================================================

#ifndef SHELL_SERVERSYSTEM_H
#define SHELL_SERVERSYSTEM_H

#include <cstddef>
#ifdef _WIN32
#pragma once
#endif

#ifdef CLIENT_DLL
// The client still has IGameSystem and still calls InitAllSystems -- but NOT
// for the reason this comment used to give. It claimed client_csharp.vpc
// $Includes client_base.vpc. It does not, and a grep says so: that vpc has zero
// $Include of client_base and lists its own 280 $File entries, exactly as
// server_csharp.vpc does. The client's systems keep their stock base because
// nothing has ported them yet, which is a different fact with a different
// owner (Phase 6/7), and the false premise was what made the old arrangement
// look reasonable.
//
// Including this header from the client's half of a SHARED file is the one
// mistake this whole phase is arranged around, and it is refused here rather
// than discovered as a
// client that quietly stopped initialising.
#error "shell_serversystem.h is the SERVER's game-system base. The client keeps IGameSystem -- see plan_gamedll.md T1."
#endif

//-----------------------------------------------------------------------------
// The base every server game system derives from.
//
// Every stage is a no-op, so a system overrides the two or three it cares about
// and the table can dispatch all of them at every row without asking which
// exist. That is the same trade CBaseGameSystem made and it is the reason the
// rows carry ONE accessor rather than a function pointer per stage: fifty
// hand-written stage thunks would be fifty chances to omit one, and an omitted
// stage is exactly the silent failure above. A vtable cannot omit one.
//-----------------------------------------------------------------------------
class CServerSystem
{
public:
	// The name is a field rather than a registration argument. It exists so a
	// system can answer for itself in a warning; kServerSystems[] carries its
	// own label, and ServerSystemNamesMatchTheirSystems checks the two agree.
	CServerSystem( char const *pszName = NULL ) : m_pszName( pszName ) {}
	virtual ~CServerSystem() {}

	virtual char const *Name() { return m_pszName ? m_pszName : "unnamed"; }

	// Return false to abort the server's start-up. SourceSharp_ServerSystemsInit
	// stops on the first refusal, for stock's reason: a system that could not
	// init is a subsystem something later dereferences without knowing it failed.
	virtual bool Init() { return true; }
	virtual void PostInit() {}
	virtual void Shutdown() {}

	virtual void LevelInitPreEntity() {}
	virtual void LevelInitPostEntity() {}

	virtual void LevelShutdownPreClearSteamAPIContext() {}
	virtual void LevelShutdownPreEntity() {}
	virtual void LevelShutdownPostEntity() {}

	// Answered by the per-frame subclass below. The three per-frame walks in
	// shell_serversystems.cpp select on this, so a system that is not per-frame
	// is not entered into an MDLCACHE critical section sixty-six times a second
	// for three no-ops.
	virtual bool IsPerFrame() { return false; }

private:
	char const *m_pszName;
};

//-----------------------------------------------------------------------------
// The same, for a system with a per-frame half.
//
// Split from CServerSystem for the reason stock split CBaseGameSystemPerFrame
// from CBaseGameSystem: the three methods below exist only on the objects the
// frame walks, so a system that grows a FrameUpdatePostEntityThink without
// changing its base gets a compile error rather than a method nothing calls.
//-----------------------------------------------------------------------------
class CServerSystemPerFrame : public CServerSystem
{
public:
	CServerSystemPerFrame( char const *pszName = NULL ) : CServerSystem( pszName ) {}

	virtual bool IsPerFrame() { return true; }

	virtual void FrameUpdatePreEntityThink() {}
	virtual void FrameUpdatePostEntityThink() {}
	virtual void PreClientUpdate() {}
};

#endif // SHELL_SERVERSYSTEM_H
