//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the standalone entity's own experiment =============//
// SS_REASON f  a bit-level wire codec
//
// ss_barrel, and the ServerClass every networked standalone entity shares.
//
// This file is the one that proved the parallel hierarchy end to end — a native
// shell that is not a CBaseEntity, created by a map, addressed by name,
// networked to a client, and driven entirely from C#. The map entities that
// moved onto the hierarchy afterwards speak the contract the C#-backed entity
// classes have always spoken, which is in shell_dispatch.cpp; this
// one keeps its own, because what it is testing is the wire rather than an
// entity anybody plays with.
//
// It registers the ServerClass every networked entity on the hierarchy shares.
// That used to be gated behind -ss_standalone, because a ServerClass with no
// matching client class makes the game unjoinable and says nothing about it —
// see the note near the end of this file for why the gate is gone, and for why
// this file has no game system left at all.
//
//=============================================================================//

#include "shell_standalone.h"
#include "shell_standalone_player.h"
#include "standalone_nettable.h"
#include "generic_nettable.h"
#include "generic_payload.h"
#include "sourcesharp.h"

#include <vector>


std::vector< CStandaloneEntity * > &StandaloneEntities();

//-----------------------------------------------------------------------------
// The table and the class, built by hand at runtime.
//
// Exactly the technique shell_nettable.cpp already proved: a SendTable is a
// runtime object and a ServerClass links itself into the list the engine walks
// later, so neither needs a macro and neither needs CBaseEntity.
//-----------------------------------------------------------------------------
// StandaloneByName, the by-name walk cs_standalone_parent resolves its two
// arguments with, went with that command to shell_standalone_debug.cpp: it was
// the only caller.



//-----------------------------------------------------------------------------
// Built at DLL init, and that timing is the first real finding of this
// experiment.
//
// A ServerClass links itself into g_pServerClassHead when it is constructed,
// and the engine walks that list ONCE, through CServerGameDLL::GetAllServerClasses,
// to build the class list it negotiates with every client at connect. Building
// one later does not fail, warn, or do anything visible — the class simply is
// not in the list, and the first entity that claims it sends the engine into a
// table that was never flattened.
//
// The first attempt built it lazily inside the console command. The game got as
// far as printing the edict number and then jumped to address zero inside the
// engine's frame loop.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The four props every entity on the hierarchy sends, appended to an array.
//
// Written once and used twice — by the shared class below and by each per-class
// table — because a per-class table that spelled these out again would be a
// second copy of the thing standalone_nettable.h exists to make singular. They
// go FIRST in every table, so a class that adds fields extends rather than
// renumbers.
//-----------------------------------------------------------------------------
static void BuildStandaloneProps( SendProp *pOut, int &nOut )
{
	CStandaloneEntity *pNull = NULL;

	// The offsets of OUR members, in the shared list's order. Everything else
	// about the table — the names, the order, the encoding — comes from
	// standalone_nettable.h, so the client cannot disagree about any of it.
	int nOffsets[ STANDALONE_NET_FIELD_COUNT ];
	// THE FIRST FOUR ARE IN THE ENGINE BLOCK (plan_entity.md §3.2, step 1b),
	// which is the fixed prefix of the blob rather than a member of the shell,
	// so their offsets are the block's base plus their offset inside it.
	//
	// The SAME ARITHMETIC CStandaloneEntity::MarkBlockChanged uses, and it has
	// to be: the engine matches a recorded change offset against a SendProp's
	// offset exactly, so a prop built one way and a mark computed another would
	// leave every partial update on the local backdoor matching nothing.
	nOffsets[ STANDALONE_NET_FIELD_ORIGIN ]       = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( Origin );
	nOffsets[ STANDALONE_NET_FIELD_ANGLES ]       = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( Angles );
	nOffsets[ STANDALONE_NET_FIELD_MODELINDEX ]   = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( ModelIndex );
	// AND THE FOURTH, since step 8e-render: the EF_* bits are a block field too
	// (EngineBlockHead.Effects), and they are sent by the same arithmetic rather
	// than out of a shell member, so SetVisible's MarkBlockChanged offset and
	// this prop's offset are the same number by construction.
	nOffsets[ STANDALONE_NET_FIELD_EFFECTS ]      = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( Effects );
	// AND THE FIFTH: that the entity JUMPED rather than moved. A block field
	// like the four above, and for the same reason -- the value is written by
	// MANAGED code (SourceSharp.Abi.TeleportDiscontinuity, on the managed side
	// of every teleport crossing) and announced through
	// SourceSharp_EntityMarkBlockChanged with this same offset, so the mark and
	// the prop are the same number by construction. Nothing native writes it.
	nOffsets[ STANDALONE_NET_FIELD_INTERPFRAME ]  = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( InterpolationFrame );
	nOffsets[ STANDALONE_NET_FIELD_TYPEID ]       = (int)( (intp)&pNull->m_nTypeId );
	nOffsets[ STANDALONE_NET_FIELD_SCHEMAHASH ]   = (int)( (intp)&pNull->m_nSchemaHash );
	nOffsets[ STANDALONE_NET_FIELD_PAYLOADWORDS ] = (int)( (intp)&pNull->m_nPayloadWords );
	// THE SOLIDITY PAIR, at the tail with their list rows. Block fields again —
	// the SAME ARITHMETIC CStandaloneEntity::SetSolidity's MarkBlockChanged uses
	// (shell_standalone_base.cpp), so the change it marks matches these props
	// exactly, and a prop built one way and a mark computed another would leave
	// every solidity change invisible to the delta pass.
	nOffsets[ STANDALONE_NET_FIELD_SOLIDTYPE ]    = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( SolidType );
	nOffsets[ STANDALONE_NET_FIELD_SOLIDFLAGS ]   = CStandaloneEntity::BlockBase()
	                                              + SS_BLOCK_HEAD_OFFSET( SolidFlags );

	int nCount = 0;
	const StandaloneNetField_t *pFields = StandaloneNetFields( &nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		switch ( pFields[i].kind )
		{
		case STANDALONE_NET_VECTOR:
			pOut[ nOut++ ] = SendPropVector( pFields[i].pName, nOffsets[i], 12, 32, SPROP_NOSCALE );
			break;

		case STANDALONE_NET_INT:
			pOut[ nOut++ ] = SendPropInt( pFields[i].pName, nOffsets[i], 4,
			                              pFields[i].nBits, SPROP_UNSIGNED );
			break;

		case STANDALONE_NET_STRING:
			pOut[ nOut++ ] = SendPropString( pFields[i].pName, nOffsets[i], pFields[i].nBits );
			break;
		}
	}
}

