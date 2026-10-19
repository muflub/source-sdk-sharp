//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the temp entity description, and its arithmetic =====//
//
// plan_tmp.md Part 3 and Part 9, gate 2. A temp entity is a TABLE and a byte
// blob: the engine's PlaybackTempEntity takes a `const void *pSender` and a
// SendTable and never asks what class the sender is, so every prop reads at its
// own declared offset and nothing here needs a C++ object per effect.
//
// NO ENGINE HEADERS HERE, on purpose, and it is the same argument
// generic_payload.h makes for the same reason. Three things about a temp entity
// can be quietly wrong and none of them reports itself:
//
//   * an OFFSET outside the blob, which networks whatever is beyond it;
//   * a WIDTH one bit narrower than the other side expects, which is a
//     bitstream that stops making sense partway through and produces no log
//     line at all;
//   * a worst case over the engine's 192-byte event cap, which is a
//     Host_Error in the middle of a firefight rather than an error at startup.
//
// All three are arithmetic over the description, so all three live here where a
// gtest can reach them with no running Source. See
// sourcesharp/test/tempent_schema_test.cpp.
//
// The only include is the ABI header, which is itself pure C and knows nothing
// about Source; the structs it declares are the description. The table BUILDING
// is in shell_tempents.cpp, which is the only part that needs dt_send.h.
//
//=============================================================================//
#ifndef SOURCESHARP_TEMPENT_SCHEMA_H
#define SOURCESHARP_TEMPENT_SCHEMA_H
#ifdef _WIN32
#pragma once
#endif

#include "sourcesharp.h"

#include <string.h>
#include <stdlib.h>

#include <set>
#include <string>

//=============================================================================//
// CONSTANTS THIS HEADER RESTATES, AND THE PARITY CHECK THAT KEEPS THEM HONEST.
//
// Each one belongs to a header this file deliberately does not include. A
// restated constant that drifts is exactly the failure docs/writing-tests.md
// records under "a fixture that stands in for native must be checked against
// it" — so tempent_schema_test.cpp READS the owning headers and asserts the
// agreement rather than trusting these lines.
//=============================================================================//

//-----------------------------------------------------------------------------
// The engine's per-event data cap, 192 bytes — which is NOT in the
// SDK, so this is the one constant with no header to check against. Overflow is
// a Host_Error, not a dropped effect: the engine calls SendTable_Encode into a
// fixed 192-byte buffer and dies if the encoder runs past it.
//
// DT_TEBeamSpline (16 fixed COORD vectors) and DT_EffectData (31 props) are the
// two tables anywhere near it, and both are priced at REGISTRATION — where the
// answer is a console line at startup — rather than at playback.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_TE_MAX_EVENT_BYTES = 192 };
enum { SOURCESHARP_TE_MAX_EVENT_BITS  = SOURCESHARP_TE_MAX_EVENT_BYTES * 8 };

//-----------------------------------------------------------------------------
// public/coordsize.h. These decide what SPROP_COORD and SPROP_NORMAL cost, and
// therefore whether a table fits.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_TE_COORD_INTEGER_BITS               = 14 };
enum { SOURCESHARP_TE_COORD_FRACTIONAL_BITS            =  5 };
enum { SOURCESHARP_TE_COORD_INTEGER_BITS_MP            = 11 };
enum { SOURCESHARP_TE_COORD_FRACTIONAL_BITS_MP_LOWPREC =  3 };
enum { SOURCESHARP_TE_NORMAL_FRACTIONAL_BITS           = 11 };

//-----------------------------------------------------------------------------
// The symbolic widths of plan 3.1, resolved from the headers that own them.
// public/const.h, game/server/baseanimating.h, game/shared/shareddefs.h and
// game/shared/effect_dispatch_data.h respectively.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_TE_MAX_EDICT_BITS         = 11 };  // MAX_EDICT_BITS
enum { SOURCESHARP_TE_MODEL_INDEX_BITS       = 13 };  // SP_MODEL_INDEX_BITS
enum { SOURCESHARP_TE_EF_MAX_BITS            = 10 };  // EF_MAX_BITS
enum { SOURCESHARP_TE_ANIM_SKIN_BITS         = 10 };  // ANIMATION_SKIN_BITS
enum { SOURCESHARP_TE_EFFECT_FLAG_BITS       =  8 };  // MAX_EFFECT_FLAG_BITS
enum { SOURCESHARP_TE_EFFECT_STRING_BITS     = 10 };  // MAX_EFFECT_DISPATCH_STRING_BITS
enum { SOURCESHARP_TE_MAX_PLAYERS            = 33 };  // MAX_PLAYERS, the non-TF/non-CS branch
enum { SOURCESHARP_TE_EHANDLE_BITS           = 21 };  // NUM_NETWORKED_EHANDLE_BITS

//-----------------------------------------------------------------------------
// What one prop costs in INDEX bits before its value.
//
// CDeltaBitsWriter::WritePropIndex does not write an index, it writes the gap
// from the last index written, and charges 7 bits for a gap of 16 or less —
// generic_payload.h Part 1B says so and prices the whole payload design on it.
// Every prop of a temp entity is written on every send (there is no previous
// tick to delta against, 2B-bis), so every gap is 1 and every prop pays 7.
//
// An UPPER BOUND, deliberately. Under-pricing here is a Host_Error at playback;
// over-pricing is a table this side declines to register and says why.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_TE_PROP_INDEX_BITS = 7 };

//-----------------------------------------------------------------------------
// The message's own framing, before any prop: the class id and the delta
// terminator the encoder writes around the prop run. Bounded generously for the
// same reason the index cost is.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_TE_MESSAGE_OVERHEAD_BITS = 32 };

//-----------------------------------------------------------------------------
// How many classes and fields the registry will hold. 44 classes is the whole
// port (plan 0.2b) and DT_EffectData's 31 props is the widest table; both have
// headroom, and both refuse rather than overflow.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_TE_MAX_CLASSES = 64 };
enum { SOURCESHARP_TE_MAX_FIELDS  = 64 };

//-----------------------------------------------------------------------------
// Q_log2 without tier1: how many bits it takes to hold [0, n].
//-----------------------------------------------------------------------------
inline int SourceSharpTe_BitsFor( int nValue )
{
	int nBits = 0;

	while ( nValue > 0 )
	{
		nBits++;
		nValue >>= 1;
	}

	return nBits;
}

