// The native tests' <strings.h> under MSVC (test builds only): the two
// case-insensitive compares, the CRT's under their underscore names.
#pragma once
#include <string.h>
#define strcasecmp _stricmp
#define strncasecmp _strnicmp
