//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
//
// The engine-facing half of entity_slots.h. C# binds an entity's blob once and then
// writes through it, marking a dirty word; once a frame the dirty entities get one
// NetworkStateChanged each. Reads mark nothing, so untouched entities skip encoding.

#include "shell_standalone.h"

#include "world_subject.h"
#include "ss_world.h"

#include "native_command_body.h"
#include "server_state.h"

// The block sizes itself without const.h so the gtest needs no engine; a block shorter
// than the entity list would refuse its tail.
COMPILE_TIME_ASSERT( SOURCESHARP_MAX_ENTITY_SLOTS >= NUM_ENT_ENTRIES );

void *SourceSharp_EntityGetDataBlob( uint32_t hEntity, int32_t *pOutSize );

// Both tables live in the DLL tier (server_state.h) from DLLInit to the end of
// DLLShutdown, after managed unload, so pointers C# holds stay valid. NULL outside it.
static CSourceSharpEntitySlots *EntitySlots()
{
	CServerDllState *pDll = ShellGameDll_Dll();
	return pDll ? &pDll->m_EntitySlots : NULL;
}

static CSourceSharpSlotTable *SlotTable()
{
	CServerDllState *pDll = ShellGameDll_Dll();
	return pDll ? &pDll->m_SlotTable : NULL;
}

// The entry index a packed handle names, or -1. ENT_ENTRY_MASK is wider than the entity
// list, so the mask alone can name a slot that does not exist.
static int SourceSharp_SlotEntryOf( uint32_t hEntity )
{
	if ( hEntity == (uint32_t)INVALID_EHANDLE_INDEX )
		return -1;

	const CBaseHandle handle = CBaseHandle::UnsafeFromIndex( (int)hEntity );
	const int nEntry = handle.GetEntryIndex();

	return CSourceSharpEntitySlots::IsEntry( nEntry ) ? nEntry : -1;
}

// Marks an entity's networked state as moved.
static bool SourceSharp_MarkStateChanged( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->NetworkStateChanged();
		return true;
	}

	return false;
}

// The one crossing per bound state: the blob, its size, a pointer to the slot and the
// slot's serial. Slots never move for the life of the DLL tier. NULL, size 0 and serial
// 0 for anything with no blob.
void *SourceSharp_EntityBindBlob( uint32_t hEntity, int32_t *pOutSize, SourceSharpEntitySlot_t **pOutSlot,
								  uint32_t *pOutSerial )
{
	if ( pOutSize )
		*pOutSize = 0;
	if ( pOutSlot )
		*pOutSlot = NULL;
	if ( pOutSerial )
		*pOutSerial = SOURCESHARP_SLOT_SERIAL_NONE;

	const int nEntry = SourceSharp_SlotEntryOf( hEntity );
	CSourceSharpEntitySlots *pSlots = EntitySlots();

	if ( nEntry < 0 || !pSlots )
	{
		return NULL;
	}

	int32_t nSize = 0;
	void *pBlob = SourceSharp_EntityGetDataBlob( hEntity, &nSize );

	if ( !pBlob || nSize <= 0 )
	{
		return NULL;
	}

	uint32_t nSerial = SOURCESHARP_SLOT_SERIAL_NONE;

	if ( !pSlots->Open( nEntry, hEntity, &nSerial ) )
	{
		return NULL;
	}

	if ( pOutSize )
		*pOutSize = nSize;
	if ( pOutSlot )
		*pOutSlot = pSlots->Slots() + nEntry;
	if ( pOutSerial )
		*pOutSerial = nSerial;

	return pBlob;
}

// Closes an entity's slot, from the destructor of every shell that carries a blob,
// before the handle is torn down and the memory freed. A slot left open would let C#
// write into freed memory with a matching serial.
void SourceSharp_EntitySlotClosed( uint32_t hEntity )
{
	const int nEntry = SourceSharp_SlotEntryOf( hEntity );
	CSourceSharpEntitySlots *pSlots = EntitySlots();

	if ( nEntry >= 0 && pSlots )
		pSlots->Close( nEntry );
}

// Once per frame: turns the frame's stores into NetworkStateChanged calls and closes
// slots whose entity died without closing them. Walks backwards because Close swaps the
// tail entry into the freed position.
int SourceSharp_DrainEntitySlots()
{
	int nMarked = 0;
	CSourceSharpEntitySlots *pSlots = EntitySlots();
	if ( !pSlots )
		return 0;

	for ( int i = pSlots->ActiveCount() - 1; i >= 0; --i )
	{
		const int nEntry = pSlots->ActiveAt( i );
		const uint32_t hEntity = pSlots->HandleAt( nEntry );

		const bool bDirty = pSlots->TakeDirty( nEntry );

		if ( bDirty )
		{
			if ( SourceSharp_MarkStateChanged( hEntity ) )
			{
				++nMarked;
				continue;
			}

			// Dirty and gone: a shell freed an entity without closing its slot.
			Warning( "[SourceSharp] entity slot %d was written and its entity is gone; "
					 "a shell is freeing without closing its slot\n",
					 nEntry );
		}
		else if ( !SourceSharp_ResolveEntity( hEntity ).IsValid() )
		{
			pSlots->Close( nEntry );
			continue;
		}
	}

	return nMarked;
}

