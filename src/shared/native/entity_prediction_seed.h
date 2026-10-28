// The prediction seeds and player, which stock declared as static members of
// CBaseEntity and C_BaseEntity. Both sides must use this one storage so they agree.
#pragma once
#define ENTITY_PREDICTION_SEED_H

// -1 means "not inside a usercmd", which SharedRandom* asserts on; the setter puts it
// back when handed a NULL command.
inline int s_nPredictionRandomSeed = -1;

inline int &EntityPrediction_SeedRef()
{
	return s_nPredictionRandomSeed;
}

// The server's unsynced seed. Declared on both sides; only GAME_DLL reads or writes it.
inline int s_nPredictionRandomSeedServer = -1;

inline int &EntityPrediction_ServerSeedRef()
{
	return s_nPredictionRandomSeedServer;
}

inline int EntityPrediction_Seed( bool bUseUnSyncedServerPlatTime = false )
{
#ifdef GAME_DLL
	return bUseUnSyncedServerPlatTime ? EntityPrediction_ServerSeedRef() : EntityPrediction_SeedRef();
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

// The player whose usercmd is running. Stored as void * because CBasePlayer is a macro
// for C_BasePlayer on the client, so a typed forward declaration here could name two
// types depending on include order. The typed accessors are in baseentity_shared.h.
inline void *s_pPredictionPlayer = 0;

inline void *&EntityPrediction_PlayerStorage()
{
	return s_pPredictionPlayer;
}
