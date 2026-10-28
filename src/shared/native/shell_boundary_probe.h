//========= SourceSharp ======================================================//
//
// ENGINE-BOUNDARY PROBES -- plan_entity.md Phase 0 step 0e.
//
// WHAT THIS IS FOR. Phase 1 and Phase 5 delete native entity machinery, and
// the question they cannot answer from the source is "may this method answer
// NULL?". Only two things settle that: the engine never calls it, or the
// engine ignores what it returns. This counts calls so the first can be
// measured, and it records WHERE EACH CALL CAME FROM so that each call site
// is counted on its own.
//
// THE CALLER ADDRESS IS THE POINT, not the count. A count says a method ran;
// a return address says which module ran it, and from which call site. Two
// sites calling one method are two rows, not one merged total, so a site that
// fires once at map load is not hidden behind one that fires every frame.
// docs/engine-boundary.md carries what the runs showed; this file is only the
// half that runs.
//
// UP TO kMaxCallers DISTINCT ADDRESSES PER METHOD, and an overflow count
// beside them. A fixed table rather than a growing one because this runs
// inside CheckTransmit, which the engine calls once per client per frame: an
// allocation there would change the thing being measured. Eight is a fixed
// ceiling, and the overflow counter is what says when it stopped being
// enough, so the table can never quietly lie by being full.
//
// ENGINE-FREE ON PURPOSE. No cbase.h, no tier0, nothing Valve: the native test
// suite compiles this header on its own (src/buildcpptests), and a probe that
// could only be tested inside a running game is a probe nobody checks. The
// report is produced as TEXT LINES and printed by the caller, so Msg() -- the
// one part that needs the engine -- stays in the .cpp that owns the console
// command.
//
// NOT ATOMIC, and it must not become so. Every method armed here is called on
// the engine's main thread inside the game DLL's frame; a lock or an atomic in
// CheckTransmit would cost more than the thing it measures. A torn count on
// some future threaded caller would misreport a number nobody is reading as
// exact anyway -- what the doc reads out of these is zero versus non-zero, and
// which module the caller was.
//
//===========================================================================//

#pragma once


#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "ss_platform.h"

//-----------------------------------------------------------------------------
// Every probed method, in vtable order per interface.
//
// The three kBoundaryProbe_GetPlayerState_* entries are not methods: they are
// the three ANSWERS GetPlayerState can give, counted separately because "the
// engine called it 40,000 times" and "the standalone arm answered every one of
// them" are different facts and Phase 1 needs the second one.
//
// STEP 3c CHANGED WHAT TWO OF THEM MEAN, and the labels below say so rather
// than going on reading zero for a reason nobody would guess. The SDK arm is
// DELETED, so its counter is unarmed and a zero there is an absence of code and
// not an absence of calls; and the method can no longer answer NULL at all --
// the third counter now counts the answer that used to be one, which is an edict
// with no player of ours behind it.
//-----------------------------------------------------------------------------
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

	// IClientEntityList, vtable slots 0..8 of the SECONDARY vtable --
	// CClientEntityList inherits CBaseEntityList first, so the interface the
	// engine holds is a subobject and its table starts 0x78 past the primary
	// one. Verified by dumping `vtable for CClientEntityList` out of client.so;
	// the order is icliententitylist.h's, unchanged.
	//
	// ONE ENUM FOR BOTH DLLs, and each side's report prints the whole table.
	// server.so and client.so are separate shared objects with separate copies
	// of the counters, so the client rows read zero in the server's report and
	// the server rows read zero in the client's -- which is a fact about which
	// DLL was asked, not a missing measurement. The report's header line names
	// the side so the two cannot be confused.
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

