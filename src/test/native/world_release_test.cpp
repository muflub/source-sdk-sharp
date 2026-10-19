//====== SourceSharp — who frees the world, and the arm that answers ========//
//
// THE BUG THIS FILE EXISTS FOR, and it shipped on the integration branch.
//
// `dm_lockdown` paints eighty-one decals during level init. The eighty-second
// -- the FIRST one after a `map` command -- took the process down with SIGSEGV
// inside the engine, two frames below UTIL_TraceLine, called from
// SourceSharp_PaintDecal. No lane caught it for weeks because EVERY live gate
// in this tree loaded exactly one map, and one map load cannot reach it.
//
// The cause was six words in world.h that were true when they were written and
// false in the same commit:
//
//     // NOT `delete this`. The world is destroyed by the entity purge at
//     // level shutdown through the route it always took [...]
//     virtual void Release() OVERRIDE {}
//
// "The route it always took" is CGlobalEntityList::Clear(), and Clear forks on
// the one property ffd3e097 had just taken away from this class:
//
//     if ( !SourceSharp_SDKEntityFromNetworkable< IServerEntity >( ent ) )
//         ent->Release();          <- CWorld arrives HERE from ffd3e097 on
//     else
//         UTIL_Remove( ent );      <- and this is where it used to arrive
//
// UTIL_Remove is the arm that deletes. Release IS the arm on the other side --
// CStandaloneEntity::Release is `{ delete this; }` and stock's own
// CServerNetworkProperty::Release is `delete m_pOuter`. So a no-op Release is
// not a second owner politely declining; it is NO owner at all.
//
// THE CHAIN FROM THERE, and every link was observed in one log:
//
//     Release() {}            ->  ~CWorld never runs
//     ~CWorld never runs      ->  g_WorldEntity is never cleared
//     g_WorldEntity is set    ->  the NEXT map's CWorldFactory::Create sees a
//                                 world already standing and REFUSES to build
//                                 one ("worldspawn: a second one was asked for
//                                 while one exists. REFUSED")
//     no world on map two     ->  edict 0 and entity-list slot 0 still hold the
//                                 PREVIOUS map's world, and the first trace of
//                                 the load faults inside the engine.
//
//=============================================================================//
//
// WHY THIS IS A SOURCE-PROPERTY TEST AND NOT A RUNTIME ONE.
//
// The subject is a destruction ROUTE that runs once per level change, through
// three files and the engine. CWorld cannot be constructed without an engine --
// its constructor calls INDEXENT( 0 ) and hands itself to g_pEntityList -- so a
// gtest that builds one would have to build a stand-in for all of that, and a
// stand-in is exactly what cannot detect this class of bug: a replica whose
// Release deleted would pass every assertion a correct tree passes AND every
// assertion the broken tree passed. This project has the note written down
// ("a stand-in for native drifts silently").
//
// So the facts below are read out of the REAL headers, and each one names a
// thing no compiler can see: which arm of a fork a class falls down, whether a
// virtual's body is empty, and whether a destructor undoes what a constructor
// did. The runtime half of the same guard is the level-reload step now in the
// live gate, which is what actually reproduces the crash.
//
// EACH FACT IS EXTRACTED FROM ITS OWN BODY, not grepped for over the file.
// "world.h contains the text `delete this`" is a check that cannot fail once
// any method in the class deletes anything; what is asserted is the body of
// CWorld::Release specifically. Mutation-proved in world_release_mutate.py.
//
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadFromSrc( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), std::ios::binary );
	EXPECT_TRUE( file.is_open() ) << "could not open " << pszRel
		<< " -- every invariant about it would pass vacuously";
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

// Comments are where this bug LIVED -- the false claim in world.h was a comment
// beside the defect -- so every assertion below runs on code with the prose
// taken out. Otherwise "the destructor clears g_WorldEntity" would be satisfied
// by a comment saying that it does.
std::string StripComments( const std::string &in )
{
	std::string out;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' )
				i++;
			if ( i < in.size() )
				out += '\n';
			continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '*' )
		{
			i += 2;
			while ( i + 1 < in.size() && !( in[ i ] == '*' && in[ i + 1 ] == '/' ) )
				i++;
			i++;
			continue;
		}
		out += in[ i ];
	}
	return out;
}

// The braced body that follows pszAfter, brace-matched. Empty string if the
// needle is not there at all, which every caller asserts against separately --
// a body that cannot be found must not read as a body that says nothing.
std::string BodyAfter( const std::string &code, const char *pszAfter )
{
	const size_t at = code.find( pszAfter );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = code.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	int depth = 0;
	for ( size_t i = open; i < code.size(); i++ )
	{
		if ( code[ i ] == '{' )
			depth++;
		else if ( code[ i ] == '}' )
		{
			depth--;
			if ( depth == 0 )
				return code.substr( open, i - open + 1 );
		}
	}
	return std::string();
}

int CountAll( const std::string &haystack, const char *pszNeedle )
{
	const std::string needle( pszNeedle );
	int n = 0;
	for ( size_t at = haystack.find( needle ); at != std::string::npos;
	      at = haystack.find( needle, at + 1 ) )
	{
		n++;
	}
	return n;
}

bool Has( const std::string &haystack, const char *pszNeedle )
{
	return haystack.find( pszNeedle ) != std::string::npos;
}

const char *kWorldHdr    = "sourcesharp/game/server/ss_world.h";
const char *kWorldImpl   = "sourcesharp/game/server/world_adapter.cpp";
const char *kStandalone  = "sourcesharp/game/server/shell_standalone.h";

} // namespace

