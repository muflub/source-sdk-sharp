//========= SourceSharp — animation POLICY, and what native code may still do =//
//
// plan_entity.md §3.7 splits animation in two. Turning a sequence, a cycle, a
// set of pose parameters and a bone-controller table into matrices is
// arithmetic over mdl data and stays in C++ as a leaf library (§6). Everything
// that DECIDES — which sequence, what cycle, which layers at what weight, when
// an event fires, which hitbox set the model is wearing — is managed.
//
// Step 6d is the second half. What is checkable HERE, without an engine, is the
// shape of what is left behind in the shim, and there are two things worth
// pinning because each of them failed silently once already:
//
//   1. NO DURATION IS READ THROUGH AN ARRAY OF ZEROES. A sequence's length can
//      depend on a pose parameter — a blend authored short at one end of a
//      movement range and long at the other, which is how one sequence is a
//      walk and a run. Five places in this shim handed Studio_Duration a
//      `static float s_flPoseParameter[MAXSTUDIOPOSEPARAM]` that nothing ever
//      wrote. Zero is the START of each declared range and not its middle, so
//      every one of those durations was read at one extreme of the blend, and
//      every cycle computed from it was wrong for the whole sequence. Nothing
//      logged, and a gesture at the wrong speed looks like a gesture.
//
//      Step 4d fixed one, 6c reported three more and left them (its fixture was
//      recorded against them), and a FIFTH — the standalone arm of the server's
//      own duration ask — was in nobody's list. Which is why this test walks
//      for the IDENTIFIER over the whole shim rather than checking the files
//      already known to have carried it.
//
//   2. THE HITBOX SET COMES FROM THE BLOCK. It was the literal 0 in two places,
//      and 0 is every set every model in this mod declares — so the wrong
//      answer and the right one are the same number today and will not be the
//      day a model carries two.
//
//   3. DoAnimationEvents IS A NO-OP, and that is a MOVE rather than a deletion:
//      managed code asks the leaf library for the events between the previous
//      cycle and this one when it advances the cycle, so it already knows which
//      fired. The doors it asks through are asserted here beside the empty
//      body, because "we deleted it and replaced it with nothing" and "we
//      deleted it and replaced it with a batch" are the same diff.
//
// COMMENTS ARE STRIPPED BEFORE ANY OF THIS IS MATCHED. Every file under test is
// full of prose ABOUT the bug being asserted gone — this file included — and a
// needle that matched the prose would pass on a tree that still had the bug.
// That has happened in this project: a tombstone comment put deleted cast sites
// straight back into a census.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Where the shim is, from wherever the test binary was started.
//-----------------------------------------------------------------------------
const char *ShimRoot()
{
	static const char *kCandidates[] = { "sourcesharp", "src/sourcesharp",
	                                     "../src/sourcesharp" };

	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		DIR *pDir = opendir( kCandidates[ i ] );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadShimFile( const std::string &strRelative )
{
	const char *pszRoot = ShimRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += "/";
	strPath += strRelative;

	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

//-----------------------------------------------------------------------------
// // and /* */ removed. Not a C++ lexer and it does not need to be: what is
// built on it is an identifier search, and the identifiers being searched for
// do not occur inside string literals in these files.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );

	for ( size_t i = 0; i < s.size(); )
	{
		if ( i + 1 < s.size() && s[ i ] == '/' && s[ i + 1 ] == '/' )
		{
			while ( i < s.size() && s[ i ] != '\n' )
				i++;
			continue;
		}
		if ( i + 1 < s.size() && s[ i ] == '/' && s[ i + 1 ] == '*' )
		{
			i += 2;
			while ( i + 1 < s.size() && !( s[ i ] == '*' && s[ i + 1 ] == '/' ) )
				i++;
			i = ( i + 1 < s.size() ) ? i + 2 : s.size();
			continue;
		}
		out += s[ i ];
		i++;
	}
	return out;
}

// The model descriptor's reader: managed since studio.cpp's eviction.
const char *const kDescriptorReader = "managed/SourceSharp.Shared/Bones/StudioModelDescriptor.cs";

const std::string &Code( const std::string &strRelative )
{
	// One cache per path, because every test below reads two or three of the
	// same handful of files and each is a few hundred kilobytes.
	static std::vector< std::pair< std::string, std::string > > s_cache;

	for ( size_t i = 0; i < s_cache.size(); i++ )
	{
		if ( s_cache[i].first == strRelative )
			return s_cache[i].second;
	}

	s_cache.push_back( std::make_pair( strRelative,
	                                   StripComments( ReadShimFile( strRelative ) ) ) );
	return s_cache.back().second;
}

int Occurrences( const std::string &strHaystack, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t at = strHaystack.find( strNeedle ); at != std::string::npos;
	      at = strHaystack.find( strNeedle, at + strNeedle.size() ) )
		n++;
	return n;
}

