//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the parallel entity hierarchy ======================//
//
// ONE class, and that is the point.
//
// This started as a native tree mirroring the SDK's — CStandaloneEntity,
// CStandaloneAnimating, and a class per entity on top. That was the wrong
// shape, and the reason is worth writing down: the SDK needs six shells today
// (Logical, Item, Brush, Prop, Surface, Projectile) ONLY because each derives
// from a different CBaseEntity descendant. Take CBaseEntity away and the reason
// goes with it. What an entity can do stops being its C++ type and becomes its
// data — a model index it may or may not have, a solid type, a bounding box.
//
// So the native side is one shell serving any number of entity classes, taking
// its classname from the factory that made it. The LAYERING lives in C#:
//
//     StandaloneEntity      <-> CBaseEntity            (11,297 lines)
//     StandaloneAnimating   <-> CBaseAnimating         ( 3,768 lines)
//     ...                       CBaseAnimatingOverlay  ( 1,168 lines)
//     ...                       CBaseFlex              ( 2,841 lines)
//     ...                       CBaseCombatCharacter   ( 4,381 lines)
//     ...                       CBasePlayer            (11,779 lines)
//
// A managed class inherits what it actually is, and the shell below it neither
// knows nor cares which layer it came from. It holds what the engine demands —
// interfaces, an edict, a partition handle, a send table — and relays
// everything else.
//
// It relays through VIRTUALS rather than by calling managed code directly. The
// shell has no opinion about which managed contract an entity speaks: the map
// entities speak the one the C#-backed entity classes have always spoken, and
// the experiment in shell_standalone.cpp speaks its own. Both are subclasses,
// and neither is in this file.
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_STANDALONE_H
#define SOURCESHARP_SHELL_STANDALONE_H
#ifdef _WIN32
#pragma once
#endif

#include "toucher.h"
#include "igameentity.h"
#include "iservernetworkable.h"
#include "iserverentity.h"
#include "engine/ICollideable.h"
#include "server_class.h"
// The bone cache's validity rule, shared with the client's copy of the same
// cache. One header, one rule, one set of gtests — see the server bones below.
// THE ENGINE BLOCK (plan_entity.md §3.2, step 1b). Every ICollideable,
// IServerEntity and IServerNetworkable getter below returns a field of it.
#include "engine_block.h"
// Who caused an input or a damage event, on either hierarchy.
#include "ss_activator.h"
// m_Partition's handle type.
#include "ispatialpartition.h"

#include <vector>

// Taken by reference only: the TUs that build or read one include their headers.
namespace ss { class variant_t; }   // ss_variant.h
namespace ss { class CTakeDamageInfo; }	// ss_takedamageinfo.h

//-----------------------------------------------------------------------------
// One registered entity class.
//
// Everything that is the same for every instance of a classname and nothing
// that is not: the name a map writes, the outputs the managed class declared,
// and whether the entity needs to exist on the client at all.
//
// A shell holds a pointer to one of these and asks it what it is. That is the
// whole of "one shell, many entity classes" — the differences are data, and
// this is the data.
//-----------------------------------------------------------------------------
struct StandaloneClassDesc_t
{
	const char  *pClassName;

	// In declaration order, which is the order C# declared them in. Both sides
	// resolve an output by NAME, so the order is only a stable slot assignment
	// and not something the two have to agree on. The strings are owned by the
	// registration and outlive every instance.
	const char **ppOutputNames;
	int          nOutputCount;

	// No edict, not networked, never sent. What CServerOnlyEntity means, and
	// what a logic_timer or a filter actually is — an edict spent on one is an
	// edict the map cannot spend on something a player can see.
	bool         bServerOnly;

	// What it does to a trace and to something walking into it.
	//
	// Per CLASS rather than per instance, because it is what the kind of thing
	// IS: a logic entity has no shape, an item is a volume you collect by
	// walking through. An instance may still change its mind — a pickup that
	// has been taken stops being a trigger until it respawns — which is what
	// SetSolidity is for.
	SolidType_t  nSolidType;
	int          nSolidFlags;

	// Whether a touch is reported for every tick something is inside it, or
	// only when it arrives and when it leaves.
	//
	// The two are genuinely different questions and the CBaseEntity shells
	// answered them differently: an item hooks Touch and is told every tick,
	// because a pickup has to re-check whether the player can take it now; a
	// brush hooks StartTouch and EndTouch, because a trigger fires on the way
	// in and not sixty-six times a second afterwards.
	bool         bTouchEveryTick;

	// Whether instances are CStandalonePlayer rather than the plain scripted
	// shell (Phase 9a). Per class because it is what the kind of thing IS,
	// and read exactly once, by the factory choosing which type to construct.
	bool         bPlayer;

	// WHICH OPS THE MANAGED CLASS ACTUALLY OVERRIDES (plan_entity.md §3.4,
	// step 1f). A bitmask over EntityOp_t: bit `1u << op` set means "this class
	// has a body for that op, so crossing is worth it".
	//
	// THIS FIELD IS THE CROSSING BUDGET. Before it, every OnManaged* relay
	// called into managed code unconditionally -- every think of every entity,
	// every touch of every tick -- whether or not the C# class had anything to
	// say. The shell tests one bit of this word instead, and a class that does
	// not override an op costs ZERO managed transitions for it.
	//
	// Set by RegisterClassOps, which managed startup calls once per class after
	// RegisterScriptedEntity. NOT a fourteenth argument to that call: the mask
	// is the one thing a class declares that a RELOAD can change without the
	// class list changing (somebody adds an OnThink and types cs_sv_reload), so
	// it wants to be re-statable on its own.
	//
	// DEFAULTS TO ALL ONES, and the direction matters more than it looks. A
	// class whose RegisterClassOps never arrived keeps working and merely pays
	// the crossings it used to pay; the other default would silently stop
	// delivering touches and thinks to entities that DO override them, which is
	// a game broken in a way no build error and no warning names.
	uint32_t     nOverrideMask;

	// WHICH TIER ITS ENTITIES ARE ALLOCATED AND NETWORKED AT (Part 18).
	//
	// The one field here that is not something C# declared at registration: it
	// is looked up from the published type table the first time the factory
	// makes an instance, because publishing happens after registration and the
	// answer is not knowable at the moment this struct is filled in.
	//
	// THIS IS THE SINGLE SOURCE FOR BOTH HALVES of a per-class tier. The
	// factory allocates from it and PostConstruct copies it onto the instance,
	// so the length of the blob and the table whose props address that blob
	// cannot disagree — which is the one way this change could corrupt memory
	// silently. -1 means "not looked up yet".
	int          iTier;
};

// Storage for the output slots, which cannot be sized per instance without
// giving the shell a second allocation to own. The same bound the CBaseEntity
// shells use and for the same reason: the busiest stock entity declares
// seventeen. A class that wants more is reported at registration rather than
// silently truncated.
#define SOURCESHARP_MAX_STANDALONE_OUTPUTS 32

