//========= SourceSharp — the two shim TUs that STILL derive, and why ========//
//
// THIS FILE RECORDS TWO REFUSALS, and a refusal is the only kind of record that
// goes stale without anybody noticing — which is why it is a test and not a
// comment. Same standard, and the same reason, as gib_verdict_test.cpp:
// A RED HERE MEANS RE-TAKE THE VERDICT, NOT "REGRESSION".
//
// Route step 4 was to stop our own shim TUs deriving from the entity. Five of
// the eight objects were done (shim_underive_test.cpp pins them). THREE were
// evaluated and refused, each on a DIFFERENT ground.
//
// THE THIRD REFUSAL IS GONE BECAUSE IT WAS TAKEN. shell_netentity is deleted by
// route (c), and this file is where that instruction was written down: every
// assertion about the shell ended "if the class is GONE, delete this test".
// netentity_deleted_test.cpp stands in their place.
//
// Each remaining ground is a fact about a file somebody else may change for
// their own reasons.
//
//-----------------------------------------------------------------------------
// 1. shell_areaportal.cpp — THE REFUSAL WAS TAKEN (plan_entity.md step 4g),
//    AND THE RECORD HAS SINCE LEFT NATIVE CODE (plan_server_native_v2 T4d).
//
// The account below is kept as the record of why the native record looked as
// it did. Its facts were retired with shell_areaportal.cpp and
// areaportal_list.h: the records and the walk are the managed AreaPortals now,
// and AreaPortalsTests holds what these facts held (the backface arm leaves
// the client's flag alone, the 80-unit padding, the 10-unit fade buffer, a
// token never reused).
//
// THIS IS A RE-TAKE, WHICH IS WHAT THIS FILE'S OWN STANDARD DEMANDS: "A RED
// HERE MEANS RE-TAKE THE VERDICT, NOT 'REGRESSION'". The refusal read:
//
//     "The walk (gameinterface.cpp) touches exactly TWO things on each element:
//      m_portalNumber and UpdateVisibility(). Nothing about CBaseEntity. So the
//      un-derivation is cheap — retype g_AreaPortals to an interface carrying
//      those two — but it is a change to func_areaportalbase.h, an ABOVE-CLIFF
//      SDK OBJECT (84 refs of its own) owned by a step-3 lane. This lane is
//      fenced out of it."
//
// THE GROUND WAS THE FENCE, AND THE FENCE IS GONE — measured, not assumed.
// func_areaportalbase.cpp, func_areaportal.cpp and func_areaportalwindow.cpp
// are none of them in server_csharp.vpc, so func_areaportalbase.h had exactly
// two compiled readers left and both were shim files: shell_areaportal.cpp and
// shell_gameclients.cpp. plan_entity.md names shell_areaportal.cpp in step 4g
// with no fence around it. The header is not edited at all in the end: the list
// is RETYPED SOMEWHERE ELSE, in the shim's own areaportal_list.h, and the two
// shim readers point at that instead. func_areaportalbase.h keeps its long
// bIsOpenOnClient illustration and is simply no longer reached.
//
// The second half of the refusal was that our class "cannot collapse into its
// base the way C_SourceSharpRopeAnchor collapsed into C_BaseEntity: it
// OVERRIDES UpdateVisibility". CORRECT, AND NOT THE SHAPE THE TAKING USED.
// There is no base to collapse into now and no override to keep: stock had two
// portal classes (a door and a window) and dispatched between them through a
// vtable, and the managed side had already made that one object with a flag —
// FuncAreaPortalWindow.cs calls AreaPortalCreate and then AreaPortalSetWindow
// on the same portal. Two shapes of one record, so one record, so one
// non-virtual UpdateVisibility with the door test, the window test and stock's
// backface arm inlined in that order.
//
// WHAT WENT WITH THE BASE CLASS. CBaseEntity's network property, collision
// property, datadesc, think list, touch links and save/restore, on an object
// that used none of them; LINK_ENTITY_TO_CLASS and the CreateEntityByName that
// called it; the EFL_SERVER_ONLY / UpdateTransmitState pair whose only job was
// to refuse the edict the base class would have taken; and one dynamic_cast
// (§3.16, server shim), replaced by an index-and-serial token so that a
// recycled list slot cannot answer a stale handle.
//
// WHAT IS ASSERTED BELOW IS NOW THE TAKING rather than the refusal, the same
// treatment this file already gave verdict 2 and its deleted third.
//
//-----------------------------------------------------------------------------
// 2. world_adapter.cpp — THE REFUSAL WAS TAKEN (plan_cworld_trace.md T3).
//
// THIS IS A RE-TAKE, WHICH IS WHAT THIS FILE'S OWN STANDARD DEMANDS: "A RED
// HERE MEANS RE-TAKE THE VERDICT, NOT 'REGRESSION'". The owner's instruction
// forced it:
//
//     "CWorld should not inherit CStandaloneEntity either — it should be its
//      own standalone class. A trace should be able to return a pointer to
//      CWorld, or CStandaloneEntity, or CBaseEntity."
//
// The verdict rested on four grounds. THREE DISSOLVED AND ONE WAS WRONG — and
// the one that was wrong is the one the plan said was already dissolved.
//
//   GROUND 1 — EDICT 0, "the single hardest blocker".
//       DISSOLVED, AND IT IS FOUR LINES. The reasoning was exact for the shape
//       it compared: NetworkProp()->AttachEdict is CBaseEntity's, and
//       CStandaloneEntity::PostConstruct calls engine->CreateEdict(), so that
//       hierarchy "can take AN edict but has no route to THE one the engine
//       assumes". What dissolves it is that the world is on NEITHER hierarchy:
//       it does not need a route through either, it NAMES the edict. INDEXENT(
//       0 ) is a slot the engine has already allocated, and AttachEdict's whole
//       content is four assignments — SetEdict, m_EdictIndex, m_pNetworkable,
//       and the entity list. CWorld::AttachToEdictZero is those four.
//
//   GROUND 2 — DT_WORLD, paired with a client half that is itself an entity.
//       STOOD, AND THE PLAN SAID IT WAS ALREADY DISSOLVED. plan_cworld_trace.md
//       records it as "DISSOLVED — the CWorld shim moved the ServerClass to
//       managed registration; neither half declares a table any more".
//       Measured, THAT IS FALSE, and it is the most dangerous of the four.
//       Neither half declares a table with a MACRO; both halves BUILD one, and
//       prop 0 of each was the base table BY POINTER —
//
//           SendPropDataTable( "baseclass", 0, CBaseEntity::m_pClassSendTable )
//           RecvPropDataTable( "baseclass", 0, 0, C_BaseEntity::m_pClassRecvTable )
//
//       DT_BaseEntity's props carry CBaseEntity's MEMBER OFFSETS, and the
//       engine encodes a table against edict->GetUnknown(), which is the CWorld
//       object. Off the hierarchy, most of those props read PAST THE END of a
//       much smaller object and put the result on the wire. The absence of a
//       macro is not the absence of a table.
//
//       It dissolved by being DONE: both halves lost prop 0, in one commit,
//       which is exactly what "both halves would have to move together" asked
//       for. They could, because both are this port's own files built from one
//       managed declaration — c_world.o being above the cliff stopped being the
//       obstacle when the table stopped being a macro in it. And the client
//       loses nothing measurable: C_BaseEntity::PostDataUpdate already forces
//       model index 1, SOLID_BSP, the origin and the angles for entity 0, under
//       stock's own comment "If it's the world, force solid flags".
//
//   GROUND 3 — the NULL-subject class: "un-deriving CWorld collapses 'hit the
//       world' into 'hit nothing' at every one of those sites — silently, and
//       only for geometry."
//       ENTIRELY CORRECT, AND THE WHOLE OF T1 AND T2. The census found 44
//       server sites rather than the thirteen this text named, because the
//       population is TraceEntity( tr ) — EntityFromEntityHandle( tr.m_pEnt )
//       behind an inline — and not one of its call sites names m_pEnt. It
//       dissolved by a THIRD ARM on the partition (world_subject.h) rather than
//       by argument: SourceSharp_IsTheWorld, an identity compare against a
//       singleton, asked AHEAD of the two hierarchy arms in every resolver —
//       MovementGroundFromTrace, AnyEntityOf, SourceSharp_ResolveEntity,
//       PhysicsOwnerOf, UTIL_ImpactTrace and the classname walk.
//
//   GROUND 4 — "GetWorldEntity() returns CWorld * to eighteen files".
//       WRONG WHEN MEASURED, and the plan caught this one: FIVE compiled
//       consumers, not eighteen. Of the five, two needed a decision
//       (PhysCreateWorld's game data, and the object-stress fallback) and three
//       did not.
//
// AGAINST "THAT IS MORE C++ THAN IT REMOVES": what went in is about 130 lines
// of interface bodies, most of them one-line constants, plus four resolver
// arms. What went out is a base class carrying a network property, a collision
// property, a datadesc, think lists, touch links and save/restore — for an
// object that used none of them.
//
// WHAT IS ASSERTED BELOW IS NOW THE TAKING rather than the refusal, the same
// treatment this file already gave its third verdict when shell_netentity was
// deleted. Each fact is one a later lane could undo by accident.
//
//-----------------------------------------------------------------------------
// WHAT THIS FILE IS FOR. Every blocker above is asserted below, so that the day
// one is fixed this goes RED naming this file, rather than staying a paragraph
// nobody re-reads.
//
// THE STRIPPER IS LOAD-BEARING: the prose above spells the very strings the
// assertions look for, and an unstripped scan would read the explanation as the
// fact. Same rule as effects_evicted_test.cpp.
//
// ANCHORS: every file is identified before anything is asserted absent or
// present about it, so a renamed file is a red test and not a vacuous pass.
//
//=============================================================================//

