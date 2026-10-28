//========= SourceSharp — a DSO shaped like server.so, for alloc_check's unload report
//
// Built with alloc_check.cpp and its link wraps, as server.so is. At load it
// fills a static std::unordered_map (whose nodes its destructor frees) and
// leaks exactly one block on purpose. When alloc_check_unload_test dlcloses it
// -- as the engine unloads server.so -- the unload report must count the one
// leak and none of the map's nodes: the report runs after static destructors.
//
//=============================================================================//

#include <unordered_map>

namespace
{
// Ints only: a std::string built out-of-line in libstdc++.so is allocated
// unwrapped and freed here, which the checker rightly calls a foreign free.
std::unordered_map<int, int> s_Registry = []
{
	std::unordered_map<int, int> m;
	for ( int i = 0; i < 50; ++i )
		m.emplace( i, i );
	return m;
}();

}

// The one real leak, never freed: the report's positive arm. Exported, so the
// pointer escapes and the compiler may not elide the new (C++14 allows that
// for an allocation nothing can observe).
extern "C" __attribute__( ( visibility( "default" ) ) ) char *g_AllocUnloadProbeLeak = new char[7];

extern "C" __attribute__( ( visibility( "default" ) ) ) int AllocUnloadProbeEntries()
{
	return (int)s_Registry.size();
}
