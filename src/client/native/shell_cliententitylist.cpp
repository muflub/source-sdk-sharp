//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// The engine's VClientEntityList003, answered from the slot table. Nine index
// and handle lookups plus counts; the counts are maintained by bind and unbind.
//
// GetClientUnknownFromHandle must not answer NULL for a live entity: the
// engine's ClipRayToEntity calls GetCollideable() on the answer without a test.
// GetHighestEntityIndex is a loop bound for the engine's own tables, so it must
// never be too small.

#include <stdio.h>
#include <stdlib.h>
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "icliententitylist.h"
#include "iclientunknown.h"
#include "engine/IStaticPropMgr.h"
#include "engine/iserverplugin.h"
#include "tier0/dbg.h"
#include "tier0/platform.h"
#include "tier1/convar.h"
#include "tier1/interface.h"
#include "basehandle.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "collisionproperty.h"
#include "const.h"
#include "ihandleentity.h"

#include <typeinfo>

#include "slot_table.h"
#include "client_entity_counts.h"
#include "shell_cliententitylist.h"
#include "shell_standalone_client.h"

#include "handle_cross.h"

#include "shell_boundary_probe.h"

// The client's slot table. Not reset at level shutdown: every binding is
// removed by its shell's destructor, so a non-zero count between maps is a leak.
CSourceSharpSlotTable &SourceSharp_ClientSlotTable()
{
	static CSourceSharpSlotTable s_SlotTable;
	return s_SlotTable;
}

// Binds at the server's handle; the client never chooses its own serial.
bool SourceSharp_ClientSlotBindAt( const CBaseHandle &handle, C_StandaloneEntity *pShell )
{
	return SourceSharp_ClientSlotTable().BindAt( handle, pShell );
}

void SourceSharp_ClientSlotUnbind( int nEntry )
{
	SourceSharp_ClientSlotTable().Unbind( nEntry );
}

// The client's one downcast: SourceSharp_ClientSlotBindAt is the only route
// into the table and takes a C_StandaloneEntity *. Resolve compares the serial.
C_StandaloneEntity *SourceSharp_ClientShellFromHandle( const CBaseHandle &handle )
{
	return static_cast< C_StandaloneEntity * >( SourceSharp_ClientSlotTable().Resolve( handle ) );
}

// By entry index, which carries no serial; through the handle resolver so the
// cast stays in one place.
C_StandaloneEntity *SourceSharp_ClientShellFromIndex( int nEntry )
{
	return SourceSharp_ClientShellFromHandle( SourceSharp_ClientSlotTable().HandleOf( nEntry ) );
}

// IHandleEntity to shell, by the object's own handle.
C_StandaloneEntity *SourceSharp_ClientShellFromHandleEntity( IHandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return NULL;

	return SourceSharp_ClientShellFromHandle( pHandleEntity->GetRefEHandle() );
}

// What a trace hit, as a shell. Static props are filtered before the table.
C_StandaloneEntity *SourceSharp_ClientShellFromTraceHit( IHandleEntity *pHandleEntity )
{
	return SourceSharp_CrossHandleToClientShell< C_StandaloneEntity >( pHandleEntity,
		[]( IHandleEntity *pHit ) { return SourceSharp_ClientShellFromHandleEntity( pHit ); },
		[]( IHandleEntity *pHit ) { return staticpropmgr && staticpropmgr->IsStaticProp( pHit ); } );
}

// Table first, then the world, then the stock-list remnant.
static IClientUnknown *ClientUnknownFromIndex( int nEntry )
{
	C_StandaloneEntity *pShell = SourceSharp_ClientShellFromIndex( nEntry );

	if ( pShell )
		return pShell->GetIClientUnknown();

	// The world is not in the table.
	if ( IClientUnknown *pWorld = SourceSharp_ClientWorldFromIndex( nEntry ) )
		return pWorld;

	return SourceSharp_LegacyListUnknownFromIndex( nEntry );
}

static IClientUnknown *ClientUnknownFromHandle( const CBaseHandle &hEnt )
{
	C_StandaloneEntity *pShell = SourceSharp_ClientShellFromHandle( hEnt );

	if ( pShell )
		return pShell->GetIClientUnknown();

	// The serial is compared, so the previous map's world does not resolve.
	if ( IClientUnknown *pWorld = SourceSharp_ClientWorldFromHandle( hEnt ) )
		return pWorld;

	return SourceSharp_LegacyListUnknownFromHandle( hEnt );
}

