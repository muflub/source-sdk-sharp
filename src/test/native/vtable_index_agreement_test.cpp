//========= SourceSharp — the vtable table lengths, asked of the compiler =====//
//
// C# calls five engine interfaces by indexing an object's vtable, through the
// structs in sourcesharp/managed/SourceSharp.Abi/VtableLayouts.g.cs. That file
// is GENERATED from these same headers by tools/vtable-layout, and a generated
// file can go stale: somebody edits a header, or lands the generator's input
// without re-running it. Then field N of the struct is a different method from
// slot N of the engine's table, nothing faults, and the wrong function returns
// whatever it left in the return register.
//
// So there is ONE check and this file is its build-time half.
//
//   * shell_vtable_interfaces.cpp takes each table's length from the COMPILER,
//     `VTableHook::MemberOf( &Interface::LastMethod ).slot + 1`, and hands it to
//     managed beside the interface pointer;
//   * managed compares it to sizeof(TVtbl) / sizeof(void *) and refuses to bind
//     when they differ, naming both numbers;
//   * and the tests below hold the compiler's number against the COMMITTED
//     generated file, so a stale layout is red in `make check` rather than only
//     at run time on somebody's machine.
//
// THE COMMITTED FILE IS READ RATHER THAN REGENERATED, and that distinction is
// the whole value of these tests. A check that regenerates the layout from the
// headers and compares it to a count taken from the same headers derives BOTH
// SIDES FROM ONE INPUT: a shifted header shifts the layout too, the two agree,
// and the arm passes while being wrong. Parsing what is on disk is what makes
// this a second opinion.
//
//-----------------------------------------------------------------------------
// WHAT WAS HERE BEFORE. 1,219 lines and forty-odd tests, comparing five C#
// ledgers of hand-written slot numbers against `&Interface::Method` one row at a
// time, plus the `this` adjustment for IBaseFileSystem, plus the four-way
// resolution of cdll_int.h's two conditional declarations.
//
// The ledgers are gone -- the owner's ruling is that the engine's factory
// interfaces are stable and the version string is the contract, so there is
// nothing to re-derive -- and with them went every row-by-row arm. The `this`
// adjustment is gone because native now asks for "VBaseFileSystem011" by name
// and the module hands back the adjusted pointer. What is left is the length.
//
//=============================================================================//

//-----------------------------------------------------------------------------
// THE DEFINES MUST MATCH THE ENGINE'S, AND THEY GO ABOVE THE INCLUDES.
//
// cdll_int.h declares two of IVEngineClient013's virtuals inside preprocessor
// conditionals:
//
//     #if !defined( NO_VOICE )   GetVoiceTweakAPI   slot 53
//     #if defined( USE_SDL )     GetMouseDelta      slot 127
//
// Each one that is compiled OUT shifts every slot below it by one. The shipped
// engine has both IN: slot 138 answers 24, which is proto_version.h's
// PROTOCOL_VERSION, only when both are counted in.
//
// This translation unit compiles the same header, so it has to make the same
// choice. It did not: `src/buildcpptests`' engine-header line defines USE_SDL
// nowhere while the mod's own client build defines it ten times, which is
// exactly why the header walk once answered 128 where the compiler answered 127.
//
// USE_SDL is therefore defined HERE, above the include of cdll_int.h and not on
// a compile line, so the file that depends on the choice is the file that
// records it. NO_VOICE stays undefined, which is already the case.
//-----------------------------------------------------------------------------
#define USE_SDL 1

#include "gtest/gtest.h"

#include <type_traits>

#include "vstdlib/random.h"
#include "engine/ivmodelinfo.h"
#include "filesystem.h"
#include "cdll_int.h"
#include "eiface.h"

// THE REST OF THE RESOLVED SET. Every interface the client or the server asks a
// factory for now has a generated layout, and a layout is a CLAIM about a table
// length that nothing else in this tree can check.
#include "ivrenderview.h"
#include "engine/ivdebugoverlay.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "engine/ishadowmgr.h"
#include "engine/IEngineTrace.h"
#include "engine/IEngineSound.h"
#include "iefx.h"
#include "engine/ivmodelrender.h"
#include "ienginevgui.h"
#include "ispatialpartition.h"
#include "igameevents.h"
#include "networkstringtabledefs.h"
#include "inetchannelinfo.h"
#include "engine/IStaticPropMgr.h"
#include "inputsystem/iinputsystem.h"
#include "IGameUIFuncs.h"
#include "datacache/idatacache.h"
#include "scenefilecache/ISceneFileCache.h"
#include "icvar.h"
#include "vphysics_interface.h"
#include "vphysics/friction.h"
#include "vphysics/constraints.h"
#include "vphysics/player_controller.h"
#include "vphysics/collision_set.h"
#include "vcollide_parse.h"
#include "vcollide.h"
#include "ivoiceserver.h"
#include "engine/iserverplugin.h"
#include "steam/steam_api_common.h"
#include "steam/isteamuser.h"
#include "steam/isteamapps.h"
#include "steam/isteamfriends.h"

// THE SHIM LAYER'S HEADERS, which are in THIS tree rather than the SDK's -- and
// that is the whole reason their agreement is cheap to check. The engine
// headers above describe a prebuilt module compiled by somebody else; these
// describe a class compiled from this file's own include, in this build, by
// this compiler.
#include "../game/server/ishim_frame.h"
#include "../game/server/ishim_level.h"
#include "../game/shared/ishim_root.h"
#include "../game/shared/ishim_console.h"
#include "../game/shared/ishim_areas.h"
#include "../game/shared/ishim_areas_client.h"
#include "../game/shared/imanaged_runtime_client.h"
#include "../game/shared/imanaged_root.h"
#include "../game/client/particles_lib/include/ss_particles/iparticlelib.h"
#include "../game/shared/imanaged_areas.h"
#include "../game/shared/imanaged_areas_client.h"
#include "../game/shared/sourcesharp_gamesystem.h"   // IManagedRuntimeServer

#include "../game/shared/vtable_hook.h"

#include <ctype.h>
#include <stdio.h>
#include <string.h>
#include <cstdlib>
#include <string>

// The compile-time half, which needs no test to run and fires on the build.
// USE_SDL is defined immediately above, so this can only fail if somebody
// removes that line or the header stops honouring it.
#if !defined( USE_SDL )
#error "vtable_index_agreement_test.cpp: USE_SDL must be defined above cdll_int.h -- without it IVEngineClient013 is one slot short from 127 down."
#endif
#if defined( NO_VOICE )
#error "vtable_index_agreement_test.cpp: the shipped engine is built with voice IN, so NO_VOICE must not be defined -- with it IVEngineClient013 is one slot short from 53 down."
#endif

namespace
{

//-----------------------------------------------------------------------------
// THE GENERATED LAYOUT, found by walking up from wherever the binary was run --
// the same way map_entity_block_test.cpp finds its table, and for the same
// reason: a compiled-in copy of the answers would be a SECOND record of them.
//-----------------------------------------------------------------------------
const char *const kLayoutRelative =
	"sourcesharp/managed/SourceSharp.Abi/VtableLayouts.g.cs";

std::string ReadRelative( const char *relative )
{
	std::string prefix;

	for ( int i = 0; i < 8; i++ )
	{
		std::string candidate = prefix + relative;

		if ( FILE *f = fopen( candidate.c_str(), "rb" ) )
		{
			std::string text;
			char buffer[ 4096 ];
			size_t got;

			while ( ( got = fread( buffer, 1, sizeof( buffer ), f ) ) > 0 )
				text.append( buffer, got );

			fclose( f );
			return text;
		}

		prefix += "../";
	}

	return std::string();
}

std::string ReadLayout()
{
	return ReadRelative( kLayoutRelative );
}

//-----------------------------------------------------------------------------
// The NAME of the last field one generated struct declares.
//
// The struct's fields are in the header's own declaration order, so the last
// one names the interface's last member -- which is the name an
// `Acquire( &nEntries )` has to read its slot out of. Empty for a struct that
// is not there, so a struct the generator stopped emitting cannot read as
// agreement.
//-----------------------------------------------------------------------------
std::string LastFieldOf( const std::string &text, const char *structName )
{
	// " unsafe struct ": internal, or public for an implemented-by-managed layout.
	std::string needle = std::string( " unsafe struct " ) + structName + "\n{";
	size_t at = text.find( needle );

	if ( at == std::string::npos )
		return std::string();

	size_t end = text.find( "\n}", at );

	if ( end == std::string::npos )
		return std::string();

	std::string name;
	size_t line = at;

	while ( ( line = text.find( "\n    public ", line ) ) != std::string::npos && line < end )
	{
		// `    public delegate* unmanaged<...> Name;` -- the identifier is what
		// sits between the last space and the semicolon. A layout MSVC orders
		// differently writes `    public delegate* unmanaged<...> Name => ...;`,
		// and there it is the word before the arrow.
		size_t semi = text.find( ';', line );

		if ( semi == std::string::npos || semi > end )
			break;

		size_t arrow = text.find( " => ", line );

		if ( arrow != std::string::npos && arrow < semi )
		{
			size_t space = text.rfind( ' ', arrow - 1 );

			if ( space != std::string::npos && space > line )
				name = text.substr( space + 1, arrow - space - 1 );
		}
		else
		{
			size_t space = text.rfind( ' ', semi );

			if ( space != std::string::npos && space > line )
				name = text.substr( space + 1, semi - space - 1 );
		}

		line += 2;
	}

	return name;
}

//-----------------------------------------------------------------------------
// The member an `Acquire` reads its entry count out of.
//
// Finds `VTableHook::MemberOf( &IShimFoo::` in the file and answers the
// identifier after it. Empty when the shape is not there at all, which fails
// the comparison rather than passing it -- an Acquire that stopped reading a
// slot must not read as agreeing with whatever the layout says.
//-----------------------------------------------------------------------------
std::string AcquireMemberIn( const std::string &text, const char *prefix )
{
	size_t at = text.rfind( prefix );

	if ( at == std::string::npos )
		return std::string();

	at += strlen( prefix );
	size_t end = at;

	while ( end < text.size()
	     && ( isalnum( (unsigned char)text[ end ] ) || text[ end ] == '_' ) )
	{
		end++;
	}

	return text.substr( at, end - at );
}

//-----------------------------------------------------------------------------
// How many fields one generated struct declares.
//
// COUNTED FROM `public ` AT THE START OF A LINE INSIDE THE STRUCT BODY, which is
// what every field declaration is and what no comment line is -- the prose the
// generator carries onto a field is `    // ...`. Returns -1 for a struct that
// is not there at all, so "the generator stopped emitting it" cannot read as
// "it has no fields".
//-----------------------------------------------------------------------------
int FieldsIn( const std::string &text, const char *structName )
{
	// " unsafe struct ": internal, or public for an implemented-by-managed layout.
	std::string needle = std::string( " unsafe struct " ) + structName + "\n{";
	size_t at = text.find( needle );

	if ( at == std::string::npos )
		return -1;

	size_t end = text.find( "\n}", at );

	if ( end == std::string::npos )
		return -1;

	int fields = 0;
	size_t line = at;

	while ( ( line = text.find( "\n    public ", line ) ) != std::string::npos && line < end )
	{
		fields++;
		line += 2;
	}

	return fields;
}

//-----------------------------------------------------------------------------
// THE ABI THIS BINARY WAS COMPILED FOR picks the expected number.
//
// The engine's Windows binaries are MSVC's, and the Microsoft C++ ABI orders
// some tables differently: a virtual destructor takes ONE slot rather than the
// Itanium pair, and a class's overloads are grouped and laid out in REVERSE
// declaration order. This file is compiled by the same compiler as the shell
// it checks (MSVC or clang-cl on Windows, both of which follow the Microsoft
// ABI and define _MSC_VER), so `MemberOf( &I::M ).slot` answers in that ABI's
// numbering, and each expectation says what it is under both.
//-----------------------------------------------------------------------------
#if defined( _MSC_VER )
	#define SS_ABI( itanium, microsoft ) ( microsoft )
#else
	#define SS_ABI( itanium, microsoft ) ( itanium )
#endif

// [begin, end) of one generated struct's body, or false when it is not there.
bool StructBody( const std::string &text, const char *structName, size_t *pBegin, size_t *pEnd )
{
	const std::string needle = std::string( " unsafe struct " ) + structName + "\n{";
	const size_t at = text.find( needle );

	if ( at == std::string::npos )
		return false;

	const size_t end = text.find( "\n}", at );

	if ( end == std::string::npos )
		return false;

	*pBegin = at;
	*pEnd = end;
	return true;
}

//-----------------------------------------------------------------------------
// The table length the generated file claims for THIS binary's ABI: its span
// of slots, except where MSVC orders the table differently, which the struct
// then says outright as `internal const int MicrosoftEntries = N;`. -1 for a
// struct that is not there.
//-----------------------------------------------------------------------------
int EntriesIn( const std::string &text, const char *structName )
{
#if defined( _MSC_VER )
	size_t begin, end;

	if ( !StructBody( text, structName, &begin, &end ) )
		return -1;

	const char *const kMicrosoft = "internal const int MicrosoftEntries = ";
	const size_t at = text.find( kMicrosoft, begin );

	if ( at != std::string::npos && at < end )
		return atoi( text.c_str() + at + strlen( kMicrosoft ) );
#endif

	return FieldsIn( text, structName );
}

//-----------------------------------------------------------------------------
// The slot the generated file gives one named slot, for THIS binary's ABI; -1
// when the struct or the slot is not there.
//
// A slot MSVC numbers differently carries `[VtableSlot(itanium, microsoft)]`
// above its declaration; every other slot's Itanium number leads its trailing
// comment, and is its Microsoft number too.
//-----------------------------------------------------------------------------
int SlotIn( const std::string &text, const char *structName, const char *field )
{
	size_t begin, end;

	if ( !StructBody( text, structName, &begin, &end ) )
		return -1;

	size_t line = std::string::npos;

	for ( const std::string &shape : { std::string( " " ) + field + ";", std::string( " " ) + field + " => " } )
	{
		const size_t at = text.find( shape, begin );

		if ( at != std::string::npos && at < end )
		{
			line = text.rfind( '\n', at );
			break;
		}
	}

	if ( line == std::string::npos || line < begin )
		return -1;

	// Its own block: back to the blank line before it.
	size_t block = text.rfind( "\n\n", line );

	if ( block == std::string::npos || block < begin )
		block = begin;

	const char *const kSlot = "[VtableSlot(";
	const size_t attribute = text.find( kSlot, block );

	if ( attribute != std::string::npos && attribute < line )
	{
		const char *p = text.c_str() + attribute + strlen( kSlot );
		const int itanium = atoi( p );
		const char *comma = strchr( p, ',' );

		if ( !comma )
			return -1;

		const int microsoft = atoi( comma + 1 );
		(void)itanium;
		(void)microsoft;
		return SS_ABI( itanium, microsoft );
	}

	const size_t comment = text.find( "// ", text.find( ';', line ) );

	if ( comment == std::string::npos || comment > end )
		return -1;

	return atoi( text.c_str() + comment + 3 );
}

} // namespace


