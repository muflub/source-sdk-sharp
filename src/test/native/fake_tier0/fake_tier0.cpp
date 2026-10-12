//========= SourceSharp — a libtier0.so with only a spew slot ==================//
//
// For tier0_spew_forward_test: built as fake_tier0/libtier0.so, so the loaded-
// object list names it as the game's libtier0.so is named. It exports tier0's
// two slot functions, unmangled as the game's are (nm -D), and nothing else.
// The slot starts empty; the test installs its recorder through it.
//
//=============================================================================//

typedef int ( *FakeSpewFunc_t )( int type, const char *pText );

static FakeSpewFunc_t s_pSpew = nullptr;

extern "C" __attribute__(( visibility( "default" ) )) void SpewOutputFunc( FakeSpewFunc_t func )
{
	s_pSpew = func;
}

extern "C" __attribute__(( visibility( "default" ) )) FakeSpewFunc_t GetSpewOutputFunc()
{
	return s_pSpew;
}
