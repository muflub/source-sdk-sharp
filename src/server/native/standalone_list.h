// Every live standalone entity, as a view: empty outside a level, whose tier owns the
// list (CServerLevelState::m_Shells). gEntList's traversals skip these entities.

#pragma once

#include <cstddef>

class CStandaloneEntity;

class CStandaloneList
{
public:
	CStandaloneList() = default;

	CStandaloneList( CStandaloneEntity *const *pBegin, size_t nCount )
		: m_pBegin( pBegin )
		, m_nCount( nCount )
	{
	}

	CStandaloneEntity *const *begin() const { return m_pBegin; }

	CStandaloneEntity *const *end() const { return m_pBegin + m_nCount; }

	size_t size() const { return m_nCount; }

private:
	CStandaloneEntity *const *m_pBegin = nullptr;
	size_t m_nCount = 0;
};

CStandaloneList StandaloneEntities();