//=============================================================================
// ONE TEST PER INTERFACE: the compiler's own table length, against the number of
// fields the committed generated file declares.
//
// The compiler's answer comes from a pointer to the interface's LAST
// declaration. Under the Itanium ABI a pointer-to-member-function carries its
// slot, so this is not a count of anything -- and on IVModelInfo, whose virtual
// destructor occupies slots 0 and 1, those two are already inside the number.
//=============================================================================

//=============================================================================
// THE COUNT THE FACTORY REPORTS, AGAINST THE MEMBER THE LAYOUT ENDS WITH.
//
// ShimFrame_Acquire and ShimLevel_Acquire each answer their table length as
// `MemberOf( &IShimFoo::<a member> ).slot + 1`. That reads the slot out of the
// compiler and is exactly right -- FOR THE MEMBER IT NAMES. Name any member but
// the LAST one and the count is short, managed refuses the bind, and the whole
// area is dead.
//
// THE SUB-IOQUEUE LANE DID EXACTLY THAT. It appended
// IShimFrame::DeliverToSdkEntities, updated ishim_frame.h, shell_frame.cpp's
// class body, VtableLayouts.g.cs and both slot-count facts -- and left
// ShimFrame_Acquire naming StandaloneFinish. `make check` was green, the two
// EXPECTs below were green, and every server frame of the shipped game threw
//
//     native says "SourceSharpFrame001" has 18 vtable entries and
//     ShimFrameVtbl declares 19
//
// A LIVE RUN IS WHAT FOUND IT. The two tests here are so that the next one is
// not found that way.
//
// NOT CIRCULAR, and that is the whole design of the pair: the tests below
// already hold the LAYOUT against the COMPILER, so holding the ACQUIRE against
// the layout's last field name closes the triangle. Each half can fail on its
// own.
//=============================================================================

TEST( VtableAcquireCount, ShimFrameAcquireNamesTheLastMember )
{
	const std::string source =
		ReadRelative( "sourcesharp/game/server/shell_frame.cpp" );

	ASSERT_FALSE( source.empty() )
		<< "cannot find shell_frame.cpp -- this test is vacuous without it";

	const std::string named =
		AcquireMemberIn( source, "VTableHook::MemberOf( &IShimFrame::" );
	const std::string last = LastFieldOf( ReadLayout(), "ShimFrameVtbl" );

	EXPECT_FALSE( last.empty() )
		<< "ShimFrameVtbl has no fields in the committed layout";

	EXPECT_EQ( last, named )
		<< "ShimFrame_Acquire reads its entry count out of IShimFrame::" << named
		<< ", and the last slot of the interface is " << last
		<< ". A count taken from anything but the last member is SHORT, and a "
		   "short count makes managed refuse the whole area at run time while "
		   "every hermetic test stays green.";
}

TEST( VtableAcquireCount, ShimLevelAcquireNamesTheLastMember )
{
	// The same shape on the other shim interface, because the mistake is a
	// property of the pattern and not of the lane that made it.
	const std::string source =
		ReadRelative( "sourcesharp/game/server/shell_level.cpp" );

	ASSERT_FALSE( source.empty() )
		<< "cannot find shell_level.cpp -- this test is vacuous without it";

	const std::string named =
		AcquireMemberIn( source, "VTableHook::MemberOf( &IShimLevel::" );
	const std::string last = LastFieldOf( ReadLayout(), "ShimLevelVtbl" );

	EXPECT_FALSE( last.empty() )
		<< "ShimLevelVtbl has no fields in the committed layout";

	EXPECT_EQ( last, named );
}

TEST( VtableAcquireCount, ShimRootGetNamesTheLastMember )
{
	// The same shape again, for the root LoadSideV2 hands managed.
	const std::string source =
		ReadRelative( "sourcesharp/game/shared/shell_internalfactory.cpp" );

	ASSERT_FALSE( source.empty() )
		<< "cannot find shell_internalfactory.cpp -- this test is vacuous without it";

	const std::string named =
		AcquireMemberIn( source, "VTableHook::MemberOf( &IShimRoot::" );
	const std::string last = LastFieldOf( ReadLayout(), "ShimRootVtbl" );

	EXPECT_FALSE( last.empty() )
		<< "ShimRootVtbl has no fields in the committed layout";

	EXPECT_EQ( last, named );
}

TEST( VtableAcquireCount, ShimConsoleAcquireNamesTheLastMember )
{
	const std::string source =
		ReadRelative( "sourcesharp/game/shared/shell_internalfactory.cpp" );

	ASSERT_FALSE( source.empty() )
		<< "cannot find shell_internalfactory.cpp -- this test is vacuous without it";

	const std::string named =
		AcquireMemberIn( source, "VTableHook::MemberOf( &IShimConsole::" );
	const std::string last = LastFieldOf( ReadLayout(), "ShimConsoleVtbl" );

	EXPECT_FALSE( last.empty() )
		<< "ShimConsoleVtbl has no fields in the committed layout";

	EXPECT_EQ( last, named );
}

TEST( VtableAcquireCount, TheMemberReaderAnswersNothingForAShapeThatIsNotThere )
{
	// The reader has to be able to FAIL, or both tests above would agree with
	// an Acquire that had stopped naming a member at all.
	EXPECT_EQ( std::string(),
	           AcquireMemberIn( "nothing of the kind", "VTableHook::MemberOf( &IShimFrame::" ) );

	EXPECT_EQ( std::string( "Zeta" ),
	           AcquireMemberIn( "x VTableHook::MemberOf( &IShimFrame::Zeta ).slot + 1;",
	                            "VTableHook::MemberOf( &IShimFrame::" ) );
}

TEST( VtableAcquireCount, TheLastFieldReaderAnswersNothingForAStructThatIsNotThere )
{
	EXPECT_EQ( std::string(), LastFieldOf( ReadLayout(), "NoSuchVtblStructExists" ) );
}

TEST( VtableLayoutLength, TheGeneratedFileIsFound )
{
	EXPECT_FALSE( ReadLayout().empty() )
		<< "cannot find " << kLayoutRelative << " -- every test below is vacuous without it";
}

TEST( VtableLayoutLength, UniformRandomStreamIsFourSlots )
{
	const int compiler =
		VTableHook::MemberOf( &IUniformRandomStream::RandomFloatExp ).slot + 1;

	EXPECT_EQ( 4, compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "UniformRandomStreamVtbl" ) );
}

TEST( VtableLayoutLength, TheRandomStreamHasNoVirtualDestructorSoSetSeedIsSlotZero )
{
	// public/vstdlib/random.h:28 is `//virtual ~IUniformRandomStream() { }`,
	// COMMENTED OUT. Counting it would put the complete/deleting pair above
	// these four and move every slot by two.
	EXPECT_EQ( 0, VTableHook::MemberOf( &IUniformRandomStream::SetSeed ).slot );
}

TEST( VtableLayoutLength, ModelInfoIsFiftyNineSlots )
{
	const int compiler =
		VTableHook::MemberOf( &IVModelInfo::UnregisterModelLoadCallback ).slot + 1;

	EXPECT_EQ( SS_ABI( 59, 58 ), compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "ModelInfoVtbl" ) );
}

