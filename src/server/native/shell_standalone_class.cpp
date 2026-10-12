//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — which classnames the parallel hierarchy hosts =======//
// SS_REASON a  a vtable the engine calls through
//
// See shell_standalone_class.h. This is the registration half of what
// `shell_standalone_scripted.cpp` split into for plan_entity.md's Phase 1 exit:
// one CStandaloneScriptedClass per classname C# declares, the IEntityFactory
// the engine makes instances through, the override mask each class carries, and
// the blob offset past the largest shell.
//
// It decides NOTHING about what a call does — that is `shell_dispatch.cpp`,
// which routes an op to managed code. The two were one file, and the reason
// they are two now is that "which classes exist" and "how a call reaches one"
// are answered by different code with different lifetimes: the class list is
// built once at managed startup and re-described on `cs_sv_reload`, while the
// relays run per entity per frame.
//
//=============================================================================//

#include "shell_standalone_class.h"
#include "shell_standalone_player.h"
#include "entity_op.h"
#include "slot_table.h"
#include "generic_nettable.h"
#include "entity_factory_table.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "sourcesharp.h"
#include "native_command_body.h"
#include "ss_string.h"

#include <map>
#include <string>
#include <unordered_map>
#include <vector>

#include "ss_entityfactory.h"   // ss::IEntityFactory: the factories this file registers

void SourceSharp_LogEntityCreate( const char *pClassName );
void SourceSharp_LogEntityCreateFailed( const char *pClassName );
void SourceSharp_LogEntityDestroy( const char *pClassName );

//=============================================================================//
// Registration.
//=============================================================================//

//-----------------------------------------------------------------------------
// One registered class: its description, and the factory that makes instances.
//
// Created during managed startup and lives for the process. The engine holds
// pointers to the strings, so nothing here is freed: they are interned in
// m_Kept (ss::StringPool), whose strings never move and which the object owns
// for the process. Equal strings share one copy; the engine only reads them.
//-----------------------------------------------------------------------------
class CStandaloneScriptedClass : public ss::IEntityFactory
{
public:
	CStandaloneScriptedClass( const char *pClassName, const char **ppOutputNames,
	                          int nOutputCount, bool bServerOnly,
	                          SolidType_t nSolidType, int nSolidFlags,
	                          bool bTouchEveryTick, bool bPlayer )
	{
		memset( &m_Desc, 0, sizeof( m_Desc ) );
		Describe( pClassName, ppOutputNames, nOutputCount, bServerOnly,
		          nSolidType, nSolidFlags, bTouchEveryTick, bPlayer );
	}

