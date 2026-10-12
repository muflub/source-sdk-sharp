//========= SourceSharp — the one way this mod stops the process ==============//
//
// A failed assert (shell_diagnostics' spew hook) and Error() (server.so's tier0
// port) both end here. See fatal_report.cpp.
//
//=============================================================================//

#ifndef SOURCESHARP_FATAL_REPORT_H
#define SOURCESHARP_FATAL_REPORT_H
#ifdef _WIN32
#pragma once
#endif

// The process's exit status when this mod stops it on purpose: -3 as the
// 8-bit status the parent sees. The launcher (source#) keys on it.
enum { kSourceSharpFatalExitCode = 253 };

// Reports pKind ("ASSERT", "ERROR") and pMsg with the stack to stderr and, when
// $SOURCESHARP_ASSERT_DIR names a directory, to <dir>/assert-<pid>.txt, then
// ends the process with kSourceSharpFatalExitCode. Allocates nothing.
[[noreturn]] void SourceSharp_FatalReport( const char *pKind, const char *pMsg );

#endif // SOURCESHARP_FATAL_REPORT_H
