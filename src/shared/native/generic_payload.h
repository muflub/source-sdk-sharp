//========= SourceSharp — the payload props, and the arithmetic behind them ===//
//
// plan_nettable_generic.md Part 3 and Part 11: the generic table carries a C#
// entity's declared state as N flat uint32 props at fixed offsets inside the
// entity, instead of one typed prop per declared field. This file builds that
// prop set. Nothing here is on the wire yet — Step 1 builds it standalone and
// dumps it, and Step 3 is what registers a table containing it.
//
// TWO THINGS DECIDE THE SHAPE, AND BOTH ARE CORRECTIONS TO THE FIRST DRAFT.
//
// The element is a DWORD, not a byte (Part 1B). CDeltaBitsWriter::WritePropIndex
// does not write a prop index, it writes the gap from the last index written,
// and charges 7 bits for a gap of 16 or less. So a changed four-byte field costs
// 4*7 + 4*8 = 60 bits as four byte props and 7 + 32 = 39 bits as one dword prop
// — which is exactly what an ordinary typed SendPropInt costs. At byte
// granularity the payload would have been a 54% bandwidth regression; at dword
// granularity it is not a regression at all.
//
// The proxy is the DEFAULT one, not a custom SendProxy_PayloadByte (Part 1A).
// The engine's local transfer sorts every prop into lists when the table is
// set up, and a prop reaches a FAST list only if its send proxy is one
// of the standard ones the game DLL published through GetStandardSendProxies.
// Anything else is copied slowly, proxies and all. A custom proxy would
// therefore have put every payload word
// of every managed entity on the slow path of every listen server, which is
// every headless test run this project makes.
//
// A default proxy is also all the payload needs. The utlvector machinery exists
// because its data sits behind a CUtlVector pointer indirection; our blob is
// inline in the entity at a fixed offset, so
//
//     SendPropInt( name, blobBase + i*4, 4, 32, SPROP_UNSIGNED )
//
// reads exactly the right four bytes with no help. dt_send.cpp:482-556: a
// sizeofVar of 4 selects SendProxy_Int32ToInt32, and SPROP_UNSIGNED then
// upgrades it to SendProxy_UInt32ToInt32 — which is g_StandardSendProxies.
// m_UInt32ToInt32, one of the two the fast int32 list accepts. The payload
// sub-table, the length sub-table and CSendPropExtra_UtlVector all disappear
// with it.
//
// NO ENGINE HEADERS HERE, on purpose. The offset arithmetic is the part that can
// be quietly wrong — an off-by-one lands a prop on the entity's own members
// rather than the blob, and nothing reports that — so it is inline and testable
// without a running Source. See sourcesharp/test/generic_payload_test.cpp. The
// two builders below are declared against forward-declared prop types and
// defined in the .cpp, which is the only part that needs dt_send.h/dt_recv.h.
//
//=============================================================================//
#ifndef SOURCESHARP_GENERIC_PAYLOAD_H
#define SOURCESHARP_GENERIC_PAYLOAD_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// The payload element. Four bytes, for the delta-index reason above, and not a
// tunable: the props are named and offset from it and both sides derive the
// same layout from the same constant.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_PAYLOAD_WORD_BYTES = 4 };

//-----------------------------------------------------------------------------
// The largest payload the prop-name table can name, which is also the largest
// tier Part 7 asks for: 2048 bytes. Well under MAX_DATATABLE_PROPS (4096), and
// the bound the names "p000".."p511" are formatted to fit.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_PAYLOAD_MAX_WORDS = 512 };

//-----------------------------------------------------------------------------
// A prop name is "pNNN" plus the terminator. Fixed width so the names sort the
// way the props are ordered, which is what makes a dump readable next to a
// bitstream.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_PAYLOAD_NAME_BYTES = 5 };

//-----------------------------------------------------------------------------
// The engine stores a prop's byte offset in an unsigned short and asserts on it
// (the offset must stay below 0xFFFF). Entity
// plus blob is nowhere near this today, but a tier list that grew carelessly
// could reach it, and the failure would be an assert deep in table setup rather
// than anything naming the payload.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_PAYLOAD_MAX_OFFSET = 0xFFFE };

