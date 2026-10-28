//========= SourceSharp — the tier list, which is not generated ==============//
//
// plan_nettable_generic.md Part 7 and Part 11. One SendTable per SIZE instead of
// one per class: a managed type is assigned to the smallest tier its layout fits
// in, and the tier list itself is a hardcoded constant that never appears in any
// handshake. That is the whole point — a C# type may be added, removed or
// reshaped at runtime without either side renegotiating anything, as long as it
// stays inside its tier.
//
// STEP 6 REGISTERS EVERY TIER, ON BOTH SHELLS, and every managed entity is now
// created through one of them. Two things about that turned out not to be what
// Part 7 described, and both are recorded here because the tier list is where a
// reader will look for them.
//
// A TIER NEEDED A NAME PER SHELL, AND NO LONGER DOES. A SendTable's props are
// offsets into one object, so a single ServerClass cannot serve two unrelated
// C++ classes; CSourceSharpNetEntity and CStandaloneEntity were exactly that,
// so each shell had its own set. THE FIRST OF THOSE CLASSES IS GONE — it was a
// CBaseEntity, which step 6 turns into an alias for IGameEntity, and a class
// deriving from the alias is abstract. One shell, one name pair per tier.
//
// THE TIER BELONGS TO THE SHELL, NOT TO THE TYPE, on the parallel hierarchy.
// Part 7 says "tier is chosen from EntityLayout.Size at publish time", which is
// right for the runtime-networked entity and wrong for the hierarchy: operator
// new there is a static function with no idea which class the factory is about
// to make, so every entity on it is allocated ONE blob whatever its class. That
// is the same correction Step 4 made about the payload word count, arriving one
// level up — the tier is a property of the family, and a per-class tier would
// give two classes on one blob two different tables' worth of payload props.
//
// NO ENGINE HEADERS, for the same reason generic_payload.h has none: the
// boundary arithmetic is the part that can be quietly wrong, and it is testable
// without a running Source. See sourcesharp/test/generic_payload_test.cpp.
//
// THE MANAGED SIDE NO LONGER HAS A LIST. NetTiers.Sizes was a projection of the
// rule below, flagged as one; it now READS this file across the ABI, through
// NetTierInfo, and falls back to nothing rather than to a second opinion when no
// engine is up. A rule with two implementations drifts, and a boundary rule
// drifts silently: a type assigned to a tier one side thinks is 256 bytes and
// the other thinks is 32 has its tail simply absent from the wire.
//
//=============================================================================//
#pragma once

#include "generic_payload.h"

#include <cstddef>

//-----------------------------------------------------------------------------
// Which native shell hosts a managed type.
//
// THERE IS ONE SHELL NOW: the runtime-networked entity was a CBaseEntity, step
// 6 makes CBaseEntity an alias for IGameEntity, and a class deriving from the
// alias is abstract -- so it was deleted rather than given a de-entified base
// of its own, which would have added more C++ than it removed.
//
// SS_SHELL_NETENTITY IS RETIRED, NOT REMOVED. THE VALUES ARE THE ABI --
// NetShellKind in NetSchema.cs sends these numbers -- so reusing 0, or
// renumbering STANDALONE down onto it, changes what one side means by a number
// the other reads. SourceSharp_NetPublishType refuses it by name on both sides,
// so an old managed build publishing on it is loud rather than quietly landing
// its types on the live shell's tables.
//-----------------------------------------------------------------------------
enum SourceSharpNetShell
{
	// RETIRED. The runtime-networked entity, whose whole table was generated.
	// A reserved value; nothing may publish on it.
	SS_SHELL_NETENTITY  = 0,

	// The parallel hierarchy, every entity of which is allocated one blob
	// whatever its class -- which is why the tier there is the shell's and not
	// the type's. The only shell.
	SS_SHELL_STANDALONE = 1,
};

//-----------------------------------------------------------------------------
// One tier: the class name the two sides pair on, the table name derived from
// it, and how many payload bytes it carries.
//
// The names are literals rather than "DT_" + a classname, because a tier's
// names are not derived from anything a generator produced — they are fixed for
// the life of the protocol, and spelling them out is what makes them greppable
// from either side.
//-----------------------------------------------------------------------------
struct SourceSharpTier_t
{
	// The names this tier's SendTable and RecvTable pair on. TWO per tier: it
	// was four while the two shells were unrelated C++ classes needing a
	// ServerClass each at every size, and the second pair went with the deleted
	// one.
	//
	// Spelled out rather than concatenated: a SendProp keeps the const char *
	// it was handed for the life of the process, and these want to be greppable
	// from either side.
	const char *pClass;
	const char *pTable;

