//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's effect registrations ==================//
// SS_REASON c  static-init registration
//
// plan_client_novalve.md §2 cluster 4: c_te_effect_dispatch.cpp's successor.
// The dispatch-by-name decision is C#'s (SourceSharp.Client.Effects.
// EffectDispatchTable, which replays stock's recorded table); what stays native
// is what a DECLARE_CLIENT_EFFECT is, a name and a function pointer linked at
// static init, and the doors in and out of the managed table:
//
//   CClientEffectRegistration     the list every DECLARE_CLIENT_EFFECT joins
//                                 (B3), in stock's order: each pushes itself
//                                 on the head
//   IShimFxClient::EffectRegistration{Count,Name,Run}
//                                 managed reads the list and runs the entry it
//                                 picked, by walk index (B4)
//   DispatchEffectToCallback      a native caller's dispatch, carried to the
//                                 managed table through IManagedFxClient
//   TE_DispatchEffect, DispatchEffect
//                                 stock's two client entries, unchanged in
//                                 what they call
//   CEffectData's entity reads    GetRenderable, GetEntity, entindex (from
//                                 effect_dispatch_data.cpp)
//
// Not carried: c_te_effect_dispatch.cpp's RecordEffect, which posts a tool
// message only in tool mode (ToolsEnabled(): Source Filmmaker recording through
// VClientTools, which no binary in the 243750 install requests, C0 probe 2),
// and the KeyValues TE_DispatchEffect overload that plays such a recording
// back, which no client object imports.
//=============================================================================//

#include "cbase.h"
#include "c_te_effect_dispatch.h"
#include "effect_dispatch_data.h"
#include "c_recipientfilter.h"
#include "itempents.h"

// The slot table's index -> handle resolver (plan_entity.md Phase 5a).
#include "shell_cliententitylist.h"

#include "sourcesharp.h"
#include "imanaged_areas_client.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// The registration list. Zero-initialised, so it is ready before any
// DECLARE_CLIENT_EFFECT's constructor runs, whatever the static-init order.
//-----------------------------------------------------------------------------
CClientEffectRegistration *CClientEffectRegistration::s_pHead = NULL;

CClientEffectRegistration::CClientEffectRegistration( const char *pEffectName, ClientEffectCallback fn )
{
	m_pEffectName = pEffectName;
	m_pFunction = fn;
	m_pNext = s_pHead;
	s_pHead = this;
}