// Whether this side holds the entity a handle names, serial and all.
int32_t SourceSharp_ClientEntityIsValid( uint32_t hEntity )
{
	// Splitting the invalid handle would hand Init a serial out of range.
	if ( hEntity == INVALID_EHANDLE_INDEX )
		return 0;

	const CBaseHandle handle( (int)( hEntity & ENT_ENTRY_MASK ),
	                          (int)( hEntity >> NUM_SERIAL_NUM_SHIFT_BITS ) );

	return ClientUnknownFromHandle( handle ) ? 1 : 0;
}

// The engine's IClientEntityList. Methods stay in icliententitylist.h's order,
// which is the vtable the engine holds. Holds no entities and cannot be walked.
class CSourceSharpClientEntityList : public IClientEntityList
{
public:
	CSourceSharpClientEntityList()
		: m_Counts( SourceSharp_ClientSlotTable() ), m_bWarnedUnresolved( false )
	{
	}

	// The busiest method on either interface; the engine tolerates NULL.
	virtual IClientNetworkable *GetClientNetworkable( int entnum ) OVERRIDE
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_ClientList_GetClientNetworkable );

		IClientUnknown *pUnknown = ClientUnknownFromIndex( entnum );
		return pUnknown ? pUnknown->GetClientNetworkable() : NULL;
	}

	// No engine caller; NULL for an unresolved handle, as stock answered.
	virtual IClientNetworkable *GetClientNetworkableFromHandle( CBaseHandle hEnt ) OVERRIDE
	{
		IClientUnknown *pUnknown = ClientUnknownFromHandle( hEnt );

		IClientNetworkable *pResult = pUnknown ? pUnknown->GetClientNetworkable() : NULL;
		SS_BOUNDARY_HIT_PTR( kBoundaryProbe_ClientList_GetClientNetworkableFromHandle, pResult );
		return pResult;
	}

	// The engine dereferences this answer in its own trace without a test, so
	// a miss is reported once per session.
	virtual IClientUnknown *GetClientUnknownFromHandle( CBaseHandle hEnt ) OVERRIDE
	{
		IClientUnknown *pResult = ClientUnknownFromHandle( hEnt );

		if ( !pResult && hEnt.IsValid() && !m_bWarnedUnresolved )
		{
			m_bWarnedUnresolved = true;
			Warning( "[SourceSharp] IClientEntityList::GetClientUnknownFromHandle: "
			         "entry %d serial %d resolves to NOTHING. The engine's trace "
			         "dereferences this without a test -- if "
			         "the caller was engine.so the next thing that happens is a "
			         "crash inside the engine. Said once per session.\n",
			         hEnt.GetEntryIndex(), hEnt.GetSerialNumber() );
		}

		SS_BOUNDARY_HIT_PTR( kBoundaryProbe_ClientList_GetClientUnknownFromHandle, pResult );
		return pResult;
	}

	// The engine tolerates NULL; the trace stores it in m_pEnt.
	virtual IClientEntity *GetClientEntity( int entnum ) OVERRIDE
	{
		IClientUnknown *pUnknown = ClientUnknownFromIndex( entnum );

		IClientEntity *pResult = pUnknown ? pUnknown->GetIClientEntity() : NULL;
		SS_BOUNDARY_HIT_PTR( kBoundaryProbe_ClientList_GetClientEntity, pResult );
		return pResult;
	}

	virtual IClientEntity *GetClientEntityFromHandle( CBaseHandle hEnt ) OVERRIDE
	{
		IClientUnknown *pUnknown = ClientUnknownFromHandle( hEnt );

		IClientEntity *pResult = pUnknown ? pUnknown->GetIClientEntity() : NULL;
		SS_BOUNDARY_HIT_PTR( kBoundaryProbe_ClientList_GetClientEntityFromHandle, pResult );
		return pResult;
	}

	// Maintained by Bind and Unbind, never derived by walking entries.
	virtual int NumberOfEntities( bool bIncludeNonNetworkable ) OVERRIDE
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_ClientList_NumberOfEntities );

		// The world is a networked entity outside the table; it counts under
		// both arguments.
		const int nSlot = SourceSharp_ClientCountWithWorld(
			SourceSharp_ClientWorldHandle(),
			m_Counts.NumberOfEntities( bIncludeNonNetworkable ) );

		return nSlot;
	}

	// A loop bound for the engine's own tables: too small and entities stop
	// drawing. Maintained by Bind and Unbind.
	virtual int GetHighestEntityIndex() OVERRIDE
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_ClientList_GetHighestEntityIndex );

		// The world raises it from -1 to 0 on a map with nothing else bound.
		const int nHighest =
			SourceSharp_ClientHighestWithWorld( SourceSharp_ClientWorldHandle(),
			                                    m_Counts.HighestEntityIndex() );

		return nHighest;
	}

	// Stored and handed back, as stock did.
	virtual void SetMaxEntities( int maxents ) OVERRIDE
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_ClientList_SetMaxEntities );

		m_Counts.SetMaxEntities( maxents );
	}

	virtual int GetMaxEntities() OVERRIDE
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_ClientList_GetMaxEntities );

		return m_Counts.MaxEntities();
	}

	// Called only after the table has accepted a bind, and after it has
	// released one.
	void OnBound( int nEntry )   { m_Counts.OnBound( nEntry ); }
	void OnUnbound( int nEntry ) { m_Counts.OnUnbound( nEntry ); }

