//========= SourceSharp — baseentity.cpp's nine static data members ==========//
//
// A SOURCE SCANNER, like baseentity_shared_lift_test, physicslink_lift_test and
// soundemitter_lift_test, and for the same reason: the claim is about the TEXT
// of files that need a whole engine to run, so the text is what is read.
// buildcpptests links each test binary against gtest and nothing else.
//
// WHAT WAS DONE. plan_baseentity.md Phase 13 note C counted SIXTEEN static DATA
// members of CBaseEntity / C_BaseEntity across the tree and found that FOURTEEN
// of them lived in the two files this lane owns:
//
//   baseentity.o    6   m_pClassSendTable, s_bAbsQueriesValid,
//                       m_pPredictionPlayer, m_nPredictionRandomSeed,
//                       m_nPredictionRandomSeedServer, m_DataMap
//   c_baseentity.o  8   those, minus m_nPredictionRandomSeedServer, plus
//                       m_pClassRecvTable, s_bAbsRecomputationEnabled,
//                       s_bInterpolate, m_PredMap
//
// NINE of the fourteen were written by hand in the two .cpp files and this lane
// removed all nine. The other five are emitted by the network-table and datamap
// MACROS -- IMPLEMENT_SERVERCLASS_ST_NOBASE, IMPLEMENT_CLIENTCLASS,
// BEGIN_DATADESC_NO_BASE, BEGIN_PREDICTION_DATA_NO_BASE -- so no line of either
// file spells them and no source scanner can see them. They are a separate
// problem with a separate shape and they are named in the plan.
//
//   THREE WERE DELETED, not moved. CBaseEntity::m_nPredictionRandomSeed,
//   CBaseEntity::m_nPredictionRandomSeedServer and
//   C_BaseEntity::m_nPredictionRandomSeed had no odr-user anywhere: the lane
//   that landed entity_prediction_seed.h rerouted every reader and writer, but
//   the definitions were in THESE files, which were not that lane's, so they
//   stayed behind as dead objects. Proven with nm --undefined-only over all 199
//   server and 328 client objects on the debug link lines, naming no referrer,
//   plus a tree-wide grep finding only the definitions and the declarations.
//
//   SIX WERE MOVED, because they are live.
//     m_pPredictionPlayer          -> EntityPrediction_PlayerStorage()
//                                     in shared/entity_prediction_seed.h
//     s_bAbsQueriesValid           -> EntityAbs_QueriesValidRef()
//     s_bAbsRecomputationEnabled   -> EntityAbs_RecomputationEnabledRef()
//     s_bInterpolate               -> EntityInterp_EnabledRef()
//                                     in shared/entity_abs_query_state.h (new)
//   -- two of those names are one per side, which is how six members come from
//   four storages.
//
// WHY A DATA MEMBER IS THE WORST OF THE THREE FAILURES -- CORRECTED, and the
// correction matters because work was planned on the old claim.
//
//   THIS FILE USED TO SAY that a static DATA member of an alias "stops the
//   translation unit PARSING, so every other diagnostic in that file
//   disappears behind it", and that arming step 4 with these in place "would
//   have produced two files that stop parsing". THAT IS FALSE. The diagnostic
//   is fully recoverable and gcc keeps going.
//
//   MEASURED, three times independently rather than argued. Armed, at the tip
//   that carried these, game/server/baseentity.cpp:296 is
//     error: 'SendTable* IGameEntity::m_pClassSendTable' is not a static data
//            member of 'class IGameEntity'
//   and 283 FURTHER DIAGNOSTICS FOLLOW IT IN THE SAME TRANSLATION UNIT -- 573
//   in that file altogether. On the client, 915 followed the first. Reduced to
//   twelve lines and re-run standalone, the same construct still reports every
//   later error in the file:
//
//     struct SendTable {};
//     struct IGameEntity { SendTable *m_pClassSendTable; };
//     using CBaseEntity = IGameEntity;
//     SendTable *CBaseEntity::m_pClassSendTable = 0;   // the error
//     int later = undeclared;                          // still diagnosed
//
//     $ g++ -fsyntax-only -fmax-errors=0 probe.cpp
//     ... 'SendTable* IGameEntity::m_pClassSendTable' is not a static data member
//     ... 'undeclared' was not declared in this scope
//
//   WHAT IS ACTUALLY TRUE, and it is still the worst of the three. An
//   out-of-line FUNCTION definition written `CBaseEntity::Foo()` under
//   `using CBaseEntity = IGameEntity;` is one error on one line. A static DATA
//   member is not one error: the definition is rejected, the member stays
//   undeclared at that scope, and every later mention of it is rejected in
//   turn. The cost is VOLUME, not a lost TU -- hundreds of diagnostics whose
//   single cause is one line, with the real errors of the file scattered among
//   them. Step 4 arming with these in place would still have given a list of
//   broken TUs to work through; it would have given one buried in an order of
//   magnitude more output than it needed to be.
//
//   So the lift these nine justified was worth doing, for error volume. Nobody
//   should plan around the TU being lost, because it is not.
//
// THE DECLARATIONS DELIBERATELY STAY in baseentity.h and c_baseentity.h. A
// static data member that is declared and never defined links clean as long as
// nobody odr-uses it, and the declarations go away with the class body at step
// 4. That is entity_prediction_seed.h's landed shape, followed here.
//
// WHY THIS NEEDS A GUARD, and none of these produces a diagnostic:
//
//   1. Somebody puts logic in a forwarder. Then there are two implementations
//      of a gate: the SDK's call sites take the class-scope one, the port's own
//      files take the free one, and they drift silently in both directions.
//   2. Somebody moves the @MULTICORE worker-thread rule out of the client's
//      class-scope functions and into the shared storage. That would hand the
//      SERVER a behaviour it never had -- IsAbsQueriesValid answering true off
//      the main thread -- and no test in the tree would notice.
//   3. Somebody drops one of the includes, and the accessor resolves through
//      some other transitive path today and stops resolving tomorrow.
//
// game/server/baseentity.cpp, game/client/c_baseentity.cpp and
// c_baseentity_statics.cpp are no longer compiled by client.so or server.so and
// have left the source tree. The checks that read them -- the nine static data
// members gone from both .cpp files, the class-scope definitions gone from
// them, the blocked-static inventory, the client's own two of the deleted
// names -- and the static-data-member and member-function detectors that
// existed only for them were culled with them. What is left pins the headers
// and the shim/statics files the build does compile.
//
// Each file is pinned by an ANCHOR string rather than a line count: a lane
// elsewhere had `lines.size() > 100` sitting four lines from a false alarm.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <cstring>
#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Strip // and /* */ comments, so that a name written in PROSE -- and every
// file under test is full of exactly that, being about exactly this -- is not
// read as code. This file's own header block would fail nearly every assertion
// below without it.
//
// Not a C++ lexer: it does not track string or character literals, and does not
// need to, because what is built on it is a DEFINITION detector and a
// definition cannot occur inside a string literal.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The body of one function DEFINED as `<Scope>::<Name>(...)`, squeezed of
// whitespace. Empty if it is not defined in this text. Used to prove a
// forwarder still forwards and has not grown a second implementation.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
} // namespace