namespace
{

CClientEffectRegistration *EffectRegistrationAt( int32_t iIndex )
{
	if ( iIndex < 0 )
		return NULL;
	CClientEffectRegistration *pReg = CClientEffectRegistration::s_pHead;
	for ( int32_t i = 0; pReg && i < iIndex; ++i )
		pReg = pReg->m_pNext;
	return pReg;
}

//-----------------------------------------------------------------------------
// SourceSharpEffectData_t -> CEffectData, field for field. The entity rule is
// effect_dispatch_data.cpp's: a negative index is no entity, anything else is
// looked up. AbiParityTests holds the struct's two halves in step.
//-----------------------------------------------------------------------------
void EffectDataToClient( const SourceSharpEffectData_t *pData, CEffectData *pOut )
{
	CEffectData &data = *pOut;

	data.m_vOrigin.Init( pData->OriginXYZ[0], pData->OriginXYZ[1], pData->OriginXYZ[2] );
	data.m_vStart.Init ( pData->StartXYZ[0],  pData->StartXYZ[1],  pData->StartXYZ[2]  );
	data.m_vNormal.Init( pData->NormalXYZ[0], pData->NormalXYZ[1], pData->NormalXYZ[2] );
	data.m_vAngles.Init( pData->AnglesPYR[0], pData->AnglesPYR[1], pData->AnglesPYR[2] );

	data.m_fFlags           = pData->Flags;
	data.m_flScale          = pData->Scale;
	data.m_flMagnitude      = pData->Magnitude;
	data.m_flRadius         = pData->Radius;
	data.m_nAttachmentIndex = pData->AttachmentIndex;
	data.m_nSurfaceProp     = (short)pData->SurfaceProp;
	data.m_nMaterial        = pData->Material;
	data.m_nDamageType      = pData->DamageType;
	data.m_nHitBox          = pData->HitBox;
	data.m_nColor           = (unsigned char)pData->Color;

	data.m_hEntity = ( pData->EntIndex >= 0 )
	               ? SourceSharp_ClientHandleFromIndex( pData->EntIndex )
	               : INVALID_EHANDLE;

	data.m_bCustomColors = ( pData->CustomColors != 0 );
	data.m_CustomColors.m_vecColor1.Init( pData->Color1XYZ[0], pData->Color1XYZ[1], pData->Color1XYZ[2] );
	data.m_CustomColors.m_vecColor2.Init( pData->Color2XYZ[0], pData->Color2XYZ[1], pData->Color2XYZ[2] );

	data.m_bControlPoint1 = ( pData->ControlPoint1 != 0 );
	data.m_ControlPoint1.m_eParticleAttachment = (ParticleAttachment_t)pData->ControlPointAttachment;
	data.m_ControlPoint1.m_vecOffset.Init( pData->ControlPointOffsetXYZ[0],
	                                       pData->ControlPointOffsetXYZ[1],
	                                       pData->ControlPointOffsetXYZ[2] );
}

//-----------------------------------------------------------------------------
// The other direction, for a native caller's dispatch: CEffectData ->
// SourceSharpEffectData_t. The handle crosses as its entry index (the wire's
// own currency), -1 for none; EffectDataToClient turns it back.
//-----------------------------------------------------------------------------
void EffectDataFromClient( const CEffectData &data, SourceSharpEffectData_t *pOut )
{
	SourceSharpEffectData_t &d = *pOut;

	d.OriginXYZ[0] = data.m_vOrigin.x; d.OriginXYZ[1] = data.m_vOrigin.y; d.OriginXYZ[2] = data.m_vOrigin.z;
	d.StartXYZ[0]  = data.m_vStart.x;  d.StartXYZ[1]  = data.m_vStart.y;  d.StartXYZ[2]  = data.m_vStart.z;
	d.NormalXYZ[0] = data.m_vNormal.x; d.NormalXYZ[1] = data.m_vNormal.y; d.NormalXYZ[2] = data.m_vNormal.z;
	d.AnglesPYR[0] = data.m_vAngles.x; d.AnglesPYR[1] = data.m_vAngles.y; d.AnglesPYR[2] = data.m_vAngles.z;

	d.Flags           = data.m_fFlags;
	d.EntIndex        = data.m_hEntity.IsValid() ? data.m_hEntity.GetEntryIndex() : -1;
	d.Scale           = data.m_flScale;
	d.Magnitude       = data.m_flMagnitude;
	d.Radius          = data.m_flRadius;
	d.AttachmentIndex = data.m_nAttachmentIndex;
	d.SurfaceProp     = data.m_nSurfaceProp;
	d.Material        = data.m_nMaterial;
	d.DamageType      = data.m_nDamageType;
	d.HitBox          = data.m_nHitBox;
	d.Color           = data.m_nColor;

	d.CustomColors = data.m_bCustomColors ? 1 : 0;
	const Vector &c1 = data.m_CustomColors.m_vecColor1;
	const Vector &c2 = data.m_CustomColors.m_vecColor2;
	d.Color1XYZ[0] = c1.x; d.Color1XYZ[1] = c1.y; d.Color1XYZ[2] = c1.z;
	d.Color2XYZ[0] = c2.x; d.Color2XYZ[1] = c2.y; d.Color2XYZ[2] = c2.z;

	d.ControlPoint1          = data.m_bControlPoint1 ? 1 : 0;
	d.ControlPointAttachment = (int32_t)data.m_ControlPoint1.m_eParticleAttachment;
	const Vector &off = data.m_ControlPoint1.m_vecOffset;
	d.ControlPointOffsetXYZ[0] = off.x; d.ControlPointOffsetXYZ[1] = off.y; d.ControlPointOffsetXYZ[2] = off.z;
}

} // namespace

