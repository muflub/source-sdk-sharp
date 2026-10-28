//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

// common to server, too

#include "tempentity.h"

// beam flags

class C_Beam;
class Beam_t;

// Purpose: Popcorn trail for Beam Follow rendering...

struct BeamTrail_t
{
	// NOTE:  Don't add user defined fields except after these four fields.
	BeamTrail_t*	next;
	float			die;
	Vector			org;
	Vector			vel;
};

// Data type for beams.
struct BeamInfo_t
{
	int			m_nType;

	// Entities
	alignas( 8 ) unsigned char unused_m_pStartEnt[ 52 ];

	int			m_nModelIndex;
	const char	*m_pszModelName;

	int			m_nHaloIndex;
	const char	*m_pszHaloName;
	alignas( 8 ) unsigned char unused_m_flHaloScale[ 52 ];

	bool		m_bRenderable;

	int			m_nSegments;

	int			m_nFlags;

	// Rings
	Vector		m_vecCenter;
	float		m_flStartRadius;
	float		m_flEndRadius;

	BeamInfo_t() 
	{ 
		m_nType = TE_BEAMPOINTS;
		m_nSegments = -1;
		m_pszModelName = NULL;
		m_pszHaloName = NULL;
		m_nModelIndex = -1;
		m_nHaloIndex = -1;
		m_bRenderable = true;
		m_nFlags = 0;
	}
};

// Purpose: Declare client .dll beam entity interface

abstract_class IViewRenderBeams
{
public:
	virtual void	InitBeams() = 0;
	virtual void	ShutdownBeams() = 0;
	virtual void	ClearBeams() = 0;

	// Updates the state of the temp ent beams
	virtual void	UpdateTempEntBeams() = 0;

	virtual void	DrawBeam( C_Beam* pbeam, ITraceFilter *pEntityBeamTraceFilter = NULL ) = 0;
	virtual void	DrawBeam( Beam_t *pbeam ) = 0;

	virtual void	KillDeadBeams( CBaseEntity *pEnt ) = 0;

	// New interfaces!
	virtual Beam_t	*CreateBeamEnts( BeamInfo_t &beamInfo ) = 0;
	virtual Beam_t	*CreateBeamEntPoint( BeamInfo_t &beamInfo ) = 0;
	virtual	Beam_t	*CreateBeamPoints( BeamInfo_t &beamInfo ) = 0;
	virtual Beam_t	*CreateBeamRing( BeamInfo_t &beamInfo ) = 0;
	virtual Beam_t	*CreateBeamRingPoint( BeamInfo_t &beamInfo ) = 0;
	virtual Beam_t	*CreateBeamCirclePoints( BeamInfo_t &beamInfo ) = 0;
	virtual Beam_t	*CreateBeamFollow( BeamInfo_t &beamInfo ) = 0;

	virtual void	FreeBeam( Beam_t *pBeam ) = 0;
	virtual void	UpdateBeamInfo( Beam_t *pBeam, BeamInfo_t &beamInfo ) = 0;

	// These will go away!
	virtual void	CreateBeamEnts( int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale,  
							float life, float width, float m_nEndWidth, float m_nFadeLength, float amplitude, 
							float brightness, float speed, int startFrame, 
							float framerate, float r, float g, float b, int type = -1 ) = 0;
	virtual void	CreateBeamEntPoint( int	nStartEntity, const Vector *pStart, int nEndEntity, const Vector* pEnd,
							int modelIndex, int haloIndex, float haloScale,   
							float life, float width, float m_nEndWidth, float m_nFadeLength, float amplitude, 
							float brightness, float speed, int startFrame, 
							float framerate, float r, float g, float b ) = 0;
	virtual void	CreateBeamPoints( Vector& start, Vector& end, int modelIndex, int haloIndex, float haloScale,   
							float life, float width, float m_nEndWidth, float m_nFadeLength, float amplitude, 
							float brightness, float speed, int startFrame, 
							float framerate, float r, float g, float b ) = 0;
	virtual void	CreateBeamRing( int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale,   
							float life, float width, float m_nEndWidth, float m_nFadeLength, float amplitude, 
							float brightness, float speed, int startFrame, 
							float framerate, float r, float g, float b, int flags = 0 ) = 0;
	virtual void	CreateBeamRingPoint( const Vector& center, float start_radius, float end_radius, int modelIndex, int haloIndex, float haloScale,   
							float life, float width, float m_nEndWidth, float m_nFadeLength, float amplitude, 
							float brightness, float speed, int startFrame, 
							float framerate, float r, float g, float b, int flags = 0 ) = 0;
	virtual void	CreateBeamCirclePoints( int type, Vector& start, Vector& end, 
							int modelIndex,  int haloIndex,  float haloScale, float life, float width, 
							float m_nEndWidth, float m_nFadeLength, float amplitude, float brightness, float speed, 
							int startFrame, float framerate, float r, float g, float b ) = 0;
	virtual void	CreateBeamFollow( int startEnt, int modelIndex, int haloIndex, float haloScale,  
							float life, float width, float m_nEndWidth, float m_nFadeLength, float r, float g, float b, 
							float brightness ) = 0;
};

extern IViewRenderBeams *beams;
