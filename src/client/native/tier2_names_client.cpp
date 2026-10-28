//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — tier2's names, over our own code
//
// SS_REASON g  leaf math over mdl/bsp data
//
// plan_client_novalve C9: the names client.so's objects import from tier2.a,
// each forwarding to ours, so the archive leaves the link. Kept apart from
// the implementations so the oracles can link the archive's objects beside
// those (meshutils_oracle_test).
//=============================================================================//

#include "mesh_index_client.h"

void GenerateSequentialIndexBuffer( unsigned short *pIndexMemory, int nIndexCount, int nFirstVertex )
{
	ss::meshutils::GenerateSequential( pIndexMemory, nIndexCount, nFirstVertex );
}

void GenerateQuadIndexBuffer( unsigned short *pIndexMemory, int nIndexCount, int nFirstVertex )
{
	ss::meshutils::GenerateQuad( pIndexMemory, nIndexCount, nFirstVertex );
}

void GeneratePolygonIndexBuffer( unsigned short *pIndexMemory, int nIndexCount, int nFirstVertex )
{
	ss::meshutils::GeneratePolygon( pIndexMemory, nIndexCount, nFirstVertex );
}

void GenerateLineStripIndexBuffer( unsigned short *pIndexMemory, int nIndexCount, int nFirstVertex )
{
	ss::meshutils::GenerateLineStrip( pIndexMemory, nIndexCount, nFirstVertex );
}

void GenerateLineLoopIndexBuffer( unsigned short *pIndexMemory, int nIndexCount, int nFirstVertex )
{
	ss::meshutils::GenerateLineLoop( pIndexMemory, nIndexCount, nFirstVertex );
}