//-----------------------------------------------------------------------------
// A symbolic width, resolved. Answers -1 for a kind this does not know, which
// every caller refuses rather than substituting a plausible number: a width
// nobody chose is exactly the failure the symbols exist to prevent.
//
// MAX_PLAYERS_LOG2 is Q_log2( MAX_PLAYERS ), which is what
// SendPropInt( SENDINFO( m_nPlayer ), Q_log2( MAX_PLAYERS ), SPROP_UNSIGNED )
// spells in te_playerdecal.cpp.
//
// Q_log2(33) is FIVE, and the arithmetic below deliberately reads as though it
// were six. Both halves are worth spelling out, because the tempting "fix" here
// puts m_nPlayer one bit wide of stock's RecvTable, which is the failure with
// no log line.
//
//   Q_log2 is a right-shift loop (mathlib_base.cpp:480), so it is FLOOR of
//   log2, not the number of bits: log2(33) is 5.04, and Q_log2(33) = 5.
//   BitsFor(33) is 6 -- 33 is 0b100001 and needs six bits to hold. Floor of
//   log2 is one less than that for every value that is not an exact power of
//   two, which is why this is BitsFor(...) - 1 and not BitsFor(...).
//
// MAX_PLAYERS is 33 here: shareddefs.h:251-257 gives 65 under CSTRIKE_DLL and
// 101 under TF_DLL/HL2MP, and this build defines none of them (plan_tmp.md 2G).
// The live engine dump reads back 5 bits for m_nPlayer, which is the only
// check of this that is not a computation compared with itself.
//-----------------------------------------------------------------------------
inline int SourceSharpTe_ResolveBits( int nBitsFrom, int nLiteralBits )
{
	switch ( nBitsFrom )
	{
	case SOURCESHARP_TE_BITS_LITERAL:          return nLiteralBits;
	case SOURCESHARP_TE_BITS_MAX_EDICT:        return SOURCESHARP_TE_MAX_EDICT_BITS;
	case SOURCESHARP_TE_BITS_MODEL_INDEX:      return SOURCESHARP_TE_MODEL_INDEX_BITS;
	case SOURCESHARP_TE_BITS_EF_MAX:           return SOURCESHARP_TE_EF_MAX_BITS;
	case SOURCESHARP_TE_BITS_ANIM_SKIN:        return SOURCESHARP_TE_ANIM_SKIN_BITS;
	case SOURCESHARP_TE_BITS_MAX_PLAYERS_LOG2: return SourceSharpTe_BitsFor( SOURCESHARP_TE_MAX_PLAYERS ) - 1;
	case SOURCESHARP_TE_BITS_EFFECT_FLAG:      return SOURCESHARP_TE_EFFECT_FLAG_BITS;
	case SOURCESHARP_TE_BITS_EFFECT_STRING:    return SOURCESHARP_TE_EFFECT_STRING_BITS;
	case SOURCESHARP_TE_BITS_EHANDLE:          return SOURCESHARP_TE_EHANDLE_BITS;
	}

	return -1;
}

//-----------------------------------------------------------------------------
// The bits ONE FLOAT costs at its worst.
//
// The branch order is the engine encoder's own — COORD before COORD_MP before
// NOSCALE before NORMAL before the clamped-range form — and the order is
// load-bearing, not cosmetic. SendPropFloat ORs SPROP_NOSCALE into any prop
// declared with nBits <= 0 (dt_send.cpp:274), so stock's
//
//     SendPropFloat( SENDINFO_VECTORELEM( m_vecOrigin, 0 ), -1, SPROP_COORD )
//
// arrives here carrying BOTH flags. Reading NOSCALE first would price it at 32
// bits where the engine spends at most 22, and DT_TEBeamSpline — which is the
// one table this function exists to judge — would be refused for a size it does
// not have.
//
// Every arm is the worst case of the bf_write function the engine calls:
//
//   WriteBitCoord      1 + 1 + 1 + 14 + 5   (bitbuf.cpp:619)
//   WriteBitCoordMP    3 + 14 + 5           float, out of the MP bounds
//   ... low precision  3 + 14 + 3
//   ... integral       3 + 14
//   WriteBitFloat      32
//   WriteBitNormal     1 + 11               (bitbuf.cpp:674)
//-----------------------------------------------------------------------------
inline int SourceSharpTe_FloatWireBits( int nBits, int nFlags )
{
	if ( nFlags & SOURCESHARP_TE_FLAG_COORD )
		return 3 + SOURCESHARP_TE_COORD_INTEGER_BITS + SOURCESHARP_TE_COORD_FRACTIONAL_BITS;

	if ( nFlags & SOURCESHARP_TE_FLAG_COORD_MP )
		return 3 + SOURCESHARP_TE_COORD_INTEGER_BITS + SOURCESHARP_TE_COORD_FRACTIONAL_BITS;

	if ( nFlags & SOURCESHARP_TE_FLAG_COORD_MP_LOWPRECISION )
		return 3 + SOURCESHARP_TE_COORD_INTEGER_BITS + SOURCESHARP_TE_COORD_FRACTIONAL_BITS_MP_LOWPREC;

	if ( nFlags & SOURCESHARP_TE_FLAG_COORD_MP_INTEGRAL )
		return 3 + SOURCESHARP_TE_COORD_INTEGER_BITS;

	if ( nFlags & SOURCESHARP_TE_FLAG_NOSCALE )
		return 32;

	if ( nFlags & SOURCESHARP_TE_FLAG_NORMAL )
		return 1 + SOURCESHARP_TE_NORMAL_FRACTIONAL_BITS;

	// The clamped-range form. A width of 32 or less that nobody narrowed is a
	// whole float; SendPropFloat turns nBits <= 0 into NOSCALE before it ever
	// reaches here, so this arm only sees a width somebody chose.
	return ( nBits > 0 && nBits <= 32 ) ? nBits : 32;
}

//-----------------------------------------------------------------------------
// A vector's worst case. SPROP_NORMAL sends x and y and ONE SIGN BIT for z,
// which the receiver reconstructs — the reason a normal is cheaper than
// three floats rather than three times one.
//-----------------------------------------------------------------------------
inline int SourceSharpTe_VectorWireBits( int nBits, int nFlags )
{
	const int nPer = SourceSharpTe_FloatWireBits( nBits, nFlags );

	return ( nFlags & SOURCESHARP_TE_FLAG_NORMAL ) ? ( 2 * nPer + 1 ) : ( 3 * nPer );
}

//-----------------------------------------------------------------------------
// How many PROPS one field becomes.
//
// One for most of them. N for a field that expands the way SENDINFO_VECTORELEM
// and SENDINFO_ARRAYELEM do — a Vector as three floats, a QAngle as three
// angles, a colour as three bytes. An ARRAY is a PAIR (the element template and
// the DPT_Array prop, plan 3.3) but only one of the two is ever encoded, so it
// pays one index.
//-----------------------------------------------------------------------------
inline int SourceSharpTe_FieldPropCount( const SourceSharpTeField_t *pField )
{
	if ( !pField )
		return 0;

	if ( pField->nKind == SOURCESHARP_TE_PROP_ARRAY
	  || pField->nKind == SOURCESHARP_TE_PROP_DATATABLE )
		return 1;

	return ( pField->nElems > 1 ) ? pField->nElems : 1;
}

