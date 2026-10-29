//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The real props every standalone entity sends before its tier's payload,
// declared once for both sides: names and order (what the handshake pairs on)
// live here, and each side supplies only its own member offsets.
#pragma once
#include "generic_nettable.h"

// How a field goes on the wire.
enum StandaloneNetKind_t
{
	STANDALONE_NET_VECTOR = 0,
	STANDALONE_NET_INT,
	STANDALONE_NET_STRING,
};

struct StandaloneNetField_t
{
	// The name the handshake pairs on, spelled as the C++ member.
	const char *pName;

	StandaloneNetKind_t kind;

	// Bits for an INT, the buffer length for a STRING, ignored for a VECTOR (sent unscaled).
	int nBits;
};

// The fields in wire order. m_fEffects carries EF_NODRAW. m_ubInterpolationFrame
// is stock's teleport counter, which every later delta carries, unlike a one-shot
// bit. m_nTypeId (FNV-1a of the classname), m_nSchemaHash and m_nPayloadWords
// describe the payload; a schema hash that disagrees with the receiver's registry
// is refused. m_nSolidType and m_nSolidFlags let the client clip bullets against
// managed props. Vectors are unscaled: they carry physics results, and
// quantisation would keep a settled prop moving.
// NOINTERP_PARITY_MAX_BITS (shareddefs.h); shell_standalone_client.cpp asserts they agree.
enum
{
	STANDALONE_NOINTERP_PARITY_BITS = 2
};

// FSOLID_MAX_BITS (const.h); shell_standalone_client.cpp asserts they agree.
enum
{
	STANDALONE_SOLID_FLAGS_MAX_BITS = 10
};

// ANIMATION_SKIN_BITS (shareddefs.h).
enum
{
	STANDALONE_SKIN_BITS = 10
};

// ANIMATION_BODY_BITS (baseanimating.h).
enum
{
	STANDALONE_BODY_BITS = 32
};

inline const StandaloneNetField_t *StandaloneNetFields( int *pOutCount )
{
	static const StandaloneNetField_t s_Fields[] = {
		{ "m_vecOrigin", STANDALONE_NET_VECTOR, 0 },
		{ "m_angRotation", STANDALONE_NET_VECTOR, 0 },
		{ "m_nModelIndex", STANDALONE_NET_INT, MAX_EDICT_BITS },
		{ "m_fEffects", STANDALONE_NET_INT, EF_MAX_BITS },
		{ "m_ubInterpolationFrame", STANDALONE_NET_INT, STANDALONE_NOINTERP_PARITY_BITS },
		{ "m_nTypeId", STANDALONE_NET_INT, 32 },
		{ "m_nSchemaHash", STANDALONE_NET_INT, 32 },
		{ "m_nPayloadWords", STANDALONE_NET_INT, 16 },
		// The list's order is the wire format, so new rows go at the tail.
		// SolidType_t tops out at 7; the fourth bit leaves room.
		{ "m_nSolidType", STANDALONE_NET_INT, 4 },
		{ "m_nSolidFlags", STANDALONE_NET_INT, STANDALONE_SOLID_FLAGS_MAX_BITS },
		{ "m_nSkin", STANDALONE_NET_INT, STANDALONE_SKIN_BITS },
		{ "m_nBody", STANDALONE_NET_INT, STANDALONE_BODY_BITS },
	};

	if ( pOutCount )
		*pOutCount = ARRAYSIZE( s_Fields );

	return s_Fields;
}

// Each field's index, in the order above; each side supplies the member offsets.
enum
{
	STANDALONE_NET_FIELD_ORIGIN = 0,
	STANDALONE_NET_FIELD_ANGLES,
	STANDALONE_NET_FIELD_MODELINDEX,
	STANDALONE_NET_FIELD_EFFECTS,
	STANDALONE_NET_FIELD_INTERPFRAME,
	STANDALONE_NET_FIELD_TYPEID,
	STANDALONE_NET_FIELD_SCHEMAHASH,
	STANDALONE_NET_FIELD_PAYLOADWORDS,
	// Appended at the tail with their rows.
	STANDALONE_NET_FIELD_SOLIDTYPE,
	STANDALONE_NET_FIELD_SOLIDFLAGS,
	STANDALONE_NET_FIELD_SKIN,
	STANDALONE_NET_FIELD_BODY,

	STANDALONE_NET_FIELD_COUNT,
};

// Cast to int: gcc refuses to compare enumerators of two unnamed enums.
static_assert( (int)STANDALONE_NET_FIELD_COUNT == (int)SOURCESHARP_TIER_PROPS_STANDALONE,
			   "NetTierInfo's real-prop count is this list's length" );