//-----------------------------------------------------------------------------
// The name a report line carries. A switch rather than a table so the header
// holds no data at all and the compiler fails on an unhandled id.
//-----------------------------------------------------------------------------
inline const char *BoundaryProbeName( int id )
{
	switch ( id )
	{
	case kBoundaryProbe_Ents_SetDebugEdictBase:      return "IServerGameEnts::SetDebugEdictBase";
	case kBoundaryProbe_Ents_MarkEntitiesAsTouching: return "IServerGameEnts::MarkEntitiesAsTouching";
	case kBoundaryProbe_Ents_FreeContainingEntity:   return "IServerGameEnts::FreeContainingEntity";
	case kBoundaryProbe_Ents_BaseEntityToEdict:      return "IServerGameEnts::BaseEntityToEdict";
	case kBoundaryProbe_Ents_EdictToBaseEntity:      return "IServerGameEnts::EdictToBaseEntity";
	case kBoundaryProbe_Ents_CheckTransmit:          return "IServerGameEnts::CheckTransmit";

	case kBoundaryProbe_Clients_GetPlayerLimits:       return "IServerGameClients::GetPlayerLimits";
	case kBoundaryProbe_Clients_ClientConnect:         return "IServerGameClients::ClientConnect";
	case kBoundaryProbe_Clients_ClientActive:          return "IServerGameClients::ClientActive";
	case kBoundaryProbe_Clients_ClientDisconnect:      return "IServerGameClients::ClientDisconnect";
	case kBoundaryProbe_Clients_ClientPutInServer:     return "IServerGameClients::ClientPutInServer";
	case kBoundaryProbe_Clients_ClientCommand:         return "IServerGameClients::ClientCommand";
	case kBoundaryProbe_Clients_SetCommandClient:      return "IServerGameClients::SetCommandClient";
	case kBoundaryProbe_Clients_ClientSettingsChanged: return "IServerGameClients::ClientSettingsChanged";
	case kBoundaryProbe_Clients_ClientSetupVisibility: return "IServerGameClients::ClientSetupVisibility";
	case kBoundaryProbe_Clients_ProcessUsercmds:       return "IServerGameClients::ProcessUsercmds";
	case kBoundaryProbe_Clients_PostClientMessagesSent:return "IServerGameClients::PostClientMessagesSent_DEPRECIATED";
	case kBoundaryProbe_Clients_GetPlayerState:        return "IServerGameClients::GetPlayerState";
	case kBoundaryProbe_Clients_ClientEarPosition:     return "IServerGameClients::ClientEarPosition";
	case kBoundaryProbe_Clients_GetReplayDelay:        return "IServerGameClients::GetReplayDelay";
	case kBoundaryProbe_Clients_GetBugReportInfo:      return "IServerGameClients::GetBugReportInfo";
	case kBoundaryProbe_Clients_NetworkIDValidated:    return "IServerGameClients::NetworkIDValidated";
	case kBoundaryProbe_Clients_ClientCommandKeyValues:return "IServerGameClients::ClientCommandKeyValues";
	case kBoundaryProbe_Clients_ClientSpawned:         return "IServerGameClients::ClientSpawned";
	case kBoundaryProbe_Clients_ClientVoice:           return "IServerGameClients::ClientVoice";

	case kBoundaryProbe_GetPlayerState_Standalone: return "GetPlayerState -> the player's engine block";
	case kBoundaryProbe_GetPlayerState_SDK:        return "GetPlayerState -> SDK arm (UNARMED since 3c: the arm is gone)";
	case kBoundaryProbe_GetPlayerState_Null:       return "GetPlayerState -> orphan state (was NULL before 3c)";

	case kBoundaryProbe_ClientList_GetClientNetworkable:           return "IClientEntityList::GetClientNetworkable";
	case kBoundaryProbe_ClientList_GetClientNetworkableFromHandle: return "IClientEntityList::GetClientNetworkableFromHandle";
	case kBoundaryProbe_ClientList_GetClientUnknownFromHandle:     return "IClientEntityList::GetClientUnknownFromHandle";
	case kBoundaryProbe_ClientList_GetClientEntity:                return "IClientEntityList::GetClientEntity";
	case kBoundaryProbe_ClientList_GetClientEntityFromHandle:      return "IClientEntityList::GetClientEntityFromHandle";
	case kBoundaryProbe_ClientList_NumberOfEntities:               return "IClientEntityList::NumberOfEntities";
	case kBoundaryProbe_ClientList_GetHighestEntityIndex:          return "IClientEntityList::GetHighestEntityIndex";
	case kBoundaryProbe_ClientList_SetMaxEntities:                 return "IClientEntityList::SetMaxEntities";
	case kBoundaryProbe_ClientList_GetMaxEntities:                 return "IClientEntityList::GetMaxEntities";
	}
	return "<unknown probe id>";
}

