//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The client's game rules: the object the server's GameRulesCreation string makes, and the
// hulls managed publishes, which outlive it.

#pragma once

#include "mathlib/vector.h"
#include "tier1/utlvector.h"
#include "shareddefs.h"
#include "igamerules.h"
#include "sourcesharp.h"

class INetworkStringTable;
class INetworkStringTableContainer;

class CGameRulesClient final
{
public:
	CGameRulesClient();

	CGameRulesClient(const CGameRulesClient&) = delete;
	CGameRulesClient& operator=(const CGameRulesClient&) = delete;

	// NULL between levels, so test it before calling.
	IGameRules* Rules() const { return m_pRules; }

	const RulesViewVectors_t& ViewVectors() const { return m_ViewVectors; }

	void PublishViewVectors(const SourceSharpMovementParams* pParams);

	void InstallStringTableCallback(INetworkStringTableContainer* pStringTables);

private:
	static void OnStringChanged(void* object, INetworkStringTable* stringTable, int stringNumber, const char* newString,
								void const* newData);

	IGameRules* m_pRules = nullptr;
	INetworkStringTable* m_pTable = nullptr;
	RulesViewVectors_t m_ViewVectors;
};