//-----------------------------------------------------------------------------
// The CBaseEntity layer.
//
// Everything an entity is before it has a shape: a place in the world, an
// edict, a handle that resolves, a spot in the spatial partition, keyvalues,
// I/O, a think, and the ability to be hurt.
//
// WHERE ITS MEMBERS LIVE, AND WHY THAT IS WRITTEN DOWN HERE.
//
//     shell_standalone_base.cpp        the entity, touch, damage and blob arms
//     shell_standalone_hierarchy.cpp   the bone, attachment and pose arms
//     shell_standalone.cpp             GetServerClass
//
// THIS FILE IS THE SWAP PARTNER OF NONE OF THEM. Every dispatch instrument in
// this port pairs a header with a .cpp by filename -- `foo.cpp` -> `foo.h` --
// so this class belongs, by that rule, to nobody. Step 5's shard manifest gave
// shell_standalone_hierarchy.cpp to one agent and shell_standalone_base.cpp to
// another, and neither of the two checks that exist to catch that could see it:
// `no class spans two shards` is keyed off gcc scope lines, which named only
// one of the files, and `no paired header is claimed by two shards` counts
// CLAIMS, and neither shard claimed this header.
//
// (That is fixed on the tooling side -- docs/typedef-dryrun/route/s5_source.py
// measures it from the source and s5_verify.py's ninth invariant reports it.
// The note stays here because the next person to split this class will be
// reading THIS file, not that one.)
//
// The split as dispatched did no damage: neither agent edited this header, and
// neither edited a member body of this class. What would have done damage is
// one `#ifndef` around a MEMBER. Two TUs would then disagree about sizeof, the
// blob would stay at its fixed offset past the members, and
// SourceSharp_EntityGetDataBlob reaches it through NON-VIRTUAL DataBlob() and
// BlobBytes() -- so there would be no dispatch anywhere to disagree and nothing
// to report. sourcesharp/test/standalone_layout_test.cpp is what says no.
//
// SO: THE THREE FILES ABOVE CHANGE TOGETHER, and no member of this class goes
// behind a preprocessor conditional. If one genuinely must, the conditional
// belongs around the whole class or in a separate type -- never around a field.
//-----------------------------------------------------------------------------
class CStandaloneEntity : public IServerEntity, public IServerNetworkable,
                          public ICollideable, public IGameEntity
{
private:
	// THE BLOCK THIS ENTITY READS AND WRITES (step 1b).
	//
	// `(unsigned char *)this + SourceSharp_StandaloneBlobOffset()`, computed
	// once, in the constructor's member-initialiser list.
	//
	// DECLARED FIRST, AND THAT IS NOT A STYLE CHOICE. A member initialiser runs
	// in DECLARATION order, never in the order the constructor writes them, so
	// a pointer declared among the last members would be written after any
	// initialiser that reached through it. Nothing does today -- the thirteen
	// block defaults are in the constructor's BODY, which runs after every
	// member is initialised -- and putting the declaration here is what keeps
	// that true for the next initialiser somebody adds.
	//
	// It is also the entity's first eight bytes, which is where the getters
	// the spatial partition calls per trace would want it anyway.
	EngineBlock_t *m_pEngineBlock;

public:
	CStandaloneEntity();
	virtual ~CStandaloneEntity();

	//-------------------------------------------------------------------------
	// THE ENGINE BLOCK (plan_entity.md §3.2, step 1b).
	//
	// Every value an ICollideable, IServerEntity or IServerNetworkable getter
	// returns lives HERE, in the fixed 352-byte prefix of the blob this entity
	// already carries -- not in a member of this class. The shell used to hold
	// its own origin, angles, bounds, transform, solid type, solid flags,
	// collision group, model indices, handle, owner and PVS info; all thirteen
	// are gone and every one of those getters is now a load out of the block.
	//
	// WHY THE DUPLICATION HAD TO GO rather than the block merely existing
	// beside it. Two copies of an entity's origin is not a cost, it is a bug
	// waiting for its first divergent writer: managed code writes the block
	// (that is what the block is FOR) and native code wrote the member, and the
	// partition, the trace and the wire would then disagree about where the
	// entity is depending on which of the two the reader happened to reach.
	// standalone_layout_test.cpp pins the absence, by name, for all thirteen.
	//
	// A CACHED POINTER RATHER THAN ARITHMETIC PER GETTER.
	// SourceSharp_StandaloneBlobOffset() is one non-inline function returning
	// one number for the whole process, so `(char *)this + offset` cannot be
	// folded by any caller and every getter would carry a call. The partition
	// asks GetCollisionOrigin and OBBMins per trace; a call there is exactly
	// what §3.2 says these getters must not have. Eight bytes per entity buys
	// it, and the thirteen members it replaced were 176 -- measured, not
	// counted: sizeof( CStandaloneEntity ) went 1944 -> 1776 and
	// sizeof( CStandalonePlayer ) 3192 -> 3024, read out of the two server.so
	// builds with gdb.
	//-------------------------------------------------------------------------
	EngineBlock_t       &Block()       { return *m_pEngineBlock; }
	const EngineBlock_t &Block() const { return *m_pEngineBlock; }

	// Where the block starts, measured from the object base -- which is the
	// same thing as the blob base, because the block is the blob's prefix.
	//
	// A SendProp addressing a block field is offset by this, and so is every
	// edict_t::StateChanged() call about one: the engine matches change offsets
	// against SendProp offsets and nothing else, so the two arithmetics have to
	// be the same arithmetic. Defined beside the blob offset it wraps.
	static int BlockBase();

	// Announces a write to the block field at nBlockOffset, as the ONE change
	// offset the engine can match against the SendProp that addresses it.
	//
	// THIS IS THE WHOLE OF WHAT "MARKS EXACTLY ITS OWN OFFSET" MEANS, and it
	// is why no path on this hierarchy calls the argument-less
	// edict_t::StateChanged() any more. That form sets FL_FULL_EDICT_CHANGED,
	// which tells the engine to re-compare every prop in the table -- 500-odd
	// of them at the top tier -- and to send the whole entity to a local
	// client rather than the words that moved. It is not a wrong VALUE, which
	// is why no functional test would ever catch it; it is bandwidth and delta
	// work, silently, for the life of the build. plan.md §10.
	void MarkBlockChanged( int nBlockOffset );

	// EVERYTHING THAT FOLLOWS FROM A MOVE, once the block has been written:
	// the spatial partition, the two block marks, the children, and the
	// triggers -- in that order and for the reasons at the definition
	// (shell_standalone_base.cpp). SetAbsTransform is one caller;
	// ENTITY_OP_POSITION_CHANGED is the other, which is how managed code that
	// writes the block itself says it has moved (plan_entity.md step 1b).
	void OnPositionChanged( const Vector &vecPrevOrigin, bool bTouchTriggers = true );

	// The offset of one EngineBlockHead_t field, measured from the block's own
	// base -- which is what both MarkBlockChanged and the SendProp builder in
	// shell_standalone.cpp take. Head is the first member of EngineBlock_t, so
	// the outer offsetof is zero today and is written anyway: it is the thing
	// that would move if a field were ever added ahead of the head.
	#define SS_BLOCK_HEAD_OFFSET( field ) \
		( (int)( offsetof( EngineBlock_t, Head ) + offsetof( EngineBlockHead_t, field ) ) )

	// Allocated the way the engine allocates an entity: PvAllocEntPrivateData
	// returns ZEROED memory, and the SDK depends on that everywhere —
	// CBaseEntityOutput has a destructor and no constructor, so its action list
	// is NULL only because the allocation was.
	//
	// THE BLOB LENGTH IS AN ARGUMENT, and the plain form is deliberately NOT
	// declared (Part 18). Every entity here used to be allocated the greediest
	// class's blob because operator new is a static function with no idea which
	// class the factory is about to make; it is per class now, and the way that
	// is made safe is that there is no un-sized spelling of `new` for this
	// hierarchy at all. `new CStandaloneEntity` does not compile. A
	// missing size would otherwise be a blob shorter than the table addressing
	// it, which is the silent corruption this whole design is written against.
	void *operator new( size_t nBytes, int nBlobBytes );
	void operator delete( void *pMem );

	// The placement delete that pairs with it. Never called in this build —
	// nothing here throws — but a compiler that cannot find it warns, and the
	// warning is right: without it a constructor that threw would leak the
	// allocation.
	void operator delete( void *pMem, int nBlobBytes );

	// Binds the instance to its class and gives it a handle, before the map
	// hands it a single key.
	//
	// Called by the factory, and that timing is the whole point. The managed
	// instance is keyed by handle and created on the first keyvalue it is
	// offered, so an entity whose handle only arrived at Spawn had already
	// dropped every value the map wrote — silently, because a key nobody
	// claimed looks exactly like a key the map never had.
	//
	// CBaseEntity::PostConstructor does the same job at the same moment.
	void PostConstruct( const StandaloneClassDesc_t *pClass );

	// Places it in the world. Called after the keyvalues, never before: an
	// entity put somewhere before it has been told where to go has to be moved.
	virtual void Spawn();

	// --- IHandleEntity -------------------------------------------------------
	// Not just a store: the handle's serial has to reach the EDICT, because
	// that serial is the only thing telling the engine an edict slot changed
	// occupants. Defined in shell_standalone_base.cpp; the story is on
	// standalone_netserial.h.
	virtual void SetRefEHandle( const CBaseHandle &handle ) OVERRIDE;

	// THE HANDLE IS THE BLOCK'S, and the reference is an overlay on its uint32.
	// CBaseHandle is one uint32 and nothing else -- standard layout, no vtable,
	// no padding -- which shell_standalone_base.cpp asserts rather than assumes.
	virtual const CBaseHandle &GetRefEHandle() const OVERRIDE { return BlockHandle(); }

	// --- IServerUnknown ------------------------------------------------------
	virtual ICollideable *GetCollideable() OVERRIDE { return this; }
	virtual IServerNetworkable *GetNetworkable() OVERRIDE { return this; }

	// THIS ENTITY, as its entity face: the one accessor since
	// plan_server_native_v2 T1.3. It answered NULL while the server also had
	// the SDK's CBaseEntity tree, and the patch's GetGameEntity() answered
	// `this` beside it; once no SDK entity was left the two partitioned the
	// world identically, and stock iserverunknown.h has only this one. On the
	// stock build its type is CBaseEntity, which IGameEntity aliases (D1).
	// handle_cross.h's partition reads it: non-NULL is "a standalone", and
	// the world answers NULL.
	virtual IGameEntity *GetBaseEntity() OVERRIDE { return this; }

	// --- IServerEntity -------------------------------------------------------
	virtual int GetModelIndex( void ) const OVERRIDE { return Block().Head.ModelIndex; }
	virtual string_t GetModelName( void ) const OVERRIDE { return NULL_STRING; }

	// Out of line, because it is a WRITE: the model index is networked, so the
	// engine has to be told which four bytes moved. See MarkBlockChanged.
	virtual void SetModelIndex( int index ) OVERRIDE;

	// --- IServerNetworkable --------------------------------------------------
	virtual IHandleEntity *GetEntityHandle() OVERRIDE { return this; }

	// The class registered for THIS entity's classname if it declared state of
	// its own, and the one every entity on the hierarchy shares if it did not.
	//
	// Resolved on the first ask and kept: the engine asks per entity per
	// transmit, and a walk of string compares is not what that should cost.
	virtual ServerClass *GetServerClass() OVERRIDE;

	// The class's own networked state, in the block allocated past this object.
	// See SourceSharp_StandaloneBlobOffset below for why it lives there.
	unsigned char *DataBlob();

	// HOW LONG THAT BLOCK IS, FOR THIS ENTITY (Part 18).
	//
	// Per instance, because the tier is per class now. Everything that reads or
	// writes the blob must ask THIS rather than a global: a length that is no
	// longer true for the entity being read is not an error anywhere, it is a
	// read past the end of the allocation or a payload silently truncated.
	int BlobBytes() const;

	// And which tier that is, for the callers that want the index rather than
	// the length — the ServerClass lookup and the payload word count.
	int Tier() const { return m_iTier; }

	// Tells the engine this entity's networked state has moved.
	//
	// Every OTHER mutation on this shell does it as part of the setter — the
	// model index, the origin, the angles. The blob has no setter to hang it
	// off: C# is handed a pointer and writes through it, so nothing native sees
	// the write happen. Without this the engine never re-encodes the entity and
	// the value sits on the server, correct and unsent.
	void NetworkStateChanged();
	virtual edict_t *GetEdict() const OVERRIDE { return m_pEdict; }
	virtual const char *GetClassName() const OVERRIDE { return GetClassname(); }
	virtual void Release() OVERRIDE { delete this; }
	virtual int AreaNum() const OVERRIDE { return BlockPVSInfo()->m_nAreaNum; }
	virtual CBaseNetworkable *GetBaseNetworkable() OVERRIDE { return NULL; }
	// The ENGINE fills this in, in BuildEntityClusterList, which is exactly why
	// the block's server tail reserves room for it rather than declaring it.
	virtual PVSInfo_t *GetPVSInfo() OVERRIDE { return BlockPVSInfo(); }

	// --- ICollideable --------------------------------------------------------
	virtual const Vector &OBBMinsPreScaled() const OVERRIDE { return Block().Head.Mins; }
	virtual const Vector &OBBMaxsPreScaled() const OVERRIDE { return Block().Head.Maxs; }
	virtual const Vector &OBBMins() const OVERRIDE { return Block().Head.Mins; }
	virtual const Vector &OBBMaxs() const OVERRIDE { return Block().Head.Maxs; }
	virtual void WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const OVERRIDE;
	virtual bool TestCollision( const Ray_t &, unsigned int, trace_t & ) OVERRIDE { return false; }
	// A RAY AGAINST THIS MODEL'S HITBOXES: a thunk into IManagedBones, defined
	// in shell_standalone_hierarchy.cpp. False still means "no hitboxes, use
	// the box", which is what a brush entity and a beam answer.
	virtual bool TestHitboxes( const Ray_t &ray, unsigned int fContentsMask, trace_t &tr ) OVERRIDE;
	// The COLLISION model, which is not the same number as the drawn one.
	//
	// A trigger is a brush you cannot see and can walk into; hiding it must not
	// stop it being a volume. Head.ModelIndex is what the client draws and goes
	// to zero when the entity is hidden — reading collision from it made every
	// invisible brush a SOLID_BSP with no BSP, which the engine tests against
	// nothing at all and reports as no overlap.
	virtual int GetCollisionModelIndex() OVERRIDE { return Block().Head.CollisionModelIndex; }
	// The brush model, for an entity that is one. SOLID_BSP traces against the
	// submodel itself rather than a box, and this is what it traces against;
	// answering NULL made a brush entity solid in name only.
	virtual const model_t *GetCollisionModel() OVERRIDE;
	virtual const Vector &GetCollisionOrigin() const OVERRIDE { return Block().Head.Origin; }
	virtual const QAngle &GetCollisionAngles() const OVERRIDE { return Block().Head.Angles; }
	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE { return Block().Head.WorldTransform; }
	// A cast and not a computation. The block stores a fixed-width int32 because
	// an enum's underlying type is a compiler's choice and the C# side mirrors
	// this byte for byte; the value is SolidType_t's and always was.
	virtual SolidType_t GetSolid() const OVERRIDE { return (SolidType_t)Block().Head.SolidType; }
	virtual int GetSolidFlags() const OVERRIDE { return Block().Head.SolidFlags; }
	virtual IClientUnknown *GetIClientUnknown() OVERRIDE { return NULL; }
	// Which half of the pair CGameRules::ShouldCollide is asked about.
	//
	// This was a constant COLLISION_GROUP_NONE, and for every entity on this
	// hierarchy so far that was the right answer. It is not an answer a brush
	// that restricts vehicles can give: "solid to one kind of thing and to
	// nothing else" is not a shape or a flag, it is a group, and the rule that
	// reads it is already in the trace filter for both hierarchies.
	virtual int GetCollisionGroup() const OVERRIDE { return Block().Head.CollisionGroup; }
	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE;
	// Whether the engine should report this entity overlapping a trigger with
	// those solid flags. Asked of the entity that MOVED, once per candidate.
	//
	// This answered a flat no, and while nothing on this hierarchy ever asked
	// the engine for its trigger overlaps that cost nothing — the question was
	// never put. It is now, and a flat no is a veto on every one of them: the
	// engine finds the overlap, asks, and discards it. Measured exactly that
	// way, with two managed props sitting inside a managed trigger's volume and
	// the pair never once reported.
	//
	// CCollisionProperty's answer, which the engine reads identically whoever
	// gave it. Debris is spared unless the trigger asked for debris, and a
	// trigger does not touch other triggers — that second rule is what stops
	// every trigger in a map linking to every other one it overlaps.
	virtual bool ShouldTouchTrigger( int triggerSolidFlags ) const OVERRIDE
	{
		if ( GetCollisionGroup() == COLLISION_GROUP_DEBRIS )
			return ( triggerSolidFlags & FSOLID_TRIGGER_TOUCH_DEBRIS ) != 0;

		return ( Block().Head.SolidFlags & FSOLID_TRIGGER ) == 0;
	}
	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE { return NULL; }

	// --- IGameEntity ---------------------------------------------------------
	virtual bool ShouldCollide( int, int ) const OVERRIDE { return true; }

	// Concrete conveniences, no longer on IGameEntity: origin and angles are
	// ICollideable's GetCollisionOrigin/GetCollisionAngles, the classname is
	// IServerNetworkable's GetClassName, and the index is the edict's. The
	// shim asks the concrete type; game code asks the interface that already
	// answered — see Phase 11's duplicate eviction in igameentity.h.
	const Vector &GetAbsOrigin() const { return Block().Head.Origin; }
	const QAngle &GetAbsAngles() const { return Block().Head.Angles; }
	int EntIndex() const;
	const char *GetClassname() const { return m_szClassname; }
	void SetClassname( const char *pName );
	virtual const char *GetEntityName() const { return m_szName; }

	//-------------------------------------------------------------------------
	// BONES, on the server: MANAGED (plan physics_bones_native_zero 1.2).
	//
	// The skeleton -- the bone cache, the attachments, the sequence's duration,
	// the pose defaults, every decision about when to rebuild -- is
	// SourceSharp.Server's ServerSkeletons now, over the managed bone port.
	// What is left here is what the engine forces: TestHitboxes above (an
	// ICollideable virtual the engine's traces call, a thunk into
	// IManagedBones), and the model scale below, which player movement reads.
	// The sequence clock stays in the block (Client.Sequence / PlaybackRate /
	// AnimTime), written by managed code.
	//-------------------------------------------------------------------------

	// HOW BIG this entity wears its model: STORED here, DECIDED in managed
	// code. ServerSkeletons refuses zero and below and keeps its own copy for
	// the skeleton; what reaches this setter is a scale it took, and the copy
	// here is for the player's movement (IMovementPlayer::GetModelScale) and the
	// scaled-collision rule (scaled_collision_rule.h).
	void  SetModelScale( float flScale ) { m_flModelScale = flScale; }
	float GetModelScale() const { return m_flModelScale; }

	// --- being hurt ----------------------------------------------------------
	// IEntityDamageTarget's three, now the shell's own: the damage paths that
	// still ask are C++ with no shell of their own, and they come through the
	// relays in standalone_relays.h rather than through the boundary.

	// --- being talked to -----------------------------------------------------
	// IEntityIo's pair, now the shell's own: the event queue and the shims
	// that print a debug name are C++ with no shell of their own, and they
	// come through the relays in standalone_relays.h rather than through the
	// boundary.

	// --- being placed by a map -----------------------------------------------
	// IEntityMapPlaceable's five, now the shell's own: the map loader,
	// point_template and ent_create are C++ with no shell of their own, and
	// they come through the relays in standalone_relays.h rather than
	// through the boundary.
	virtual bool IsPlayer() const { return false; }
	virtual int TakeDamage( const ss::CTakeDamageInfo &info );
	int GetSpawnFlags() const { return m_nSpawnFlags; }

	// Replaces them wholesale rather than adding or removing bits, because
	// what wants it is a savegame restore putting back a value it read.
	// See NativeField.SpawnFlags on the managed side.
	void SetSpawnFlags( int nFlags ) { m_nSpawnFlags = nFlags; }

	virtual void Touch( const Toucher_t &toucher ) OVERRIDE;

	// The edges, for the one reporter that knows them: VPhysics says exactly
	// once that an object entered a trigger volume and exactly once that it
	// left. See the definitions for why these cannot go through Touch.
	virtual void StartTouch( const Toucher_t &toucher ) OVERRIDE;
	virtual void EndTouch( const Toucher_t &toucher ) OVERRIDE;

	// This entity AS a toucher, which only the concrete type can build: the two
	// halves of its identity are reached by different casts and IGameEntity is
	// not one of them.
	Toucher_t AsToucher();

	// Asks the engine which triggers this is standing in.
	//
	// CBaseEntity::PhysicsTouchTriggers, for an entity that is not one. The
	// engine looks for trigger overlaps only on behalf of an entity that ASKS —
	// SolidMoved enumerates the trigger list, TriggerMoved enumerates the solid
	// list — and nothing on this hierarchy ever asked.
	//
	// Measured before it did: across 337 consecutive overlap reports on a map
	// with 36 managed triggers and 34 managed props, every single one was the
	// player, because the player is the only entity left in this mod that still
	// runs CBaseEntity's version. So a managed trigger saw players and nothing
	// else, and a managed prop set off nothing at all.
	//
	// Neither reports anything when it is wrong: an overlap nobody looked for
	// looks exactly like an overlap that did not happen.
	void TouchTriggers( const Vector &vecPrevOrigin );

	// The solver moved it. Named for the SDK's call rather than for this side's
	// so one loop can drive both — see PhysicsOwner_t::VPhysicsUpdate.
	virtual void VPhysicsUpdate( IPhysicsObject *pObject ) OVERRIDE;

	virtual bool KeyValue( const char *szKeyName, const char *szValue );

	// THE SHELL'S OWN HALF OF KeyValue, SPLIT OUT -- plan_entity.md step 2d.
	//
	// KeyValue is the shell's arm followed by an UNCONDITIONAL offer to managed
	// code, and the offer is a crossing: on dm_lockdown that was 5709 of the
	// 7981 entity crossings a map load cost, one per key per entity, and 72% of
	// the whole budget.
	//
	// The managed map spawner (SourceSharp.Server/MapSpawner.cs) has the map's
	// text in managed memory and hands every key to ScriptedEntityRegistry
	// itself, with no crossing at all -- so what it needs from the shell is the
	// shell's arm ALONE. Calling KeyValue would deliver each key to managed a
	// second time and pay for it.
	//
	// One body, not two: KeyValue below is this plus the offer. The if-chain is
	// not copied anywhere, which is the whole point of splitting rather than
	// adding a second spelling of the same key names.
	bool KeyValueShellOnly( const char *szKeyName, const char *szValue );

	virtual bool AcceptInput( const char *szInputName, const Activator_t &activator,
	                          IGameEntity *pCaller, const ss::variant_t &Value );

	// --- what the map set, and what the managed side reads back --------------
	//
	// Spawnflags, health and angles live HERE rather than in the managed object
	// for the reason CBaseEntity keeps them: they are asked for by handle, from
	// other entities and from the engine API, and a value held only inside one
	// managed instance could not answer that.
	int  GetHealth() const { return m_iHealth; }
	void SetHealth( int nHealth ) { m_iHealth = nHealth; }

	// What it looks like.
	//
	// Precaches and applies in one go, because managed code sets this from its
	// own OnSpawn — which runs at the END of the shell's Spawn, so there is no
	// later moment left to apply it in. Precaching after level load warns, and
	// that warning is correct and worth hearing: a model a map did not declare
	// is a hitch the first time something wears it.
	void SetModelName( const char *pModel );

	// Whether it is drawn: EF_NODRAW in the block's Effects word (step
	// 8e-render), which is stock's own switch on stock's own storage.
	//
	// IT USED TO BE THE MODEL INDEX, and that was the defect 7a measured: the
	// index is what the entity LOOKS LIKE, not whether it is looked at, and
	// zeroing it left an invisible brush with no shape for the client to test
	// points against. See CStandaloneEntity::SetVisible for the whole chain.
	void SetVisible( bool bVisible );
	bool IsVisible() const { return ( Block().Head.Effects & EF_NODRAW ) == 0; }

	virtual void SetAbsOrigin( const Vector &vecOrigin );

	// SetAbsOrigin (and the angles) touching no trigger: stock's TeleportEntity.
	void TeleportTo( const Vector &vecOrigin, const QAngle *pAngles );

	// A real function, and it has to be one: the transform is built from the
	// angles as well as the origin, so an angles write that did not rebuild it
	// left the matrix rotation-free for the whole life of the entity.
	virtual void SetAbsAngles( const QAngle &angles );

	// Both at once, which is what the transform actually needs — the two
	// setters above are this one holding the other half still. See the
	// definition for why splitting them was the bug.
	void SetAbsTransform( const Vector &vecOrigin, const QAngle &angles );

	// What it does to a trace and to something walking into it.
	//
	// Re-inserts into the spatial partition, because which LISTS an entity
	// belongs to is decided by exactly these two values — see the definition,
	// which follows CCollisionProperty::UpdatePartition rule for rule.
	void SetSolidity( SolidType_t nSolidType, int nSolidFlags );

	// Its size, in its own space. A pickup is a box a player walks into, and
	// how big that box is decides whether they can collect it by brushing past.
	void SetCollisionBounds( const Vector &vecMins, const Vector &vecMaxs );

	// Which collision group it is in. Not what it collides AS — that is
	// SetSolidity — but what it collides WITH, since the group is the half of
	// the pair the game rules answer about.
	void SetCollisionGroup( int nGroup );

	// Where the mapper drew it, kept because movement is described relative to
	// it and the current origin has by then moved.
	const Vector &GetSpawnOrigin() const { return m_vecSpawnOrigin; }
	const QAngle &GetSpawnAngles() const { return m_angSpawnRotation; }

	const StandaloneClassDesc_t *GetClassDesc() const { return m_pClass; }

	// FireNamedOutput WAS HERE, AND IS DELETED -- plan_entity.md step 2a. An
	// output the class declared is fired by the class, in managed code:
	// Output.Fire posts into EntityDirectory's queue and no crossing happens.
	// See shell_standalone_base.cpp at the same place.

	//-------------------------------------------------------------------------
	// Being simulated.
	//
	// The solver stays native — it always was, and this hierarchy changes
	// nothing about that. What is new is that the physics object's GAME DATA is
	// NULL rather than an entity pointer.
	//
	// That is not a shortcut, it is the whole design. The SDK's physics bridge
	// reads game data as a CBaseEntity in 77 places, and every one of them null
	// checks first — the comment at CCollisionEvent::PostCollision even says the
	// NULL is expected, "to trap crashes due to NULLing out the game data on
	// delete". So an object with none is skipped cleanly by the entire bridge,
	// with not one line of it changed, and this hierarchy drives its own.
	//
	// What is given up is everything that bridge provides: physics damage
	// forces, impact sounds, water, the physcannon. Those are CBaseEntity
	// services and each has to be re-offered deliberately rather than inherited
	// by accident. See docs/divergences.md.
	//-------------------------------------------------------------------------
	void DestroyPhysics();
	IPhysicsObject *GetPhysics() const { return m_pPhysics; }

	// Stock's VPhysicsSwapObject: hold this object (NULL for none) and answer
	// the one held before, which is NOT destroyed. Managed makes the object
	// (EntityPhysicsCreation); the shell only holds what the engine reads.
	IPhysicsObject *SwapPhysics( IPhysicsObject *pObject )
	{
		IPhysicsObject *pPrevious = m_pPhysics;
		m_pPhysics = pObject;
		return pPrevious;
	}

	// Copies the solver's answer onto the entity. Once a frame, for anything
	// awake — an object asleep has not moved and asking would be a transform
	// rebuild and a network update for a value that did not change.
	void UpdatePhysics();

	//-------------------------------------------------------------------------
	// Moving.
	//
	// A brush entity is a slab of the map that goes somewhere: a door opens, a
	// platform rises, a fan turns. The SDK does this through CBaseToggle's
	// LinearMove and AngularMove, which are MOVETYPE_PUSH — the engine sweeps
	// the brush and carries or crushes whatever is in the way.
	//
	// This is the smaller thing: the shell integrates the position itself and
	// asks who is standing in the destination before committing to it. What
	// that buys is a door that stops when somebody is in the doorway, which is
	// the rule that matters. What it does NOT buy is CARRYING — a player
	// standing on a rising platform is not lifted by it. Recorded in
	// docs/divergences.md rather than left to be discovered.
	//-------------------------------------------------------------------------
	//-------------------------------------------------------------------------
	// Flying.
	//
	// A projectile is not a brush that moves: it travels in a line at a fixed
	// speed until it hits something, optionally falling as it goes. Stock does
	// it with MOVETYPE_FLYGRAVITY and lets the engine's own movement code sweep
	// it; this integrates the arc and traces the step itself, which is the same
	// arithmetic and reports the hit to managed code rather than to a touch
	// function named in a datadesc.
	//-------------------------------------------------------------------------
	void SetVelocity( const Vector &vecVelocity );
	void SetGravityScale( float flScale ) { m_flGravityScale = flScale; }
	float GetGravityScale() const { return m_flGravityScale; }
	const Vector &GetVelocity() const { return m_vecVelocity; }

	// THE VELOCITY A RIDER'S GROUND RESOLVES TO, held (plan_physics_bones_perf
	// C2, 2026-10-12). Managed decides it: its physics frame hands each active
	// own-object owner the solver's velocity (PhysicsUpdateOwners), and every
	// set is held at once (SetVelocity, with or without an object, as stock's
	// Teleport; DestroyPhysics hands the member back when the object goes).
	// Native stores and answers; StandaloneEntity_Velocity reads it per trace
	// without asking anything.
	void HoldMoveVelocity( const Vector &vecVelocity ) { m_vecMoveVelocity = vecVelocity; }
	const Vector &MoveVelocity() const { return m_vecMoveVelocity; }

	// Who launched it. Stored as a handle rather than a pointer because the
	// shooter can die mid-flight and a rocket must not carry a dangling one.
	//
	// The owner is a TRACE concern, not an attribution one — C# already tracks
	// who fired for the scoreboard. What this buys is the step below skipping
	// the shooter, which no managed check can do: by the time managed code sees
	// the touch, the flight has already been stopped and the projectile is
	// sitting in the player who fired it.
	void SetOwnerHandle( uint32_t hOwner );
	IHandleEntity *OwnerHandleEntity() const;

	// How many times it may bounce off what it hits before the flight ends.
	//
	// Zero is the ordinary projectile: the first thing it touches stops it. A
	// positive count reflects the velocity about the surface instead, which is
	// what an energy ball is.
	//
	// THIS COMMENT USED TO SAY THE REFLECTION HAD TO BE NATIVE, "because the
	// surface NORMAL only exists inside the step trace". That was true of a
	// trace nobody could ask about: the ABI's TraceLine hands the normal back,
	// so step 2f does the reflection in C# beside the decision it serves. What
	// is left here is the value's MIRROR -- managed holds the count it counts
	// down.
	void SetBounces( int nBounces ) { m_nBouncesLeft = nBounces; }

	// UpdateFlight WAS HERE. plan_entity.md step 2f moved one frame of flight
	// into SourceSharp.Server/EntityMotion.cs, which is where the decisions it
	// made belong; the block in shell_standalone_base.cpp where its body was
	// says what went with it and what did not. The two setters above stay: the
	// managed integrator is told these values at launch and the shell's copies
	// are what EntityGetGravity reads back for the save state.

	// MoveToPosition, MoveToAngles, StopMoving, UpdateMovement AND BlockerAt
	// WERE HERE, AND ARE DELETED -- plan_entity.md step 2g. Where a brush was
	// going, how far it got this frame, whether the person standing in its way
	// was an obstruction or a passenger, how fast it was actually travelling and
	// when it had arrived were five decisions about what the game does, and they
	// are SourceSharp.Server/EntityMotion.cs's push half now. The block where
	// their bodies were (shell_standalone_base.cpp) says what went with them.
	//
	// The riders are the thing that was never here at all, and are now.

	void SetAngularVelocity( const QAngle &angPerSecond );

	// Readable as well as writable, because a sensor entity exists to watch
	// something turn and report on it. Degrees per second about each axis, the
	// same units the setter takes. NOT what the integrator reads: the managed
	// side holds the number it steps, and this is the mirror anything else asks.
	const QAngle &GetAngularVelocity() const { return m_angVelocity; }

	// --- thinking is NOT HERE ------------------------------------------------
	//
	// It used to be: an m_flNextThink per shell, and a native walk of every
	// shell each frame asking whose time had come, each due one crossing into
	// managed code on its own. plan_entity.md §3.4 forbids exactly that shape
	// -- "Per-frame work is never one crossing per entity" -- so step 1g moved
	// the whole schedule to SourceSharp.Abi/ThinkScheduler.cs, which the one
	// GameFrame crossing already there runs. Booking a think now costs no
	// crossing and running N of them costs none either.
	//
	// So there is no SetNextThink, no GetNextThink and no Think() on this
	// class, and there is no ENTITY_OP_THINK relay below. Nothing native
	// decides when an entity thinks.

	// Deferred, always. Deleting an entity inside its own damage handler or its
	// own think frees it while the caller still holds it — UTIL_Remove exists
	// for exactly this reason.
	virtual void Remove();


protected:
	//-------------------------------------------------------------------------
	// WHERE THE MANAGED CONTRACT IS SPOKEN -- and it is spoken HERE, on the
	// base, which is the whole of plan_entity.md §3.4's "§3.4 makes the base do
	// that" (§3.1).
	//
	// These used to be empty inline bodies overridden by
	// CStandaloneScriptedEntity, a subclass that existed for no other purpose
	// than to hold thirteen relays into thirteen ABI slots. There is one slot
	// now, Dispatch, and one op number per event -- so the relay is four lines,
	// the same four lines every time, and a subclass to hold them would be a
	// class whose only member is a switch statement. The bodies are in
	// shell_dispatch.cpp.
	//
	// EVERY ONE OPENS WITH THE OVERRIDE BIT (StandaloneClassDesc_t
	// ::nOverrideMask). A class that does not override the managed virtual an
	// op maps to does not get crossed for; the ops where managed code does
	// BOOKKEEPING rather than merely calling a virtual -- spawn, keyvalue,
	// removed, filter -- are in ENTITY_OP_ALWAYS_MASK and cannot be switched
	// off. entity_op.h says why, per op.
	//
	// STILL VIRTUAL, because CStandalonePlayer overrides OnManagedKilled to do
	// its own death handling and then chains to the base.
	//-------------------------------------------------------------------------
	virtual void OnManagedSpawn();
	virtual bool OnManagedKeyValue( const char *szKeyName, const char *szValue );
	virtual bool OnManagedInput( const char *szInputName, const Activator_t &activator,
	                             const ss::variant_t &Value );

	// OnManagedRemoved IS GONE -- plan_entity.md step 2c. The base destructor's
	// call to it was one inbound crossing per entity removed, and what it carried
	// -- "tell the managed instance it is going, and drop the registry's row" --
	// is a managed call now, made by EntityDirectory's removal pass before it asks
	// for the shell to be freed. See EntityDirectory.RemovalNotify.

	// OnManagedStartTouch, OnManagedTouch AND OnManagedEndTouch WERE HERE, AND
	// ARE DELETED -- plan_entity.md step 2b. Each was one inbound crossing per
	// touching pair per frame, dispatched from the link table this shell used
	// to keep. The table is managed now, so the three edges are raised on the
	// managed instance directly, inside the one GameFrame crossing, and cost
	// nothing. ENTITY_OP_START_TOUCH, ENTITY_OP_TOUCH and ENTITY_OP_END_TOUCH
	// stay in the op enum: they are still what a class OVERRIDES, and
	// ClassOverridesOp still answers about them for anything that asks.

	// It ran into something.
	//
	// An IHandleEntity for OnManagedBlocked's reason, and it was measurably
	// costing more here: what a projectile hits is whatever the trace's m_pEnt
	// names, and half the solid things in this mod are on THIS hierarchy. Taken
	// as a CBaseEntity * the argument had to be narrowed through TraceEntity(),
	// which answers NULL for a standalone entity — so a managed rocket striking
	// a managed prop reported the same invalid handle as a rocket striking
	// nothing, and the managed side could not tell "I hit a crate" from "I hit
	// the sky". The comment on the old signature claimed the opposite ("the
	// thing hit may be on this hierarchy too"), which is what the narrow type
	// made impossible.
	//
	// NULL still means "nothing named it" — the trace stopped against no
	// entity at all. The world is entity zero and arrives as itself, exactly as
	// it did before.
	virtual void OnManagedImpact( IHandleEntity *pOther );

	// A movement it started has arrived, or has met something it cannot move
	// through. Where to go next and what to do about the obstruction are both
	// game rules, which is why they are asked rather than settled here.
	//
	// The blocker is an IHandleEntity for BlockerAt's reason: it may be on
	// either hierarchy, and in this mod the commonest blocker of all — a person
	// standing in a doorway — is on the one a CBaseEntity * cannot name.
	virtual void OnManagedMoveDone();
	virtual void OnManagedBlocked( IHandleEntity *pOther );

	// Returns the damage actually taken, which is also how "none" is said. The
	// shell applies it to health and reports death; deciding how much lands is
	// the managed side's.
	virtual int  OnManagedDamage( const ss::CTakeDamageInfo &info );
	virtual void OnManagedKilled( IGameEntity *pAttacker );

	// ReportTouch WAS HERE, AND IS DELETED -- plan_entity.md step 2b. It was the
	// shared body of the three touch edges, and all three are managed now.

public:
	//-------------------------------------------------------------------------
	// DOES THE MANAGED CLASS OVERRIDE THIS OP -- the test §3.4 is built on.
	//
	// One AND against a word in the class descriptor the entity is already
	// holding, which is why the op vocabulary is small contiguous integers
	// rather than names or hashes.
	//
	// A SHELL WITH NO CLASS ANSWERS YES. m_pClass is NULL between the
	// allocation and PostConstruct, and an entity that crossed when it did not
	// need to is a wasted call; an entity that did NOT cross when it should
	// have is a spawn that never reached C#. The two are not symmetrical, so
	// the unknown case takes the safe side.
	//-------------------------------------------------------------------------
	bool ClassOverridesOp( int nOp ) const
	{
		if ( !m_pClass )
			return true;

		return ( m_pClass->nOverrideMask & ( 1u << (uint32_t)nOp ) ) != 0;
	}

public:
	// THE NETWORKED PAIR MOVED INTO THE BLOCK (step 1b).
	//
	// m_vecOrigin and m_nModelIndex were public for one reason: the SendProp
	// offsets were taken against them, with `(intp)&pNull->m_vecOrigin`. They
	// are EngineBlockHead_t::Origin and ::ModelIndex now, and the props are
	// built from BlockBase() plus offsetof -- the same arithmetic every
	// StateChanged() call about them uses, which is what makes the engine able
	// to match one against the other.
	//
	// What the entity collides as is EngineBlockHead_t::CollisionModelIndex,
	// which a hidden entity still has; ModelIndex is what the client draws and
	// goes to zero when the entity is hidden.

	// How big the model is worn. See SetModelScale above; 1.0f is "as the
	// artist built it" and is what every entity that never asks keeps.
	float                     m_flModelScale;

	// The angles are EngineBlockHead_t::Angles. Networked since the props
	// arrived: a tumbling crate that sent only its origin slid around the floor
	// perfectly upright, which reads as a physics bug and is a missing SendProp.

	// NOT networked any more. The name is still what the server addresses this
	// entity by — factories, GetClassname, the event queue — and it is still 64
	// bytes of it. What goes on the wire is the four below.
	char   m_szClassname[ 64 ];

	// The discriminators (plan_nettable_generic.md Part 3 and Part 6).
	//
	// m_nTypeId is FNV-1a of m_szClassname, set by SetClassname so the two
	// cannot get out of step — a type id assigned anywhere else would be a
	// second place the mapping lives, and the failure of the two disagreeing is
	// an entity that pairs with the wrong managed class and decodes its bytes.
	//
	// m_nSchemaHash and m_nPayloadWords are stamped once, at the first
	// GetServerClass, out of the published type table: they are facts about the
	// CLASS rather than the instance, and every entity of the class carries the
	// same pair. Stamped rather than proxied because a send prop reading a
	// static through a proxy is exactly the custom-proxy cost Part 1A rules
	// out, and four bytes per entity is cheaper than a slow-list prop.
	uint32 m_nTypeId;
	uint32 m_nSchemaHash;
	uint32 m_nPayloadWords;

public:
	// The entity's own EHANDLE, encoded the way every other managed call
	// encodes one — so a standalone entity is not a second kind of thing with a
	// second numbering, it is an entity the managed side can already name.
	//
	// Public because the entity API hands this out: SourceSharp_CreateEntity
	// answers C# with the handle of whatever it just made, and for a class on
	// this hierarchy that is this. Protected was right while only the shell's
	// own dispatch called it, and stopped being right the moment C# could
	// create one.
	uint32_t ManagedHandle() const;

public:
	// Whether GetServerClass has run, which is the same question as "have
	// m_nSchemaHash and m_nPayloadWords been stamped yet". The reload resync
	// asks it: an entity nobody has asked for a class has nothing stale to put
	// right, and stamping it early would hide a bug in the path that should.
	bool HasServerClass() const { return m_pServerClass != NULL; }

	// Ask GetServerClass again, which is what re-stamps m_nSchemaHash and
	// m_nPayloadWords from the type table as it stands now.
	//
	// The clear is the whole of it: GetServerClass early-returns on
	// m_pServerClass, so an entity is stamped once for its whole life. That is
	// right for an encode and wrong for a managed reload, which republishes
	// every type and moves the hash of any that was reshaped. Written here
	// rather than in the reload walk so that the RULE for what an entity
	// carries has one home -- a second copy of it would agree today and drift
	// the first time the rule changes.
	void RestampServerClass()
	{
		m_pServerClass = NULL;
		GetServerClass();
	}

protected:
	// Resolved lazily by GetServerClass. NULL means "not asked yet" and never
	// "no class", because the shared class is always there to fall back to.
	ServerClass *m_pServerClass;

	// THE TIER THIS INSTANCE WAS ALLOCATED AT (Part 18).
	//
	// Copied from the class desc in PostConstruct, from the same field the
	// factory sized the allocation with. It decides three things that must all
	// agree: how many bytes exist past the object, which ServerClass the engine
	// is told this entity is, and how many payload words that class's table
	// carries. One member rather than three, so they cannot drift.
	int          m_iTier;

	char         m_szName[ 64 ];

	// The model, if it has one. A shell does not care whether it does — that is
	// what StandaloneAnimating means in C#, and here it is just an index that
	// may be zero.
	char         m_szModel[ 128 ];

public:
	// The model name as it was set, for an entity that wants to WEAR another's.
	// Public rather than a friend declaration: it is the same string SetModelName
	// was handed, and there is nothing to protect about it.
	const char *GetModelNameString() const { return m_szModel; }

private:
	int          m_nSpawnFlags;
	int          m_iHealth;

	Vector       m_vecSpawnOrigin;
	QAngle       m_angSpawnRotation;

	// The bounds, the solid type, the solid flags, the collision group and the
	// collision model index are all EngineBlockHead_t's. Every reason they were
	// stored rather than derived still holds -- SOLID_NONE for a logical entity
	// that must not stop a bullet, a collision group because two identical
	// invisible brushes differ only in who may walk through, a collision model
	// index that hiding does not clear -- and the storage is simply somewhere
	// the engine's getters can reach in one load and managed code can write.

	// The solver's object for this entity, or NULL for one that is not
	// simulated — which is most of them.
	IPhysicsObject *m_pPhysics;


	// Puts it in, moves it within, or takes it out of the spatial partition,
	// according to what it is currently solid as. One place, because the lists
	// an entity is in and the lists it should be in disagreeing is a bug with
	// no symptom until something fails to find it.
	void UpdatePartition();

	// The class this instance is. NULL only for a shell created outside a
	// factory, which nothing does.
	const StandaloneClassDesc_t *m_pClass;

	// m_Outputs WAS HERE, AND IS DELETED -- plan_entity.md step 2a. It held one
	// COutputEvent per output the class declared, each with a CEventAction list
	// the map's connections were parsed into. The whole of that is managed now
	// (EntityConnections in SourceSharp.Abi/EntityIo.cs), which is what lets a
	// one-shot's remaining firings and a delayed output already in flight
	// survive a hot reload -- neither could be reached from C# while they lived
	// here.

	// Touch_t AND m_Touching WERE HERE, AND ARE DELETED -- plan_entity.md step
	// 2b. They were this hierarchy's touch link table: who is inside this
	// entity, when each was last seen, and whether the solver is holding the
	// link open. Diffing that set from frame to frame is what turns the engine's
	// "these two are touching, again" into an arrival and a departure, and it is
	// a DECISION about the game rather than a thing the engine demands -- so it
	// is EntityDirectory's touch half now (SourceSharp.Abi/EntityDirectory.cs).
	//
	// What is left on this side is the REPORT: SourceSharp_EntityOverlapReport
	// hands managed code every overlapping pair once per frame and managed code
	// says which of them are new, which are still there and which have gone.

	// WHAT A MOVEMENT IN PROGRESS IS DOING IS NOT STORED HERE ANY MORE --
	// plan_entity.md step 2g. m_vecMoveTarget, m_angMoveTarget, m_flMoveSpeed,
	// m_flAngleSpeed, m_bMovingLinear and m_bMovingAngular were six pieces of
	// native state that were neither in the engine block nor networked, which
	// §3.5 says do not exist; EntityMotion holds them.
	//
	// m_bFlying went with them. It answered "was this velocity set as a flyer's
	// or as a mover's" for shell_mover_velocity.h, and that question only had to
	// be asked while two different owners wrote m_vecVelocity. One managed class
	// owns both now.
	//
	// The three that stay are MIRRORS the ABI reads back, not state anything
	// here integrates: m_vecVelocity is what a rider's ground velocity resolves
	// to and what EntityGetVelocity answers, m_flGravityScale and
	// m_nBouncesLeft are what EntityGetGravity answers for the save state, and
	// m_angVelocity is what a sensor watching something turn reads.
	Vector       m_vecVelocity;
	Vector       m_vecMoveVelocity;   // HoldMoveVelocity's
	float        m_flGravityScale;
	int          m_nBouncesLeft;
	QAngle       m_angVelocity;

	// TouchEdge_t, TellToucher AND UpdateTouchLinks WERE HERE, AND ARE DELETED
	// -- plan_entity.md step 2b.
	//
	// UpdateTouchLinks was the per-frame expiry sweep over m_Touching; the
	// managed diff is that sweep, and it now happens where the link table is.
	//
	// TellToucher relayed the edge to the entity that walked IN, and it could
	// only ever reach an SDK toucher: its own body opened with
	// `if ( !toucher.pBase ) return;`. What it did for one, its own paragraphs
	// recorded and measured -- StartTouchAny/TouchAny/EndTouchAny with a toucher
	// carrying no pBase (which is every toucher AsToucher builds) "walks the SDK
	// entity's parent chain and does nothing else: it cannot enter a link list,
	// reach managed code, or arrive back here". A relay that delivers nothing is
	// not a thing to carry into the managed table, so it was deleted rather than
	// reproduced -- and the reasoning is here rather than in a doc because the
	// doc that used to hold it (docs/divergences.md) went with the plan that
	// wrote it.

private:
	//-------------------------------------------------------------------------
	// The typed views of the two block fields whose C++ type is not the type
	// the block declares.
	//
	// Everything else in the head is already spelled as the type the getter
	// returns -- Vector, QAngle, matrix3x4_t, int32 -- and is reached as
	// Block().Head.<Field> with nothing in between. These two are not:
	//
	//   * a HANDLE is a uint32 in the block, because the C# declaration mirrors
	//     the block byte for byte and CBaseHandle is a C++ class it does not
	//     have. It is a class with exactly one uint32 member, no bases and no
	//     virtuals, so the overlay is a reinterpretation of four bytes and not
	//     a copy -- and shell_standalone_base.cpp static_asserts the size
	//     rather than trusting it.
	//   * PVSInfo_t is the ENGINE's struct, and §3.2 is deliberate about not
	//     restating it in C#: the block reserves the ROOM and the layout test
	//     asserts the engine's real struct still fits. So the storage is
	//     uint64[5] and this is the only place that names what lives in it.
	//
	// BlockPVSInfo is const and hands back a mutable pointer, which is right
	// rather than sloppy: GetPVSInfo's whole purpose is to give the engine
	// somewhere to WRITE, and AreaNum -- a const getter -- reads the same
	// storage. The constness of this shell says nothing about it.
	//-------------------------------------------------------------------------
	CBaseHandle &BlockHandle()
	{ return *reinterpret_cast< CBaseHandle * >( &m_pEngineBlock->Head.Handle ); }
	const CBaseHandle &BlockHandle() const
	{ return *reinterpret_cast< const CBaseHandle * >( &m_pEngineBlock->Head.Handle ); }

	CBaseHandle &BlockOwner()
	{ return *reinterpret_cast< CBaseHandle * >( &m_pEngineBlock->Head.OwnerHandle ); }
	const CBaseHandle &BlockOwner() const
	{ return *reinterpret_cast< const CBaseHandle * >( &m_pEngineBlock->Head.OwnerHandle ); }

	PVSInfo_t *BlockPVSInfo() const
	{ return reinterpret_cast< PVSInfo_t * >( &m_pEngineBlock->Server.PvsInfo[ 0 ] ); }

	edict_t                 *m_pEdict;

	// False for exactly one kind of entity: the player, whose edict the ENGINE
	// allocated for the connecting client and will free with the client. An
	// edict this entity created it also removes; an edict it was handed it
	// only unhooks (see the destructor, which mirrors what ClientDisconnect
	// has always left behind for stock players).
	bool                     m_bOwnsEdict;

	// The PVS info and the world transform are the block's -- the first in its
	// server tail, because the ENGINE writes that one, and the second in its
	// head beside the origin and angles it is built from.
	SpatialPartitionHandle_t m_Partition;
};