	//-------------------------------------------------------------------------
	// RE-DESCRIBED, NOT RE-INSTALLED, when C# comes back (`cs_sv_reload`).
	//
	// The factory dictionary is native and outlives the managed load context,
	// so every class C# declares is declared again on the far side of a reload.
	// The old code answered that by calling InstallFactory a second time, and
	// `CEntityFactoryDictionary::InstallFactory` opens with
	// `Assert( FindFactory( pClassName ) == NULL )` (util.cpp:178). One reload
	// therefore fired the assertion once per declared class -- MEASURED at
	// exactly 284, against 284 classes in `./debug-api classes` -- and then
	// inserted a duplicate key into `m_Factories` anyway, so a second reload
	// left three factories under one name and nothing ever removed any of them.
	//
	// Re-describing the factory that is already installed is the fix that does
	// not need an uninstall the interface does not have: `IEntityFactory
	// Dictionary` declares InstallFactory, Create, Destroy and
	// GetCannonicalName and NOTHING that removes an entry, and the dictionary
	// itself is a class local to util.cpp -- a stock file this port is trying
	// to stop compiling, not extend. The engine keeps holding the same pointer
	// it was given at first load, which is what it wants: it stores the
	// IEntityFactory *, not a copy of anything behind it.
	//
	// A class the NEW managed side no longer declares keeps its factory, and
	// that is a real residue named rather than hidden -- but it is strictly
	// better than what it replaces, which kept the factory AND stacked another
	// one on top of it.
	//-------------------------------------------------------------------------
	void Describe( const char *pClassName, const char **ppOutputNames,
	               int nOutputCount, bool bServerOnly,
	               SolidType_t nSolidType, int nSolidFlags,
	               bool bTouchEveryTick, bool bPlayer )
	{
		// THE OLD STRINGS ARE NOT FREED, and that is the file's standing
		// contract rather than an oversight -- see the class comment above.
		// `CStandaloneEntity::m_pClass` is a POINTER to this very m_Desc
		// (`PostConstruct`, shell_standalone_base.cpp:1441), so every entity of
		// this class that is standing when C# comes back reads whatever is here
		// afterwards. Overwriting the fields is safe because the pointer does
		// not move; freeing what they used to point at would not be. A name and
		// its output list per class per reload is a few kilobytes a developer
		// spends by typing cs_sv_reload, against a whole leaked factory object
		// per class per reload before this change.
		if ( nOutputCount > SOURCESHARP_MAX_STANDALONE_OUTPUTS )
		{
			// Truncating silently would produce a map whose connections simply
			// never fire, with nothing to explain why.
			Warning( "[SourceSharp] entity class '%s' declares %d outputs; only %d are available\n",
				pClassName, nOutputCount, SOURCESHARP_MAX_STANDALONE_OUTPUTS );
			nOutputCount = SOURCESHARP_MAX_STANDALONE_OUTPUTS;
		}

		// Copied, because the caller's array is the managed marshaller's and
		// does not outlive the call. The entity reads these on every keyvalue
		// of every instance for the rest of the process.
		char **ppNames = ( nOutputCount > 0 ) ? new char *[ nOutputCount ] : NULL;

		for ( int i = 0; i < nOutputCount; i++ )
			ppNames[i] = const_cast< char * >( Keep( ppOutputNames[i] ) );

		// The NAME is claimed once and never re-pointed. It is what
		// InstallFactory keyed the dictionary on and what GetClassName answers,
		// and a second copy of the same characters would leave those two
		// holding different addresses for one classname -- true and confusing,
		// and confusing is what costs the next reader an hour.
		if ( !m_Desc.pClassName )
			m_Desc.pClassName = Keep( pClassName );

		m_Desc.ppOutputNames = (const char **)ppNames;
		m_Desc.nOutputCount  = nOutputCount;
		m_Desc.bServerOnly   = bServerOnly;
		m_Desc.nSolidType      = nSolidType;
		m_Desc.nSolidFlags     = nSolidFlags;
		m_Desc.bTouchEveryTick = bTouchEveryTick;
		m_Desc.bPlayer         = bPlayer;

		// Not knowable yet. Registration runs before C# publishes the types,
		// so the tier is looked up on the first Create and kept. See
		// ResolveTier below for why the lookup is late rather than eager.
		m_Desc.iTier = -1;

		// ALL ONES UNTIL RegisterClassOps SAYS OTHERWISE (plan_entity.md §3.4).
		// Registration runs before managed code has reflected over the class,
		// and the mask arrives in a second call; until it does, every op
		// crosses, which is what this class did before the mask existed. The
		// other default would stop delivering touches and thinks to entities
		// that DO override them, silently.
		//
		// Set on every Describe, including the re-describe a reload does, so a
		// class that LOST an override across a reload does not keep the old
		// mask until the new one lands.
		m_Desc.nOverrideMask = ENTITY_OP_MASK_ALL;
	}

	//-------------------------------------------------------------------------
	// WHICH OPS THIS CLASS OVERRIDES. Written after Describe, by
	// RegisterClassOps, because managed code computes it by reflection and only
	// gets to do that once the class exists on both sides.
	//
	// ENTITY_OP_ALWAYS_MASK is OR'd in HERE rather than trusted to the caller.
	// Spawn, keyvalue, removed and the filter answer are bookkeeping on the
	// managed side, not calls to a virtual a class may not have overridden, and
	// a managed build that computed the mask wrongly -- or an older one that
	// did not know an op had joined the always set -- would otherwise switch
	// off the call that CREATES the entity's C# object. The native side is
	// where the entity lives, so the native side keeps the floor.
	//-------------------------------------------------------------------------
	void SetOps( uint32_t nOpMask )
	{
		m_Desc.nOverrideMask = nOpMask | ENTITY_OP_ALWAYS_MASK;
	}

	uint32_t Ops() const { return m_Desc.nOverrideMask; }

