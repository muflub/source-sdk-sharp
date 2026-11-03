//========= SourceSharp — what the fake engine tier library records ==========//
//
// fake_engine_tier0.cpp (libtier0.so) records each call made into it;
// tier_bindings_client_test reads the record through these exports.
//
//=============================================================================//

#pragma once

struct FakeTierCall
{
	const char* pszName;
	const void* pThis;
	unsigned int nArg;
};

extern "C"
{
// libtier0.so's record.
int FakeTier_CallCount();
const FakeTierCall* FakeTier_Call(int i);
void FakeTier_Reset();
void* FakeTier_CommandLineObject();
void* FakeTier_HeapObject();
}
