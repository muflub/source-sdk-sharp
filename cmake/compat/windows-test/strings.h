// The native tests' <strings.h> under MSVC (test builds only): the two
// case-insensitive compares, the CRT's under their underscore names.
#ifndef SS_TEST_COMPAT_STRINGS_H
#define SS_TEST_COMPAT_STRINGS_H
#include <string.h>
#define strcasecmp _stricmp
#define strncasecmp _strnicmp
#endif