//-----------------------------------------------------------------------------
// WHICH ARM THE WORLD IS ON. This is the property ffd3e097 changed, and it is
// the reason a no-op Release stopped being harmless on that exact commit.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheWorldIsOnTheNonSdkArm )
{
	const std::string hdr = StripComments( ReadFromSrc( kWorldHdr ) );

	EXPECT_TRUE( Has( hdr, "class CWorld : public IServerEntity" ) )
		<< "CWorld's bases changed. Whether it is freed by Release() or by "
		   "UTIL_Remove follows directly from them.";

	EXPECT_FALSE( Has( hdr, "class CWorld : public CBaseEntity" ) )
		<< "CWorld derives CBaseEntity again -- then it is on the UTIL_Remove arm "
		   "and this file's premise needs re-deriving, not merely re-running.";

	// THE OBJECT Clear() ACTUALLY HOLDS IS THE MEMBER, and this is the fact the
	// interface split moved rather than removed. Clear() reaches an entity as
	// GetServerNetworkable( handle ), which is pUnk->GetNetworkable()
	// (entitylist.h:265) -- and that is &m_Networkable now, not the CWorld. So
	// the arm is decided by CWorldNetworkable::GetBaseEntity, not by CWorld's.
	// THE SPELLING MOVED AND THE FACT DID NOT. Lane `the-close` retired the
	// alias (0bc727ed9): `CBaseEntity` was `IGameEntity` -- the alias header
	// said so with `using CBaseEntity = IGameEntity;` -- so this is a RENAME of
	// the return type in code, not a change of what the function answers. Every
	// object's symbol set is identical across it. The needles below therefore
	// read IGameEntity; the assertion is the same assertion.
	const std::string netBase = BodyAfter( hdr, "IGameEntity *CWorldNetworkable::GetBaseEntity()" );
	ASSERT_FALSE( netBase.empty() )
		<< "CWorldNetworkable::GetBaseEntity has no definition in " << kWorldHdr;
	EXPECT_TRUE( Has( netBase, "return NULL" ) )
		<< "GetBaseEntity is what SourceSharp_SDKEntityFromNetworkable asks of "
		   "whatever Clear() is holding. A non-NULL answer here moves the world "
		   "to the UTIL_Remove arm, which reaches MarkForDeletion through a "
		   "CServerNetworkProperty this object does not have. Body was: "
		<< netBase;

	// And CWorld's own, which is IServerUnknown's and is what every resolver in
	// handle_cross.h asks. Both must be NULL; asserting only one would leave the
	// world half outside the SDK.
	EXPECT_TRUE( Has( hdr, "virtual IGameEntity *GetBaseEntity() OVERRIDE { return NULL; }" ) )
		<< "CWorld::GetBaseEntity no longer answers NULL. That is the arm "
		   "handle_cross.h's resolvers read, and a non-NULL answer there is a "
		   "cast to a vtable that is not present.";

	// AND THE OLD SPELLING IS GONE, which is what stops the two needles above
	// from being satisfied by a header that carries BOTH. The alias is retired:
	// a `CBaseEntity *` here would not compile, so a hit means this header has
	// been reverted rather than merely re-typed.
	EXPECT_EQ( 0, CountAll( hdr, "CBaseEntity *GetBaseEntity()" ) )
		<< "world.h declares GetBaseEntity returning CBaseEntity again. That "
		   "name does not exist in this build; public/ss_cbaseentity_alias.h is "
		   "deleted.";

	// THE THIRD-HIERARCHY HALF IS THE SAME NULL since plan_server_native_v2
	// T1.3. handle_cross.h's partition reads GetBaseEntity() now -- non-NULL
	// means "a standalone" -- so CWorld's NULL above keeps the world out of
	// that arm as well as out of the SDK's. The patch's second accessor, which
	// the world used to answer NULL to separately, must not come back: a
	// second accessor is a second answer that can disagree.
	EXPECT_EQ( 0, CountAll( hdr, "GetGameEntity" ) )
		<< "the world declares GetGameEntity() again; the partition asks "
		   "GetBaseEntity(), and two accessors can disagree.";
}

//-----------------------------------------------------------------------------
// THE SPLIT ITSELF, and every fact here is about a LIFETIME rather than about a
// shape. CWorld stopped inheriting IServerNetworkable and ICollideable and
// holds one of each instead -- so the object the engine's edict points at, and
// the object the entity purge frees, are now members of the world rather than
// the world. Three ways that goes wrong, each pinned below:
//
//   * a member allocated rather than embedded -- then it has a second owner and
//     a lifetime that can differ from the world's;
//   * a member that answers ITSELF where it is asked who owns it -- then
//     trace_t::m_pEnt holds an interior pointer and every resolver in the game
//     reads an ICollideable as an entity;
//   * a Release() that deletes the MEMBER instead of the world -- a delete of
//     an interior pointer, and the world leaks exactly as ffd3e097 leaked it.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheEngineInterfacesAreMembersOfTheWorld )
{
	const std::string hdr = StripComments( ReadFromSrc( kWorldHdr ) );

	EXPECT_TRUE( Has( hdr, "CWorldCollideable m_Collideable;" ) )
		<< "the world's ICollideable is not a member. Anything else -- a "
		   "pointer, an allocation, a singleton -- gives it a lifetime that is "
		   "not the world's, and edict 0 holds it past the level change.";
	EXPECT_TRUE( Has( hdr, "CWorldNetworkable m_Networkable;" ) )
		<< "the world's IServerNetworkable is not a member. It is what "
		   "edict_t::m_pNetworkable points at, read unguarded by the engine.";

	// AND THE ENGINE IS STILL HANDED THEM. Without these two, the assertions
	// above would be satisfied by a world that carried both members and gave
	// the engine something else -- or nothing.
	EXPECT_TRUE( Has( hdr, "virtual ICollideable *GetCollideable() OVERRIDE { return &m_Collideable; }" ) )
		<< "IServerUnknown::GetCollideable no longer hands back the member. "
		   "edict_t::GetCollideable() is GetIServerEntity()->GetCollideable(), "
		   "so whatever this returns is what the engine's trace path calls "
		   "GetEntityHandle() on.";
	EXPECT_TRUE( Has( hdr, "virtual IServerNetworkable *GetNetworkable() OVERRIDE { return &m_Networkable; }" ) )
		<< "IServerUnknown::GetNetworkable no longer hands back the member. "
		   "This is the call CGlobalEntityList::Clear() makes before it picks an "
		   "arm, so what it returns decides who frees the world.";

	// NOT ALLOCATED. A `new` for either of these is the second owner this whole
	// file exists to refuse.
	const std::string impl = StripComments( ReadFromSrc( kWorldImpl ) );
	EXPECT_FALSE( Has( impl, "new CWorldCollideable" ) )
		<< "the collideable is allocated somewhere. It is a subobject; "
		   "allocating one gives it a life the world does not control.";
	EXPECT_FALSE( Has( impl, "new CWorldNetworkable" ) )
		<< "the networkable is allocated somewhere. It is a subobject; an "
		   "allocated one has a lifetime edict 0 does not follow.";

	// `delete m_Collideable` IS NOT ASSERTED AGAINST, and the omission is
	// deliberate rather than an oversight: neither member is a pointer, so the
	// compiler refuses a delete of one outright. An assertion the compiler
	// already enforces is a check that cannot fail, and this tree has 48 of
	// those on record. What makes the delete impossible is the two EXPECT_TRUEs
	// above -- that each one is declared as a member -- and those are mutated.
}