//-----------------------------------------------------------------------------
// How many props a payload of nBytes needs.
//
// Rounds up: a 33-byte layout is nine words, the last one carrying a byte of
// real data and three of padding. Rounding down would put a field off the end of
// the table and network three quarters of it.
//
// Returns -1 for a size no tier can carry, rather than clamping — a caller that
// silently got fewer props than it asked for would send a truncated blob.
//-----------------------------------------------------------------------------
inline int SourceSharpPayload_WordCount( int nBytes )
{
	if ( nBytes < 0 )
		return -1;

	const int nWords = ( nBytes + SOURCESHARP_PAYLOAD_WORD_BYTES - 1 ) / SOURCESHARP_PAYLOAD_WORD_BYTES;

	if ( nWords > SOURCESHARP_PAYLOAD_MAX_WORDS )
		return -1;

	return nWords;
}

//-----------------------------------------------------------------------------
// Where word i of the payload lives, as a byte offset from the START OF THE
// ENTITY — which is what a SendProp offset means.
//
// nBlobBase is the blob's own offset within the object, so the same arithmetic
// served CSourceSharpNetEntity and CStandaloneEntity despite their being
// unrelated classes: no virtual DataBlob() call and no dynamic_cast, just a
// number the caller already knows. The first of those two is deleted and the
// parameter is not: the server's and the client's halves of the hierarchy are
// still unrelated classes with different blob bases, which is the case this
// argument has always actually been carrying.
//
// Getting nBlobBase wrong is the failure worth guarding: a base of 0 does not
// crash, it networks the first N words of the entity's own members — vtable
// pointer included — and the client decodes them as state.
//-----------------------------------------------------------------------------
inline int SourceSharpPayload_WordOffset( int nBlobBase, int iWord )
{
	return nBlobBase + iWord * SOURCESHARP_PAYLOAD_WORD_BYTES;
}

//-----------------------------------------------------------------------------
// Whether every prop of this set lands on an offset the engine can store.
// See SOURCESHARP_PAYLOAD_MAX_OFFSET.
//-----------------------------------------------------------------------------
inline bool SourceSharpPayload_OffsetsFit( int nBlobBase, int nWords )
{
	if ( nBlobBase < 0 || nWords < 0 || nWords > SOURCESHARP_PAYLOAD_MAX_WORDS )
		return false;

	if ( nWords == 0 )
		return nBlobBase <= SOURCESHARP_PAYLOAD_MAX_OFFSET;

	return SourceSharpPayload_WordOffset( nBlobBase, nWords - 1 ) <= SOURCESHARP_PAYLOAD_MAX_OFFSET;
}

//-----------------------------------------------------------------------------
// Writes "p000".."p511" for word i. Returns false and writes nothing for an
// index out of range or a buffer that cannot hold the result.
//
// Formatted by hand rather than with Q_snprintf so this header stays free of
// tier1, and so the SAME function can be the one the test pins: the names are
// half of the client/server contract for the generic table (Part 1D — a send
// prop with no matching recv prop by NAME is decoded and discarded), so both
// sides must spell them identically and a test has to be able to say so.
//
// Not DT_ArrayElementNameForIdx, which would also produce three digits: these
// are not array elements and naming them as if they were would make a dump lie
// about the table's shape.
//-----------------------------------------------------------------------------
inline bool SourceSharpPayload_FormatPropName( int iWord, char *pOut, int nOutBytes )
{
	if ( !pOut || nOutBytes < SOURCESHARP_PAYLOAD_NAME_BYTES )
		return false;

	if ( iWord < 0 || iWord >= SOURCESHARP_PAYLOAD_MAX_WORDS )
		return false;

	pOut[0] = 'p';
	pOut[1] = (char)( '0' + ( ( iWord / 100 ) % 10 ) );
	pOut[2] = (char)( '0' + ( ( iWord / 10 ) % 10 ) );
	pOut[3] = (char)( '0' + ( iWord % 10 ) );
	pOut[4] = '\0';

	return true;
}

//=============================================================================//
// PACKED FIELDS — reading one out of the word it shares (Part 10, Step 8).
//
// PART 10 SAYS THIS STEP NEEDS NO C++ AT ALL. It is wrong, and the reason is a
// thing that did not exist when it was written: Step 7 gave native a by-name
// reader of C#-declared state — the client's surface renderer, which asks C#
// where a field is and reads it out of the blob itself. It is the only code on
// this side with an opinion about what the bits inside a payload word mean, and
// packing is precisely a change to that.
//
// Concretely: func_breakable_surf's Wide, High, Broken and SurfaceType were four
// bytes and are now nineteen bits of one word. A renderer that asked where
// "Broken" is and read a byte there gets the pane count, silently, and draws an
// unbroken sheet as a broken one or the reverse.
//
// THE PLACEMENT IS ASKED FOR, NEVER DERIVED. C# answers the offset and the
// shift/width pair; this only shifts and masks. That is the same rule the offset
// itself has followed since Step 7 and it is the whole reason a native renderer
// can read managed state at all without a second copy of the layout — a copy
// that would drift silently, because a wrong shift reads a plausible number out
// of the field beside it.
//
// HERE rather than in the renderer because a header with no engine dependency is
// the only place a gtest can reach, and the arithmetic is the part that can be
// quietly wrong. Sign extension especially: a wrongly-extended negative is a
// plausible number and nothing reports it, which is the same argument
// SS_FIELD_UINT was invented for.
//=============================================================================//

