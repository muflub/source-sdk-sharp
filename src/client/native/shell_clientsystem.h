//========= SourceSharp =======================================================
//
// THE CLIENT'S GAME-SYSTEM BASE -- plan_cworld_trace.md Phase T4.
//
// The mirror of sourcesharp/game/server/shell_serversystem.h. Phase 7a took
// stock's CAutoGameSystem off the SERVER; this takes it off the CLIENT, and the
// reason is the same one, restated for the half that still had it:
//
//   A LEFTOVER REGISTRANT IS SILENT. CAutoGameSystem's CONSTRUCTOR appends the
//   object to an anonymous file-scope list in igamesystem.cpp which
//   IGameSystem::Add builds and IGameSystem::InitAllSystems walks. A system
//   whose InitAllSystems is never called does not warn, does not fail to link
//   and does not run -- it simply never initialises, and whatever it was for
//   stops working somewhere else, at a distance, later.
//
// On the client that failure has a shape the server's does not: a client whose
// systems stopped initialising DOES NOT CRASH. It renders wrong, or renders
// nothing, and the log is clean. That is why the live gate for this phase is a
// painted HUD and a drawn world rather than an assertion count.
//
//=============================================================================
// WHAT THIS CLASS IS, AND WHAT IT DELIBERATELY IS NOT
//
// CBaseGameSystem's SHAPE with CAutoGameSystem's BEHAVIOUR removed:
//
//   * NO CONSTRUCTOR SIDE EFFECT. Constructing one of these appends to nothing,
//     registers with nothing and is visible to nothing. The ONLY way a system
//     runs on the client is by being named in kClientSystems[]
//     (shell_clientsystems.cpp). That is a property of this class rather than
//     of anybody's discipline.
//   * NO STATIC ANYTHING. igamesystem.cpp carried s_GameSystems,
//     s_GameSystemsPerFrame and s_pMapName. This header has no state at file
//     scope at all, which is this project's standing rule for SourceSharp C++.
//   * NO Add / Remove / RemoveAll / *AllSystems. There is no list to add to.
//
// It is NOT a rename of CBaseGameSystem past this phase's symbol-count gate.
// The gate counts CAutoGameSystem and CBaseGameSystem symbols in client.so, and
// the stronger property underneath it is that igamesystem.cpp LEAVES THE
// CLIENT'S LINK LINE (client_csharp.vpc): IGameSystem::~IGameSystem,
// IGameSystemPerFrame::~IGameSystemPerFrame, CAutoGameSystem::CAutoGameSystem
// and CAutoGameSystemPerFrame::CAutoGameSystemPerFrame are defined there and
// nowhere else, so a client class that goes back to a stock base is an
// UNDEFINED REFERENCE rather than a silent non-registration. The count confirms
// the absence; the missing translation unit is what enforces it.
//
//=============================================================================
// THE STAGE LIST IS THE WALKER LIST, EXACTLY
//
// Every virtual below is dispatched by a named function in
// shell_clientsystems.cpp, and every walk in that file dispatches a virtual
// from below. One-to-one, on purpose:
//
//   Name                        the walker, cross-checked by a test
//   Init / PostInit / Shutdown  SourceSharp_ClientSystems{Init,PostInit,Shutdown}
//   LevelInitPreEntity          SourceSharp_ClientSystemsLevelInitPreEntity
//   LevelInitPostEntity         SourceSharp_ClientSystemsLevelInitPostEntity
//   LevelShutdownPreEntity      SourceSharp_ClientSystemsLevelShutdownPreEntity
//   LevelShutdownPostEntity     SourceSharp_ClientSystemsLevelShutdownPostEntity
//   PreRender                   SourceSharp_ClientSystemsPreRender
//   Update                      SourceSharp_ClientSystemsUpdate
//
//=============================================================================
// FOUR OF STOCK'S STAGES ARE ABSENT, and each one is a MEASUREMENT rather than
// a preference. Phase 7a's ledger records that three server stages were wired
// with no production caller and that the third COST something. So each of these
// was checked from both ends -- is there a driver, and is there a body:
//
//   OnSave, OnRestore  -- the save cluster, refused in full by this build
//                         (Phase 8's fifteen refusals; the engine's own answer
//                         is "Can't save multiplayer games"). Their only client
//                         drivers were two calls inside
//                         CEntitySaveRestoreBlockHandler's #ifdef CLIENT_DLL
//                         arm in game/shared/saverestore.cpp, and the SERVER
//                         arm of those same two sites was already emptied by
//                         Phase 7a. This phase empties the client's.
//
//   LevelShutdownPreClearSteamAPIContext
//                      -- NO CLIENT DRIVER AT ALL. Stock declares the stage and
//                         declares IGameSystem::LevelShutdownPreClear...
//                         AllSystems, and nothing on the client side ever calls
//                         it; the one client class that overrides the stage is
//                         CBaseGameStats_Driver, whose override has therefore
//                         never run in this build. Measured, not assumed:
//                         `grep -rn LevelShutdownPreClearSteamAPIContextAllSystems
//                         src/game/client` is empty.
//
//   SafeRemoveIfDesired
//                      -- stock's idiom for a system that unregisters itself
//                         mid-frame. Nothing here registers itself; the table
//                         does. Its only driver was inside
//                         IGameSystem::UpdateAllSystems, which this phase
//                         replaces.
//
// A system that still declares one of the four keeps a method nothing can
// reach, and after this header it READS that way instead of looking like a live
// callback.
//
//=============================================================================
// AND A FIFTH, PostRender, WENT LAST
//
// It had a driver (the render plan's ClientSystemsPostRender op) and NO
// non-empty body in the whole client once CToolFrameworkClient's
// `if ( m_pTools ) m_pTools->PostRenderAllTools()` was deleted with the tool
// framework (SFM is dead, user ruling 2026-10-19). The stage, its walk and the
// op left in the serialised ABI pass (2026-10-20).
//
//=============================================================================

