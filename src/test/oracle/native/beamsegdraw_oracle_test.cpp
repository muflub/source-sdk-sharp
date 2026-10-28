// tier2's CBeamSegDraw, from tier2.a's own beamsegdraw.o linked into this
// binary (plan_client_novalve C9). The SDK ships no source for it: the archive
// object is the reference. Its Start/NextSeg/End maths is managed's now
// (src/client/managed/Ropes/BeamSegDraw.cs); native only writes the vertices
// managed decided (src/client/native/beam_strip_vertex.h).
//
// The stock object runs a seeded scenario over a recording material-system
// double: a render context that answers the camera and a dynamic mesh, and a
// mesh whose LockMesh hands out one interleaved vertex array and an index
// array, every other slot a trap. Both ways the client used it are run: a
// strip on its own dynamic mesh (the beam executor) and strips sharing one
// caller's triangle-list builder from a base vertex (the rope executor).
//
// BOTH SIDES ARE PINNED TO THE GOLDEN src/test/client/Fixtures/
// beamsegdraw-stock.tsv. This test asserts that stock produces exactly it;
// SourceSharp.Client.Tests' BeamSegDrawOracleTests asserts the managed port
// computes the same vertices from the same rows, bit for bit; and the third
// test here asserts beam_strip_vertex.h writes, from the vertices managed
// computes, the buffers stock wrote, byte for byte. Regenerating the golden to
// make a test green deletes the check: it changes only when the scenario does,
// and then on purpose (touch beamsegdraw_golden.regen beside this file, run,
// delete the marker, and say why in the commit).

#include "gtest/gtest.h"
#include "../../native/ss_ci_skip.h"

#include "beam_strip_vertex.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imesh.h"
#include "tier2/beamsegdraw.h"
#include "vtable_hook.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{
// tier2.a was compiled against this layout: CBeamSegDraw's members sit at the
// offsets its NextSeg reads (m_Seg at 0x2e0, the render context last).
static_assert( sizeof( CMeshBuilder ) == 0x2c8, "CMeshBuilder is not the layout tier2.a was built against" );
static_assert( sizeof( CBeamSegDraw ) == 0x320, "CBeamSegDraw is not the layout tier2.a was built against" );

const int kSlots = 512;
const int kMaxVerts = 256;
const int kMaxIndices = 1024;

void Trap()
{
	ADD_FAILURE() << "a slot the double does not answer was called";
}

struct Fake_t
{
	void **pVtbl;
	void *pTable[kSlots];

	Fake_t()
		: pVtbl( pTable )
	{
		for ( int i = 0; i < kSlots; i++ )
			pTable[i] = (void *)&Trap;
	}

	template <typename Fn, typename Impl>
	void Set( Fn pmf, Impl impl )
	{
		const VTableHook::Member m = VTableHook::MemberOf( pmf );
		ASSERT_EQ( m.adjust, 0 );
		ASSERT_GE( m.slot, 0 );
		ASSERT_LT( m.slot, kSlots );
		pTable[m.slot] = (void *)impl;
	}
};

// One interleaved vertex: every attribute CMeshBuilder may be asked to write.
struct Vertex_t
{
	float pos[3];
	unsigned char color[4];
	float tc0[2];
	float tc1[2];
	float tangentS[3];
	float tangentT[3];
	float normal[3];
	unsigned char specular[4];
};

// What the double saw and holds.
struct Record_t
{
	Vertex_t verts[kMaxVerts];
	unsigned short indices[kMaxIndices];
	float dummy[64];
	int nPrimitiveType;
	int nLocks;
	int nUnlockVerts;
	int nUnlockIndices;
	int nDraws;
	Vector camera;
};

Record_t g_Rec;
Fake_t g_Context, g_Mesh;

void GetCamera( void *, Vector *pOut )
{
	*pOut = g_Rec.camera;
}

IMesh *GetDynamicMesh( void *, bool, IMesh *, IMesh *, IMaterial * )
{
	return (IMesh *)&g_Mesh;
}

void SetPrimitiveType( void *, MaterialPrimitiveType_t type )
{
	g_Rec.nPrimitiveType = type;
}

