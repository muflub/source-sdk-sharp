//========= SourceSharp — shell_scripted.cpp's invisible-reach sites ==========//
//
// What shell_scripted_debug.cpp's cs_sv_invisible_reach needs to name the call
// sites shell_scripted.cpp recorded, and nothing more. The site table and its
// count stay file-static there; the running total has its own accessor already,
// SourceSharp_InvisibleRangeReach (shell_resolve.h).
//
//=============================================================================//

#pragma once

// How many distinct sites were kept. *pbCapped (when given) says whether the
// table is full, so that further sites were counted and not named.
int ShellScripted_InvisibleReachSiteCount( bool *pbCapped );

// The return address of site i, or NULL when i is out of range.
void *ShellScripted_InvisibleReachSite( int i );
