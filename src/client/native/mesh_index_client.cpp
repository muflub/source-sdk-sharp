//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON g  leaf math over mdl/bsp data
//
// Index-buffer generators: each writes nIndexCount indices for a primitive over
// vertices starting at nFirstVertex.

namespace ss
{
namespace meshutils
{

// 0, 1, 2, ...
void GenerateSequential(unsigned short* pIndices, int nIndexCount, int nFirstVertex)
{
	for (int i = 0; i < nIndexCount; i++)
		pIndices[i] = (unsigned short)(nFirstVertex + i);
}

// Two triangles per quad of four vertices: (0,1,2) (0,2,3).
void GenerateQuad(unsigned short* pIndices, int nIndexCount, int nFirstVertex)
{
	for (int i = 0, v = nFirstVertex; i + 6 <= nIndexCount; i += 6, v += 4)
	{
		pIndices[i + 0] = (unsigned short)v;
		pIndices[i + 1] = (unsigned short)(v + 1);
		pIndices[i + 2] = (unsigned short)(v + 2);
		pIndices[i + 3] = (unsigned short)v;
		pIndices[i + 4] = (unsigned short)(v + 2);
		pIndices[i + 5] = (unsigned short)(v + 3);
	}
}

// A fan around the first vertex: (0,1,2) (0,2,3) ...
void GeneratePolygon(unsigned short* pIndices, int nIndexCount, int nFirstVertex)
{
	for (int i = 0, v = 1; i + 3 <= nIndexCount; i += 3, v++)
	{
		pIndices[i + 0] = (unsigned short)nFirstVertex;
		pIndices[i + 1] = (unsigned short)(nFirstVertex + v);
		pIndices[i + 2] = (unsigned short)(nFirstVertex + v + 1);
	}
}

// Segments joining each vertex to the next: (0,1) (1,2) ...
void GenerateLineStrip(unsigned short* pIndices, int nIndexCount, int nFirstVertex)
{
	for (int i = 0, v = nFirstVertex; i + 2 <= nIndexCount; i += 2, v++)
	{
		pIndices[i + 0] = (unsigned short)v;
		pIndices[i + 1] = (unsigned short)(v + 1);
	}
}

// The closing segment first, then the strip over the rest. The closing pair is written
// whatever the count, 0 included (it then names vertex nFirstVertex - 1), as tier2's is.
void GenerateLineLoop(unsigned short* pIndices, int nIndexCount, int nFirstVertex)
{
	pIndices[0] = (unsigned short)(nFirstVertex + nIndexCount / 2 - 1);
	pIndices[1] = (unsigned short)nFirstVertex;
	GenerateLineStrip(pIndices + 2, nIndexCount - 2, nFirstVertex);
}

} // namespace meshutils
} // namespace ss