//-----------------------------------------------------------------------------
// The body of one function, brace-balanced from the first '{' after its
// signature.
//
// BALANCED AND NOT "TO THE NEXT BLANK LINE", which is the mistake that let a
// mutation survive in this tree once: a check that measured brace depth was
// defeated by a braceless `for`. Counting braces from the signature has no such
// hole, because what it is looking for is the WHOLE body and the closing brace
// of the whole body is the one that returns the depth to zero.
//-----------------------------------------------------------------------------
std::string BodyOf( const std::string &strCode, const std::string &strSignature )
{
	const size_t at = strCode.find( strSignature );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = strCode.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	int depth = 0;
	for ( size_t i = open; i < strCode.size(); i++ )
	{
		if ( strCode[i] == '{' )
			depth++;
		else if ( strCode[i] == '}' )
		{
			depth--;
			if ( depth == 0 )
				return strCode.substr( open, i + 1 - open );
		}
	}
	return std::string();
}

//-----------------------------------------------------------------------------
// Every .cpp and .h under the shim, so that a sixth copy of a named bug is
// found by the test rather than by the next person to grep.
//-----------------------------------------------------------------------------
void CollectSources( const std::string &strDir, std::vector< std::string > &out )
{
	DIR *pDir = opendir( strDir.c_str() );
	if ( !pDir )
		return;

	while ( struct dirent *pEntry = readdir( pDir ) )
	{
		const std::string name( pEntry->d_name );

		if ( name == "." || name == ".." )
			continue;

		const std::string strPath = strDir + "/" + name;

		if ( pEntry->d_type == DT_DIR )
		{
			CollectSources( strPath, out );
			continue;
		}

		const size_t dot = name.rfind( '.' );
		if ( dot == std::string::npos )
			continue;

		const std::string ext = name.substr( dot );
		if ( ext == ".cpp" || ext == ".h" )
			out.push_back( strPath );
	}

	closedir( pDir );
}

const std::vector< std::string > &ShimSources()
{
	static std::vector< std::string > s_files;
	static bool s_bDone = false;

	if ( !s_bDone )
	{
		s_bDone = true;
		const char *pszRoot = ShimRoot();
		if ( pszRoot )
		{
			CollectSources( std::string( pszRoot ) + "/game", s_files );
			CollectSources( std::string( pszRoot ) + "/host", s_files );
		}
	}
	return s_files;
}

const char *kClientShell = "game/client/shell_standalone_client.cpp";
const char *kClientShellH = "game/client/shell_standalone_client.h";
const char *kServerHierarchy = "game/server/shell_standalone_hierarchy.cpp";
const char *kServerScripted = "game/server/shell_scripted.cpp";

//=============================================================================//
// 0. The check can fail, which is the first thing to establish about a test
//    made of string searches over files it locates by guessing.
//=============================================================================//

TEST( AnimationPolicy, TheShimIsWhereThisTestThinksItIs )
{
	ASSERT_TRUE( ShimRoot() != NULL ) << "no sourcesharp/ under the working directory";
	EXPECT_GT( ShimSources().size(), 100u );
}

TEST( AnimationPolicy, EveryFileUnderTestWasActuallyRead )
{
	// A SIZE AND A TOKEN, because either alone is satisfiable by the wrong
	// thing: a size floor passes on any large file the guesser happened to
	// find, and a token passes on a file that is nothing but that token. The
	// tokens are each the class the file defines, which is the one string that
	// cannot be true of any of the other three.
	EXPECT_GT( Code( kClientShell ).size(), 50000u );
	EXPECT_GT( Occurrences( Code( kClientShell ), "C_StandaloneEntity::" ), 50 );

	EXPECT_GT( Code( kClientShellH ).size(), 5000u );
	EXPECT_GT( Occurrences( Code( kClientShellH ), "class C_StandaloneEntity" ), 0 );

	EXPECT_GT( Code( kServerHierarchy ).size(), 5000u );
	EXPECT_GT( Occurrences( Code( kServerHierarchy ), "CStandaloneEntity::TestHitboxes" ), 0 );
	EXPECT_GT( Occurrences( Code( kServerHierarchy ), "IManagedBones" ), 0 );

	EXPECT_GT( Code( kServerScripted ).size(), 5000u );
	EXPECT_GT( Occurrences( Code( kServerScripted ), "SourceSharp_Entity" ), 5 );   // 10 -> 5: the mover slots left (plan_server_native_v2 T2)
}

