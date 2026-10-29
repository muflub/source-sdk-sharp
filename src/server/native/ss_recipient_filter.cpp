//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through

#include "ss_recipient_filter.h"

#include "edict.h"
#include "tier0/dbg.h"

#include <algorithm>

int ss::CRecipientFilter::GetRecipientIndex(int slot) const
{
	if (slot < 0 || slot >= GetRecipientCount())
		return -1;

	return m_Recipients[slot];
}

void ss::CRecipientFilter::AddRecipientByPlayerIndex(int playerindex)
{
	if (playerindex < 1 || playerindex > m_nMaxClients)
		return;

	// Already listed
	if (std::find(m_Recipients.begin(), m_Recipients.end(), playerindex) != m_Recipients.end())
		return;

	m_Recipients.push_back(playerindex);
}