private:
	CClientEntityCounts m_Counts;
	bool                m_bWarnedUnresolved;
};

static CSourceSharpClientEntityList &SourceSharpClientEntityList()
{
	static CSourceSharpClientEntityList s_List;
	return s_List;
}

// VClientEntityList003, registered through a factory so the object is built on
// the first CreateInterface rather than during static init.
static void *CreateSourceSharpClientEntityList()
{
	return static_cast< IClientEntityList * >( &SourceSharpClientEntityList() );
}

static InterfaceReg g_CreateSourceSharpClientEntityList(
	CreateSourceSharpClientEntityList, VCLIENTENTITYLIST_INTERFACE_VERSION );

IClientUnknown *SourceSharp_ClientUnknownFromIndex( int nEntry )
{
	return ClientUnknownFromIndex( nEntry );
}

IClientEntity *SourceSharp_ClientEntityFromIndex( int nEntry )
{
	IClientUnknown *pUnknown = ClientUnknownFromIndex( nEntry );
	return pUnknown ? pUnknown->GetIClientEntity() : NULL;
}

IClientUnknown *SourceSharp_ClientUnknownFromHandle( const CBaseHandle &hEnt )
{
	return ClientUnknownFromHandle( hEnt );
}

// Every EHANDLE resolve on the client: the slot table, then the world at entry 0.
IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle )
{
	if ( !handle.IsValid() )
		return NULL;

	return ClientUnknownFromHandle( handle );
}

IClientNetworkable *SourceSharp_ClientNetworkableFromHandle( const CBaseHandle &hEnt )
{
	IClientUnknown *pUnknown = ClientUnknownFromHandle( hEnt );
	return pUnknown ? pUnknown->GetClientNetworkable() : NULL;
}

IClientEntity *SourceSharp_ClientEntityFromHandle( const CBaseHandle &hEnt )
{
	IClientUnknown *pUnknown = ClientUnknownFromHandle( hEnt );
	return pUnknown ? pUnknown->GetIClientEntity() : NULL;
}

IClientRenderable *SourceSharp_ClientRenderableFromHandle( const CBaseHandle &hEnt )
{
	IClientUnknown *pUnknown = ClientUnknownFromHandle( hEnt );
	return pUnknown ? pUnknown->GetClientRenderable() : NULL;
}

// Index to handle: the occupant's own handle.
CBaseHandle SourceSharp_ClientHandleFromIndex( int nEntry )
{
	const CBaseHandle hSlot = SourceSharp_ClientSlotTable().HandleOf( nEntry );

	if ( hSlot.IsValid() )
		return hSlot;

	// Entry 0 is not in the table.
	const CBaseHandle hWorld = SourceSharp_ClientWorldHandle();

	if ( SourceSharp_ClientWorldAnswersForIndex( hWorld, nEntry ) )
		return hWorld;

	IClientUnknown *pLegacy = SourceSharp_LegacyListUnknownFromIndex( nEntry );

	return pLegacy ? pLegacy->GetRefEHandle() : CBaseHandle();
}