//=============================================================================//
// 1. No duration is read through an array of zeroes. ANYWHERE.
//=============================================================================//

TEST( AnimationPolicy, NoShimSourceHandsAStudioCallAFileStaticPoseArray )
{
	std::vector< std::string > offenders;

	const std::vector< std::string > &files = ShimSources();

	for ( size_t i = 0; i < files.size(); i++ )
	{
		std::ifstream file( files[i].c_str() );
		if ( !file.is_open() )
			continue;

		const std::string stripped = StripComments(
			std::string( ( std::istreambuf_iterator< char >( file ) ),
			             std::istreambuf_iterator< char >() ) );

		if ( stripped.find( "s_flPoseParameter" ) != std::string::npos )
			offenders.push_back( files[i] );
	}

	std::string strList;
	for ( size_t i = 0; i < offenders.size(); i++ )
		strList += "\n    " + offenders[i];

	EXPECT_TRUE( offenders.empty() )
		<< "a duration read through an array of zeroes, in:" << strList;
}

// THE CLIENT'S SKELETON IS MANAGED TOO (plan physics_bones_native_zero 1.2,
// client half): SourceSharp.Client's ClientSkeletons over ClientBoneDriver.
// What these held -- a sequence's and a layer's length read through the
// library at this entity's real pose -- is ClientBoneDriver.SetPlayingSequence
// / SetAnimLayer over BoneLibrary.SequenceFacts( hdr, sequence,
// state.PoseParametersForDuration( model ) ) (BonesClientDriverTests,
// ClientSkeletonsTests). What is pinned here is that no native body reads a
// length any more: native only STORES the clock managed chose.
TEST( AnimationPolicy, TheClientsBaseSequenceLengthComesFromTheLibrary )
{
	const std::string &code = Code( kClientShell );

	EXPECT_TRUE( BodyOf( code, "void C_StandaloneEntity::SetPlayingSequence(" ).empty() );

	const std::string store = BodyOf( code, "void C_StandaloneEntity::StoreSequence(" );

	ASSERT_FALSE( store.empty() );
	EXPECT_EQ( 0, Occurrences( store, "SequenceFacts" ) );
	EXPECT_EQ( 0, Occurrences( store, "Studio_Duration" ) );
	EXPECT_EQ( 0, Occurrences( code, "BoneLibrary_SequenceFacts" ) );
}

TEST( AnimationPolicy, TheClientsLayerLengthComesFromTheLibraryToo )
{
	const std::string &code = Code( kClientShell );

	EXPECT_TRUE( BodyOf( code, "void C_StandaloneEntity::SetAnimLayer(" ).empty() );

	// The slot that reached it is retired with its row.
	ASSERT_GT( code.size(), 50000u );
	EXPECT_TRUE( BodyOf( code, "void SourceSharp_StandaloneSetAnimLayer(" ).empty() );
}

