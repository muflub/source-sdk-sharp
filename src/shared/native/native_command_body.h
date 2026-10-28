//========= SourceSharp — a console command's native BODY, without a native declaration ==//
//
// plan_server_novalve.md P2.3c. Every console command server.so answers is
// DECLARED in C# -- its name, help text and flags are rows of
// SourceSharp.Server.NativeCommands.Server, registered through the one
// RegisterConCommand slot like any managed command. What stays native is the
// BODY of a command whose work is reading native state (a diagnostic dump, a
// probe of the parallel hierarchy): that is mechanism, and it lives beside
// the state it reads.
//
// SS_NATIVE_COMMAND( name ) defines such a body. It declares nothing to the
// engine: it links the body into a list, keyed by the command's name, and the
// carrier the managed registration builds (CSourceSharpCommand,
// sourcesharp_gamesystem.cpp) looks its own name up in that list once, when it
// is built. When there is a body, a dispatch runs it directly -- no crossing,
// the same CCommand the engine tokenised, the same call stack, so
// UTIL_GetCommandClient still answers. When there is none, the dispatch goes
// to C# as every other command's does.
//
// A BODY WITH NO DECLARATION ANSWERS TO NOTHING, and a declaration with no body
// reaches C#, which says so (NativeCommandTable). native_command_body_test and
// NativeCommandTableTests hold the two lists to each other.
//
// The list is built during static initialisation. Its head is a plain pointer
// and is constant-initialised to NULL before any constructor runs, so the
// order the translation units are initialised in cannot lose a node.
//
//=============================================================================//

#pragma once

#include "ss_platform.h"   // ss_strcasecmp

class CCommand;

class CNativeCommandBody
{
public:
	typedef void ( *Fn )( const CCommand &args );

	CNativeCommandBody( const char *pName, Fn pfn )
		: m_pName( pName ), m_pfn( pfn ), m_pNext( Head() )
	{
		Head() = this;
	}

	const char *Name() const { return m_pName; }
	void Run( const CCommand &args ) const { m_pfn( args ); }

	// Case-insensitive, as the console is. NULL when no body has that name.
	static const CNativeCommandBody *Find( const char *pName )
	{
		for ( const CNativeCommandBody *p = Head(); p && pName; p = p->m_pNext )
		{
			if ( !ss_strcasecmp( p->m_pName, pName ) )
				return p;
		}
		return NULL;
	}

	// The whole list, for the test that holds it to the C# table.
	static const CNativeCommandBody *First() { return Head(); }
	const CNativeCommandBody *Next() const { return m_pNext; }

private:
	// Function-local and inline, so the list is one list across the DLL with
	// no .cpp to add to either target. A pointer initialised to NULL is
	// constant-initialised: no guard, no ordering against the constructors.
	static const CNativeCommandBody *&Head()
	{
		static const CNativeCommandBody *s_pHead = NULL;
		return s_pHead;
	}

	const char               *m_pName;
	Fn                        m_pfn;
	const CNativeCommandBody *m_pNext;
};

#define SS_NATIVE_COMMAND( name )                                                        \
	static void SSNativeCommand_##name( const CCommand &args );                          \
	static CNativeCommandBody s_SSNativeCommandBody_##name( #name, SSNativeCommand_##name ); \
	static void SSNativeCommand_##name( const CCommand &args )
