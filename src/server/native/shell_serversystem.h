// The base for the server's native systems (CGameStringPool, CPhysicsHook). Nothing
// registers on construction and there is no list: managed ServerSystems.cs owns the
// order, and each stage is called through one IShimLevel step.

#pragma once

#include <cstddef>

// Every stage is a no-op, so a system overrides the ones it has.
class CServerSystem
{
public:
	CServerSystem( char const *pszName = NULL ) : m_pszName( pszName ) {}
	virtual ~CServerSystem() {}

	virtual char const *Name() { return m_pszName ? m_pszName : "unnamed"; }

	// Return false to abort the server's start-up.
	virtual bool Init() { return true; }

	virtual void LevelInitPreEntity() {}
	virtual void LevelInitPostEntity() {}
	virtual void LevelShutdownPreEntity() {}
	virtual void LevelShutdownPostEntity() {}

private:
	char const *m_pszName;
};