void LockMesh( void *, int nVerts, int nIndices, MeshDesc_t &desc )
{
	EXPECT_LE( nVerts, kMaxVerts );
	EXPECT_LE( nIndices, kMaxIndices );
	g_Rec.nLocks++;
	memset( &desc, 0, sizeof( desc ) );

	const int stride = sizeof( Vertex_t );
	desc.m_VertexSize_Position = stride;
	desc.m_VertexSize_Normal = stride;
	desc.m_VertexSize_Color = stride;
	desc.m_VertexSize_Specular = stride;
	desc.m_VertexSize_TexCoord[0] = stride;
	desc.m_VertexSize_TexCoord[1] = stride;
	desc.m_VertexSize_TangentS = stride;
	desc.m_VertexSize_TangentT = stride;
	desc.m_ActualVertexSize = stride;
	desc.m_CompressionType = VERTEX_COMPRESSION_NONE;
	desc.m_pPosition = g_Rec.verts[0].pos;
	desc.m_pNormal = g_Rec.verts[0].normal;
	desc.m_pColor = g_Rec.verts[0].color;
	desc.m_pSpecular = g_Rec.verts[0].specular;
	desc.m_pTexCoord[0] = g_Rec.verts[0].tc0;
	desc.m_pTexCoord[1] = g_Rec.verts[0].tc1;
	for ( int i = 2; i < VERTEX_MAX_TEXTURE_COORDINATES; i++ )
		desc.m_pTexCoord[i] = g_Rec.dummy;
	desc.m_pTangentS = g_Rec.verts[0].tangentS;
	desc.m_pTangentT = g_Rec.verts[0].tangentT;
	desc.m_pBoneWeight = g_Rec.dummy;
	desc.m_pBoneMatrixIndex = (unsigned char *)g_Rec.dummy;
	desc.m_pWrinkle = g_Rec.dummy;
	desc.m_pUserData = g_Rec.dummy;
	desc.m_pIndices = g_Rec.indices;
	desc.m_nIndexSize = 1;
}

void UnlockMesh( void *, int nVerts, int nIndices, MeshDesc_t & )
{
	g_Rec.nUnlockVerts = nVerts;
	g_Rec.nUnlockIndices = nIndices;
}

void Draw( void *, int, int )
{
	g_Rec.nDraws++;
}

void Build()
{
	g_Context.Set( &IMatRenderContext::GetWorldSpaceCameraPosition, &GetCamera );
	g_Context.Set( &IMatRenderContext::GetDynamicMesh, &GetDynamicMesh );
	g_Mesh.Set( &IMesh::SetPrimitiveType, &SetPrimitiveType );
	g_Mesh.Set( &IMesh::LockMesh, &LockMesh );
	g_Mesh.Set( &IMesh::UnlockMesh, &UnlockMesh );
	g_Mesh.Set( static_cast<void ( IMesh::* )( int, int )>( &IMesh::Draw ), &Draw );
}

void Reset( const Vector &camera )
{
	memset( (void *)&g_Rec, 0, sizeof( g_Rec ) );
	g_Rec.nPrimitiveType = -1;
	g_Rec.camera = camera;
}

IMatRenderContext *Context()
{
	return (IMatRenderContext *)&g_Context;
}

//-----------------------------------------------------------------------------
// The scenario: seeded strips, and the shapes a strip can take that a random
// one never would.
//-----------------------------------------------------------------------------
struct Rng_t
{
	unsigned int s = 0x2545F491u;

	unsigned int Next()
	{
		s ^= s << 13;
		s ^= s >> 17;
		s ^= s << 5;
		return s;
	}

	float Range( float lo, float hi ) { return lo + ( hi - lo ) * (float)( Next() & 0xFFFFFF ) / (float)0xFFFFFF; }

	// k / 255: a colour CMeshBuilder's byte conversion maps back to k.
	float Unit() { return (float)( Next() % 256 ) / 255.0f; }
};

struct Strip_t
{
	std::vector<BeamSeg_t> segs;
};

// A case: one camera, and one strip on its own mesh ('m') or several strips
// sharing a builder from a base vertex ('b').
struct Case_t
{
	char cMode;
	int nBase;
	Vector camera;
	std::vector<Strip_t> strips;
};

BeamSeg_t RandomSeg( Rng_t &r, const Vector &around )
{
	BeamSeg_t s;
	s.m_vPos.Init( around.x + r.Range( -300, 300 ), around.y + r.Range( -300, 300 ), around.z + r.Range( -300, 300 ) );
	s.m_vColor.Init( r.Unit(), r.Unit(), r.Unit() );
	s.m_flTexCoord = r.Range( -4, 4 );
	s.m_flWidth = r.Range( 0, 40 );
	s.m_flAlpha = r.Unit();
	return s;
}

