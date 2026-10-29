// Our side of saverestore_symbol_oracle_test: engineabi/saverestore.h, in a
// namespace of its own so its inline members cannot be folded into upstream's
// (same names, different bodies: the linker would keep one and the test would
// compare a body with itself). Its includes are pulled in first, globally.
#include "const.h"
#include "string_t.h"
#include "mathlib/vector.h"
#include <stdint.h>
#include <string.h>
#include <new>

namespace ours
{
#include "engineabi/saverestore.h"
}

static ours::CSaveRestoreSegment s_Ours;

void Ours_InitSymbolTable(char** pTokens, int nTokens)
{
	s_Ours = ours::CSaveRestoreSegment();
	s_Ours.InitSymbolTable(pTokens, nTokens);
}

unsigned short Ours_FindCreateSymbol(const char* pszToken)
{
	return s_Ours.FindCreateSymbol(pszToken);
}

const char* Ours_StringFromSymbol(int token)
{
	return s_Ours.StringFromSymbol(token);
}
