// CMaterialReference and CTextureReference, ours (src/client/native/
// material_reference_client.cpp) against upstream's MaterialSystemUtil.cpp
// (plan_client_novalve C9). Upstream's is compiled in this binary with its two
// classes renamed (materialreference_stock.cpp), so both run here against one
// recording double: an IMaterialSystem, IMaterials and ITextures whose vtables
// are built from the compiler's own slot numbers (vtable_hook.h), every slot a
// trap but the handful the two classes call. Each script runs on both, and the
// call logs and the held pointers must match.

#include "gtest/gtest.h"

#include "materialreference_stock.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/itexture.h"
#include "vtable_hook.h"

#include <string>
#include <vector>

IMaterialSystem *materials = NULL;

namespace
{
std::vector<std::string> g_Log;

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

	Fake_t( int id )
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
	g_Log.push_back( "inc " + Name( self ) );
}

void DecRef( void *self )
{
	g_Log.push_back( "dec " + Name( self ) );
}

void DeleteIfUnref( void *self )
{
	g_Log.push_back( "deleteifunref " + Name( self ) );
}

bool IsError( const void * )
{
	return false;
}

Fake_t g_Mat1( 1 ), g_Mat2( 2 ), g_Tex1( 11 ), g_Tex2( 12 ), g_System( 100 );

IMaterial *FindMaterial( void *, const char *pName, const char *pGroup, bool bComplain, const char * )
{
	g_Log.push_back( "findmaterial " + Str( pName ) + " " + Str( pGroup ) + " " + std::to_string( bComplain ) );
	return std::string( Str( pName ) ) == "missing"
		? NULL
		: (IMaterial *)( std::string( Str( pName ) ) == "two" ? &g_Mat2 : &g_Mat1 );
}

IMaterial *CreateMaterial( void *, const char *pName, KeyValues * )
{
	g_Log.push_back( "creatematerial " + Str( pName ) );
	return (IMaterial *)&g_Mat2;
}

IMaterial *FindProcedural( void *, const char *pName, const char *pGroup, KeyValues * )
{
	g_Log.push_back( "findprocedural " + Str( pName ) + " " + Str( pGroup ) );
	return (IMaterial *)&g_Mat1;
}

ITexture *FindTexture( void *, const char *pName, const char *pGroup, bool bComplain, int )
{
	g_Log.push_back( "findtexture " + Str( pName ) + " " + Str( pGroup ) + " " + std::to_string( bComplain ) );
	return std::string( Str( pName ) ) == "missing" ? NULL : (ITexture *)&g_Tex1;
}

ITexture *CreateProcedural( void *, const char *pName, const char *pGroup, int w, int h, ImageFormat fmt, int nFlags )
{
	g_Log.push_back( "createprocedural " + Str( pName ) + " " + Str( pGroup ) + " " + std::to_string( w ) + "x" +
					 std::to_string( h ) + " " + std::to_string( (int)fmt ) + " " + std::to_string( nFlags ) );
	return (ITexture *)&g_Tex2;
}

ITexture *CreateRT( void *, const char *pName, int w, int h, RenderTargetSizeMode_t mode, ImageFormat fmt,
					MaterialRenderTargetDepth_t depth, unsigned int texFlags, unsigned int rtFlags )
{
	g_Log.push_back( "creatert " + Str( pName ) + " " + std::to_string( w ) + "x" + std::to_string( h ) + " " +
					 std::to_string( (int)mode ) + " " + std::to_string( (int)fmt ) + " " +
					 std::to_string( (int)depth ) + " " + std::to_string( texFlags ) + " " +
					 std::to_string( rtFlags ) );
	return (ITexture *)&g_Tex2;
}

