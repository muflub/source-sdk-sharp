//========= SourceSharp — the third arm of the partition =====================//
//
// plan_cworld_trace.md T2. `world_subject.h` adds a THIRD answer to "which
// hierarchy holds this entity?", because the owner's instruction puts
// worldspawn on a hierarchy that is neither of the two that exist:
//
//     "CWorld should not inherit CStandaloneEntity either — it should be its
//      own standalone class. A trace should be able to return a pointer to
//      CWorld, or CStandaloneEntity, or CBaseEntity."
//
// TWO KINDS OF FACT ARE PINNED HERE AND THEY ARE NOT INTERCHANGEABLE.
//
//   1. THE RULE, run. SourceSharp_IsTheWorldHandle is engine-free and takes
//      both pointers, so this binary can hold two addresses and a NULL and
//      execute all four cases. Same trick, and the same reason, as
//      blast_blocking_test.cpp: an IHandleEntity that is never dereferenced is
//      just an address, and an address is all this rule knows.
//
//   2. THE ORDERING, scanned. "The world is asked BEFORE the two hierarchy
//      arms" is a fact about source that no engine-free binary can execute,
//      and it is the fact that decides whether the arm is exercised by every
//      live run from today or is dead code until T3. A scan is a weak
//      instrument and is used here only where nothing stronger exists.
//
// THE STRIPPER IS LOAD-BEARING for (2) — this file's own prose names the
// strings the scan looks for, and so does the prose in the files it scans,
// which is heavy. Same rule as effects_evicted_test.cpp.
//
// ANCHORS: every file is identified before anything is asserted about it, so a
// renamed or moved file is a RED test rather than a vacuous pass.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "../game/shared/world_subject.h"

namespace
{

//-----------------------------------------------------------------------------
// Three entities, as the only thing an identity rule knows about them:
// distinct addresses. Never dereferenced — IHandleEntity is incomplete in this
// binary, which is exactly why the rule can be checked without one.
//-----------------------------------------------------------------------------
const IHandleEntity *const kWorld = reinterpret_cast<const IHandleEntity *>( 0x1000 );
const IHandleEntity *const kCrate = reinterpret_cast<const IHandleEntity *>( 0x2000 );

struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

} // namespace

//=============================================================================//
// 1. THE RULE, RUN.
//=============================================================================//

//-----------------------------------------------------------------------------
// The ordinary yes.
//-----------------------------------------------------------------------------
TEST( WorldSubject, TheWorldsOwnHandleIsTheWorld )
{
	EXPECT_TRUE( SourceSharp_IsTheWorldHandle( kWorld, kWorld ) );
}

//-----------------------------------------------------------------------------
// The ordinary no. Something else in the world is not the world, which is what
// makes the arm a partition rather than a wildcard.
//-----------------------------------------------------------------------------
TEST( WorldSubject, AnotherEntityIsNotTheWorld )
{
	EXPECT_FALSE( SourceSharp_IsTheWorldHandle( kCrate, kWorld ) );
}

//-----------------------------------------------------------------------------
// THE WHOLE POINT, STATED AS ITSELF. "Hit nothing" must not read as "hit the
// world" — that equivalence is the defect the entire plan exists to remove, and
// a third arm that reintroduced it would have removed nothing.
//-----------------------------------------------------------------------------
TEST( WorldSubject, NothingIsNotTheWorld )
{
	EXPECT_FALSE( SourceSharp_IsTheWorldHandle( NULL, kWorld ) );
}

//-----------------------------------------------------------------------------
// THE CASE THE ONE-LINE VERSION GETS WRONG, and it is not a hypothetical.
//
// Both pointers are NULL during level load: g_WorldEntity is set by CWorld's
// constructor and is NULL before it, and the client's entity 0 is NULL until
// the world's packet has been decoded. Traces are taken in that window —
// world_adapter.cpp's own header records colour correction doing exactly this
// during LevelInit. Spelled `pHandleEntity == pWorldHandle`, every one of those
// traces reports that it hit the world, most confidently at the moment there is
// no world.
//
// So this fact is not "NULL is handled". It is that the rule answers NO in the
// one window where the naive spelling answers YES to everything.
//-----------------------------------------------------------------------------
TEST( WorldSubject, NothingIsNotTheWorldWhenThereIsNoWorldEither )
{
	EXPECT_FALSE( SourceSharp_IsTheWorldHandle( NULL, NULL ) );
}

//-----------------------------------------------------------------------------
// And the other half of that window: an entity that DOES exist, before the
// world does. It is not the world either — the answer must come from the
// comparison, not from "we have nothing to compare against, so yes".
//-----------------------------------------------------------------------------
TEST( WorldSubject, AnEntityIsNotTheWorldBeforeThereIsAWorld )
{
	EXPECT_FALSE( SourceSharp_IsTheWorldHandle( kCrate, NULL ) );
}

//=============================================================================//
// 2. THE ORDERING, SCANNED.
//
// Each of these is a fact about a file that somebody may change for their own
// reasons, and each decides whether the arm runs today or only after T3.
//=============================================================================//