//=============================================================================
// THE FILES ARE THERE AT ALL. Every assertion below is over text read off
// disk, and a missing file reads as an empty string -- which passes every
// empty-list assertion in this file. This is the check that stops the rest of
// them being checks that cannot fail.
//=============================================================================

//=============================================================================
// THE SERVER ENTITY HEADER IS OUT OF THE FARM.
//
// Several facts below asserted ABSENCES in game/server/baseentity.h: no
// class-scope accessor bodies, no s_bAbsQueriesValid member, no call into the
// client's lifted EntityAbs_* pair, no gEntList. The header is compiled by
// nothing since plan_server_novalve.md P5 and left the farm in P6, which is the
// stronger form of every one of them -- nothing can come back into a header no
// file can include. So those absences are retired at their sites and asserted
// here once, as the file's absence, anchored on a sibling the farm still links
// so a reader that sees nothing cannot satisfy it.
//=============================================================================

//=============================================================================
// THE NINE ARE GONE FROM THE TWO .cpp FILES.
//=============================================================================

//=============================================================================
// THE STORAGE EXISTS, AND IS WHERE THE COMMITS SAY IT IS.
//=============================================================================

//=============================================================================
// THE FORWARDERS STILL FORWARD. A forwarder that grows a body is two
// implementations of one gate, and the build is green in both directions.
//=============================================================================

//=============================================================================
// THE WORKER-THREAD RULE STAYED WHERE IT WAS. This is the one behaviour
// difference between the two sides, and moving it into the shared storage
// would hand the server a rule it never had.
//=============================================================================

