//========= SourceSharp — this process's own command line, without tier0 ======//
//
// tier0's CommandLine()->FindParm / CheckParm / ParmValue, for native code that
// no longer links libtier0 (plan_server_novalve.md P4). Read from
// /proc/self/cmdline -- NUL-separated argv, the process's own -- which is what
// the managed side reads too (ProcessCommandLine, LocalCommandLine.cs), with
// the same matching: a parameter is present when it is its own argument,
// ignoring case as FindParm does, and its value is the argument after it
// unless that one is another parameter.
//
// Read on each call, nothing kept: the callers ask a handful of times, at load.
// The *In forms take the argument block, so a test need not be the process.
//
//=============================================================================//

#pragma once

#include <cstddef>
#include <string>

namespace ss
{

// Whether pParam is on this process's command line.
bool CommandLineHas( const char *pParam );

// Copies the argument after pParam into pOut (always terminated) and returns
// true; false, with pOut = pDefault (or ""), when pParam is absent, last, or
// followed by another parameter ('-' or '+').
bool CommandLineValue( const char *pParam, const char *pDefault, char *pOut, size_t nOut );

// The same, over an argv block: nLen bytes of NUL-terminated arguments.
bool CommandLineHasIn( const char *pArgs, size_t nLen, const char *pParam );
bool CommandLineValueIn( const char *pArgs, size_t nLen, const char *pParam,
                         const char *pDefault, char *pOut, size_t nOut );

// The environment variable the launcher (src/launcher/Launcher.cs) moves
// SourceSharp's own flags into, one argument per line: libtier0 refuses an
// engine command line over 512 bytes. Read as if it followed argv.
extern const char kLaunchArgsVariable[];

// That variable's value as an argv block ("a\0b\0"); "" for NULL or "".
std::string LaunchArgsBlock( const char *pValue );

} // namespace ss