	const char *GetClassName() const { return m_Desc.pClassName; }

	//-------------------------------------------------------------------------
	// WHICH TIER THIS CLASS'S ENTITIES ARE (Part 18), looked up once.
	//
	// THIS IS THE WHOLE OF WHY PER-CLASS TIERS ARE POSSIBLE. operator new is a
	// static function with no idea which class is being made, which is what the
	// design notes said made per-class sizing impossible — but the FACTORY is
	// per class and always was: there is one CStandaloneScriptedClass per
	// registered classname and it is holding that name in m_Desc. Nothing had
	// to be threaded through a global; the information was already at the
	// allocation site and only operator new's signature was hiding it.
	//
	// LATE rather than at registration, because registration happens before C#
	// publishes the types and an eager lookup would find nothing.
	//
	// AND A MISS IS NOT CACHED, which is the whole care in this function. An
	// entity created before its type was published — the map loading early, a
	// managed reload in flight — would otherwise pin tier 0 for the life of the
	// process, and every later entity of a 148-byte class would be allocated a
	// 32-byte blob that C# writes 148 bytes into. That is a heap overrun with
	// nothing between it and the next allocation, and the payload agreement
	// check cannot see it: that check compares props against the blob, and both
	// ends of the comparison would be inside the short one.
	//
	// Re-looking-up until it succeeds costs a string compare over a few hundred
	// entries, once per entity until the table is filled and never again.
	//
	// Caching a HIT is safe even across a managed reload that moved a class to
	// a different tier, and this is worth stating because it was the argument
	// for caching the miss too. Two entities of one class on two different
	// tiers is harmless HERE, where it would have been fatal before: each
	// entity carries its own tier and is allocated, encoded and read at its own
	// length, so there is no cross-entity invariant left to break. What must
	// hold is per entity — its blob is at least its class's layout — and
	// PostConstruct is what checks it.
	//
	// Tier 0 for a class the type table has never heard of at all. Its entities
	// carry no schema hash and the client refuses their payload, so the
	// smallest blob is the honest allocation as well as the safe one.
	//-------------------------------------------------------------------------
	int ResolveTier()
	{
		if ( m_Desc.iTier >= 0 )
			return m_Desc.iTier;

		const SourceSharpNetType_t *pType =
			SourceSharp_NetTypes().ByName( m_Desc.pClassName );

		// Nothing published yet: answer the floor and DO NOT remember it.
		if ( !pType || pType->iTier < 0 )
			return 0;

		m_Desc.iTier = pType->iTier;

		return m_Desc.iTier;
	}

	IServerNetworkable *Create( const char *pClassName ) OVERRIDE
	{
		SourceSharp_LogEntityCreate( pClassName );

		// ONE local feeding both halves. The allocation length and the tier the
		// instance will report are the same number read once, so the blob
		// cannot be one tier long while the table addressing it is another's.
		const int iTier = ResolveTier();

		// The one kind whose instances are a subclass: the player carries the
		// engine-facing blocks (CPlayerState, CPlayerLocalData) and answers
		// IMovementPlayer, and choosing the type HERE — where the class is
		// known — is what lets everything downstream stay type-blind.
		CStandaloneEntity *pEnt = m_Desc.bPlayer
			? new ( SourceSharp_TierBytes( iTier ) ) CStandalonePlayer
			: new ( SourceSharp_TierBytes( iTier ) ) CStandaloneEntity;

		if ( !pEnt )
		{
			SourceSharp_LogEntityCreateFailed( pClassName );
			return NULL;
		}

		// Bound to its class, and given its handle, before the map hands it a
		// single key. See CStandaloneEntity::PostConstruct for why that timing
		// is the difference between a map's values arriving and not — and for
		// why it is what copies m_Desc.iTier onto the instance.
		pEnt->PostConstruct( &m_Desc );

		return pEnt;
	}

	void Destroy( IServerNetworkable *pNetworkable ) OVERRIDE
	{
		SourceSharp_LogEntityDestroy( m_Desc.pClassName );

		if ( pNetworkable )
			pNetworkable->Release();
	}

