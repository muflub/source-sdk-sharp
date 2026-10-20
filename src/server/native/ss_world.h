//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Purpose: The worldspawn entity. This spawns first when each level begins.
//
// Relocated from the fork's game/server/world.h (plan_server_novalve.md P5),
// which every server TU included in its place, so CWorld has this one body.
// It needs only public/ headers and server.so's own, so TUs on the R5 include
// set can reach it.
//=============================================================================//

#ifndef SS_WORLD_H
#define SS_WORLD_H
#ifdef _WIN32
#pragma once
#endif

#include "basehandle.h"
#include "edict.h"
#include "iserverentity.h"
#include "iservernetworkable.h"
#include "engine/ICollideable.h"
#include "standalone_netserial.h"
#include "shell_world_nettable.h"
#include "world_net_sync.h"
#include "igameentity_fwd.h"

//-----------------------------------------------------------------------------
// CWorld IS A SHIM (plan_gamedll_after.md Part B2), and this is what that means.
//
// The owner's ruling:
//
//     "CWorld needs a native shim class, but all the logic can be managed. It
//      should NOT DECLARE_SERVERCLASS -- that should all come from the managed
//      side. Network vars live in managed code, with C++ calling into managed
//      for get/set."
//
// So this class keeps exactly the two things the ENGINE needs from it and
// nothing else:
//
//   * IDENTITY -- it is edict 0 (RequiredEdictIndex), it owns brush model 1,
//     every trace that hits nothing hits it, mapentities.cpp dynamic_casts to
//     it to spawn the world first, and GetWorldEntity() is what fifteen native
//     files mean by "the world";
//   * the ENGINE-FACING VTABLE -- Spawn, Precache, KeyValue, DecalTrace, the
//     two VPhysics stubs, and GetServerClass.
//
// WHAT LEFT: ten CNetworkVar declarations and a DECLARE_SERVERCLASS. The values
// live in a blob past the object now, declared by
// SourceSharp.Shared.WorldNetState and reached by asking C# where each field is
// (world_net_sync.h). The ServerClass and every one of its props are built from
// that declaration in shell_world_nettable.cpp.
//
// THE SS_CBASEENTITY_ALIAS ARM WENT WITH THEM, and its deletion is the change
// rather than an oversight. Its whole subject was that the alias takes
// NetworkStateChanged with it, so "all ten CNetworkVar/CNetworkVector
// declarations below stop compiling -- in EVERY translation unit that includes
// this header". There are no CNetworkVars below. The pair of empty
// NetworkStateChanged shims it supplied had nothing left to serve, and a guard
// with no subject is a guard that reads as protecting something.
//
// The question that arm CARRIED has now been answered by taking it: armed,
// CWorld was abstract because C_BaseEntity/CBaseEntity become IGameEntity, so
// LINK_ENTITY_TO_CLASS( worldspawn, CWorld ) could not construct one. That
// ground is gone with the base class -- see below, and see the re-taken
// verdict in shim_underive_verdict_test.cpp.
//
//-----------------------------------------------------------------------------
// AND IT IS ITS OWN HIERARCHY (plan_cworld_trace.md T3). The owner's
// instruction, in full:
//
//     "CWorld should not inherit CStandaloneEntity either -- it should be its
//      own standalone class. A trace should be able to return a pointer to
//      CWorld, or CStandaloneEntity, or CBaseEntity."
//
// THREE HIERARCHIES IS THE END STATE, NOT A TRANSITIONAL ONE, and the reason
// the world does not simply join the parallel hierarchy is the same reason it
// does not DECLARE_SERVERCLASS: worldspawn IS NOT A SCRIPTED ENTITY.
// CStandaloneEntity is the shell for a class C# declares, spawns, gives inputs
// and outputs to, and drives per think; the world is a piece of the BSP that
// the engine names by index. Inheriting that shell would buy a base class this
// object does not want in order to reuse an ALLOCATOR -- and the allocator is
// the one thing it need not inherit, because over-allocating for the blob is a
// `new`, not a base. Both operator new forms below do it in four lines.
//
// WHAT IT IS, AND WHAT IT MERELY HAS. The owner's follow-up question was
// whether the world needs IServerEntity, IServerNetworkable and ICollideable at
// all, and whether it needs to be collideable. It needs all three answered; it
// needs to BE only one of them.
//
//   IServerEntity / IServerUnknown / IHandleEntity   -- INHERITED, and it is
//       the only base this class has. Eleven methods on one single-inheritance
//       chain. This is the type trace_t::m_pEnt holds (gametrace.h:84 is
//       IHandleEntity *, not CBaseEntity *) and the type an edict's owner is
//       reached through, so it has to be THIS OBJECT'S OWN vtable: the engine
//       hands the pointer back and the game asks it who it is.
//
//   IServerNetworkable   -- HELD, as m_Networkable. The engine never asks for
//       it: edict_t::m_pNetworkable is a pointer AttachToEdictZero assigns, and
//       CBaseEdict::AreaNum and GetClassName read it unguarded. A pointer to a
//       member is as good a pointer as a pointer to the whole, and it costs the
//       world ten methods it was implementing to satisfy a field.
//
//   ICollideable   -- HELD, as m_Collideable. Reached only through
//       IServerUnknown::GetCollideable(), which is also just a pointer the
//       engine asks for.
//
// AND THE COLLIDEABLE IS MEASURED RATHER THAN ARGUED. Every one of CWorld's
// collideable methods was instrumented with a first-call probe and driven
// through three level loads -- dm_lockdown twice and ss_sandbox (1241 entities,
// 275 classes) -- each with a player shooting, jumping, breaking every
// breakable, spawning physics props, setting off explosions and throwing things
// with the physcannon. Of the nineteen box-and-shape methods, TWO were ever
// entered, and neither by the engine:
//
//     GetCollisionModel   twice, both times from CWorld::Spawn just below
//     GetCollisionOrigin  once, from SourceSharpEntity_t::Origin
//
// TestCollision, TestHitboxes, OBBMins/OBBMaxs, WorldSpaceSurroundingBounds,
// GetSolid, GetSolidFlags, ShouldTouchTrigger and CollisionToWorldTransform
// were never entered at all, which is what the class's own comment always said:
// the world's collision is the engine's BSP clip against brush model 1 and it
// never asks the game. The structural corroboration is that this object never
// registers with the spatial partition, and the partition is what drives an
// ordinary entity's collideable traffic.
//
// THE ONE COLLIDEABLE METHOD THAT IS LIVE IS GetEntityHandle, and it is live in
// the tens of thousands. The engine's trace path is literally
//
//     pUnk->GetCollideable()->GetEntityHandle()
//
// -- two adjacent call sites in engine.so, measured 29,614 and 9,926 times in
// one session -- because a trace filter takes an IHandleEntity *. So the
// collideable is asked for constantly and asked only WHO OWNS IT. That is why
// CWorldCollideable::GetEntityHandle answers m_pOuter and not itself: an
// answer of `this` would put a member's address into trace_t::m_pEnt and every
// resolver in the game would then be reading an ICollideable as an entity.
//
// The stubs below are still IMPLEMENTED rather than asserted. The measurement
// covers three maps, not every map a mod might load, and a wrong answer that is
// sensible is a bad frame; a wrong answer that is an assert is a crash on
// somebody else's level.
//
// WHAT IT DOES NOT IMPLEMENT is as much the point: no datadesc, no think
// function, no touch links, no network property, no CBaseEntity anything.
//-----------------------------------------------------------------------------
class CWorld;
namespace ss { struct gamevcollisionevent_t; }
class IPhysicsObject;
class CGameTrace;
typedef CGameTrace trace_t;
struct Ray_t;
struct model_t;

