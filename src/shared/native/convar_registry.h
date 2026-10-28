//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CCVarRegistry: this DLL's half of tier1's console glue, as one object owned by the DLL's
// root (user 2026-10-27: "remove g_pCVar and have a CVar registry class"). It holds the
// engine's ICvar, registers the DLL's ConVar and ConCommand carriers (ConVar_Register,
// ConVar_Unregister) under the DLL identifier it allocates, and is the ConCommandBase
// accessor, which is how a carrier -- an object the engine holds and calls with no owner
// -- reaches the console (convar_port.cpp).

#pragma once

#include "tier1/convar.h"

class ICvar;

class CCVarRegistry final : public IConCommandBaseAccessor
{
public:
	CCVarRegistry() = default;

	CCVarRegistry( const CCVarRegistry & ) = delete;
	CCVarRegistry &operator=( const CCVarRegistry & ) = delete;

	// The engine's console, from the factory DLLInit is handed; NULL to disconnect.
	void Connect( ICvar *pCvar ) { m_pCvar = pCvar; }

	ICvar *Cvar() const { return m_pCvar; }

	// Allocates this DLL's identifier, becomes the ConCommandBase accessor, and registers
	// every carrier built so far, with nFlags added. pPolicy, when given, registers each
	// one in its place (the game DLL's command-line apply). A second call does nothing.
	void Register( int nFlags, IConCommandBaseAccessor *pPolicy = nullptr );

	// Unregisters this DLL's carriers; before the console disconnects.
	void Unregister();

	CVarDLLIdentifier_t DllIdentifier() const { return m_nDllIdentifier; }

	bool RegisterConCommandBase( ConCommandBase *pVar ) override;

	// The registry a carrier reaches: ConCommandBase's accessor, NULL before Register.
	static CCVarRegistry *Current();

private:
	ICvar *m_pCvar = nullptr;
	IConCommandBaseAccessor *m_pPolicy = nullptr;
	bool m_bRegistered = false;
	int m_nRegisterFlags = 0;
	CVarDLLIdentifier_t m_nDllIdentifier = -1;
};
