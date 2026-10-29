// Engine-boundary probes: per-method call counts, null/non-null results and the
// distinct return addresses that called, for finding out whether the engine
// calls a method and from where. Engine-free so it can be tested; not atomic,
// because every probed method runs on the main thread and an atomic in
// CheckTransmit would cost more than it measures.

#pragma once

#include "ss_platform.h"

// Every probed method, in vtable order per interface. The GetPlayerState_*
// entries count which answer GetPlayerState gave.
enum BoundaryProbeId_t
{
	// IServerGameEnts, vtable slots 2..7 (slots 0 and 1 are the destructor pair).
	kBoundaryProbe_Ents_SetDebugEdictBase = 0,
	kBoundaryProbe_Ents_MarkEntitiesAsTouching,
	kBoundaryProbe_Ents_FreeContainingEntity,
	kBoundaryProbe_Ents_BaseEntityToEdict,
	kBoundaryProbe_Ents_EdictToBaseEntity,
	kBoundaryProbe_Ents_CheckTransmit,

	// IServerGameClients, vtable slots 0..18.
	kBoundaryProbe_Clients_GetPlayerLimits,
	kBoundaryProbe_Clients_ClientConnect,
	kBoundaryProbe_Clients_ClientActive,
	kBoundaryProbe_Clients_ClientDisconnect,
	kBoundaryProbe_Clients_ClientPutInServer,
	kBoundaryProbe_Clients_ClientCommand,
	kBoundaryProbe_Clients_SetCommandClient,
	kBoundaryProbe_Clients_ClientSettingsChanged,
	kBoundaryProbe_Clients_ClientSetupVisibility,
	kBoundaryProbe_Clients_ProcessUsercmds,
	kBoundaryProbe_Clients_PostClientMessagesSent,
	kBoundaryProbe_Clients_GetPlayerState,
	kBoundaryProbe_Clients_ClientEarPosition,
	kBoundaryProbe_Clients_GetReplayDelay,
	kBoundaryProbe_Clients_GetBugReportInfo,
	kBoundaryProbe_Clients_NetworkIDValidated,
	kBoundaryProbe_Clients_ClientCommandKeyValues,
	kBoundaryProbe_Clients_ClientSpawned,
	kBoundaryProbe_Clients_ClientVoice,

	// Which arm answered GetPlayerState.
	kBoundaryProbe_GetPlayerState_Standalone,
	kBoundaryProbe_GetPlayerState_SDK,
	kBoundaryProbe_GetPlayerState_Null,

	// IClientEntityList, slots 0..8 of CClientEntityList's secondary vtable. Each
	// DLL has its own counters, so the other side's rows read zero.
	kBoundaryProbe_ClientList_GetClientNetworkable,
	kBoundaryProbe_ClientList_GetClientNetworkableFromHandle,
	kBoundaryProbe_ClientList_GetClientUnknownFromHandle,
	kBoundaryProbe_ClientList_GetClientEntity,
	kBoundaryProbe_ClientList_GetClientEntityFromHandle,
	kBoundaryProbe_ClientList_NumberOfEntities,
	kBoundaryProbe_ClientList_GetHighestEntityIndex,
	kBoundaryProbe_ClientList_SetMaxEntities,
	kBoundaryProbe_ClientList_GetMaxEntities,

	kBoundaryProbeCount
};