//-----------------------------------------------------------------------------
// THE WORLD'S ICollideable, and its lifetime is the world's BY CONSTRUCTION.
//
// A member subobject, not a pointer and not an allocation: it is created with
// the CWorld, destroyed with the CWorld, and cannot be replaced or outlive it.
// m_pOuter is set in the member-initialiser list, so it is valid before the
// CWorld constructor body -- and therefore before AttachToEdictZero -- runs.
//
// The box is the MAP's bounds and the transform is the identity: the world is
// at the origin, unrotated, for the whole of every level. Kept as members
// because the interface hands back REFERENCES, and a reference to a temporary
// is a dangling one.
//-----------------------------------------------------------------------------
class CWorldCollideable : public ICollideable
{
public:
	explicit CWorldCollideable( CWorld *pOuter );

	// The map's own bounds, taken from the model at CWorld::Spawn. NOT the same
	// numbers as the world_mins / world_maxs KEYS -- those are the mapper's
	// declaration and ride DT_WORLD to the client renderer; these are what the
	// BSP is.
	void SetBounds( const Vector &vecMins, const Vector &vecMaxs )
	{
		m_vecMins = vecMins;
		m_vecMaxs = vecMaxs;
	}

	// THE ONE METHOD THE ENGINE ACTUALLY CALLS ON THIS OBJECT, tens of
	// thousands of times per session, and the one that must NOT answer `this`.
	// Defined below CWorld, because the answer is the owner.
	virtual IHandleEntity *GetEntityHandle() OVERRIDE;

