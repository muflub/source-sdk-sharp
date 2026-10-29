//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE RENDER TEXTURE ORACLE (C7, rendertexture.cpp): stock's client hooks into
// the material system's named render targets -- every getter
// (GetPowerOfTwoFrameBufferTexture .. GetTeenyTexture), the name each one asks
// FindTexture for and with which group, the reference each takes, the release
// function registered on the first find, and which references that release
// function drops -- run offline from the PRISTINE game/client/rendertexture.cpp
// and public/materialsystem/MaterialSystemUtil.cpp (CTextureReference) over a
// recording material-system double, and written down as the golden
// src/test/client/Fixtures/rendertexture-stock.tsv.
//
// The double's vtables are built from the compiler's own slot numbers
// (vtable_hook.h), every slot a trap but the handful the subject calls, as
// materialreference_oracle_test's are.
//
// THE SCRIPT. Every getter in rendertexture.h's order, each index of the
// indexed two; the same again (cached: no finds); the release function the
// subject registered; every getter again (the released ones find again, the
// kept ones do not); the release function once more.
//
// BOTH SIDES ARE PINNED TO THE GOLDEN. This test asserts that stock produces
// exactly it; RenderTargetCatalogOracleTests (src/test/client) asserts the
// managed catalog names, groups and release set are the ones it records, and
// OurStoreMakesStocksCalls (below) runs SourceSharp's native store
// (src/client/native/render_target_store.h) over the same double with the
// rows the golden records and holds its calls to stock's. Regenerating the
// golden to make this test green deletes the check: it changes only when the
// script does (touch rendertexture_golden.regen beside this file, run, delete
// the marker, and say why in the commit).
//
//=============================================================================

#include "gtest/gtest.h"
#include "../../native/ss_ci_skip.h"

#include "materialsystem/MaterialSystemUtil.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/itexture.h"
#include "vtable_hook.h"

#include "rendertexture.h"

#include <cstdlib>
#include <fstream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

IMaterialSystem *materials = NULL;

namespace
{

std::string g_Log;

void Log( const std::string &s )
{
	g_Log += s;
	g_Log += '\n';
}

const int kSlots = 512;

void Trap()
{
	ADD_FAILURE() << "a slot the double does not answer was called";
}

struct Fake_t
{
	void **pVtbl;
	void *pTable[kSlots];
	int nId;

	explicit Fake_t( int id )
		: pVtbl( pTable )
		, nId( id )
	{
		for ( int i = 0; i < kSlots; i++ )
			pTable[i] = (void *)&Trap;
	}

