//========= SourceSharp — what the fake engine tier libraries record =========//
//
// fake_engine_tier0.cpp (libtier0.so) and fake_engine_vstdlib.cpp
// (libvstdlib.so) record each call made into them; tier_bindings_client_test
// reads the record through these exports.
//
//=============================================================================//

#pragma once

struct FakeTierCall
{
	const char *pszName;
	const void *pThis;
	unsigned int nArg;
};

extern "C"
{
// libtier0.so's record (the vstdlib calls go to their own library's).
int FakeTier_CallCount();
const FakeTierCall *FakeTier_Call( int i );
void FakeTier_Reset();
void *FakeTier_CommandLineObject();
void *FakeTier_HeapObject();

// libvstdlib.so's.
int FakeVstdlib_DestroyCount();
void *FakeVstdlib_LastDestroyed();
void *FakeVstdlib_PoolObject();
}
