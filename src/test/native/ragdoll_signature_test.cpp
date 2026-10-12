//========= SourceSharp ========================================================//
//
// ragdoll_signature_test.cpp -- plan_entity.md Phase 6 step 6e's ragdoll
// library, after plan_physics_bones_native_zero Phase 3.
//
// The library was "a flat function over a bone snapshot returning ids"; the
// bodies are SourceSharp.Client's ClientRagdolls now, and what is left native
// is what fork game/client/physics.cpp calls: two forwarders into
// IManagedRagdollsClient and a no-op resolver install. These facts are
// properties of TEXT -- that the library holds no body logic any more, names no
// entity type, and that the physics step still calls it where a body's pose
// must be written (after Simulate) and where bodies must go (before
// DestroyEnvironment).
//
// EVERY SCAN STRIPS COMMENTS FIRST, and every needle is checked against a
// positive: a `find` that returns npos on a file it could not open is
// indistinguishable from one that returns npos because the property holds.
//
//=============================================================================

#include <stdio.h>
#include <string>
#include <vector>

#include "gtest/gtest.h"

// ARRAYSIZE without tier0, because this test has no Source header in its
// include path -- deliberately: what it reads is TEXT, and a scanner that had
// to link the engine to read a file would be a scanner nobody could run.
#define SS_ARRAYSIZE( a ) ( sizeof( a ) / sizeof( ( a )[0] ) )

namespace
{

std::string ReadFile( const char *pPath )
{
	FILE *fp = fopen( pPath, "rb" );

	if ( !fp )
		return std::string();

	std::string out;
	char buf[ 4096 ];
	size_t n;

	while ( ( n = fread( buf, 1, sizeof( buf ), fp ) ) > 0 )
		out.append( buf, n );

	fclose( fp );
	return out;
}

// The same stripper overlay_library_test.cpp carries, and it survives an
// unterminated block comment rather than running off the end -- a file that
// ends inside one is a file this scan would otherwise read as empty and pass.
std::string StripComments( const std::string &src )
{
	std::string out;
	out.reserve( src.size() );

	size_t i = 0;

	while ( i < src.size() )
	{
		if ( src[i] == '/' && i + 1 < src.size() && src[i + 1] == '/' )
		{
			while ( i < src.size() && src[i] != '\n' )
				i++;
			continue;
		}

		if ( src[i] == '/' && i + 1 < src.size() && src[i + 1] == '*' )
		{
			i += 2;
			while ( i + 1 < src.size() && !( src[i] == '*' && src[i + 1] == '/' ) )
				i++;
			i = ( i + 1 < src.size() ) ? i + 2 : src.size();
			continue;
		}

		out.push_back( src[i] );
		i++;
	}

	return out;
}

const char *kEntityTypes[] = {
	"CBaseEntity", "C_BaseEntity", "CBasePlayer", "C_BasePlayer",
	"CBaseAnimating", "C_BaseAnimating", "CStandaloneEntity", "C_StandaloneEntity",
	"IGameEntity", "IClientEntity", "IServerEntity", "EHANDLE", "CHandle<",
};

std::string HeaderText()
{
	return StripComments( ReadFile( "sourcesharp/game/client/ragdoll_library.h" ) );
}

std::string SourceText()
{
	return StripComments( ReadFile( "sourcesharp/game/client/ragdoll_library.cpp" ) );
}

} // namespace

//=============================================================================//
// THE STRIPPER ITSELF, because every scan below is only as good as it is.
//=============================================================================//

TEST( RagdollSignatureStripper, RemovesBothCommentForms )
{
	EXPECT_EQ( "int a;\nint b;\n",
	           StripComments( "int a;// CBaseEntity\nint b;/* C_BasePlayer */\n" ) );
}

TEST( RagdollSignatureStripper, SurvivesAnUnterminatedBlockComment )
{
	EXPECT_EQ( "int a;", StripComments( "int a;/* and then nothing" ) );
}

TEST( RagdollSignatureStripper, WouldCatchAnEntityTypeInCODE )
{
	// THE POSITIVE CONTROL for every scan below. Fed a line that DOES name one,
	// the search has to find it -- otherwise npos means "I could not look".
	const std::string src = StripComments( "C_BaseEntity *p; // and not this one\n" );

	EXPECT_NE( std::string::npos, src.find( "C_BaseEntity" ) );
}

TEST( RagdollSignatureStripper, AndWOULDNOTCatchOneInPROSE )
{
	// The other half, and the reason the stripper is here at all: this file's
	// own subject header explains at length what it does NOT name, and a scan
	// that read the explanation would fail on the fix.
	const std::string src = StripComments( "// this file names no C_BaseEntity\nint a;\n" );

	EXPECT_EQ( std::string::npos, src.find( "C_BaseEntity" ) );
}

