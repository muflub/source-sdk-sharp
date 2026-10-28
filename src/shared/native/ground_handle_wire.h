//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the ground entity, as one networked word ===========//
//
// WHAT THE PLAYER IS STANDING ON IS PREDICTED STATE, and this header is how it
// travels: one int, shaped like stock's EHANDLE send prop
// (SendProxy_EHandleToInt in game/shared/sendproxy.cpp, undone by
// RecvProxy_IntToEHandle in game/client/recvproxy.cpp) — the entry index in the
// low MAX_EDICT_BITS, and one reserved all-ones value meaning "nothing".
//
// WHY IT IS PREDICTED AT ALL, because "FL_ONGROUND is already in the blob" is
// the reading that let this bug live. FL_ONGROUND is a MIRROR that
// SetGroundEntity keeps; the movement loop itself never reads the bit. Every
// branch that matters asks for the ENTITY — gamemovement.cpp:2475 refuses a
// jump when MvGetGroundEntity() is NULL, FullWalkMove picks WalkMove or
// AirMove by it (2157/2166), and the gravity zeroing at 2188 is the same ask.
// So a client that adopts the flags word from the acknowledged command while
// keeping a ground handle left over from a LATER replayed command holds a
// contradiction, and it holds it exactly at a ground-state transition: with
// FL_ONGROUND set and the handle NULL, the replay refuses the jump the server
// took, and one command later the whole jump velocity is missing.
//
// Stock carries the handle for this reason and says so in one line —
// c_baseplayer.cpp:405, DEFINE_PRED_FIELD( m_hGroundEntity, FIELD_EHANDLE,
// FTYPEDESC_INSENDTABLE ), beside the RecvPropEHandle at :240. This header is
// that line's equivalent for a player whose wire format is a C# declaration.
//
//=============================================================================
// THE INDEX AND NOT THE SERIAL, WHICH IS MEASURED AND NOT ASSUMED
//=============================================================================
//
// Stock's word carries ten bits of serial above the index, and the first
// version of this header did too — CBaseHandle::GetSerialNumber() masked to
// NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS on both sides, which is exactly what
// the send proxy does. It was wrong here, and the prediction visualiser said so
// in one line on the first run after it landed:
//
//     GroundEntity  pred 0 (idx 0 ser 0)   srv 606208 (idx 0 ser 296)
//
// ONE ENTITY — worldspawn, index 0, which is what a player standing on the map
// is standing on — and the two sides disagree by 296 on its serial. They do not
// share a serial counter: the server's is CBaseEntityList's, counting every
// reuse of the slot since the process started, and this build's client half
// simply has not counted the same reuses. So a serial in the word makes the
// SAME GROUND compare unequal on every single packet, which is the failure this
// field was added to remove, wearing the field's own name.
//
// So the word is the index alone, and the client resolves it through ITS OWN
// list (C_StandalonePlayer::AdoptServerState) and stores the handle that list
// hands back — serial included, correct for that side. What crosses the wire is
// the part the two sides agree on by construction; what each side keeps is its
// own valid handle.
//
// WHAT IS GIVEN UP is a recycled index: if the entity a player was standing on
// dies and a different one takes its slot in the same packet, the basis names
// the newcomer. That is bounded to one command — CategorizePosition re-derives
// the ground from a trace at the end of every command (gamemovement.cpp:2172)
// — where a serial that never matches is unbounded and permanent. Recorded in
// docs/divergences.md rather than left as a silent choice.
//
// ENGINE-FREE ON PURPOSE, like prediction_ring.h: ints
// in, ints out, no CBaseHandle and no const.h, so ground_handle_wire_test.cpp
// can compile it with nothing but gtest. The callers own the CBaseHandle
// plumbing, and a COMPILE_TIME_ASSERT beside them pins the bit widths restated
// below to the engine's own.
//
//=============================================================================//
#pragma once

//-----------------------------------------------------------------------------
// Stock's numbers, restated so this header needs no engine. const.h:65, :87 and
// :89 are the originals; the COMPILE_TIME_ASSERTs live next to the callers,
// where const.h is in scope.
//-----------------------------------------------------------------------------
enum
{
	GROUNDWIRE_INDEX_BITS  = 11,  // MAX_EDICT_BITS
	GROUNDWIRE_SERIAL_BITS = 10,  // NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS

	GROUNDWIRE_INDEX_MASK  = ( 1 << GROUNDWIRE_INDEX_BITS ) - 1,

	// INVALID_NETWORKED_EHANDLE_VALUE. Kept as the "nothing" word rather than
	// something narrower now that the serial field is unused, for two reasons:
	// it is stock's own spelling of no-entity, and it is NOT ZERO — a blob that
	// has never been written would otherwise read as "standing on entity 0",
	// which is the world, which is the most plausible-looking wrong answer this
	// file could produce.
	GROUNDWIRE_NONE = ( 1 << ( GROUNDWIRE_INDEX_BITS + GROUNDWIRE_SERIAL_BITS ) ) - 1,
};

//-----------------------------------------------------------------------------
// An entity index -> the word. Anything that is not a usable index is
// "nothing", so a caller holding an invalid handle does not have to remember to
// check: an out-of-range index cannot encode to a plausible neighbour.
//-----------------------------------------------------------------------------
inline int SourceSharpGroundWire_Encode( int nEntryIndex )
{
	if ( nEntryIndex < 0 || nEntryIndex > GROUNDWIRE_INDEX_MASK )
		return GROUNDWIRE_NONE;

	return nEntryIndex;
}

//-----------------------------------------------------------------------------
// The word -> an entity index. False means "no ground", and on that answer the
// output is written anyway (index -1) so a caller that ignores the return value
// still cannot end up standing on entity 0.
//
// A word outside the encodable range — a blob that never resolved, a field read
// at the wrong offset — is "nothing" too, for the same reason: the alternative
// is silently standing on whichever entity the low bits name.
//-----------------------------------------------------------------------------
inline bool SourceSharpGroundWire_Decode( int nEncoded, int *pEntryIndex )
{
	if ( pEntryIndex )
		*pEntryIndex = -1;

	if ( nEncoded < 0 || nEncoded > GROUNDWIRE_INDEX_MASK )
		return false;

	if ( pEntryIndex )
		*pEntryIndex = nEncoded;

	return true;
}
