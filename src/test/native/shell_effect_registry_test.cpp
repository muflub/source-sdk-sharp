//========= SourceSharp — the registry the de-entified effects hang on =======//
//
// shell_effect_registry.h replaces a guarantee rather than adding a helper. The
// `_render` shells used to hold CHandles, which read as null of their own
// accord once the engine destroyed the entity behind them; the effects are not
// entities any more (shell_particle_effect.h), so the row is removed by the
// EFFECT, from its destructor, and the shell must not remove it again.
//
// Every failure that inversion can produce is silent:
//
//   * Release-then-Remove deletes a NEIGHBOUR'S row. That neighbour's effect is
//     then pointed at by nothing and freed by nobody — a leak that only shows
//     up as a level change getting slower, in a build nobody is measuring.
//   * a forwards walk that removes rows skips the row after each removal, so a
//     shutdown that removes two adjacent effects leaves one behind.
//   * `while ( Count() ) list[0]->Release()` hangs the client on a level change
//     the day somebody forgets to hook the gone callback at a factory in a
//     different file.
//
// None of those is a crash on the frame it happens, which is why they are
// pinned here rather than left to a play-test.
//
// Engine-free: a std::vector of a row struct and a fake effect that removes its
// own row when released, which is exactly the contract the real one has.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/shell_effect_registry.h"

#include <vector>
#include <fstream>
#include <sstream>
#include <string>
#include <sys/stat.h>

namespace
{

//-----------------------------------------------------------------------------
// A CUtlVector's surface, over a std::vector, because the header is templated
// on the container precisely so this file does not need the engine's one.
//
// Count() returns int and Remove(i) removes by index, which is what
// shell_effect_registry.h uses and all it uses.
//-----------------------------------------------------------------------------
template < typename T >
struct FakeList
{
	std::vector< T > v;

	int Count() const { return (int)v.size(); }
	T &operator[]( int i ) { return v[i]; }
	const T &operator[]( int i ) const { return v[i]; }
	void Remove( int i ) { v.erase( v.begin() + i ); }
	int AddToTail( const T &row ) { v.push_back( row ); return (int)v.size() - 1; }
};

struct FakeEffect;

struct Row
{
	unsigned int  hOwner;
	FakeEffect   *pEffect;
};

typedef FakeList< Row > List;

//-----------------------------------------------------------------------------
// A stand-in for CShellParticleEffect. Release() does what the real one does:
// it reaches the registry through the gone callback and removes the row, and
// only then is the object gone.
//
// m_bHooked is what a factory that forgot SetGoneCallback looks like. It is not
// a hypothetical: it is one line at a call site in another file, and the
// consequence lands here.
//-----------------------------------------------------------------------------
struct FakeEffect
{
	List *m_pList;
	bool  m_bHooked;
	int   m_nReleases;

	FakeEffect( List *pList, bool bHooked = true )
		: m_pList( pList ), m_bHooked( bHooked ), m_nReleases( 0 ) {}