	int         nBytes;
};

//-----------------------------------------------------------------------------
// How many REAL props sit in front of the payload: the rows of
// StandaloneNetFields, which standalone_nettable.h static_asserts (this stayed
// six when m_fEffects and m_ubInterpolationFrame joined, and is TEN now that
// the solidity pair — m_nSolidType, m_nSolidFlags — carries a managed prop's
// shape across for the client's impact clip, and ELEVEN since m_nSkin carries a
// skin the server picks, 2026-10-19). Not spelled
// `10 + bytes/4` at the call sites: NetTiers reads this across the ABI rather
// than projecting it, because a boundary rule with two implementations drifts.
//-----------------------------------------------------------------------------
enum
{
	SOURCESHARP_TIER_PROPS_STANDALONE = 11,
};

// Still a function of nShell, because nShell is ABI: NetTierInfo takes it from
// managed code, and a build asking about the retired shell must be told what
// the tables actually carry rather than nothing.
inline int SourceSharp_TierRealProps( int nShell )
{
	(void)nShell;
	return SOURCESHARP_TIER_PROPS_STANDALONE;
}

//-----------------------------------------------------------------------------
// HOW MANY TIERS THERE ARE, as a constant something can be sized from.
//
// SourceSharp_TierCount() is the same number and is what code should ASK; this
// is what an array is DECLARED with, because a static array needs its bound at
// compile time and every tier-indexed array in this tree used to be spelled
// [ 3 ] by hand. Six of them, in four files, in two modules, plus two
// create-function tables — and a row added to the list below with one of them
// missed would have written past the end of a static array during DLL init.
//
// The list below is DECLARED with this bound rather than deduced from its
// initialisers, which is what pins the two together without a macro this
// engine-free header would have to invent: one row too many does not compile,
// and bumping this without adding a row leaves a zero-filled tier that
// PayloadTiers.EveryTierIsNamed fails on. Both directions provoked, twice —
// once when the list went to five and again when it went to seven, because a
// guard that has not been re-provoked since the code it guards changed is a
// guard nobody has tested.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_TIER_MAX = 7 };

//-----------------------------------------------------------------------------
// The tiers, smallest first. 2048 is the largest because it is 512 payload props
// plus the handful of real ones, and MAX_DATATABLE_PROPS is 4096: a 4096-byte
// tier would need 1025 props of its own, which fits, but its ARRAY form would
// not have and the number the plan priced is this one.
//
// EVERY ROW IS A DOUBLING, and the list was 32, 256, 2048 — three rows spanning
// the same range in three steps of 8x. The four rows that fill it in were added
// for one reason each, measured rather than guessed, and the reason is worth
// keeping because it is the argument for any future row.
//
// 512 AND 1024 CAME FIRST. The jump from 256 straight to 2048 was the whole cost
// of one class: the hierarchy's greediest declaration is 260 bytes — four over
// the 256 ceiling — and with no 512 row that class alone moved to 2048.
//
// 64 AND 128 CAME SECOND, AND ARE WORTH MORE. With per-class tiers the 32-to-256
// gap is where the mod actually lives: nine of the twenty classes that declare
// anything are 40 to 68 bytes, and one of them — ss_beam, 52 bytes — is 126 of
// the 1064 hierarchy entities on ss_sandbox. Every one of those paid 256 bytes
// and 70 props for a declaration that fits in 64 or 128.
//
// It is still true per class that a type one byte over a boundary pays for the
// next tier whole. What changed is that the next tier is now 2x rather than 8x
// everywhere, so the worst case a class can suffer is bounded at double its own
// size rather than eight times it.
//
// Ordered smallest first and searched in order, so "the smallest tier that fits"
// is the search rather than a rule applied to the answer.
//-----------------------------------------------------------------------------
inline const SourceSharpTier_t *SourceSharp_Tiers( int *pOutCount )
{
	static const SourceSharpTier_t s_Tiers[ SOURCESHARP_TIER_MAX ] =
	{
		{ "CSourceSharpStdEnt32",   "DT_SourceSharpStdEnt32",     32 },
		{ "CSourceSharpStdEnt64",   "DT_SourceSharpStdEnt64",     64 },
		{ "CSourceSharpStdEnt128",  "DT_SourceSharpStdEnt128",   128 },
		{ "CSourceSharpStdEnt256",  "DT_SourceSharpStdEnt256",   256 },
		{ "CSourceSharpStdEnt512",  "DT_SourceSharpStdEnt512",   512 },
		{ "CSourceSharpStdEnt1024", "DT_SourceSharpStdEnt1024", 1024 },
		{ "CSourceSharpStdEnt2048", "DT_SourceSharpStdEnt2048", 2048 },
	};

	// EVERY ARRAY INDEXED BY TIER IS SIZED FROM SOURCESHARP_TIER_MAX, and the
	// declared bound above is what makes adding a row here safe. Three tiers
	// became five and then seven, and there are six fixed-size arrays and two
	// create-function tables keyed by tier across the two modules; a row added
	// without all of them growing would have written past the end of a static
	// array during DLL init, which is the kind of corruption that surfaces
	// somewhere else entirely.

	if ( pOutCount )
		*pOutCount = (int)( sizeof( s_Tiers ) / sizeof( s_Tiers[0] ) );

	return s_Tiers;
}

