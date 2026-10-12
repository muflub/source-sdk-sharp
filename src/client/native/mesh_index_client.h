//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the index-buffer generators, ours
//
// public/tier2/meshutils.h's five functions (plan_client_novalve C9: tier2
// leaves client.so). mesh_index_client.cpp holds these and the Valve names
// over them; meshutils_oracle_test holds them to tier2.a's own object.
//=============================================================================//
#ifndef SOURCESHARP_MESH_INDEX_CLIENT_H
#define SOURCESHARP_MESH_INDEX_CLIENT_H
#ifdef _WIN32
#pragma once
#endif

namespace ss
{
namespace meshutils
{
void GenerateSequential( unsigned short *pIndices, int nIndexCount, int nFirstVertex );
void GenerateQuad( unsigned short *pIndices, int nIndexCount, int nFirstVertex );
void GeneratePolygon( unsigned short *pIndices, int nIndexCount, int nFirstVertex );
void GenerateLineStrip( unsigned short *pIndices, int nIndexCount, int nFirstVertex );
void GenerateLineLoop( unsigned short *pIndices, int nIndexCount, int nFirstVertex );
} // namespace meshutils
} // namespace ss

#endif // SOURCESHARP_MESH_INDEX_CLIENT_H
