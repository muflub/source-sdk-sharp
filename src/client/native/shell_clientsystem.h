// The client's game-system base. Constructing one registers nothing: a system
// runs only if it is named in kClientSystems[] (shell_clientsystems.cpp), and
// each stage below has one walker there.

#pragma once

// Every stage is a no-op, so a system overrides the ones it needs and the table
// dispatches all of them at every row.
class CClientSystem
{
public:
	CClientSystem( char const *pszName = NULL )
		: m_pszName( pszName )
	{
	}

	virtual ~CClientSystem() {}

	virtual char const *Name() { return m_pszName ? m_pszName : "unnamed"; }

	// Return false to abort the client's start-up; the walk stops on the first refusal.
	virtual bool Init() { return true; }

	virtual void PostInit() {}

	virtual void Shutdown() {}

	virtual void LevelInitPreEntity() {}

	virtual void LevelInitPostEntity() {}

	virtual void LevelShutdownPreEntity() {}

	virtual void LevelShutdownPostEntity() {}

	// The per-frame walks select on this, so a system that is not per-frame is not
	// entered into an MDLCACHE critical section for no-ops.
	virtual bool IsPerFrame() { return false; }

private:
	char const *m_pszName;
};

// A system with a per-frame half; a PreRender on a plain system would be a
// method nothing calls, so it is a compile error instead.
class CClientSystemPerFrame : public CClientSystem
{
public:
	CClientSystemPerFrame( char const *pszName = NULL )
		: CClientSystem( pszName )
	{
	}

	virtual bool IsPerFrame() { return true; }

	virtual void PreRender() {}

	virtual void Update( float frametime ) { NOTE_UNUSED( frametime ); }
};

// The walkers over kClientSystems[], defined in shell_clientsystems.cpp.
bool SourceSharp_ClientSystemsInit();
void SourceSharp_ClientSystemsPostInit();
void SourceSharp_ClientSystemsShutdown();

void SourceSharp_ClientSystemsLevelInitPreEntity();
void SourceSharp_ClientSystemsLevelInitPostEntity();
void SourceSharp_ClientSystemsLevelShutdownPreEntity();
void SourceSharp_ClientSystemsLevelShutdownPostEntity();

void SourceSharp_ClientSystemsPreRender();
void SourceSharp_ClientSystemsUpdate( float frametime );

// The current map's bare name ("dm_lockdown", not "maps/dm_lockdown.bsp") into
// the caller's buffer; the empty string before any level.
const char *SourceSharp_ClientMapName( char *pszOut, int nOutBytes );

// A row over an object stock's headers make an IGameSystem: each stage is handed
// to the object.
class IGameSystem;

template <class TGameSystem>
class CGameSystemRow : public CClientSystem
{
public:
	explicit CGameSystemRow( TGameSystem *pSystem )
		: m_pSystem( pSystem )
	{
	}

	virtual char const *Name() { return m_pSystem->Name(); }

	virtual bool Init() { return m_pSystem->Init(); }

	virtual void PostInit() { m_pSystem->PostInit(); }

	virtual void Shutdown() { m_pSystem->Shutdown(); }

	virtual void LevelInitPreEntity() { m_pSystem->LevelInitPreEntity(); }

	virtual void LevelInitPostEntity() { m_pSystem->LevelInitPostEntity(); }

	virtual void LevelShutdownPreEntity() { m_pSystem->LevelShutdownPreEntity(); }

	virtual void LevelShutdownPostEntity() { m_pSystem->LevelShutdownPostEntity(); }

private:
	TGameSystem *m_pSystem;
};
