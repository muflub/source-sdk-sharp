//========= SourceSharp — a libtier0.so holding what client.so binds to ======//
//
// For tier_bindings_client_test: built as fake_engine_tier/libtier0.so, so
// the loaded-object list names it as the game's libtier0.so is named. It
// exports, unmangled or mangled as the game's are (nm -D), each name
// tier_bindings_client.cpp binds, and records every call with its object, so a
// fact can tell that a call reached this library and not a copy of its own.
//
//=============================================================================//

#include "fake_engine_tier.h"

#define FAKE_EXPORT extern "C" __attribute__((visibility("default")))

namespace
{
FakeTierCall s_Calls[64];
int s_nCalls = 0;
char s_CommandLine;
char s_Heap;

void Record(const char* pszName, const void* pThis, unsigned int nArg)
{
	if (s_nCalls < 64)
		s_Calls[s_nCalls++] = FakeTierCall{pszName, pThis, nArg};
}
} // namespace

FAKE_EXPORT int FakeTier_CallCount()
{
	return s_nCalls;
}

FAKE_EXPORT const FakeTierCall* FakeTier_Call(int i)
{
	return i >= 0 && i < s_nCalls ? &s_Calls[i] : nullptr;
}

FAKE_EXPORT void FakeTier_Reset()
{
	s_nCalls = 0;
}

FAKE_EXPORT void* FakeTier_CommandLineObject()
{
	return &s_CommandLine;
}

FAKE_EXPORT void* FakeTier_HeapObject()
{
	return &s_Heap;
}

FAKE_EXPORT void* g_pMemAlloc;
void* g_pMemAlloc = &s_Heap;

FAKE_EXPORT void* CommandLine_Tier0()
{
	Record("CommandLine_Tier0", nullptr, 0);
	return &s_CommandLine;
}