#include <string>
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

// THE ANCHOR IS THE CLASS HEAD, NOT THE CLASS NAME. "class CWorld" became a
// PREFIX the day world.h gained CWorldCollideable and CWorldNetworkable above
// it, and a prefix anchor is satisfied by a file that no longer declares the
// class this verdict is about.
const File_t kWorldHeader = { "sourcesharp/game/server/ss_world.h",
	"class CWorld : public IServerEntity" };

// worldspawn's table, since it stopped being a macro in world_adapter.cpp
// (plan_gamedll_after.md Part B2). Anchored on the function that answers the
// engine, so a file rewritten around this test fails to anchor rather than
// reading as "the table is gone".
const File_t kWorldNetTable = { "sourcesharp/game/server/shell_world_nettable.cpp",
                                "SourceSharp_WorldServerClass" };
const File_t kWorldAdapter = { "sourcesharp/game/server/world_adapter.cpp",
	"CWorld* GetWorldEntity()" };
const File_t kClientWorld = { "game/client/c_world.h", "class C_World" };
const File_t kStandaloneHeader = { "sourcesharp/game/server/shell_standalone.h",
	"class CStandaloneEntity" };

// The third arm of the partition, and the client half of the world's table.
// Both are T3's, and both are things a later lane could undo without noticing
// what they were for.
const File_t kWorldSubject = { "sourcesharp/game/shared/world_subject.h",
	"SourceSharp_IsTheWorldHandle" };