TEST( WorldRelease, TheMembersKnowTheirOwnerBeforeTheConstructorBodyRuns )
{
	const std::string impl = StripComments( ReadFromSrc( kWorldImpl ) );

	// THE MEMBER-INITIALISER LIST, NOT AN Attach() CALL IN THE BODY. The body
	// hands &m_Networkable to edict 0 (AttachToEdictZero), and the engine may
	// read that pointer from the next tick on -- so a member whose owner is set
	// later in the same body has a window in which it is published and null
	// inside. The initialiser list has no such window.
	EXPECT_TRUE( Has( impl, ": m_Collideable( this )" ) )
		<< "the collideable's owner is no longer set in CWorld's initialiser "
		   "list. Anywhere later is after the members are already reachable.";
	EXPECT_TRUE( Has( impl, ", m_Networkable( this )" ) )
		<< "the networkable's owner is no longer set in CWorld's initialiser "
		   "list.";
}

TEST( WorldRelease, TheMembersAnswerTheOwnerAndNotThemselves )
{
	const std::string hdr = StripComments( ReadFromSrc( kWorldHdr ) );

	// MEASURED, not assumed: the engine's trace path is
	// pUnk->GetCollideable()->GetEntityHandle() -- two adjacent call sites in
	// engine.so, entered 29,614 and 9,926 times in one session -- and what it
	// does with the answer is put it in a trace filter and in trace_t::m_pEnt.
	// `return this` here would be the member's address.
	const std::string cHandle = BodyAfter( hdr, "IHandleEntity *CWorldCollideable::GetEntityHandle()" );
	ASSERT_FALSE( cHandle.empty() )
		<< "CWorldCollideable::GetEntityHandle has no body in " << kWorldHdr;
	EXPECT_TRUE( Has( cHandle, "m_pOuter" ) )
		<< "the world's collideable answers something other than its owner when "
		   "the engine asks who it belongs to. trace_t::m_pEnt would then hold "
		   "an interior pointer and SourceSharp_IsTheWorld would never match. "
		   "Body was: " << cHandle;
	EXPECT_FALSE( Has( cHandle, "return this" ) )
		<< "the world's collideable hands ITSELF back as the entity handle.";

	const std::string nHandle = BodyAfter( hdr, "IHandleEntity *CWorldNetworkable::GetEntityHandle()" );
	ASSERT_FALSE( nHandle.empty() )
		<< "CWorldNetworkable::GetEntityHandle has no body in " << kWorldHdr;
	EXPECT_TRUE( Has( nHandle, "m_pOuter" ) )
		<< "the world's networkable answers something other than its owner. "
		   "SourceSharp_SDKEntityFromNetworkable asks exactly this before it "
		   "decides which side of the partition the entity is on. Body was: "
		<< nHandle;
	EXPECT_FALSE( Has( nHandle, "return this" ) )
		<< "the world's networkable hands ITSELF back as the entity handle.";
}

TEST( WorldRelease, EdictZeroHoldsTheMemberAndTheDestructorTakesItBack )
{
	const std::string impl = StripComments( ReadFromSrc( kWorldImpl ) );

	const std::string attach = BodyAfter( impl, "void CWorld::AttachToEdictZero()" );
	ASSERT_FALSE( attach.empty() ) << "CWorld::AttachToEdictZero has no body";
	EXPECT_TRUE( Has( attach, "m_pEdict->m_pNetworkable = &m_Networkable" ) )
		<< "edict 0's second pointer is not the world's networkable member. "
		   "CBaseEdict::AreaNum and GetClassName read that field with no null "
		   "check, so a stale or wrong pointer is a jump into the engine.";

	// And the same pointer taken back out, because the edict outlives this
	// object and the member goes with it.
	const std::string dtor = BodyAfter( impl, "CWorld::~CWorld" );
	ASSERT_FALSE( dtor.empty() ) << "~CWorld has no body";
	EXPECT_TRUE( Has( dtor, "m_pEdict->m_pNetworkable = NULL" ) )
		<< "~CWorld leaves edict 0 pointing at a member of a freed object.";
}

TEST( WorldRelease, TheFactoryHandsBackTheMember )
{
	const std::string impl = StripComments( ReadFromSrc( kWorldImpl ) );
	const std::string body = BodyAfter( impl, "class CWorldFactory" );

	ASSERT_FALSE( body.empty() ) << "CWorldFactory has no body in " << kWorldImpl;
	EXPECT_TRUE( Has( body, "return pWorld->GetNetworkable();" ) )
		<< "CWorldFactory::Create no longer returns the world's networkable. "
		   "Its return type is IServerNetworkable * and CWorld is not one any "
		   "more, so whatever it does return is either a compile error or the "
		   "wrong object for Destroy() to Release().";
}

//-----------------------------------------------------------------------------
// THE DEFECT ITSELF. `virtual void Release() OVERRIDE {}` is what shipped.
//-----------------------------------------------------------------------------
TEST( WorldRelease, CWorldReleaseDestroysTheObject )
{
	const std::string hdr = StripComments( ReadFromSrc( kWorldHdr ) );

	// THE ANCHOR IS THE DEFINITION'S QUALIFIED SIGNATURE, and it has to be.
	// "void Release()" matched the in-class DECLARATION once Release moved to
	// CWorldNetworkable and was declared there without a body -- BodyAfter then
	// returned the NEXT function's body and the assertion was asked about
	// AreaNum. That is this project's "a mutation that stops reaching its
	// subject", found by running the test rather than by reading it.
	const std::string body = BodyAfter( hdr, "void CWorldNetworkable::Release()" );

	ASSERT_FALSE( body.empty() )
		<< "CWorldNetworkable::Release has no definition in " << kWorldHdr;

	EXPECT_TRUE( Has( body, "delete m_pOuter" ) )
		<< "the world's Release does not destroy the world. Release() is the "
		   "ONLY route CGlobalEntityList::Clear takes for an entity outside the "
		   "SDK, so an empty body leaks the world past the level change -- "
		   "g_WorldEntity stays set, the next map's worldspawn is REFUSED, and "
		   "the first trace of that load faults inside the engine. Body was: "
		<< body;

	// `delete this` WOULD BE A DIFFERENT BUG WEARING THE SAME CLOTHES. This
	// object is a member of the thing being destroyed, so deleting it directly
	// is a delete of an interior pointer -- and it would leave the CWorld
	// itself, and therefore edict 0 and entity-list slot 0, exactly as unfreed
	// as the empty body did.
	EXPECT_FALSE( Has( body, "delete this" ) )
		<< "Release deletes the MEMBER rather than its owner. The member is a "
		   "subobject of CWorld; the world would still leak, and the delete "
		   "would be of a pointer no allocator ever returned.";

	// AND NOTHING ELSE CLAIMS TO OWN THE WORLD. Two Releases that both delete
	// is a double free at the level change.
	EXPECT_FALSE( Has( hdr, "virtual void Release() OVERRIDE { delete this; }" ) )
		<< "something in world.h is a second owner of the world.";
}

