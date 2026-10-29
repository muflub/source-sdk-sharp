// The readouts behind the diagnostics console commands, shared with the debug-only
// server commands in shell_diagnostics_debug.cpp.

#pragma once

// The not-shipped ledger of this DLL, and the declared list beside it.
void SourceSharp_PrintNotShipped();

#ifndef CLIENT_DLL
class IManagedDiagnostics;
typedef IManagedDiagnostics ManagedDiagnosticsArea_t;
#else
class IManagedDiagnosticsClient;
typedef IManagedDiagnosticsClient ManagedDiagnosticsArea_t;
#endif

// The process-wide crossing counters, asked of this load's diagnostics area (NULL
// outside a load). bReset closes the interval: the report printed is the one just
// ended, and the counters start again at zero.
void SourceSharp_PrintCrossings(ManagedDiagnosticsArea_t* pDiagnostics, bool bReset);
