// The native tests' <unistd.h> under MSVC (test builds only). The calls the
// tests make are the CRT's under their underscore names, which
// ss_posix_test_compat.h (forced into every test) maps; this header only has
// to exist and bring the CRT's declarations in.
#ifndef SS_TEST_COMPAT_UNISTD_H
#define SS_TEST_COMPAT_UNISTD_H
#include <direct.h>
#include <io.h>
#include <process.h>
#endif
