//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — what a standalone entity networks, declared once ===//
//
// A SendTable and its RecvTable are two hand-written lists that must agree, and
// the engine pairs them BY NAME during the connect handshake. A prop the server
// sends that the client has no slot for is not a missing value — it is a
// bitstream that stops making sense partway through, and the symptom is a client
// that begins joining and stops with nothing in any log.
//
// This project has been bitten by two hand-written lists disagreeing twice
// already: the ammo table's ordering, and the ABI mirror, which drifted once on
// a version constant and once on two transposed slots. Both now have a parity
// test. This has something better than a test — it has one list.
//
// The FIELDS are declared here, in order, with their names and encoding. What
// each side supplies is only the OFFSET of each field within its own class,
// because CStandaloneEntity and C_StandaloneEntity are genuinely different
// objects and always will be. Order and names — everything the handshake pairs
// on — cannot drift, because there is only one copy of them.
//
// STEP 6 REPLACED WHAT THIS DESCRIBES, and left the mechanism alone.
//
// There are no per-class tables any more. Every entity on the hierarchy is
// created through one of the tier ServerClasses in generic_nettable.h, and the
// list below is the REAL props that sit in front of that tier's payload — three
// values the C++ shell genuinely needs in members, and three that say which
// managed type the payload belongs to.
//
// m_szClassname is gone from it. It was 64 bytes carrying a string the far side
// already had a copy of; m_nTypeId is four carrying FNV-1a of the same string,
// and Part 6's objection to an index — "a second thing the two sides must agree
// the ordering of" — does not apply to a hash, because there is no ordering.
//
//=============================================================================//
#ifndef SOURCESHARP_STANDALONE_NETTABLE_H
#define SOURCESHARP_STANDALONE_NETTABLE_H
#ifdef _WIN32
#pragma once
#endif
#include "generic_nettable.h"

//-----------------------------------------------------------------------------
// How a field goes on the wire. Deliberately a small set: this describes what
// entities on the parallel hierarchy actually send, not everything a SendProp
// can express.
//-----------------------------------------------------------------------------
enum StandaloneNetKind_t
{
	STANDALONE_NET_VECTOR = 0,
	STANDALONE_NET_INT,
	STANDALONE_NET_STRING,
};

struct StandaloneNetField_t
{
	// The name the handshake pairs on. It must match the C++ member so anyone
	// reading a packet dump can find it, which is why these are spelled with
	// the member's own m_ prefix.
	const char         *pName;

	StandaloneNetKind_t kind;

	// Bits for an INT, the buffer length for a STRING, ignored for a VECTOR
	// (which is sent unscaled — see the note on the table below).
	int                 nBits;
};