// The name a report line carries.
inline const char *BoundaryProbeName( int id )
{
	switch ( id )
	{
		case kBoundaryProbe_Ents_SetDebugEdictBase:
			return "IServerGameEnts::SetDebugEdictBase";
		case kBoundaryProbe_Ents_MarkEntitiesAsTouching:
			return "IServerGameEnts::MarkEntitiesAsTouching";
		case kBoundaryProbe_Ents_FreeContainingEntity:
			return "IServerGameEnts::FreeContainingEntity";
		case kBoundaryProbe_Ents_BaseEntityToEdict:
			return "IServerGameEnts::BaseEntityToEdict";
		case kBoundaryProbe_Ents_EdictToBaseEntity:
			return "IServerGameEnts::EdictToBaseEntity";
		case kBoundaryProbe_Ents_CheckTransmit:
			return "IServerGameEnts::CheckTransmit";

		case kBoundaryProbe_Clients_GetPlayerLimits:
			return "IServerGameClients::GetPlayerLimits";
		case kBoundaryProbe_Clients_ClientConnect:
			return "IServerGameClients::ClientConnect";
		case kBoundaryProbe_Clients_ClientActive:
			return "IServerGameClients::ClientActive";
		case kBoundaryProbe_Clients_ClientDisconnect:
			return "IServerGameClients::ClientDisconnect";
		case kBoundaryProbe_Clients_ClientPutInServer:
			return "IServerGameClients::ClientPutInServer";
		case kBoundaryProbe_Clients_ClientCommand:
			return "IServerGameClients::ClientCommand";
		case kBoundaryProbe_Clients_SetCommandClient:
			return "IServerGameClients::SetCommandClient";
		case kBoundaryProbe_Clients_ClientSettingsChanged:
			return "IServerGameClients::ClientSettingsChanged";
		case kBoundaryProbe_Clients_ClientSetupVisibility:
			return "IServerGameClients::ClientSetupVisibility";
		case kBoundaryProbe_Clients_ProcessUsercmds:
			return "IServerGameClients::ProcessUsercmds";
		case kBoundaryProbe_Clients_PostClientMessagesSent:
			return "IServerGameClients::PostClientMessagesSent_DEPRECIATED";
		case kBoundaryProbe_Clients_GetPlayerState:
			return "IServerGameClients::GetPlayerState";
		case kBoundaryProbe_Clients_ClientEarPosition:
			return "IServerGameClients::ClientEarPosition";
		case kBoundaryProbe_Clients_GetReplayDelay:
			return "IServerGameClients::GetReplayDelay";
		case kBoundaryProbe_Clients_GetBugReportInfo:
			return "IServerGameClients::GetBugReportInfo";
		case kBoundaryProbe_Clients_NetworkIDValidated:
			return "IServerGameClients::NetworkIDValidated";
		case kBoundaryProbe_Clients_ClientCommandKeyValues:
			return "IServerGameClients::ClientCommandKeyValues";
		case kBoundaryProbe_Clients_ClientSpawned:
			return "IServerGameClients::ClientSpawned";
		case kBoundaryProbe_Clients_ClientVoice:
			return "IServerGameClients::ClientVoice";

		case kBoundaryProbe_GetPlayerState_Standalone:
			return "GetPlayerState -> the player's engine block";
		case kBoundaryProbe_GetPlayerState_SDK:
			return "GetPlayerState -> SDK arm (UNARMED since 3c: the arm is gone)";
		case kBoundaryProbe_GetPlayerState_Null:
			return "GetPlayerState -> orphan state (was NULL before 3c)";

		case kBoundaryProbe_ClientList_GetClientNetworkable:
			return "IClientEntityList::GetClientNetworkable";
		case kBoundaryProbe_ClientList_GetClientNetworkableFromHandle:
			return "IClientEntityList::GetClientNetworkableFromHandle";
		case kBoundaryProbe_ClientList_GetClientUnknownFromHandle:
			return "IClientEntityList::GetClientUnknownFromHandle";
		case kBoundaryProbe_ClientList_GetClientEntity:
			return "IClientEntityList::GetClientEntity";
		case kBoundaryProbe_ClientList_GetClientEntityFromHandle:
			return "IClientEntityList::GetClientEntityFromHandle";
		case kBoundaryProbe_ClientList_NumberOfEntities:
			return "IClientEntityList::NumberOfEntities";
		case kBoundaryProbe_ClientList_GetHighestEntityIndex:
			return "IClientEntityList::GetHighestEntityIndex";
		case kBoundaryProbe_ClientList_SetMaxEntities:
			return "IClientEntityList::SetMaxEntities";
		case kBoundaryProbe_ClientList_GetMaxEntities:
			return "IClientEntityList::GetMaxEntities";
	}
	return "<unknown probe id>";
}

class CBoundaryProbe
{
public:
	// The overflow counter says when this was not enough.
	enum
	{
		kMaxCallers = 16
	};

	CBoundaryProbe() { Reset(); }

	void Reset() { memset( m_Methods, 0, sizeof( m_Methods ) ); }

	// A call whose result is not counted.
	void Hit( int id, const void *pCaller ) { Record( id, pCaller, kResultNotCounted ); }

	// A call whose result is a pointer: counts null and non-null answers.
	void HitPointerResult( int id, const void *pCaller, bool bResultWasNull )
	{
		Record( id, pCaller, bResultWasNull ? kResultNull : kResultNonNull );
	}

	uint64_t Calls( int id ) const { return Valid( id ) ? m_Methods[id].nCalls : 0; }

	uint64_t NullResults( int id ) const { return Valid( id ) ? m_Methods[id].nNullResults : 0; }

	uint64_t NonNullResults( int id ) const { return Valid( id ) ? m_Methods[id].nNonNullResults : 0; }

	int CallerCount( int id ) const { return Valid( id ) ? m_Methods[id].nCallers : 0; }

	uint64_t CallerOverflow( int id ) const { return Valid( id ) ? m_Methods[id].nCallerOverflow : 0; }

	const void *Caller( int id, int slot ) const
	{
		if ( !Valid( id ) || slot < 0 || slot >= m_Methods[id].nCallers )
			return NULL;
		return m_Methods[id].pCallers[slot];
	}

	uint64_t CallerHits( int id, int slot ) const
	{
		if ( !Valid( id ) || slot < 0 || slot >= m_Methods[id].nCallers )
			return 0;
		return m_Methods[id].nCallerHits[slot];
	}

