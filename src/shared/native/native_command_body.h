// SS_NATIVE_COMMAND( name ) defines the native body of a console command declared
// in C#. The body joins a list keyed by name; the carrier the managed registration
// builds looks its name up once and, when found, runs the body directly with the
// engine's CCommand. Commands with no body dispatch to C#.

#pragma once

#include "ss_platform.h"

class CCommand;
class CServerDllServices;

class CNativeCommandBody
{
public:
	// A body is handed the DLL tier's services, which it reads instead of finding them.
	typedef void (*Fn)(const CCommand& args, CServerDllServices& services);

	CNativeCommandBody(const char* pName, Fn pfn)
		: m_pName(pName)
		, m_pfn(pfn)
		, m_pNext(Head())
	{
		Head() = this;
	}

	const char* Name() const { return m_pName; }

	void Run(const CCommand& args, CServerDllServices& services) const { m_pfn(args, services); }

	// Case-insensitive, as the console is. NULL when no body has that name.
	static const CNativeCommandBody* Find(const char* pName)
	{
		for (const CNativeCommandBody* p = Head(); p && pName; p = p->m_pNext)
		{
			if (!ss_strcasecmp(p->m_pName, pName))
				return p;
		}
		return NULL;
	}

	// The whole list, for tests.
	static const CNativeCommandBody* First() { return Head(); }

	const CNativeCommandBody* Next() const { return m_pNext; }

private:
	// Function-local, so it is one list across the DLL; a NULL pointer is
	// constant-initialised, so no static-init order can lose a node.
	static const CNativeCommandBody*& Head()
	{
		static const CNativeCommandBody* s_pHead = NULL;
		return s_pHead;
	}

	const char* m_pName;
	Fn m_pfn;
	const CNativeCommandBody* m_pNext;
};

#define SS_NATIVE_COMMAND(name)                                                             \
	static void SSNativeCommand_##name(const CCommand& args, CServerDllServices& services); \
	static CNativeCommandBody s_SSNativeCommandBody_##name(#name, SSNativeCommand_##name);  \
	static void SSNativeCommand_##name(const CCommand& args, CServerDllServices& services)
