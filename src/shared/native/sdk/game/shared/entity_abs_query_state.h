// Per-DLL flags stock kept as static members of CBaseEntity / C_BaseEntity: whether
// absolute-transform queries are valid, and (client only) whether abs recomputation is
// enabled. The client's main-thread rule lives in the accessor bodies, not here.
#pragma once
#define ENTITY_ABS_QUERY_STATE_H

// Whether absolute-transform queries may be asked right now. False while the hierarchy
// is incomplete (save/restore); readers only Assert on it.
inline bool s_bAbsQueriesValid = true;

inline bool &EntityAbs_QueriesValidRef()
{
	return s_bAbsQueriesValid;
}

// Whether CalcAbsolutePosition may recompute a coordinate frame right now. Client only.
inline bool s_bAbsRecomputationEnabled = true;

inline bool &EntityAbs_RecomputationEnabledRef()
{
	return s_bAbsRecomputationEnabled;
}

// The client's accessors, defined in c_baseentity.cpp beside their file statics. Client
// only: the server's accessors have no worker-thread arm.
#ifdef CLIENT_DLL

// Off the main thread these are no-ops or answer true.
void EntityAbs_SetQueriesValid( bool bValid );
bool EntityAbs_AreQueriesValid();

void EntityAbs_SetRecomputationsEnabled( bool bEnable );
bool EntityAbs_AreRecomputationsEnabled();

#endif // CLIENT_DLL