//-----------------------------------------------------------------------------
// What ONE of a field's props costs in value bits, worst case. -1 for a shape
// this cannot price, which every caller refuses rather than substituting a
// plausible number.
//
// A DATATABLE prop costs nothing itself: what it nests is priced by walking the
// nested table, which the registry below does.
//-----------------------------------------------------------------------------
inline int SourceSharpTe_PropValueBits( const SourceSharpTeField_t *pField )
{
	if ( !pField )
		return -1;

	const int nBits = SourceSharpTe_ResolveBits( pField->nBitsFrom, pField->nBits );

	switch ( pField->nKind )
	{
	case SOURCESHARP_TE_PROP_INT:
		return ( nBits > 0 && nBits <= 32 ) ? nBits : -1;

	case SOURCESHARP_TE_PROP_FLOAT:
	case SOURCESHARP_TE_PROP_ANGLE:
		return SourceSharpTe_FloatWireBits( nBits, pField->nFlags );

	case SOURCESHARP_TE_PROP_VECTOR:
	case SOURCESHARP_TE_PROP_QANGLES:
		return SourceSharpTe_VectorWireBits( nBits, pField->nFlags );

	case SOURCESHARP_TE_PROP_DATATABLE:
		return 0;

	case SOURCESHARP_TE_PROP_ARRAY:
		{
			if ( pField->nElems <= 0 )
				return -1;

			// The element encoding, times the count, plus the length the engine
			// writes for the array itself. With a NULL length proxy every
			// element goes every time (plan 3.3), which is why this is one of
			// the two tables priced against the engine's event cap at all.
			//
			// THE ELEMENT IS A VECTOR, and that is a statement about the
			// inventory rather than a limitation this cannot lift: there is
			// exactly ONE array in all 44 tables — DT_TEBeamSpline's
			// m_vecPoints, sixteen COORD vectors — and giving the description an
			// element-kind knob that only ever holds one value would be a knob
			// nobody reads. A second array kind adds the knob and this comment
			// goes with it.
			const int nElem = SourceSharpTe_VectorWireBits( nBits, pField->nFlags );

			return pField->nElems * nElem + SourceSharpTe_BitsFor( pField->nElems );
		}
	}

	return -1;
}

//-----------------------------------------------------------------------------
// The whole field's value bits: one prop's cost times however many props it
// becomes. -1 propagates.
//-----------------------------------------------------------------------------
inline int SourceSharpTe_FieldValueBits( const SourceSharpTeField_t *pField )
{
	const int nOne = SourceSharpTe_PropValueBits( pField );

	if ( nOne < 0 )
		return -1;

	return nOne * SourceSharpTe_FieldPropCount( pField );
}

//-----------------------------------------------------------------------------
// How many bytes of blob a field occupies, from its kind and declared storage.
//
// Answers the DECLARED size for the kinds that carry one and the natural size
// for the rest, so that a field whose storage disagrees with its kind is caught
// here rather than by reading three bytes past the end of a byte.
//-----------------------------------------------------------------------------
inline int SourceSharpTe_FieldBlobBytes( const SourceSharpTeField_t *pField )
{
	if ( !pField )
		return -1;

	int nOne = -1;

	switch ( pField->nKind )
	{
	case SOURCESHARP_TE_PROP_INT:
		nOne = ( pField->nSizeBytes == 1 || pField->nSizeBytes == 2 || pField->nSizeBytes == 4 )
			? pField->nSizeBytes : -1;
		break;

	case SOURCESHARP_TE_PROP_FLOAT:
	case SOURCESHARP_TE_PROP_ANGLE:
		nOne = ( pField->nSizeBytes == 4 ) ? 4 : -1;
		break;

	case SOURCESHARP_TE_PROP_VECTOR:
	case SOURCESHARP_TE_PROP_QANGLES:
		nOne = ( pField->nSizeBytes == 12 ) ? 12 : -1;
		break;

	case SOURCESHARP_TE_PROP_DATATABLE:
		// The nested table's own fields carry the bytes; the prop itself is a
		// pointer shift of zero.
		return 0;

	case SOURCESHARP_TE_PROP_ARRAY:
		return ( pField->nElems > 0 && pField->nElemStride > 0 )
			? pField->nElems * pField->nElemStride : -1;
	}

	if ( nOne < 0 )
		return -1;

	const int nProps = SourceSharpTe_FieldPropCount( pField );

	if ( nProps == 1 )
		return nOne;

	// A stride shorter than the element itself would overlap the props, which
	// reads one field's bytes as another's — refused rather than trusted,
	// because the value that comes out of it is plausible.
	if ( pField->nElemStride < nOne )
		return -1;

	return ( nProps - 1 ) * pField->nElemStride + nOne;
}


//=============================================================================//
// THE PROP PLAN — one description, one prop sequence, BOTH SIDES.
//
// ADDED BY C-PHASE 1, and it is the one place this lane extends a header
// S-Phase 1 owns. Saying so loudly, because the brief asks: the client needed
// something the server did not, and it is not a field on the description.
//
// The server had ONE builder walking a description into SendProps, so the
// expansion rule — the leading "baseclass" prop, one field becoming three
// props named "m_vecOrigin[0..2]", the negated offset a VECTOR_ELEM declares
// with, the array's element-template-then-DPT_Array pair — lived inside that
// walk and was true by construction. The moment a SECOND builder exists on the
// client, that rule has two implementations, and generic_nettable.h's
// NetTierInfo already records what happens next: "a rule with two
// implementations drifts". The symptom here is the worst one in this whole
// port — a client whose prop list differs from the server's by one name or one
// offset begins joining and stops, with nothing in any log.
//
// So the rule moved DOWN here, where neither side owns it and a gtest can read
// it with no engine anywhere. Each side still builds its own SendProp/RecvProp
// out of a plan entry, because those constructors are the part that genuinely
// differs; what they may no longer disagree about is WHICH props exist, in what
// order, at what offsets, with what widths.
//
// The name is a BUFFER and not a pointer, deliberately. "m_vecOrigin[1]" does
// not exist anywhere until it is derived, and a SendProp keeps the const char *
// it is handed for the life of the process — so an engine-free header cannot
// hand out a pointer that lives long enough. Each side interns the text into
// its own table, which is what it did before.
//=============================================================================//

enum { SOURCESHARP_TE_PROP_NAME_MAX = 128 };

//-----------------------------------------------------------------------------
// The baseclass prop's name, which is stock's own and not a choice.
// BEGIN_SEND_TABLE / BEGIN_RECV_TABLE both spell it exactly this way
// (dt_send.h:588, dt_recv.h:307), and the client pairs the two by string.
//-----------------------------------------------------------------------------
#define SOURCESHARP_TE_BASECLASS_PROP "baseclass"