//=============================================================================
// THE HEADERS ARE REACHED DIRECTLY, not by luck through some other include.
//=============================================================================

//=============================================================================
// THE DETECTOR ANSWERS TRUE WHEN THERE IS SOMETHING TO FIND.
//
// Without these, every empty-list assertion above passes for free. Twenty-seven
// checks-that-cannot-fail have been found in this tree, several inside
// instruments built to catch them.
//=============================================================================

//-----------------------------------------------------------------------------
// THE DEFINITION DETECTOR, ON FIXTURES -- and the FIRST fact is the one that
// made it necessary.
//
// FunctionBody answers with a body's CONTENTS, so an EMPTY body and no
// definition at all are the same answer: "". Every "must not be defined here"
// assertion built on it therefore passes on an empty stub, which is exactly the
// silent no-op plan_entity.md §7 forbids and exactly what those assertions
// exist to catch. Measured on this tree, not feared: a retired function given a
// `{ // comment }` body was invisible and a `{ int x = 1; }` body was not.
//-----------------------------------------------------------------------------
//=============================================================================
// THE SIX CLIENT ACCESSORS THAT WERE LIFTED WITH THE STORAGE.
//
// c_baseentity.cpp defined all six as `static` C_BaseEntity members. They take
// no `this` and read none, so the class was a namespace; under the alias a
// definition written `C_BaseEntity::Foo()` is a compile error. The bodies did
// NOT move -- four reach that file's own eight-deep save stack and all six
// reach ThreadInMainThread() -- only the scope did, which is
// EntitySim_IsSimulatingOnAlternateTicks's landed shape.
//=============================================================================

namespace
{

struct Lifted
{
	const char *pszMember; // the old C_BaseEntity:: name, now an inline forwarder
	const char *pszFree; // the free function it forwards to
};

} // namespace

//=============================================================================
// THE FRAME-LOOP STATICS: NINE LIFTED, ONE BLOCKED.
//
// c_baseentity.cpp declares EIGHTEEN `static` members. Six of them are the abs
// cluster above. Of the rest, three lifted cleanly, SIX were blocked because a
// free function is not a member and cannot touch a private or protected one,
// and those six are lifted now.
//
// THE BLOCKED LIST WORKED. It said in so many words that it was an inventory
// and not a ban -- "when one is unblocked the test goes red for the right
// reason and says so" -- and that is exactly what happened: the lane that added
// the friend block ran this file and got six failures naming six members.
//
// FOUR OF THE SIX WERE UNBLOCKED BY A FRIEND DECLARATION at the bottom of
// C_BaseEntity, which is the decision this list declined to make. TWO WERE
// NEVER BLOCKED AT ALL, and the reasons are worth keeping:
//
//   InterpolateServerEntities  was blocked on the two PROTECTED statics it
//                              calls. Lifting those dissolved it rather than
//                              working around it.
//   AddVisibleEntities         was listed as blocked on "reads
//                              ent->m_PredictableID". That field is PUBLIC
//                              (c_baseentity.h:1407, under the `public:` at
//                              1369) and always was. The entry was written
//                              from a read of the body rather than from a
//                              probe -- in a list whose own comment says the
//                              compiler is the only reliable oracle.
//
// ONE REMAINED BLOCKED (CreatePredictedEntityByName, on CPredictableList's
// protected members). Its inventory read c_baseentity.cpp, which is no longer
// compiled and has left the tree, so it was culled with that file.
//
// The per-lift detail -- the forwarder bodies, the four friend declarations and
// the allocation operators that went the same way -- is in
// c_baseentity_lift_test.cpp. What stays here is the inventory.
//=============================================================================

namespace
{

// WHERE EACH OF THE NINE ENDED UP -- CLIENT_FLIP lane 2 (`c8-entitystatics`).
//
// Step 6 evacuated all nine to c_baseentity_statics.cpp so the link would hold
// when c_baseentity.cpp went. The client flip deletes THAT file too, and at
// that point the nine stop being one group: one of them is the port's own
// mechanism and eight walked SDK registries that only C_BaseEntity ever added
// to -- g_AimEntsList, g_TeleportList, g_InterpolationList, the predictables
// list -- so after the flip every one of those loops runs over nothing.
//
// bShim distinguishes the two, and BOTH are asserted, in opposite directions.
// A row that is RETIRED must be defined in NO file: a retired function left
// defined somewhere is a capability everybody believes is gone. A row that is
// SHIM must be defined in the shim and in neither dying file.
struct FrameLoopLifted
{
	const char *pszMember;
	const char *pszFree;
	bool bShim; // true: moved to the shim. false: RETIRED.
};

} // namespace

