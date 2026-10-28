//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// Each game rules class must register using this in it's .cpp file.
#if !defined(_STATIC_LINKED)
#define REGISTER_GAMERULES_CLASS( className ) \
	void __CreateGameRules_##className() { new className; } \
	static CGameRulesRegister __g_GameRulesRegister_##className( #className, __CreateGameRules_##className );
#else
#define REGISTER_GAMERULES_CLASS( className ) \
	void MAKE_NAME_UNIQUE(__CreateGameRules_)##className() { new className; } \
	static CGameRulesRegister __g_GameRulesRegister_##className( #className, MAKE_NAME_UNIQUE(__CreateGameRules_)##className );
#endif

class CGameRulesRegister
{
public:
	typedef void (*CreateGameRulesFn)();

	CGameRulesRegister( const char *pClassName, CreateGameRulesFn fn );

private:
	const char *m_pClassName;
	CreateGameRulesFn m_pFn;
	CGameRulesRegister *m_pNext;	// Links it into the global list.
	
};

	// The client forwards this call so the game rules manager can create the appropriate
	// game rules class.
	void InstallStringTableCallback_GameRules();