inline int SourceSharp_TierCount( void )
{
	int nCount = 0;
	SourceSharp_Tiers( &nCount );
	return nCount;
}

//-----------------------------------------------------------------------------
// The index of the smallest tier a payload of nBytes fits in, or -1.
//
// -1 for a size no tier carries, and -1 for a negative one. Never a clamp: a
// type quietly assigned to a tier too small for it would have its tail fields
// simply absent from the wire, decoding as whatever the client's blob happened
// to hold. Refusing is loud at publish time; clamping is silent forever.
//
// Zero bytes is tier 0 rather than "no tier". A type that declares no networked
// state is a real thing — most of the parallel hierarchy is exactly that — and
// it belongs in the smallest tier, not outside the scheme.
//-----------------------------------------------------------------------------
inline int SourceSharp_TierForSize( int nBytes )
{
	if ( nBytes < 0 )
		return -1;

	int nCount = 0;
	const SourceSharpTier_t *pTiers = SourceSharp_Tiers( &nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		if ( nBytes <= pTiers[i].nBytes )
			return i;
	}

	return -1;
}

//-----------------------------------------------------------------------------
// The tier a payload of nBytes lands in, or NULL. The pointer form, for callers
// that want the names rather than the index.
//-----------------------------------------------------------------------------
inline const SourceSharpTier_t *SourceSharp_TierForSizePtr( int nBytes )
{
	const int iTier = SourceSharp_TierForSize( nBytes );
	if ( iTier < 0 )
		return NULL;

	return SourceSharp_Tiers( NULL ) + iTier;
}

//-----------------------------------------------------------------------------
// How many BYTES a tier carries, or 0 for an index that is not a tier.
//
// Zero rather than -1, and that is deliberate: every caller is about to use it
// as a length, and a length of zero reads and writes nothing while a length of
// -1 is an enormous unsigned number one memcpy away from the heap. The callers
// that need to tell "no tier" from "the smallest tier" ask SourceSharp_TierCount
// or hold the index.
//-----------------------------------------------------------------------------
inline int SourceSharp_TierBytes( int iTier )
{
	if ( iTier < 0 || iTier >= SourceSharp_TierCount() )
		return 0;

	return SourceSharp_Tiers( NULL )[ iTier ].nBytes;
}

//-----------------------------------------------------------------------------
// How many payload props a tier carries. Every tier is a whole number of words
// by construction, so this never rounds — but it goes through the same word
// count everything else does, so a tier size that stopped being a multiple of
// four would be carried whole rather than truncated.
//-----------------------------------------------------------------------------
inline int SourceSharp_TierWords( int iTier )
{
	if ( iTier < 0 || iTier >= SourceSharp_TierCount() )
		return -1;

	return SourceSharpPayload_WordCount( SourceSharp_Tiers( NULL )[ iTier ].nBytes );
}

//-----------------------------------------------------------------------------
// A tier's names. Still takes nShell — ABI, see SourceSharp_TierRealProps — and
// one shell answers for every value of it.
//
// "StdEnt" rather than "Standalone" in the stem, and that is not cosmetic:
// DATATABLE NAMES ARE MATCHED CASE-INSENSITIVELY (a managed DT_func_lod once
// paired with stock's DT_Func_LOD and killed the join), so a tier stem that is a
// prefix of the shared table this replaces — DT_SourceSharpStandalone — is a
// name collision waiting for somebody to add a suffix. A distinct stem is
// cheaper than a rule about suffixes.
//-----------------------------------------------------------------------------
inline const char *SourceSharp_TierClassName( int nShell, int iTier )
{
	(void)nShell;

	if ( iTier < 0 || iTier >= SourceSharp_TierCount() )
		return 0;

	return SourceSharp_Tiers( 0 )[ iTier ].pClass;
}