	void Release()
	{
		m_nReleases++;

		if ( m_bHooked )
			ShellEffects_Forget( *m_pList, this );
	}
};

void Add( List &list, unsigned int hOwner, FakeEffect *pEffect )
{
	Row row;
	row.hOwner  = hOwner;
	row.pEffect = pEffect;
	list.AddToTail( row );
}

//=============================================================================
// Find
//=============================================================================

TEST( ShellEffectRegistry, FindAnswersMinusOneForAnEmptyList )
{
	List list;
	EXPECT_EQ( -1, ShellEffects_Find( list, 7u ) );
}

TEST( ShellEffectRegistry, FindAnswersTheIndexOfTheOwner )
{
	List list;
	FakeEffect a( &list ), b( &list ), c( &list );

	Add( list, 10u, &a );
	Add( list, 20u, &b );
	Add( list, 30u, &c );

	EXPECT_EQ( 0, ShellEffects_Find( list, 10u ) );
	EXPECT_EQ( 1, ShellEffects_Find( list, 20u ) );
	EXPECT_EQ( 2, ShellEffects_Find( list, 30u ) );
	EXPECT_EQ( -1, ShellEffects_Find( list, 40u ) );
}

TEST( ShellEffectRegistry, FindAnswersTheFirstOfTwoRowsForOneOwner )
{
	// A second row for one owner is a bug elsewhere. The answer still has to be
	// the same one every time it is asked, or the bug reads as intermittent.
	List list;
	FakeEffect a( &list ), b( &list );

	Add( list, 10u, &a );
	Add( list, 10u, &b );

	EXPECT_EQ( 0, ShellEffects_Find( list, 10u ) );
}

//=============================================================================
// Forget
//=============================================================================

TEST( ShellEffectRegistry, ForgetRemovesTheRowAndLeavesTheOthersWhereTheyPoint )
{
	List list;
	FakeEffect a( &list ), b( &list ), c( &list );

	Add( list, 10u, &a );
	Add( list, 20u, &b );
	Add( list, 30u, &c );

	ShellEffects_Forget( list, &b );

	ASSERT_EQ( 2, list.Count() );
	EXPECT_EQ( &a, list[0].pEffect );
	EXPECT_EQ( &c, list[1].pEffect );
	EXPECT_EQ( 10u, list[0].hOwner );
	EXPECT_EQ( 30u, list[1].hOwner );
}

TEST( ShellEffectRegistry, ForgetIsANoOpForAnEffectThatIsNotInTheList )
{
	List list;
	FakeEffect a( &list ), stranger( &list );

	Add( list, 10u, &a );

	ShellEffects_Forget( list, &stranger );

	ASSERT_EQ( 1, list.Count() );
	EXPECT_EQ( &a, list[0].pEffect );
}

TEST( ShellEffectRegistry, ForgetRemovesBOTHRowsWhenTwoPointAtOneEffect )
{
	// The failure this half exists for: leave the second row behind and the
	// next sweep dereferences a freed pointer. A forwards walk that removed the
	// first would skip straight over the second, which is why the loop counts
	// down.
	List list;
	FakeEffect a( &list ), doubled( &list );

	Add( list, 10u, &doubled );
	Add( list, 20u, &doubled );
	Add( list, 30u, &a );

	ShellEffects_Forget( list, &doubled );

	ASSERT_EQ( 1, list.Count() );
	EXPECT_EQ( &a, list[0].pEffect );
}

TEST( ShellEffectRegistry, ForgetRemovesADJACENTRowsForOneEffect )
{
	// The same skip, in the shape it actually occurs in: two rows next to each
	// other. A forwards walk removes index 1, the list shifts, the loop moves
	// to index 2, and the row that just became index 1 is never looked at.
	List list;
	FakeEffect a( &list ), doubled( &list ), c( &list );

	Add( list, 10u, &a );
	Add( list, 20u, &doubled );
	Add( list, 30u, &doubled );
	Add( list, 40u, &c );

	ShellEffects_Forget( list, &doubled );

	ASSERT_EQ( 2, list.Count() );
	EXPECT_EQ( &a, list[0].pEffect );
	EXPECT_EQ( &c, list[1].pEffect );
}

//=============================================================================
// ReleaseAll
//=============================================================================

TEST( ShellEffectRegistry, ReleaseAllOnAnEmptyListReleasesNothing )
{
	List list;
	EXPECT_EQ( 0, ShellEffects_ReleaseAll( list ) );
	EXPECT_EQ( 0, list.Count() );
}

TEST( ShellEffectRegistry, ReleaseAllEmptiesTheListAndReleasesEachOnce )
{
	List list;
	FakeEffect a( &list ), b( &list ), c( &list );

	Add( list, 10u, &a );
	Add( list, 20u, &b );
	Add( list, 30u, &c );

	EXPECT_EQ( 3, ShellEffects_ReleaseAll( list ) );
	EXPECT_EQ( 0, list.Count() );

	EXPECT_EQ( 1, a.m_nReleases );
	EXPECT_EQ( 1, b.m_nReleases );
	EXPECT_EQ( 1, c.m_nReleases );
}

TEST( ShellEffectRegistry, ReleaseAllStopsRatherThanSpinningWhenTheCallbackIsMissing )
{
	// A factory that forgot SetGoneCallback. Without the bound this is a frozen
	// client on every level change; with it, it is a leak the caller can see.
	List list;
	FakeEffect a( &list ), unhooked( &list, false );

	Add( list, 10u, &a );
	Add( list, 20u, &unhooked );

	// One released, then the list stopped shrinking.
	EXPECT_EQ( -1, ShellEffects_ReleaseAll( list ) );

	EXPECT_EQ( 1, a.m_nReleases );
	EXPECT_EQ( 1, unhooked.m_nReleases );
	EXPECT_EQ( 1, list.Count() );
}

TEST( ShellEffectRegistry, ReleaseAllStopsOnTheVeryFirstRowWhenNothingIsHooked )
{
	List list;
	FakeEffect unhooked( &list, false );

	Add( list, 10u, &unhooked );

	// Nothing released before it gave up, so the answer is a negative zero's
	// only honest spelling: zero, with a list that is not empty.
	EXPECT_EQ( 0, ShellEffects_ReleaseAll( list ) );
	EXPECT_EQ( 1, list.Count() );
	EXPECT_EQ( 1, unhooked.m_nReleases );
}

//=============================================================================
// The inversion itself: what the CALLER must not do.
//=============================================================================

TEST( ShellEffectRegistry, ReleaseAloneIsEnoughAndRemovingAsWellEatsANeighbour )
{
	// This is the rule the header states, asserted from both sides so that the
	// "do not also Remove" comment is a fact rather than advice.
	List list;
	FakeEffect a( &list ), b( &list );

	Add( list, 10u, &a );
	Add( list, 20u, &b );

	// The right way: release the row's effect and stop.
	const int iEntry = ShellEffects_Find( list, 10u );
	ASSERT_EQ( 0, iEntry );
	list[iEntry].pEffect->Release();

	ASSERT_EQ( 1, list.Count() );
	EXPECT_EQ( &b, list[0].pEffect ) << "the release removed its own row";

	// The wrong way, shown on a fresh pair: the extra Remove( iEntry ) takes the
	// neighbour that shifted into that slot.
	List wrong;
	FakeEffect c( &wrong ), d( &wrong );

	Add( wrong, 10u, &c );
	Add( wrong, 20u, &d );

	const int iWrong = ShellEffects_Find( wrong, 10u );
	wrong[iWrong].pEffect->Release();
	wrong.Remove( iWrong );

	EXPECT_EQ( 0, wrong.Count() ) << "d's row was eaten by the redundant Remove";
}

}  // namespace