	size_t GetEntitySize() OVERRIDE
	{
		return m_Desc.bPlayer ? sizeof( CStandalonePlayer )
		                      : sizeof( CStandaloneEntity );
	}

private:
	// A copy that lives as long as this object -- the process. Replaces
	// strdup, which memdbgon.h routes through tier0's g_pMemAlloc.
	const char *Keep( const char *psz ) { return m_Kept.Intern( psz ); }

	StandaloneClassDesc_t m_Desc;
	ss::StringPool        m_Kept;
};

//=============================================================================//
// The instance blob.
//
// Here rather than in shell_standalone_base.cpp because this is where the
// CONCRETE shell is declared, and sizeof() of an abstract base says nothing
// about how long an instance actually is. Everything on this hierarchy is a
// CStandaloneEntity or its one subclass CStandalonePlayer; the offset is past
// it, 16-aligned so a generated prop lands on a natural boundary rather than
// wherever the class happens to end.
//=============================================================================//
int SourceSharp_StandaloneBlobOffset()
{
	// THE LARGEST SHELL ON THE HIERARCHY, not the common one (Phase 9a). The
	// payload props are absolute offsets from the object base, built once per
	// tier, and the player shares those tiers — so there is exactly ONE blob
	// offset for the whole hierarchy and it must clear every shell that will
	// ever stand in front of a blob. The first -ss_player run proved the
	// alternative: the player's members and its blob shared an address, its
	// command queue grew through a garbage pointer, and the server died in
	// malloc with nothing in the stack naming the overlap.
	//
	// What this costs: every standalone entity's allocation carries the
	// player's headroom. Measured at registration and printed once, because a
	// cost this quiet should have a number beside it.
	const size_t nLargest = MAX( sizeof( CStandaloneEntity ), sizeof( CStandalonePlayer ) );

	return (int)ALIGN_VALUE( nLargest, 16 );
}

//-----------------------------------------------------------------------------
// THE BLOB LENGTH IS NOT A GLOBAL ANY MORE (Part 18).
//
// SourceSharp_StandaloneBlobBytes and its setter used to live here, holding the
// greediest declared class's length for every entity on the hierarchy. Each
// entity is sized to its own tier now, so the answer is
// CStandaloneEntity::BlobBytes() and there is no build-wide number to return.
//
// Deleted rather than left answering the maximum, deliberately. A global that
// still compiled would have gone on being called by anything that had not been
// converted, and it would have answered a length that is true of one class and
// wrong for every other — which reads past the end of a shorter entity's
// allocation and reports nothing. Removing the symbol turns every one of those
// call sites into a link error instead.
//
// The BASE above stays a global and stays a constant: it is the offset of the
// blob within the object, past a fixed-size class, and it is what every tier's
// props were resolved against at DLL init.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The classes really hosted by this hierarchy.
//
// Kept because "is there a factory under this name" is not the same question:
// the CBaseEntity shells register factories too, and a per-class SendTable built
// for one of those would resolve its props against an object that never sees
// them. The list is the answer to the question actually being asked.
//
// It holds the FACTORIES rather than a second copy of their names, which is the
// other half of the reload fix: the answer to "have I installed this one
// already" and the object to re-describe are then the same lookup. It was a
// CUtlVector< char * > of strdup'd names, and that vector grew by 284 leaked
// strings on every cs_sv_reload for the same reason the factories did.
//-----------------------------------------------------------------------------
// Case-blind, as the Q_stricmp walk it replaced was. Installed after a miss
// only, so one carrier per name in any case; nothing walks it.
static std::unordered_map< std::string, CStandaloneScriptedClass *, ss::CaselessHash, ss::CaselessEqual >
	g_StandaloneClassNames;

static CStandaloneScriptedClass *FindStandaloneClass( const char *pClassName )
{
	auto it = g_StandaloneClassNames.find( pClassName );
	return ( it != g_StandaloneClassNames.end() ) ? it->second : NULL;
}

