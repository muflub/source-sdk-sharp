//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON h  the CoreCLR host and bootstrap dispatch
#ifdef CLIENT_DLL

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#endif
#include "ss_command_line.h"

#include "fatal_report.h"
#include "shell_notshipped.h"

#ifdef CLIENT_DLL
#else
#include "server_class.h"

#endif
#include "assert_stack.h"
#include "diagnostics_hooks.h"

#include "crossings_fetch.h"
#include "imanaged_areas.h"
#include "imanaged_areas_client.h"
#include "managed_convar_ref.h"
#include "shell_log.h"
#include "shell_diagnostics.h"
#include "shell_diagnostics_commands.h"

#include "ss_string.h"
#ifndef CLIENT_DLL
#include "shell_factory.h"
#endif

#ifdef CLIENT_DLL
#define SOURCESHARP_SIDE_TAG "client"
#else
#define SOURCESHARP_SIDE_TAG "server"
#endif

#if defined( SS_COVERAGE )
// Declared rather than included: <gcov.h> exists only on a coverage build, and a
// linkage specification is not allowed at block scope.
extern "C" void __gcov_dump();
#endif

#ifdef CLIENT_DLL
// Always false: the client-side SourceTV camera is not shipped. engine->IsHLTV() is
// still asked so a session that really was watching HLTV records the refusal.
bool SourceSharp_HltvCameraAvailable()
{
	if ( engine != NULL && engine->IsHLTV() )
	{
		SourceSharp_NotShipped( "HLTV spectator camera",
								"game/client/hltvcamera.cpp is not shipped (plan_entity.md 7g); "
								"this client is watching a SourceTV broadcast and has no camera for it" );
	}

	return false;
}
#endif // CLIENT_DLL

// SourceSharp's own tables always dump their props: they must match across the
// boundary, and the client dump runs before command-line ConVars are applied.
static bool IsSourceSharpTable( const char *pTableName )
{
	return pTableName && strncmp( pTableName, "DT_SourceSharp", 14 ) == 0;
}

static const char *DTTypeName( int nType )
{
	switch ( nType )
	{
		case DPT_Int:
			return "int";
		case DPT_Float:
			return "float";
		case DPT_Vector:
			return "vector";
		case DPT_VectorXY:
			return "vectorxy";
		case DPT_String:
			return "string";
		case DPT_Array:
			return "array";
		case DPT_DataTable:
			return "datatable";
		default:
			return "?";
	}
}

void SourceSharp_DumpNetworkClasses()
{
	int nCount = 0;

#ifdef CLIENT_DLL
	for ( ClientClass *pCur = g_pClientClassHead; pCur; pCur = pCur->m_pNext )
		nCount++;

	SourceSharp_DiagPrint( "[SourceSharp] client.so registered %d ClientClass(es)\n", nCount );

	{
		for ( ClientClass *pCur = g_pClientClassHead; pCur; pCur = pCur->m_pNext )
		{
			RecvTable *pTable = pCur->m_pRecvTable;
			SourceSharp_DiagPrint( "[SourceSharp]     %s -> %s (%d prop(s))\n", pCur->GetName(),
								   pTable ? pTable->GetName() : "(no recv table)", pTable ? pTable->GetNumProps() : 0 );

			if ( !pTable )
				continue;
			if ( SourceSharp_GetVerbosity() < 2 && !IsSourceSharpTable( pTable->GetName() ) )
				continue;

			for ( int i = 0; i < pTable->GetNumProps(); i++ )
			{
				RecvProp *pProp = pTable->GetProp( i );
				if ( !pProp )
					continue;

				SourceSharp_DiagPrint( "[SourceSharp]         %-28s %s\n", pProp->GetName(),
									   DTTypeName( (int)pProp->GetType() ) );
			}
		}
	}
#else
	for ( ServerClass *pCur = g_pServerClassHead; pCur; pCur = pCur->m_pNext )
		nCount++;

	SourceSharp_DiagPrint( "[SourceSharp] server.so registered %d ServerClass(es)\n", nCount );

	{
		for ( ServerClass *pCur = g_pServerClassHead; pCur; pCur = pCur->m_pNext )
		{
			SendTable *pTable = pCur->m_pTable;
			SourceSharp_DiagPrint( "[SourceSharp]     %s -> %s (%d prop(s))\n", pCur->GetName(),
								   pTable ? pTable->GetName() : "(no send table)", pTable ? pTable->GetNumProps() : 0 );

			if ( !pTable )
				continue;
			if ( SourceSharp_GetVerbosity() < 2 && !IsSourceSharpTable( pTable->GetName() ) )
				continue;

			for ( int i = 0; i < pTable->GetNumProps(); i++ )
			{
				SendProp *pProp = pTable->GetProp( i );
				if ( !pProp )
					continue;

				SourceSharp_DiagPrint( "[SourceSharp]         %-28s %s offset=%d\n", pProp->GetName(),
									   DTTypeName( (int)pProp->GetType() ), pProp->GetOffset() );
			}
		}
	}
#endif
}

