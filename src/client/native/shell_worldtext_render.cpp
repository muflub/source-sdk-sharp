//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// Draws a managed point_worldtext: a leaf-system renderable that binds the font page and
// builds the quads managed laid out. Its place is followed every frame, because a managed
// entity's origin and angles are outside the blob and a move raises no data change.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/dbg.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "model_types.h"
#include "view.h"
#include "engine/IClientLeafSystem.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imesh.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "particles/particles.h"
#include "tier0/platform.h"
#include "tier1/refcount.h"
#include "tier1/strtools.h"
#include "tier1/utlvector.h"
#include "cdll_client_int.h"
#include "globalvars_base.h"
#include "networkvar.h"
#include "particledraw.h"
#include "texture_group_names.h"
#include "shell_standalone_client.h"
#include "shell_renderable.h"
#include "shell_effect_registry.h"
#include "imanaged_areas_client.h"
#include "sourcesharp.h"

class CManagedWorldText : public CShellRenderable
{
public:
	DECLARE_CLASS_GAMEROOT( CManagedWorldText, CShellRenderable );

	explicit CManagedWorldText( uint32_t hOwner ) : m_hOwner( hOwner )
	{
		m_vecMins.Init();
		m_vecMaxs.Init();
	}

	void Configure( const char *pszMaterial, const float *pBounds )
	{
		if ( !m_Material.IsValid() || Q_stricmp( m_Material->GetName(), pszMaterial ) != 0 )
			m_Material.Init( pszMaterial, TEXTURE_GROUP_OTHER, true );
		m_vecMins.Init( pBounds[0], pBounds[1], pBounds[2] );
		m_vecMaxs.Init( pBounds[3], pBounds[4], pBounds[5] );
	}

	// Always drawn (a text that should not show is destroyed) and translucent, as stock's.
	virtual bool ShouldDraw() OVERRIDE { return true; }
	virtual bool IsTransparent() OVERRIDE { return true; }

	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) OVERRIDE
	{
		mins = m_vecMins;
		maxs = m_vecMaxs;
	}

	virtual int DrawModel( int flags ) OVERRIDE
	{
		if ( flags & STUDIO_SHADOWDEPTHTEXTURE )
			return 0;

		IManagedWorldTextClient *pArea = ManagedWorldTextClient();
		IMaterial *pMaterial = m_Material;
		if ( !pArea || !pMaterial )
			return 0;

		SourceSharpWorldTextView_t view;
		VectorCopy( GetAbsOrigin().Base(), view.OriginXYZ );
		view.AnglesPYR[0] = GetAbsAngles().x;
		view.AnglesPYR[1] = GetAbsAngles().y;
		view.AnglesPYR[2] = GetAbsAngles().z;
		VectorCopy( CurrentViewForward().Base(), view.ForwardXYZ );
		VectorCopy( CurrentViewRight().Base(), view.RightXYZ );
		VectorCopy( CurrentViewUp().Base(), view.UpXYZ );
		view.CurTime = gpGlobals->curtime;

		int32_t nVerts = pArea->Draw( m_hOwner, &view, m_Verts.Base(), m_Verts.Count() );
		if ( nVerts > m_Verts.Count() )
		{
			m_Verts.SetCount( nVerts + 64 );
			nVerts = pArea->Draw( m_hOwner, &view, m_Verts.Base(), m_Verts.Count() );
		}
		if ( nVerts < 4 || nVerts > m_Verts.Count() )
			return 0;

		CMatRenderContextPtr pRenderContext( g_pMaterialSystem );
		pRenderContext->Bind( pMaterial );
		IMesh *pMesh = pRenderContext->GetDynamicMesh();

		const int nQuads = nVerts / 4;
		CMeshBuilder meshBuilder;
		meshBuilder.Begin( pMesh, MATERIAL_QUADS, nQuads );
		for ( int i = 0; i < nQuads * 4; i++ )
		{
			const SourceSharpWorldTextVertex_t &v = m_Verts[i];
			unsigned char color[4] = { (unsigned char)( v.Color & 0xFF ), (unsigned char)( ( v.Color >> 8 ) & 0xFF ),
			                           (unsigned char)( ( v.Color >> 16 ) & 0xFF ), (unsigned char)( ( v.Color >> 24 ) & 0xFF ) };
			meshBuilder.Position3fv( v.PositionXYZ );
			meshBuilder.TexCoord2f( 0, v.S, v.T );
			meshBuilder.Color4ubv( color );
			meshBuilder.AdvanceVertex();
		}
		meshBuilder.End();
		pMesh->Draw();
		return 1;
	}

