// The pristine side of saverestore_symbol_oracle_test: upstream's
// CSaveRestoreSegment, compiled from the clone at valve-sdk.lock, behind two
// plain functions so the test TU never sees its class.
#include "tier0/platform.h"
#include "mathlib/vector.h"
#include "datamap.h"
#include "string_t.h"
#include "const.h"
#include "saverestoretypes.h"

static CSaveRestoreSegment s_Upstream;

void Upstream_InitSymbolTable(char** pTokens, int nTokens)
{
	s_Upstream = CSaveRestoreSegment();
	s_Upstream.InitSymbolTable(pTokens, nTokens);
}

unsigned short Upstream_FindCreateSymbol(const char* pszToken)
{
	return s_Upstream.FindCreateSymbol(pszToken);
}

const char* Upstream_StringFromSymbol(int token)
{
	return s_Upstream.StringFromSymbol(token);
}
