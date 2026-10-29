// The one way this mod stops the process: failed asserts and Error() end here.

#pragma once

// -3 as the 8-bit status the parent sees; the launcher keys on it.
enum
{
	kSourceSharpFatalExitCode = 253
};

// Reports pKind ("ASSERT", "ERROR") and pMsg with the stack to stderr and, when
// $SOURCESHARP_ASSERT_DIR names a directory, to <dir>/assert-<pid>.txt, then
// ends the process with kSourceSharpFatalExitCode. Allocates nothing.
[[noreturn]] void SourceSharp_FatalReport( const char *pKind, const char *pMsg );