//-----------------------------------------------------------------------------
// THE SIBLING ON THE SAME ARM, and it is the reference implementation rather
// than a bonus assertion: CStandaloneEntity is the class whose behaviour made
// the Release arm correct for everything EXCEPT the world.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheStandaloneHierarchyReleasesTheSameWay )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kStandalone ) ), "void Release()" );

	ASSERT_FALSE( body.empty() ) << "CStandaloneEntity::Release has no body in " << kStandalone;
	EXPECT_TRUE( Has( body, "delete this" ) )
		<< "the other class on Clear()'s non-SDK arm stopped destroying either";
}

//-----------------------------------------------------------------------------
// WHAT THE DESTRUCTOR HAS TO UNDO. Until ffd3e097 every line of this was
// ~CBaseEntity's and ~CServerNetworkProperty's; un-deriving took the inherited
// teardown away and nothing replaced it, so a deleting Release() alone would
// have freed the object and left its registrations pointing at the hole.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheDestructorClearsTheSingleton )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kWorldImpl ) ), "CWorld::~CWorld" );

	ASSERT_FALSE( body.empty() ) << "~CWorld has no body in " << kWorldImpl;
	EXPECT_TRUE( Has( body, "g_WorldEntity = NULL" ) )
		<< "~CWorld leaves the singleton set. CWorldFactory::Create refuses to "
		   "build a world while one stands, so the next map would get none.";
}

//-----------------------------------------------------------------------------
// THE WORLD IS NOT IN THE ENTITY LIST ANY MORE, so what the destructor has to
// undo is its own handle and nothing else -- plan_entity.md §3.3, lane
// entlist-server.
//
// This fact used to read the other way round: AttachToEdictZero registered the
// world in the entity list at slot 0, so the destructor had to take it out or
// the slot outlived the object. §3.3 moved the registration -- the world issues
// its own handle and SourceSharp_WorldFromHandle is what resolves it -- so
// there is no entry to free, and terminating the handle is the whole of the
// removal. Every handle to the dead world stops matching on that instruction,
// which is what the list's serial bump bought.
//
// BOTH DIRECTIONS ARE ASSERTED. Terminating without also having stopped
// registering would leave a live list entry behind; still registering without
// terminating is the original bug. So the fact says the call is gone AND the
// Term is there, and the ledger in entity_list_sweep_test.cpp counts the file.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheDestructorTerminatesTheHandleAndHasNoListToLeave )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kWorldImpl ) ), "CWorld::~CWorld" );

	ASSERT_FALSE( body.empty() ) << "~CWorld has no body in " << kWorldImpl;

	EXPECT_TRUE( Has( body, "m_RefEHandle.Term()" ) )
		<< "the world's handle outlives the object. Nothing else invalidates it: "
		   "SourceSharp_WorldFromHandle answers by comparing against this member, "
		   "so a handle left standing resolves a freed world.";

	EXPECT_FALSE( Has( body, "RemoveEntity" ) )
		<< "~CWorld removes itself from the entity list again. It is not in it -- "
		   "AttachToEdictZero issues the world's own handle now -- so this call "
		   "would free somebody else's slot 0, or none.";
}

//-----------------------------------------------------------------------------
// AND WHO CALLS Release() NOW THAT THE PURGE CANNOT REACH THE WORLD.
//
// This is the fact that stands where TheEntityPurgeDestroysNonSdkEntitiesThrough
// Release stood for the world specifically. Clear()'s walk enumerated the list
// and the world was in it; the world left the list with §3.3, so the walk cannot
// name it and the release moved to the step that runs the walk -- ahead of it,
// which is the position the world had at the head of the active list.
//
// WITHOUT THIS CALL THE WHOLE CHAIN AT THE TOP OF THIS FILE COMES BACK, exactly
// as it did when Release() was `{}`: ~CWorld never runs, g_WorldEntity is never
// cleared, the next map's factory refuses to build a world, and the first trace
// of the load faults inside the engine. The runtime half is the live gate's
// level-reload step; this is the half a compiler cannot see.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheLevelClearStepReleasesTheWorldBeforeItClearsTheList )
{
	// THE STEP IS A VTABLE METHOD NOW. plan_shim.md \u00a75 3a (lane
	// 3b-enginebind) folded shell_levelshim.cpp into shell_levelsteps.cpp as
	// shell_level.cpp, so the step's body lives in the method that used to
	// forward to it. What this fact reads is unchanged.
	const std::string body = BodyAfter(
		StripComments( ReadFromSrc( "sourcesharp/game/server/shell_level.cpp" ) ),
		"virtual void ClearEntityList() OVERRIDE" );

	ASSERT_FALSE( body.empty() )
		<< "IShimLevel::ClearEntityList has no body";

	// READ THE ASSERTIONS, NOT THE NAME. There is no "before it clears the
	// list" left: the step does not clear a list any more.
	//
	// AND THE ORDERING THIS FACT ASSERTED WAS NEVER A REAL ORDERING. The
	// `gEntList.Clear();` it ordered the release against sat inside
	// `#ifndef SS_CBASEENTITY_ALIAS` (shell_level.cpp, deleted at
	// f288a5e11). game/server/server_csharp.vpc:69 defines that macro, so the
	// armed build -- the only build there is -- compiled the release and not
	// the purge. `nRelease < nClear` was a live line ordered against a dead
	// one, and it could not fail for any reason worth knowing about. This is
	// the same defect finding 3 of .claude/entity-port/status/residue-tests.md
	// filed against materialproxy_subject_test.cpp, in a file nobody flagged.
	//
	// THE SUCCESSOR PROPERTY IS STRONGER THAN THE ORDERING WAS. The release is
	// not merely FIRST, it is the WHOLE STEP -- and because nothing else in the
	// armed build can reach the world (§3.3 moved it onto its own handle, out
	// of the list any walk enumerates), "the whole step" is also "the only
	// thing that frees the world at level shutdown". That is the chain at the
	// top of this file, asserted at the one line that closes it.
	const size_t nRelease = body.find( "GetNetworkable()->Release()" );

	EXPECT_NE( std::string::npos, nRelease )
		<< "nothing releases the world at level shutdown. The world is no "
		   "longer in any list a walk enumerates, so if this step does not "
		   "release it, NOTHING does -- see the chain at the top of this file. "
		   "Body was: " << body;

	// EXACTLY ONCE. Two releases is a double delete of the CWorld, and the
	// second one runs after `delete m_pOuter` has already destroyed the object
	// the member lives inside.
	EXPECT_EQ( 1, CountAll( body, "Release()" ) )
		<< "the step releases more than once, or not at all. Body was: " << body;

	EXPECT_EQ( std::string::npos, body.find( "Clear()" ) )
		<< "the entity purge is back in the step. It was inside "
		   "`#ifndef SS_CBASEENTITY_ALIAS` and was never compiled -- so check "
		   "which it is: a restored dead arm, or a purge that now really runs. "
		   "If it really runs it must run AFTER the release, because the world "
		   "was the head of the active list and is freed by the line above.";

	// UNCONDITIONAL, which is the property the deleted guard makes assertable
	// for the first time. A release the armed build skips is exactly the `{}`
	// Release this whole file was written about: ~CWorld never runs,
	// g_WorldEntity is never cleared, and the SECOND `map` command faults
	// inside the engine on its first trace.
	EXPECT_EQ( std::string::npos, body.find( "#if" ) )
		<< "the step has conditional compilation in it again. Whatever it "
		   "guards, this fact stops being a statement about the build and "
		   "becomes a statement about one arm. Body was: " << body;

	// AND THE GUARD IS ON THE WORLD BEING THERE, not on a build flag: a level
	// shutdown before a world was ever built must not dereference NULL.
	EXPECT_NE( std::string::npos, body.find( "if ( CWorld *pWorld = GetWorldEntity() )" ) )
		<< "the step no longer tests that a world exists before releasing it. "
		   "Body was: " << body;

	// THROUGH THE MEMBER, which is the object the purge held (world.h) and the
	// one whose Release deletes the outer. Releasing the CWorld directly would
	// be a second spelling of one destruction, and two spellings is how the
	// first one became a no-op without anything noticing.
	EXPECT_EQ( std::string::npos, body.find( "pWorld->Release()" ) )
		<< "the step releases the CWorld rather than its networkable member.";
}