//=============================================================================//
// The tiers (plan_nettable_generic.md Part 7 and Part 14 Step 6).
//
// ONE ServerClass PER TIER for the whole hierarchy, where there used to be one
// shared class plus one per class that declared state. Each is the eight real
// props above followed by that tier's payload words, and every entity here is
// created through whichever one ITS OWN CLASS's declaration lands in.
//
// Every tier is registered whether or not any class is on it, and that is the
// point rather than an oversight: the class list is what the engine negotiates
// at connect, so a tier that is not in it cannot be moved to without a client
// that was rebuilt. Registering the full set is what makes reshaping a C# type
// inside the design a pure managed change.
//
// They go on the wire in the DLL-init window like everything else. A
// ServerClass links itself into g_pServerClassHead when constructed and the
// engine walks that list exactly once.
//=============================================================================//
static SendProp    *g_pTierProps[ SOURCESHARP_TIER_MAX ]   = { NULL };
static SendTable   *g_pTierTables[ SOURCESHARP_TIER_MAX ]  = { NULL };
static ServerClass *g_pTierClasses[ SOURCESHARP_TIER_MAX ] = { NULL };
static bool         g_bTiersRegistered = false;

static void RegisterStandaloneTiers( void )
{
	if ( g_bTiersRegistered )
		return;

	g_bTiersRegistered = true;

	int nShared = 0;
	StandaloneNetFields( &nShared );

	const int nBlobBase = SourceSharp_StandaloneBlobOffset();

	// The player headroom, priced out loud (Phase 9a): one blob offset serves
	// the whole hierarchy, so every entity's allocation clears the LARGEST
	// shell — the player — and the difference is dead space on everything
	// that is not one. A quiet per-entity cost deserves a number beside it.
	Msg( "[SourceSharp] standalone: blob offset %d (entity shell %d, player shell %d)\n",
		nBlobBase, (int)sizeof( CStandaloneEntity ), (int)sizeof( CStandalonePlayer ) );

	for ( int iTier = 0; iTier < SourceSharp_TierCount(); iTier++ )
	{
		const int nWords = SourceSharp_TierWords( iTier );
		const char *pClassName = SourceSharp_TierClassName( SS_SHELL_STANDALONE, iTier );
		const char *pTableName = SourceSharp_TierTableName( SS_SHELL_STANDALONE, iTier );

		if ( nWords < 0 || !SourceSharpPayload_OffsetsFit( nBlobBase, nWords ) )
		{
			Warning( "[SourceSharp] standalone tier '%s': %d word(s) will not fit at blob "
			         "base %d; NOT registered — every entity on the hierarchy would then "
			         "have no class at all\n", pTableName, nWords, nBlobBase );
			continue;
		}

		g_pTierProps[ iTier ] = new SendProp[ nShared + nWords ];

		int nBuilt = 0;
		BuildStandaloneProps( g_pTierProps[ iTier ], nBuilt );

		if ( SourceSharpPayload_BuildSendProps( g_pTierProps[ iTier ] + nBuilt,
		                                        nWords, nBlobBase ) != nWords )
		{
			Warning( "[SourceSharp] standalone tier '%s': payload build refused %d word(s); "
			         "not registered\n", pTableName, nWords );
			delete[] g_pTierProps[ iTier ];
			g_pTierProps[ iTier ] = NULL;
			continue;
		}

		nBuilt += nWords;

		g_pTierTables[ iTier ]  = new SendTable( g_pTierProps[ iTier ], nBuilt, pTableName );
		g_pTierClasses[ iTier ] = new ServerClass( pClassName, g_pTierTables[ iTier ] );

		// Counted rather than dumped per line: one word of five hundred on the
		// engine's slow local-transfer list still arrives, still decodes and
		// still passes every test in this tree — it just costs a proxy call and
		// a bit-encode per word per frame on every listen server, which is every
		// headless run this project makes (Part 1A).
		int nSlow = 0;

		for ( int i = 0; i < nWords; i++ )
		{
			if ( !SourceSharpPayload_IsFastSendProxy( g_pTierTables[ iTier ]->GetProp( nShared + i ) ) )
				nSlow++;
		}

		Msg( "[SourceSharp] standalone tier %d '%s' -> '%s': %d real + %d payload word(s) "
		     "= %d prop(s), %d payload byte(s) at blob base %d\n",
			iTier, pClassName, pTableName, nShared, nWords, nBuilt,
			SourceSharp_Tiers( NULL )[ iTier ].nBytes, nBlobBase );

		if ( nSlow > 0 )
		{
			Warning( "[SourceSharp] standalone tier '%s': %d of %d payload word(s) did NOT "
			         "resolve to m_UInt32ToInt32 and would be copied slowly, proxies and "
			         "all\n", pTableName, nSlow, nWords );
		}
	}

	// THE FLOOR IS PER ENTITY NOW (Part 18), and is applied where the
	// allocation happens rather than to a global here: CStandaloneEntity::
	// operator new refuses anything shorter than tier 0. A build whose C#
	// declared nothing still gets tier 0's payload rather than a zero-length
	// blob with props reading past the object, which is what this line bought.
}