	virtual const Vector &OBBMinsPreScaled() const OVERRIDE { return m_vecMins; }
	virtual const Vector &OBBMaxsPreScaled() const OVERRIDE { return m_vecMaxs; }
	virtual const Vector &OBBMins() const OVERRIDE { return m_vecMins; }
	virtual const Vector &OBBMaxs() const OVERRIDE { return m_vecMaxs; }

	virtual void WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const OVERRIDE
	{
		if ( pMins ) *pMins = m_vecMins;
		if ( pMaxs ) *pMaxs = m_vecMaxs;
	}

	// FALSE MEANS "use the ordinary test", NOT "nothing is there". Both of
	// these are the hook an entity uses to answer a ray ITSELF -- hitboxes, a
	// custom shape -- and the world has neither: the engine clips against the
	// BSP, which is the ordinary test.
	virtual bool TestCollision( const Ray_t &, unsigned int, trace_t & ) OVERRIDE { return false; }
	virtual bool TestHitboxes( const Ray_t &, unsigned int, trace_t & ) OVERRIDE { return false; }

	// Brush model 1, which is the world. Both read the owner's model index, so
	// there is one place the number lives and it is CWorld's.
	virtual int GetCollisionModelIndex() OVERRIDE;
	virtual const model_t *GetCollisionModel() OVERRIDE;

	virtual const Vector      &GetCollisionOrigin() const OVERRIDE { return m_vecOrigin; }
	virtual const QAngle      &GetCollisionAngles() const OVERRIDE { return m_angRotation; }
	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE { return m_Transform; }

	virtual SolidType_t GetSolid() const OVERRIDE { return m_nSolidType; }
	virtual int         GetSolidFlags() const OVERRIDE { return m_nSolidFlags; }

	// Server side. There is no client half of THIS object: the client's world
	// is C_World and is still a C_BaseEntity.
	virtual IClientUnknown *GetIClientUnknown() OVERRIDE { return NULL; }

	virtual int GetCollisionGroup() const OVERRIDE { return COLLISION_GROUP_NONE; }

	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE
	{
		if ( pMins ) *pMins = m_vecMins;
		if ( pMaxs ) *pMaxs = m_vecMaxs;
	}

	// The world does not enter triggers. It is what the triggers are drawn in.
	virtual bool ShouldTouchTrigger( int ) const OVERRIDE { return false; }

	// Never parented. NULL is how every caller of this spells "no parent
	// transform", which is the identity by another name.
	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE { return NULL; }

private:
	CWorld      *m_pOuter;

	Vector       m_vecMins;
	Vector       m_vecMaxs;