TEST( WorldRelease, TheDestructorDetachesEdictZeroWithoutReturningIt )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kWorldImpl ) ), "CWorld::~CWorld" );

	ASSERT_FALSE( body.empty() ) << "~CWorld has no body in " << kWorldImpl;

	EXPECT_TRUE( Has( body, "m_pEdict->SetEdict( NULL, false )" ) )
		<< "edict 0 keeps a pointer to this object past its life; "
		   "FreeContainingEntity dereferences whatever it still holds.";
	EXPECT_TRUE( Has( body, "m_pEdict->m_pNetworkable = NULL" ) )
		<< "the edict's networkable pointer is the one the engine uses the moment "
		   "it tries to send anything, and with no null check.";

	// The one place this deliberately parts from CServerNetworkProperty::
	// DetachEdict, which calls RemoveEdict unconditionally. AttachToEdictZero
	// takes edict 0 with INDEXENT( 0 ), not engine->CreateEdict, so nothing here
	// allocated it -- returning it to the free list would let the next
	// CreateEdict hand the world's index to an ordinary entity.
	EXPECT_FALSE( Has( body, "RemoveEdict" ) )
		<< "~CWorld returns edict 0 to the engine's free list. It never allocated "
		   "it: AttachToEdictZero takes it with INDEXENT( 0 ).";
}

TEST( WorldRelease, TheDestructorFreesTheClusterList )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kWorldImpl ) ), "CWorld::~CWorld" );

	ASSERT_FALSE( body.empty() ) << "~CWorld has no body in " << kWorldImpl;
	EXPECT_TRUE( Has( body, "CleanUpEntityClusterList" ) )
		<< "~CServerNetworkProperty makes exactly this call for every networked "
		   "entity, and the world is FL_EDICT_ALWAYS, so it has clusters.";
}

//-----------------------------------------------------------------------------
// THE MECHANISM THAT TURNED A LEAK INTO A CRASH, pinned so that the refusal
// stays coupled to the singleton it reads. It is a correct refusal -- a second
// world really would take edict 0 from the first -- and it is only harmful when
// the first world is a ghost.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheFactoryRefusesASecondWorldWhileOneStands )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kWorldImpl ) ), "class CWorldFactory" );

	ASSERT_FALSE( body.empty() ) << "CWorldFactory has no body in " << kWorldImpl;
	EXPECT_TRUE( Has( body, "GetWorldEntity() != NULL" ) )
		<< "the factory no longer refuses a second world. That refusal is what "
		   "made an unfreed world visible as a map with no worldspawn instead of "
		   "as two of them.";
}

//=============================================================================//
//
// THE CLIENT'S HALF — added by step 5e, when C_World came off C_BaseEntity.
//
// THE TWO SIDES ARE DELIBERATELY OPPOSITE HERE and that is the point of putting
// them in one file. On the server, Release() must DELETE: it is the only arm
// CGlobalEntityList::Clear takes for an entity outside the SDK, and the empty
// body that shipped as ffd3e097 leaked the world into the next map and killed
// the process on dm_lockdown's 82nd decal. On the client, Release() must NOT
// delete: the world object is a PROCESS-lifetime singleton — ClientWorldFactoryInit
// allocates one at DLL init and hands the same one back for every map — and
// stock's C_World::Release was `ActivityList_Free(); Term();` for exactly that
// reason.
//
// So the same bug wears opposite clothes on the two sides, and a reader who
// carried the server's rule across would write the client's crash. What the
// client HAS to give back per map is the BINDING, and the facts below are that
// obligation, split into the four places it is discharged.
//
// A HANDLE AND NOT AN INDEX, which is the recorded lesson this whole section is
// the client's copy of: a token carrying only an index would let the first
// map's object answer for the second map's, and the SERIAL is what prevents it.
// The client cannot choose its own serial — the engine forces one with each
// create call — so `Init` binding at the serial it was handed is the whole of
// the client's part in that.
//
//=============================================================================//

namespace
{
const char *kClientWorldHdr  = "game/client/c_world.h";
const char *kClientWorldImpl = "sourcesharp/game/client/world_adapter_client.cpp";
}

TEST( WorldRelease, TheClientsWorldIsASingletonThatOutlivesEveryMap )
{
	const std::string impl = StripComments( ReadFromSrc( kClientWorldImpl ) );

	const std::string init = BodyAfter( impl, "void ClientWorldFactoryInit()" );
	ASSERT_FALSE( init.empty() )
		<< "ClientWorldFactoryInit has no body in " << kClientWorldImpl;
	EXPECT_TRUE( Has( init, "new C_World" ) )
		<< "the client's world is no longer allocated at DLL init. Every fact "
		   "below is about a singleton that is reused across maps; if the object "
		   "became per-map, the client would need the server's rules and not "
		   "these. Body was: " << init;

	const std::string shutdown = BodyAfter( impl, "void ClientWorldFactoryShutdown()" );
	ASSERT_FALSE( shutdown.empty() )
		<< "ClientWorldFactoryShutdown has no body in " << kClientWorldImpl;
	EXPECT_TRUE( Has( shutdown, "delete g_pClientWorld" ) )
		<< "nothing frees the client's world at DLL shutdown, so the ONE owner "
		   "this arrangement has does not free it either.";
}

