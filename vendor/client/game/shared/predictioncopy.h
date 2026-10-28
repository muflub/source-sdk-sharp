//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <memory.h>
#include "datamap.h"
#include "ehandle.h"
#include "tier1/utlstring.h"

class C_BaseEntity;
typedef CHandle<C_BaseEntity> EHANDLE;

#if defined( _DEBUG )
// #define COPY_CHECK_STRESSTEST
class IGameSystem;
#endif

enum
{
	PC_EVERYTHING = 0,
	PC_NON_NETWORKED_ONLY,
	PC_NETWORKED_ONLY,
};

class CValueChangeTracker
{
public:
	CValueChangeTracker();

	void Reset();

	bool IsActive() const;

private:

	enum
	{
		eChangeTrackerBufSize = 128,
	};

	// Returns field size
	void				GetValue( char *buf, size_t bufsize );

	bool				m_bActive : 1;
	bool				m_bTracking : 1;
	EHANDLE				m_hEntityToTrack;
	CUtlVector< typedescription_t * > m_FieldStack;
	CUtlString			m_strFieldName;
	CUtlString			m_strContext;
	// First 128 bytes of data is all we will consider
	char				m_OrigValueBuf[ eChangeTrackerBufSize ];
	CUtlVector< CUtlString >	 m_History;
};