// A managed reload is about to happen: void every serial handed out so far. Called
// between unload and load, the only window with no managed code running.
int SourceSharp_RetireEntitySlots()
{
	CSourceSharpEntitySlots *pSlots = EntitySlots();
	return pSlots ? pSlots->Retire() : 0;
}

// Level shutdown: a slot that outlived the map would be handed to the next occupant.
void SourceSharp_ResetEntitySlots()
{
	CSourceSharpEntitySlots *pSlots = EntitySlots();
	CSourceSharpSlotTable *pTable = SlotTable();
	if ( pSlots )
		pSlots->Reset();

	// ClearBindings, not Reset: it bumps serials instead of rewinding them. The engine keeps
	// edict_t::m_NetworkSerialNumber across a map load and reuses packed state when the
	// serial matches, so a rewound serial would pack a new entity as the old one.
	if ( pTable )
		pTable->ClearBindings();
}

// The slot table: shell pointer and serial per entry, so an engine index or handle
// becomes the concrete shell without RTTI. It shares this file with the control block
// above because they share a lifetime.

// For the debug readout; console commands run inside the DLL tier's lifetime.
CSourceSharpSlotTable &SourceSharp_SlotTable()
{
	CSourceSharpSlotTable *pTable = SlotTable();
	AssertMsg( pTable, "the slot table was asked for outside DLLInit..DLLShutdown" );
	return *pTable;
}

// The handle a shell at this entry will carry, issued without binding. The shell's
// SetRefEHandle binds it (SourceSharp_SlotBindAt), which mirrors the edict serial.
//-----------------------------------------------------------------------------
CBaseHandle SourceSharp_SlotIssue( int nEntry )
{
	CSourceSharpSlotTable *pTable = SlotTable();
	return pTable ? pTable->IssueHandle( nEntry ) : CBaseHandle();
}

// Binds a handle assigned elsewhere.
bool SourceSharp_SlotBindAt( const CBaseHandle &handle, CStandaloneEntity *pShell, edict_t *pEdict )
{
	CSourceSharpSlotTable *pTable = SlotTable();
	if ( !pTable || !pTable->BindAt( handle, pShell ) )
		return false;

	StandaloneNetSerial_Mirror( pEdict, (unsigned long)handle.GetSerialNumber() );
	return true;
}

// Called from the shell's destructor before the handle is torn down. Keyed by entry, so
// an entity whose handle is already invalid can still unbind.
//-----------------------------------------------------------------------------
void SourceSharp_SlotUnbind( int nEntry )
{
	if ( CSourceSharpSlotTable *pTable = SlotTable() )
		pTable->Unbind( nEntry );
}

// The one permitted downcast on an entity path; tools/entity-gates-allowlist names this
// cast by line. Only CStandaloneEntity pointers are ever bound, and Resolve checks the
// serial before any pointer is returned.
CStandaloneEntity *SourceSharp_ShellFromHandle( const CBaseHandle &handle )
{
	CSourceSharpSlotTable *pTable = SlotTable();
	return pTable ? static_cast<CStandaloneEntity *>( pTable->Resolve( handle ) ) : NULL;
}

// By entry index (the engine's route, no serial), through the handle resolver so there
// is only one cast site.
CStandaloneEntity *SourceSharp_ShellFromIndex( int nEntry )
{
	CSourceSharpSlotTable *pTable = SlotTable();
	return pTable ? SourceSharp_ShellFromHandle( pTable->HandleOf( nEntry ) ) : NULL;
}

// No cast: GetRefEHandle is virtual, and a foreign IHandleEntity's handle fails the
// serial compare.
CStandaloneEntity *SourceSharp_ShellFromHandleEntity( IHandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return NULL;

	return SourceSharp_ShellFromHandle( pHandleEntity->GetRefEHandle() );
}

// A handle to whatever holds it: the slot table's shells, then the world. The two are
// disjoint, so the order is one of cost only.
IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle )
{
	if ( !handle.IsValid() )
		return NULL;

	if ( CStandaloneEntity *pShell = SourceSharp_ShellFromHandle( handle ) )
		return static_cast<IHandleEntity *>( static_cast<IServerEntity *>( pShell ) );

	if ( CWorld *pWorld = SourceSharp_WorldFromHandle( handle ) )
		return static_cast<IHandleEntity *>( static_cast<IServerEntity *>( pWorld ) );

	// No other population remains.
	return NULL;
}