inline const char *SourceSharp_TierTableName( int nShell, int iTier )
{
	(void)nShell;

	if ( iTier < 0 || iTier >= SourceSharp_TierCount() )
		return 0;

	return SourceSharp_Tiers( 0 )[ iTier ].pTable;
}

//-----------------------------------------------------------------------------
// THE ONE HASH (Part 6). FNV-1a over the classname's bytes.
//
// m_nTypeId is NOT an index into anything. It is this function of the
// classname, computed independently on each side from the same string, so
// there is no ordering to agree and nothing to drift — which is the objection
// Part 6 raised against an index, answered rather than ignored.
//
// Pinned against NetSchema.TypeIdOf by a fixed corpus in both test suites. The
// two are the same arithmetic in two languages and that is exactly the shape
// this tree keeps getting bitten by, so it is checked at startup as well: the
// publish call hands back the id native computed and C# compares it with its
// own before anything is on the wire.
//-----------------------------------------------------------------------------
inline unsigned int SourceSharp_HashClassName( const char *pName )
{
	const unsigned int nOffsetBasis = 2166136261u;
	const unsigned int nPrime       = 16777619u;

	unsigned int nHash = nOffsetBasis;

	for ( const char *p = pName; p && *p; p++ )
	{
		nHash ^= (unsigned int)(unsigned char)*p;
		nHash *= nPrime;
	}

	return nHash;
}

//=============================================================================//
// The published type table.
//
// What m_szClassname used to carry in 64 bytes, in four: an entity says its
// TYPE ID and the receiving side looks the rest up here. The table is built at
// startup from C#'s publish calls, identically on both sides because both read
// the same shared assembly, and it is not on the wire in any form.
//
// It answers three questions and each has a caller that cannot do without it:
//
//   id -> classname     the client's managed pairing, which used to read the
//                       replicated string.
//   id -> schema hash   the rejection path. An entity carrying a hash that
//                       disagrees with this side's registry is a build
//                       mismatch, and its bytes are refused rather than
//                       decoded as somebody else's layout.
//   name -> tier        which ServerClass an entity of that class is created
//                       through.
//
// COLLISIONS ARE CAUGHT HERE, once, at startup. Two names hashing alike is a
// build-time fact about a fixed set of strings, so catching it at startup
// catches it forever — and the failure it prevents is one entity decoding as
// another, which has no other symptom.
//
// Header-only and engine-free, for the reason the arithmetic above is: this is
// the part that can be quietly wrong.
//=============================================================================//
enum { SOURCESHARP_TYPES_MAX = 512 };
enum { SOURCESHARP_TYPE_NAME_BYTES = 64 };

struct SourceSharpNetType_t
{
	char         szClass[ SOURCESHARP_TYPE_NAME_BYTES ];
	unsigned int nTypeId;
	unsigned int nSchemaHash;

	// The class's OWN declared layout, which is what the inspector reports and
	// is not the same number as its tier's payload: on the hierarchy the tier
	// is the family's and this is the class's.
	int          nLayoutBytes;

	int          nShell;
	int          iTier;
};

class CSourceSharpTypeTable
{
public:
	CSourceSharpTypeTable() : m_nCount( 0 ), m_nCollisions( 0 ) {}

	//-------------------------------------------------------------------------
	// Records a type. Returns the index, or -1 for a name that will not fit, a
	// table that is full, or a COLLISION — a different name with the same id.
	//
	// Re-publishing the same name is an update rather than a duplicate: a
	// managed reload publishes again, and a second row for one name would make
	// every later lookup depend on which one it found first.
	//-------------------------------------------------------------------------
	int Publish( const char *pClassName, int nShell, unsigned int nSchemaHash,
	             int nLayoutBytes, int iTier )
	{
		if ( !pClassName || !pClassName[0] )
			return -1;

		int nLen = 0;
		while ( pClassName[ nLen ] )
			nLen++;

		if ( nLen >= SOURCESHARP_TYPE_NAME_BYTES )
			return -1;

		const unsigned int nId = SourceSharp_HashClassName( pClassName );

		for ( int i = 0; i < m_nCount; i++ )
		{
			if ( Same( m_Types[i].szClass, pClassName ) )
			{
				m_Types[i].nSchemaHash  = nSchemaHash;
				m_Types[i].nLayoutBytes = nLayoutBytes;
				m_Types[i].nShell       = nShell;
				m_Types[i].iTier        = iTier;
				return i;
			}

			// Two DIFFERENT names hashing alike. Refused rather than recorded:
			// the second one would be reachable by no lookup and the first
			// would answer for both, which is one entity decoding as another.
			if ( m_Types[i].nTypeId == nId )
			{
				m_nCollisions++;
				return -1;
			}
		}

		if ( m_nCount >= SOURCESHARP_TYPES_MAX )
			return -1;

		SourceSharpNetType_t &type = m_Types[ m_nCount ];

		for ( int i = 0; i <= nLen; i++ )
			type.szClass[i] = pClassName[i];

		type.nTypeId      = nId;
		type.nSchemaHash  = nSchemaHash;
		type.nLayoutBytes = nLayoutBytes;
		type.nShell       = nShell;
		type.iTier        = iTier;

		return m_nCount++;
	}