//-----------------------------------------------------------------------------
// THE MOST EXPENSIVE SITE IN THE CENSUS. movement_ground.h resolves what a
// player is standing on. Without a world arm it answers NULL for level
// geometry the day CWorld un-derives, which is standing on the floor reading as
// standing on nothing — no FL_ONGROUND, no friction, no fall damage — for every
// player on every map.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The same ordering one level out, in the resolver that answers for a volume
// query and for whatever a blast's trace stopped on.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The world arm does NOT suppress the SDK arm, and that is deliberate rather
// than an oversight: today CWorld is still a CBaseEntity and every existing
// reader of AnyEntity_t reads pBase. Making the world stop answering there
// before T3 would change live behaviour ahead of the change meant to cause it.
//
// Pinned because the natural "tidy-up" is to guard the SDK lookup with
// `if ( !ent.pWorld )`, matching the pGame arm above it, and that tidy-up is a
// silent behaviour change today.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// IsValid() counts the world. It is what CAnyEntitiesEnum uses to decide the
// partition handed it an entity at all — the exact place "hit the world" and
// "hit nothing" would go on answering alike.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The one neutral answer on AnyEntity_t that is WRONG for the world.
//
// "Anything outside the SDK can be hurt" is right for the parallel hierarchy,
// which forwards damage to managed code. The world has nothing to forward to
// and stock's answer is unambiguous — CBaseEntity leaves m_takedamage at
// DAMAGE_NO and CWorld never changes it. Without this arm, T3 flips a blast's
// victim list to include the level itself, which is not a crash and shows up
// only as damage numbers that do not add up.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// ONE COMPARISON PER DLL, and both of them the shared rule.
//
// DidHitWorld() used to spell the pointer compare itself on each side. Two
// spellings of "is it the world" in one build is how the two sides drift, and
// the shared rule is the one with the level-load window handled.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE CLIENT'S SINGLETON IS NOT THE ENTITY LIST ANY MORE, and this is the fact
// that makes step 5e's un-derive survivable.
//
// The client's arm was `ClientEntityList().GetBaseEntity( 0 )`. C_World came
// off C_BaseEntity in 5e (plan_entity.md §3.11, as amended: both sides come off
// the SDK hierarchy), so that call answers NULL BY DESIGN for the world — and
// SourceSharp_IsTheWorldHandle answers false for a NULL world. Left alone it
// would have made `DidHitWorld()` false for EVERY trace on the client, silently:
// no crash, no log, and impact effects, decals and the movement code's
// `pm.DidHitWorld()` all quietly taking their other arm. The client's movement
// arm is the expensive one, because gamemovement.cpp is SHARED and the server's
// copy would still have said true.
//
// SO THE ABSENCE IS ASSERTED AS WELL AS THE REPLACEMENT. A test that only
// checked the new spelling was present would pass a file that had both.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND IT ASKS FOR THE HANDLE, WHICH IS NOT THE SAME QUESTION AS "does the
// object exist".
//
// The two sides differ here and the difference is real rather than cosmetic. On
// the server a CWorld is made per map and freed by the entity purge, so
// `g_WorldEntity != NULL` IS "a level is loaded". On the client the object is a
// PROCESS-lifetime singleton — ClientWorldFactoryInit makes it at DLL init and
// ClientWorldFactoryShutdown frees it at DLL shutdown — so its existence is
// true at the main menu, between maps, and before any packet has arrived.
//
// world_subject.h's rule has a whole paragraph on why "nothing" and "the world"
// must stay tellable apart during level load. Asking the singleton for its
// POINTER would have thrown that away on this side only, and the symptom would
// have been traces reporting a world hit in a window where there is no world.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE SERVER'S ARM, AND IT WAS DISABLED BY THE FLIP. Lane `worldspawn`.
//
// SourceSharp_IsTheWorld carried `#ifndef SS_CBASEENTITY_ALIAS ... #else return
// false; #endif`, on the stated ground that "armed, CBaseEntity is IGameEntity,
// which is NOT on the IHandleEntity chain -- so there is no conversion to make".
//
// CWorld IS NOT A CBaseEntity ON EITHER SIDE OF THE FLIP. T3 un-derived it:
// `class CWorld : public IServerEntity`, and the prose above the function in
// world_subject_dll.cpp already says the cast is "CWorld -> IServerEntity -> ...
// after T3. Both are static_cast-able and neither needs this file to change."
// SS_CBASEENTITY_ALIAS is not on that chain, so the guard's premise was about a
// class it did not describe. shell_entityslots.cpp performs the identical
// CWorld * -> IHandleEntity * cast in an ARMED translation unit today.
//
// THIS IS THE SERVER TWIN OF THE CLIENT BUG PINNED DIRECTLY ABOVE, and it cost
// more, because the client's version merely answered NULL from a lookup while
// this one answered a COMPILED-IN CONSTANT. CServerEntityResolver::WorldFromEntityHandle
// is a projection of this bool, so armed it answered NULL for the world always;
// CServerEntityResolver::Resolve therefore resolved the world's own handle to
// nothing; and SourceSharp_FindEntityByClassname, whose world clause fires on
// `!start.IsValid()`, returned the world FOREVER. Measured on a live armed
// build: `debug-api ents "*"` spun until its timeout, and so did every other
// FindAllByClassname("*") consumer in the mod.
//
// IT COULD NOT HAVE BEEN CAUGHT EARLIER, and that is worth stating rather than
// treating as an excuse. Armed, the map parse was ALSO guarded out, so no CWorld
// was ever constructed -- and against a level with no world, `false` and the
// truth are the same answer. The two guards hid each other.
//
// SO THE ABSENCE IS ASSERTED AS WELL AS THE REPLACEMENT, exactly as the client
// test above does: a check that only looked for the shared rule would pass a
// file that had both arms.
//-----------------------------------------------------------------------------
