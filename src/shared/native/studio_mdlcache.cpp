//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — a studio header's four questions to the model cache =//
// SS_REASON g  leaf math over mdl/bsp data
//
// plan_server_novalve.md P3. studiohdr_t::GetVirtualModel, GetAnimBlock and
// GetAutoplayList, and virtualgroup_t::GetStudioHdr, are declared by
// public/studio.h and answered by whoever links the model cache. The server
// took them from tier3's studiohdrstub.o; tier3 is off the link, so they are
// answered here, the way that object answered them: each asks IMDLCache for
// the model by its MDL handle.
//
// THE HANDLE. On a 64-bit build the engine keeps a model's MDL handle in the
// low sixteen bits of studiohdr2_t::virtualModel (the "back link to cache
// pointers" studio.h describes), and a model with no studiohdr2 block is asked
// about as handle 0 -- both read off tier3's object and kept exactly.
//
//=============================================================================//

#include "studio.h"
#include "datacache/imdlcache.h"

extern IMDLCache *mdlcache;

static MDLHandle_t StudioMdlHandle( const studiohdr_t *pHdr )
{
	if ( !pHdr->studiohdr2index )
		return 0;

	return *reinterpret_cast< const unsigned short * >( &pHdr->pStudioHdr2()->virtualModel );
}

virtualmodel_t *studiohdr_t::GetVirtualModel( void ) const
{
	return mdlcache->GetVirtualModel( StudioMdlHandle( this ) );
}

byte *studiohdr_t::GetAnimBlock( int iBlock ) const
{
	return mdlcache->GetAnimBlock( StudioMdlHandle( this ), iBlock );
}

int studiohdr_t::GetAutoplayList( unsigned short **pOut ) const
{
	return mdlcache->GetAutoplayList( StudioMdlHandle( this ), pOut );
}

const studiohdr_t *virtualgroup_t::GetStudioHdr() const
{
	return mdlcache->GetStudioHdr( (MDLHandle_t)(size_t)cache );
}
