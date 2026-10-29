//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The server's recipient filters. The engine reads them only through
// IRecipientFilter; players are named by slot index (1..maxClients), never by pointer.

#pragma once

#include "irecipientfilter.h"

#include <vector>

namespace ss
{

// A list of player slots, plus the two flags the engine asks for.
class CRecipientFilter : public IRecipientFilter
{
public:
	// Slots 1..nMaxClients are players; the engine reserves them.
	explicit CRecipientFilter(int nMaxClients)
		: m_nMaxClients(nMaxClients)
	{
	}

	virtual ~CRecipientFilter() = default;

	virtual bool IsReliable() const { return m_bReliable; }

	virtual bool IsInitMessage() const { return m_bInitMessage; }

	virtual int GetRecipientCount() const { return (int)m_Recipients.size(); }

	virtual int GetRecipientIndex(int slot) const;

	void Reset();

	void MakeReliable() { m_bReliable = true; }

	void MakeInitMessage() { m_bInitMessage = true; }

	// One slot, 1..maxClients; anything else, or a slot already listed, is ignored.
	void AddRecipientByPlayerIndex(int playerindex);

	void RemoveAllRecipients() { m_Recipients.clear(); }

	// Prediction rules: carried for the callers that set and read them.
	void UsePredictionRules() { m_bUsingPredictionRules = true; }

	bool IsUsingPredictionRules() const { return m_bUsingPredictionRules; }

	bool IgnorePredictionCull() const { return m_bIgnorePredictionCull; }

	void SetIgnorePredictionCull(bool ignore) { m_bIgnorePredictionCull = ignore; }

private:
	int m_nMaxClients;
	bool m_bReliable = false;
	bool m_bInitMessage = false;
	std::vector<int> m_Recipients;
	bool m_bUsingPredictionRules = false;
	bool m_bIgnorePredictionCull = false;
};

} // namespace ss