	Vector       m_vecOrigin;
	QAngle       m_angRotation;
	matrix3x4_t  m_Transform;

	// SOLID_BSP, and it is the only honest answer: this object's shape IS the
	// level. Never changes after the constructor.
	SolidType_t  m_nSolidType;
	int          m_nSolidFlags;
};

//-----------------------------------------------------------------------------
// THE WORLD'S IServerNetworkable, held on exactly the same terms, and this is
// the object CGlobalEntityList::Clear() ACTUALLY GETS.
//
// Clear() reaches an entity as `GetServerNetworkable( handle )`, which is
// `pUnk->GetNetworkable()` (entitylist.h:265) -- so after this split it is THIS
// object, not the CWorld, that arrives at the fork:
//
//     if ( !SourceSharp_SDKEntityFromNetworkable< IServerEntity >( ent ) )
//         ent->Release();          <- the world's networkable arrives HERE
//     else
//         UTIL_Remove( ent );
//
// which makes two of the methods below load-bearing for the world's LIFETIME
// rather than for its behaviour:
//
//   GetBaseEntity   answers NULL, so the fork takes the Release arm. A
//                   non-NULL answer sends the world to UTIL_Remove, which
//                   reaches MarkForDeletion through a CServerNetworkProperty
//                   this object does not have.
//   Release         `delete m_pOuter`, which is stock's own spelling --
//                   CServerNetworkProperty::Release is `delete m_pOuter` for
//                   exactly this reason: a networkable that is a MEMBER of the
//                   entity destroys the entity, not itself.
//
// The empty Release that shipped as ffd3e097 is the bug this whole surface is
// haunted by; see the note on Release below and world_release_test.cpp.
//-----------------------------------------------------------------------------
class CWorldNetworkable : public IServerNetworkable
{
public:
	explicit CWorldNetworkable( CWorld *pOuter );

	// The engine fills this through GetPVSInfo(). ~CWorld hands it back to
	// engine->CleanUpEntityClusterList, which is the call
	// ~CServerNetworkProperty makes for every networked entity.
	PVSInfo_t *PVSInfo() { return &m_PVSInfo; }

	virtual IHandleEntity *GetEntityHandle() OVERRIDE;
	virtual ServerClass   *GetServerClass() OVERRIDE;
	virtual edict_t       *GetEdict() const OVERRIDE;
	virtual const char    *GetClassName() const OVERRIDE { return "worldspawn"; }

	// `delete m_pOuter`, AND THE COMMENT THAT ONCE STOOD HERE SAID THE OPPOSITE.
	//
	// It read: "NOT `delete this`. The world is destroyed by the entity purge at
	// level shutdown through the route it always took; a Release() that deleted
	// would be a second owner of one object." Every clause of that was true
	// while CWorld was a CBaseEntity, and the commit that wrote it is the commit
	// that made it false -- the same edit un-derived the class.
	//
	// "The route it always took" is CGlobalEntityList::Clear()
	// (entitylist.cpp:517), and it forks on exactly the property that commit
	// removed. Release IS the arm on this side, which is why
	// CStandaloneEntity::Release is `{ delete this; }` (shell_standalone.h:294)
	// and why stock's CServerNetworkProperty::Release is `delete m_pOuter`. A
	// no-op here is not a second owner declining to free; it is NO owner at all.
	//
	// WHAT AN UNFREED WORLD COSTS, measured rather than reasoned: ~CWorld never
	// runs, so g_WorldEntity is never cleared, so the NEXT map's
	// CWorldFactory::Create sees a world already standing and REFUSES to build
	// one. The second map then runs with edict 0 and entity-list slot 0 still
	// holding the previous map's world, and the first trace of the load --
	// SourceSharp_PaintDecal's, at level init -- faults inside the engine.
	// dm_lockdown paints 81 decals on the first load and dies on the 82nd.
	//
	// `delete m_pOuter` AND NOT `delete this`, and the difference is not
	// stylistic: this object is a member of the thing being destroyed, so
	// deleting it directly would be a delete of an interior pointer. The
	// owner's destructor destroys this member on its way out, which is why
	// nothing may touch this object after the line below.
	//
	// Pinned by world_release_test.cpp, and by the level-reload step every live
	// gate runs now: one map load could never have reached this.
	virtual void Release() OVERRIDE;

