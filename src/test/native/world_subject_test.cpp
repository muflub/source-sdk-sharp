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
const IHandleEntity *const kWorld = reinterpret_cast< const IHandleEntity * >( 0x1000 );
const IHandleEntity *const kCrate = reinterpret_cast< const IHandleEntity * >( 0x2000 );

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false; i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string ReadStripped( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line; all += '\n';
	}
	return StripComments( all );
}

std::string NoWhitespace( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

struct File_t { const char *pszRel; const char *pszAnchor; };

const File_t kMovementGround = { "sourcesharp/game/shared/movement_ground.h",
	"MovementGroundFromTrace" };
const File_t kAnyEntity = { "sourcesharp/game/server/anyentity.h", "AnyEntityOf" };
// MOVED BY plan_entity.md step 4a. game/server/gametrace_dll.cpp is out of
// server_csharp.vpc and off disk; its five definitions -- the two
// SourceSharp_ ones this port wrote and CGameTrace's three accessors --
// are sourcesharp/game/server/world_subject_dll.cpp now. The path is
// followed rather than the assertion softened: ReadAnchored fails loudly on
// a file it cannot read, so a stale path here would have been a red test
// and never a vacuous pass.
const File_t kTraceDll = { "sourcesharp/game/server/world_subject_dll.cpp", "CGameTrace::DidHitWorld" };
// MOVED BY C6 (lane c6-input): game/client/gametrace_client.cpp left client.so,
// and the client's SourceSharp_IsTheWorld went to world_adapter_client.cpp,
// beside the world it compares against. Its callers ask it directly; the
// client defines no CGameTrace member any more.
const File_t kTraceClient = { "sourcesharp/game/client/world_adapter_client.cpp", "SourceSharp_IsTheWorld" };

int CountAll( const std::string &strHay, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t at = strHay.find( strNeedle ); at != std::string::npos;
	      at = strHay.find( strNeedle, at + 1 ) )
	{
		n++;
	}
	return n;
}

// The client's SourceSharp_IsTheWorld body alone, from the whitespace-free
// file: the adapter defines a dozen other world reads, and a needle over the
// whole file would find them instead.
std::string ClientIsTheWorldBody( const std::string &cliNoWs )
{
	const std::string head = "boolSourceSharp_IsTheWorld(constIHandleEntity*pHandleEntity){";
	const size_t at = cliNoWs.find( head );
	EXPECT_NE( at, std::string::npos ) << "the client's SourceSharp_IsTheWorld definition is not in its file";
	if ( at == std::string::npos )
		return std::string();
	const size_t end = cliNoWs.find( "}", at );
	return cliNoWs.substr( at, end - at + 1 );
}

std::string ReadAnchored( const File_t &file )
{
	const std::string body = ReadStripped( file.pszRel );

	EXPECT_FALSE( body.empty() )
		<< file.pszRel << " could not be read; everything asserted about it "
		   "would otherwise pass vacuously.";

	if ( !body.empty() )
	{
		EXPECT_NE( body.find( file.pszAnchor ), std::string::npos )
			<< file.pszRel << " no longer contains its anchor \""
			<< file.pszAnchor << "\".";
	}
	return body;
}

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
TEST( WorldSubject, StandingOnTheWorldIsAskedBeforeEitherHierarchy )
{
	const std::string body = NoWhitespace( ReadAnchored( kMovementGround ) );

	// READ THE ASSERTIONS, NOT THE NAME. There is ONE hierarchy arm now, not
	// two: the SDK arm this fact used to order the world against is gone.
	//
	// `TraceEntity( tr )` bottomed out in IServerUnknown/IClientUnknown::
	// GetBaseEntity(), and all eight implementations of that in the two DLLs
	// are `mov $0x0,%eax; ret`. Lane `the-close` proved it a constant NULL and
	// deleted it (d3a0bfa3a); TraceGameEntity, which asks the virtual
	// GetGameEntity() and exists on BOTH sides, is what it left behind.
	//
	// SO THE ORDERING CLAIM GETS STRONGER RATHER THAN WEAKER. The world arm
	// used to be asked first so that it would be EXERCISED before T3 instead of
	// sitting behind an arm that answered for the world today. Now the arm it
	// was sheltering behind does not exist, and the world arm is the only thing
	// between level geometry and an answer of NULL. It is still asserted FIRST,
	// because the same reasoning applies to the arm that DID survive: a world
	// hit must not have to fail TraceGameEntity before it is recognised.
	const size_t nWorld = body.find( "SourceSharp_IsTheWorld(tr.m_pEnt)" );
	const size_t nGame  = body.find( "TraceGameEntity(tr)!=NULL" );

	ASSERT_NE( nWorld, std::string::npos )
		<< "MovementGroundFromTrace no longer asks whether the trace hit the "
		   "world. With CWorld off the CBaseEntity hierarchy this function "
		   "answers NULL for level geometry and the game stops having floors.";
	ASSERT_NE( nGame, std::string::npos )
		<< "MovementGroundFromTrace no longer asks TraceGameEntity, which is "
		   "the only arm left that answers for an entity. Without it every "
		   "managed floor reads as nothing to stand on.";

	EXPECT_LT( nWorld, nGame )
		<< "the world arm has been moved AFTER the entity arm. It is a fallback "
		   "there: it cannot run until CWorld un-derives, so it is untested "
		   "until the day it is first needed. Asked first it returns the same "
		   "answer today and is exercised by every world hit in every frame.";

	// THE ARM THAT WENT, asserted absent so a revert is a red rather than a
	// silently restored constant NULL. `TraceEntity` and `TraceGameEntity`
	// differ by an INFIX, so neither is a substring of the other and this
	// absence really can fail while the successor above is present.
	EXPECT_EQ( body.find( "TraceEntity(tr)" ), std::string::npos )
		<< "MovementGroundFromTrace resolves through TraceEntity again. It "
		   "answers a constant NULL for every entity in both DLLs, so it can "
		   "only take ground away -- and above the world arm it would take the "
		   "floor away too.";
}

