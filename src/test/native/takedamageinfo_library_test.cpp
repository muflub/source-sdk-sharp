//====== SourceSharp — the damage-info value type, moved out of SCC 11 ========//
//
// plan_entity.md Phase 4 exit, lane p4-util. `game/shared/takedamageinfo.cpp`
// is one of the 31 translation units of SCC 11, and every definition it held
// now lives in `sourcesharp/game/shared/takedamageinfo_library.cpp`.
//
// WHY THE WHOLE TU AND NOT A FAMILY, which is the thing this suite exists to
// stop being undone: step 7f measured `ClearMultiDamage()` on its own and
// recorded it as a CONVERSION —
//
//     ClearMultiDamage() -> g_MultiDamage -> CMultiDamage::Init
//                        -> CTakeDamageInfo::Init + CTakeDamageInfo::m_DataMap
//
// all of which were out of line in stock's object. Move the function and the
// cross-edge lands on the symbol next door; move the whole unit and there is
// nothing next door to land on. Any future "tidy-up" that puts one of these
// bodies back into `game/shared/takedamageinfo.cpp` re-creates exactly that.
// The stock file's CONTENTS are no longer read -- it is compiled into neither
// DLL, which is asserted below -- so the library side is what is pinned.
//
// THREE FAILURES THIS PINS:
//
//   1. A ONE-SIDED SOURCE ROW. takedamageinfo.cpp was compiled into server.so
//      AND client.so, so the shim TU is in BOTH targets' source lists. The recorded lesson —
//      a shared file read on one side compiles green and breaks the other at a
//      later step — is exactly this shape.
//
//   2. THE DATADESCS SPLIT FROM THEIR CLASSES. `BEGIN_SIMPLE_DATADESC(
//      CTakeDamageInfo )` defines `CTakeDamageInfo::m_DataMap`, and
//      `BEGIN_SIMPLE_DATADESC_( CMultiDamage, CTakeDamageInfo )` NAMES the
//      base's. Split across two TUs they still link; what breaks is the save
//      game, silently.
//
//   3. `phys_pushscale` LEFT BEHIND. It is a plain (non-static) ConVar that
//      four other files declare `extern`, so a copy left in the stock TU is a
//      duplicate registration, which the ConVar system reports at runtime and
//      no compile or link step sees.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <algorithm>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

std::string ReadFile( const char *pPath )
{
	std::ifstream f( pPath );
	EXPECT_TRUE( f.good() ) << "cannot open " << pPath;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t i = hay.find( needle ); i != std::string::npos;
	      i = hay.find( needle, i + needle.size() ) )
	{
		n++;
	}
	return n;
}

const char *const kLibrary   = "sourcesharp/game/shared/takedamageinfo_library.cpp";
const char *const kStockCpp  = "game/shared/takedamageinfo.cpp";
const char *const kStockHdr  = "game/shared/takedamageinfo.h";

// Every out-of-line definition the stock TU held, spelled as its own first
// line. None of these strings occurs in either file's prose, which is checked
// by NoNeedleIsSatisfiedByProseAlone below.
const char *const kMovedDefinitions[] = {
	"void CTakeDamageInfo::Init( IGameEntity *pInflictor, IGameEntity *pAttacker, IGameEntity *pWeapon, const Vector &damageForce, const Vector &damagePosition, const Vector &reportedPosition, float flDamage, int bitsDamageType, int iCustomDamage )",
	"CTakeDamageInfo::CTakeDamageInfo()",
	"CMultiDamage::CMultiDamage()",
	"void CMultiDamage::Init( IGameEntity *pTarget, IGameEntity *pInflictor, IGameEntity *pAttacker, IGameEntity *pWeapon, const Vector &damageForce, const Vector &damagePosition, const Vector &reportedPosition, float flDamage, int bitsDamageType, int iKillType )",
	"void ClearMultiDamage( void )",
	"void ApplyMultiDamage( void )",
	"float ImpulseScale( float flTargetMass, float flDesiredSpeed )",
	"void CalculateExplosiveDamageForce( CTakeDamageInfo *info, const Vector &vecDir, const Vector &vecForceOrigin, float flScale )",
};
const size_t kNumMoved = sizeof( kMovedDefinitions ) / sizeof( kMovedDefinitions[ 0 ] );