//-----------------------------------------------------------------------------
// THE TWO DURATION-SLOT FACTS ARE RETIRED WITH THEIR SUBJECT -- plan_shim.md
// Phase 2d.
//
// They were the client's and the server's arms of
// SourceSharp_EntitySequenceDuration, and each asserted the same thing: ask
// BoneLibrary_SequenceFacts, never Studio_Duration directly, because
// SequenceFacts materialises the model's OWN default pose parameters and a
// zeroed array is a different pose on any model whose parameters do not have
// zero in range.
//
// THE SLOT IS RETIRED. A sequence's duration at the default pose is a fixed
// number per sequence, so it rides in the ModelDescriptor and crosses once per
// model instead of once per ask.
//
// THE CLAIM IS NOT LOST, AND SINCE 2026-10-12 IT IS C#'s. It moved first to
// model_descriptor.cpp's walk (Studio_CalcDefaultPoseParameters, then
// Studio_Duration over the result), then out of native code: StudioSequenceTiming
// computes it from the side's managed StudioHdr, seeded with the model's own
// defaults (StudioSequenceTimingTests.TheBlendIsTimedAtTheModelsOwnDefaultPose
// holds the rule). Since 2026-10-18 the whole descriptor is managed's
// (StudioModelDescriptor; studio.cpp's eviction) and the walk is gone.
// TheDescriptorIsTimedInManagedCode below holds both halves: the managed
// reader times each sequence, and the native walk has not come back.
//-----------------------------------------------------------------------------
TEST( AnimationPolicy, TheDescriptorIsTimedInManagedCode )
{
	const std::string reader = Code( kDescriptorReader );

	ASSERT_GT( reader.size(), 1000u )
		<< kDescriptorReader << " could not be read, so this fact asserts "
		   "nothing about where a sequence's duration is computed.";

	EXPECT_GT( Occurrences( reader, "StudioSequenceTiming.DefaultDurations(hdr)" ), 0 )
		<< "the managed descriptor reader no longer times its sequences; every "
		   "duration would answer 0 and a sequence would play in one frame.";

	EXPECT_TRUE( ReadShimFile( "game/shared/model_descriptor.cpp" ).empty() )
		<< "model_descriptor.cpp is back: the descriptor is the managed "
		   "StudioModelDescriptor's, read from the side's StudioHdr.";
}

TEST( AnimationPolicy, TheGroundSpeedWalkMovesTheModelWithARealPose )
{
	// Managed: ClientSkeletons.SequenceGroundSpeed walks Studio_SeqMovement's
	// port with a REAL array -- the entity's pose, or the model's defaults
	// materialised (ClientSkeletonsTests). The native slot is retired.
	const std::string &code = Code( kClientShell );

	ASSERT_GT( code.size(), 50000u );
	EXPECT_TRUE( BodyOf( code, "float SourceSharp_StandaloneSequenceGroundSpeed(" ).empty() );
	EXPECT_EQ( 0, Occurrences( code, "Studio_SeqMovement" ) );
}

// THE SERVER'S SKELETON IS MANAGED (plan physics_bones_native_zero 1.2):
// SourceSharp.Server's ServerSkeletons over ServerBoneDriver. These facts held
// the native bodies; what they asserted is now held in managed code --
// ServerSkeletonsTests, BonesServerDriverTests -- and what is pinned here is
// that the native bodies are GONE, so a native skeleton cannot come back
// beside the managed one and the two disagree.
TEST( AnimationPolicy, TheServersOwnSequenceFactsPassTheModelsDefaults )
{
	// Managed: ServerBoneDriver.SetPoseSequence reads its duration with
	// BoneLibrary.SequenceFacts( hdr, sequence, null ) -- the model's defaults
	// (BonesServerDriverTests). No native body may read one.
	const std::string &code = Code( kServerHierarchy );

	ASSERT_GT( code.size(), 5000u );
	EXPECT_TRUE( BodyOf( code, "void CStandaloneEntity::SetPoseSequence(" ).empty() );
	EXPECT_EQ( 0, Occurrences( code, "BoneLibrary_SequenceFacts" ) );
	EXPECT_EQ( 0, Occurrences( code, "Studio_Duration" ) );
}

TEST( AnimationPolicy, ThePoseHandedToADurationIsNullUntilTheDefaultsAreForThisModel )
{
	// The rule lives in BoneEntityState.PoseParametersForDuration now (null
	// until the defaults were read for the model worn: BonesEntityStateTests);
	// the native accessor and the pose it guarded are gone with the skeleton.
	const std::string &header = Code( kClientShellH );

	ASSERT_GT( header.size(), 5000u );
	EXPECT_EQ( 0, Occurrences( header, "PoseParametersForDuration" ) );
	EXPECT_EQ( 0, Occurrences( header, "m_flPoseParameter" ) );
	EXPECT_EQ( 0, Occurrences( header, "m_nPoseDefaultsFor" ) );
}

//=============================================================================//
// 2. The hitbox set is a decision, so it comes out of the block.
//=============================================================================//

