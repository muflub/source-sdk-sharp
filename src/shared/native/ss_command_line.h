// tier0's CommandLine()->FindParm / ParmValue, for native code that links no tier0.
// A parameter is present when it is its own argument, ignoring case; its value is the
// next argument unless that is another parameter. Read on each call, nothing kept.

#pragma once

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
bool CommandLineValueIn( const char *pArgs, size_t nLen, const char *pParam, const char *pDefault, char *pOut,
						 size_t nOut );

// The variable the launcher moves SourceSharp's own flags into, one per line, because
// libtier0 refuses an engine command line over 512 bytes. Read as if it followed argv.
extern const char kLaunchArgsVariable[];

// That variable's value as an argv block ("a\0b\0"); "" for NULL or "".
std::string LaunchArgsBlock( const char *pValue );

} // namespace ss
