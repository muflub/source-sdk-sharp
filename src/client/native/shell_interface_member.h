//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// An object of the client DLL object's, exposed by name through CreateInterface as the
// EXPOSE macros expose a static. CreateInterface's create functions take no context, so the
// registration's is a thunk bound to this row (bound_callback.h).

#pragma once

#include "interface.h"

#include "bound_callback.h"

class CInterfaceMember
{
public:
	CInterfaceMember(void* pObject, const char* pszVersion)
		: m_pObject(pObject)
		, m_Create(this, &Fn_t::Trampoline<CInterfaceMember, &CInterfaceMember::Create>)
		, m_Reg(m_Create.Get(), pszVersion)
	{
	}

	CInterfaceMember(const CInterfaceMember&) = delete;
	CInterfaceMember& operator=(const CInterfaceMember&) = delete;

private:
	typedef CBoundCallback<void*()> Fn_t;

	void* Create() { return m_pObject; }

	void* m_pObject;
	Fn_t m_Create;
	InterfaceReg m_Reg;
};