//-----------------------------------------------------------------------------
// CEffectData's three entity reads (effect_dispatch_data.h), which the fork's
// effect TUs still call (fx.cpp; shell_impact_effects.cpp): the handle resolved
// through the slot table. effect_dispatch_data.cpp, which held them with the
// DT_EffectData recv table, left client.so (C2, lane c2-fx): the table had no
// class left to nest it (CTEEffectDispatch's is managed and builds its own).
//-----------------------------------------------------------------------------
IClientRenderable *CEffectData::GetRenderable() const
{
	return SourceSharp_ClientRenderableFromHandle( m_hEntity );
}

IGameEntity *CEffectData::GetEntity() const
{
	return SourceSharp_ClientSdkEntityFromHandle( m_hEntity );
}

int CEffectData::entindex() const
{
	IClientNetworkable *pNet = SourceSharp_ClientNetworkableFromHandle( m_hEntity );
	return pNet ? pNet->entindex() : -1;
}

//-----------------------------------------------------------------------------
// IShimFxClient: the list, read and run by walk index.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EffectRegistrationCount( void )
{
	int32_t n = 0;
	for ( CClientEffectRegistration *pReg = CClientEffectRegistration::s_pHead; pReg; pReg = pReg->m_pNext )
		++n;
	return n;
}

int32_t SourceSharp_EffectRegistrationName( int32_t iIndex, char *pOut, int32_t nOutSize )
{
	CClientEffectRegistration *pReg = EffectRegistrationAt( iIndex );
	if ( !pReg || !pReg->m_pEffectName )
		return -1;
	if ( pOut && nOutSize > 0 )
		Q_strncpy( pOut, pReg->m_pEffectName, nOutSize );
	return Q_strlen( pReg->m_pEffectName );
}

void SourceSharp_EffectRegistrationRun( int32_t iIndex, const SourceSharpEffectData_t *pData )
{
	CClientEffectRegistration *pReg = EffectRegistrationAt( iIndex );
	if ( !pReg || !pReg->m_pFunction || !pData )
		return;

	CEffectData data;
	EffectDataToClient( pData, &data );
	pReg->m_pFunction( data );
}

//-----------------------------------------------------------------------------
// A native caller's dispatch: to the managed table, which decides which
// registration runs (or that none does) and runs it through the slot above.
// Outside a managed load there is no table and nothing runs.
//-----------------------------------------------------------------------------
void DispatchEffectToCallback( const char *pEffectName, const CEffectData &data )
{
	IManagedFxClient *pFx = ManagedFxClient();
	if ( !pFx || !pEffectName )
		return;

	SourceSharpEffectData_t d;
	EffectDataFromClient( data, &d );
	pFx->DispatchEffect( pEffectName, &d );
}

//-----------------------------------------------------------------------------
// Stock's client entries (c_te_effect_dispatch.cpp). TE_DispatchEffect is the
// client's ITempEntsSystem::DispatchEffect body (shell_te_forwards_client.cpp);
// DispatchEffect is a client dispatching for itself, through that same te.
//-----------------------------------------------------------------------------
void TE_DispatchEffect( IRecipientFilter& filter, float delay, const Vector &pos, const char *pName, const CEffectData &data )
{
	DispatchEffectToCallback( pName, data );
}

void DispatchEffect( const char *pName, const CEffectData &data )
{
	CPASFilter filter( data.m_vOrigin );
	DispatchEffect( pName, data, filter );
}

void DispatchEffect( const char *pName, const CEffectData &data, C_RecipientFilter &filter )
{
	te->DispatchEffect( filter, 0.0, data.m_vOrigin, pName, data );
}
