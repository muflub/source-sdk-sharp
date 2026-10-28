//========= SourceSharp ======================================================//
//
// THE CLIENT PVS-NOTIFY PATH, AND WHY GUARDING ITS TWO WALKS IS SOUND.
// Lane `pvs-ragdoll`, closing the defect lane `f-list` left open.
//
// WHAT WENT WRONG BEFORE, AND IT HAS NOW GONE WRONG THREE TIMES ON THIS PORT.
// `f-list`'s refusal note argued the client's PVS-notifier list was "empty by
// construction" and quoted `C_BaseAnimating`'s header inline
// `GetPVSNotifyInterface() { return this; }` IN SUPPORT of that. It is the
// opposite of support: that inline belongs to `C_ClientRagdoll`, which IS an
// `IPVSNotify`, and whose `OnPVSStatusChanged` calls `CreateShadow()` /
// `DestroyShadow()`. The lane had read a STATIC REACHABILITY argument as a
// LIFETIME argument -- the same error that, one paragraph earlier in the same
// handover, had already cost it five crashed live-gate runs.
//
// SO THE ARGUMENT IS NOT MADE AGAIN HERE. It was MEASURED, on a live
// dm_lockdown, two passes in one process, with a positive control that built a
// real `C_ClientRagdoll` and watched the whole path fire. The measurement is
// written up in `.claude/entity-port/status/pvs-ragdoll.md`; the headline is:
//
//     AddPVSNotifier entered 371 times in a level and added 0 rows.
//     0 C_ClientRagdoll were constructed by the game.
//     The ONE that existed was the probe's own, and it worked perfectly --
//     3 x CreateShadow, 3 x DestroyShadow, over 79,626 walked rows.
//
// The path is LIVE MACHINERY WITH NO OCCUPANT. What these facts pin is the two
// properties that make that true, so that neither can be undone silently:
//
//   (a) exactly ONE `GetPVSNotifyInterface` in the whole tree answers non-NULL,
//       and it is `C_ClientRagdoll`'s, in game/client/c_baseanimating.h;
//   (b) both that file's TU and the ONLY thing that can put a row in the list
//       (`CClientEntityList::AddPVSNotifier`, game/client/cliententitylist.cpp)
//       are in the flip's own removal seed, so the guard and the deletion are
//       one commit and cannot come apart.
//
// AND (c), which is the half `f-list` could not have known without measuring:
// a `C_ClientRagdoll`'s shadow does NOT depend on the notify for its lifetime.
// `InitAsClientRagdoll` creates it and `Release()` -> `RemoveFromLeafSystem()`
// destroys it; the notify only TOGGLES it while the body is out of the PVS. So
// guarding the walks out cannot leak a shadow into the next level -- which is
// precisely the crash class `f-list` and `shell-lifetime` chased, and this is
// the reason it does not apply here.
//
// (a) IS A SWEEP AND NOT A SPOT-CHECK, on purpose. A spot-check on the two
// implementations we know about is a check that cannot fail for the case that
// matters: somebody ADDING a third. The classifier it sweeps with is itself
// positive-controlled below, because an empty-list assertion over a classifier
// that always answers "NULL" is not a check either.
//
// HEADERS ARE SWEPT TOO. `f-list`'s other structural finding was that
// `ClientEntityResolve.TheOnlyClientTusLeftNamingTheListAreTheWaivedOnes`
// walked the vpc's `$File` rows (a build source list, which CMake's is too) and
// so cannot see a header at all -- and the one non-NULL implementation in this
// tree IS A HEADER INLINE. A source-list-driven
// sweep would have reported this file's subject as absent.
//
//===========================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <dirent.h>

