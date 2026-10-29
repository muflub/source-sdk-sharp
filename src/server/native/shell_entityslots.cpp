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
#include "server_network.h"
#include "server_entity_resolver.h"
#include "server_entity_context.h"

// The block sizes itself without const.h so the gtest needs no engine; a block shorter
// than the entity list would refuse its tail.
COMPILE_TIME_ASSERT(SOURCESHARP_MAX_ENTITY_SLOTS >= NUM_ENT_ENTRIES);

// The entry index a packed handle names, or -1. ENT_ENTRY_MASK is wider than the entity
// list, so the mask alone can name a slot that does not exist.
static int SourceSharp_SlotEntryOf(uint32_t hEntity)
{
	if (hEntity == (uint32_t)INVALID_EHANDLE_INDEX)
		return -1;

	const CBaseHandle handle = CBaseHandle::UnsafeFromIndex((int)hEntity);
	const int nEntry = handle.GetEntryIndex();

	return CSourceSharpEntitySlots::IsEntry(nEntry) ? nEntry : -1;
}

// Marks an entity's networked state as moved.
static bool SourceSharp_MarkStateChanged(CServerEntityResolver& entities, uint32_t hEntity)
{
	const SourceSharpEntity_t ent = entities.Resolve(hEntity);

	if (ent.pStandalone)
	{
		ent.pStandalone->NetworkStateChanged();
		return true;
	}

	return false;
}

// The one crossing per bound state: the blob, its size, a pointer to the slot and the
// slot's serial. Slots never move for the life of the DLL tier. NULL, size 0 and serial
// 0 for anything with no blob.
void* CServerNetwork::EntityBindBlob(uint32_t hEntity, int32_t* pOutSize, SourceSharpEntitySlot_t** pOutSlot,
									 uint32_t* pOutSerial)
{
	if (pOutSize)
		*pOutSize = 0;
	if (pOutSlot)
		*pOutSlot = NULL;
	if (pOutSerial)
		*pOutSerial = SOURCESHARP_SLOT_SERIAL_NONE;

	const int nEntry = SourceSharp_SlotEntryOf(hEntity);
	CSourceSharpEntitySlots* pSlots = &m_Slots;

	if (nEntry < 0)
	{
		return NULL;
	}

	int32_t nSize = 0;
	void* pBlob = EntityGetDataBlob(hEntity, &nSize);

	if (!pBlob || nSize <= 0)
	{
		return NULL;
	}

	uint32_t nSerial = SOURCESHARP_SLOT_SERIAL_NONE;

	if (!pSlots->Open(nEntry, hEntity, &nSerial))
	{
		return NULL;
	}

	if (pOutSize)
		*pOutSize = nSize;
	if (pOutSlot)
		*pOutSlot = pSlots->Slots() + nEntry;
	if (pOutSerial)
		*pOutSerial = nSerial;

	return pBlob;
}

// Closes an entity's slot, from the destructor of every shell that carries a blob,
// before the handle is torn down and the memory freed. A slot left open would let C#
// write into freed memory with a matching serial.
void SourceSharp_EntitySlotClosed(CSourceSharpEntitySlots& slots, uint32_t hEntity)
{
	const int nEntry = SourceSharp_SlotEntryOf(hEntity);

	if (nEntry >= 0)
		slots.Close(nEntry);
}

// Once per frame: turns the frame's stores into NetworkStateChanged calls and closes
// slots whose entity died without closing them. Walks backwards because Close swaps the
// tail entry into the freed position.
int SourceSharp_DrainEntitySlots(const CServerEntityContext& context)
{
	int nMarked = 0;
	CSourceSharpEntitySlots* pSlots = &context.m_EntitySlots;
	CServerEntityResolver& entities = context.Resolver();

	for (int i = pSlots->ActiveCount() - 1; i >= 0; --i)
	{
		const int nEntry = pSlots->ActiveAt(i);
		const uint32_t hEntity = pSlots->HandleAt(nEntry);

		const bool bDirty = pSlots->TakeDirty(nEntry);

		if (bDirty)
		{
			if (SourceSharp_MarkStateChanged(entities, hEntity))
			{
				++nMarked;
				continue;
			}

			// Dirty and gone: a shell freed an entity without closing its slot.
			Warning("[SourceSharp] entity slot %d was written and its entity is gone; "
					"a shell is freeing without closing its slot\n",
					nEntry);
		}
		else if (!entities.Resolve(hEntity).IsValid())
		{
			pSlots->Close(nEntry);
			continue;
		}
	}

	return nMarked;
}

// A managed reload is about to happen: void every serial handed out so far. Called
// between unload and load, the only window with no managed code running.
int SourceSharp_RetireEntitySlots(CSourceSharpEntitySlots& slots)
{
	return slots.Retire();
}

// Level shutdown: a slot that outlived the map would be handed to the next occupant.
void SourceSharp_ResetEntitySlots(CSourceSharpEntitySlots& slots, CSourceSharpSlotTable& table)
{
	slots.Reset();

	// ClearBindings, not Reset: it bumps serials instead of rewinding them. The engine keeps
	// edict_t::m_NetworkSerialNumber across a map load and reuses packed state when the
	// serial matches, so a rewound serial would pack a new entity as the old one.
	table.ClearBindings();
}

// The slot table: shell pointer and serial per entry, so an engine index or handle
// becomes the concrete shell without RTTI. It shares this file with the control block
// above because they share a lifetime.

// The handle a shell at this entry will carry, issued without binding. The shell's
// SetRefEHandle binds it (SourceSharp_SlotBindAt), which mirrors the edict serial.
//-----------------------------------------------------------------------------
CBaseHandle SourceSharp_SlotIssue(CSourceSharpSlotTable& table, int nEntry)
{
	return table.IssueHandle(nEntry);
}

// Binds a handle assigned elsewhere.
bool SourceSharp_SlotBindAt(CSourceSharpSlotTable& table, const CBaseHandle& handle, CStandaloneEntity* pShell,
							edict_t* pEdict)
{
	if (!table.BindAt(handle, pShell))
		return false;

	StandaloneNetSerial_Mirror(pEdict, (unsigned long)handle.GetSerialNumber());
	return true;
}

// Called from the shell's destructor before the handle is torn down. Keyed by entry, so
// an entity whose handle is already invalid can still unbind.
//-----------------------------------------------------------------------------
void SourceSharp_SlotUnbind(CSourceSharpSlotTable& table, int nEntry)
{
	table.Unbind(nEntry);
}