//-----------------------------------------------------------------------------
// Where a hierarchy entity's C#-declared state lives.
//
// A trailing block past the object rather than a member array: the size comes
// from C# at DLL init and is not a constant. Both numbers are defined in
// shell_standalone_class.cpp, which is where the concrete shell is and
// therefore the only place sizeof() means anything.
//
// ONE SIZE PER CLASS SINCE PART 18, where it used to be one size for every
// class on the hierarchy. The note here said per-class sizing would mean telling
// operator new through a global set immediately beforehand — a hidden argument —
// because operator new is a static function with no idea which class the factory
// is about to make.
//
// That was true of operator new and false of the FACTORY. There is one
// CStandaloneScriptedClass per registered classname and it holds that name, so
// the allocation site always knew the class; only the signature was hiding it.
// The length is an argument now (see operator new above), the plain form is not
// declared, and there is no global to set.
//-----------------------------------------------------------------------------

int  SourceSharp_StandaloneBlobOffset();

//-----------------------------------------------------------------------------
// The smallest tier a shell on this hierarchy can be allocated at, and its
// length in bytes (step 1b).
//
// The engine block is the blob's fixed 352-byte prefix, so a blob shorter than
// the block is not a small entity -- it is an overrun. The tier table starts at
// 32 bytes and one live path reaches that floor (a classname the type table has
// not published yet), so the floor is searched for rather than assumed.
// Defined in shell_standalone_base.cpp beside operator new, which is the caller
// that matters.
//-----------------------------------------------------------------------------
int  SourceSharp_StandaloneMinTier();
int  SourceSharp_StandaloneMinBlobBytes();