//-----------------------------------------------------------------------------
// The counters.
//-----------------------------------------------------------------------------
class CBoundaryProbe
{
public:
	// SIXTEEN, RAISED FROM EIGHT BY A MEASUREMENT. Eight looked generous; a
	// live run put nine distinct sites on
	// IClientEntityList::GetClientNetworkable and reported 1,789 calls as
	// overflow. The overflow counter is what said so -- a table that silently
	// dropped the ninth site would have left a caller out of the doc with
	// nothing to show it had.
	enum { kMaxCallers = 16 };

	CBoundaryProbe() { Reset(); }

	void Reset()
	{
		memset( m_Methods, 0, sizeof( m_Methods ) );
	}

	// A call with no result worth counting (a void method, or one whose result
	// this probe does not care about).
	void Hit( int id, const void *pCaller )
	{
		Record( id, pCaller, kResultNotCounted );
	}

	// A call whose result is a pointer. The null/non-null split is the whole
	// reason this method exists: a method that already answers NULL on every
	// call is one Phase 1 can make answer NULL unconditionally.
	void HitPointerResult( int id, const void *pCaller, bool bResultWasNull )
	{
		Record( id, pCaller, bResultWasNull ? kResultNull : kResultNonNull );
	}

	uint64_t Calls( int id ) const          { return Valid( id ) ? m_Methods[ id ].nCalls          : 0; }
	uint64_t NullResults( int id ) const    { return Valid( id ) ? m_Methods[ id ].nNullResults    : 0; }
	uint64_t NonNullResults( int id ) const { return Valid( id ) ? m_Methods[ id ].nNonNullResults : 0; }
	int      CallerCount( int id ) const    { return Valid( id ) ? m_Methods[ id ].nCallers        : 0; }
	uint64_t CallerOverflow( int id ) const { return Valid( id ) ? m_Methods[ id ].nCallerOverflow : 0; }

	const void *Caller( int id, int slot ) const
	{
		if ( !Valid( id ) || slot < 0 || slot >= m_Methods[ id ].nCallers )
			return NULL;
		return m_Methods[ id ].pCallers[ slot ];
	}

	uint64_t CallerHits( int id, int slot ) const
	{
		if ( !Valid( id ) || slot < 0 || slot >= m_Methods[ id ].nCallers )
			return 0;
		return m_Methods[ id ].nCallerHits[ slot ];
	}

	//-------------------------------------------------------------------------
	// One report line for one probe id. Returns the number of characters that
	// would have been written, snprintf-style, so a truncated line is
	// detectable rather than silently short.
	//
	// A CALLER IS PRINTED AS module+0xOFFSET, never as an absolute address:
	// the absolute one moves with ASLR, so two runs' reports could not be
	// compared line for line.
	//-------------------------------------------------------------------------
	int FormatLine( int id, char *pBuf, int nBufLen ) const
	{
		if ( !Valid( id ) || nBufLen <= 0 )
		{
			if ( nBufLen > 0 )
				pBuf[ 0 ] = '\0';
			return 0;
		}

		const Method_t &m = m_Methods[ id ];

		int nWritten = snprintf( pBuf, (size_t)nBufLen,
		                         "%-56s calls=%llu null=%llu nonnull=%llu callers=",
		                         BoundaryProbeName( id ),
		                         (unsigned long long)m.nCalls,
		                         (unsigned long long)m.nNullResults,
		                         (unsigned long long)m.nNonNullResults );

		if ( m.nCallers == 0 )
			nWritten += AppendTo( pBuf, nBufLen, nWritten, "none" );

		for ( int i = 0; i < m.nCallers; ++i )
		{
			char szWhere[ 256 ];
			DescribeAddress( m.pCallers[ i ], szWhere, sizeof( szWhere ) );

			char szOne[ 320 ];
			snprintf( szOne, sizeof( szOne ), "%s%s x%llu",
			          i ? ", " : "", szWhere,
			          (unsigned long long)m.nCallerHits[ i ] );
			nWritten += AppendTo( pBuf, nBufLen, nWritten, szOne );
		}

		if ( m.nCallerOverflow )
		{
			char szOver[ 64 ];
			snprintf( szOver, sizeof( szOver ), " (+%llu calls from further sites)",
			          (unsigned long long)m.nCallerOverflow );
			nWritten += AppendTo( pBuf, nBufLen, nWritten, szOver );
		}

		return nWritten;
	}