std::vector<Case_t> Scenario()
{
	Rng_t r;
	std::vector<Case_t> cases;

	// Random strips, 2 to 16 segments, both ways.
	for ( int i = 0; i < 60; i++ )
	{
		Case_t c;
		c.cMode = ( i % 2 ) ? 'b' : 'm';
		c.nBase = c.cMode == 'b' ? (int)( r.Next() % 40 ) : 0;
		c.camera.Init( r.Range( -2000, 2000 ), r.Range( -2000, 2000 ), r.Range( -500, 500 ) );
		const Vector around( r.Range( -1000, 1000 ), r.Range( -1000, 1000 ), r.Range( -200, 200 ) );
		const int nStrips = c.cMode == 'b' ? 1 + (int)( r.Next() % 3 ) : 1;
		for ( int k = 0; k < nStrips; k++ )
		{
			Strip_t s;
			const int n = 2 + (int)( r.Next() % 15 );
			for ( int j = 0; j < n; j++ )
				s.segs.push_back( RandomSeg( r, around ) );
			c.strips.push_back( s );
		}
		cases.push_back( c );
	}

	// A rope-like strip: a smooth curve with near-parallel normals.
	{
		Case_t c;
		c.cMode = 'b';
		c.nBase = 0;
		c.camera.Init( 10, -250, 64 );
		Strip_t s;
		for ( int j = 0; j < 12; j++ )
		{
			BeamSeg_t seg = RandomSeg( r, Vector( 0, 0, 0 ) );
			seg.m_vPos.Init( j * 16.0f, 0.5f * j, 100.0f - 0.8f * j * j );
			seg.m_flWidth = 2.0f;
			s.segs.push_back( seg );
		}
		c.strips.push_back( s );
		cases.push_back( c );
	}

	// Degenerate shapes: two segments at one point (a zero tangent), the camera
	// on the beam's line (a zero normal), a zero and a negative width.
	{
		Case_t c;
		c.cMode = 'm';
		c.nBase = 0;
		c.camera.Init( 0, 0, 0 );
		Strip_t s;
		BeamSeg_t a = RandomSeg( r, Vector( 0, 0, 0 ) );
		a.m_vPos.Init( 100, 0, 0 );
		BeamSeg_t b = a;
		BeamSeg_t d = RandomSeg( r, Vector( 0, 0, 0 ) );
		d.m_vPos.Init( 200, 0, 0 );
		d.m_flWidth = 0;
		BeamSeg_t e = RandomSeg( r, Vector( 0, 0, 0 ) );
		e.m_vPos.Init( 200, 50, 10 );
		e.m_flWidth = -8;
		s.segs = { a, b, d, e };
		c.strips.push_back( s );
		cases.push_back( c );
	}
	return cases;
}

//-----------------------------------------------------------------------------
// Rows. Floats are their bits in hex, so a row is the value exactly.
//-----------------------------------------------------------------------------
std::string Hex( float f )
{
	unsigned int u;
	memcpy( &u, &f, 4 );
	char buf[16];
	snprintf( buf, sizeof( buf ), "%08x", u );
	return buf;
}

std::string Floats( const float *p, int n )
{
	std::string s;
	for ( int i = 0; i < n; i++ )
		s += "\t" + Hex( p[i] );
	return s;
}

// Runs stock over one case, into g_Rec.
void RunStock( const Case_t &c )
{
	Reset( c.camera );
	if ( c.cMode == 'm' )
	{
		CBeamSegDraw draw;
		Strip_t s = c.strips[0];
		draw.Start( Context(), (int)s.segs.size(), NULL );
		for ( BeamSeg_t &seg : s.segs )
			draw.NextSeg( &seg );
		draw.End();
		return;
	}

	int nVerts = 0, nIndices = 0;
	for ( const Strip_t &s : c.strips )
	{
		nVerts += (int)s.segs.size() * 2;
		nIndices += ( (int)s.segs.size() - 1 ) * 6;
	}
	CMeshBuilder mb;
	mb.Begin( (IMesh *)&g_Mesh, MATERIAL_TRIANGLES, c.nBase + nVerts, nIndices );
	for ( int i = 0; i < c.nBase; i++ )
		mb.AdvanceVertex(); // the vertices earlier strips in the builder wrote
	CBeamSegDraw draw;
	int nSoFar = c.nBase;
	for ( const Strip_t &strip : c.strips )
	{
		Strip_t s = strip;
		draw.Start( Context(), (int)s.segs.size(), NULL, &mb, nSoFar );
		for ( BeamSeg_t &seg : s.segs )
			draw.NextSeg( &seg );
		draw.End();
		nSoFar += (int)s.segs.size() * 2;
	}
	mb.End();
}