//-----------------------------------------------------------------------------
// One of the two answers behind SourceSharp_IsManagedServerClass (Part 5's
// meter). These tiers exist because C# declared a hierarchy: the count, the
// widths and the classes on them all come from managed type declarations, so
// they are managed registrations even though the ServerClass object is built
// here.
//
// By identity against the array that already holds them rather than by name:
// a name test would have to agree with SourceSharp_TierClassName's format
// forever, and a rename would silently move a class from one column of the
// meter to the other.
//-----------------------------------------------------------------------------
bool SourceSharp_IsStandaloneTierServerClass( const ServerClass *pClass )
{
	if ( pClass == NULL )
		return false;

	for ( int i = 0; i < SOURCESHARP_TIER_MAX; i++ )
	{
		if ( g_pTierClasses[ i ] == pClass )
			return true;
	}

	return false;
}


//-----------------------------------------------------------------------------
// WHETHER ANY PUBLISHED CLASS OF A SHELL IS ON A TIER (Part 18).
//
// This is what "live" means now, and it had to change shape rather than change
// its answer. It used to be "iTier == the hierarchy's one tier", which cannot
// express two tiers being in use at once — and per-class tiers make that the
// ordinary state, so the old form would have reported one live tier out of
// several and the dev UI would have shown the others as empty.
//
// A READING of the published type table, which is the record of what was
// actually assigned, rather than a second opinion computed from sizes. Registered
// and empty is still the ordinary state and still means "room to grow into".
//-----------------------------------------------------------------------------
static bool SourceSharp_TierHasPublishedTypes( int nShell, int iTier )
{
	const CSourceSharpTypeTable &types = SourceSharp_NetTypes();

	for ( int i = 0; i < types.Count(); i++ )
	{
		const SourceSharpNetType_t *pType = types.At( i );

		if ( pType && pType->nShell == nShell && pType->iTier == iTier )
			return true;
	}

	return false;
}



//-----------------------------------------------------------------------------
// The hierarchy's shared blob size, declared by C# before any class on it is
// described (plan_nettable_generic.md Part 14 Step 4).
//
// This is the one number that makes the generic payload expressible here at
// all. Every entity on this hierarchy is allocated the same blob length, so the
// payload word count is a property of the HIERARCHY; the per-class shape the
// runtime-networked entity uses cannot say that, and deriving it per class from
// the blob size at that moment gets a different answer for the first class than
// for the last.
//
// It also fixes the blob length up front instead of growing it a class at a
// time. That was harmless while only typed props existed — a prop reads its own
// class's offsets and a longer blob is slack — and it is not harmless now,
// because the payload props of the FIRST class described would be sized to a
// blob that later classes grew.
//
// Returns the word count both sides will carry. See the ABI comment for why C#
// is the one that knows it.
//-----------------------------------------------------------------------------
int32_t SourceSharp_NetHierarchyDeclare( int32_t nBlobBytes )
{
	// The tables have to exist before the number can pick one of them, and this
	// is the first call in the managed startup sequence that touches the
	// hierarchy at all. Idempotent, so the gamesystem's Init getting here first
	// changes nothing.
	RegisterStandaloneTiers();

	// STEP 6: THE BLOB IS ROUNDED UP TO ITS TIER, not left at what C# declared.
	//
	// That is the whole difference between "a payload" and "a tier". Sized to
	// the declaration, a type growing by one field needs both sides rebuilt;
	// sized to the tier, everything up to the tier's ceiling is a pure managed
	// change and the class list never moves.
	//
	// PART 18: THIS NO LONGER PICKS THE TIER FOR ANYBODY. It is the ceiling
	// check and nothing else — "does the greediest declaration on the hierarchy
	// still fit a tier at all" — because each class is now sized to its own.
	// The number it returns is what the greediest class will carry, which is
	// what the managed log line reports and is a genuine fact about the build.
	const int iTier = SourceSharp_TierForSize( nBlobBytes );

	if ( iTier < 0 )
	{
		Warning( "[SourceSharp] standalone: %d declared blob byte(s) exceeds the largest tier "
		         "(%d); that class has NO table it can use and its entities will not "
		         "replicate\n", nBlobBytes,
			SourceSharp_TierBytes( SourceSharp_TierCount() - 1 ) );
		return -1;
	}

	const int nWords = SourceSharp_TierWords( iTier );

	Msg( "[SourceSharp] standalone: the greediest declaration is %d byte(s) -> tier %d '%s' "
	     "(%d bytes, %d payload word(s)); every OTHER class is sized to its own tier, "
	     "blob base %d\n",
		nBlobBytes, iTier, SourceSharp_TierTableName( SS_SHELL_STANDALONE, iTier ),
		SourceSharp_TierBytes( iTier ), nWords, SourceSharp_StandaloneBlobOffset() );

	return nWords;
}

