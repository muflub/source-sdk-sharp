//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "predictable_entity.h"

class CBaseEntity;

// Purpose: Interfaces derived from this are able to filter out the local player
//  when doing prediction on the client, this includes not sending network data to
//  the local player from the server if needed.
class IPredictionSystem
{
public:
	IPredictionSystem()
	{
		m_bSuppressEvent = false;
		m_pSuppressHost = NULL;

		m_nStatusPushed = 0;
	};

	virtual ~IPredictionSystem() {};

	void SetSuppressEvent(bool state) { m_bSuppressEvent = state; }

	bool CanPredict() const
	{
		if (DisableFiltering())
		{
			return false;
		}

		return !m_bSuppressEvent;
	}

private:
	bool DisableFiltering() const { return (m_nStatusPushed > 0) ? true : false; }

	bool m_bSuppressEvent;
	CBaseEntity* m_pSuppressHost;

	int m_nStatusPushed;
};
