//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimWorldServer (world_queries.cpp): the 3D skybox a managed sky_camera states, a brush model's own
// surfaceprop, and the count of handles that reached native from the engine-invisible
// range. One object, the DLL tier's.

#pragma once

#include "ishim_areas.h"

class IVModelInfo;
class IPhysicsSurfaceProps;

namespace ss
{
struct sky3dparams_t;
}

class CServerWorldQueries final : public IShimWorldServer
{
public:
	CServerWorldQueries( IVModelInfo *pModelInfo, const int32_t &nInvisibleRangeReach,
						 IPhysicsSurfaceProps *const &pSurfaceProps )
		: m_pModelInfo( pModelInfo )
		, m_nInvisibleRangeReach( nInvisibleRangeReach )
		, m_pSurfaceProps( pSurfaceProps )
	{
	}

	CServerWorldQueries( const CServerWorldQueries & ) = delete;
	CServerWorldQueries &operator=( const CServerWorldQueries & ) = delete;

	// The surfaceprop an entity's own brush model declares, or -1, read as stock's
	// CBreakableSurface::Spawn reads $crackmaterial.
	int32_t EntityModelSurfaceProp( uint32_t hEntity ) override;

	// Handles from the engine-invisible range that reached native and did not resolve.
	int32_t InvisibleRangeReach() override;

	// The 3D skybox, as the managed sky_camera states it: pushed once per level, NULL for
	// a map with none.
	void SetSkyboxParams( const SourceSharpSkyboxParams *pParams ) override;

	// Fills the engine's struct from the last push, for every player every snapshot. False
	// for a map with no sky_camera.
	bool FillSkyboxParams( ss::sky3dparams_t &out ) const;

private:
	IVModelInfo *m_pModelInfo;

	// The DLL tier's count, kept where the resolver that notes it can reach it.
	const int32_t &m_nInvisibleRangeReach;

	// The physics DLL's surface properties, which name a material's surfaceprop. Filled when
	// physics initialises, after this is built.
	IPhysicsSurfaceProps *const &m_pSurfaceProps;

	SourceSharpSkyboxParams m_SkyboxParams{};
	bool m_bHaveSkyboxParams = false;
};