//-----------------------------------------------------------------------------
// C# publishing one managed type (plan_nettable_generic.md Part 12, Step 6).
//
// The three-call description ABI said what every FIELD was; this says only that
// a type exists, what its layout fingerprints to, and how big it is. Native has
// no opinion about what a field is any more, which is the point of the whole
// exercise — the type table is a name, an id and a hash.
//
// THE TIER IT ANSWERS WITH IS THIS TYPE'S, on both shells (Part 18). Part 7 says
// the tier comes from EntityLayout.Size at publish time, and that is now simply
// true rather than true of one shell only.
//
// It was the shell's on the hierarchy, for a reason that has been removed rather
// than worked around: every entity there was allocated one blob whatever its
// class, so two classes on one blob could not be on two tiers without one of
// them having props past the end of the object. The factory sizes each entity to
// its own class now, so no two classes share a blob and the objection is gone
// with the sharing.
//
// pOutTypeId hands back the id NATIVE computed from the name, so C# can compare
// it against its own before anything is on the wire. Two implementations of
// FNV-1a in two languages is precisely the drift this tree keeps paying for,
// and a test pins it — but a test pins the corpus somebody thought of, and this
// pins every name the build actually uses.
//-----------------------------------------------------------------------------
int32_t SourceSharp_NetPublishType( const char *pClassName, int32_t nShell,
                                    uint32_t nSchemaHash, int32_t nLayoutBytes,
                                    uint32_t *pOutTypeId )
{
	if ( pOutTypeId )
		*pOutTypeId = pClassName ? SourceSharp_HashClassName( pClassName ) : 0;

	if ( !pClassName || !pClassName[0] )
		return -1;

	// The retired shell is refused BY NAME rather than quietly taken for the
	// live one. Mirror of the client's in shell_recvtable.cpp; keep the two
	// identical.
	if ( nShell == SS_SHELL_NETENTITY )
	{
		Warning( "[SourceSharp] type '%s': shell %d is the RETIRED runtime-networked "
		         "entity, which no longer exists; NOT published. A managed build "
		         "publishing on it is older than this native one.\n",
			pClassName, (int)nShell );
		return -1;
	}

	RegisterStandaloneTiers();

	// THE TIER IS THE TYPE'S, ON BOTH SHELLS NOW (Part 18).
	//
	// This used to answer the hierarchy's single tier for a standalone type,
	// and the comment below said why: every entity there was allocated one blob
	// whatever its class, so two classes on one blob could not be on two tiers.
	// Per-class allocation removes the premise — no two classes share a blob any
	// more — so the answer is the same rule the other shell always used.
	const int iTier = SourceSharp_TierForSize( nLayoutBytes );

	if ( iTier < 0 )
	{
		Warning( "[SourceSharp] type '%s': %d layout byte(s) exceeds the largest tier; "
		         "NOT published, and its entities will carry no state\n",
			pClassName, nLayoutBytes );
		return -1;
	}

	if ( SourceSharp_NetTypes().Publish( pClassName, nShell, nSchemaHash, nLayoutBytes, iTier ) < 0 )
	{
		// The only interesting way this fails is a COLLISION — two classnames
		// with the same FNV-1a. It is a build-time fact about a fixed set of
		// strings, so saying so once at startup says so forever, and the
		// failure it prevents is one entity decoding as another with nothing
		// anywhere reporting it.
		Warning( "[SourceSharp] type '%s' (id 0x%08x) was REFUSED by the type table: either a "
		         "name collision with a type already published, or the table is full "
		         "(%d entries). Its entities cannot be paired with a managed class.\n",
			pClassName, SourceSharp_HashClassName( pClassName ), SOURCESHARP_TYPES_MAX );
		return -1;
	}

	return iTier;
}

//-----------------------------------------------------------------------------
// One tier, as native actually registered it (Part 14 Step 6's note from Step
// 2). NetTiers used to be a projection of the boundary rule in C#; this is what
// it reads instead.
//
// Reports the props NATIVE BUILT rather than a formula. Part 3's "6 +
// payloadBytes/4" is right for the hierarchy and wrong for the other shell,
// which puts three real props in front of its payload rather than six — and a
// formula that was right for one of them would have been quietly wrong about
// the other forever.
//-----------------------------------------------------------------------------
int32_t SourceSharp_NetTierInfo( int32_t nShell, int32_t iTier, const char **ppOutTable,
                                 int32_t *pOutBytes, int32_t *pOutWords,
                                 int32_t *pOutProps, int32_t *pOutLive )
{
	RegisterStandaloneTiers();

	if ( iTier < 0 || iTier >= SourceSharp_TierCount() )
		return 0;

	const int nWords = SourceSharp_TierWords( iTier );
	const int nReal  = SourceSharp_TierRealProps( nShell );

	if ( ppOutTable )
		*ppOutTable = SourceSharp_TierTableName( nShell, iTier );
	if ( pOutBytes )
		*pOutBytes = SourceSharp_Tiers( NULL )[ iTier ].nBytes;
	if ( pOutWords )
		*pOutWords = nWords;
	if ( pOutProps )
		*pOutProps = nReal + nWords;

	// "Live" is the tier entities of that shell are actually CREATED THROUGH,
	// which is not the same as the tier being registered. Every tier is
	// registered so the class list the handshake negotiates is the full set and
	// a type can grow into a bigger one without a rebuilt client — so a
	// registered, empty tier is the ordinary state and means "room to grow
	// into". A panel that showed every tier as live would be reporting the
	// registration and calling it the assignment.
	//
	// MORE THAN ONE CAN BE LIVE AT ONCE SINCE PART 18, on both shells, which is
	// why this is a scan of the published type table rather than a comparison
	// against one remembered index.
	if ( pOutLive )
	{
		*pOutLive = SourceSharp_TierHasPublishedTypes( nShell, iTier ) ? 1 : 0;
	}

	return 1;
}

