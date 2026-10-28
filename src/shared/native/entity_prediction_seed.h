//========= SourceSharp — the prediction seed, out of a class scope ==========//
//
// Two more process-wide variables that were declared `static` members of
// CBaseEntity and C_BaseEntity, and are not entity state by any reading:
//
//     m_nPredictionRandomSeed         the seed the client and the server both
//                                     use while running one usercmd, so that
//                                     SharedRandomInt/Float/Vector/Angle give
//                                     the same answers on both sides
//     m_nPredictionRandomSeedServer   the server's own unsynced seed, for the
//                                     places that must NOT match the client
//
// There is one of each per DLL, they are written once per usercmd by whoever is
// running that command, and no entity ever consults its own copy because there
// are no copies. The class supplied a namespace and nothing else — the same
// finding, and the same fix, as entity_debug_globals.h's six.
//
// WHY IT HAD TO MOVE. plan_baseentity.md Phase 13 makes `CBaseEntity` an alias
// for `IGameEntity`. A static DATA MEMBER of a name that is an alias does not
// merely fail to link: `CBaseEntity::m_nPredictionRandomSeed` stops PARSING,
// everywhere, at once. And the two lines that own these — the inline getter in
// baseentity_shared.h and the setter in baseentity_shared.cpp — are in the file
// step 3 DELETES, so they cannot be where the answer lives either.
//
// HEADER-ONLY, VIA ACCESSORS, for entity_debug_globals.h's reasons verbatim: a
// .cpp would mean editing server_csharp.vpc and client_csharp.vpc, which are
// the integration lane's; an `inline` variable (C++17) is one object across the
// program, and for a scalar with a constant initialiser it is
// constant-initialised, so no guard variable and no thread-safe-init branch —
// the read costs what the old member read cost.
//
// BOTH SIDES USE THIS ONE HEADER, and that matters more here than it did for
// the debug globals. The seed's whole job is that the client and the server
// agree, so two storages with one name would be the exact bug it exists to
// prevent. CBaseEntity::GetPredictionRandomSeed and SetPredictionRandomSeed are
// forwarders now, in the SHARED files that define them, so every existing
// caller on either side reads and writes this and nothing else does.
//
//=============================================================================//
#pragma once
#define ENTITY_PREDICTION_SEED_H

//-----------------------------------------------------------------------------
// -1 means "not inside a usercmd", which is what the four SharedRandom*
// functions assert on. Preserved exactly: the setter is handed a NULL command
// at the end of one and puts -1 back.
//-----------------------------------------------------------------------------
inline int s_nPredictionRandomSeed = -1;
inline int &EntityPrediction_SeedRef()
{
	return s_nPredictionRandomSeed;
}

//-----------------------------------------------------------------------------
// The server's unsynced companion. Declared on both sides so that shared code
// can be written once; only GAME_DLL ever writes or reads it, exactly as the
// member's own #ifdef GAME_DLL arms did.
//-----------------------------------------------------------------------------
inline int s_nPredictionRandomSeedServer = -1;
inline int &EntityPrediction_ServerSeedRef()
{
	return s_nPredictionRandomSeedServer;
}

inline int EntityPrediction_Seed( bool bUseUnSyncedServerPlatTime = false )
{
#ifdef GAME_DLL
	return bUseUnSyncedServerPlatTime ? EntityPrediction_ServerSeedRef()
	                                  : EntityPrediction_SeedRef();
#else
	return EntityPrediction_SeedRef();
#endif
}

inline void EntityPrediction_SetSeed( int nSeed )
{
	EntityPrediction_SeedRef() = nSeed;
}

inline void EntityPrediction_SetServerSeed( int nSeed )
{
	EntityPrediction_ServerSeedRef() = nSeed;
}

//-----------------------------------------------------------------------------
// THE PREDICTION PLAYER, the third static data member of the same cluster.
//
// `static CBasePlayer *CBaseEntity::m_pPredictionPlayer` (baseentity.h:2062) and
// `static C_BasePlayer *C_BaseEntity::m_pPredictionPlayer` (c_baseentity.h:1690)
// were defined in baseentity.cpp:94 and c_baseentity.cpp:91 -- the two files
// this lane owns. It is the player whose usercmd is being run right now, one per
// DLL, written only at the top and bottom of that run. No entity has a copy and
// no entity consults one, so it is a global with a class scope drawn round it,
// exactly like the two seeds above.
//
// WHY THE STORAGE IS `void *` AND THE TYPED ACCESSORS ARE ELSEWHERE. On the
// client `CBasePlayer` is not a class name at all -- baseplayer_shared.h:58 and
// shared_classnames.h:20 both `#define CBasePlayer C_BasePlayer`. So a
// `class CBasePlayer;` forward declaration in THIS header means `C_BasePlayer`
// in a translation unit that reached shared_classnames.h first and a genuinely
// distinct `::CBasePlayer` in one that did not, and the accessor's return type,
// its mangled name and so the storage it answered with would all
// silently differ between the two. That is an ODR violation whose symptom is
// two prediction players, which is precisely the bug the member existed to
// prevent -- and it would be introduced by an include order nobody edited.
//
// A void pointer has one type everywhere and cannot be got wrong. The two
// TYPED accessors stay where the type is unambiguous: CBaseEntity::
// GetPredictionPlayer / SetPredictionPlayer in game/shared/baseentity_shared.h,
// which is compiled after the class is complete on both sides. Both casts
// therefore happen in one place against one static type, so the round trip is
// exact -- no base-subobject adjustment can creep in between them.
//-----------------------------------------------------------------------------
inline void *s_pPredictionPlayer = 0;
inline void *&EntityPrediction_PlayerStorage()
{
	return s_pPredictionPlayer;
}
