//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The edict's network serial number. The engine reuses an edict's previous
// ChangeFrameList while the serial matches, and only the game writes it (stock's
// CBaseEntity::SetRefEHandle); a stale serial makes a new occupant inherit the
// old one's delta state.
#pragma once

// Stock's NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS; asserted equal beside the caller.
enum
{
	STANDALONE_NETSERIAL_BITS = 10
};

inline int StandaloneNetSerial_ForHandle( unsigned long nHandleSerial )
{
	return (int)( nHandleSerial & ( ( 1u << STANDALONE_NETSERIAL_BITS ) - 1 ) );
}

// Mirrors a newly assigned handle serial onto the edict. A null edict is a
// server-only entity. Runs for INVALID_EHANDLE on removal too, as stock does.
template <typename TEdict>
inline void StandaloneNetSerial_Mirror( TEdict *pEdict, unsigned long nHandleSerial )
{
	if ( !pEdict )
		return;

	pEdict->m_NetworkSerialNumber = (short)StandaloneNetSerial_ForHandle( nHandleSerial );
}