//-----------------------------------------------------------------------------
// IDEMPOTENT BY CLASSNAME, and that is the whole of the fix for the 284
// assertions a cs_sv_reload used to fire. See CStandaloneScriptedClass
// ::Describe for the argument; the shape here is the one CAmmoDef already uses
// a few files away in shell_shared.cpp, for the same reason: the native side
// outlives the managed load context, so everything C# re-declares on a reload
// has to be re-declarable.
//-----------------------------------------------------------------------------
bool SourceSharp_InstallStandaloneClass( const char *pClassName, const char **ppOutputNames,
                                         int nOutputCount, bool bServerOnly,
                                         SolidType_t nSolidType, int nSolidFlags,
                                         bool bTouchEveryTick, bool bPlayer )
{
	if ( !pClassName || !pClassName[0] )
		return false;

	if ( CStandaloneScriptedClass *pExisting = FindStandaloneClass( pClassName ) )
	{
		// Already in the dictionary under this name and the engine is holding
		// the pointer. Take the new declaration -- a developer who added an
		// output and typed cs_sv_reload means the new one -- and do NOT install
		// it a second time.
		pExisting->Describe( pClassName, ppOutputNames, nOutputCount, bServerOnly,
			nSolidType, nSolidFlags, bTouchEveryTick, bPlayer );
		return true;
	}

	CStandaloneScriptedClass *pClass = new CStandaloneScriptedClass(
		pClassName, ppOutputNames, nOutputCount, bServerOnly, nSolidType, nSolidFlags,
		bTouchEveryTick, bPlayer );

	g_StandaloneClassNames.emplace( pClass->GetClassName(), pClass );

	ss::EntityFactoryDictionary()->InstallFactory( pClass, pClass->GetClassName() );
	return true;
}

//-----------------------------------------------------------------------------
// THE MASK, ARRIVING SEPARATELY (plan_entity.md §3.4).
//
// A second call rather than a fourteenth argument to the install above, and the
// reason is the reload: the mask is the one thing a class declares that a
// RELOAD can change without the class list changing. Somebody adds an OnThink
// and types cs_sv_reload; the classname, the outputs and the kind are all what
// they were, and this is the only thing that moved.
//
// FALSE FOR A NAME THAT WAS NEVER REGISTERED, and the caller ignores it:
// managed code may legitimately describe a class the native side has not been
// told about, and the honest response is to drop the mask rather than to invent
// a class to hang it on.
//-----------------------------------------------------------------------------
bool SourceSharp_SetStandaloneClassOps( const char *pClassName, uint32_t nOpMask )
{
	if ( !pClassName || !pClassName[0] )
		return false;

	CStandaloneScriptedClass *pClass = FindStandaloneClass( pClassName );

	if ( !pClass )
		return false;

	pClass->SetOps( nOpMask );
	return true;
}

//-----------------------------------------------------------------------------
// The ABI slot's body. A thin relay, exactly as SourceSharp_RegisterScripted
// Entity is one: the slot takes the classname because managed code addresses
// classes by name, and the class object is what holds the mask.
//
// The return is dropped deliberately. A mask for a name nobody registered is
// ORDINARY -- managed code may describe a class the native side has not been
// told about, and the honest response is to ignore it rather than to warn on
// every reload about a class the map never used.
//-----------------------------------------------------------------------------
void SourceSharp_RegisterClassOps( const char *pClassName, uint32_t nOpMask )
{
	SourceSharp_SetStandaloneClassOps( pClassName, nOpMask );
}

//=============================================================================//
// THE DICTIONARY ITSELF — `EntityFactoryDictionary()`, moved off stock.
//
// `p4-flip` measured this as one of the nine live subsystems holding the Phase
// 4 flip, and the ONLY reading of it that survives contact with the tree is
// that it is machinery THIS PORT RUNS ON:
//
//     shell_standalone_class.cpp:395   every [ScriptedEntity] C# declares
//     world_adapter.cpp:190            worldspawn
//     shell_diagnostics.h:168          every SOURCESHARP_LINK_ENTITY
//     shell_standalone_player.cpp:2416 FindFactory( "ss_player" ) at connect
//
// So it MOVES rather than goes. Everything above this line installs into it;
// this is what they install into, and `src/game/server/util.cpp`'s copy is
// `#ifndef SOURCESHARP` from the same commit.
//
// WHAT IS AND IS NOT DECIDED HERE. Nothing here decides what the game does:
// which classnames exist is decided in C# (`ScriptedEntityRegistry`, which
// pushes each one down through `SourceSharp_RegisterScriptedEntity`), and
// which of a map's blocks are the port's is decided in C# too
// (`MapSpawner.SpawnMapText`, whose residue is what reaches the native parse).
// This is the lookup those decisions are read back through, and it is native
// for one reason: `Create` is called SYNCHRONOUSLY from inside stock's map
// parse (`mapentities.cpp:112,164,247`) and from `CreateEntityByName`, and
// there is no point in that call where a crossing could be waited on.
//
// The name table it is built over is `entity_factory_table.h`, which is a
// header precisely so it can be gtested; see that file for the three stock
// defects the move corrects.
//=============================================================================//