const File_t kClientWorldTable = { "sourcesharp/game/client/shell_world_nettable_client.cpp",
	"SourceSharp_WorldNetDeclare" };

std::string ReadAnchored( const File_t &file )
{
	const std::string body = ReadStripped( file.pszRel );

	EXPECT_FALSE( body.empty() )
		<< file.pszRel << " could not be read; every fact this verdict rests on "
		   "would otherwise pass vacuously.";

	if ( !body.empty() )
	{
		EXPECT_NE( body.find( file.pszAnchor ), std::string::npos )
			<< file.pszRel << " no longer contains its anchor \""
			<< file.pszAnchor << "\" — RE-TAKE THE VERDICT.";
	}
	return body;
}

} // namespace

//=============================================================================//
// VERDICT 1 — TAKEN, AND ITS SUBJECT HAS LEFT NATIVE CODE. The area portal
// record, its list and the per-client walk are the managed AreaPortals since
// plan_server_native_v2 T4d; the five facts that stood here went with
// shell_areaportal.cpp and areaportal_list.h (see the header comment), and
// AreaPortalsTests holds the behaviour they guarded.
//=============================================================================//

//=============================================================================//
// VERDICT 2 — TAKEN. world_adapter.cpp no longer derives.
//
// The four grounds are gone; what is asserted here is THE SHAPE THAT REPLACED
// THEM, so that a later lane cannot quietly put any of them back. Each fact
// below would be a real regression rather than a verdict to re-take.
//=============================================================================//
TEST( ShimUnderiveVerdict, TheWorldIsItsOwnHierarchy )
{
	const std::string worldH  = NoWhitespace( ReadAnchored( kWorldHeader ) );
	const std::string adapter = NoWhitespace( ReadAnchored( kWorldAdapter ) );

	// (a) IT IS NOT A CBaseEntity, AND NOT A CStandaloneEntity EITHER -- the
	//     owner's instruction has both halves in it, and asserting only the
	//     first would be satisfied by the easier change the instruction
	//     explicitly refuses.
	EXPECT_EQ( worldH.find( "classCWorld:publicCBaseEntity" ), std::string::npos )
		<< "CWorld derives from CBaseEntity again. That is the un-derive undone, "
		   "not a verdict to re-take.";
	EXPECT_EQ( worldH.find( "classCWorld:publicCStandaloneEntity" ), std::string::npos )
		<< "CWorld has been put on the parallel hierarchy. The owner's "
		   "instruction refuses that specifically: it would buy a base class the "
		   "world does not want in order to reuse an allocator.";

	// And what it IS, so the two absences above are a measurement rather than a
	// file that has stopped declaring the class at all.
	//
	// ONE BASE, AND THAT IS A NARROWING RATHER THAN A RETREAT. This read
	// "publicIServerEntity,publicIServerNetworkable,publicICollideable" until
	// the owner's follow-up to T3 asked whether the world needs to BE all
	// three. It does not: the engine reaches two of them BY POINTER --
	// IServerUnknown::GetCollideable() and the edict's assigned
	// m_pNetworkable -- and only the IServerEntity chain has to be this
	// object's own vtable, because that is the type trace_t::m_pEnt holds.
	// So the other two are HELD, and the next two assertions are what stops
	// this one from reading as "the world stopped answering the engine".
	EXPECT_NE( worldH.find( "classCWorld:publicIServerEntity" ), std::string::npos )
		<< "CWorld no longer implements the engine's entity chain directly. "
		   "The two EXPECT_EQs above would then be passing vacuously.";

	EXPECT_EQ( worldH.find( "classCWorld:publicIServerEntity,publicIServerNetworkable" ),
	           std::string::npos )
		<< "CWorld inherits IServerNetworkable again. It is held as a member "
		   "(m_Networkable) precisely because the engine only ever asks for the "
		   "POINTER -- edict_t::m_pNetworkable is assigned, never derived.";

	// THE TWO INTERFACES, STILL ANSWERED -- through members, which is the whole
	// of the change. Without these the EXPECT_EQ above would be satisfied by a
	// world that had simply stopped being networkable or collideable at all.
	EXPECT_NE( worldH.find( "CWorldCollideablem_Collideable;" ), std::string::npos )
		<< "the world no longer HOLDS an ICollideable. edict_t::GetCollideable() "
		   "is GetIServerEntity()->GetCollideable(), so something has to be "
		   "there -- and the engine's trace path calls GetEntityHandle() on it "
		   "tens of thousands of times a session.";
	EXPECT_NE( worldH.find( "CWorldNetworkablem_Networkable;" ), std::string::npos )
		<< "the world no longer HOLDS an IServerNetworkable. That is what "
		   "edict_t::m_pNetworkable points at, and CBaseEdict::AreaNum reads it "
		   "with no null check.";
	EXPECT_NE( worldH.find( "GetCollideable()OVERRIDE{return&m_Collideable;}" ), std::string::npos )
		<< "IServerUnknown::GetCollideable no longer hands back the member.";
	EXPECT_NE( worldH.find( "GetNetworkable()OVERRIDE{return&m_Networkable;}" ), std::string::npos )
		<< "IServerUnknown::GetNetworkable no longer hands back the member.";

	// (b) EDICT 0, TAKEN DIRECTLY rather than through a network property.
	//     Ground 1, re-pointed rather than dropped: the day the world stops
	//     claiming edict 0 is the day the engine stops finding it, whichever
	//     route it used to get there.
	EXPECT_EQ( adapter.find( "NetworkProp()->AttachEdict(" ), std::string::npos )
		<< "the world attaches its edict through NetworkProp() again, which is "
		   "CBaseEntity's CServerNetworkProperty.";
	EXPECT_NE( adapter.find( "m_pEdict=ss::INDEXENT(RequiredEdictIndex())" ), std::string::npos )
		<< "the world no longer names edict 0 for itself.";

	// THE LINE THAT COSTS AN AFTERNOON WHEN IT IS MISSING. CBaseEdict::AreaNum
	// and GetClassName read m_pNetworkable UNGUARDED, so without this the world
	// spawns, answers traces perfectly well, and then jumps to address zero deep
	// in the engine the moment anything is sent.
	// CStandaloneEntity::PostConstruct records the same line and the same
	// afternoon.
	//
	// IT IS `&m_Networkable` AND NOT `this` NOW, and the fact is the same fact:
	// what this field holds must be a live IServerNetworkable for as long as
	// edict 0 names this world. A member of the world is exactly that, and
	// ~CWorld nulls the field before the member goes.
	EXPECT_NE( adapter.find( "m_pEdict->m_pNetworkable=&m_Networkable" ), std::string::npos )
		<< "the world no longer sets its edict's SECOND pointer. The engine "
		   "dereferences it without checking on the first send.";

	// (c) THE SERVER CLASS IS STILL BUILT AND STILL CALLED CWorld / DT_WORLD.
	//     The engine pairs the two sides by name, so this is the handshake
	//     rather than decoration.
	const std::string worldTable = NoWhitespace( ReadAnchored( kWorldNetTable ) );
	EXPECT_NE( worldTable.find( "newServerClass(\"CWorld\"" ), std::string::npos )
		<< "nothing builds a ServerClass called CWorld any more.";
	EXPECT_NE( worldTable.find( "\"DT_WORLD\"" ), std::string::npos )
		<< "the world's send table is no longer called DT_WORLD. The engine "
		   "pairs the two sides by name.";

	// The macro really is still gone: a build with BOTH would have two
	// registrations for one class, and two InterfaceReg-shaped registrations do
	// not collide at link time -- static-initialisation order picks one,
	// silently.
	EXPECT_EQ( adapter.find( "IMPLEMENT_SERVERCLASS_ST(CWorld,DT_WORLD)" ), std::string::npos )
		<< "the SDK macro is back in world_adapter.cpp beside the managed "
		   "registration.";
}