//-----------------------------------------------------------------------------
// WHAT REPLACED THE AGREEMENT ORACLE, and why it had to be replaced rather
// than dropped.
//
// Steps 3 to 5 cross-checked the payload against the typed props: on the server
// by reading each payload word back through its own SendProp's offset and
// comparing it with the blob, and on the client by giving the payload a shadow
// buffer of its own and comparing the two. Step 6 deletes the typed props, so
// both halves of that comparison lose their other side. "The check passed and
// then I deleted the check" is exactly the shape this plan exists to avoid.
//
// Three things stand in its place, and between them they cover what the oracle
// covered:
//
// 1. THE PROBE. cs_sv_payload_probe writes a deterministic pattern into a live
//    entity's blob — word i gets a hash of (i, entity index) — and the client's
//    cs_cl_payload_probe recomputes the same pattern and compares it against
//    what arrived, IN CODE, reporting the first word that differs. That is the
//    end-to-end fact the shadow was standing in for, and it is stronger: the
//    shadow proved two prop sets on ONE side agreed, and this proves the bytes
//    the server wrote are the bytes the client holds. It can fail, and the
//    negative control is to probe with a deliberately wrong seed.
//
// 2. THE SCHEMA HASH, which is new here and permanent. Every update carries the
//    fingerprint of the layout it was written with, and the receiving side
//    refuses bytes whose fingerprint it does not recognise. The oracle it
//    replaces could only ever have caught a disagreement between two things in
//    ONE build; this catches a disagreement between two builds, which is the
//    failure that was never covered at all.
//
// 3. THE SERVER-SIDE OFFSET CHECK BELOW, kept. It still reads each payload word
//    through pProp->GetOffset() as the engine will use it — but its right-hand
//    side is now the blob rather than a typed prop, which makes it a check that
//    the TABLE points into the object where the object thinks its blob is.
//    That is a real and separate fact from the probe, and it is the one that
//    fails if the blob base and the prop offsets ever part company.
//
// Returns false with *pOutWords == 0 for an entity whose class carries no
// payload, which is not a disagreement.
//-----------------------------------------------------------------------------
bool SourceSharp_StandalonePayloadAgrees( CStandaloneEntity *pEntity,
                                          int *pOutWords, int *pOutFirstBadWord )
{
	if ( pOutWords )
		*pOutWords = 0;
	if ( pOutFirstBadWord )
		*pOutFirstBadWord = -1;

	if ( !pEntity )
		return false;

	// THE ENTITY'S OWN TIER (Part 18). Reading the table of a different tier
	// than the one this entity is encoded through would compare its blob
	// against props that never touch it, which is a check that passes or fails
	// for the wrong reason.
	const int iTier = pEntity->Tier();

	if ( iTier < 0 || iTier >= SourceSharp_TierCount() )
		return false;

	SendTable *pTable = g_pTierTables[ iTier ];

	if ( !pTable )
		return false;

	int nShared = 0;
	StandaloneNetFields( &nShared );

	const int nWords = SourceSharp_TierWords( iTier );

	if ( nWords <= 0 )
		return false;

	if ( pOutWords )
		*pOutWords = nWords;

	const unsigned char *pObject = (const unsigned char *)pEntity;
	const unsigned char *pBlob   = pEntity->DataBlob();
	const int nBlobBytes = pEntity->BlobBytes();

	bool bAgrees = true;

	for ( int i = 0; i < nWords; i++ )
	{
		const SendProp *pProp = pTable->GetProp( nShared + i );
		const int nWordEnd = ( i + 1 ) * SOURCESHARP_PAYLOAD_WORD_BYTES;

		// A word the blob does not reach cannot agree with anything, and it is
		// worse than a mismatch: the engine would be reading past the object.
		if ( nWordEnd > nBlobBytes )
		{
			bAgrees = false;

			if ( pOutFirstBadWord && *pOutFirstBadWord < 0 )
				*pOutFirstBadWord = i;

			continue;
		}

		if ( memcmp( pObject + pProp->GetOffset(),
		               pBlob + i * SOURCESHARP_PAYLOAD_WORD_BYTES,
		               SOURCESHARP_PAYLOAD_WORD_BYTES ) == 0 )
		{
			continue;
		}

		bAgrees = false;

		if ( pOutFirstBadWord && *pOutFirstBadWord < 0 )
			*pOutFirstBadWord = i;
	}

	return bAgrees;
}

//-----------------------------------------------------------------------------
// The same check over the hierarchy, spelled out for a human.
//
// Lives here rather than in cs_sv_payload_agree because the entity list it
// walks is this file's — the SDK's entity list is IHandleEntity-typed and its
// iteration correctly skips every entity on this hierarchy. The console command
// calls it so that ONE command answers for both shells; two commands would mean
// a green answer from the one that happened to be typed.
//-----------------------------------------------------------------------------
void SourceSharp_StandalonePayloadAgreeReport( int nWantedIndex, int *pOutChecked, int *pOutAgreed )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	int nShared = 0;
	StandaloneNetFields( &nShared );

	for ( CStandaloneEntity *pEnt : standalones )
	{
		if ( nWantedIndex >= 0 && pEnt->EntIndex() != nWantedIndex )
			continue;

		int nWords = 0;
		int nFirstBad = -1;
		const bool bAgrees = SourceSharp_StandalonePayloadAgrees( pEnt, &nWords, &nFirstBad );

		if ( nWords <= 0 )
		{
			if ( nWantedIndex >= 0 )
			{
				Msg( "[SourceSharp]   standalone %d '%s': no payload props on its table\n",
					pEnt->EntIndex(), pEnt->GetClassname() );
			}
			continue;
		}

		if ( pOutChecked )
			(*pOutChecked)++;

		if ( bAgrees )
		{
			if ( pOutAgreed )
				(*pOutAgreed)++;

			Msg( "[SourceSharp]   standalone %d '%s': %d payload word(s) land where the blob is\n",
				pEnt->EntIndex(), pEnt->GetClassname(), nWords );
		}
		else
		{
			Warning( "[SourceSharp]   standalone %d '%s': payload word %d DISAGREES with the "
			         "blob (%d word(s) checked)\n",
				pEnt->EntIndex(), pEnt->GetClassname(), nFirstBad, nWords );
		}

		if ( nWantedIndex < 0 )
			continue;

		// THIS entity's table, matching the words counted for it above.
		const int iTier = pEnt->Tier();
		SendTable *pTable = ( iTier >= 0 && iTier < SourceSharp_TierCount() )
			? g_pTierTables[ iTier ] : NULL;

		for ( int i = 0; pTable && i < nWords; i++ )
		{
			const SendProp *pProp = pTable->GetProp( nShared + i );
			const unsigned char *pAtProp = (const unsigned char *)pEnt + pProp->GetOffset();
			const unsigned char *pAtBlob = pEnt->DataBlob() + i * SOURCESHARP_PAYLOAD_WORD_BYTES;

			Msg( "[SourceSharp]     %-6s offset=%-5d prop=%02x%02x%02x%02x  "
			     "blob=%02x%02x%02x%02x  %s  proxy=%s\n",
				pProp->GetName(), pProp->GetOffset(),
				pAtProp[0], pAtProp[1], pAtProp[2], pAtProp[3],
				pAtBlob[0], pAtBlob[1], pAtBlob[2], pAtBlob[3],
				memcmp( pAtProp, pAtBlob, SOURCESHARP_PAYLOAD_WORD_BYTES ) == 0 ? "ok" : "DIFFERS",
				SourceSharpPayload_SendProxyName( pProp ) );
		}
	}
}