	// CALLED BY THE ENGINE THE MOMENT IT TRIES TO SEND ANYTHING, through
	// edict_t::m_pNetworkable and with no null check -- which is the crash
	// CStandaloneEntity::PostConstruct's own comment records costing an
	// afternoon. Read from the PVS info the engine itself fills.
	virtual int AreaNum() const OVERRIDE { return m_PVSInfo.m_nAreaNum; }

	virtual CBaseNetworkable *GetBaseNetworkable() OVERRIDE { return NULL; }

	// NULL, AND THIS IS THE LINE THAT KEEPS THE WORLD ON Clear()'s Release ARM.
	// SourceSharp_SDKEntityFromNetworkable asks exactly this; a non-NULL answer
	// moves the world to UTIL_Remove and back into the crash above.
	//
	// DEFINED BELOW rather than here, and the reason is a test rather than a
	// taste: CWorld answers IServerUnknown::GetBaseEntity with the same words,
	// so an in-class body would make the two textually identical and every
	// source-property anchor on either of them ambiguous. A qualified
	// definition is a signature; an in-class one is a prefix.
	virtual IGameEntity *GetBaseEntity() OVERRIDE;

	virtual PVSInfo_t *GetPVSInfo() OVERRIDE { return &m_PVSInfo; }

private:
	CWorld   *m_pOuter;
	PVSInfo_t m_PVSInfo;
};

class CWorld : public IServerEntity
{
public:

	CWorld();
	~CWorld();

	//-------------------------------------------------------------------------
	// THIS CLASS DECLARES NO SERVER CLASS OF ITS OWN. The ServerClass comes
	// from the managed declaration; this is the one line of the shim that hands
	// it to the engine.
	//
	// The sentence above is worded the long way ROUND ON PURPOSE.
	// StockServerClassCensus.NoDeclareServerclassIsLeftCommentedOut refuses a
	// comment whose first token is the macro, or the marker Phase A5 left, and
	// it is right to: that is what a commented-out invocation looks like, and
	// telling it apart from prose about the macro is not something a scanner
	// can do. The rule the fact enforces is the owner's, so the prose moves.
	//
	// CServerNetworkProperty::GetServerClass calls m_pOuter->GetServerClass()
	// once and keeps the answer, so this runs at most once per entity and the
	// table it names has to exist by then -- which it does, because a
	// ServerClass may only be built during DLL init and the first CWorld is
	// made when the first map is parsed.
	//-------------------------------------------------------------------------
	virtual ServerClass *GetServerClass();

	//-------------------------------------------------------------------------
	// THE BLOB, AND THE ONLY REASON THIS CLASS NEEDS AN operator new.
	//
	// CStandaloneEntity over-allocates so its C#-declared state sits past the
	// object; CBaseEntity does not, and that -- not who calls the factory -- is
	// the whole of why the parallel hierarchy could carry managed net state and
	// this class could not. worldspawn is created by THIS DLL through
	// CreateEntityByName exactly as every managed class is (mapentities.cpp:1199
	// -> util.h:134 -> util.h:66 `new T`), so giving it the same allocation
	// gives it the same capability.
	//
	// ONE FORM NOW. tier0/memdbgon.h used to rewrite `new` to its four-argument
	// debug form, so CWorld declared that too; since server.so compiles with
	// NO_MALLOC_OVERRIDE (P5 step 5) memdbgon rewrites nothing, nothing called
	// the debug pair, and the server gc cull (2026-10-01) deleted it.
	//
	// AND THE MATCHING operator delete: the block is the engine's, so it goes
	// back through engine->FreeEntPrivateData. There is no base to inherit one
	// from (CWorld derives from IServerEntity alone), and without it the
	// global delete freed engine memory in server.so's allocator.
	//-------------------------------------------------------------------------
	static void *operator new( size_t stAllocateBlock );
	static void operator delete( void *pMem );

