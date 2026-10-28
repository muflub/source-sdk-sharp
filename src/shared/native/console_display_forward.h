//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// Forwards the engine's console prints to the managed side's line buffer. nWarning
// is read back from the spew colour: 1 for warning, assert and error, else 0.
//
//=============================================================================//

#pragma once

class ICvar;

// Installs the forwarder on pCVar; false when pCVar is NULL or a forwarder is already live.
bool ConsoleDisplay_Install( ICvar *pCVar );

// Removes it from the ICvar it was installed on; a no-op when not installed.
void ConsoleDisplay_Remove();

// Takes the forwarder off the engine's list for a managed reload, when every area slot
// is on the trap table and a forwarded print would recurse. Back on at End() or scope end.
class CConsoleDisplayReloadGap
{
public:
	CConsoleDisplayReloadGap();
	~CConsoleDisplayReloadGap() { End(); }
	void End();   // back on the list now; the scope's end then does nothing
	CConsoleDisplayReloadGap( const CConsoleDisplayReloadGap & ) = delete;
	CConsoleDisplayReloadGap &operator=( const CConsoleDisplayReloadGap & ) = delete;

private:
	ICvar *m_pCVar;
};