	//-------------------------------------------------------------------------
	// "libfoo.so+0x1234" for an address inside a loaded module, and the raw
	// pointer when the platform cannot say. Public so the test can pin it.
	//-------------------------------------------------------------------------
	static void DescribeAddress( const void *pAddr, char *pBuf, int nBufLen )
	{
		if ( nBufLen <= 0 )
			return;

		SSModuleInfo info;
		if ( SS_ModuleOfAddress( pAddr, &info ) && info.szPath[ 0 ] )
		{
			const char *pszName = SS_ModuleBaseName( info.szPath );

			const unsigned long long nOffset =
				(unsigned long long)( (const char *)pAddr - (const char *)info.pBase );

			snprintf( pBuf, (size_t)nBufLen, "%s+0x%llx", pszName, nOffset );
			return;
		}
		snprintf( pBuf, (size_t)nBufLen, "%p", pAddr );
	}

private:
	enum ResultKind_t { kResultNotCounted, kResultNull, kResultNonNull };

	struct Method_t
	{
		uint64_t	nCalls;
		uint64_t	nNullResults;
		uint64_t	nNonNullResults;
		uint64_t	nCallerOverflow;
		const void *pCallers[ kMaxCallers ];
		uint64_t	nCallerHits[ kMaxCallers ];
		int			nCallers;
	};

	static bool Valid( int id ) { return id >= 0 && id < kBoundaryProbeCount; }

	// Appends into a snprintf-style buffer at nSoFar, honouring truncation.
	// Returns what snprintf would have written, so the caller's running total
	// stays a "would have been" length rather than a clamped one.
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

		Method_t &m = m_Methods[ id ];
		++m.nCalls;

		if ( kind == kResultNull )
			++m.nNullResults;
		else if ( kind == kResultNonNull )
			++m.nNonNullResults;

		for ( int i = 0; i < m.nCallers; ++i )
		{
			if ( m.pCallers[ i ] == pCaller )
			{
				++m.nCallerHits[ i ];
				return;
			}
		}

		if ( m.nCallers < kMaxCallers )
		{
			m.pCallers[ m.nCallers ] = pCaller;
			m.nCallerHits[ m.nCallers ] = 1;
			++m.nCallers;
			return;
		}

		++m.nCallerOverflow;
	}

	Method_t m_Methods[ kBoundaryProbeCount ];
};

//-----------------------------------------------------------------------------
// The one instance, FUNCTION-LOCAL rather than file-scope for the reason
// shell_movement.cpp's MoveCounters() gives: tools/statics-audit holds
// src/sourcesharp to a banked count of file-scope statics, and diagnostics is
// not a reason to move that number. Two translation units arm it and one
// console command reads it, so it cannot be a member of either singleton.
//-----------------------------------------------------------------------------
inline CBoundaryProbe &BoundaryProbes()
{
	static CBoundaryProbe s_Probes;
	return s_Probes;
}

// The caller's return address, which is the engine instruction after the call.
#if defined( __GNUC__ ) || defined( __clang__ )
#define SS_BOUNDARY_CALLER()	( SS_RETURN_ADDRESS() )
#else
#define SS_BOUNDARY_CALLER()	( (const void *)0 )
#endif

#define SS_BOUNDARY_HIT( id ) \
	BoundaryProbes().Hit( ( id ), SS_BOUNDARY_CALLER() )

#define SS_BOUNDARY_HIT_PTR( id, ptr ) \
	BoundaryProbes().HitPointerResult( ( id ), SS_BOUNDARY_CALLER(), ( ptr ) == NULL )
