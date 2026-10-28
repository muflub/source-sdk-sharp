// The invisible-reach call sites shell_scripted.cpp recorded, for cs_sv_invisible_reach.

#pragma once

// How many distinct sites were kept. *pbCapped (when given) says whether the
// table is full, so that further sites were counted and not named.
int ShellScripted_InvisibleReachSiteCount( bool *pbCapped );

// The return address of site i, or NULL when i is out of range.
void *ShellScripted_InvisibleReachSite( int i );