void Build()
{
	for ( Fake_t *p : { &g_Mat1, &g_Mat2 } )
	{
		p->Set( &IMaterial::IncrementReferenceCount, &IncRef );
		p->Set( &IMaterial::DecrementReferenceCount, &DecRef );
		p->Set( &IMaterial::IsErrorMaterial, &IsError );
	}
	for ( Fake_t *p : { &g_Tex1, &g_Tex2 } )
	{
		p->Set( &ITexture::IncrementReferenceCount, &IncRef );
		p->Set( &ITexture::DecrementReferenceCount, &DecRef );
		p->Set( &ITexture::DeleteIfUnreferenced, &DeleteIfUnref );
	}
	g_System.Set( &IMaterialSystem::FindMaterial, &FindMaterial );
	g_System.Set( &IMaterialSystem::CreateMaterial, &CreateMaterial );
	g_System.Set( &IMaterialSystem::FindProceduralMaterial, &FindProcedural );
	g_System.Set( &IMaterialSystem::FindTexture, &FindTexture );
	g_System.Set( &IMaterialSystem::CreateProceduralTexture, &CreateProcedural );
	g_System.Set( &IMaterialSystem::CreateNamedRenderTargetTextureEx, &CreateRT );
	materials = (IMaterialSystem *)&g_System;
}

// One script, both classes: the log, and the pointer held after each step.
template <typename MatRef, typename TexRef>
std::vector<std::string> RunScript()
{
	Build();
	g_Log.clear();
	auto held = []( IMaterial *p )
	{
		g_Log.push_back( "holds " + Name( p ) );
	};
	auto heldTex = []( ITexture *p )
	{
		g_Log.push_back( "holds " + Name( p ) );
	};
	{
		MatRef a( "one", "World textures" );
		held( a );
		a.Init( "one", "World textures", false ); // the same material: no new reference
		held( a );
		a.Init( "two", "Model textures", true );
		held( a );
		a.Init( "created", (KeyValues *)NULL );
		held( a );
		a.Init( "proc", "Other", (KeyValues *)NULL );
		held( a );
		MatRef b;
		b.Init( a );
		held( b );
		b.Init( (IMaterial *)NULL );
		held( b );
		a.Init( "missing", "World textures" );
		held( a );
		a.Shutdown();
		held( a );
		MatRef c( NULL, NULL );
		held( c );
	}
	{
		TexRef t;
		t.Init( "tex", "Other", true );
		heldTex( t );
		TexRef u( t );
		heldTex( u );
		u = t; // takes another reference without giving one back
		heldTex( u );
		t.InitProceduralTexture( "proc", "Group", 64, 32, IMAGE_FORMAT_RGBA8888, 7 );
		heldTex( t );
		t.InitRenderTarget( 256, 128, RT_SIZE_NO_CHANGE, IMAGE_FORMAT_RGB888, MATERIAL_RT_DEPTH_ONLY, true,
							(char *)"_rt" );
		heldTex( t );
		t.InitRenderTarget( 16, 16, RT_SIZE_FULL_FRAME_BUFFER, IMAGE_FORMAT_RGBA8888, MATERIAL_RT_DEPTH_SHARED, false,
							NULL );
		heldTex( t );
		t.Shutdown( true );
		heldTex( t );
		t.Init( (ITexture *)&g_Tex1 );
		t.Init( "missing", "Other", false );
		heldTex( t );
	}
	// With no material system, a detach keeps its pointer and touches nothing.
	{
		MatRef d( "one", "World textures" );
		materials = NULL;
		d.Shutdown();
		held( d );
		materials = (IMaterialSystem *)&g_System;
	}
	return g_Log;
}
} // namespace

TEST( MaterialReferenceOracle, OursMakesUpstreamsCallsAndHoldsUpstreamsPointers )
{
	const std::vector<std::string> stock = RunScript<StockMaterialReference, StockTextureReference>();
	const std::vector<std::string> ours = RunScript<CMaterialReference, CTextureReference>();

	// The stimulus reached the subject: a real sequence, with every kind of call in it.
	ASSERT_GT( stock.size(), 40u );
	ASSERT_EQ( stock.size(), ours.size() );
	for ( size_t i = 0; i < stock.size(); i++ )
		EXPECT_EQ( stock[i], ours[i] ) << "step " << i;
}