#ifndef SHELL_CLIENTSYSTEM_H
#define SHELL_CLIENTSYSTEM_H
#ifdef _WIN32
#pragma once
#endif

#ifndef CLIENT_DLL
// The server's base is shell_serversystem.h and its stages are different ones
// -- FrameUpdatePreEntityThink / FrameUpdatePostEntityThink / PreClientUpdate
// where this has PreRender / Update / PostRender. Including THIS header from
// the server's half of a SHARED file is the mistake this whole phase is
// arranged around, in the direction the mirror image of Phase 7a runs, and it
// is refused here rather than discovered as a server that quietly stopped
// initialising.
#error "shell_clientsystem.h is the CLIENT's game-system base. The server's is shell_serversystem.h -- see plan_cworld_trace.md T4."
#endif

//-----------------------------------------------------------------------------
// The base every client game system derives from.
//
// Every stage is a no-op, so a system overrides the two or three it cares about
// and the table can dispatch all of them at every row without asking which
// exist. That is the same trade CBaseGameSystem made and it is the reason the
// rows carry ONE accessor rather than a function pointer per stage: forty
// hand-written stage thunks would be forty chances to omit one, and an omitted
// stage is exactly the silent failure above. A vtable cannot omit one.
//-----------------------------------------------------------------------------
class CClientSystem
{
public:
	// The name is a field rather than a registration argument. It exists so a
	// system can answer for itself in a warning; kClientSystems[] carries its
	// own label, and ClientSystemNamesMatchTheirSystems checks the two agree.
	CClientSystem( char const *pszName = NULL ) : m_pszName( pszName ) {}
	virtual ~CClientSystem() {}

	virtual char const *Name() { return m_pszName ? m_pszName : "unnamed"; }

