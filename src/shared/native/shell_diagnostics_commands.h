// The readouts behind the diagnostics console commands, shared with the debug-only
// server commands in shell_diagnostics_debug.cpp.

#pragma once

// The not-shipped ledger of this DLL, and the declared list beside it.
void SourceSharp_PrintNotShipped();

// The process-wide crossing counters. bReset closes the interval: the report
// printed is the one just ended, and the counters start again at zero.
void SourceSharp_PrintCrossings( bool bReset );
