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

#include <atomic>
#include <memory>

#include "icvar.h"
#include "console_line_buffer.h"

#ifdef CLIENT_DLL
class IManagedConsoleOutputClient;
typedef IManagedConsoleOutputClient ManagedConsoleOutputArea_t;
#else
class IManagedConsoleOutput;
typedef IManagedConsoleOutput ManagedConsoleOutputArea_t;
#endif

// The engine's console prints, handed to this DLL's managed side: an IConsoleDisplayFunc
// installed on the side's ICvar appends each print to a line buffer, which the managed
// console-output area drains. One per DLL, a member of its managed side; the engine's
// list holds the forwarder by pointer, so a second install would double every line.
class CConsoleDisplay
{
public:
	// ppArea is where the side's holders keep this load's console-output area.
	explicit CConsoleDisplay( ManagedConsoleOutputArea_t *const &ppArea );

	// Off the engine's list before the forwarder goes.
	~CConsoleDisplay() { Remove(); }

	CConsoleDisplay( const CConsoleDisplay & ) = delete;
	CConsoleDisplay &operator=( const CConsoleDisplay & ) = delete;

	// False for a NULL ICvar, or a second ICvar while one is installed.
	bool Install( ICvar *pCVar );
	void Remove();

	ICvar *Installed() const { return m_pInstalledCvar; }

private:
	class CForwarder final : public IConsoleDisplayFunc
	{
	public:
		explicit CForwarder( CConsoleDisplay &owner )
			: m_Owner( owner )
		{
		}

		void ColorPrint( const Color &color, const char *pMessage ) override;
		void Print( const char *pMessage ) override;
		void DPrint( const char *pMessage ) override;

	private:
		CConsoleDisplay &m_Owner;
	};

	// The engine calls the forwarder on the printing thread.
	void Forward( const char *pText, int nWarning );

	// Hands the buffer to this load's area, once per install; false outside a load.
	bool Attach();

	ManagedConsoleOutputArea_t *const &m_ppArea;
	CForwarder m_Forwarder{ *this };
	ICvar *m_pInstalledCvar = nullptr;
	std::atomic<bool> m_bAttached{ false };

	// Allocated once and kept across reloads: managed holds its address. On the heap, so
	// the root that owns this does not carry 1 MiB.
	std::unique_ptr<CConsoleLineBuffer> m_pBuffer;
};

// Off the engine's list for the span of a managed reload, then back on the same ICvar.
class CConsoleDisplayReloadGap
{
public:
	explicit CConsoleDisplayReloadGap( CConsoleDisplay &display );

	~CConsoleDisplayReloadGap() { End(); }

	void End(); // back on the list now; the scope's end then does nothing
	CConsoleDisplayReloadGap( const CConsoleDisplayReloadGap & ) = delete;
	CConsoleDisplayReloadGap &operator=( const CConsoleDisplayReloadGap & ) = delete;

private:
	CConsoleDisplay &m_Display;
	ICvar *m_pCVar;
};