namespace
{

//-----------------------------------------------------------------------------
// A classname somebody asked to create and nothing answers to.
//
// STOCK PRINTED THIS PER CALL, and on this map that is not a diagnostic, it is
// noise that buries one. `dm_lockdown` carries 18 blocks whose classnames have
// no factory in this build -- func_useableladder x3, info_ladder_dismount x14,
// info_node x1 -- and `mapentities.cpp` reaches `Create` TWICE per block, so
// stock emitted 36 `Attempted to create unknown entity type` lines per map
// load, MEASURED (24 + 112 + 8 over four loads, this worktree's console.log).
//
// plan_entity.md §7 asks for a DECLARED, PRINTED refusal rather than a silent
// no-op, and 36 repetitions of one sentence is closer to the second than to the
// first: nothing in the tree reads them, and p4-flip found the missing ladders
// only by grepping a log no gate opens. So the line is printed ONCE per
// classname, says what the consequence is, and the tally is kept for
// `dumpentityfactories` and for the managed side to read back.
//
// The count is NOT reset between maps. A refusal is a fact about this build --
// there is no factory for that name and there was not one on the last map
// either -- and a per-map reset would re-print it on every load, which is the
// noise this replaces.
//-----------------------------------------------------------------------------
class CSourceSharpFactoryDictionary : public ss::IEntityFactoryDictionary
{
public:
	virtual void InstallFactory( ss::IEntityFactory *pFactory, const char *pClassName ) OVERRIDE
	{
		switch ( m_Table.Install( pFactory, pClassName ) )
		{
		case kEntityFactoryInstalled:
		case kEntityFactoryAlreadyInstalled:
			// The second is a cs_sv_reload re-declaring a class it already
			// declared, against the same factory object the shim re-describes
			// rather than re-creates. Ordinary, and silent on purpose.
			return;

		case kEntityFactoryNameTaken:
			// STOCK ASSERTED AND THEN INSERTED ANYWAY, so a release build got
			// two entries under one name and `Find` answered whichever it
			// reached. The first is kept -- the engine may already hold
			// instances made through it -- and the collision is said out loud
			// instead of becoming a lookup that is right half the time.
			Warning( "[SourceSharp] two entity factories claim the classname '%s'; "
			         "keeping the first, the second will never be reached\n",
			         pClassName ? pClassName : "(null)" );
			return;

		case kEntityFactoryNoName:
			Warning( "[SourceSharp] an entity factory was installed with no classname; refused\n" );
			return;
		}
	}

	virtual ss::IEntityFactory *FindFactory( const char *pClassName ) OVERRIDE
	{
		return m_Table.Find( pClassName );
	}

	virtual const char *GetCannonicalName( const char *pClassName ) OVERRIDE
	{
		return m_Table.CanonicalName( pClassName );
	}

	virtual IServerNetworkable *Create( const char *pClassName ) OVERRIDE
	{
		ss::IEntityFactory *pFactory = m_Table.Find( pClassName );

		if ( !pFactory )
		{
			Refuse( pClassName );
			return NULL;
		}

		return pFactory->Create( pClassName );
	}

	virtual void Destroy( const char *pClassName, IServerNetworkable *pNetworkable ) OVERRIDE
	{
		ss::IEntityFactory *pFactory = m_Table.Find( pClassName );

		if ( !pFactory )
		{
			// NOT counted as a refusal. Nothing was asked to be created, so
			// there is nothing this build cannot do; a Destroy for a name with
			// no factory means the caller is holding something this dictionary
			// did not make, which is a different and rarer mistake.
			Warning( "[SourceSharp] asked to destroy a '%s', which no factory here made\n",
			         pClassName ? pClassName : "(null)" );
			return;
		}

		pFactory->Destroy( pNetworkable );
	}