// A shell enters the world. LegacyListBind first, because it writes the
// shell's m_RefEHandle, which the table then adopts. A refused bind means two
// live shells claim one entry; it is reported.
void SourceSharp_ClientEntityBind( C_StandaloneEntity *pShell, int nEntNum, int nSerialNum )
{
	if ( !pShell )
		return;

	// Without the handle the engine cannot look the entity up to unpack into
	// it, and PostDataUpdate never runs.
	const CBaseHandle hEnt =
		SourceSharp_LegacyListBind( pShell->GetIClientUnknown(), nEntNum, nSerialNum );

	if ( !SourceSharp_ClientSlotBindAt( hEnt, pShell ) )
	{
		Warning( "[SourceSharp] client slot table REFUSED the bind for entity %d "
		         "(serial %d): the entry is already bound, outside the table, or the "
		         "handle is invalid. This entity will resolve to nothing.\n",
		         nEntNum, nSerialNum );
		return;
	}

	SourceSharpClientEntityList().OnBound( hEnt.GetEntryIndex() );
}

// A shell leaves, by its own handle, read before either half is torn down.
// The table is unbound first, mirroring Bind.
void SourceSharp_ClientEntityUnbind( C_StandaloneEntity *pShell )
{
	if ( !pShell )
		return;

	const CBaseHandle hEnt = pShell->GetRefEHandle();

	if ( hEnt.IsValid() )
	{
		const int nEntry = hEnt.GetEntryIndex();

		// Only if this shell is what is bound there: a refused bind never owned it.
		if ( SourceSharp_ClientShellFromHandle( hEnt ) == pShell )
		{
			SourceSharp_ClientSlotUnbind( nEntry );
			SourceSharpClientEntityList().OnUnbound( nEntry );
		}
	}

	SourceSharp_LegacyListUnbind( hEnt );
}

// `bound` should return to zero between maps; `refused` is a bind that did not happen.
CON_COMMAND( cs_cl_slot_table, "SourceSharp: the client entity slot table (plan_entity.md 3.3)" )
{
	const CSourceSharpSlotTable &table = SourceSharp_ClientSlotTable();

	Msg( "[SourceSharp] client slot table: %d entries (%d edict, %d engine-invisible)\n",
	     CSourceSharpSlotTable::EntryCount(), MAX_EDICTS,
	     CSourceSharpSlotTable::EntryCount() - MAX_EDICTS );
	Msg( "  bound %d  binds %d  unbinds %d  refused %d\n",
	     table.BoundCount(), table.Binds(), table.Unbinds(), table.Refused() );
	Msg( "  highest networked entry %d  max entities %d\n",
	     SourceSharpClientEntityList().GetHighestEntityIndex(),
	     SourceSharpClientEntityList().GetMaxEntities() );
}

// The client's engine-boundary probe counters; client.so's are separate from server.so's.
CON_COMMAND( ss_cl_boundary_probe_report,
	"ss_cl_boundary_probe_report -- engine-boundary call counts inside client.so" )
{
	Msg( "[SourceSharp] engine-boundary probe (client): map '%s'\n",
	     engine->GetLevelName() ? engine->GetLevelName() : "(none)" );

	for ( int id = 0; id < kBoundaryProbeCount; id++ )
	{
		char szLine[ 2048 ];
		BoundaryProbes().FormatLine( id, szLine, sizeof( szLine ) );
		Msg( "  %s\n", szLine );
	}
}

CON_COMMAND( ss_cl_boundary_probe_reset,
	"ss_cl_boundary_probe_reset -- zero client.so's engine-boundary counters" )
{
	BoundaryProbes().Reset();
	Msg( "[SourceSharp] engine-boundary probe (client): counters zeroed\n" );
}

// What is left of the stock client entity list. The bind assigns the shell's
// handle from the server's entry and serial; the rest answer nothing.
CBaseHandle SourceSharp_LegacyListBind( IClientUnknown *pUnknown, int nEntNum, int nSerialNum )
{
	if ( !pUnknown )
		return CBaseHandle();

	const CBaseHandle hEnt( nEntNum, nSerialNum );

	pUnknown->SetRefEHandle( hEnt );

	return hEnt;
}

void SourceSharp_LegacyListUnbind( const CBaseHandle &hEnt )
{
	// Both callers clear the handle themselves.
	(void)hEnt;
}

IClientUnknown *SourceSharp_LegacyListUnknownFromIndex( int nEntry )
{
	(void)nEntry;
	return NULL;
}

IClientUnknown *SourceSharp_LegacyListUnknownFromHandle( const CBaseHandle &hEnt )
{
	(void)hEnt;
	return NULL;
}