TEST( AnimationPolicy, TheServerTracesAgainstTheHitboxSetTheBlockNames )
{
	// The thunk hands managed the entity's whole block, and ServerSkeletons
	// traces against read.Client.HitboxSet out of it
	// (ServerSkeletonsTests.AHitboxSetTheModelLacksTestsNothing): the set is
	// still the block's word, read on the other side.
	const std::string body = BodyOf( Code( kServerHierarchy ),
		"bool CStandaloneEntity::TestHitboxes(" );

	ASSERT_FALSE( body.empty() );
	EXPECT_GT( Occurrences( body, "&Block()" ), 0 );
	EXPECT_GT( Occurrences( body, "->TestHitboxes(" ), 0 );
	EXPECT_EQ( 0, Occurrences( body, "nHitboxSet=0" ) );
	EXPECT_EQ( 0, Occurrences( body, "BoneLibrary_TraceHitboxes" ) );
}

TEST( AnimationPolicy, TheClientDrawsWithTheHitboxSetTheBlockNames )
{
	const std::string &code = Code( kClientShell );

	EXPECT_GT( Occurrences( code, "info.hitboxset    = Block().Client.HitboxSet;" ), 0 );
	EXPECT_EQ( 0, Occurrences( code, "info.hitboxset    = 0;" ) );
}

//-----------------------------------------------------------------------------
// The Nth argument of a call, by splitting the argument list at commas that are
// not inside a nested paren, bracket or brace.
//-----------------------------------------------------------------------------
std::string ArgumentOf( const std::string &strCode, size_t atCall, int nWanted )
{
	size_t i = strCode.find( '(', atCall );
	if ( i == std::string::npos )
		return std::string();

	int depth = 0;
	int nArg = 0;
	size_t begin = i + 1;

	for ( ; i < strCode.size(); i++ )
	{
		const char c = strCode[i];

		if ( c == '(' || c == '[' || c == '{' )
			depth++;
		else if ( c == ')' || c == ']' || c == '}' )
		{
			depth--;
			if ( depth == 0 )
				break;
		}
		else if ( c == ',' && depth == 1 )
		{
			if ( nArg == nWanted )
				return strCode.substr( begin, i - begin );
			nArg++;
			begin = i + 1;
		}
	}

	if ( nArg == nWanted && i < strCode.size() )
		return strCode.substr( begin, i - begin );

	return std::string();
}

std::string Trimmed( const std::string &s )
{
	size_t a = 0, b = s.size();
	while ( a < b && ( s[a] == ' ' || s[a] == '\t' || s[a] == '\n' || s[a] == '\r' ) ) a++;
	while ( b > a && ( s[b-1] == ' ' || s[b-1] == '\t' || s[b-1] == '\n' || s[b-1] == '\r' ) ) b--;
	return s.substr( a, b - a );
}

bool IsIntegerLiteral( const std::string &s )
{
	if ( s.empty() )
		return false;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		if ( s[i] < '0' || s[i] > '9' )
			return false;
	}
	return true;
}