	CEntityFactoryTable< ss::IEntityFactory > m_Table;

	// classname -> how many times it was asked for and refused, case-blind as
	// the CUtlDict it was; m_RefusedOrder is first-refusal order.
	typedef std::map< std::string, int, ss::CaselessLess > Refused_t;
	Refused_t                              m_Refused;
	std::vector< Refused_t::value_type * > m_RefusedOrder;

private:
	void Refuse( const char *pClassName )
	{
		if ( !pClassName || !pClassName[0] )
		{
			Warning( "[SourceSharp] REFUSED: an entity was asked for with no classname\n" );
			return;
		}

		Refused_t::iterator it = m_Refused.find( pClassName );

		if ( it != m_Refused.end() )
		{
			it->second++;
			return;
		}

		m_RefusedOrder.push_back( &*m_Refused.emplace( pClassName, 1 ).first );

		Warning( "[SourceSharp] REFUSED: no entity factory answers to '%s'. Nothing of "
		         "that class will be created, in this map or any other, until one is "
		         "registered -- see `dumpentityfactories`\n", pClassName );
	}
};

} // namespace

//-----------------------------------------------------------------------------
// THE SINGLETON, and the same shape stock used: a function-local static.
//
// It has to be. `CWorldFactory` (world_adapter.cpp) and every
// SOURCESHARP_LINK_ENTITY registration install from static initialisers this
// port does not order, so the dictionary must be constructed by the first call
// rather than by its own file-scope initialiser. A namespace-scope object here
// would reintroduce exactly the static-initialisation-order dependency
// `shell_diagnostics.h`'s deferred `Install()` was written to avoid.
//-----------------------------------------------------------------------------
static CSourceSharpFactoryDictionary &SourceSharpFactoryDictionary()
{
	static CSourceSharpFactoryDictionary s_Dictionary;
	return s_Dictionary;
}

ss::IEntityFactoryDictionary *ss::EntityFactoryDictionary()
{
	return &SourceSharpFactoryDictionary();
}

//-----------------------------------------------------------------------------
// Enumeration, for the console commands (shell_standalone_class_debug.cpp) and
// for the managed side.
//
// Managed code cannot see this table today -- `DevCommandsLive.cs:34` says so
// in as many words, and warns about a classname it cannot check instead of
// refusing one it knows is wrong. Closing that needs an ABI slot, requested in
// .claude/entity-port/abi-requests/sub-factory.md; these are the bodies it
// will be wired to, and they are useful now regardless because the console
// command is built on them.
//-----------------------------------------------------------------------------
int SourceSharp_EntityFactoryCount()
{
	return SourceSharpFactoryDictionary().m_Table.Count();
}

const char *SourceSharp_EntityFactoryNameAt( int i )
{
	return SourceSharpFactoryDictionary().m_Table.NameAt( i );
}

int SourceSharp_EntityFactoryRefusalCount()
{
	return (int)SourceSharpFactoryDictionary().m_RefusedOrder.size();
}

const char *SourceSharp_EntityFactoryRefusalNameAt( int i )
{
	CSourceSharpFactoryDictionary &d = SourceSharpFactoryDictionary();

	if ( i < 0 || i >= (int)d.m_RefusedOrder.size() )
		return NULL;

	return d.m_RefusedOrder[i]->first.c_str();
}

int SourceSharp_EntityFactoryRefusalTimesAt( int i )
{
	CSourceSharpFactoryDictionary &d = SourceSharpFactoryDictionary();

	if ( i < 0 || i >= (int)d.m_RefusedOrder.size() )
		return 0;

	return d.m_RefusedOrder[i]->second;
}

//-----------------------------------------------------------------------------
// The i'th factory's entity size, or 0 past the end, for the debug dumps.
//-----------------------------------------------------------------------------
size_t SourceSharp_EntityFactorySizeAt( int i )
{
	ss::IEntityFactory *pFactory = SourceSharpFactoryDictionary().m_Table.FactoryAt( i );
	return pFactory ? pFactory->GetEntitySize() : 0;
}

// `dumpentityfactories` and `dump_entity_sizes`, the console dumps of this
// dictionary, are diagnostics: they live in shell_standalone_class_debug.cpp,
// which only a debug build compiles.