//-----------------------------------------------------------------------------
// A field's placement inside the blob: where its word is, how far into it the
// field's low bit sits, and how wide it is.
//
// Resolved ONCE per field per process from SourceSharp_StandaloneFieldOffset and
// SourceSharp_StandaloneFieldBits, and then read with no further calls. A width
// of zero means unresolved, and every reader below answers zero for it rather
// than reading whatever is at offset zero.
//-----------------------------------------------------------------------------
struct SourceSharpPackedField_t
{
	int nOffset;    // object-relative, blob base already added
	int nShift;     // 0..31
	int nWidth;     // 1..32, or 0 for a field that did not resolve

	SourceSharpPackedField_t() : nOffset( -1 ), nShift( 0 ), nWidth( 0 ) {}

	bool IsResolved() const { return nOffset >= 0 && nWidth > 0 && nWidth <= 32
	                                && nShift >= 0 && nShift + nWidth <= 32; }
};

//-----------------------------------------------------------------------------
// Unpacks the shift/width pair SourceSharp_StandaloneFieldBits returns.
//
// Its own function so the encoding has ONE reader on this side. It is decided in
// C# and consumed here, which is the shape every other number crossing this
// boundary has, and the alternative is two places that shift by eight.
//-----------------------------------------------------------------------------
inline SourceSharpPackedField_t SourceSharpPayload_Place( int nOffset, int nBits )
{
	SourceSharpPackedField_t field;

	if ( nOffset < 0 || nBits < 0 )
		return field;

	const int nShift = nBits & 0xFF;
	const int nWidth = ( nBits >> 8 ) & 0xFF;

	// Refused rather than clamped. A placement that does not fit a word is a
	// disagreement about the layout, and reading SOMETHING from it is how a
	// wrong offset becomes a plausible value instead of a blank one.
	if ( nWidth <= 0 || nWidth > 32 || nShift < 0 || nShift + nWidth > 32 )
		return field;

	field.nOffset = nOffset;
	field.nShift  = nShift;
	field.nWidth  = nWidth;

	return field;
}

//-----------------------------------------------------------------------------
// The field's own bits, unsigned. Zero for a field that never resolved.
//
// pObject is the entity; the offset already carries the blob base, exactly as
// SourceSharp_StandaloneFieldOffset returns it.
//-----------------------------------------------------------------------------
inline unsigned int SourceSharpPayload_ReadPacked( const void *pObject,
                                                   const SourceSharpPackedField_t &field )
{
	if ( !pObject || !field.IsResolved() )
		return 0;

	const unsigned char *pBase = (const unsigned char *)pObject;

	unsigned int nWord = 0;

	// Byte by byte rather than a uint32 load, because the offset is whatever C#
	// placed the word at plus whatever this shell's blob base is, and neither
	// promises four-byte alignment of the OBJECT. Little-endian by construction,
	// which is what the blob is on both ends of every platform this ships on and
	// what the payload props already assume.
	for ( int i = 0; i < 4; i++ )
		nWord |= ( (unsigned int)pBase[ field.nOffset + i ] ) << ( i * 8 );

	const unsigned int nMask = ( field.nWidth >= 32 )
		? 0xFFFFFFFFu
		: ( ( 1u << field.nWidth ) - 1u );

	return ( nWord >> field.nShift ) & nMask;
}

//-----------------------------------------------------------------------------
// The same field SIGN EXTENDED from its declared width, for a C#-signed one.
//
// Separate from the unsigned reader for the reason NetFieldKind.UInt is its own
// kind rather than a flag: getting this wrong yields a plausible negative and
// nothing anywhere reports it.
//-----------------------------------------------------------------------------
inline int SourceSharpPayload_ReadPackedSigned( const void *pObject,
                                                const SourceSharpPackedField_t &field )
{
	if ( !pObject || !field.IsResolved() )
		return 0;

	const unsigned int nRaw = SourceSharpPayload_ReadPacked( pObject, field );

	if ( field.nWidth >= 32 )
		return (int)nRaw;

	const unsigned int nSignBit = 1u << ( field.nWidth - 1 );

	// Two's complement by arithmetic rather than by a shift pair, because a
	// left shift of a signed value past its sign bit is undefined in C++ and the
	// obvious spelling of this is exactly that.
	return ( nRaw & nSignBit ) ? (int)( nRaw ) - (int)( nSignBit << 1 ) : (int)nRaw;
}

