//========= SourceSharp — asking an entity for its skeleton ===================//
//
// The shim's animation calls each opened with
//
//     dynamic_cast< CBaseAnimating * >( ent.pBase )
//
// five times, on the path the entity API takes for every animation call, when
// CBaseEntity declares a virtual for exactly that question and CBaseAnimating
// overrides it — baseanimating.h asks it that way itself. So the RTTI was never
// needed.
//
// THE SUBSTITUTION IS NOT MECHANICAL, and that is what this file is about.
//
//     dynamic_cast< CBaseAnimating * >( pNull )   is NULL, defined, harmless
//     pNull->GetBaseAnimating()                   is a call through no object
//
// and ent.pBase is NULL for every entity on the parallel hierarchy — which is
// most of the entities in this mod, not an edge case. Five hand-written
// substitutions would have been five chances to leave the guard out, and the
// one that forgot would fault only on a managed entity, i.e. on the ordinary
// case rather than the rare one.
//
// So it is ONE projection, SourceSharpEntity_t::Animating(), beside the other
// projections of the same resolve, and this file pins both halves:
//
//   THE AGREEMENT — the virtual answers what the cast answered, for an
//   animating entity, for a non-animating one, and through a base pointer where
//   the two could differ. On mocks, because CBaseEntity needs the whole engine
//   to construct.
//
//   THE GUARD — the projection answers NULL for a resolve with no SDK arm. That
//   is the case the substitution would have broken, and the mock's arithmetic
//   is the same arithmetic the real one does.
//
//   THE BAN — no dynamic_cast to the animating class left in the shim, read out
//   of the source in sdk_player_cast_test.cpp's idiom, because "somebody wrote
//   the cast again" is a shape and not a value.
//
// PROVEN RED. Drop the NULL guard from the projection mock
// (`return pBase->GetBaseAnimating();`) and NoSdkArmHasNoSkeleton segfaults —
// which is the failure the real change exists to make impossible, arriving in
// the test instead of in the game. Put a dynamic_cast back into
// shell_scripted.cpp and TheShimAsksTheVirtualRatherThanTheRuntimeType fails
// naming the file and the line. Both were run.
//
//=============================================================================//
#include <gtest/gtest.h>

#include <dirent.h>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

//=============================================================================//
// HALF ONE — the virtual and the cast answer the same thing, and the guard.
//=============================================================================//

class FakeAnimating;

// ~ CBaseEntity: declares the question and answers "no".
class FakeBase
{
public:
	virtual ~FakeBase() {}
	virtual FakeAnimating *GetBaseAnimating() { return 0; }
	virtual int WhoAreYou() const { return 0xBA5E; }
};

// A second base first, so the FakeBase subobject is not at offset zero inside
// an animating entity. Without it the cast and the virtual would agree by
// accident of layout rather than by answering the same question, and the
// comparison below would prove nothing.
class FakePadding
{
public:
	virtual ~FakePadding() {}
	int m_nPadding[ 4 ];
};

// ~ CBaseAnimating: answers with itself.
class FakeAnimating : public FakePadding, public FakeBase
{
public:
	virtual FakeAnimating *GetBaseAnimating() { return this; }
	virtual int WhoAreYou() const { return 0xA11A; }
	virtual int Sequence() const { return 7; }
};

// ~ CBaseEntity subclass that is not animating: a logical entity, a filter, the
// gamerules proxy. GetBaseAnimating() inherited, so it answers "no" for itself.
class FakeLogical : public FakeBase
{
public:
	virtual int WhoAreYou() const { return 0x109; }
};

// ~ SourceSharpEntity_t, cut down to the arm this projection reads. The body of
// Animating() is the real one, character for character.
struct FakeResolve
{
	FakeBase *pBase;

	explicit FakeResolve( FakeBase *p = 0 ) : pBase( p ) {}

	FakeAnimating *Animating() const
	{
		return pBase ? pBase->GetBaseAnimating() : 0;
	}
};

TEST( AnimatingProjection, TheProjectionAgreesWithTheCast )
{
	FakeAnimating animating;
	FakeBase *pAsBase = &animating;      // what a resolve holds

	// The two answers, side by side. dynamic_cast is what the shim used to do;
	// the virtual is what it does now. Same object, and the SAME ADDRESS —
	// which is the half that matters, because the animating subobject is not at
	// offset zero and a wrong answer here would be an adjusted pointer rather
	// than a NULL.
	EXPECT_EQ( &animating, dynamic_cast< FakeAnimating * >( pAsBase ) );
	EXPECT_EQ( &animating, FakeResolve( pAsBase ).Animating() );

	// And it really is the object, not something at the same address wearing
	// the wrong vtable.
	EXPECT_EQ( 7, FakeResolve( pAsBase ).Animating()->Sequence() );
}

TEST( AnimatingProjection, AnEntityWithNoSkeletonAnswersNoEitherWay )
{
	FakeLogical logical;
	FakeBase *pAsBase = &logical;

	EXPECT_EQ( ( FakeAnimating * )0, dynamic_cast< FakeAnimating * >( pAsBase ) );
	EXPECT_EQ( ( FakeAnimating * )0, FakeResolve( pAsBase ).Animating() );
}