private:
	uint32_t m_hOwner;
	CMaterialReference m_Material;
	Vector m_vecMins;
	Vector m_vecMaxs;
	CUtlVector< SourceSharpWorldTextVertex_t > m_Verts;
};

// hOwner and pEffect are the members shell_effect_registry.h requires of a row.
struct SourceSharpWorldText_t
{
	uint32_t             hOwner;
	CManagedWorldText   *pEffect;
};

static CUtlVector< SourceSharpWorldText_t > g_ManagedWorldText;

// A text renderable is being destroyed: drop its row. Nothing may Remove() a row it has
// just released.
static void ManagedWorldText_Forget( CShellRenderable *pText )
{
	ShellEffects_Forget( g_ManagedWorldText, pText );
}

// The font page and local bounds managed measured; the renderable is made on the first,
// at the owner's place. 1 when the owner has a renderable after the call.
int32_t SourceSharp_WorldTextMeshUpdate( uint32_t hOwner, const char *pszMaterial, const float *pBounds, int32_t nFloats )
{
	C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( hOwner );
	if ( !pOwner || !pszMaterial || !pBounds || nFloats < 6 )
		return 0;

	int iEntry = ShellEffects_Find( g_ManagedWorldText, hOwner );
	if ( iEntry < 0 )
	{
		CManagedWorldText *pText = new CManagedWorldText( hOwner );
		pText->AddToLeafSystem( RENDER_GROUP_TRANSLUCENT_ENTITY );
		pText->SetGoneCallback( ManagedWorldText_Forget );
		pText->SetAbsOrigin( pOwner->GetAbsOrigin() );
		pText->SetAbsAngles( pOwner->GetAbsAngles() );

		SourceSharpWorldText_t entry;
		entry.hOwner  = hOwner;
		entry.pEffect = pText;
		iEntry = g_ManagedWorldText.AddToTail( entry );
	}

	g_ManagedWorldText[ iEntry ].pEffect->Configure( pszMaterial, pBounds );
	return 1;
}

void SourceSharp_WorldTextMeshDestroy( uint32_t hOwner )
{
	const int iEntry = ShellEffects_Find( g_ManagedWorldText, hOwner );
	if ( iEntry < 0 )
		return;

	g_ManagedWorldText[ iEntry ].pEffect->Release();
}

// Each text at its owner's place, once a frame; an ownerless one is released.
static void ManagedWorldText_Follow()
{
	for ( int i = g_ManagedWorldText.Count() - 1; i >= 0; i-- )
	{
		CManagedWorldText *pText = g_ManagedWorldText[i].pEffect;
		C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( g_ManagedWorldText[i].hOwner );

		if ( !pOwner )
		{
			pText->Release();
			continue;
		}

		pText->SetAbsOrigin( pOwner->GetAbsOrigin() );
		pText->SetAbsAngles( pOwner->GetAbsAngles() );
	}
}

#include "shell_clientsystem.h"

class CSourceSharpWorldTextSystem : public CClientSystemPerFrame
{
public:
	CSourceSharpWorldTextSystem()
		: CClientSystemPerFrame( "CSourceSharpWorldTextSystem" ) {}

	virtual void PreRender() OVERRIDE
	{
		ManagedWorldText_Follow();
	}

	// A level change takes every owner with it; release first so the order against any
	// later sweep does not matter.
	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		ShellEffects_ReleaseAll( g_ManagedWorldText );
	}
};

static CSourceSharpWorldTextSystem g_WorldTextSystem;

CClientSystem *ClientSystem_WorldText() { return &g_WorldTextSystem; }