typedef struct
{
	char        szName[ SOURCESHARP_TE_PROP_NAME_MAX ];

	int32_t     nKind;            // SOURCESHARP_TE_PROP_*; ARRAY is the DPT_Array prop itself
	int32_t     nOffset;          // into the blob, ALWAYS POSITIVE -- what the CLIENT declares with

	// What the SEND side hands its prop constructor, which is -nOffset for a
	// vector elem. SEND ONLY, and the asymmetry is stock's rather than ours:
	// SENDINFO_VECTORELEM negates (dt_send.h:631) so SendTable_InitTable can
	// detect it and set SPROP_IS_A_VECTOR_ELEM, while RECVINFO(m_vecOrigin[0])
	// is a plain offsetof and the recv side never negates anything. A client
	// that declared with this would decode into memory before the blob.
	int32_t     nDeclareOffset;
	int32_t     nSizeBytes;
	int32_t     nBits;            // RESOLVED — no symbol survives into a plan
	int32_t     nFlags;           // SPROP_* to pass on, with VECTOR_ELEM already stripped
	float       flLow;
	float       flHigh;
	int32_t     nElems;           // ARRAY only: the DPT_Array's element count
	int32_t     nElemStride;      // ARRAY only: its stride
	const char *pSubTable;        // DATATABLE only: the table nested here, by name

	// THE ELEMENT TEMPLATE OF AN ARRAY, which the two sides NAME DIFFERENTLY.
	// Like nDeclareOffset above, the asymmetry is stock's rather than ours:
	//
	//   server  SendPropVector( SENDINFO_ARRAY(m_vecPoints), -1, SPROP_COORD )
	//   client  RecvPropVector( RECVINFO(m_vecPoints[0]) )
	//
	// SENDINFO_ARRAY stringises the array (dt_send.h:622) and gives the
	// template the BARE name; RECVINFO stringises whatever it is handed
	// (dt_recv.h) and stock hands it `m_vecPoints[0]`, so the template gets the
	// INDEXED name. Only the DPT_Array prop that follows is named the same on
	// both sides, because both spell it `#arrayName`.
	//
	// IT IS NOT COSMETIC AND IT COST A LIVE JOIN. The recv side resolves a
	// server prop to a client prop BY NAME; with the template named
	// `m_vecPoints` as well, the lookup for the DPT_Array prop finds the
	// DPT_Vector template first and the client dies with
	//
	//   RecvProp type doesn't match server type for DT_TEBeamSpline/m_vecPoints
	//   Host_EndGame: CL_ParseClassInfo_EndClasses: CreateDecoders failed.
	//
	// which is a REFUSED JOIN, not a warning. The send side never looks a prop
	// up by name, which is why the bare template name is harmless there and why
	// the server half of this port has been green with it since S-Phase 1.
	// Seen live by lane CN, the first lane to put an array on the managed
	// client; SourceSharpTe_RecvPropName below is the whole fix.
	int32_t     bArrayElement;

	// Which description field this came from, and which of its elements.
	// -1 for the leading baseclass prop, which belongs to no field.
	int32_t     iField;
	int32_t     iElem;
} SourceSharpTePropPlan_t;

//-----------------------------------------------------------------------------
// "m_vecOrigin" or "m_vecOrigin[1]", into a caller's buffer.
//-----------------------------------------------------------------------------
inline void SourceSharpTe_PropName( char *pOut, int nOutBytes, const char *pBase,
                                    int iElem, int bExpanded )
{
	if ( !pOut || nOutBytes <= 0 )
		return;

	pOut[0] = '\0';

	if ( !pBase )
		return;

	int nAt = 0;

	while ( pBase[ nAt ] && nAt < nOutBytes - 1 )
	{
		pOut[ nAt ] = pBase[ nAt ];
		nAt++;
	}

	if ( bExpanded && nAt < nOutBytes - 1 )
	{
		// Decimal, one or two digits — stock never expands a field past three
		// elements and DT_ArrayElementNameForIdx would be a different spelling
		// for a different thing. snprintf is deliberately not used: this header
		// is included by a gtest that links nothing.
		char szDigits[ 12 ];
		int  nDigits = 0;
		int  nValue  = ( iElem < 0 ) ? 0 : iElem;

		do
		{
			szDigits[ nDigits++ ] = (char)( '0' + ( nValue % 10 ) );
			nValue /= 10;
		}
		while ( nValue > 0 && nDigits < (int)sizeof( szDigits ) );

		if ( nAt < nOutBytes - 1 )
			pOut[ nAt++ ] = '[';

		while ( nDigits > 0 && nAt < nOutBytes - 1 )
			pOut[ nAt++ ] = szDigits[ --nDigits ];

		if ( nAt < nOutBytes - 1 )
			pOut[ nAt++ ] = ']';
	}

	pOut[ nAt ] = '\0';
}

//-----------------------------------------------------------------------------
// The pool a side interns its prop names in. A SendProp/RecvProp keeps the
// name pointer it is handed for the life of the table, so the pool OWNS the
// copies and a name's pointer never moves: a std::set node is never relocated,
// so c_str() is stable until the set itself is destroyed. Case-sensitive, as
// the strcmp walk it replaced was; the same name twice is the same pointer.
//-----------------------------------------------------------------------------
typedef std::set< std::string > SourceSharpTePropNamePool_t;

inline const char *SourceSharpTe_InternPropName( SourceSharpTePropNamePool_t &pool, const char *pName )
{
	return pool.emplace( pName ).first->c_str();
}

//-----------------------------------------------------------------------------
// Every prop of one description, in the order both sides must emit them.
// Answers how many, or -1 for a description that cannot be built.
//
// THE BASE GOES FIRST, which is where BEGIN_SEND_TABLE puts it and therefore
// where the client's own table has it. A base emitted anywhere else still pairs
// by name, but a table that differs from stock's in ANY way is a table somebody
// has to argue about when a join goes wrong (plan 2E).
//
// AN ARRAY IS TWO PROPS OF ONE NAME (plan 3.3): the element template, then the
// DPT_Array carrying the count and the stride. m_pArrayProp is left NULL on
// both sides and the engine links it to the preceding prop in
// SendTable_InitTable / the recv equivalent. Reproduce that shape exactly —
// emit both, link neither.
//
// "OF ONE NAME" IS THE SEND SIDE'S STATEMENT. The recv side names the element
// template after its first element (`m_vecPoints[0]`), which is stock's own
// asymmetry between SENDINFO_ARRAY and RECVINFO and is not optional: two recv
// props of one name make CreateDecoders resolve the DPT_Array to the DPT_Vector
// and refuse the join. SourceSharpTe_RecvPropName above is where that lives.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The name the RECV side declares a planned prop under.
//
// The same string as the send side for every prop in all 44 tables EXCEPT one:
// the element template of an array, which stock spells `m_vecPoints[0]` on the
// client and `m_vecPoints` on the server. See bArrayElement on the plan struct
// for the whole reason, and for what a client that gets this wrong does (it
// refuses the join in CreateDecoders and takes the whole connection with it).
//
// DERIVED HERE, IN THE SHARED HEADER, rather than in the client's builder,
// because the two sides pair props by string and a derivation only one of them
// can see is a derivation the other cannot be checked against.
//
// The index is 0 and not iElem: stock names the template after the FIRST
// element, and there is only ever one template however many elements follow it.
//-----------------------------------------------------------------------------
inline void SourceSharpTe_RecvPropName( char *pOut, int nOutBytes,
                                        const SourceSharpTePropPlan_t *pPlan )
{
	if ( !pOut || nOutBytes <= 0 )
		return;

	pOut[0] = '\0';

	if ( !pPlan )
		return;

	SourceSharpTe_PropName( pOut, nOutBytes, pPlan->szName, 0, pPlan->bArrayElement );
}

