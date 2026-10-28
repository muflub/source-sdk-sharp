// The generic net table's payload: a managed entity's state as flat uint32 props at fixed
// offsets inside the entity. Dword props cost the same as typed SendPropInts (the delta
// writer charges per prop index), and the default proxy keeps every word on the engine's
// fast local-transfer list. Engine-free so the arithmetic is testable.
#pragma once

// Fixed: both sides derive the prop names and offsets from it.
enum { SOURCESHARP_PAYLOAD_WORD_BYTES = 4 };

// The largest payload, 2048 bytes; names run "p000".."p511".
enum { SOURCESHARP_PAYLOAD_MAX_WORDS = 512 };

// "pNNN" plus the terminator; fixed width so names sort in prop order.
enum { SOURCESHARP_PAYLOAD_NAME_BYTES = 5 };

// The engine stores a prop offset in an unsigned short and asserts below 0xFFFF.
enum { SOURCESHARP_PAYLOAD_MAX_OFFSET = 0xFFFE };

// How many props a payload of nBytes needs, rounding up; -1 for a size no tier carries.
inline int SourceSharpPayload_WordCount( int nBytes )
{
	if ( nBytes < 0 )
		return -1;

	const int nWords = ( nBytes + SOURCESHARP_PAYLOAD_WORD_BYTES - 1 ) / SOURCESHARP_PAYLOAD_WORD_BYTES;

	if ( nWords > SOURCESHARP_PAYLOAD_MAX_WORDS )
		return -1;

	return nWords;
}

// Word i's byte offset from the start of the entity, which is what a SendProp offset
// means. A wrong nBlobBase networks the entity's own members, vtable pointer included.
inline int SourceSharpPayload_WordOffset( int nBlobBase, int iWord )
{
	return nBlobBase + iWord * SOURCESHARP_PAYLOAD_WORD_BYTES;
}

inline bool SourceSharpPayload_OffsetsFit( int nBlobBase, int nWords )
{
	if ( nBlobBase < 0 || nWords < 0 || nWords > SOURCESHARP_PAYLOAD_MAX_WORDS )
		return false;

	if ( nWords == 0 )
		return nBlobBase <= SOURCESHARP_PAYLOAD_MAX_OFFSET;

	return SourceSharpPayload_WordOffset( nBlobBase, nWords - 1 ) <= SOURCESHARP_PAYLOAD_MAX_OFFSET;
}

// Writes "p000".."p511" for word i; false, writing nothing, when out of range or the
// buffer is too small. Both sides must spell the names identically, as the engine
// matches send and recv props by name.
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

// A packed field's placement inside the blob, as managed reports it: the word's offset,
// the low bit and the width. Native only shifts and masks; it never derives a layout.
// A width of zero means unresolved, and every reader answers zero for it.
struct SourceSharpPackedField_t
{
	int nOffset;    // object-relative, blob base already added
	int nShift;     // 0..31
	int nWidth;     // 1..32, or 0 for a field that did not resolve

	SourceSharpPackedField_t() : nOffset( -1 ), nShift( 0 ), nWidth( 0 ) {}

	bool IsResolved() const { return nOffset >= 0 && nWidth > 0 && nWidth <= 32
	                                && nShift >= 0 && nShift + nWidth <= 32; }
};

inline SourceSharpPackedField_t SourceSharpPayload_Place( int nOffset, int nBits )
{
	SourceSharpPackedField_t field;

	if ( nOffset < 0 || nBits < 0 )
		return field;

	const int nShift = nBits & 0xFF;
	const int nWidth = ( nBits >> 8 ) & 0xFF;

	// Refused rather than clamped: a placement that does not fit a word is a layout
	// disagreement.
	if ( nWidth <= 0 || nWidth > 32 || nShift < 0 || nShift + nWidth > 32 )
		return field;

	field.nOffset = nOffset;
	field.nShift  = nShift;
	field.nWidth  = nWidth;

	return field;
}

// The field's bits, unsigned; zero if unresolved. The offset already includes the blob base.
inline unsigned int SourceSharpPayload_ReadPacked( const void *pObject,
                                                   const SourceSharpPackedField_t &field )
{
	if ( !pObject || !field.IsResolved() )
		return 0;

	const unsigned char *pBase = (const unsigned char *)pObject;

	unsigned int nWord = 0;

	// Byte by byte: the object's alignment is not promised. The blob is little-endian.
	for ( int i = 0; i < 4; i++ )
		nWord |= ( (unsigned int)pBase[ field.nOffset + i ] ) << ( i * 8 );

	const unsigned int nMask = ( field.nWidth >= 32 )
		? 0xFFFFFFFFu
		: ( ( 1u << field.nWidth ) - 1u );

	return ( nWord >> field.nShift ) & nMask;
}

inline int SourceSharpPayload_ReadPackedSigned( const void *pObject,
                                                const SourceSharpPackedField_t &field )
{
	if ( !pObject || !field.IsResolved() )
		return 0;

	const unsigned int nRaw = SourceSharpPayload_ReadPacked( pObject, field );

	if ( field.nWidth >= 32 )
		return (int)nRaw;

	const unsigned int nSignBit = 1u << ( field.nWidth - 1 );

	// Arithmetic rather than a shift pair, which would left-shift past the sign bit (UB).
	return ( nRaw & nSignBit ) ? (int)( nRaw ) - (int)( nSignBit << 1 ) : (int)nRaw;
}

// Replaces the field's bits, preserving its word-mates. Unresolved placements are refused.
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

// The payload probe pattern: the server fills a blob from (seed, word index) and the
// client recomputes and compares, proving the wire round trip. A different seed must
// disagree at word 0.
inline unsigned int SourceSharpPayload_ProbeWord( unsigned int nSeed, int iWord )
{
	// FNV-1a over the seed's and the index's bytes.
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

	// Never zero, so a pattern word cannot look like a never-written blob.
	return nHash ? nHash : 1u;
}

class SendProp;
class RecvProp;

// The interned name for word i, or NULL if out of range. The engine keeps the pointer for
// the life of the process.
const char *SourceSharpPayload_PropName( int iWord );

#ifndef CLIENT_DLL

// Fills pProps[0..nWords-1] with the payload's send props, all on the default proxy.
// Returns the number written, or -1 for an out-of-range count or offset.
int SourceSharpPayload_BuildSendProps( SendProp *pProps, int nWords, int nBlobBase );

// Whether this prop resolved to m_UInt32ToInt32, the proxy the engine's fast int32 local
// transfer accepts. A word on the slow list still works, so nothing else reports it.
bool SourceSharpPayload_IsFastSendProxy( const SendProp *pProp );

const char *SourceSharpPayload_SendProxyName( const SendProp *pProp );

#endif // !CLIENT_DLL

#ifdef CLIENT_DLL

// Whether this side recognises the type and layout an update was written with; false
// means the bytes must not be decoded. The decision is managed's. *ppOutClassName gets
// the classname the type id resolves to.
bool SourceSharpSchema_Accept( unsigned int nTypeId, unsigned int nSchemaHash,
                               int nEntIndex, const char **ppOutClassName );

int SourceSharpPayload_BuildRecvProps( RecvProp *pProps, int nWords, int nBlobBase );

bool SourceSharpPayload_IsFastRecvProxy( const RecvProp *pProp );
const char *SourceSharpPayload_RecvProxyName( const RecvProp *pProp );

#endif // CLIENT_DLL
