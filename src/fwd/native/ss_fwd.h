//========= SourceSharp — the forwarder table, as managed binds it ===========//
//
// plan_engine_forwarders.md. A forwarder library (libss_fwd_engine.so,
// ss_fwd_engine.dll) holds one generated function per engine method managed
// calls, each a direct call through the engine object's own vtable compiled
// for this ABI. Its forwarders are served as one table per interface:
//
//   word 0   the forwarders, an array of plain function pointers in the order
//            tools/engine-forwarders wrote them (EngineForwarders.g.cs names
//            the same order, field by field);
//   word 1   how many slots there are.
//
// A plain array has one layout on every ABI, so nothing here or in managed
// knows which C++ ABI the engine was built with. A table's slots only ever
// append (the generator refuses anything else), so managed checks only that
// word 1 is at least the number of fields it was built with.
//
// No SDK header: this is the contract between the library, the game DLL that
// serves its tables (SourceSharp_InternalFactory) and managed.
//=============================================================================//
#ifndef SS_FWD_H
#define SS_FWD_H

#include <stdint.h>

struct SSForwarderTable
{
	void *const *ppSlots;
	int32_t nSlots;
};

// One table by the name managed asks for (SSFwd_<table>).
struct SSForwarderNamed
{
	const char *pszName;
	const SSForwarderTable *pTable;
};

#if defined( _WIN32 )
	#if defined( SS_FWD_ENGINE_BUILD )
		#define SS_FWD_ENGINE_API extern "C" __declspec( dllexport )
	#else
		#define SS_FWD_ENGINE_API extern "C" __declspec( dllimport )
	#endif
#else
	#define SS_FWD_ENGINE_API extern "C" __attribute__(( visibility( "default" ) ))
#endif

// The library's one export: the table served under pszName, or NULL for a
// name it does not serve. The tables are constant data that live as long as
// the library: nothing is allocated, so nothing is released.
SS_FWD_ENGINE_API const SSForwarderTable *SSFwdEngine_Table( const char *pszName );

// What a game DLL's internal factory answers for a name (SourceSharp_
// InternalFactory): the table, as the object managed binds (its first word is
// the slot array), with its count in *pnEntries; NULL, and *pnEntries left
// alone, for a name the library does not serve.
inline void *SSFwdEngine_Serve( const char *pszName, int32_t *pnEntries )
{
	const SSForwarderTable *pTable = SSFwdEngine_Table( pszName );

	if ( !pTable )
		return nullptr;

	if ( pnEntries )
		*pnEntries = pTable->nSlots;

	return const_cast< SSForwarderTable * >( pTable );
}

#endif // SS_FWD_H