// Moved here, then deleted by the server gc cull (2026-10-01): server.so's link
// discarded each one and the client never compiled them, so no binary had them.
const char *const kCulledDefinitions[] = {
	"void CTakeDamageInfo::SetCritType( ECritType eType )",
	"void AddMultiDamage( const CTakeDamageInfo &info, IGameEntity *pEntity )",
	"void CalculateBulletDamageForce( CTakeDamageInfo *info, int iBulletType, const Vector &vecBulletDir, const Vector &vecForceOrigin, float flScale )",
	"void CalculateMeleeDamageForce( CTakeDamageInfo *info, const Vector &vecMeleeDir, const Vector &vecForceOrigin, float flScale )",
	"void GuessDamageForce( CTakeDamageInfo *info, const Vector &vecForceDir, const Vector &vecForceOrigin, float flScale )",
};
const size_t kNumCulled = sizeof( kCulledDefinitions ) / sizeof( kCulledDefinitions[ 0 ] );

// The objects the stock TU owned, as opposed to the functions. Each fails a
// different way if it is split from the code that reads it.
// phys_pushscale IS NO LONGER ON THIS LIST. plan_shim.md Phase 5 (lane 5d)
// moved the declaration to SourceSharp.Server/MoveVars.cs and
// SourceSharp.Client/MoveVars.cs, and the three reads below it in
// takedamageinfo_library.cpp became a CManagedConVarRef of the same name.
// The assertion that used to be here required the exact text
//     ConVar phys_pushscale( "phys_pushscale", "1", FCVAR_REPLICATED );
// in the shim TU. It is retired rather than reworded because it stopped
// being a claim about this build: what it stood for -- one declaration of
// this name, since a second is neither a compile nor a link error -- is now
// docs/convar-ledger.tsv and convar_ledger_test.cpp, which check it across
// the whole tree instead of in this one file.
//
// The ABSENCE is asserted below, in ThePushScaleDeclarationDidNotStayBehind,
// so that a lane bringing a native one back has to come here and say so.
const char *const kMovedObjects[] = {
	"BEGIN_SIMPLE_DATADESC( CTakeDamageInfo )",
	"BEGIN_SIMPLE_DATADESC_( CMultiDamage, CTakeDamageInfo )",
	"CMultiDamage g_MultiDamage;",
};
const size_t kNumObjects = sizeof( kMovedObjects ) / sizeof( kMovedObjects[ 0 ] );

//-----------------------------------------------------------------------------
// THE FOUR DEFINITIONS PHASE 3f DELETED, and the reason they are a list rather
// than four missing rows.
//
// plan_shim.md §5 3f. Each had 0 importers on either link line AND no caller
// inside this translation unit -- both halves were needed, because
// `orphan-syms` cannot see an intra-TU caller and two of this file's other
// orphans (SetCritType, ApplyMultiDamage) turn out to have one. SetCritType is
// called from AddMultiDamage at the point where the accumulator takes the
// incoming crit type, which is why it is still in kMovedDefinitions above and
// why the plan's own list for this file is one name too long.
//
// A ROW THAT SIMPLY VANISHES IS NOT A CHECK. These assert the ABSENCE, paired
// with the surviving declaration in stock's header, so the pair cannot be
// satisfied by the file being gone or by the header being gutted -- and a lane
// that brings one of these back has to say so here.
//-----------------------------------------------------------------------------
const char *const kDeletedDefinitions[] = {
	"void CTakeDamageInfo::AdjustPlayerDamageInflictedForSkillLevel()",
	"void CTakeDamageInfo::AdjustPlayerDamageTakenForSkillLevel()",
	"const char *CTakeDamageInfo::GetAmmoName() const",
	"void CTakeDamageInfo::DebugGetDamageTypeString(unsigned int damageType, char *outbuf, int outbuflength )",
};
const size_t kNumDeletedDefs =
	sizeof( kDeletedDefinitions ) / sizeof( kDeletedDefinitions[ 0 ] );

// The string table DebugGetDamageTypeString was the only reader of. It was
// file-static, so it went with its reader or it became a warning.
const char *const kDeletedObject = "static const char * const s_DamageTypeToStrTable[] =";