TEST( AnimatingProjection, NoSdkArmHasNoSkeleton )
{
	// THE CASE THE MECHANICAL SUBSTITUTION WOULD HAVE BROKEN. A resolve that
	// found an entity on the parallel hierarchy has pBase == NULL, and that is
	// the ordinary case in this mod rather than an edge one: dynamic_cast of
	// NULL is NULL, a virtual call on NULL is not, and the projection is where
	// the difference is handled once.
	EXPECT_EQ( ( FakeAnimating * )0, FakeResolve( 0 ).Animating() );
	EXPECT_EQ( ( FakeAnimating * )0, dynamic_cast< FakeAnimating * >( ( FakeBase * )0 ) );
}

//=============================================================================//
// HALF TWO — and the cast is not written again.
//=============================================================================//

const char *SourceRoot()
{
	static const char *kCandidates[] = { "sourcesharp", "src/sourcesharp", "../src/sourcesharp" };
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

// Code lines only. The files below discuss dynamic_cast at length in their
// comments — one of them has a paragraph about a dynamic_cast that has not been
// able to succeed for two years — so a scan over raw text would be a scan over
// commentary. sdk_player_cast_test.cpp learned this the same way.
std::vector< std::string > CodeLines( const char *pszRelative )
{
	std::vector< std::string > out;

	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return out;

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRelative ).c_str() );
	if ( !file.is_open() )
		return out;

	std::string line;
	while ( std::getline( file, line ) )
	{
		const size_t nFirst = line.find_first_not_of( " \t" );
		if ( nFirst != std::string::npos && line.compare( nFirst, 2, "//" ) == 0 )
			continue;

		out.push_back( line );
	}

	return out;
}

// The shim files that ask an entity for its skeleton. Not every file in the
// lane: a ban is only worth having where the thing banned has a caller, and
// naming them keeps a reader able to check the list against the code.
//
// WAS TWO. shell_scripted.cpp came off the list at plan_shim.md Phase 2d, and
// it came off because it stopped asking rather than because the ban was
// relaxed: its `.Animating()` sites all lived in the five sequence and event
// ABI entry points, and those are RETIRED -- one ModelDescriptor crossing
// answers what they answered, in C#, reading the MODEL rather than an entity,
// so there is no skeleton resolve left to ban a cast in.
//
// THE FILE IS REMOVED RATHER THAN THE CHECK RELAXED, deliberately. The
// "actually read" fact above requires every scanned file to really ask the
// question, so that "no casts" means the projection is being USED and not that
// the callers went away. Keeping a file with no callers on the list would have
// meant weakening exactly that guard, and the whole family of facts around
// this one exists because the weakened form is what shipped a silent no-op
// last time.
const char *kScanned[] =
{
	"game/server/shell_standalone_hierarchy.cpp",
};

// THE CHECK THAT WOULD OTHERWISE NOT BE ABLE TO FAIL. The ban asserts an
// ABSENCE, and a file that could not be read is absent in the same way — so a
// rename, a move, or an unexpected working directory would turn the ban green
// and mean nothing.
TEST( AnimatingProjectionBan, TheFilesTheBanIsAboutWereActuallyRead )
{
	ASSERT_TRUE( SourceRoot() != NULL )
		<< "no src/sourcesharp/ from here, so the source ban below is vacuous";

	for ( size_t i = 0; i < sizeof( kScanned ) / sizeof( kScanned[ 0 ] ); i++ )
	{
		const std::vector< std::string > lines = CodeLines( kScanned[ i ] );

		ASSERT_FALSE( lines.empty() ) << kScanned[ i ] << " read as empty";

		// And each really does still ask the question, so "no casts" means the
		// projection is being used rather than that the callers went away.
		bool bAsks = false;
		for ( size_t j = 0; j < lines.size(); j++ )
			if ( lines[ j ].find( ".Animating()" ) != std::string::npos )
				bAsks = true;

		EXPECT_TRUE( bAsks )
			<< kScanned[ i ] << " no longer asks a resolve for its skeleton at "
			   "all, so the ban below is watching nothing";
	}
}

TEST( AnimatingProjectionBan, TheShimAsksTheVirtualRatherThanTheRuntimeType )
{
	std::ostringstream found;
	int nFound = 0;

	for ( size_t i = 0; i < sizeof( kScanned ) / sizeof( kScanned[ 0 ] ); i++ )
	{
		const std::vector< std::string > lines = CodeLines( kScanned[ i ] );

		for ( size_t j = 0; j < lines.size(); j++ )
		{
			if ( lines[ j ].find( "dynamic_cast" ) == std::string::npos )
				continue;
			if ( lines[ j ].find( "CBaseAnimating" ) == std::string::npos )
				continue;

			nFound++;
			found << "\n    " << kScanned[ i ] << ":" << ( j + 1 ) << ": " << lines[ j ];
		}
	}

	EXPECT_EQ( 0, nFound )
		<< "the shim casts on runtime type to reach a skeleton, where "
		   "CBaseEntity::GetBaseAnimating() answers the same question with no "
		   "RTTI and SourceSharpEntity_t::Animating() carries the NULL guard "
		   "the cast used to provide for free:" << found.str();
}

} // namespace