// Walks the DLL's own interface registry, which is what the engine's CreateInterface sees.
void SourceSharp_DumpExportedInterfaces()
{
#ifdef CLIENT_DLL
	int nCount = 0;
	for ( InterfaceReg *pReg = InterfaceReg::s_pInterfaceRegs; pReg; pReg = pReg->m_pNext )
		nCount++;

	SourceSharp_DiagPrint( "[SourceSharp] %s.so exports %d interface(s):\n", SOURCESHARP_SIDE_TAG, nCount );

	for ( InterfaceReg *pReg = InterfaceReg::s_pInterfaceRegs; pReg; pReg = pReg->m_pNext )
		SourceSharp_DiagPrint( "[SourceSharp]     %s\n", pReg->m_pName );
#else
	// server.so's factory is a table (shell_factory.cpp), not a registry.
	const int nCount = SourceSharp_ExposedCount();
	SourceSharp_DiagPrint( "[SourceSharp] %s.so exports %d interface(s):\n", SOURCESHARP_SIDE_TAG, nCount );

	for ( int i = 0; i < nCount; i++ )
		SourceSharp_DiagPrint( "[SourceSharp]     %s\n", SourceSharp_ExposedAt( i ).m_pszVersion );
#endif
}

#ifdef CLIENT_DLL
CON_COMMAND( cs_cl_notshipped, "SourceSharp: what reached a path this client build does not ship." )
{
	SourceSharp_PrintNotShipped();
}
#endif

// Managed<->native crossing counters: one process-wide table, so the client and server
// commands print the same counters. The area is the caller's, read at the point of use:
// a hot reload replaces the table.
void SourceSharp_PrintCrossings( ManagedDiagnosticsArea_t *pDiagnostics, bool bReset )
{
	char *pReport = NULL;
	const SourceSharpCrossingsStatus status = pDiagnostics
		? SourceSharp_FetchCrossingsReportWith( [pDiagnostics]( char *pBuffer, int nBytes, int bResetNow )
												{ return pDiagnostics->CrossingsReport( pBuffer, nBytes, bResetNow ); },
												bReset, &pReport )
		: SOURCESHARP_CROSSINGS_NO_SLOT;

	if ( status != SOURCESHARP_CROSSINGS_OK )
	{
		Msg( "[SourceSharp] crossings: %s\n", SourceSharp_CrossingsStatusText( status ) );
		return;
	}

	// One Msg for the whole report, so another thread's spew cannot interleave.
	Msg( "%s", pReport );

	if ( bReset )
		Msg( "[SourceSharp] crossings: counters reset\n" );

	free( pReport );
}

#ifdef CLIENT_DLL
CON_COMMAND( cs_cl_crossings,
			 "Print the managed<->native ABI crossing counts. "
			 "Pass 'reset' to print the interval and start a new one." )
{
	SourceSharp_PrintCrossings( ManagedDiagnosticsClient(),
								args.ArgC() > 1 && ss::StrICmp( args.Arg( 1 ), "reset" ) == 0 );
}
#endif
