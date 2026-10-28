//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// One CTextureReference per named render target, found on the slot's first use,
// with the material system's release function registered at the first find.
// Which name a slot finds and the release order are declared by managed code.
// A header so the oracle test compiles it against the pristine CTextureReference.

#pragma once

#include <string.h>

#include "materialsystem/imaterialsystem.h"
#include "materialsystem/itexture.h"
#include "materialsystem/MaterialSystemUtil.h"
// cdll_client_int.h's, which a client TU defines.
extern IMaterialSystem *materials;

// rendertexture.h's getters, one slot each in the header's order (the frame
// buffers by index, the teeny textures by which). RenderTargetSlot (C#) matches.
enum RenderTargetSlot_t
{
	SS_RT_PowerOfTwoFrameBuffer = 0,
	SS_RT_FullFrameFrameBuffer0 = 1,
	SS_RT_FullFrameFrameBuffer1 = 2,
	SS_RT_FullFrameFrameBuffer2 = 3,
	SS_RT_FullFrameFrameBuffer3 = 4,
	SS_RT_WaterReflection = 5,
	SS_RT_WaterRefraction = 6,
	SS_RT_Fullscreen = 7,
	SS_RT_Camera = 8,
	SS_RT_FullFrameDepth = 9,
	SS_RT_SmallBufferHdr0 = 10,
	SS_RT_SmallBufferHdr1 = 11,
	SS_RT_SmallBuffer0 = 12,
	SS_RT_SmallBuffer1 = 13,
	SS_RT_Teeny0 = 14,
	SS_RT_Teeny1 = 15,
	SS_RT_Teeny2 = 16,
	SS_RT_COUNT = 17,
};

class CRenderTargetStore
{
public:
	struct Row_t
	{
		char Name[ 64 ];
		char Group[ 64 ];
		int Complain;
		int CreationFlags;
		// Its place in the release's order, 1 first; 0 keeps it across a release.
		int ReleaseOrder;
	};

	CRenderTargetStore() : m_pfnRelease( NULL ), m_bReleaseAdded( false )
	{
		memset( m_Rows, 0, sizeof( m_Rows ) );
	}

	// The function the material system is handed: the owner's thunk to Release.
	void SetReleaseFunc( MaterialBufferReleaseFunc_t pfn ) { m_pfnRelease = pfn; }

	// Forgets every row and reference.
	void Reset()
	{
		for ( int i = 0; i < SS_RT_COUNT; i++ )
			m_Refs[ i ].Shutdown();
		memset( m_Rows, 0, sizeof( m_Rows ) );
		m_bReleaseAdded = false;
	}

	// A slot's declaration. A row that names another texture than the one
	// held drops the reference, so the next use finds the new name; the same
	// row again keeps it (a managed reload declares the same table).
	bool Declare( int nSlot, const char *pszName, const char *pszGroup, int bComplain, int nCreationFlags, int nReleaseOrder )
	{
		if ( nSlot < 0 || nSlot >= SS_RT_COUNT || !pszName || !pszName[ 0 ] || !pszGroup ||
		     strlen( pszName ) >= sizeof( m_Rows[ 0 ].Name ) || strlen( pszGroup ) >= sizeof( m_Rows[ 0 ].Group ) ||
		     nReleaseOrder < 0 || nReleaseOrder > SS_RT_COUNT )
			return false;

		Row_t &row = m_Rows[ nSlot ];
		if ( strcmp( row.Name, pszName ) || strcmp( row.Group, pszGroup ) || row.Complain != bComplain ||
		     row.CreationFlags != nCreationFlags )
			m_Refs[ nSlot ].Shutdown();

		memset( &row, 0, sizeof( row ) );
		strcpy( row.Name, pszName );
		strcpy( row.Group, pszGroup );
		row.Complain = bComplain;
		row.CreationFlags = nCreationFlags;
		row.ReleaseOrder = nReleaseOrder;
		return true;
	}

	// The slot's texture, found on its first use; NULL for a slot never declared.
	ITexture *Get( int nSlot )
	{
		if ( nSlot < 0 || nSlot >= SS_RT_COUNT || !m_Rows[ nSlot ].Name[ 0 ] )
			return NULL;

		if ( !m_Refs[ nSlot ] )
		{
			const Row_t &row = m_Rows[ nSlot ];
			m_Refs[ nSlot ].Init( materials->FindTexture( row.Name, row.Group, row.Complain != 0, row.CreationFlags ) );
			if ( !m_bReleaseAdded && m_pfnRelease )
			{
				m_bReleaseAdded = true;
				materials->AddReleaseFunc( m_pfnRelease );
			}
		}
		return m_Refs[ nSlot ];
	}

	// The material system's release: the declared references, in their order.
	void Release()
	{
		for ( int order = 1; order <= SS_RT_COUNT; order++ )
		{
			for ( int i = 0; i < SS_RT_COUNT; i++ )
			{
				if ( m_Rows[ i ].ReleaseOrder == order )
					m_Refs[ i ].Shutdown();
			}
		}
	}

	const Row_t &Row( int nSlot ) const { return m_Rows[ nSlot ]; }

private:
	Row_t m_Rows[ SS_RT_COUNT ];
	CTextureReference m_Refs[ SS_RT_COUNT ];
	MaterialBufferReleaseFunc_t m_pfnRelease;
	bool m_bReleaseAdded;
};
