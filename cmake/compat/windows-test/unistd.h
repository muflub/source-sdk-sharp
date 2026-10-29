// The native tests' <unistd.h> under MSVC (test builds only). The calls the
// tests make are the CRT's under their underscore names, which
// ss_posix_test_compat.h (forced into every test) maps; this header only has
// to exist and bring the CRT's declarations in.
#pragma once
#include <direct.h>
#include <io.h>
#include <process.h>