TEST( AnimationPolicy, NoHitboxAskInTheShimPassesALiteral )
{
	// POSITIVE, AND MEASURED RATHER THAN ARGUED. The first version of this
	// hunted for the string `/*nHitboxSet=*/0` -- which lives INSIDE a comment,
	// and this test strips comments before it looks. Pointed at a build with
	// the hard-coded 0 restored on the server, it PASSED. That is the shape of
	// check this project has been misled by more than forty times.
	//
	// So the rule is now about the ARGUMENT rather than about a spelling: the
	// third argument of every hitbox ray in the shim, and the right-hand side
	// of every ModelRenderInfo_t::hitboxset write, must not be an integer
	// literal. Any literal fails, not one particular one.
	const std::vector< std::string > &files = ShimSources();

	int nRays = 0;
	int nDraws = 0;
	int nFromBlock = 0;
	std::vector< std::string > offenders;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		// bone_library.* DECLARES and DEFINES the ray; it takes the set as an
		// argument and never chooses one, which is the whole point of it being
		// a leaf library. Its two mentions are not call sites.
		if ( files[i].find( "bone_library." ) != std::string::npos )
			continue;

		std::ifstream file( files[i].c_str() );
		if ( !file.is_open() )
			continue;

		const std::string code = StripComments(
			std::string( ( std::istreambuf_iterator< char >( file ) ),
			             std::istreambuf_iterator< char >() ) );

		for ( size_t at = code.find( "BoneLibrary_TraceHitboxes(" );
		      at != std::string::npos;
		      at = code.find( "BoneLibrary_TraceHitboxes(", at + 1 ) )
		{
			nRays++;

			const std::string arg = Trimmed( ArgumentOf( code, at, 2 ) );

			if ( IsIntegerLiteral( arg ) )
				offenders.push_back( files[i] + "  ray set = " + arg );

			if ( arg == "Block().Client.HitboxSet" )
				nFromBlock++;
		}

		for ( size_t at = code.find( "info.hitboxset" );
		      at != std::string::npos;
		      at = code.find( "info.hitboxset", at + 1 ) )
		{
			const size_t eq = code.find( '=', at );
			const size_t semi = code.find( ';', at );

			if ( eq == std::string::npos || semi == std::string::npos || eq > semi )
				continue;

			nDraws++;

			const std::string rhs = Trimmed( code.substr( eq + 1, semi - eq - 1 ) );

			if ( IsIntegerLiteral( rhs ) )
				offenders.push_back( files[i] + "  draw set = " + rhs );

			if ( rhs == "Block().Client.HitboxSet" )
				nFromBlock++;
		}
	}

	// NOTHING COMPARED IS A FAILURE, in each category: an empty offender list
	// is also what a walk that found no call sites produces.
	//
	// THREE rays and not one. The second is cs_sv_boneprobe's replay arm, which
	// takes the set from the RECORDED ROW -- a fixture asking the model about a
	// set it wrote down is not the game deciding anything, and pinning the
	// count is how a third one gets noticed. The third arrived with the client
	// prop's collideable: C_StandaloneEntity::TestHitboxes in
	// shell_standalone_client.cpp, the custom-ray/box-test arm the engine clip
	// calls, whose set is Block().Client.HitboxSet received from the server --
	// the same block word as the draw, so the clip and the renderer cannot
	// disagree about which set the model is wearing.
	//
	// ONE ray since plan physics_bones_native_zero 1.2: the server's ray is
	// managed (its set is the block's, read by ServerSkeletons out of the block
	// the thunk hands it -- TheServerTracesAgainstTheHitboxSetTheBlockNames),
	// and the replay arm went with shell_standalone_hierarchy_debug.cpp.
	//
	// NONE since the same step's client half: the client prop's ray is
	// ClientSkeletons', which traces against read.Client.HitboxSet out of the
	// block its thunk hands over (ClientSkeletonsTests). The draw is the one
	// native site left, and it names the block.
	EXPECT_EQ( 0, nRays );
	EXPECT_EQ( 1, nDraws );
	EXPECT_EQ( 1, nFromBlock ) << "the draw, naming the block";

	std::string strList;
	for ( size_t i = 0; i < offenders.size(); i++ )
		strList += "\n    " + offenders[i];

	EXPECT_TRUE( offenders.empty() ) << "a hard-coded hitbox set, in:" << strList;
}

//=============================================================================//
// 3. The animation CLOCK is in the block, on both sides, in the same words.
//
// It was three bespoke members on each side under six different names. The
// client's move is recorded in its own header; the server's is step 6d's, and
// what makes it worth a test rather than a comment is that the two sides
// drifting apart here is invisible: each is self-consistent, and the two are
// never compared by anything the game runs.
//=============================================================================//

TEST( AnimationPolicy, TheServerNoLongerKeepsItsAnimationClockInMembers )
{
	const std::string &header = Code( "game/server/shell_standalone.h" );

	ASSERT_GT( header.size(), 5000u );
	EXPECT_GT( Occurrences( header, "class CStandaloneEntity" ), 0 );

	EXPECT_EQ( 0, Occurrences( header, "m_nPoseSequence" ) );
	EXPECT_EQ( 0, Occurrences( header, "m_flPosePlaybackRate" ) );
	EXPECT_EQ( 0, Occurrences( header, "m_flPoseSequenceStart" ) );
}