// What takedamageinfo.h must STILL declare for the four above. The unported
// stock files that call them -- hl2_player.cpp, gamerules.cpp, the tf tree --
// are compiled into neither target, and a lane porting one wants the declaration there.
const char *const kDeletedButStillDeclared[] = {
	"void			AdjustPlayerDamageInflictedForSkillLevel();",
	"void			AdjustPlayerDamageTakenForSkillLevel();",
	"const char *	GetAmmoName() const;",
	"static void		DebugGetDamageTypeString(unsigned int DamageType, char *outbuf, int outbuflength );",
};
const size_t kNumDeletedDeclared =
	sizeof( kDeletedButStillDeclared ) / sizeof( kDeletedButStillDeclared[ 0 ] );

// What takedamageinfo.h must still declare, so that no caller on either side
// changed. A move that also edited the header would be a different change.
const char *const kStillDeclared[] = {
	"void ClearMultiDamage( void );",
	"void ApplyMultiDamage( void );",
	"void AddMultiDamage( const CTakeDamageInfo &info, IGameEntity *pEntity );",
	"float ImpulseScale( float flTargetMass, float flDesiredSpeed );",
	"void CalculateExplosiveDamageForce( CTakeDamageInfo *info, const Vector &vecDir, const Vector &vecForceOrigin, float flScale = 1.0 );",
};
const size_t kNumDeclared = sizeof( kStillDeclared ) / sizeof( kStillDeclared[ 0 ] );

// Strip /* */ and // comments, so a needle cannot be satisfied by prose.
std::string StripComments( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );
	for ( size_t i = 0; i < s.size(); )
	{
		if ( s.compare( i, 2, "/*" ) == 0 )
		{
			size_t e = s.find( "*/", i + 2 );
			i = ( e == std::string::npos ) ? s.size() : e + 2;
		}
		else if ( s.compare( i, 2, "//" ) == 0 )
		{
			size_t e = s.find( '\n', i + 2 );
			i = ( e == std::string::npos ) ? s.size() : e;
		}
		else
		{
			out.push_back( s[ i++ ] );
		}
	}
	return out;
}

} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

TEST( TakeDamageInfoLibrary, EveryMovedDefinitionIsInTheShimFile )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMovedDefinitions[ i ] ) )
			<< kMovedDefinitions[ i ] << " is not defined exactly once in "
			<< kLibrary;
	}
}

TEST( TakeDamageInfoLibrary, TheGcCulledDefinitionsAreGone )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	// The anchor: a surviving definition, so the absences below read a real file.
	ASSERT_EQ( 1u, CountOf( lib, "float ImpulseScale( float flTargetMass, float flDesiredSpeed )" ) )
		<< kLibrary << " did not load, or lost a definition it keeps";

	for ( size_t i = 0; i < kNumCulled; i++ )
		EXPECT_EQ( 0u, CountOf( lib, kCulledDefinitions[ i ] ) )
			<< kCulledDefinitions[ i ] << " is back in " << kLibrary
			<< "; nothing in either DLL calls it";
}

TEST( TakeDamageInfoLibrary, TheFourDeletedDefinitionsAreGoneFromTheLibrary )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load -- without this "
		<< "every absence below is a statement about nothing";

	// The positive control: this is still the file that holds the value type.
	ASSERT_NE( lib.find( "CTakeDamageInfo::CTakeDamageInfo()" ), std::string::npos )
		<< kLibrary << " no longer defines CTakeDamageInfo at all";

	for ( size_t i = 0; i < kNumDeletedDefs; i++ )
	{
		EXPECT_EQ( 0u, CountOf( lib, kDeletedDefinitions[ i ] ) )
			<< kDeletedDefinitions[ i ] << " is back. It had 0 importers on "
			   "either link line and no caller in this TU when it went; if "
			   "that changed, say so in the plan.";
	}

	EXPECT_EQ( 0u, CountOf( lib, kDeletedObject ) )
		<< "s_DamageTypeToStrTable is back without its only reader, which is a "
		   "file-static with no consumer -- a warning, not a table.";
}

TEST( TakeDamageInfoLibrary, TheFourDeletedDefinitionsAreStillDeclaredInStocksHeader )
{
	const std::string h = ReadFile( kStockHdr );
	ASSERT_FALSE( h.empty() ) << kStockHdr << " did not load";

	// This is the half that stops the absences above degenerating: delete the
	// declarations too and every one of them still passes while this fails.
	for ( size_t i = 0; i < kNumDeletedDeclared; i++ )
	{
		EXPECT_EQ( 1u, CountOf( h, kDeletedButStillDeclared[ i ] ) )
			<< "takedamageinfo.h no longer declares: "
			<< kDeletedButStillDeclared[ i ];
	}
}