TEST( VtableLayoutLength, ModelInfoReservesTwoSlotsForItsVirtualDestructor )
{
	// THE WHOLE REASON THE DESTRUCTOR PAIR IS EMITTED. IVModelInfo declares a
	// virtual destructor, so slots 0 and 1 are the complete-object destructor
	// (D1) and the deleting destructor (D0), and the first method is at 2. An
	// index one too low here frees the engine's singleton.
	EXPECT_EQ( SS_ABI( 2, 1 ), VTableHook::MemberOf( &IVModelInfo::GetModel ).slot );
	EXPECT_EQ( VTableHook::MemberOf( &IVModelInfo::GetModel ).slot, SlotIn( ReadLayout(), "ModelInfoVtbl", "GetModel" ) );
}

TEST( VtableLayoutLength, BaseFileSystemIsSeventeenSlots )
{
	const int compiler = VTableHook::MemberOf( &IBaseFileSystem::UnzipFile ).slot + 1;

	EXPECT_EQ( 17, compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "BaseFileSystemVtbl" ) );
}

TEST( VtableLayoutLength, TheTwoSizeOverloadsAreDifferentSlotsAndTheSecondIsTheOneWithNames )
{
	// IBaseFileSystem declares Size TWICE, and the generated struct spells them
	// `Size` and `Size_2` in that order. EngineBaseFileSystem calls Size_2.
	// Binding the first would hand a filename to a method expecting a
	// FileHandle_t -- which is why a table keyed by method NAME could never have
	// been right here.
	const int byHandle =
		VTableHook::MemberOf< unsigned int ( IBaseFileSystem::* )( FileHandle_t ) >(
			&IBaseFileSystem::Size ).slot;
	const int byName =
		VTableHook::MemberOf< unsigned int ( IBaseFileSystem::* )( const char *, const char * ) >(
			&IBaseFileSystem::Size ).slot;

	EXPECT_NE( byHandle, byName );

	// Declared handle first, then names: Itanium numbers them 6 and 7, and
	// MSVC, which lays a class's overloads out in reverse, 7 and 6.
	EXPECT_EQ( SS_ABI( 6, 7 ), byHandle );
	EXPECT_EQ( SS_ABI( 7, 6 ), byName );
}

TEST( VtableLayoutLength, EngineClientIsOneHundredAndFortySixSlots )
{
	// IVEngineClient derives from IVEngineClient013 by SINGLE inheritance, so
	// its slots CONTINUE the base's numbering and one number is the length of
	// the whole chain.
	//
	// 146 IS THE BOTH-CONDITIONALS-IN ANSWER, which is what the `#define
	// USE_SDL 1` at the top of this file buys. Without it this is 145 and the
	// generated file, which counts both declarations, says 146.
	const int compiler =
		VTableHook::MemberOf( &IVEngineClient::SetRichPresenceConnect ).slot + 1;

	EXPECT_EQ( 146, compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "EngineClientVtbl" ) );
}

TEST( VtableLayoutLength, TheDerivedEngineClientKeepsTheBaseSlotsWhereTheyWere )
{
	// The derived interface's FIRST method sits one past the base's last, so
	// this number is the length of IVEngineClient013 alone -- 138 with both
	// conditional declarations compiled in.
	EXPECT_EQ( 138, VTableHook::MemberOf( &IVEngineClient::GetProtocolVersion ).slot );
}

TEST( VtableLayoutLength, TheConditionalDeclarationsOccupyTheirSlots )
{
	// The two `#if` declarations, named directly. If either were compiled out
	// the slot below it would move, which is the defect the define at the top of
	// this file exists to prevent -- so it is asserted rather than assumed.
	EXPECT_EQ( 53, VTableHook::MemberOf( &IVEngineClient013::GetVoiceTweakAPI ).slot );
	EXPECT_EQ( 127, VTableHook::MemberOf( &IVEngineClient013::GetMouseDelta ).slot );
}

TEST( VtableLayoutLength, EngineServerIsOneHundredAndTwentyFourSlots )
{
	// eiface.h's IVEngineServer body holds 124 `virtual` declarations and ZERO
	// preprocessor directives, so nothing about this number depends on how
	// anything was compiled.
	const int compiler = VTableHook::MemberOf( &IVEngineServer::SetPausedForced ).slot + 1;

	EXPECT_EQ( 124, compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "EngineServerVtbl" ) );
}

TEST( VtableLayoutLength, ShimRootIsThreeSlots )
{
	// plan_engineapi_vtables step A3, and C5 (AcquireInterface, before Release);
	// AbiVersion left with the ABI version (2026-10-01). No virtual destructor,
	// for ishim_frame.h's reason. Release is the anchor managed's
	// ShimRoot.TryBind holds the count against, with equality.
	const int compiler = VTableHook::MemberOf( &IShimRoot::Release ).slot + 1;

	EXPECT_EQ( 3, compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "ShimRootVtbl" ) );
}

TEST( VtableLayoutLength, ManagedRootIsThreeSlots )
{
	// plan_engineapi_vtables D3/D4, reshaped by plan_managed_vtable_reload.md
	// L1: Release (0), Validate (1), Acquire, managed-built. Native accepts
	// managed's object through Validate against these slots' hashes.
	EXPECT_EQ( 0, VTableHook::MemberOf( &IManagedRoot::Release ).slot );
	EXPECT_EQ( 1, VTableHook::MemberOf( &IManagedRoot::Validate ).slot );
	const int compiler = VTableHook::MemberOf( &IManagedRoot::Acquire ).slot + 1;

	EXPECT_EQ( 3, compiler );
	EXPECT_EQ( compiler, kIManagedRootSlotCount );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "ManagedRootVtbl" ) );
}

TEST( VtableLayoutLength, ManagedRuntimeClientIsNineSlots )
{
	// plan_client_vtables CD: the shell client DLL's calls into the mod,
	// managed-built. Release (0) and Validate (1) from IManagedObject, then
	// Frame, ClientStage, LevelInit, LevelShutdown, ClientInvalidateMdlCache
	// (appended at CE9a), ParticlesNeedFullReload (plan_map_load P8) and
	// MapMaterialReload (P1).
	EXPECT_EQ( 0, VTableHook::MemberOf( &IManagedRuntimeClient::Release ).slot );
	EXPECT_EQ( 1, VTableHook::MemberOf( &IManagedRuntimeClient::Validate ).slot );
	EXPECT_EQ( 6, VTableHook::MemberOf( &IManagedRuntimeClient::ClientInvalidateMdlCache ).slot );
	EXPECT_EQ( 7, VTableHook::MemberOf( &IManagedRuntimeClient::ParticlesNeedFullReload ).slot );
	const int compiler = VTableHook::MemberOf( &IManagedRuntimeClient::MapMaterialReload ).slot + 1;

	EXPECT_EQ( 9, compiler );
	EXPECT_EQ( compiler, kIManagedRuntimeClientSlotCount );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "ManagedRuntimeClientVtbl" ) );
}

TEST( VtableLayoutLength, ManagedRuntimeServerIsThirtyOneSlots )
{
	// plan_engineapi_vtables D5/E1: the shell game DLL's calls into the mod,
	// managed-built. No virtual destructor, no stop slot; Release (0) and
	// Validate (1) first (plan_managed_vtable_reload.md L1).
	EXPECT_EQ( 0, VTableHook::MemberOf( &IManagedRuntimeServer::Release ).slot );
	EXPECT_EQ( 1, VTableHook::MemberOf( &IManagedRuntimeServer::Validate ).slot );
	const int compiler = VTableHook::MemberOf( &IManagedRuntimeServer::OnGetGameDescription ).slot + 1;

	EXPECT_EQ( 31, compiler );   // 26 at D5, +3 at E1 (the pre-entity pair, worldspawn's keys), +1 at E11 (game description), +1 at L1 (Validate)
	EXPECT_EQ( compiler, kIManagedRuntimeServerSlotCount );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "ManagedRuntimeServerVtbl" ) );
}

TEST( VtableLayoutLength, ShimConsoleIsElevenSlots )
{
	// plan_engineapi_vtables step A4: the ten console slots of EngineApi, in
	// the struct's order, then RevertNamedConVar (sound cluster 17, lane
	// c2-sims). No virtual destructor.
	const int compiler = VTableHook::MemberOf( &IShimConsole::ConsoleSnapshotEntry ).slot + 1;

	EXPECT_EQ( 11, compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "ShimConsoleVtbl" ) );
}

TEST( VtableLayoutLength, ParticleLibIsFortyFiveSlots )
{
	// plan_particles_so.md: libss_particles.so's ISSParticleLib -- Connect,
	// Disconnect, Mgr, Counters, then the 41 particles.a members client.so
	// forwards. No virtual destructor. MgrUncacheAllParticleSystems is the
	// anchor ParticleLib_Acquire reports (particles_lib_client.cpp).
	const int compiler = VTableHook::MemberOf( &ISSParticleLib::MgrUncacheAllParticleSystems ).slot + 1;

	EXPECT_EQ( 45, compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "ParticleLibVtbl" ) );
}

TEST( VtableLayoutLength, ShimFrameIsNineteenSlots )
{
	// NINETEEN AND NOT TWENTY-ONE. IShimFrame declares no virtual destructor --
	// deliberately, see ishim_frame.h -- so there is no Itanium complete/
	// deleting pair in front of the first method and slot 0 really is
	// IsSimulatingOnAlternateTicks. Adding one would move every slot by two,
	// and this arm is where that arrives.
	//
	// Seventeen until plan_entity.md step 1g, which split StandaloneRunFrame
	// into StandaloneUpdate and StandaloneFinish so the managed think pass
	// could run between them; eighteen until the sub-ioqueue lane appended
	// DeliverToSdkEntities, the leaf the managed I/O queue asks when nothing
	// managed answered an input.
	//
	// READ OFF THE LAST MEMBER, whichever it is: this is `+ 1` on the tail of
	// the header, so an append that forgets the layout file is caught by the
	// second EXPECT rather than by the first agreeing with a stale constant.
	const int compiler = VTableHook::MemberOf( &IShimFrame::DeliverToSdkEntities ).slot + 1;

	EXPECT_EQ( 19, compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "ShimFrameVtbl" ) );
}

TEST( VtableLayoutLength, ShimLevelIsTwentySixSlots )
{
	// Twenty-six and not twenty-eight: IShimLevel declares no virtual
	// destructor either, for the reason ishim_level.h gives.
	//
	// TWENTY-TWO UNTIL STAGE 2 OF THE SAVE LANE, which appended
	// LoadSaveGameState -- the one call the game DLL has to make for a saved
	// world to come back, and the reason stage 1's ReadRestoreHeaders and
	// Restore never fired. TWENTY-THREE UNTIL plan_server_novalve P2.1, which
	// appended FileSizeOnPath and ReadFileOnPath for the managed particle
	// manifest. TWENTY-FIVE UNTIL P5, which appended SpawnWorld: the world
	// arrives as parsed pairs instead of map text. The number moving is what the
	// append-only rule looks like from here; the ORDER check below is what says
	// it appended.
	const int compiler = VTableHook::MemberOf( &IShimLevel::SpawnWorld ).slot + 1;

	EXPECT_EQ( 26, compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "ShimLevelVtbl" ) );
}