//=============================================================================//
// THE FILES ARE THERE. A scan over an empty string passes everything.
//=============================================================================//

TEST( RagdollSignatureFiles, TheLibraryWasRead )
{
	EXPECT_GT( HeaderText().size(), 200u );
	EXPECT_GT( SourceText().size(), 200u );
}

//=============================================================================//
// NO ENTITY TYPE, AND NO BODY: TWO FORWARDERS.
//=============================================================================//

TEST( RagdollSignatureFiles, TheHeaderNamesNoEntityType )
{
	const std::string src = HeaderText();

	for ( size_t i = 0; i < SS_ARRAYSIZE( kEntityTypes ); i++ )
		EXPECT_EQ( std::string::npos, src.find( kEntityTypes[i] ) )
			<< "ragdoll_library.h names " << kEntityTypes[i];
}

TEST( RagdollSignatureFiles, TheSourceNamesNoEntityType )
{
	const std::string src = SourceText();

	for ( size_t i = 0; i < SS_ARRAYSIZE( kEntityTypes ); i++ )
		EXPECT_EQ( std::string::npos, src.find( kEntityTypes[i] ) )
			<< "ragdoll_library.cpp names " << kEntityTypes[i];
}

TEST( RagdollSignatureFiles, TheLibraryOnlyForwardsToManaged )
{
	const std::string src = SourceText();

	// THE PRESENCE: both of physics.cpp's calls ask managed.
	size_t nAsks = 0;
	for ( size_t at = src.find( "ManagedRagdollsClient()" ); at != std::string::npos;
	      at = src.find( "ManagedRagdollsClient()", at + 1 ) )
		nAsks++;
	EXPECT_EQ( 2u, nAsks ) << "StepPoses and DestroyAll each ask IManagedRagdollsClient once";
	EXPECT_NE( std::string::npos, src.find( "->StepPoses()" ) );
	EXPECT_NE( std::string::npos, src.find( "->DestroyAll()" ) );

	// THE ABSENCE: no solver, no body, no table (plan_physics_bones_native_zero
	// Phase 3 moved them to SourceSharp.Client's ClientRagdolls / RagdollSolver).
	static const char *const kGone[] = {
		"RagdollCreate(", "RagdollDestroy(", "ragdoll_t", "IPhysicsObject", "physenv",
		"RagdollLibrary_Create", "CSourceSharpRagdollIdTable",
	};
	for ( size_t i = 0; i < SS_ARRAYSIZE( kGone ); i++ )
		EXPECT_EQ( std::string::npos, src.find( kGone[i] ) )
			<< "ragdoll_library.cpp names " << kGone[i] << " again";
	EXPECT_EQ( std::string::npos, HeaderText().find( "RagdollLibrary_Create" ) );
}

//=============================================================================//
// THE PHYSICS STEP IS WHERE THE POSE IS WRITTEN.
//=============================================================================//

TEST( RagdollSignatureFiles, ThePhysicsStepWritesThePoses )
{
	const std::string src = StripComments( ReadFile( "game/client/physics.cpp" ) );

	ASSERT_GT( src.size(), 1000u );

	// Called from the client's physics step, and taken apart at level shutdown
	// -- the two things that file owns that no per-entity path can do.
	EXPECT_NE( std::string::npos, src.find( "RagdollLibrary_StepPoses();" ) );
	EXPECT_NE( std::string::npos, src.find( "RagdollLibrary_DestroyAll();" ) );
}

TEST( RagdollSignatureFiles, TheStepRunsAfterTheSolverAndBeforeTheFrameEnds )
{
	const std::string src = StripComments( ReadFile( "game/client/physics.cpp" ) );

	const size_t sim  = src.find( "physenv->Simulate(" );
	const size_t step = src.find( "RagdollLibrary_StepPoses();" );

	ASSERT_NE( std::string::npos, sim );
	ASSERT_NE( std::string::npos, step );

	// ORDER IS THE ASSERTION. Before Simulate, every body would draw one step
	// behind -- which looks like interpolation lag and is a misplaced call.
	EXPECT_LT( sim, step );
}

TEST( RagdollSignatureFiles, BodiesAreDestroyedBEFORETheEnvironmentIs )
{
	const std::string src = StripComments( ReadFile( "game/client/physics.cpp" ) );

	const size_t destroyAll = src.find( "RagdollLibrary_DestroyAll();" );
	const size_t destroyEnv = src.find( "physics->DestroyEnvironment(" );

	ASSERT_NE( std::string::npos, destroyAll );
	ASSERT_NE( std::string::npos, destroyEnv );

	// RagdollDestroy releases physics objects and a constraint group THROUGH the
	// environment, so a body still standing when DestroyEnvironment runs is
	// released against one that no longer exists.
	EXPECT_LT( destroyAll, destroyEnv );
}