//-----------------------------------------------------------------------------
// Everything an entity on the hierarchy sends BEFORE its payload, in wire
// order. Ten fields, seven of them things and three of them labels:
//
//   m_vecOrigin     where it is.
//   m_angRotation   which way it is facing. Not needed until the props arrived:
//                   a tumbling crate that sends only its origin slides around
//                   the floor perfectly upright, which reads as a physics bug
//                   and is a missing SendProp.
//   m_nModelIndex   what it looks like. NOT the visibility switch: step 8e-render
//                   took that job off it, because "invisible" and "shapeless"
//                   were the same state and the client cannot ask a shapeless
//                   collideable whether a point is inside it (7a measured two
//                   func_dust brushes arriving with ModelIndex 0 forever).
//   m_fEffects      the EF_* bits, which is where EF_NODRAW lives — stock's
//                   own visibility switch, on stock's own CBaseEntity SendProp,
//                   in the EngineBlockHead.Effects word that already existed.
//                   EF_MAX_BITS wide and unsigned, exactly as
//                   SendPropInt( SENDINFO( m_fEffects ), EF_MAX_BITS,
//                   SPROP_UNSIGNED ) sends it on a stock entity.
//   m_ubInterpolationFrame
//                   THAT IT JUMPED, rather than moved. Stock's own name and
//                   stock's own encoding: NOINTERP_PARITY_MAX_BITS wide and
//                   unsigned, exactly as baseentity.cpp:320 sends it. Two bits
//                   of a counter, not a flag -- a "do not interpolate" BIT is
//                   only true for the one snapshot that carries it, so a client
//                   whose delta was computed against a later tick never sees it
//                   and the entity slides anyway with nothing to say so. A
//                   counter is in every delta after the jump, and the client
//                   compares it against the value it last acted on
//                   (EngineBlockHead.OldInterpolationFrame). It is stock's
//                   reason for retiring EF_NOINTERP, which c_baseentity.h:1402
//                   still records in its comment.
//
//                   It is a THING and not a label because the client shell needs
//                   it in the block: the interpolation pass is managed code
//                   reading the block directly (plan_entity.md 3.9), and an
//                   entity with no payload at all -- which is most of them --
//                   still teleports.
//
// These seven stay TYPED rather than moving into the payload, and Part 5 says
// why: the client-side shell needs them in real members for interpolation,
// rendering and collision to read, and they get the engine's specialised
// coordinate encoding on the values that update every tick. m_fEffects joins
// them for the same reason and no other: C_StandaloneEntity::ShouldDraw and the
// managed render policy behind it are read per draw, so the bits have to be in
// the block rather than in a payload a class may not even declare. They are also not
// the part of the schema that changes when a C# type is reshaped, which is what
// the payload exists to absorb.
//
//   m_nTypeId       WHICH managed class it is: FNV-1a of the classname, four
//                   bytes where the string was sixty-four. Every entity on the
//                   hierarchy shares its tier's ServerClass, so this is the
//                   only thing telling them apart, and the client picks its
//                   managed class by looking it up in the published type table.
//   m_nSchemaHash   the fingerprint of the layout the payload was written
//                   with. The receiving side compares it against its own
//                   registry's and REFUSES the bytes on a disagreement rather
//                   than decoding one build's memory image through another
//                   build's field offsets.
//   m_nPayloadWords how many words of the payload are meaningful. It does not
//                   truncate the bitstream — every word of the tier exists for
//                   the life of the class — but it is what makes a genuinely
//                   variable-length payload possible later, and it is a second
//                   opinion about the type that can be checked against the
//                   first.
//   m_nSolidType    whether a bullet stops in it. The client's impact clip
//                   asks the prop's shell GetSolid and dispatches on the
//                   answer, and a client that was never told it reads
//                   SOLID_NONE: the bullet passes through every managed prop,
//                   the trace never hits, and the custom impact effect is
//                   gated behind exactly that hit. The server's SetSolidity
//                   already writes this block word and marks it changed;
//                   until this row existed, nothing on the wire carried it.
//                   Four bits: SolidType_t tops out at SOLID_LAST == 7
//                   (const.h:236-245), so three would fit today and the fourth
//                   is the room an added solid type gets without a wire
//                   change. Unsigned like every INT row here.
//   m_nSolidFlags   WHICH solid it is: FSOLID_* bits, FSOLID_MAX_BITS wide —
//                   stock's own width for them (const.h:262), where the
//                   highest set bit is FSOLID_TRIGGER_TOUCH_DEBRIS at 1<<9.
//                   FSOLID_NOT_SOLID on its own is how managed says "no
//                   shape", so the pair travels together; a flags byte
//                   without the type would let the client clip a
//                   SOLID_NONE prop as if the type said otherwise.
//                   FSOLID_CUSTOMRAYTEST / FSOLID_CUSTOMBOXTEST inside it are
//                   what route stock's TestCollision gate to TestHitboxes, so
//                   a client that cannot see the flags cannot run the gate.
//
// Unscaled vectors, because these carry physics results rather than player
// positions: a prop that settles is compared against where the map put it, and
// quantisation there shows up as a prop that never quite stops moving.
//-----------------------------------------------------------------------------
// NOINTERP_PARITY_MAX_BITS (game/shared/shareddefs.h, 2), named here so this
// header needs no game/ header: server TUs on the R5 include set include it.
// shell_standalone_client.cpp, which still sees shareddefs.h, asserts the two
// agree, so a change to either stops the client build.
enum { STANDALONE_NOINTERP_PARITY_BITS = 2 };

//-----------------------------------------------------------------------------
// FSOLID_MAX_BITS (public/const.h, the SolidFlags_t enum at :262 — the highest
// set bit is FSOLID_TRIGGER_TOUCH_DEBRIS at 1<<9), named here so this header
// needs no public/ header on its own: the shell TUs see const.h through their
// game headers, and the engine-free test binaries that compile this list do
// not. shell_standalone_client.cpp asserts the two agree, so a change to
// either stops the client build — the NOINTERP row above is the precedent.
enum { STANDALONE_SOLID_FLAGS_MAX_BITS = 10 };

