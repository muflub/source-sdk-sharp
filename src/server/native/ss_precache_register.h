//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// ss::CPrecacheRegister: precache_register.h's static-init precache list,
// server.so's own (plan_server_novalve.md P5, lane E), for TUs on the R5
// include set.
//
// Nothing outside server.so sees the list: a registrant is a file-static
// object whose constructor links it in, and CWorld::Precache walks it. So the
// class is ss::, not a second body under the fork's name. The client keeps the
// fork's class in its own DLL.
//
// The members are the fork's, less PrecacheFn_Other: only the
// PRECACHE_REGISTER / PRECACHE_WEAPON_REGISTER macros named it, no server TU
// used them, and it went in lane E 33. So the one macro kept is
// SS_PRECACHE_REGISTER_FN, the fork's PRECACHE_REGISTER_FN. world_adapter.cpp
// defines the constructor and Precache().
//=============================================================================//

#ifndef SS_PRECACHE_REGISTER_H
#define SS_PRECACHE_REGISTER_H
#ifdef _WIN32
#pragma once
#endif

// Registers functionName to run at world precache.
#define SS_PRECACHE_REGISTER_FN( functionName ) \
	static ss::CPrecacheRegister precache_function_##functionName( functionName, 0 );

namespace ss
{

class CPrecacheRegister
{
public:
	typedef void ( *PrecacheFn )( void *pUser );	// a registrant's precache function

	CPrecacheRegister( PrecacheFn fn, const void *pUser );

	PrecacheFn         m_Fn;
	void              *m_pUser;
	CPrecacheRegister *m_pNext;

	// Runs every registrant, newest first.
	static void Precache();
};

} // namespace ss

#endif // SS_PRECACHE_REGISTER_H