TEST( VtableLayoutLength, TheShimLevelSlotsAreInTheHeadersOwnOrder )
{
	// The first, one in the middle and the last -- because the span check
	// cannot see a reorder and this is where a reorder arrives.
	EXPECT_EQ( 0, VTableHook::MemberOf( &IShimLevel::ResetServerTime ).slot );
	EXPECT_EQ( 7, VTableHook::MemberOf( &IShimLevel::ResetDeleteList ).slot );

	// PropDataClearFile WAS the last slot and is now the second to last. It
	// stays at 21, which is what says stage 2's LoadSaveGameState was APPENDED
	// rather than inserted -- the one diff shape the append-only rule permits
	// and the one the span check cannot check.
	EXPECT_EQ( 21, VTableHook::MemberOf( &IShimLevel::PropDataClearFile ).slot );
	EXPECT_EQ( 22, VTableHook::MemberOf( &IShimLevel::LoadSaveGameState ).slot );
}

TEST( VtableLayoutLength, TheShimFrameSlotsAreInTheHeadersOwnOrder )
{
	// THE SPAN CHECK CANNOT SEE A REORDER, which is the whole reason
	// ishim_frame.h is append-only by rule: `managed <= native` protects against
	// APPENDS and nothing else, and two methods of the same shape swapped in the
	// middle passes it and silently misbinds.
	//
	// So the compiler is asked where three of them are -- the first, one in the
	// middle, and the last -- rather than only how many there are.
	EXPECT_EQ( 0, VTableHook::MemberOf( &IShimFrame::IsSimulatingOnAlternateTicks ).slot );
	EXPECT_EQ( 6, VTableHook::MemberOf( &IShimFrame::PhysicsRunThinkFunctions ).slot );
	EXPECT_EQ( 16, VTableHook::MemberOf( &IShimFrame::StandaloneUpdate ).slot );
	EXPECT_EQ( 17, VTableHook::MemberOf( &IShimFrame::StandaloneFinish ).slot );
	EXPECT_EQ( 18, VTableHook::MemberOf( &IShimFrame::DeliverToSdkEntities ).slot );
}

TEST( VtableLayoutLength, EveryBoundMethodIsVirtualAndNeedsNoThisAdjustment )
{
	// A NON-VIRTUAL MEMBER IS NOT IN THE TABLE AT ALL, so indexing one is a
	// category error rather than an off-by-one. And a non-zero adjustment would
	// mean the pointer was converted from a base's member to a derived's, in
	// which case the slot is numbered against a table this is not indexing.
	const VTableHook::Member members[] = {
		VTableHook::MemberOf( &IUniformRandomStream::RandomFloat ),
		VTableHook::MemberOf( &IVModelInfo::GetModel ),
		VTableHook::MemberOf( &IVModelInfo::GetModelName ),
		VTableHook::MemberOf( &IBaseFileSystem::GetFileTime ),
		VTableHook::MemberOf( &IVEngineClient013::GetLevelName ),
		VTableHook::MemberOf( &IVEngineClient::GetProtocolVersion ),
		VTableHook::MemberOf( &IVEngineServer::GetGameDir ),

		// A SHIM MEMBER TOO, and it is not decoration: CShimFrame derives from
		// IShimFrame singly, so a non-zero adjustment here would mean the
		// pointer had been converted across a base and the slot numbered
		// against a table nothing is indexing.
		VTableHook::MemberOf( &IShimFrame::TickCount ),
	};

	for ( size_t i = 0; i < sizeof( members ) / sizeof( members[ 0 ] ); i++ )
	{
		EXPECT_TRUE( members[ i ].isVirtual ) << "member " << i;
		EXPECT_EQ( 0, members[ i ].adjust ) << "member " << i;
	}
}

TEST( VtableLayoutLength, AStructTheGeneratorNoLongerEmitsReadsAsMinusOneRatherThanZero )
{
	// THE ARM THAT MAKES EVERY TEST ABOVE MEAN SOMETHING. If FieldsIn answered 0
	// for a struct it could not find, every EXPECT_EQ above would compare a real
	// number against 0 and fail loudly -- but a future refactor that made it
	// answer the count of an EMPTY body would pass silently. -1 can never equal
	// a table length.
	EXPECT_EQ( -1, FieldsIn( ReadLayout(), "NoSuchVtblStructExists" ) );
}

//=============================================================================
// THE REST OF THE RESOLVED SET.
//
// One test per interface, and each is the same two questions: what does the
// COMPILER say this table's length is, and does the committed generated file
// say the same? The compiler's answer comes from a pointer to the chain's last
// declaration, which under the Itanium ABI carries its own slot.
//
// THE TWO ANSWERS ARE INDEPENDENT even though the method's NAME came from the
// header walk. If the walk missed a method, the last one is still last and its
// true slot is higher than the walk's count; if the walk counted one the
// compiler does not (a `#if` compiled out, say), the true slot is lower. Either
// way the numbers part company, which is the whole failure mode a generated
// layout has.
//
// NONE OF THESE IS EXPOSED. There is no impl class for any of them and no
// managed method reaches a single one of these slots. The layouts exist because
// under this design they cost nothing to have and because a later lane that
// finds a caller should not have to derive one -- see docs/vtable-binding.md.
//=============================================================================

#define VTABLE_LENGTH_TEST( name, structName, expr, expected )   \
	TEST( VtableLayoutLength, name )                             \
	{                                                            \
		const int compiler = VTableHook::MemberOf( expr ).slot + 1; \
		EXPECT_EQ( expected, compiler );                         \
		EXPECT_EQ( compiler, EntriesIn( ReadLayout(), structName ) ); \
	}

// IVModelInfoClient appends ONE method to IVModelInfo, whose virtual destructor
// already occupies slots 0 and 1. 60 and not 61: it also RE-DECLARES
// FindOrLoadModel, which IVModelInfo declares at slot 40 as an obsolete stub, and
// an override replaces the entry in that slot rather than appending a new one.
//
// The last method is therefore OnDynamicModelsStringTableChange and not
// FindOrLoadModel -- `&IVModelInfoClient::FindOrLoadModel` answers 40, which is
// the base's slot and says nothing about the chain's length.
VTABLE_LENGTH_TEST( ModelInfoClientIsSixtySlots, "ModelInfoClientVtbl",
                    &IVModelInfoClient::OnDynamicModelsStringTableChange, SS_ABI( 60, 59 ) )

VTABLE_LENGTH_TEST( RenderViewIsFiftyTwoSlots, "RenderViewVtbl",
                    &IVRenderView::DrawBrushModelEx, 52 )

VTABLE_LENGTH_TEST( DebugOverlayIsTwentySlots, "DebugOverlayVtbl",
                    &IVDebugOverlay::AddScreenTextOverlay2, 20 )

// The first five slots are IAppSystem's -- Connect, Disconnect, QueryInterface,
// Init, Shutdown -- so a struct walked from this interface's own header alone
// would number every one of its methods five too low.
VTABLE_LENGTH_TEST( SoundEmitterSystemIsFortyFiveSlots, "SoundEmitterSystemVtbl",
                    &ISoundEmitterSystemBase::Flush, 45 )

VTABLE_LENGTH_TEST( ShadowMgrIsTwentyThreeSlots, "ShadowMgrVtbl",
                    &IShadowMgr::SetFlashlightRenderState, 23 )

VTABLE_LENGTH_TEST( EngineTraceIsEighteenSlots, "EngineTraceVtbl",
                    &IEngineTrace::GetLeafContainingPoint, 18 )

VTABLE_LENGTH_TEST( EngineSoundIsTwentyFiveSlots, "EngineSoundVtbl",
                    &IEngineSound::ExtraUpdate, 25 )

VTABLE_LENGTH_TEST( EffectsIsNineSlots, "EffectsVtbl",
                    &IVEfx::GetElightByKey, 9 )

VTABLE_LENGTH_TEST( ModelRenderIsTwentySixSlots, "ModelRenderVtbl",
                    &IVModelRender::GetMaterialOverride, 26 )

VTABLE_LENGTH_TEST( EngineVGuiIsFiveSlots, "EngineVGuiVtbl",
                    &IEngineVGui::GetWorkshopMap, SS_ABI( 5, 4 ) )

VTABLE_LENGTH_TEST( SpatialPartitionIsThirtyFiveSlots, "SpatialPartitionVtbl",
                    &ISpatialPartition::InstallQueryCallback, SS_ABI( 35, 34 ) )

// IBaseInterface declares nothing but a virtual destructor, so it contributes
// exactly the reserved PAIR and this interface's own first method is slot 2.
//
// 14 and not 16: IGameEventManager2 declares a virtual destructor of ITS OWN,
// which OVERRIDES IBaseInterface's and takes the same pair rather than a second
// one. A walk that counted both answered 16.
VTABLE_LENGTH_TEST( GameEventManagerIsFourteenSlots, "GameEventManagerVtbl",
                    &IGameEventManager2::UnserializeEvent, SS_ABI( 14, 13 ) )

VTABLE_LENGTH_TEST( NetworkStringTableContainerIsNineSlots, "NetworkStringTableContainerVtbl",
                    &INetworkStringTableContainer::SetAllowClientSideAddString, SS_ABI( 9, 8 ) )

// A string table, as FindTable hands it back: the first layout REACHED BY
// POINTER rather than by factory name, so no factory hands a slot count with it
// and this test is the count's only check. The virtual destructor takes 0 and 1;
// SetStringChangedCallback is the last of thirteen methods.
VTABLE_LENGTH_TEST( NetworkStringTableIsFifteenSlots, "NetworkStringTableVtbl",
                    &INetworkStringTable::SetStringChangedCallback, SS_ABI( 15, 14 ) )
VTABLE_LENGTH_TEST( NetChannelInfoIsTwentySixSlots, "NetChannelInfoVtbl",
                    &INetChannelInfo::GetTimeoutSeconds, 26 )

// plan_physics_managed B0: vphysics' own objects, reached by pointer from the
// environment managed creates. Each is held here, as the string table is; the
// counts include a virtual destructor's two slots where the header declares one.
VTABLE_LENGTH_TEST( PhysicsEnvironmentIsSixtyEightSlots, "PhysicsEnvironmentVtbl",
                    &IPhysicsEnvironment::DebugCheckContacts, SS_ABI( 68, 67 ) )
VTABLE_LENGTH_TEST( PhysicsObjectIsEightyFourSlots, "PhysicsObjectVtbl",
                    &IPhysicsObject::GetBuoyancyRatio, SS_ABI( 84, 83 ) )
VTABLE_LENGTH_TEST( PhysicsShadowControllerIsSixteenSlots, "PhysicsShadowControllerVtbl",
                    &IPhysicsShadowController::GetMaxSpeed, SS_ABI( 16, 15 ) )
VTABLE_LENGTH_TEST( PhysicsMotionControllerIsTenSlots, "PhysicsMotionControllerVtbl",
                    &IPhysicsMotionController::SetPriority, SS_ABI( 10, 9 ) )
VTABLE_LENGTH_TEST( PhysicsFrictionSnapshotIsFifteenSlots, "PhysicsFrictionSnapshotVtbl",
                    &IPhysicsFrictionSnapshot::GetFrictionCoefficient, SS_ABI( 15, 14 ) )
VTABLE_LENGTH_TEST( PhysicsCollisionDataIsThreeSlots, "PhysicsCollisionDataVtbl",
                    &IPhysicsCollisionData::GetContactSpeed, 3 )