	// One report line for one probe id. Returns what snprintf would have written.
	// Callers print as module+0xOFFSET, which survives ASLR.
	int FormatLine( int id, char *pBuf, int nBufLen ) const
	{
		if ( !Valid( id ) || nBufLen <= 0 )
		{
			if ( nBufLen > 0 )
				pBuf[0] = '\0';
			return 0;
		}

		const Method_t &m = m_Methods[id];

		int nWritten = snprintf(
			pBuf, (size_t)nBufLen, "%-56s calls=%llu null=%llu nonnull=%llu callers=", BoundaryProbeName( id ),
			(unsigned long long)m.nCalls, (unsigned long long)m.nNullResults, (unsigned long long)m.nNonNullResults );

		if ( m.nCallers == 0 )
			nWritten += AppendTo( pBuf, nBufLen, nWritten, "none" );

		for ( int i = 0; i < m.nCallers; ++i )
		{
			char szWhere[256];
			DescribeAddress( m.pCallers[i], szWhere, sizeof( szWhere ) );

			char szOne[320];
			snprintf( szOne, sizeof( szOne ), "%s%s x%llu", i ? ", " : "", szWhere,
					  (unsigned long long)m.nCallerHits[i] );
			nWritten += AppendTo( pBuf, nBufLen, nWritten, szOne );
		}

		if ( m.nCallerOverflow )
		{
			char szOver[64];
			snprintf( szOver, sizeof( szOver ), " (+%llu calls from further sites)",
					  (unsigned long long)m.nCallerOverflow );
			nWritten += AppendTo( pBuf, nBufLen, nWritten, szOver );
		}

		return nWritten;
	}

	// "libfoo.so+0x1234" for an address inside a loaded module, else the raw pointer.
	static void DescribeAddress( const void *pAddr, char *pBuf, int nBufLen )
	{
		if ( nBufLen <= 0 )
			return;

		SSModuleInfo info;
		if ( SS_ModuleOfAddress( pAddr, &info ) && info.szPath[0] )
		{
			const char *pszName = SS_ModuleBaseName( info.szPath );

			const unsigned long long nOffset = (unsigned long long)( (const char *)pAddr - (const char *)info.pBase );

			snprintf( pBuf, (size_t)nBufLen, "%s+0x%llx", pszName, nOffset );
			return;
		}
		snprintf( pBuf, (size_t)nBufLen, "%p", pAddr );
	}

private:
	enum ResultKind_t
	{
		kResultNotCounted,
		kResultNull,
		kResultNonNull
	};

	struct Method_t
	{
		uint64_t nCalls;
		uint64_t nNullResults;
		uint64_t nNonNullResults;
		uint64_t nCallerOverflow;
		const void *pCallers[kMaxCallers];
		uint64_t nCallerHits[kMaxCallers];
		int nCallers;
	};

	static bool Valid( int id ) { return id >= 0 && id < kBoundaryProbeCount; }

	// Appends at nSoFar, returning what snprintf would have written.
	static int AppendTo( char *pBuf, int nBufLen, int nSoFar, const char *pszText )
	{
		if ( nSoFar >= nBufLen )
			return (int)strlen( pszText );
		return snprintf( pBuf + nSoFar, (size_t)( nBufLen - nSoFar ), "%s", pszText );
	}

	void Record( int id, const void *pCaller, ResultKind_t kind )
	{
		if ( !Valid( id ) )
			return;

		Method_t &m = m_Methods[id];
		++m.nCalls;

		if ( kind == kResultNull )
			++m.nNullResults;
		else if ( kind == kResultNonNull )
			++m.nNonNullResults;

		for ( int i = 0; i < m.nCallers; ++i )
		{
			if ( m.pCallers[i] == pCaller )
			{
				++m.nCallerHits[i];
				return;
			}
		}

		if ( m.nCallers < kMaxCallers )
		{
			m.pCallers[m.nCallers] = pCaller;
			m.nCallerHits[m.nCallers] = 1;
			++m.nCallers;
			return;
		}

		++m.nCallerOverflow;
	}

	Method_t m_Methods[kBoundaryProbeCount];
};

#ifdef CLIENT_DLL
// The client's probe. Function-local, so it is not a file-scope static; the server's is
// a member of its DLL tier's services, handed to the objects it counts.
inline CBoundaryProbe &BoundaryProbes()
{
	static CBoundaryProbe s_Probes;
	return s_Probes;
}
#endif

// The caller's return address, which is the engine instruction after the call.
#if defined( __GNUC__ ) || defined( __clang__ )
#define SS_BOUNDARY_CALLER() ( SS_RETURN_ADDRESS() )
#else
#define SS_BOUNDARY_CALLER() ( (const void *)0 )
#endif

// On the probe object the caller was handed.
#define SS_BOUNDARY_HIT( probes, id ) ( probes ).Hit( ( id ), SS_BOUNDARY_CALLER() )

#define SS_BOUNDARY_HIT_PTR( probes, id, ptr ) \
	( probes ).HitPointerResult( ( id ), SS_BOUNDARY_CALLER(), ( ptr ) == NULL )
