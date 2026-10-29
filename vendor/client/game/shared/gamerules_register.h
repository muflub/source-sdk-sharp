//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// Each game rules class must register using this in it's .cpp file.

class CGameRulesRegister
{
public:
	typedef void (*CreateGameRulesFn)();

	CGameRulesRegister(const char* pClassName, CreateGameRulesFn fn);

private:
	const char* m_pClassName;
	CreateGameRulesFn m_pFn;
	CGameRulesRegister* m_pNext; // Links it into the global list.
};

// The client forwards this call so the game rules manager can create the appropriate
// game rules class.
void InstallStringTableCallback_GameRules();
