//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The index-buffer generators of public/tier2/meshutils.h.
#pragma once

namespace ss
{
namespace meshutils
{
void GenerateSequential(unsigned short* pIndices, int nIndexCount, int nFirstVertex);
void GenerateQuad(unsigned short* pIndices, int nIndexCount, int nFirstVertex);
void GeneratePolygon(unsigned short* pIndices, int nIndexCount, int nFirstVertex);
void GenerateLineStrip(unsigned short* pIndices, int nIndexCount, int nFirstVertex);
void GenerateLineLoop(unsigned short* pIndices, int nIndexCount, int nFirstVertex);
} // namespace meshutils
} // namespace ss