//=============================================================================
// THE SERVER SLICE: THREE LIFTED, THREE MOVED INTO THE CLASS BODY, ONE BLOCKED.
//
// baseentity.cpp declares SEVEN `static` CBaseEntity members. Three were lifted
// to free functions in the new sourcesharp/game/server/baseentity_statics.h with their
// bodies unmoved. Three needed no lifting at all -- they were already one-line
// forwarders onto free functions that exist -- so only their DEFINITIONS moved,
// into the class body, which removes the symbol without inventing anything.
// One is blocked.
//=============================================================================

namespace
{

// The kServerLifted table went with baseentity_statics.{h,cpp}
// (plan_server_native_min P1): the files and the absence pins over them are
// gone together.

// THE STEP-6 EVACUATION, of the four above. A body stays in baseentity.cpp
// unless something OUTSIDE the removal set names it, in which case it has to
// leave -- baseentity.cpp is deleted at step 6 and the caller is not.
//
// Measured, not guessed: src/evict_consumers relinks server.so with
// baseentity.o removed and src/s6r_linkonly.py reads the undefined symbols that
// are not CBaseEntity:: members. EntityCreate_Spawned came back demanded from
// gameinterface.cpp:1108. EntityCreate_NoSpawn did NOT -- its only demand was
// baseentity.h's inline forwarder, which dies with the class -- and it is
// evacuated anyway, because EntityCreate_Spawned calls it.
//
// GameSound_EmitSentenceByIndex IS HERE, AND IT APPEARS IN NO PROBE ON A NORMAL
// BUILD. game/server/sound.cpp names it at four sites and survives step 6, so
// leaving the definition in the removal set is a link failure -- but with
// --gc-sections on, the linker drops sound.cpp's four SENTENCEG_* emitters
// before it ever needs the callee, so the reference never becomes undefined. It
// is absent from a release evict_consumers, absent from a debug one, and
// present under `make debug GC_SECTIONS=0`. Makefile:30-52 documents that
// failure mode for an evicted FILE; this is the same thing through a lifted
// free function.
//
// EntityPredict_CreateByName was in no probe in any of the three
// configurations, so it stays, and this table says so rather than leaving it to
// be inferred from an absence.
// (The evacuation table that stood here held GameSound_EmitSentenceByIndex
// alone; it went with the function in the server gc cull, 2026-10-01.)

// (kInBody stood here: PrecacheModel, PrecacheModelComponents and Instance,
// the three in-body one-liners whose absence was read out of
// game/server/baseentity.h. That header is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6, and the table went with
// the facts that read it.)

} // namespace

// TheServerForwardersWentWithTheClassAndTheFreeFunctionsAreWhatIsLeft went
// with the files it read (plan_server_native_min P1): baseentity_statics.{h,cpp}
// are deleted -- the last lifted body left in the 2026-10-01 gc cull and the
// two tenants that remained (one unreferenced send proxy, one write-only
// global) had no reader between them. The client half of the fact is pinned
// by the client tests that follow.

// TheDefaultArgumentsAreNotRepeatedOnTheFreeFunctions read
// baseentity_statics.h and went with it (plan_server_native_min P1).

// TheServerHeaderNamesNoEntityTypeAndForwardDeclaresNone read
// baseentity_statics.h and went with it (plan_server_native_min P1).

// TheDeadFactoriesStayDeleted read baseentity_statics.{h,cpp} and went with
// them (plan_server_native_min P1).