//-----------------------------------------------------------------------------
// The write that pairs with ReadPacked (Phase 9c5): the field's bits replaced,
// its word-mates preserved. A masked read-modify-write, byte by byte for the
// same alignment reason the reader gives.
//
// Refuses an unresolved placement the way every reader does — writing
// SOMETHING through a bad placement is how a layout disagreement becomes a
// neighbouring field's corruption instead of a blank.
//-----------------------------------------------------------------------------
inline void SourceSharpPayload_WritePacked( void *pObject,
                                            const SourceSharpPackedField_t &field,
                                            unsigned int nValue )
{
	if ( !pObject || !field.IsResolved() )
		return;

	unsigned char *pBase = (unsigned char *)pObject;

	unsigned int nWord = 0;

	for ( int i = 0; i < 4; i++ )
		nWord |= ( (unsigned int)pBase[ field.nOffset + i ] ) << ( i * 8 );

	const unsigned int nMask = ( field.nWidth >= 32 )
		? 0xFFFFFFFFu
		: ( ( 1u << field.nWidth ) - 1u );

	nWord &= ~( nMask << field.nShift );
	nWord |= ( nValue & nMask ) << field.nShift;

	for ( int i = 0; i < 4; i++ )
		pBase[ field.nOffset + i ] = (unsigned char)( ( nWord >> ( i * 8 ) ) & 0xFF );
}

//=============================================================================//
// THE PROBE PATTERN — what replaced the typed-prop oracle (Part 14 Step 6).
//
// Steps 3 to 5 checked the payload against the typed props carrying the same
// state. Step 6 deletes the typed props, so that comparison loses its other
// side, and "the check passed and then I deleted the check" is the failure this
// document is written against.
//
// What stands in its place is a round trip that goes further than the oracle
// did. cs_sv_payload_probe fills a live entity's blob with THIS function of the
// word index and a seed and marks the entity changed; cs_cl_payload_probe
// recomputes the same words on the client and compares them against what
// arrived, in code, reporting the first that differs. The shadow proved two
// prop sets on ONE side read the same bytes; this proves the bytes the server
// wrote are the bytes the client holds, through the encoder, the delta and the
// decoder.
//
// It has to be here rather than in either side's file for the reason every
// other shared rule in this tree is: two implementations of the pattern would
// agree at first and drift, and a drifting pattern reports a wire fault that is
// not there — which is worse than no check, because it trains a reader to
// ignore it.
//
// The SEED is what makes the check falsifiable. Probing with one seed and
// checking with another must report a disagreement at word 0; a check that
// cannot go red is not a measurement.
//=============================================================================//
inline unsigned int SourceSharpPayload_ProbeWord( unsigned int nSeed, int iWord )
{
	// FNV-1a over the four bytes of the seed and the four of the index. Not
	// chosen for strength — nothing here is adversarial — but for being the
	// hash already spelled out twice in this tree, so a reader checking it by
	// hand has something to check it against.
	const unsigned int nPrime = 16777619u;

	unsigned int nHash = 2166136261u;
	const unsigned int nMix[ 2 ] = { nSeed, (unsigned int)iWord };

	for ( int m = 0; m < 2; m++ )
	{
		for ( int b = 0; b < 4; b++ )
		{
			nHash ^= ( nMix[ m ] >> ( b * 8 ) ) & 0xFFu;
			nHash *= nPrime;
		}
	}

	// Never zero. A blob that was never written is all zeroes, so a pattern
	// word that came out zero would be indistinguishable from one that never
	// arrived — which is the one answer this check must not be able to give.
	return nHash ? nHash : 1u;
}

//=============================================================================//
// CSourceSharpSharedPayload IS GONE (Part 18).
//
// It held ONE payload word count for a whole family of classes, declared up
// front and then sealed by the first table built against it — because every
// entity on the parallel hierarchy was allocated one blob whatever its class,
// so the word count there belonged to the hierarchy and not to any class on it.
//
// Per-class tiers remove the thing it was describing. The factory sizes each
// entity to its own class's tier now, no two classes share a blob, and a tier's
// word count IS its byte count divided by four — a constant of the tier list,
// never declared and with nothing to seal. Every caller asks
// SourceSharp_TierWords( entity's tier ) instead.
//
// DELETED RATHER THAN LEFT UNUSED, and that is the whole point of removing it
// here rather than later. A sealed hierarchy-wide word count still compiling is
// a second opinion about which words an entity carries, and this file exists
// because the boundary arithmetic is the part that can be quietly wrong. Its
// six gtests went with it: they pinned "one number for every class on the
// family", which is now the opposite of what the code does, and a test that
// pins a removed invariant either fails or — worse — passes against a class
// nothing calls.
//=============================================================================//