//-----------------------------------------------------------------------------
// AND THEREFORE Release() MUST NOT DELETE — the exact inverse of
// CWorldReleaseDestroysTheObject above, on the other side of the wire.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheClientsReleaseDoesNotDestroyTheSingleton )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kClientWorldImpl ) ), "void C_World::Release()" );

	ASSERT_FALSE( body.empty() )
		<< "C_World::Release has no definition in " << kClientWorldImpl;

	EXPECT_FALSE( Has( body, "delete" ) )
		<< "the client's Release destroys the world object. It is a "
		   "process-lifetime singleton that ClientWorldFactoryShutdown owns and "
		   "that the factory hands back for the NEXT map -- so this is a "
		   "use-after-free on the second level load, and a double free at DLL "
		   "shutdown. The server's Release must delete; this one must not. "
		   "Body was: " << body;
}

//-----------------------------------------------------------------------------
// WHAT IT MUST GIVE BACK INSTEAD. The object survives the level change; the
// handle must not.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheClientsReleaseGivesTheLevelsBindingBack )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kClientWorldImpl ) ), "void C_World::Release()" );

	ASSERT_FALSE( body.empty() )
		<< "C_World::Release has no definition in " << kClientWorldImpl;

	EXPECT_TRUE( Has( body, "SourceSharp_LegacyListUnbind" ) )
		<< "the client's world stays in the entity list past the level change. "
		   "Entry 0 would then hold the previous map's object at the previous "
		   "map's serial while the new map's create tries to bind the same "
		   "entry. Body was: " << body;

	EXPECT_TRUE( Has( body, "m_RefEHandle.Term()" ) )
		<< "the world keeps the previous level's handle. IsBoundToTheLevel() "
		   "would answer true at the main menu, and every trace's world arm "
		   "reads it.";
}

//-----------------------------------------------------------------------------
// AND THE REFUSAL THAT MAKES A MISSED Release VISIBLE, which is the client's
// twin of CWorldFactory's `GetWorldEntity() != NULL`.
//
// It is a correct refusal for the same reason the server's is: two live
// bindings for one object is not a state anything can recover from. Its VALUE
// is that a leak announces itself as a map with no world rather than as a map
// whose world silently answers the previous level's questions.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheClientsInitRefusesASecondBindingWhileOneStands )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kClientWorldImpl ) ),
		           "bool C_World::Init( int entnum, int iSerialNum )" );

	ASSERT_FALSE( body.empty() )
		<< "C_World::Init has no definition in " << kClientWorldImpl;

	EXPECT_TRUE( Has( body, "m_RefEHandle.IsValid()" ) )
		<< "Init no longer checks whether the world is already bound, so a "
		   "missed Release becomes two rows in the entity list for one object -- "
		   "the second of which nothing will ever remove. Body was: " << body;

	EXPECT_TRUE( Has( body, "return false" ) )
		<< "Init has a check but no refusal. A warning without a refusal is the "
		   "double bind with a line in the log.";
}

//-----------------------------------------------------------------------------
// THE SERIAL, WHICH IS THE PART A RELOAD BUG TURNS ON.
//
// CUtlLinkedList hands entries 0 and 1 back on every map reload -- measured, on
// this tree -- so an index alone identifies the previous map's object as
// exactly as it identifies this one's. The engine forces the serial with the
// create call and Init has to bind at it rather than at anything of its own.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheClientsInitBindsAtTheSerialTheEngineForced )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kClientWorldImpl ) ),
		           "bool C_World::Init( int entnum, int iSerialNum )" );

	ASSERT_FALSE( body.empty() )
		<< "C_World::Init has no definition in " << kClientWorldImpl;

	EXPECT_TRUE( Has( body, "SourceSharp_LegacyListBind( this, entnum, iSerialNum )" ) )
		<< "the world is not bound at the entry and serial the engine handed the "
		   "create call. A binding that chose its own serial would resolve on "
		   "this side and not on the server's, and an index without a serial "
		   "would let the first map's world answer the second map's lookups. "
		   "Body was: " << body;
}

//-----------------------------------------------------------------------------
// THE ONE PATH Release DOES NOT COVER: a DLL shutdown with a level still
// loaded. Nothing calls Release there, and the singleton is deleted out from
// under an entity-list row that still points at it.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheClientsDestructorUnbindsWhatReleaseDidNot )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kClientWorldImpl ) ), "C_World::~C_World" );

	ASSERT_FALSE( body.empty() ) << "~C_World has no body in " << kClientWorldImpl;

	EXPECT_TRUE( Has( body, "SourceSharp_LegacyListUnbind" ) )
		<< "~C_World leaves the entity list holding a pointer to freed memory "
		   "when the DLL shuts down with a level loaded. Body was: " << body;
}

//-----------------------------------------------------------------------------
// AND THE FACTORY PASSES THE REFUSAL ON rather than handing the engine an
// object that refused to bind. A create whose Init failed and that returned the
// world anyway would put an unbound object into the engine's hands and the
// engine would decode into it.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheClientFactoryRefusesWhenInitDoes )
{
	const std::string body =
		BodyAfter( StripComments( ReadFromSrc( kClientWorldImpl ) ),
		           "IClientNetworkable *SourceSharp_ClientWorldFactory" );

	ASSERT_FALSE( body.empty() )
		<< "SourceSharp_ClientWorldFactory has no body in " << kClientWorldImpl;

	EXPECT_TRUE( Has( body, "!g_pClientWorld->Init( entnum, serialNum )" ) )
		<< "the factory does not test what Init answered, so a refused bind is "
		   "handed to the engine as a working world. Body was: " << body;
	EXPECT_TRUE( Has( body, "return NULL" ) )
		<< "the factory has no refusal arm at all.";
}

