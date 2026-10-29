//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The player's ground entity as one networked int, for prediction: the movement
// code asks for the ground entity, not FL_ONGROUND. Only the entry index travels,
// because the two sides' serials differ; each side resolves it through its own
// entity list. Engine-free, so it can be tested.
#pragma once

// Stock's widths from const.h; the callers assert them against the originals.
enum
{
	GROUNDWIRE_INDEX_BITS = 11, // MAX_EDICT_BITS
	GROUNDWIRE_SERIAL_BITS = 10, // NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS

	GROUNDWIRE_INDEX_MASK = ( 1 << GROUNDWIRE_INDEX_BITS ) - 1,

	// INVALID_NETWORKED_EHANDLE_VALUE. Not zero, which would read as the world.
	GROUNDWIRE_NONE = ( 1 << ( GROUNDWIRE_INDEX_BITS + GROUNDWIRE_SERIAL_BITS ) ) - 1,
};

// An entity index to the word; anything out of range encodes as nothing.
inline int SourceSharpGroundWire_Encode( int nEntryIndex )
{
	if ( nEntryIndex < 0 || nEntryIndex > GROUNDWIRE_INDEX_MASK )
		return GROUNDWIRE_NONE;

	return nEntryIndex;
}

// The word to an entity index. False means no ground, and *pEntryIndex is then -1.
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