	// The bytes the props address, and how many of them there are.
	unsigned char *DataBlob();
	int            BlobBytes() const;

	virtual int RequiredEdictIndex( void ) { return 0; }   // the world always needs to be in slot 0

	virtual void Spawn( void );
	virtual void Precache( void );
	virtual bool KeyValue( const char *szKeyName, const char *szValue );
	virtual void DecalTrace( trace_t *pTrace, char const *decalName );
	virtual void VPhysicsCollision( int index, ss::gamevcollisionevent_t *pEvent ) {}
	virtual void VPhysicsFriction( IPhysicsObject *pObject, float energy, int surfaceProps, int surfacePropsHit ) {}

	// The ten, printed. Called from Spawn -- once the map's keys have landed --
	// and from cs_sv_world. See SourceSharpWorld_Report for why this ships.
	void  ReportNetState();

	// The chapter title is not held here (plan_server_native_v2 T4d): the key
	// is ServerGameDll.WorldKeyValue's, which keeps it for ChapterTitle.cs.


	//=========================================================================
	// IHandleEntity -- the two methods every hierarchy answers, and the whole
	// reason a trace can hand this object back.
	//=========================================================================
	// THE STORE, AND THE ONE LINE THAT IS NOT A STORE. Stock's
	// CBaseEntity::SetRefEHandle (baseentity.cpp:7925) mirrors the handle's
	// serial into edict()->m_NetworkSerialNumber; this class stopped deriving
	// from CBaseEntity and the mirror went with it, so the world's edict has
	// carried serial ZERO ever since -- and the engine publishes that zero to
	// every client as the world's serial number.
	//
	// WHAT THAT COSTS is not a networking curiosity, it is the local player's
	// prediction. A client handle of (index 0, serial 0) packs to m_Index == 0,
	// and CBaseEntityList::LookupEntity (entitylist_base.h:138) opens with
	//
	//     if ( handle.m_Index == INVALID_EHANDLE ) return NULL;
	//
	// where INVALID_EHANDLE is the ENUMERATOR from basehandle.h:25 -- value
	// ZERO, not INVALID_EHANDLE_INDEX. Stock never trips it because a serial of
	// 0 for entity 0 does not happen there: CBaseEntityList seeds every slot's
	// serial randomly and CBaseEntity mirrors whatever it drew. With the mirror
	// gone the client's world sat on exactly the one handle the engine's own
	// lookup refuses, so NO CLIENT COULD RESOLVE A HANDLE TO THE WORLD. The
	// local player's ground entity was therefore NULL on every replayed
	// command while FL_ONGROUND said otherwise, and the movement loop reads the
	// entity, not the bit (gamemovement.cpp:2475) -- so the client took AirMove
	// where the server took WalkMove and refused every jump the server made.
	// Measured: server hGround(idx=0 ser=29254) resolves=1, client
	// hGround(idx=0 ser=0) resolves=0, velocity.z stuck at -4.5 forever.
	//
	// The mirror is the same one masked store the parallel hierarchy already
	// makes for the same reason -- standalone_netserial.h has the rule and
	// CStandaloneEntity::SetRefEHandle the other caller.
	virtual void SetRefEHandle( const CBaseHandle &handle ) OVERRIDE
	{
		COMPILE_TIME_ASSERT( STANDALONE_NETSERIAL_BITS == NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS );

		m_RefEHandle = handle;

		StandaloneNetSerial_Mirror( m_pEdict, (unsigned long)handle.GetSerialNumber() );
	}

	virtual const CBaseHandle &GetRefEHandle() const OVERRIDE { return m_RefEHandle; }