class SendProp;
class RecvProp;

//-----------------------------------------------------------------------------
// The interned name for word i, or NULL if out of range.
//
// A SendProp keeps the const char * it was handed and the engine reads it for
// the life of the process, so the names cannot be built on a caller's stack.
// One static table, filled once.
//-----------------------------------------------------------------------------
const char *SourceSharpPayload_PropName( int iWord );

#ifndef CLIENT_DLL

//-----------------------------------------------------------------------------
// Fills pProps[0..nWords-1] with the payload's send props. Returns the number
// written, or -1 if the request cannot be honoured — an out-of-range word count
// or an offset the engine cannot store.
//
// Every prop uses the default proxy. Passing one here would be the mistake
// Part 1A is about; there is deliberately no parameter for it.
//-----------------------------------------------------------------------------
int SourceSharpPayload_BuildSendProps( SendProp *pProps, int nWords, int nBlobBase );

//-----------------------------------------------------------------------------
// Whether this prop's resolved proxy is the one the engine's fast int32 local
// transfer accepts (g_StandardSendProxies.m_UInt32ToInt32).
//
// The whole cost argument of Part 1A rests on this being true, and nothing else
// in the system would report it having stopped being true: a payload word on the
// slow list still arrives, still decodes, and still passes every test — it just
// costs a proxy call and a bit-encode per word per frame on every listen server.
//-----------------------------------------------------------------------------
bool SourceSharpPayload_IsFastSendProxy( const SendProp *pProp );

//-----------------------------------------------------------------------------
// A name for whichever standard proxy this prop resolved to, for the dump.
// "custom" for anything that is not one of them, which is the answer that means
// something is wrong.
//-----------------------------------------------------------------------------
const char *SourceSharpPayload_SendProxyName( const SendProp *pProp );

#endif // !CLIENT_DLL

#ifdef CLIENT_DLL

//-----------------------------------------------------------------------------
// The mirror. RecvPropInt with a sizeofVar of 4 defaults to
// RecvProxy_Int32ToInt32 (dt_recv.cpp), which is the proxy the fast path
// requires on the RECEIVING side — the engine checks both ends, so a
// correct send prop paired with a wrong recv prop is still the slow list.
//-----------------------------------------------------------------------------
//=============================================================================//
// THE SCHEMA-HASH REJECTION PATH (Part 4, Part 13, and Part 14 Step 6).
//
// Every update carries the fingerprint of the layout its payload was written
// with. This is the client asking whether it recognises that fingerprint, and
// the answer decides whether C# is handed the bytes at all.
//
// REFUSING IS THE WHOLE POINT. Two builds whose C# differs is the one
// disagreement this design cannot make impossible — both sides generate from
// their own copy of the shared assembly — and until now it decoded silently:
// one build's memory image read through another build's field offsets, which
// produces plausible numbers in the wrong fields and nothing anywhere saying
// so. That is the pane-of-glass failure exactly, and it is what the hash is for.
//
// Client only. The server writes the hash and has nothing to check it against.
//=============================================================================//

//-----------------------------------------------------------------------------
// Whether this side recognises the type and the layout an update was written
// with. False means the bytes must NOT be decoded.
//
// *ppOutClassName receives the classname the type id resolves to, which is what
// replaced the replicated m_szClassname: four bytes on the wire and a lookup
// here instead of sixty-four bytes per entity.
//
// The decision and the refusals it keeps are managed's (ClientSchemaGate,
// through IManagedStandaloneClient::SchemaAccept): this hands it what the type
// table holds and carries out the answer. No managed side refuses.
//-----------------------------------------------------------------------------
bool SourceSharpSchema_Accept( unsigned int nTypeId, unsigned int nSchemaHash,
                               int nEntIndex, const char **ppOutClassName );

int SourceSharpPayload_BuildRecvProps( RecvProp *pProps, int nWords, int nBlobBase );

bool SourceSharpPayload_IsFastRecvProxy( const RecvProp *pProp );
const char *SourceSharpPayload_RecvProxyName( const RecvProp *pProp );

#endif // CLIENT_DLL

#endif // SOURCESHARP_GENERIC_PAYLOAD_H