// plan_physics_managed B2.2: the fluid slots' controller, reached by pointer.
VTABLE_LENGTH_TEST( PhysicsFluidControllerIsEightSlots, "PhysicsFluidControllerVtbl",
                    &IPhysicsFluidController::GetContents, SS_ABI( 8, 7 ) )
// plan_physics_bones_native_zero 2.4: the managed constraint and spring
// tables call these through the pointers the environment's creators return.
VTABLE_LENGTH_TEST( PhysicsConstraintIsFourteenSlots, "PhysicsConstraintVtbl",
                    &IPhysicsConstraint::OutputDebugInfo, SS_ABI( 14, 13 ) )
VTABLE_LENGTH_TEST( PhysicsSpringIsEightSlots, "PhysicsSpringVtbl",
                    &IPhysicsSpring::GetEndObject, SS_ABI( 8, 7 ) )
// plan_physics_bones_native_zero 2.5: the managed player shadow calls the
// controller the environment's CreatePlayerController returns; the player
// holds the pointer.
VTABLE_LENGTH_TEST( PhysicsPlayerControllerIsEighteenSlots, "PhysicsPlayerControllerVtbl",
                    &IPhysicsPlayerController::WasFrozen, SS_ABI( 18, 17 ) )

// plan_physics_managed B0b: the callbacks vphysics calls, in Valve's layout and
// written by managed. No virtual destructor in any of them, so slot 0 is the
// first method; a count that drifts from the header is a call through garbage.
VTABLE_LENGTH_TEST( PhysicsObjectEventIsTwoSlots, "PhysicsObjectEventVtbl",
                    &IPhysicsObjectEvent::ObjectSleep, 2 )
VTABLE_LENGTH_TEST( PhysicsCollisionEventIsTenSlots, "PhysicsCollisionEventVtbl",
                    &IPhysicsCollisionEvent::ObjectLeaveTrigger, 10 )
VTABLE_LENGTH_TEST( PhysicsCollisionSolverIsFiveSlots, "PhysicsCollisionSolverVtbl",
                    &IPhysicsCollisionSolver::ShouldFreezeContacts, 5 )
VTABLE_LENGTH_TEST( PhysicsConstraintEventIsOneSlot, "PhysicsConstraintEventVtbl",
                    &IPhysicsConstraintEvent::ConstraintBroken, 1 )
VTABLE_LENGTH_TEST( MotionEventIsOneSlot, "MotionEventVtbl",
                    &IMotionEvent::Simulate, 1 )

// plan_physics_bones_native_zero Phase 3: the managed ragdoll builder walks a
// .phy through the key parser, keeps a model's collision set and its joints'
// group, and answers the parser's custom keys from its own Valve-layout table.
VTABLE_LENGTH_TEST( VPhysicsKeyParserIsElevenSlots, "VPhysicsKeyParserVtbl",
                    &IVPhysicsKeyParser::SkipBlock, SS_ABI( 11, 10 ) )
VTABLE_LENGTH_TEST( PhysicsCollisionSetIsThreeSlots, "PhysicsCollisionSetVtbl",
                    &IPhysicsCollisionSet::ShouldCollide, 3 )
VTABLE_LENGTH_TEST( PhysicsConstraintGroupIsEightSlots, "PhysicsConstraintGroupVtbl",
                    &IPhysicsConstraintGroup::SolvePenetration, SS_ABI( 8, 7 ) )
VTABLE_LENGTH_TEST( VPhysicsKeyHandlerIsTwoSlots, "VPhysicsKeyHandlerVtbl",
                    &IVPhysicsKeyHandler::SetDefaults, 2 )
// Client Steam (Decision 1a): one Steam callback, in Valve's layout and written
// by managed. Unlike the vphysics callbacks it has DATA after its vptr, which
// SteamAPI_RegisterCallback writes, so managed's block reserves words for it.
VTABLE_LENGTH_TEST( SteamCallbackIsThreeSlots, "SteamCallbackVtbl",
                    &CCallbackBase::GetCallbackSizeBytes, 3 )

namespace
{
	// Reads the protected members' offsets from a derived class, which may
	// name them; offsetof on a class with a vptr is only conditionally supported.
	class CallbackBaseProbe final : public CCallbackBase
	{
	public:
		void Run( void * ) override {}
		void Run( void *, bool, SteamAPICall_t ) override {}
		int GetCallbackSizeBytes() override { return 0; }

		size_t FlagsAt() const { return size_t( reinterpret_cast<const char *>( &m_nCallbackFlags ) - reinterpret_cast<const char *>( this ) ); }
		size_t CallbackAt() const { return size_t( reinterpret_cast<const char *>( &m_iCallback ) - reinterpret_cast<const char *>( this ) ); }
	};

	// The N of the layout's `ReservedWords = N`, or -1 when the attribute is
	// not on the struct's own line before it.
	int ReservedWordsOf( const std::string &layout, const std::string &nativeClass )
	{
		const std::string head = "[ImplementedByManaged(\"" + nativeClass + "\", ValveLayout = true, ReservedWords = ";
		const size_t at = layout.find( head );
		if ( at == std::string::npos )
			return -1;
		return atoi( layout.c_str() + at + head.size() );
	}
}

// What the block reserves is what the class keeps after its vptr: the flags
// at 8 and the callback id at 12, one word, which Steam writes when it
// registers the callback. A block reserving less would hand Steam our header.
TEST( SteamCallbackLayout, ReservedWordsAreTheMembersAfterTheVptr )
{
	CallbackBaseProbe probe;
	EXPECT_EQ( 8u, probe.FlagsAt() );
	EXPECT_EQ( 12u, probe.CallbackAt() );
	EXPECT_EQ( 16u, sizeof( CCallbackBase ) );

	const int reserved = ReservedWordsOf( ReadLayout(), "CCallbackBase" );
	EXPECT_EQ( int( ( sizeof( CCallbackBase ) - sizeof( void * ) ) / sizeof( void * ) ), reserved );
}

// The five callbacks managed registers (ClientSteamCallbacks.cs): each id and
// payload size the SDK headers give, which ClientSteamCallbacksTests pins on
// the managed side. A wrong size makes Steam refuse or misread the payload; a
// wrong id files the block under another callback.
TEST( SteamCallbackLayout, CallbackIdsAndSizes )
{
	EXPECT_EQ( 101, int( SteamServersConnected_t::k_iCallback ) );
	EXPECT_EQ( 1u, sizeof( SteamServersConnected_t ) );
	EXPECT_EQ( 102, int( SteamServerConnectFailure_t::k_iCallback ) );
	EXPECT_EQ( 8u, sizeof( SteamServerConnectFailure_t ) );
	EXPECT_EQ( 103, int( SteamServersDisconnected_t::k_iCallback ) );
	EXPECT_EQ( 4u, sizeof( SteamServersDisconnected_t ) );
	EXPECT_EQ( 1014, int( NewUrlLaunchParameters_t::k_iCallback ) );
	EXPECT_EQ( 1u, sizeof( NewUrlLaunchParameters_t ) );
	EXPECT_EQ( 337, int( GameRichPresenceJoinRequested_t::k_iCallback ) );
	EXPECT_EQ( 264u, sizeof( GameRichPresenceJoinRequested_t ) );
	EXPECT_EQ( 0u, offsetof( GameRichPresenceJoinRequested_t, m_steamIDFriend ) );
	EXPECT_EQ( 8u, offsetof( GameRichPresenceJoinRequested_t, m_rgchConnect ) );
	EXPECT_EQ( 256, k_cchMaxRichPresenceValueLength );
}

// plan_physics_managed B2.2: Pre/PostCollision hand managed a
// vcollisionevent_t*, read through VCollisionEvent's explicit offsets. These
// are the numbers VCollisionEventLayoutTests pins on the managed side.
TEST( VCollisionEventLayout, OffsetsAreTheManagedViews )
{
	EXPECT_EQ( 0u,  offsetof( vcollisionevent_t, pObjects ) );
	EXPECT_EQ( 16u, offsetof( vcollisionevent_t, surfaceProps ) );
	EXPECT_EQ( 24u, offsetof( vcollisionevent_t, isCollision ) );
	EXPECT_EQ( 25u, offsetof( vcollisionevent_t, isShadowCollision ) );
	EXPECT_EQ( 28u, offsetof( vcollisionevent_t, deltaCollisionTime ) );
	EXPECT_EQ( 32u, offsetof( vcollisionevent_t, collisionSpeed ) );
	EXPECT_EQ( 40u, offsetof( vcollisionevent_t, pInternalData ) );
	EXPECT_EQ( 48u, sizeof( vcollisionevent_t ) );
}

// plan_physics_bones_native_zero 2.4: the managed constraint and spring tables
// hand vphysics' creators these blocks, built in VPhysicsJointParams.cs. These
// are the numbers VPhysicsJointParamsLayoutTests pins on the managed side.
// plan_physics_bones_native_zero Phase 3: the managed ragdoll builder reads a
// vcollide_t, parses each solid into a solid_t it allocates, writes its
// objectparams_t in place, and makes the joints' group from a
// constraint_groupparams_t. These are the numbers VPhysicsJointParamsLayoutTests
// (ObjectParams, ConstraintGroupParams) and SolidLayout / VCollideFields spell.
TEST( RagdollParamsLayout, ObjectParamsSolidGroupAndCollide )
{
	EXPECT_EQ( 8u,  offsetof( objectparams_t, mass ) );
	EXPECT_EQ( 24u, offsetof( objectparams_t, rotInertiaLimit ) );
	EXPECT_EQ( 32u, offsetof( objectparams_t, pName ) );
	EXPECT_EQ( 40u, offsetof( objectparams_t, pGameData ) );
	EXPECT_EQ( 48u, offsetof( objectparams_t, volume ) );
	EXPECT_EQ( 52u, offsetof( objectparams_t, dragCoefficient ) );
	EXPECT_EQ( 56u, offsetof( objectparams_t, enableCollisions ) );
	EXPECT_EQ( 64u, sizeof( objectparams_t ) );

	EXPECT_EQ( 4u,    offsetof( solid_t, name ) );
	EXPECT_EQ( 516u,  offsetof( solid_t, parent ) );
	EXPECT_EQ( 1028u, offsetof( solid_t, surfaceprop ) );
	EXPECT_EQ( 1540u, offsetof( solid_t, massCenterOverride ) );
	EXPECT_EQ( 1552u, offsetof( solid_t, params ) );
	EXPECT_EQ( 1616u, sizeof( solid_t ) );

	EXPECT_EQ( 4u,  offsetof( constraint_groupparams_t, minErrorTicks ) );
	EXPECT_EQ( 8u,  offsetof( constraint_groupparams_t, errorTolerance ) );
	EXPECT_EQ( 12u, sizeof( constraint_groupparams_t ) );

	EXPECT_EQ( 8u,  offsetof( vcollide_t, solids ) );
	EXPECT_EQ( 16u, offsetof( vcollide_t, pKeyValues ) );
}

