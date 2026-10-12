//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The engine's console prints, forwarded in process (docs/todo.md's console
// capture item). shell_internalfactory.cpp installs this once per side at the
// end of ManagedRoot_AcquireAreas -- the point where the side holds both its
// ICvar and its managed areas -- and removes it in ManagedRoot_Release, the
// teardown the same file owns.
//
// The three IConsoleDisplayFunc methods make no decision: each appends the
// engine's already-formatted text, with its channel, to the forwarder's
// console line buffer (console_line_buffer.h, console-batch 2026-10-11), which
// the load's managed side drains without a crossing; the buffer is handed to
// the load's console-output area once per install (ConsoleBufferAttach).
// Outside a load the area accessor answers NULL and the line is dropped (the
// hot-reload lesson: the accessor is read per call, so a side that released
// its managed module stops forwarding at once, and the engine's own console
// keeps working untouched -- the list is not a replacement).
//
// The Color& of ColorPrint is not forwarded; consumers carry a warning flag.
// nWarning is read back from the spew colour (every spew reaches ColorPrint:
// the engine's spew handler colours each one): 1 for SPEW_WARNING/
// ASSERT/ERROR's colours, 0 for any other (a plain Msg, a ConColorMsg), and
// always 0 for Print and DPrint. A ConColorMsg in a spew colour reads as a
// warning: the flag is the spew type the engine chose, not a colour.
//
//=============================================================================//

#ifndef SOURCESHARP_CONSOLE_DISPLAY_FORWARD_H
#define SOURCESHARP_CONSOLE_DISPLAY_FORWARD_H

class ICvar;

/// Installs the forwarder on pCVar; false when pCVar is NULL or a forwarder
/// is already live. Install is the native decision; forwarding is pure.
bool ConsoleDisplay_Install( ICvar *pCVar );

/// Removes it from the ICvar it was installed on; a no-op when not installed.
void ConsoleDisplay_Remove();

/// The forwarder off the engine's list for a managed reload's gap, back on the
/// same ICvar at End() or the scope's end: in the gap every area slot is on the
/// trap table, and a forwarded print recursed through the trap's own report
/// (the nightly hot-reload SIGABRT, 10-10/11). A no-op when not installed.
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

#endif // SOURCESHARP_CONSOLE_DISPLAY_FORWARD_H