//-----------------------------------------------------------------------------
// The same ordering one level out, in the resolver that answers for a volume
// query and for whatever a blast's trace stopped on.
//-----------------------------------------------------------------------------
TEST( WorldSubject, AnyEntityOfAsksTheWorldFirst )
{
	const std::string body = NoWhitespace( ReadAnchored( kAnyEntity ) );

	// READ THE ASSERTIONS, NOT THE NAME -- or rather, read what the SDK arm now
	// IS. `ent.pBase = EntityFromEntityHandle( pHandleEntity );` is gone: lane
	// `the-close` proved that call a constant NULL in both DLLs and deleted the
	// family (d3a0bfa3a). The field survives, and the assignment survives, as
	// the literal `ent.pBase = NULL;` -- so the three-arm SHAPE this fact is
	// about is intact and the ordering is still a real ordering.
	//
	// The literal is the successor, not a placeholder to be tidied away: every
	// reader of AnyEntity_t reads the three arms, and computing a NULL with a
	// partition walk in front of it is strictly worse than writing one.
	const size_t nWorld = body.find( "ent.pWorld=SourceSharp_WorldFromEntityHandle(" );
	const size_t nBase  = body.find( "ent.pBase=NULL;" );
	// ss:: since P5 lane E: anyentity.h is on public/ headers (ss_entityhandle.h).
	const size_t nGame  = body.find( "ent.pGame=ss::GameEntityFromEntityHandle(" );

	ASSERT_NE( nWorld, std::string::npos )
		<< "AnyEntityOf no longer resolves a world arm.";
	ASSERT_NE( nBase, std::string::npos )
		<< "AnyEntityOf no longer sets pBase at all. The field is read by "
		   "IsValid(), ClassMatches and the world_adapter diagnostic; leaving "
		   "it unwritten is an uninitialised read, not a simplification.";
	ASSERT_NE( nGame, std::string::npos );

	EXPECT_LT( nWorld, nBase );
	EXPECT_LT( nWorld, nGame );

	// And the deleted resolver has not come back. Asserted as a count against
	// its own live successor, because `EntityFromEntityHandle(` IS a suffix of
	// `GameEntityFromEntityHandle(` -- a plain absence check here could never
	// fail while the successor is present, which is a green that measures
	// nothing.
	EXPECT_EQ( CountAll( body, "EntityFromEntityHandle(" ),
	           CountAll( body, "GameEntityFromEntityHandle(" ) )
		<< "anyentity.h names EntityFromEntityHandle on its own again. It "
		   "bottoms out in GetBaseEntity(), which is a constant NULL in both "
		   "DLLs, so it is a partition walk in front of a zero.";

	ASSERT_LT( 0, CountAll( body, "GameEntityFromEntityHandle(" ) )
		<< "neither spelling is in the file, which would make the equality "
		   "above 0 == 0 and this half vacuous";
}

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
TEST( WorldSubject, TheWorldArmDoesNotSuppressTheSdkArm )
{
	const std::string body = NoWhitespace( ReadAnchored( kAnyEntity ) );

	EXPECT_EQ( body.find( "if(!ent.pWorld)ent.pBase=" ), std::string::npos )
		<< "the SDK lookup has been made conditional on the world arm. Today "
		   "the world IS a CBaseEntity and pBase must keep finding it; the "
		   "overlap ends at T3 by itself, when the inheritance goes.";
}