	//=========================================================================
	// IServerUnknown
	//=========================================================================
	// THE TWO POINTERS THE ENGINE ASKS FOR, and they are the whole of what it
	// wanted from the two interfaces this class used to inherit. Members, so
	// their lifetime is this object's and neither can be swapped or outlive it.
	virtual ICollideable *GetCollideable() OVERRIDE { return &m_Collideable; }
	virtual IServerNetworkable *GetNetworkable() OVERRIDE { return &m_Networkable; }

	// THE ANSWER THAT MAKES THIS A THIRD HIERARCHY RATHER THAN A SECOND
	// SPELLING OF THE FIRST. NULL, always: this object is not a CBaseEntity,
	// and saying otherwise is exactly the lie this port has spent its length
	// removing. Everything that used to reach the world through here reaches it
	// through world_subject.h's third arm instead -- built in T2, and answering
	// live since before this class changed.
	virtual IGameEntity *GetBaseEntity() OVERRIDE { return NULL; }

	// handle_cross.h's partition reads this same NULL as "not a standalone",
	// which keeps the world out of that arm too: a third hierarchy is one
	// that answers neither.

	//=========================================================================
	// IServerEntity
	//=========================================================================
	virtual int      GetModelIndex( void ) const OVERRIDE { return m_nModelIndex; }
	virtual string_t GetModelName( void ) const OVERRIDE { return m_iszModelName; }
	virtual void     SetModelIndex( int index ) OVERRIDE { m_nModelIndex = index; }

	//=========================================================================
	// The edict, which is this object's own state. m_Networkable::GetEdict
	// forwards to it, so the engine and the game read one field.
	//=========================================================================
	edict_t *GetEdict() const { return m_pEdict; }

	//=========================================================================
	// What the shim does to itself, and what is done TO it.
	//=========================================================================

	// Takes edict 0 and joins the entity list. Called once, from the
	// constructor.
	void AttachToEdictZero();

	// Bullet holes and blood on level geometry. This was CBaseEntity
	// ::ImpactTrace, reached by UTIL_DecalTrace resolving the trace to a
	// CBaseEntity; there is no CBaseEntity to resolve to now, so the world
	// answers for itself. It is the BRUSH arm of stock's function and nothing
	// else -- worldspawn has no physics object to push and no gibs to spawn.
	void ImpactTrace( trace_t *pTrace, int iDamageType, const char *pCustomImpactName );

private:
	//-------------------------------------------------------------------------
	// THERE IS NO DECLARE_DATADESC, AND THERE IS NO BEGIN_DATADESC EITHER.
	//
	// The owner's second ruling on this shim: "Remove BEGIN_DATADESC( CWorld )
	// completely. If any of those fields still need parsing, special-case them
	// in the method." So this class declares no data description of its own and
	// inherits CBaseEntity's, which is what still answers every key that was
	// never CWorld's -- targetname, origin, angles and the rest -- through
	// CBaseEntity::KeyValue.
	//
	// WHAT THE BLOCK HELD, MEASURED AT b7a78a3a: three DEFINE_FIELD
	// (m_flWaveHeight, m_WorldMins, m_WorldMaxs) and nine DEFINE_KEYFIELD
	// (chaptertitle, startdark, gametitle, maxoccludeearea, minoccluderarea,
	// maxpropscreenwidth, minpropscreenwidth, detailmaterial, coldworld -- two
	// of them behind an #ifdef _X360 this build has no arm for). The
	// disposition of every one of the twelve is written out in
	// world_adapter.cpp where the block used to be; the short version is that
	// the three DEFINE_FIELDs were save/restore only and this build refuses the
	// save cluster, and the nine keys are parsed in CWorld::KeyValue -- eight of
	// them by handing the raw text to the managed declaration that owns the
	// value, one (chaptertitle) here, and one (gametitle) not at all, with its
	// loss recorded.
	//-------------------------------------------------------------------------

	// Resolves the ten field placements out of C#, once, and again after a
	// managed reload has moved them.
	bool ResolveFields();

	SourceSharpWorldFields_t m_Fields;