inline int SourceSharpTe_PlanProps( const SourceSharpTeClass_t *pClass,
                                    SourceSharpTePropPlan_t *pOut, int nRoom )
{
	if ( !pClass || !pOut || nRoom < 0 )
		return -1;

	int nOut = 0;

	if ( pClass->pBaseTable && pClass->pBaseTable[0] )
	{
		if ( nOut >= nRoom )
			return -1;

		SourceSharpTePropPlan_t *pPlan = &pOut[ nOut++ ];
		memset( pPlan, 0, sizeof( *pPlan ) );

		SourceSharpTe_PropName( pPlan->szName, (int)sizeof( pPlan->szName ),
		                        SOURCESHARP_TE_BASECLASS_PROP, 0, 0 );

		pPlan->nKind     = SOURCESHARP_TE_PROP_DATATABLE;
		pPlan->pSubTable = pClass->pBaseTable;
		pPlan->iField    = -1;
		pPlan->iElem     = 0;
	}

	for ( int f = 0; f < pClass->nFieldCount; f++ )
	{
		const SourceSharpTeField_t *pField = &pClass->pFields[f];

		if ( !pField->pName || !pField->pName[0] )
			return -1;

		if ( pField->nKind == SOURCESHARP_TE_PROP_DATATABLE )
		{
			if ( nOut >= nRoom || !pField->pSubTable || !pField->pSubTable[0] )
				return -1;

			SourceSharpTePropPlan_t *pPlan = &pOut[ nOut++ ];
			memset( pPlan, 0, sizeof( *pPlan ) );

			SourceSharpTe_PropName( pPlan->szName, (int)sizeof( pPlan->szName ),
			                        pField->pName, 0, 0 );

			pPlan->nKind     = SOURCESHARP_TE_PROP_DATATABLE;
			pPlan->nOffset   = pField->nOffset;
			pPlan->nDeclareOffset = pField->nOffset;
			pPlan->pSubTable = pField->pSubTable;
			pPlan->iField    = f;
			pPlan->iElem     = 0;
			continue;
		}

		const int nBits = SourceSharpTe_ResolveBits( pField->nBitsFrom, pField->nBits );

		// A symbol this build does not know is refused rather than substituted.
		// A width nobody chose is exactly the failure the symbols exist to
		// prevent.
		if ( nBits < 0 && pField->nBitsFrom != SOURCESHARP_TE_BITS_LITERAL )
			return -1;

		// The flag is an INSTRUCTION to negate, not a flag to pass on: the
		// engine sets SPROP_IS_A_VECTOR_ELEM itself, and handing it through
		// would tell the engine about a prop that is not one.
		const int  nFlags  = pField->nFlags & ~SOURCESHARP_TE_FLAG_VECTOR_ELEM;
		const int  bNegate = ( pField->nFlags & SOURCESHARP_TE_FLAG_VECTOR_ELEM ) != 0;
		const int  nStride = ( pField->nElemStride > 0 ) ? pField->nElemStride : 0;

		if ( pField->nKind == SOURCESHARP_TE_PROP_ARRAY )
		{
			if ( nOut + 2 > nRoom || pField->nElems <= 0 || pField->nElemStride <= 0 )
				return -1;

			// The element TEMPLATE, which is a vector — the one array in all 44
			// tables is DT_TEBeamSpline's sixteen COORD vectors, and
			// SourceSharpTe_PropValueBits prices it on exactly that assumption.
			// A second array kind adds an element-kind knob to both.
			SourceSharpTePropPlan_t *pElem = &pOut[ nOut++ ];
			memset( pElem, 0, sizeof( *pElem ) );

			SourceSharpTe_PropName( pElem->szName, (int)sizeof( pElem->szName ),
			                        pField->pName, 0, 0 );

			pElem->nKind          = SOURCESHARP_TE_PROP_VECTOR;
			pElem->nOffset        = pField->nOffset;
			pElem->nDeclareOffset = bNegate ? -pField->nOffset : pField->nOffset;
			pElem->nSizeBytes     = pField->nSizeBytes;
			pElem->nBits          = nBits;
			pElem->nFlags         = nFlags;
			pElem->flLow          = pField->flLow;
			pElem->flHigh         = pField->flHigh;
			pElem->iField         = f;
			pElem->iElem          = 0;

			// The one bit the two sides read differently: see bArrayElement's
			// comment on the plan struct. The plan carries stock's SEND name
			// and the client derives stock's RECV name from it, so both sides
			// stay one description.
			pElem->bArrayElement  = 1;

			// Then the DPT_Array itself, of the same name.
			SourceSharpTePropPlan_t *pArray = &pOut[ nOut++ ];
			memset( pArray, 0, sizeof( *pArray ) );

			SourceSharpTe_PropName( pArray->szName, (int)sizeof( pArray->szName ),
			                        pField->pName, 0, 0 );

			pArray->nKind       = SOURCESHARP_TE_PROP_ARRAY;
			pArray->nOffset     = pField->nOffset;
			pArray->nDeclareOffset = pField->nOffset;
			pArray->nElems      = pField->nElems;
			pArray->nElemStride = pField->nElemStride;
			pArray->iField      = f;
			pArray->iElem       = 0;
			continue;
		}

		const int nProps  = SourceSharpTe_FieldPropCount( pField );
		const int bExpand = nProps > 1;

		if ( nOut + nProps > nRoom )
			return -1;

		for ( int e = 0; e < nProps; e++ )
		{
			const int nOffset = pField->nOffset + e * nStride;

			SourceSharpTePropPlan_t *pPlan = &pOut[ nOut++ ];
			memset( pPlan, 0, sizeof( *pPlan ) );

			SourceSharpTe_PropName( pPlan->szName, (int)sizeof( pPlan->szName ),
			                        pField->pName, e, bExpand );

			pPlan->nKind          = pField->nKind;
			pPlan->nOffset        = nOffset;

			// The one place the negation cannot be reproduced is an element at
			// blob offset zero: -0 is 0, the engine sees no negative, and that
			// prop alone carries no SPROP_IS_A_VECTOR_ELEM. Stock has the same
			// hole and never falls into it, because a CBaseTempEntity's vtable
			// pointer is at offset 0 and a member never is. No decode path
			// reads the flag — the encoder branches on COORD/NOSCALE/NORMAL
			// and nothing else — so the difference is cosmetic, and both dumps
			// print it rather than leaving it to be discovered.
			pPlan->nDeclareOffset = bNegate ? -nOffset : nOffset;

			pPlan->nSizeBytes     = pField->nSizeBytes;
			pPlan->nBits          = nBits;
			pPlan->nFlags         = nFlags;
			pPlan->flLow          = pField->flLow;
			pPlan->flHigh         = pField->flHigh;
			pPlan->iField         = f;
			pPlan->iElem          = e;
		}
	}

	return nOut;
}

//-----------------------------------------------------------------------------
// How many props a description becomes, counted rather than guessed.
//
// A SHORT ARRAY HERE IS A BUFFER OVERRUN into whatever the allocator put next,
// and every prop past the end still builds — so both sides ask this before
// allocating rather than reserving a number that looks big enough.
//-----------------------------------------------------------------------------
inline int SourceSharpTe_PlanCount( const SourceSharpTeClass_t *pClass )
{
	if ( !pClass )
		return -1;

	int nWanted = ( pClass->pBaseTable && pClass->pBaseTable[0] ) ? 1 : 0;

	for ( int f = 0; f < pClass->nFieldCount; f++ )
	{
		nWanted += ( pClass->pFields[f].nKind == SOURCESHARP_TE_PROP_ARRAY )
			? 2 : SourceSharpTe_FieldPropCount( &pClass->pFields[f] );
	}

	return nWanted;
}