//-----------------------------------------------------------------------------
// IsValid() counts the world. It is what CAnyEntitiesEnum uses to decide the
// partition handed it an entity at all — the exact place "hit the world" and
// "hit nothing" would go on answering alike.
//-----------------------------------------------------------------------------
TEST( WorldSubject, TheWorldCountsAsSomethingBeingThere )
{
	const std::string body = NoWhitespace( ReadAnchored( kAnyEntity ) );

	EXPECT_NE( body.find( "boolIsValid()const{returnpBase!=NULL||pGame!=NULL||pWorld!=NULL;}" ),
	           std::string::npos )
		<< "AnyEntity_t::IsValid no longer counts the world, so a resolved "
		   "world reads as nothing being there.";
}

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
TEST( WorldSubject, TheWorldCannotBeHurt )
{
	const std::string body = NoWhitespace( ReadAnchored( kAnyEntity ) );

	const size_t nDamage = body.find( "boolCanTakeDamage()const{if(pWorld)returnfalse;" );

	EXPECT_NE( nDamage, std::string::npos )
		<< "AnyEntity_t::CanTakeDamage no longer refuses for the world. Its "
		   "neutral for an entity off the SDK's tree is TRUE, so after T3 a "
		   "radius blast would offer damage to the level itself.";
}