	//-------------------------------------------------------------------------
	// THE STATE CBaseEntity USED TO HOLD FOR IT, and it is less than it looks.
	//
	// Eleven members, of which nine never change after the constructor: the
	// world is at the origin, unrotated, solid as the BSP, model index 1, for
	// the whole of every level. What made this look expensive from inside
	// CBaseEntity was never the state -- it was the machinery AROUND the state:
	// a network property, a collision property, a datadesc, think lists, touch
	// links, an ehandle-to-owner, save/restore. worldspawn used none of it.
	//-------------------------------------------------------------------------
	edict_t     *m_pEdict;
	CBaseHandle  m_RefEHandle;

	int          m_nModelIndex;
	string_t     m_iszModelName;

	//-------------------------------------------------------------------------
	// THE TWO INTERFACES THE ENGINE ASKS FOR BY POINTER, HELD RATHER THAN
	// INHERITED -- and every field that only they read went inside them: the
	// PVS info to the networkable, the box, the transform and the solid type to
	// the collideable.
	//
	// MEMBERS, WHICH IS THE WHOLE LIFETIME ARGUMENT. They are constructed with
	// this object and destroyed with it; there is no second owner, no allocation
	// to match, and no window in which one exists and the other does not. Their
	// m_pOuter is set in this class's member-initialiser list, so it is valid
	// before the constructor body -- and therefore before AttachToEdictZero
	// hands &m_Networkable to edict 0. ~CWorld takes that pointer back out
	// before the members go, which is the same order the pointers went in.
	//-------------------------------------------------------------------------
	CWorldCollideable m_Collideable;
	CWorldNetworkable m_Networkable;
};

//-----------------------------------------------------------------------------
// The two members' answers that are ABOUT THE OWNER, defined here because here
// is the first place CWorld is a complete type.
//-----------------------------------------------------------------------------

// THE ANSWER THE ENGINE READS TENS OF THOUSANDS OF TIMES A SESSION. Measured:
// the engine's trace path is `pUnk->GetCollideable()->GetEntityHandle()`, two
// adjacent call sites, and what it does with the result is put it in a trace
// filter and in trace_t::m_pEnt. `this` would be the member's address, and
// every resolver in the game would read an ICollideable as an entity.
inline IHandleEntity *CWorldCollideable::GetEntityHandle()
{
	// THE CONVERSION IS IMPLICIT ON PURPOSE, and tools/cast-audit is the reason
	// it is worth a sentence. CWorld -> IServerEntity -> IServerUnknown ->
	// IHandleEntity is one unambiguous single-inheritance chain, so the compiler
	// performs this itself and adjusts the pointer if it ever needs to. An
	// explicit static_cast would do the same thing today and would DEGRADE TO A
	// reinterpret_cast, silently and with no diagnostic at any -W level, on any
	// day that chain stops holding -- which is the entire bug class cast-audit
	// exists for. Written this way the same day is a compile error instead.
	return m_pOuter;
}

inline int CWorldCollideable::GetCollisionModelIndex()
{
	return m_pOuter->GetModelIndex();
}

inline IHandleEntity *CWorldNetworkable::GetEntityHandle()
{
	// Implicit, for the reason spelled out over the collideable's copy above.
	return m_pOuter;
}

inline ServerClass *CWorldNetworkable::GetServerClass()
{
	return m_pOuter->GetServerClass();
}

inline edict_t *CWorldNetworkable::GetEdict() const
{
	return m_pOuter->GetEdict();
}

// NULL, and it is the fork CGlobalEntityList::Clear() takes for this object.
// See the note on the declaration.
inline IGameEntity *CWorldNetworkable::GetBaseEntity()
{
	return NULL;
}

// See the long note on the declaration. `delete m_pOuter` destroys the object
// this member lives inside, so NOTHING may follow this line.
inline void CWorldNetworkable::Release()
{
	delete m_pOuter;
}


CWorld* GetWorldEntity();


#endif // SS_WORLD_H