	template <typename Fn, typename Impl>
	void Set( Fn pmf, Impl impl )
	{
		const int slot = VTableHook::MemberOf( pmf ).slot;
		ASSERT_GE( slot, 0 );
		ASSERT_LT( slot, kSlots );
		pTable[slot] = (void *)impl;
	}
};

std::string Name( const void *p )
{
	return p ? "#" + std::to_string( static_cast<const Fake_t *>( p )->nId ) : "null";
}

std::string Str( const char *p )
{
	return p ? p : "(null)";
}

void IncRef( void *self )
{
	Log( "inc\t" + Name( self ) );
}

void DecRef( void *self )
{
	Log( "dec\t" + Name( self ) );
}

void DeleteIfUnref( void *self )
{
	Log( "deleteifunref\t" + Name( self ) );
}

bool IsError( const void *self )
{
	Log( "iserror\t" + Name( self ) );
	return false;
}

// One texture per name, numbered in the order first asked for, so a find
// that comes back answers the same texture.
std::vector<Fake_t *> g_Textures;
std::map<std::string, Fake_t *> g_TexturesByName;
Fake_t g_System( 100 );
MaterialBufferReleaseFunc_t g_pRelease = NULL;
int g_nReleaseFuncs = 0;

ITexture *FindTexture( void *, const char *pName, const char *pGroup, bool bComplain, int nFlags )
{
	Log( "findtexture\t" + Str( pName ) + "\t" + Str( pGroup ) + "\t" + std::to_string( bComplain ? 1 : 0 ) + "\t" +
		 std::to_string( nFlags ) );
	auto it = g_TexturesByName.find( Str( pName ) );
	if ( it != g_TexturesByName.end() )
		return (ITexture *)it->second;
	Fake_t *pTex = new Fake_t( (int)g_Textures.size() + 1 );
	pTex->Set( &ITexture::IncrementReferenceCount, &IncRef );
	pTex->Set( &ITexture::DecrementReferenceCount, &DecRef );
	pTex->Set( &ITexture::DeleteIfUnreferenced, &DeleteIfUnref );
	pTex->Set( &ITexture::IsError, &IsError );
	g_Textures.push_back( pTex );
	g_TexturesByName[Str( pName )] = pTex;
	return (ITexture *)pTex;
}

void AddReleaseFunc( void *, MaterialBufferReleaseFunc_t func )
{
	++g_nReleaseFuncs;
	g_pRelease = func;
	Log( "addreleasefunc\t" + std::to_string( g_nReleaseFuncs ) );
}

void Build()
{
	g_Log.clear();
	g_pRelease = NULL;
	g_nReleaseFuncs = 0;
	// The textures are never freed: stock's kept references still point at
	// them when a later script (the store's) builds the double again.
	g_Textures.clear();
	g_TexturesByName.clear();
	g_System.Set( &IMaterialSystem::FindTexture, &FindTexture );
	g_System.Set( &IMaterialSystem::AddReleaseFunc, &AddReleaseFunc );
	materials = (IMaterialSystem *)&g_System;
}

// rendertexture.h's getters, in its order, each index of the two indexed
// ones. The arg column is the index, or - for a getter that takes none.
struct Getter_t
{
	const char *pszName;
	int nArg;
};

const Getter_t kGetters[] = {
	{ "GetPowerOfTwoFrameBufferTexture", -1 },
	{ "GetFullFrameFrameBufferTexture", 0 },
	{ "GetFullFrameFrameBufferTexture", 1 },
	{ "GetFullFrameFrameBufferTexture", 2 },
	{ "GetFullFrameFrameBufferTexture", 3 },
	{ "GetWaterReflectionTexture", -1 },
	{ "GetWaterRefractionTexture", -1 },
	{ "GetFullscreenTexture", -1 },
	{ "GetCameraTexture", -1 },
	{ "GetFullFrameDepthTexture", -1 },
	{ "GetSmallBufferHDR0", -1 },
	{ "GetSmallBufferHDR1", -1 },
	{ "GetSmallBuffer0", -1 },
	{ "GetSmallBuffer1", -1 },
	{ "GetTeenyTexture", 0 },
	{ "GetTeenyTexture", 1 },
	{ "GetTeenyTexture", 2 },
};

// The getter by its row, through a function of the shape the caller hands in
// (stock's free functions here; the native store's in a later test).
typedef ITexture *( *GetFn_t )( const Getter_t & );

ITexture *StockGet( const Getter_t &g )
{
	const std::string n = g.pszName;
	if ( n == "GetPowerOfTwoFrameBufferTexture" )
		return GetPowerOfTwoFrameBufferTexture();
	if ( n == "GetFullFrameFrameBufferTexture" )
		return GetFullFrameFrameBufferTexture( g.nArg );
	if ( n == "GetWaterReflectionTexture" )
		return GetWaterReflectionTexture();
	if ( n == "GetWaterRefractionTexture" )
		return GetWaterRefractionTexture();
	if ( n == "GetFullscreenTexture" )
		return GetFullscreenTexture();
	if ( n == "GetCameraTexture" )
		return GetCameraTexture();
	if ( n == "GetFullFrameDepthTexture" )
		return GetFullFrameDepthTexture();
	if ( n == "GetSmallBufferHDR0" )
		return GetSmallBufferHDR0();
	if ( n == "GetSmallBufferHDR1" )
		return GetSmallBufferHDR1();
	if ( n == "GetSmallBuffer0" )
		return GetSmallBuffer0();
	if ( n == "GetSmallBuffer1" )
		return GetSmallBuffer1();
	if ( n == "GetTeenyTexture" )
		return GetTeenyTexture( g.nArg );
	ADD_FAILURE() << "no getter " << n;
	return NULL;
}

void Pass( const char *pszPhase, GetFn_t get )
{
	Log( std::string( "phase\t" ) + pszPhase );
	for ( const Getter_t &g : kGetters )
	{
		Log( std::string( "G\t" ) + g.pszName + "\t" + ( g.nArg < 0 ? std::string( "-" ) : std::to_string( g.nArg ) ) );
		ITexture *p = get( g );
		Log( "holds\t" + Name( p ) );
	}
}

void Release( const char *pszPhase )
{
	Log( std::string( "phase\t" ) + pszPhase );
	if ( g_pRelease )
		g_pRelease();
	else
		Log( "norelease" );
}

// Stock's references are file statics that a release does not all clear (the
// fullscreen, HDR and teeny textures are kept), so the script runs ONCE per
// process and every test reads that one run.
std::string RunScript()
{
	static std::string s_Run;
	static bool s_bRan = false;
	if ( !s_bRan )
	{
		s_bRan = true;
		Build();
		Pass( "first", &StockGet );
		Pass( "again", &StockGet );
		Release( "release" );
		Pass( "after", &StockGet );
		Release( "release2" );
		s_Run = g_Log;
	}
	return s_Run;
}

std::string RepoFile( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

std::string GoldenPath()
{
	return RepoFile( "src/test/client/Fixtures/rendertexture-stock.tsv" );
}

std::string MarkerPath()
{
	return RepoFile( "src/test/oracle/native/rendertexture_golden.regen" );
}

std::string ReadGolden()
{
	std::ifstream in( GoldenPath() );
	if ( !in.good() )
		return std::string();
	std::stringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

void ExpectSameLines( const std::string &run, const std::string &golden, const char *pszWho )
{
	std::istringstream a( run ), b( golden );
	std::string la, lb;
	int n = 0;
	while ( true )
	{
		const bool ha = (bool)std::getline( a, la );
		const bool hb = (bool)std::getline( b, lb );
		++n;
		if ( !ha && !hb )
			break;
		ASSERT_TRUE( ha && hb && la == lb )
			<< "line " << n << ": " << pszWho << " writes\n  " << ( ha ? la : "<end>" ) << "\nand the golden has\n  "
			<< ( hb ? lb : "<end>" ) << "\nDo NOT regenerate the golden to make this pass; see the file header.";
	}
}

} // namespace

TEST( RenderTextureOracle, StockProducesTheGolden )
{
	const std::string run = RunScript();

	if ( std::ifstream( MarkerPath() ).good() )
	{
		std::ofstream out( GoldenPath() );
		out << run;
	}

	const std::string golden = ReadGolden();
	ASSERT_FALSE( golden.empty() ) << GoldenPath() << " is missing or empty";
	ASSERT_GT( golden.size(), 2000u ) << "the golden is truncated";
	ExpectSameLines( run, golden, "stock" );
}

// Each arm the script exists to reach, found in the run: a check that the
// golden is not silently a recording of one path.
TEST( RenderTextureOracle, TheScriptReachesEveryArm )
{
	const std::string run = RunScript();

	struct Arm_t
	{
		const char *pszNeedle;
		const char *pszWhat;
	};

	static const Arm_t kArms[] = {
		{ "findtexture\t_rt_PowerOfTwoFB\tRenderTargets\t1\t0\ninc\t#1\naddreleasefunc\t1\n",
		  "the first find registered the release function" },
		{ "findtexture\t_rt_FullFrameFB\tRenderTargets", "index 0 of the frame buffers has no number" },
		{ "findtexture\t_rt_FullFrameFB3\tRenderTargets", "index 3 of the frame buffers is numbered" },
		{ "findtexture\t_rt_TeenyFB2\tRenderTargets", "the teeny textures are numbered" },
		{ "phase\trelease\ndec\t#1\n", "the release function dropped the first reference" },
		{ "phase\tafter\nG\tGetPowerOfTwoFrameBufferTexture\t-\nfindtexture\t_rt_PowerOfTwoFB",
		  "a released texture is found again" },
		{ "G\tGetFullscreenTexture\t-\nholds\t#", "a getter answered a texture" },
	};
	for ( const Arm_t &arm : kArms )
		EXPECT_NE( run.find( arm.pszNeedle ), std::string::npos ) << arm.pszWhat << ": no";

	// Exactly one release function, for all of them.
	EXPECT_EQ( run.find( "addreleasefunc\t2" ), std::string::npos ) << "the release function was registered twice";

	// A kept texture is not found again after the release: the fullscreen
	// texture's only find is the first pass's.
	size_t n = 0;
	for ( size_t at = run.find( "findtexture\t_rt_Fullscreen\t" ); at != std::string::npos;
		  at = run.find( "findtexture\t_rt_Fullscreen\t", at + 1 ) )
		++n;
	EXPECT_EQ( n, 1u ) << "the fullscreen texture should be found once and kept across the release";

	// The cached pass found nothing.
	const size_t again = run.find( "phase\tagain\n" );
	const size_t release = run.find( "phase\trelease\n" );
	ASSERT_NE( again, std::string::npos );
	ASSERT_NE( release, std::string::npos );
	EXPECT_EQ( run.substr( again, release - again ).find( "findtexture" ), std::string::npos )
		<< "the second pass should answer every getter from its reference";
}

//-----------------------------------------------------------------------------
// THE STORE, HELD TO STOCK. SourceSharp's native store carries out the rows
// managed declares (src/client/native/render_target_store.h); here it is
// handed the rows the golden records -- each getter's name, group, complaint
// and flags from its first find, and its place in the release's order -- and
// must make stock's calls, in stock's order, over the same double.
//-----------------------------------------------------------------------------
#include "render_target_store.h"

namespace
{

CRenderTargetStore g_Store;

void StoreRelease()
{
	g_Store.Release();
}

// A getter's slot: its row in kGetters, which is rendertexture.h's order and
// the store's SS_RT_* numbering.
int SlotOf( const std::string &getter, const std::string &arg )
{
	const int nArg = arg == "-" ? -1 : atoi( arg.c_str() );
	for ( int i = 0; i < (int)( sizeof( kGetters ) / sizeof( kGetters[0] ) ); i++ )
	{
		if ( getter == kGetters[i].pszName && nArg == kGetters[i].nArg )
			return i;
	}
	return -1;
}

ITexture *StoreGet( const Getter_t &g )
{
	return g_Store.Get( SlotOf( g.pszName, g.nArg < 0 ? std::string( "-" ) : std::to_string( g.nArg ) ) );
}

struct GoldenRow_t
{
	std::string Name, Group;
	int Complain = 0, Flags = 0, ReleaseOrder = 0;
};

// The rows, from the golden's own lines.
std::vector<GoldenRow_t> RowsFromGolden( const std::string &golden )
{
	std::vector<GoldenRow_t> rows( SS_RT_COUNT );
	std::map<std::string, int> slotOfTexture;
	int nReleased = 0;
	std::istringstream in( golden );
	std::string line, phase;
	int slot = -1;
	while ( std::getline( in, line ) )
	{
		std::vector<std::string> f;
		std::istringstream ls( line );
		std::string c;
		while ( std::getline( ls, c, '\t' ) )
			f.push_back( c );
		if ( f.size() < 2 )
			continue;
		if ( f[0] == "phase" )
			phase = f[1];
		else if ( f[0] == "G" && f.size() == 3 )
			slot = SlotOf( f[1], f[2] );
		else if ( f[0] == "findtexture" && phase == "first" && slot >= 0 && f.size() == 5 )
		{
			rows[slot].Name = f[1];
			rows[slot].Group = f[2];
			rows[slot].Complain = atoi( f[3].c_str() );
			rows[slot].Flags = atoi( f[4].c_str() );
		}
		else if ( f[0] == "holds" && phase == "first" && slot >= 0 )
			slotOfTexture[f[1]] = slot;
		else if ( f[0] == "dec" && phase == "release" && slotOfTexture.count( f[1] ) )
			rows[slotOfTexture[f[1]]].ReleaseOrder = ++nReleased;
	}
	return rows;
}

} // namespace

TEST( RenderTextureOracle, OurStoreMakesStocksCalls )
{
	const std::string golden = ReadGolden();
	ASSERT_FALSE( golden.empty() ) << GoldenPath() << " is missing or empty";

	// Every getter is a slot, in order.
	ASSERT_EQ( (int)( sizeof( kGetters ) / sizeof( kGetters[0] ) ), (int)SS_RT_COUNT );

	const std::vector<GoldenRow_t> rows = RowsFromGolden( golden );
	int named = 0, released = 0;
	for ( const GoldenRow_t &r : rows )
	{
		named += r.Name.empty() ? 0 : 1;
		released += r.ReleaseOrder ? 1 : 0;
	}
	// The stimulus reached the store: every row names a texture; some are released and some kept.
	ASSERT_EQ( named, (int)SS_RT_COUNT );
	ASSERT_GT( released, 0 );
	ASSERT_LT( released, (int)SS_RT_COUNT );

	Build();
	g_Store.Reset();
	g_Store.SetReleaseFunc( &StoreRelease );
	for ( int i = 0; i < SS_RT_COUNT; i++ )
		ASSERT_TRUE( g_Store.Declare( i, rows[i].Name.c_str(), rows[i].Group.c_str(), rows[i].Complain, rows[i].Flags,
									  rows[i].ReleaseOrder ) )
			<< "slot " << i;
	EXPECT_EQ( g_Log.find( "findtexture" ), std::string::npos ) << "a declaration found a texture before its first use";

	g_Log.clear();
	Pass( "first", &StoreGet );
	Pass( "again", &StoreGet );
	Release( "release" );
	Pass( "after", &StoreGet );
	Release( "release2" );
	ExpectSameLines( g_Log, golden, "the store" );
}

// A row the store cannot hold is refused, and an undeclared slot answers no texture.
TEST( RenderTextureOracle, TheStoreRefusesWhatItCannotHold )
{
	Build();
	g_Store.Reset();
	g_Store.SetReleaseFunc( &StoreRelease );
	EXPECT_FALSE( g_Store.Declare( -1, "_rt_x", "RenderTargets", 1, 0, 0 ) );
	EXPECT_FALSE( g_Store.Declare( SS_RT_COUNT, "_rt_x", "RenderTargets", 1, 0, 0 ) );
	EXPECT_FALSE( g_Store.Declare( 0, "", "RenderTargets", 1, 0, 0 ) );
	EXPECT_FALSE( g_Store.Declare( 0, NULL, "RenderTargets", 1, 0, 0 ) );
	EXPECT_FALSE( g_Store.Declare( 0, "_rt_x", "RenderTargets", 1, 0, SS_RT_COUNT + 1 ) );
	EXPECT_FALSE( g_Store.Declare( 0, std::string( 80, 'x' ).c_str(), "RenderTargets", 1, 0, 0 ) );
	EXPECT_EQ( g_Store.Get( 0 ), (ITexture *)NULL );
	EXPECT_EQ( g_Store.Get( -1 ), (ITexture *)NULL );
	EXPECT_EQ( g_Log.find( "findtexture" ), std::string::npos );

	// The positive arm: the same slot declared properly finds on its first use.
	ASSERT_TRUE( g_Store.Declare( 0, "_rt_x", "RenderTargets", 1, 0, 1 ) );
	EXPECT_NE( g_Store.Get( 0 ), (ITexture *)NULL );
	EXPECT_NE( g_Log.find( "findtexture\t_rt_x\tRenderTargets\t1\t0" ), std::string::npos );
}

// A managed reload declares the same rows: the reference is kept. A row that
// names another texture drops it, and the next use finds the new name.
TEST( RenderTextureOracle, TheStoreKeepsAReferenceAcrossTheSameDeclaration )
{
	Build();
	g_Store.Reset();
	g_Store.SetReleaseFunc( &StoreRelease );
	ASSERT_TRUE( g_Store.Declare( 3, "_rt_a", "RenderTargets", 1, 0, 1 ) );
	ITexture *pFirst = g_Store.Get( 3 );
	ASSERT_NE( pFirst, (ITexture *)NULL );

	g_Log.clear();
	ASSERT_TRUE( g_Store.Declare( 3, "_rt_a", "RenderTargets", 1, 0, 1 ) );
	EXPECT_EQ( g_Store.Get( 3 ), pFirst );
	EXPECT_EQ( g_Log, "" ) << "the same declaration touched the reference";

	ASSERT_TRUE( g_Store.Declare( 3, "_rt_b", "RenderTargets", 1, 0, 1 ) );
	ITexture *pSecond = g_Store.Get( 3 );
	EXPECT_NE( pSecond, pFirst );
	EXPECT_EQ( g_Log,
			   "dec\t" + Name( pFirst ) + "\nfindtexture\t_rt_b\tRenderTargets\t1\t0\ninc\t" + Name( pSecond ) + "\n" );
}