//-----------------------------------------------------------------------------
// ONE COMPARISON PER DLL, and both of them the shared rule.
//
// DidHitWorld() used to spell the pointer compare itself on each side. Two
// spellings of "is it the world" in one build is how the two sides drift, and
// the shared rule is the one with the level-load window handled.
//-----------------------------------------------------------------------------
TEST( WorldSubject, BothDllsAskTheSharedRuleRatherThanComparingThemselves )
{
	const std::string dll = NoWhitespace( ReadAnchored( kTraceDll ) );
	const std::string cli = NoWhitespace( ReadAnchored( kTraceClient ) );

	EXPECT_NE( dll.find( "SourceSharp_IsTheWorldHandle(" ), std::string::npos )
		<< "the server no longer routes its world comparison through the "
		   "shared rule.";
	EXPECT_NE( ClientIsTheWorldBody( cli ).find( "SourceSharp_IsTheWorldHandle(" ), std::string::npos )
		<< "the client no longer routes its world comparison through the "
		   "shared rule.";

	EXPECT_NE( dll.find( "boolCGameTrace::DidHitWorld()const{returnSourceSharp_IsTheWorld(m_pEnt);}" ),
	           std::string::npos )
		<< "the server's DidHitWorld has stopped being the free function's one "
		   "caller, so a trace and a bare handle can now disagree.";
	// The client has no DidHitWorld at all since C6: its callers ask the free
	// function, so there is no second spelling to drift.
	EXPECT_EQ( cli.find( "CGameTrace::DidHitWorld" ), std::string::npos )
		<< "the client defines a CGameTrace member again; its callers ask "
		   "SourceSharp_IsTheWorld directly.";
}

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
TEST( WorldSubject, TheClientsArmAsksTheWorldSingletonAndNotTheEntityList )
{
	const std::string body = ClientIsTheWorldBody( NoWhitespace( ReadAnchored( kTraceClient ) ) );

	EXPECT_EQ( body.find( "GetBaseEntity(0)" ), std::string::npos )
		<< "the client's world arm resolves entity 0 through the entity list's "
		   "SDK view again. C_World is not on that hierarchy, so it answers NULL "
		   "and every DidHitWorld() on this side becomes false.";

	// Since C6 the singleton is reached through the adapter's own resolver,
	// SourceSharp_ClientWorldFromHandle, which answers the singleton's
	// IClientUnknown for the world's handle and NULL otherwise.
	EXPECT_NE( body.find( "SourceSharp_ClientWorldFromHandle(" ), std::string::npos )
		<< "the client's world arm no longer reads the world singleton through "
		   "the adapter's resolver.";
}

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
TEST( WorldSubject, TheClientsArmAsksWhetherTheWorldIsBoundToALevel )
{
	const std::string body = ClientIsTheWorldBody( NoWhitespace( ReadAnchored( kTraceClient ) ) );

	// Since C6: asked of the bound handle. SourceSharp_ClientWorldHandle() is
	// invalid between maps, and SourceSharp_ClientWorldFromHandle answers NULL
	// for it (ClientWorldArm's AnUnboundWorld* facts pin that rule).
	EXPECT_NE( body.find( "SourceSharp_ClientWorldFromHandle(SourceSharp_ClientWorldHandle())" ), std::string::npos )
		<< "the client's world arm no longer asks whether the world is bound to "
		   "a level. The object outlives every map, so its existence cannot mean "
		   "'there is a world' -- and world_subject.h's rule is written on the "
		   "assumption that the second argument is NULL when there is none.";

	// AND THE HANDLE IS WHAT ANSWERS IT, read out of the accessor's own body
	// rather than from its name. A method called IsBoundToTheLevel that
	// returned true unconditionally would satisfy the line above.
	const std::string hdr = NoWhitespace( ReadStripped( "game/client/c_world.h" ) );
	EXPECT_NE( hdr.find( "boolIsBoundToTheLevel()const{returnm_RefEHandle.IsValid();}" ),
	           std::string::npos )
		<< "IsBoundToTheLevel is not the handle's validity any more. The handle "
		   "is what the level's create call binds and Release gives back; "
		   "anything else is a second answer to the same question.";
}

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
// this one answered a COMPILED-IN CONSTANT. SourceSharp_WorldFromEntityHandle
// is a projection of this bool, so armed it answered NULL for the world always;
// SourceSharp_ResolveEntity therefore resolved the world's own handle to
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
TEST( WorldSubject, TheServersArmIsNotTurnedOffByTheAliasFlip )
{
	const std::string dll = NoWhitespace( ReadAnchored( kTraceDll ) );

	const size_t at = dll.find( "boolSourceSharp_IsTheWorld(constIHandleEntity*pHandleEntity){" );
	ASSERT_NE( at, std::string::npos )
		<< "SourceSharp_IsTheWorld has been renamed or resignatured in "
		<< kTraceDll.pszRel << "; everything below would pass vacuously.";

	// The body, by brace matching. NoWhitespace has already removed every
	// newline, so "the line with the closing brace on it" does not exist here
	// and looking for one would silently take the whole file -- which would
	// make the two absences below assertions about world_subject_dll.cpp
	// ENTIRELY rather than about this function, and they would then fail on
	// SourceSharp_IsTheWorldHandle's own `return false` two functions away.
	size_t p = dll.find( '{', at );
	ASSERT_NE( p, std::string::npos ) << "no opening brace after the signature.";
	int nDepth = 0;
	size_t end = std::string::npos;
	for ( ; p < dll.size(); p++ )
	{
		if ( dll[ p ] == '{' )
			nDepth++;
		else if ( dll[ p ] == '}' && --nDepth == 0 )
		{
			end = p;
			break;
		}
	}
	ASSERT_NE( end, std::string::npos ) << "SourceSharp_IsTheWorld's body is unterminated.";

	const std::string body = dll.substr( at, end - at );

	EXPECT_EQ( body.find( "returnfalse;" ), std::string::npos )
		<< "the server's world comparison answers a compiled-in false again. "
		   "Every find-by-classname that can reach the world then loops "
		   "forever, and DidHitWorld() is false for every trace that hits it.";

	EXPECT_EQ( body.find( "SS_CBASEENTITY_ALIAS" ), std::string::npos )
		<< "the server's world comparison is behind the alias flip again. "
		   "CWorld is not a CBaseEntity on either side of it -- the cast is "
		   "CWorld -> IServerEntity -> IServerUnknown -> IHandleEntity in both "
		   "builds -- so a guard here can only disable the arm on one of them.";

	EXPECT_NE( body.find( "SourceSharp_IsTheWorldHandle(" ), std::string::npos )
		<< "the server's world comparison no longer asks the shared rule.";
}