//-----------------------------------------------------------------------------
// The player's edict, on its way from the engine to PostConstruct (Phase 9a).
//
// The engine allocates a player's edict itself — index 1..maxplayers, handed to
// ClientPutInServer — so the one entity that binds to a pre-existing edict
// needs a way to say so before PostConstruct runs. The same shape as
// CBasePlayer::s_PlayerEdict, for the same reason: the factory's Create takes
// no arguments, so the edict travels beside the call rather than through it.
//
// Set immediately before the factory call, claimed exactly once inside
// PostConstruct (which also marks the entity as not owning it), and never left
// set: a pending edict nobody claimed is a bug, and PostConstruct clearing it
// unconditionally is what keeps the NEXT entity from binding to a client.
//-----------------------------------------------------------------------------
void     SourceSharp_SetPendingPlayerEdict( edict_t *pEdict );
edict_t *SourceSharp_ClaimPendingPlayerEdict();


//-----------------------------------------------------------------------------
// Builds the SendTable and ServerClass for a hierarchy class that declared
// state of its own: the four fields every entity here shares, then the class's
// own at the blob offsets C# computed.
//
// False when the name is not a registered hierarchy class, which is how the
// caller knows the description belongs to the other shell.

//-----------------------------------------------------------------------------
// Parenting: a standalone child carried by an entity of either hierarchy.
//
// Declared here and RELAYED in shell_standalone_hierarchy.cpp, which is a
// different thing from implemented. The registry, the composition, the cycle
// refusal, the deferred `parentname` and the frame rule are
// SourceSharp.Abi/Hierarchy.cs; every function below is a guard and a call into
// it. Read that file for the shape and this one's header for the guard.
//
// A position write reaches two of these on every call, and each of the two is
// gated on a word in the entity's own ENGINE BLOCK — ChildCount for "something
// hangs off me", ParentHandle for "something carries me", both written by
// managed code. An entity nothing is parented to or from pays a load and a
// branch and crosses nothing.
//
// TWO NAMES ARE NOT HERE AND WERE NOT REPLACED. The once-a-frame sweep and the
// level teardown are driven from C# — SourceSharp.Server's StandaloneHierarchy
// already owns this hierarchy's frame and its shutdown — so they run inside
// crossings that already happen rather than through relays of their own.
//-----------------------------------------------------------------------------

