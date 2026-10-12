//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — CMaterialReference and CTextureReference, ours
//
// SS_REASON b  an interface resolved by name at load
//
// plan_client_novalve C9: public/materialsystem/MaterialSystemUtil.h's two
// reference holders, the members client.so took from tier2.a's
// MaterialSystemUtil.o. Each holds one material or texture and its reference
// count: every attach takes a reference, every detach gives it back, and the
// creating calls (CreateMaterial, CreateProceduralTexture, the render target)
// arrive already referenced. materialreference_oracle_test holds the call
// sequence against upstream's MaterialSystemUtil.cpp over a recording double.
//
// Not carried: Init's IsErrorMaterial check, which only prints on OS X; and
// the two _X360 render-target builders, which are not compiled here either.
//=============================================================================//

#include "materialsystem/MaterialSystemUtil.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/itexture.h"
#include "tier2/tier2.h"
#include "tier0/dbg.h"

//-----------------------------------------------------------------------------
// CMaterialReference
//-----------------------------------------------------------------------------
CMaterialReference::CMaterialReference( char const *pMaterialName, const char *pTextureGroupName, bool bComplain )
	: m_pMaterial( NULL )
{
	if ( !pMaterialName )
		return;
	Assert( pTextureGroupName );
	Init( pMaterialName, pTextureGroupName, bComplain );
}

CMaterialReference::~CMaterialReference()
{
	Shutdown();
}

void CMaterialReference::Init( char const *pMaterialName, const char *pTextureGroupName, bool bComplain )
{
	IMaterial *pFound = materials->FindMaterial( pMaterialName, pTextureGroupName, bComplain );
	Assert( pFound );
	Init( pFound );
}

// A created material arrives with its one reference already taken.
void CMaterialReference::Init( const char *pMaterialName, KeyValues *pVMTKeyValues )
{
	Shutdown();
	m_pMaterial = materials->CreateMaterial( pMaterialName, pVMTKeyValues );
}

void CMaterialReference::Init( const char *pMaterialName, const char *pTextureGroupName, KeyValues *pVMTKeyValues )
{
	IMaterial *pFound = materials->FindProceduralMaterial( pMaterialName, pTextureGroupName, pVMTKeyValues );
	Assert( pFound );
	Init( pFound );
}

// Re-attaching the material already held keeps its one reference.
void CMaterialReference::Init( IMaterial *pMaterial )
{
	if ( pMaterial == m_pMaterial )
		return;
	Shutdown();
	m_pMaterial = pMaterial;
	if ( m_pMaterial )
		m_pMaterial->IncrementReferenceCount();
}

void CMaterialReference::Init( CMaterialReference &ref )
{
	Init( ref.m_pMaterial );
}

// Gives the reference back -- unless the material system has gone, when the
// holder keeps its pointer (as upstream's does).
void CMaterialReference::Shutdown()
{
	if ( !m_pMaterial || !materials )
		return;
	m_pMaterial->DecrementReferenceCount();
	m_pMaterial = NULL;
}

//-----------------------------------------------------------------------------
// CTextureReference
//-----------------------------------------------------------------------------
CTextureReference::CTextureReference()
	: m_pTexture( NULL )
{
}

CTextureReference::CTextureReference( const CTextureReference &ref )
	: m_pTexture( ref.m_pTexture )
{
	if ( m_pTexture )
		m_pTexture->IncrementReferenceCount();
}

// Takes the other's texture and a reference to it; what this held is NOT
// given back first (upstream's assignment does not).
void CTextureReference::operator=( CTextureReference &ref )
{
	m_pTexture = ref.m_pTexture;
	if ( m_pTexture )
		m_pTexture->IncrementReferenceCount();
}

CTextureReference::~CTextureReference()
{
	Shutdown();
}

void CTextureReference::Init( char const *pTextureName, const char *pTextureGroupName, bool bComplain )
{
	Shutdown();
	m_pTexture = materials->FindTexture( pTextureName, pTextureGroupName, bComplain );
	if ( m_pTexture )
		m_pTexture->IncrementReferenceCount();
}

void CTextureReference::Init( ITexture *pTexture )
{
	Shutdown();
	m_pTexture = pTexture;
	if ( m_pTexture )
		m_pTexture->IncrementReferenceCount();
}

// Created textures arrive with their one reference already taken.
void CTextureReference::InitProceduralTexture( const char *pTextureName, const char *pTextureGroupName,
                                               int w, int h, ImageFormat fmt, int nFlags )
{
	Shutdown();
	m_pTexture = materials->CreateProceduralTexture( pTextureName, pTextureGroupName, w, h, fmt, nFlags );
}

void CTextureReference::InitRenderTarget( int w, int h, RenderTargetSizeMode_t sizeMode, ImageFormat fmt,
                                          MaterialRenderTargetDepth_t depth, bool bHDR, char *pStrOptionalName )
{
	Shutdown();

	// Clamped in both directions; a depth-only target point-sampled.
	unsigned int nTextureFlags = TEXTUREFLAGS_CLAMPS | TEXTUREFLAGS_CLAMPT;
	if ( depth == MATERIAL_RT_DEPTH_ONLY )
		nTextureFlags |= TEXTUREFLAGS_POINTSAMPLE;
	const unsigned int nTargetFlags = bHDR ? CREATERENDERTARGETFLAGS_HDR : 0;

	m_pTexture = materials->CreateNamedRenderTargetTextureEx( pStrOptionalName, w, h, sizeMode, fmt, depth,
	                                                         nTextureFlags, nTargetFlags );
	Assert( m_pTexture );
}

void CTextureReference::Shutdown( bool bDeleteIfUnReferenced )
{
	if ( !m_pTexture || !materials )
		return;
	m_pTexture->DecrementReferenceCount();
	if ( bDeleteIfUnReferenced )
		m_pTexture->DeleteIfUnreferenced();
	m_pTexture = NULL;
}
