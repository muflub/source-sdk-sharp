//========= SourceSharp — a death test's child leaves no core dump ============//
//
// A death test that ends in abort() is a crash to the OS: systemd-coredump
// records it, and the desktop's crash handler (KDE's DrKonqi here) opens a
// "has encountered a fatal error" dialog for every run of the test. The abort
// is the expected result, so the child marks itself non-dumpable first: the
// kernel writes no core for it, and nothing downstream hears of it. The
// parent, and any unexpected crash in the test binary, still dump as before.
//
// Use it as the first statement of the death test's statement block:
//
//   EXPECT_DEATH( { SS_DEATH_NO_CORE(); abort(); }, "..." );
//
// A death test that ends in exit()/_exit() needs none: that is no crash.
//
//=============================================================================//

#ifndef SOURCESHARP_TEST_DEATH_TEST_NO_CORE_H
#define SOURCESHARP_TEST_DEATH_TEST_NO_CORE_H

#include <sys/prctl.h>

#define SS_DEATH_NO_CORE() ( (void)prctl( PR_SET_DUMPABLE, 0, 0, 0, 0 ) )

#endif // SOURCESHARP_TEST_DEATH_TEST_NO_CORE_H