TEST( AnimationPolicy, NoServerSourceStillReadsTheMovedMembers )
{
	// The header having lost the declaration is not the same fact as nothing
	// reading it -- a stale read would not compile, but a stale read behind an
	// #ifdef the game build does not take would.
	const std::vector< std::string > &files = ShimSources();

	std::vector< std::string > offenders;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		std::ifstream file( files[i].c_str() );
		if ( !file.is_open() )
			continue;

		const std::string code = StripComments(
			std::string( ( std::istreambuf_iterator< char >( file ) ),
			             std::istreambuf_iterator< char >() ) );

		if ( code.find( "m_nPoseSequence" ) != std::string::npos ||
		     code.find( "m_flPosePlaybackRate" ) != std::string::npos ||
		     code.find( "m_flPoseSequenceStart" ) != std::string::npos )
			offenders.push_back( files[i] );
	}

	std::string strList;
	for ( size_t i = 0; i < offenders.size(); i++ )
		strList += "\n    " + offenders[i];

	EXPECT_TRUE( offenders.empty() ) << "a moved animation member, in:" << strList;
}

// THE SERVER'S SKELETON IS MANAGED (plan physics_bones_native_zero 1.2):
// SourceSharp.Server's ServerSkeletons over ServerBoneDriver. These facts held
// the native bodies; what they asserted is now held in managed code --
// ServerSkeletonsTests, BonesServerDriverTests -- and what is pinned here is
// that the native bodies are GONE, so a native skeleton cannot come back
// beside the managed one and the two disagree.
TEST( AnimationPolicy, TheServerWritesItsAnimationClockIntoTheBlock )
{
	// Managed: ServerSkeletons.SetSequence writes Client.Sequence,
	// PlaybackRate and AnimTime into the block
	// (ServerSkeletonsTests.SetSequenceWritesTheClockIntoTheBlock).
	const std::string &code = Code( kServerHierarchy );

	ASSERT_GT( code.size(), 5000u );
	EXPECT_TRUE( BodyOf( code, "void CStandaloneEntity::SetPoseSequence(" ).empty() );
	EXPECT_EQ( 0, Occurrences( code, "Block().Client.Sequence" ) );
}

TEST( AnimationPolicy, TheServerReadsItsCycleBackOutOfTheBlock )
{
	// Managed: a skeleton takes its clock from the block
	// (ServerSkeletonsTests.AFreshSkeletonTakesItsClockFromTheBlock) and runs
	// BoneLibraryMath.Cycle over it. No native cycle remains.
	const std::string &code = Code( kServerHierarchy );

	ASSERT_GT( code.size(), 5000u );
	EXPECT_TRUE( BodyOf( code, "float CStandaloneEntity::PoseCycle() const" ).empty() );
	EXPECT_EQ( 0, Occurrences( code, "BoneLibrary_Cycle" ) );
}

TEST( AnimationPolicy, BothSidesWriteTheSameThreeWords )
{
	// The point of the move, and the thing a per-side test cannot state: each
	// side is self-consistent whatever it chose, and nothing the game runs
	// compares them.
	//
	// The server's three writes are managed since plan
	// physics_bones_native_zero 1.2 (ServerSkeletons.WriteClock, the same three
	// EngineBlockClientTail fields; ServerSkeletonsTests holds them), so the
	// client's are what is checked here: the store of the clock its managed
	// skeleton chose (ClientSkeletons.SetSequence -> StandaloneStoreSequence).
	const std::string client = BodyOf( Code( kClientShell ),
		"void C_StandaloneEntity::StoreSequence(" );

	ASSERT_FALSE( client.empty() );

	static const char *kWords[] = { "Block().Client.Sequence",
	                                "Block().Client.PlaybackRate",
	                                "Block().Client.AnimTime" };

	for ( size_t i = 0; i < sizeof( kWords ) / sizeof( kWords[0] ); i++ )
	{
		EXPECT_GT( Occurrences( client, kWords[i] ), 0 ) << "client: " << kWords[i];
	}
}

TEST( AnimationPolicy, ThePlaybackRateIsSeededToOneAndNotToTheZeroedBlob )
{
	// The one of the three that a zeroed blob gets WRONG. A rate of zero
	// freezes the cycle at the sequence's first frame forever, so every entity
	// on this hierarchy would be a statue and nothing anywhere would say so.
	const std::string &code = Code( "game/server/shell_standalone_base.cpp" );

	ASSERT_GT( code.size(), 5000u );
	EXPECT_GT( Occurrences( code, "Block().Client.PlaybackRate = 1.0f;" ), 0 );
}

