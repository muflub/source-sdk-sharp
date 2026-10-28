//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Writes the camera-facing strip vertices managed code computed. A vertex is
// BEAM_STRIP_VERTEX_FLOATS floats: position, r g b a, two texture coordinates
// (u v, u v), tangent S, tangent T.

#pragma once

#include "materialsystem/imesh.h"
#include "materialsystem/imaterialsystem.h"
#include "particles/particles.h"

enum
{
	BEAM_STRIP_VERTEX_FLOATS = 17
};

namespace ss
{
namespace beamstrip
{
inline void WriteVertex( CMeshBuilder &mb, const float *v )
{
	mb.Position3fv( v );
	mb.Color4f( v[3], v[4], v[5], v[6] );
	mb.TexCoord2f( 0, v[7], v[8] );
	mb.TexCoord2f( 1, v[9], v[10] );
	mb.TangentS3fv( v + 11 );
	mb.TangentT3fv( v + 14 );
	mb.AdvanceVertex();
}

// Appends to a shared triangle list: the vertices, then two triangles per quad,
// indexed from nFirstVertex.
inline void WriteIndexed( CMeshBuilder &mb, const float *pVerts, int nVerts, int nFirstVertex )
{
	for ( int i = 0; i < nVerts; i++ )
		WriteVertex( mb, pVerts + i * BEAM_STRIP_VERTEX_FLOATS );

	for ( int k = 0; k + 3 < nVerts; k += 2 )
	{
		const int b = nFirstVertex + k;
		mb.FastIndex( b );
		mb.FastIndex( b + 1 );
		mb.FastIndex( b + 2 );
		mb.FastIndex( b + 1 );
		mb.FastIndex( b + 3 );
		mb.FastIndex( b + 2 );
	}
}

// Draws the strip on its own as one triangle strip on pMesh.
inline void DrawStrip( IMesh *pMesh, const float *pVerts, int nVerts )
{
	CMeshBuilder mb;
	mb.Begin( pMesh, MATERIAL_TRIANGLE_STRIP, nVerts - 2 );
	for ( int i = 0; i < nVerts; i++ )
		WriteVertex( mb, pVerts + i * BEAM_STRIP_VERTEX_FLOATS );
	mb.End( false, true );
}
} // namespace beamstrip
} // namespace ss