// ANIMATION_SKIN_BITS (game/shared/shareddefs.h), named here for the same
// reason as the two above.
enum { STANDALONE_SKIN_BITS = 10 };

inline const StandaloneNetField_t *StandaloneNetFields( int *pOutCount )
{
	static const StandaloneNetField_t s_Fields[] =
	{
		{ "m_vecOrigin",     STANDALONE_NET_VECTOR, 0              },
		{ "m_angRotation",   STANDALONE_NET_VECTOR, 0              },
		{ "m_nModelIndex",   STANDALONE_NET_INT,    MAX_EDICT_BITS },
		{ "m_fEffects",      STANDALONE_NET_INT,    EF_MAX_BITS    },
		{ "m_ubInterpolationFrame",
		                     STANDALONE_NET_INT,    STANDALONE_NOINTERP_PARITY_BITS },
		{ "m_nTypeId",       STANDALONE_NET_INT,    32             },
		{ "m_nSchemaHash",   STANDALONE_NET_INT,    32             },
		{ "m_nPayloadWords", STANDALONE_NET_INT,    16             },
		// The solidity PAIR, appended at the TAIL rather than beside the block
		// fields they are words of: the order of this list IS the wire format,
		// so an inserted row would renumber every prop after it, while a row
		// added at the end leaves all eight existing slots — and every client
		// built against them — untouched. The server's SetSolidity already
		// writes and marks both block words; until these rows existed nothing
		// on the wire carried them, the client's GetSolid answered
		// SOLID_NONE for every managed prop, and its impact clip passed
		// straight through. Four bits: SolidType_t tops out at SOLID_LAST == 7
		// (const.h:236-245), so three would fit today and the fourth is the
		// room an added solid type gets without another wire change.
		{ "m_nSolidType",    STANDALONE_NET_INT,    4               },
		// WHICH solid it is, at stock's own width for the FSOLID_* bits
		// (FSOLID_MAX_BITS, const.h:262 — the highest set bit is
		// FSOLID_TRIGGER_TOUCH_DEBRIS at 1<<9). The pair travels together:
		// FSOLID_NOT_SOLID alone is managed's "no shape", and
		// FSOLID_CUSTOMRAYTEST / FSOLID_CUSTOMBOXTEST inside it are what route
		// stock's TestCollision gate to TestHitboxes — a client that cannot
		// see the flags cannot run the gate.
		{ "m_nSolidFlags",   STANDALONE_NET_INT,    STANDALONE_SOLID_FLAGS_MAX_BITS },
		// THE SKIN, at the tail for the same wire-order reason. Stock sends
		// CBaseAnimating's m_nSkin at ANIMATION_SKIN_BITS (10); a skin is never
		// negative, so it travels unsigned like every INT row here. Both sides
		// address the engine block's Client.Skin, which is what the client's
		// GetSkin answers: a skin the SERVER picks (env_beverage's can, a gib)
		// reached no client before this row (plan_server_native_v2, 2026-10-19).
		{ "m_nSkin",         STANDALONE_NET_INT,    STANDALONE_SKIN_BITS },
	};

	if ( pOutCount )
		*pOutCount = ARRAYSIZE( s_Fields );

	return s_Fields;
}

// What each side must supply, in the same order as the fields above: the offset
// of each member within ITS OWN class. The two classes are different objects,
// so the offsets differ; nothing else about the table does.
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
	// Appended with the fields, for the same wire-order reason.
	STANDALONE_NET_FIELD_SOLIDTYPE,
	STANDALONE_NET_FIELD_SOLIDFLAGS,
	// Appended with its row.
	STANDALONE_NET_FIELD_SKIN,

	STANDALONE_NET_FIELD_COUNT,
};
// Both sides are enumerators of two different unnamed enums, which gcc's
// -Wenum-compare (an error here) refuses to compare as enums; as ints the
// assertion says the same thing.
static_assert( (int)STANDALONE_NET_FIELD_COUNT == (int)SOURCESHARP_TIER_PROPS_STANDALONE,
               "NetTierInfo's real-prop count is this list's length" );

#endif // SOURCESHARP_STANDALONE_NETTABLE_H