//=============================================================================
// PART 2 — AND EVERY SHELL ASKS IT, WHICH IS A DIFFERENT KIND OF FACT.
//
// Everything above DRIVES the header: a std::vector, a fake effect, and the
// contract exercised. That says the rule is right. It says nothing at all about
// whether anybody uses it — and when this lane arrived, six of the ten `_render`
// shells in the client shim had the Find loop WRITTEN OUT AGAIN beside the
// include of the file that holds it.
//
// A second copy of a rule is not a style problem here. ShellEffects_Find's
// FIRST-match is argued in the header: one managed entity owns at most one
// effect of a family, so a second row for one owner is a bug elsewhere, and
// finding the first keeps the answer STABLE while that bug is found. A copy that
// answers "last" or "any" makes the same bug intermittent, in one family only,
// and nothing says which.
//
// So this half is a source scan, and it is an ABSENCE paired with a count: no
// shell declares its own lookup, AND the shared one is asked the number of times
// the shells between them need it. The count is exact rather than a floor
// because a floor passes for a shell that quietly grew a copy beside its calls.
//
// Lane 4c-render, plan_shim.md §6.
//=============================================================================

namespace
{

const char *SrcRootForScan()
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

std::string ReadShell( const char *pszName )
{
	const char *pszRoot = SrcRootForScan();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/sourcesharp/game/client/"
	                    + pszName ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::stringstream ss;
	ss << file.rdbuf();
	return ss.str();
}

size_t CountIn( const std::string &strHaystack, const std::string &strNeedle )
{
	size_t n = 0;
	for ( size_t at = strHaystack.find( strNeedle );
	      at != std::string::npos;
	      at = strHaystack.find( strNeedle, at + strNeedle.size() ) )
	{
		n++;
	}
	return n;
}

// Every `_render` shell in the client shim that keeps an owner-keyed pool, with
// how many times it asks the shared lookup. Written out rather than globbed so
// that a shell DELETED is a red test and not a silently shorter list.
// nOwnLoops is how many hand-rolled lookups the shell is ALLOWED, which is zero
// everywhere but one — and the one is not a leftover.
struct Shell_t { const char *pszFile; size_t nCalls; size_t nOwnLoops; };

const Shell_t kShells[] = {
	// shell_dust_render.cpp left the family with lane c5-particles: its two
	// lists (live and pending volumes) went with the native dust volume.
	// shell_precipitation_render.cpp left (lane c2-sims): the weather is C#'s.
	// shell_rope_render.cpp left the table with the C2 rope port: its spans
	// are C#'s (ClientRopeSystem), and the executor holds strips, not owners.
	// FIVE SHARED CALLS AND ONE OF ITS OWN. The exception is RendererFor, and
	// it cannot use the helper: its list is CUtlVector< SourceSharpSprite_t * >
	// — POINTERS to heap rows — because a CSourceSharpSpriteRenderer is handed
	// out BY ADDRESS and a CUtlVector of values moves its elements when it
	// grows. The helper takes a list of rows. Folding it would dangle every
	// renderer address the next time the vector reallocated, and what dangles
	// is a pixel-visibility query handle read once per frame.
	//
	// It is a NUMBER here rather than an exemption by name so that a SECOND
	// hand-rolled loop in this file is still a red.
	{ "shell_sprite_render.cpp",        2, 1 },   // the trails
	// The flame's particle systems left shell_sprite_render.cpp for the
	// particles shell (lane c5-particles), their three calls with them, and
	// the particles route's read asks once more.
	{ "shell_managed_particles.cpp",    4, 0 },
	{ "shell_worldtext_render.cpp",     2, 0 },
};

const size_t kNumShells = sizeof( kShells ) / sizeof( kShells[ 0 ] );

} // namespace

