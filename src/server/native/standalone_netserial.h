//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the edict's network serial number ==================//
//
// This one exists because of an ss_barrel that aborted the server.
//
// The engine's delta machinery keys "what did I last send for this edict" by
// (edict index, edict->m_NetworkSerialNumber): SV_PackEntity asks
// CFrameSnapshotManager::GetPreviouslySentPacket for the PackedEntity it sent
// last time, and reuses its ChangeFrameList — the per-prop "last tick this
// changed" array — whenever the serial still matches. The serial is the ONLY
// thing telling the engine that an edict slot has changed occupants; the engine
// itself never bumps it. THE GAME writes it, one line in stock's
// CBaseEntity::SetRefEHandle (baseentity.cpp): every time the entity list hands
// an entity its handle, the handle's serial — which CBaseEntityList increments
// on every reuse of a slot — is mirrored into the edict, masked to the ten bits
// the wire carries.
//
// The parallel hierarchy's SetRefEHandle stored the handle and mirrored
// nothing. Every standalone edict carried whatever serial was left in it, so a
// freed edict reused by the next standalone entity LOOKED, to the engine, like
// the same entity it had been packing all along — and it reused the old
// occupant's ChangeFrameList for the new occupant's SendTable. With per-class
// tiers those tables honestly differ in size, and the engine's own guard caught
// the pair and aborted the process:
//
//     SV_PackEntity: SnagChangeFrameList mismatched number of props [38 vs 14]
//
// on dm_lockdown, from two console commands: ent_fire logic_relay Trigger
// frees a prop_physics (no published state: tier 0, 32 bytes, 14 props) and
// ent_create ss_barrel puts a tier-2 entity (84-byte BarrelNetState, 128 bytes,
// 38 props) into the freed edict. Two occupants of MATCHING size would not have
// aborted — they would have inherited each other's change ticks and delta
// baseline silently, which is worse.
//
// The rule is one masked store, and it lives here — engine-free, templated over
// the edict — because the failure it prevents reports nothing anywhere near the
// line that causes it: the abort is in engine.so, two entity lifetimes after
// the store that was missing. See standalone_netserial_test.cpp.
//
// TEN BITS is stock's NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS (const.h). It is
// restated here so this header needs no engine, and pinned to the real one by a
// COMPILE_TIME_ASSERT next to the caller in shell_standalone_base.cpp — the two
// drifting apart would put a serial on the edict that EHANDLE props truncate
// differently than the engine does.
//
//=============================================================================//
#pragma once

enum { STANDALONE_NETSERIAL_BITS = 10 };

//-----------------------------------------------------------------------------
// The serial the edict must publish for a handle the entity list assigned.
//
// Masked rather than truncated by the short: the mask is what stock applies,
// and it is what makes the value the engine compares tomorrow equal the value
// EHANDLE props put on the wire today.
//-----------------------------------------------------------------------------
inline int StandaloneNetSerial_ForHandle( unsigned long nHandleSerial )
{
	return (int)( nHandleSerial & ( ( 1u << STANDALONE_NETSERIAL_BITS ) - 1 ) );
}

//-----------------------------------------------------------------------------
// Mirror a freshly assigned handle serial onto the edict, if there is one.
//
// Templated over the edict so the test can hand a plain struct; the game hands
// edict_t. NULL is the server-only entity — no edict, nothing to publish, and
// nothing the engine will ever pack.
//
// Called for EVERY handle assignment, including the INVALID_EHANDLE the entity
// list assigns on removal — which writes a junk serial into an edict that is
// about to be freed, exactly as stock does, and is immediately overwritten by
// the next occupant's real serial before the engine can pack the slot again.
//-----------------------------------------------------------------------------
template < typename TEdict >
inline void StandaloneNetSerial_Mirror( TEdict *pEdict, unsigned long nHandleSerial )
{
	if ( !pEdict )
		return;

	pEdict->m_NetworkSerialNumber = (short)StandaloneNetSerial_ForHandle( nHandleSerial );
}