//=============================================================================//
// WHICH SIDE MAY OWN A TABLE, AND THE ONE CONSTRAINT THAT DECIDES IT.
//
// Nothing here is code. It is the finding two live joins paid for, written
// where the next lane will look for it, because it is the rule that says which
// temp entities C-Phase 3 can port and in what order.
//
// THE FACT: the client resolves a nested data table BY NAME, not by the pointer
// its own recv prop carries. So two RecvTables of one name cannot coexist in a
// client, and whichever the engine finds first is used for BOTH -- for stock's
// class and for ours. Measured twice on dm_lockdown:
//
//   ours found first -- a ClientClass built at ClientEarlyInit lands at the head
//     of a push-front list, so ours was found for STOCK's CTEParticleSystem.
//     Stock's own copy then never received a decoder and the first dereference
//     of it was a SEGFAULT inside the engine with no client frame on the stack,
//     during CreateDecoders, right after "The server is using sv_pure";
//
//   stock's found first -- WORSE, because it JOINED. CTESparks decoded its own
//     props correctly and its BASE's props through stock's offsets, which for a
//     C_TEParticleSystem put m_vecOrigin at byte 32 of a 32-byte blob. A spark
//     fired at (-2932, 3218, 60) read back (0,0,0), and the write went past the
//     end of the blob. Nothing anywhere reported either.
//
// THE SERVER HAS NO SUCH PROBLEM, and that asymmetry is why S-Phase 1's join
// was green with two SendTables of one name registered: a ServerClass reaches
// its table by POINTER and the server never resolves one of its own tables by
// name. "A duplicate table name is proven harmless" is a statement about the
// SERVER ONLY.
//
// AND THE FIX IS NOT A RENAME. Building the managed copy under a different name
// was tried and broke the beams. The server is shared by BOTH client
// implementations at once -- the managed one for whatever C-Phase 3 has
// evicted, the stock one for everything else -- and it has only one name per
// table. DT_TEParticleSystem is nested by CTESparks (managed client) and by
// Explosion, LargeFunnel, BloodStream, ShowLine, GaussExplosion and Dust (all
// still stock); DT_BaseBeam by seven beams whose client halves are native.
// Renaming server side produced, on the first join:
//
//     Missing RecvProp for DT_TEBeamEntPoint - SS_DT_BaseBeam/m_nModelIndex
//
// which is plan 2E arriving as a symptom: the set of registered names, table
// names, prop lists and nesting must be exactly stock's. SERVER TABLE NAMES ARE
// STOCK'S, ALWAYS.
//
// SO THE CONSTRAINT LANDS ENTIRELY ON THE CLIENT, and it is this:
//
//     A TABLE MAY MOVE TO THE MANAGED CLIENT ONLY WHEN EVERY NATIVE USER OF IT
//     HAS BEEN EVICTED.
//
// For a concrete class that is one file: evict its c_te_*.cpp and the name is
// free. For a BASE table it is every derived class at once --
// DT_TEParticleSystem has seven client users and DT_BaseBeam has seven -- so
// those two move LAST, and until they do, nothing that nests them can be
// received in managed code either.
//
// Which is why C-Phase 1 does NOT prove itself with CTESparks, even though the
// server has sent it from managed code since S-Phase 1: CTESparks nests
// DT_TEParticleSystem, six of whose users are still native. A NOBASE table has
// no such chain and moves on its own, today, under stock's exact name with
// nothing nesting anything -- DT_TEEnergySplash, DT_TEMetalSparks,
// DT_TEBloodSprite and DT_TEBeamSpline are the four, and the first of them is
// what C-Phase 1 ends with.
//
// shell_tempents_client.cpp REFUSES rather than trusting this comment: a
// managed table whose name a native RecvTable already carries is not
// registered, and says so. That is the loud version of the segfault above, and
// it is what makes the other forty-two safe while their client halves are still
// stock's.
//=============================================================================//

//=============================================================================//
// THE REGISTRY.
//
// It holds the descriptions, refuses the ones that cannot be right, and answers
// "which class is this" for the dump and for playback. It is engine-free so
// that the two refusals worth having — a DUPLICATE NAME and a LATE
// REGISTRATION — are gtest-reachable, because neither can be seen from inside
// a running game: a duplicate class name makes the client begin joining and
// stop with nothing in any log (plan 2E), and a late registration produces no
// symptom at all until the first effect fires into a table that was never
// flattened.
//
// It COPIES what it is handed. The descriptions come from managed memory that
// the GC may move or free, while a SendProp keeps the const char * it was given
// for the life of the process.
//=============================================================================//

//-----------------------------------------------------------------------------
// Whether a base table this registry does not hold exists ANYWAY — because
// stock's own class of that name is still registered and its SendTable is the
// one the derived table nests.
//
// A function pointer rather than a base class, so the gtest can answer for a
// world with no engine in it and shell_tempents.cpp can answer by walking
// g_pServerClassHead. Without it the registry would have to refuse every
// staged port: a lane that lands DT_TESparks before DT_TEParticleSystem has
// moved is the ordinary state of this port for its whole length.
//-----------------------------------------------------------------------------
typedef bool ( *SourceSharpTeExternalBaseFn )( const char *pTableName, void *pUser );

enum SourceSharpTeRefusal_t
{
	SOURCESHARP_TE_ACCEPTED = 0,
	SOURCESHARP_TE_REFUSED_CLOSED,        // the registration window has shut
	SOURCESHARP_TE_REFUSED_NULL,          // no description, or no names on it
	SOURCESHARP_TE_REFUSED_FULL,          // more classes than this holds
	SOURCESHARP_TE_REFUSED_DUP_CLASS,     // a second class of that name
	SOURCESHARP_TE_REFUSED_DUP_TABLE,     // a second table of that name
	SOURCESHARP_TE_REFUSED_FIELD_COUNT,   // more fields than this holds, or none where some are needed
	SOURCESHARP_TE_REFUSED_DUP_FIELD,     // two props of one name in one table
	SOURCESHARP_TE_REFUSED_FIELD_SHAPE,   // a kind, width or storage size that cannot be built
	SOURCESHARP_TE_REFUSED_OFFSET,        // a field outside the declared blob
	SOURCESHARP_TE_REFUSED_BASE,          // a base table nobody has registered
	SOURCESHARP_TE_REFUSED_TOO_BIG,       // worst case over the 192-byte event cap
};

