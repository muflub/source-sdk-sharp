//========= SourceSharp — a libvstdlib.so holding what client.so binds to ====//
//
// For tier_bindings_client_test: built as fake_engine_tier/libvstdlib.so. It
// exports vstdlib's thread pool pointer and DestroyThreadPool, unmangled as
// the game's are (nm -D), and records each destruction.
//
//=============================================================================//

#include "fake_engine_tier.h"

#define FAKE_EXPORT extern "C" __attribute__( ( visibility( "default" ) ) )

namespace
{
char s_Pool;
int s_nDestroyed = 0;
void *s_pLastDestroyed = nullptr;
} // namespace

FAKE_EXPORT void *g_pThreadPool;
void *g_pThreadPool = &s_Pool;

FAKE_EXPORT void DestroyThreadPool( void *pPool )
{
	++s_nDestroyed;
	s_pLastDestroyed = pPool;
}

FAKE_EXPORT int FakeVstdlib_DestroyCount()
{
	return s_nDestroyed;
}

FAKE_EXPORT void *FakeVstdlib_LastDestroyed()
{
	return s_pLastDestroyed;
}

FAKE_EXPORT void *FakeVstdlib_PoolObject()
{
	return &s_Pool;
}