//=============================================================================//
// 2. THE OBJECTS TRAVELLED WITH THE CODE THAT READS THEM
//=============================================================================//

TEST( TakeDamageInfoLibrary, TheConVarAndTheGlobalsAreInTheLibrary )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	for ( size_t i = 0; i < kNumObjects; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMovedObjects[ i ] ) )
			<< kMovedObjects[ i ] << " is not in " << kLibrary;
	}
}

TEST( TakeDamageInfoLibrary, BothDatadescsAreInTheSameTranslationUnitAsTheirClasses )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	// CMultiDamage's datadesc names CTakeDamageInfo as its base, so it reads
	// CTakeDamageInfo::m_DataMap. The two must be in one TU or the save-game
	// description of a CMultiDamage silently loses the base's fields.
	const size_t base    = CountOf( lib, "BEGIN_SIMPLE_DATADESC( CTakeDamageInfo )" );
	const size_t derived = CountOf( lib, "BEGIN_SIMPLE_DATADESC_( CMultiDamage, CTakeDamageInfo )" );
	EXPECT_EQ( 1u, base );
	EXPECT_EQ( 1u, derived );
	EXPECT_EQ( 1u, CountOf( lib, "CMultiDamage g_MultiDamage;" ) )
		<< "g_MultiDamage must be in the same TU as the CMultiDamage members "
		   "ClearMultiDamage and ApplyMultiDamage call on it.";
}

//=============================================================================//
// 3. BOTH LINK LINES COMPILE IT
//=============================================================================//

// Listed EXACTLY ONCE in the target's CMakeLists (a second row is a duplicate
// TU) and present in its compile database. cmake_graph throws on a missing,
// empty or stale input.
static void ExpectCompiledOnce( const char *pTarget )
{
	const std::vector< std::string > listed = cmake_graph::ListedSources( pTarget );
	EXPECT_EQ( 1, std::count( listed.begin(), listed.end(), std::string( kLibrary ) ) )
		<< kLibrary << " is not listed exactly once in " << pTarget
		<< " (" << cmake_graph::ListFile( pTarget ) << ")";
	EXPECT_TRUE( cmake_graph::Compiles( pTarget, kLibrary ) )
		<< kLibrary << " is not compiled into " << pTarget;
}

TEST( TakeDamageInfoLibrary, TheServerCompilesIt )
{
	ExpectCompiledOnce( cmake_graph::kServer );
	if ( HasFailure() )
		ADD_FAILURE() << "server.so needs ApplyMultiDamage, g_MultiDamage and "
		   "CTakeDamageInfo::Init, and takedamageinfo.cpp no longer defines them.";
}

TEST( TakeDamageInfoLibrary, TheClientCompilesIt )
{
	ExpectCompiledOnce( cmake_graph::kClient );
	if ( HasFailure() )
		ADD_FAILURE() << "client.so asks for ClearMultiDamage and the CTakeDamageInfo "
		   "constructors, and takedamageinfo.cpp no longer defines any of them.";
}

TEST( TakeDamageInfoLibrary, TheStockTranslationUnitIsCompiledOnNeitherSide )
{
	// THIS ASSERTION IS THE OTHER WAY ROUND SINCE plan_shim.md PHASE 1d, and
	// the flip is the point rather than an edit to get a suite green.
	//
	// What it said was that both vpcs still named game/shared/takedamageinfo.cpp
	// -- because the lane that emptied that TU moved behaviour only, and the
	// Phase 4 exit was to remove all 31 SCC 11 rows in one commit. Phase 1d
	// is a different commit and a narrower claim: this TU is not one of the
	// 31 waiting on a flip, it is an EMPTY translation unit. It defines 0
	// strong symbols on either side -- its eight weak ones are mathlib header
	// inlines (IsFinite, the Vector2D/4D constructors) that every other
	// object emits for itself -- and tools/shim-census reports 0 objects
	// importing it on server and 0 on client. Compiling 81 lines of
	// commented-out DMG_ table into both DLLs is the whole of what the two
	// rows bought.
	//
	// The file itself is untouched and still on disk (Valve's stock build
	// lists it), and every fact above about what it must not contain reads it
	// from disk and goes on working.
	//
	// Proof-of-read for the absences: the successor IS compiled on each side.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kLibrary ) )
		<< "server_sourcesharp does not compile " << kLibrary;
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kLibrary ) )
		<< "client_sourcesharp does not compile " << kLibrary;

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kStockCpp ) )
		<< "the stock TU is back in server_sourcesharp "
		   "(src/server/native/CMakeLists.txt). It defines nothing; if "
		   "something has been typed back into it, that is a SECOND definition "
		   "of whatever " << kLibrary << " already holds.";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kStockCpp ) )
		<< "the stock TU is back in client_sourcesharp -- see above, and note "
		   "that a row added to one side only is the shared-file failure this "
		   "suite's header lists second.";
}