//=============================================================================//
// GROUND 2 WAS NOT DISSOLVED BY THE MACRO GOING. It was dissolved by BOTH
// HALVES LOSING PROP 0, together, and this is what keeps them together.
//
// The base table was a POINTER to CBaseEntity's / C_BaseEntity's own tables,
// whose props carry that class's member offsets. Re-adding either one puts a
// much larger class's offsets on a much smaller object -- most of them past the
// end of it -- and sends the result to every client. It would look like a
// rendering bug.
//=============================================================================//
TEST( ShimUnderiveVerdict, NeitherHalfOfTheWorldsTableInheritsTheEntityTable )
{
	const std::string serverTable = NoWhitespace( ReadAnchored( kWorldNetTable ) );
	const std::string clientTable = NoWhitespace( ReadAnchored( kClientWorldTable ) );

	EXPECT_EQ( serverTable.find( "SendPropDataTable(\"baseclass\"" ), std::string::npos )
		<< "the world's SendTable inherits DT_BaseEntity again. Its props carry "
		   "CBaseEntity's member offsets and the engine encodes them against the "
		   "CWorld object, which is not one and is much smaller.";

	EXPECT_EQ( clientTable.find( "RecvPropDataTable(\"baseclass\"" ), std::string::npos )
		<< "the world's RecvTable inherits DT_BaseEntity again. The two halves "
		   "move together or the update does not pair at all.";
}

