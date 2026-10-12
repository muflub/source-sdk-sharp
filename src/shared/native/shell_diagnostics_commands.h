//========= SourceSharp — the readouts behind the diagnostics console commands ==//
//
// shell_diagnostics.cpp defines both and registers the client's command for
// each (cs_cl_notshipped, cs_cl_crossings). The server's names for them --
// cs_sv_notshipped and cs_sv_crossings -- are diagnostics and live in
// shell_diagnostics_debug.cpp, which only a debug build of server.so compiles;
// this header is how that file reaches the bodies.
//
// Deliberately NOT shell_diagnostics.h: that header is included by dozens of
// translation units, and these have exactly two callers each.
//
//=============================================================================//

#ifndef SHELL_DIAGNOSTICS_COMMANDS_H
#define SHELL_DIAGNOSTICS_COMMANDS_H
#ifdef _WIN32
#pragma once
#endif

// The not-shipped ledger of THIS DLL, and the declared list beside it.
void SourceSharp_PrintNotShipped();

// The process-wide crossing counters. bReset closes the interval: the report
// printed is the one just ended, and the counters start again at zero.
void SourceSharp_PrintCrossings( bool bReset );

#endif // SHELL_DIAGNOSTICS_COMMANDS_H