inline const char *SourceSharpTe_RefusalText( int nWhy )
{
	switch ( nWhy )
	{
	case SOURCESHARP_TE_ACCEPTED:           return "accepted";
	case SOURCESHARP_TE_REFUSED_CLOSED:     return "the registration window has closed";
	case SOURCESHARP_TE_REFUSED_NULL:       return "no description, or no class/table name on it";
	case SOURCESHARP_TE_REFUSED_FULL:       return "more temp entity classes than the registry holds";
	case SOURCESHARP_TE_REFUSED_DUP_CLASS:  return "a class of that name is already registered";
	case SOURCESHARP_TE_REFUSED_DUP_TABLE:  return "a table of that name is already registered";
	case SOURCESHARP_TE_REFUSED_FIELD_COUNT:return "more fields than the registry holds";
	case SOURCESHARP_TE_REFUSED_DUP_FIELD:  return "two props of one name in one table";
	case SOURCESHARP_TE_REFUSED_FIELD_SHAPE:return "a field whose kind, width or storage size cannot be built";
	case SOURCESHARP_TE_REFUSED_OFFSET:     return "a field outside the declared blob";
	case SOURCESHARP_TE_REFUSED_BASE:       return "a base table nobody has registered";
	case SOURCESHARP_TE_REFUSED_TOO_BIG:    return "worst case over the engine's 192-byte event cap";
	}

	return "refused";
}

class CSourceSharpTeRegistry
{
public:
	CSourceSharpTeRegistry()
		: m_nCount( 0 ), m_bClosed( false ), m_pExternalBase( 0 ), m_pExternalUser( 0 )
	{
		for ( int i = 0; i < SOURCESHARP_TE_MAX_CLASSES; i++ )
		{
			m_Classes[i].pClassName     = 0;
			m_Classes[i].pTableName     = 0;
			m_Classes[i].pBaseTable     = 0;
			m_Classes[i].nBlobBytes     = 0;
			m_Classes[i].nRegisterClass = 0;
			m_Classes[i].nFieldCount    = 0;
			m_Classes[i].pFields        = 0;
		}
	}

	~CSourceSharpTeRegistry()
	{
		for ( int i = 0; i < m_nCount; i++ )
		{
			free( (void *)m_Classes[i].pClassName );
			free( (void *)m_Classes[i].pTableName );
			free( (void *)m_Classes[i].pBaseTable );

			for ( int f = 0; f < m_Classes[i].nFieldCount; f++ )
			{
				free( (void *)m_Fields[i][f].pName );
				free( (void *)m_Fields[i][f].pSubTable );
			}
		}
	}

	// Who to ask about a base table this registry does not hold. Set once,
	// before anything registers.
	void SetExternalBaseResolver( SourceSharpTeExternalBaseFn pFn, void *pUser )
	{
		m_pExternalBase = pFn;
		m_pExternalUser = pUser;
	}

	int  Count() const   { return m_nCount; }
	bool IsClosed() const { return m_bClosed; }

	// Shuts the window. See TempEntRegister in sourcesharp.h for why a late
	// registration is worse than a refused one.
	void Close() { m_bClosed = true; }

	const SourceSharpTeClass_t *At( int i ) const
	{
		return ( i >= 0 && i < m_nCount ) ? &m_Classes[i] : 0;
	}

	int FindByClassName( const char *pName ) const
	{
		if ( !pName )
			return -1;

		for ( int i = 0; i < m_nCount; i++ )
		{
			if ( m_Classes[i].pClassName && strcmp( m_Classes[i].pClassName, pName ) == 0 )
				return i;
		}

		return -1;
	}

	int FindByTableName( const char *pName ) const
	{
		if ( !pName )
			return -1;

		for ( int i = 0; i < m_nCount; i++ )
		{
			if ( m_Classes[i].pTableName && strcmp( m_Classes[i].pTableName, pName ) == 0 )
				return i;
		}

		return -1;
	}

	//-------------------------------------------------------------------------
	// The worst case, in bits, of one registered class INCLUDING everything it
	// nests. -1 if any field cannot be priced.
	//
	// Walks the base chain by name, and stops at a depth no legal table reaches
	// rather than recursing forever on a cycle — DT_TEArmorRicochet's base is
	// DT_TEMetalSparks, whose base is nothing, and three links is already one
	// more than the deepest table in the inventory.
	//-------------------------------------------------------------------------
	int WorstCaseBits( int iClass ) const
	{
		int nBits = SOURCESHARP_TE_MESSAGE_OVERHEAD_BITS;
		int nGuard = 0;

		for ( int i = iClass; i >= 0 && nGuard < SOURCESHARP_TE_MAX_CLASSES; nGuard++ )
		{
			const SourceSharpTeClass_t *pClass = At( i );
			if ( !pClass )
				return -1;

			for ( int f = 0; f < pClass->nFieldCount; f++ )
			{
				const SourceSharpTeField_t *pField = &pClass->pFields[f];

				if ( pField->nKind == SOURCESHARP_TE_PROP_DATATABLE )
				{
					const int iNested = FindByTableName( pField->pSubTable );

					// A nested table this side does not hold is stock's own, and
					// its cost is not knowable from here. Priced as the whole
					// remaining budget would refuse every table that nests one;
					// priced as zero would under-count. Answer "cannot price",
					// which the caller reads as "do not judge the size" rather
					// than as "it fits".
					if ( iNested < 0 )
						return -1;

					const int nNested = WorstCaseBits( iNested );
					if ( nNested < 0 )
						return -1;

					// Its own framing is already counted once, at the top.
					nBits += nNested - SOURCESHARP_TE_MESSAGE_OVERHEAD_BITS;
					continue;
				}

				const int nValue = SourceSharpTe_FieldValueBits( pField );
				if ( nValue < 0 )
					return -1;

				nBits += SOURCESHARP_TE_PROP_INDEX_BITS
				         * SourceSharpTe_FieldPropCount( pField ) + nValue;
			}

			i = FindByTableName( pClass->pBaseTable );

			// A base that is stock's own has props this side cannot see. Same
			// answer as a nested table it cannot see, and for the same reason.
			if ( i < 0 && pClass->pBaseTable && pClass->pBaseTable[0] )
				return -1;
		}

		return nBits;
	}