//=============================================================================//
// THE THIRD ARM EXISTS, AND THE CLIENT HALF DID NOT MOVE.
//
// Ground 3 -- "un-deriving CWorld collapses 'hit the world' into 'hit nothing'"
// -- was the correct one, and it is closed by a resolver arm rather than by an
// argument. If the arm goes, the ground comes back: silently, and only for
// level geometry.
//=============================================================================//
TEST( ShimUnderiveVerdict, TheWorldStillAnswersATrace )
{
	const std::string subject = NoWhitespace( ReadAnchored( kWorldSubject ) );

	EXPECT_NE( subject.find( "boolSourceSharp_IsTheWorld(" ), std::string::npos )
		<< "the third arm of the partition is no longer declared. Every resolver "
		   "in the game -- TraceEntity, AnyEntityOf, MovementGroundFromTrace, "
		   "SourceSharp_ResolveEntity, PhysicsOwnerOf -- then answers NULL for "
		   "the world.";

	// THE CLIENT HALF, RE-TAKEN IN STEP 5e — and this clause used to say the
	// opposite.
	//
	// It read: EXPECT_NE( ..."classC_World:publicC_BaseEntity"... ) << "the
	// client world has ALSO stopped being an entity. That is separate work with
	// its own grounds -- RE-TAKE rather than assume this lane covered it." That
	// was the right guard for the lane that wrote it: the verdict above is
	// about the SERVER, and a client that had quietly followed would have been
	// a change nobody had grounds for.
	//
	// 5e is the lane with the grounds. plan_entity.md §3.11, as amended, says
	// both sides come off the SDK hierarchy and neither lands on the parallel
	// one — so what is asserted here now is THE SAME THREE FACTS the server's
	// verdict asserts, on the client's file. Not "it changed": the two absences
	// plus what replaced them, so that a later lane cannot put either back and
	// cannot satisfy this by deleting the class.
	const std::string clientW = NoWhitespace( ReadAnchored( kClientWorld ) );

	EXPECT_EQ( clientW.find( "classC_World:publicC_BaseEntity" ), std::string::npos )
		<< "C_World derives from C_BaseEntity again. That is 5e's un-derive "
		   "undone, not a verdict to re-take.";
	EXPECT_EQ( clientW.find( "classC_World:publicC_StandaloneEntity" ), std::string::npos )
		<< "C_World has been put on the parallel hierarchy. The owner's "
		   "instruction refuses that on this side for the same reason it refuses "
		   "it on the server's.";

	// AND WHAT IT IS, so the two absences above are a measurement rather than a
	// file that has stopped declaring the class at all. IClientEntity and not
	// the server's narrow chain, because the engine reaches entity 0 as an
	// IClientEntity * and dereferences it with no test (see c_world.h).
	EXPECT_NE( clientW.find( "classC_World:publicIClientEntity" ), std::string::npos )
		<< "C_World no longer implements the engine's client entity chain "
		   "directly. The two EXPECT_EQs above would then be passing vacuously.";

	// THE COLLIDEABLE, STILL ANSWERED — through a member, exactly as the
	// server's is. Without this the absences above would be satisfied by a
	// world that had simply stopped being collideable, and the engine's
	// client-side trace dereferences what it gets.
	EXPECT_NE( clientW.find( "CWorldClientCollideablem_Collideable;" ), std::string::npos )
		<< "the client's world no longer HOLDS an ICollideable. "
		   "entitylist->GetClientEntity( 0 )->GetCollideable() is every world "
		   "clip on that side and the answer is dereferenced with no test.";

	// (d) The NULL-subject contract the parallel hierarchy still keeps: it
	//     answers NULL for "give me a CBaseEntity", which is what makes the
	//     three hierarchies tellable apart at all.
	const std::string standalone = ReadAnchored( kStandaloneHeader );
	EXPECT_NE( standalone.find( "GetBaseEntity" ), std::string::npos )
		<< "CStandaloneEntity no longer answers GetBaseEntity at all — the "
		   "NULL-subject contract the three hierarchies are told apart by has "
		   "moved. RE-TAKE.";
}