#include <algorithm>
#include <cstring>
#include <fstream>
#include <iterator>
#include <sstream>
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
// PROSE IS NOT CODE, and a raw scan cannot tell them apart. COMMON.md records
// this as a standing hazard -- tools/entity-gates and tools/entity-census both
// strip comments for it -- and it FIRED ON THIS FILE'S FIRST RUN: the sweep
// below reported shell_clientdll.cpp as a second non-NULL implementation,
// because the banner ABOVE the two walks discusses `GetPVSNotifyInterface()`
// in English and the classifier read the next `{ ... }` in the prose as its
// body. That is a finding-shaped false positive about the very file the ruling
// is about.
//
// Line comments, block comments, and the contents of string literals (so that
// a `"//"` inside one does not swallow the rest of the line). Character
// literals are left alone; nothing in these files puts a quote in one.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &strIn )
{
	std::string strOut;
	strOut.reserve( strIn.size() );

	enum
	{
		kCode,
		kLine,
		kBlock,
		kString
	} state = kCode;

	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		const char c = strIn[i];
		const char n = ( i + 1 < strIn.size() ) ? strIn[i + 1] : '\0';

		switch ( state )
		{
			case kCode:
				if ( c == '/' && n == '/' )
				{
					state = kLine;
					i++;
				}
				else if ( c == '/' && n == '*' )
				{
					state = kBlock;
					i++;
				}
				else if ( c == '"' )
				{
					state = kString;
					strOut += c;
				}
				else
				{
					strOut += c;
				}
				break;

			case kLine:
				if ( c == '\n' )
				{
					state = kCode;
					strOut += c;
				}
				break;

			case kBlock:
				if ( c == '*' && n == '/' )
				{
					state = kCode;
					i++;
				}
				else if ( c == '\n' )
				{
					strOut += c;
				} // keep the line count honest
				break;

			case kString:
				if ( c == '\\' )
				{
					i++;
				} // skip the escaped byte
				else if ( c == '"' )
				{
					state = kCode;
					strOut += c;
				}
				break;
		}
	}

	return strOut;
}

//-----------------------------------------------------------------------------
// THE CLASSIFIER. Given one file's text, return the bodies of every
// `GetPVSNotifyInterface` DEFINITION in it that does NOT answer NULL.
//
// A declaration (`virtual IPVSNotify* GetPVSNotifyInterface();`, which is what
// game/client/c_baseentity.h:336 carries) reaches a `;` before it reaches a
// `{`, and is skipped -- a declaration says nothing about what is returned.
//
// Every body in this tree is a single `return <x>;`, so "up to the next `}`"
// is the whole body. A future body with a nested brace would be truncated;
// that would make this fact MORE likely to fail, never less, which is the
// right way round for a guard.
//-----------------------------------------------------------------------------
std::vector<std::string> NonNullNotifyBodies( const std::string &strRaw )
{
	const std::string strText = StripComments( strRaw );

	std::vector<std::string> vecOut;
	const std::string strName = "GetPVSNotifyInterface";

	for ( size_t at = strText.find( strName ); at != std::string::npos;
		  at = strText.find( strName, at + strName.size() ) )
	{
		const size_t open = strText.find( '{', at );
		const size_t semi = strText.find( ';', at );

		if ( open == std::string::npos )
			continue; // nothing left to read
		if ( semi != std::string::npos && semi < open )
			continue; // a declaration, not a definition

		const size_t close = strText.find( '}', open );
		if ( close == std::string::npos )
			continue;

		const std::string strBody = strText.substr( open, close - open + 1 );
		if ( strBody.find( "return NULL" ) != std::string::npos )
			continue;
		if ( strBody.find( "return 0" ) != std::string::npos )
			continue;

		vecOut.push_back( strBody );
	}

	return vecOut;
}