// plan_physics_bones_native_zero 2.6: the managed world build parses each fluid
// block into a fluid_t and hands vphysics its fluidparams_t with the game data
// set, and the surface sound precache reads surfacedata_t's ten sound names and
// writes its ten handles. These are the numbers FluidLayout and
// SurfaceDataLayout (PhysicsWorldVPhysics.cs) spell.
//-----------------------------------------------------------------------------
// A Vector RETURNED BY VALUE IS SPELLED IN MANAGED (tools/vtable-layout's
// BY_VALUE_RETURNS, 2026-10-12), and this is the C++ half of why that is
// sound. The Itanium C++ ABI passes a class like a C struct unless it is
// "non-trivial for the purposes of calls" -- a non-trivial copy or move
// constructor or destructor -- and the SysV x86-64 ABI then returns a struct
// of at most 16 bytes in registers; three floats classify as SSE:SSE, so x and
// y come back in xmm0 and z in xmm1, with no hidden sret pointer. vector.h's
// copy constructor is commented out (187) and its definition is under `#if 0`
// (534). Vector is NOT trivially copyable -- it declares its own operator= --
// but assignment does not enter the call rule, so that is not asserted. If a
// copy constructor or destructor came back, the return would become an sret
// call and every managed `delegate* unmanaged<..., Vector>` would read
// garbage -- so those are pinned.
// (server.so's SourceSharp_PhysicsInertia, at the time: `call *0x100(%rax)`
// with only `this` in rdi, then `movlpd %xmm0` / `movss %xmm1`.)
//-----------------------------------------------------------------------------
TEST( VectorByValue, IsReturnedInRegisters )
{
	static_assert( std::is_trivially_copy_constructible< Vector >::value, "Vector grew a copy constructor" );
	static_assert( std::is_trivially_destructible< Vector >::value, "Vector grew a destructor" );
	static_assert( !std::is_polymorphic< Vector >::value, "Vector grew a vtable" );
	static_assert( std::is_standard_layout< Vector >::value, "Vector is not standard layout" );

	EXPECT_EQ( 12u, sizeof( Vector ) );
	EXPECT_EQ( 0u, offsetof( Vector, x ) );
	EXPECT_EQ( 4u, offsetof( Vector, y ) );
	EXPECT_EQ( 8u, offsetof( Vector, z ) );
}

TEST( WorldParamsLayout, FluidAndSurfaceSounds )
{
	EXPECT_EQ( 0u,   offsetof( fluid_t, index ) );
	EXPECT_EQ( 4u,   offsetof( fluid_t, surfaceprop ) );
	EXPECT_EQ( 512u, sizeof( fluid_t::surfaceprop ) );
	EXPECT_EQ( 520u, offsetof( fluid_t, params ) );
	EXPECT_EQ( 40u,  offsetof( fluidparams_t, pGameData ) );
	EXPECT_EQ( 56u,  sizeof( fluidparams_t ) );
	EXPECT_EQ( 576u, sizeof( fluid_t ) );

	EXPECT_EQ( 44u, offsetof( surfacedata_t, sounds ) );
	EXPECT_EQ( 0u,  offsetof( surfacesoundnames_t, stepleft ) );
	EXPECT_EQ( 18u, offsetof( surfacesoundnames_t, strainSound ) );
	EXPECT_EQ( 20u, sizeof( surfacesoundnames_t ) );
	EXPECT_EQ( 76u, offsetof( surfacedata_t, soundhandles ) );
	EXPECT_EQ( 0u,  offsetof( surfacesoundhandles_t, stepleft ) );
	EXPECT_EQ( 18u, offsetof( surfacesoundhandles_t, strainSound ) );
	EXPECT_EQ( 20u, sizeof( surfacesoundhandles_t ) );
	EXPECT_EQ( 96u, sizeof( surfacedata_t ) );
	EXPECT_EQ( 0x0200, CALLBACK_NEVER_DELETED );
}

TEST( ConstraintParamsLayout, BreakableAndAxisLimit )
{
	EXPECT_EQ( 0u,  offsetof( constraint_breakableparams_t, strength ) );
	EXPECT_EQ( 4u,  offsetof( constraint_breakableparams_t, forceLimit ) );
	EXPECT_EQ( 8u,  offsetof( constraint_breakableparams_t, torqueLimit ) );
	EXPECT_EQ( 12u, offsetof( constraint_breakableparams_t, bodyMassScale ) );
	EXPECT_EQ( 20u, offsetof( constraint_breakableparams_t, isActive ) );
	EXPECT_EQ( 24u, sizeof( constraint_breakableparams_t ) );
	EXPECT_EQ( 12u, offsetof( constraint_axislimit_t, torque ) );
	EXPECT_EQ( 16u, sizeof( constraint_axislimit_t ) );
	EXPECT_EQ( 48u, sizeof( matrix3x4_t ) );
}

TEST( ConstraintParamsLayout, FixedHingeBallsocketLength )
{
	EXPECT_EQ( 48u, offsetof( constraint_fixedparams_t, constraint ) );
	EXPECT_EQ( 72u, sizeof( constraint_fixedparams_t ) );

	EXPECT_EQ( 12u, offsetof( constraint_hingeparams_t, worldAxisDirection ) );
	EXPECT_EQ( 24u, offsetof( constraint_hingeparams_t, hingeAxis ) );
	EXPECT_EQ( 40u, offsetof( constraint_hingeparams_t, constraint ) );
	EXPECT_EQ( 64u, sizeof( constraint_hingeparams_t ) );

	EXPECT_EQ( 24u, offsetof( constraint_ballsocketparams_t, constraint ) );
	EXPECT_EQ( 48u, sizeof( constraint_ballsocketparams_t ) );

	EXPECT_EQ( 24u, offsetof( constraint_lengthparams_t, objectPosition ) );
	EXPECT_EQ( 48u, offsetof( constraint_lengthparams_t, totalLength ) );
	EXPECT_EQ( 52u, offsetof( constraint_lengthparams_t, minLength ) );
	EXPECT_EQ( 56u, sizeof( constraint_lengthparams_t ) );
}

TEST( ConstraintParamsLayout, SlidingPulleyRagdollSpring )
{
	EXPECT_EQ( 48u,  offsetof( constraint_slidingparams_t, slideAxisRef ) );
	EXPECT_EQ( 60u,  offsetof( constraint_slidingparams_t, constraint ) );
	EXPECT_EQ( 84u,  offsetof( constraint_slidingparams_t, limitMin ) );
	EXPECT_EQ( 88u,  offsetof( constraint_slidingparams_t, limitMax ) );
	EXPECT_EQ( 92u,  offsetof( constraint_slidingparams_t, friction ) );
	EXPECT_EQ( 96u,  offsetof( constraint_slidingparams_t, velocity ) );
	EXPECT_EQ( 100u, sizeof( constraint_slidingparams_t ) );

	EXPECT_EQ( 24u, offsetof( constraint_pulleyparams_t, pulleyPosition ) );
	EXPECT_EQ( 48u, offsetof( constraint_pulleyparams_t, objectPosition ) );
	EXPECT_EQ( 72u, offsetof( constraint_pulleyparams_t, totalLength ) );
	EXPECT_EQ( 76u, offsetof( constraint_pulleyparams_t, gearRatio ) );
	EXPECT_EQ( 80u, offsetof( constraint_pulleyparams_t, isRigid ) );
	EXPECT_EQ( 84u, sizeof( constraint_pulleyparams_t ) );

	EXPECT_EQ( 24u,  offsetof( constraint_ragdollparams_t, constraintToReference ) );
	EXPECT_EQ( 72u,  offsetof( constraint_ragdollparams_t, constraintToAttached ) );
	EXPECT_EQ( 120u, offsetof( constraint_ragdollparams_t, parentIndex ) );
	EXPECT_EQ( 124u, offsetof( constraint_ragdollparams_t, childIndex ) );
	EXPECT_EQ( 128u, offsetof( constraint_ragdollparams_t, axes ) );
	EXPECT_EQ( 176u, offsetof( constraint_ragdollparams_t, onlyAngularLimits ) );
	EXPECT_EQ( 177u, offsetof( constraint_ragdollparams_t, isActive ) );
	EXPECT_EQ( 178u, offsetof( constraint_ragdollparams_t, useClockwiseRotations ) );
	EXPECT_EQ( 180u, sizeof( constraint_ragdollparams_t ) );

	EXPECT_EQ( 4u,  offsetof( springparams_t, naturalLength ) );
	EXPECT_EQ( 8u,  offsetof( springparams_t, damping ) );
	EXPECT_EQ( 12u, offsetof( springparams_t, relativeDamping ) );
	EXPECT_EQ( 16u, offsetof( springparams_t, startPosition ) );
	EXPECT_EQ( 28u, offsetof( springparams_t, endPosition ) );
	EXPECT_EQ( 40u, offsetof( springparams_t, useLocalPositions ) );
	EXPECT_EQ( 41u, offsetof( springparams_t, onlyStretch ) );
	EXPECT_EQ( 44u, sizeof( springparams_t ) );
}

// objectparams_t, written by managed's player shadow for CreatePolyObject
// (plan_physics_bones_native_zero 2.5). VPhysicsObjectParams in
// SourceSharp.Abi carries the same offsets.
TEST( ObjectParamsLayout, TheFieldsTheManagedShadowWrites )
{
	EXPECT_EQ( 0u,  offsetof( objectparams_t, massCenterOverride ) );
	EXPECT_EQ( 8u,  offsetof( objectparams_t, mass ) );
	EXPECT_EQ( 12u, offsetof( objectparams_t, inertia ) );
	EXPECT_EQ( 16u, offsetof( objectparams_t, damping ) );
	EXPECT_EQ( 20u, offsetof( objectparams_t, rotdamping ) );
	EXPECT_EQ( 24u, offsetof( objectparams_t, rotInertiaLimit ) );
	EXPECT_EQ( 32u, offsetof( objectparams_t, pName ) );
	EXPECT_EQ( 40u, offsetof( objectparams_t, pGameData ) );
	EXPECT_EQ( 48u, offsetof( objectparams_t, volume ) );
	EXPECT_EQ( 52u, offsetof( objectparams_t, dragCoefficient ) );
	EXPECT_EQ( 56u, offsetof( objectparams_t, enableCollisions ) );
	EXPECT_EQ( 64u, sizeof( objectparams_t ) );
}

// surfacedata_t, read in place by managed's queued physics sounds through
// IPhysicsSurfaceProps::GetSurfaceData (plan_physics_bones_native_zero 2.3).
// VPhysicsSurfaceData in SourceSharp.Abi carries the same offsets.
TEST( SurfaceDataLayout, TheSoundFieldsTheManagedBatchReads )
{
	EXPECT_EQ( 20u, offsetof( surfacedata_t, audio ) );
	EXPECT_EQ( 44u, offsetof( surfacedata_t, sounds ) );
	EXPECT_EQ( 24u, offsetof( surfacedata_t, audio ) + offsetof( surfaceaudioparams_t, hardnessFactor ) );
	EXPECT_EQ( 36u, offsetof( surfacedata_t, audio ) + offsetof( surfaceaudioparams_t, hardThreshold ) );
	EXPECT_EQ( 40u, offsetof( surfacedata_t, audio ) + offsetof( surfaceaudioparams_t, hardVelocityThreshold ) );
	EXPECT_EQ( 48u, offsetof( surfacedata_t, sounds ) + offsetof( surfacesoundnames_t, impactSoft ) );
	EXPECT_EQ( 50u, offsetof( surfacedata_t, sounds ) + offsetof( surfacesoundnames_t, impactHard ) );
	EXPECT_EQ( 60u, offsetof( surfacedata_t, sounds ) + offsetof( surfacesoundnames_t, breakSound ) );
	EXPECT_EQ( 96u, sizeof( surfacedata_t ) );
}