//-----------------------------------------------------------------------------
// THE CLIENT COLLIDEABLE ANSWERS ITS OWNER, and this is the twin of
// TheMembersAnswerTheOwnerAndNotThemselves above.
//
// It is the ONE method of that member the engine actually calls, and the engine
// then makes a virtual call on the answer: its client-side trace takes the
// answer as an IClientUnknown and calls GetIClientEntity() on it to fill
// trace_t::m_pEnt, and its point-contents query hands the answer back as the
// entity it found.
//
// `return this` would hand back an ICollideable where an IClientUnknown is
// expected -- a second vtable at a different offset, called through as if it
// were the first. That is not a smaller version of the bug handle_cross.h
// prevents; it is the same bug, arriving through a cast the engine makes and
// this side cannot see.
//
// AND m_pOuter HAS TO BE SET IN THE INITIALISER LIST for the same reason the
// server's two are: the collideable is reachable from the moment the world is,
// and a body-assigned owner has a window in which it is published and null
// inside.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheClientCollideableAnswersTheOwnerAndNotItself )
{
	const std::string impl = StripComments( ReadFromSrc( kClientWorldImpl ) );

	const std::string handle =
		BodyAfter( impl, "IHandleEntity *CWorldClientCollideable::GetEntityHandle()" );
	ASSERT_FALSE( handle.empty() )
		<< "CWorldClientCollideable::GetEntityHandle has no body in "
		<< kClientWorldImpl;
	EXPECT_TRUE( Has( handle, "return m_pOuter" ) )
		<< "the client world's collideable answers something other than its "
		   "owner when the engine asks who it belongs to. trace_t::m_pEnt would "
		   "then hold an interior pointer or nothing at all, and "
		   "SourceSharp_IsTheWorld would never match -- so DidHitWorld() would "
		   "be false for every trace on this side. Body was: " << handle;
	EXPECT_FALSE( Has( handle, "return this" ) )
		<< "the client world's collideable hands ITSELF back as the entity "
		   "handle. The engine calls GetIClientEntity() on that answer.";

	// The IClientUnknown face of the same question, which is what
	// the engine's cast assumes and what a debug engine asserts.
	const std::string unknown =
		BodyAfter( impl, "IClientUnknown *CWorldClientCollideable::GetIClientUnknown()" );
	ASSERT_FALSE( unknown.empty() )
		<< "CWorldClientCollideable::GetIClientUnknown has no body in "
		<< kClientWorldImpl;
	EXPECT_TRUE( Has( unknown, "return m_pOuter" ) )
		<< "the client world's collideable does not name its owner as the "
		   "unknown either. Body was: " << unknown;

	EXPECT_TRUE( Has( impl, ", m_Collideable( this )" ) )
		<< "the client collideable's owner is no longer set in C_World's "
		   "initialiser list. Anywhere later is after the member is already "
		   "reachable through GetCollideable().";
}

//-----------------------------------------------------------------------------
// THE WORLD'S OnDataChanged IS DEFERRED AND IT IS DELIVERED, AND THIS IS A
// REGRESSION PIN ON THE WHOLE CHAIN.
//
// THE BUG IT PINS SHIPPED IN THIS LANE AND WAS FOUND LIVE, not by reading.
// OnDataChanged is not the ENGINE's call on this side: a networkable that
// changed during a net update records that it is owed one in PreDataUpdate,
// and the delivery happens from OnRenderStart, after the update is complete.
// C_BaseEntity::PreDataUpdate is the three lines that did the recording, and
// C_World inherited them until it came off that base -- at which point an
// empty PreDataUpdate took the recording away.
//
// THE SYMPTOM WAS NOTHING. No crash, no log, no failing step: the world's ten
// values decoded into the blob exactly as before, and OnDataChanged simply
// never ran -- so engine->SetOcclusionParameters, modelinfo->
// SetLevelScreenFadeRange, modemanager->SwitchMode and the startdark fade all
// silently stopped happening, and every one of those fails as a rendering
// difference nobody attributes. It was caught by the CLIENT's own world report
// line being absent from console.log: 128 of them on a control run of the same
// map on entity-port, 0 on this branch.
//
// REWRITTEN IN PHASE 1c, AND WHY IT IS NOT THE SAME TEST WITH A NEW STRING.
// The old version asserted ONE literal: that PreDataUpdate's body contained
// `AddDataChangeEvent( this, updateType, &m_DataChangeEventRef )`. Phase 1c
// inlined the queue -- it had exactly one producer, this class, so the list
// could never hold more than one element -- and that string is gone while the
// behaviour it stood for is not. Re-pointing the string at the new spelling
// would pin the new spelling and nothing else: a PreDataUpdate that recorded
// the deferral and a client frame that never delivered it would still pass,
// and THAT is the failure mode this test exists for -- the one whose symptom
// is nothing at all.
//
// So the assertions below walk the CHAIN instead, end to end, in the three
// places it can break independently:
//
//   1. PreDataUpdate records the deferral
//   2. the delivery reads it back, clears it, and calls OnDataChanged
//   3. the client frame calls the delivery
//
// Break any one and OnDataChanged stops running, silently, exactly as before.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheClientsWorldRecordsThatItIsOwedADataChangedCall )
{
	const std::string impl = StripComments( ReadFromSrc( kClientWorldImpl ) );

	const std::string body =
		BodyAfter( impl, "void C_World::PreDataUpdate( DataUpdateType_t updateType )" );
	ASSERT_FALSE( body.empty() )
		<< "C_World::PreDataUpdate has no definition in " << kClientWorldImpl
		<< ". An empty or absent one means OnDataChanged never runs.";

	EXPECT_TRUE( Has( body, "m_DataChangeEventRef = (int)updateType" ) )
		<< "PreDataUpdate does not record that the world is owed an "
		   "OnDataChanged, so it never gets one -- and nothing fails, which is "
		   "what makes this worth a test. Body was: " << body;

	// AND OnPreDataChanged RUNS EXACTLY ONCE PER DEFERRAL, which is the arm the
	// queue's `return false` bought: a second PreDataUpdate before the delivery
	// must not re-announce it.
	EXPECT_TRUE( Has( body, "OnPreDataChanged( updateType )" ) )
		<< "PreDataUpdate no longer calls OnPreDataChanged. Body was: " << body;
}

TEST( WorldRelease, TheClientsWorldActuallyDeliversTheDeferredDataChangedCall )
{
	const std::string impl = StripComments( ReadFromSrc( kClientWorldImpl ) );

	const std::string body =
		BodyAfter( impl, "void C_World::DeliverPendingDataChanged()" );
	ASSERT_FALSE( body.empty() )
		<< "C_World::DeliverPendingDataChanged has no definition in "
		<< kClientWorldImpl << ". PreDataUpdate records a deferral that is "
		   "then never delivered, and OnDataChanged never runs.";

	EXPECT_TRUE( Has( body, "OnDataChanged( updateType )" ) )
		<< "the delivery does not call OnDataChanged, which is the only thing "
		   "it exists to do. Body was: " << body;

	// CLEARED BEFORE THE CALL AND NOT AFTER. The queue cleared the slot before
	// delivering (`*pEvent->m_pStoredEvent = -1;` preceded OnDataChanged), so
	// an OnDataChanged that caused a further PreDataUpdate kept its second
	// deferral. Clearing afterwards would throw it away.
	const size_t cleared   = body.find( "m_DataChangeEventRef = -1" );
	const size_t delivered = body.find( "OnDataChanged( updateType )" );
	EXPECT_NE( cleared, std::string::npos )
		<< "the delivery does not clear the deferral, so the world is delivered "
		   "the same OnDataChanged on every frame for the rest of the level. "
		   "Body was: " << body;
	EXPECT_LT( cleared, delivered )
		<< "the deferral is cleared AFTER OnDataChanged rather than before, so a "
		   "deferral raised from inside OnDataChanged is thrown away. Body was: "
		<< body;
}