	const SourceSharpNetType_t *ById( unsigned int nTypeId ) const
	{
		for ( int i = 0; i < m_nCount; i++ )
		{
			if ( m_Types[i].nTypeId == nTypeId )
				return m_Types + i;
		}

		return 0;
	}

	const SourceSharpNetType_t *ByName( const char *pClassName ) const
	{
		if ( !pClassName )
			return 0;

		for ( int i = 0; i < m_nCount; i++ )
		{
			if ( Same( m_Types[i].szClass, pClassName ) )
				return m_Types + i;
		}

		return 0;
	}

	int Count( void ) const { return m_nCount; }
	int Collisions( void ) const { return m_nCollisions; }

	const SourceSharpNetType_t *At( int i ) const
	{
		return ( i >= 0 && i < m_nCount ) ? m_Types + i : 0;
	}

	void Reset( void ) { m_nCount = 0; m_nCollisions = 0; }

private:
	// Case-sensitive, deliberately. The id is the hash of the exact bytes, so
	// two spellings that differ in case are two different ids however this
	// compares them — matching them here would hand back a row whose id no
	// entity will ever carry.
	static bool Same( const char *pA, const char *pB )
	{
		while ( *pA && *pA == *pB )
		{
			pA++;
			pB++;
		}

		return *pA == *pB;
	}

	SourceSharpNetType_t m_Types[ SOURCESHARP_TYPES_MAX ];
	int m_nCount;
	int m_nCollisions;
};

//=============================================================================//
// WHAT A MANAGED RELOAD MOVES, AND WHAT IT DOES NOT.
// plan_nettable_generic.md Step 10.
//
// A reload republishes every type, and Publish above updates a row in place.
// The row is not the only copy of a schema hash, though: every LIVE entity
// carries its own, stamped once at its first GetServerClass and never looked at
// again, because it is a fact about the class rather than the instance and a
// proxy per encode is the cost Part 1A rules out.
//
// So after a reload that reshaped a type, the registry says one thing and every
// entity of that type says another. The receiving side compares the ENTITY's
// hash against ITS registry's, so the disagreement is a refusal — of correct
// bytes, permanently, until the entity dies. The stamp has to be redone.
//
// The decision is here rather than in the walk that applies it because it is the
// part that can be quietly wrong, and it is testable with no engine under it:
//
//   - A class the registry does not know is LEFT ALONE. It is the same fact
//     GetServerClass reports as "no published type", and re-stamping it to zero
//     would turn a class that merely went away into an entity whose payload is
//     refused on a build where nothing had changed.
//   - A hash that did not move is left alone, which is the normal case and the
//     whole of what makes a reload cheap: an entity's blob survives untouched
//     when its layout did.
//   - A hash that moved is the reshape. The blob's bytes were written through
//     the OLD layout and mean nothing under the new one, so re-stamping alone
//     would replace a refusal with a silent misdecode — which is strictly
//     worse. The caller zeroes the blob as well, and the pair is what makes the
//     entity honest rather than merely accepted.
//-----------------------------------------------------------------------------
inline bool SourceSharp_SchemaNeedsRestamp( const CSourceSharpTypeTable &types,
                                            const char *pClassName,
                                            unsigned int nStamped,
                                            unsigned int *pOutWanted )
{
	if ( pOutWanted )
		*pOutWanted = nStamped;

	const SourceSharpNetType_t *pType = types.ByName( pClassName );

	if ( !pType )
		return false;

	if ( pOutWanted )
		*pOutWanted = pType->nSchemaHash;

	return pType->nSchemaHash != nStamped;
}

//-----------------------------------------------------------------------------
// This module's table. One per module, like the shared payload beside it: the
// client and the server each build their own from the same C# list.
//-----------------------------------------------------------------------------
CSourceSharpTypeTable &SourceSharp_NetTypes( void );