// Two derived classes over one base, and each is its own table: the shared
// prefix is IStaticPropMgr's and the tails are different lengths.
VTABLE_LENGTH_TEST( StaticPropMgrClientIsFifteenSlots, "StaticPropMgrClientVtbl",
                    &IStaticPropMgrClient::AddColorDecalToStaticProp, 15 )

VTABLE_LENGTH_TEST( StaticPropMgrServerIsEightSlots, "StaticPropMgrServerVtbl",
                    &IStaticPropMgrServer::GetAllStaticPropsInOBB, 8 )

VTABLE_LENGTH_TEST( InputSystemIsFiftyFiveSlots, "InputSystemVtbl",
                    &IInputSystem::SetSkipControllerInitialization, 55 )

VTABLE_LENGTH_TEST( GameUIFuncsIsSevenSlots, "GameUIFuncsVtbl",
                    &IGameUIFuncs::IsConnectedToVACSecureServer, 7 )

// ZERO compiled call sites, and the layout is here anyway.
VTABLE_LENGTH_TEST( DataCacheIsFifteenSlots, "DataCacheVtbl",
                    &IDataCache::OutputReport, 15 )

VTABLE_LENGTH_TEST( SceneFileCacheIsElevenSlots, "SceneFileCacheVtbl",
                    &ISceneFileCache::Reload, 11 )

// ICvar's LAST declaration is protected, so a pointer to it cannot be taken
// from outside the class -- a derived type can, which is what this is for and
// all it is for. Nothing is ever constructed.
//
// TWO DEFECTS IN THE WALK WERE FOUND HERE AND NOWHERE ELSE, both of which made
// it answer a plausible wrong length:
//
//   * ICvar declares a nested `class ICVarIteratorInternal` with a virtual
//     destructor and four methods, and the walk counted all six as ICvar's;
//   * `PublishToVXConsole` sits inside `#if defined( _X360 )`, which is false
//     here, and the walk counted it too.
//
// 42, then 36, against the compiler's 35. Both are fixed in tools/vtable-layout;
// this test is why they were fixed rather than shipped.
namespace
{
struct CvarPeek : public ICvar
{
	static int Slots()
	{
		return VTableHook::MemberOf( &CvarPeek::FactoryInternalIterator ).slot + 1;
	}
};
}

TEST( VtableLayoutLength, CvarIsThirtyFiveSlots )
{
	const int compiler = CvarPeek::Slots();

	EXPECT_EQ( 35, compiler );
	EXPECT_EQ( compiler, EntriesIn( ReadLayout(), "CvarVtbl" ) );
}

VTABLE_LENGTH_TEST( PhysicsIsThirteenSlots, "PhysicsVtbl",
                    &IPhysics::DestroyAllCollisionSets, 13 )

VTABLE_LENGTH_TEST( PhysicsCollisionIsFiftyThreeSlots, "PhysicsCollisionVtbl",
                    &IPhysicsCollision::ReadStat, SS_ABI( 53, 52 ) )

VTABLE_LENGTH_TEST( PhysicsSurfacePropsIsElevenSlots, "PhysicsSurfacePropsVtbl",
                    &IPhysicsSurfaceProps::GetPhysicsParameters, SS_ABI( 11, 10 ) )

VTABLE_LENGTH_TEST( VoiceServerIsFiveSlots, "VoiceServerVtbl",
                    &IVoiceServer::SetClientProximity, SS_ABI( 5, 4 ) )

VTABLE_LENGTH_TEST( ServerPluginHelpersIsThreeSlots, "ServerPluginHelpersVtbl",
                    &IServerPluginHelpers::StartQueryCvarValue, 3 )

VTABLE_LENGTH_TEST( UploadGameStatsIsSevenSlots, "UploadGameStatsVtbl",
                    &IUploadGameStats::IsHDREnabled, 7 )

// plan_engineapi_vtables Phase C: one length test per interface tools/shim-areas
// generates, and kShimAreasCovered naming their structs -- generated from the
// same table as the interfaces and their layouts.
#include "shim_areas_layout.inc"

// plan_engineapi_vtables E: the managed-built areas tools/managed-areas generates.
#include "managed_areas_layout.inc"

//=============================================================================
// EVERY OVERLOAD IS WHERE THE LAYOUT SAYS, ON THIS BINARY'S ABI.
//
// The lengths above cannot see an overload in the wrong place: swapping two
// slots changes no count. Under Itanium an overload pair is two slots in
// declaration order; under the Microsoft ABI the same pair is reversed, which
// is the one rule in tools/vtable-layout's Windows numbering that moves a slot
// without moving the length. So every overload of every generated layout is
// asked of the compiler here, through a pointer typed to pick it, and held
// against the slot the committed file gives it.
//
// Each type below is the overload's own, const included (ICvar declares four
// const/non-const pairs with identical parameter lists).
//=============================================================================
namespace
{
	struct OverloadCase
	{
		const char *structName;
		const char *field;
		int compiler;
	};

	#define OVERLOAD( structName, field, pmfType, expr ) \
		{ structName, field, VTableHook::MemberOf< pmfType >( expr ).slot }
}

TEST( VtableOverloadOrder, EveryOverloadIsWhereTheLayoutSaysOnThisAbi )
{
	const OverloadCase cases[] = {
OVERLOAD( "ModelInfoVtbl", "GetVCollide", vcollide_t * ( IVModelInfo::* )( const model_t * ), &IVModelInfo::GetVCollide ),
		OVERLOAD( "ModelInfoVtbl", "GetVCollide_2", vcollide_t * ( IVModelInfo::* )( int ), &IVModelInfo::GetVCollide ),
		OVERLOAD( "ModelInfoVtbl", "FindModel", const studiohdr_t * ( IVModelInfo::* )( const studiohdr_t *, void **, const char * ) const, &IVModelInfo::FindModel ),
		OVERLOAD( "ModelInfoVtbl", "FindModel_2", const studiohdr_t * ( IVModelInfo::* )( void * ) const, &IVModelInfo::FindModel ),
		OVERLOAD( "BaseFileSystemVtbl", "Size", unsigned int ( IBaseFileSystem::* )( FileHandle_t ), &IBaseFileSystem::Size ),
		OVERLOAD( "BaseFileSystemVtbl", "Size_2", unsigned int ( IBaseFileSystem::* )( const char *, const char * ), &IBaseFileSystem::Size ),
		OVERLOAD( "ModelInfoClientVtbl", "GetVCollide", vcollide_t * ( IVModelInfo::* )( const model_t * ), &IVModelInfo::GetVCollide ),
		OVERLOAD( "ModelInfoClientVtbl", "GetVCollide_2", vcollide_t * ( IVModelInfo::* )( int ), &IVModelInfo::GetVCollide ),
		OVERLOAD( "ModelInfoClientVtbl", "FindModel", const studiohdr_t * ( IVModelInfo::* )( const studiohdr_t *, void **, const char * ) const, &IVModelInfo::FindModel ),
		OVERLOAD( "ModelInfoClientVtbl", "FindModel_2", const studiohdr_t * ( IVModelInfo::* )( void * ) const, &IVModelInfo::FindModel ),
		OVERLOAD( "RenderViewVtbl", "Push3DView", void ( IVRenderView::* )( const CViewSetup &, int, ITexture *, VPlane * ), &IVRenderView::Push3DView ),
		OVERLOAD( "RenderViewVtbl", "Push3DView_2", void ( IVRenderView::* )( const CViewSetup &, int, ITexture *, VPlane *, ITexture * ), &IVRenderView::Push3DView ),
		OVERLOAD( "DebugOverlayVtbl", "Refused_AddTextOverlay", void ( IVDebugOverlay::* )( const Vector &, float, const char *, ... ), &IVDebugOverlay::AddTextOverlay ),
		OVERLOAD( "DebugOverlayVtbl", "Refused_AddTextOverlay_2", void ( IVDebugOverlay::* )( const Vector &, int, float, const char *, ... ), &IVDebugOverlay::AddTextOverlay ),
		OVERLOAD( "DebugOverlayVtbl", "ScreenPosition", int ( IVDebugOverlay::* )( const Vector &, Vector & ), &IVDebugOverlay::ScreenPosition ),
		OVERLOAD( "DebugOverlayVtbl", "ScreenPosition_2", int ( IVDebugOverlay::* )( float, float, Vector & ), &IVDebugOverlay::ScreenPosition ),
		OVERLOAD( "DebugOverlayVtbl", "Refused_AddTextOverlayRGB", void ( IVDebugOverlay::* )( const Vector &, int, float, float, float, float, float, const char *, ... ), &IVDebugOverlay::AddTextOverlayRGB ),
		OVERLOAD( "DebugOverlayVtbl", "Refused_AddTextOverlayRGB_2", void ( IVDebugOverlay::* )( const Vector &, int, float, int, int, int, int, const char *, ... ), &IVDebugOverlay::AddTextOverlayRGB ),
		OVERLOAD( "SoundEmitterSystemVtbl", "GetWavFileForSound", const char * ( ISoundEmitterSystemBase::* )( const char *, const char * ), &ISoundEmitterSystemBase::GetWavFileForSound ),
		OVERLOAD( "SoundEmitterSystemVtbl", "GetWavFileForSound_2", const char * ( ISoundEmitterSystemBase::* )( const char *, gender_t ), &ISoundEmitterSystemBase::GetWavFileForSound ),
		OVERLOAD( "SoundEmitterSystemVtbl", "GenderExpandString", void ( ISoundEmitterSystemBase::* )( const char *, const char *, char *, int ), &ISoundEmitterSystemBase::GenderExpandString ),
		OVERLOAD( "SoundEmitterSystemVtbl", "GenderExpandString_2", void ( ISoundEmitterSystemBase::* )( gender_t, const char *, char *, int ), &ISoundEmitterSystemBase::GenderExpandString ),
		OVERLOAD( "EngineTraceVtbl", "EnumerateEntities", void ( IEngineTrace::* )( const Ray_t &, bool, IEntityEnumerator * ), &IEngineTrace::EnumerateEntities ),
		OVERLOAD( "EngineTraceVtbl", "EnumerateEntities_2", void ( IEngineTrace::* )( const Vector &, const Vector &, IEntityEnumerator * ), &IEngineTrace::EnumerateEntities ),
		OVERLOAD( "EngineSoundVtbl", "EmitSound", void ( IEngineSound::* )( IRecipientFilter &, int, int, const char *, float, float, int, int, int, const Vector *, const Vector *, CUtlVector<Vector> *, bool, float, int ), &IEngineSound::EmitSound ),
		OVERLOAD( "EngineSoundVtbl", "EmitSound_2", void ( IEngineSound::* )( IRecipientFilter &, int, int, const char *, float, soundlevel_t, int, int, int, const Vector *, const Vector *, CUtlVector<Vector> *, bool, float, int ), &IEngineSound::EmitSound ),
		OVERLOAD( "SpatialPartitionVtbl", "CreateHandle", SpatialPartitionHandle_t ( ISpatialPartition::* )( IHandleEntity * ), &ISpatialPartition::CreateHandle ),
		OVERLOAD( "SpatialPartitionVtbl", "CreateHandle_2", SpatialPartitionHandle_t ( ISpatialPartition::* )( IHandleEntity *, SpatialPartitionListMask_t, const Vector &, const Vector & ), &ISpatialPartition::CreateHandle ),
		OVERLOAD( "SpatialPartitionVtbl", "Remove", void ( ISpatialPartition::* )( SpatialPartitionListMask_t, SpatialPartitionHandle_t ), &ISpatialPartition::Remove ),
		OVERLOAD( "SpatialPartitionVtbl", "Remove_2", void ( ISpatialPartition::* )( SpatialPartitionHandle_t ), &ISpatialPartition::Remove ),
		OVERLOAD( "StaticPropMgrClientVtbl", "IsStaticProp", bool ( IStaticPropMgr::* )( IHandleEntity * ) const, &IStaticPropMgr::IsStaticProp ),
		OVERLOAD( "StaticPropMgrClientVtbl", "Refused_IsStaticProp", bool ( IStaticPropMgr::* )( CBaseHandle ) const, &IStaticPropMgr::IsStaticProp ),
		OVERLOAD( "StaticPropMgrServerVtbl", "IsStaticProp", bool ( IStaticPropMgr::* )( IHandleEntity * ) const, &IStaticPropMgr::IsStaticProp ),
		OVERLOAD( "StaticPropMgrServerVtbl", "Refused_IsStaticProp", bool ( IStaticPropMgr::* )( CBaseHandle ) const, &IStaticPropMgr::IsStaticProp ),
		OVERLOAD( "InputSystemVtbl", "GetActionSetHandle", ControllerActionSetHandle_t ( IInputSystem::* )( GameActionSet_t ), &IInputSystem::GetActionSetHandle ),
		OVERLOAD( "InputSystemVtbl", "GetActionSetHandle_2", ControllerActionSetHandle_t ( IInputSystem::* )( const char * ), &IInputSystem::GetActionSetHandle ),
		OVERLOAD( "InputSystemVtbl", "GetSteamControllerActionOrigin", EControllerActionOrigin ( IInputSystem::* )( const char *, GameActionSet_t ), &IInputSystem::GetSteamControllerActionOrigin ),
		OVERLOAD( "InputSystemVtbl", "GetSteamControllerActionOrigin_2", EControllerActionOrigin ( IInputSystem::* )( const char *, ControllerActionSetHandle_t ), &IInputSystem::GetSteamControllerActionOrigin ),
		OVERLOAD( "CvarVtbl", "FindCommandBase", ConCommandBase * ( ICvar::* )( const char * ), &ICvar::FindCommandBase ),
		OVERLOAD( "CvarVtbl", "FindCommandBase_2", const ConCommandBase * ( ICvar::* )( const char * ) const, &ICvar::FindCommandBase ),
		OVERLOAD( "CvarVtbl", "FindVar", ConVar * ( ICvar::* )( const char * ), &ICvar::FindVar ),
		OVERLOAD( "CvarVtbl", "FindVar_2", const ConVar * ( ICvar::* )( const char * ) const, &ICvar::FindVar ),
		OVERLOAD( "CvarVtbl", "FindCommand", ConCommand * ( ICvar::* )( const char * ), &ICvar::FindCommand ),
		OVERLOAD( "CvarVtbl", "FindCommand_2", const ConCommand * ( ICvar::* )( const char * ) const, &ICvar::FindCommand ),
		OVERLOAD( "CvarVtbl", "GetCommands", ConCommandBase * ( ICvar::* )(), &ICvar::GetCommands ),
		OVERLOAD( "CvarVtbl", "GetCommands_2", const ConCommandBase * ( ICvar::* )() const, &ICvar::GetCommands ),
		OVERLOAD( "CvarVtbl", "QueueMaterialThreadSetValue", void ( ICvar::* )( ConVar *, const char * ), &ICvar::QueueMaterialThreadSetValue ),
		OVERLOAD( "CvarVtbl", "QueueMaterialThreadSetValue_2", void ( ICvar::* )( ConVar *, int ), &ICvar::QueueMaterialThreadSetValue ),
		OVERLOAD( "CvarVtbl", "QueueMaterialThreadSetValue_3", void ( ICvar::* )( ConVar *, float ), &ICvar::QueueMaterialThreadSetValue ),
		OVERLOAD( "PhysicsCollisionVtbl", "TraceBox", void ( IPhysicsCollision::* )( const Vector &, const Vector &, const Vector &, const Vector &, const CPhysCollide *, const Vector &, const QAngle &, trace_t * ), &IPhysicsCollision::TraceBox ),
		OVERLOAD( "PhysicsCollisionVtbl", "TraceBox_2", void ( IPhysicsCollision::* )( const Ray_t &, const CPhysCollide *, const Vector &, const QAngle &, trace_t * ), &IPhysicsCollision::TraceBox ),
		OVERLOAD( "PhysicsCollisionVtbl", "TraceBox_3", void ( IPhysicsCollision::* )( const Ray_t &, unsigned int, IConvexInfo *, const CPhysCollide *, const Vector &, const QAngle &, trace_t * ), &IPhysicsCollision::TraceBox ),
		OVERLOAD( "SteamCallbackVtbl", "Run", void ( CCallbackBase::* )( void * ), &CCallbackBase::Run ),
		OVERLOAD( "SteamCallbackVtbl", "Run_2", void ( CCallbackBase::* )( void *, bool, SteamAPICall_t ), &CCallbackBase::Run ),
	};

	const std::string layout = ReadLayout();

	for ( const OverloadCase &c : cases )
	{
		EXPECT_LE( 0, c.compiler ) << c.structName << "." << c.field << " is not virtual";
		EXPECT_EQ( c.compiler, SlotIn( layout, c.structName, c.field ) ) << c.structName << "." << c.field;
	}
}