	// Return false to abort the client's start-up. SourceSharp_ClientSystemsInit
	// stops on the first refusal, for stock's reason: a system that could not
	// init is a subsystem something later dereferences without knowing it failed.
	virtual bool Init() { return true; }
	virtual void PostInit() {}
	virtual void Shutdown() {}

	virtual void LevelInitPreEntity() {}
	virtual void LevelInitPostEntity() {}

	virtual void LevelShutdownPreEntity() {}
	virtual void LevelShutdownPostEntity() {}

	// Answered by the per-frame subclass below. The three per-frame walks in
	// shell_clientsystems.cpp select on this, so a system that is not per-frame
	// is not entered into an MDLCACHE critical section three times a frame for
	// three no-ops.
	virtual bool IsPerFrame() { return false; }

private:
	char const *m_pszName;
};

//-----------------------------------------------------------------------------
// The same, for a system with a per-frame half.
//
// Split from CClientSystem for the reason stock split CBaseGameSystemPerFrame
// from CBaseGameSystem: the three methods below exist only on the objects the
// frame walks, so a system that grows a PreRender without changing its base
// gets a compile error rather than a method nothing calls.
//-----------------------------------------------------------------------------
class CClientSystemPerFrame : public CClientSystem
{
public:
	CClientSystemPerFrame( char const *pszName = NULL ) : CClientSystem( pszName ) {}

	virtual bool IsPerFrame() { return true; }

	virtual void PreRender() {}
	virtual void Update( float frametime ) { NOTE_UNUSED( frametime ); }
};

//-----------------------------------------------------------------------------
// THE MAP NAME, AND IT IS NOT A STATIC.
//
// Stock kept it in igamesystem.cpp's s_pMapName -- a file-scope char* assigned
// by LevelInitPreEntityAllSystems and read back through IGameSystem::MapName().
// TWO client systems read it and both readings are live, measured on a
// dm_lockdown load at 2761d5d6:
//
//   C_SoundscapeSystem::Init          -> scripts/soundscapes_<map>.txt
//   CSourceSharpSystem::LevelInitPostEntity -> g_ManagedApi.LevelInit( "dm_lockdown" )
//
// So the reading had to survive, and a file-scope static could not: this
// project's standing rule forbids one and tools/statics-audit enforces it. The
// engine already knows the answer, so this asks it and NORMALISES -- the engine
// answers "maps/dm_lockdown.bsp" where stock's static held "dm_lockdown", and
// the caller supplies the buffer so nothing here holds state.
//
// Before any level it answers the empty string, which is what MapName() answered
// then too (s_pMapName was NULL until the first LevelInitPreEntity) -- so
// C_SoundscapeSystem::Init's first call, from CHLClient::Init, still skips the
// per-map file exactly as it did.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE WALKERS, which are the client's whole replacement for
// IGameSystem::*AllSystems.
//
// Ten stages, ten walkers, and shell_clientsystems.cpp holds the only
// definitions. Declared here rather than beside the table because unlike the
// ACCESSORS -- which have exactly one reader and stay in the table's own file --
// these have callers in three other translation units: cdll_client_int.cpp
// (eight) and viewrender.cpp (two).
//-----------------------------------------------------------------------------
bool SourceSharp_ClientSystemsInit( void );
void SourceSharp_ClientSystemsPostInit( void );
void SourceSharp_ClientSystemsShutdown( void );

void SourceSharp_ClientSystemsLevelInitPreEntity( void );
void SourceSharp_ClientSystemsLevelInitPostEntity( void );
void SourceSharp_ClientSystemsLevelShutdownPreEntity( void );
void SourceSharp_ClientSystemsLevelShutdownPostEntity( void );

void SourceSharp_ClientSystemsPreRender( void );
void SourceSharp_ClientSystemsUpdate( float frametime );

const char *SourceSharp_ClientMapName( char *pszOut, int nOutBytes );

#endif // SHELL_CLIENTSYSTEM_H