//-----------------------------------------------------------------------------
// The sweep. Every .cpp and .h under the three source roots the client is
// built from. `thirdparty` is not ours. (VPC's `_vpc_` build-output directory
// was skipped here too; the CMake build writes outside the source roots.)
//-----------------------------------------------------------------------------
// FlipClosureSource / FlipRemovalSeed STOOD HERE. They read
// tools/client-flip-closure's DEFAULT_SEED, which is deleted -- one of seven
// removal-set instruments retired in `the-close 11/N` because what they forecast
// has landed. TheRagdollsTuAndTheListsTuAreBothInTheFlipsRemovalSeed measures
// the client's source list directly instead (client_csharp.vpc then,
// client_sourcesharp in src/client/native/CMakeLists.txt now), which is the
// thing the seed forecast.

} // namespace

//=============================================================================
// 1. THE CLASSIFIER'S OWN POSITIVE CONTROL.
//
// Facts 2 and 3 below are "the sweep found exactly one" and "the sweep found
// none outside these files". Both are assertions ABOUT A LIST, and a
// classifier that answered "NULL" to everything would make both pass forever
// while seeing nothing. Ten instruments lied on this wave; this one says out
// loud what it can tell apart.
//=============================================================================

TEST( PvsNotifyClassifier, AReturnThisBodyIsFound )
{
	const std::string strFake = "virtual IPVSNotify* GetPVSNotifyInterface() { return this; }\n";

	const std::vector<std::string> vec = NonNullNotifyBodies( strFake );
	ASSERT_EQ( 1u, vec.size() );
	EXPECT_NE( std::string::npos, vec[0].find( "return this" ) );
}

TEST( PvsNotifyClassifier, AReturnNullBodyIsNotFound )
{
	const std::string strFake = "IPVSNotify* C_BaseEntity::GetPVSNotifyInterface()\n{\n\treturn NULL;\n}\n";

	EXPECT_TRUE( NonNullNotifyBodies( strFake ).empty() );
}

TEST( PvsNotifyClassifier, ABareDeclarationIsNotADefinition )
{
	// game/client/c_baseentity.h:336's shape. It says nothing about what is
	// returned, and counting it would put c_baseentity.h on the non-NULL list
	// and make fact 2's count wrong in the safe direction -- which reads as a
	// real finding.
	const std::string strFake = "	virtual IPVSNotify*				GetPVSNotifyInterface();\n"
								"	virtual void SomethingElse() { return; }\n";

	EXPECT_TRUE( NonNullNotifyBodies( strFake ).empty() );
}

TEST( PvsNotifyClassifier, ProseAboutTheInterfaceIsNotAnImplementation )
{
	// THIS IS THE ONE THAT FIRED FOR REAL. shell_clientdll.cpp's banner above
	// the walks says, in English, "whose GetPVSNotifyInterface() answers
	// non-NULL", and a few lines further down the comment block contains a
	// brace. Before the stripper, the sweep named that file as a second
	// non-NULL implementation -- a false positive about the very file whose
	// guard this whole ruling defends.
	const std::string strFake = "// ... whose GetPVSNotifyInterface() answers non-NULL, and there are\n"
								"// exactly four implementations { like this one } in the client.\n";

	EXPECT_TRUE( NonNullNotifyBodies( strFake ).empty() );
}

TEST( PvsNotifyClassifier, ABlockCommentedOutImplementationIsNotAnImplementation )
{
	const std::string strFake = "/* virtual IPVSNotify* GetPVSNotifyInterface() { return this; } */\n";

	EXPECT_TRUE( NonNullNotifyBodies( strFake ).empty() );
}

TEST( PvsNotifyClassifier, ARealImplementationBesideProseIsStillFound )
{
	// The other half, and the half that keeps the stripper from being a way to
	// make the sweep answer "nothing" forever: prose about the interface next
	// to a real body must still yield the body.
	const std::string strFake = "// GetPVSNotifyInterface() is discussed here { with a brace }\n"
								"class C_Thing { virtual IPVSNotify* GetPVSNotifyInterface() { return this; } };\n";

	const std::vector<std::string> vec = NonNullNotifyBodies( strFake );
	ASSERT_EQ( 1u, vec.size() );
	EXPECT_NE( std::string::npos, vec[0].find( "return this" ) );
}