//-----------------------------------------------------------------------------
// Writes one dword into a live hierarchy entity's blob and marks it changed.
//
// The negative control, and the reason it exists is that a green agreement
// which cannot go red is not a measurement. Two experiments, and which word is
// poked decides which:
//
//   a word a typed prop covers  — both paths move together on the server,
//                                 because there they are the same bytes, and
//                                 the client's two buffers move together too.
//                                 Agreement holding is the delta path working.
//
//   a word NO typed prop covers — the blob is one length for every class on the
//                                 hierarchy, so every class but the greediest
//                                 has a tail of blob no typed prop of ITS table
//                                 describes, and so does the alignment padding
//                                 of the greediest. The payload carries those
//                                 words and the typed props do not, so the
//                                 client's shadow moves and its blob does not,
//                                 and it must say so. Agreement holding THROUGH
//                                 THAT means the check has no teeth.
//
// Called by cs_sv_poke_payload, which finds the entity on either hierarchy.
//-----------------------------------------------------------------------------
bool SourceSharp_StandalonePokePayload( int nIndex, int iWord, uint32 nValue )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	for ( CStandaloneEntity *pEnt : standalones )
	{
		if ( pEnt->EntIndex() != nIndex )
			continue;

		const int nBlobBytes = pEnt->BlobBytes();

		if ( iWord < 0 || ( iWord + 1 ) * SOURCESHARP_PAYLOAD_WORD_BYTES > nBlobBytes )
		{
			Warning( "[SourceSharp] word %d is outside a %d-byte blob (%d word(s))\n",
				iWord, nBlobBytes, nBlobBytes / SOURCESHARP_PAYLOAD_WORD_BYTES );
			return true;
		}

		unsigned char *pAt = pEnt->DataBlob() + iWord * SOURCESHARP_PAYLOAD_WORD_BYTES;
		memcpy( pAt, &nValue, SOURCESHARP_PAYLOAD_WORD_BYTES );

		// Without this the write is correct on the server and invisible
		// everywhere else: the engine skips the encode entirely for an edict
		// nothing marked.
		pEnt->NetworkStateChanged();

		Msg( "[SourceSharp] standalone %d '%s': blob word %d (offset %d) = 0x%08x\n",
			nIndex, pEnt->GetClassname(), iWord,
			iWord * SOURCESHARP_PAYLOAD_WORD_BYTES, nValue );

		return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// Fills a live entity's whole payload with the probe pattern, on the hierarchy.
//
// This is the half of what replaced the typed-prop oracle that the server can
// do. The pattern is a function of the seed and the WORD INDEX only, so the
// client can recompute it without being told anything but the seed — which is
// what makes the comparison a check rather than an echo. See
// SourceSharpPayload_ProbeWord.
//
// Poking is the older, narrower instrument and it stays: one word by hand is
// what you reach for when you already know which one is wrong. The probe is
// what says whether any of them are.
//-----------------------------------------------------------------------------
bool SourceSharp_StandaloneProbePayload( int nIndex, uint32 nSeed, int *pOutWords )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	for ( CStandaloneEntity *pEnt : standalones )
	{
		if ( pEnt->EntIndex() != nIndex )
			continue;

		// This entity's own words. The MIN was guarding against a sealed word
		// count larger than the blob, which could happen while one number
		// served every class; a tier's word count IS its blob length divided by
		// four, so the two cannot now disagree and there is nothing to take the
		// smaller of.
		const int nWords = SourceSharp_TierWords( pEnt->Tier() );

		unsigned char *pBlob = pEnt->DataBlob();

		for ( int i = 0; i < nWords; i++ )
		{
			const uint32 nWord = (uint32)SourceSharpPayload_ProbeWord( nSeed, i );
			memcpy( pBlob + i * SOURCESHARP_PAYLOAD_WORD_BYTES, &nWord,
			          SOURCESHARP_PAYLOAD_WORD_BYTES );
		}

		// Without this the write is correct on the server and invisible
		// everywhere else: the engine skips the encode entirely for an edict
		// nothing marked.
		pEnt->NetworkStateChanged();

		if ( pOutWords )
			*pOutWords = nWords;

		Msg( "[SourceSharp] standalone %d '%s': %d payload word(s) filled with probe seed "
		     "0x%08x\n", nIndex, pEnt->GetClassname(), nWords, nSeed );

		return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// Breaks an entity's schema hash on purpose, so the client's rejection path can
// be EXERCISED rather than trusted.
//
// On a listen server both halves are one build reading one shared assembly, so
// the hashes always agree and the refusal can never fire by itself. A path that
// has never run is a path nobody has tested, and this one is the last line of
// defence against the failure the whole plan is about — so there has to be a
// way to make it happen.
//
// It writes the replicated MEMBER, not the registry: what the client sees is
// exactly what a build with different C# would have sent, and every step after
// it on the client is the real one.
//
// FCVAR_CHEAT, because it deliberately corrupts replicated state.
//-----------------------------------------------------------------------------
bool SourceSharp_StandaloneBreakSchema( int nIndex, uint32 nHash )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	for ( CStandaloneEntity *pEnt : standalones )
	{
		if ( pEnt->EntIndex() != nIndex )
			continue;

		// Through GetServerClass first, so the stamped value exists to be
		// overwritten — otherwise a never-transmitted entity would have this
		// clobbered back by its first stamp and the test would silently pass.
		pEnt->GetServerClass();

		const uint32 nWas = pEnt->m_nSchemaHash;
		pEnt->m_nSchemaHash = nHash;
		pEnt->NetworkStateChanged();

		Msg( "[SourceSharp] standalone %d '%s': schema hash 0x%08x -> 0x%08x; the client must "
		     "now REFUSE its payload and say so\n",
			nIndex, pEnt->GetClassname(), nWas, nHash );

		return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// A MANAGED RELOAD MOVED THE LAYOUTS; THIS PUTS THE LIVE ENTITIES BACK IN STEP.
// plan_nettable_generic.md Step 10.
//
// Called after LoadSide has run, so the type table already holds the NEW
// assembly's hashes. Every entity that has been stamped -- which is every entity
// the engine has ever asked a ServerClass for -- is compared against the
// registry, and the ones whose class was reshaped are put right.
//
// THREE THINGS HAPPEN TO A RESHAPED ENTITY, and leaving any of them out is
// worse than doing none:
//
//   The stamp is redone, by clearing m_pServerClass and asking again rather
//   than by writing the members here. GetServerClass is where the rule for what
//   an entity carries lives -- the tier clamp, the payload word count, the
//   unpublished-class warning -- and a second copy of it in this file would
//   agree today and drift the first time that rule changes. It early-returns on
//   m_pServerClass, so without the clear nothing would ever stamp again.
//
//   The blob is zeroed. Its bytes were written through the old layout and mean
//   nothing under the new one, so a re-stamp on its own would trade a refusal
//   for a silent misdecode -- a value that is plausible and wrong, which is the
//   failure this whole plan is written against. Zero is also what the reloaded
//   managed half starts from: its entity object is newly constructed, so every
//   field it did not network reads its default already, and this makes the ones
//   it did network agree with them.
//
//   The edict is marked. The new hash and the cleared payload are both wire
//   state, and an unmarked edict reuses its previous packet.
//
// AN ENTITY WHOSE CLASS DID NOT MOVE IS NOT TOUCHED, and that is the headline
// rather than an optimisation: the blob is native memory the managed assembly
// does not own, so state survives a reload for free exactly as long as the
// layout it was written through survives with it.
//
// AND ONE THING PER-CLASS TIERS MADE POSSIBLE THAT WAS NOT BEFORE (Part 18). A
// reshape can now grow a type past the tier its LIVE entities were allocated
// at: the entity's width was fixed by operator new when the factory made it and
// cannot change, while the type's tier is recomputed at publish from the new
// layout. Such an entity cannot hold its class any more, and re-stamping it
// would be a lie a receiving side would believe. It is stamped ZERO instead --
// the value an unpublished class carries, which every receiving side already
// refuses -- and said out loud, because the only fix is to create it again.
//-----------------------------------------------------------------------------
void SourceSharp_StandaloneResyncSchema( int *pOutChecked, int *pOutRestamped )
{
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	int nChecked   = 0;
	int nRestamped = 0;

	for ( CStandaloneEntity *pEnt : standalones )
	{
		// Never asked for a class, so never stamped. Its first GetServerClass
		// will read the CURRENT registry and get the right answer without help
		// -- and stamping it here instead would hide a bug in that path.
		if ( !pEnt->HasServerClass() )
			continue;

		nChecked++;

		unsigned int nWanted = 0;

		if ( !SourceSharp_SchemaNeedsRestamp( SourceSharp_NetTypes(), pEnt->GetClassname(),
		                                      pEnt->m_nSchemaHash, &nWanted ) )
		{
			continue;
		}

		const uint32 nWas       = pEnt->m_nSchemaHash;
		const int    nBlobBytes = pEnt->BlobBytes();

		if ( nBlobBytes > 0 && pEnt->DataBlob() )
			memset( pEnt->DataBlob(), 0, nBlobBytes );

		const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ByName( pEnt->GetClassname() );

		if ( pType && pType->iTier > pEnt->Tier() )
		{
			pEnt->m_nSchemaHash   = 0;
			pEnt->m_nPayloadWords = 0;
			pEnt->NetworkStateChanged();
			nRestamped++;

			Warning( "[SourceSharp] reload: standalone %d '%s' grew past the tier it was "
			         "ALLOCATED at (%d byte(s), tier %d -> tier %d): its blob cannot hold "
			         "the new layout, so it carries no schema hash and every receiving side "
			         "will refuse its payload. Create it again to get the new width.\n",
				pEnt->EntIndex(), pEnt->GetClassname(), nBlobBytes,
				pEnt->Tier(), pType->iTier );
			continue;
		}

		// Through GetServerClass rather than by hand, so there is exactly one
		// place that decides what an entity carries.
		pEnt->RestampServerClass();
		pEnt->NetworkStateChanged();
		nRestamped++;

		Msg( "[SourceSharp] reload: standalone %d '%s' reshaped -- schema hash 0x%08x -> "
		     "0x%08x, %d blob byte(s) cleared\n",
			pEnt->EntIndex(), pEnt->GetClassname(), nWas, pEnt->m_nSchemaHash, nBlobBytes );
	}

	// ADDED, not assigned. One caller passes one pair of counters to both
	// shells' walks, so an assignment here made the second answer for the whole
	// reload -- and a reload that really did re-stamp an entity then printed
	// "0 reshaped and re-stamped" on the very next line, which is a report that
	// contradicts the line above it and cannot be believed either way.
	if ( pOutChecked )
		*pOutChecked += nChecked;

	if ( pOutRestamped )
		*pOutRestamped += nRestamped;
}

//-----------------------------------------------------------------------------
// THE TIER THIS ENTITY WAS ALLOCATED AT (Part 18), and nothing else.
//
// It used to be the hierarchy's one tier for every entity here. It is the
// instance's now, and the pairing with operator new is the load-bearing part:
// m_iTier came from the same m_Desc.iTier the factory sized the allocation
// with, so the table this hands the engine is exactly the table whose last
// payload prop is the last word of this entity's blob.
//
// Resolved on the first ask and kept, because the engine asks per entity per
// transmit.
//-----------------------------------------------------------------------------
ServerClass *CStandaloneEntity::GetServerClass()
{
	if ( m_pServerClass )
		return m_pServerClass;

	// Clamped rather than trusted. A tier index outside the table would index a
	// static array out of bounds here, which is the one place in this change
	// that could read wild memory rather than merely the wrong length.
	const int iTier = ( m_iTier >= 0 && m_iTier < SourceSharp_TierCount() ) ? m_iTier : 0;

	m_pServerClass = g_pTierClasses[ iTier ];

	// The two facts about the CLASS that travel with every instance, stamped
	// once here rather than proxied per encode.
	//
	// This is the only moment both are available: the classname is set by the
	// factory in PostConstructor, and the type table is filled during managed
	// startup — so the constructor is too early for one and a static
	// initialiser too early for both. The engine asks for this before it can
	// transmit the entity, which is exactly when the numbers have to be right.
	const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ByName( GetClassname() );

	if ( pType )
	{
		m_nSchemaHash = pType->nSchemaHash;

		// FROM THIS ENTITY'S OWN TIER (Part 18). It was the hierarchy's single
		// sealed word count, which is precisely the "one number for the whole
		// family" per-class tiers removes. A payload word count that disagreed
		// with the table actually carrying the entity would tell the client to
		// expect a different number of words than arrived.
		m_nPayloadWords = (uint32)SourceSharp_TierWords( iTier );
	}
	else
	{
		// A class nobody published. Its entities carry no state, so they say
		// so: a zero hash is a value the client checks against its own registry
		// and finds nothing for, and it refuses the payload rather than
		// decoding whatever the blob happened to hold. Loud once, because a
		// hierarchy class that never reached the type table is a startup order
		// bug rather than a runtime one.
		m_nSchemaHash   = 0;
		m_nPayloadWords = 0;

		static bool s_bWarned = false;

		if ( !s_bWarned )
		{
			s_bWarned = true;
			Warning( "[SourceSharp] standalone '%s' has no published type; its entities will "
			         "carry type id 0x%08x with no schema hash, and the client will refuse "
			         "their payload\n", GetClassname(), m_nTypeId );
		}
	}

	return m_pServerClass;
}

//=============================================================================//
// ss_barrel used to live here.
//
// It had a shell of its own — CStandaloneTestEntity — and a managed contract of
// its own, written while this hierarchy was an experiment and it was not yet
// clear that the contract the C#-backed map entities already spoke would fit.
// It does: all 109 of them run on this hierarchy now, on CStandaloneEntity
// itself -- the shell speaks the managed contract, since step 1f collapsed
// thirteen relay slots into one Dispatch op table.
//
// So ss_barrel is an ordinary [ScriptedEntity] in SourceSharp.Server/TestBarrel.cs,
// registered by the same factory as everything else, and the second contract is
// gone from this side entirely. What remains of it is the CLIENT's — three
// calls that are how every entity on the hierarchy pairs with its managed half
// over there, and not a duplicate of anything.
//=============================================================================//

//=============================================================================//
// THERE IS NO GAME SYSTEM HERE ANY MORE -- plan_gamedll_after.md Phase A3, the
// fifth and last "C" system, and it is the only one of the five that was
// SourceSharp's own code to begin with.
//
// CSourceSharpStandaloneSystem was a row of kServerSystems[] at index 11, with
// three stages. Part 9a lists it beside the four stock systems with the note
// "SourceSharp's own code, already ours", which reads as an argument that it
// moves easily. MEASURED, THE THREE STAGES HAD THREE DIFFERENT ANSWERS and the
// system's name was what hid that:
//
//   Init()                        DEAD. RegisterStandaloneTiers had already run
//                                 by the time the table reached this row -- see
//                                 the note where the override was. Deleted.
//
//   FrameUpdatePostEntityThink()  THREE STEPS, driven from C#, with the
//                                 managed think pass between the last two.
//                                 SourceSharp_FrameStandalonePlayerCommands,
//                                 SourceSharp_FrameStandaloneUpdate and
//                                 SourceSharp_FrameStandaloneFinish in
//                                 shell_frame.cpp, called by the
//                                 StandaloneHierarchy participant at
//                                 FrameStage.Standalone.
//
//   LevelShutdownPreEntity()      ONE STEP.
//                                 SourceSharp_LevelShutdownStandaloneEntities in
//                                 shell_level.cpp, called by the same
//                                 participant at LifecycleStage.Standalone.
//
// WHAT MOVED IS THE ORDER, WHICH IS ALL THE ROW EVER CONTRIBUTED. The work is
// native and stays native: it walks a CUtlVector of native shells, replays
// CUserCmds around a saved gpGlobals->curtime, and composes attachment-parented
// children. This file is the floor the managed hierarchy stands on, and a floor
// cannot stand on itself. What was decided in C++ and is decided in C# now is
// where in the frame the hierarchy runs, and that the players' commands run
// before the entities' thinks -- which used to be a comment rather than a
// decision anybody could see beside the other frame orderings.
//
// AND THE POSITION IS THE ONE THE ROW HELD, which is the part worth checking
// rather than assuming. The row was index 11 and SourceSharpManaged is index
// 12, with NOTHING between them. FrameStage.Standalone is below every other
// frame stage, so the two steps run at the very start of the managed row's
// post-think body; the backward walk reaches the managed row's
// LevelShutdownPreEntity at 12 and no row between 11 and 12 declares that stage
// either.
//
// The ServerClass registration above is unaffected and is still built in the
// DLL-init window, from SourceSharp_NetHierarchyDeclare.
//=============================================================================//



// cs_standalone_spawn, cs_standalone_parent, cs_standalone_mapspawn and
// cs_sv_viewmodel_report are diagnostics: they live in shell_standalone_debug.cpp,
// which only a debug build compiles.