	//-------------------------------------------------------------------------
	// Takes a description, or says why not. Returns the handle, or -1.
	//
	// EVERY REFUSAL IS A THING THAT CANNOT BE SEEN FROM INSIDE THE GAME, which
	// is the whole reason they are checks rather than comments:
	//
	//   a duplicate CLASS name puts two ServerClasses of one name in the list
	//     the engine negotiates at connect, and the client begins instantiating
	//     and stops, silently (plan 2E);
	//   a duplicate FIELD name inside a table pairs one send prop against a recv
	//     prop of the same name and drops the other, silently;
	//   an offset outside the blob networks whatever is past it, which is a
	//     plausible number;
	//   a worst case over the engine's event cap is a Host_Error at PLAYBACK.
	//
	// The description is copied. It arrives in managed memory.
	//-------------------------------------------------------------------------
	int Add( const SourceSharpTeClass_t *pDesc, int *pOutWhy )
	{
		int nWhy = SOURCESHARP_TE_ACCEPTED;
		const int iHandle = AddInternal( pDesc, &nWhy );

		if ( pOutWhy )
			*pOutWhy = nWhy;

		return iHandle;
	}

private:
	int AddInternal( const SourceSharpTeClass_t *pDesc, int *pWhy )
	{
		if ( m_bClosed )
			return Refuse( pWhy, SOURCESHARP_TE_REFUSED_CLOSED );

		if ( !pDesc || !pDesc->pClassName || !pDesc->pClassName[0]
		            || !pDesc->pTableName || !pDesc->pTableName[0] )
			return Refuse( pWhy, SOURCESHARP_TE_REFUSED_NULL );

		if ( m_nCount >= SOURCESHARP_TE_MAX_CLASSES )
			return Refuse( pWhy, SOURCESHARP_TE_REFUSED_FULL );

		if ( FindByClassName( pDesc->pClassName ) >= 0 )
			return Refuse( pWhy, SOURCESHARP_TE_REFUSED_DUP_CLASS );

		if ( FindByTableName( pDesc->pTableName ) >= 0 )
			return Refuse( pWhy, SOURCESHARP_TE_REFUSED_DUP_TABLE );

		if ( pDesc->nFieldCount < 0 || pDesc->nFieldCount > SOURCESHARP_TE_MAX_FIELDS )
			return Refuse( pWhy, SOURCESHARP_TE_REFUSED_FIELD_COUNT );

		if ( pDesc->nFieldCount > 0 && !pDesc->pFields )
			return Refuse( pWhy, SOURCESHARP_TE_REFUSED_FIELD_COUNT );

		if ( pDesc->nBlobBytes < 0 )
			return Refuse( pWhy, SOURCESHARP_TE_REFUSED_OFFSET );

		// A base by name, from here or from whoever still owns it natively.
		if ( pDesc->pBaseTable && pDesc->pBaseTable[0] )
		{
			const bool bKnown = FindByTableName( pDesc->pBaseTable ) >= 0
				|| ( m_pExternalBase && m_pExternalBase( pDesc->pBaseTable, m_pExternalUser ) );

			if ( !bKnown )
				return Refuse( pWhy, SOURCESHARP_TE_REFUSED_BASE );
		}

		for ( int f = 0; f < pDesc->nFieldCount; f++ )
		{
			const SourceSharpTeField_t *pField = &pDesc->pFields[f];

			if ( !pField->pName || !pField->pName[0] )
				return Refuse( pWhy, SOURCESHARP_TE_REFUSED_FIELD_SHAPE );

			for ( int g = 0; g < f; g++ )
			{
				if ( strcmp( pDesc->pFields[g].pName, pField->pName ) == 0 )
					return Refuse( pWhy, SOURCESHARP_TE_REFUSED_DUP_FIELD );
			}

			if ( pField->nKind == SOURCESHARP_TE_PROP_DATATABLE )
			{
				if ( !pField->pSubTable || !pField->pSubTable[0] )
					return Refuse( pWhy, SOURCESHARP_TE_REFUSED_FIELD_SHAPE );

				const bool bKnown = FindByTableName( pField->pSubTable ) >= 0
					|| ( m_pExternalBase && m_pExternalBase( pField->pSubTable, m_pExternalUser ) );

				if ( !bKnown )
					return Refuse( pWhy, SOURCESHARP_TE_REFUSED_BASE );

				continue;
			}

			if ( SourceSharpTe_FieldValueBits( pField ) < 0 )
				return Refuse( pWhy, SOURCESHARP_TE_REFUSED_FIELD_SHAPE );

			const int nBytes = SourceSharpTe_FieldBlobBytes( pField );
			if ( nBytes < 0 )
				return Refuse( pWhy, SOURCESHARP_TE_REFUSED_FIELD_SHAPE );

			if ( pField->nOffset < 0 || pField->nOffset + nBytes > pDesc->nBlobBytes )
				return Refuse( pWhy, SOURCESHARP_TE_REFUSED_OFFSET );
		}

		// Copied BEFORE the size is priced, because pricing walks the registry
		// by name and this class's own fields are half of what it walks.
		const int iHandle = m_nCount;

		m_Classes[iHandle].pClassName     = Dup( pDesc->pClassName );
		m_Classes[iHandle].pTableName     = Dup( pDesc->pTableName );
		m_Classes[iHandle].pBaseTable     = ( pDesc->pBaseTable && pDesc->pBaseTable[0] )
		                                      ? Dup( pDesc->pBaseTable ) : 0;
		m_Classes[iHandle].nBlobBytes     = pDesc->nBlobBytes;
		m_Classes[iHandle].nRegisterClass = pDesc->nRegisterClass;
		m_Classes[iHandle].nFieldCount    = pDesc->nFieldCount;
		m_Classes[iHandle].pFields        = m_Fields[iHandle];

		for ( int f = 0; f < pDesc->nFieldCount; f++ )
		{
			m_Fields[iHandle][f] = pDesc->pFields[f];
			m_Fields[iHandle][f].pName     = Dup( pDesc->pFields[f].pName );
			m_Fields[iHandle][f].pSubTable = pDesc->pFields[f].pSubTable
			                                   ? Dup( pDesc->pFields[f].pSubTable ) : 0;
		}

		m_nCount++;

		// -1 means "there is a table in the chain this side cannot see", not
		// "it does not fit". Judging a size that was never computed is how a
		// legal table gets refused for a number nobody produced.
		const int nWorst = WorstCaseBits( iHandle );

		if ( nWorst >= 0 && nWorst > SOURCESHARP_TE_MAX_EVENT_BITS )
		{
			Drop( iHandle );
			return Refuse( pWhy, SOURCESHARP_TE_REFUSED_TOO_BIG );
		}

		*pWhy = SOURCESHARP_TE_ACCEPTED;
		return iHandle;
	}

	void Drop( int iHandle )
	{
		if ( iHandle != m_nCount - 1 )
			return;   // only ever the one just added

		free( (void *)m_Classes[iHandle].pClassName );
		free( (void *)m_Classes[iHandle].pTableName );
		free( (void *)m_Classes[iHandle].pBaseTable );

		for ( int f = 0; f < m_Classes[iHandle].nFieldCount; f++ )
		{
			free( (void *)m_Fields[iHandle][f].pName );
			free( (void *)m_Fields[iHandle][f].pSubTable );
			m_Fields[iHandle][f].pName     = 0;
			m_Fields[iHandle][f].pSubTable = 0;
		}

		m_Classes[iHandle].pClassName  = 0;
		m_Classes[iHandle].pTableName  = 0;
		m_Classes[iHandle].pBaseTable  = 0;
		m_Classes[iHandle].nFieldCount = 0;
		m_nCount--;
	}

	static int Refuse( int *pWhy, int nWhy )
	{
		if ( pWhy )
			*pWhy = nWhy;

		return -1;
	}

	static char *Dup( const char *pText )
	{
		const size_t nLen = strlen( pText );
		char *pOut = (char *)malloc( nLen + 1 );

		if ( pOut )
			memcpy( pOut, pText, nLen + 1 );

		return pOut;
	}

	SourceSharpTeClass_t m_Classes[ SOURCESHARP_TE_MAX_CLASSES ];
	SourceSharpTeField_t m_Fields[ SOURCESHARP_TE_MAX_CLASSES ][ SOURCESHARP_TE_MAX_FIELDS ];

	int  m_nCount;
	bool m_bClosed;

	SourceSharpTeExternalBaseFn m_pExternalBase;
	void                       *m_pExternalUser;
};

#endif // SOURCESHARP_TEMPENT_SCHEMA_H