// Answered from the block, with no crossing at all: managed code writes the
// parent's handle when it makes a link and clears it when it drops one, and this
// resolves that handle. A parent whose model lacks the named attachment still
// CARRIES the child, so this is a different question from whether the child's
// frame can be built right now.
bool StandaloneHierarchy_IsParented( const CStandaloneEntity *pChild );

// The map's keys, recorded during the parse and turned into a link afterwards.
// The attachment is a NAME here, because an index means nothing until the
// parent — and therefore the parent's model — has been found.
void StandaloneHierarchy_SetParentName( CStandaloneEntity *pChild, const char *pParentName );
void StandaloneHierarchy_SetParentAttachmentName( CStandaloneEntity *pChild,
                                                  const char *pAttachmentName );

// The two halves of a move. A parent that moved carries its children; a child
// that moved itself has a new offset from its parent.
void StandaloneHierarchy_ParentMoved( CStandaloneEntity *pParent );
void StandaloneHierarchy_ChildMoved( CStandaloneEntity *pChild );

// Between the world's frame and the parent's, in place. False, having touched
// nothing, for an entity with no parent — so a caller that does not care about
// the difference can ignore the answer and still be right.
bool StandaloneHierarchy_WorldToLocal( const CStandaloneEntity *pChild,
                                       Vector *pOrigin, QAngle *pAngles );
