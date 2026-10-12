//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// tools/probes/client/fx_probe_mesh.h -- lane c2-fx: a CMeshBuilder that logs
// what it is handed, for the probe copies of fork TUs that build meshes
// (probe_copies.cmake). A probe copy says
//
//     #include "fx_probe_mesh.h"
//     #define CMeshBuilder CProbeMeshBuilder
//
// after its includes, and every vertex it emits is logged on AdvanceVertex,
// with the primitive it began and every index it wrote. The base methods are
// called unchanged, so the mesh drawn is the one stock drew.
//
//   fxm-begin <primitive> <count> [<vertices> <indices>]
//   fxm-v <x> <y> <z> <s> <t> <f|b> <r> <g> <b> <a> <nx> <ny> <nz>
//   fxm-i <index>
//   fxm-end
//
// Colours are logged as handed over: floats for Color4fv ("f"), bytes for
// Color4ub ("b").

#ifndef FX_PROBE_MESH_H
#define FX_PROBE_MESH_H

#include "materialsystem/imesh.h"
#include "fx_probe_log.h"

class CProbeMeshBuilder : public CMeshBuilder
{
public:
	void Begin( IMesh *pMesh, MaterialPrimitiveType_t type, int numPrimitives )
	{
		FxProbeLog( "fxm-begin %d %d", (int)type, numPrimitives );
		CMeshBuilder::Begin( pMesh, type, numPrimitives );
	}

	void Begin( IMesh *pMesh, MaterialPrimitiveType_t type, int nVertexCount, int nIndexCount )
	{
		FxProbeLog( "fxm-begin %d 0 %d %d", (int)type, nVertexCount, nIndexCount );
		CMeshBuilder::Begin( pMesh, type, nVertexCount, nIndexCount );
	}

	void End( bool bSpewData = false, bool bDraw = false )
	{
		FxProbeLog( "fxm-end" );
		CMeshBuilder::End( bSpewData, bDraw );
	}

	void Position3fv( const float *v )
	{
		m_p[0] = v[0]; m_p[1] = v[1]; m_p[2] = v[2];
		CMeshBuilder::Position3fv( v );
	}

	void Normal3fv( const float *n )
	{
		m_n[0] = n[0]; m_n[1] = n[1]; m_n[2] = n[2];
		CMeshBuilder::Normal3fv( n );
	}

	void TexCoord2f( int stage, float s, float t )
	{
		m_st[0] = s; m_st[1] = t;
		CMeshBuilder::TexCoord2f( stage, s, t );
	}

	void Color4fv( const float *rgba )
	{
		m_bByte = false;
		m_c[0] = rgba[0]; m_c[1] = rgba[1]; m_c[2] = rgba[2]; m_c[3] = rgba[3];
		CMeshBuilder::Color4fv( rgba );
	}

	void Color4ub( unsigned char r, unsigned char g, unsigned char b, unsigned char a )
	{
		m_bByte = true;
		m_c[0] = r; m_c[1] = g; m_c[2] = b; m_c[3] = a;
		CMeshBuilder::Color4ub( r, g, b, a );
	}

	void FastIndex( unsigned short nIndex )
	{
		FxProbeLog( "fxm-i %d", (int)nIndex );
		CMeshBuilder::FastIndex( nIndex );
	}

	void AdvanceVertex()
	{
		FxProbeLog( "fxm-v %.9g %.9g %.9g %.9g %.9g %s %.9g %.9g %.9g %.9g %.9g %.9g %.9g",
		            m_p[0], m_p[1], m_p[2], m_st[0], m_st[1], m_bByte ? "b" : "f",
		            m_c[0], m_c[1], m_c[2], m_c[3], m_n[0], m_n[1], m_n[2] );
		CMeshBuilder::AdvanceVertex();
	}

private:
	float m_p[3] = { 0, 0, 0 };
	float m_n[3] = { 0, 0, 0 };
	float m_st[2] = { 0, 0 };
	float m_c[4] = { 0, 0, 0, 0 };
	bool m_bByte = false;
};

#endif // FX_PROBE_MESH_H