//=============================================================================
// THE FIVE DELETIONS, PINNED AS ABSENCES.
//
// A deletion that gets typed back is exactly as invisible as one that never
// happened, so each is pinned here -- absent from baseentity.cpp AND from
// baseentity.h, because a declaration without a definition is the shape that
// invites somebody to supply one.
//
// Found by a four-stage funnel over baseentity.o, and the two middle stages are
// the ones that matter, because without them the answer is 120 and every one of
// those 120 is alive:
//
//   261  defined out of line
//   124  no object on the link line names it undefined
//   120  ... and not declared `virtual` in EITHER header
//         (a virtual call emits no symbol, so the linker never saw its callers)
//    16  ... and the DEFINING FILE names it exactly once -- the definition
//         (this is the stage that matters most: CBaseEntity's own datadesc,
//          send table and VScript registrations live in baseentity.cpp and take
//          member-function POINTERS -- DEFINE_INPUTFUNC( ..., InputAlpha ),
//          DEFINE_SCRIPTFUNC( GetScriptScope ). A pointer taken in the defining
//          TU produces no undefined symbol anywhere, so every input function
//          and every script function reads as dead without this filter)
//     6  ... and no other source file in the tree names it
//     5  ... minus GetBaseMap, which is MACRO-generated: BEGIN_DATADESC_NO_BASE
//         defines it and BEGIN_DATADESC_GUTS calls it, both by token pasting, so
//         the text of baseentity.cpp never spells it and no source scan can see
//         the call. Alive.
//
// The same funnel over c_baseentity.o answers ZERO: 266 defined, 62
// unreferenced, 13 surviving the own-file filter, and the single survivor is
// GetBaseMap again. The client has no dead member by this standard.
//=============================================================================

// TheFiveDeletedMembersAreStillGoneFromTheServer is RETIRED. It asserted
// IsInTeam, IsInAnyTeam, GetTransmitState, ComputeAbsDirection and
// SetLocalTransform stayed undeclared in game/server/baseentity.h, which is
// compiled by nothing since plan_server_novalve.md P5 and left the farm in P6.
// Nothing can be redeclared in a header no file includes; the header's own
// absence is TheServerEntityHeaderIsOutOfTheFarm.

//=============================================================================
// THE void* ROUND TRIP. The one piece of this lane that is LOGIC rather than
// text: storing a typed pointer through void* and reading it back must give
// the same pointer. Run rather than read, because a source scanner cannot say
// anything about it.
//=============================================================================

namespace
{

// Stand-ins for the real shapes. A player is not a standard-layout single-base
// type in the SDK, and the risk the void* raises is precisely that a cast on
// one side and a cast on the other pick up different base-subobject offsets.
struct FakeUnknownBase
{
	virtual ~FakeUnknownBase() {}

	int a;
};

struct FakeNetworkableBase
{
	virtual ~FakeNetworkableBase() {}

	int b;
};

struct FakePlayer : public FakeUnknownBase, public FakeNetworkableBase
{
	int c;
};

// The header's shape, reproduced exactly: a function-local static behind an
// inline accessor. Reproduced rather than included because buildcpptests links
// gtest and nothing else, and entity_prediction_seed.h is reachable only with
// the SDK's include tree.
inline void *&FakePlayerStorage()
{
	static void *s_p = 0;
	return s_p;
}

// The two forwarders from baseentity_shared.h, same static type at both casts.
inline FakePlayer *GetFakePlayer()
{
	return static_cast<FakePlayer *>( FakePlayerStorage() );
}

inline void SetFakePlayer( FakePlayer *p )
{
	FakePlayerStorage() = p;
}

} // namespace

TEST( BaseEntityStatics, VoidStorageRoundTripsThePointerExactly )
{
	FakePlayer player;
	player.c = 0x5A5A;

	SetFakePlayer( &player );
	ASSERT_EQ( &player, GetFakePlayer() ) << "the void* storage did not return the pointer that was put in it. "
										  << "The two casts in baseentity_shared.h are the only place the "
										  << "prediction player's type exists; if they stop agreeing, "
										  << "GetPredictionPlayer() hands out a pointer into the middle of an "
										  << "object and every caller reads garbage.";
	EXPECT_EQ( 0x5A5A, GetFakePlayer()->c );

	SetFakePlayer( 0 );
	EXPECT_EQ( (FakePlayer *)0, GetFakePlayer() )
		<< "storing NULL did not read back as NULL. player_command.cpp and "
		<< "prediction.cpp both clear it that way at the end of a usercmd, and "
		<< "IsInPrediction() is `GetPredictionPlayer() != NULL`.";
}

TEST( BaseEntityStatics, VoidStorageStartsNull )
{
	// Not a tautology with the round trip: this is about the INITIAL value, and
	// it is what makes IsInPrediction() false before any usercmd has run. The
	// member it replaced was `= NULL`.
	//
	// A second storage, untouched by the test above, because that one has
	// already written to FakePlayerStorage().
	struct Fresh
	{
		static void *&Ref()
		{
			static void *s_p = 0;
			return s_p;
		}
	};

	EXPECT_EQ( (void *)0, Fresh::Ref() ) << "a function-local static void* did not start null";
}