int VertexCount( const Case_t &c )
{
	int n = c.nBase;
	for ( const Strip_t &s : c.strips )
		n += (int)s.segs.size() * 2;
	return n;
}

int IndexCount( const Case_t &c )
{
	if ( c.cMode == 'm' )
		return (int)c.strips[0].segs.size() * 2; // a triangle strip's indices are its vertices
	int n = 0;
	for ( const Strip_t &s : c.strips )
		n += ( (int)s.segs.size() - 1 ) * 6;
	return n;
}

// What the double holds after a run, as rows.
std::string Buffers( const Case_t &c )
{
	std::string out;
	char buf[96];
	snprintf( buf, sizeof( buf ), "P\t%d\t%d\t%d\t%d\t%d\n", g_Rec.nPrimitiveType, g_Rec.nLocks, g_Rec.nUnlockVerts,
			  g_Rec.nUnlockIndices, g_Rec.nDraws );
	out += buf;
	for ( int i = c.nBase; i < VertexCount( c ); i++ )
	{
		const Vertex_t &v = g_Rec.verts[i];
		snprintf( buf, sizeof( buf ), "V\t%d", i );
		out += buf;
		out += Floats( v.pos, 3 );
		snprintf( buf, sizeof( buf ), "\t%02x%02x%02x%02x", v.color[0], v.color[1], v.color[2], v.color[3] );
		out += buf;
		out += Floats( v.tc0, 2 ) + Floats( v.tc1, 2 ) + Floats( v.tangentS, 3 ) + Floats( v.tangentT, 3 ) + "\n";
	}
	out += "I";
	for ( int i = 0; i < IndexCount( c ); i++ )
	{
		snprintf( buf, sizeof( buf ), "\t%d", g_Rec.indices[i] );
		out += buf;
	}
	out += "\n";
	return out;
}

std::string RunScenario()
{
	Build();
	std::string out = "# tier2.a beamsegdraw.o over beamsegdraw_oracle_test's scenario; README-beamsegdraw-stock.md\n";
	const std::vector<Case_t> cases = Scenario();
	char buf[64];
	for ( size_t i = 0; i < cases.size(); i++ )
	{
		const Case_t &c = cases[i];
		snprintf( buf, sizeof( buf ), "C\t%d\t%c\t%d", (int)i, c.cMode, c.nBase );
		out += buf + Floats( c.camera.Base(), 3 ) + "\n";
		for ( const Strip_t &s : c.strips )
		{
			snprintf( buf, sizeof( buf ), "S\t%d\n", (int)s.segs.size() );
			out += buf;
			for ( const BeamSeg_t &seg : s.segs )
				out += "G" + Floats( (const float *)&seg, 9 ) + "\n";
		}
		RunStock( c );
		out += Buffers( c );
	}
	return out;
}

std::string RepoPath( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

std::string GoldenPath()
{
	return RepoPath( "src/test/client/Fixtures/beamsegdraw-stock.tsv" );
}

std::string MarkerPath()
{
	return RepoPath( "src/test/oracle/native/beamsegdraw_golden.regen" );
}

std::string ReadGolden()
{
	std::ifstream in( GoldenPath() );
	std::stringstream ss;
	ss << in.rdbuf();
	return ss.str();
}
} // namespace

TEST( BeamSegDrawOracle, StockProducesTheGolden )
{
	SS_SKIP_ON_CI(
		"a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit" );
	const std::string run = RunScenario();

	if ( std::ifstream( MarkerPath() ).good() )
	{
		std::ofstream out( GoldenPath() );
		out << run;
	}

	ASSERT_TRUE( std::ifstream( GoldenPath() ).good() ) << GoldenPath() << " is missing";
	const std::string golden = ReadGolden();
	ASSERT_GT( golden.size(), 50000u ) << "the golden is empty or truncated";

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
			<< "line " << n << ": stock now writes\n  " << ( ha ? la : "<end>" ) << "\nand the golden has\n  "
			<< ( hb ? lb : "<end>" ) << "\nDo NOT regenerate the golden to make this pass; see the file header.";
	}
}

