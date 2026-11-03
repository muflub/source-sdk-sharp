// A console command whose body is a method of the object that owns it. ConCommand has no
// context of its own, so a file-scope one could only find its object through a locator.
// A member of an object in the client DLL object registers in Init's ConVar_Register with the
// file-scope ones, which are constructed during static initialisation too.

#pragma once

#include "tier1/convar.h"

#if defined(_MSC_VER)
// 'this' in the initialiser list is deliberate. tier1/convar.h turns C4355 back on.
#pragma warning(disable : 4355)
#endif
template <class T, void (T::*Method)(const CCommand&)>
class CMemberCommand final : public ICommandCallback
{
public:
	CMemberCommand(T& owner, const char* pName, const char* pHelp, int nFlags)
		: m_Owner(owner)
		, m_Command(pName, this, pHelp, nFlags)
	{
	}

	CMemberCommand(const CMemberCommand&) = delete;
	CMemberCommand& operator=(const CMemberCommand&) = delete;

	void CommandCallback(const CCommand& command) override { (m_Owner.*Method)(command); }

private:
	T& m_Owner;
	ConCommand m_Command;
};