TEST( ShellEffectRegistryIsUsed, EveryShellIsReadableAndIncludesTheHeader )
{
	ASSERT_TRUE( SrcRootForScan() != NULL ) << "cannot locate the src tree from the cwd";

	for ( size_t i = 0; i < kNumShells; i++ )
	{
		const std::string src = ReadShell( kShells[ i ].pszFile );
		ASSERT_FALSE( src.empty() )
			<< kShells[ i ].pszFile << " read as nothing — every absence below "
			   "would pass vacuously for it";
		EXPECT_NE( src.find( "shell_effect_registry.h" ), std::string::npos )
			<< kShells[ i ].pszFile << " no longer includes the shared registry";
	}
}

TEST( ShellEffectRegistryIsUsed, NoShellHandRollsItsOwnOwnerLookup )
{
	// The shape, not a name: `static int Find<anything>( uint32_t hOwner )` is
	// what all six copies looked like, and naming them individually would let
	// the seventh be called something else.
	for ( size_t i = 0; i < kNumShells; i++ )
	{
		const std::string src = ReadShell( kShells[ i ].pszFile );
		ASSERT_FALSE( src.empty() ) << kShells[ i ].pszFile;

		EXPECT_EQ( CountIn( src, "( uint32_t hOwner )\n{\n\tfor ( int i = 0;" ),
		           kShells[ i ].nOwnLoops )
			<< kShells[ i ].pszFile << " has an owner-lookup loop of its own "
			<< "again. ShellEffects_Find's FIRST-match rule is argued in "
			<< "shell_effect_registry.h and driven by the facts above; a second "
			<< "copy that answers 'last' or 'any' makes a duplicate-row bug "
			<< "intermittent in one effect family and silent everywhere else. "
			<< "The one allowed exception is RendererFor's — see the table.";
	}
}

TEST( ShellEffectRegistryIsUsed, EveryShellAsksTheSharedLookupTheExpectedNumberOfTimes )
{
	size_t nTotal = 0;

	for ( size_t i = 0; i < kNumShells; i++ )
	{
		const std::string src = ReadShell( kShells[ i ].pszFile );
		ASSERT_FALSE( src.empty() ) << kShells[ i ].pszFile;

		const size_t nFound = CountIn( src, "ShellEffects_Find(" );
		EXPECT_EQ( nFound, kShells[ i ].nCalls )
			<< kShells[ i ].pszFile << " asks the shared lookup " << nFound
			<< " times, expected " << kShells[ i ].nCalls << ". If a call site "
			<< "was legitimately added or removed, move this number and say "
			<< "which in the commit — do not raise it to a floor.";

		nTotal += nFound;
	}

	// The whole family, as one number, so that a shell losing a call while
	// another gains one is still a red. 25 when they were folded onto it, less
	// the rope's two when its spans moved to C#, less two for each leaf
	// renderer lane c2-sims retired with its sim (the steam jet, the smoke
	// stack, the smoke volume, the trails, the embers: their effects are the managed
	// particle executor's, keyed by owner and channel), and two for the
	// precipitation shell when the weather went to C# (13 -> 11).
	// Lane c5-particles: +1, the particles route's read (11 -> 12); -4 when
	// the dust volume's two lists left with it (12 -> 8).
	EXPECT_EQ( nTotal, 8u )
		<< "the leaf renderers between them ask the shared lookup " << nTotal
		<< " times, and it was 8 when the dust volume's lists left.";
	// (lane c2-sims, rebased: -2 for the shell it retired: 19.)
}