TEST( WorldRelease, TheClientFrameCallsTheWorldsDeferredDataChangedDelivery )
{
	// THE THIRD LINK, AND THE ONE NOTHING ELSE IN THIS FILE CAN SEE. A world
	// that records a deferral and a delivery that would work are both useless if
	// the client frame never calls it -- and that is precisely the shape of the
	// original bug, whose symptom was nothing at all.
	const std::string dll = StripComments(
		ReadFromSrc( "sourcesharp/game/client/shell_clientdll.cpp" ) );
	ASSERT_FALSE( dll.empty() ) << "could not read shell_clientdll.cpp";

	EXPECT_TRUE( Has( dll, "DeliverPendingDataChanged()" ) )
		<< "nothing in the client DLL delivers the world's deferred "
		   "OnDataChanged. The world decodes correctly and OnDataChanged never "
		   "runs: no crash, no log, and the occlusion parameters, the prop fade "
		   "range, the view-mode reset and the startdark fade all stop.";
}

TEST( WorldRelease, TheClientsWorldDropsItsDeferredDataChangedOnLevelChangeAndDeath )
{
	// GIVEN BACK, twice, for two different reasons. The delivery runs per frame,
	// so a deferral recorded by the last update of the level that is leaving
	// would be delivered into the level that is arriving; and the object is
	// reused across levels, so a stale deferral survives it.
	const std::string impl = StripComments( ReadFromSrc( kClientWorldImpl ) );

	const std::string release = BodyAfter( impl, "void C_World::Release()" );
	ASSERT_FALSE( release.empty() ) << "C_World::Release has no definition";
	EXPECT_TRUE( Has( release, "m_DataChangeEventRef = -1" ) )
		<< "Release leaves a deferred OnDataChanged to be delivered into the "
		   "next level. Body was: " << release;

	const std::string dtor = BodyAfter( impl, "C_World::~C_World" );
	ASSERT_FALSE( dtor.empty() ) << "~C_World has no body";
	EXPECT_TRUE( Has( dtor, "m_DataChangeEventRef = -1" ) )
		<< "~C_World leaves a deferral recorded on an object that is about to be "
		   "freed. Body was: " << dtor;
}

//-----------------------------------------------------------------------------
// AND WHAT THE WORLD SAYS ITS INDEX IS BETWEEN MAPS.
//
// The object outlives every level, so entindex() is asked in a window where
// there is no entity 0 to be. CBaseHandle::GetEntryIndex() on an INVALID handle
// answers NUM_ENT_ENTRIES-1 -- basehandle.h:150, whose own comment calls it a
// bug-submarining hack kept because "a lot of code actually depends on this
// behavior" -- so an unguarded read would have the world claim to be entity
// 2047 at the main menu. -1 is the client's spelling of "not in the list", the
// one C_BaseEntity carries in `index` for a client-only entity and the one
// CGameTrace::GetEntityIndex answers for a trace that hit nothing.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheClientsWorldHasNoIndexWhenItIsBoundToNoLevel )
{
	const std::string hdr = StripComments( ReadFromSrc( kClientWorldHdr ) );

	const std::string body = BodyAfter( hdr, "virtual int          entindex( void ) const OVERRIDE" );
	ASSERT_FALSE( body.empty() )
		<< "C_World::entindex has no body in " << kClientWorldHdr;

	EXPECT_TRUE( Has( body, "m_RefEHandle.IsValid()" ) )
		<< "entindex reads the handle's entry without asking whether the handle "
		   "is valid, so between maps the world says it is entity "
		   "NUM_ENT_ENTRIES-1. Body was: " << body;
	EXPECT_TRUE( Has( body, "-1" ) )
		<< "entindex has a validity test but no -1 arm, so the answer for 'no "
		   "level' is something other than the client's own spelling of it. "
		   "Body was: " << body;
}

//-----------------------------------------------------------------------------
// AND THE WORLD STILL SPAWNS ITSELF — the second thing C_BaseEntity::PreDataUpdate
// did for it, and the second thing that went silent when the base went.
//
// `C_BaseEntity::PreDataUpdate` (c_baseentity.cpp:2042) is the ONLY caller of
// the client world's Spawn() — measured over the compiled client, nothing else
// calls it — and it made the call under stock's own comment: "Need to spawn on
// client before receiving original network data in case it overrides any values
// set up in spawn".
//
// WHAT STOPS IF IT IS NOT CALLED, and none of it fails loudly:
// ActivityList_RegisterSharedActivities and EventList_RegisterSharedEvents, so
// no animation activity or event on this client has a name; W_Precache, so the
// eight g_sModelIndex* sprite indexes every TE_* dispatch names BY INDEX all
// stay 0 — blood, smoke, bubbles, both explosions and the laser;
// CPrecacheRegister::Precache; and the haptics driver's world precache.
//-----------------------------------------------------------------------------
TEST( WorldRelease, TheClientsWorldStillSpawnsItselfOnTheCreate )
{
	const std::string impl = StripComments( ReadFromSrc( kClientWorldImpl ) );

	const std::string body =
		BodyAfter( impl, "void C_World::PreDataUpdate( DataUpdateType_t updateType )" );
	ASSERT_FALSE( body.empty() )
		<< "C_World::PreDataUpdate has no definition in " << kClientWorldImpl;

	EXPECT_TRUE( Has( body, "updateType == DATA_UPDATE_CREATED" ) )
		<< "PreDataUpdate no longer distinguishes the create, so either it "
		   "spawns the world on every update or it never spawns it. Body was: "
		<< body;
	EXPECT_TRUE( Has( body, "Spawn();" ) )
		<< "the client's world is never spawned, so its Precache never runs -- "
		   "no shared activities, no shared events, and every g_sModelIndex* "
		   "sprite index left at 0. Body was: " << body;

	// AND THE PRECACHE IS STILL WHAT SPAWN DOES. A Spawn() that had stopped
	// calling Precache would satisfy the line above and lose everything it is
	// there for.
	const std::string spawn = BodyAfter( impl, "void C_World::Spawn( void )" );
	ASSERT_FALSE( spawn.empty() ) << "C_World::Spawn has no body";
	EXPECT_TRUE( Has( spawn, "Precache();" ) )
		<< "C_World::Spawn no longer precaches. Body was: " << spawn;

	const std::string precache = BodyAfter( impl, "void C_World::Precache( void )" );
	ASSERT_FALSE( precache.empty() ) << "C_World::Precache has no body";
	EXPECT_TRUE( Has( precache, "RegisterSharedActivities();" ) )
		<< "Precache no longer registers the shared activity and event lists.";
	EXPECT_TRUE( Has( precache, "W_Precache();" ) )
		<< "Precache no longer takes the sprite indexes, so every TE_* that "
		   "names one by index draws nothing.";
}
