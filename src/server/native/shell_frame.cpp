//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

#include "shell_frame.h"

#include "shell_debugoverlays.h"
#include "eiface.h"
#include "igamerules.h"

#include "shell_standalone_player.h"
#include "datacache/imdlcache.h"
#include "server_entity_context.h"

// Defined in shell_standalone_base.cpp.
void StandaloneEntities_Finish( const CServerEntityContext &context );

void UpdateAllClientData( const CServerEntityContext &context, const CServerWorldQueries &world );

bool EntitySim_IsSimulatingOnAlternateTicks( const CGlobalVars *pGlobals );

int SourceSharp_DrainEntitySlots( const CServerEntityContext &context );

int32_t CServerFrame::IsSimulatingOnAlternateTicks()
{
	return EntitySim_IsSimulatingOnAlternateTicks( m_pGlobals ) ? 1 : 0;
}

int32_t CServerFrame::TickCount()
{
	return m_pGlobals->tickcount;
}

float CServerFrame::GetFrameTime()
{
	return m_pGlobals->frametime;
}

void CServerFrame::SetFrameTime( float flFrameTime )
{
	m_pGlobals->frametime = flFrameTime;
}

void CServerFrame::UpdateAllClientData()
{
	::UpdateAllClientData( m_Entities, m_World );
}

void CServerFrame::EndGameFrame()
{
	if ( IGameRules *pRules = m_Entities.GameRules() )
		pRules->EndGameFrame();
}

void CServerFrame::GamestatsUpdateConnection()
{
	m_pGameStats->UpdateConnection();
}

void CServerFrame::DrawAllDebugOverlays()
{
	m_Overlays.DrawAll();
}

void CServerFrame::StandalonePlayerCommands()
{
	CStandalonePlayer::RunAllQueuedCommands( m_Entities );
}

void CServerFrame::StandaloneFinish()
{
	StandaloneEntities_Finish( m_Entities );
}

void CServerFrame::DrainEntitySlots()
{
	SourceSharp_DrainEntitySlots( m_Entities );
}

void CServerFrame::BeginModelCacheLock()
{
	m_pMdlCache->BeginLock();
}

void CServerFrame::EndModelCacheLock()
{
	m_pMdlCache->EndLock();
}