//=============================================================================//
// 4. THE HEADER DID NOT MOVE, SO NO CALLER DID
//=============================================================================//

TEST( TakeDamageInfoLibrary, EveryDeclarationStaysInTheStockHeader )
{
	const std::string hdr = StripComments( ReadFile( kStockHdr ) );
	ASSERT_FALSE( hdr.empty() ) << kStockHdr << " did not load";

	for ( size_t i = 0; i < kNumDeclared; i++ )
	{
		EXPECT_EQ( 1u, CountOf( hdr, kStillDeclared[ i ] ) )
			<< kStillDeclared[ i ] << " is no longer declared in " << kStockHdr
			<< " -- callers on both sides reach these through the stock header "
			   "and none of them changed.";
	}
}

//=============================================================================//
// 5. THE NEEDLES ARE CODE, NOT PROSE
//
// Both files carry a banner naming what moved. A needle that occurs only in a
// comment would make every fact above pass whatever the code did.
//=============================================================================//

TEST( TakeDamageInfoLibrary, NoNeedleIsSatisfiedByProseAlone )
{
	const std::string libRaw  = ReadFile( kLibrary );
	const std::string libCode = StripComments( libRaw );
	ASSERT_FALSE( libCode.empty() );

	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( CountOf( libRaw, kMovedDefinitions[ i ] ),
		           CountOf( libCode, kMovedDefinitions[ i ] ) )
			<< kMovedDefinitions[ i ] << " occurs in a COMMENT in " << kLibrary
			<< ", so the fact that reads it could be satisfied by prose.";
	}
	for ( size_t i = 0; i < kNumObjects; i++ )
	{
		EXPECT_EQ( CountOf( libRaw, kMovedObjects[ i ] ),
		           CountOf( libCode, kMovedObjects[ i ] ) )
			<< kMovedObjects[ i ] << " occurs in a COMMENT in " << kLibrary;
	}
}

//=============================================================================//
// phys_pushscale MOVED TO MANAGED CODE (plan_shim.md Phase 5, lane 5d)
//
// A deletion that is only an absence is not a check: the file could have been
// emptied and every "is it gone" assertion would still pass. So this pins the
// pair -- no native DECLARATION left in the shim TU, and the three READS still
// here, through the by-name handle that replaced it.
//=============================================================================//

TEST( TakeDamageInfoLibrary, ThePushScaleDeclarationDidNotStayBehind )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	EXPECT_EQ( 0u, CountOf( lib, "ConVar phys_pushscale(" ) )
		<< "a native ConVar phys_pushscale is back in " << kLibrary
		<< ". The declaration is SourceSharp.{Server,Client}/MoveVars.cs now, and "
		   "two ConVars under one name is neither a compile error nor a link "
		   "error -- neither the console nor the game says which one it read.";
}

TEST( TakeDamageInfoLibrary, ThePushScaleReadsAreStillHereThroughTheByNameHandle )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	EXPECT_EQ( 1u, CountOf( lib,
		"CManagedConVarRef phys_pushscale( \"phys_pushscale\" )" ) )
		<< "the by-name handle that replaced the declaration is not in " << kLibrary
		<< ". Without it the explosive damage-force scaling reads nothing at all, "
		   "and a scaling that reads zero looks exactly like one somebody turned off.";

	// Three until the server gc cull deleted CalculateBulletDamageForce and
	// CalculateMeleeDamageForce; CalculateExplosiveDamageForce's read remains.
	EXPECT_EQ( 1u, CountOf( lib, "phys_pushscale.GetFloat()" ) )
		<< "the read of phys_pushscale in " << kLibrary << " is not there. The "
		   "move relocates the DECLARATION and leaves the rule that reads it "
		   "exactly as it was.";
}