void StandaloneHierarchy_SetLocal( CStandaloneEntity *pChild, const Vector &vecOrigin,
                                   const QAngle &angles );

//-----------------------------------------------------------------------------
// Tells the managed side that a shell NATIVE decided to free is going, before
// it goes. See shell_dispatch.cpp for why exactly one caller has this and every
// other removal goes through EntityDirectory's free pass instead.
//-----------------------------------------------------------------------------
void SourceSharp_AnnounceStandaloneRemoved( CStandaloneEntity *pEntity );

//-----------------------------------------------------------------------------
// Making a cause current for the length of a call.
//
// Declared here rather than kept private to the shell because the one bridge
// C# fires an output through serves BOTH hierarchies, and an activator named
// from managed code has to be current for whichever shell the handle turns out
// to name. Nesting is the normal case: an input handler that fires an output
// which lands on another entity stacks these, so the previous cause is restored
// rather than cleared.
//
// Built per call and never stored, like the Activator_t it holds.
//-----------------------------------------------------------------------------
// CStandaloneActivatorScope went (plan_server_native_min P1): its global had
// no reader.


//-----------------------------------------------------------------------------
// THE BOUNDARY TO THE SHELL: ONE CONVERSION, ONE PLACE, CHECKED IN DEBUG.
//
// plan_entity.md §3.16 family 5 rules that a downcast onto an entity path is
// not a thing this port keeps: "the shell is `final`, so there is no subtype to
// cast to; and the concrete pointer comes from the slot table, which stored it
// at bind time." The slot table answers that when a HANDLE is what you hold --
// `SourceSharp_ShellFromHandle`, the one entry §5's allowlist blesses. THIS is
// the other direction, where what you hold is already the object and the only
// thing missing is its concrete type, and it had FIFTEEN spellings scattered
// across eight files, each with its own paragraph arguing its own safety.
//
// Collapsed to one, for the same reason `PhysicsOwnerOf` and
// `GameEntityFromEntityHandle` are one: a conversion argued in eight places is
// a conversion nobody can check.
//
// WHY IT IS SAFE, ONCE. `CStandaloneEntity` is `final` and is the ONLY class in
// server.so deriving from `IGameEntity` -- `CWorld` answers NULL to
// `GetBaseEntity()` by design (world.h: "this object is not a CBaseEntity, and
// saying otherwise is exactly the lie this port has spent its length
// removing"), so it never arrives here as one.
//
// AND IT IS NOT LEFT AS AN ARGUMENT. IGameEntity is polymorphic, so
// `dynamic_cast` is EXACT; the debug build compares the two on every call and
// asserts they agree. Release keeps the static_cast and pays nothing. That
// turns "every IGameEntity in this build is a shell" from a sentence in eight
// comments into something a debug run falsifies the first time it is untrue.
//
// SAID PLAINLY BECAUSE tools/entity-gates CANNOT SEE IT: gate 3 scans compiled
// TUs, not headers, and that is a FLOOR the tool declares in its own banner.
// These conversions did not stop existing when they moved here; there is ONE of
// them now instead of fifteen, it is named, and it is checked.
//-----------------------------------------------------------------------------
inline CStandaloneEntity *SourceSharp_ShellOf( IGameEntity *pEnt )
{
	Assert( !pEnt || dynamic_cast< CStandaloneEntity * >( pEnt ) != NULL );
	return static_cast< CStandaloneEntity * >( pEnt );
}

inline const CStandaloneEntity *SourceSharp_ShellOf( const IGameEntity *pEnt )
{
	return SourceSharp_ShellOf( const_cast< IGameEntity * >( pEnt ) );
}

//-----------------------------------------------------------------------------
// The same for an IServerNetworkable, which on this hierarchy IS the entity --
// CStandaloneEntity implements both faces itself, so this is a sidestep between
// two bases of one object rather than a walk down a tree.
//-----------------------------------------------------------------------------
inline CStandaloneEntity *SourceSharp_ShellOf( IServerNetworkable *pNet )
{
	Assert( !pNet || dynamic_cast< CStandaloneEntity * >( pNet ) != NULL );
	return static_cast< CStandaloneEntity * >( pNet );
}

//-----------------------------------------------------------------------------
// plan_entity.md 1e -- CHandle< CStandaloneEntity > IS BANNED, and the ban is
// here because this header is what makes the shell nameable.
//
// One line rather than a block: the whole argument, the mechanism and the
// compile-must-fail probe that keeps it honest are in the header itself.
// LAST in the file so it cannot be reordered ahead of the class it names.
//-----------------------------------------------------------------------------
#include "standalone_handle_ban.h"

#endif // SOURCESHARP_SHELL_STANDALONE_H