TEST( PvsNotifyClassifier, AStringLiteralDoesNotSwallowTheRestOfTheLine )
{
	// A `"//"` inside a literal must not put the stripper into line-comment
	// state; if it did, the stripper would eat real code and the sweep would
	// quietly answer "nothing".
	const std::string strFake =
		"const char *p = \"http://example\"; IPVSNotify* GetPVSNotifyInterface() { return this; }\n";

	EXPECT_EQ( 1u, NonNullNotifyBodies( strFake ).size() );
}

//=============================================================================
// 2. EXACTLY ONE IMPLEMENTATION IN THE TREE ANSWERS NON-NULL.
//=============================================================================

//=============================================================================
// 3. THE GUARD AND THE DELETION ARE ONE COMMIT.
//=============================================================================

// READ THE ASSERTIONS, NOT THE NAME. There is no removal SEED to read any more,
// and there is no longer anything to predict.
//
// This fact used to read tools/client-flip-closure's DEFAULT_SEED -- this
// tree's written-down statement of what the flip would remove -- and check that
// both TUs were named in it. That tool is one of seven removal-set instruments
// deleted in `the-close 11/N`, because what they predicted has happened: both
// files have LEFT client_csharp.vpc. So the claim is measured against what the
// client compiles -- client_sourcesharp, read through cmake_graph.h
// (compile_commands.json cross-checked against src/client/native/CMakeLists.txt)
// -- which is the thing the seed was a forecast of.
TEST( PvsNotifyRuling, TheRagdollsTuAndTheListsTuAreBothInTheFlipsRemovalSeed )
{
	// THE POSITIVE CONTROL FIRST, for the two absences below: a source that IS
	// compiled and whose name CONTAINS one of the needles. It is also the
	// proof-of-read. (The vpc reading also had a "more than 200 $File rows"
	// floor against a truncated file; it is retired because CompiledSources
	// throws unless compile_commands.json and the CMakeLists list agree
	// exactly, which a truncated read cannot.)
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_cliententitylist.cpp" ) )
		<< "the port's own list seam is not compiled into client_sourcesharp, so "
		   "the two absences below are not being measured the way they look.";

	// Whole farm-relative paths, so `cliententitylist.cpp` cannot be confused
	// with `shell_cliententitylist.cpp`, which IS compiled and must stay so.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/c_baseanimating.cpp" ) )
		<< "C_ClientRagdoll's TU is back in the client build. It is the ONLY "
		   "class in the tree whose GetPVSNotifyInterface() answers non-NULL, "
		   "so with it alive and the two walks emptied, the one thing that "
		   "wants PVS notifications is alive and nothing will ever notify it.";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/cliententitylist.cpp" ) )
		<< "AddPVSNotifier's TU is back in the client build.";
}

//=============================================================================
// 4. AND THE HALF THAT SAYS THIS IS NOT THE CRASH CLASS.
//
// `f-list` and `shell-lifetime` chased a shadow that outlived its level and
// was double-freed on the next one. A reader who knows that story will ask
// whether guarding the notify out leaks a C_ClientRagdoll's shadow the same
// way. It cannot, and these two facts are why: the shadow is created when the
// ragdoll is built and destroyed when it is released, on paths that have
// nothing to do with the PVS. The notify only TOGGLES it in between.
//=============================================================================

//=============================================================================
// 5. THE BANNER THAT CARRIES THE ARGUMENT SAYS THE MEASURED THING.
//
// The banner above the two walks is the only place a reader meets this
// reasoning. It was written from the SAME static reading `f-list` got wrong --
// it listed c_baseentity.cpp:1562 among the implementations that matter, and
// that one returns NULL -- so it is corrected, and the correction is pinned
// here rather than left to survive on goodwill.
//=============================================================================
