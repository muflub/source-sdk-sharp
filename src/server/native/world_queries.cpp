//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CServerWorldQueries: IShimWorldServer's three slots (world_queries.h).

#include "world_queries.h"

#include "ss_util.h"
#include "igameentity.h"
#include "shell_resolve.h"
#include "shell_diagnostics.h"
#include "ss_physics_shared.h"
#include "model_types.h"
#include "engine/ivmodelinfo.h"
#include "iserverentity.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"
#include "ss_playerlocaldata.h"
#include "server_entity_resolver.h"

void CServerWorldQueries::SetSkyboxParams( const SourceSharpSkyboxParams *pParams )
{
	SOURCESHARP_TRACE();

	if ( !pParams )
	{
		m_bHaveSkyboxParams = false;
		return;
	}

	m_SkyboxParams = *pParams;
	m_bHaveSkyboxParams = true;
}

// One packed RGBA word into the engine's colour type, which wants its components
// set through its setters.
template <typename T>
static void SourceSharp_UnpackColor32( uint32_t packed, T &out )
{
	out.SetR( (unsigned char)( packed & 0xFF ) );
	out.SetG( (unsigned char)( ( packed >> 8 ) & 0xFF ) );
	out.SetB( (unsigned char)( ( packed >> 16 ) & 0xFF ) );
	out.SetA( (unsigned char)( ( packed >> 24 ) & 0xFF ) );
}

bool CServerWorldQueries::FillSkyboxParams( ss::sky3dparams_t &out ) const
{
	if ( !m_bHaveSkyboxParams )
		return false;

	const SourceSharpSkyboxParams &in = m_SkyboxParams;

	out.origin = Vector( in.OriginXYZ[0], in.OriginXYZ[1], in.OriginXYZ[2] );
	out.scale = in.Scale;
	out.area = in.Area;

	out.fog.enable = in.FogEnable != 0;
	out.fog.blend = in.FogBlend != 0;
	out.fog.radial = in.FogRadial != 0;
	out.fog.dirPrimary = Vector( in.FogDirPrimaryXYZ[0], in.FogDirPrimaryXYZ[1], in.FogDirPrimaryXYZ[2] );

	// Bytes, as the send table carries them, to avoid rounding twice.
	SourceSharp_UnpackColor32( in.FogColorPrimary, out.fog.colorPrimary );
	SourceSharp_UnpackColor32( in.FogColorSecondary, out.fog.colorSecondary );

	out.fog.start = in.FogStart;
	out.fog.end = in.FogEnd;
	out.fog.maxdensity = in.FogMaxDensity;

	return true;
}

// A trace reports the struck face,
// and a func_breakable_surf's undrawn faces carry nodraw's surfaceprop. -1 is the
// common answer: a studio model, more than one material, or no materials loaded
// (a dedicated server).
int32_t CServerWorldQueries::EntityModelSurfaceProp( uint32_t hEntity )
{
	SOURCESHARP_TRACE();

	SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( !ent.IsValid() || !m_pSurfaceProps )
		return -1;

	// Both hierarchies are IServerEntity by single inheritance, so the views share an address.
	IServerEntity *pServerEntity = (IServerEntity *)ent.ToHandleEntity();

	if ( !pServerEntity )
		return -1;

	const model_t *pModel = m_pModelInfo->GetModel( pServerEntity->GetModelIndex() );

	if ( !pModel || m_pModelInfo->GetModelType( pModel ) != mod_brush )
		return -1;

	model_t *pMutable = const_cast<model_t *>( pModel );

	if ( m_pModelInfo->GetModelMaterialCount( pMutable ) != 1 )
		return -1;

	IMaterial *pMaterial = NULL;
	m_pModelInfo->GetModelMaterials( pMutable, 1, &pMaterial );

	if ( !pMaterial )
		return -1;

	bool bFound = false;
	IMaterialVar *pVar = pMaterial->FindVar( "$surfaceprop", &bFound, false );

	if ( !bFound || !pVar )
		return -1;

	const char *pszName = pVar->GetStringValue();

	if ( !pszName || !pszName[0] )
		return -1;

	return m_pSurfaceProps->GetSurfaceIndex( pszName );
}

int32_t CServerWorldQueries::InvisibleRangeReach()
{
	return m_nInvisibleRangeReach;
}