// THE ARM THAT SAYS THE TABLE ABOVE CAN FAIL. SlotIn answers -1 for a name the
// layout does not have, and a pair read the wrong way round differs.
TEST( VtableOverloadOrder, ALookupOfAMissingSlotAnswersMinusOne )
{
	EXPECT_EQ( -1, SlotIn( ReadLayout(), "BaseFileSystemVtbl", "NoSuchSlot" ) );
	EXPECT_EQ( -1, SlotIn( ReadLayout(), "NoSuchVtblStructExists", "Size" ) );
	EXPECT_NE( SlotIn( ReadLayout(), "BaseFileSystemVtbl", "Size" ),
	           SlotIn( ReadLayout(), "BaseFileSystemVtbl", "Size_2" ) );
}

// A layout both ABIs order alike has no [VtableSlot]: its slot is the number
// leading the trailing comment, on either.
TEST( VtableOverloadOrder, ASlotOfALayoutBothAbisOrderAlikeIsItsDeclarationOrder )
{
	EXPECT_EQ( VTableHook::MemberOf( &IVEngineClient::GetProtocolVersion ).slot,
	           SlotIn( ReadLayout(), "EngineClientVtbl", "GetProtocolVersion" ) );
}

TEST( VtableLayoutLength, EveryGeneratedStructIsCoveredByATestAbove )
{
	// THE ARM THAT NOTICES A LAYOUT NOBODY CHECKED. tools/vtable-layout emits
	// one struct per interface it knows about; if a later lane adds one and no
	// test here names it, its length is a claim with nothing behind it.
	const char *const kCovered[] = {
		"UniformRandomStreamVtbl", "ModelInfoVtbl", "BaseFileSystemVtbl",
		"EngineClientVtbl", "EngineServerVtbl", "ModelInfoClientVtbl",
		"RenderViewVtbl", "DebugOverlayVtbl", "SoundEmitterSystemVtbl",
		"ShadowMgrVtbl", "EngineTraceVtbl", "EngineSoundVtbl", "EffectsVtbl",
		"ModelRenderVtbl", "EngineVGuiVtbl", "SpatialPartitionVtbl",
		"GameEventManagerVtbl", "NetworkStringTableContainerVtbl", "NetworkStringTableVtbl",
		"NetChannelInfoVtbl",
		"StaticPropMgrClientVtbl", "StaticPropMgrServerVtbl", "InputSystemVtbl",
		"GameUIFuncsVtbl", "DataCacheVtbl", "SceneFileCacheVtbl", "CvarVtbl",
		"PhysicsVtbl", "PhysicsCollisionVtbl", "PhysicsSurfacePropsVtbl",
		"PhysicsEnvironmentVtbl", "PhysicsObjectVtbl", "PhysicsShadowControllerVtbl",
		"PhysicsMotionControllerVtbl", "PhysicsFrictionSnapshotVtbl", "PhysicsCollisionDataVtbl",
		"PhysicsFluidControllerVtbl", "PhysicsConstraintVtbl", "PhysicsSpringVtbl",
		"PhysicsPlayerControllerVtbl",
		"PhysicsObjectEventVtbl", "PhysicsCollisionEventVtbl", "PhysicsCollisionSolverVtbl",
		"PhysicsConstraintEventVtbl", "MotionEventVtbl",
		"VPhysicsKeyParserVtbl", "PhysicsCollisionSetVtbl", "PhysicsConstraintGroupVtbl",
		"VPhysicsKeyHandlerVtbl",
		"SteamCallbackVtbl",
		"VoiceServerVtbl", "ServerPluginHelpersVtbl", "UploadGameStatsVtbl",

		// The shim layer. Walked and checked exactly like the engine's, which
		// is the point of the route rather than an economy of it.
		"ShimFrameVtbl", "ShimLevelVtbl", "ShimRootVtbl", "ShimConsoleVtbl",
		// plan_particles_so.md: libss_particles.so's own export, served by the
		// client's internal factory.
		"ParticleLibVtbl",

		// Phase D: the tables MANAGED writes and native calls.
		"ManagedRootVtbl", "ManagedRuntimeServerVtbl", "ManagedRuntimeClientVtbl",
	};

	const std::string layout = ReadLayout();
	const int covered = (int)( sizeof( kCovered ) / sizeof( kCovered[ 0 ] ) );

	const int areas = (int)( sizeof( kShimAreasCovered ) / sizeof( kShimAreasCovered[ 0 ] ) );
	const int managedAreas = (int)( sizeof( kManagedAreasCovered ) / sizeof( kManagedAreasCovered[ 0 ] ) );

	// Every name above must really be in the file...
	for ( int i = 0; i < covered; i++ )
		EXPECT_NE( -1, FieldsIn( layout, kCovered[ i ] ) ) << kCovered[ i ];
	for ( int i = 0; i < areas; i++ )
		EXPECT_NE( -1, FieldsIn( layout, kShimAreasCovered[ i ] ) ) << kShimAreasCovered[ i ];
	for ( int i = 0; i < managedAreas; i++ )
		EXPECT_NE( -1, FieldsIn( layout, kManagedAreasCovered[ i ] ) ) << kManagedAreasCovered[ i ];

	// ...and the file must hold no OTHER struct.
	int emitted = 0;
	size_t at = 0;
	const std::string needle = " unsafe struct ";   // internal or public

	while ( ( at = layout.find( needle, at ) ) != std::string::npos )
	{
		emitted++;
		at += needle.size();
	}

	EXPECT_EQ( covered + areas + kManagedAreasGenerated, emitted )
		<< "a generated struct has no length test naming it";
}