TEST( AnimationPolicy, TheModelFactsStayNativeBecauseTheyAreNotDecisions )
{
	// SUPERSEDED by plan physics_bones_native_zero 1.2: the duration and the
	// loop flag were native because the skeleton that read them was; the
	// skeleton is managed and they are BoneEntityState.SeqDuration /
	// SeqLooping beside it. The block still holds only what a caller chose.
	const std::string &header = Code( "game/server/shell_standalone.h" );

	ASSERT_GT( header.size(), 5000u );
	EXPECT_EQ( 0, Occurrences( header, "m_flPoseSeqDuration" ) );
	EXPECT_EQ( 0, Occurrences( header, "m_bPoseSeqLooping" ) );
}

//=============================================================================//
// 3. DoAnimationEvents is a no-op, and the batch it was replaced BY exists.
//=============================================================================//

TEST( AnimationPolicy, TheClientShellsDoAnimationEventsIsAnEmptyBody )
{
	const std::string &header = Code( kClientShellH );

	const size_t at = header.find( "DoAnimationEvents" );
	ASSERT_NE( std::string::npos, at )
		<< "the override is gone entirely, so the engine calls C_BaseEntity's";

	// The whole declaration, and what makes it a no-op is the {} on the end of
	// it rather than a semicolon that would send it out of line.
	const size_t eol = header.find( '\n', at );
	const std::string strLine = header.substr( at, eol - at );

	EXPECT_NE( std::string::npos, strLine.find( "OVERRIDE {}" ) )
		<< "declaration reads: " << strLine;
}

TEST( AnimationPolicy, NoShimSourceDefinesADoAnimationEventsBodyOutOfLine )
{
	const std::vector< std::string > &files = ShimSources();

	std::vector< std::string > offenders;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		std::ifstream file( files[i].c_str() );
		if ( !file.is_open() )
			continue;

		const std::string stripped = StripComments(
			std::string( ( std::istreambuf_iterator< char >( file ) ),
			             std::istreambuf_iterator< char >() ) );

		if ( stripped.find( "::DoAnimationEvents(" ) != std::string::npos )
			offenders.push_back( files[i] );
	}

	std::string strList;
	for ( size_t i = 0; i < offenders.size(); i++ )
		strList += "\n    " + offenders[i];

	EXPECT_TRUE( offenders.empty() )
		<< "an out-of-line DoAnimationEvents, in:" << strList;
}

TEST( AnimationPolicy, TheEventTableStillHasADoorManagedCodeReadsItThrough )
{
	// The no-op is only correct because the events are delivered as a BATCH
	// instead: managed code asks for the table between two cycles when it
	// advances the cycle, so it already knows which fired. Without a door the
	// empty body would simply be animation events deleted, and the two diffs
	// are otherwise identical.
	//
	// THE DOOR MOVED AND GOT WIDER -- plan_shim.md Phase 2d. It was two slots
	// in the client shell, SourceSharp_EntitySequenceEventCount and
	// ...SequenceEvent, the second costing ONE CROSSING PER EVENT. It is one
	// ModelDescriptor crossing now, which carries every sequence's whole event
	// list, and it is in a shared file because the event table is a fact of
	// the .mdl and both sides read the same file.
	//
	// So what this fact watches is the DOOR rather than its old address: the
	// reader must still carry the events, and the shell must still be the
	// no-op that the batch delivery makes correct. Since 2026-10-18 the reader
	// is managed's (StudioModelDescriptor, over the side's StudioHdr), and
	// model_descriptor.cpp's walk is gone.
	const std::string code = Code( kDescriptorReader );

	ASSERT_GT( code.size(), 1000u )
		<< kDescriptorReader << " could not be read; without it this fact "
		   "cannot tell a batch delivery from animation events deleted.";

	EXPECT_GT( Occurrences( code, "int eventCount = seq.NumEvents;" ), 0 )
		<< "the descriptor no longer reads a sequence's event count, so the "
		   "empty DoAnimationEvents above is animation events DELETED rather "
		   "than animation events delivered as a batch.";

	EXPECT_GT( Occurrences( code, "MstudioEvent e = seq.Event(j);" ), 0 )
		<< "the descriptor no longer reads the events themselves.";

	EXPECT_GT( Occurrences( code, "e.Options" ), 0 )
		<< "the descriptor no longer carries each event's OPTIONS -- the "
		   "muzzle flash's attachment, the sound's script name. An event "
		   "delivered without them fires nothing.";
}

} // namespace