// The stimulus reached the subject: stock wrote vertices, drew its own strips,
// and indexed the shared builder from its base.
TEST( BeamSegDrawOracle, TheScenarioReachesEveryArm )
{
	Build();
	const std::vector<Case_t> cases = Scenario();
	int nOwn = 0, nShared = 0;
	for ( const Case_t &c : cases )
	{
		RunStock( c );
		ASSERT_EQ( g_Rec.nLocks, 1 );
		ASSERT_EQ( g_Rec.nUnlockVerts, VertexCount( c ) );
		bool bWrote = false;
		for ( int i = c.nBase; i < VertexCount( c ); i++ )
			bWrote |= g_Rec.verts[i].pos[0] != 0.0f || g_Rec.verts[i].color[3] != 0;
		ASSERT_TRUE( bWrote );
		if ( c.cMode == 'm' )
		{
			EXPECT_EQ( g_Rec.nPrimitiveType, MATERIAL_TRIANGLE_STRIP );
			EXPECT_EQ( g_Rec.nDraws, 1 );
			nOwn++;
		}
		else
		{
			EXPECT_EQ( g_Rec.nDraws, 0 );
			EXPECT_EQ( g_Rec.indices[0], c.nBase );
			nShared++;
		}
	}
	EXPECT_GT( nOwn, 20 );
	EXPECT_GT( nShared, 20 );
}

// beam_strip_vertex.h, given the vertices managed computes (the positions,
// texture coordinates and tangents stock wrote; each segment's colour and
// alpha on its two vertices), writes the buffers stock wrote, byte for byte.
TEST( BeamSegDrawOracle, TheExecutorWritesStocksBuffers )
{
	Build();
	const std::vector<Case_t> cases = Scenario();
	int nCompared = 0;
	for ( const Case_t &c : cases )
	{
		RunStock( c );
		const Record_t stock = g_Rec;

		// Managed's vertices, per strip.
		std::vector<std::vector<float>> strips;
		int iVert = c.nBase;
		for ( const Strip_t &s : c.strips )
		{
			std::vector<float> v;
			for ( size_t k = 0; k < s.segs.size(); k++ )
			{
				for ( int side = 0; side < 2; side++ )
				{
					const Vertex_t &sv = stock.verts[iVert++];
					const BeamSeg_t &seg = s.segs[k];
					const float f[BEAM_STRIP_VERTEX_FLOATS] = {
						sv.pos[0],      sv.pos[1],      sv.pos[2],      seg.m_vColor.x, seg.m_vColor.y, seg.m_vColor.z,
						seg.m_flAlpha,  sv.tc0[0],      sv.tc0[1],      sv.tc1[0],      sv.tc1[1],      sv.tangentS[0],
						sv.tangentS[1], sv.tangentS[2], sv.tangentT[0], sv.tangentT[1], sv.tangentT[2] };
					v.insert( v.end(), f, f + BEAM_STRIP_VERTEX_FLOATS );
				}
			}
			strips.push_back( v );
		}

		Reset( c.camera );
		if ( c.cMode == 'm' )
		{
			ss::beamstrip::DrawStrip( (IMesh *)&g_Mesh, strips[0].data(),
									  (int)strips[0].size() / BEAM_STRIP_VERTEX_FLOATS );
		}
		else
		{
			CMeshBuilder mb;
			mb.Begin( (IMesh *)&g_Mesh, MATERIAL_TRIANGLES, VertexCount( c ), IndexCount( c ) );
			for ( int i = 0; i < c.nBase; i++ )
				mb.AdvanceVertex();
			int nSoFar = c.nBase;
			for ( const std::vector<float> &v : strips )
			{
				const int n = (int)v.size() / BEAM_STRIP_VERTEX_FLOATS;
				ss::beamstrip::WriteIndexed( mb, v.data(), n, nSoFar );
				nSoFar += n;
			}
			mb.End();
		}

		ASSERT_EQ( 0, memcmp( stock.verts, g_Rec.verts, sizeof( stock.verts ) ) ) << "the vertex buffers differ";
		ASSERT_EQ( 0, memcmp( stock.indices, g_Rec.indices, sizeof( stock.indices ) ) ) << "the index buffers differ";
		EXPECT_EQ( stock.nPrimitiveType, g_Rec.nPrimitiveType );
		EXPECT_EQ( stock.nUnlockVerts, g_Rec.nUnlockVerts );
		EXPECT_EQ( stock.nUnlockIndices, g_Rec.nUnlockIndices );
		EXPECT_EQ( stock.nDraws, g_Rec.nDraws );
		nCompared++;
	}
	EXPECT_GT( nCompared, 50 );
}
